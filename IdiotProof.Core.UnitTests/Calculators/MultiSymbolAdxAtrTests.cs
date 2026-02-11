// ============================================================================
// Multi-Symbol ADX/ATR Tests - Validates trend/volatility across symbols
// ============================================================================

using IdiotProof.Core.UnitTests.Helpers;
using IdiotProof.Helpers;

namespace IdiotProof.Core.UnitTests.Calculators;

[TestFixture]
public class MultiSymbolAdxAtrTests
{
    private static readonly string[] Symbols = TestDataLoader.GetAvailableSymbols().ToArray();

    // ========================================================================
    // ADX Tests
    // ========================================================================

    [TestCaseSource(nameof(Symbols))]
    public void ADX_AlwaysBetween0And100_AllSymbols(string symbol)
    {
        var bars = TestDataLoader.LoadBars(symbol, 300);
        var adx = new AdxCalculator(14);

        foreach (var bar in bars)
        {
            adx.UpdateFromCandle(bar.High, bar.Low, bar.Close);
            if (adx.IsReady)
            {
                Assert.That(adx.CurrentAdx, Is.GreaterThanOrEqualTo(0).And.LessThanOrEqualTo(100),
                    $"{symbol}: ADX out of range: {adx.CurrentAdx}");
            }
        }
    }

    [TestCaseSource(nameof(Symbols))]
    public void ADX_PlusDI_AlwaysNonNegative_AllSymbols(string symbol)
    {
        var bars = TestDataLoader.LoadBars(symbol, 300);
        var adx = new AdxCalculator(14);

        foreach (var bar in bars)
        {
            adx.UpdateFromCandle(bar.High, bar.Low, bar.Close);
            if (adx.IsReady)
            {
                Assert.That(adx.PlusDI, Is.GreaterThanOrEqualTo(0),
                    $"{symbol}: +DI should be >= 0");
                Assert.That(adx.MinusDI, Is.GreaterThanOrEqualTo(0),
                    $"{symbol}: -DI should be >= 0");
            }
        }
    }

    [TestCaseSource(nameof(Symbols))]
    public void ADX_WarmUp_ConsistentAcrossSymbols(string symbol)
    {
        var bars = TestDataLoader.LoadBars(symbol, 60);
        var adx = new AdxCalculator(14);

        int readyAt = -1;
        for (int i = 0; i < 60; i++)
        {
            adx.UpdateFromCandle(bars[i].High, bars[i].Low, bars[i].Close);
            if (adx.IsReady && readyAt == -1)
                readyAt = i;
        }

        Assert.That(readyAt, Is.GreaterThanOrEqualTo(0),
            $"{symbol}: ADX should become ready within 60 bars");
        Assert.That(readyAt, Is.LessThanOrEqualTo(40),
            $"{symbol}: ADX should be ready by bar 40 (got {readyAt})");
    }

    [TestCaseSource(nameof(Symbols))]
    public void ADX_ProducesVariedValues_AllSymbols(string symbol)
    {
        var bars = TestDataLoader.LoadBars(symbol, 2000);
        var adx = new AdxCalculator(14);
        double minAdx = 100, maxAdx = 0;

        foreach (var bar in bars)
        {
            adx.UpdateFromCandle(bar.High, bar.Low, bar.Close);
            if (adx.IsReady)
            {
                minAdx = Math.Min(minAdx, adx.CurrentAdx);
                maxAdx = Math.Max(maxAdx, adx.CurrentAdx);
            }
        }

        Assert.That(maxAdx - minAdx, Is.GreaterThan(5),
            $"{symbol}: ADX should vary by at least 5 over 2000 bars (range: {minAdx:F1}-{maxAdx:F1})");
    }

    // ========================================================================
    // ATR Tests
    // ========================================================================

    [TestCaseSource(nameof(Symbols))]
    public void ATR_AlwaysPositive_AllSymbols(string symbol)
    {
        var bars = TestDataLoader.LoadBars(symbol, 300);
        var atr = new AtrCalculator(14);

        foreach (var bar in bars)
        {
            atr.UpdateFromCandle(bar.High, bar.Low, bar.Close);
            if (atr.IsReady)
            {
                Assert.That(atr.CurrentAtr, Is.GreaterThan(0),
                    $"{symbol}: ATR should always be positive");
            }
        }
    }

    [TestCaseSource(nameof(Symbols))]
    public void ATR_ProportionalToPrice_AllSymbols(string symbol)
    {
        var bars = TestDataLoader.LoadBars(symbol, 100);
        var atr = new AtrCalculator(14);

        foreach (var bar in bars)
            atr.UpdateFromCandle(bar.High, bar.Low, bar.Close);

        if (atr.IsReady)
        {
            var lastPrice = bars[^1].Close;
            var atrPercent = atr.GetAtrPercent(lastPrice);

            // ATR as % of price should be reasonable (< 20% per bar)
            Assert.That(atrPercent, Is.GreaterThan(0).And.LessThan(20),
                $"{symbol}: ATR% of {atrPercent:F2}% seems unusual");
        }
    }

    [Test]
    public void ATR_NVDAHigherAbsolute_ThanCCHH()
    {
        var nvdaBars = TestDataLoader.LoadBars("NVDA", 200);
        var cchhBars = TestDataLoader.LoadBars("CCHH", 200);
        var atrNvda = new AtrCalculator(14);
        var atrCchh = new AtrCalculator(14);

        foreach (var bar in nvdaBars)
            atrNvda.UpdateFromCandle(bar.High, bar.Low, bar.Close);
        foreach (var bar in cchhBars)
            atrCchh.UpdateFromCandle(bar.High, bar.Low, bar.Close);

        if (atrNvda.IsReady && atrCchh.IsReady)
        {
            // NVDA (~$100+) should have higher absolute ATR than CCHH (~$0.50)
            Assert.That(atrNvda.CurrentAtr, Is.GreaterThan(atrCchh.CurrentAtr),
                $"NVDA ATR ({atrNvda.CurrentAtr:F4}) should be > CCHH ATR ({atrCchh.CurrentAtr:F4})");
        }
    }

    [TestCaseSource(nameof(Symbols))]
    public void ATR_StopPrice_Long_BelowPrice_AllSymbols(string symbol)
    {
        var bars = TestDataLoader.LoadBars(symbol, 100);
        var atr = new AtrCalculator(14);

        foreach (var bar in bars)
            atr.UpdateFromCandle(bar.High, bar.Low, bar.Close);

        if (atr.IsReady)
        {
            var price = bars[^1].Close;
            var stopPrice = atr.CalculateStopPrice(price, isLong: true, multiplier: 2.0);
            Assert.That(stopPrice, Is.LessThan(price),
                $"{symbol}: Long stop should be below price");
        }
    }

    [TestCaseSource(nameof(Symbols))]
    public void ATR_StopPrice_Short_AbovePrice_AllSymbols(string symbol)
    {
        var bars = TestDataLoader.LoadBars(symbol, 100);
        var atr = new AtrCalculator(14);

        foreach (var bar in bars)
            atr.UpdateFromCandle(bar.High, bar.Low, bar.Close);

        if (atr.IsReady)
        {
            var price = bars[^1].Close;
            var stopPrice = atr.CalculateStopPrice(price, isLong: false, multiplier: 2.0);
            Assert.That(stopPrice, Is.GreaterThan(price),
                $"{symbol}: Short stop should be above price");
        }
    }

    // ========================================================================
    // ADX Reset
    // ========================================================================

    [TestCaseSource(nameof(Symbols))]
    public void ADX_Reset_WorksForAllSymbols(string symbol)
    {
        var bars = TestDataLoader.LoadBars(symbol, 60);
        var adx = new AdxCalculator(14);

        foreach (var bar in bars)
            adx.UpdateFromCandle(bar.High, bar.Low, bar.Close);

        adx.Reset();
        Assert.That(adx.IsReady, Is.False);
        Assert.That(adx.CurrentAdx, Is.EqualTo(0));
    }

    // ========================================================================
    // ATR Reset
    // ========================================================================

    [TestCaseSource(nameof(Symbols))]
    public void ATR_Reset_WorksForAllSymbols(string symbol)
    {
        var bars = TestDataLoader.LoadBars(symbol, 60);
        var atr = new AtrCalculator(14);

        foreach (var bar in bars)
            atr.UpdateFromCandle(bar.High, bar.Low, bar.Close);

        atr.Reset();
        Assert.That(atr.IsReady, Is.False);
        Assert.That(atr.CurrentAtr, Is.EqualTo(0));
    }
}
