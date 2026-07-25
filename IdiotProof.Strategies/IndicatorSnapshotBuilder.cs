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
    /// periods with the defaults. <paramref name="previousClose"/> is the prior
    /// day's official close; without it gap conditions fail closed.
    /// </summary>
    public static IndicatorSnapshot BuildWithEmas(string symbol, IReadOnlyList<Candle> candles, IEnumerable<int> emaPeriods, decimal? previousClose = null)
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
            PreviousClose = previousClose is { } pc ? (double)pc : null,
            Volume     = (long)lastBar.Volume,
        };

        // Average volume — the up-to-20 bars BEFORE the current one. The current
        // bar is excluded on purpose: VolumeRatio = Volume / AverageVolume is a
        // spike test, and folding the spike bar into its own baseline dilutes the
        // ratio (a true 10x bar would read ~7x), making WithVolumeConfirm/
        // IsVolumeAbove harder to satisfy than authored. Falls back to the current
        // bar only when it's the sole bar available.
        var volWindow = Math.Min(20, n - 1);
        decimal volSum = 0m;
        if (volWindow > 0)
        {
            for (int i = n - 1 - volWindow; i < n - 1; i++) volSum += candles[i].Volume;
            snapshot.AverageVolume = (double)(volSum / volWindow);
        }
        else
        {
            snapshot.AverageVolume = (double)lastBar.Volume;
        }

        // Window extremes — the per-tick evaluators' only memory of what price
        // did earlier (Breakout latches, HoldsAbove violations). Whole window.
        decimal winHi = decimal.MinValue, winLo = decimal.MaxValue;
        for (int i = 0; i < n; i++)
        {
            if (candles[i].High > winHi) winHi = candles[i].High;
            if (candles[i].Low < winLo) winLo = candles[i].Low;
        }
        snapshot.WindowHigh = (double)winHi;
        snapshot.WindowLow = (double)winLo;

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

        // RSI — needs a full `period` (14) of price changes before Wilder's
        // smoothing has a real seed; n>=2 let a single noisy bar produce a
        // fabricated RSI of exactly 0 or 100 (whichever direction that one
        // bar moved) instead of failing closed per IP-LAW-1.
        if (n >= 15)
        {
            var rsi = RSI.Calculate(candles);
            snapshot.Rsi = (double)rsi[^1];
        }

        // MACD — the signal line is a 9-period EMA of the MACD line itself,
        // which needs `slow` (26) bars before it exists at all. n>=26 gave
        // the signal EMA exactly ONE MACD point to seed from, so Signal was
        // forced equal to Macd (Histogram=0) — IsMacdBullish (Macd>Signal)
        // was always false, so IsMacdBearish (!IsMacdBullish) spuriously
        // read TRUE on the very first MACD snapshot regardless of price.
        if (n >= 34)
        {
            var macd = MACD.Calculate(candles);
            snapshot.MacdLine  = (double)macd[^1].Macd;
            snapshot.SignalLine = (double)macd[^1].Signal;
            snapshot.Histogram = (double)macd[^1].Histogram;
        }

        // ADX / DI — ADX.Calculate zeroes indices before its own warmup
        // window (2*period), so at exactly n=28 the last index (27) still
        // falls inside that zeroed range: n>=28 served a fabricated Adx=0.0
        // (not null) for one bar before a real smoothed value existed.
        if (n >= 29)
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

            // Swing structure: track the two most-recent pivot lows/highs. A pivot
            // is a local extreme with `piv` bars lower/higher on each side; the last
            // `piv` bars can't be confirmed yet (they lack right-side bars), which is
            // correct — a higher low is only real once price has turned back up.
            const int piv = 3;
            if (n > 2 * piv + 1)
            {
                decimal? oldLo = null, newLo = null, oldHi = null, newHi = null;
                for (int i = piv; i < n - piv; i++)
                {
                    bool isLow = true, isHigh = true;
                    for (int j = i - piv; j <= i + piv && (isLow || isHigh); j++)
                    {
                        if (candles[j].Low  < candles[i].Low)  isLow  = false;
                        if (candles[j].High > candles[i].High) isHigh = false;
                    }
                    if (isLow)  { oldLo = newLo; newLo = candles[i].Low; }
                    if (isHigh) { oldHi = newHi; newHi = candles[i].High; }
                }
                if (oldLo is { } a && newLo is { } b) snapshot.HasHigherLow = b > a;
                if (oldHi is { } c && newHi is { } d) snapshot.HasLowerHigh = d < c;

                // RSI divergence — reuse the same pivot scan but also track RSI
                // at each pivot. Classic divergence: price and RSI disagree at
                // matching swing extremes, signalling exhaustion.
                //
                //   Bullish: price makes a lower low  + RSI makes a higher low
                //            → downside momentum is waning; reversal likely up.
                //   Bearish: price makes a higher high + RSI makes a lower high
                //            → upside momentum is waning; reversal likely down.
                //
                // Indices < rsiPeriod carry the seed RSI value (fabricated), so
                // we skip them — a pivot at bar 2 with RSI=72 is noise, not signal.
                if (n >= 15)
                {
                    const int rsiPeriod = 14;
                    var rsiSeries = RSI.Calculate(candles);

                    // Rolling window of the two most-recent pivot lows / highs
                    // (price and RSI value at each). Older = 2, newer = 1.
                    decimal? pivLo1 = null, pivLo2 = null;
                    decimal? rsiLo1 = null, rsiLo2 = null;
                    decimal? pivHi1 = null, pivHi2 = null;
                    decimal? rsiHi1 = null, rsiHi2 = null;

                    for (int i = piv; i < n - piv; i++)
                    {
                        if (i < rsiPeriod) continue;
                        bool isLow = true, isHigh = true;
                        for (int j = i - piv; j <= i + piv && (isLow || isHigh); j++)
                        {
                            if (candles[j].Low  < candles[i].Low)  isLow  = false;
                            if (candles[j].High > candles[i].High) isHigh = false;
                        }
                        if (isLow)
                        {
                            pivLo2 = pivLo1; rsiLo2 = rsiLo1;
                            pivLo1 = candles[i].Low; rsiLo1 = rsiSeries[i];
                        }
                        if (isHigh)
                        {
                            pivHi2 = pivHi1; rsiHi2 = rsiHi1;
                            pivHi1 = candles[i].High; rsiHi1 = rsiSeries[i];
                        }
                    }

                    // Bullish: newer pivot low below older (lower low in price),
                    // but RSI at the newer pivot is above RSI at the older one.
                    if (pivLo1 is { } lo1 && pivLo2 is { } lo2
                        && rsiLo1 is { } rlo1 && rsiLo2 is { } rlo2)
                        snapshot.HasBullishDivergence = lo1 < lo2 && rlo1 > rlo2;

                    // Bearish: newer pivot high above older (higher high in price),
                    // but RSI at the newer pivot is below RSI at the older one.
                    if (pivHi1 is { } hi1 && pivHi2 is { } hi2
                        && rsiHi1 is { } rhi1 && rsiHi2 is { } rhi2)
                        snapshot.HasBearishDivergence = hi1 > hi2 && rhi1 < rhi2;
                }
            }
        }

        return snapshot;
    }
}
