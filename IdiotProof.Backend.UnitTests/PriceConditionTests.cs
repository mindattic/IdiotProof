// ============================================================================
// PriceConditionTests - Comprehensive tests for all price-based conditions
// ============================================================================
//
// This file contains comprehensive unit tests covering:
// 1. BreakoutCondition - Price >= level
// 2. PullbackCondition - Price <= level
// 3. AboveVwapCondition - Price >= VWAP (+ optional buffer)
// 4. BelowVwapCondition - Price <= VWAP (- optional buffer)
// 5. PriceAtOrAboveCondition - Price >= level
// 6. PriceBelowCondition - Price < level
// 7. CustomCondition - User-defined condition delegates
//
// ============================================================================

using IdiotProof.Backend.Enums;
using IdiotProof.Backend.Models;
using IdiotProof.Backend.Strategy;
using NUnit.Framework;

namespace IdiotProof.Backend.UnitTests;

/// <summary>
/// Comprehensive tests for all price-based strategy conditions.
/// </summary>
[TestFixture]
public class PriceConditionTests
{
    #region BreakoutCondition Tests

    [Test]
    public void BreakoutCondition_PriceAboveLevel_ReturnsTrue()
    {
        // Arrange
        var condition = new BreakoutCondition(100.00);

        // Act
        var result = condition.Evaluate(currentPrice: 105.00, vwap: 100.00);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void BreakoutCondition_PriceAtLevel_ReturnsTrue()
    {
        // Arrange - Breakout is >= level, so exactly at level should pass
        var condition = new BreakoutCondition(100.00);

        // Act
        var result = condition.Evaluate(currentPrice: 100.00, vwap: 100.00);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void BreakoutCondition_PriceBelowLevel_ReturnsFalse()
    {
        // Arrange
        var condition = new BreakoutCondition(100.00);

        // Act
        var result = condition.Evaluate(currentPrice: 99.99, vwap: 100.00);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void BreakoutCondition_Level_StoredCorrectly()
    {
        // Arrange & Act
        var condition = new BreakoutCondition(150.50);

        // Assert
        Assert.That(condition.Level, Is.EqualTo(150.50));
    }

    [Test]
    public void BreakoutCondition_Name_FormattedCorrectly()
    {
        // Arrange & Act
        var condition = new BreakoutCondition(150.50);

        // Assert
        Assert.That(condition.Name, Is.EqualTo("Breakout >= 150.50"));
    }

    [Test]
    public void BreakoutCondition_IgnoresVwap()
    {
        // Arrange - VWAP value shouldn't affect breakout condition
        var condition = new BreakoutCondition(100.00);

        // Act - Price above level, but VWAP is different
        var result1 = condition.Evaluate(currentPrice: 101.00, vwap: 0);
        var result2 = condition.Evaluate(currentPrice: 101.00, vwap: 200.00);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result1, Is.True);
            Assert.That(result2, Is.True);
        });
    }

    [Test]
    public void BreakoutCondition_ZeroLevel_WorksCorrectly()
    {
        // Arrange
        var condition = new BreakoutCondition(0);

        // Act
        var resultPositive = condition.Evaluate(currentPrice: 1.00, vwap: 0);
        var resultZero = condition.Evaluate(currentPrice: 0, vwap: 0);
        var resultNegative = condition.Evaluate(currentPrice: -1.00, vwap: 0);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(resultPositive, Is.True);
            Assert.That(resultZero, Is.True);
            Assert.That(resultNegative, Is.False);
        });
    }

    [Test]
    public void BreakoutCondition_NegativeLevel_WorksCorrectly()
    {
        // Arrange - Edge case: negative levels (e.g., futures)
        var condition = new BreakoutCondition(-50.00);

        // Act
        var resultAbove = condition.Evaluate(currentPrice: -40.00, vwap: 0);
        var resultAt = condition.Evaluate(currentPrice: -50.00, vwap: 0);
        var resultBelow = condition.Evaluate(currentPrice: -60.00, vwap: 0);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(resultAbove, Is.True);
            Assert.That(resultAt, Is.True);
            Assert.That(resultBelow, Is.False);
        });
    }

    #endregion

    #region PullbackCondition Tests

    [Test]
    public void PullbackCondition_PriceBelowLevel_ReturnsTrue()
    {
        // Arrange
        var condition = new PullbackCondition(100.00);

        // Act
        var result = condition.Evaluate(currentPrice: 95.00, vwap: 100.00);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void PullbackCondition_PriceAtLevel_ReturnsTrue()
    {
        // Arrange - Pullback is <= level, so exactly at level should pass
        var condition = new PullbackCondition(100.00);

        // Act
        var result = condition.Evaluate(currentPrice: 100.00, vwap: 100.00);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void PullbackCondition_PriceAboveLevel_ReturnsFalse()
    {
        // Arrange
        var condition = new PullbackCondition(100.00);

        // Act
        var result = condition.Evaluate(currentPrice: 100.01, vwap: 100.00);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void PullbackCondition_Level_StoredCorrectly()
    {
        // Arrange & Act
        var condition = new PullbackCondition(85.25);

        // Assert
        Assert.That(condition.Level, Is.EqualTo(85.25));
    }

    [Test]
    public void PullbackCondition_Name_FormattedCorrectly()
    {
        // Arrange & Act
        var condition = new PullbackCondition(85.25);

        // Assert
        Assert.That(condition.Name, Is.EqualTo("Pullback <= 85.25"));
    }

    [Test]
    public void PullbackCondition_IgnoresVwap()
    {
        // Arrange - VWAP value shouldn't affect pullback condition
        var condition = new PullbackCondition(100.00);

        // Act
        var result1 = condition.Evaluate(currentPrice: 99.00, vwap: 0);
        var result2 = condition.Evaluate(currentPrice: 99.00, vwap: 200.00);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result1, Is.True);
            Assert.That(result2, Is.True);
        });
    }

    #endregion

    #region AboveVwapCondition Tests

    [Test]
    public void AboveVwapCondition_PriceAboveVwap_ReturnsTrue()
    {
        // Arrange
        var condition = new AboveVwapCondition();

        // Act
        var result = condition.Evaluate(currentPrice: 105.00, vwap: 100.00);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void AboveVwapCondition_PriceAtVwap_ReturnsTrue()
    {
        // Arrange - AboveVwap is >= VWAP, so at VWAP should pass
        var condition = new AboveVwapCondition();

        // Act
        var result = condition.Evaluate(currentPrice: 100.00, vwap: 100.00);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void AboveVwapCondition_PriceBelowVwap_ReturnsFalse()
    {
        // Arrange
        var condition = new AboveVwapCondition();

        // Act
        var result = condition.Evaluate(currentPrice: 99.99, vwap: 100.00);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void AboveVwapCondition_ZeroVwap_ReturnsFalse()
    {
        // Arrange - VWAP of 0 indicates unavailable data, should return false
        var condition = new AboveVwapCondition();

        // Act
        var result = condition.Evaluate(currentPrice: 100.00, vwap: 0);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void AboveVwapCondition_WithBuffer_PriceAboveVwapPlusBuffer_ReturnsTrue()
    {
        // Arrange - Buffer of 2.00
        var condition = new AboveVwapCondition(buffer: 2.00);

        // Act - Price must be >= VWAP + Buffer (100 + 2 = 102)
        var result = condition.Evaluate(currentPrice: 103.00, vwap: 100.00);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void AboveVwapCondition_WithBuffer_PriceAtVwapPlusBuffer_ReturnsTrue()
    {
        // Arrange
        var condition = new AboveVwapCondition(buffer: 2.00);

        // Act - Price exactly at VWAP + Buffer
        var result = condition.Evaluate(currentPrice: 102.00, vwap: 100.00);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void AboveVwapCondition_WithBuffer_PriceBelowVwapPlusBuffer_ReturnsFalse()
    {
        // Arrange
        var condition = new AboveVwapCondition(buffer: 2.00);

        // Act - Price below VWAP + Buffer
        var result = condition.Evaluate(currentPrice: 101.00, vwap: 100.00);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void AboveVwapCondition_Buffer_StoredCorrectly()
    {
        // Arrange & Act
        var condition = new AboveVwapCondition(buffer: 1.50);

        // Assert
        Assert.That(condition.Buffer, Is.EqualTo(1.50));
    }

    [Test]
    public void AboveVwapCondition_Name_NoBuffer_FormattedCorrectly()
    {
        // Arrange & Act
        var condition = new AboveVwapCondition();

        // Assert
        Assert.That(condition.Name, Is.EqualTo("Price >= VWAP"));
    }

    [Test]
    public void AboveVwapCondition_Name_WithBuffer_FormattedCorrectly()
    {
        // Arrange & Act
        var condition = new AboveVwapCondition(buffer: 1.50);

        // Assert
        Assert.That(condition.Name, Is.EqualTo("Price >= VWAP + 1.50"));
    }

    [Test]
    public void AboveVwapCondition_DefaultBuffer_IsZero()
    {
        // Arrange & Act
        var condition = new AboveVwapCondition();

        // Assert
        Assert.That(condition.Buffer, Is.EqualTo(0));
    }

    #endregion

    #region BelowVwapCondition Tests

    [Test]
    public void BelowVwapCondition_PriceBelowVwap_ReturnsTrue()
    {
        // Arrange
        var condition = new BelowVwapCondition();

        // Act
        var result = condition.Evaluate(currentPrice: 95.00, vwap: 100.00);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void BelowVwapCondition_PriceAtVwap_ReturnsTrue()
    {
        // Arrange - BelowVwap is <= VWAP, so at VWAP should pass
        var condition = new BelowVwapCondition();

        // Act
        var result = condition.Evaluate(currentPrice: 100.00, vwap: 100.00);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void BelowVwapCondition_PriceAboveVwap_ReturnsFalse()
    {
        // Arrange
        var condition = new BelowVwapCondition();

        // Act
        var result = condition.Evaluate(currentPrice: 100.01, vwap: 100.00);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void BelowVwapCondition_ZeroVwap_ReturnsFalse()
    {
        // Arrange - VWAP of 0 indicates unavailable data
        var condition = new BelowVwapCondition();

        // Act
        var result = condition.Evaluate(currentPrice: 100.00, vwap: 0);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void BelowVwapCondition_WithBuffer_PriceBelowVwapMinusBuffer_ReturnsTrue()
    {
        // Arrange - Buffer of 2.00
        var condition = new BelowVwapCondition(buffer: 2.00);

        // Act - Price must be <= VWAP - Buffer (100 - 2 = 98)
        var result = condition.Evaluate(currentPrice: 97.00, vwap: 100.00);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void BelowVwapCondition_WithBuffer_PriceAtVwapMinusBuffer_ReturnsTrue()
    {
        // Arrange
        var condition = new BelowVwapCondition(buffer: 2.00);

        // Act - Price exactly at VWAP - Buffer
        var result = condition.Evaluate(currentPrice: 98.00, vwap: 100.00);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void BelowVwapCondition_WithBuffer_PriceAboveVwapMinusBuffer_ReturnsFalse()
    {
        // Arrange
        var condition = new BelowVwapCondition(buffer: 2.00);

        // Act - Price above VWAP - Buffer
        var result = condition.Evaluate(currentPrice: 99.00, vwap: 100.00);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void BelowVwapCondition_Buffer_StoredCorrectly()
    {
        // Arrange & Act
        var condition = new BelowVwapCondition(buffer: 1.50);

        // Assert
        Assert.That(condition.Buffer, Is.EqualTo(1.50));
    }

    [Test]
    public void BelowVwapCondition_DefaultBuffer_IsZero()
    {
        // Arrange & Act
        var condition = new BelowVwapCondition();

        // Assert
        Assert.That(condition.Buffer, Is.EqualTo(0));
    }

    [Test]
    public void BelowVwapCondition_Name_NoBuffer_FormattedCorrectly()
    {
        // Arrange & Act
        var condition = new BelowVwapCondition();

        // Assert
        Assert.That(condition.Name, Is.EqualTo("Price <= VWAP"));
    }

    [Test]
    public void BelowVwapCondition_Name_WithBuffer_FormattedCorrectly()
    {
        // Arrange & Act
        var condition = new BelowVwapCondition(buffer: 1.50);

        // Assert
        Assert.That(condition.Name, Is.EqualTo("Price <= VWAP - 1.50"));
    }

    #endregion

    #region PriceAtOrAboveCondition Tests

    [Test]
    public void PriceAtOrAboveCondition_PriceAboveLevel_ReturnsTrue()
    {
        // Arrange
        var condition = new PriceAtOrAboveCondition(100.00);

        // Act
        var result = condition.Evaluate(currentPrice: 105.00, vwap: 100.00);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void PriceAtOrAboveCondition_PriceAtLevel_ReturnsTrue()
    {
        // Arrange
        var condition = new PriceAtOrAboveCondition(100.00);

        // Act
        var result = condition.Evaluate(currentPrice: 100.00, vwap: 100.00);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void PriceAtOrAboveCondition_PriceBelowLevel_ReturnsFalse()
    {
        // Arrange
        var condition = new PriceAtOrAboveCondition(100.00);

        // Act
        var result = condition.Evaluate(currentPrice: 99.99, vwap: 100.00);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void PriceAtOrAboveCondition_Name_FormattedCorrectly()
    {
        // Arrange & Act
        var condition = new PriceAtOrAboveCondition(125.75);

        // Assert
        Assert.That(condition.Name, Is.EqualTo("Price >= 125.75"));
    }

    [Test]
    public void PriceAtOrAboveCondition_Level_StoredCorrectly()
    {
        // Arrange & Act
        var condition = new PriceAtOrAboveCondition(125.75);

        // Assert
        Assert.That(condition.Level, Is.EqualTo(125.75));
    }

    [Test]
    public void PriceAtOrAboveCondition_IgnoresVwap()
    {
        // Arrange
        var condition = new PriceAtOrAboveCondition(100.00);

        // Act
        var result1 = condition.Evaluate(currentPrice: 101.00, vwap: 0);
        var result2 = condition.Evaluate(currentPrice: 101.00, vwap: 500.00);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result1, Is.True);
            Assert.That(result2, Is.True);
        });
    }

    #endregion

    #region PriceBelowCondition Tests

    [Test]
    public void PriceBelowCondition_PriceBelowLevel_ReturnsTrue()
    {
        // Arrange
        var condition = new PriceBelowCondition(100.00);

        // Act
        var result = condition.Evaluate(currentPrice: 99.99, vwap: 100.00);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void PriceBelowCondition_PriceAtLevel_ReturnsFalse()
    {
        // Arrange - PriceBelow is strictly < level, not <=
        var condition = new PriceBelowCondition(100.00);

        // Act
        var result = condition.Evaluate(currentPrice: 100.00, vwap: 100.00);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void PriceBelowCondition_PriceAboveLevel_ReturnsFalse()
    {
        // Arrange
        var condition = new PriceBelowCondition(100.00);

        // Act
        var result = condition.Evaluate(currentPrice: 100.01, vwap: 100.00);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void PriceBelowCondition_Name_FormattedCorrectly()
    {
        // Arrange & Act
        var condition = new PriceBelowCondition(75.50);

        // Assert
        Assert.That(condition.Name, Is.EqualTo("Price < 75.50"));
    }

    [Test]
    public void PriceBelowCondition_Level_StoredCorrectly()
    {
        // Arrange & Act
        var condition = new PriceBelowCondition(75.50);

        // Assert
        Assert.That(condition.Level, Is.EqualTo(75.50));
    }

    [Test]
    public void PriceBelowCondition_IgnoresVwap()
    {
        // Arrange
        var condition = new PriceBelowCondition(100.00);

        // Act
        var result1 = condition.Evaluate(currentPrice: 99.00, vwap: 0);
        var result2 = condition.Evaluate(currentPrice: 99.00, vwap: 500.00);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result1, Is.True);
            Assert.That(result2, Is.True);
        });
    }

    #endregion

    #region CustomCondition Tests

    [Test]
    public void CustomCondition_DelegateReturnsTrue_ReturnsTrue()
    {
        // Arrange
        var condition = new CustomCondition("Always True", (price, vwap) => true);

        // Act
        var result = condition.Evaluate(currentPrice: 100.00, vwap: 100.00);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void CustomCondition_DelegateReturnsFalse_ReturnsFalse()
    {
        // Arrange
        var condition = new CustomCondition("Always False", (price, vwap) => false);

        // Act
        var result = condition.Evaluate(currentPrice: 100.00, vwap: 100.00);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void CustomCondition_UsesPrice()
    {
        // Arrange - Condition that checks if price > 100
        var condition = new CustomCondition("Price > 100", (price, vwap) => price > 100);

        // Act
        var resultAbove = condition.Evaluate(currentPrice: 101.00, vwap: 50.00);
        var resultBelow = condition.Evaluate(currentPrice: 99.00, vwap: 50.00);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(resultAbove, Is.True);
            Assert.That(resultBelow, Is.False);
        });
    }

    [Test]
    public void CustomCondition_UsesVwap()
    {
        // Arrange - Condition that checks if price > VWAP * 1.05 (5% above VWAP)
        var condition = new CustomCondition("Price > VWAP * 1.05", (price, vwap) => price > vwap * 1.05);

        // Act
        var resultAbove = condition.Evaluate(currentPrice: 110.00, vwap: 100.00);
        var resultBelow = condition.Evaluate(currentPrice: 103.00, vwap: 100.00);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(resultAbove, Is.True);
            Assert.That(resultBelow, Is.False);
        });
    }

    [Test]
    public void CustomCondition_Name_StoredCorrectly()
    {
        // Arrange & Act
        var condition = new CustomCondition("Custom Condition Name", (price, vwap) => true);

        // Assert
        Assert.That(condition.Name, Is.EqualTo("Custom Condition Name"));
    }

    [Test]
    public void CustomCondition_NullName_ThrowsArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new CustomCondition(null!, (price, vwap) => true));
    }

    [Test]
    public void CustomCondition_NullEvaluator_ThrowsArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new CustomCondition("Test", null!));
    }

    [Test]
    public void CustomCondition_ComplexLogic_EvaluatesCorrectly()
    {
        // Arrange - Complex condition: price within 2% of VWAP and above $50
        var condition = new CustomCondition(
            "Price within 2% of VWAP and above $50",
            (price, vwap) => 
                price > 50 && 
                vwap > 0 && 
                Math.Abs(price - vwap) / vwap <= 0.02);

        // Act
        var resultValid = condition.Evaluate(currentPrice: 101.00, vwap: 100.00); // 1% diff, above $50
        var resultTooFar = condition.Evaluate(currentPrice: 105.00, vwap: 100.00); // 5% diff
        var resultTooLow = condition.Evaluate(currentPrice: 45.00, vwap: 44.00); // Within 2%, but below $50

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(resultValid, Is.True);
            Assert.That(resultTooFar, Is.False);
            Assert.That(resultTooLow, Is.False);
        });
    }

    #endregion

    #region Edge Cases and Boundary Tests

    [Test]
    public void AllConditions_VerySmallValues_WorkCorrectly()
    {
        // Arrange - Penny stocks
        var breakout = new BreakoutCondition(0.01);
        var pullback = new PullbackCondition(0.01);
        var aboveVwap = new AboveVwapCondition();
        var belowVwap = new BelowVwapCondition();

        // Act & Assert
        Assert.Multiple(() =>
        {
            Assert.That(breakout.Evaluate(0.02, 0.01), Is.True);
            Assert.That(breakout.Evaluate(0.005, 0.01), Is.False);
            Assert.That(pullback.Evaluate(0.005, 0.01), Is.True);
            Assert.That(pullback.Evaluate(0.02, 0.01), Is.False);
            Assert.That(aboveVwap.Evaluate(0.02, 0.01), Is.True);
            Assert.That(belowVwap.Evaluate(0.005, 0.01), Is.True);
        });
    }

    [Test]
    public void AllConditions_VeryLargeValues_WorkCorrectly()
    {
        // Arrange - High-priced stocks like BRK.A
        var breakout = new BreakoutCondition(500000.00);
        var pullback = new PullbackCondition(500000.00);
        var aboveVwap = new AboveVwapCondition();
        var belowVwap = new BelowVwapCondition();

        // Act & Assert
        Assert.Multiple(() =>
        {
            Assert.That(breakout.Evaluate(510000.00, 505000.00), Is.True);
            Assert.That(breakout.Evaluate(490000.00, 505000.00), Is.False);
            Assert.That(pullback.Evaluate(490000.00, 505000.00), Is.True);
            Assert.That(pullback.Evaluate(510000.00, 505000.00), Is.False);
            Assert.That(aboveVwap.Evaluate(510000.00, 500000.00), Is.True);
            Assert.That(belowVwap.Evaluate(490000.00, 500000.00), Is.True);
        });
    }

    [Test]
    public void VwapConditions_NegativeVwap_ReturnsFalse()
    {
        // Arrange - Invalid VWAP (negative shouldn't happen but test defensive coding)
        var aboveVwap = new AboveVwapCondition();
        var belowVwap = new BelowVwapCondition();

        // Act - Negative VWAP should be treated as invalid
        var resultAbove = aboveVwap.Evaluate(100.00, -50.00);
        var resultBelow = belowVwap.Evaluate(100.00, -50.00);

        // Assert - Condition checks vwap > 0, so negative should return false
        Assert.Multiple(() =>
        {
            Assert.That(resultAbove, Is.False);
            Assert.That(resultBelow, Is.False);
        });
    }

    [Test]
    public void BreakoutAndPullback_SameLevel_ComplementaryBehavior()
    {
        // Arrange - Same level for breakout (>=) and pullback (<=)
        var breakout = new BreakoutCondition(100.00);
        var pullback = new PullbackCondition(100.00);

        // Act & Assert - At the level, both should return true (>= and <=)
        Assert.Multiple(() =>
        {
            Assert.That(breakout.Evaluate(100.00, 0), Is.True);
            Assert.That(pullback.Evaluate(100.00, 0), Is.True);
            
            // Above level: breakout true, pullback false
            Assert.That(breakout.Evaluate(101.00, 0), Is.True);
            Assert.That(pullback.Evaluate(101.00, 0), Is.False);
            
            // Below level: breakout false, pullback true
            Assert.That(breakout.Evaluate(99.00, 0), Is.False);
            Assert.That(pullback.Evaluate(99.00, 0), Is.True);
        });
    }

    [Test]
    public void PriceAtOrAbove_Vs_PriceBelow_MutuallyExclusive()
    {
        // Arrange
        var atOrAbove = new PriceAtOrAboveCondition(100.00);
        var below = new PriceBelowCondition(100.00);

        // Act & Assert - These should be mutually exclusive
        Assert.Multiple(() =>
        {
            // At the level: atOrAbove true, below false (strict <)
            Assert.That(atOrAbove.Evaluate(100.00, 0), Is.True);
            Assert.That(below.Evaluate(100.00, 0), Is.False);
            
            // Above level
            Assert.That(atOrAbove.Evaluate(101.00, 0), Is.True);
            Assert.That(below.Evaluate(101.00, 0), Is.False);
            
            // Below level
            Assert.That(atOrAbove.Evaluate(99.00, 0), Is.False);
            Assert.That(below.Evaluate(99.00, 0), Is.True);
        });
    }

    #endregion

    #region Special Floating-Point Value Tests

    [Test]
    public void BreakoutCondition_NaNPrice_ReturnsFalse()
    {
        // Arrange
        var condition = new BreakoutCondition(100.00);

        // Act - NaN comparisons always return false
        var result = condition.Evaluate(currentPrice: double.NaN, vwap: 100.00);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void PullbackCondition_NaNPrice_ReturnsFalse()
    {
        // Arrange
        var condition = new PullbackCondition(100.00);

        // Act
        var result = condition.Evaluate(currentPrice: double.NaN, vwap: 100.00);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void AboveVwapCondition_NaNVwap_ReturnsFalse()
    {
        // Arrange
        var condition = new AboveVwapCondition();

        // Act - NaN > 0 is false, so condition should fail
        var result = condition.Evaluate(currentPrice: 100.00, vwap: double.NaN);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void BreakoutCondition_InfinityPrice_ReturnsTrue()
    {
        // Arrange
        var condition = new BreakoutCondition(100.00);

        // Act - Positive infinity is greater than any finite number
        var result = condition.Evaluate(currentPrice: double.PositiveInfinity, vwap: 100.00);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void PriceBelowCondition_NegativeInfinityPrice_ReturnsTrue()
    {
        // Arrange
        var condition = new PriceBelowCondition(100.00);

        // Act - Negative infinity is less than any finite number
        var result = condition.Evaluate(currentPrice: double.NegativeInfinity, vwap: 100.00);

        // Assert
        Assert.That(result, Is.True);
    }

    #endregion

    #region Fluent API Integration Tests

    [Test]
    public void FluentApi_Breakout_CreatesBreakoutCondition()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .Breakout(150.00)
            .Long().Quantity(100)
            .Build();

        // Assert
        Assert.That(strategy.Conditions, Has.Count.EqualTo(1));
        Assert.That(strategy.Conditions[0], Is.TypeOf<BreakoutCondition>());
        Assert.That(((BreakoutCondition)strategy.Conditions[0]).Level, Is.EqualTo(150.00));
    }

    [Test]
    public void FluentApi_Pullback_CreatesPullbackCondition()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .Pullback(140.00)
            .Long().Quantity(100)
            .Build();

        // Assert
        Assert.That(strategy.Conditions, Has.Count.EqualTo(1));
        Assert.That(strategy.Conditions[0], Is.TypeOf<PullbackCondition>());
        Assert.That(((PullbackCondition)strategy.Conditions[0]).Level, Is.EqualTo(140.00));
    }

    [Test]
    public void FluentApi_IsAboveVwap_CreatesAboveVwapCondition()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .IsAboveVwap()
            .Long().Quantity(100)
            .Build();

        // Assert
        Assert.That(strategy.Conditions, Has.Count.EqualTo(1));
        Assert.That(strategy.Conditions[0], Is.TypeOf<AboveVwapCondition>());
    }

    [Test]
    public void FluentApi_IsAboveVwap_WithBuffer_StoresBuffer()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .IsAboveVwap(1.50)
            .Long().Quantity(100)
            .Build();

        // Assert
        var condition = (AboveVwapCondition)strategy.Conditions[0];
        Assert.That(condition.Buffer, Is.EqualTo(1.50));
    }

    [Test]
    public void FluentApi_IsBelowVwap_CreatesBelowVwapCondition()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .IsBelowVwap()
            .Short().Quantity(100)
            .Build();

        // Assert
        Assert.That(strategy.Conditions, Has.Count.EqualTo(1));
        Assert.That(strategy.Conditions[0], Is.TypeOf<BelowVwapCondition>());
    }

    [Test]
    public void FluentApi_IsPriceAbove_CreatesPriceAtOrAboveCondition()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .IsPriceAbove(150.00)
            .Long().Quantity(100)
            .Build();

        // Assert
        Assert.That(strategy.Conditions, Has.Count.EqualTo(1));
        Assert.That(strategy.Conditions[0], Is.TypeOf<PriceAtOrAboveCondition>());
    }

    [Test]
    public void FluentApi_IsPriceBelow_CreatesPriceBelowCondition()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .IsPriceBelow(140.00)
            .Long().Quantity(100)
            .Build();

        // Assert
        Assert.That(strategy.Conditions, Has.Count.EqualTo(1));
        Assert.That(strategy.Conditions[0], Is.TypeOf<PriceBelowCondition>());
    }

    [Test]
    public void FluentApi_When_CreatesCustomCondition()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .When("Custom Test", (price, vwap) => price > vwap)
            .Long().Quantity(100)
            .Build();

        // Assert
        Assert.That(strategy.Conditions, Has.Count.EqualTo(1));
        Assert.That(strategy.Conditions[0], Is.TypeOf<CustomCondition>());
        Assert.That(strategy.Conditions[0].Name, Is.EqualTo("Custom Test"));
    }

    [Test]
    public void FluentApi_MultipleConditions_AllAdded()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .Breakout(150.00)
            .IsAboveVwap()
            .IsPriceAbove(145.00)
            .Long().Quantity(100)
            .Build();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(strategy.Conditions, Has.Count.EqualTo(3));
            Assert.That(strategy.Conditions[0], Is.TypeOf<BreakoutCondition>());
            Assert.That(strategy.Conditions[1], Is.TypeOf<AboveVwapCondition>());
            Assert.That(strategy.Conditions[2], Is.TypeOf<PriceAtOrAboveCondition>());
        });
    }

    #endregion

    #region GapUpCondition Tests

    [Test]
    public void GapUpCondition_PriceGappedUp5Percent_ReturnsTrue()
    {
        // Arrange - Previous close at 100, current price at 106 (6% gap)
        var condition = new GapUpCondition(5);
        condition.SetPreviousClose(100.00);

        // Act
        var result = condition.Evaluate(currentPrice: 106.00, vwap: 100.00);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void GapUpCondition_PriceGappedUpExactly5Percent_ReturnsTrue()
    {
        // Arrange - Previous close at 100, current price at 105 (exactly 5% gap)
        var condition = new GapUpCondition(5);
        condition.SetPreviousClose(100.00);

        // Act
        var result = condition.Evaluate(currentPrice: 105.00, vwap: 100.00);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void GapUpCondition_PriceGappedUpLessThan5Percent_ReturnsFalse()
    {
        // Arrange - Previous close at 100, current price at 104 (4% gap)
        var condition = new GapUpCondition(5);
        condition.SetPreviousClose(100.00);

        // Act
        var result = condition.Evaluate(currentPrice: 104.00, vwap: 100.00);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void GapUpCondition_PriceGappedDown_ReturnsFalse()
    {
        // Arrange - Previous close at 100, current price at 95 (gapped down)
        var condition = new GapUpCondition(5);
        condition.SetPreviousClose(100.00);

        // Act
        var result = condition.Evaluate(currentPrice: 95.00, vwap: 100.00);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void GapUpCondition_NoPreviousCloseSet_ReturnsFalse()
    {
        // Arrange - Previous close not set
        var condition = new GapUpCondition(5);

        // Act
        var result = condition.Evaluate(currentPrice: 110.00, vwap: 100.00);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void GapUpCondition_Percentage_StoredCorrectly()
    {
        // Arrange & Act
        var condition = new GapUpCondition(7.5);

        // Assert
        Assert.That(condition.Percentage, Is.EqualTo(7.5));
    }

    [Test]
    public void GapUpCondition_Name_FormattedCorrectly()
    {
        // Arrange & Act
        var condition = new GapUpCondition(5);

        // Assert
        Assert.That(condition.Name, Is.EqualTo("Gap Up >= 5.0%"));
    }

    [Test]
    public void GapUpCondition_IsPreviousCloseSet_CorrectlyReportsState()
    {
        // Arrange
        var condition = new GapUpCondition(5);

        // Assert - before setting
        Assert.That(condition.IsPreviousCloseSet, Is.False);

        // Act - set previous close
        condition.SetPreviousClose(100.00);

        // Assert - after setting
        Assert.That(condition.IsPreviousCloseSet, Is.True);
    }

    [Test]
    public void GapUpCondition_InvalidPercentage_ThrowsException()
    {
        // Assert - negative percentage
        Assert.Throws<ArgumentOutOfRangeException>(() => new GapUpCondition(-5));

        // Assert - percentage > 100
        Assert.Throws<ArgumentOutOfRangeException>(() => new GapUpCondition(150));
    }

    [Test]
    public void GapUpCondition_SmallGapPercentage_WorksCorrectly()
    {
        // Arrange - 0.5% gap threshold
        var condition = new GapUpCondition(0.5);
        condition.SetPreviousClose(100.00);

        // Act
        var resultPass = condition.Evaluate(currentPrice: 100.50, vwap: 0);
        var resultFail = condition.Evaluate(currentPrice: 100.40, vwap: 0);

        // Assert
        Assert.That(resultPass, Is.True);
        Assert.That(resultFail, Is.False);
    }

    #endregion

    #region GapDownCondition Tests

    [Test]
    public void GapDownCondition_PriceGappedDown5Percent_ReturnsTrue()
    {
        // Arrange - Previous close at 100, current price at 94 (6% gap down)
        var condition = new GapDownCondition(5);
        condition.SetPreviousClose(100.00);

        // Act
        var result = condition.Evaluate(currentPrice: 94.00, vwap: 100.00);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void GapDownCondition_PriceGappedDownExactly5Percent_ReturnsTrue()
    {
        // Arrange - Previous close at 100, current price at 95 (exactly 5% gap down)
        var condition = new GapDownCondition(5);
        condition.SetPreviousClose(100.00);

        // Act
        var result = condition.Evaluate(currentPrice: 95.00, vwap: 100.00);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void GapDownCondition_PriceGappedDownLessThan5Percent_ReturnsFalse()
    {
        // Arrange - Previous close at 100, current price at 96 (4% gap down)
        var condition = new GapDownCondition(5);
        condition.SetPreviousClose(100.00);

        // Act
        var result = condition.Evaluate(currentPrice: 96.00, vwap: 100.00);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void GapDownCondition_PriceGappedUp_ReturnsFalse()
    {
        // Arrange - Previous close at 100, current price at 105 (gapped up)
        var condition = new GapDownCondition(5);
        condition.SetPreviousClose(100.00);

        // Act
        var result = condition.Evaluate(currentPrice: 105.00, vwap: 100.00);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void GapDownCondition_NoPreviousCloseSet_ReturnsFalse()
    {
        // Arrange - Previous close not set
        var condition = new GapDownCondition(5);

        // Act
        var result = condition.Evaluate(currentPrice: 90.00, vwap: 100.00);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void GapDownCondition_Percentage_StoredCorrectly()
    {
        // Arrange & Act
        var condition = new GapDownCondition(3.5);

        // Assert
        Assert.That(condition.Percentage, Is.EqualTo(3.5));
    }

    [Test]
    public void GapDownCondition_Name_FormattedCorrectly()
    {
        // Arrange & Act
        var condition = new GapDownCondition(5);

        // Assert
        Assert.That(condition.Name, Is.EqualTo("Gap Down >= 5.0%"));
    }

    [Test]
    public void GapDownCondition_InvalidPercentage_ThrowsException()
    {
        // Assert - negative percentage
        Assert.Throws<ArgumentOutOfRangeException>(() => new GapDownCondition(-5));

        // Assert - percentage > 100
        Assert.Throws<ArgumentOutOfRangeException>(() => new GapDownCondition(150));
    }

    #endregion

    #region GapUp/GapDown Fluent API Tests

    [Test]
    public void FluentApi_GapUp_CreatesGapUpCondition()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("NVDA")
            .GapUp(5)
            .Long().Quantity(100)
            .Build();

        // Assert
        Assert.That(strategy.Conditions, Has.Count.EqualTo(1));
        Assert.That(strategy.Conditions[0], Is.TypeOf<GapUpCondition>());
        Assert.That(((GapUpCondition)strategy.Conditions[0]).Percentage, Is.EqualTo(5));
    }

    [Test]
    public void FluentApi_IsGapUp_CreatesGapUpCondition()
    {
        // Arrange & Act - IsGapUp is an alias for GapUp
        var strategy = Stock.Ticker("NVDA")
            .IsGapUp(5)
            .Long().Quantity(100)
            .Build();

        // Assert
        Assert.That(strategy.Conditions, Has.Count.EqualTo(1));
        Assert.That(strategy.Conditions[0], Is.TypeOf<GapUpCondition>());
    }

    [Test]
    public void FluentApi_GapDown_CreatesGapDownCondition()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .GapDown(3)
            .Long().Quantity(100)
            .Build();

        // Assert
        Assert.That(strategy.Conditions, Has.Count.EqualTo(1));
        Assert.That(strategy.Conditions[0], Is.TypeOf<GapDownCondition>());
        Assert.That(((GapDownCondition)strategy.Conditions[0]).Percentage, Is.EqualTo(3));
    }

    [Test]
    public void FluentApi_IsGapDown_CreatesGapDownCondition()
    {
        // Arrange & Act - IsGapDown is an alias for GapDown
        var strategy = Stock.Ticker("AAPL")
            .IsGapDown(3)
            .Long().Quantity(100)
            .Build();

        // Assert
        Assert.That(strategy.Conditions, Has.Count.EqualTo(1));
        Assert.That(strategy.Conditions[0], Is.TypeOf<GapDownCondition>());
    }

    [Test]
    public void FluentApi_GapUp_CombinesWithOtherConditions()
    {
        // Arrange & Act - Gap and Go strategy
        var strategy = Stock.Ticker("NVDA")
            .GapUp(5)
            .IsAboveVwap()
            .IsDiPositive()
            .Long().Quantity(100)
            .Build();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(strategy.Conditions, Has.Count.EqualTo(3));
            Assert.That(strategy.Conditions[0], Is.TypeOf<GapUpCondition>());
            Assert.That(strategy.Conditions[1], Is.TypeOf<AboveVwapCondition>());
            Assert.That(strategy.Conditions[2], Is.TypeOf<DiCondition>());
        });
    }

    #endregion
}


