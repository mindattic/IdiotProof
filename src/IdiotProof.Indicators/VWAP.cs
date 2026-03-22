using IdiotProof.Models;

namespace IdiotProof.Indicators;

/// <summary>
/// Volume-Weighted Average Price. Cumulative across the session.
/// </summary>
public static class VWAP
{
    public static decimal[] Calculate(IReadOnlyList<Candle> candles)
    {
        var result = new decimal[candles.Count];
        decimal cumPV = 0m, cumVol = 0m;
        for (int i = 0; i < candles.Count; i++)
        {
            var c = candles[i];
            var tp = (c.High + c.Low + c.Close) / 3m;
            cumPV += tp * c.Volume;
            cumVol += c.Volume;
            result[i] = cumVol == 0m ? 0m : cumPV / cumVol;
        }
        return result;
    }
}
