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

        // Wilder smoothing. The first ATR lands at index `seedIdx`, the average of
        // the TRs at indices 1..seedIdx. With a full window that's index `period`
        // (period TRs); with fewer than period+1 candles we seed on whatever TRs
        // exist and place the value at the last index. Clamp to n-1 so we never
        // index past the end of `result` (the previous `Math.Min(period, n)` wrote
        // result[n] when n <= period, throwing IndexOutOfRangeException).
        int seedIdx = Math.Min(period, n - 1);
        decimal sum = 0m;
        int count = 0;
        for (int i = 1; i <= seedIdx; i++) { sum += tr[i]; count++; }
        result[seedIdx] = sum / Math.Max(1, count);

        for (int i = seedIdx + 1; i < n; i++)
            result[i] = (result[i - 1] * (period - 1) + tr[i]) / period;

        for (int i = 0; i < seedIdx; i++) result[i] = result[seedIdx];
        return result;
    }
}
