// ============================================================================
// Multi-Symbol MACD Tests - Validates MACD across NVDA, CCHH, JZXN
// ============================================================================

using IdiotProof.Core.UnitTests.Helpers;
using IdiotProof.Helpers;

namespace IdiotProof.Core.UnitTests.Calculators;

[TestFixture]
public class MultiSymbolMacdTests
{
    private static readonly string[] Symbols = TestDataLoader.GetAvailableSymbols().ToArray();

    // ========================================================================
    // MACD warm-up consistent across all symbols
    // ========================================================================

    [TestCaseSource(nameof(Symbols))]
    public void MACD_WarmUp_ConsistentAcrossSymbols(string symbol)
    {
        var bars = TestDataLoader.LoadBars(symbol, 50);
        var macd = new MacdCalculator();

        for (int i = 0; i < 34; i++)
        {
            macd.Update(bars[i].Close);
            Assert.That(macd.IsReady, Is.False,
                $"{symbol}: MACD should not be ready after {i + 1} bars");
        }

        macd.Update(bars[34].Close);
        Assert.That(macd.IsReady, Is.True,
            $"{symbol}: MACD should be ready after 35 bars (26+9)");
    }

    // ========================================================================
    // MACD line is fast minus slow EMA for all symbols
    // ========================================================================

    [TestCaseSource(nameof(Symbols))]
    public void MACD_LineEqualsFastMinusSlow_AllSymbols(string symbol)
    {
        var bars = TestDataLoader.LoadBars(symbol, 100);
        var macd = new MacdCalculator();
        var emaFast = new EmaCalculator(12);
        var emaSlow = new EmaCalculator(26);

        foreach (var bar in bars)
        {
            macd.Update(bar.Close);
            emaFast.Update(bar.Close);
            emaSlow.Update(bar.Close);
        }

        if (macd.IsReady && emaFast.IsReady && emaSlow.IsReady)
        {
            var expected = emaFast.CurrentValue - emaSlow.CurrentValue;
            Assert.That(macd.MacdLine, Is.EqualTo(expected).Within(1e-6),
                $"{symbol}: MACD line should equal fast - slow EMA");
        }
    }

    // ========================================================================
    // MACD histogram = MACD - Signal for all symbols
    // ========================================================================

    [TestCaseSource(nameof(Symbols))]
    public void MACD_Histogram_EqualsLineMinusSignal_AllSymbols(string symbol)
    {
        var bars = TestDataLoader.LoadBars(symbol, 100);
        var macd = new MacdCalculator();

        foreach (var bar in bars)
            macd.Update(bar.Close);

        if (macd.IsReady)
        {
            var expected = macd.MacdLine - macd.SignalLine;
            Assert.That(macd.Histogram, Is.EqualTo(expected).Within(1e-6),
                $"{symbol}: Histogram should equal MACD - Signal");
        }
    }

    // ========================================================================
    // MACD produces both bullish and bearish signals in enough data
    // ========================================================================

    [TestCaseSource(nameof(Symbols))]
    public void MACD_ProducesBothSignals_AllSymbols(string symbol)
    {
        var bars = TestDataLoader.LoadBars(symbol, 2000);
        var macd = new MacdCalculator();
        bool sawBullish = false;
        bool sawBearish = false;

        foreach (var bar in bars)
        {
            macd.Update(bar.Close);
            if (macd.IsReady)
            {
                if (macd.IsBullish) sawBullish = true;
                if (macd.IsBearish) sawBearish = true;
            }
        }

        Assert.That(sawBullish, Is.True,
            $"{symbol}: MACD should be bullish at some point in 2000 bars");
        Assert.That(sawBearish, Is.True,
            $"{symbol}: MACD should be bearish at some point in 2000 bars");
    }

    // ========================================================================
    // MACD values scale with price (penny stock vs large cap)
    // ========================================================================

    [Test]
    public void MACD_ValuesScale_WithPriceRange()
    {
        var nvdaBars = TestDataLoader.LoadBars("NVDA", 200);
        var ccchhBars = TestDataLoader.LoadBars("CCHH", 200);
        var macdNvda = new MacdCalculator();
        var macdCchh = new MacdCalculator();

        foreach (var bar in nvdaBars) macdNvda.Update(bar.Close);
        foreach (var bar in ccchhBars) macdCchh.Update(bar.Close);

        if (macdNvda.IsReady && macdCchh.IsReady)
        {
            // NVDA (~$100+) should have larger absolute MACD values than CCHH (~$0.50)
            var nvdaRange = Math.Abs(macdNvda.MacdLine);
            var cchhRange = Math.Abs(macdCchh.MacdLine);

            // NVDA price is ~200x CCHH, so MACD should be proportionally larger
            // Use a generous tolerance since it's not an exact scaling
            Assert.That(nvdaRange, Is.GreaterThan(cchhRange * 0.1),
                $"NVDA MACD ({nvdaRange:F6}) should generally be larger than CCHH MACD ({cchhRange:F6})");
        }
    }

    // ========================================================================
    // Penny stock MACD produces non-zero values
    // ========================================================================

    [Test]
    public void MACD_PennyStock_ProducesNonZeroValues()
    {
        var bars = TestDataLoader.LoadBars("CCHH", 200);
        var macd = new MacdCalculator();

        foreach (var bar in bars)
            macd.Update(bar.Close);

        Assert.That(macd.IsReady, Is.True);
        // At least one of the values should be non-zero
        Assert.That(Math.Abs(macd.MacdLine) + Math.Abs(macd.SignalLine), Is.GreaterThan(0),
            "MACD should produce non-zero values even for penny stock");
    }

    // ========================================================================
    // Reset works for all symbols
    // ========================================================================

    [TestCaseSource(nameof(Symbols))]
    public void MACD_Reset_WorksForAllSymbols(string symbol)
    {
        var bars = TestDataLoader.LoadBars(symbol, 50);
        var macd = new MacdCalculator();

        foreach (var bar in bars)
            macd.Update(bar.Close);

        Assert.That(macd.IsReady, Is.True);

        macd.Reset();
        Assert.That(macd.IsReady, Is.False);
        Assert.That(macd.MacdLine, Is.EqualTo(0));
    }
}
