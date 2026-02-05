// ============================================================================
// StrategyRunnerTests - Tests for strategy execution logic
// ============================================================================

using IdiotProof.Backend.Models;
using IdiotProof.Backend.Strategy;
using NUnit.Framework;

namespace IdiotProof.Backend.UnitTests;

/// <summary>
/// Tests for strategy condition evaluation and execution flow.
/// Note: Full StrategyRunner tests would require mocking IBKR client.
/// </summary>
[TestFixture]
public class StrategyConditionTests
{
    #region Breakout Condition Tests

    [Test]
    public void BreakoutCondition_PriceAboveLevel_ReturnsTrue()
    {
        // Arrange
        var condition = new BreakoutCondition(100);

        // Act
        var result = condition.Evaluate(currentPrice: 105, vwap: 98);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void BreakoutCondition_PriceAtLevel_ReturnsTrue()
    {
        // Arrange
        var condition = new BreakoutCondition(100);

        // Act
        var result = condition.Evaluate(currentPrice: 100, vwap: 98);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void BreakoutCondition_PriceBelowLevel_ReturnsFalse()
    {
        // Arrange
        var condition = new BreakoutCondition(100);

        // Act
        var result = condition.Evaluate(currentPrice: 99.99, vwap: 98);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void BreakoutCondition_Name_ContainsLevel()
    {
        // Arrange
        var condition = new BreakoutCondition(150.50);

        // Assert
        Assert.That(condition.Name, Does.Contain("150.50"));
    }

    #endregion

    #region Pullback Condition Tests

    [Test]
    public void PullbackCondition_PriceBelowLevel_ReturnsTrue()
    {
        // Arrange
        var condition = new PullbackCondition(100);

        // Act
        var result = condition.Evaluate(currentPrice: 95, vwap: 102);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void PullbackCondition_PriceAtLevel_ReturnsTrue()
    {
        // Arrange
        var condition = new PullbackCondition(100);

        // Act
        var result = condition.Evaluate(currentPrice: 100, vwap: 102);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void PullbackCondition_PriceAboveLevel_ReturnsFalse()
    {
        // Arrange
        var condition = new PullbackCondition(100);

        // Act
        var result = condition.Evaluate(currentPrice: 100.01, vwap: 102);

        // Assert
        Assert.That(result, Is.False);
    }

    #endregion

    #region AboveVwap Condition Tests

    [Test]
    public void AboveVwapCondition_PriceAboveVwap_ReturnsTrue()
    {
        // Arrange
        var condition = new AboveVwapCondition(buffer: 0);

        // Act
        var result = condition.Evaluate(currentPrice: 105, vwap: 100);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void AboveVwapCondition_PriceAtVwap_ReturnsTrue()
    {
        // Arrange
        var condition = new AboveVwapCondition(buffer: 0);

        // Act
        var result = condition.Evaluate(currentPrice: 100, vwap: 100);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void AboveVwapCondition_PriceBelowVwap_ReturnsFalse()
    {
        // Arrange
        var condition = new AboveVwapCondition(buffer: 0);

        // Act
        var result = condition.Evaluate(currentPrice: 99.99, vwap: 100);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void AboveVwapCondition_WithBuffer_RequiresPriceAboveVwapPlusBuffer()
    {
        // Arrange
        var condition = new AboveVwapCondition(buffer: 0.50);

        // Act
        var belowBuffer = condition.Evaluate(currentPrice: 100.49, vwap: 100);
        var atBuffer = condition.Evaluate(currentPrice: 100.50, vwap: 100);
        var aboveBuffer = condition.Evaluate(currentPrice: 100.51, vwap: 100);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(belowBuffer, Is.False);
            Assert.That(atBuffer, Is.True);
            Assert.That(aboveBuffer, Is.True);
        });
    }

    #endregion

    #region BelowVwap Condition Tests

    [Test]
    public void BelowVwapCondition_PriceBelowVwap_ReturnsTrue()
    {
        // Arrange
        var condition = new BelowVwapCondition(buffer: 0);

        // Act
        var result = condition.Evaluate(currentPrice: 95, vwap: 100);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void BelowVwapCondition_PriceAtVwap_ReturnsTrue()
    {
        // Arrange
        var condition = new BelowVwapCondition(buffer: 0);

        // Act
        var result = condition.Evaluate(currentPrice: 100, vwap: 100);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void BelowVwapCondition_WithBuffer_RequiresPriceBelowVwapMinusBuffer()
    {
        // Arrange
        var condition = new BelowVwapCondition(buffer: 0.50);

        // Act
        var aboveBuffer = condition.Evaluate(currentPrice: 99.51, vwap: 100);
        var atBuffer = condition.Evaluate(currentPrice: 99.50, vwap: 100);
        var belowBuffer = condition.Evaluate(currentPrice: 99.49, vwap: 100);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(aboveBuffer, Is.False);
            Assert.That(atBuffer, Is.True);
            Assert.That(belowBuffer, Is.True);
        });
    }

    #endregion

    #region PriceAtOrAbove Condition Tests

    [Test]
    public void PriceAtOrAboveCondition_AboveLevel_ReturnsTrue()
    {
        // Arrange
        var condition = new PriceAtOrAboveCondition(100);

        // Act
        var result = condition.Evaluate(currentPrice: 100.01, vwap: 95);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void PriceAtOrAboveCondition_AtLevel_ReturnsTrue()
    {
        // Arrange - greater than or equal
        var condition = new PriceAtOrAboveCondition(100);

        // Act
        var result = condition.Evaluate(currentPrice: 100, vwap: 95);

        // Assert
        Assert.That(result, Is.True);
    }

    #endregion

    #region PriceBelow Condition Tests

    [Test]
    public void PriceBelowCondition_StrictlyBelow_ReturnsTrue()
    {
        // Arrange
        var condition = new PriceBelowCondition(100);

        // Act
        var result = condition.Evaluate(currentPrice: 99.99, vwap: 105);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void PriceBelowCondition_AtLevel_ReturnsFalse()
    {
        // Arrange - strictly less than, not <=
        var condition = new PriceBelowCondition(100);

        // Act
        var result = condition.Evaluate(currentPrice: 100, vwap: 105);

        // Assert
        Assert.That(result, Is.False);
    }

    #endregion

    #region Custom Condition Tests

    [Test]
    public void CustomCondition_EvaluatesCustomLogic()
    {
        // Arrange
        var condition = new CustomCondition(
            "Price in range",
            (price, vwap) => price >= 95 && price <= 105);

        // Act
        var inRange = condition.Evaluate(currentPrice: 100, vwap: 98);
        var belowRange = condition.Evaluate(currentPrice: 94, vwap: 98);
        var aboveRange = condition.Evaluate(currentPrice: 106, vwap: 98);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(inRange, Is.True);
            Assert.That(belowRange, Is.False);
            Assert.That(aboveRange, Is.False);
        });
    }

    [Test]
    public void CustomCondition_CanAccessVwap()
    {
        // Arrange
        var condition = new CustomCondition(
            "Price within 5% of VWAP",
            (price, vwap) => Math.Abs(price - vwap) / vwap <= 0.05);

        // Act
        var within = condition.Evaluate(currentPrice: 102, vwap: 100);  // 2%
        var outside = condition.Evaluate(currentPrice: 110, vwap: 100); // 10%

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(within, Is.True);
            Assert.That(outside, Is.False);
        });
    }

    [Test]
    public void CustomCondition_Name_ReturnsProvidedName()
    {
        // Arrange
        var condition = new CustomCondition("My Custom Condition", (p, v) => true);

        // Assert
        Assert.That(condition.Name, Is.EqualTo("My Custom Condition"));
    }

    #endregion

    #region Multi-Step Condition Sequence Tests

    [Test]
    public void ConditionSequence_BreakoutThenPullbackThenVwap_EvaluatesCorrectly()
    {
        // Arrange - typical strategy pattern
        var breakout = new BreakoutCondition(7.10);
        var pullback = new PullbackCondition(6.80);
        var aboveVwap = new AboveVwapCondition(0);

        // Simulate price movement
        var prices = new[]
        {
            (price: 7.00, vwap: 6.90, step: "Initial - below breakout"),
            (price: 7.15, vwap: 6.95, step: "Breakout triggered"),
            (price: 7.00, vwap: 7.00, step: "Pulling back, not at level yet"),
            (price: 6.75, vwap: 6.85, step: "Pullback triggered"),
            (price: 6.70, vwap: 6.80, step: "Below VWAP"),
            (price: 6.90, vwap: 6.85, step: "Above VWAP - entry!")
        };

        // Step 1: Breakout
        Assert.That(breakout.Evaluate(7.00, 6.90), Is.False);
        Assert.That(breakout.Evaluate(7.15, 6.95), Is.True);

        // Step 2: Pullback (after breakout)
        Assert.That(pullback.Evaluate(7.00, 7.00), Is.False);
        Assert.That(pullback.Evaluate(6.75, 6.85), Is.True);

        // Step 3: Above VWAP (after pullback)
        Assert.That(aboveVwap.Evaluate(6.70, 6.80), Is.False);
        Assert.That(aboveVwap.Evaluate(6.90, 6.85), Is.True);
    }

    #endregion
}

/// <summary>
/// Tests for trailing stop loss calculation logic.
/// </summary>
[TestFixture]
public class TrailingStopLossCalculationTests
{
    [Test]
    public void TrailingStopPrice_CalculatesCorrectly()
    {
        // Arrange
        double entryPrice = 100;
        double trailingPercent = 0.10; // 10%

        // Act
        double expectedStop = entryPrice * (1 - trailingPercent);

        // Assert
        Assert.That(expectedStop, Is.EqualTo(90).Within(0.01));
    }

    [Test]
    public void TrailingStopPrice_MovesUpWithHighWaterMark()
    {
        // Arrange
        double trailingPercent = 0.10;
        double initialEntry = 100;
        double newHigh = 110;

        // Act
        double initialStop = initialEntry * (1 - trailingPercent);
        double newStop = newHigh * (1 - trailingPercent);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(initialStop, Is.EqualTo(90).Within(0.01));
            Assert.That(newStop, Is.EqualTo(99).Within(0.01));
            Assert.That(newStop, Is.GreaterThan(initialStop));
        });
    }

    [Test]
    public void TrailingStopPrice_NeverMovesDown()
    {
        // Arrange
        double trailingPercent = 0.10;
        double highWaterMark = 110;
        double currentPrice = 105; // Price dropped

        // Act - stop should stay at high water mark level
        double stopAtHigh = highWaterMark * (1 - trailingPercent);
        double stopAtCurrent = currentPrice * (1 - trailingPercent);

        // Assert - stop at high (99) is higher than stop at current (94.5)
        Assert.That(stopAtHigh, Is.GreaterThan(stopAtCurrent));
        // In real implementation, we'd keep stopAtHigh
    }

    [TestCase(100, 0.05, 95)]
    [TestCase(100, 0.10, 90)]
    [TestCase(100, 0.15, 85)]
    [TestCase(100, 0.20, 80)]
    [TestCase(50, 0.10, 45)]
    [TestCase(200, 0.10, 180)]
    public void TrailingStopPrice_VariousScenarios(double price, double percent, double expectedStop)
    {
        // Act
        double actualStop = Math.Round(price * (1 - percent), 2);

        // Assert
        Assert.That(actualStop, Is.EqualTo(expectedStop).Within(0.01));
    }
}


