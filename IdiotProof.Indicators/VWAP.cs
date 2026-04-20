using IdiotProof.Models;

namespace IdiotProof.Indicators;

/// <summary>
/// Volume-Weighted Average Price. Resets at the start of each calendar UTC date
/// (US equity sessions never cross UTC midnight, so date boundaries = session boundaries).
/// </summary>
public static class VWAP
{
    public static decimal[] Calculate(IReadOnlyList<Candle> candles)
    {
        var result = new decimal[candles.Count];
        decimal cumPV = 0m, cumVol = 0m;
        DateOnly currentDay = default;

        for (int i = 0; i < candles.Count; i++)
        {
            var c = candles[i];
            var day = DateOnly.FromDateTime(c.StartUtc);

            if (day != currentDay)
            {
                cumPV = 0m;
                cumVol = 0m;
                currentDay = day;
            }

            var tp = (c.High + c.Low + c.Close) / 3m;
            cumPV += tp * c.Volume;
            cumVol += c.Volume;
            result[i] = cumVol == 0m ? 0m : cumPV / cumVol;
        }

        return result;
    }
}
