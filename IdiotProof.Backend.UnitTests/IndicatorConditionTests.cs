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

    // ========================================================================
    // MOMENTUM CONDITION TESTS
    // ========================================================================
    //
    // Momentum Indicator Visualization:
    //
    //     Momentum = Current Price - Price N periods ago
    //     ┌────────────────────────────────────────────────┐
    //     │     /\                        /\               │
    //     │    /  \                      /  \     Price    │
    //     │   /    \                    /    \             │
    //     │──/──────\──────────────────/──────\────────────│ Zero
    //     │          \                /        \           │
    //     │           \              /          \          │
    //     │            \____________/                      │
    //     └────────────────────────────────────────────────┘
    //           ↑ Momentum > 0        ↑ Momentum > 0
    //              (Bullish)             (Bullish)
    //
    // ========================================================================

    #region Momentum Condition Tests

    [Test]
    public void MomentumAboveCondition_AboveThreshold_ReturnsTrue()
    {
        // Arrange
        var condition = new MomentumAboveCondition(0);
        condition.GetMomentumValue = () => 2.5;

        // Act
        var result = condition.Evaluate(currentPrice: 150, vwap: 148);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void MomentumAboveCondition_BelowThreshold_ReturnsFalse()
    {
        // Arrange
        var condition = new MomentumAboveCondition(0);
        condition.GetMomentumValue = () => -1.5;

        // Act
        var result = condition.Evaluate(currentPrice: 150, vwap: 148);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void MomentumAboveCondition_AtThreshold_ReturnsTrue()
    {
        // Arrange
        var condition = new MomentumAboveCondition(0);
        condition.GetMomentumValue = () => 0;

        // Act
        var result = condition.Evaluate(currentPrice: 150, vwap: 148);

        // Assert - >= threshold
        Assert.That(result, Is.True);
    }

    [Test]
    public void MomentumAboveCondition_WithoutCallback_ReturnsFalse()
    {
        // Arrange
        var condition = new MomentumAboveCondition(0);
        // No callback set

        // Act
        var result = condition.Evaluate(currentPrice: 150, vwap: 148);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void MomentumAboveCondition_Name_ContainsThreshold()
    {
        // Arrange
        var condition = new MomentumAboveCondition(2.5);

        // Assert
        Assert.That(condition.Name, Does.Contain("2.50"));
    }

    [Test]
    public void MomentumBelowCondition_BelowThreshold_ReturnsTrue()
    {
        // Arrange
        var condition = new MomentumBelowCondition(0);
        condition.GetMomentumValue = () => -2.5;

        // Act
        var result = condition.Evaluate(currentPrice: 150, vwap: 148);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void MomentumBelowCondition_AboveThreshold_ReturnsFalse()
    {
        // Arrange
        var condition = new MomentumBelowCondition(0);
        condition.GetMomentumValue = () => 1.5;

        // Act
        var result = condition.Evaluate(currentPrice: 150, vwap: 148);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void MomentumBelowCondition_AtThreshold_ReturnsTrue()
    {
        // Arrange
        var condition = new MomentumBelowCondition(0);
        condition.GetMomentumValue = () => 0;

        // Act
        var result = condition.Evaluate(currentPrice: 150, vwap: 148);

        // Assert - <= threshold
        Assert.That(result, Is.True);
    }

    [Test]
    public void MomentumBelowCondition_WithoutCallback_ReturnsFalse()
    {
        // Arrange
        var condition = new MomentumBelowCondition(0);

        // Act
        var result = condition.Evaluate(currentPrice: 150, vwap: 148);

        // Assert
        Assert.That(result, Is.False);
    }

    #endregion

    // ========================================================================
    // ROC (RATE OF CHANGE) CONDITION TESTS
    // ========================================================================
    //
    // ROC = ((Current Price - Price N periods ago) / Price N periods ago) × 100
    //
    //     ┌────────────────────────────────────────────────┐
    //     │  +5% ────────────────────── Strong bullish     │
    //     │  +2% ══════════════════════════════════════════│ ← Threshold
    //     │   0% ──────────────────────────────────────────│ Zero
    //     │  -2% ══════════════════════════════════════════│ ← Threshold
    //     │  -5% ────────────────────── Strong bearish     │
    //     └────────────────────────────────────────────────┘
    //
    // ========================================================================

    #region ROC Condition Tests

    [Test]
    public void RocAboveCondition_AboveThreshold_ReturnsTrue()
    {
        // Arrange
        var condition = new RocAboveCondition(2.0);
        condition.GetRocValue = () => 3.5;

        // Act
        var result = condition.Evaluate(currentPrice: 150, vwap: 148);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void RocAboveCondition_BelowThreshold_ReturnsFalse()
    {
        // Arrange
        var condition = new RocAboveCondition(2.0);
        condition.GetRocValue = () => 1.5;

        // Act
        var result = condition.Evaluate(currentPrice: 150, vwap: 148);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void RocAboveCondition_AtThreshold_ReturnsTrue()
    {
        // Arrange
        var condition = new RocAboveCondition(2.0);
        condition.GetRocValue = () => 2.0;

        // Act
        var result = condition.Evaluate(currentPrice: 150, vwap: 148);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void RocAboveCondition_WithoutCallback_ReturnsFalse()
    {
        // Arrange
        var condition = new RocAboveCondition(2.0);

        // Act
        var result = condition.Evaluate(currentPrice: 150, vwap: 148);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void RocAboveCondition_Name_ContainsThresholdPercent()
    {
        // Arrange
        var condition = new RocAboveCondition(2.5);

        // Assert
        Assert.That(condition.Name, Does.Contain("2.5%"));
    }

    [Test]
    public void RocBelowCondition_BelowThreshold_ReturnsTrue()
    {
        // Arrange
        var condition = new RocBelowCondition(-2.0);
        condition.GetRocValue = () => -3.5;

        // Act
        var result = condition.Evaluate(currentPrice: 150, vwap: 148);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void RocBelowCondition_AboveThreshold_ReturnsFalse()
    {
        // Arrange
        var condition = new RocBelowCondition(-2.0);
        condition.GetRocValue = () => -1.5;

        // Act
        var result = condition.Evaluate(currentPrice: 150, vwap: 148);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void RocBelowCondition_AtThreshold_ReturnsTrue()
    {
        // Arrange
        var condition = new RocBelowCondition(-2.0);
        condition.GetRocValue = () => -2.0;

        // Act
        var result = condition.Evaluate(currentPrice: 150, vwap: 148);

        // Assert
        Assert.That(result, Is.True);
    }

    #endregion

    // ========================================================================
    // CONTINUATION PATTERN CONDITION TESTS
    // ========================================================================
    //
    // Higher Lows Pattern (Bullish):
    //     ┌────────────────────────────────────────────────┐
    //     │                                    /\          │
    //     │                        /\         /  \         │
    //     │            /\         /  \       /    \        │
    //     │           /  \       /    \     /      \       │
    //     │          /    \     /      \   /        \      │
    //     │         /      \___/   ↑    \_/          \     │
    //     │        /        ↑    Higher              \    │
    //     │       /       Higher  Low                 \   │
    //     │      /         Low                         \  │
    //     └────────────────────────────────────────────────┘
    //              Low 1 < Low 2 < Low 3 = Bullish
    //
    // EMA Turning Up:
    //     ┌────────────────────────────────────────────────┐
    //     │                                     ___/       │
    //     │                                 ___/           │
    //     │                             ___/ ← Turning up  │
    //     │   \_____                ___/                   │
    //     │         \______________/                       │
    //     │              ↑ Flattening                      │
    //     └────────────────────────────────────────────────┘
    //
    // ========================================================================

    #region Higher Lows Condition Tests

    [Test]
    public void HigherLowsCondition_AscendingLows_ReturnsTrue()
    {
        // Arrange
        var condition = new HigherLowsCondition(3);
        // Lows: most recent first - 150, 148, 145 (each higher than previous)
        condition.GetRecentLows = () => [150.0, 148.0, 145.0];

        // Act
        var result = condition.Evaluate(currentPrice: 155, vwap: 150);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void HigherLowsCondition_DescendingLows_ReturnsFalse()
    {
        // Arrange
        var condition = new HigherLowsCondition(3);
        // Lows: most recent first - 145, 148, 150 (lower lows = bearish)
        condition.GetRecentLows = () => [145.0, 148.0, 150.0];

        // Act
        var result = condition.Evaluate(currentPrice: 155, vwap: 150);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void HigherLowsCondition_EqualLows_ReturnsFalse()
    {
        // Arrange
        var condition = new HigherLowsCondition(3);
        // Equal lows = no clear pattern
        condition.GetRecentLows = () => [150.0, 150.0, 150.0];

        // Act
        var result = condition.Evaluate(currentPrice: 155, vwap: 150);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void HigherLowsCondition_WithoutCallback_ReturnsFalse()
    {
        // Arrange
        var condition = new HigherLowsCondition(3);

        // Act
        var result = condition.Evaluate(currentPrice: 155, vwap: 150);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void HigherLowsCondition_InsufficientData_ReturnsFalse()
    {
        // Arrange
        var condition = new HigherLowsCondition(3);
        condition.GetRecentLows = () => [150.0]; // Only 1 value

        // Act
        var result = condition.Evaluate(currentPrice: 155, vwap: 150);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void HigherLowsCondition_Name_IsDescriptive()
    {
        // Arrange
        var condition = new HigherLowsCondition(3);

        // Assert
        Assert.That(condition.Name, Does.Contain("Higher Lows"));
    }

    [Test]
    public void HigherLowsCondition_MinimumLookback_ThrowsForLessThan2()
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => new HigherLowsCondition(1));
    }

    #endregion

    #region EMA Turning Up Condition Tests

    [Test]
    public void EmaTurningUpCondition_PositiveSlope_ReturnsTrue()
    {
        // Arrange
        var condition = new EmaTurningUpCondition(9);
        condition.GetCurrentEmaValue = () => 150.0;
        condition.GetPreviousEmaValue = () => 149.0; // Current > Previous = turning up

        // Act
        var result = condition.Evaluate(currentPrice: 155, vwap: 150);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void EmaTurningUpCondition_NegativeSlope_ReturnsFalse()
    {
        // Arrange
        var condition = new EmaTurningUpCondition(9);
        condition.GetCurrentEmaValue = () => 148.0;
        condition.GetPreviousEmaValue = () => 150.0; // Current < Previous = turning down

        // Act
        var result = condition.Evaluate(currentPrice: 155, vwap: 150);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void EmaTurningUpCondition_FlatSlope_ReturnsTrue()
    {
        // Arrange - flat is considered "not falling"
        var condition = new EmaTurningUpCondition(9);
        condition.GetCurrentEmaValue = () => 150.0;
        condition.GetPreviousEmaValue = () => 150.0; // Equal = flat (acceptable)

        // Act
        var result = condition.Evaluate(currentPrice: 155, vwap: 150);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void EmaTurningUpCondition_WithoutCallbacks_ReturnsFalse()
    {
        // Arrange
        var condition = new EmaTurningUpCondition(9);

        // Act
        var result = condition.Evaluate(currentPrice: 155, vwap: 150);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void EmaTurningUpCondition_ZeroEmaValues_ReturnsFalse()
    {
        // Arrange
        var condition = new EmaTurningUpCondition(9);
        condition.GetCurrentEmaValue = () => 0;
        condition.GetPreviousEmaValue = () => 0;

        // Act
        var result = condition.Evaluate(currentPrice: 155, vwap: 150);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void EmaTurningUpCondition_Name_ContainsPeriod()
    {
        // Arrange
        var condition = new EmaTurningUpCondition(21);

        // Assert
        Assert.That(condition.Name, Does.Contain("21"));
        Assert.That(condition.Name, Does.Contain("Turning Up"));
    }

    [Test]
    public void EmaTurningUpCondition_Period_MustBePositive()
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => new EmaTurningUpCondition(0));
    }

    #endregion

    // ========================================================================
    // VOLUME CONDITION TESTS
    // ========================================================================
    //
    // Volume Spike Visualization:
    //     ┌────────────────────────────────────────────────┐
    //     │                    ████                        │
    //     │                    ████                        │
    //     │  Average ─────────────────────────────────────│
    //     │    ████      ████  ████      ████             │
    //     │    ████  ██  ████  ████  ██  ████             │
    //     │    ████  ██  ████  ████  ██  ████             │
    //     └────────────────────────────────────────────────┘
    //                    ↑ Volume spike (1.5x+ average)
    //
    // ========================================================================

    #region Volume Above Condition Tests

    [Test]
    public void VolumeAboveCondition_AboveMultiplier_ReturnsTrue()
    {
        // Arrange
        var condition = new VolumeAboveCondition(1.5);
        condition.GetCurrentVolume = () => 1_500_000;
        condition.GetAverageVolume = () => 1_000_000;

        // Act
        var result = condition.Evaluate(currentPrice: 150, vwap: 148);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void VolumeAboveCondition_BelowMultiplier_ReturnsFalse()
    {
        // Arrange
        var condition = new VolumeAboveCondition(1.5);
        condition.GetCurrentVolume = () => 1_200_000;
        condition.GetAverageVolume = () => 1_000_000;

        // Act
        var result = condition.Evaluate(currentPrice: 150, vwap: 148);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void VolumeAboveCondition_AtMultiplier_ReturnsTrue()
    {
        // Arrange
        var condition = new VolumeAboveCondition(1.5);
        condition.GetCurrentVolume = () => 1_500_000;
        condition.GetAverageVolume = () => 1_000_000;

        // Act
        var result = condition.Evaluate(currentPrice: 150, vwap: 148);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void VolumeAboveCondition_WithoutCallbacks_ReturnsFalse()
    {
        // Arrange
        var condition = new VolumeAboveCondition(1.5);

        // Act
        var result = condition.Evaluate(currentPrice: 150, vwap: 148);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void VolumeAboveCondition_ZeroVolume_ReturnsFalse()
    {
        // Arrange
        var condition = new VolumeAboveCondition(1.5);
        condition.GetCurrentVolume = () => 0;
        condition.GetAverageVolume = () => 1_000_000;

        // Act
        var result = condition.Evaluate(currentPrice: 150, vwap: 148);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void VolumeAboveCondition_ZeroAverage_ReturnsFalse()
    {
        // Arrange
        var condition = new VolumeAboveCondition(1.5);
        condition.GetCurrentVolume = () => 1_500_000;
        condition.GetAverageVolume = () => 0;

        // Act
        var result = condition.Evaluate(currentPrice: 150, vwap: 148);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void VolumeAboveCondition_Name_ContainsMultiplier()
    {
        // Arrange
        var condition = new VolumeAboveCondition(2.0);

        // Assert
        Assert.That(condition.Name, Does.Contain("2.0x"));
    }

    [Test]
    public void VolumeAboveCondition_Multiplier_MustBePositive()
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => new VolumeAboveCondition(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new VolumeAboveCondition(-1));
    }

    #endregion

    // ========================================================================
    // VWAP PATTERN CONDITION TESTS
    // ========================================================================
    //
    // Close Above VWAP (Strong Bullish):
    //     ┌────────────────────────────────────────────────┐
    //     │         ┌───┐                                  │
    //     │         │   │ ← Close above VWAP              │
    //     │  VWAP ══│═══│══════════════════════════════════│
    //     │         │   │                                  │
    //     │         └───┘                                  │
    //     └────────────────────────────────────────────────┘
    //
    // VWAP Rejection (Bearish):
    //     ┌────────────────────────────────────────────────┐
    //     │           │ ← Wick above VWAP                 │
    //     │  VWAP ════╪═══════════════════════════════════│
    //     │         ┌─┴─┐                                  │
    //     │         │   │ ← Close below VWAP (rejected)   │
    //     │         └───┘                                  │
    //     └────────────────────────────────────────────────┘
    //
    // ========================================================================

    #region Close Above VWAP Condition Tests

    [Test]
    public void CloseAboveVwapCondition_CloseAboveVwap_ReturnsTrue()
    {
        // Arrange
        var condition = new CloseAboveVwapCondition();
        condition.GetLastClose = () => 152.0;

        // Act - VWAP = 150
        var result = condition.Evaluate(currentPrice: 151, vwap: 150);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void CloseAboveVwapCondition_CloseBelowVwap_ReturnsFalse()
    {
        // Arrange
        var condition = new CloseAboveVwapCondition();
        condition.GetLastClose = () => 148.0;

        // Act - VWAP = 150
        var result = condition.Evaluate(currentPrice: 151, vwap: 150);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void CloseAboveVwapCondition_CloseAtVwap_ReturnsFalse()
    {
        // Arrange - must be strictly above
        var condition = new CloseAboveVwapCondition();
        condition.GetLastClose = () => 150.0;

        // Act - VWAP = 150
        var result = condition.Evaluate(currentPrice: 151, vwap: 150);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void CloseAboveVwapCondition_WithoutCallback_ReturnsFalse()
    {
        // Arrange
        var condition = new CloseAboveVwapCondition();

        // Act
        var result = condition.Evaluate(currentPrice: 151, vwap: 150);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void CloseAboveVwapCondition_ZeroClose_ReturnsFalse()
    {
        // Arrange
        var condition = new CloseAboveVwapCondition();
        condition.GetLastClose = () => 0;

        // Act
        var result = condition.Evaluate(currentPrice: 151, vwap: 150);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void CloseAboveVwapCondition_ZeroVwap_ReturnsFalse()
    {
        // Arrange
        var condition = new CloseAboveVwapCondition();
        condition.GetLastClose = () => 152.0;

        // Act
        var result = condition.Evaluate(currentPrice: 151, vwap: 0);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void CloseAboveVwapCondition_Name_IsDescriptive()
    {
        // Arrange
        var condition = new CloseAboveVwapCondition();

        // Assert
        Assert.That(condition.Name, Is.EqualTo("Close Above VWAP"));
    }

    #endregion

    #region VWAP Rejection Condition Tests

    [Test]
    public void VwapRejectionCondition_WickAboveCloseBelowVwap_ReturnsTrue()
    {
        // Arrange - Classic rejection pattern
        var condition = new VwapRejectionCondition();
        condition.GetLastHigh = () => 152.0; // High went above VWAP
        condition.GetLastClose = () => 148.0; // But closed below VWAP

        // Act - VWAP = 150
        var result = condition.Evaluate(currentPrice: 149, vwap: 150);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void VwapRejectionCondition_HighAndCloseBothAboveVwap_ReturnsFalse()
    {
        // Arrange - Not a rejection, closed above
        var condition = new VwapRejectionCondition();
        condition.GetLastHigh = () => 155.0;
        condition.GetLastClose = () => 152.0; // Closed above VWAP

        // Act - VWAP = 150
        var result = condition.Evaluate(currentPrice: 151, vwap: 150);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void VwapRejectionCondition_HighAndCloseBothBelowVwap_ReturnsFalse()
    {
        // Arrange - Never even touched VWAP
        var condition = new VwapRejectionCondition();
        condition.GetLastHigh = () => 148.0; // Never reached VWAP
        condition.GetLastClose = () => 145.0;

        // Act - VWAP = 150
        var result = condition.Evaluate(currentPrice: 146, vwap: 150);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void VwapRejectionCondition_HighAtVwapCloseBelowVwap_ReturnsFalse()
    {
        // Arrange - High must be strictly ABOVE VWAP
        var condition = new VwapRejectionCondition();
        condition.GetLastHigh = () => 150.0; // At VWAP, not above
        condition.GetLastClose = () => 148.0;

        // Act - VWAP = 150
        var result = condition.Evaluate(currentPrice: 149, vwap: 150);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void VwapRejectionCondition_WithoutCallbacks_ReturnsFalse()
    {
        // Arrange
        var condition = new VwapRejectionCondition();

        // Act
        var result = condition.Evaluate(currentPrice: 149, vwap: 150);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void VwapRejectionCondition_ZeroValues_ReturnsFalse()
    {
        // Arrange
        var condition = new VwapRejectionCondition();
        condition.GetLastHigh = () => 0;
        condition.GetLastClose = () => 0;

        // Act
        var result = condition.Evaluate(currentPrice: 149, vwap: 150);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void VwapRejectionCondition_ZeroVwap_ReturnsFalse()
    {
        // Arrange
        var condition = new VwapRejectionCondition();
        condition.GetLastHigh = () => 152.0;
        condition.GetLastClose = () => 148.0;

        // Act
        var result = condition.Evaluate(currentPrice: 149, vwap: 0);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void VwapRejectionCondition_Name_IsDescriptive()
    {
        // Arrange
        var condition = new VwapRejectionCondition();

        // Assert
        Assert.That(condition.Name, Is.EqualTo("VWAP Rejection"));
    }

    #endregion

    // ========================================================================
    // EMA CONDITION TESTS
    // ========================================================================
    //
    // EMA (Exponential Moving Average) Visualization:
    //
    //     Price vs EMA(9) - Short-term trend
    //     +--------------------------------------------+
    //     |     /\                                     |
    //     |    /  \        ___/                        |
    //     |   /    \______/    <- Price above EMA     |
    //     |  /     EMA(9) ___                         |
    //     | /  ___________/   \____                   |
    //     |/__/                    \___  <- Below EMA |
    //     +--------------------------------------------+
    //
    //     Price Between EMA(9) and EMA(21):
    //     +--------------------------------------------+
    //     |  EMA(21) ______________________________    |
    //     |         /                              \   |
    //     |  ______/  * * * Price between * * *    \__|
    //     | /     EMA(9) ____________________________ |
    //     |/___________________________________________|
    //     +--------------------------------------------+
    //
    // ========================================================================

    #region EMA Above Condition Tests

    [Test]
    public void EmaAboveCondition_PriceAboveEma_ReturnsTrue()
    {
        // Arrange
        var condition = new EmaAboveCondition(9);
        condition.GetEmaValue = () => 148.0;

        // Act - Price 150, EMA 148
        var result = condition.Evaluate(currentPrice: 150, vwap: 145);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void EmaAboveCondition_PriceBelowEma_ReturnsFalse()
    {
        // Arrange
        var condition = new EmaAboveCondition(9);
        condition.GetEmaValue = () => 152.0;

        // Act - Price 150, EMA 152
        var result = condition.Evaluate(currentPrice: 150, vwap: 145);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void EmaAboveCondition_PriceAtEma_ReturnsTrue()
    {
        // Arrange - >= comparison
        var condition = new EmaAboveCondition(9);
        condition.GetEmaValue = () => 150.0;

        // Act - Price equals EMA
        var result = condition.Evaluate(currentPrice: 150, vwap: 145);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void EmaAboveCondition_WithoutCallback_ReturnsFalse()
    {
        // Arrange
        var condition = new EmaAboveCondition(9);

        // Act
        var result = condition.Evaluate(currentPrice: 150, vwap: 145);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void EmaAboveCondition_ZeroEmaValue_ReturnsFalse()
    {
        // Arrange
        var condition = new EmaAboveCondition(9);
        condition.GetEmaValue = () => 0;

        // Act
        var result = condition.Evaluate(currentPrice: 150, vwap: 145);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void EmaAboveCondition_Name_ContainsPeriod()
    {
        // Arrange
        var condition = new EmaAboveCondition(21);

        // Assert
        Assert.That(condition.Name, Does.Contain("21"));
        Assert.That(condition.Name, Does.Contain("EMA"));
    }

    [Test]
    public void EmaAboveCondition_Period_MustBePositive()
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => new EmaAboveCondition(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EmaAboveCondition(-1));
    }

    [Test]
    public void EmaAboveCondition_Period_IsStored()
    {
        // Arrange
        var condition = new EmaAboveCondition(200);

        // Assert
        Assert.That(condition.Period, Is.EqualTo(200));
    }

    #endregion

    #region EMA Below Condition Tests

    [Test]
    public void EmaBelowCondition_PriceBelowEma_ReturnsTrue()
    {
        // Arrange
        var condition = new EmaBelowCondition(9);
        condition.GetEmaValue = () => 152.0;

        // Act - Price 150, EMA 152
        var result = condition.Evaluate(currentPrice: 150, vwap: 145);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void EmaBelowCondition_PriceAboveEma_ReturnsFalse()
    {
        // Arrange
        var condition = new EmaBelowCondition(9);
        condition.GetEmaValue = () => 148.0;

        // Act - Price 150, EMA 148
        var result = condition.Evaluate(currentPrice: 150, vwap: 145);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void EmaBelowCondition_PriceAtEma_ReturnsTrue()
    {
        // Arrange - <= comparison
        var condition = new EmaBelowCondition(9);
        condition.GetEmaValue = () => 150.0;

        // Act - Price equals EMA
        var result = condition.Evaluate(currentPrice: 150, vwap: 145);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void EmaBelowCondition_WithoutCallback_ReturnsFalse()
    {
        // Arrange
        var condition = new EmaBelowCondition(9);

        // Act
        var result = condition.Evaluate(currentPrice: 150, vwap: 145);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void EmaBelowCondition_ZeroEmaValue_ReturnsFalse()
    {
        // Arrange
        var condition = new EmaBelowCondition(9);
        condition.GetEmaValue = () => 0;

        // Act
        var result = condition.Evaluate(currentPrice: 150, vwap: 145);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void EmaBelowCondition_Name_ContainsPeriod()
    {
        // Arrange
        var condition = new EmaBelowCondition(50);

        // Assert
        Assert.That(condition.Name, Does.Contain("50"));
        Assert.That(condition.Name, Does.Contain("EMA"));
    }

    [Test]
    public void EmaBelowCondition_Period_MustBePositive()
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => new EmaBelowCondition(0));
    }

    #endregion

    #region EMA Between Condition Tests

    [Test]
    public void EmaBetweenCondition_PriceBetweenEmas_ReturnsTrue()
    {
        // Arrange
        var condition = new EmaBetweenCondition(9, 21);
        condition.GetLowerEmaValue = () => 148.0; // EMA(9)
        condition.GetUpperEmaValue = () => 152.0; // EMA(21)

        // Act - Price 150 is between 148 and 152
        var result = condition.Evaluate(currentPrice: 150, vwap: 145);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void EmaBetweenCondition_PriceAboveBothEmas_ReturnsFalse()
    {
        // Arrange
        var condition = new EmaBetweenCondition(9, 21);
        condition.GetLowerEmaValue = () => 145.0;
        condition.GetUpperEmaValue = () => 148.0;

        // Act - Price 150 is above both
        var result = condition.Evaluate(currentPrice: 150, vwap: 145);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void EmaBetweenCondition_PriceBelowBothEmas_ReturnsFalse()
    {
        // Arrange
        var condition = new EmaBetweenCondition(9, 21);
        condition.GetLowerEmaValue = () => 152.0;
        condition.GetUpperEmaValue = () => 155.0;

        // Act - Price 150 is below both
        var result = condition.Evaluate(currentPrice: 150, vwap: 145);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void EmaBetweenCondition_PriceAtLowerEma_ReturnsTrue()
    {
        // Arrange - >= minEma comparison
        var condition = new EmaBetweenCondition(9, 21);
        condition.GetLowerEmaValue = () => 150.0;
        condition.GetUpperEmaValue = () => 155.0;

        // Act - Price equals lower EMA
        var result = condition.Evaluate(currentPrice: 150, vwap: 145);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void EmaBetweenCondition_PriceAtUpperEma_ReturnsTrue()
    {
        // Arrange - <= maxEma comparison
        var condition = new EmaBetweenCondition(9, 21);
        condition.GetLowerEmaValue = () => 145.0;
        condition.GetUpperEmaValue = () => 150.0;

        // Act - Price equals upper EMA
        var result = condition.Evaluate(currentPrice: 150, vwap: 145);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void EmaBetweenCondition_ReversedEmas_StillWorks()
    {
        // Arrange - EMAs can cross, so the condition handles it
        var condition = new EmaBetweenCondition(9, 21);
        condition.GetLowerEmaValue = () => 155.0; // "Lower period" EMA is actually higher
        condition.GetUpperEmaValue = () => 145.0; // "Upper period" EMA is actually lower

        // Act - Price 150 should still be between 145 and 155
        var result = condition.Evaluate(currentPrice: 150, vwap: 145);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void EmaBetweenCondition_WithoutCallbacks_ReturnsFalse()
    {
        // Arrange
        var condition = new EmaBetweenCondition(9, 21);

        // Act
        var result = condition.Evaluate(currentPrice: 150, vwap: 145);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void EmaBetweenCondition_ZeroLowerEma_ReturnsFalse()
    {
        // Arrange
        var condition = new EmaBetweenCondition(9, 21);
        condition.GetLowerEmaValue = () => 0;
        condition.GetUpperEmaValue = () => 155.0;

        // Act
        var result = condition.Evaluate(currentPrice: 150, vwap: 145);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void EmaBetweenCondition_ZeroUpperEma_ReturnsFalse()
    {
        // Arrange
        var condition = new EmaBetweenCondition(9, 21);
        condition.GetLowerEmaValue = () => 145.0;
        condition.GetUpperEmaValue = () => 0;

        // Act
        var result = condition.Evaluate(currentPrice: 150, vwap: 145);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void EmaBetweenCondition_Name_ContainsBothPeriods()
    {
        // Arrange
        var condition = new EmaBetweenCondition(9, 21);

        // Assert
        Assert.That(condition.Name, Does.Contain("9"));
        Assert.That(condition.Name, Does.Contain("21"));
    }

    [Test]
    public void EmaBetweenCondition_Periods_MustBePositive()
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => new EmaBetweenCondition(0, 21));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EmaBetweenCondition(9, 0));
    }

    [Test]
    public void EmaBetweenCondition_Periods_AreStored()
    {
        // Arrange
        var condition = new EmaBetweenCondition(9, 200);

        // Assert
        Assert.That(condition.LowerPeriod, Is.EqualTo(9));
        Assert.That(condition.UpperPeriod, Is.EqualTo(200));
    }

    #endregion

    #region Fluent API EMA Integration Tests

    [Test]
    public void FluentApi_IsEmaAbove_CreatesConditionInStrategy()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .IsEmaAbove(9)
            .Breakout(150)
            .Buy(100, Price.Current)
            .Build();

        // Assert
        Assert.That(strategy.Conditions, Has.Count.EqualTo(2));
        Assert.That(strategy.Conditions[0], Is.TypeOf<EmaAboveCondition>());
        var emaCondition = strategy.Conditions[0] as EmaAboveCondition;
        Assert.That(emaCondition!.Period, Is.EqualTo(9));
    }

    [Test]
    public void FluentApi_IsEmaBelow_CreatesConditionInStrategy()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .IsEmaBelow(200)
            .Breakout(150)
            .Buy(100, Price.Current)
            .Build();

        // Assert
        Assert.That(strategy.Conditions, Has.Count.EqualTo(2));
        Assert.That(strategy.Conditions[0], Is.TypeOf<EmaBelowCondition>());
        var emaCondition = strategy.Conditions[0] as EmaBelowCondition;
        Assert.That(emaCondition!.Period, Is.EqualTo(200));
    }

    [Test]
    public void FluentApi_IsEmaBetween_CreatesConditionInStrategy()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .IsEmaBetween(9, 21)
            .Breakout(150)
            .Buy(100, Price.Current)
            .Build();

        // Assert
        Assert.That(strategy.Conditions, Has.Count.EqualTo(2));
        Assert.That(strategy.Conditions[0], Is.TypeOf<EmaBetweenCondition>());
        var emaCondition = strategy.Conditions[0] as EmaBetweenCondition;
        Assert.That(emaCondition!.LowerPeriod, Is.EqualTo(9));
        Assert.That(emaCondition.UpperPeriod, Is.EqualTo(21));
    }

    [Test]
    public void FluentApi_MultipleEmas_CanBeChained()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .IsEmaAbove(9)
            .IsEmaAbove(21)
            .IsEmaBelow(200)
            .Breakout(150)
            .Buy(100, Price.Current)
            .Build();

        // Assert
        Assert.That(strategy.Conditions, Has.Count.EqualTo(4));
        Assert.That(strategy.Conditions[0], Is.TypeOf<EmaAboveCondition>());
        Assert.That(strategy.Conditions[1], Is.TypeOf<EmaAboveCondition>());
        Assert.That(strategy.Conditions[2], Is.TypeOf<EmaBelowCondition>());
    }

    #endregion
}
