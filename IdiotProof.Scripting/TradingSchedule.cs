namespace IdiotProof.Scripting;

/// <summary>
/// Classification of the current moment into a Monitor evaluation cadence.
/// </summary>
public enum TradingWindow
{
    /// <summary>
    /// 8:15 PM – 3:45 AM ET on equity trading days (plus weekends and holidays).
    /// No bar data, no orders possible. Monitor writes heartbeats only.
    /// </summary>
    Hibernate,

    /// <summary>
    /// Active trading hours outside the high-frequency windows.
    /// Strategies are evaluated every 5 seconds.
    /// </summary>
    Normal,

    /// <summary>
    /// Critical opens/closes where fast reaction matters.
    /// Strategies are evaluated every 1 second.
    ///
    /// Windows (all ET):
    ///   04:00–04:15  premarket open
    ///   09:15–09:45  pre-bell + RTH open
    ///   15:45–16:15  pre-close + RTH close / afterhours open
    ///   19:45–20:00  afterhours close
    /// </summary>
    HighFrequency,
}

/// <summary>
/// Determines the <see cref="TradingWindow"/> for a given UTC instant using
/// the NYSE market schedule: weekends, US equity holidays, and early-close
/// days are all accounted for.
/// </summary>
public static class TradingSchedule
{
    // Active day: 3:45 AM → 8:15 PM ET (spin-up 15 min before premarket;
    // spin-down 15 min after afterhours closes at 8:00 PM).
    private static readonly TimeSpan ActiveStart      = new(3,  45, 0);
    private static readonly TimeSpan ActiveEnd        = new(20, 15, 0);
    private static readonly TimeSpan EarlyCloseActive = new(17, 15, 0); // 15 min after 5 PM AH close

    // High-frequency critical windows.
    private static readonly (TimeSpan From, TimeSpan To)[] HighFreqWindows =
    [
        (new TimeSpan(4,  0, 0), new TimeSpan(4,  15, 0)),  // premarket open
        (new TimeSpan(9, 15, 0), new TimeSpan(9,  45, 0)),  // pre-bell + RTH open
        (new TimeSpan(15, 45, 0), new TimeSpan(16, 15, 0)), // pre-close + AH open
        (new TimeSpan(19, 45, 0), new TimeSpan(20,  0, 0)), // AH close
    ];

    public static readonly TimeSpan NormalInterval    = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan HighFreqInterval  = TimeSpan.FromSeconds(1);

    public static TradingWindow Classify(DateTime utcNow)
    {
        var et = TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(utcNow, DateTimeKind.Utc), MarketTime.Eastern);
        var today = DateOnly.FromDateTime(et);
        var tod   = et.TimeOfDay;

        // Weekends and full holidays → all day hibernate.
        if (et.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) return TradingWindow.Hibernate;
        if (MarketTime.IsMarketHoliday(today)) return TradingWindow.Hibernate;

        // Active window depends on whether it's an early-close day.
        var activeEnd = MarketTime.IsEarlyCloseDay(today) ? EarlyCloseActive : ActiveEnd;
        if (tod < ActiveStart || tod >= activeEnd) return TradingWindow.Hibernate;

        // High-frequency critical windows.
        foreach (var (from, to) in HighFreqWindows)
            if (tod >= from && tod < to) return TradingWindow.HighFrequency;

        return TradingWindow.Normal;
    }
}
