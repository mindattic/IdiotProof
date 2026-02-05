// ============================================================================
// ValidationTests - Tests for invalid combinations and edge cases
// ============================================================================

using IdiotProof.Backend.Enums;
using IdiotProof.Backend.Models;
using IdiotProof.Backend.Strategy;
using NUnit.Framework;

namespace IdiotProof.Backend.UnitTests;

/// <summary>
/// Tests for invalid input combinations and validation edge cases.
/// Ensures proper error handling for misuse of the fluent API.
/// </summary>
[TestFixture]
public class ValidationTests
{
    #region Strategy Builder Validation

    [Test]
    public void Buy_WithNoConditions_ThrowsInvalidOperationException()
    {
        // Arrange, Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
        {
            Stock.Ticker("AAPL")
                .Long().Quantity(100)
                .Build();
        });
    }

    [Test]
    public void Sell_WithNoConditions_ThrowsInvalidOperationException()
    {
        // Arrange, Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
        {
            Stock.Ticker("AAPL")
                .Short().Quantity(100)
                .Build();
        });
    }

    #endregion

    #region Time Validation

    [Test]
    public void TradingPeriod_StartAfterEnd_ThrowsArgumentException()
    {
        // Arrange
        var start = new TimeOnly(10, 0);
        var end = new TimeOnly(8, 0);  // End before start

        // Act & Assert
        Assert.Throws<ArgumentException>(() => new TradingPeriod(start, end));
    }

    [Test]
    public void TradingPeriod_StartEqualsEnd_ThrowsArgumentException()
    {
        // Arrange
        var time = new TimeOnly(8, 0);

        // Act & Assert
        Assert.Throws<ArgumentException>(() => new TradingPeriod(time, time));
    }

    [Test]
    public void TradingPeriod_ValidRange_DoesNotThrow()
    {
        // Arrange
        var start = new TimeOnly(8, 0);
        var end = new TimeOnly(10, 0);

        // Act & Assert
        Assert.DoesNotThrow(() => new TradingPeriod(start, end));
    }

    #endregion

    #region Percent Validation

    [Test]
    public void Percent_Custom_NegativeValue_ThrowsArgumentOutOfRangeException()
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => Percent.Custom(-5));
    }

    [Test]
    public void Percent_Custom_OverHundred_ThrowsArgumentOutOfRangeException()
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => Percent.Custom(101));
    }

    [Test]
    public void Percent_Custom_Zero_ReturnsZero()
    {
        // Act
        var result = Percent.Custom(0);

        // Assert
        Assert.That(result, Is.EqualTo(0));
    }

    [Test]
    public void Percent_Custom_Hundred_ReturnsOne()
    {
        // Act
        var result = Percent.Custom(100);

        // Assert
        Assert.That(result, Is.EqualTo(1.0));
    }

    [Test]
    public void Percent_Custom_ValidValue_ReturnsCorrectDecimal()
    {
        // Act
        var result = Percent.Custom(12.5);

        // Assert
        Assert.That(result, Is.EqualTo(0.125).Within(0.0001));
    }

    #endregion

    #region Quantity Validation

    [Test]
    public void Buy_WithZeroQuantity_CreatesStrategy()
    {
        // Note: Zero quantity is technically allowed by the builder
        // IBKR will reject it, but validation is broker-side
        var strategy = Stock.Ticker("AAPL")
            .Breakout(150)
            .Long().Quantity(0)
            .Build();

        Assert.That(strategy.Order.Quantity, Is.EqualTo(0));
    }

    [Test]
    public void Buy_WithNegativeQuantity_CreatesStrategy()
    {
        // Note: Negative quantity is allowed by builder - broker validates
        var strategy = Stock.Ticker("AAPL")
            .Breakout(150)
            .Long().Quantity(-100).PriceType(Price.Current)
            .Build();

        Assert.That(strategy.Order.Quantity, Is.EqualTo(-100));
    }

    #endregion

    #region Condition Evaluation Edge Cases

    [Test]
    public void BreakoutCondition_AtExactLevel_ReturnsTrue()
    {
        // Arrange
        var condition = new BreakoutCondition(100);

        // Act
        var result = condition.Evaluate(100, 95);  // price == level

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void BreakoutCondition_BelowLevel_ReturnsFalse()
    {
        // Arrange
        var condition = new BreakoutCondition(100);

        // Act
        var result = condition.Evaluate(99.99, 95);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void PullbackCondition_AtExactLevel_ReturnsTrue()
    {
        // Arrange
        var condition = new PullbackCondition(100);

        // Act
        var result = condition.Evaluate(100, 105);  // price == level

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void PullbackCondition_AboveLevel_ReturnsFalse()
    {
        // Arrange
        var condition = new PullbackCondition(100);

        // Act
        var result = condition.Evaluate(100.01, 105);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void AboveVwapCondition_AtExactVwap_ReturnsTrue()
    {
        // Arrange
        var condition = new AboveVwapCondition(0);

        // Act
        var result = condition.Evaluate(100, 100);  // price == vwap

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void AboveVwapCondition_WithBuffer_RequiresBuffer()
    {
        // Arrange
        var condition = new AboveVwapCondition(0.05);  // 5 cent buffer

        // Act
        var atVwap = condition.Evaluate(100, 100);        // price == vwap
        var belowBuffer = condition.Evaluate(100.04, 100); // price < vwap + buffer
        var atBuffer = condition.Evaluate(100.05, 100);    // price == vwap + buffer

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(atVwap, Is.False);
            Assert.That(belowBuffer, Is.False);
            Assert.That(atBuffer, Is.True);
        });
    }

    [Test]
    public void BelowVwapCondition_WithBuffer_RequiresBuffer()
    {
        // Arrange
        var condition = new BelowVwapCondition(0.05);  // 5 cent buffer

        // Act
        var atVwap = condition.Evaluate(100, 100);         // price == vwap
        var aboveBuffer = condition.Evaluate(99.96, 100);  // price > vwap - buffer
        var atBuffer = condition.Evaluate(99.95, 100);     // price == vwap - buffer

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(atVwap, Is.False);
            Assert.That(aboveBuffer, Is.False);
            Assert.That(atBuffer, Is.True);
        });
    }

    [Test]
    public void PriceAtOrAboveCondition_AtExactLevel_ReturnsTrue()
    {
        // Arrange - PriceAtOrAbove is greater than or equal
        var condition = new PriceAtOrAboveCondition(100);

        // Act
        var result = condition.Evaluate(100, 95);

        // Assert
        Assert.That(result, Is.True);  // >= includes exact level
    }

    [Test]
    public void PriceBelowCondition_AtExactLevel_ReturnsFalse()
    {
        // Arrange - PriceBelow is strictly less than
        var condition = new PriceBelowCondition(100);

        // Act
        var result = condition.Evaluate(100, 105);

        // Assert
        Assert.That(result, Is.False);  // Must be < not <=
    }

    [Test]
    public void CustomCondition_WithNullName_ThrowsException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new CustomCondition(null!, (p, v) => true));
    }

    [Test]
    public void CustomCondition_WithNullCondition_ThrowsException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new CustomCondition("Test", null!));
    }

    #endregion

    #region Multiple Exit Strategy Tests

    [Test]
    public void TakeProfitAndStopLoss_BothEnabled_CreatesBothOrders()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .Breakout(150)
            .Long().Quantity(100)
            .TakeProfit(160)
            .StopLoss(145)
            .Build();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(strategy.Order.EnableTakeProfit, Is.True);
            Assert.That(strategy.Order.TakeProfitPrice, Is.EqualTo(160));
            Assert.That(strategy.Order.EnableStopLoss, Is.True);
            Assert.That(strategy.Order.StopLossPrice, Is.EqualTo(145));
        });
    }

    [Test]
    public void TakeProfitAndTrailingStopLoss_BothEnabled_CreatesBoth()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .Breakout(150)
            .Long().Quantity(100)
            .TakeProfit(160)
            .TrailingStopLoss(Percent.Ten)
            .Build();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(strategy.Order.EnableTakeProfit, Is.True);
            Assert.That(strategy.Order.EnableTrailingStopLoss, Is.True);
        });
    }

    [Test]
    public void StopLossAndTrailingStopLoss_BothEnabled_BothSet()
    {
        // Note: Both can be enabled - runner should handle precedence
        var strategy = Stock.Ticker("AAPL")
            .Breakout(150)
            .Long().Quantity(100)
            .StopLoss(145)
            .TrailingStopLoss(Percent.Ten)
            .Build();

        Assert.Multiple(() =>
        {
            Assert.That(strategy.Order.EnableStopLoss, Is.True);
            Assert.That(strategy.Order.EnableTrailingStopLoss, Is.True);
        });
    }

    #endregion

    #region Condition Null Handling

    [Test]
    public void Condition_WithNullCondition_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            Stock.Ticker("AAPL").Condition(null!));
    }

    [Test]
    public void When_WithNullConditionFunction_ThrowsException()
    {
        // Act & Assert - underlying CustomCondition throws
        Assert.Throws<ArgumentNullException>(() =>
            Stock.Ticker("AAPL").When("Test", null!));
    }

    #endregion
}


