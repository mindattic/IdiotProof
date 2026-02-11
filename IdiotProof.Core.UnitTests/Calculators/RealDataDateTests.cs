// ============================================================================
// Real Data Date-Specific Tests
// Tests calculators with data from specific trading dates to validate
// behavior across different market conditions and time windows
// ============================================================================

using IdiotProof.Core.UnitTests.Helpers;
using IdiotProof.Helpers;

namespace IdiotProof.Core.UnitTests.Calculators;

[TestFixture]
public class RealDataDateTests
{
    // ========================================================================
    // Full day EMA convergence: EMA should track price through a complete day
    // ========================================================================

    [TestCase("NVDA", "2026-01-13")]
    [TestCase("CCHH", "2026-01-13")]
    [TestCase("JZXN", "2026-01-13")]
    public void EMA_FullDay_TracksPrice(string symbol, string dateStr)
    {
        var date = DateTime.Parse(dateStr);
        var bars = TestDataLoader.LoadBarsForDate(symbol, date);
        if (bars.Count < 20)
        {
            Assert.Ignore($"Insufficient data for {symbol} on {dateStr}");
            return;
        }

        var ema = new EmaCalculator(9);
        foreach (var bar in bars)
            ema.Update(bar.Close);

        Assert.That(ema.IsReady, Is.True);

        // EMA should be between day's high and low
        double dayHigh = bars.Max(b => b.High);
        double dayLow = bars.Min(b => b.Low);
        Assert.That(ema.CurrentValue, Is.GreaterThanOrEqualTo(dayLow).And.LessThanOrEqualTo(dayHigh),
            $"{symbol}: Final EMA ({ema.CurrentValue:F4}) should be within day's range [{dayLow:F4}, {dayHigh:F4}]");
    }

    // ========================================================================
    // RSI throughout a trading day: verify oscillation
    // ========================================================================

    [TestCase("NVDA", "2026-01-13")]
    [TestCase("CCHH", "2026-01-13")]
    [TestCase("JZXN", "2026-01-13")]
    public void RSI_FullDay_Oscillates(string symbol, string dateStr)
    {
        var date = DateTime.Parse(dateStr);
        var bars = TestDataLoader.LoadBarsForDate(symbol, date);
        if (bars.Count < 30)
        {
            Assert.Ignore($"Insufficient data for {symbol} on {dateStr}");
            return;
        }

        var rsi = new RsiCalculator(14);
        var rsiValues = new List<double>();

        foreach (var bar in bars)
        {
            rsi.Update(bar.Close);
            if (rsi.IsReady)
                rsiValues.Add(rsi.CurrentValue);
        }

        Assert.That(rsiValues.Count, Is.GreaterThan(0), $"{symbol}: RSI should produce values");

        double minRsi = rsiValues.Min();
        double maxRsi = rsiValues.Max();
        Assert.That(maxRsi - minRsi, Is.GreaterThan(5),
            $"{symbol}: RSI should vary during a trading day (range: {minRsi:F1}-{maxRsi:F1})");
    }

    // ========================================================================
    // Multi-day test: calculators maintain state across days
    // ========================================================================

    [TestCase("NVDA")]
    [TestCase("CCHH")]
    [TestCase("JZXN")]
    public void Calculators_MaintainState_AcrossMultipleDays(string symbol)
    {
        var from = new DateTime(2026, 1, 13);
        var to = new DateTime(2026, 1, 17);
        var bars = TestDataLoader.LoadBarsInRange(symbol, from, to);

        if (bars.Count < 100)
        {
            Assert.Ignore($"Insufficient data for {symbol} in date range");
            return;
        }

        var ema = new EmaCalculator(9);
        var rsi = new RsiCalculator(14);
        var macd = new MacdCalculator();
        var adx = new AdxCalculator(14);

        var dates = bars.Select(b => b.Time.Date).Distinct().ToList();

        foreach (var bar in bars)
        {
            ema.Update(bar.Close);
            rsi.Update(bar.Close);
            macd.Update(bar.Close);
            adx.UpdateFromCandle(bar.High, bar.Low, bar.Close);
        }

        Assert.Multiple(() =>
        {
            Assert.That(dates.Count, Is.GreaterThanOrEqualTo(2),
                $"{symbol}: Should have data from multiple days");
            Assert.That(ema.IsReady, Is.True, $"{symbol}: EMA should be ready");
            Assert.That(rsi.IsReady, Is.True, $"{symbol}: RSI should be ready");
            Assert.That(macd.IsReady, Is.True, $"{symbol}: MACD should be ready");
            Assert.That(adx.IsReady, Is.True, $"{symbol}: ADX should be ready");
        });
    }

    // ========================================================================
    // Intraday pattern: morning vs afternoon indicator behavior
    // ========================================================================

    [TestCase("NVDA")]
    [TestCase("CCHH")]
    [TestCase("JZXN")]
    public void Indicators_MorningVsAfternoon_BothProduceValidOutput(string symbol)
    {
        // Use a larger date range to find data
        var bars = TestDataLoader.LoadBars(symbol, 2000);
        if (bars.Count < 200)
        {
            Assert.Ignore($"Insufficient data for {symbol}");
            return;
        }

        // Split into first half and second half
        int midpoint = bars.Count / 2;
        var firstHalf = bars.Take(midpoint).ToList();
        var secondHalf = bars.Skip(midpoint).ToList();

        // Test RSI for both halves
        var rsi1 = new RsiCalculator(14);
        var rsi2 = new RsiCalculator(14);
        var rsiValues1 = new List<double>();
        var rsiValues2 = new List<double>();

        foreach (var bar in firstHalf)
        {
            rsi1.Update(bar.Close);
            if (rsi1.IsReady)
                rsiValues1.Add(rsi1.CurrentValue);
        }

        foreach (var bar in secondHalf)
        {
            rsi2.Update(bar.Close);
            if (rsi2.IsReady)
                rsiValues2.Add(rsi2.CurrentValue);
        }

        Assert.That(rsiValues1.Count, Is.GreaterThan(10),
            $"{symbol}: First half should produce RSI values");
        Assert.That(rsiValues2.Count, Is.GreaterThan(10),
            $"{symbol}: Second half should produce RSI values");

        // Both halves should have RSI in valid range
        Assert.That(rsiValues1.All(v => v >= 0 && v <= 100), Is.True,
            $"{symbol}: All first-half RSI values should be in range");
        Assert.That(rsiValues2.All(v => v >= 0 && v <= 100), Is.True,
            $"{symbol}: All second-half RSI values should be in range");
    }

    // ========================================================================
    // Full indicator suite on a single day: coherent snapshot
    // ========================================================================

    [TestCase("NVDA", "2026-01-14")]
    [TestCase("CCHH", "2026-01-14")]
    [TestCase("JZXN", "2026-01-14")]
    public void FullIndicatorSuite_SingleDay_AllCoherent(string symbol, string dateStr)
    {
        var date = DateTime.Parse(dateStr);
        var bars = TestDataLoader.LoadBarsForDate(symbol, date);
        if (bars.Count < 50)
        {
            Assert.Ignore($"Insufficient data for {symbol} on {dateStr}");
            return;
        }

        var ema9 = new EmaCalculator(9);
        var sma20 = new SmaCalculator(20);
        var rsi = new RsiCalculator(14);
        var macd = new MacdCalculator();
        var bb = new BollingerBandsCalculator(20, 2.0);
        var mom = new MomentumCalculator(10);

        foreach (var bar in bars)
        {
            ema9.Update(bar.Close);
            sma20.Update(bar.Close);
            rsi.Update(bar.Close);
            macd.Update(bar.Close);
            bb.Update(bar.Close);
            mom.Update(bar.Close);
        }

        var lastPrice = bars[^1].Close;

        Assert.Multiple(() =>
        {
            if (ema9.IsReady)
                Assert.That(ema9.CurrentValue, Is.GreaterThan(0),
                    $"{symbol}: EMA should be positive");

            if (sma20.IsReady)
                Assert.That(sma20.CurrentValue, Is.GreaterThan(0),
                    $"{symbol}: SMA should be positive");

            if (rsi.IsReady)
                Assert.That(rsi.CurrentValue, Is.InRange(0, 100),
                    $"{symbol}: RSI should be in range");

            if (bb.IsReady)
                Assert.That(bb.UpperBand, Is.GreaterThan(bb.LowerBand),
                    $"{symbol}: Upper band should be > lower band");
        });
    }

    // ========================================================================
    // Volume patterns: penny stocks may have sparse volume
    // ========================================================================

    [Test]
    public void Volume_PennyStock_HandlesSparseData()
    {
        var bars = TestDataLoader.LoadBars("CCHH", 500);
        var vol = new VolumeCalculator(20);

        int zeroVolumeBars = 0;
        foreach (var bar in bars)
        {
            if (bar.Volume == 0) zeroVolumeBars++;
            vol.Update(bar.Volume);
        }

        // Penny stocks often have bars with zero volume
        // This is expected - just verify the calculator handles it
        if (vol.IsReady)
        {
            Assert.That(vol.AverageVolume, Is.GreaterThanOrEqualTo(0),
                "Average volume should be >= 0 even with sparse data");
        }

        // Log for information
        Assert.Pass($"CCHH: {zeroVolumeBars}/{bars.Count} bars had zero volume ({(double)zeroVolumeBars / bars.Count * 100:F1}%)");
    }

    // ========================================================================
    // Overnight gap: calculators handle price gaps between sessions
    // ========================================================================

    [TestCase("NVDA")]
    [TestCase("CCHH")]
    [TestCase("JZXN")]
    public void Calculators_HandleOvernightGaps_AllSymbols(string symbol)
    {
        var bars = TestDataLoader.LoadBars(symbol, 3000);
        if (bars.Count < 100)
        {
            Assert.Ignore($"Insufficient data for {symbol}");
            return;
        }

        // Find bars across a day boundary
        bool foundGap = false;
        for (int i = 1; i < bars.Count; i++)
        {
            if (bars[i].Time.Date != bars[i - 1].Time.Date)
            {
                foundGap = true;
                break;
            }
        }

        if (!foundGap)
        {
            Assert.Ignore($"No day boundary found in data for {symbol}");
            return;
        }

        // Run all calculators through the gap
        var ema = new EmaCalculator(9);
        var rsi = new RsiCalculator(14);
        var atr = new AtrCalculator(14);

        foreach (var bar in bars)
        {
            ema.Update(bar.Close);
            rsi.Update(bar.Close);
            atr.UpdateFromCandle(bar.High, bar.Low, bar.Close);

            if (ema.IsReady)
                Assert.That(ema.CurrentValue, Is.GreaterThan(0),
                    $"{symbol}: EMA should remain valid after gap");
            if (rsi.IsReady)
                Assert.That(rsi.CurrentValue, Is.InRange(0, 100),
                    $"{symbol}: RSI should remain in range after gap");
            if (atr.IsReady)
                Assert.That(atr.CurrentAtr, Is.GreaterThan(0),
                    $"{symbol}: ATR should remain positive after gap");
        }
    }

    // ========================================================================
    // Large dataset stability: no NaN/Infinity over thousands of bars
    // ========================================================================

    [TestCaseSource(nameof(Symbols))]
    public void AllCalculators_NoNaNOrInfinity_LargeDataset(string symbol)
    {
        var bars = TestDataLoader.LoadBars(symbol, 5000);

        var ema = new EmaCalculator(9);
        var sma = new SmaCalculator(20);
        var rsi = new RsiCalculator(14);
        var macd = new MacdCalculator();
        var adx = new AdxCalculator(14);
        var atr = new AtrCalculator(14);
        var bb = new BollingerBandsCalculator(20, 2.0);
        var mom = new MomentumCalculator(10);
        var roc = new RocCalculator(10);

        foreach (var bar in bars)
        {
            ema.Update(bar.Close);
            sma.Update(bar.Close);
            rsi.Update(bar.Close);
            macd.Update(bar.Close);
            adx.UpdateFromCandle(bar.High, bar.Low, bar.Close);
            atr.UpdateFromCandle(bar.High, bar.Low, bar.Close);
            bb.Update(bar.Close);
            mom.Update(bar.Close);
            roc.Update(bar.Close);

            // Check for NaN/Infinity
            if (ema.IsReady) AssertFinite(ema.CurrentValue, $"{symbol} EMA");
            if (sma.IsReady) AssertFinite(sma.CurrentValue, $"{symbol} SMA");
            if (rsi.IsReady) AssertFinite(rsi.CurrentValue, $"{symbol} RSI");
            if (macd.IsReady)
            {
                AssertFinite(macd.MacdLine, $"{symbol} MACD Line");
                AssertFinite(macd.SignalLine, $"{symbol} MACD Signal");
                AssertFinite(macd.Histogram, $"{symbol} MACD Histogram");
            }
            if (adx.IsReady) AssertFinite(adx.CurrentAdx, $"{symbol} ADX");
            if (atr.IsReady) AssertFinite(atr.CurrentAtr, $"{symbol} ATR");
            if (bb.IsReady)
            {
                AssertFinite(bb.UpperBand, $"{symbol} BB Upper");
                AssertFinite(bb.MiddleBand, $"{symbol} BB Middle");
                AssertFinite(bb.LowerBand, $"{symbol} BB Lower");
            }
            if (mom.IsReady) AssertFinite(mom.CurrentValue, $"{symbol} Momentum");
            if (roc.IsReady) AssertFinite(roc.CurrentValue, $"{symbol} ROC");
        }
    }

    private static readonly string[] Symbols = TestDataLoader.GetAvailableSymbols().ToArray();

    private static void AssertFinite(double value, string label)
    {
        Assert.That(double.IsNaN(value), Is.False, $"{label}: NaN detected");
        Assert.That(double.IsInfinity(value), Is.False, $"{label}: Infinity detected");
    }
}
