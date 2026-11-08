using IdiotProof.Models;

namespace IdiotProof.Evidence.Gappers;

/// <summary>How a detection at t is turned into a buy (explore-plan.md track 4).</summary>
public enum GapperEntryKind
{
    /// <summary>The open of the first traded bar starting at or after t.</summary>
    Now,
    /// <summary>The first touch of the 04:00-anchored VWAP after t, at the VWAP (or the bar's open if it opened below).</summary>
    VwapPullback,
    /// <summary>The first trade above the premarket high as of t, at that level (or the bar's open if it gapped over it).</summary>
    PmHighBreakout,
}

public sealed record GapperEntry(DateTime BarStartUtc, decimal Fill);

/// <summary>
/// Finds the fill for one entry kind after a detection at <c>decisionUtc</c>, using only bars that start
/// at or after it for the trigger and only bars ending by the trigger bar for the levels, so nothing looks
/// ahead. Waiting entries give up after <c>windowMinutes</c>. Pure.
/// </summary>
public static class GapperEntrySimulator
{
    public static GapperEntry? Find(GapperEntryKind kind, IReadOnlyList<Candle> dayBars, DateOnly dateEt, DateTime decisionUtc, int windowMinutes = 30)
    {
        var real = dayBars.Where(b => b.Volume > 0 && b.Open > 0).OrderBy(b => b.StartUtc).ToList();
        var giveUp = decisionUtc.AddMinutes(windowMinutes);
        var after = real.Where(b => b.StartUtc >= decisionUtc).ToList();
        if (after.Count == 0) return null;
        if (kind == GapperEntryKind.Now) return new GapperEntry(after[0].StartUtc, after[0].Open);

        var sessionStart = MinuteBarMath.ToUtc(dateEt, MinuteBarMath.SessionStartEt);
        if (kind == GapperEntryKind.PmHighBreakout)
        {
            var before = real.Where(b => b.StartUtc >= sessionStart && b.EndUtc <= decisionUtc).ToList();
            if (before.Count == 0) return null;
            var level = before.Max(b => b.High);
            foreach (var b in after)
            {
                if (b.StartUtc >= giveUp) return null;
                if (b.High > level) return new GapperEntry(b.StartUtc, Math.Max(b.Open, level));
            }
            return null;
        }

        // VWAP pullback: the VWAP through the previous bar is the level the next bar can touch.
        decimal pv = 0, vol = 0;
        foreach (var b in real)
        {
            if (b.StartUtc < sessionStart) continue;
            if (b.StartUtc >= decisionUtc)
            {
                if (b.StartUtc >= giveUp) return null;
                if (vol > 0)
                {
                    var vwap = pv / vol;
                    if (b.Low <= vwap) return new GapperEntry(b.StartUtc, Math.Min(b.Open, vwap));
                }
            }
            pv += (b.High + b.Low + b.Close) / 3m * b.Volume;
            vol += b.Volume;
        }
        return null;
    }
}
