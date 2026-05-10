using IdiotProof.Indicators;
using IdiotProof.Models;
using IdiotProof.Shared;

namespace IdiotProof.Strategies;

/// <summary>
/// Computes a fully-populated <see cref="IndicatorSnapshot"/> from a window of candles.
/// This is the bridge between the raw market-data feed (candles) and the DSL
/// evaluator (which speaks IndicatorSnapshot only). One snapshot per evaluation
/// tick — the latest bar plus enough history for indicator math to converge.
/// </summary>
public static class IndicatorSnapshotBuilder
{
    /// <summary>
    /// Default EMA periods always populated on every snapshot. The DSL can
    /// request additional periods on demand via <see cref="BuildWithEmas"/>.
    /// </summary>
    public static readonly int[] DefaultEmaPeriods = [9, 21, 31, 50, 200];

    public static IndicatorSnapshot Build(string symbol, IReadOnlyList<Candle> candles)
        => BuildWithEmas(symbol, candles, DefaultEmaPeriods);

    /// <summary>
    /// Build a snapshot, ensuring the requested EMA periods are populated in
    /// addition to the defaults. Used when a DSL strategy references unusual
    /// periods (e.g. IsBetweenEma(7, 65)) — we union the strategy's required
    /// periods with the defaults.
    /// </summary>
    public static IndicatorSnapshot BuildWithEmas(string symbol, IReadOnlyList<Candle> candles, IEnumerable<int> emaPeriods)
    {
        if (candles.Count == 0)
        {
            return new IndicatorSnapshot { Symbol = symbol };
        }

        var n = candles.Count;
        var lastBar = candles[^1];
        var priorBar = n >= 2 ? candles[^2] : null;

        var snapshot = new IndicatorSnapshot
        {
            Symbol     = symbol,
            Timestamp  = lastBar.EndUtc,
            Price      = (double)lastBar.Close,
            BarOpen    = (double)lastBar.Open,
            BarHigh    = (double)lastBar.High,
            BarLow     = (double)lastBar.Low,
            PriorPrice = priorBar is not null ? (double)priorBar.Close : null,
            Volume     = (long)lastBar.Volume,
        };

        // Average volume — last 20 bars (or whatever's available).
        var volWindow = Math.Min(20, n);
        decimal volSum = 0m;
        for (int i = n - volWindow; i < n; i++) volSum += candles[i].Volume;
        snapshot.AverageVolume = volWindow > 0 ? (double)(volSum / volWindow) : 0;

        // VWAP
        var vwap = VWAP.Calculate(candles);
        snapshot.Vwap      = (double)vwap[^1];
        snapshot.PriorVwap = n >= 2 ? (double)vwap[^2] : null;

        // EMAs — union of requested periods with defaults
        var allPeriods = new HashSet<int>(DefaultEmaPeriods);
        foreach (var p in emaPeriods) allPeriods.Add(p);
        foreach (var period in allPeriods)
        {
            if (period <= 0 || period > n) continue;
            var emaSeries = EMA.Calculate(candles, period);
            snapshot.Emas[period] = (double)emaSeries[^1];
            if (n >= 2) snapshot.PriorEmas[period] = (double)emaSeries[^2];
        }

        // Mirror the well-known periods to fixed fields for back-compat with
        // existing IndicatorSnapshot consumers (CalculateMarketScore, tests).
        if (snapshot.Emas.TryGetValue(9, out var e9))     snapshot.Ema9   = e9;
        if (snapshot.Emas.TryGetValue(21, out var e21))   snapshot.Ema21  = e21;
        if (snapshot.Emas.TryGetValue(50, out var e50))   snapshot.Ema50  = e50;
        if (snapshot.Emas.TryGetValue(200, out var e200)) snapshot.Ema200 = e200;

        // RSI
        if (n >= 2)
        {
            var rsi = RSI.Calculate(candles);
            snapshot.Rsi = (double)rsi[^1];
        }

        // MACD
        if (n >= 26)
        {
            var macd = MACD.Calculate(candles);
            snapshot.MacdLine  = (double)macd[^1].Macd;
            snapshot.SignalLine = (double)macd[^1].Signal;
            snapshot.Histogram = (double)macd[^1].Histogram;
        }

        // ADX / DI
        if (n >= 28)
        {
            var adxSeries = ADX.Calculate(candles);
            snapshot.PlusDI  = (double)adxSeries[^1].PlusDI;
            snapshot.MinusDI = (double)adxSeries[^1].MinusDI;
            snapshot.Adx     = (double)adxSeries[^1].ADX;
        }

        // ATR
        if (n >= 15)
        {
            var atr = ATR.Calculate(candles);
            snapshot.Atr = (double)atr[^1];
        }

        // Candlestick patterns (one-bar + two-bar)
        snapshot.IsHammer       = CandlestickPatterns.IsHammer(lastBar);
        snapshot.IsShootingStar = CandlestickPatterns.IsShootingStar(lastBar);
        snapshot.IsDoji         = CandlestickPatterns.IsDoji(lastBar);
        if (priorBar is not null)
        {
            snapshot.IsBullishEngulfing = CandlestickPatterns.IsBullishEngulfing(priorBar, lastBar);
            snapshot.IsBearishEngulfing = CandlestickPatterns.IsBearishEngulfing(priorBar, lastBar);
        }

        // Recent swing high/low — naive: extremes over last 20 bars excluding
        // the current bar (so "at support" doesn't fire on the bar that *makes*
        // the new low).
        if (n >= 5)
        {
            var swingWindow = Math.Min(20, n - 1);
            decimal hi = decimal.MinValue, lo = decimal.MaxValue;
            for (int i = n - 1 - swingWindow; i < n - 1; i++)
            {
                if (i < 0) continue;
                if (candles[i].High > hi) hi = candles[i].High;
                if (candles[i].Low  < lo) lo = candles[i].Low;
            }
            snapshot.RecentSwingHigh = hi == decimal.MinValue ? null : (double)hi;
            snapshot.RecentSwingLow  = lo == decimal.MaxValue ? null : (double)lo;
        }

        return snapshot;
    }
}
