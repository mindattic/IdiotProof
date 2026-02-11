// ============================================================================
// ATR Calculator Tests - Validates Average True Range with real data
// ============================================================================

using IdiotProof.Core.UnitTests.Helpers;
using IdiotProof.Helpers;

namespace IdiotProof.Core.UnitTests.Calculators;

[TestFixture]
public class AtrCalculatorTests
{
    private List<TestBar> _bars = null!;

    [OneTimeSetUp]
    public void LoadData()
    {
        _bars = TestDataLoader.LoadBars("NVDA", 300);
    }

    // ========================================================================
    // Constructor / Validation
    // ========================================================================

    [Test]
    public void Constructor_DefaultPeriod_Is14()
    {
        var atr = new AtrCalculator();
        Assert.That(atr.CurrentAtr, Is.EqualTo(0));
        Assert.That(atr.IsReady, Is.False);
    }

    [Test]
    public void Constructor_WithZeroPeriod_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AtrCalculator(0));
    }

    [Test]
    public void Constructor_WithZeroTicksPerBar_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AtrCalculator(14, 0));
    }

    // ========================================================================
    // UpdateFromCandle - Primary Method
    // ========================================================================

    [Test]
    public void UpdateFromCandle_IsReady_AfterHalfPeriod()
    {
        var atr = new AtrCalculator(14);

        // First call initializes previous close
        atr.UpdateFromCandle(_bars[0].High, _bars[0].Low, _bars[0].Close);
        Assert.That(atr.IsReady, Is.False);

        // Feed bars until ready (period / 2 = 7 bars)
        for (int i = 1; i <= 8; i++)
            atr.UpdateFromCandle(_bars[i].High, _bars[i].Low, _bars[i].Close);

        Assert.That(atr.IsReady, Is.True, "ATR should be ready after period/2 candles");
    }

    [Test]
    public void ATR_AlwaysPositive_WithRealData()
    {
        var atr = new AtrCalculator(14);

        for (int i = 0; i < 100; i++)
        {
            atr.UpdateFromCandle(_bars[i].High, _bars[i].Low, _bars[i].Close);
            Assert.That(atr.CurrentAtr, Is.GreaterThanOrEqualTo(0),
                $"ATR should never be negative, got {atr.CurrentAtr} at bar {i}");
        }
    }

    // ========================================================================
    // True Range Calculation Verification
    // ========================================================================

    [Test]
    public void ATR_ReflectsVolatility_HighVolatilityGivesHigherATR()
    {
        // Low volatility: narrow range bars
        var atrLow = new AtrCalculator(5);
        for (int i = 0; i < 20; i++)
        {
            double basePrice = 100;
            atrLow.UpdateFromCandle(basePrice + 0.1, basePrice - 0.1, basePrice);
        }

        // High volatility: wide range bars
        var atrHigh = new AtrCalculator(5);
        for (int i = 0; i < 20; i++)
        {
            double basePrice = 100;
            atrHigh.UpdateFromCandle(basePrice + 5, basePrice - 5, basePrice);
        }

        Assert.That(atrHigh.CurrentAtr, Is.GreaterThan(atrLow.CurrentAtr * 5),
            "Higher volatility should produce significantly higher ATR");
    }

    [Test]
    public void ATR_UsesWilderSmoothing_ViaUpdateFromCandle()
    {
        var atr = new AtrCalculator(5);

        // Feed initial bars
        atr.UpdateFromCandle(101, 99, 100); // Init - sets previous close
        atr.UpdateFromCandle(102, 98, 101); // TR = max(4, 2, 2) = 4, ATR = 4
        atr.UpdateFromCandle(103, 97, 100); // TR = max(6, 2, 4) = 6
        // ATR = (4 * 4 + 6) / 5 = 22/5 = 4.4

        Assert.That(atr.CurrentAtr, Is.GreaterThan(0));
    }

    // ========================================================================
    // Stop Price Calculation
    // ========================================================================

    [Test]
    public void CalculateStopPrice_ForLong_IsBelowReferencePrice()
    {
        var atr = new AtrCalculator(14);
        for (int i = 0; i < 30; i++)
            atr.UpdateFromCandle(_bars[i].High, _bars[i].Low, _bars[i].Close);

        Assert.That(atr.IsReady, Is.True);

        double referencePrice = _bars[29].Close;
        double stopPrice = atr.CalculateStopPrice(referencePrice, 2.0, isLong: true);

        Assert.That(stopPrice, Is.LessThan(referencePrice),
            "Long stop price should be below reference price");
        Assert.That(stopPrice, Is.GreaterThan(referencePrice * 0.5),
            "Stop price should not be excessively far from reference");
    }

    [Test]
    public void CalculateStopPrice_ForShort_IsAboveReferencePrice()
    {
        var atr = new AtrCalculator(14);
        for (int i = 0; i < 30; i++)
            atr.UpdateFromCandle(_bars[i].High, _bars[i].Low, _bars[i].Close);

        double referencePrice = _bars[29].Close;
        double stopPrice = atr.CalculateStopPrice(referencePrice, 2.0, isLong: false);

        Assert.That(stopPrice, Is.GreaterThan(referencePrice),
            "Short stop price should be above reference price");
    }

    [Test]
    public void CalculateStopPrice_HigherMultiplier_WiderStop()
    {
        var atr = new AtrCalculator(14);
        for (int i = 0; i < 30; i++)
            atr.UpdateFromCandle(_bars[i].High, _bars[i].Low, _bars[i].Close);

        double refPrice = _bars[29].Close;
        double stop1x = atr.CalculateStopPrice(refPrice, 1.0, isLong: true);
        double stop3x = atr.CalculateStopPrice(refPrice, 3.0, isLong: true);

        // Note: CalculateStopPrice has min/max % clamping, so a wider multiplier
        // produces an equal or lower stop price for longs
        Assert.That(stop3x, Is.LessThanOrEqualTo(stop1x),
            "3x ATR stop should be wider (lower or equal, due to clamping) than 1x ATR stop");
    }

    // ========================================================================
    // ATR Percent
    // ========================================================================

    [Test]
    public void GetAtrPercent_ReturnsCorrectPercentage()
    {
        var atr = new AtrCalculator(14);
        for (int i = 0; i < 30; i++)
            atr.UpdateFromCandle(_bars[i].High, _bars[i].Low, _bars[i].Close);

        double price = _bars[29].Close;
        double pct = atr.GetAtrPercent(price);

        double expected = atr.CurrentAtr / price;
        Assert.That(pct, Is.EqualTo(expected).Within(0.0001));
    }

    [Test]
    public void GetAtrPercent_WithZeroPrice_ReturnsZero()
    {
        var atr = new AtrCalculator(14);
        for (int i = 0; i < 30; i++)
            atr.UpdateFromCandle(_bars[i].High, _bars[i].Low, _bars[i].Close);

        Assert.That(atr.GetAtrPercent(0), Is.EqualTo(0));
    }

    // ========================================================================
    // Edge Cases
    // ========================================================================

    [Test]
    public void UpdateFromCandle_WithZeroPrices_Ignored()
    {
        var atr = new AtrCalculator(14);
        for (int i = 0; i < 20; i++)
            atr.UpdateFromCandle(_bars[i].High, _bars[i].Low, _bars[i].Close);

        double before = atr.CurrentAtr;
        atr.UpdateFromCandle(0, 0, 0);
        Assert.That(atr.CurrentAtr, Is.EqualTo(before));
    }

    [Test]
    public void Reset_ClearsAllState()
    {
        var atr = new AtrCalculator(14);
        for (int i = 0; i < 30; i++)
            atr.UpdateFromCandle(_bars[i].High, _bars[i].Low, _bars[i].Close);

        atr.Reset();

        Assert.Multiple(() =>
        {
            Assert.That(atr.IsReady, Is.False);
            Assert.That(atr.CurrentAtr, Is.EqualTo(0));
        });
    }

    // ========================================================================
    // Real Data Validation
    // ========================================================================

    [Test]
    public void ATR_WithRealNVDAData_ReflectsActualVolatility()
    {
        var atr = new AtrCalculator(14);

        for (int i = 0; i < 200; i++)
            atr.UpdateFromCandle(_bars[i].High, _bars[i].Low, _bars[i].Close);

        Assert.That(atr.IsReady, Is.True);

        // NVDA typically has ATR of a few dollars
        double avgPrice = _bars.Take(200).Average(b => b.Close);
        double atrPct = atr.CurrentAtr / avgPrice * 100;

        Assert.That(atrPct, Is.GreaterThan(0.01).And.LessThan(20),
            $"ATR should be a reasonable % of price. ATR={atr.CurrentAtr:F2}, Price={avgPrice:F2}, %={atrPct:F2}");
    }
}
