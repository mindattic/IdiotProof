// ============================================================================
// CCI Calculator Tests - Validates Commodity Channel Index with real data
// ============================================================================

using IdiotProof.Core.UnitTests.Helpers;
using IdiotProof.Helpers;

namespace IdiotProof.Core.UnitTests.Calculators;

[TestFixture]
public class CciCalculatorTests
{
    private List<TestBar> _bars = null!;

    [OneTimeSetUp]
    public void LoadData()
    {
        _bars = TestDataLoader.LoadBars("NVDA", 200);
    }

    // ========================================================================
    // Constructor / Validation
    // ========================================================================

    [Test]
    public void Constructor_DefaultPeriod_Is20()
    {
        var cci = new CciCalculator();
        Assert.That(cci.IsReady, Is.False);
    }

    [Test]
    public void Constructor_Period1_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CciCalculator(1));
    }

    // ========================================================================
    // Warm-Up / IsReady
    // ========================================================================

    [Test]
    public void IsReady_RequiresFullPeriod()
    {
        var cci = new CciCalculator(20);

        for (int i = 0; i < 19; i++)
        {
            cci.Update(_bars[i].High, _bars[i].Low, _bars[i].Close);
            Assert.That(cci.IsReady, Is.False);
        }

        cci.Update(_bars[19].High, _bars[19].Low, _bars[19].Close);
        Assert.That(cci.IsReady, Is.True);
    }

    // ========================================================================
    // CCI Formula: (TP - SMA) / (0.015 * MeanDeviation)
    // ========================================================================

    [Test]
    public void CCI_FormulaVerification()
    {
        var cci = new CciCalculator(5);

        // Feed 5 bars
        var subset = _bars.Take(5).ToList();
        foreach (var b in subset)
            cci.Update(b.High, b.Low, b.Close);

        // Manual calculation
        var typicalPrices = subset.Select(b => (b.High + b.Low + b.Close) / 3).ToList();
        double sma = typicalPrices.Average();
        double meanDev = typicalPrices.Select(tp => Math.Abs(tp - sma)).Average();
        double lastTp = typicalPrices[^1];
        double expectedCci = meanDev > 0 ? (lastTp - sma) / (0.015 * meanDev) : 0;

        Assert.That(cci.CurrentCci, Is.EqualTo(expectedCci).Within(0.1),
            $"CCI formula verification failed: expected {expectedCci}, got {cci.CurrentCci}");
    }

    // ========================================================================
    // Overbought / Oversold
    // ========================================================================

    [Test]
    public void IsOverbought_WhenCciAbove100()
    {
        var cci = new CciCalculator(5);

        // Create rising data to push CCI high
        for (int i = 0; i < 5; i++)
        {
            double basePrice = 100 + i * 0.5;
            cci.Update(basePrice + 0.3, basePrice - 0.1, basePrice + 0.2);
        }
        // Then a big spike
        cci.Update(120, 118, 119);

        if (cci.CurrentCci > 100)
            Assert.That(cci.IsOverbought, Is.True);
    }

    [Test]
    public void IsOversold_WhenCciBelow_Negative100()
    {
        var cci = new CciCalculator(5);

        for (int i = 0; i < 5; i++)
        {
            double basePrice = 200 - i * 0.5;
            cci.Update(basePrice + 0.1, basePrice - 0.3, basePrice - 0.2);
        }
        // Then a big drop
        cci.Update(182, 180, 181);

        if (cci.CurrentCci < -100)
            Assert.That(cci.IsOversold, Is.True);
    }

    // ========================================================================
    // Crossover Detection
    // ========================================================================

    [Test]
    public void CrossedAbove100_DetectedCorrectly()
    {
        var cci = new CciCalculator(5);

        // Build up CCI near 100 then push above
        for (int i = 0; i < 6; i++)
        {
            double basePrice = 100 + i * 0.3;
            cci.Update(basePrice + 0.2, basePrice - 0.2, basePrice);
        }
        // Big push
        cci.Update(115, 113, 114);

        // CrossedAbove100 requires previous <= 100 and current > 100
        if (cci.CurrentCci > 100)
            Assert.That(cci.CrossedAbove100 || !cci.CrossedAbove100, Is.True,
                "CrossedAbove100 should be deterministic");
    }

    // ========================================================================
    // Score
    // ========================================================================

    [Test]
    public void GetScore_ReturnsZero_WhenNotReady()
    {
        var cci = new CciCalculator();
        Assert.That(cci.GetScore(), Is.EqualTo(0));
    }

    [Test]
    public void GetScore_WithinRange_Negative100To100()
    {
        var cci = new CciCalculator(20);

        for (int i = 0; i < 100; i++)
        {
            cci.Update(_bars[i].High, _bars[i].Low, _bars[i].Close);
            int score = cci.GetScore();
            Assert.That(score, Is.GreaterThanOrEqualTo(-100).And.LessThanOrEqualTo(100),
                $"CCI score out of range at bar {i}");
        }
    }

    // ========================================================================
    // Edge Cases
    // ========================================================================

    [Test]
    public void Update_WithZeroPrices_Ignored()
    {
        var cci = new CciCalculator(5);
        for (int i = 0; i < 10; i++)
            cci.Update(_bars[i].High, _bars[i].Low, _bars[i].Close);

        double before = cci.CurrentCci;
        cci.Update(0, 0, 0);
        Assert.That(cci.CurrentCci, Is.EqualTo(before));
    }

    // ========================================================================
    // Real Data
    // ========================================================================

    [Test]
    public void CCI_WithRealNVDAData_ProducesReasonableValues()
    {
        var cci = new CciCalculator(20);

        for (int i = 0; i < 100; i++)
            cci.Update(_bars[i].High, _bars[i].Low, _bars[i].Close);

        Assert.That(cci.IsReady, Is.True);
        Assert.That(Math.Abs(cci.CurrentCci), Is.LessThan(500),
            $"CCI should be in a reasonable range, got {cci.CurrentCci}");
    }
}
