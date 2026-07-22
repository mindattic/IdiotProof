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
    private static readonly TimeSpan EarlyCloseActive = new(13,  5, 0);  // 5 min after 1 PM early-close

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

        // Early-close days: RTH ends at 1 PM ET; no after-hours session.
        var activeEnd = MarketTime.IsEarlyCloseDay(today) ? EarlyCloseActive : ActiveEnd;
        if (tod < ActiveStart || tod >= activeEnd) return TradingWindow.Hibernate;

        return TradingWindow.Active;
    }
}
