using IdiotProof.DataFeeds;
using IdiotProof.Operations.Rendering;
using IdiotProof.Strategies;

namespace IdiotProof.Operations.Indicators;

public sealed record IndicatorsSnapshotArgs
{
    [Argument(0, Name = "symbol", Description = "Ticker, e.g. NVDA.")]
    public string Symbol { get; init; } = "";

    [Option("timeframe", Description = "Bar size: 1Min (default, what the Monitor evaluates on), 5Min, 15Min, 1Hour or 1Day.")]
    public string Timeframe { get; init; } = "1Min";

    [Option("bars", Description = "Window of bars the snapshot is built from, newest last (default 240, the Monitor's 4-hour window).")]
    public int Bars { get; init; } = 240;

    [Option("ema", Description = "Extra EMA periods to include beside 9/21/31/50/200, comma separated (e.g. 7,65).")]
    public string? Ema { get; init; }
}

/// <summary>
/// The full <c>IndicatorSnapshot</c> a strategy condition would see for a symbol right now: built
/// by the Monitor's own <see cref="IndicatorSnapshotBuilder"/> over the same window, with the
/// previous close for gap math. Price and VWAP, the EMA stack, RSI, MACD, ADX/DI, ATR, Bollinger,
/// Stochastic, volume ratio, gap, window high/low, swing pivots and the candle-pattern flags.
/// </summary>
[Operation("indicators snapshot", Summary = "The full indicator snapshot a strategy condition sees for a symbol: VWAP, EMAs, RSI, MACD, ADX, gap, window high/low, pivots, patterns.", Risk = OperationRisk.Read)]
[Example("idiotproof indicators snapshot NVDA")]
[Example("idiotproof indicators snapshot INTC --ema 7,65 --timeframe 5Min")]
public sealed class IndicatorsSnapshotOperation : IOperation<IndicatorsSnapshotArgs>
{
    public async Task<OperationResult> ExecuteAsync(IndicatorsSnapshotArgs args, OperationContext ctx)
    {
        var symbol = args.Symbol.Trim().ToUpperInvariant();
        if (symbol.Length == 0) return OperationResult.Usage("Name a symbol, e.g. `idiotproof indicators snapshot NVDA`.");
        if (IndicatorBars.ParseTimeframe(args.Timeframe) is not { } size) return OperationResult.Usage("--timeframe is 1Min, 5Min, 15Min, 1Hour or 1Day.");
        if (args.Bars is < 2 or > 5000) return OperationResult.Usage("--bars must be 2-5000.");
        var emas = new List<int>();
        foreach (var part in (args.Ema ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!int.TryParse(part, out var p) || p is < 1 or > 500) return OperationResult.Usage($"--ema takes periods 1-500, comma separated; '{part}' isn't one.");
            emas.Add(p);
        }

        var candles = await IndicatorBars.LoadAsync(ctx, symbol, size, args.Bars);
        decimal? previousClose = null;
        try { previousClose = await ctx.Get<IMarketDataFeed>().GetPreviousCloseAsync(symbol, ctx.UtcNow, ctx.CancellationToken); }
        catch (Exception ex) when (ex is not OperationCanceledException) { /* gap conditions fail closed without it; say so below */ }

        var s = IndicatorSnapshotBuilder.BuildWithEmas(symbol, candles, emas, previousClose);
        var emaText = string.Join("  ", s.Emas.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}: {IndicatorBars.Num(kv.Value, 2)}"));
        string Flag(bool? b) => b is null ? "-" : b.Value ? "yes" : "no";
        var patterns = new[]
        {
            (s.IsBullishEngulfing, "bullish engulfing"), (s.IsBearishEngulfing, "bearish engulfing"), (s.IsHammer, "hammer"),
            (s.IsShootingStar, "shooting star"), (s.IsDoji, "doji"),
        }.Where(p => p.Item1).Select(p => p.Item2).ToList();

        var block = new KeyValueBlock()
            .Add("Symbol", $"{symbol}  {IndicatorBars.Label(size)} bars x{candles.Count}, last bar {IndicatorBars.BarTime(candles[^1].StartUtc, size)} ET")
            .Add("Price", $"{IndicatorBars.Num(s.Price, 2)}  (O {IndicatorBars.Num(s.BarOpen, 2)}  H {IndicatorBars.Num(s.BarHigh, 2)}  L {IndicatorBars.Num(s.BarLow, 2)}  prior close {IndicatorBars.Num(s.PriorPrice, 2)})")
            .Add("Previous day close", previousClose is { } pc ? $"{IndicatorBars.Num(pc, 2)}  gap {IndicatorBars.Num(s.GapPercent, 2)}%" : "- (unavailable: gap conditions fail closed)")
            .Add("VWAP", $"{IndicatorBars.Num(s.Vwap, 2)}  ({IndicatorBars.Num(s.VwapDistance, 2)}% away)")
            .Add("EMAs", emaText.Length == 0 ? "-" : emaText)
            .Add("RSI(14)", IndicatorBars.Num(s.Rsi, 1) + (s.HasBullishDivergence == true ? "  bullish divergence" : s.HasBearishDivergence == true ? "  bearish divergence" : ""))
            .Add("MACD", $"{IndicatorBars.Num(s.MacdLine, 3)}  signal {IndicatorBars.Num(s.SignalLine, 3)}  hist {IndicatorBars.Num(s.Histogram, 3)}{(s.MacdLine is null ? "" : s.IsMacdBullish ? "  bullish" : "  bearish")}")
            .Add("ADX(14)", $"{IndicatorBars.Num(s.Adx, 1)}  +DI {IndicatorBars.Num(s.PlusDI, 1)}  -DI {IndicatorBars.Num(s.MinusDI, 1)}{(s.Adx is null ? "" : s.IsTrending ? "  trending" : "  not trending")}")
            .Add("ATR(14)", IndicatorBars.Num(s.Atr, 3))
            .Add("Bollinger(20)", $"{IndicatorBars.Num(s.BollingerUpper, 2)} / {IndicatorBars.Num(s.BollingerMiddle, 2)} / {IndicatorBars.Num(s.BollingerLower, 2)}")
            .Add("Stochastic", $"%K {IndicatorBars.Num(s.StochasticK, 1)}  %D {IndicatorBars.Num(s.StochasticD, 1)}")
            .Add("Volume", $"{Fmt.Number(s.Volume)}  avg {Fmt.Number((decimal)s.AverageVolume, 0)}  ratio {IndicatorBars.Num(s.VolumeRatio, 2)}x")
            .Add("Window high/low", $"{IndicatorBars.Num(s.WindowHigh, 2)} / {IndicatorBars.Num(s.WindowLow, 2)}")
            .Add("Swing high/low", $"{IndicatorBars.Num(s.RecentSwingHigh, 2)} / {IndicatorBars.Num(s.RecentSwingLow, 2)}")
            .Add("Pivots", $"higher low {Flag(s.HasHigherLow)}  lower high {Flag(s.HasLowerHigh)}")
            .Add("Patterns", patterns.Count == 0 ? "none on the last bar" : string.Join(", ", patterns))
            .Add("Market score", $"{s.CalculateMarketScore():+0;-0;0} (-100..+100)");

        var data = new
        {
            symbol, timeframe = IndicatorBars.Label(size), bars = candles.Count, previousClose,
            snapshot = s, gapPercent = s.GapPercent, vwapDistance = s.VwapDistance, volumeRatio = s.VolumeRatio, marketScore = s.CalculateMarketScore(),
        };
        return OperationResult.Ok(data, block.Render());
    }
}
