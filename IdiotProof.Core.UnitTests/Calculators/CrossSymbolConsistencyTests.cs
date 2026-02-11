// ============================================================================
// Cross-Symbol Consistency Tests
// Validates that all calculators behave consistently regardless of price scale
// Tests calculator convergence, relative behavior, and data integrity
// ============================================================================

using IdiotProof.Core.UnitTests.Helpers;
using IdiotProof.Helpers;

namespace IdiotProof.Core.UnitTests.Calculators;

[TestFixture]
public class CrossSymbolConsistencyTests
{
    private static readonly string[] Symbols = TestDataLoader.GetAvailableSymbols().ToArray();

    // ========================================================================
    // All calculators become ready for all symbols
    // ========================================================================

    [TestCaseSource(nameof(Symbols))]
    public void AllCalculators_BecomeReady_AllSymbols(string symbol)
    {
        var bars = TestDataLoader.LoadBars(symbol, 200);

        var ema9 = new EmaCalculator(9);
        var ema21 = new EmaCalculator(21);
        var sma20 = new SmaCalculator(20);
        var rsi = new RsiCalculator(14);
        var macd = new MacdCalculator();
        var adx = new AdxCalculator(14);
        var atr = new AtrCalculator(14);
        var bb = new BollingerBandsCalculator(20, 2.0);
        var cci = new CciCalculator(20);
        var mom = new MomentumCalculator(10);
        var roc = new RocCalculator(10);
        var vol = new VolumeCalculator(20);
        var obv = new ObvCalculator(20);
        var stoch = new StochasticCalculator(14, 3);
        var wr = new WilliamsRCalculator(14);

        double prevClose = 0;
        int nonZeroVolumeCount = 0;
        foreach (var bar in bars)
        {
            ema9.Update(bar.Close);
            ema21.Update(bar.Close);
            sma20.Update(bar.Close);
            rsi.Update(bar.Close);
            macd.Update(bar.Close);
            adx.UpdateFromCandle(bar.High, bar.Low, bar.Close);
            atr.UpdateFromCandle(bar.High, bar.Low, bar.Close);
            bb.Update(bar.Close);
            cci.Update(bar.High, bar.Low, bar.Close);
            mom.Update(bar.Close);
            roc.Update(bar.Close);
            vol.Update(bar.Volume);
            if (bar.Volume > 0) nonZeroVolumeCount++;
            if (prevClose > 0)
                obv.Update(bar.Close, bar.Volume);
            stoch.Update(bar.High, bar.Low, bar.Close);
            wr.Update(bar.High, bar.Low, bar.Close);
            prevClose = bar.Close;
        }

        Assert.Multiple(() =>
        {
            Assert.That(ema9.IsReady, Is.True, $"{symbol}: EMA(9) not ready");
            Assert.That(ema21.IsReady, Is.True, $"{symbol}: EMA(21) not ready");
            Assert.That(sma20.IsReady, Is.True, $"{symbol}: SMA(20) not ready");
            Assert.That(rsi.IsReady, Is.True, $"{symbol}: RSI not ready");
            Assert.That(macd.IsReady, Is.True, $"{symbol}: MACD not ready");
            Assert.That(adx.IsReady, Is.True, $"{symbol}: ADX not ready");
            Assert.That(atr.IsReady, Is.True, $"{symbol}: ATR not ready");
            Assert.That(bb.IsReady, Is.True, $"{symbol}: Bollinger not ready");
            Assert.That(cci.IsReady, Is.True, $"{symbol}: CCI not ready");
            Assert.That(mom.IsReady, Is.True, $"{symbol}: Momentum not ready");
            Assert.That(roc.IsReady, Is.True, $"{symbol}: ROC not ready");
            // Volume may not be ready for penny stocks with mostly zero-volume bars
            if (nonZeroVolumeCount >= 20)
                Assert.That(vol.IsReady, Is.True, $"{symbol}: Volume not ready");
            Assert.That(stoch.IsReady, Is.True, $"{symbol}: Stochastic not ready");
            Assert.That(wr.IsReady, Is.True, $"{symbol}: Williams %R not ready");
        });
    }

    // ========================================================================
    // All range-bounded indicators stay in range for all symbols
    // ========================================================================

    [TestCaseSource(nameof(Symbols))]
    public void AllRangeBoundedIndicators_StayInRange_AllSymbols(string symbol)
    {
        var bars = TestDataLoader.LoadBars(symbol, 500);

        var rsi = new RsiCalculator(14);
        var adx = new AdxCalculator(14);
        var stoch = new StochasticCalculator(14, 3);
        var wr = new WilliamsRCalculator(14);

        foreach (var bar in bars)
        {
            rsi.Update(bar.Close);
            adx.UpdateFromCandle(bar.High, bar.Low, bar.Close);
            stoch.Update(bar.High, bar.Low, bar.Close);
            wr.Update(bar.High, bar.Low, bar.Close);

            if (rsi.IsReady)
                Assert.That(rsi.CurrentValue, Is.InRange(0, 100),
                    $"{symbol}: RSI out of range");
            if (adx.IsReady)
            {
                Assert.That(adx.CurrentAdx, Is.InRange(0, 100),
                    $"{symbol}: ADX out of range");
                Assert.That(adx.PlusDI, Is.GreaterThanOrEqualTo(0),
                    $"{symbol}: +DI negative");
                Assert.That(adx.MinusDI, Is.GreaterThanOrEqualTo(0),
                    $"{symbol}: -DI negative");
            }
            if (stoch.IsReady)
                Assert.That(stoch.PercentK, Is.InRange(0, 100),
                    $"{symbol}: Stochastic %K out of range");
            if (wr.IsReady)
                Assert.That(wr.CurrentValue, Is.InRange(-100, 0),
                    $"{symbol}: Williams %R out of range");
        }
    }

    // ========================================================================
    // EMA and SMA converge for all symbols
    // ========================================================================

    [TestCaseSource(nameof(Symbols))]
    public void EmaAndSma_SamePeriod_ConvergeOverTime_AllSymbols(string symbol)
    {
        var bars = TestDataLoader.LoadBars(symbol, 300);
        var ema = new EmaCalculator(20);
        var sma = new SmaCalculator(20);

        foreach (var bar in bars)
        {
            ema.Update(bar.Close);
            sma.Update(bar.Close);
        }

        if (ema.IsReady && sma.IsReady)
        {
            // EMA and SMA with same period should be in the same ballpark
            var pctDiff = Math.Abs(ema.CurrentValue - sma.CurrentValue) / sma.CurrentValue * 100;
            Assert.That(pctDiff, Is.LessThan(5),
                $"{symbol}: EMA(20) and SMA(20) should be within 5% of each other (diff: {pctDiff:F2}%)");
        }
    }

    // ========================================================================
    // ATR percentage similar across stocks (normalized by price)
    // ========================================================================

    [Test]
    public void ATR_Percent_SimilarMagnitude_AcrossStocks()
    {
        var atrPercents = new Dictionary<string, double>();

        foreach (var symbol in Symbols)
        {
            var bars = TestDataLoader.LoadBars(symbol, 200);
            var atr = new AtrCalculator(14);

            foreach (var bar in bars)
                atr.UpdateFromCandle(bar.High, bar.Low, bar.Close);

            if (atr.IsReady)
            {
                var lastPrice = bars[^1].Close;
                atrPercents[symbol] = atr.GetAtrPercent(lastPrice);
            }
        }

        // All ATR percentages should be within a reasonable range for 1-min bars
        foreach (var (symbol, pct) in atrPercents)
        {
            Assert.That(pct, Is.GreaterThan(0).And.LessThan(20),
                $"{symbol}: ATR% of {pct:F3}% is unusual for 1-min bars");
        }
    }

    // ========================================================================
    // Bollinger %B distribution similar across stocks
    // ========================================================================

    [Test]
    public void Bollinger_PercentB_DistributionSimilar_AcrossStocks()
    {
        var avgPercentB = new Dictionary<string, double>();

        foreach (var symbol in Symbols)
        {
            var bars = TestDataLoader.LoadBars(symbol, 500);
            var bb = new BollingerBandsCalculator(20, 2.0);
            var values = new List<double>();

            foreach (var bar in bars)
            {
                bb.Update(bar.Close);
                if (bb.IsReady)
                    values.Add(bb.PercentB);
            }

            if (values.Count > 0)
                avgPercentB[symbol] = values.Average();
        }

        // Average %B should be around 0.5 for all stocks (mean reversion)
        foreach (var (symbol, avg) in avgPercentB)
        {
            Assert.That(avg, Is.GreaterThan(0.1).And.LessThan(0.9),
                $"{symbol}: Average %B of {avg:F3} should be closer to 0.5");
        }
    }

    // ========================================================================
    // Score functions produce valid scores for all symbols
    // ========================================================================

    [TestCaseSource(nameof(Symbols))]
    public void AllScoreFunctions_ProduceValidScores_AllSymbols(string symbol)
    {
        var bars = TestDataLoader.LoadBars(symbol, 200);

        var sma = new SmaCalculator(20);
        var cci = new CciCalculator(20);
        var obv = new ObvCalculator(20);
        var stoch = new StochasticCalculator(14, 3);
        var wr = new WilliamsRCalculator(14);
        var bb = new BollingerBandsCalculator(20, 2.0);

        double prevClose = 0;
        foreach (var bar in bars)
        {
            sma.Update(bar.Close);
            cci.Update(bar.High, bar.Low, bar.Close);
            if (prevClose > 0) obv.Update(bar.Close, bar.Volume);
            stoch.Update(bar.High, bar.Low, bar.Close);
            wr.Update(bar.High, bar.Low, bar.Close);
            bb.Update(bar.Close);
            prevClose = bar.Close;
        }

        var lastPrice = bars[^1].Close;
        Assert.Multiple(() =>
        {
            if (sma.IsReady)
                Assert.That(sma.GetScore(lastPrice), Is.InRange(-100, 100), $"{symbol}: SMA score");
            if (cci.IsReady)
                Assert.That(cci.GetScore(), Is.InRange(-100, 100), $"{symbol}: CCI score");
            Assert.That(obv.GetScore(), Is.InRange(-100, 100), $"{symbol}: OBV score");
            if (stoch.IsReady)
                Assert.That(stoch.GetScore(), Is.InRange(-100, 100), $"{symbol}: Stochastic score");
            if (wr.IsReady)
                Assert.That(wr.GetScore(), Is.InRange(-100, 100), $"{symbol}: Williams %R score");
            if (bb.IsReady)
                Assert.That(bb.GetScore(), Is.InRange(-100, 100), $"{symbol}: Bollinger score");
        });
    }

    // ========================================================================
    // Data integrity: bars are chronologically ordered for all symbols
    // ========================================================================

    [TestCaseSource(nameof(Symbols))]
    public void DataIntegrity_BarsChronological_AllSymbols(string symbol)
    {
        var bars = TestDataLoader.LoadBars(symbol, 1000);

        for (int i = 1; i < bars.Count; i++)
        {
            Assert.That(bars[i].Time, Is.GreaterThanOrEqualTo(bars[i - 1].Time),
                $"{symbol}: Bar {i} at {bars[i].Time} is before bar {i - 1} at {bars[i - 1].Time}");
        }
    }

    // ========================================================================
    // Data integrity: OHLC relationships for all symbols
    // ========================================================================

    [TestCaseSource(nameof(Symbols))]
    public void DataIntegrity_OHLC_Relationships_AllSymbols(string symbol)
    {
        var bars = TestDataLoader.LoadBars(symbol, 1000);

        foreach (var bar in bars)
        {
            Assert.That(bar.High, Is.GreaterThanOrEqualTo(bar.Low),
                $"{symbol}: High ({bar.High}) < Low ({bar.Low}) at {bar.Time}");
            Assert.That(bar.High, Is.GreaterThanOrEqualTo(bar.Open),
                $"{symbol}: High ({bar.High}) < Open ({bar.Open}) at {bar.Time}");
            Assert.That(bar.High, Is.GreaterThanOrEqualTo(bar.Close),
                $"{symbol}: High ({bar.High}) < Close ({bar.Close}) at {bar.Time}");
            Assert.That(bar.Low, Is.LessThanOrEqualTo(bar.Open),
                $"{symbol}: Low ({bar.Low}) > Open ({bar.Open}) at {bar.Time}");
            Assert.That(bar.Low, Is.LessThanOrEqualTo(bar.Close),
                $"{symbol}: Low ({bar.Low}) > Close ({bar.Close}) at {bar.Time}");
        }
    }

    // ========================================================================
    // Data integrity: prices are positive for all symbols
    // ========================================================================

    [TestCaseSource(nameof(Symbols))]
    public void DataIntegrity_PricesPositive_AllSymbols(string symbol)
    {
        var bars = TestDataLoader.LoadBars(symbol, 1000);

        foreach (var bar in bars)
        {
            Assert.That(bar.Open, Is.GreaterThan(0), $"{symbol}: Non-positive open at {bar.Time}");
            Assert.That(bar.High, Is.GreaterThan(0), $"{symbol}: Non-positive high at {bar.Time}");
            Assert.That(bar.Low, Is.GreaterThan(0), $"{symbol}: Non-positive low at {bar.Time}");
            Assert.That(bar.Close, Is.GreaterThan(0), $"{symbol}: Non-positive close at {bar.Time}");
        }
    }

    // ========================================================================
    // TestDataLoader verifies available symbols
    // ========================================================================

    [Test]
    public void TestDataLoader_FindsAllExpectedSymbols()
    {
        var available = TestDataLoader.GetAvailableSymbols();

        // Should find at least one symbol with history data
        Assert.That(available.Count, Is.GreaterThanOrEqualTo(1),
            "Should find at least one symbol with history data");

        // Every discovered symbol should be loadable
        foreach (var symbol in available)
        {
            var bars = TestDataLoader.LoadBars(symbol, 10);
            Assert.That(bars.Count, Is.GreaterThan(0),
                $"Symbol {symbol} was discovered but has no loadable bars");
        }
    }

    [Test]
    public void TestDataLoader_LoadBarsForDate_ReturnsData()
    {
        // NVDA data starts 2026-01-12
        var bars = TestDataLoader.LoadBarsForDate("NVDA", new DateTime(2026, 1, 13));
        Assert.That(bars.Count, Is.GreaterThan(0),
            "Should find NVDA bars for 2026-01-13");
        Assert.That(bars.All(b => b.Time.Date == new DateTime(2026, 1, 13).Date), Is.True,
            "All returned bars should be from the requested date");
    }

    [Test]
    public void TestDataLoader_LoadBarsInRange_FiltersCorrectly()
    {
        var from = new DateTime(2026, 1, 13);
        var to = new DateTime(2026, 1, 15);
        var bars = TestDataLoader.LoadBarsInRange("NVDA", from, to);

        Assert.That(bars.Count, Is.GreaterThan(0), "Should find bars in range");
        Assert.That(bars.All(b => b.Time >= from && b.Time <= to), Is.True,
            "All returned bars should be within the requested range");
    }
}
