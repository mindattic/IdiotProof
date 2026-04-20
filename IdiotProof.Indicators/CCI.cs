using IdiotProof.Models;

namespace IdiotProof.Indicators;

/// <summary>
/// Commodity Channel Index. CCI = (TP - SMA(TP)) / (0.015 * MeanDeviation).
/// </summary>
public static class CCI
{
    public static decimal[] Calculate(IReadOnlyList<Candle> candles, int period = 20)
    {
        var n = candles.Count;
        var result = new decimal[n];

        for (int i = 0; i < n; i++)
        {
            int start = Math.Max(0, i - period + 1);
            int count = i - start + 1;
            if (count < period) continue;

            decimal tpSum = 0m;
            for (int j = start; j <= i; j++)
                tpSum += (candles[j].High + candles[j].Low + candles[j].Close) / 3m;
            decimal sma = tpSum / count;

            decimal meanDev = 0m;
            for (int j = start; j <= i; j++)
            {
                decimal tp = (candles[j].High + candles[j].Low + candles[j].Close) / 3m;
                meanDev += Math.Abs(tp - sma);
            }
            meanDev /= count;

            decimal currentTp = (candles[i].High + candles[i].Low + candles[i].Close) / 3m;
            result[i] = meanDev > 0 ? (currentTp - sma) / (0.015m * meanDev) : 0m;
        }
        return result;
    }
}
