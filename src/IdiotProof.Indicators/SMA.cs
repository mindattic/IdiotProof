using IdiotProof.Models;

namespace IdiotProof.Indicators;

/// <summary>
/// Simple Moving Average over close prices.
/// </summary>
public static class SMA
{
    public static decimal[] Calculate(IReadOnlyList<Candle> candles, int period)
    {
        if (period <= 0) throw new ArgumentOutOfRangeException(nameof(period));
        var result = new decimal[candles.Count];
        if (candles.Count == 0) return result;

        decimal sum = 0m;
        for (int i = 0; i < candles.Count; i++)
        {
            sum += candles[i].Close;
            if (i >= period) sum -= candles[i - period].Close;
            int count = Math.Min(i + 1, period);
            result[i] = sum / count;
        }
        return result;
    }
}
