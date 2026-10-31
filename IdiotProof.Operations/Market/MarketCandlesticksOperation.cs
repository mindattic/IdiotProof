using IdiotProof.Indicators;
using IdiotProof.Operations.Indicators;
using IdiotProof.Operations.Rendering;

namespace IdiotProof.Operations.Market;

public sealed record MarketCandlesticksArgs
{
    [Argument(0, Name = "symbol", Description = "Ticker, e.g. NVDA.")]
    public string Symbol { get; init; } = "";

    [Option("timeframe", Description = "Bar size: 1Day (default), 1Hour, 15Min, 5Min or 1Min (what the Monitor's pattern verbs see).")]
    public string Timeframe { get; init; } = "1Day";

    [Option("bars", Description = "How many recent bars to scan, newest last (default 30).")]
    public int Bars { get; init; } = 30;

    [Option("all", Description = "List every bar, not only the ones with a pattern.")]
    public bool All { get; init; }
}

/// <summary>One scanned bar and the patterns found on it; the time is ET.</summary>
public sealed record CandlestickBar(string TimeEt, DateTime StartUtc, decimal Open, decimal High, decimal Low, decimal Close, string[] Patterns);

/// <summary>
/// Runs <see cref="CandlestickPatterns"/> (bullish/bearish engulfing, hammer, shooting star, doji),
/// the same detectors behind the DSL's pattern verbs, over a symbol's recent bars from the Hub's
/// feed and lists where each pattern formed. Refuses the Mock feed.
/// </summary>
[Operation("market candlesticks", Summary = "Candlestick patterns (engulfing, hammer, shooting star, doji) on a symbol's recent bars.", Risk = OperationRisk.Read)]
[Example("idiotproof market candlesticks NVDA")]
[Example("idiotproof market candlesticks INTC --timeframe 5Min --bars 78 --all")]
public sealed class MarketCandlesticksOperation : IOperation<MarketCandlesticksArgs>
{
    public async Task<OperationResult> ExecuteAsync(MarketCandlesticksArgs args, OperationContext ctx)
    {
        var symbol = args.Symbol.Trim().ToUpperInvariant();
        if (symbol.Length == 0) return OperationResult.Usage("Name a symbol, e.g. `idiotproof market candlesticks NVDA`.");
        if (IndicatorBars.ParseTimeframe(args.Timeframe) is not { } size) return OperationResult.Usage("--timeframe is 1Min, 5Min, 15Min, 1Hour or 1Day.");
        if (args.Bars is < 2 or > 2000) return OperationResult.Usage("--bars must be 2-2000.");

        // One extra bar in front so the oldest shown bar has a prior for the engulfing checks.
        var candles = await IndicatorBars.LoadAsync(ctx, symbol, size, args.Bars + 1);
        var rows = new List<CandlestickBar>();
        for (var i = Math.Max(0, candles.Count - args.Bars); i < candles.Count; i++)
        {
            var c = candles[i];
            var prior = i > 0 ? candles[i - 1] : null;
            var found = new List<string>();
            if (prior is not null && CandlestickPatterns.IsBullishEngulfing(prior, c)) found.Add("bullish engulfing");
            if (prior is not null && CandlestickPatterns.IsBearishEngulfing(prior, c)) found.Add("bearish engulfing");
            if (CandlestickPatterns.IsHammer(c)) found.Add("hammer");
            if (CandlestickPatterns.IsShootingStar(c)) found.Add("shooting star");
            if (CandlestickPatterns.IsDoji(c)) found.Add("doji");
            rows.Add(new CandlestickBar(IndicatorBars.BarTime(c.StartUtc, size), c.StartUtc, c.Open, c.High, c.Low, c.Close, [.. found]));
        }

        var shown = args.All ? rows : rows.Where(r => r.Patterns.Length > 0).ToList();
        var counts = rows.SelectMany(r => r.Patterns).GroupBy(p => p).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count());
        var head = $"{symbol}: {rows.Count} {IndicatorBars.Label(size)} bars scanned, " +
                   (counts.Count == 0 ? "no patterns." : string.Join(", ", counts.Select(kv => $"{kv.Value} {kv.Key}")) + ".");
        var last = rows[^1];
        head += $" Last bar {last.TimeEt} ET: {(last.Patterns.Length == 0 ? "no pattern" : string.Join(", ", last.Patterns))}.";

        var table = new TextTable { MaxWidth = ctx.Width }
            .Column("Bar (ET)").Column("Open", Align.Right).Column("High", Align.Right).Column("Low", Align.Right)
            .Column("Close", Align.Right).Column("Patterns");
        foreach (var r in shown)
            table.Row(r.TimeEt, MarketData.Px(r.Open), MarketData.Px(r.High), MarketData.Px(r.Low), MarketData.Px(r.Close),
                r.Patterns.Length == 0 ? "-" : string.Join(", ", r.Patterns));
        var data = new { symbol, timeframe = IndicatorBars.Label(size), scanned = rows.Count, counts, latest = last, bars = shown };
        return OperationResult.Ok(data, shown.Count == 0 ? head : head + "\n\n" + table.Render());
    }
}
