// ============================================================================
// Multi-Symbol Bollinger/CCI/Stochastic/WilliamsR Tests
// Validates oscillator-type indicators across all available symbols
// ============================================================================

using IdiotProof.Core.UnitTests.Helpers;
using IdiotProof.Helpers;

namespace IdiotProof.Core.UnitTests.Calculators;

[TestFixture]
public class MultiSymbolOscillatorTests
{
    private static readonly string[] Symbols = TestDataLoader.GetAvailableSymbols().ToArray();

    // ========================================================================
    // Bollinger Bands
    // ========================================================================

    [TestCaseSource(nameof(Symbols))]
    public void Bollinger_BandOrdering_AllSymbols(string symbol)
    {
        var bars = TestDataLoader.LoadBars(symbol, 200);
        var bb = new BollingerBandsCalculator(20, 2.0);

        foreach (var bar in bars)
        {
            bb.Update(bar.Close);
            if (bb.IsReady)
            {
                Assert.That(bb.UpperBand, Is.GreaterThanOrEqualTo(bb.MiddleBand),
                    $"{symbol}: Upper band should be >= middle");
                Assert.That(bb.MiddleBand, Is.GreaterThanOrEqualTo(bb.LowerBand),
                    $"{symbol}: Middle band should be >= lower");
            }
        }
    }

    [TestCaseSource(nameof(Symbols))]
    public void Bollinger_BandwidthAlwaysPositive_AllSymbols(string symbol)
    {
        var bars = TestDataLoader.LoadBars(symbol, 200);
        var bb = new BollingerBandsCalculator(20, 2.0);

        foreach (var bar in bars)
        {
            bb.Update(bar.Close);
            if (bb.IsReady)
            {
                Assert.That(bb.Bandwidth, Is.GreaterThanOrEqualTo(0),
                    $"{symbol}: Bandwidth should be >= 0");
            }
        }
    }

    [TestCaseSource(nameof(Symbols))]
    public void Bollinger_PercentB_VariesAcrossRange_AllSymbols(string symbol)
    {
        var bars = TestDataLoader.LoadBars(symbol, 2000);
        var bb = new BollingerBandsCalculator(20, 2.0);
        double minPctB = double.MaxValue, maxPctB = double.MinValue;

        foreach (var bar in bars)
        {
            bb.Update(bar.Close);
            if (bb.IsReady)
            {
                minPctB = Math.Min(minPctB, bb.PercentB);
                maxPctB = Math.Max(maxPctB, bb.PercentB);
            }
        }

        Assert.That(maxPctB - minPctB, Is.GreaterThan(0.3),
            $"{symbol}: %B should vary meaningfully (range: {minPctB:F2} to {maxPctB:F2})");
    }

    [Test]
    public void Bollinger_PennyStock_BandsAreNarrow()
    {
        var bars = TestDataLoader.LoadBars("CCHH", 200);
        var bb = new BollingerBandsCalculator(20, 2.0);

        foreach (var bar in bars)
            bb.Update(bar.Close);

        if (bb.IsReady)
        {
            var bandWidth = bb.UpperBand - bb.LowerBand;
            // For a ~$0.50 stock, bands should be proportionally narrow
            Assert.That(bandWidth, Is.LessThan(1.0),
                $"CCHH band width ({bandWidth:F4}) should be < $1 for a penny stock");
            Assert.That(bandWidth, Is.GreaterThan(0),
                "Band width should be positive");
        }
    }

    // ========================================================================
    // CCI
    // ========================================================================

    [TestCaseSource(nameof(Symbols))]
    public void CCI_ProducesReasonableValues_AllSymbols(string symbol)
    {
        var bars = TestDataLoader.LoadBars(symbol, 300);
        var cci = new CciCalculator(20);

        foreach (var bar in bars)
        {
            cci.Update(bar.High, bar.Low, bar.Close);
            if (cci.IsReady)
            {
                // CCI can be any value, but extreme values (> 500) are unusual
                Assert.That(cci.CurrentCci, Is.GreaterThan(-1000).And.LessThan(1000),
                    $"{symbol}: CCI extremely out of range: {cci.CurrentCci}");
            }
        }
    }

    [TestCaseSource(nameof(Symbols))]
    public void CCI_DetectsOverboughtAndOversold_AllSymbols(string symbol)
    {
        var bars = TestDataLoader.LoadBars(symbol, 2000);
        var cci = new CciCalculator(20);
        bool sawOverbought = false;
        bool sawOversold = false;

        foreach (var bar in bars)
        {
            cci.Update(bar.High, bar.Low, bar.Close);
            if (cci.IsReady)
            {
                if (cci.IsOverbought) sawOverbought = true;
                if (cci.IsOversold) sawOversold = true;
            }
        }

        Assert.That(sawOverbought, Is.True,
            $"{symbol}: CCI should detect overbought in 2000 bars");
        Assert.That(sawOversold, Is.True,
            $"{symbol}: CCI should detect oversold in 2000 bars");
    }

    [TestCaseSource(nameof(Symbols))]
    public void CCI_GetScore_InRange_AllSymbols(string symbol)
    {
        var bars = TestDataLoader.LoadBars(symbol, 100);
        var cci = new CciCalculator(20);

        foreach (var bar in bars)
        {
            cci.Update(bar.High, bar.Low, bar.Close);
            if (cci.IsReady)
            {
                var score = cci.GetScore();
                Assert.That(score, Is.GreaterThanOrEqualTo(-100).And.LessThanOrEqualTo(100),
                    $"{symbol}: CCI score out of range: {score}");
            }
        }
    }

    // ========================================================================
    // Stochastic
    // ========================================================================

    [TestCaseSource(nameof(Symbols))]
    public void Stochastic_PercentK_0To100_AllSymbols(string symbol)
    {
        var bars = TestDataLoader.LoadBars(symbol, 300);
        var stoch = new StochasticCalculator(14, 3);

        foreach (var bar in bars)
        {
            stoch.Update(bar.High, bar.Low, bar.Close);
            if (stoch.IsReady)
            {
                Assert.That(stoch.PercentK, Is.GreaterThanOrEqualTo(0).And.LessThanOrEqualTo(100),
                    $"{symbol}: %K out of range: {stoch.PercentK}");
            }
        }
    }

    [TestCaseSource(nameof(Symbols))]
    public void Stochastic_DetectsExtremes_AllSymbols(string symbol)
    {
        var bars = TestDataLoader.LoadBars(symbol, 2000);
        var stoch = new StochasticCalculator(14, 3);
        bool sawOverbought = false;
        bool sawOversold = false;

        foreach (var bar in bars)
        {
            stoch.Update(bar.High, bar.Low, bar.Close);
            if (stoch.IsReady)
            {
                if (stoch.IsOverbought) sawOverbought = true;
                if (stoch.IsOversold) sawOversold = true;
            }
        }

        Assert.That(sawOverbought, Is.True,
            $"{symbol}: Stochastic should detect overbought in 2000 bars");
        Assert.That(sawOversold, Is.True,
            $"{symbol}: Stochastic should detect oversold in 2000 bars");
    }

    [TestCaseSource(nameof(Symbols))]
    public void Stochastic_GetScore_InRange_AllSymbols(string symbol)
    {
        var bars = TestDataLoader.LoadBars(symbol, 100);
        var stoch = new StochasticCalculator(14, 3);

        foreach (var bar in bars)
        {
            stoch.Update(bar.High, bar.Low, bar.Close);
            if (stoch.IsReady)
            {
                var score = stoch.GetScore();
                Assert.That(score, Is.GreaterThanOrEqualTo(-100).And.LessThanOrEqualTo(100),
                    $"{symbol}: Stochastic score out of range: {score}");
            }
        }
    }

    // ========================================================================
    // Williams %R
    // ========================================================================

    [TestCaseSource(nameof(Symbols))]
    public void WilliamsR_AlwaysInRange_AllSymbols(string symbol)
    {
        var bars = TestDataLoader.LoadBars(symbol, 300);
        var wr = new WilliamsRCalculator(14);

        foreach (var bar in bars)
        {
            wr.Update(bar.High, bar.Low, bar.Close);
            if (wr.IsReady)
            {
                Assert.That(wr.CurrentValue, Is.GreaterThanOrEqualTo(-100).And.LessThanOrEqualTo(0),
                    $"{symbol}: Williams %R out of range: {wr.CurrentValue}");
            }
        }
    }

    [TestCaseSource(nameof(Symbols))]
    public void WilliamsR_DetectsExtremes_AllSymbols(string symbol)
    {
        var bars = TestDataLoader.LoadBars(symbol, 2000);
        var wr = new WilliamsRCalculator(14);
        bool sawOverbought = false;
        bool sawOversold = false;

        foreach (var bar in bars)
        {
            wr.Update(bar.High, bar.Low, bar.Close);
            if (wr.IsReady)
            {
                if (wr.IsOverbought) sawOverbought = true;
                if (wr.IsOversold) sawOversold = true;
            }
        }

        Assert.That(sawOverbought, Is.True,
            $"{symbol}: Williams %R should detect overbought in 2000 bars");
        Assert.That(sawOversold, Is.True,
            $"{symbol}: Williams %R should detect oversold in 2000 bars");
    }

    [TestCaseSource(nameof(Symbols))]
    public void WilliamsR_GetScore_InRange_AllSymbols(string symbol)
    {
        var bars = TestDataLoader.LoadBars(symbol, 100);
        var wr = new WilliamsRCalculator(14);

        foreach (var bar in bars)
        {
            wr.Update(bar.High, bar.Low, bar.Close);
            if (wr.IsReady)
            {
                var score = wr.GetScore();
                Assert.That(score, Is.GreaterThanOrEqualTo(-100).And.LessThanOrEqualTo(100),
                    $"{symbol}: Williams %R score out of range: {score}");
            }
        }
    }
}
