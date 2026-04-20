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
        for (int i = 0; i < n; i++) results[i] = new AdxResult(diPlus[i], diMinus[i], adx[i]);
        return results;
    }

    private static decimal[] WilderSmooth(decimal[] values, int period)
    {
        var n = values.Length;
        var res = new decimal[n];
        if (n == 0) return res;

        int seed = Math.Min(period, n);
        decimal sum = 0m;
        for (int i = 0; i < seed; i++) sum += values[i];
        res[seed - 1] = sum / seed;

        for (int i = seed; i < n; i++)
            res[i] = (res[i - 1] * (period - 1) + values[i]) / period;

        for (int i = 0; i < seed - 1; i++) res[i] = res[seed - 1];
        return res;
    }
}
