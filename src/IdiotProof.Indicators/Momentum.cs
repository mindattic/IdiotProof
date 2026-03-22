using IdiotProof.Models;

namespace IdiotProof.Indicators;

/// <summary>
/// Price Momentum = Current Close - Close N periods ago.
/// </summary>
public static class Momentum
{
    public static decimal[] Calculate(IReadOnlyList<Candle> candles, int period = 10)
    {
        var result = new decimal[candles.Count];
        for (int i = period; i < candles.Count; i++)
            result[i] = candles[i].Close - candles[i - period].Close;
        return result;
    }
}

/// <summary>
/// Rate of Change = ((Close - Close N ago) / Close N ago) * 100.
/// </summary>
public static class ROC
{
    public static decimal[] Calculate(IReadOnlyList<Candle> candles, int period = 10)
    {
        var result = new decimal[candles.Count];
        for (int i = period; i < candles.Count; i++)
        {
            var prev = candles[i - period].Close;
            result[i] = prev != 0 ? (candles[i].Close - prev) / prev * 100m : 0m;
        }
        return result;
    }
}
