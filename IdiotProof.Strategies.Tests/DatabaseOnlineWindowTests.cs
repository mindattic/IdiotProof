using IdiotProof.Scripting;

namespace IdiotProof.Strategies.Tests;

/// <summary>
/// The database online window (IP-T542): premarket open minus a lead to the after-hours close plus a tail, on
/// trading days only. A serverless history database is awake inside it and asleep outside it.
/// </summary>
public class DatabaseOnlineWindowTests
{
    private static readonly TimeSpan Thirty = TimeSpan.FromMinutes(30);

    private static DateTime EtToUtc(int year, int month, int day, int hour, int minute) =>
        TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(new DateTime(year, month, day, hour, minute, 0), DateTimeKind.Unspecified),
            MarketTime.Eastern);

    [Test]
    public void NormalDay_Is0330To2030()
    {
        // Tuesday 2026-11-17
        var w = TradingSchedule.DatabaseOnlineWindow(new DateOnly(2026, 11, 17), Thirty, Thirty);
        Assert.That(w, Is.EqualTo((new TimeSpan(3, 30, 0), new TimeSpan(20, 30, 0))));
    }

    [Test]
    public void EarlyCloseDay_EndsAfterThe1700ExtendedClose()
    {
        // Friday 2026-11-27, the day after Thanksgiving
        var w = TradingSchedule.DatabaseOnlineWindow(new DateOnly(2026, 11, 27), Thirty, Thirty);
        Assert.That(w, Is.EqualTo((new TimeSpan(3, 30, 0), new TimeSpan(17, 30, 0))));
    }

    [TestCase(2026, 11, 21, Description = "Saturday")]
    [TestCase(2026, 11, 22, Description = "Sunday")]
    [TestCase(2026, 11, 26, Description = "Thanksgiving")]
    [TestCase(2026, 12, 25, Description = "Christmas Day")]
    [TestCase(2026, 7, 3, Description = "Independence Day observed (July 4 is a Saturday)")]
    public void WeekendOrHoliday_HasNoWindow(int year, int month, int day)
    {
        Assert.Multiple(() =>
        {
            Assert.That(TradingSchedule.DatabaseOnlineWindow(new DateOnly(year, month, day), Thirty, Thirty), Is.Null);
            Assert.That(TradingSchedule.IsDatabaseOnline(EtToUtc(year, month, day, 10, 0), Thirty, Thirty), Is.False);
        });
    }

    [TestCase(3, 29, false)]
    [TestCase(3, 30, true)]
    [TestCase(3, 55, true)]
    [TestCase(12, 0, true)]
    [TestCase(20, 29, true)]
    [TestCase(20, 30, false)]
    [TestCase(23, 59, false)]
    [TestCase(0, 0, false)]
    public void NormalDay_StartIsInclusive_EndIsExclusive(int hour, int minute, bool online) =>
        Assert.That(TradingSchedule.IsDatabaseOnline(EtToUtc(2026, 11, 17, hour, minute), Thirty, Thirty), Is.EqualTo(online));

    [TestCase(17, 29, true)]
    [TestCase(17, 30, false)]
    [TestCase(20, 15, false)]
    public void EarlyCloseDay_ClosesAt1730(int hour, int minute, bool online) =>
        Assert.That(TradingSchedule.IsDatabaseOnline(EtToUtc(2026, 11, 27, hour, minute), Thirty, Thirty), Is.EqualTo(online));

    [Test]
    public void SummerTime_UsesTheEasternWallClock()
    {
        // Friday 2026-07-17 (EDT, UTC-4): 03:30 ET is 07:30 UTC.
        Assert.Multiple(() =>
        {
            Assert.That(TradingSchedule.IsDatabaseOnline(new DateTime(2026, 7, 17, 7, 29, 0, DateTimeKind.Utc), Thirty, Thirty), Is.False);
            Assert.That(TradingSchedule.IsDatabaseOnline(new DateTime(2026, 7, 17, 7, 30, 0, DateTimeKind.Utc), Thirty, Thirty), Is.True);
        });
    }

    [Test]
    public void LeadAndTail_MoveTheEdges()
    {
        var w = TradingSchedule.DatabaseOnlineWindow(new DateOnly(2026, 11, 17), TimeSpan.FromMinutes(60), TimeSpan.FromMinutes(10));
        Assert.That(w, Is.EqualTo((new TimeSpan(3, 0, 0), new TimeSpan(20, 10, 0))));
    }

    [Test]
    public void NegativeOrOversizedLeadAndTail_StayInsideTheDay()
    {
        Assert.Multiple(() =>
        {
            Assert.That(TradingSchedule.DatabaseOnlineWindow(new DateOnly(2026, 11, 17), TimeSpan.FromMinutes(-15), TimeSpan.FromMinutes(-15)),
                Is.EqualTo((TradingSchedule.PremarketOpen, TradingSchedule.AfterHoursClose)));
            Assert.That(TradingSchedule.DatabaseOnlineWindow(new DateOnly(2026, 11, 17), TimeSpan.FromHours(6), TimeSpan.FromHours(6)),
                Is.EqualTo((TimeSpan.Zero, TimeSpan.FromDays(1))));
        });
    }

    [Test]
    public void TheMonitorsActiveWindow_IsInsideTheDefaultOnlineWindow()
    {
        // 03:55 and 20:04 ET are the first and last active minutes of a normal day.
        Assert.Multiple(() =>
        {
            foreach (var (h, m) in new[] { (3, 55), (9, 30), (20, 4) })
            {
                var utc = EtToUtc(2026, 11, 17, h, m);
                Assert.That(TradingSchedule.Classify(utc), Is.EqualTo(TradingWindow.Active));
                Assert.That(TradingSchedule.IsDatabaseOnline(utc, Thirty, Thirty), Is.True);
            }
        });
    }
}
