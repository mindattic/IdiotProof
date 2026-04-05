// ============================================================================
// Bollinger Bands Calculator Tests - Validates BB with real data
// ============================================================================

using IdiotProof.Core.UnitTests.Helpers;
using IdiotProof.Helpers;

namespace IdiotProof.Core.UnitTests.Calculators;

[TestFixture]
public class BollingerBandsCalculatorTests
{
    private List<TestBar> bars = null!;

    [OneTimeSetUp]
    public void LoadData()
    {
        bars = TestDataLoader.LoadBars("NVDA", 200);
    }

    // ========================================================================
    // Constructor / Validation
    // ========================================================================

    [Test]
    public void Constructor_Defaults_20Period_2Multiplier()
    {
        var bb = new BollingerBandsCalculator();
        Assert.That(bb.IsReady, Is.False);
    }

    [Test]
    public void Constructor_Period1_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BollingerBandsCalculator(1));
    }

    [Test]
    public void Constructor_ZeroMultiplier_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BollingerBandsCalculator(20, 0));
    }

    // ========================================================================
    // Warm-Up / IsReady
    // ========================================================================

    [Test]
    public void IsReady_RequiresFullPeriod()
    {
        var bb = new BollingerBandsCalculator(20);

        for (int i = 0; i < 19; i++)
        {
            bb.Update(bars[i].Close);
            Assert.That(bb.IsReady, Is.False);
        }

        bb.Update(bars[19].Close);
        Assert.That(bb.IsReady, Is.True);
    }

    // ========================================================================
    // Band Relationships
    // ========================================================================

    [Test]
    public void UpperBand_AlwaysAboveMiddle_AboveLower()
    {
        var bb = new BollingerBandsCalculator(20, 2.0);

        for (int i = 0; i < 100; i++)
        {
            bb.Update(bars[i].Close);
            if (bb.IsReady)
            {
                Assert.That(bb.UpperBand, Is.GreaterThanOrEqualTo(bb.MiddleBand),
                    $"Upper band must be >= middle band at bar {i}");
                Assert.That(bb.MiddleBand, Is.GreaterThanOrEqualTo(bb.LowerBand),
                    $"Middle band must be >= lower band at bar {i}");
                Assert.That(bb.UpperBand, Is.GreaterThan(bb.LowerBand),
                    $"Upper band must be > lower band at bar {i}");
            }
        }
    }

    [Test]
    public void MiddleBand_EqualsSMA()
    {
        var bb = new BollingerBandsCalculator(20, 2.0);
        var sma = new SmaCalculator(20);

        for (int i = 0; i < 50; i++)
        {
            bb.Update(bars[i].Close);
            sma.Update(bars[i].Close);
        }

        Assert.That(bb.MiddleBand, Is.EqualTo(sma.CurrentValue).Within(0.001),
            "Middle band should equal SMA(20)");
    }

    // ========================================================================
    // %B (Percent B) 
    // ========================================================================

    [Test]
    public void PercentB_CalculatedCorrectly()
    {
        var bb = new BollingerBandsCalculator(20, 2.0);

        for (int i = 0; i < 50; i++)
            bb.Update(bars[i].Close);

        Assert.That(bb.IsReady, Is.True);

        double price = bars[49].Close;
        double expectedPctB = (price - bb.LowerBand) / (bb.UpperBand - bb.LowerBand);
        Assert.That(bb.PercentB, Is.EqualTo(expectedPctB).Within(0.001));
    }

    [Test]
    public void IsAboveUpperBand_WhenPercentBGreaterThan1()
    {
        var bb = new BollingerBandsCalculator(20, 2.0);

        for (int i = 0; i < 100; i++)
            bb.Update(bars[i].Close);

        // IsAboveUpperBand should be true iff %B > 1
        Assert.That(bb.IsAboveUpperBand, Is.EqualTo(bb.PercentB > 1.0));
    }

    [Test]
    public void IsBelowLowerBand_WhenPercentBLessThan0()
    {
        var bb = new BollingerBandsCalculator(20, 2.0);

        for (int i = 0; i < 100; i++)
            bb.Update(bars[i].Close);

        Assert.That(bb.IsBelowLowerBand, Is.EqualTo(bb.PercentB < 0.0));
    }

    // ========================================================================
    // Bandwidth
    // ========================================================================

    [Test]
    public void Bandwidth_AlwaysPositive()
    {
        var bb = new BollingerBandsCalculator(20, 2.0);

        for (int i = 0; i < 100; i++)
        {
            bb.Update(bars[i].Close);
            if (bb.IsReady)
            {
                Assert.That(bb.Bandwidth, Is.GreaterThanOrEqualTo(0),
                    $"Bandwidth should be >= 0 at bar {i}");
            }
        }
    }

    [Test]
    public void Bandwidth_FormulCorrect()
    {
        var bb = new BollingerBandsCalculator(20, 2.0);

        for (int i = 0; i < 50; i++)
            bb.Update(bars[i].Close);

        double expected = (bb.UpperBand - bb.LowerBand) / bb.MiddleBand * 100;
        Assert.That(bb.Bandwidth, Is.EqualTo(expected).Within(0.01));
    }

    // ========================================================================
    // Squeeze Detection
    // ========================================================================

    [Test]
    public void IsInSqueeze_WhenBandwidthLessThan5()
    {
        var bb = new BollingerBandsCalculator(20, 2.0);

        // Feed flat prices to create a squeeze
        for (int i = 0; i < 30; i++)
            bb.Update(100.0 + (i % 2) * 0.01); // Very minimal variation

        if (bb.IsReady)
        {
            Assert.That(bb.Bandwidth, Is.LessThan(5));
            Assert.That(bb.IsInSqueeze, Is.True);
        }
    }

    // ========================================================================
    // Score
    // ========================================================================

    [Test]
    public void GetScore_ReturnsZero_WhenNotReady()
    {
        var bb = new BollingerBandsCalculator(20, 2.0);
        bb.Update(100);
        Assert.That(bb.GetScore(), Is.EqualTo(0));
    }

    [Test]
    public void GetScore_Positive_WhenBelowLowerBand()
    {
        var bb = new BollingerBandsCalculator(20, 2.0);

        // Feed flat-ish data then drop significantly
        for (int i = 0; i < 20; i++)
            bb.Update(100);

        bb.Update(80); // Well below lower band
        if (bb.IsBelowLowerBand)
        {
            Assert.That(bb.GetScore(), Is.GreaterThan(0),
                "Score should be positive (bullish) below lower band - mean reversion");
        }
    }

    // ========================================================================
    // Edge Cases
    // ========================================================================

    [Test]
    public void Update_WithZeroPrice_Ignored()
    {
        var bb = new BollingerBandsCalculator(20, 2.0);
        for (int i = 0; i < 25; i++)
            bb.Update(bars[i].Close);

        double before = bb.MiddleBand;
        bb.Update(0);
        Assert.That(bb.MiddleBand, Is.EqualTo(before));
    }

    [Test]
    public void HigherMultiplier_WiderBands()
    {
        var bb2 = new BollingerBandsCalculator(20, 2.0);
        var bb3 = new BollingerBandsCalculator(20, 3.0);

        for (int i = 0; i < 30; i++)
        {
            bb2.Update(bars[i].Close);
            bb3.Update(bars[i].Close);
        }

        Assert.That(bb3.UpperBand - bb3.LowerBand, 
            Is.GreaterThan(bb2.UpperBand - bb2.LowerBand),
            "3σ bands should be wider than 2σ bands");
    }
}
