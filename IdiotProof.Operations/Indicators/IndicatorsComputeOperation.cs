using IdiotProof.Operations.Rendering;

namespace IdiotProof.Operations.Indicators;

public sealed record IndicatorsComputeArgs
{
    [Argument(0, Name = "symbol", Description = "Ticker, e.g. NVDA.")]
    public string Symbol { get; init; } = "";

    [Argument(1, Name = "indicator", Description = "rsi, ema, sma, atr, macd, vwap, adx, bollinger, stochastic, cci, williams-r, momentum, roc or obv.")]
    public string Indicator { get; init; } = "";

    [Option("period", Description = "Look-back period (default per indicator: rsi 14, ema 9, sma 20, atr 14, adx 14, bollinger 20, ...). Ignored by macd, vwap and obv.")]
    public int? Period { get; init; }

    [Option("timeframe", Description = "Bar size: 1Min (default), 5Min, 15Min, 1Hour or 1Day.")]
    public string Timeframe { get; init; } = "1Min";

    [Option("bars", Description = "Bars of history to compute over, newest last (default 240, the Monitor's window).")]
    public int Bars { get; init; } = 240;

    [Option("last", Description = "How many of the newest values to print (default 10).")]
    public int Last { get; init; } = 10;
}

/// <summary>One bar's indicator output in the payload; the time is ET.</summary>
public sealed record IndicatorPoint(string TimeEt, DateTime StartUtc, decimal Close, Dictionary<string, decimal> Values);

/// <summary>
/// Runs one technical indicator over the symbol's recent bars from the Hub's feed, with the same
/// <c>IdiotProof.Indicators</c> code the Monitor evaluates strategy conditions with, and prints
/// the newest values. Refuses the Mock feed.
/// </summary>
[Operation("indicators compute", Summary = "Compute one indicator (rsi, ema, atr, macd, vwap, adx, bollinger, ...) on a symbol's recent bars.", Risk = OperationRisk.Read)]
[Example("idiotproof indicators compute NVDA rsi")]
[Example("idiotproof indicators compute INTC ema --period 21 --timeframe 5Min")]
[Example("idiotproof indicators compute AAPL macd --timeframe 1Day --bars 300")]
public sealed class IndicatorsComputeOperation : IOperation<IndicatorsComputeArgs>
{
    public async Task<OperationResult> ExecuteAsync(IndicatorsComputeArgs args, OperationContext ctx)
    {
        var symbol = args.Symbol.Trim().ToUpperInvariant();
        if (symbol.Length == 0) return OperationResult.Usage("Name a symbol, e.g. `idiotproof indicators compute NVDA rsi`.");
        var spec = IndicatorCatalog.Find(args.Indicator);
        if (spec is null) return OperationResult.Usage($"Unknown indicator '{args.Indicator}'. Known: {IndicatorCatalog.Names}.");
        if (IndicatorBars.ParseTimeframe(args.Timeframe) is not { } size) return OperationResult.Usage("--timeframe is 1Min, 5Min, 15Min, 1Hour or 1Day.");
        if (args.Period is < 1 or > 500) return OperationResult.Usage("--period must be 1-500.");
        if (args.Bars is < 2 or > 5000) return OperationResult.Usage("--bars must be 2-5000.");
        if (args.Last is < 1 or > 500) return OperationResult.Usage("--last must be 1-500.");

        var period = spec.DefaultPeriod is null ? 0 : args.Period ?? spec.DefaultPeriod.Value;
        var candles = await IndicatorBars.LoadAsync(ctx, symbol, size, Math.Max(args.Bars, spec.MinBars(period)));
        if (candles.Count < spec.MinBars(period))
            return OperationResult.Fail($"{spec.Name.ToUpperInvariant()}({period}) needs {spec.MinBars(period)} bars; only {candles.Count} {IndicatorBars.Label(size)} bars of {symbol} are available.");

        var series = spec.Compute(candles, period);
        var shown = Math.Min(args.Last, candles.Count);
        var points = new List<IndicatorPoint>();
        for (var i = candles.Count - shown; i < candles.Count; i++)
            points.Add(new IndicatorPoint(IndicatorBars.BarTime(candles[i].StartUtc, size), candles[i].StartUtc, candles[i].Close,
                series.ToDictionary(s => s.Column, s => Math.Round(s.Values[i], 6))));

        var table = new TextTable { MaxWidth = ctx.Width }.Column("Bar (ET)").Column("Close", Align.Right);
        foreach (var s in series) table.Column(s.Column, Align.Right);
        foreach (var p in points)
            table.Row([p.TimeEt, IndicatorBars.Num(p.Close), .. series.Select(s => IndicatorBars.Num(p.Values[s.Column], spec.Name == "obv" ? 0 : 2))]);

        var label = spec.DefaultPeriod is null ? spec.Name.ToUpperInvariant() : $"{spec.Name.ToUpperInvariant()}({period})";
        var latest = points[^1];
        var head = $"{symbol} {label} on {IndicatorBars.Label(size)} bars ({candles.Count} bars, last {latest.TimeEt} ET): " +
                   string.Join("  ", series.Select(s => $"{s.Column} {IndicatorBars.Num(latest.Values[s.Column], spec.Name == "obv" ? 0 : 2)}"));
        var data = new
        {
            symbol, indicator = spec.Name, period = spec.DefaultPeriod is null ? (int?)null : period, timeframe = IndicatorBars.Label(size),
            bars = candles.Count, latest = latest.Values, points,
        };
        return OperationResult.Ok(data, head + "\n\n" + table.Render());
    }
}
