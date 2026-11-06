using IdiotProof.Models;

namespace IdiotProof.Evidence.Gappers;

/// <summary>
/// Wall-clock bar arithmetic shared by the live snapshot builder, the gapper exit brain and the
/// formation-feature study, so velocity means the same thing in every path. Every window is
/// measured in minutes of clock time, never in bar counts: a missing minute carries the last close
/// forward and contributes no volume. "Close as of t" is the close of the last bar whose EndUtc is
/// at or before t.
/// </summary>
public static class MinuteBarMath
{
    public static readonly TimeZoneInfo Eastern = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
    public static readonly TimeSpan SessionStartEt = new(4, 0, 0);
    public static readonly TimeSpan RthOpenEt = new(9, 30, 0);

    public static DateTime ToEastern(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Eastern);

    public static DateTime ToUtc(DateOnly dateEt, TimeSpan timeEt) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(dateEt.ToDateTime(TimeOnly.FromTimeSpan(timeEt)), DateTimeKind.Unspecified), Eastern);

    /// <summary>The ET session date a UTC instant belongs to (a session runs 04:00 ET to 03:59 ET next day).</summary>
    public static DateOnly SessionDate(DateTime utc)
    {
        var et = ToEastern(utc);
        if (et.TimeOfDay < SessionStartEt) et = et.AddDays(-1);
        return DateOnly.FromDateTime(et);
    }

    /// <summary>Close of the last bar ending at or before <paramref name="atUtc"/>; null when none exists.</summary>
    public static decimal? CloseAsOf(IReadOnlyList<Candle> bars, DateTime atUtc)
    {
        for (int i = bars.Count - 1; i >= 0; i--)
            if (bars[i].EndUtc <= atUtc) return bars[i].Close;
        return null;
    }

    /// <summary>
    /// Price velocity in percent per minute over the last <paramref name="windowMinutes"/> minutes of
    /// clock time ending at <paramref name="nowUtc"/>. Null when no close exists at the window start.
    /// </summary>
    public static double? Velocity(IReadOnlyList<Candle> bars, DateTime nowUtc, int windowMinutes)
    {
        if (windowMinutes <= 0) return null;
        var now = CloseAsOf(bars, nowUtc);
        var then = CloseAsOf(bars, nowUtc.AddMinutes(-windowMinutes));
        if (now is not { } c1 || then is not { } c0 || c0 <= 0) return null;
        return (double)(c1 / c0 - 1m) * 100.0 / windowMinutes;
    }

    /// <summary>Change in the 3-minute velocity over the last three minutes: v3(now) - v3(now - 3).</summary>
    public static double? Acceleration(IReadOnlyList<Candle> bars, DateTime nowUtc)
    {
        var now = Velocity(bars, nowUtc, 3);
        var before = Velocity(bars, nowUtc.AddMinutes(-3), 3);
        return now is { } a && before is { } b ? a - b : null;
    }

    /// <summary>Volume printed by bars ending in (fromUtc, toUtc].</summary>
    public static decimal VolumeBetween(IReadOnlyList<Candle> bars, DateTime fromUtc, DateTime toUtc)
    {
        decimal sum = 0;
        foreach (var b in bars)
            if (b.EndUtc > fromUtc && b.EndUtc <= toUtc) sum += b.Volume;
        return sum;
    }

    /// <summary>Volume of the last three minutes over the three minutes before them; null when the earlier window printed nothing.</summary>
    public static double? VolumeRoc(IReadOnlyList<Candle> bars, DateTime nowUtc)
    {
        var recent = VolumeBetween(bars, nowUtc.AddMinutes(-3), nowUtc);
        var prior = VolumeBetween(bars, nowUtc.AddMinutes(-6), nowUtc.AddMinutes(-3));
        return prior > 0 ? (double)(recent / prior) : null;
    }

    /// <summary>Bars on the session of <paramref name="nowUtc"/> from 04:00 ET that end at or before it.</summary>
    public static List<Candle> SessionBars(IReadOnlyList<Candle> bars, DateTime nowUtc)
    {
        var day = SessionDate(nowUtc);
        var start = ToUtc(day, SessionStartEt);
        var list = new List<Candle>();
        foreach (var b in bars)
            if (b.EndUtc > start && b.EndUtc <= nowUtc) list.Add(b);
        return list;
    }

    /// <summary>Volume-weighted average of typical price over the session so far; null when nothing traded.</summary>
    public static decimal? SessionVwap(IReadOnlyList<Candle> bars, DateTime nowUtc)
    {
        decimal pv = 0, vol = 0;
        foreach (var b in SessionBars(bars, nowUtc))
        {
            pv += (b.High + b.Low + b.Close) / 3m * b.Volume;
            vol += b.Volume;
        }
        return vol > 0 ? pv / vol : null;
    }

    /// <summary>Open of the first bar starting at or after 09:30 ET on the session of <paramref name="nowUtc"/>; null before the bell.</summary>
    public static decimal? RthOpen(IReadOnlyList<Candle> bars, DateTime nowUtc)
    {
        var open = ToUtc(SessionDate(nowUtc), RthOpenEt);
        foreach (var b in bars)
            if (b.StartUtc >= open && b.EndUtc <= nowUtc) return b.Open;
        return null;
    }

    /// <summary>
    /// One bar per clock minute from <paramref name="fromUtc"/> up to <paramref name="toUtc"/> (exclusive):
    /// real bars where they exist, otherwise a zero-volume bar carrying the last close. Bars before the
    /// first real bar are omitted (nothing to carry).
    /// </summary>
    public static List<Candle> ForwardFill(IReadOnlyList<Candle> bars, DateTime fromUtc, DateTime toUtc)
    {
        var byStart = new Dictionary<DateTime, Candle>();
        foreach (var b in bars)
            if (b.StartUtc >= fromUtc && b.EndUtc <= toUtc) byStart[Floor(b.StartUtc)] = b;
        var result = new List<Candle>();
        Candle? last = null;
        for (var t = Floor(fromUtc); t < toUtc; t = t.AddMinutes(1))
        {
            if (byStart.TryGetValue(t, out var real))
            {
                result.Add(real);
                last = real;
            }
            else if (last is not null)
            {
                result.Add(new Candle
                {
                    Symbol = last.Symbol, StartUtc = t, EndUtc = t.AddMinutes(1),
                    Open = last.Close, High = last.Close, Low = last.Close, Close = last.Close, Volume = 0,
                });
            }
        }
        return result;
    }

    private static DateTime Floor(DateTime utc) =>
        new(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, 0, DateTimeKind.Utc);
}
