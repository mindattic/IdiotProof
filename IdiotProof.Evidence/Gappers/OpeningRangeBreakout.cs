namespace IdiotProof.Evidence.Gappers;

/// <summary>The opening range candle: the first real bar's open in the range window, the last real close in it, and the side it points.</summary>
public readonly record struct OrbRange(float Open, float Close, double Volume, int Direction);

/// <summary>One opening-range-breakout trade on the grid: fill, exit and the signed gross return (a short gains when the price falls).</summary>
public readonly record struct OrbTrade(int EntryMinute, double Entry, int ExitMinute, double Exit, double Gross, bool Stopped);

/// <summary>
/// The 5-minute opening-range breakout of Zarattini, Barbon and Aziz (2024, "A Profitable Day Trading Strategy for the U.S.
/// Equity Market"; methods-daytrader-evidence.md, IP-T492) on the <see cref="RunnerGrid"/>: the direction is the 09:30-09:35
/// candle (long if it closed above its open, short if below, none on a doji); the entry is the open of the first real bar
/// from 09:35 (within 5 minutes); the stop sits x ATR14 from the entry and fills at the worse of the stop and the bar open
/// (the entry bar's own range counts); everything left is sold at the open of the first real bar from the exit minute
/// (default 15:55), else at the last close before it. Gross = side x (exit / entry - 1). Pure.
/// </summary>
public static class OpeningRangeBreakout
{
    public const int RangeMinutes = 5;
    /// <summary>Grid minute of 15:55 ET.</summary>
    public const int DefaultExitMinute = 715;

    public static OrbRange? Range(RunnerGrid g, int minutes = RangeMinutes)
    {
        float open = float.NaN, close = float.NaN;
        double vol = 0;
        for (int m = RunnerGrid.RthOpen; m < RunnerGrid.RthOpen + minutes; m++)
        {
            if (!g.Real(m)) continue;
            if (float.IsNaN(open)) open = g.O[m];
            close = g.C[m];
            vol += g.V[m];
        }
        if (float.IsNaN(open) || !(open > 0)) return null;
        return new OrbRange(open, close, vol, close > open ? 1 : close < open ? -1 : 0);
    }

    /// <summary>The open of the first real bar in [<paramref name="from"/>, <paramref name="until"/>), as (minute, price).</summary>
    public static (int Minute, double Price)? FirstOpen(RunnerGrid g, int from, int until)
    {
        for (int m = Math.Max(0, from); m < Math.Min(until, RunnerGrid.Minutes); m++)
            if (g.Real(m)) return (m, g.O[m]);
        return null;
    }

    /// <summary>The exit print at <paramref name="minute"/>: the first real bar's open from it to 16:00, else the last close before it.</summary>
    public static (int Minute, double Price)? ExitAt(RunnerGrid g, int minute)
    {
        if (FirstOpen(g, minute, RunnerGrid.Minutes) is { } x) return x;
        for (int m = Math.Min(minute, RunnerGrid.Minutes) - 1; m >= 0; m--)
            if (g.Real(m)) return (m, g.C[m]);
        return null;
    }

    /// <summary>
    /// The trade for <paramref name="direction"/> (+1 long, -1 short) with the stop <paramref name="stopAtr"/> x <paramref name="atr"/>
    /// from the entry; null when there is no entry bar within 5 minutes of the range's end or the inputs are unusable.
    /// </summary>
    public static OrbTrade? Simulate(RunnerGrid g, int direction, double atr, double stopAtr, int rangeMinutes = RangeMinutes, int exitMinute = DefaultExitMinute)
    {
        if (direction is not (1 or -1) || !(atr > 0) || !(stopAtr > 0)) return null;
        int start = RunnerGrid.RthOpen + rangeMinutes;
        if (FirstOpen(g, start, Math.Min(start + 5, exitMinute)) is not { } en || !(en.Price > 0)) return null;
        double entry = en.Price;
        double stop = entry - direction * stopAtr * atr;
        for (int m = en.Minute; m < exitMinute; m++)
        {
            if (!g.Real(m)) continue;
            if (direction == 1 && g.L[m] <= stop)
            {
                double fill = Math.Min(stop, g.O[m]);
                if (!(fill > 0)) fill = 0;
                return new OrbTrade(en.Minute, entry, m, fill, fill / entry - 1, true);
            }
            if (direction == -1 && g.H[m] >= stop)
            {
                double fill = Math.Max(stop, g.O[m]);
                return new OrbTrade(en.Minute, entry, m, fill, 1 - fill / entry, true);
            }
        }
        if (ExitAt(g, exitMinute) is not { } ex || !(ex.Price > 0)) return null;
        return new OrbTrade(en.Minute, entry, ex.Minute, ex.Price, direction * (ex.Price / entry - 1), false);
    }

    /// <summary>Mean true range of the <paramref name="sessions"/> daily bars before index <paramref name="j"/> (needs a prior close for each); null if short.</summary>
    public static double? Atr(IReadOnlyList<double> high, IReadOnlyList<double> low, IReadOnlyList<double> close, int j, int sessions = 14)
    {
        if (j - sessions < 1) return null;
        double sum = 0;
        for (int i = j - sessions; i < j; i++)
        {
            double pc = close[i - 1];
            if (!(pc > 0) || !(high[i] > 0) || !(low[i] > 0)) return null;
            sum += Math.Max(high[i] - low[i], Math.Max(Math.Abs(high[i] - pc), Math.Abs(low[i] - pc)));
        }
        return sum / sessions;
    }
}
