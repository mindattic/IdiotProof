using IdiotProof.Evidence.Gappers;
using IdiotProof.Models;

namespace IdiotProof.Core.Research.Gappers;

/// <summary>Labels that depend only on the day (not on the decision time).</summary>
public sealed record GapperDayLabels(
    bool? Reach20By0945,
    decimal? PmHigh,
    TimeOnly? HodTimePmEt,
    TimeOnly? HodTimeRthEt,
    TimeOnly? HodTimeEt,
    string? HodBucket,
    double? Fade,
    bool? FadeFlag30,
    bool? CloseBelowOpen,
    double? ExtensionFromOpen0945,
    TimeOnly? VelocityDeathTimeEt,
    int? DeathToHodMin,
    bool? HaltFlag,
    TimeOnly? VwapLossTimeEt);

/// <summary>Forward returns from the next-bar-open entry after t, per horizon label ("09:30", "16:00").</summary>
public sealed record GapperForwardLabels(decimal? EntryRef, DateTime? EntryUtc, IReadOnlyDictionary<string, double?> FwdMax, IReadOnlyDictionary<string, double?> FwdClose);

/// <summary>
/// The frozen labels (protocol §Labels), computed from a day's extended-hours minute bars and the
/// split-adjusted prior close. Entry is the open of the first bar starting at or after t (the bar
/// at t is never traded); every window is wall-clock on forward-filled closes; the halt flag is a
/// bar-gap proxy. Pure.
/// </summary>
public static class GapperStudyLabeler
{
    public static GapperDayLabels ComputeDay(GapperStudyParams p, IReadOnlyList<Candle> dayBars, decimal prevClose, DateOnly dateEt,
        TimeOfDayProfile? profile = null, int? advDecile = null, double? adv20Shares = null)
    {
        var l = p.Labels;
        var sessionStart = MinuteBarMath.ToUtc(dateEt, MinuteBarMath.SessionStartEt);
        var open = MinuteBarMath.ToUtc(dateEt, MinuteBarMath.RthOpenEt);
        var close = MinuteBarMath.ToUtc(dateEt, new TimeSpan(16, 0, 0));
        var reachBy = MinuteBarMath.ToUtc(dateEt, GapperStudyParams.ParseEt(l.Reach20.ByEt));
        var extAt = MinuteBarMath.ToUtc(dateEt, GapperStudyParams.ParseEt(l.ExtensionFromOpenAtEt));

        var bars = dayBars.Where(b => b.StartUtc >= sessionStart).OrderBy(b => b.StartUtc).ToList();
        if (bars.Count == 0)
            return new GapperDayLabels(null, null, null, null, null, null, null, null, null, null, null, null, null, null);

        var pm = bars.Where(b => b.EndUtc <= open).ToList();
        var rth = bars.Where(b => b.StartUtc >= open && b.EndUtc <= close).ToList();
        var session = bars.Where(b => b.EndUtc <= close).ToList();
        var pmHighBar = pm.Count > 0 ? pm.MaxBy(b => b.High) : null;
        var rthHighBar = rth.Count > 0 ? rth.MaxBy(b => b.High) : null;
        var hodBar = session.Count > 0 ? session.MaxBy(b => b.High) : null;

        bool? reach20 = prevClose > 0 && bars.Any(b => b.EndUtc <= reachBy)
            ? (double)(bars.Where(b => b.EndUtc <= reachBy).Max(b => b.High) / prevClose - 1m) >= l.Reach20.Threshold
            : null;

        var lastClose = session.Count > 0 ? session[^1].Close : (decimal?)null;
        var rthOpenBar = rth.Count > 0 ? rth[0] : null;
        double? fade = pmHighBar is { High: > 0 } && lastClose is { } lc ? (double)(lc / pmHighBar.High - 1m) : null;
        var ext = rth.Where(b => b.EndUtc <= extAt).ToList();
        double? ext0945 = rthOpenBar is { Open: > 0 } && ext.Count > 0 ? (double)(ext.Max(b => b.High) / rthOpenBar.Open - 1m) : null;

        var filled = MinuteBarMath.ForwardFill(bars, sessionStart, close);
        var death = VelocityDeath(p, filled, dateEt);
        TimeOnly? hodTime = hodBar is null ? null : TimeEt(hodBar.StartUtc);
        int? deathToHod = death is { } dt && hodBar is not null
            ? (int)Math.Round((hodBar.StartUtc - MinuteBarMath.ToUtc(dateEt, dt.ToTimeSpan())).TotalMinutes)
            : null;

        return new GapperDayLabels(
            reach20,
            pmHighBar?.High,
            pmHighBar is null ? null : TimeEt(pmHighBar.StartUtc),
            rthHighBar is null ? null : TimeEt(rthHighBar.StartUtc),
            hodTime,
            hodTime is { } ht ? HodBucket(p, ht) : null,
            fade,
            fade is { } f ? f <= l.FadeFlagThreshold : null,
            rthOpenBar is not null && lastClose is { } c2 ? c2 < rthOpenBar.Open : null,
            ext0945,
            death,
            deathToHod,
            HaltProxy(p, bars, dateEt, profile, advDecile, adv20Shares),
            VwapLoss(p, filled, dateEt));
    }

    public static GapperForwardLabels ComputeForward(GapperStudyParams p, IReadOnlyList<Candle> dayBars, DateOnly dateEt, DateTime decisionUtc)
    {
        var bars = dayBars.OrderBy(b => b.StartUtc).ToList();
        var entryBar = bars.FirstOrDefault(b => b.StartUtc >= decisionUtc);
        var fwdMax = new Dictionary<string, double?>();
        var fwdClose = new Dictionary<string, double?>();
        foreach (var h in p.Labels.HorizonsEt)
        {
            var horizon = MinuteBarMath.ToUtc(dateEt, GapperStudyParams.ParseEt(h));
            double? max = null, cl = null;
            if (entryBar is { Open: > 0 } && horizon > decisionUtc)
            {
                var through = bars.Where(b => b.StartUtc >= entryBar.StartUtc && b.EndUtc <= horizon).ToList();
                if (through.Count > 0)
                {
                    max = (double)(through.Max(b => b.High) / entryBar.Open - 1m);
                    cl = (double)(through[^1].Close / entryBar.Open - 1m);
                }
            }
            fwdMax[h] = max;
            fwdClose[h] = cl;
        }
        return new GapperForwardLabels(entryBar?.Open, entryBar?.StartUtc, fwdMax, fwdClose);
    }

    /// <summary>Bucket label for a high-of-day minute from the frozen list ("&lt;09:15", "09:15-09:29", ..., "later").</summary>
    public static string HodBucket(GapperStudyParams p, TimeOnly hodEt)
    {
        foreach (var bucket in p.Labels.HodBuckets)
        {
            if (bucket.StartsWith('<'))
            {
                if (hodEt < TimeOnly.FromTimeSpan(GapperStudyParams.ParseEt(bucket[1..]))) return bucket;
                continue;
            }
            var dash = bucket.IndexOf('-');
            if (dash < 0) continue;
            var from = TimeOnly.FromTimeSpan(GapperStudyParams.ParseEt(bucket[..dash]));
            var to = TimeOnly.FromTimeSpan(GapperStudyParams.ParseEt(bucket[(dash + 1)..]));
            if (hodEt >= from && hodEt <= to) return bucket;
        }
        return p.Labels.HodBuckets.Count > 0 ? p.Labels.HodBuckets[^1] : "later";
    }

    /// <summary>First minute from searchFrom with v_window below zero for the required consecutive bars after an earlier minute exceeded the arm threshold.</summary>
    public static TimeOnly? VelocityDeath(GapperStudyParams p, IReadOnlyList<Candle> filled, DateOnly dateEt)
    {
        var v = p.Labels.VelocityDeath;
        var from = MinuteBarMath.ToUtc(dateEt, GapperStudyParams.ParseEt(v.SearchFromEt));
        var to = MinuteBarMath.ToUtc(dateEt, GapperStudyParams.ParseEt(v.SearchToEt));
        bool armed = false;
        int negatives = 0;
        for (var t = from; t <= to; t = t.AddMinutes(1))
        {
            var vel = MinuteBarMath.Velocity(filled, t, v.Window);
            if (vel is null) { negatives = 0; continue; }
            if (vel > v.ArmThresholdPctPerMin) armed = true;
            negatives = vel < 0 ? negatives + 1 : 0;
            if (armed && negatives >= v.ConsecutiveNegativeBars && vel < 0) return TimeEt(t);
        }
        return null;
    }

    /// <summary>First minute at or after 09:30 whose close is under the session VWAP after the required run of minutes above it.</summary>
    public static TimeOnly? VwapLoss(GapperStudyParams p, IReadOnlyList<Candle> filled, DateOnly dateEt)
    {
        var open = MinuteBarMath.ToUtc(dateEt, MinuteBarMath.RthOpenEt);
        var close = MinuteBarMath.ToUtc(dateEt, new TimeSpan(16, 0, 0));
        decimal pv = 0, vol = 0;
        int above = 0;
        foreach (var b in filled)
        {
            if (b.EndUtc > close) break;
            pv += (b.High + b.Low + b.Close) / 3m * b.Volume;
            vol += b.Volume;
            if (vol <= 0) continue;
            var vwap = pv / vol;
            if (b.Close < vwap)
            {
                if (b.EndUtc > open && above >= p.Labels.Vwap.MinConsecutiveAboveBeforeLoss) return TimeEt(b.StartUtc);
                above = 0;
            }
            else if (b.Close > vwap) above++;
        }
        return null;
    }

    /// <summary>
    /// Bar-gap halt proxy: at least N consecutive RTH minutes with no bar after activity began,
    /// where each of the M minutes before the gap printed. Activity begins at the first abnormal
    /// minute when a profile is given, else at the first RTH minute with volume.
    /// </summary>
    public static bool? HaltProxy(GapperStudyParams p, IReadOnlyList<Candle> bars, DateOnly dateEt,
        TimeOfDayProfile? profile, int? advDecile, double? adv20Shares)
    {
        var h = p.Labels.HaltProxy;
        var open = MinuteBarMath.ToUtc(dateEt, MinuteBarMath.RthOpenEt);
        var close = MinuteBarMath.ToUtc(dateEt, new TimeSpan(16, 0, 0));
        var printed = new HashSet<DateTime>(bars.Where(b => b.Volume > 0).Select(b => Floor(b.StartUtc)));
        if (printed.Count == 0) return null;

        DateTime? activity = null;
        if (profile is not null && advDecile is { } decile && adv20Shares is { } adv)
        {
            foreach (var b in bars.OrderBy(b => b.StartUtc))
            {
                var expected = adv * profile.MinuteFractionAt(decile, MinuteBarMath.ToEastern(b.EndUtc).TimeOfDay);
                if (expected > 0 && (double)b.Volume >= p.Features.AbnormalVolumeMultiple * expected) { activity = Floor(b.StartUtc); break; }
            }
        }
        activity ??= printed.Where(t => t >= open).OrderBy(t => t).Cast<DateTime?>().FirstOrDefault();
        if (activity is not { } start) return false;

        for (var t = start > open ? start : open; t.AddMinutes(h.MinConsecutiveEmptyRthMinutes) <= close; t = t.AddMinutes(1))
        {
            bool gap = true;
            for (int i = 0; i < h.MinConsecutiveEmptyRthMinutes && gap; i++) gap = !printed.Contains(t.AddMinutes(i));
            if (!gap) continue;
            bool primed = true;
            for (int i = 1; i <= h.RequirePrintsInPriorMinutes && primed; i++) primed = printed.Contains(t.AddMinutes(-i));
            if (primed) return true;
        }
        return false;
    }

    public static TimeOnly TimeEt(DateTime utc) => TimeOnly.FromTimeSpan(MinuteBarMath.ToEastern(utc).TimeOfDay);

    private static DateTime Floor(DateTime utc) => new(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, 0, DateTimeKind.Utc);
}
