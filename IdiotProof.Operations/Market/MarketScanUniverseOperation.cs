using System.Text.Json;
using IdiotProof.Blazor.Data;
using IdiotProof.Operations.Rendering;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Operations.Market;

public enum UniverseSort { Volume, Change, Price, Dollar }

public sealed record MarketScanUniverseArgs
{
    [Option("min-price", Description = "Lowest last price to keep (default 1).")]
    public decimal MinPrice { get; init; } = 1m;

    [Option("max-price", Description = "Highest last price to keep (default: no cap).")]
    public decimal? MaxPrice { get; init; }

    [Option("min-volume", Description = "Least shares traded today to keep (default 100000).")]
    public decimal MinVolume { get; init; } = 100_000m;

    [Option("min-change", Description = "Least percent change from the previous close to keep, e.g. 5 or -10 (default: any).")]
    public decimal? MinChange { get; init; }

    [Option("max-change", Description = "Most percent change from the previous close to keep, e.g. -5 for losers only (default: any).")]
    public decimal? MaxChange { get; init; }

    [Option("exchange", Description = "Only tickers listed on this exchange (NASDAQ, NYSE, AMEX, ARCA, ...).")]
    public string? Exchange { get; init; }

    [Option("sort", Description = "volume (default), change, price or dollar (price x volume); always largest first.")]
    public UniverseSort Sort { get; init; } = UniverseSort.Volume;

    [Option("limit", Description = "Rows to show (default 25).")]
    public int Limit { get; init; } = 25;

    [Option("max-symbols", Description = "Most tracked tickers to quote, after the stored-price prefilter (default 3000).")]
    public int MaxSymbols { get; init; } = 3000;
}

/// <summary>One ticker that passed the screen.</summary>
public sealed record UniverseHit(string Symbol, string Exchange, decimal Price, decimal? PrevClose, decimal? ChangePercent, decimal Volume, decimal DollarVolume);

/// <summary>
/// Screens the research scanner's tracked ticker universe (<c>TrackedTicker</c>, tradable only) on
/// live price and volume: a cheap prefilter on each ticker's stored last price and exchange, then
/// Alpaca snapshots in batches for today's price, volume and change from the previous close. A
/// read-only screen; it arms nothing.
/// </summary>
[Operation("market scan-universe", Summary = "Screen the tracked ticker universe on live price, volume and % change (Alpaca snapshots); arms nothing.", Risk = OperationRisk.Read)]
[Example("idiotproof market scan-universe")]
[Example("idiotproof market scan-universe --min-price 2 --max-price 20 --min-change 10 --sort change", Description = "Small-cap gappers.")]
[Example("idiotproof market scan-universe --exchange NASDAQ --min-volume 5000000 --limit 10")]
public sealed class MarketScanUniverseOperation : IOperation<MarketScanUniverseArgs>
{
    private const int Batch = 200;

    public async Task<OperationResult> ExecuteAsync(MarketScanUniverseArgs args, OperationContext ctx)
    {
        if (args.MinPrice < 0 || args.MaxPrice is < 0 || args.MaxPrice < args.MinPrice) return OperationResult.Usage("--min-price/--max-price must be a non-negative range.");
        if (args.Limit is < 1 or > 1000) return OperationResult.Usage("--limit must be 1-1000.");
        if (args.MaxSymbols is < 1 or > 20_000) return OperationResult.Usage("--max-symbols must be 1-20000.");

        var dbFactory = ctx.Get<IDbContextFactory<AppDbContext>>();
        List<TrackedTicker> universe;
        await using (var db = await dbFactory.CreateDbContextAsync(ctx.CancellationToken))
        {
            var q = db.TrackedTickers.AsNoTracking().Where(t => t.IsTradable);
            if (!string.IsNullOrWhiteSpace(args.Exchange))
            {
                var ex = args.Exchange.Trim().ToUpperInvariant();
                q = q.Where(t => t.Exchange.ToUpper() == ex);
            }
            // Prefilter on the stored price with 50% slack (it can be a day old); unknown prices stay in.
            var lo = args.MinPrice * 0.5m;
            var hi = args.MaxPrice * 1.5m;
            q = q.Where(t => t.LastPrice == null || (t.LastPrice >= lo && (hi == null || t.LastPrice <= hi)));
            universe = await q.OrderBy(t => t.Symbol).ToListAsync(ctx.CancellationToken);
        }
        if (universe.Count == 0)
            return OperationResult.Ok(new { scanned = 0, hits = Array.Empty<UniverseHit>() },
                "No tracked tickers match the prefilter. The universe is filled by the research scanner (`research universe refresh`).");
        var truncated = universe.Count > args.MaxSymbols;
        if (truncated) universe = universe.Take(args.MaxSymbols).ToList();

        await using var api = await MarketData.OpenAsync(ctx);
        var exchanges = universe.ToDictionary(t => t.Symbol, t => t.Exchange, StringComparer.OrdinalIgnoreCase);
        var hits = new List<UniverseHit>();
        var quoted = 0;
        foreach (var chunk in universe.Select(t => t.Symbol).Chunk(Batch))
        {
            var root = await api.GetDataAsync("/v2/stocks/snapshots",
                MarketData.Q(("symbols", string.Join(',', chunk)), ("feed", MarketData.Feed)), ctx.CancellationToken);
            foreach (var (symbol, snap) in MarketData.BySymbol(root, null))
            {
                if (snap.ValueKind != JsonValueKind.Object) continue;
                quoted++;
                JsonElement day = MarketData.Obj(snap, "dailyBar"), prev = MarketData.Obj(snap, "prevDailyBar");
                var price = MarketData.Dec(MarketData.Obj(snap, "latestTrade"), "p") ?? MarketData.Dec(day, "c");
                if (price is not { } p) continue;
                var volume = MarketData.Dec(day, "v") ?? 0m;
                var prevClose = MarketData.Dec(prev, "c");
                decimal? change = prevClose is > 0 ? Math.Round((p - prevClose.Value) / prevClose.Value * 100m, 2) : null;
                if (p < args.MinPrice || p > args.MaxPrice) continue;
                if (volume < args.MinVolume) continue;
                if (args.MinChange is { } mn && (change is null || change < mn)) continue;
                if (args.MaxChange is { } mx && (change is null || change > mx)) continue;
                hits.Add(new UniverseHit(symbol.ToUpperInvariant(), exchanges.GetValueOrDefault(symbol, ""), p, prevClose, change, volume, Math.Round(p * volume, 0)));
            }
        }

        var sorted = (args.Sort switch
        {
            UniverseSort.Change => hits.OrderByDescending(h => h.ChangePercent ?? decimal.MinValue),
            UniverseSort.Price => hits.OrderByDescending(h => h.Price),
            UniverseSort.Dollar => hits.OrderByDescending(h => h.DollarVolume),
            _ => hits.OrderByDescending(h => h.Volume),
        }).ThenBy(h => h.Symbol, StringComparer.Ordinal).Take(args.Limit).ToList();

        var table = new TextTable { MaxWidth = ctx.Width }
            .Column("Symbol").Column("Exch").Column("Price", Align.Right).Column("Chg %", Align.Right)
            .Column("Volume", Align.Right).Column("$ Volume", Align.Right).Column("Prev close", Align.Right);
        foreach (var h in sorted)
            table.Row(h.Symbol, h.Exchange, MarketData.Px(h.Price), h.ChangePercent is { } c ? Fmt.PercentPoints(c, 2, signed: true) : "-",
                MarketData.Compact(h.Volume), MarketData.Compact(h.DollarVolume), MarketData.Px(h.PrevClose));
        var head = $"{hits.Count} of {quoted} quoted tickers pass (universe {universe.Count}{(truncated ? $", capped by --max-symbols {args.MaxSymbols}" : "")}; feed {MarketData.Feed}); " +
                   $"top {sorted.Count} by {args.Sort.ToString().ToLowerInvariant()}.";
        return OperationResult.Ok(new { scanned = universe.Count, quoted, matched = hits.Count, truncated, feed = MarketData.Feed, hits = sorted },
            sorted.Count == 0 ? head : head + "\n" + table.Render());
    }
}
