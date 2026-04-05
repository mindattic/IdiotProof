// ============================================================================
// Volume Calculator Tests - Validates Volume analysis with real data
// ============================================================================

using IdiotProof.Core.UnitTests.Helpers;
using IdiotProof.Helpers;

namespace IdiotProof.Core.UnitTests.Calculators;

[TestFixture]
public class VolumeCalculatorTests
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
    public void Constructor_DefaultPeriod_Is20()
    {
        var vol = new VolumeCalculator();
        Assert.That(vol.Period, Is.EqualTo(20));
    }

    [Test]
    public void Constructor_WithZeroPeriod_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new VolumeCalculator(0));
    }

    // ========================================================================
    // Warm-Up / IsReady
    // ========================================================================

    [Test]
    public void IsReady_BecomesTrueAfterPeriodUpdates()
    {
        var vol = new VolumeCalculator(20);

        for (int i = 0; i < 19; i++)
        {
            vol.Update(bars[i].Volume);
            Assert.That(vol.IsReady, Is.False);
        }

        vol.Update(bars[19].Volume);
        Assert.That(vol.IsReady, Is.True);
    }

    // ========================================================================
    // Average Volume Calculation
    // ========================================================================

    [Test]
    public void AverageVolume_IsArithmeticMean()
    {
        var vol = new VolumeCalculator(10);

        for (int i = 0; i < 10; i++)
            vol.Update(bars[i].Volume);

        double expected = bars.Take(10).Where(b => b.Volume > 0).Average(b => (double)b.Volume);
        // Note: zero-volume bars are ignored by the calculator
        // Only include non-zero volumes
        var nonZeroVols = bars.Take(10).Where(b => b.Volume > 0).ToList();
        if (nonZeroVols.Count == 10)
        {
            Assert.That(vol.AverageVolume, Is.EqualTo(expected).Within(1),
                "Average volume should be the mean of recent volumes");
        }
    }

    // ========================================================================
    // Volume Ratio
    // ========================================================================

    [Test]
    public void VolumeRatio_IsCurrentDividedByAverage()
    {
        var vol = new VolumeCalculator(10);

        for (int i = 0; i < 20; i++)
            vol.Update(bars[i].Volume);

        if (vol.AverageVolume > 0)
        {
            double expectedRatio = vol.CurrentVolume / vol.AverageVolume;
            Assert.That(vol.VolumeRatio, Is.EqualTo(expectedRatio).Within(0.001));
        }
    }

    // ========================================================================
    // IsAboveAverage
    // ========================================================================

    [Test]
    public void IsAboveAverage_WithHighMultiplier_RequiresSpike()
    {
        var vol = new VolumeCalculator(5);

        // Feed consistent volumes
        for (int i = 0; i < 5; i++)
            vol.Update(1000);

        // Average is now 1000
        vol.Update(1200); // 1.2x
        Assert.That(vol.IsAboveAverage(1.5), Is.False, "1200 is not 1.5x of ~1000");

        vol.Update(2000); // Well above
        Assert.That(vol.IsAboveAverage(1.5), Is.True, "2000 should be above 1.5x average");
    }

    [Test]
    public void IsAboveAverage_NotReadyYet_ReturnsFalse()
    {
        var vol = new VolumeCalculator(20);
        vol.Update(10000);
        Assert.That(vol.IsAboveAverage(1.0), Is.False);
    }

    // ========================================================================
    // Edge Cases
    // ========================================================================

    [Test]
    public void Update_WithZeroVolume_Ignored()
    {
        var vol = new VolumeCalculator(5);
        for (int i = 0; i < 5; i++)
            vol.Update(1000);

        double avgBefore = vol.AverageVolume;
        vol.Update(0);
        Assert.That(vol.AverageVolume, Is.EqualTo(avgBefore));
    }

    [Test]
    public void Reset_ClearsAllState()
    {
        var vol = new VolumeCalculator(10);
        for (int i = 0; i < 20; i++)
            vol.Update(bars[i].Volume);

        vol.Reset();

        Assert.Multiple(() =>
        {
            Assert.That(vol.IsReady, Is.False);
            Assert.That(vol.CurrentVolume, Is.EqualTo(0));
            Assert.That(vol.AverageVolume, Is.EqualTo(0));
        });
    }

    // ========================================================================
    // Real Data
    // ========================================================================

    [Test]
    public void Volume_WithRealNVDAData_ProducesPositiveAverage()
    {
        var vol = new VolumeCalculator(20);

        for (int i = 0; i < 100; i++)
            vol.Update(bars[i].Volume);

        Assert.That(vol.IsReady, Is.True);
        Assert.That(vol.AverageVolume, Is.GreaterThan(0),
            "Average volume should be positive with real NVDA data");
    }
}
