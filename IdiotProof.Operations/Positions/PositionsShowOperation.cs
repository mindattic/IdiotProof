using IdiotProof.Blazor.Data;
using IdiotProof.Blazor.Services;
using IdiotProof.Brokers;
using IdiotProof.Core.Trading;
using IdiotProof.Operations.Account;
using IdiotProof.Operations.Rendering;
using IdiotProof.Operations.Strategies;

namespace IdiotProof.Operations.Positions;

public sealed record PositionsShowArgs
{
    [Argument(0, Name = "symbol", Description = "Ticker, OCC option symbol or crypto pair, e.g. NVDA.")]
    public string Symbol { get; init; } = "";

    [Option("mode", Description = "paper, live or all (default all).")]
    public AccountScope Mode { get; init; } = AccountScope.All;
}

[Operation("positions show", Summary = "One position in your Alpaca paper/live accounts, the IdiotProof strategy that owns it, and its latest trade-diary row.", Risk = OperationRisk.Read)]
[Example("idiotproof positions show NVDA")]
[Example("idiotproof positions show HIHO --mode paper")]
public sealed class PositionsShowOperation : IOperation<PositionsShowArgs>
{
    public async Task<OperationResult> ExecuteAsync(PositionsShowArgs args, OperationContext ctx)
    {
        var owner = ctx.RequireOwner();
        if (string.IsNullOrWhiteSpace(args.Symbol)) throw OperationException.Usage("Name the symbol, e.g. `idiotproof positions show NVDA`.");
        var symbol = args.Symbol.Trim().ToUpperInvariant();

        var strategies = (await ctx.Get<StrategyRepository>().GetAllForUserAsync(owner, ctx.CancellationToken))
            .Where(s => string.Equals(BrokerPositions.PathSymbol(s.Symbol), BrokerPositions.PathSymbol(symbol), StringComparison.OrdinalIgnoreCase))
            .ToList();
        var diary = (await ctx.Get<TradeDiaryRepository>().GetForUserAsync(owner, 1000, ctx.CancellationToken))
            .Where(t => string.Equals(BrokerPositions.PathSymbol(t.Symbol), BrokerPositions.PathSymbol(symbol), StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(t => t.EntryUtc)
            .ToList();

        var blocks = new List<string>();
        var data = new List<object>();
        foreach (var mode in BrokerPositions.Modes(args.Mode))
        {
            BrokerPosition? position = null;
            string? error = null;
            try
            {
                position = await BrokerPositions.GetAsync(ctx, owner, mode, symbol);
            }
            catch (Exception ex) when (args.Mode == AccountScope.All && ex is AlpacaAccessException or AlpacaApiException)
            {
                error = ex is AlpacaApiException api ? api.AlpacaMessage : ex.Message;
            }

            var owners = strategies.Where(s => OrderIntent.ParseMode(s.BrokerMode) == mode).ToList();
            var holders = owners.Where(s => s.PositionQty != 0).ToList();
            var ownerIds = owners.Select(s => s.Id).ToHashSet();
            // The owning strategy's newest row wins; otherwise the newest row for the symbol in this account.
            var latest = diary.FirstOrDefault(t => ownerIds.Contains(t.StrategyId))
                ?? diary.FirstOrDefault(t => t.IsPaper == (mode == BrokerMode.Paper) && !t.Broker.Equals("Sandbox", StringComparison.OrdinalIgnoreCase));

            var kv = new KeyValueBlock();
            if (error is not null) kv.Add("broker", error);
            else if (position is null) kv.Add("broker", "flat — the account holds none");
            else
            {
                var p = position;
                kv.Add("qty", $"{Fmt.Number(Math.Abs(p.Quantity))} {(p.IsShort ? "short" : "long")}"
                        + (p.QuantityAvailable is { } avail && avail != p.Quantity ? $" ({Fmt.Number(Math.Abs(avail))} available, the rest held by open orders)" : ""))
                  .Add("avg cost", AccountReads.Money(p.AverageEntryPrice))
                  .Add("price", AccountReads.Money(p.CurrentPrice))
                  .Add("value", AccountReads.Money(p.MarketValue))
                  .Add("unrealized", AccountReads.Signed(p.UnrealizedPnl) + (p.UnrealizedPnlPercent is { } pc ? $" ({Fmt.Percent(pc, 2, signed: true)})" : ""))
                  .Add("today", AccountReads.Signed(p.UnrealizedIntradayPnl));
                if (p.Option is { } o) kv.Add("contract", o.DisplayName);
            }

            if (owners.Count == 0) kv.Add("strategy", position is null ? "none" : "none — no IdiotProof strategy owns this position");
            foreach (var s in owners)
                kv.Add(holders.Contains(s) ? "owner" : "strategy",
                    $"{StrategyRefs.ShortGuid(s.Id)} '{s.Title}' · {(s.IsActive ? "active" : "inactive")} · books {(s.PositionQty == 0 ? "flat" : s.PositionQty + " shares")}"
                    + (s.EntryFilledUtc is { } filled && s.PositionQty != 0 ? $" @ {AccountReads.Money(s.LastEntryPrice)} since {Fmt.Et(filled)}" : ""));
            var booked = holders.Sum(s => s.PositionQty);
            if (error is null && (position is not null || booked != 0) && !position.IsOptionOrCrypto() && booked != (position?.Quantity ?? 0m))
                kv.Add("MISMATCH", $"strategies book {booked} shares, the broker holds {Fmt.Number(position?.Quantity ?? 0m)} — see `idiotproof positions reconcile`");

            kv.Add("diary", latest is null ? "no trade-diary row" : DiaryLine(latest));

            blocks.Add($"{mode} account — {symbol}\n{kv.Render()}");
            data.Add(new
            {
                account = mode.ToString().ToLowerInvariant(),
                error,
                position = position?.ToData(),
                strategies = owners.Select(s => new
                {
                    s.Id, s.Title, s.IsActive, s.PositionQty, s.LastEntryPrice, s.EntryFilledUtc, Owns = holders.Contains(s),
                }),
                bookedQty = booked,
                diary = latest is null ? null : new
                {
                    latest.Id, latest.StrategyId, latest.StrategyTitle, latest.Status, latest.Direction, latest.Broker, latest.IsPaper,
                    latest.Quantity, latest.EntryUtc, latest.EntryPrice, latest.StopLossPrice, latest.TakeProfitPrice,
                    latest.ExitUtc, latest.ExitPrice, latest.ExitReason, latest.RealizedPnL, latest.ReturnPercent,
                },
            });
        }

        var sandbox = strategies.Where(s => OrderIntent.ParseMode(s.BrokerMode) == BrokerMode.Sandbox && s.PositionQty != 0).ToList();
        if (sandbox.Count > 0)
            blocks.Add("Sandbox (simulator) strategies holding it: " + string.Join(", ", sandbox.Select(s => $"{StrategyRefs.ShortGuid(s.Id)} ({s.PositionQty} shares)")));
        return OperationResult.Ok(new { symbol, accounts = data }, string.Join("\n\n", blocks));
    }

    internal static string DiaryLine(TradeDiaryEntry t)
    {
        var entry = $"{t.Status} · {t.Direction.ToLowerInvariant()} {t.Quantity} @ {Fmt.Money(t.EntryPrice)} {Fmt.Et(t.EntryUtc)}";
        if (t.StopLossPrice is { } stop) entry += $" · stop {Fmt.Money(stop)}";
        if (t.ExitUtc is { } exit)
            entry += $" → out @ {AccountReads.Money(t.ExitPrice)} {Fmt.Et(exit)} ({t.ExitReason}) {AccountReads.Signed(t.RealizedPnL)}";
        return entry;
    }
}

internal static class BrokerPositionExtensions
{
    /// <summary>Strategies book whole shares of stock; options and crypto are never compared to them.</summary>
    public static bool IsOptionOrCrypto(this BrokerPosition? p) => p is not null && (p.IsOption || p.IsCrypto);
}
