using IdiotProof.Models;

namespace IdiotProof.Indicators;

/// <summary>
/// Relative Strength Index using Wilder-style averaging.
/// </summary>
public static class RSI
{
    public static decimal[] Calculate(IReadOnlyList<Candle> candles, int period = 14)
    {
        var result = new decimal[candles.Count];
        if (candles.Count <= 1) return result;

        decimal avgGain = 0m, avgLoss = 0m;
        for (int i = 1; i <= period && i < candles.Count; i++)
        {
            var change = candles[i].Close - candles[i - 1].Close;
            if (change > 0) avgGain += change; else avgLoss -= change;
        }
        int seed = Math.Min(period, candles.Count - 1);
        avgGain /= seed == 0 ? 1 : seed;
        avgLoss /= seed == 0 ? 1 : seed;

        result[seed] = RsiFromAvgs(avgGain, avgLoss);
        for (int i = seed + 1; i < candles.Count; i++)
        {
            var change = candles[i].Close - candles[i - 1].Close;
            var gain = change > 0 ? change : 0m;
            var loss = change < 0 ? -change : 0m;
            avgGain = (avgGain * (period - 1) + gain) / period;
            avgLoss = (avgLoss * (period - 1) + loss) / period;
            result[i] = RsiFromAvgs(avgGain, avgLoss);
        }
        for (int i = 0; i < seed; i++) result[i] = result[seed];
        return result;
    }

    private static decimal RsiFromAvgs(decimal avgGain, decimal avgLoss)
    {
        if (avgLoss == 0m) return 100m;
        var rs = avgGain / avgLoss;
        return 100m - 100m / (1 + rs);
    }
}
