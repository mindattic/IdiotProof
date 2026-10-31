using IdiotProof.Blazor.Data;
using IdiotProof.Blazor.Services;
using IdiotProof.Operations.Rendering;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Operations.Risk;

/// <summary>Today's risk picture for <c>risk status</c>.</summary>
public sealed record RiskStatusView(
    string TradingDay, decimal MaxLossPerDay, decimal DailyLossCounted, decimal RemainingDailyRisk, decimal DailyLossPercent,
    bool CircuitBreakerTripped, bool KillSwitch, decimal RealizedPnlToday, int TradesClosedToday, int OpenDiaryTrades,
    decimal MaxLossPerTrade, bool EntriesAllowed);

/// <summary>
/// Where the owner stands today: the losses the Risk Guardian has counted against the daily
/// circuit breaker (the in-memory counter the Monitor feeds), how much room is left, whether the
/// breaker or the kill switch is stopping entries, and today's realized P&amp;L from the trade
/// diary (wins included, so it can differ from the losses counted).
/// </summary>
[Operation("risk status", Summary = "Today's realized P&L, losses counted against the daily breaker, remaining room, breaker and kill-switch state.", Risk = OperationRisk.Read)]
[Example("idiotproof risk status")]
public sealed class RiskStatusOperation : IOperation<NoArgs>
{
    public async Task<OperationResult> ExecuteAsync(NoArgs args, OperationContext ctx)
    {
        var owner = ctx.RequireOwner();
        var guardian = await ctx.Get<RiskGuardianService>().GetForUserAsync(owner, ctx.CancellationToken);
        var status = guardian.GetStatus();
        var killSwitch = await ctx.Get<RiskGuardianService>().IsKillSwitchOnAsync(owner, ctx.CancellationToken);

        // The trading day rolls at 4:00 AM ET, like the Guardian's own counter.
        var et = EasternClock.ToEastern(ctx.UtcNow);
        var day = DateOnly.FromDateTime(et.Hour < 4 ? et.AddDays(-1) : et);
        var dayStartUtc = Market.MarketData.EtDayStartUtc(day).AddHours(4);
        decimal realized = 0m;
        int closed = 0, open = 0;
        if (ctx.Services.GetService(typeof(IDbContextFactory<AppDbContext>)) is IDbContextFactory<AppDbContext> dbFactory)
        {
            await using var db = await dbFactory.CreateDbContextAsync(ctx.CancellationToken);
            var today = await db.TradeDiary
                .Where(t => t.OwnerUserId == owner && t.ExitUtc != null && t.ExitUtc >= dayStartUtc)
                .Select(t => t.RealizedPnL)
                .ToListAsync(ctx.CancellationToken);
            realized = today.Sum(p => p ?? 0m);
            closed = today.Count;
            open = await db.TradeDiary.CountAsync(t => t.OwnerUserId == owner && t.Status == TradeDiaryStatus.Open, ctx.CancellationToken);
        }

        var view = new RiskStatusView(day.ToString("yyyy-MM-dd"), status.MaxLossPerDay, status.DailyLossSoFar, status.RemainingDailyRisk,
            Math.Round(status.DailyLossPercent, 1), status.IsCircuitBreakerTripped, killSwitch, realized, closed, open,
            status.MaxLossPerTrade, !status.IsCircuitBreakerTripped && !killSwitch);

        var verdict = killSwitch ? "HALTED by the kill switch (`idiotproof risk kill-switch off`)."
            : status.IsCircuitBreakerTripped ? "HALTED: the daily circuit breaker tripped; entries resume next trading day at 4:00 AM ET."
            : $"Entries allowed. A new trade may risk up to {Fmt.Money(Math.Min(status.MaxLossPerTrade, status.RemainingDailyRisk))} worst case.";
        var block = new KeyValueBlock()
            .Add("Trading day", view.TradingDay + " (rolls at 4:00 AM ET)")
            .Add("Realized P&L today", $"{Fmt.SignedMoney(realized)} over {closed} closed trade{(closed == 1 ? "" : "s")} ({open} open in the diary)")
            .Add("Losses counted", $"{Fmt.Money(status.DailyLossSoFar)} of {Fmt.Money(status.MaxLossPerDay)} ({Fmt.PercentPoints(view.DailyLossPercent, 1)})")
            .Add("Room left today", Fmt.Money(status.RemainingDailyRisk))
            .Add("Circuit breaker", status.IsCircuitBreakerTripped ? "TRIPPED" : "armed")
            .Add("Kill switch", killSwitch ? "ON" : "off")
            .Add("Verdict", verdict);
        return OperationResult.Ok(view, block.Render());
    }
}
