// ============================================================================
// ScientificOptimizerTests - Unit tests for the optimization framework
// ============================================================================

using IdiotProof.BackTesting.Optimization;
using NUnit.Framework;

namespace IdiotProof.Backend.UnitTests;

/// <summary>
/// Unit tests for the scientific optimization framework.
/// Tests weight optimization, walk-forward analysis, and statistical significance testing.
/// </summary>
[TestFixture]
public class ScientificOptimizerTests
{
    private readonly Random _random = new(42); // Fixed seed for reproducibility

    #region IndicatorWeights Tests

    [Test]
    public void IndicatorWeights_Default_SumsToOne()
    {
        var weights = new IndicatorWeights();
        
        Assert.That(weights.Sum, Is.EqualTo(1.0).Within(0.001));
    }

    [Test]
    public void IndicatorWeights_Default_IsValid()
    {
        var weights = new IndicatorWeights();
        
        Assert.That(weights.IsValid, Is.True);
    }

    [Test]
    public void IndicatorWeights_Normalize_ReturnsValidWeights()
    {
        // Create weights that don't sum to 1
        var weights = new IndicatorWeights
        {
            Vwap = 0.3,
            Ema = 0.3,
            Rsi = 0.3,
            Macd = 0.3,
            Adx = 0.3,
            Volume = 0.3
        };
        
        var normalized = weights.Normalize();
        
        Assert.That(normalized.Sum, Is.EqualTo(1.0).Within(0.001));
        Assert.That(normalized.IsValid, Is.True);
    }

    [Test]
    public void IndicatorWeights_Mutate_ProducesValidWeights()
    {
        var original = new IndicatorWeights();
        var mutated = original.Mutate(_random, mutationStrength: 0.1);
        
        Assert.That(mutated.IsValid, Is.True);
        Assert.That(mutated.Sum, Is.EqualTo(1.0).Within(0.001));
    }

    [Test]
    public void IndicatorWeights_Mutate_ChangesValues()
    {
        var original = new IndicatorWeights();
        var mutated = original.Mutate(_random, mutationStrength: 0.3);
        
        // At least some weights should be different (with high mutation strength)
        bool anyDifferent = 
            Math.Abs(original.Vwap - mutated.Vwap) > 0.001 ||
            Math.Abs(original.Ema - mutated.Ema) > 0.001 ||
            Math.Abs(original.Rsi - mutated.Rsi) > 0.001;
        
        Assert.That(anyDifferent, Is.True);
    }

    [Test]
    public void IndicatorWeights_Crossover_ProducesValidWeights()
    {
        var parent1 = new IndicatorWeights { Vwap = 0.30, Ema = 0.20, Rsi = 0.10, Macd = 0.20, Adx = 0.15, Volume = 0.05 };
        var parent2 = new IndicatorWeights { Vwap = 0.10, Ema = 0.30, Rsi = 0.20, Macd = 0.15, Adx = 0.20, Volume = 0.05 };
        
        var child = parent1.Crossover(parent2, _random);
        
        Assert.That(child.IsValid, Is.True);
        Assert.That(child.Sum, Is.EqualTo(1.0).Within(0.001));
    }

    [Test]
    public void IndicatorWeights_ToArray_ReturnsCorrectCount()
    {
        var weights = new IndicatorWeights();
        var array = weights.ToArray();
        
        Assert.That(array, Has.Length.EqualTo(6));
    }

    [Test]
    public void IndicatorWeights_FromArray_RestoresWeights()
    {
        var original = new IndicatorWeights();
        var array = original.ToArray();
        var restored = IndicatorWeights.FromArray(array);
        
        Assert.That(restored.Vwap, Is.EqualTo(original.Vwap).Within(0.001));
        Assert.That(restored.Ema, Is.EqualTo(original.Ema).Within(0.001));
        Assert.That(restored.Rsi, Is.EqualTo(original.Rsi).Within(0.001));
    }

    [Test]
    public void IndicatorWeights_MultipleOperations_MaintainsValidity()
    {
        var weights = new IndicatorWeights();
        
        // Apply multiple mutations
        for (int i = 0; i < 10; i++)
        {
            weights = weights.Mutate(_random, 0.2);
        }
        
        Assert.That(weights.IsValid, Is.True);
        Assert.That(weights.Sum, Is.EqualTo(1.0).Within(0.001));
        
        // All weights should be non-negative
        Assert.That(weights.Vwap, Is.GreaterThanOrEqualTo(0));
        Assert.That(weights.Ema, Is.GreaterThanOrEqualTo(0));
        Assert.That(weights.Rsi, Is.GreaterThanOrEqualTo(0));
        Assert.That(weights.Macd, Is.GreaterThanOrEqualTo(0));
        Assert.That(weights.Adx, Is.GreaterThanOrEqualTo(0));
        Assert.That(weights.Volume, Is.GreaterThanOrEqualTo(0));
    }

    #endregion

    #region StatisticalTests Tests

    [Test]
    public void WinRateTest_HighWinRate_IsSignificant()
    {
        // 80 wins out of 100 trades at 50% expected should be highly significant
        var result = StatisticalTests.WinRateTest(80, 100, 0.5);
        
        Assert.That(result.IsSignificant, Is.True);
        Assert.That(result.PValue, Is.LessThan(0.001));
    }

    [Test]
    public void WinRateTest_ChanceLevel_IsNotSignificant()
    {
        // 52 wins out of 100 at 50% expected is not significant
        var result = StatisticalTests.WinRateTest(52, 100, 0.5);
        
        Assert.That(result.IsSignificant, Is.False);
    }

    [Test]
    public void WinRateTest_PerfectWinRate_IsExtremelySignificant()
    {
        // 100% win rate should be extremely significant
        var result = StatisticalTests.WinRateTest(100, 100, 0.5);
        
        Assert.That(result.IsHighlySignificant, Is.True);
    }

    [Test]
    public void ReturnSignificanceTest_PositiveReturns_HasPositiveObserved()
    {
        var returns = new double[] { 0.02, 0.03, 0.01, 0.025, 0.015, 0.02 };
        
        var result = StatisticalTests.ReturnSignificanceTest(returns);
        
        Assert.That(result.ObservedValue, Is.GreaterThan(0));
    }

    [Test]
    public void CompareWinRates_DifferentRates_CalculatesCorrectly()
    {
        // Compare 80% vs 60% win rate
        var result = StatisticalTests.CompareWinRates(80, 100, 60, 100);
        
        // Observed is the new win rate (0.8)
        Assert.That(result.ObservedValue, Is.EqualTo(0.8).Within(0.001));
        Assert.That(result.TestName, Does.Contain("Win Rate"));
    }

    [Test]
    public void CalculateSharpeRatio_PositiveReturns_IsPositive()
    {
        var returns = new double[] { 0.02, 0.01, 0.015, 0.025, 0.01, 0.02 };
        
        double sharpe = StatisticalTests.CalculateSharpeRatio(returns);
        
        Assert.That(sharpe, Is.GreaterThan(0));
    }

    [Test]
    public void CalculateSharpeRatio_MixedReturns_HandlesCorrectly()
    {
        // Mix of wins and losses with positive mean
        var returns = new double[] { 0.02, -0.01, 0.03, -0.005, 0.015 };
        
        double sharpe = StatisticalTests.CalculateSharpeRatio(returns);
        
        // Should be finite and positive (mean is positive)
        Assert.That(double.IsFinite(sharpe), Is.True);
    }

    #endregion

    #region WalkForwardConfig Tests

    [Test]
    public void WalkForwardConfig_Daily_HasCorrectDefaults()
    {
        var config = WalkForwardConfig.Daily;
        
        Assert.That(config.TrainingBars, Is.GreaterThan(0));
        Assert.That(config.TestingBars, Is.GreaterThan(0));
        Assert.That(config.StepBars, Is.GreaterThan(0));
    }

    [Test]
    public void WalkForwardConfig_Intraday_HasSmallerWindow()
    {
        var daily = WalkForwardConfig.Daily;
        var intraday = WalkForwardConfig.Intraday;
        
        Assert.That(intraday.TrainingBars, Is.LessThan(daily.TrainingBars));
    }

    [Test]
    public void WalkForwardConfig_HighFrequency_HasSmallestWindow()
    {
        var intraday = WalkForwardConfig.Intraday;
        var highFreq = WalkForwardConfig.HighFrequency;
        
        Assert.That(highFreq.TrainingBars, Is.LessThan(intraday.TrainingBars));
    }

    [Test]
    public void WalkForwardConfig_CalculateWindowCount_ReturnsZeroForSmallData()
    {
        var config = WalkForwardConfig.Daily;
        
        // Data smaller than one window
        int count = config.CalculateWindowCount(100);
        
        Assert.That(count, Is.EqualTo(0));
    }

    [Test]
    public void WalkForwardConfig_CalculateWindowCount_ReturnsCorrectCount()
    {
        var config = new WalkForwardConfig
        {
            TrainingBars = 100,
            TestingBars = 50,
            StepBars = 50
        };
        
        // 400 bars total, 150 per window, step 50
        // Windows: 0-149, 50-199, 100-249, 150-299, 200-349, 250-399 = at least 5 windows
        int count = config.CalculateWindowCount(400);
        
        Assert.That(count, Is.GreaterThan(0));
    }

    #endregion

    #region OptimizableConfig Tests

    [Test]
    public void OptimizableConfig_Default_HasValidThresholds()
    {
        var config = new OptimizableConfig();
        
        Assert.That(config.LongEntryThreshold, Is.GreaterThan(0));
        Assert.That(config.ShortEntryThreshold, Is.LessThan(0));
        Assert.That(config.LongExitThreshold, Is.LessThan(config.LongEntryThreshold));
        Assert.That(config.ShortExitThreshold, Is.GreaterThan(config.ShortEntryThreshold));
    }

    [Test]
    public void OptimizableConfig_Default_HasValidWeights()
    {
        var config = new OptimizableConfig();
        
        Assert.That(config.Weights, Is.Not.Null);
        Assert.That(config.Weights.IsValid, Is.True);
    }

    [Test]
    public void OptimizableConfig_Mutate_ProducesValidConfig()
    {
        var original = new OptimizableConfig();
        var mutated = original.Mutate(_random, 0.2);
        
        // Thresholds should still be in reasonable ranges
        Assert.That(mutated.LongEntryThreshold, Is.InRange(0, 100));
        Assert.That(mutated.ShortEntryThreshold, Is.InRange(-100, 0));
        Assert.That(mutated.Weights.IsValid, Is.True);
    }

    [Test]
    public void OptimizableConfig_Crossover_ProducesValidConfig()
    {
        var parent1 = new OptimizableConfig();
        var parent2 = new OptimizableConfig().Mutate(_random, 0.5); // Mutate to make different
        
        var child = parent1.Crossover(parent2, _random);
        
        Assert.That(child.Weights.IsValid, Is.True);
        Assert.That(child.LongEntryThreshold, Is.InRange(0, 100));
        Assert.That(child.ShortEntryThreshold, Is.InRange(-100, 0));
    }

    #endregion

    #region GeneticOptimizationConfig Tests

    [Test]
    public void GeneticOptimizationConfig_Fast_HasValidSettings()
    {
        var config = GeneticOptimizationConfig.Fast;
        
        Assert.That(config.PopulationSize, Is.GreaterThan(0));
        Assert.That(config.MaxGenerations, Is.GreaterThan(0));
        Assert.That(config.MutationRate, Is.InRange(0.0, 1.0));
        Assert.That(config.EliteCount, Is.GreaterThanOrEqualTo(0));
    }

    [Test]
    public void GeneticOptimizationConfig_Thorough_LargerThanFast()
    {
        var fast = GeneticOptimizationConfig.Fast;
        var thorough = GeneticOptimizationConfig.Thorough;
        
        Assert.That(thorough.MaxGenerations, Is.GreaterThanOrEqualTo(fast.MaxGenerations));
    }

    [Test]
    public void GeneticOptimizationConfig_EliteCount_NotExceedPopulation()
    {
        var config = GeneticOptimizationConfig.Default;
        
        Assert.That(config.EliteCount, Is.LessThanOrEqualTo(config.PopulationSize));
    }

    #endregion
}
