// ============================================================================
// IndicatorConditionTests - Tests for technical indicator conditions
// ============================================================================

using IdiotProof.Backend.Enums;
using IdiotProof.Backend.Models;
using IdiotProof.Backend.Strategy;
using NUnit.Framework;

namespace IdiotProof.Backend.UnitTests;

/// <summary>
/// Tests for technical indicator conditions (RSI, ADX, MACD, DI).
/// </summary>
[TestFixture]
public class IndicatorConditionTests
{
    #region RSI Condition Tests

    [Test]
    public void RsiCondition_Overbought_DefaultThreshold_Is70()
    {
        // Arrange
        var condition = new RsiCondition(RsiState.Overbought);

        // Assert
        Assert.That(condition.Threshold, Is.EqualTo(70));
    }

    [Test]
    public void RsiCondition_Oversold_DefaultThreshold_Is30()
    {
        // Arrange
        var condition = new RsiCondition(RsiState.Oversold);

        // Assert
        Assert.That(condition.Threshold, Is.EqualTo(30));
    }

    [Test]
    public void RsiCondition_Overbought_RsiAt75_ReturnsTrue()
    {
        // Arrange
        var condition = new RsiCondition(RsiState.Overbought);

        // Act
        var result = condition.EvaluateRsi(75);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void RsiCondition_Overbought_RsiAt70_ReturnsTrue()
    {
        // Arrange
        var condition = new RsiCondition(RsiState.Overbought);

        // Act
        var result = condition.EvaluateRsi(70);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void RsiCondition_Overbought_RsiAt65_ReturnsFalse()
    {
        // Arrange
        var condition = new RsiCondition(RsiState.Overbought);

        // Act
        var result = condition.EvaluateRsi(65);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void RsiCondition_Oversold_RsiAt25_ReturnsTrue()
    {
        // Arrange
        var condition = new RsiCondition(RsiState.Oversold);

        // Act
        var result = condition.EvaluateRsi(25);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void RsiCondition_Oversold_RsiAt30_ReturnsTrue()
    {
        // Arrange
        var condition = new RsiCondition(RsiState.Oversold);

        // Act
        var result = condition.EvaluateRsi(30);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void RsiCondition_Oversold_RsiAt35_ReturnsFalse()
    {
        // Arrange
        var condition = new RsiCondition(RsiState.Oversold);

        // Act
        var result = condition.EvaluateRsi(35);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void RsiCondition_CustomThreshold_UsesProvidedValue()
    {
        // Arrange
        var condition = new RsiCondition(RsiState.Overbought, 80);

        // Assert
        Assert.That(condition.Threshold, Is.EqualTo(80));

        // Act - RSI at 75 should NOT be overbought with 80 threshold
        var result = condition.EvaluateRsi(75);
        Assert.That(result, Is.False);

        // Act - RSI at 85 should be overbought with 80 threshold
        result = condition.EvaluateRsi(85);
        Assert.That(result, Is.True);
    }

    [Test]
    public void RsiCondition_Name_ContainsStateAndThreshold()
    {
        // Arrange
        var overboughtCondition = new RsiCondition(RsiState.Overbought);
        var oversoldCondition = new RsiCondition(RsiState.Oversold);

        // Assert
        Assert.That(overboughtCondition.Name, Does.Contain("70"));
        Assert.That(overboughtCondition.Name, Does.Contain("Overbought"));
        Assert.That(oversoldCondition.Name, Does.Contain("30"));
        Assert.That(oversoldCondition.Name, Does.Contain("Oversold"));
    }

    [Test]
    public void RsiCondition_EvaluatePriceVwap_WithoutCallback_ReturnsFalse()
    {
        // RSI condition without callback should return false (not ready)
        var condition = new RsiCondition(RsiState.Overbought);

        // Act - no callback set
        var result = condition.Evaluate(currentPrice: 100, vwap: 95);

        // Assert - should return false when no callback is set
        Assert.That(result, Is.False);
    }

    [Test]
    public void RsiCondition_EvaluatePriceVwap_WithCallback_UsesCallbackValue()
    {
        // RSI condition with callback should use the callback value
        var condition = new RsiCondition(RsiState.Overbought);
        condition.GetRsiValue = () => 75; // Overbought (>= 70)

        // Act
        var result = condition.Evaluate(currentPrice: 100, vwap: 95);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void RsiCondition_EvaluatePriceVwap_WithCallback_BelowThreshold_ReturnsFalse()
    {
        // RSI condition with callback but value below threshold
        var condition = new RsiCondition(RsiState.Overbought);
        condition.GetRsiValue = () => 65; // Not overbought

        // Act
        var result = condition.Evaluate(currentPrice: 100, vwap: 95);

        // Assert
        Assert.That(result, Is.False);
    }

    #endregion

    #region ADX Condition Tests

    [Test]
    public void AdxCondition_Gte_ValueAboveThreshold_ReturnsTrue()
    {
        // Arrange
        var condition = new AdxCondition(Comparison.Gte, 25);

        // Act
        var result = condition.EvaluateAdx(30);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void AdxCondition_Gte_ValueAtThreshold_ReturnsTrue()
    {
        // Arrange
        var condition = new AdxCondition(Comparison.Gte, 25);

        // Act
        var result = condition.EvaluateAdx(25);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void AdxCondition_Gte_ValueBelowThreshold_ReturnsFalse()
    {
        // Arrange
        var condition = new AdxCondition(Comparison.Gte, 25);

        // Act
        var result = condition.EvaluateAdx(20);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void AdxCondition_Lte_ValueBelowThreshold_ReturnsTrue()
    {
        // Arrange
        var condition = new AdxCondition(Comparison.Lte, 20);

        // Act
        var result = condition.EvaluateAdx(15);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void AdxCondition_Lte_ValueAtThreshold_ReturnsTrue()
    {
        // Arrange
        var condition = new AdxCondition(Comparison.Lte, 20);

        // Act
        var result = condition.EvaluateAdx(20);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void AdxCondition_Lte_ValueAboveThreshold_ReturnsFalse()
    {
        // Arrange
        var condition = new AdxCondition(Comparison.Lte, 20);

        // Act
        var result = condition.EvaluateAdx(25);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void AdxCondition_Gt_ValueAboveThreshold_ReturnsTrue()
    {
        // Arrange
        var condition = new AdxCondition(Comparison.Gt, 25);

        // Act
        var result = condition.EvaluateAdx(26);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void AdxCondition_Gt_ValueAtThreshold_ReturnsFalse()
    {
        // Arrange
        var condition = new AdxCondition(Comparison.Gt, 25);

        // Act
        var result = condition.EvaluateAdx(25);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void AdxCondition_Lt_ValueBelowThreshold_ReturnsTrue()
    {
        // Arrange
        var condition = new AdxCondition(Comparison.Lt, 20);

        // Act
        var result = condition.EvaluateAdx(19);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void AdxCondition_Lt_ValueAtThreshold_ReturnsFalse()
    {
        // Arrange
        var condition = new AdxCondition(Comparison.Lt, 20);

        // Act
        var result = condition.EvaluateAdx(20);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void AdxCondition_ThresholdOutOfRange_ThrowsException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => new AdxCondition(Comparison.Gte, -5));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AdxCondition(Comparison.Gte, 105));
    }

    [Test]
    public void AdxCondition_Name_ContainsComparisonAndThreshold()
    {
        // Arrange
        var condition = new AdxCondition(Comparison.Gte, 25);

        // Assert
        Assert.That(condition.Name, Does.Contain("25"));
        Assert.That(condition.Name, Does.Contain(">="));
    }

    [Test]
    public void AdxCondition_EvaluatePriceVwap_WithoutCallback_ReturnsFalse()
    {
        // ADX condition without callback should return false (not ready)
        var condition = new AdxCondition(Comparison.Gte, 25);

        // Act - no callback set
        var result = condition.Evaluate(currentPrice: 100, vwap: 95);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void AdxCondition_EvaluatePriceVwap_WithCallback_UsesCallbackValue()
    {
        // ADX condition with callback should use the callback value
        var condition = new AdxCondition(Comparison.Gte, 25);
        condition.GetAdxValue = () => 30; // Strong trend

        // Act
        var result = condition.Evaluate(currentPrice: 100, vwap: 95);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void AdxCondition_EvaluatePriceVwap_WithCallback_BelowThreshold_ReturnsFalse()
    {
        // ADX condition with callback but value below threshold
        var condition = new AdxCondition(Comparison.Gte, 25);
        condition.GetAdxValue = () => 20; // Weak trend

        // Act
        var result = condition.Evaluate(currentPrice: 100, vwap: 95);

        // Assert
        Assert.That(result, Is.False);
    }

    #endregion

    #region MACD Condition Tests

    [Test]
    public void MacdCondition_Bullish_MacdAboveSignal_ReturnsTrue()
    {
        // Arrange
        var condition = new MacdCondition(MacdState.Bullish);

        // Act - MACD = 2.5, Signal = 1.5
        var result = condition.EvaluateMacd(macdLine: 2.5, signalLine: 1.5);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void MacdCondition_Bullish_MacdBelowSignal_ReturnsFalse()
    {
        // Arrange
        var condition = new MacdCondition(MacdState.Bullish);

        // Act - MACD = 1.0, Signal = 1.5
        var result = condition.EvaluateMacd(macdLine: 1.0, signalLine: 1.5);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void MacdCondition_Bearish_MacdBelowSignal_ReturnsTrue()
    {
        // Arrange
        var condition = new MacdCondition(MacdState.Bearish);

        // Act - MACD = 1.0, Signal = 1.5
        var result = condition.EvaluateMacd(macdLine: 1.0, signalLine: 1.5);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void MacdCondition_Bearish_MacdAboveSignal_ReturnsFalse()
    {
        // Arrange
        var condition = new MacdCondition(MacdState.Bearish);

        // Act - MACD = 2.5, Signal = 1.5
        var result = condition.EvaluateMacd(macdLine: 2.5, signalLine: 1.5);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void MacdCondition_AboveZero_PositiveMacd_ReturnsTrue()
    {
        // Arrange
        var condition = new MacdCondition(MacdState.AboveZero);

        // Act - MACD = 1.5
        var result = condition.EvaluateMacd(macdLine: 1.5, signalLine: 1.0);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void MacdCondition_AboveZero_NegativeMacd_ReturnsFalse()
    {
        // Arrange
        var condition = new MacdCondition(MacdState.AboveZero);

        // Act - MACD = -1.5
        var result = condition.EvaluateMacd(macdLine: -1.5, signalLine: -1.0);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void MacdCondition_BelowZero_NegativeMacd_ReturnsTrue()
    {
        // Arrange
        var condition = new MacdCondition(MacdState.BelowZero);

        // Act - MACD = -1.5
        var result = condition.EvaluateMacd(macdLine: -1.5, signalLine: -1.0);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void MacdCondition_BelowZero_PositiveMacd_ReturnsFalse()
    {
        // Arrange
        var condition = new MacdCondition(MacdState.BelowZero);

        // Act - MACD = 1.5
        var result = condition.EvaluateMacd(macdLine: 1.5, signalLine: 1.0);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void MacdCondition_HistogramRising_IncreasingHistogram_ReturnsTrue()
    {
        // Arrange
        var condition = new MacdCondition(MacdState.HistogramRising);

        // Act - Histogram went from 0.5 to 1.0
        var result = condition.EvaluateMacd(macdLine: 2.0, signalLine: 1.0, histogram: 1.0, previousHistogram: 0.5);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void MacdCondition_HistogramRising_DecreasingHistogram_ReturnsFalse()
    {
        // Arrange
        var condition = new MacdCondition(MacdState.HistogramRising);

        // Act - Histogram went from 1.0 to 0.5
        var result = condition.EvaluateMacd(macdLine: 1.5, signalLine: 1.0, histogram: 0.5, previousHistogram: 1.0);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void MacdCondition_HistogramFalling_DecreasingHistogram_ReturnsTrue()
    {
        // Arrange
        var condition = new MacdCondition(MacdState.HistogramFalling);

        // Act - Histogram went from 1.0 to 0.5
        var result = condition.EvaluateMacd(macdLine: 1.5, signalLine: 1.0, histogram: 0.5, previousHistogram: 1.0);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void MacdCondition_HistogramFalling_IncreasingHistogram_ReturnsFalse()
    {
        // Arrange
        var condition = new MacdCondition(MacdState.HistogramFalling);

        // Act - Histogram went from 0.5 to 1.0
        var result = condition.EvaluateMacd(macdLine: 2.0, signalLine: 1.0, histogram: 1.0, previousHistogram: 0.5);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void MacdCondition_HistogramRising_NoPreviousValue_ReturnsFalse()
    {
        // Arrange
        var condition = new MacdCondition(MacdState.HistogramRising);

        // Act - No previous histogram value
        var result = condition.EvaluateMacd(macdLine: 2.0, signalLine: 1.0, histogram: 1.0);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void MacdCondition_Name_ContainsState()
    {
        // Arrange
        var bullishCondition = new MacdCondition(MacdState.Bullish);
        var bearishCondition = new MacdCondition(MacdState.Bearish);

        // Assert
        Assert.That(bullishCondition.Name, Does.Contain("Bullish"));
        Assert.That(bearishCondition.Name, Does.Contain("Bearish"));
    }

    [Test]
    public void MacdCondition_EvaluatePriceVwap_WithoutCallback_ReturnsFalse()
    {
        // MACD condition without callback should return false (not ready)
        var condition = new MacdCondition(MacdState.Bullish);

        // Act - no callback set
        var result = condition.Evaluate(currentPrice: 100, vwap: 95);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void MacdCondition_EvaluatePriceVwap_WithCallback_UsesCallbackValue()
    {
        // MACD condition with callback should use the callback value
        var condition = new MacdCondition(MacdState.Bullish);
        condition.GetMacdValues = () => (MacdLine: 2.5, SignalLine: 1.5, Histogram: 1.0, PreviousHistogram: 0.5);

        // Act
        var result = condition.Evaluate(currentPrice: 100, vwap: 95);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void MacdCondition_EvaluatePriceVwap_WithCallback_Bearish_ReturnsFalse()
    {
        // MACD condition for bullish but values are bearish
        var condition = new MacdCondition(MacdState.Bullish);
        condition.GetMacdValues = () => (MacdLine: 1.0, SignalLine: 2.0, Histogram: -1.0, PreviousHistogram: 0.0);

        // Act
        var result = condition.Evaluate(currentPrice: 100, vwap: 95);

        // Assert
        Assert.That(result, Is.False);
    }

    #endregion

    #region DI Condition Tests

    [Test]
    public void DiCondition_Positive_PlusDIGreater_ReturnsTrue()
    {
        // Arrange
        var condition = new DiCondition(DiDirection.Positive);

        // Act - +DI = 30, -DI = 20
        var result = condition.EvaluateDI(plusDI: 30, minusDI: 20);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void DiCondition_Positive_MinusDIGreater_ReturnsFalse()
    {
        // Arrange
        var condition = new DiCondition(DiDirection.Positive);

        // Act - +DI = 20, -DI = 30
        var result = condition.EvaluateDI(plusDI: 20, minusDI: 30);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void DiCondition_Positive_EqualValues_ReturnsFalse()
    {
        // Arrange - With 0 minDifference, equal values should return false (no direction dominates)
        var condition = new DiCondition(DiDirection.Positive);

        // Act - +DI = 25, -DI = 25
        var result = condition.EvaluateDI(plusDI: 25, minusDI: 25);

        // Assert - When equal, +DI is NOT greater than -DI, so no bullish dominance
        Assert.That(result, Is.False);
    }

    [Test]
    public void DiCondition_Negative_MinusDIGreater_ReturnsTrue()
    {
        // Arrange
        var condition = new DiCondition(DiDirection.Negative);

        // Act - +DI = 20, -DI = 30
        var result = condition.EvaluateDI(plusDI: 20, minusDI: 30);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void DiCondition_Negative_PlusDIGreater_ReturnsFalse()
    {
        // Arrange
        var condition = new DiCondition(DiDirection.Negative);

        // Act - +DI = 30, -DI = 20
        var result = condition.EvaluateDI(plusDI: 30, minusDI: 20);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void DiCondition_MinDifference_MeetsThreshold_ReturnsTrue()
    {
        // Arrange - Require at least 5 points difference
        var condition = new DiCondition(DiDirection.Positive, minDifference: 5);

        // Act - +DI = 30, -DI = 25 (difference = 5)
        var result = condition.EvaluateDI(plusDI: 30, minusDI: 25);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void DiCondition_MinDifference_BelowThreshold_ReturnsFalse()
    {
        // Arrange - Require at least 10 points difference
        var condition = new DiCondition(DiDirection.Positive, minDifference: 10);

        // Act - +DI = 30, -DI = 25 (difference = 5)
        var result = condition.EvaluateDI(plusDI: 30, minusDI: 25);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void DiCondition_NegativeMinDifference_TreatedAsZero()
    {
        // Arrange - Negative minDifference should be treated as 0
        var condition = new DiCondition(DiDirection.Positive, minDifference: -5);

        // Assert
        Assert.That(condition.MinDifference, Is.EqualTo(0));
    }

    [Test]
    public void DiCondition_Name_ContainsDirection()
    {
        // Arrange
        var positiveCondition = new DiCondition(DiDirection.Positive);
        var negativeCondition = new DiCondition(DiDirection.Negative);

        // Assert
        Assert.That(positiveCondition.Name, Does.Contain("Bullish"));
        Assert.That(negativeCondition.Name, Does.Contain("Bearish"));
    }

    [Test]
    public void DiCondition_Name_ContainsMinDifference()
    {
        // Arrange
        var condition = new DiCondition(DiDirection.Positive, minDifference: 5);

        // Assert
        Assert.That(condition.Name, Does.Contain("5"));
    }

    [Test]
    public void DiCondition_EvaluatePriceVwap_WithoutCallback_ReturnsFalse()
    {
        // DI condition without callback should return false (not ready)
        var condition = new DiCondition(DiDirection.Positive);

        // Act - no callback set
        var result = condition.Evaluate(currentPrice: 100, vwap: 95);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void DiCondition_EvaluatePriceVwap_WithCallback_UsesCallbackValue()
    {
        // DI condition with callback should use the callback value
        var condition = new DiCondition(DiDirection.Positive);
        condition.GetDiValues = () => (PlusDI: 30, MinusDI: 20);

        // Act
        var result = condition.Evaluate(currentPrice: 100, vwap: 95);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void DiCondition_EvaluatePriceVwap_WithCallback_Negative_ReturnsFalse()
    {
        // DI condition for positive but values are negative
        var condition = new DiCondition(DiDirection.Positive);
        condition.GetDiValues = () => (PlusDI: 20, MinusDI: 30);

        // Act
        var result = condition.Evaluate(currentPrice: 100, vwap: 95);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void DiCondition_EvaluatePriceVwap_WithCallback_ZeroValues_ReturnsFalse()
    {
        // DI condition with zero values (not warmed up) should return false
        var condition = new DiCondition(DiDirection.Positive);
        condition.GetDiValues = () => (PlusDI: 0, MinusDI: 0);

        // Act
        var result = condition.Evaluate(currentPrice: 100, vwap: 95);

        // Assert
        Assert.That(result, Is.False);
    }

    #endregion

    #region Fluent API Integration Tests

    [Test]
    public void FluentApi_IsRsi_CreatesConditionInStrategy()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .IsRsi(RsiState.Oversold)
            .Breakout(150)
            .Buy(100, Price.Current)
            .Build();

        // Assert
        Assert.That(strategy.Conditions, Has.Count.EqualTo(2));
        Assert.That(strategy.Conditions[0], Is.TypeOf<RsiCondition>());
    }

    [Test]
    public void FluentApi_IsRsi_WithCustomThreshold_CreatesCondition()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .IsRsi(RsiState.Overbought, 80)
            .Breakout(150)
            .Buy(100, Price.Current)
            .Build();

        // Assert
        var rsiCondition = strategy.Conditions[0] as RsiCondition;
        Assert.That(rsiCondition, Is.Not.Null);
        Assert.That(rsiCondition!.Threshold, Is.EqualTo(80));
    }

    [Test]
    public void FluentApi_IsAdx_CreatesConditionInStrategy()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .IsAdx(Comparison.Gte, 25)
            .Breakout(150)
            .Buy(100, Price.Current)
            .Build();

        // Assert
        Assert.That(strategy.Conditions, Has.Count.EqualTo(2));
        Assert.That(strategy.Conditions[0], Is.TypeOf<AdxCondition>());
    }

    [Test]
    public void FluentApi_IsMacd_CreatesConditionInStrategy()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .IsMacd(MacdState.Bullish)
            .Breakout(150)
            .Buy(100, Price.Current)
            .Build();

        // Assert
        Assert.That(strategy.Conditions, Has.Count.EqualTo(2));
        Assert.That(strategy.Conditions[0], Is.TypeOf<MacdCondition>());
    }

    [Test]
    public void FluentApi_IsDI_CreatesConditionInStrategy()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .IsDI(DiDirection.Positive)
            .Breakout(150)
            .Buy(100, Price.Current)
            .Build();

        // Assert
        Assert.That(strategy.Conditions, Has.Count.EqualTo(2));
        Assert.That(strategy.Conditions[0], Is.TypeOf<DiCondition>());
    }

    [Test]
    public void FluentApi_IsDI_WithMinDifference_CreatesCondition()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .IsDI(DiDirection.Positive, 5)
            .Breakout(150)
            .Buy(100, Price.Current)
            .Build();

        // Assert
        var diCondition = strategy.Conditions[0] as DiCondition;
        Assert.That(diCondition, Is.Not.Null);
        Assert.That(diCondition!.MinDifference, Is.EqualTo(5));
    }

    [Test]
    public void FluentApi_MultipleIndicators_CanBeChained()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .IsAdx(Comparison.Gte, 25)
            .IsDI(DiDirection.Positive)
            .IsMacd(MacdState.Bullish)
            .IsRsi(RsiState.Oversold)
            .Breakout(150)
            .Buy(100, Price.Current)
            .Build();

        // Assert
        Assert.That(strategy.Conditions, Has.Count.EqualTo(5));
        Assert.That(strategy.Conditions[0], Is.TypeOf<AdxCondition>());
        Assert.That(strategy.Conditions[1], Is.TypeOf<DiCondition>());
        Assert.That(strategy.Conditions[2], Is.TypeOf<MacdCondition>());
        Assert.That(strategy.Conditions[3], Is.TypeOf<RsiCondition>());
        Assert.That(strategy.Conditions[4], Is.TypeOf<BreakoutCondition>());
    }

    [Test]
    public void FluentApi_IndicatorsWithPriceConditions_CanBeMixed()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .Breakout(145)
            .IsAdx(Comparison.Gte, 25)
            .Pullback(148)
            .IsMacd(MacdState.AboveZero)
            .IsAboveVwap()
            .Buy(100, Price.Current)
            .Build();

        // Assert
        Assert.That(strategy.Conditions, Has.Count.EqualTo(5));
        Assert.That(strategy.Conditions[0], Is.TypeOf<BreakoutCondition>());
        Assert.That(strategy.Conditions[1], Is.TypeOf<AdxCondition>());
        Assert.That(strategy.Conditions[2], Is.TypeOf<PullbackCondition>());
        Assert.That(strategy.Conditions[3], Is.TypeOf<MacdCondition>());
        Assert.That(strategy.Conditions[4], Is.TypeOf<AboveVwapCondition>());
    }

    #endregion

    #region Additional RSI Edge Case Tests

    [Test]
    public void RsiCondition_Oversold_CustomThreshold_UsesProvidedValue()
    {
        // Arrange - Custom oversold threshold of 25
        var condition = new RsiCondition(RsiState.Oversold, 25);

        // Assert
        Assert.That(condition.Threshold, Is.EqualTo(25));

        // RSI at 30 should NOT be oversold with 25 threshold
        Assert.That(condition.EvaluateRsi(30), Is.False);

        // RSI at 20 should be oversold with 25 threshold
        Assert.That(condition.EvaluateRsi(20), Is.True);
    }

    [Test]
    public void RsiCondition_State_IsStoredCorrectly()
    {
        // Arrange & Act
        var overbought = new RsiCondition(RsiState.Overbought);
        var oversold = new RsiCondition(RsiState.Oversold);

        // Assert
        Assert.That(overbought.State, Is.EqualTo(RsiState.Overbought));
        Assert.That(oversold.State, Is.EqualTo(RsiState.Oversold));
    }

    [Test]
    public void RsiCondition_DefaultConstants_AreCorrect()
    {
        // Assert
        Assert.That(RsiCondition.DefaultOverboughtThreshold, Is.EqualTo(70.0));
        Assert.That(RsiCondition.DefaultOversoldThreshold, Is.EqualTo(30.0));
    }

    #endregion

    #region Additional ADX Edge Case Tests

    [Test]
    public void AdxCondition_Eq_ValueAtThreshold_ReturnsTrue()
    {
        // Arrange
        var condition = new AdxCondition(Comparison.Eq, 25);

        // Act
        var result = condition.EvaluateAdx(25);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void AdxCondition_Eq_ValueNotAtThreshold_ReturnsFalse()
    {
        // Arrange
        var condition = new AdxCondition(Comparison.Eq, 25);

        // Act
        var result = condition.EvaluateAdx(26);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void AdxCondition_Eq_ValueVeryClose_ReturnsTrue()
    {
        // Arrange - Should handle floating point comparison
        var condition = new AdxCondition(Comparison.Eq, 25);

        // Act - Value within epsilon
        var result = condition.EvaluateAdx(25.0005);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void AdxCondition_BoundaryValue_Zero_IsValid()
    {
        // Arrange & Act
        var condition = new AdxCondition(Comparison.Gte, 0);

        // Assert
        Assert.That(condition.Threshold, Is.EqualTo(0));
        Assert.That(condition.EvaluateAdx(0), Is.True);
    }

    [Test]
    public void AdxCondition_BoundaryValue_100_IsValid()
    {
        // Arrange & Act
        var condition = new AdxCondition(Comparison.Lte, 100);

        // Assert
        Assert.That(condition.Threshold, Is.EqualTo(100));
        Assert.That(condition.EvaluateAdx(100), Is.True);
    }

    [Test]
    public void AdxCondition_Name_AllComparisons_FormattedCorrectly()
    {
        // Arrange & Assert
        Assert.That(new AdxCondition(Comparison.Gte, 25).Name, Is.EqualTo("ADX >= 25"));
        Assert.That(new AdxCondition(Comparison.Lte, 20).Name, Is.EqualTo("ADX <= 20"));
        Assert.That(new AdxCondition(Comparison.Gt, 30).Name, Is.EqualTo("ADX > 30"));
        Assert.That(new AdxCondition(Comparison.Lt, 15).Name, Is.EqualTo("ADX < 15"));
        Assert.That(new AdxCondition(Comparison.Eq, 50).Name, Is.EqualTo("ADX == 50"));
    }

    [Test]
    public void AdxCondition_Comparison_IsStoredCorrectly()
    {
        // Arrange & Act
        var condition = new AdxCondition(Comparison.Gt, 25);

        // Assert
        Assert.That(condition.Comparison, Is.EqualTo(Comparison.Gt));
        Assert.That(condition.Threshold, Is.EqualTo(25));
    }

    #endregion

    #region Additional MACD Edge Case Tests

    [Test]
    public void MacdCondition_Bullish_EqualValues_ReturnsFalse()
    {
        // Arrange
        var condition = new MacdCondition(MacdState.Bullish);

        // Act - MACD = Signal (not bullish, must be strictly above)
        var result = condition.EvaluateMacd(macdLine: 1.5, signalLine: 1.5);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void MacdCondition_Bearish_EqualValues_ReturnsFalse()
    {
        // Arrange
        var condition = new MacdCondition(MacdState.Bearish);

        // Act - MACD = Signal (not bearish, must be strictly below)
        var result = condition.EvaluateMacd(macdLine: 1.5, signalLine: 1.5);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void MacdCondition_AboveZero_AtZero_ReturnsFalse()
    {
        // Arrange
        var condition = new MacdCondition(MacdState.AboveZero);

        // Act - MACD = 0 (not above zero)
        var result = condition.EvaluateMacd(macdLine: 0, signalLine: -0.5);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void MacdCondition_BelowZero_AtZero_ReturnsFalse()
    {
        // Arrange
        var condition = new MacdCondition(MacdState.BelowZero);

        // Act - MACD = 0 (not below zero)
        var result = condition.EvaluateMacd(macdLine: 0, signalLine: 0.5);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void MacdCondition_HistogramFalling_NoPreviousValue_ReturnsFalse()
    {
        // Arrange
        var condition = new MacdCondition(MacdState.HistogramFalling);

        // Act - No previous histogram value
        var result = condition.EvaluateMacd(macdLine: 1.5, signalLine: 1.0, histogram: 0.5);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void MacdCondition_HistogramRising_EqualHistograms_ReturnsFalse()
    {
        // Arrange
        var condition = new MacdCondition(MacdState.HistogramRising);

        // Act - Histogram unchanged
        var result = condition.EvaluateMacd(macdLine: 2.0, signalLine: 1.0, histogram: 1.0, previousHistogram: 1.0);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void MacdCondition_HistogramFalling_EqualHistograms_ReturnsFalse()
    {
        // Arrange
        var condition = new MacdCondition(MacdState.HistogramFalling);

        // Act - Histogram unchanged
        var result = condition.EvaluateMacd(macdLine: 2.0, signalLine: 1.0, histogram: 1.0, previousHistogram: 1.0);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void MacdCondition_Name_AllStates_FormattedCorrectly()
    {
        // Arrange & Assert
        Assert.That(new MacdCondition(MacdState.Bullish).Name, Does.Contain("Bullish"));
        Assert.That(new MacdCondition(MacdState.Bearish).Name, Does.Contain("Bearish"));
        Assert.That(new MacdCondition(MacdState.AboveZero).Name, Does.Contain("Uptrend"));
        Assert.That(new MacdCondition(MacdState.BelowZero).Name, Does.Contain("Downtrend"));
        Assert.That(new MacdCondition(MacdState.HistogramRising).Name, Does.Contain("Rising"));
        Assert.That(new MacdCondition(MacdState.HistogramFalling).Name, Does.Contain("Falling"));
    }

    [Test]
    public void MacdCondition_State_IsStoredCorrectly()
    {
        // Arrange & Act
        var condition = new MacdCondition(MacdState.HistogramRising);

        // Assert
        Assert.That(condition.State, Is.EqualTo(MacdState.HistogramRising));
    }

    [Test]
    public void MacdCondition_NegativeValues_WorkCorrectly()
    {
        // Arrange
        var bullish = new MacdCondition(MacdState.Bullish);
        var bearish = new MacdCondition(MacdState.Bearish);

        // Act & Assert - Both negative, MACD less negative than signal = bullish
        Assert.That(bullish.EvaluateMacd(macdLine: -0.5, signalLine: -1.0), Is.True);
        Assert.That(bearish.EvaluateMacd(macdLine: -1.0, signalLine: -0.5), Is.True);
    }

    #endregion

    #region Additional DI Edge Case Tests

    [Test]
    public void DiCondition_Negative_EqualValues_ReturnsFalse()
    {
        // Arrange - With 0 minDifference, equal values should return false (no direction dominates)
        var condition = new DiCondition(DiDirection.Negative);

        // Act - +DI = 25, -DI = 25
        var result = condition.EvaluateDI(plusDI: 25, minusDI: 25);

        // Assert - When equal, -DI is NOT greater than +DI, so no bearish dominance
        Assert.That(result, Is.False);
    }

    [Test]
    public void DiCondition_Negative_WithMinDifference_MeetsThreshold_ReturnsTrue()
    {
        // Arrange - Require at least 5 points difference for bearish
        var condition = new DiCondition(DiDirection.Negative, minDifference: 5);

        // Act - +DI = 20, -DI = 25 (difference = 5)
        var result = condition.EvaluateDI(plusDI: 20, minusDI: 25);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void DiCondition_Negative_WithMinDifference_BelowThreshold_ReturnsFalse()
    {
        // Arrange - Require at least 10 points difference for bearish
        var condition = new DiCondition(DiDirection.Negative, minDifference: 10);

        // Act - +DI = 20, -DI = 25 (difference = 5)
        var result = condition.EvaluateDI(plusDI: 20, minusDI: 25);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void DiCondition_Direction_IsStoredCorrectly()
    {
        // Arrange & Act
        var positive = new DiCondition(DiDirection.Positive);
        var negative = new DiCondition(DiDirection.Negative);

        // Assert
        Assert.That(positive.Direction, Is.EqualTo(DiDirection.Positive));
        Assert.That(negative.Direction, Is.EqualTo(DiDirection.Negative));
    }

    [Test]
    public void DiCondition_Name_NegativeWithMinDifference_FormattedCorrectly()
    {
        // Arrange
        var condition = new DiCondition(DiDirection.Negative, minDifference: 10);

        // Assert
        Assert.That(condition.Name, Does.Contain("10"));
        Assert.That(condition.Name, Does.Contain("Bearish"));
    }

    [Test]
    public void DiCondition_LargeValues_WorkCorrectly()
    {
        // Arrange
        var condition = new DiCondition(DiDirection.Positive, minDifference: 20);

        // Act - Large values: +DI = 80, -DI = 50 (difference = 30)
        var result = condition.EvaluateDI(plusDI: 80, minusDI: 50);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void DiCondition_ZeroValues_WorkCorrectly()
    {
        // Arrange
        var condition = new DiCondition(DiDirection.Positive);

        // Act - Both zero
        var result = condition.EvaluateDI(plusDI: 0, minusDI: 0);

        // Assert - When equal (both zero), +DI is NOT greater than -DI, so no bullish dominance
        Assert.That(result, Is.False);
    }

    #endregion

    #region Fluent API Additional Tests

    [Test]
    public void FluentApi_IsAdx_StoresComparisonCorrectly()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .IsAdx(Comparison.Lt, 15)
            .Breakout(150)
            .Buy(100, Price.Current)
            .Build();

        // Assert
        var adxCondition = strategy.Conditions[0] as AdxCondition;
        Assert.That(adxCondition, Is.Not.Null);
        Assert.That(adxCondition!.Comparison, Is.EqualTo(Comparison.Lt));
        Assert.That(adxCondition.Threshold, Is.EqualTo(15));
    }

    [Test]
    public void FluentApi_IsMacd_StoresStateCorrectly()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .IsMacd(MacdState.HistogramFalling)
            .Breakout(150)
            .Buy(100, Price.Current)
            .Build();

        // Assert
        var macdCondition = strategy.Conditions[0] as MacdCondition;
        Assert.That(macdCondition, Is.Not.Null);
        Assert.That(macdCondition!.State, Is.EqualTo(MacdState.HistogramFalling));
    }

    [Test]
    public void FluentApi_IsRsi_StoresStateCorrectly()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .IsRsi(RsiState.Overbought)
            .Breakout(150)
            .Buy(100, Price.Current)
            .Build();

        // Assert
        var rsiCondition = strategy.Conditions[0] as RsiCondition;
        Assert.That(rsiCondition, Is.Not.Null);
        Assert.That(rsiCondition!.State, Is.EqualTo(RsiState.Overbought));
    }

    [Test]
    public void FluentApi_IsDI_StoresDirectionCorrectly()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .IsDI(DiDirection.Negative, 10)
            .Breakout(150)
            .Buy(100, Price.Current)
            .Build();

        // Assert
        var diCondition = strategy.Conditions[0] as DiCondition;
        Assert.That(diCondition, Is.Not.Null);
        Assert.That(diCondition!.Direction, Is.EqualTo(DiDirection.Negative));
        Assert.That(diCondition.MinDifference, Is.EqualTo(10));
    }

    [Test]
    public void FluentApi_AllIndicators_WithSell_Works()
    {
        // Arrange & Act - Test with Sell instead of Buy
        var strategy = Stock.Ticker("AAPL")
            .IsRsi(RsiState.Overbought)
            .IsAdx(Comparison.Gte, 25)
            .IsMacd(MacdState.Bearish)
            .IsDI(DiDirection.Negative)
            .Breakout(150)
            .Sell(100, Price.Current)
            .Build();

        // Assert
        Assert.That(strategy.Conditions, Has.Count.EqualTo(5));
        Assert.That(strategy.Order.Side, Is.EqualTo(OrderSide.Sell));
    }

    [Test]
    public void FluentApi_IndicatorOnly_Strategy_Works()
    {
        // Arrange & Act - Strategy with only indicator conditions (no price conditions)
        var strategy = Stock.Ticker("AAPL")
            .IsRsi(RsiState.Oversold)
            .Buy(100, Price.Current)
            .Build();

        // Assert
        Assert.That(strategy.Conditions, Has.Count.EqualTo(1));
        Assert.That(strategy.Conditions[0], Is.TypeOf<RsiCondition>());
    }

    #endregion
}
