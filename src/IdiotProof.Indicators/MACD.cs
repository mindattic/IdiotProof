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

        var signalLine = EMA.Calculate(macdLine, signal);
        var res = new MacdResult[closes.Length];
        for (int i = 0; i < closes.Length; i++)
            res[i] = new MacdResult(macdLine[i], signalLine[i], macdLine[i] - signalLine[i]);
        return res;
    }
}
