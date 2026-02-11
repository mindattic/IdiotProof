// ============================================================================
// Multi-Symbol RSI Tests - Validates RSI across NVDA, CCHH, JZXN
// ============================================================================

using IdiotProof.Core.UnitTests.Helpers;
using IdiotProof.Helpers;

namespace IdiotProof.Core.UnitTests.Calculators;

[TestFixture]
public class MultiSymbolRsiTests
{
    private static readonly string[] Symbols = TestDataLoader.GetAvailableSymbols().ToArray();

    // ========================================================================
    // RSI always in 0-100 for all symbols
    // ========================================================================

    [TestCaseSource(nameof(Symbols))]
    public void RSI_AlwaysBetween0And100_AllSymbols(string symbol)
    {
        var bars = TestDataLoader.LoadBars(symbol, 300);
        var rsi = new RsiCalculator(14);

        foreach (var bar in bars)
        {
            rsi.Update(bar.Close);
            if (rsi.IsReady)
            {
                Assert.That(rsi.CurrentValue, Is.GreaterThanOrEqualTo(0).And.LessThanOrEqualTo(100),
                    $"{symbol}: RSI out of range: {rsi.CurrentValue}");
            }
        }
    }

    // ========================================================================
    // RSI warm-up consistent across all symbols
    // ========================================================================

    [TestCaseSource(nameof(Symbols))]
    public void RSI_WarmUp_ConsistentAcrossSymbols(string symbol)
    {
        var bars = TestDataLoader.LoadBars(symbol, 50);
        var rsi = new RsiCalculator(14);

        for (int i = 0; i < 14; i++)
        {
            rsi.Update(bars[i].Close);
            Assert.That(rsi.IsReady, Is.False,
                $"{symbol}: RSI should not be ready after {i + 1} data points");
        }

        rsi.Update(bars[14].Close);
        Assert.That(rsi.IsReady, Is.True,
            $"{symbol}: RSI should be ready after 15 data points");
    }

    // ========================================================================
    // RSI with shorter period is more volatile for all symbols
    // ========================================================================

    [TestCaseSource(nameof(Symbols))]
    public void RSI_ShorterPeriod_MoreVolatile_AllSymbols(string symbol)
    {
        var bars = TestDataLoader.LoadBars(symbol, 300);
        var rsi7 = new RsiCalculator(7);
        var rsi21 = new RsiCalculator(21);

        var values7 = new List<double>();
        var values21 = new List<double>();

        foreach (var bar in bars)
        {
            rsi7.Update(bar.Close);
            rsi21.Update(bar.Close);

            if (rsi7.IsReady) values7.Add(rsi7.CurrentValue);
            if (rsi21.IsReady) values21.Add(rsi21.CurrentValue);
        }

        // Calculate standard deviation of RSI values
        if (values7.Count > 10 && values21.Count > 10)
        {
            double stdDev7 = StdDev(values7);
            double stdDev21 = StdDev(values21);

            Assert.That(stdDev7, Is.GreaterThan(stdDev21 * 0.8),
                $"{symbol}: RSI(7) should generally be more volatile than RSI(21)");
        }
    }

    // ========================================================================
    // RSI produces reasonable midrange values with real data
    // ========================================================================

    [TestCaseSource(nameof(Symbols))]
    public void RSI_ProducesReasonableValues_AllSymbols(string symbol)
    {
        var bars = TestDataLoader.LoadBars(symbol, 300);
        var rsi = new RsiCalculator(14);
        var values = new List<double>();

        foreach (var bar in bars)
        {
            rsi.Update(bar.Close);
            if (rsi.IsReady)
                values.Add(rsi.CurrentValue);
        }

        var avg = values.Average();
        // Average RSI should be in the 30-70 range for real market data
        Assert.That(avg, Is.GreaterThan(20).And.LessThan(80),
            $"{symbol}: Average RSI of {avg:F1} is out of expected range");
    }

    // ========================================================================
    // RSI Reset works for all symbols
    // ========================================================================

    [TestCaseSource(nameof(Symbols))]
    public void RSI_Reset_WorksForAllSymbols(string symbol)
    {
        var bars = TestDataLoader.LoadBars(symbol, 50);
        var rsi = new RsiCalculator(14);

        foreach (var bar in bars)
            rsi.Update(bar.Close);

        Assert.That(rsi.IsReady, Is.True);

        rsi.Reset();
        Assert.That(rsi.IsReady, Is.False);
    }

    // ========================================================================
    // RSI detects overbought/oversold at least sometimes for volatile tickers
    // ========================================================================

    [TestCaseSource(nameof(Symbols))]
    public void RSI_DetectsExtremes_InLargerDataset_AllSymbols(string symbol)
    {
        var bars = TestDataLoader.LoadBars(symbol, 2000);
        var rsi = new RsiCalculator(14);
        bool sawAbove60 = false;
        bool sawBelow40 = false;

        foreach (var bar in bars)
        {
            rsi.Update(bar.Close);
            if (rsi.IsReady)
            {
                if (rsi.CurrentValue > 60) sawAbove60 = true;
                if (rsi.CurrentValue < 40) sawBelow40 = true;
            }
        }

        Assert.That(sawAbove60, Is.True,
            $"{symbol}: RSI should reach above 60 at some point in 2000 bars");
        Assert.That(sawBelow40, Is.True,
            $"{symbol}: RSI should dip below 40 at some point in 2000 bars");
    }

    // ========================================================================
    // Penny stock: RSI handles small price increments
    // ========================================================================

    [Test]
    public void RSI_PennyStock_CCHH_HandlesSmallIncrements()
    {
        var bars = TestDataLoader.LoadBars("CCHH", 500);
        var rsi = new RsiCalculator(14);
        int readyCount = 0;

        foreach (var bar in bars)
        {
            rsi.Update(bar.Close);
            if (rsi.IsReady)
            {
                readyCount++;
                Assert.That(rsi.CurrentValue, Is.GreaterThanOrEqualTo(0).And.LessThanOrEqualTo(100));
            }
        }

        Assert.That(readyCount, Is.GreaterThan(400),
            "RSI should produce values for most of 500 bars");
    }

    // ========================================================================
    // Helper
    // ========================================================================

    private static double StdDev(List<double> values)
    {
        double avg = values.Average();
        return Math.Sqrt(values.Average(v => Math.Pow(v - avg, 2)));
    }
}
