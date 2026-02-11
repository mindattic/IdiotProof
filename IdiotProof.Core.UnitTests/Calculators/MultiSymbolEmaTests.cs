// ============================================================================
// Multi-Symbol EMA Tests - Validates EMA across NVDA, CCHH, JZXN
// ============================================================================

using IdiotProof.Core.UnitTests.Helpers;
using IdiotProof.Helpers;

namespace IdiotProof.Core.UnitTests.Calculators;

[TestFixture]
public class MultiSymbolEmaTests
{
    private static readonly string[] Symbols = TestDataLoader.GetAvailableSymbols().ToArray();

    // ========================================================================
    // EMA value stays within price range for all symbols
    // ========================================================================

    [TestCaseSource(nameof(Symbols))]
    public void EMA9_StaysWithinPriceRange_AllSymbols(string symbol)
    {
        var bars = TestDataLoader.LoadBars(symbol, 200);
        var ema = new EmaCalculator(9);

        foreach (var bar in bars)
        {
            ema.Update(bar.Close);
            if (ema.IsReady)
            {
                var allCloses = bars.Select(b => b.Close).ToArray();
                Assert.That(ema.CurrentValue, Is.GreaterThan(allCloses.Min() * 0.5),
                    $"{symbol}: EMA too far below price range");
                Assert.That(ema.CurrentValue, Is.LessThan(allCloses.Max() * 1.5),
                    $"{symbol}: EMA too far above price range");
            }
        }
    }

    [TestCaseSource(nameof(Symbols))]
    public void EMA21_StaysWithinPriceRange_AllSymbols(string symbol)
    {
        var bars = TestDataLoader.LoadBars(symbol, 200);
        var ema = new EmaCalculator(21);

        foreach (var bar in bars)
        {
            ema.Update(bar.Close);
            if (ema.IsReady)
            {
                var allCloses = bars.Select(b => b.Close).ToArray();
                Assert.That(ema.CurrentValue, Is.GreaterThan(allCloses.Min() * 0.5));
                Assert.That(ema.CurrentValue, Is.LessThan(allCloses.Max() * 1.5));
            }
        }
    }

    // ========================================================================
    // Shorter EMA more responsive across all symbols
    // ========================================================================

    [TestCaseSource(nameof(Symbols))]
    public void ShorterEMA_MoreResponsive_AllSymbols(string symbol)
    {
        var bars = TestDataLoader.LoadBars(symbol, 200);
        var ema9 = new EmaCalculator(9);
        var ema50 = new EmaCalculator(50);

        foreach (var bar in bars)
        {
            ema9.Update(bar.Close);
            ema50.Update(bar.Close);
        }

        Assert.That(ema9.IsReady && ema50.IsReady, Is.True,
            $"{symbol}: Both EMAs should be ready after 200 bars");

        // Measure deviation from last price - shorter period should be closer
        var lastPrice = bars[^1].Close;
        var diff9 = Math.Abs(ema9.CurrentValue - lastPrice);
        var diff50 = Math.Abs(ema50.CurrentValue - lastPrice);

        Assert.That(diff9, Is.LessThanOrEqualTo(diff50 * 1.5),
            $"{symbol}: EMA(9) should generally be closer to current price than EMA(50)");
    }

    // ========================================================================
    // EMA warm-up consistent across all symbols
    // ========================================================================

    [TestCaseSource(nameof(Symbols))]
    public void EMA_WarmUp_ConsistentAcrossSymbols(string symbol)
    {
        var bars = TestDataLoader.LoadBars(symbol, 50);
        var ema = new EmaCalculator(20);

        for (int i = 0; i < 19; i++)
        {
            ema.Update(bars[i].Close);
            Assert.That(ema.IsReady, Is.False, $"{symbol}: Not ready at {i + 1}");
        }

        ema.Update(bars[19].Close);
        Assert.That(ema.IsReady, Is.True, $"{symbol}: Ready at 20");
    }

    // ========================================================================
    // EMA seed equals SMA for all symbols
    // ========================================================================

    [TestCaseSource(nameof(Symbols))]
    public void EMA_SeedEqualsSMA_AllSymbols(string symbol)
    {
        var bars = TestDataLoader.LoadBars(symbol, 30);
        var ema = new EmaCalculator(9);
        double expectedSma = bars.Take(9).Average(b => b.Close);

        for (int i = 0; i < 9; i++)
            ema.Update(bars[i].Close);

        Assert.That(ema.CurrentValue, Is.EqualTo(expectedSma).Within(1e-6),
            $"{symbol}: EMA seed should equal SMA of first 9 prices");
    }

    // ========================================================================
    // EMA PreviousValue tracks correctly for all symbols
    // ========================================================================

    [TestCaseSource(nameof(Symbols))]
    public void EMA_PreviousValue_TracksCorrectly_AllSymbols(string symbol)
    {
        var bars = TestDataLoader.LoadBars(symbol, 30);
        var ema = new EmaCalculator(9);

        double lastValue = 0;
        for (int i = 0; i < 20; i++)
        {
            if (ema.IsReady)
                lastValue = ema.CurrentValue;
            ema.Update(bars[i].Close);
        }

        Assert.That(ema.PreviousValue, Is.EqualTo(lastValue).Within(1e-10),
            $"{symbol}: PreviousValue should track correctly");
    }

    // ========================================================================
    // Penny stock EMA handles small prices correctly
    // ========================================================================

    [Test]
    public void EMA_PennyStock_CCHH_HandlesSmallPricesCorrectly()
    {
        var bars = TestDataLoader.LoadBars("CCHH", 100);
        var ema = new EmaCalculator(9);

        foreach (var bar in bars)
            ema.Update(bar.Close);

        Assert.That(ema.IsReady, Is.True);
        Assert.That(ema.CurrentValue, Is.GreaterThan(0),
            "EMA should be positive for penny stock");
        // CCHH trades around $0.50 - EMA should be in that ballpark
        Assert.That(ema.CurrentValue, Is.LessThan(10),
            "EMA for ~$0.50 stock should be well under $10");
    }

    // ========================================================================
    // Large cap EMA handles large prices correctly
    // ========================================================================

    [Test]
    public void EMA_LargeCap_NVDA_HandlesLargePricesCorrectly()
    {
        var bars = TestDataLoader.LoadBars("NVDA", 100);
        var ema = new EmaCalculator(9);

        foreach (var bar in bars)
            ema.Update(bar.Close);

        Assert.That(ema.IsReady, Is.True);
        // NVDA trades >$100 - EMA should reflect that
        Assert.That(ema.CurrentValue, Is.GreaterThan(50),
            "EMA for NVDA should be well above $50");
    }

    // ========================================================================
    // Reset works regardless of price scale
    // ========================================================================

    [TestCaseSource(nameof(Symbols))]
    public void EMA_Reset_WorksForAllSymbols(string symbol)
    {
        var bars = TestDataLoader.LoadBars(symbol, 30);
        var ema = new EmaCalculator(9);

        foreach (var bar in bars)
            ema.Update(bar.Close);

        Assert.That(ema.IsReady, Is.True);

        ema.Reset();
        Assert.That(ema.IsReady, Is.False);
        Assert.That(ema.CurrentValue, Is.EqualTo(0));
    }
}
