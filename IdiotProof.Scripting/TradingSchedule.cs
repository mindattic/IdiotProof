namespace IdiotProof.Scripting;

/// <summary>
/// Classification of the current moment into a Monitor evaluation cadence.
/// </summary>
public enum TradingWindow
{
    /// <summary>
    /// Outside trading hours (8 PM – 4 AM ET, weekends, NYSE holidays).
    /// No bar data arrives, no orders are possible. Monitor emits a
    /// liveness ping every 5 minutes and does nothing else.
    /// </summary>
    Hibernate,

    /// <summary>
    /// All US equity trading hours: premarket (4 AM – 9:30 AM ET),
    /// RTH (9:30 AM – 4 PM ET), and after-hours (4 PM – 8 PM ET).
    /// Trade-stream events update sub-second; strategies are evaluated
    /// every 1 second against the latest trade price.
    /// </summary>
    Active,
}

/// <summary>
/// Determines the <see cref="TradingWindow"/> for a given UTC instant using
/// the NYSE market schedule: weekends, US equity holidays, and early-close
/// days are all accounted for.
/// </summary>
public static class TradingSchedule
{
    // Active window: 5 min before premarket opens (3:55 AM ET) through
    // 5 min after after-hours closes (8:05 PM ET). The 5-minute pre-open
    // buffer gives 5 x 1-minute pings to confirm the process is healthy
    // before the first trade can legally fire.
    private static readonly TimeSpan ActiveStart      = new(3,  55, 0);  // 3:55 AM
    private static readonly TimeSpan ActiveEnd        = new(20,  5, 0);  // 8:05 PM
    // Early-close days: RTH ends at 1 PM but the extended session runs 1-5 PM ET, so a position held
    // into the afternoon (a SellBy past 13:00) is still managed and flattened before it ends.
    private static readonly TimeSpan EarlyCloseActive = new(17,  5, 0);  // 5 min after the 5 PM extended close

    /// <summary>Evaluation interval during active trading hours.</summary>
    public static readonly TimeSpan ActiveInterval    = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Liveness-ping interval during hibernate. One ping per minute so
    /// 5 pings fire in the 5-minute window before the active session begins.
    /// </summary>
    public static readonly TimeSpan HibernateInterval = TimeSpan.FromMinutes(1);

    public static TradingWindow Classify(DateTime utcNow)
    {
        var et = TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(utcNow, DateTimeKind.Utc), MarketTime.Eastern);
        var today = DateOnly.FromDateTime(et);
        var tod   = et.TimeOfDay;

        // Weekends and full holidays → all day hibernate.
        if (et.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) return TradingWindow.Hibernate;
        if (MarketTime.IsMarketHoliday(today)) return TradingWindow.Hibernate;

        // Early-close days: RTH ends at 1 PM ET; the extended session ends at 5 PM ET.
        var activeEnd = MarketTime.IsEarlyCloseDay(today) ? EarlyCloseActive : ActiveEnd;
        if (tod < ActiveStart || tod >= activeEnd) return TradingWindow.Hibernate;

        return TradingWindow.Active;
    }

    // ── Database online window (IP-T542) ─────────────────────────────────────
    // The extended session itself, without the Monitor's 5-minute buffers above: premarket opens 4:00 AM ET and
    // after-hours ends 8:00 PM ET (5:00 PM on an early-close day).

    /// <summary>Premarket open, ET.</summary>
    public static readonly TimeSpan PremarketOpen = new(4, 0, 0);

    /// <summary>End of after-hours trading on a normal day, ET.</summary>
    public static readonly TimeSpan AfterHoursClose = new(20, 0, 0);

    /// <summary>End of the extended session on an early-close day (RTH ends 1 PM), ET.</summary>
    public static readonly TimeSpan EarlyCloseAfterHoursClose = new(17, 0, 0);

    /// <summary>True on a weekday that is not a full market holiday (same calendar as <see cref="Classify"/>).</summary>
    public static bool IsTradingDay(DateOnly etDay) =>
        etDay.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && !MarketTime.IsMarketHoliday(etDay);

    /// <summary>When the extended session ends on <paramref name="etDay"/>, ET: 8 PM, or 5 PM on an early-close day.</summary>
    public static TimeSpan SessionClose(DateOnly etDay) =>
        MarketTime.IsEarlyCloseDay(etDay) ? EarlyCloseAfterHoursClose : AfterHoursClose;

    /// <summary>
    /// The hours of <paramref name="etDay"/> during which a pay-per-use (serverless) database must be awake, as ET
    /// times of day: premarket open minus <paramref name="lead"/> to the session close plus <paramref name="tail"/>
    /// (start inclusive, end exclusive). With 30 minutes each: 03:30-20:30 ET, 03:30-17:30 on an early-close day.
    /// Null on a weekend or a market holiday. A negative lead or tail counts as zero and the window never leaves
    /// its own ET day.
    /// </summary>
    public static (TimeSpan Start, TimeSpan End)? DatabaseOnlineWindow(DateOnly etDay, TimeSpan lead, TimeSpan tail)
    {
        if (!IsTradingDay(etDay)) return null;
        var start = PremarketOpen - (lead > TimeSpan.Zero ? lead : TimeSpan.Zero);
        var end = SessionClose(etDay) + (tail > TimeSpan.Zero ? tail : TimeSpan.Zero);
        if (start < TimeSpan.Zero) start = TimeSpan.Zero;
        if (end > TimeSpan.FromDays(1)) end = TimeSpan.FromDays(1);
        return (start, end);
    }

    /// <summary>True when <paramref name="utcNow"/> falls inside <see cref="DatabaseOnlineWindow"/> of its ET day.</summary>
    public static bool IsDatabaseOnline(DateTime utcNow, TimeSpan lead, TimeSpan tail)
    {
        var et = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc), MarketTime.Eastern);
        return DatabaseOnlineWindow(DateOnly.FromDateTime(et), lead, tail) is { } w && et.TimeOfDay >= w.Start && et.TimeOfDay < w.End;
    }
}
