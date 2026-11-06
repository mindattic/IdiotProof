using IdiotProof.Core.Research.Gappers;
using IdiotProof.Models;
using static IdiotProof.Core.Tests.GapperStudy.GapperStudyTestKit;

namespace IdiotProof.Core.Tests.GapperStudy;

/// <summary>IP-T442: the frozen labels on synthetic bars: next-bar-open entry, HOD bucket, fade, velocity death, death-to-HOD, VWAP loss, halt proxy.</summary>
[TestFixture]
public class GapperLabelTests
{
    private const decimal PrevClose = 2.00m;

    private static List<Candle> Shape()
    {
        // 04:00-08:29 flat 2.00; 08:30-09:29 +0.02/min to 3.20; 09:30 3.30, 09:31 3.40 (HOD), then -0.01/min to the close.
        return Session(Day, i =>
        {
            if (i < 270) return 2.00;
            if (i < 330) return 2.00 + 0.02 * (i - 270);
            if (i == 330) return 3.30;
            if (i == 331) return 3.40;
            return Math.Max(1.80, 3.40 - 0.01 * (i - 331));
        }, i => i < 960 ? 1000 : 0);
    }

    [Test]
    public void Entry_IsTheNextBarOpen_AndForwardReturnsUseIt()
    {
        var bars = Shape();
        var t = Et(Day, 9, 0);
        var fwd = GapperStudyLabeler.ComputeForward(Params, bars, Day, t);
        var entryBar = bars.First(b => b.StartUtc >= t);
        Assert.Multiple(() =>
        {
            Assert.That(entryBar.StartUtc, Is.EqualTo(t), "the bar starting at t is the first tradeable one");
            Assert.That(fwd.EntryRef, Is.EqualTo(entryBar.Open));
            Assert.That(fwd.FwdMax["09:45"], Is.EqualTo((double)(3.40m / entryBar.Open - 1m)).Within(1e-9));
            Assert.That(fwd.FwdClose["09:30"], Is.EqualTo((double)(bars.Last(b => b.EndUtc <= Et(Day, 9, 30)).Close / entryBar.Open - 1m)).Within(1e-9));
            Assert.That(fwd.FwdMax.Keys, Is.EquivalentTo(Params.Labels.HorizonsEt));
        });
        var late = GapperStudyLabeler.ComputeForward(Params, bars, Day, Et(Day, 9, 45));
        Assert.That(late.FwdMax["09:30"], Is.Null, "horizons at or before t are not labels");
        Assert.That(late.FwdMax["16:00"], Is.Not.Null);
    }

    [Test]
    public void DayLabels_HodBucket_Fade_Extension_Reach20()
    {
        var day = GapperStudyLabeler.ComputeDay(Params, Shape(), PrevClose, Day);
        Assert.Multiple(() =>
        {
            Assert.That(day.Reach20By0945, Is.True);
            Assert.That(day.PmHigh, Is.EqualTo(3.18m).Within(0.0001m), "last premarket bar (09:29) opens at 3.16 and closes at 3.18");
            Assert.That(day.HodTimeEt, Is.EqualTo(new TimeOnly(9, 31)));
            Assert.That(day.HodTimeRthEt, Is.EqualTo(new TimeOnly(9, 31)));
            Assert.That(day.HodBucket, Is.EqualTo("09:30-09:31"));
            Assert.That(day.Fade, Is.EqualTo((double)(1.80m / 3.18m - 1m)).Within(1e-6));
            Assert.That(day.FadeFlag30, Is.True);
            Assert.That(day.CloseBelowOpen, Is.True);
            Assert.That(day.ExtensionFromOpen0945, Is.EqualTo((double)(3.40m / 3.18m - 1m)).Within(1e-6), "09:30 open is the 09:29 close");
            Assert.That(day.HaltFlag, Is.False);
        });
    }

    [Test]
    public void VelocityDeath_ArmsOnTheRun_DiesOnTwoNegativeBars_AndDeathToHodIsSigned()
    {
        var day = GapperStudyLabeler.ComputeDay(Params, Shape(), PrevClose, Day);
        Assert.Multiple(() =>
        {
            // v3 at 09:35 compares the 09:34 close (3.37) with the 09:31 close (3.40): the first negative minute; 09:36 is the second.
            Assert.That(day.VelocityDeathTimeEt, Is.EqualTo(new TimeOnly(9, 36)));
            Assert.That(day.DeathToHodMin, Is.EqualTo(-5), "the high (09:31) printed five minutes before velocity died");
        });
    }

    [Test]
    public void VelocityDeath_IsNull_WhenNeverArmed()
    {
        var flat = Session(Day, _ => 2.00, _ => 1000);
        Assert.That(GapperStudyLabeler.ComputeDay(Params, flat, PrevClose, Day).VelocityDeathTimeEt, Is.Null);
    }

    [Test]
    public void VwapLoss_IsTheFirstRthMinuteUnderVwap_AfterThreeAbove()
    {
        var day = GapperStudyLabeler.ComputeDay(Params, Shape(), PrevClose, Day);
        Assert.That(day.VwapLossTimeEt, Is.Not.Null);
        Assert.That(day.VwapLossTimeEt, Is.GreaterThanOrEqualTo(new TimeOnly(9, 30)));
        var flat = Session(Day, _ => 2.00, _ => 1000);
        Assert.That(GapperStudyLabeler.ComputeDay(Params, flat, PrevClose, Day).VwapLossTimeEt, Is.Null, "never above VWAP, never a loss");
    }

    [Test]
    public void HaltProxy_FlagsFiveEmptyRthMinutesAfterFivePrintedOnes()
    {
        var halted = Session(Day, _ => 2.50, i => i is >= 340 and < 345 ? 0 : 1000);
        var day = GapperStudyLabeler.ComputeDay(Params, halted, PrevClose, Day);
        Assert.That(day.HaltFlag, Is.True);
        var shortGap = Session(Day, _ => 2.50, i => i is >= 340 and < 344 ? 0 : 1000);
        Assert.That(GapperStudyLabeler.ComputeDay(Params, shortGap, PrevClose, Day).HaltFlag, Is.False, "four empty minutes are not a halt");
    }

    [Test]
    public void HodBuckets_FollowTheFrozenList()
    {
        Assert.Multiple(() =>
        {
            Assert.That(GapperStudyLabeler.HodBucket(Params, new TimeOnly(8, 0)), Is.EqualTo("<09:15"));
            Assert.That(GapperStudyLabeler.HodBucket(Params, new TimeOnly(9, 15)), Is.EqualTo("09:15-09:29"));
            Assert.That(GapperStudyLabeler.HodBucket(Params, new TimeOnly(9, 30)), Is.EqualTo("09:30-09:31"));
            Assert.That(GapperStudyLabeler.HodBucket(Params, new TimeOnly(9, 32)), Is.EqualTo("09:32-09:45"));
            Assert.That(GapperStudyLabeler.HodBucket(Params, new TimeOnly(10, 30)), Is.EqualTo("09:46-10:30"));
            Assert.That(GapperStudyLabeler.HodBucket(Params, new TimeOnly(13, 0)), Is.EqualTo("later"));
        });
    }
}
