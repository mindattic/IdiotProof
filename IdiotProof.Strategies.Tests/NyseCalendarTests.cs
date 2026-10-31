using IdiotProof.Models;
using IdiotProof.Scripting;

namespace IdiotProof.Strategies.Tests;

/// <summary>
/// IP-T5: the NYSE calendar the Monitor's hibernate gate runs on. <see cref="MarketTime.IsMarketHoliday"/>
/// (all ten holidays, Good Friday via the Easter algorithm, observed shifts), <see cref="MarketTime.IsEarlyCloseDay"/>,
/// <see cref="MarketTime.IsInsideSession"/> boundaries and <see cref="TradingSchedule.Classify"/>. A calendar bug
/// lets the Monitor trade on closed days or hibernate through an open session.
/// </summary>
[TestFixture]
public sealed class NyseCalendarTests
{
    private static DateTime Et(int y, int m, int d, int hour, int minute) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(new DateTime(y, m, d, hour, minute, 0), DateTimeKind.Unspecified), MarketTime.Eastern);

    // ── Holidays ────────────────────────────────────────────────────────────────

    // 2026, the published NYSE calendar.
    [TestCase(2026, 1, 1, TestName = "New Year's Day 2026")]
    [TestCase(2026, 1, 19, TestName = "MLK Day 2026 (3rd Monday)")]
    [TestCase(2026, 2, 16, TestName = "Presidents' Day 2026 (3rd Monday)")]
    [TestCase(2026, 4, 3, TestName = "Good Friday 2026")]
    [TestCase(2026, 5, 25, TestName = "Memorial Day 2026 (last Monday)")]
    [TestCase(2026, 6, 19, TestName = "Juneteenth 2026")]
    [TestCase(2026, 7, 3, TestName = "Independence Day 2026 observed Friday (Jul 4 is Saturday)")]
    [TestCase(2026, 9, 7, TestName = "Labor Day 2026 (1st Monday)")]
    [TestCase(2026, 11, 26, TestName = "Thanksgiving 2026 (4th Thursday)")]
    [TestCase(2026, 12, 25, TestName = "Christmas 2026")]
    // Good Friday through the Easter algorithm in other years.
    [TestCase(2024, 3, 29, TestName = "Good Friday 2024")]
    [TestCase(2025, 4, 18, TestName = "Good Friday 2025")]
    [TestCase(2027, 3, 26, TestName = "Good Friday 2027")]
    [TestCase(2019, 4, 19, TestName = "Good Friday 2019")]
    // Saturday -> Friday.
    [TestCase(2021, 12, 24, TestName = "Christmas 2021 observed Friday Dec 24")]
    [TestCase(2027, 6, 18, TestName = "Juneteenth 2027 observed Friday")]
    // Sunday -> Monday.
    [TestCase(2022, 6, 20, TestName = "Juneteenth 2022 observed Monday")]
    [TestCase(2022, 12, 26, TestName = "Christmas 2022 observed Monday")]
    [TestCase(2023, 1, 2, TestName = "New Year 2023 observed Monday Jan 2")]
    [TestCase(2021, 7, 5, TestName = "Independence Day 2021 observed Monday")]
    // Memorial Day when May 31 itself is the Monday.
    [TestCase(2027, 5, 31, TestName = "Memorial Day 2027 is May 31")]
    public void IsMarketHoliday_True(int y, int m, int d)
    {
        var date = new DateOnly(y, m, d);
        Assert.Multiple(() =>
        {
            Assert.That(MarketTime.IsMarketHoliday(date), Is.True);
            Assert.That(MarketTime.IsEquityTradingDay(Et(y, m, d, 10, 0)), Is.False);
            Assert.That(TradingSchedule.Classify(Et(y, m, d, 10, 0)), Is.EqualTo(TradingWindow.Hibernate));
        });
    }

    [TestCase(2026, 1, 2, TestName = "Day after New Year 2026")]
    [TestCase(2026, 4, 6, TestName = "Easter Monday 2026")]
    [TestCase(2026, 4, 2, TestName = "Day before Good Friday 2026")]
    [TestCase(2026, 7, 6, TestName = "Monday after the observed Jul 3 2026")]
    [TestCase(2026, 10, 12, TestName = "Columbus Day 2026 (NYSE open)")]
    [TestCase(2026, 11, 11, TestName = "Veterans Day 2026 (NYSE open)")]
    [TestCase(2026, 11, 27, TestName = "Black Friday 2026 (early close, not closed)")]
    [TestCase(2026, 12, 24, TestName = "Christmas Eve 2026 (early close, not closed)")]
    [TestCase(2026, 5, 18, TestName = "Monday a week before Memorial Day 2026")]
    [TestCase(2021, 12, 31, TestName = "Dec 31 2021 open though New Year 2022 is a Saturday")]
    [TestCase(2026, 12, 31, TestName = "New Year's Eve 2026")]
    public void IsMarketHoliday_False(int y, int m, int d)
    {
        Assert.Multiple(() =>
        {
            Assert.That(MarketTime.IsMarketHoliday(new DateOnly(y, m, d)), Is.False);
            Assert.That(MarketTime.IsEquityTradingDay(Et(y, m, d, 10, 0)), Is.True);
        });
    }

    [Test]
    public void IsMarketHoliday_ExactlyTenFullClosuresIn2026()
    {
        var closures = Enumerable.Range(0, 365)
            .Select(i => new DateOnly(2026, 1, 1).AddDays(i))
            .Where(MarketTime.IsMarketHoliday)
            .ToList();
        Assert.That(closures, Has.Count.EqualTo(10));
    }

    // ── Early close ─────────────────────────────────────────────────────────────

    [TestCase(2026, 12, 24, true, TestName = "Christmas Eve 2026 Thursday")]
    [TestCase(2026, 11, 27, true, TestName = "Black Friday 2026")]
    [TestCase(2025, 11, 28, true, TestName = "Black Friday 2025")]
    [TestCase(2022, 12, 24, false, TestName = "Christmas Eve 2022 Saturday")]
    [TestCase(2023, 12, 24, false, TestName = "Christmas Eve 2023 Sunday")]
    [TestCase(2026, 11, 26, false, TestName = "Thanksgiving itself")]
    [TestCase(2026, 12, 23, false, TestName = "Dec 23")]
    [TestCase(2026, 12, 31, false, TestName = "New Year's Eve")]
    public void IsEarlyCloseDay(int y, int m, int d, bool expected) =>
        Assert.That(MarketTime.IsEarlyCloseDay(new DateOnly(y, m, d)), Is.EqualTo(expected));

    // ── Session boundaries (Tue 2026-07-14, EDT; and Wed 2026-01-14, EST) ───────

    [TestCase(TradingSession.Premarket, 3, 59, false)]
    [TestCase(TradingSession.Premarket, 4, 0, true)]
    [TestCase(TradingSession.Premarket, 9, 29, true)]
    [TestCase(TradingSession.Premarket, 9, 30, false)]
    [TestCase(TradingSession.RTH, 9, 29, false)]
    [TestCase(TradingSession.RTH, 9, 30, true)]
    [TestCase(TradingSession.RTH, 15, 59, true)]
    [TestCase(TradingSession.RTH, 16, 0, false)]
    [TestCase(TradingSession.AfterHours, 15, 59, false)]
    [TestCase(TradingSession.AfterHours, 16, 0, true)]
    [TestCase(TradingSession.AfterHours, 19, 59, true)]
    [TestCase(TradingSession.AfterHours, 20, 0, false)]
    [TestCase(TradingSession.Extended, 3, 59, false)]
    [TestCase(TradingSession.Extended, 4, 0, true)]
    [TestCase(TradingSession.Extended, 9, 30, true)]
    [TestCase(TradingSession.Extended, 16, 0, true)]
    [TestCase(TradingSession.Extended, 19, 59, true)]
    [TestCase(TradingSession.Extended, 20, 0, false)]
    public void IsInsideSession_Boundaries(TradingSession session, int hour, int minute, bool expected)
    {
        Assert.Multiple(() =>
        {
            Assert.That(MarketTime.IsInsideSession(session, Et(2026, 7, 14, hour, minute)), Is.EqualTo(expected), "summer (EDT)");
            Assert.That(MarketTime.IsInsideSession(session, Et(2026, 1, 14, hour, minute)), Is.EqualTo(expected), "winter (EST)");
        });
    }

    [TestCase(TradingSession.Premarket, 5)]
    [TestCase(TradingSession.RTH, 10)]
    [TestCase(TradingSession.AfterHours, 17)]
    [TestCase(TradingSession.Extended, 12)]
    public void IsInsideSession_ClosedOnWeekendsAndHolidays(TradingSession session, int hour)
    {
        Assert.Multiple(() =>
        {
            Assert.That(MarketTime.IsInsideSession(session, Et(2026, 7, 18, hour, 0)), Is.False, "Saturday");
            Assert.That(MarketTime.IsInsideSession(session, Et(2026, 7, 19, hour, 0)), Is.False, "Sunday");
            Assert.That(MarketTime.IsInsideSession(session, Et(2026, 7, 3, hour, 0)), Is.False, "observed Independence Day");
            Assert.That(MarketTime.IsInsideSession(session, Et(2026, 7, 14, hour, 0)), Is.True, "an ordinary Tuesday");
        });
    }

    // ── TradingSchedule.Classify ────────────────────────────────────────────────

    [TestCase(2026, 7, 14, 3, 54, TradingWindow.Hibernate, TestName = "Summer 03:54 before the pre-open buffer")]
    [TestCase(2026, 7, 14, 3, 55, TradingWindow.Active, TestName = "Summer 03:55 active")]
    [TestCase(2026, 7, 14, 12, 0, TradingWindow.Active, TestName = "Summer midday")]
    [TestCase(2026, 7, 14, 20, 4, TradingWindow.Active, TestName = "Summer 20:04 still active")]
    [TestCase(2026, 7, 14, 20, 5, TradingWindow.Hibernate, TestName = "Summer 20:05 hibernate")]
    [TestCase(2026, 1, 14, 3, 55, TradingWindow.Active, TestName = "Winter 03:55 active")]
    [TestCase(2026, 1, 14, 20, 5, TradingWindow.Hibernate, TestName = "Winter 20:05 hibernate")]
    [TestCase(2026, 1, 14, 0, 30, TradingWindow.Hibernate, TestName = "Overnight")]
    [TestCase(2026, 11, 27, 13, 4, TradingWindow.Active, TestName = "Black Friday 13:04 active")]
    [TestCase(2026, 11, 27, 13, 5, TradingWindow.Hibernate, TestName = "Black Friday 13:05 hibernate (no after-hours)")]
    [TestCase(2026, 12, 24, 16, 30, TradingWindow.Hibernate, TestName = "Christmas Eve afternoon hibernate")]
    [TestCase(2026, 11, 26, 10, 0, TradingWindow.Hibernate, TestName = "Thanksgiving hibernate")]
    [TestCase(2021, 12, 24, 10, 0, TradingWindow.Hibernate, TestName = "Dec 24 that is the observed Christmas hibernates all day")]
    [TestCase(2026, 7, 18, 10, 0, TradingWindow.Hibernate, TestName = "Saturday hibernate")]
    [TestCase(2026, 7, 19, 10, 0, TradingWindow.Hibernate, TestName = "Sunday hibernate")]
    public void Classify(int y, int m, int d, int hour, int minute, TradingWindow expected) =>
        Assert.That(TradingSchedule.Classify(Et(y, m, d, hour, minute)), Is.EqualTo(expected));

    [Test]
    public void Classify_UsesTheEasternDate_NotTheUtcDate()
    {
        // Friday 20:00 ET is Saturday in UTC, but it is still Friday's after-hours tail.
        var fridayEvening = Et(2026, 7, 17, 20, 0);
        Assert.Multiple(() =>
        {
            Assert.That(fridayEvening.DayOfWeek, Is.EqualTo(DayOfWeek.Saturday));
            Assert.That(TradingSchedule.Classify(fridayEvening), Is.EqualTo(TradingWindow.Active));
        });
    }

    [Test]
    public void Intervals()
    {
        Assert.Multiple(() =>
        {
            Assert.That(TradingSchedule.ActiveInterval, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(TradingSchedule.HibernateInterval, Is.EqualTo(TimeSpan.FromMinutes(1)));
        });
    }
}
