// ============================================================================
// OBV Calculator Tests - Validates On-Balance Volume with real data
// ============================================================================

using IdiotProof.Core.UnitTests.Helpers;
using IdiotProof.Helpers;

namespace IdiotProof.Core.UnitTests.Calculators;

[TestFixture]
public class ObvCalculatorTests
{
    private List<TestBar> bars = null!;

    [OneTimeSetUp]
    public void LoadData()
    {
        bars = TestDataLoader.LoadBars("NVDA", 200);
    }

    // ========================================================================
    // Basic OBV Logic
    // ========================================================================

    [Test]
    public void OBV_IncreasesOnUp_ClosePrices()
    {
        var obv = new ObvCalculator(5);

        obv.Update(100, 1000); // First bar - sets previous close
        obv.Update(105, 2000); // Close > PrevClose → OBV += 2000

        Assert.That(obv.CurrentObv, Is.EqualTo(2000));
    }

    [Test]
    public void OBV_DecreasesOnDown_ClosePrices()
    {
        var obv = new ObvCalculator(5);

        obv.Update(100, 1000); // First bar
        obv.Update(95, 1500);  // Close < PrevClose → OBV -= 1500

        Assert.That(obv.CurrentObv, Is.EqualTo(-1500));
    }

    [Test]
    public void OBV_UnchangedOnFlat_ClosePrices()
    {
        var obv = new ObvCalculator(5);

        obv.Update(100, 1000);
        obv.Update(100, 2000); // Close == PrevClose → OBV unchanged

        Assert.That(obv.CurrentObv, Is.EqualTo(0));
    }

    // ========================================================================
    // Cumulative Behavior
    // ========================================================================

    [Test]
    public void OBV_IsCumulative()
    {
        var obv = new ObvCalculator(5);

        obv.Update(100, 1000); // Init
        obv.Update(105, 2000); // +2000 → OBV = 2000
        obv.Update(110, 3000); // +3000 → OBV = 5000
        obv.Update(108, 1000); // -1000 → OBV = 4000
        obv.Update(112, 4000); // +4000 → OBV = 8000

        Assert.That(obv.CurrentObv, Is.EqualTo(8000));
    }

    // ========================================================================
    // IsRising / IsFalling
    // ========================================================================

    [Test]
    public void IsRising_WhenOBV_Increases()
    {
        var obv = new ObvCalculator(3);

        obv.Update(100, 1000);
        obv.Update(101, 2000); // OBV increases
        obv.Update(102, 3000); // OBV increases more
        obv.Update(103, 4000); // OBV increases more

        Assert.That(obv.IsRising, Is.True);
    }

    [Test]
    public void IsFalling_WhenOBV_Decreases()
    {
        var obv = new ObvCalculator(3);

        obv.Update(110, 1000);
        obv.Update(109, 2000); // OBV decreases
        obv.Update(108, 3000); // OBV decreases
        obv.Update(107, 4000); // OBV decreases

        Assert.That(obv.IsFalling, Is.True);
    }

    // ========================================================================
    // Score
    // ========================================================================

    [Test]
    public void GetScore_ReturnsZero_WhenNotReady()
    {
        var obv = new ObvCalculator(20);
        obv.Update(100, 1000);
        Assert.That(obv.GetScore(), Is.EqualTo(0));
    }

    [Test]
    public void GetScore_WithinRange_Negative100To100()
    {
        var obv = new ObvCalculator(10);

        for (int i = 0; i < 50; i++)
            obv.Update(bars[i].Close, bars[i].Volume);

        if (obv.IsReady)
        {
            int score = obv.GetScore();
            Assert.That(score, Is.GreaterThanOrEqualTo(-100).And.LessThanOrEqualTo(100));
        }
    }

    // ========================================================================
    // Edge Cases
    // ========================================================================

    [Test]
    public void Update_WithZeroClose_Ignored()
    {
        var obv = new ObvCalculator(5);
        obv.Update(100, 1000);
        double before = obv.CurrentObv;
        obv.Update(0, 5000);
        Assert.That(obv.CurrentObv, Is.EqualTo(before));
    }

    [Test]
    public void Update_WithNegativeVolume_Ignored()
    {
        var obv = new ObvCalculator(5);
        obv.Update(100, 1000);
        double before = obv.CurrentObv;
        obv.Update(110, -500);
        Assert.That(obv.CurrentObv, Is.EqualTo(before));
    }

    // ========================================================================
    // Real Data
    // ========================================================================

    [Test]
    public void OBV_WithRealNVDAData_IsNonZero()
    {
        var obv = new ObvCalculator(20);

        for (int i = 0; i < 100; i++)
            obv.Update(bars[i].Close, bars[i].Volume);

        Assert.That(obv.CurrentObv, Is.Not.EqualTo(0),
            "OBV with 100 bars of real NVDA data should be non-zero");
    }
}
