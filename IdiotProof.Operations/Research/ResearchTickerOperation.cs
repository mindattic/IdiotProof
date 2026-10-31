using IdiotProof.Core.Data;
using IdiotProof.Operations.Rendering;
using Microsoft.EntityFrameworkCore;

using IdiotProof.Core.Services;
namespace IdiotProof.Operations.Research;

public sealed record ResearchTickerArgs
{
    [Argument(0, Name = "symbol", Description = "Ticker symbol, e.g. NVDA.")]
    public string Symbol { get; init; } = "";

    [Option("days", Description = "Look back this many days, 1-3650 (default 90).")]
    public int Days { get; init; } = 90;

    [Option("limit", Description = "Most claims to list, 1-200 (default 15).")]
    public int Limit { get; init; } = 15;
}

/// <summary>
/// Everything the research pipeline knows about one ticker: its universe row (exchange, last
/// price, shares outstanding), its claims ranked by significance, pending portents, macro and
/// index events that name it, Form 4 insider activity, and how its backfilled claims played out.
/// </summary>
[Operation("research ticker", Summary = "Everything research knows about one ticker: claims, portents, macro and index events, insiders, outcomes.", Risk = OperationRisk.Read)]
[Example("idiotproof research ticker NVDA")]
[Example("idiotproof research ticker SMCI --days 365 --limit 30")]
public sealed class ResearchTickerOperation : IOperation<ResearchTickerArgs>
{
    public async Task<OperationResult> ExecuteAsync(ResearchTickerArgs args, OperationContext ctx)
    {
        var symbol = (args.Symbol ?? "").Trim().ToUpperInvariant();
        if (symbol.Length == 0) return OperationResult.Usage("Name a ticker, e.g. `idiotproof research ticker NVDA`.");
        if (args.Days is < 1 or > 3650) return OperationResult.Usage("--days must be 1-3650.");
        if (args.Limit is < 1 or > 200) return OperationResult.Usage("--limit must be 1-200.");

        var sinceUtc = ctx.UtcNow.AddDays(-args.Days);
        var ct = ctx.CancellationToken;
        await using var db = await ResearchSupport.Db(ctx).CreateDbContextAsync(ct);

        var tracked = await db.TrackedTickers.AsNoTracking().FirstOrDefaultAsync(t => t.Symbol == symbol, ct);
        var own = await db.ResearchClaims.AsNoTracking()
            .Where(c => c.Ticker == symbol && c.CreatedUtc >= sinceUtc)
            .OrderByDescending(c => c.SignificanceScore ?? -1).ThenByDescending(c => c.CreatedUtc)
            .Take(2000)
            .ToListAsync(ct);
        var macro = (await db.ResearchClaims.AsNoTracking()
                .Where(c => c.IsMacro && c.CreatedUtc >= sinceUtc && c.AffectedTickersJson != null)
                .OrderByDescending(c => c.CreatedUtc)
                .Take(2000)
                .ToListAsync(ct))
            .Where(c => ResearchSupport.AffectedTickers(c.AffectedTickersJson).Contains(symbol, StringComparer.OrdinalIgnoreCase))
            .ToList();

        var claimIds = own.Select(c => c.Id).ToList();
        var insiders = await db.InsiderTransactions.AsNoTracking()
            .Where(t => claimIds.Contains(t.ClaimId))
            .OrderByDescending(t => t.TransactionDate)
            .ToListAsync(ct);

        var portents = own.Where(c => !c.HasHappenedAlready && c.Status == "Pending").ToList();
        var indexEvents = own.Where(c => c.ClaimType == IndexEventScanner.ClaimType).ToList();
        var outcomes = own.Where(c => c.OutcomePctChange is not null).ToList();
        var buys = insiders.Where(t => t.TransactionCode == "P").Sum(t => t.DollarValue ?? 0);
        var sells = insiders.Where(t => t.TransactionCode == "S").Sum(t => t.DollarValue ?? 0);

        var summary = new
        {
            Claims = own.Count,
            Pending = portents.Count,
            Bullish = own.Count(c => c.Sentiment == "Bullish"),
            Bearish = own.Count(c => c.Sentiment == "Bearish"),
            Backfilled = outcomes.Count,
            AvgOutcomePct = outcomes.Count > 0 ? Math.Round(outcomes.Average(c => c.OutcomePctChange!.Value), 2) : (decimal?)null,
            InsiderBuys = buys,
            InsiderSells = sells,
        };
        var data = new
        {
            Symbol = symbol,
            Days = args.Days,
            Universe = tracked is null ? null : new
            {
                tracked.Exchange, tracked.IsTradable, tracked.LastPrice, tracked.SharesOutstanding,
                MarketCap = tracked.LastPrice * tracked.SharesOutstanding, tracked.LastRefreshedUtc,
            },
            Summary = summary,
            TopClaims = own.Take(args.Limit).Select(ResearchSupport.ToRow).ToList(),
            Portents = portents.Take(args.Limit).Select(ResearchSupport.ToRow).ToList(),
            Macro = macro.Take(args.Limit).Select(ResearchSupport.ToRow).ToList(),
            IndexEvents = indexEvents.Select(ResearchSupport.ToRow).ToList(),
            Insiders = insiders.Take(args.Limit).Select(InsiderRow.From).ToList(),
        };

        var kv = new KeyValueBlock()
            .Add("ticker", symbol)
            .Add("universe", tracked is null
                ? "not in the tracked universe (`idiotproof research universe refresh` reloads it)"
                : $"{tracked.Exchange}{(tracked.IsTradable ? "" : " (not tradable)")}"
                  + (tracked.LastPrice is { } p ? $" · last {Fmt.Money(p)}" : "")
                  + (tracked.LastPrice is { } lp && tracked.SharesOutstanding is { } so ? $" · mkt cap {Fmt.Money(lp * so, 0)}" : "")
                  + $" · refreshed {Fmt.Et(tracked.LastRefreshedUtc)}")
            .Add("claims", $"{own.Count} in {args.Days}d: {summary.Bullish} bullish, {summary.Bearish} bearish, {portents.Count} pending portents")
            .Add("outcomes", outcomes.Count == 0 ? "none backfilled yet"
                : $"{outcomes.Count} backfilled, average {Fmt.PercentPoints(summary.AvgOutcomePct!.Value, 2, signed: true)} after the claim")
            .Add("insiders", insiders.Count == 0 ? "no Form 4 lines"
                : $"{insiders.Count} Form 4 lines: {Fmt.Money(buys, 0)} bought, {Fmt.Money(sells, 0)} sold")
            .Add("macro", macro.Count == 0 ? "no macro claims name it" : $"{macro.Count} macro/regulatory claims name it")
            .Add("index", indexEvents.Count == 0 ? "no S&P index events" : string.Join("; ", indexEvents.Select(e => e.ClaimSummary)));

        var blocks = new List<string> { kv.Render() };
        if (own.Count > 0) blocks.Add("Top claims:\n" + ResearchSupport.ClaimsTable(data.TopClaims, ctx.Width));
        if (portents.Count > 0) blocks.Add("Pending portents:\n" + ResearchSupport.ClaimsTable(data.Portents, ctx.Width));
        if (macro.Count > 0) blocks.Add("Macro claims naming it:\n" + ResearchSupport.ClaimsTable(data.Macro, ctx.Width));
        if (insiders.Count > 0) blocks.Add("Insider transactions:\n" + InsiderRow.Table(data.Insiders, ctx.Width));
        if (own.Count == 0 && macro.Count == 0)
            blocks.Add($"Nothing found in {args.Days} days. `idiotproof research scan --tickers {symbol}` scans it now.");
        return OperationResult.Ok(data, string.Join("\n\n", blocks));
    }
}
