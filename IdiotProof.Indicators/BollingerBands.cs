using IdiotProof.Models;

namespace IdiotProof.Indicators;

public record BollingerResult(decimal Upper, decimal Middle, decimal Lower, decimal PercentB, decimal Bandwidth);

/// <summary>
/// Bollinger Bands: Middle = SMA, Upper/Lower = Middle +/- (stdDev * multiplier).
/// </summary>
public static class BollingerBands
{
    public static BollingerResult[] Calculate(IReadOnlyList<Candle> candles, int period = 20, decimal multiplier = 2m)
    {
        var n = candles.Count;
        var result = new BollingerResult[n];

        for (int i = 0; i < n; i++)
        {
            int start = Math.Max(0, i - period + 1);
            int count = i - start + 1;

            if (count < period)
            {
                result[i] = new BollingerResult(0, 0, 0, 0.5m, 0);
                continue;
            }

            decimal sum = 0m;
            for (int j = start; j <= i; j++) sum += candles[j].Close;
            decimal sma = sum / count;

            decimal sumSqDiff = 0m;
            for (int j = start; j <= i; j++)
            {
                decimal diff = candles[j].Close - sma;
                sumSqDiff += diff * diff;
            }
            decimal stdDev = (decimal)Math.Sqrt((double)(sumSqDiff / count));

            decimal upper = sma + multiplier * stdDev;
            decimal lower = sma - multiplier * stdDev;
            decimal bandWidth = upper - lower;
            decimal percentB = bandWidth > 0 ? (candles[i].Close - lower) / bandWidth : 0.5m;
            decimal bandwidth = sma > 0 ? bandWidth / sma * 100m : 0m;

            result[i] = new BollingerResult(upper, sma, lower, percentB, bandwidth);
        }
        return result;
    }
}
