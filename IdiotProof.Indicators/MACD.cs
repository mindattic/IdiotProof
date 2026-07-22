using IdiotProof.Models;

namespace IdiotProof.Indicators;

public record MacdResult(decimal Macd, decimal Signal, decimal Histogram);

/// <summary>
/// Moving Average Convergence Divergence.
/// MACD = EMA(fast) - EMA(slow); Signal = EMA(MACD, signal); Histogram = MACD - Signal.
/// </summary>
public static class MACD
{
    public static MacdResult[] Calculate(IReadOnlyList<Candle> candles, int fast = 12, int slow = 26, int signal = 9)
    {
        var closes = new decimal[candles.Count];
        for (int i = 0; i < candles.Count; i++) closes[i] = candles[i].Close;

        var emaFast = EMA.Calculate(closes, fast);
        var emaSlow = EMA.Calculate(closes, slow);

        var macdLine = new decimal[closes.Length];
        for (int i = 0; i < closes.Length; i++) macdLine[i] = emaFast[i] - emaSlow[i];

        // Seed the signal EMA from the first meaningful MACD values (index slow-1
        // is when emaSlow exits its seed period). Seeding from the EMA-backfilled
        // constant prefix biases the signal line for the entire warm-up window.
        var signalLine = new decimal[closes.Length];
        int realMacdStart = Math.Min(slow - 1, closes.Length - 1);
        if (closes.Length > realMacdStart)
        {
            var macdSlice = macdLine[realMacdStart..];
            var signalSlice = EMA.Calculate(macdSlice, signal);
            decimal backfill = signalSlice.Length > 0 ? signalSlice[0] : 0m;
            for (int i = 0; i < realMacdStart; i++) signalLine[i] = backfill;
            for (int i = 0; i < signalSlice.Length; i++) signalLine[realMacdStart + i] = signalSlice[i];
        }

        var res = new MacdResult[closes.Length];
        for (int i = 0; i < closes.Length; i++)
            res[i] = new MacdResult(macdLine[i], signalLine[i], macdLine[i] - signalLine[i]);
        return res;
    }
}
