using IdiotProof.Operations.Rendering;
using IdiotProof.Shared.Risk;

using IdiotProof.Core.Services;
namespace IdiotProof.Operations.Risk;

/// <summary>The owner's Risk Guardian limits as <c>risk show</c> and <c>risk set</c> report them.</summary>
public sealed record RiskLimits(
    decimal MaxLossPerTrade, decimal MaxLossPerDay, decimal MinStopLossPercent, decimal MaxStopLossPercent,
    decimal AccountBalance, decimal MaxAccountRiskPercent, bool KillSwitch)
{
    public static RiskLimits From(RiskGuardianConfig c) => new(
        c.MaxLossPerTrade, c.MaxLossPerDay, c.MinStopLossPercent, c.MaxStopLossPercent, c.AccountBalance, c.MaxAccountRiskPercent, c.KillSwitch);

    /// <summary>The six limits plus the kill switch, aligned.</summary>
    public string Render() => new KeyValueBlock()
        .Add("Max loss per trade", Fmt.Money(MaxLossPerTrade) + "  (worst case, 1.5x slippage through the stop)")
        .Add("Max loss per day", Fmt.Money(MaxLossPerDay) + "  (daily circuit breaker)")
        .Add("Min stop distance", Fmt.PercentPoints(MinStopLossPercent) + " of entry")
        .Add("Max stop distance", Fmt.PercentPoints(MaxStopLossPercent) + " of entry")
        .Add("Account balance", Fmt.Money(AccountBalance) + "  (what position size is measured against)")
        .Add("Max account risk", Fmt.PercentPoints(MaxAccountRiskPercent) + " of the balance per trade")
        .Add("Kill switch", KillSwitch ? "ON: every new entry is vetoed" : "off")
        .Render();
}

internal static class RiskReads
{
    /// <summary>The owner's limits straight from SQL (UserPreferences + the kill switch), not the 2-minute cache.</summary>
    public static async Task<RiskLimits> LimitsAsync(OperationContext ctx, Guid owner) =>
        RiskLimits.From(await ctx.Get<RiskGuardianService>().LoadConfigAsync(owner, ctx.CancellationToken));
}

/// <summary>
/// The six limits the Risk Guardian holds every entry to (IP-LAW-2): max loss per trade, max loss
/// per day, min and max stop distance, the account balance it sizes against and the max percent of
/// it one trade may risk, plus the owner's kill switch.
/// </summary>
[Operation("risk show", Summary = "Your six Risk Guardian limits (per-trade and daily loss, stop range, balance, account risk %) and the kill switch.", Risk = OperationRisk.Read)]
[Example("idiotproof risk show")]
public sealed class RiskShowOperation : IOperation<NoArgs>
{
    public async Task<OperationResult> ExecuteAsync(NoArgs args, OperationContext ctx)
    {
        var limits = await RiskReads.LimitsAsync(ctx, ctx.RequireOwner());
        return OperationResult.Ok(limits, limits.Render() + "\nChange them with `idiotproof risk set`; see today's room with `idiotproof risk status`.");
    }
}
