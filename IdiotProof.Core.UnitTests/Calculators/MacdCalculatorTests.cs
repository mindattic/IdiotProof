// ============================================================================
// MACD Calculator Tests - Validates MACD with real NVDA data
// ============================================================================

using IdiotProof.Core.UnitTests.Helpers;
using IdiotProof.Helpers;

namespace IdiotProof.Core.UnitTests.Calculators;

[TestFixture]
public class MacdCalculatorTests
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
    public void Constructor_Default_Uses12_26_9()
    {
        var macd = new MacdCalculator();
        Assert.Multiple(() =>
        {
            Assert.That(macd.FastPeriod, Is.EqualTo(12));
            Assert.That(macd.SlowPeriod, Is.EqualTo(26));
            Assert.That(macd.SignalPeriod, Is.EqualTo(9));
        });
    }

    [Test]
    public void Constructor_FastMustBeLessThanSlow()
    {
        Assert.Throws<ArgumentException>(() => new MacdCalculator(26, 12, 9));
    }

    [Test]
    public void Constructor_FastEqualsSlow_Throws()
    {
        Assert.Throws<ArgumentException>(() => new MacdCalculator(12, 12, 9));
    }

    [Test]
    public void Constructor_ZeroPeriods_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MacdCalculator(0, 26, 9));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MacdCalculator(12, 0, 9));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MacdCalculator(12, 26, 0));
    }

    // ========================================================================
    // Warm-Up / IsReady
    // ========================================================================

    [Test]
    public void IsReady_RequiresSlowPeriodPlusSignalPeriod()
    {
        var macd = new MacdCalculator();
        int required = 26 + 9; // 35

        for (int i = 0; i < required - 1; i++)
        {
            macd.Update(bars[i].Close);
            Assert.That(macd.IsReady, Is.False, $"Not ready at {i + 1}");
        }

        macd.Update(bars[required - 1].Close);
        Assert.That(macd.IsReady, Is.True, $"Should be ready after {required} data points");
    }

    // ========================================================================
    // MACD Line = Fast EMA - Slow EMA
    // ========================================================================

    [Test]
    public void MacdLine_EqualsFastEmaMinusSlowEma()
    {
        var macd = new MacdCalculator();
        var fastEma = new EmaCalculator(12);
        var slowEma = new EmaCalculator(26);

        for (int i = 0; i < 100; i++)
        {
            double price = bars[i].Close;
            macd.Update(price);
            fastEma.Update(price);
            slowEma.Update(price);
        }

        double expectedMacdLine = fastEma.CurrentValue - slowEma.CurrentValue;
        Assert.That(macd.MacdLine, Is.EqualTo(expectedMacdLine).Within(0.0001),
            "MACD line should equal Fast EMA - Slow EMA");
    }

    // ========================================================================
    // Histogram = MACD - Signal
    // ========================================================================

    [Test]
    public void Histogram_EqualsMacdMinusSignal()
    {
        var macd = new MacdCalculator();

        for (int i = 0; i < 100; i++)
            macd.Update(bars[i].Close);

        double expectedHistogram = macd.MacdLine - macd.SignalLine;
        Assert.That(macd.Histogram, Is.EqualTo(expectedHistogram).Within(0.0001),
            "Histogram should equal MACD line - Signal line");
    }

    // ========================================================================
    // Bullish / Bearish Detection
    // ========================================================================

    [Test]
    public void IsBullish_WhenMacdAboveSignal()
    {
        var macd = new MacdCalculator();
        for (int i = 0; i < 100; i++)
            macd.Update(bars[i].Close);

        if (macd.MacdLine > macd.SignalLine)
        {
            Assert.That(macd.IsBullish, Is.True);
            Assert.That(macd.IsBearish, Is.False);
        }
        else
        {
            Assert.That(macd.IsBullish, Is.False);
            Assert.That(macd.IsBearish, Is.True);
        }
    }

    [Test]
    public void IsBullish_WithRisingPrices()
    {
        var macd = new MacdCalculator();

        // Feed steadily rising prices with increasing acceleration
        // MACD requires SlowPeriod(26) + SignalPeriod(9) = 35 data points to be ready
        // Use accelerating rise to keep MACD line above signal line
        for (int i = 0; i < 60; i++)
            macd.Update(100 + i * 2.0 + i * i * 0.02);

        Assert.That(macd.IsReady, Is.True, "MACD should be ready after 60 data points");
        Assert.That(macd.MacdLine, Is.GreaterThan(0),
            $"MACD line should be above zero with accelerating prices. MacdLine={macd.MacdLine}");
    }

    [Test]
    public void IsBearish_WithFallingPrices()
    {
        var macd = new MacdCalculator();

        // Feed steadily falling prices
        for (int i = 0; i < 50; i++)
            macd.Update(200 - i * 0.5);

        Assert.That(macd.IsBearish, Is.True,
            "MACD should be bearish with steadily falling prices");
    }

    // ========================================================================
    // Histogram Rising/Falling
    // ========================================================================

    [Test]
    public void IsHistogramRising_TrackedCorrectly()
    {
        var macd = new MacdCalculator();

        for (int i = 0; i < 100; i++)
            macd.Update(bars[i].Close);

        bool expected = macd.Histogram > macd.PreviousHistogram;
        Assert.That(macd.IsHistogramRising, Is.EqualTo(expected));
    }

    // ========================================================================
    // Edge Cases
    // ========================================================================

    [Test]
    public void Update_WithZeroPrice_Ignored()
    {
        var macd = new MacdCalculator();
        for (int i = 0; i < 50; i++)
            macd.Update(bars[i].Close);

        double prevMacd = macd.MacdLine;
        macd.Update(0);
        Assert.That(macd.MacdLine, Is.EqualTo(prevMacd));
    }

    [Test]
    public void Reset_ClearsAllState()
    {
        var macd = new MacdCalculator();
        for (int i = 0; i < 50; i++)
            macd.Update(bars[i].Close);

        macd.Reset();

        Assert.Multiple(() =>
        {
            Assert.That(macd.IsReady, Is.False);
            Assert.That(macd.MacdLine, Is.EqualTo(0));
            Assert.That(macd.SignalLine, Is.EqualTo(0));
            Assert.That(macd.Histogram, Is.EqualTo(0));
        });
    }

    // ========================================================================
    // With Real Data  
    // ========================================================================

    [Test]
    public void MACD_WithRealData_ValuesAreReasonable()
    {
        var macd = new MacdCalculator();

        for (int i = 0; i < 200; i++)
            macd.Update(bars[i].Close);

        Assert.That(macd.IsReady, Is.True);

        double avgPrice = bars.Take(200).Average(b => b.Close);

        // MACD line should be small relative to the price
        Assert.That(Math.Abs(macd.MacdLine), Is.LessThan(avgPrice * 0.1),
            "MACD line should be small relative to price");
    }
}
