using IdiotProof.Models;

namespace IdiotProof.Indicators;

/// <summary>
/// Exponential Moving Average. Seeded by SMA of first period values.
/// </summary>
public static class EMA
{
    public static decimal[] Calculate(IReadOnlyList<Candle> candles, int period)
    {
        if (period <= 0) throw new ArgumentOutOfRangeException(nameof(period));
        var result = new decimal[candles.Count];
        if (candles.Count == 0) return result;
        var k = 2m / (period + 1);
        int seed = Math.Min(period, candles.Count);
        decimal sma = 0m;
        for (int i = 0; i < seed; i++) sma += candles[i].Close;
        sma /= seed;
        result[seed - 1] = sma;
        for (int i = seed; i < candles.Count; i++)
            result[i] = candles[i].Close * k + result[i - 1] * (1 - k);
        for (int i = 0; i < seed - 1; i++) result[i] = result[seed - 1];
        return result;
    }

    /// <summary>
    /// EMA over a raw decimal series (used internally by MACD etc.)
    /// </summary>
    public static decimal[] Calculate(decimal[] data, int period)
    {
        var result = new decimal[data.Length];
        if (data.Length == 0) return result;
        var k = 2m / (period + 1);
        int seed = Math.Min(period, data.Length);
        decimal sma = 0m;
        for (int i = 0; i < seed; i++) sma += data[i];
        sma /= seed;
        result[seed - 1] = sma;
        for (int i = seed; i < data.Length; i++)
            result[i] = data[i] * k + result[i - 1] * (1 - k);
        for (int i = 0; i < seed - 1; i++) result[i] = result[seed - 1];
        return result;
    }
}
