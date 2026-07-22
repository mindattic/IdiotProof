using IdiotProof.Models;

namespace IdiotProof.Indicators;

public record AdxResult(decimal PlusDI, decimal MinusDI, decimal ADX);

/// <summary>
/// Average Directional Index and Directional Indicators using Wilder's formulation.
/// </summary>
public static class ADX
{
    public static AdxResult[] Calculate(IReadOnlyList<Candle> candles, int period = 14)
    {
        var n = candles.Count;
        var results = new AdxResult[n];
        if (n < 2) return results;

        var tr = new decimal[n];
        var dmPlus = new decimal[n];
        var dmMinus = new decimal[n];

        for (int i = 1; i < n; i++)
        {
            var high = candles[i].High;
            var low = candles[i].Low;
            var prevClose = candles[i - 1].Close;
            var prevHigh = candles[i - 1].High;
            var prevLow = candles[i - 1].Low;

            tr[i] = Math.Max(Math.Max(high - low, Math.Abs(high - prevClose)), Math.Abs(low - prevClose));
            var upMove = high - prevHigh;
            var downMove = prevLow - low;
            dmPlus[i] = upMove > downMove && upMove > 0 ? upMove : 0m;
            dmMinus[i] = downMove > upMove && downMove > 0 ? downMove : 0m;
        }

        var atr = WilderSmooth(tr, period);
        var smDmPlus = WilderSmooth(dmPlus, period);
        var smDmMinus = WilderSmooth(dmMinus, period);

        var diPlus = new decimal[n];
        var diMinus = new decimal[n];
        for (int i = 0; i < n; i++)
        {
            diPlus[i] = atr[i] == 0 ? 0 : 100m * smDmPlus[i] / atr[i];
            diMinus[i] = atr[i] == 0 ? 0 : 100m * smDmMinus[i] / atr[i];
        }

        var dx = new decimal[n];
        for (int i = 0; i < n; i++)
        {
            var denom = diPlus[i] + diMinus[i];
            dx[i] = denom == 0 ? 0 : 100m * Math.Abs(diPlus[i] - diMinus[i]) / denom;
        }

        var adx = WilderSmooth(dx, period);

        // ADX requires 2*period bars to be meaningful: the first period produces
        // the DI values, the second smooths those into ADX. The DI backfill makes
        // dx[0..period-1] all identical, so WilderSmooth seeds ADX from that
        // same constant — yielding adx[0..period-1] equal to the first real ADX.
        // Zero out the double-warmup window so strategies using RequireAdxAbove
        // never fire on these undefined early bars.
        int adxWarmup = Math.Min(2 * Math.Min(period, n - 1), n);
        for (int i = 0; i < adxWarmup; i++) adx[i] = 0m;

        for (int i = 0; i < n; i++) results[i] = new AdxResult(diPlus[i], diMinus[i], adx[i]);
        return results;
    }

    private static decimal[] WilderSmooth(decimal[] values, int period)
    {
        var n = values.Length;
        var res = new decimal[n];
        if (n <= 1) return res;

        // values[0] is structurally empty — TR/DM need a prior bar, so the
        // series really starts at index 1. Seeding over [0, period) averaged
        // in that phantom zero and divided by the full window, understating
        // the seed (same defect ATR.Calculate was previously patched for).
        // Seed over the first `seedCount` REAL values instead.
        int seedCount = Math.Min(period, n - 1);
        decimal sum = 0m;
        for (int i = 1; i <= seedCount; i++) sum += values[i];
        res[seedCount] = sum / seedCount;

        for (int i = seedCount + 1; i < n; i++)
            res[i] = (res[i - 1] * (period - 1) + values[i]) / period;

        for (int i = 0; i < seedCount; i++) res[i] = res[seedCount];
        return res;
    }
}
