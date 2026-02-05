// ============================================================================
// MarketTimeTests - Tests for MarketTime helper class
// ============================================================================

using IdiotProof.Shared.Helpers;

namespace IdiotProof.Shared.UnitTests.Helpers;

/// <summary>
/// Tests for MarketTime helper class.
/// All times are in Eastern Time (ET).
/// </summary>
[TestFixture]
public class MarketTimeTests
{
    #region PreMarket Times

    [Test]
    public void PreMarket_Start_Returns0400()
    {
        Assert.That(MarketTime.PreMarket.Start, Is.EqualTo(new TimeOnly(4, 0)));
    }

    [Test]
    public void PreMarket_Ending_Returns0915()
    {
        Assert.That(MarketTime.PreMarket.Ending, Is.EqualTo(new TimeOnly(9, 15)));
    }

    [Test]
    public void PreMarket_RightBeforeBell_Returns0929()
    {
        Assert.That(MarketTime.PreMarket.RightBeforeBell, Is.EqualTo(new TimeOnly(9, 29)));
    }

    [Test]
    public void PreMarket_End_Returns0930()
    {
        Assert.That(MarketTime.PreMarket.End, Is.EqualTo(new TimeOnly(9, 30)));
    }

    [Test]
    public void PreMarket_StartBeforeEnd()
    {
        Assert.That(MarketTime.PreMarket.Start, Is.LessThan(MarketTime.PreMarket.End));
    }

    [Test]
    public void PreMarket_EndingBeforeEnd()
    {
        Assert.That(MarketTime.PreMarket.Ending, Is.LessThan(MarketTime.PreMarket.End));
    }

    #endregion

    #region RTH (Regular Trading Hours) Times

    [Test]
    public void RTH_Start_Returns0930()
    {
        Assert.That(MarketTime.RTH.Start, Is.EqualTo(new TimeOnly(9, 30)));
    }

    [Test]
    public void RTH_Ending_Returns1545()
    {
        Assert.That(MarketTime.RTH.Ending, Is.EqualTo(new TimeOnly(15, 45)));
    }

    [Test]
    public void RTH_End_Returns1600()
    {
        Assert.That(MarketTime.RTH.End, Is.EqualTo(new TimeOnly(16, 0)));
    }

    [Test]
    public void RTH_StartBeforeEnd()
    {
        Assert.That(MarketTime.RTH.Start, Is.LessThan(MarketTime.RTH.End));
    }

    [Test]
    public void RTH_EndingBeforeEnd()
    {
        Assert.That(MarketTime.RTH.Ending, Is.LessThan(MarketTime.RTH.End));
    }

    [Test]
    public void RTH_StartEqualsPreMarketEnd()
    {
        Assert.That(MarketTime.RTH.Start, Is.EqualTo(MarketTime.PreMarket.End));
    }

    #endregion

    #region AfterHours Times

    [Test]
    public void AfterHours_Start_Returns1600()
    {
        Assert.That(MarketTime.AfterHours.Start, Is.EqualTo(new TimeOnly(16, 0)));
    }

    [Test]
    public void AfterHours_Ending_Returns1945()
    {
        Assert.That(MarketTime.AfterHours.Ending, Is.EqualTo(new TimeOnly(19, 45)));
    }

    [Test]
    public void AfterHours_End_Returns2000()
    {
        Assert.That(MarketTime.AfterHours.End, Is.EqualTo(new TimeOnly(20, 0)));
    }

    [Test]
    public void AfterHours_StartBeforeEnd()
    {
        Assert.That(MarketTime.AfterHours.Start, Is.LessThan(MarketTime.AfterHours.End));
    }

    [Test]
    public void AfterHours_EndingBeforeEnd()
    {
        Assert.That(MarketTime.AfterHours.Ending, Is.LessThan(MarketTime.AfterHours.End));
    }

    [Test]
    public void AfterHours_StartEqualsRTHEnd()
    {
        Assert.That(MarketTime.AfterHours.Start, Is.EqualTo(MarketTime.RTH.End));
    }

    #endregion

    #region Session Continuity

    [Test]
    public void Sessions_AreContiguous()
    {
        // PreMarket ends when RTH starts
        Assert.That(MarketTime.PreMarket.End, Is.EqualTo(MarketTime.RTH.Start));

        // RTH ends when AfterHours starts
        Assert.That(MarketTime.RTH.End, Is.EqualTo(MarketTime.AfterHours.Start));
    }

    [Test]
    public void ExtendedHours_CoverFullDay()
    {
        // Extended hours should cover 4:00 AM to 8:00 PM
        Assert.That(MarketTime.PreMarket.Start, Is.EqualTo(new TimeOnly(4, 0)));
        Assert.That(MarketTime.AfterHours.End, Is.EqualTo(new TimeOnly(20, 0)));

        // Total extended hours = 16 hours
        var extendedDuration = MarketTime.AfterHours.End.ToTimeSpan() - MarketTime.PreMarket.Start.ToTimeSpan();
        Assert.That(extendedDuration.TotalHours, Is.EqualTo(16));
    }

    [Test]
    public void RTH_Duration_Is6Point5Hours()
    {
        var rthDuration = MarketTime.RTH.End.ToTimeSpan() - MarketTime.RTH.Start.ToTimeSpan();
        Assert.That(rthDuration.TotalHours, Is.EqualTo(6.5).Within(0.01));
    }

    #endregion
}


