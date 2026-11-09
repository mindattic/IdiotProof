using IdiotProof.Models;

namespace IdiotProof.Evidence.Gappers;

/// <summary>
/// One MAE-aware short exit plan (IP-T479, explore fade-short-deep). Everything left open is covered at the open of the first
/// bar at or after <see cref="CoverByEt"/>. Optional legs, all on the remaining position unless noted:
/// a stop (its level is passed per trade, so a volatility-scaled stop resolves it from the name's own range);
/// half covered at the open of the first bar at or after <see cref="HalfAtEt"/>;
/// half covered by a limit <see cref="HalfTargetPct"/> under entry;
/// everything covered by a limit <see cref="TargetPct"/> under entry;
/// a trailing stop <see cref="TrailPct"/> over the lowest low since entry, armed once a low has reached <see cref="TrailActivatePct"/> under entry.
/// </summary>
public sealed record GapperShortPlan(string Name, TimeSpan CoverByEt, TimeSpan? HalfAtEt = null, double? HalfTargetPct = null, double? TargetPct = null,
    double? TrailActivatePct = null, double? TrailPct = null);

/// <param name="GrossReturn">Size-weighted short return before cost over every leg: sum of share x (1 - fill / entry).</param>
/// <param name="MaxAdverse">Highest price traded while any of the position was open (fills included) over entry, minus 1.</param>
/// <param name="ExitReason">What closed the last of the position: stop, trail, target or cover-by.</param>
/// <param name="FinalExitUtc">Start of the bar the last leg filled in.</param>
public sealed record GapperShortPathResult(double GrossReturn, double MaxAdverse, string ExitReason, DateTime FinalExitUtc);

/// <summary>
/// Replays a short along its minute bars under a <see cref="GapperShortPlan"/>. Pure. Fill rules match
/// <see cref="GapperShortSimulator"/>: a stop or trailing stop fills at the worse of its level and the bar's open (gaps and
/// halts fill at the open); a cover limit fills at the better of its level and the bar's open; a clock leg fills at the open of
/// the first real bar at or after its time (the last close when the day ran out). Inside one bar the adverse legs are checked
/// first (pessimistic), the trailing level uses the lowest low of the bars before this one, and the clock half fills at the open
/// before anything else in its bar.
/// </summary>
public static class GapperShortPath
{
    /// <summary>Real bars (volume and an open), ordered by start.</summary>
    public static List<Candle> Real(IReadOnlyList<Candle> dayBars) => dayBars.Where(b => b.Volume > 0 && b.Open > 0).OrderBy(b => b.StartUtc).ToList();

    /// <summary>Null when the entry is at or after the cover time or the price is not positive.</summary>
    /// <param name="real">The day's real bars from <see cref="Real"/>.</param>
    /// <param name="stopPct">Stop over entry in percent (30 = +30%); null for none.</param>
    public static GapperShortPathResult? Simulate(GapperShortPlan plan, List<Candle> real, DateOnly dateEt, DateTime entryUtc, decimal entry, double? stopPct)
    {
        if (entry <= 0 || real.Count == 0) return null;
        var clockUtc = MinuteBarMath.ToUtc(dateEt, plan.CoverByEt);
        if (entryUtc >= clockUtc) return null;
        DateTime? halfUtc = plan.HalfAtEt is { } h ? MinuteBarMath.ToUtc(dateEt, h) : null;
        decimal? stop = stopPct is { } s && double.IsFinite(s) && s > 0 ? entry * (1m + (decimal)(s / 100.0)) : null;
        decimal? halfTarget = plan.HalfTargetPct is { } ht ? entry * (1m - (decimal)(ht / 100.0)) : null;
        decimal? target = plan.TargetPct is { } tp ? entry * (1m - (decimal)(tp / 100.0)) : null;
        decimal? trailArm = plan.TrailActivatePct is { } ta ? entry * (1m - (decimal)(ta / 100.0)) : null;

        double remaining = 1, gross = 0;
        decimal high = entry, low = entry;
        bool halfDone = false, armed = false;
        void Cover(double share, decimal fill)
        {
            gross += share * (double)(1m - fill / entry);
            remaining -= share;
            if (fill > high) high = fill;
        }

        foreach (var b in real)
        {
            if (b.StartUtc < entryUtc) continue;
            if (b.StartUtc >= clockUtc) break;
            if (halfUtc is { } hu && !halfDone && b.StartUtc >= hu && hu < clockUtc)
            {
                Cover(0.5, b.Open);
                halfDone = true;
            }
            if (stop is { } st && b.High >= st)
            {
                if (b.High > high) high = b.High;
                Cover(remaining, Math.Max(st, b.Open));
                return Done("stop", b.StartUtc);
            }
            if (armed && plan.TrailPct is { } tr && low * (1m + (decimal)(tr / 100.0)) is var trail && b.High >= trail)
            {
                if (b.High > high) high = b.High;
                Cover(remaining, Math.Max(trail, b.Open));
                return Done("trail", b.StartUtc);
            }
            if (b.High > high) high = b.High;
            if (halfTarget is { } hl && !halfDone && b.Low <= hl)
            {
                Cover(0.5, Math.Min(hl, b.Open));
                halfDone = true;
            }
            if (target is { } tl && b.Low <= tl)
            {
                Cover(remaining, Math.Min(tl, b.Open));
                return Done("target", b.StartUtc);
            }
            if (b.Low < low) low = b.Low;
            if (trailArm is { } arm && low <= arm) armed = true;
        }

        var bar = real.FirstOrDefault(b => b.StartUtc >= clockUtc);
        if (bar is null)
        {
            var last = real.LastOrDefault(b => b.StartUtc >= entryUtc) ?? real[^1];
            Cover(remaining, last.Close);
            return Done("cover-by (no later bar: last close)", last.StartUtc);
        }
        Cover(remaining, bar.Open);
        return Done("cover-by", bar.StartUtc);

        GapperShortPathResult Done(string reason, DateTime at) => new(gross, (double)(high / entry - 1m), reason, at);
    }

    /// <summary>The short's mark-to-market return at <paramref name="atUtc"/>: 1 - (open of the first real bar at or after it) / entry; the last close when none.</summary>
    public static double? ReturnAt(List<Candle> real, DateTime entryUtc, decimal entry, DateTime atUtc)
    {
        if (entry <= 0 || real.Count == 0 || atUtc < entryUtc) return null;
        var bar = real.FirstOrDefault(b => b.StartUtc >= atUtc);
        decimal px = bar?.Open ?? real[^1].Close;
        return (double)(1m - px / entry);
    }

    /// <summary>When the highest high from the entry bar up to (not including) the bar at <paramref name="untilUtc"/> printed: that bar's start, and the high over entry minus 1.</summary>
    public static (DateTime AtUtc, double Excursion)? HighAfterEntry(List<Candle> real, DateTime entryUtc, decimal entry, DateTime untilUtc)
    {
        Candle? best = null;
        foreach (var b in real)
        {
            if (b.StartUtc < entryUtc) continue;
            if (b.StartUtc >= untilUtc) break;
            if (best is null || b.High > best.High) best = b;
        }
        return best is null || entry <= 0 ? null : (best.StartUtc, (double)(best.High / entry - 1m));
    }

    /// <summary>The 09:30-to-decision range over the 09:30 open: (highest high - lowest low) / open, from bars ending by the decision.</summary>
    public static double? OpeningRange(List<Candle> real, DateOnly dateEt, DateTime decisionUtc)
    {
        var open = MinuteBarMath.ToUtc(dateEt, MinuteBarMath.RthOpenEt);
        decimal hi = 0, lo = decimal.MaxValue, first = 0;
        foreach (var b in real)
        {
            if (b.StartUtc < open || b.EndUtc > decisionUtc) continue;
            if (first == 0) first = b.Open;
            hi = Math.Max(hi, b.High);
            lo = Math.Min(lo, b.Low);
        }
        return first > 0 && hi > 0 ? (double)((hi - lo) / first) : null;
    }

    /// <summary>
    /// Average daily range as a share of the close over the last <paramref name="sessions"/> daily bars before D (each day's
    /// high - low over its own close, so a split between two days cannot inflate it); null without a bar.
    /// </summary>
    public static double? RangePct(IReadOnlyList<(decimal High, decimal Low, decimal Close)> priorDays, int sessions = 14)
    {
        var v = priorDays.Where(d => d.Close > 0 && d.High >= d.Low).TakeLast(sessions).Select(d => (double)((d.High - d.Low) / d.Close)).ToList();
        return v.Count == 0 ? null : v.Average();
    }

    /// <summary>
    /// Fixed-fraction equity paths over bootstrapped years (IP-T479 tail sizing). Each path draws whole trade days with replacement
    /// until it holds <paramref name="tradesPerYear"/> trades (the last day's trades taken in order up to the count); every trade on a
    /// day is sized at <paramref name="fraction"/> of that day's opening equity, so equity x (1 + fraction x sum of the day's net
    /// returns). Returns per path: the year's return, its max drawdown from the running peak, and whether equity fell below
    /// <paramref name="ruinLevel"/> of the start.
    /// </summary>
    public static List<(double Return, double MaxDrawdown, bool Ruined)> SizingPaths(IReadOnlyList<double[]> days, int tradesPerYear, double fraction, int paths, int seed, double ruinLevel = 0.5)
    {
        var result = new List<(double, double, bool)>(paths);
        if (days.Count == 0 || tradesPerYear <= 0) return result;
        var rng = new Random(seed);
        for (int p = 0; p < paths; p++)
        {
            double equity = 1, peak = 1, dd = 0;
            bool ruined = false;
            int left = tradesPerYear;
            while (left > 0)
            {
                var day = days[rng.Next(days.Count)];
                double sum = 0;
                for (int i = 0; i < day.Length && left > 0; i++, left--) sum += day[i];
                equity *= Math.Max(0, 1 + fraction * sum);
                if (equity > peak) peak = equity;
                dd = Math.Max(dd, 1 - equity / peak);
                if (equity < ruinLevel) ruined = true;
            }
            result.Add((equity - 1, dd, ruined));
        }
        return result;
    }
}
