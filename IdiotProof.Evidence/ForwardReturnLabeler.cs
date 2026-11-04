using IdiotProof.Models;

namespace IdiotProof.Evidence;

public enum Direction { Down = -1, Flat = 0, Up = 1 }

/// <summary>What actually happened after bar i: the close-to-close return over the next h bars.</summary>
public readonly record struct ForwardLabel(int Index, double ForwardReturn, Direction Direction,
    double MaxAdverse, double MaxFavorable);

/// <summary>
/// Labels each bar with its realized future. Bar i's label reads only bars i+1..i+h, and the
/// last h bars get no label at all, so a label can never be computed from data that didn't exist yet.
/// </summary>
public static class ForwardReturnLabeler
{
    /// <param name="deadBand">Absolute return below which the move counts as Flat (0.002 = 0.2%).</param>
    public static IReadOnlyList<ForwardLabel> Label(IReadOnlyList<Candle> candles, int horizon, double deadBand = 0)
    {
        if (horizon < 1) throw new ArgumentOutOfRangeException(nameof(horizon));
        var labels = new List<ForwardLabel>(Math.Max(0, candles.Count - horizon));
        for (int i = 0; i + horizon < candles.Count; i++)
        {
            double entry = (double)candles[i].Close;
            if (entry <= 0) continue;
            double exit = (double)candles[i + horizon].Close;
            double ret = exit / entry - 1;

            double lo = double.MaxValue, hi = double.MinValue;
            for (int j = i + 1; j <= i + horizon; j++)
            {
                lo = Math.Min(lo, (double)candles[j].Low);
                hi = Math.Max(hi, (double)candles[j].High);
            }

            var dir = ret > deadBand ? Direction.Up : ret < -deadBand ? Direction.Down : Direction.Flat;
            labels.Add(new ForwardLabel(i, ret, dir, lo / entry - 1, hi / entry - 1));
        }
        return labels;
    }

    /// <summary>Simple close-to-close returns; element i is the return from bar i-1 to bar i (element 0 is 0).</summary>
    public static double[] Returns(IReadOnlyList<Candle> candles)
    {
        var r = new double[candles.Count];
        for (int i = 1; i < candles.Count; i++)
        {
            double prev = (double)candles[i - 1].Close;
            r[i] = prev > 0 ? (double)candles[i].Close / prev - 1 : 0;
        }
        return r;
    }
}
