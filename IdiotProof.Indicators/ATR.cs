using IdiotProof.Models;

namespace IdiotProof.Indicators;

/// <summary>
/// Average True Range using Wilder's smoothing over candle bars.
/// </summary>
public static class ATR
{
    public static decimal[] Calculate(IReadOnlyList<Candle> candles, int period = 14)
    {
        var n = candles.Count;
        var result = new decimal[n];
        if (n < 2) return result;

        var tr = new decimal[n];
        for (int i = 1; i < n; i++)
        {
            var high = candles[i].High;
            var low = candles[i].Low;
            var prevClose = candles[i - 1].Close;
            tr[i] = Math.Max(Math.Max(high - low, Math.Abs(high - prevClose)), Math.Abs(low - prevClose));
        }

        // Wilder smoothing
        int seed = Math.Min(period, n);
        decimal sum = 0m;
        for (int i = 1; i <= seed && i < n; i++) sum += tr[i];
        result[seed] = sum / Math.Max(1, seed);

        for (int i = seed + 1; i < n; i++)
            result[i] = (result[i - 1] * (period - 1) + tr[i]) / period;

        for (int i = 0; i < seed; i++) result[i] = result[seed];
        return result;
    }
}
