// ============================================================================
// EmaCalculatorTests - Unit tests for EMA (Exponential Moving Average) calculation
// ============================================================================
//
// Tests cover:
// - Constructor validation (period > 0)
// - Initial warm-up period (SMA seed)
// - EMA calculation accuracy
// - IsReady state transitions
// - Reset functionality
//
// ============================================================================

using IdiotProof.Backend.Helpers;
using NUnit.Framework;

namespace IdiotProof.Backend.UnitTests;

/// <summary>
/// Tests for the EmaCalculator class that calculates Exponential Moving Averages.
/// </summary>
[TestFixture]
public class EmaCalculatorTests
{
    #region Constructor Tests

    [Test]
    public void Constructor_WithValidPeriod_CreatesInstance()
    {
        // Act
        var calculator = new EmaCalculator(period: 9);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(calculator, Is.Not.Null);
            Assert.That(calculator.Period, Is.EqualTo(9));
            Assert.That(calculator.IsReady, Is.False);
            Assert.That(calculator.CurrentValue, Is.EqualTo(0));
        });
    }

    [Test]
    public void Constructor_WithPeriodOne_CreatesInstance()
    {
        // Period of 1 means EMA = current price
        var calculator = new EmaCalculator(period: 1);
        Assert.That(calculator.Period, Is.EqualTo(1));
    }

    [Test]
    public void Constructor_WithPeriodZero_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new EmaCalculator(period: 0));
    }

    [Test]
    public void Constructor_WithNegativePeriod_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new EmaCalculator(period: -5));
    }

    [Test]
    public void Constructor_WithLargePeriod_CreatesInstance()
    {
        // EMA(200) is common for long-term trend analysis
        var calculator = new EmaCalculator(period: 200);
        Assert.That(calculator.Period, Is.EqualTo(200));
    }

    #endregion

    #region Warm-Up / IsReady Tests

    [Test]
    public void IsReady_BeforeEnoughData_ReturnsFalse()
    {
        // Arrange
        var calculator = new EmaCalculator(period: 5);

        // Act - add 4 prices (need 5)
        for (int i = 1; i <= 4; i++)
            calculator.Update(100 + i);

        // Assert
        Assert.That(calculator.IsReady, Is.False);
    }

    [Test]
    public void IsReady_AfterEnoughData_ReturnsTrue()
    {
        // Arrange
        var calculator = new EmaCalculator(period: 5);

        // Act - add exactly 5 prices
        for (int i = 1; i <= 5; i++)
            calculator.Update(100 + i);

        // Assert
        Assert.That(calculator.IsReady, Is.True);
    }

    [Test]
    public void IsReady_AfterMoreThanEnoughData_StaysTrue()
    {
        // Arrange
        var calculator = new EmaCalculator(period: 5);

        // Act - add 10 prices
        for (int i = 1; i <= 10; i++)
            calculator.Update(100 + i);

        // Assert
        Assert.That(calculator.IsReady, Is.True);
    }

    #endregion

    #region Update / Calculation Tests

    [Test]
    public void Update_WithZeroPrice_IgnoresValue()
    {
        // Arrange
        var calculator = new EmaCalculator(period: 5);
        calculator.Update(100);

        // Act
        double result = calculator.Update(0);

        // Assert - should return previous value, not incorporate 0
        Assert.That(result, Is.EqualTo(0)); // Still in warm-up
    }

    [Test]
    public void Update_WithNegativePrice_IgnoresValue()
    {
        // Arrange
        var calculator = new EmaCalculator(period: 5);
        calculator.Update(100);

        // Act
        double result = calculator.Update(-50);

        // Assert
        Assert.That(result, Is.EqualTo(0)); // Still in warm-up, negative ignored
    }

    [Test]
    public void Update_DuringWarmUp_ReturnsSMA()
    {
        // Arrange
        var calculator = new EmaCalculator(period: 5);

        // Act - add 5 prices: 10, 20, 30, 40, 50
        calculator.Update(10);
        calculator.Update(20);
        calculator.Update(30);
        calculator.Update(40);
        double result = calculator.Update(50);

        // Assert - SMA = (10+20+30+40+50)/5 = 30
        Assert.That(result, Is.EqualTo(30));
    }

    [Test]
    public void Update_AfterWarmUp_AppliesEmaFormula()
    {
        // Arrange
        var calculator = new EmaCalculator(period: 5);

        // Warm up with 5 prices (SMA = 30)
        calculator.Update(10);
        calculator.Update(20);
        calculator.Update(30);
        calculator.Update(40);
        calculator.Update(50);

        // Act - add 6th price
        double result = calculator.Update(60);

        // Assert
        // EMA = (Price - PrevEMA) * Multiplier + PrevEMA
        // Multiplier = 2 / (5 + 1) = 0.333...
        // EMA = (60 - 30) * 0.333... + 30 = 10 + 30 = 40
        Assert.That(result, Is.EqualTo(40).Within(0.01));
    }

    [Test]
    public void Update_WithConstantPrice_ConvergesToPrice()
    {
        // Arrange
        var calculator = new EmaCalculator(period: 5);

        // Act - feed constant price
        for (int i = 0; i < 20; i++)
            calculator.Update(100);

        // Assert - EMA should equal the constant price
        Assert.That(calculator.CurrentValue, Is.EqualTo(100).Within(0.01));
    }

    [Test]
    public void Update_WithRisingPrices_EmaLagsBehind()
    {
        // Arrange
        var calculator = new EmaCalculator(period: 5);

        // Act - feed rising prices
        for (int i = 1; i <= 10; i++)
            calculator.Update(i * 10); // 10, 20, 30, ... 100

        // Assert - EMA should lag behind the current price (100)
        Assert.That(calculator.CurrentValue, Is.LessThan(100));
        Assert.That(calculator.CurrentValue, Is.GreaterThan(50)); // But above midpoint
    }

    [Test]
    public void Update_Period1_EqualsCurrentPrice()
    {
        // Arrange - EMA(1) should always equal current price
        var calculator = new EmaCalculator(period: 1);

        // Act
        calculator.Update(100);
        calculator.Update(200);
        calculator.Update(50);

        // Assert
        Assert.That(calculator.CurrentValue, Is.EqualTo(50));
    }

    #endregion

    #region Reset Tests

    [Test]
    public void Reset_ClearsAllState()
    {
        // Arrange
        var calculator = new EmaCalculator(period: 5);
        for (int i = 1; i <= 10; i++)
            calculator.Update(100 + i);

        // Pre-conditions
        Assert.That(calculator.IsReady, Is.True);
        Assert.That(calculator.CurrentValue, Is.GreaterThan(0));

        // Act
        calculator.Reset();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(calculator.IsReady, Is.False);
            Assert.That(calculator.CurrentValue, Is.EqualTo(0));
        });
    }

    [Test]
    public void Reset_AllowsReuse()
    {
        // Arrange
        var calculator = new EmaCalculator(period: 3);
        calculator.Update(100);
        calculator.Update(100);
        calculator.Update(100);
        Assert.That(calculator.IsReady, Is.True);

        // Act
        calculator.Reset();
        calculator.Update(200);
        calculator.Update(200);
        calculator.Update(200);

        // Assert - should have new SMA based on 200s
        Assert.That(calculator.CurrentValue, Is.EqualTo(200));
    }

    #endregion

    #region Multiplier Tests

    [Test]
    public void Multiplier_ForPeriod9_IsCorrect()
    {
        // Multiplier = 2 / (period + 1) = 2/10 = 0.2
        var calculator = new EmaCalculator(period: 9);

        // Warm up
        for (int i = 0; i < 9; i++)
            calculator.Update(100);

        // Add new price and verify calculation
        calculator.Update(200);

        // EMA = (200 - 100) * 0.2 + 100 = 20 + 100 = 120
        Assert.That(calculator.CurrentValue, Is.EqualTo(120).Within(0.01));
    }

    [Test]
    public void Multiplier_ForPeriod21_IsCorrect()
    {
        // Multiplier = 2 / (21 + 1) = 2/22 ≈ 0.0909
        var calculator = new EmaCalculator(period: 21);

        // Warm up with constant price
        for (int i = 0; i < 21; i++)
            calculator.Update(100);

        // Add new price
        calculator.Update(200);

        // EMA = (200 - 100) * (2/22) + 100 ≈ 109.09
        Assert.That(calculator.CurrentValue, Is.EqualTo(109.09).Within(0.1));
    }

    #endregion
}
