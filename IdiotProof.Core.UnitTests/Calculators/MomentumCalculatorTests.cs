// ============================================================================
// Momentum Calculator Tests - Validates Momentum indicator with real data
// ============================================================================

using IdiotProof.Core.UnitTests.Helpers;
using IdiotProof.Helpers;

namespace IdiotProof.Core.UnitTests.Calculators;

[TestFixture]
public class MomentumCalculatorTests
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
    public void Constructor_DefaultPeriod_Is10()
    {
        var mom = new MomentumCalculator();
        Assert.That(mom.Period, Is.EqualTo(10));
    }

    [Test]
    public void Constructor_WithZeroPeriod_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MomentumCalculator(0));
    }

    // ========================================================================
    // Warm-Up / IsReady
    // ========================================================================

    [Test]
    public void IsReady_BecomesTrueAfterPeriodPrices()
    {
        var mom = new MomentumCalculator(10);

        for (int i = 0; i < 9; i++)
        {
            mom.Update(bars[i].Close);
            Assert.That(mom.IsReady, Is.False);
        }

        mom.Update(bars[9].Close);
        Assert.That(mom.IsReady, Is.True, "Momentum(10) should be ready after 10 prices");
    }

    // ========================================================================
    // Formula: Momentum = Current Price - Price N periods ago
    // ========================================================================

    [Test]
    public void Momentum_EqualsCurrentMinusPriceNAgo()
    {
        var mom = new MomentumCalculator(10);

        for (int i = 0; i <= 10; i++)
            mom.Update(bars[i].Close);

        double expected = bars[10].Close - bars[0].Close;
        Assert.That(mom.CurrentValue, Is.EqualTo(expected).Within(0.0001),
            "Momentum = Current Price - Price 10 bars ago");
    }

    [Test]
    public void Momentum_SlidesForward()
    {
        var mom = new MomentumCalculator(5);

        for (int i = 0; i <= 10; i++)
            mom.Update(bars[i].Close);

        // Momentum should be bars[10] - bars[5]
        double expected = bars[10].Close - bars[5].Close;
        Assert.That(mom.CurrentValue, Is.EqualTo(expected).Within(0.0001));
    }

    // ========================================================================
    // Positive/Negative Momentum
    // ========================================================================

    [Test]
    public void Momentum_PositiveForRisingPrices()
    {
        var mom = new MomentumCalculator(5);

        for (int i = 0; i < 15; i++)
            mom.Update(100 + i);

        Assert.That(mom.CurrentValue, Is.GreaterThan(0),
            "Momentum should be positive for rising prices");
        Assert.That(mom.CurrentValue, Is.EqualTo(5),
            "Momentum should equal 5 for linear rise with period 5");
    }

    [Test]
    public void Momentum_NegativeForFallingPrices()
    {
        var mom = new MomentumCalculator(5);

        for (int i = 0; i < 15; i++)
            mom.Update(200 - i);

        Assert.That(mom.CurrentValue, Is.LessThan(0),
            "Momentum should be negative for falling prices");
    }

    [Test]
    public void Momentum_ZeroForFlatPrices()
    {
        var mom = new MomentumCalculator(5);

        for (int i = 0; i < 15; i++)
            mom.Update(150);

        Assert.That(mom.CurrentValue, Is.EqualTo(0),
            "Momentum should be zero for flat prices");
    }

    // ========================================================================
    // Edge Cases
    // ========================================================================

    [Test]
    public void Update_WithZeroPrice_Ignored()
    {
        var mom = new MomentumCalculator(5);
        for (int i = 0; i < 10; i++)
            mom.Update(bars[i].Close);

        double before = mom.CurrentValue;
        mom.Update(0);
        Assert.That(mom.CurrentValue, Is.EqualTo(before));
    }

    [Test]
    public void Reset_ClearsAllState()
    {
        var mom = new MomentumCalculator(10);
        for (int i = 0; i < 20; i++)
            mom.Update(bars[i].Close);

        mom.Reset();

        Assert.Multiple(() =>
        {
            Assert.That(mom.IsReady, Is.False);
            Assert.That(mom.CurrentValue, Is.EqualTo(0));
        });
    }
}
