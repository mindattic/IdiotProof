// ============================================================================
// RsiCalculatorTests - Unit tests for RSI (Relative Strength Index) calculation
// ============================================================================
//
// Tests cover:
// - Constructor validation (period > 0)
// - Warm-up period requirements
// - RSI calculation accuracy (0-100 range)
// - Overbought/Oversold detection
// - Edge cases (all gains, all losses, no movement)
// - Reset functionality
//
// RSI Formula:
//   RSI = 100 - (100 / (1 + RS))
//   RS = Average Gain / Average Loss
//
// ============================================================================

using IdiotProof.Backend.Helpers;
using NUnit.Framework;

namespace IdiotProof.Backend.UnitTests;

/// <summary>
/// Tests for the RsiCalculator class that calculates Relative Strength Index.
/// </summary>
[TestFixture]
public class RsiCalculatorTests
{
    #region Constructor Tests

    [Test]
    public void Constructor_WithValidPeriod_CreatesInstance()
    {
        // Act
        var calculator = new RsiCalculator(period: 14);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(calculator, Is.Not.Null);
            Assert.That(calculator.Period, Is.EqualTo(14));
            Assert.That(calculator.IsReady, Is.False);
            Assert.That(calculator.CurrentValue, Is.EqualTo(0)); // No data yet
        });
    }

    [Test]
    public void Constructor_WithPeriodOne_CreatesInstance()
    {
        var calculator = new RsiCalculator(period: 1);
        Assert.That(calculator.Period, Is.EqualTo(1));
    }

    [Test]
    public void Constructor_WithPeriodZero_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RsiCalculator(period: 0));
    }

    [Test]
    public void Constructor_WithNegativePeriod_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RsiCalculator(period: -5));
    }

    [Test]
    public void Constructor_DefaultParameters_UsesPeriod14()
    {
        var calculator = new RsiCalculator();
        Assert.That(calculator.Period, Is.EqualTo(14));
    }

    #endregion

    #region Warm-Up / IsReady Tests

    [Test]
    public void IsReady_BeforeEnoughData_ReturnsFalse()
    {
        // Arrange
        var calculator = new RsiCalculator(period: 14);

        // Act - add 14 prices (need 15 for first RSI)
        for (int i = 1; i <= 14; i++)
            calculator.Update(100 + i);

        // Assert
        Assert.That(calculator.IsReady, Is.False);
    }

    [Test]
    public void IsReady_AfterEnoughData_ReturnsTrue()
    {
        // Arrange
        var calculator = new RsiCalculator(period: 14);

        // Act - add 15 prices (period + 1)
        for (int i = 1; i <= 15; i++)
            calculator.Update(100 + i);

        // Assert
        Assert.That(calculator.IsReady, Is.True);
    }

    #endregion

    #region RSI Range Tests

    [Test]
    public void Update_RsiAlwaysBetween0And100()
    {
        // Arrange
        var calculator = new RsiCalculator(period: 14);
        var random = new Random(42);

        // Act - add random prices
        for (int i = 0; i < 100; i++)
        {
            double price = 50 + random.NextDouble() * 100; // 50-150
            calculator.Update(price);

            // Assert
            Assert.That(calculator.CurrentValue, Is.InRange(0, 100));
        }
    }

    [Test]
    public void Update_WithAllGains_RsiApproaches100()
    {
        // Arrange
        var calculator = new RsiCalculator(period: 5);

        // Act - continuously rising prices
        for (int i = 1; i <= 20; i++)
            calculator.Update(i * 10); // 10, 20, 30, ... 200

        // Assert - RSI should be very high (approaching 100)
        Assert.That(calculator.CurrentValue, Is.GreaterThan(90));
    }

    [Test]
    public void Update_WithAllLosses_RsiApproaches0()
    {
        // Arrange
        var calculator = new RsiCalculator(period: 5);

        // Act - continuously falling prices
        for (int i = 20; i >= 1; i--)
            calculator.Update(i * 10); // 200, 190, 180, ... 10

        // Assert - RSI should be very low (approaching 0)
        Assert.That(calculator.CurrentValue, Is.LessThan(10));
    }

    [Test]
    public void Update_WithNoChange_AvgLossIsZero_RsiIs100()
    {
        // Arrange
        var calculator = new RsiCalculator(period: 5);

        // Act - constant price (no gains or losses after first)
        // When there are no losses, RSI = 100
        for (int i = 0; i < 20; i++)
            calculator.Update(100);

        // Assert - With no losses, RSI = 100 (avgLoss = 0)
        Assert.That(calculator.CurrentValue, Is.EqualTo(100));
    }

    [Test]
    public void Update_WithAlternatingPrices_RsiNear50()
    {
        // Arrange
        var calculator = new RsiCalculator(period: 5);

        // Act - alternating prices create equal gains and losses
        for (int i = 0; i < 20; i++)
        {
            calculator.Update(i % 2 == 0 ? 100 : 99);
        }

        // Assert - Equal gains and losses should produce RSI near 50
        Assert.That(calculator.CurrentValue, Is.InRange(40, 60));
    }

    #endregion

    #region Overbought/Oversold Tests

    [Test]
    public void Update_StrongUptrend_IndicatesOverbought()
    {
        // Arrange
        var calculator = new RsiCalculator(period: 14);

        // Act - simulate strong uptrend
        double price = 100;
        for (int i = 0; i < 30; i++)
        {
            price += 2; // Steady gains
            calculator.Update(price);
        }

        // Assert - RSI > 70 indicates overbought
        Assert.That(calculator.CurrentValue, Is.GreaterThanOrEqualTo(70));
    }

    [Test]
    public void Update_StrongDowntrend_IndicatesOversold()
    {
        // Arrange
        var calculator = new RsiCalculator(period: 14);

        // Act - simulate strong downtrend
        double price = 200;
        for (int i = 0; i < 30; i++)
        {
            price -= 2; // Steady losses
            calculator.Update(price);
        }

        // Assert - RSI < 30 indicates oversold
        Assert.That(calculator.CurrentValue, Is.LessThanOrEqualTo(30));
    }

    [Test]
    public void Update_MixedMovement_RsiNearNeutral()
    {
        // Arrange
        var calculator = new RsiCalculator(period: 14);

        // Act - simulate mixed movement (up and down equally)
        double price = 100;
        for (int i = 0; i < 30; i++)
        {
            price += (i % 2 == 0) ? 1 : -1; // Alternating
            calculator.Update(price);
        }

        // Assert - RSI should be near 50
        Assert.That(calculator.CurrentValue, Is.InRange(40, 60));
    }

    #endregion

    #region Reset Tests

    [Test]
    public void Reset_ClearsAllState()
    {
        // Arrange
        var calculator = new RsiCalculator(period: 14);
        for (int i = 1; i <= 20; i++)
            calculator.Update(100 + i);

        Assert.That(calculator.IsReady, Is.True);

        // Act
        calculator.Reset();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(calculator.IsReady, Is.False);
            Assert.That(calculator.CurrentValue, Is.EqualTo(0)); // Back to initial
        });
    }

    [Test]
    public void Reset_AllowsReuse()
    {
        // Arrange
        var calculator = new RsiCalculator(period: 5);

        // First usage - uptrend
        for (int i = 1; i <= 10; i++)
            calculator.Update(i * 10);
        double firstRsi = calculator.CurrentValue;

        // Act - reset and use for downtrend
        calculator.Reset();
        for (int i = 10; i >= 1; i--)
            calculator.Update(i * 10);
        double secondRsi = calculator.CurrentValue;

        // Assert - RSI should reflect the different trends
        Assert.That(firstRsi, Is.GreaterThan(secondRsi));
    }

    #endregion

    #region Edge Cases

    [Test]
    public void Update_WithZeroPrice_IgnoresValue()
    {
        // Arrange
        var calculator = new RsiCalculator(period: 5);
        calculator.Update(100);

        // Act
        double result = calculator.Update(0);

        // Assert - should not crash or incorporate 0
        Assert.That(result, Is.EqualTo(50)); // Still at default
    }

    [Test]
    public void Update_WithNegativePrice_IgnoresValue()
    {
        // Arrange
        var calculator = new RsiCalculator(period: 5);
        calculator.Update(100);

        // Act
        double result = calculator.Update(-50);

        // Assert
        Assert.That(result, Is.EqualTo(50)); // Still at default
    }

    [Test]
    public void Update_SingleLargeGain_HighRsi()
    {
        // Arrange
        var calculator = new RsiCalculator(period: 5);

        // Warm up with stable prices
        for (int i = 0; i < 5; i++)
            calculator.Update(100);

        // Act - one big jump
        calculator.Update(200);

        // Assert - RSI should spike
        Assert.That(calculator.CurrentValue, Is.GreaterThan(70));
    }

    [Test]
    public void Update_SingleLargeLoss_LowRsi()
    {
        // Arrange
        var calculator = new RsiCalculator(period: 5);

        // Warm up with stable prices
        for (int i = 0; i < 5; i++)
            calculator.Update(100);

        // Act - one big drop
        calculator.Update(50);

        // Assert - RSI should drop
        Assert.That(calculator.CurrentValue, Is.LessThan(30));
    }

    #endregion
}
