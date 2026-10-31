using IdiotProof.Scripting;

namespace IdiotProof.Strategies.Tests;

/// <summary>
/// The weekend gate: US equity markets never trade Saturday/Sunday,
/// and the Monitor's session windows are time-of-day only — without this
/// helper a Saturday 10:00 ET tick counted as "inside RTH" and could queue
/// an order against Friday's stale prices.
/// </summary>
public class MarketTimeTests
{
    private static DateTime EtToUtc(int year, int month, int day, int hour, int minute) =>
        TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(new DateTime(year, month, day, hour, minute, 0), DateTimeKind.Unspecified),
            MarketTime.Eastern);

    [Test]
    public void IsEquityTradingDay_WeekdayEt_IsTrue()
        // Friday 2026-07-17, 10:00 ET
        => Assert.That(MarketTime.IsEquityTradingDay(EtToUtc(2026, 7, 17, 10, 0)), Is.True);

    [Test]
    public void IsEquityTradingDay_SaturdayEt_IsFalse()
        // Saturday 2026-07-18, 10:00 ET — the time-of-day windows would pass
        => Assert.That(MarketTime.IsEquityTradingDay(EtToUtc(2026, 7, 18, 10, 0)), Is.False);

    [Test]
    public void IsEquityTradingDay_SundayEt_IsFalse()
        => Assert.That(MarketTime.IsEquityTradingDay(EtToUtc(2026, 7, 19, 10, 0)), Is.False);

    [Test]
    public void IsEquityTradingDay_UsesEasternDay_NotUtcDay()
    {
        // Friday 22:00 ET is already Saturday in UTC — must still count as a
        // trading day (after-hours Friday), because the ET calendar decides.
        var fridayLateEt = EtToUtc(2026, 7, 17, 22, 0);
        Assert.That(fridayLateEt.DayOfWeek, Is.EqualTo(DayOfWeek.Saturday), "sanity: UTC day has rolled over");
        Assert.That(MarketTime.IsEquityTradingDay(fridayLateEt), Is.True);
    }

    [TestCase(2021, 12, 31)] // NYSE was open: New Year's Day 2022 fell on a Saturday
    [TestCase(2027, 12, 31)] // New Year's Day 2028 is a Saturday
    public void IsMarketHoliday_SaturdayNewYear_IsNotObservedOnTheFridayBefore(int y, int m, int d)
    {
        // IP-T339: NYSE Rule 7.2 keeps the year-end Friday open instead of observing a Saturday New Year.
        Assert.Multiple(() =>
        {
            Assert.That(MarketTime.IsMarketHoliday(new DateOnly(y, m, d)), Is.False);
            Assert.That(MarketTime.IsEquityTradingDay(EtToUtc(y, m, d, 10, 0)), Is.True);
            Assert.That(TradingSchedule.Classify(EtToUtc(y, m, d, 10, 0)), Is.EqualTo(TradingWindow.Active));
        });
    }
}
