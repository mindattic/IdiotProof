// ============================================================================
// SMA Calculator Tests - Validates Simple Moving Average with real data
// ============================================================================

using IdiotProof.Core.UnitTests.Helpers;
using IdiotProof.Helpers;

namespace IdiotProof.Core.UnitTests.Calculators;

[TestFixture]
public class SmaCalculatorTests
{
    private List<TestBar> bars = null!;

    [OneTimeSetUp]
    public void LoadData()
    {
        bars = TestDataLoader.LoadBars("NVDA", 300);
    }

    // ========================================================================
    // Constructor / Validation
    // ========================================================================

    [Test]
    public void Constructor_WithValidPeriod_CreatesCalculator()
    {
        var sma = new SmaCalculator(20);
        Assert.Multiple(() =>
        {
            Assert.That(sma.Period, Is.EqualTo(20));
            Assert.That(sma.IsReady, Is.False);
            Assert.That(sma.CurrentValue, Is.EqualTo(0));
        });
    }

    [Test]
    public void Constructor_WithZeroPeriod_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SmaCalculator(0));
    }

    // ========================================================================
    // Warm-Up / IsReady
    // ========================================================================

    [Test]
    public void IsReady_BecomesTrueAfterPeriodPrices()
    {
        var sma = new SmaCalculator(20);

        for (int i = 0; i < 19; i++)
        {
            sma.Update(bars[i].Close);
            Assert.That(sma.IsReady, Is.False);
        }

        sma.Update(bars[19].Close);
        Assert.That(sma.IsReady, Is.True, "Should be ready after 20 prices for SMA(20)");
    }

    // ========================================================================
    // Calculation Verification
    // ========================================================================

    [Test]
    public void SMA_EqualsArithmeticMean_OfLastNPrices()
    {
        var sma = new SmaCalculator(20);

        // Feed 50 bars
        for (int i = 0; i < 50; i++)
            sma.Update(bars[i].Close);

        // Manually calculate SMA of last 20 prices
        double expectedSum = 0;
        for (int i = 30; i < 50; i++)
            expectedSum += bars[i].Close;
        double expectedSma = expectedSum / 20;

        Assert.That(sma.CurrentValue, Is.EqualTo(expectedSma).Within(0.0001),
            "SMA should be the arithmetic mean of the last 20 prices");
    }

    [Test]
    public void SMA_SlidingWindow_DropsOldestPrice()
    {
        var sma = new SmaCalculator(5);

        // Feed exactly 5 prices
        for (int i = 0; i < 5; i++)
            sma.Update(bars[i].Close);

        double sma5 = sma.CurrentValue;
        double expectedSum = bars.Take(5).Sum(b => b.Close) / 5.0;
        Assert.That(sma5, Is.EqualTo(expectedSum).Within(0.0001));

        // Feed 6th price - should drop the 1st
        sma.Update(bars[5].Close);
        double expectedAfter = bars.Skip(1).Take(5).Sum(b => b.Close) / 5.0;
        Assert.That(sma.CurrentValue, Is.EqualTo(expectedAfter).Within(0.0001));
    }

    // ========================================================================
    // PreviousValue / Slope
    // ========================================================================

    [Test]
    public void PreviousValue_Tracks_LastSMA()
    {
        var sma = new SmaCalculator(10);
        for (int i = 0; i < 20; i++)
            sma.Update(bars[i].Close);

        double prev = sma.CurrentValue;
        sma.Update(bars[20].Close);

        Assert.That(sma.PreviousValue, Is.EqualTo(prev).Within(0.0001));
    }

    [Test]
    public void IsRising_WhenCurrentGreaterThanPrevious()
    {
        var sma = new SmaCalculator(5);
        // Feed rising prices
        double[] rising = { 100, 101, 102, 103, 104, 105, 106 };
        foreach (var p in rising)
            sma.Update(p);

        Assert.That(sma.IsRising, Is.True);
        Assert.That(sma.Slope, Is.GreaterThan(0));
    }

    [Test]
    public void IsFalling_WhenCurrentLessThanPrevious()
    {
        var sma = new SmaCalculator(5);
        double[] falling = { 110, 109, 108, 107, 106, 105, 104 };
        foreach (var p in falling)
            sma.Update(p);

        Assert.That(sma.IsFalling, Is.True);
        Assert.That(sma.Slope, Is.LessThan(0));
    }

    // ========================================================================
    // Helper Methods
    // ========================================================================

    [Test]
    public void IsPriceAbove_ReturnsTrue_WhenAboveSMA()
    {
        var sma = new SmaCalculator(20);
        for (int i = 0; i < 20; i++)
            sma.Update(bars[i].Close);

        double highPrice = sma.CurrentValue + 10;
        Assert.That(sma.IsPriceAbove(highPrice), Is.True);
    }

    [Test]
    public void IsPriceBelow_ReturnsTrue_WhenBelowSMA()
    {
        var sma = new SmaCalculator(20);
        for (int i = 0; i < 20; i++)
            sma.Update(bars[i].Close);

        double lowPrice = sma.CurrentValue - 10;
        Assert.That(sma.IsPriceBelow(lowPrice), Is.True);
    }

    [Test]
    public void GetDistancePercent_ReturnsCorrectPercentage()
    {
        var sma = new SmaCalculator(20);
        for (int i = 0; i < 20; i++)
            sma.Update(bars[i].Close);

        double smaVal = sma.CurrentValue;
        double testPrice = smaVal * 1.05; // 5% above
        double dist = sma.GetDistancePercent(testPrice);

        Assert.That(dist, Is.EqualTo(5.0).Within(0.1));
    }

    [Test]
    public void GetScore_ReturnsPositive_WhenPriceAboveSMA()
    {
        var sma = new SmaCalculator(20);
        for (int i = 0; i < 20; i++)
            sma.Update(bars[i].Close);

        double abovePrice = sma.CurrentValue * 1.02;
        int score = sma.GetScore(abovePrice);
        Assert.That(score, Is.GreaterThan(0));
    }

    [Test]
    public void GetScore_ReturnsNegative_WhenPriceBelowSMA()
    {
        var sma = new SmaCalculator(20);
        for (int i = 0; i < 20; i++)
            sma.Update(bars[i].Close);

        double belowPrice = sma.CurrentValue * 0.98;
        int score = sma.GetScore(belowPrice);
        Assert.That(score, Is.LessThan(0));
    }

    // ========================================================================
    // Reset
    // ========================================================================

    [Test]
    public void Reset_ClearsAllState()
    {
        var sma = new SmaCalculator(20);
        for (int i = 0; i < 30; i++)
            sma.Update(bars[i].Close);

        sma.Reset();

        Assert.Multiple(() =>
        {
            Assert.That(sma.IsReady, Is.False);
            Assert.That(sma.CurrentValue, Is.EqualTo(0));
            Assert.That(sma.PreviousValue, Is.EqualTo(0));
        });
    }

    // ========================================================================
    // Edge Cases
    // ========================================================================

    [Test]
    public void Update_WithZeroPrice_Ignored()
    {
        var sma = new SmaCalculator(5);
        for (int i = 0; i < 5; i++)
            sma.Update(bars[i].Close);

        double before = sma.CurrentValue;
        sma.Update(0);
        Assert.That(sma.CurrentValue, Is.EqualTo(before));
    }

    [Test]
    public void SMA_WithRealData_StaysWithinPriceRange()
    {
        var sma = new SmaCalculator(50);
        for (int i = 0; i < 200; i++)
            sma.Update(bars[i].Close);

        double min = bars.Take(200).Min(b => b.Low);
        double max = bars.Take(200).Max(b => b.High);

        Assert.That(sma.CurrentValue, Is.GreaterThanOrEqualTo(min).And.LessThanOrEqualTo(max),
            "SMA should be within the observed price range");
    }
}
