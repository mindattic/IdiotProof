namespace IdiotProof.Evidence.Gappers;

/// <summary>
/// Gapper study amendment 09's move start m0, the one definition shared by the A09 explore stage, the runner case study
/// and the live watch-only alert: the first regular-hours minute whose volume is abnormal by the protocol's rule
/// (at least <c>multiple</c> x ADV20 shares x the time-of-day profile's share for the minute ending at m+1, ADV$ decile),
/// whose close is above the prior close, and before which no regular-hours high reached <c>level</c> over the prior close.
/// Arrays are per minute from 04:00 ET (index i = the minute starting 04:00 + i); NaN marks a minute with no bar.
/// </summary>
public static class MoveStartRule
{
    public const double Level = 0.20;
    public const int RthStartIndex = 330;
    public const int RthEndIndex = 720;

    /// <summary>The m0 index, or -1 when no minute in [<paramref name="rthStart"/>, <paramref name="rthEnd"/>) qualifies.</summary>
    public static int Find(IReadOnlyList<double> volume, IReadOnlyList<double> close, IReadOnlyList<double> high, double prevClose,
        double adv20Shares, int advDecile, TimeOfDayProfile profile, double multiple,
        double level = Level, int rthStart = RthStartIndex, int rthEnd = RthEndIndex)
    {
        int end = Math.Min(rthEnd, Math.Min(volume.Count, Math.Min(close.Count, high.Count)));
        bool reached = false;
        for (int i = rthStart; i < end && !reached; i++)
        {
            var endEt = MinuteBarMath.SessionStartEt + TimeSpan.FromMinutes(i + 1);
            double expected = adv20Shares * profile.MinuteFractionAt(advDecile, endEt);
            if (expected > 0 && volume[i] >= multiple * expected && !double.IsNaN(close[i]) && close[i] > prevClose) return i;
            if (!double.IsNaN(high[i]) && high[i] / prevClose - 1 >= level) reached = true;
        }
        return -1;
    }
}
