using IdiotProof.Models;

namespace IdiotProof.Indicators;

public record StochasticResult(decimal PercentK, decimal PercentD);

/// <summary>
/// Stochastic Oscillator. %K = (Close - LowestLow) / (HighestHigh - LowestLow) * 100. %D = SMA(%K).
/// </summary>
public static class Stochastic
{
    public static StochasticResult[] Calculate(IReadOnlyList<Candle> candles, int kPeriod = 14, int dPeriod = 3)
    {
        var n = candles.Count;
        var result = new StochasticResult[n];
        var kValues = new decimal[n];

        for (int i = 0; i < n; i++)
        {
            int start = Math.Max(0, i - kPeriod + 1);
            if (i - start + 1 < kPeriod)
            {
                kValues[i] = 50m;
                result[i] = new StochasticResult(50m, 50m);
                continue;
            }

            decimal highestHigh = decimal.MinValue, lowestLow = decimal.MaxValue;
            for (int j = start; j <= i; j++)
            {
                if (candles[j].High > highestHigh) highestHigh = candles[j].High;
                if (candles[j].Low < lowestLow) lowestLow = candles[j].Low;
            }

            decimal range = highestHigh - lowestLow;
            kValues[i] = range > 0 ? 100m * (candles[i].Close - lowestLow) / range : 50m;

            // %D = SMA of %K over dPeriod, but only over bars where %K is genuinely
            // defined. Warm-up bars (i < kPeriod - 1) hold a placeholder 50; starting
            // the window no earlier than the first valid %K index keeps those
            // placeholders out of the average for the first dPeriod-1 valid bars.
            int dStart = Math.Max(kPeriod - 1, i - dPeriod + 1);
            decimal dSum = 0m;
            int dCount = 0;
            for (int j = dStart; j <= i; j++) { dSum += kValues[j]; dCount++; }
            decimal percentD = dSum / dCount;

            result[i] = new StochasticResult(kValues[i], percentD);
        }
        return result;
    }
}
