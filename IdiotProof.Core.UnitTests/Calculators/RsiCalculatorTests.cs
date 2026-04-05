// ============================================================================
// RSI Calculator Tests - Validates Relative Strength Index with real data
// ============================================================================

using IdiotProof.Core.UnitTests.Helpers;
using IdiotProof.Helpers;

namespace IdiotProof.Core.UnitTests.Calculators;

[TestFixture]
public class RsiCalculatorTests
{
    private List<TestBar> bars = null!;

    [OneTimeSetUp]
    public void LoadData()
    {
        bars = TestDataLoader.LoadBars("NVDA", 500);
    }

    // ========================================================================
    // Constructor / Validation
    // ========================================================================

    [Test]
    public void Constructor_DefaultPeriod_Is14()
    {
        var rsi = new RsiCalculator();
        Assert.That(rsi.Period, Is.EqualTo(14));
    }

    [Test]
    public void Constructor_CustomPeriod_IsSet()
    {
        var rsi = new RsiCalculator(7);
        Assert.That(rsi.Period, Is.EqualTo(7));
    }

    [Test]
    public void Constructor_WithZeroPeriod_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RsiCalculator(0));
    }

    // ========================================================================
    // Warm-Up / IsReady
    // ========================================================================

    [Test]
    public void IsReady_BecomesTrueAfter_PeriodPlus1_DataPoints()
    {
        var rsi = new RsiCalculator(14);

        for (int i = 0; i < 14; i++)
        {
            rsi.Update(bars[i].Close);
            Assert.That(rsi.IsReady, Is.False, $"Should not be ready after {i + 1} data points");
        }

        rsi.Update(bars[14].Close);
        Assert.That(rsi.IsReady, Is.True, "Should be ready after 15 data points for RSI(14)");
    }

    // ========================================================================
    // RSI Range (0-100)
    // ========================================================================

    [Test]
    public void RSI_AlwaysBetween0And100_WithRealData()
    {
        var rsi = new RsiCalculator(14);

        for (int i = 0; i < 300; i++)
        {
            rsi.Update(bars[i].Close);
            if (rsi.IsReady)
            {
                Assert.That(rsi.CurrentValue, Is.GreaterThanOrEqualTo(0).And.LessThanOrEqualTo(100),
                    $"RSI out of range at bar {i}: {rsi.CurrentValue}");
            }
        }
    }

    // ========================================================================
    // RSI Behavior Tests with Known Patterns
    // ========================================================================

    [Test]
    public void RSI_AllGains_Returns100()
    {
        var rsi = new RsiCalculator(5);

        // Feed steadily rising prices
        for (int i = 0; i < 20; i++)
            rsi.Update(100 + i);

        Assert.That(rsi.CurrentValue, Is.EqualTo(100),
            "RSI should be 100 when there are no losses");
    }

    [Test]
    public void RSI_AllLosses_Returns0()
    {
        var rsi = new RsiCalculator(5);

        // Feed steadily falling prices
        for (int i = 0; i < 20; i++)
            rsi.Update(200 - i);

        Assert.That(rsi.CurrentValue, Is.EqualTo(0),
            "RSI should be 0 when there are no gains");
    }

    [Test]
    public void RSI_EqualGainsAndLosses_Returns50()
    {
        var rsi = new RsiCalculator(4);

        // Alternating up and down by the same amount
        double[] prices = { 100, 102, 100, 102, 100, 102, 100, 102, 100 };
        foreach (var p in prices)
            rsi.Update(p);

        // With equal gains and losses, RS = 1, RSI = 100 - 100/2 = 50
        Assert.That(rsi.CurrentValue, Is.EqualTo(50).Within(5),
            "RSI should be near 50 with equal oscillation");
    }

    // ========================================================================
    // RSI with Real Market Data
    // ========================================================================

    [Test]
    public void RSI_WithRealNVDAData_ProducesReasonableValues()
    {
        var rsi = new RsiCalculator(14);

        for (int i = 0; i < 200; i++)
            rsi.Update(bars[i].Close);

        Assert.That(rsi.IsReady, Is.True);
        // In normal market conditions, RSI is typically 20-80
        Assert.That(rsi.CurrentValue, Is.GreaterThan(5).And.LessThan(95),
            $"RSI with real NVDA data should be in a reasonable range, got {rsi.CurrentValue}");
    }

    // ========================================================================
    // Wilder's Smoothing Verification
    // ========================================================================

    [Test]
    public void RSI_UsesWilderSmoothing_AfterInitialPeriod()
    {
        var rsi = new RsiCalculator(14);

        // Feed enough data to get past initial period
        for (int i = 0; i < 100; i++)
            rsi.Update(bars[i].Close);

        double prev = rsi.CurrentValue;

        // A small price change should cause small RSI movement (smoothing effect)
        rsi.Update(bars[100].Close);
        double diff = Math.Abs(rsi.CurrentValue - prev);

        Assert.That(diff, Is.LessThan(20),
            "Wilder's smoothing should prevent large jumps in RSI from small price changes");
    }

    // ========================================================================
    // Edge Cases
    // ========================================================================

    [Test]
    public void Update_WithZeroPrice_Ignored()
    {
        var rsi = new RsiCalculator(14);
        for (int i = 0; i < 20; i++)
            rsi.Update(bars[i].Close);

        double before = rsi.CurrentValue;
        rsi.Update(0);
        Assert.That(rsi.CurrentValue, Is.EqualTo(before));
    }

    [Test]
    public void Reset_ClearsAllState()
    {
        var rsi = new RsiCalculator(14);
        for (int i = 0; i < 30; i++)
            rsi.Update(bars[i].Close);

        rsi.Reset();

        Assert.Multiple(() =>
        {
            Assert.That(rsi.IsReady, Is.False);
            Assert.That(rsi.CurrentValue, Is.EqualTo(0));
        });
    }

    [Test]
    public void FirstDataPoint_ReturnsNeutral50()
    {
        var rsi = new RsiCalculator(14);
        rsi.Update(100);
        Assert.That(rsi.CurrentValue, Is.EqualTo(50),
            "First data point should return neutral RSI of 50");
    }
}
