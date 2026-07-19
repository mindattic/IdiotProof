using IdiotProof.Scripting;

namespace IdiotProof.Strategies.Tests;

/// <summary>
/// The weekend gate (IP-A16): US equity markets never trade Saturday/Sunday,
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
}
