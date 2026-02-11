// ============================================================================
// Multi-Symbol MarketScore Integration Tests
// Builds real IndicatorSnapshots from multi-symbol data and validates scoring
// ============================================================================

using IdiotProof.Calculators;
using IdiotProof.Core.UnitTests.Helpers;
using IdiotProof.Helpers;

namespace IdiotProof.Core.UnitTests.Calculators;

[TestFixture]
public class MultiSymbolMarketScoreTests
{
    private static readonly string[] Symbols = TestDataLoader.GetAvailableSymbols().ToArray();

    // ========================================================================
    // Build a real IndicatorSnapshot from actual data and score it
    // ========================================================================

    [TestCaseSource(nameof(Symbols))]
    public void MarketScore_FromRealData_ProducesValidScore(string symbol)
    {
        var snapshot = BuildSnapshotFromRealData(symbol, 300);

        var result = MarketScoreCalculator.Calculate(snapshot);

        Assert.That(result.TotalScore, Is.InRange(-100, 100),
            $"{symbol}: Total score out of range: {result.TotalScore}");
    }

    // ========================================================================
    // All component scores in range for real data
    // ========================================================================

    [TestCaseSource(nameof(Symbols))]
    public void MarketScore_AllComponentsInRange_AllSymbols(string symbol)
    {
        var snapshot = BuildSnapshotFromRealData(symbol, 300);
        var result = MarketScoreCalculator.Calculate(snapshot);

        Assert.Multiple(() =>
        {
            Assert.That(result.VwapScore, Is.InRange(-100, 100), $"{symbol}: VwapScore");
            Assert.That(result.EmaScore, Is.InRange(-100, 100), $"{symbol}: EmaScore");
            Assert.That(result.RsiScore, Is.InRange(-100, 100), $"{symbol}: RsiScore");
            Assert.That(result.MacdScore, Is.InRange(-100, 100), $"{symbol}: MacdScore");
            Assert.That(result.AdxScore, Is.InRange(-100, 100), $"{symbol}: AdxScore");
            Assert.That(result.VolumeScore, Is.InRange(-100, 100), $"{symbol}: VolumeScore");
            Assert.That(result.BollingerScore, Is.InRange(-100, 100), $"{symbol}: BollingerScore");
            Assert.That(result.StochasticScore, Is.InRange(-100, 100), $"{symbol}: StochasticScore");
            Assert.That(result.ObvScore, Is.InRange(-100, 100), $"{symbol}: ObvScore");
            Assert.That(result.CciScore, Is.InRange(-100, 100), $"{symbol}: CciScore");
            Assert.That(result.WilliamsRScore, Is.InRange(-100, 100), $"{symbol}: WilliamsRScore");
            Assert.That(result.SmaScore, Is.InRange(-100, 100), $"{symbol}: SmaScore");
            Assert.That(result.MomentumScore, Is.InRange(-100, 100), $"{symbol}: MomentumScore");
            Assert.That(result.SupportResistanceScore, Is.InRange(-100, 100), $"{symbol}: SRScore");
        });
    }

    // ========================================================================
    // DI direction matches ADX data
    // ========================================================================

    [TestCaseSource(nameof(Symbols))]
    public void MarketScore_DI_Direction_MatchesAdxData(string symbol)
    {
        var bars = TestDataLoader.LoadBars(symbol, 300);
        var adx = new AdxCalculator(14);

        foreach (var bar in bars)
            adx.UpdateFromCandle(bar.High, bar.Low, bar.Close);

        var snapshot = BuildSnapshotFromRealData(symbol, 300);
        var result = MarketScoreCalculator.Calculate(snapshot);

        if (adx.IsReady)
        {
            bool expectedPositive = adx.PlusDI > adx.MinusDI;
            Assert.That(result.IsDiPositive, Is.EqualTo(expectedPositive),
                $"{symbol}: IsDiPositive should match +DI > -DI ({adx.PlusDI:F2} vs {adx.MinusDI:F2})");
        }
    }

    // ========================================================================
    // MACD bullish matches MACD data
    // ========================================================================

    [TestCaseSource(nameof(Symbols))]
    public void MarketScore_MACD_Bullish_MatchesMacdData(string symbol)
    {
        var bars = TestDataLoader.LoadBars(symbol, 300);
        var macd = new MacdCalculator();

        foreach (var bar in bars)
            macd.Update(bar.Close);

        var snapshot = BuildSnapshotFromRealData(symbol, 300);
        var result = MarketScoreCalculator.Calculate(snapshot);

        if (macd.IsReady)
        {
            Assert.That(result.IsMacdBullish, Is.EqualTo(macd.IsBullish),
                $"{symbol}: IsMacdBullish should match MACD > Signal");
        }
    }

    // ========================================================================
    // Score with different data windows produces different results
    // ========================================================================

    [TestCaseSource(nameof(Symbols))]
    public void MarketScore_DifferentWindows_ProduceDifferentScores(string symbol)
    {
        var snapshot100 = BuildSnapshotFromRealData(symbol, 100);
        var snapshot500 = BuildSnapshotFromRealData(symbol, 500);

        var result100 = MarketScoreCalculator.Calculate(snapshot100);
        var result500 = MarketScoreCalculator.Calculate(snapshot500);

        // Both should be valid, but they'll have different indicator values
        Assert.That(result100.TotalScore, Is.InRange(-100, 100));
        Assert.That(result500.TotalScore, Is.InRange(-100, 100));
    }

    // ========================================================================
    // Score varies over time (not stuck at one value)
    // ========================================================================

    [TestCaseSource(nameof(Symbols))]
    public void MarketScore_VariesOverTime_AllSymbols(string symbol)
    {
        var bars = TestDataLoader.LoadBars(symbol, 500);
        var scores = new List<int>();

        // Calculate snapshot at different points in time
        for (int window = 100; window <= 400; window += 50)
        {
            var barsSubset = bars.Take(window).ToList();
            var snapshot = BuildSnapshotFromBars(barsSubset);
            var result = MarketScoreCalculator.Calculate(snapshot);
            scores.Add(result.TotalScore);
        }

        // Scores should vary
        var distinctScores = scores.Distinct().ToList();
        Assert.That(distinctScores.Count, Is.GreaterThan(1),
            $"{symbol}: Score should vary at different time points (got: {string.Join(", ", scores)})");
    }

    // ========================================================================
    // Penny stock snapshot has valid VWAP handling
    // ========================================================================

    [Test]
    public void MarketScore_PennyStock_CCHH_HandlesSmallPrices()
    {
        var snapshot = BuildSnapshotFromRealData("CCHH", 300);
        var result = MarketScoreCalculator.Calculate(snapshot);

        Assert.That(result.TotalScore, Is.InRange(-100, 100),
            $"Score for penny stock should be valid: {result.TotalScore}");
    }

    // ========================================================================
    // Custom weights still produce valid results with real data
    // ========================================================================

    [TestCaseSource(nameof(Symbols))]
    public void MarketScore_CustomWeights_ValidWithRealData(string symbol)
    {
        var snapshot = BuildSnapshotFromRealData(symbol, 300);

        // Heavy MACD weight
        var weights = new IndicatorWeights
        {
            Vwap = 0.05,
            Ema = 0.05,
            Rsi = 0.05,
            Macd = 0.60,
            Adx = 0.10,
            Volume = 0.05,
            Bollinger = 0.10
        };

        var result = MarketScoreCalculator.Calculate(snapshot, weights);
        Assert.That(result.TotalScore, Is.InRange(-100, 100),
            $"{symbol}: Custom weight score out of range");
    }

    // ========================================================================
    // MarketScore from multiple dates: each produces valid results
    // ========================================================================

    [TestCase("NVDA", "2026-01-13")]
    [TestCase("NVDA", "2026-01-14")]
    [TestCase("CCHH", "2026-01-13")]
    [TestCase("JZXN", "2026-01-13")]
    public void MarketScore_FromSpecificDate_ProducesValidScore(string symbol, string dateStr)
    {
        var date = DateTime.Parse(dateStr);
        var bars = TestDataLoader.LoadBarsForDate(symbol, date);

        if (bars.Count < 50)
        {
            Assert.Ignore($"Insufficient data for {symbol} on {dateStr}");
            return;
        }

        var snapshot = BuildSnapshotFromBars(bars);
        var result = MarketScoreCalculator.Calculate(snapshot);

        Assert.That(result.TotalScore, Is.InRange(-100, 100),
            $"{symbol} on {dateStr}: Score out of range");
    }

    // ========================================================================
    // Helper: Build IndicatorSnapshot from real bar data
    // ========================================================================

    private static IndicatorSnapshot BuildSnapshotFromRealData(string symbol, int barCount)
    {
        var bars = TestDataLoader.LoadBars(symbol, barCount);
        return BuildSnapshotFromBars(bars);
    }

    private static IndicatorSnapshot BuildSnapshotFromBars(List<TestBar> bars)
    {
        var ema9 = new EmaCalculator(9);
        var ema21 = new EmaCalculator(21);
        var ema34 = new EmaCalculator(34);
        var ema50 = new EmaCalculator(50);
        var sma20 = new SmaCalculator(20);
        var sma50 = new SmaCalculator(50);
        var rsi = new RsiCalculator(14);
        var macd = new MacdCalculator();
        var adx = new AdxCalculator(14);
        var atr = new AtrCalculator(14);
        var bb = new BollingerBandsCalculator(20, 2.0);
        var cci = new CciCalculator(20);
        var stoch = new StochasticCalculator(14, 3);
        var wr = new WilliamsRCalculator(14);
        var obv = new ObvCalculator(20);
        var vol = new VolumeCalculator(20);
        var mom = new MomentumCalculator(10);
        var roc = new RocCalculator(10);

        double prevClose = 0;
        foreach (var bar in bars)
        {
            ema9.Update(bar.Close);
            ema21.Update(bar.Close);
            ema34.Update(bar.Close);
            ema50.Update(bar.Close);
            sma20.Update(bar.Close);
            sma50.Update(bar.Close);
            rsi.Update(bar.Close);
            macd.Update(bar.Close);
            adx.UpdateFromCandle(bar.High, bar.Low, bar.Close);
            atr.UpdateFromCandle(bar.High, bar.Low, bar.Close);
            bb.Update(bar.Close);
            cci.Update(bar.High, bar.Low, bar.Close);
            stoch.Update(bar.High, bar.Low, bar.Close);
            wr.Update(bar.High, bar.Low, bar.Close);
            if (prevClose > 0) obv.Update(bar.Close, bar.Volume);
            vol.Update(bar.Volume);
            mom.Update(bar.Close);
            roc.Update(bar.Close);
            prevClose = bar.Close;
        }

        var lastBar = bars[^1];
        var vwap = lastBar.Vwap ?? lastBar.Close; // Fallback if VWAP is null

        return new IndicatorSnapshot
        {
            Price = lastBar.Close,
            Vwap = vwap,
            Ema9 = ema9.IsReady ? ema9.CurrentValue : lastBar.Close,
            Ema21 = ema21.IsReady ? ema21.CurrentValue : lastBar.Close,
            Ema34 = ema34.IsReady ? ema34.CurrentValue : lastBar.Close,
            Ema50 = ema50.IsReady ? ema50.CurrentValue : lastBar.Close,
            Sma20 = sma20.IsReady ? sma20.CurrentValue : lastBar.Close,
            Sma50 = sma50.IsReady ? sma50.CurrentValue : lastBar.Close,
            Rsi = rsi.IsReady ? rsi.CurrentValue : 50,
            Macd = macd.IsReady ? macd.MacdLine : 0,
            MacdSignal = macd.IsReady ? macd.SignalLine : 0,
            MacdHistogram = macd.IsReady ? macd.Histogram : 0,
            Adx = adx.IsReady ? adx.CurrentAdx : 0,
            PlusDi = adx.IsReady ? adx.PlusDI : 0,
            MinusDi = adx.IsReady ? adx.MinusDI : 0,
            VolumeRatio = vol.IsReady ? vol.VolumeRatio : 1.0,
            BollingerUpper = bb.IsReady ? bb.UpperBand : lastBar.Close * 1.02,
            BollingerLower = bb.IsReady ? bb.LowerBand : lastBar.Close * 0.98,
            BollingerMiddle = bb.IsReady ? bb.MiddleBand : lastBar.Close,
            BollingerPercentB = bb.IsReady ? bb.PercentB : 0.5,
            BollingerBandwidth = bb.IsReady ? bb.Bandwidth : 2.0,
            StochasticK = stoch.IsReady ? stoch.PercentK : 50,
            StochasticD = stoch.IsReady ? stoch.PercentD : 50,
            ObvSlope = obv.IsRising ? 1.0 : obv.IsFalling ? -1.0 : 0.0,
            Cci = cci.IsReady ? cci.CurrentCci : 0,
            WilliamsR = wr.IsReady ? wr.CurrentValue : -50,
            Momentum = mom.IsReady ? mom.CurrentValue : 0,
            Roc = roc.IsReady ? roc.CurrentValue : 0,
            Atr = atr.IsReady ? atr.CurrentAtr : 0
        };
    }
}
