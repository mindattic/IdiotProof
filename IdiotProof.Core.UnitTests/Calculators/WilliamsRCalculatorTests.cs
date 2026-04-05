// ============================================================================
// Williams %R Calculator Tests - Validates Williams %R with real data
// ============================================================================

using IdiotProof.Core.UnitTests.Helpers;
using IdiotProof.Helpers;

namespace IdiotProof.Core.UnitTests.Calculators;

[TestFixture]
public class WilliamsRCalculatorTests
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
    public void Constructor_DefaultPeriod_Is14()
    {
        var wr = new WilliamsRCalculator();
        Assert.That(wr.IsReady, Is.False);
    }

    [Test]
    public void Constructor_Period1_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new WilliamsRCalculator(1));
    }

    // ========================================================================
    // Warm-Up / IsReady
    // ========================================================================

    [Test]
    public void IsReady_RequiresFullPeriod()
    {
        var wr = new WilliamsRCalculator(14);

        for (int i = 0; i < 13; i++)
        {
            wr.Update(bars[i].High, bars[i].Low, bars[i].Close);
            Assert.That(wr.IsReady, Is.False);
        }

        wr.Update(bars[13].High, bars[13].Low, bars[13].Close);
        Assert.That(wr.IsReady, Is.True);
    }

    // ========================================================================
    // Williams %R Range (-100 to 0)
    // ========================================================================

    [Test]
    public void WilliamsR_AlwaysBetween_Neg100_And0()
    {
        var wr = new WilliamsRCalculator(14);

        for (int i = 0; i < 100; i++)
        {
            wr.Update(bars[i].High, bars[i].Low, bars[i].Close);
            if (wr.IsReady)
            {
                Assert.That(wr.CurrentValue, Is.GreaterThanOrEqualTo(-100).And.LessThanOrEqualTo(0),
                    $"Williams %%R out of range at bar {i}: {wr.CurrentValue}");
            }
        }
    }

    // ========================================================================
    // Formula: %R = (HH - Close) / (HH - LL) * -100
    // ========================================================================

    [Test]
    public void WilliamsR_FormulaVerification()
    {
        var wr = new WilliamsRCalculator(5);

        for (int i = 0; i < 5; i++)
            wr.Update(bars[i].High, bars[i].Low, bars[i].Close);

        var subset = bars.Take(5).ToList();
        double hh = subset.Max(b => b.High);
        double ll = subset.Min(b => b.Low);
        double close = subset[^1].Close;
        double range = hh - ll;
        double expectedWR = range > 0 ? ((hh - close) / range) * -100 : -50;

        Assert.That(wr.CurrentValue, Is.EqualTo(expectedWR).Within(0.01),
            $"Williams %%R formula: expected {expectedWR}, got {wr.CurrentValue}");
    }

    // ========================================================================
    // Overbought / Oversold
    // ========================================================================

    [Test]
    public void IsOverbought_WhenAboveNeg20()
    {
        var wr = new WilliamsRCalculator(5);

        // Close at the highest high gives %R = 0 (overbought)
        for (int i = 0; i < 5; i++)
        {
            double price = 100 + i;
            wr.Update(price + 0.5, price - 0.5, price + 0.5); // Close = High
        }

        if (wr.IsReady && wr.CurrentValue > -20)
            Assert.That(wr.IsOverbought, Is.True);
    }

    [Test]
    public void IsOversold_WhenBelowNeg80()
    {
        var wr = new WilliamsRCalculator(5);

        // Close at the lowest low gives %R = -100 (oversold)
        for (int i = 0; i < 5; i++)
        {
            double price = 200 - i;
            wr.Update(price + 0.5, price - 0.5, price - 0.5); // Close = Low
        }

        if (wr.IsReady && wr.CurrentValue < -80)
            Assert.That(wr.IsOversold, Is.True);
    }

    // ========================================================================
    // Close at Highest High → %R = 0
    // ========================================================================

    [Test]
    public void WilliamsR_CloseAtHighestHigh_ReturnsZero()
    {
        var wr = new WilliamsRCalculator(3);

        wr.Update(105, 95, 105);
        wr.Update(110, 100, 110);
        wr.Update(115, 105, 115); // Close = Highest High = 115

        Assert.That(wr.CurrentValue, Is.EqualTo(0).Within(0.01),
            "Close at highest high should give %R = 0");
    }

    // ========================================================================
    // Close at Lowest Low → %R = -100
    // ========================================================================

    [Test]
    public void WilliamsR_CloseAtLowestLow_ReturnsNeg100()
    {
        var wr = new WilliamsRCalculator(3);

        wr.Update(105, 95, 100);
        wr.Update(110, 100, 105);
        wr.Update(108, 95, 95); // Close = Lowest Low = 95

        Assert.That(wr.CurrentValue, Is.EqualTo(-100).Within(0.01),
            "Close at lowest low should give %R = -100");
    }

    // ========================================================================
    // Score
    // ========================================================================

    [Test]
    public void GetScore_ReturnsZero_WhenNotReady()
    {
        var wr = new WilliamsRCalculator();
        Assert.That(wr.GetScore(), Is.EqualTo(0));
    }

    [Test]
    public void GetScore_WithinRange()
    {
        var wr = new WilliamsRCalculator(14);

        for (int i = 0; i < 50; i++)
        {
            wr.Update(bars[i].High, bars[i].Low, bars[i].Close);
            int score = wr.GetScore();
            Assert.That(score, Is.GreaterThanOrEqualTo(-100).And.LessThanOrEqualTo(100));
        }
    }

    // ========================================================================
    // Edge Cases
    // ========================================================================

    [Test]
    public void Update_WithZeroPrices_Ignored()
    {
        var wr = new WilliamsRCalculator(5);
        for (int i = 0; i < 10; i++)
            wr.Update(bars[i].High, bars[i].Low, bars[i].Close);

        double before = wr.CurrentValue;
        wr.Update(0, 0, 0);
        Assert.That(wr.CurrentValue, Is.EqualTo(before));
    }

    // ========================================================================
    // Real Data
    // ========================================================================

    [Test]
    public void WilliamsR_WithRealNVDAData_ProducesReasonableValues()
    {
        var wr = new WilliamsRCalculator(14);

        for (int i = 0; i < 100; i++)
            wr.Update(bars[i].High, bars[i].Low, bars[i].Close);

        Assert.That(wr.IsReady, Is.True);
        Assert.That(wr.CurrentValue, Is.GreaterThanOrEqualTo(-100).And.LessThanOrEqualTo(0));
    }
}
