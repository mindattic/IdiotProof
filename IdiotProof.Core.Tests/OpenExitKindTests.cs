using IdiotProof.Core.Monitoring;
using IdiotProof.Models;
using IdiotProof.Scripting;
using IdiotProof.Strategies;

namespace IdiotProof.Core.Tests;

// IP-T594: how a long's pre-open SellBy exit goes out.
public class OpenExitKindTests
{
    // 2026-11-19 is a Thursday, EST (UTC-5).
    private static DateTime Et(int h, int m) => new DateTime(2026, 11, 19, h + 5, m, 0, DateTimeKind.Utc);
    private static readonly TimeSpan SellBy = new(9, 28, 0);

    private static MonitorWorker.OpenExit Kind(DateTime utc, bool unfilled = false, TradeDirection dir = TradeDirection.Long,
        GapperExitReason reason = GapperExitReason.SellByTime, TimeSpan? sellBy = null) =>
        MonitorWorker.OpenExitKind(dir, reason, sellBy ?? SellBy, utc, unfilled);

        [TestCase(4, 0, false, MonitorWorker.OpenExit.None)]
    [TestCase(9, 10, false, MonitorWorker.OpenExit.None)]
    [TestCase(9, 10, true, MonitorWorker.OpenExit.Opg)]
    [TestCase(3, 59, true, MonitorWorker.OpenExit.None)]
    [TestCase(9, 28, true, MonitorWorker.OpenExit.Hold)]
    [TestCase(9, 29, false, MonitorWorker.OpenExit.Hold)]
    [TestCase(9, 30, false, MonitorWorker.OpenExit.Market)]
    [TestCase(9, 31, false, MonitorWorker.OpenExit.Market)]
    [TestCase(16, 0, false, MonitorWorker.OpenExit.None)]
    public void Long_sell_by_by_time_of_day(int h, int m, bool unfilled, MonitorWorker.OpenExit expected) =>
        Assert.That(Kind(Et(h, m), unfilled), Is.EqualTo(expected));

    [Test]
    public void Short_other_reason_late_sell_by_and_weekend_keep_the_limit()
    {
        Assert.That(Kind(Et(9, 31), dir: TradeDirection.Short), Is.EqualTo(MonitorWorker.OpenExit.None));
        Assert.That(Kind(Et(9, 31), reason: GapperExitReason.StopLoss), Is.EqualTo(MonitorWorker.OpenExit.None));
        Assert.That(Kind(Et(9, 31), sellBy: new TimeSpan(15, 55, 0)), Is.EqualTo(MonitorWorker.OpenExit.None));
        Assert.That(Kind(new DateTime(2026, 11, 21, 14, 31, 0, DateTimeKind.Utc)), Is.EqualTo(MonitorWorker.OpenExit.None)); // Saturday
    }
}
