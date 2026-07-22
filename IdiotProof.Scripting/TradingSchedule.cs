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
    // Trading hours: premarket opens 4 AM ET, after-hours closes 8 PM ET.
    // We spin up at 3:45 AM (15 min early) to warm candle caches before
    // the first trade can legally fire; strategies' own EntryWindow conditions
    // gate actual order placement to the correct session.
    private static readonly TimeSpan ActiveStart      = new(3,  45, 0);  // 3:45 AM
    private static readonly TimeSpan ActiveEnd        = new(20,  0, 0);  // 8:00 PM
    private static readonly TimeSpan EarlyCloseActive = new(13,  0, 0);  // 1:00 PM (early-close RTH end)

    /// <summary>Evaluation interval during active trading hours.</summary>
    public static readonly TimeSpan ActiveInterval    = TimeSpan.FromSeconds(1);

    /// <summary>Liveness-ping interval during hibernate.</summary>
    public static readonly TimeSpan HibernateInterval = TimeSpan.FromMinutes(5);

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
