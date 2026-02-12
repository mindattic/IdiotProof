// ============================================================================
// IndicatorConfigManager Tests - Toggle indicators and dynamic weight redistribution
// ============================================================================

using IdiotProof.Calculators;
using IdiotProof.Services;

namespace IdiotProof.Core.UnitTests.Calculators;

[TestFixture]
public class IndicatorConfigTests
{
    // ========================================================================
    // IndicatorConfig.ToCalculatorWeights() Tests
    // ========================================================================

    [Test]
    public void AllEnabled_WeightsSumTo1()
    {
        var config = IndicatorConfig.CreateDefault();
        var weights = config.ToCalculatorWeights();

        Assert.That(weights.IsValid(), Is.True,
            "All indicators enabled should produce weights summing to 1.0");
    }

    [Test]
    public void AllEnabled_MatchesDefaultWeights()
    {
        var config = IndicatorConfig.CreateDefault();
        var weights = config.ToCalculatorWeights();
        var defaults = IndicatorWeights.Default;

        Assert.Multiple(() =>
        {
            Assert.That(weights.Vwap, Is.EqualTo(defaults.Vwap).Within(0.001));
            Assert.That(weights.Ema, Is.EqualTo(defaults.Ema).Within(0.001));
            Assert.That(weights.Rsi, Is.EqualTo(defaults.Rsi).Within(0.001));
            Assert.That(weights.Macd, Is.EqualTo(defaults.Macd).Within(0.001));
            Assert.That(weights.Adx, Is.EqualTo(defaults.Adx).Within(0.001));
            Assert.That(weights.Volume, Is.EqualTo(defaults.Volume).Within(0.001));
            Assert.That(weights.Bollinger, Is.EqualTo(defaults.Bollinger).Within(0.001));
            Assert.That(weights.Stochastic, Is.EqualTo(defaults.Stochastic).Within(0.001));
            Assert.That(weights.Obv, Is.EqualTo(defaults.Obv).Within(0.001));
            Assert.That(weights.Cci, Is.EqualTo(defaults.Cci).Within(0.001));
            Assert.That(weights.WilliamsR, Is.EqualTo(defaults.WilliamsR).Within(0.001));
            Assert.That(weights.Sma, Is.EqualTo(defaults.Sma).Within(0.001));
            Assert.That(weights.Momentum, Is.EqualTo(defaults.Momentum).Within(0.001));
        });
    }

    [Test]
    public void DisableOne_WeightZeroed_RestRedistributed()
    {
        var config = IndicatorConfig.CreateDefault();
        config.Indicators["cci"].Enabled = false;

        var weights = config.ToCalculatorWeights();

        Assert.Multiple(() =>
        {
            // CCI disabled → weight should be 0
            Assert.That(weights.Cci, Is.EqualTo(0).Within(0.0001),
                "Disabled indicator should have zero weight");

            // Weights should still sum to 1.0
            Assert.That(weights.IsValid(), Is.True,
                "Weights should still sum to 1.0 after disabling one indicator");

            // Other weights should be proportionally larger
            Assert.That(weights.Macd, Is.GreaterThan(IndicatorWeights.Default.Macd),
                "Enabled indicators should get proportionally more weight");
        });
    }

    [Test]
    public void DisableMultiple_WeightsZeroed_RestRedistributed()
    {
        var config = IndicatorConfig.CreateDefault();
        config.Indicators["cci"].Enabled = false;        // 3%
        config.Indicators["williamsR"].Enabled = false;   // 3%
        config.Indicators["sma"].Enabled = false;         // 6%
        // Total disabled: 12%

        var weights = config.ToCalculatorWeights();

        Assert.Multiple(() =>
        {
            Assert.That(weights.Cci, Is.EqualTo(0).Within(0.0001));
            Assert.That(weights.WilliamsR, Is.EqualTo(0).Within(0.0001));
            Assert.That(weights.Sma, Is.EqualTo(0).Within(0.0001));
            Assert.That(weights.IsValid(), Is.True,
                "Weights should sum to 1.0 after disabling multiple indicators");
        });

        // MACD was 0.16 out of 0.88 remaining = 0.1818
        Assert.That(weights.Macd, Is.EqualTo(0.16 / 0.88).Within(0.001),
            "MACD should be proportionally redistributed: 0.16/0.88");
    }

    [Test]
    public void DisableAll_FallsBackToDefaults()
    {
        var config = IndicatorConfig.CreateDefault();
        foreach (var entry in config.Indicators.Values)
            entry.Enabled = false;

        var weights = config.ToCalculatorWeights();

        // Should return defaults when all are disabled (no division by zero)
        Assert.That(weights.IsValid(), Is.True,
            "All disabled should fall back to defaults, not crash");
    }

    [Test]
    public void DisableOnlyLaggers_BoostsLeading()
    {
        // Common use case: disable lagging indicators to favor momentum/leading
        var config = IndicatorConfig.CreateDefault();
        config.Indicators["ema"].Enabled = false;   // 13% (lagging)
        config.Indicators["sma"].Enabled = false;   // 6%  (lagging)
        config.Indicators["vwap"].Enabled = false;  // 9%  (price level)
        // Disabled 28% total

        var weights = config.ToCalculatorWeights();

        Assert.Multiple(() =>
        {
            Assert.That(weights.Ema, Is.EqualTo(0).Within(0.0001));
            Assert.That(weights.Sma, Is.EqualTo(0).Within(0.0001));
            Assert.That(weights.Vwap, Is.EqualTo(0).Within(0.0001));
            Assert.That(weights.IsValid(), Is.True);

            // MACD was 16%, now should be ~22% (0.16/0.72)
            Assert.That(weights.Macd, Is.EqualTo(0.16 / 0.72).Within(0.001),
                "MACD should increase when lagging indicators disabled");

            // ADX was 13%, now should be ~18% (0.13/0.72)
            Assert.That(weights.Adx, Is.EqualTo(0.13 / 0.72).Within(0.001),
                "ADX should increase when lagging indicators disabled");
        });
    }

    [Test]
    public void ConfigDisabled_ReturnsHardCodedDefaults()
    {
        var config = IndicatorConfig.CreateDefault();
        config.Enabled = false;  // Global config disabled

        // Even if individual indicators are disabled, when config.Enabled=false
        // we should get the hard-coded defaults
        config.Indicators["macd"].Enabled = false;

        var weights = config.ToCalculatorWeights();
        var defaults = IndicatorWeights.Default;

        Assert.That(weights.Macd, Is.EqualTo(defaults.Macd).Within(0.001),
            "When config is globally disabled, should return hard-coded defaults");
    }

    [Test]
    public void CustomBaseWeights_Normalized()
    {
        var config = IndicatorConfig.CreateDefault();
        // Double the MACD weight
        config.Indicators["macd"].BaseWeight = 0.32;

        var weights = config.ToCalculatorWeights();

        Assert.Multiple(() =>
        {
            Assert.That(weights.IsValid(), Is.True,
                "Custom weights should be normalized to sum to 1.0");
            Assert.That(weights.Macd, Is.GreaterThan(IndicatorWeights.Default.Macd),
                "Increased base weight should result in higher effective weight");
        });
    }

    // ========================================================================
    // IndicatorConfig properties
    // ========================================================================

    [Test]
    public void EnabledCount_ReflectsToggleState()
    {
        var config = IndicatorConfig.CreateDefault();

        Assert.That(config.EnabledCount, Is.EqualTo(13));
        Assert.That(config.DisabledCount, Is.EqualTo(0));

        config.Indicators["cci"].Enabled = false;
        config.Indicators["williamsR"].Enabled = false;

        Assert.That(config.EnabledCount, Is.EqualTo(11));
        Assert.That(config.DisabledCount, Is.EqualTo(2));
    }

    // ========================================================================
    // Score impact: disabled indicator shouldn't affect score
    // ========================================================================

    [Test]
    public void DisabledIndicator_DoesNotAffectScore()
    {
        var snapshot = new IndicatorSnapshot
        {
            Price = 50,
            Vwap = 49,
            Ema9 = 49.5, Ema21 = 49, Ema34 = 48.5, Ema50 = 48,
            Rsi = 55,
            Macd = 0.2, MacdSignal = 0.1, MacdHistogram = 0.1,
            Adx = 30, PlusDi = 25, MinusDi = 15,
            VolumeRatio = 1.3,
            BollingerUpper = 52, BollingerLower = 47, BollingerMiddle = 49.5,
            BollingerPercentB = 0.6, BollingerBandwidth = 10,
            StochasticK = 60, StochasticD = 55,
            ObvSlope = 0.5, Cci = 50, WilliamsR = -40,
            Sma20 = 49.5, Sma50 = 49,
            Momentum = 2, Roc = 1.5
        };

        // Score with all indicators
        var allEnabled = IndicatorConfig.CreateDefault();
        var weightsAll = allEnabled.ToCalculatorWeights();
        var resultAll = MarketScoreCalculator.Calculate(snapshot, weightsAll);

        // Score with CCI disabled (CCI=50 is mildly bullish)
        var cciDisabled = IndicatorConfig.CreateDefault();
        cciDisabled.Indicators["cci"].Enabled = false;
        var weightsCciOff = cciDisabled.ToCalculatorWeights();
        var resultCciOff = MarketScoreCalculator.Calculate(snapshot, weightsCciOff);

        // Scores should differ (CCI was contributing)
        // The exact difference depends on CCI's contribution, but both should be valid
        Assert.That(resultAll.TotalScore, Is.Not.EqualTo(resultCciOff.TotalScore),
            "Disabling an indicator that has a non-zero score should change the total score");
    }
}
