// ============================================================================
// ROC Calculator Tests - Validates Rate of Change with real data
// ============================================================================

using IdiotProof.Core.UnitTests.Helpers;
using IdiotProof.Helpers;

namespace IdiotProof.Core.UnitTests.Calculators;

[TestFixture]
public class RocCalculatorTests
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
    public void Constructor_DefaultPeriod_Is10()
    {
        var roc = new RocCalculator();
        Assert.That(roc.Period, Is.EqualTo(10));
    }

    [Test]
    public void Constructor_WithZeroPeriod_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RocCalculator(0));
    }

    // ========================================================================
    // Warm-Up / IsReady
    // ========================================================================

    [Test]
    public void IsReady_BecomesTrueAfterPeriodPrices()
    {
        var roc = new RocCalculator(10);

        for (int i = 0; i < 9; i++)
        {
            roc.Update(_bars[i].Close);
            Assert.That(roc.IsReady, Is.False);
        }

        roc.Update(_bars[9].Close);
        Assert.That(roc.IsReady, Is.True);
    }

    // ========================================================================
    // Formula: ROC = ((Price - PriceNAgo) / PriceNAgo) × 100
    // ========================================================================

    [Test]
    public void ROC_EqualsPercentageChange()
    {
        var roc = new RocCalculator(10);

        for (int i = 0; i <= 10; i++)
            roc.Update(_bars[i].Close);

        double oldPrice = _bars[0].Close;
        double newPrice = _bars[10].Close;
        double expected = ((newPrice - oldPrice) / oldPrice) * 100;

        Assert.That(roc.CurrentValue, Is.EqualTo(expected).Within(0.001),
            "ROC should be the percentage change over 10 periods");
    }

    // ========================================================================
    // Positive / Negative / Zero
    // ========================================================================

    [Test]
    public void ROC_Positive_ForRisingPrices()
    {
        var roc = new RocCalculator(5);

        // 10% increase over 5 periods
        double[] prices = { 100, 101, 102, 103, 104, 105, 106, 107, 108, 109, 110 };
        foreach (var p in prices)
            roc.Update(p);

        Assert.That(roc.CurrentValue, Is.GreaterThan(0));
    }

    [Test]
    public void ROC_Negative_ForFallingPrices()
    {
        var roc = new RocCalculator(5);

        double[] prices = { 200, 198, 196, 194, 192, 190, 188, 186, 184, 182, 180 };
        foreach (var p in prices)
            roc.Update(p);

        Assert.That(roc.CurrentValue, Is.LessThan(0));
    }

    [Test]
    public void ROC_Zero_ForFlatPrices()
    {
        var roc = new RocCalculator(5);

        for (int i = 0; i < 15; i++)
            roc.Update(100);

        Assert.That(roc.CurrentValue, Is.EqualTo(0));
    }

    [Test]
    public void ROC_CalculatesExactPercentage()
    {
        var roc = new RocCalculator(1);

        roc.Update(100);
        roc.Update(110); // 10% increase

        Assert.That(roc.CurrentValue, Is.EqualTo(10.0).Within(0.001),
            "ROC should be 10% for 100→110");
    }

    // ========================================================================
    // Edge Cases
    // ========================================================================

    [Test]
    public void Update_WithZeroPrice_Ignored()
    {
        var roc = new RocCalculator(5);
        for (int i = 0; i < 10; i++)
            roc.Update(_bars[i].Close);

        double before = roc.CurrentValue;
        roc.Update(0);
        Assert.That(roc.CurrentValue, Is.EqualTo(before));
    }

    [Test]
    public void Reset_ClearsAllState()
    {
        var roc = new RocCalculator(10);
        for (int i = 0; i < 20; i++)
            roc.Update(_bars[i].Close);

        roc.Reset();

        Assert.Multiple(() =>
        {
            Assert.That(roc.IsReady, Is.False);
            Assert.That(roc.CurrentValue, Is.EqualTo(0));
        });
    }
}
