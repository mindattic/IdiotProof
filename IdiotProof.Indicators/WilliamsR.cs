using IdiotProof.Models;

namespace IdiotProof.Indicators;

/// <summary>
/// Williams %R. Range -100 to 0. Near 0 = overbought, near -100 = oversold.
/// </summary>
public static class WilliamsR
{
    public static decimal[] Calculate(IReadOnlyList<Candle> candles, int period = 14)
    {
        var n = candles.Count;
        var result = new decimal[n];

        for (int i = 0; i < n; i++)
        {
            int start = Math.Max(0, i - period + 1);
            if (i - start + 1 < period)
            {
                result[i] = -50m;
                continue;
            }

            decimal highestHigh = decimal.MinValue, lowestLow = decimal.MaxValue;
            for (int j = start; j <= i; j++)
            {
                if (candles[j].High > highestHigh) highestHigh = candles[j].High;
                if (candles[j].Low < lowestLow) lowestLow = candles[j].Low;
            }

            decimal range = highestHigh - lowestLow;
            result[i] = range > 0 ? (highestHigh - candles[i].Close) / range * -100m : -50m;
        }
        return result;
    }
}
