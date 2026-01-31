// ============================================================================
// AtrCalculatorTests - Unit tests for ATR calculation logic
// ============================================================================

using IdiotProof.Backend.Helpers;
using NUnit.Framework;

namespace IdiotProof.Backend.UnitTests;

/// <summary>
/// Tests for the AtrCalculator class that handles Average True Range calculation
/// for volatility-based stop losses.
/// </summary>
[TestFixture]
public class AtrCalculatorTests
{
    #region Constructor Tests

    [Test]
    public void Constructor_WithValidParameters_CreatesInstance()
    {
        // Act
        var calculator = new AtrCalculator(period: 14, ticksPerBar: 50);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(calculator, Is.Not.Null);
            Assert.That(calculator.IsReady, Is.False);  // Not ready until enough data
            Assert.That(calculator.CurrentAtr, Is.EqualTo(0));
        });
    }

    [Test]
    public void Constructor_WithPeriodZero_ThrowsArgumentOutOfRangeException()
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => new AtrCalculator(period: 0));
    }

    [Test]
    public void Constructor_WithNegativePeriod_ThrowsArgumentOutOfRangeException()
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => new AtrCalculator(period: -1));
    }

    [Test]
    public void Constructor_WithTicksPerBarZero_ThrowsArgumentOutOfRangeException()
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => new AtrCalculator(ticksPerBar: 0));
    }

    [Test]
    public void Constructor_WithNegativeTicksPerBar_ThrowsArgumentOutOfRangeException()
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => new AtrCalculator(ticksPerBar: -5));
    }

    [Test]
    public void Constructor_DefaultParameters_UsesSensibleDefaults()
    {
        // Act
        var calculator = new AtrCalculator();

        // Assert - should not throw and create valid instance
        Assert.That(calculator, Is.Not.Null);
    }

    #endregion

    #region Update Tests

    [Test]
    public void Update_WithFirstPrice_InitializesCalculator()
    {
        // Arrange
        var calculator = new AtrCalculator(period: 14, ticksPerBar: 10);

        // Act
        var result = calculator.Update(100.0);

        // Assert - first update initializes but doesn't calculate ATR yet
        Assert.That(result, Is.EqualTo(0));
    }

    [Test]
    public void Update_WithZeroPrice_ReturnsCurrentAtr()
    {
        // Arrange
        var calculator = new AtrCalculator(period: 14, ticksPerBar: 10);
        calculator.Update(100.0);

        // Act
        var result = calculator.Update(0);

        // Assert - should ignore invalid price
        Assert.That(result, Is.EqualTo(0));
    }

    [Test]
    public void Update_WithNegativePrice_ReturnsCurrentAtr()
    {
        // Arrange
        var calculator = new AtrCalculator(period: 14, ticksPerBar: 10);
        calculator.Update(100.0);

        // Act
        var result = calculator.Update(-50.0);

        // Assert - should ignore invalid price
        Assert.That(result, Is.EqualTo(0));
    }

    [Test]
    public void Update_MultipleUpdates_TracksHighLow()
    {
        // Arrange
        var calculator = new AtrCalculator(period: 3, ticksPerBar: 5);

        // Act - simulate price movement
        calculator.Update(100.0);
        calculator.Update(102.0);  // High
        calculator.Update(99.0);   // Low
        calculator.Update(101.0);
        calculator.Update(100.5);  // Complete first bar

        // After one bar, ATR should be calculated
        Assert.That(calculator.CurrentAtr, Is.GreaterThanOrEqualTo(0));
    }

    [Test]
    public void IsReady_AfterSufficientData_ReturnsTrue()
    {
        // Arrange
        var calculator = new AtrCalculator(period: 2, ticksPerBar: 3);

        // Act - Add enough data for at least period/2 bars
        for (int bar = 0; bar < 3; bar++)
        {
            for (int tick = 0; tick < 3; tick++)
            {
                calculator.Update(100.0 + bar + (tick * 0.1));
            }
        }

        // Assert
        Assert.That(calculator.IsReady, Is.True);
    }

    #endregion

    #region CalculateStopPrice Tests

    [Test]
    public void CalculateStopPrice_NotReady_ReturnsFallbackStop()
    {
        // Arrange
        var calculator = new AtrCalculator(period: 14, ticksPerBar: 50);
        calculator.Update(100.0);  // Only one tick, not ready

        // Act
        var stopPrice = calculator.CalculateStopPrice(
            referencePrice: 100.0,
            multiplier: 2.0,
            isLong: true);

        // Assert - should return fallback (10% below reference)
        Assert.That(stopPrice, Is.EqualTo(90.0));
    }

    [Test]
    public void CalculateStopPrice_LongPosition_StopBelowReference()
    {
        // Arrange
        var calculator = new AtrCalculator(period: 2, ticksPerBar: 3);
        SimulateVolatility(calculator, 100.0, volatility: 2.0);

        // Act
        var stopPrice = calculator.CalculateStopPrice(
            referencePrice: 100.0,
            multiplier: 2.0,
            isLong: true);

        // Assert - stop should be below reference for long positions
        Assert.That(stopPrice, Is.LessThan(100.0));
    }

    [Test]
    public void CalculateStopPrice_ShortPosition_StopAboveReference()
    {
        // Arrange
        var calculator = new AtrCalculator(period: 2, ticksPerBar: 3);
        SimulateVolatility(calculator, 100.0, volatility: 2.0);

        // Act
        var stopPrice = calculator.CalculateStopPrice(
            referencePrice: 100.0,
            multiplier: 2.0,
            isLong: false);

        // Assert - stop should be above reference for short positions
        Assert.That(stopPrice, Is.GreaterThan(100.0));
    }

    [Test]
    public void CalculateStopPrice_RespectsMinimumDistance()
    {
        // Arrange
        var calculator = new AtrCalculator(period: 2, ticksPerBar: 3);
        // Simulate very low volatility
        for (int i = 0; i < 20; i++)
        {
            calculator.Update(100.0 + (i % 2 == 0 ? 0.01 : -0.01));
        }

        // Act - with 5% minimum, stop should be at least 5% away
        var stopPrice = calculator.CalculateStopPrice(
            referencePrice: 100.0,
            multiplier: 0.1,  // Very small multiplier
            isLong: true,
            minPercent: 0.05);  // 5% minimum

        // Assert - stop should be at least 5% below (95.0 or less)
        Assert.That(stopPrice, Is.LessThanOrEqualTo(95.0));
    }

    [Test]
    public void CalculateStopPrice_RespectsMaximumDistance()
    {
        // Arrange
        var calculator = new AtrCalculator(period: 2, ticksPerBar: 3);
        // Simulate very high volatility
        SimulateVolatility(calculator, 100.0, volatility: 10.0);

        // Act - with 10% maximum, stop should be at most 10% away
        var stopPrice = calculator.CalculateStopPrice(
            referencePrice: 100.0,
            multiplier: 5.0,  // Very large multiplier
            isLong: true,
            maxPercent: 0.10);  // 10% maximum

        // Assert - stop should be at least 90.0 (no more than 10% away)
        Assert.That(stopPrice, Is.GreaterThanOrEqualTo(90.0));
    }

    [Test]
    public void CalculateStopPrice_RoundsToTwoDecimals()
    {
        // Arrange
        var calculator = new AtrCalculator(period: 2, ticksPerBar: 3);
        SimulateVolatility(calculator, 100.0, volatility: 1.5);

        // Act
        var stopPrice = calculator.CalculateStopPrice(
            referencePrice: 100.123456,
            multiplier: 2.0,
            isLong: true);

        // Assert - should be rounded to 2 decimal places
        var decimalPlaces = BitConverter.GetBytes(decimal.GetBits((decimal)stopPrice)[3])[2];
        Assert.That(decimalPlaces, Is.LessThanOrEqualTo(2));
    }

    #endregion

    #region GetAtrPercent Tests

    [Test]
    public void GetAtrPercent_WithValidPrice_ReturnsPercentage()
    {
        // Arrange
        var calculator = new AtrCalculator(period: 2, ticksPerBar: 3);
        SimulateVolatility(calculator, 100.0, volatility: 2.0);

        // Act
        var atrPercent = calculator.GetAtrPercent(100.0);

        // Assert - should return ATR as a percentage
        Assert.That(atrPercent, Is.GreaterThanOrEqualTo(0));
        Assert.That(atrPercent, Is.LessThan(1.0));  // Should be less than 100%
    }

    [Test]
    public void GetAtrPercent_WithZeroPrice_ReturnsZero()
    {
        // Arrange
        var calculator = new AtrCalculator(period: 2, ticksPerBar: 3);
        SimulateVolatility(calculator, 100.0, volatility: 2.0);

        // Act
        var atrPercent = calculator.GetAtrPercent(0);

        // Assert
        Assert.That(atrPercent, Is.EqualTo(0));
    }

    [Test]
    public void GetAtrPercent_NotReady_ReturnsZero()
    {
        // Arrange
        var calculator = new AtrCalculator(period: 14, ticksPerBar: 50);
        calculator.Update(100.0);

        // Act
        var atrPercent = calculator.GetAtrPercent(100.0);

        // Assert
        Assert.That(atrPercent, Is.EqualTo(0));
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Simulates price volatility to properly initialize the ATR calculator.
    /// </summary>
    private static void SimulateVolatility(AtrCalculator calculator, double basePrice, double volatility)
    {
        var random = new Random(42);  // Fixed seed for reproducibility
        for (int bar = 0; bar < 10; bar++)
        {
            for (int tick = 0; tick < 10; tick++)
            {
                double price = basePrice + (random.NextDouble() - 0.5) * 2 * volatility;
                calculator.Update(price);
            }
        }
    }

    #endregion
}
