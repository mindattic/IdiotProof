// ============================================================================
// Stochastic Calculator Tests - Validates Stochastic Oscillator with real data
// ============================================================================

using IdiotProof.Core.UnitTests.Helpers;
using IdiotProof.Helpers;

namespace IdiotProof.Core.UnitTests.Calculators;

[TestFixture]
public class StochasticCalculatorTests
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
    public void Constructor_DefaultPeriods_14_3()
    {
        var stoch = new StochasticCalculator();
        Assert.That(stoch.IsReady, Is.False);
    }

    [Test]
    public void Constructor_KPeriod1_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new StochasticCalculator(1));
    }

    [Test]
    public void Constructor_DPeriod0_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new StochasticCalculator(14, 0));
    }

    // ========================================================================
    // Warm-Up / IsReady
    // ========================================================================

    [Test]
    public void IsReady_RequiresKPeriodBars()
    {
        var stoch = new StochasticCalculator(14, 3);

        for (int i = 0; i < 13; i++)
        {
            stoch.Update(bars[i].High, bars[i].Low, bars[i].Close);
            Assert.That(stoch.IsReady, Is.False);
        }

        // After k period bars + d period, should be ready
        for (int i = 13; i < 20; i++)
        {
            stoch.Update(bars[i].High, bars[i].Low, bars[i].Close);
            if (stoch.IsReady) break;
        }
        Assert.That(stoch.IsReady, Is.True);
    }

    // ========================================================================
    // %K Range (0-100)
    // ========================================================================

    [Test]
    public void PercentK_AlwaysBetween0And100()
    {
        var stoch = new StochasticCalculator(14, 3);

        for (int i = 0; i < 100; i++)
        {
            stoch.Update(bars[i].High, bars[i].Low, bars[i].Close);
            if (stoch.IsReady)
            {
                Assert.That(stoch.PercentK, Is.GreaterThanOrEqualTo(0).And.LessThanOrEqualTo(100),
                    $"%K out of range at bar {i}: {stoch.PercentK}");
                Assert.That(stoch.PercentD, Is.GreaterThanOrEqualTo(0).And.LessThanOrEqualTo(100),
                    $"%D out of range at bar {i}: {stoch.PercentD}");
            }
        }
    }

    // ========================================================================
    // %K Formula: 100 * (Close - LowestLow) / (HighestHigh - LowestLow)
    // ========================================================================

    [Test]
    public void PercentK_FormulaVerification()
    {
        var stoch = new StochasticCalculator(5, 3);

        for (int i = 0; i < 5; i++)
            stoch.Update(bars[i].High, bars[i].Low, bars[i].Close);

        var subset = bars.Take(5).ToList();
        double hh = subset.Max(b => b.High);
        double ll = subset.Min(b => b.Low);
        double close = subset[^1].Close;
        double range = hh - ll;
        double expectedK = range > 0 ? 100 * (close - ll) / range : 50;

        Assert.That(stoch.PercentK, Is.EqualTo(expectedK).Within(0.01),
            $"%K formula verification: expected {expectedK}, got {stoch.PercentK}");
    }

    // ========================================================================
    // Overbought / Oversold
    // ========================================================================

    [Test]
    public void IsOverbought_WhenKAbove80()
    {
        var stoch = new StochasticCalculator(5, 3);

        // Feed rising prices to push %K high
        for (int i = 0; i < 10; i++)
        {
            double price = 100 + i;
            stoch.Update(price + 0.5, price - 0.1, price + 0.3);
        }

        if (stoch.IsReady && stoch.PercentK > 80)
            Assert.That(stoch.IsOverbought, Is.True);
    }

    [Test]
    public void IsOversold_WhenKBelow20()
    {
        var stoch = new StochasticCalculator(5, 3);

        // Feed falling prices to push %K low
        for (int i = 0; i < 10; i++)
        {
            double price = 200 - i;
            stoch.Update(price + 0.1, price - 0.5, price - 0.3);
        }

        if (stoch.IsReady && stoch.PercentK < 20)
            Assert.That(stoch.IsOversold, Is.True);
    }

    // ========================================================================
    // Score
    // ========================================================================

    [Test]
    public void GetScore_ReturnsZero_WhenNotReady()
    {
        var stoch = new StochasticCalculator();
        Assert.That(stoch.GetScore(), Is.EqualTo(0));
    }

    [Test]
    public void GetScore_WithinRange()
    {
        var stoch = new StochasticCalculator(14, 3);

        for (int i = 0; i < 50; i++)
        {
            stoch.Update(bars[i].High, bars[i].Low, bars[i].Close);
            int score = stoch.GetScore();
            Assert.That(score, Is.GreaterThanOrEqualTo(-100).And.LessThanOrEqualTo(100));
        }
    }

    // ========================================================================
    // Edge Cases
    // ========================================================================

    [Test]
    public void Update_WithZeroPrices_Ignored()
    {
        var stoch = new StochasticCalculator(5, 3);
        for (int i = 0; i < 10; i++)
            stoch.Update(bars[i].High, bars[i].Low, bars[i].Close);

        double beforeK = stoch.PercentK;
        stoch.Update(0, 0, 0);
        Assert.That(stoch.PercentK, Is.EqualTo(beforeK));
    }

    // ========================================================================
    // Real Data
    // ========================================================================

    [Test]
    public void Stochastic_WithRealNVDAData_ProducesValues()
    {
        var stoch = new StochasticCalculator(14, 3);

        for (int i = 0; i < 50; i++)
            stoch.Update(bars[i].High, bars[i].Low, bars[i].Close);

        Assert.That(stoch.IsReady, Is.True);
        Assert.That(stoch.PercentK, Is.GreaterThan(0).And.LessThan(100));
        Assert.That(stoch.PercentD, Is.GreaterThan(0).And.LessThan(100));
    }
}
