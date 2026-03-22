using IdiotProof.Models;

namespace IdiotProof.Indicators;

/// <summary>
/// On-Balance Volume. Cumulative volume weighted by close direction.
/// </summary>
public static class OBV
{
    public static decimal[] Calculate(IReadOnlyList<Candle> candles)
    {
        var result = new decimal[candles.Count];
        if (candles.Count == 0) return result;

        result[0] = candles[0].Volume;
        for (int i = 1; i < candles.Count; i++)
        {
            if (candles[i].Close > candles[i - 1].Close)
                result[i] = result[i - 1] + candles[i].Volume;
            else if (candles[i].Close < candles[i - 1].Close)
                result[i] = result[i - 1] - candles[i].Volume;
            else
                result[i] = result[i - 1];
        }
        return result;
    }
}
