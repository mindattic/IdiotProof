// ============================================================================
// EMA Calculator Tests - Validates EMA calculation with real NVDA data
// ============================================================================

using IdiotProof.Core.UnitTests.Helpers;
using IdiotProof.Helpers;

namespace IdiotProof.Core.UnitTests.Calculators;

[TestFixture]
public class EmaCalculatorTests
{
    private List<TestBar> bars = null!;

    [OneTimeSetUp]
    public void LoadData()
    {
        bars = TestDataLoader.LoadBars("NVDA", 500);
        Assert.That(bars, Has.Count.GreaterThanOrEqualTo(500), "Need at least 500 NVDA bars for EMA tests");
    }

    // ========================================================================
    // Constructor / Validation
    // ========================================================================

    [Test]
    public void Constructor_WithValidPeriod_CreatesCalculator()
    {
        var ema = new EmaCalculator(9);
        Assert.That(ema.Period, Is.EqualTo(9));
        Assert.That(ema.IsReady, Is.False);
        Assert.That(ema.CurrentValue, Is.EqualTo(0));
    }

    [Test]
    public void Constructor_WithZeroPeriod_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new EmaCalculator(0));
    }

    [Test]
    public void Constructor_WithNegativePeriod_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new EmaCalculator(-5));
    }

    // ========================================================================
    // Warm-Up / IsReady
    // ========================================================================

    [Test]
    public void IsReady_BecomesTrueAfterPeriodPrices()
    {
        var ema = new EmaCalculator(9);

        for (int i = 0; i < 8; i++)
        {
            ema.Update(bars[i].Close);
            Assert.That(ema.IsReady, Is.False, $"Should not be ready after {i + 1} prices");
        }

        ema.Update(bars[8].Close);
        Assert.That(ema.IsReady, Is.True, "Should be ready after 9 prices for EMA(9)");
    }

    [Test]
    public void IsReady_EMA21_BecomesTrueAfter21Prices()
    {
        var ema = new EmaCalculator(21);

        for (int i = 0; i < 20; i++)
            ema.Update(bars[i].Close);

        Assert.That(ema.IsReady, Is.False, "Should not be ready after 20 prices");

        ema.Update(bars[20].Close);
        Assert.That(ema.IsReady, Is.True, "Should be ready after 21 prices for EMA(21)");
    }

    // ========================================================================
    // Initial Value = SMA Seed
    // ========================================================================

    [Test]
    public void InitialEMA_EqualsSMAOfFirstNPrices()
    {
        var ema = new EmaCalculator(9);
        double sum = 0;

        for (int i = 0; i < 9; i++)
        {
            sum += bars[i].Close;
            ema.Update(bars[i].Close);
        }

        double expectedSma = sum / 9;
        Assert.That(ema.CurrentValue, Is.EqualTo(expectedSma).Within(0.0001),
            "Initial EMA should equal SMA of first 9 prices");
    }

    // ========================================================================
    // EMA Formula Verification
    // ========================================================================

    [Test]
    public void EMAFormula_CorrectlyApplied_AfterWarmUp()
    {
        var ema = new EmaCalculator(9);
        double multiplier = 2.0 / (9 + 1); // 0.2

        // Warm up
        for (int i = 0; i < 9; i++)
            ema.Update(bars[i].Close);

        double previousEma = ema.CurrentValue;

        // Apply one more price and verify the formula
        double newPrice = bars[9].Close;
        ema.Update(newPrice);

        double expectedEma = (newPrice - previousEma) * multiplier + previousEma;
        Assert.That(ema.CurrentValue, Is.EqualTo(expectedEma).Within(0.0001),
            "EMA formula: (Price - PrevEMA) × Multiplier + PrevEMA");
    }

    [Test]
    public void EMAFormula_VerifyMultipleSteps()
    {
        var ema = new EmaCalculator(9);
        double multiplier = 2.0 / (9 + 1);

        // Warm up
        for (int i = 0; i < 9; i++)
            ema.Update(bars[i].Close);

        // Verify formula for next 50 bars
        for (int i = 9; i < 59; i++)
        {
            double previousEma = ema.CurrentValue;
            double price = bars[i].Close;
            ema.Update(price);
            double expected = (price - previousEma) * multiplier + previousEma;
            Assert.That(ema.CurrentValue, Is.EqualTo(expected).Within(0.0001),
                $"EMA formula mismatch at bar {i}");
        }
    }

    // ========================================================================
    // PreviousValue Tracking
    // ========================================================================

    [Test]
    public void PreviousValue_TracksLastEMA()
    {
        var ema = new EmaCalculator(9);

        for (int i = 0; i < 10; i++)
            ema.Update(bars[i].Close);

        double prevBefore = ema.CurrentValue;
        ema.Update(bars[10].Close);

        Assert.That(ema.PreviousValue, Is.EqualTo(prevBefore).Within(0.0001),
            "PreviousValue should match the EMA before the last update");
    }

    // ========================================================================
    // Behavioral Properties with Real Data
    // ========================================================================

    [Test]
    public void EMA_ReflectsRecentPricesTrend_WithRealNVDAData()
    {
        var ema9 = new EmaCalculator(9);
        var ema21 = new EmaCalculator(21);

        // Feed 200 bars of real NVDA data
        for (int i = 0; i < 200; i++)
        {
            ema9.Update(bars[i].Close);
            ema21.Update(bars[i].Close);
        }

        Assert.That(ema9.IsReady, Is.True);
        Assert.That(ema21.IsReady, Is.True);

        // EMA values should be in the price range
        double minPrice = bars.Take(200).Min(b => b.Low);
        double maxPrice = bars.Take(200).Max(b => b.High);

        Assert.That(ema9.CurrentValue, Is.GreaterThan(minPrice * 0.95).And.LessThan(maxPrice * 1.05),
            "EMA(9) should be within the price range");
        Assert.That(ema21.CurrentValue, Is.GreaterThan(minPrice * 0.95).And.LessThan(maxPrice * 1.05),
            "EMA(21) should be within the price range");
    }

    [Test]
    public void ShorterEMA_IsMoreResponsive_ThanLongerEMA()
    {
        var ema9 = new EmaCalculator(9);
        var ema50 = new EmaCalculator(50);

        // Feed 100 bars
        for (int i = 0; i < 100; i++)
        {
            ema9.Update(bars[i].Close);
            ema50.Update(bars[i].Close);
        }

        // Feed a price spike
        double spike = bars[99].Close * 1.05; // 5% spike
        ema9.Update(spike);
        ema50.Update(spike);

        double ema9Response = Math.Abs(ema9.CurrentValue - bars[99].Close);
        double ema50Response = Math.Abs(ema50.CurrentValue - bars[99].Close);

        Assert.That(ema9Response, Is.GreaterThan(ema50Response),
            "EMA(9) should react more to price spike than EMA(50)");
    }

    // ========================================================================
    // Edge Cases
    // ========================================================================

    [Test]
    public void Update_WithZeroPrice_IgnoresAndReturnsCurrentEma()
    {
        var ema = new EmaCalculator(9);
        for (int i = 0; i < 10; i++)
            ema.Update(bars[i].Close);

        double before = ema.CurrentValue;
        ema.Update(0);
        Assert.That(ema.CurrentValue, Is.EqualTo(before));
    }

    [Test]
    public void Update_WithNegativePrice_IgnoresAndReturnsCurrentEma()
    {
        var ema = new EmaCalculator(9);
        for (int i = 0; i < 10; i++)
            ema.Update(bars[i].Close);

        double before = ema.CurrentValue;
        ema.Update(-100);
        Assert.That(ema.CurrentValue, Is.EqualTo(before));
    }

    [Test]
    public void Reset_ClearsAllState()
    {
        var ema = new EmaCalculator(9);
        for (int i = 0; i < 20; i++)
            ema.Update(bars[i].Close);

        Assert.That(ema.IsReady, Is.True);

        ema.Reset();

        Assert.That(ema.IsReady, Is.False);
        Assert.That(ema.CurrentValue, Is.EqualTo(0));
        Assert.That(ema.PreviousValue, Is.EqualTo(0));
    }

    [Test]
    public void Period1_EMA_EqualsCurrentPrice()
    {
        var ema = new EmaCalculator(1);
        // With period 1, multiplier = 2/(1+1) = 1.0
        // After init: EMA = first price (SMA of 1)
        // Then: EMA = (price - prev) * 1.0 + prev = price
        ema.Update(bars[0].Close);
        Assert.That(ema.IsReady, Is.True);
        Assert.That(ema.CurrentValue, Is.EqualTo(bars[0].Close).Within(0.0001));

        ema.Update(bars[1].Close);
        Assert.That(ema.CurrentValue, Is.EqualTo(bars[1].Close).Within(0.0001));
    }
}
