// ============================================================================
// ADX Calculator Tests - Validates Average Directional Index with real data
// ============================================================================

using IdiotProof.Core.UnitTests.Helpers;
using IdiotProof.Helpers;

namespace IdiotProof.Core.UnitTests.Calculators;

[TestFixture]
public class AdxCalculatorTests
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
        var adx = new AdxCalculator();
        Assert.That(adx.Period, Is.EqualTo(14));
    }

    [Test]
    public void Constructor_WithZeroPeriod_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AdxCalculator(0));
    }

    [Test]
    public void Constructor_WithZeroTicksPerBar_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AdxCalculator(14, 0));
    }

    // ========================================================================
    // Warm-Up / IsReady (using UpdateFromCandle)
    // ========================================================================

    [Test]
    public void IsReady_Requires2xPeriodBars_ViaUpdateFromCandle()
    {
        var adx = new AdxCalculator(14);

        for (int i = 0; i < 27; i++)
        {
            adx.UpdateFromCandle(bars[i].High, bars[i].Low, bars[i].Close);
            Assert.That(adx.IsReady, Is.False, $"Not ready at {i + 1} bars");
        }

        // Need barsCompleted >= period * 2 = 28
        // First call initializes, so bar 0 doesn't count
        // barsCompleted increments from the second call onward
        // We need 28 completed bars, which means 29 calls (1 init + 28 completed)
        adx.UpdateFromCandle(bars[27].High, bars[27].Low, bars[27].Close);
        // Keep feeding until ready
        for (int i = 28; i < 40; i++)
        {
            adx.UpdateFromCandle(bars[i].High, bars[i].Low, bars[i].Close);
            if (adx.IsReady) break;
        }

        Assert.That(adx.IsReady, Is.True, "ADX should be ready after enough bars");
    }

    // ========================================================================
    // ADX Range (0-100)
    // ========================================================================

    [Test]
    public void ADX_AlwaysBetween0And100_WithRealData()
    {
        var adx = new AdxCalculator(14);

        for (int i = 0; i < 300; i++)
        {
            adx.UpdateFromCandle(bars[i].High, bars[i].Low, bars[i].Close);
            if (adx.IsReady)
            {
                Assert.That(adx.CurrentAdx, Is.GreaterThanOrEqualTo(0).And.LessThanOrEqualTo(100),
                    $"ADX out of range at bar {i}: {adx.CurrentAdx}");
            }
        }
    }

    [Test]
    public void PlusDI_AlwaysBetween0And100_WithRealData()
    {
        var adx = new AdxCalculator(14);

        for (int i = 0; i < 300; i++)
        {
            adx.UpdateFromCandle(bars[i].High, bars[i].Low, bars[i].Close);
            if (adx.IsReady)
            {
                Assert.That(adx.PlusDI, Is.GreaterThanOrEqualTo(0).And.LessThanOrEqualTo(100),
                    $"+DI out of range at bar {i}");
                Assert.That(adx.MinusDI, Is.GreaterThanOrEqualTo(0).And.LessThanOrEqualTo(100),
                    $"-DI out of range at bar {i}");
            }
        }
    }

    // ========================================================================
    // Trend Strength Detection
    // ========================================================================

    [Test]
    public void ADX_StrongTrend_WithSteadilyRisingPrices()
    {
        var adx = new AdxCalculator(14);

        // Create strongly trending data: steady uptrend
        for (int i = 0; i < 100; i++)
        {
            double basePrice = 100 + i * 0.5;
            adx.UpdateFromCandle(basePrice + 0.3, basePrice - 0.1, basePrice + 0.2);
        }

        Assert.That(adx.IsReady, Is.True);
        Assert.That(adx.CurrentAdx, Is.GreaterThan(20),
            "ADX should indicate a trend with steadily rising prices");
        Assert.That(adx.PlusDI, Is.GreaterThan(adx.MinusDI),
            "+DI should be greater than -DI in an uptrend");
    }

    [Test]
    public void ADX_StrongDowntrend_MinusDIGreaterThanPlusDI()
    {
        var adx = new AdxCalculator(14);

        // Create strong downtrend
        for (int i = 0; i < 100; i++)
        {
            double basePrice = 200 - i * 0.5;
            adx.UpdateFromCandle(basePrice + 0.1, basePrice - 0.3, basePrice - 0.2);
        }

        Assert.That(adx.IsReady, Is.True);
        Assert.That(adx.MinusDI, Is.GreaterThan(adx.PlusDI),
            "-DI should be greater than +DI in a downtrend");
    }

    // ========================================================================
    // UpdateFromCandle vs Update (tick-based)
    // ========================================================================

    [Test]
    public void UpdateFromCandle_ProducesValidResults()
    {
        var adx = new AdxCalculator(14);

        for (int i = 0; i < 100; i++)
            adx.UpdateFromCandle(bars[i].High, bars[i].Low, bars[i].Close);

        if (adx.IsReady)
        {
            Assert.That(adx.CurrentAdx, Is.GreaterThanOrEqualTo(0));
            Assert.That(adx.PlusDI + adx.MinusDI, Is.GreaterThan(0),
                "DI values should be non-zero after warm-up");
        }
    }

    // ========================================================================
    // Edge Cases
    // ========================================================================

    [Test]
    public void UpdateFromCandle_WithZeroPrice_Ignored()
    {
        var adx = new AdxCalculator(14);
        for (int i = 0; i < 50; i++)
            adx.UpdateFromCandle(bars[i].High, bars[i].Low, bars[i].Close);

        double before = adx.CurrentAdx;
        adx.UpdateFromCandle(0, 0, 0);
        Assert.That(adx.CurrentAdx, Is.EqualTo(before));
    }

    [Test]
    public void Reset_ClearsAllState()
    {
        var adx = new AdxCalculator(14);
        for (int i = 0; i < 50; i++)
            adx.UpdateFromCandle(bars[i].High, bars[i].Low, bars[i].Close);

        adx.Reset();

        Assert.Multiple(() =>
        {
            Assert.That(adx.IsReady, Is.False);
            Assert.That(adx.CurrentAdx, Is.EqualTo(0));
            Assert.That(adx.PlusDI, Is.EqualTo(0));
            Assert.That(adx.MinusDI, Is.EqualTo(0));
        });
    }

    // ========================================================================
    // Real Data with Known Behavior
    // ========================================================================

    [Test]
    public void ADX_WithRealNVDAData_ProducesReasonableValues()
    {
        var adx = new AdxCalculator(14);

        for (int i = 0; i < 200; i++)
            adx.UpdateFromCandle(bars[i].High, bars[i].Low, bars[i].Close);

        Assert.That(adx.IsReady, Is.True);
        // A stock like NVDA typically has some trend, ADX usually 10-60
        Assert.That(adx.CurrentAdx, Is.GreaterThan(0).And.LessThan(100),
            $"ADX should be in reasonable range for NVDA, got {adx.CurrentAdx}");
    }
}
