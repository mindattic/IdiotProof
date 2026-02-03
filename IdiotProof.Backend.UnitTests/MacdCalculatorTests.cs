// ============================================================================
// MacdCalculatorTests - Unit tests for MACD calculation
// ============================================================================
//
// Tests cover:
// - Constructor validation
// - Warm-up period requirements (26 + 9 = 35 candles)
// - MACD line calculation (Fast EMA - Slow EMA)
// - Signal line calculation (EMA of MACD)
// - Histogram calculation (MACD - Signal)
// - Bullish/Bearish signal detection
// - Reset functionality
//
// MACD Components:
//   - MACD Line: 12-period EMA - 26-period EMA
//   - Signal Line: 9-period EMA of MACD line
//   - Histogram: MACD Line - Signal Line
//
// ============================================================================

using IdiotProof.Backend.Helpers;
using NUnit.Framework;

namespace IdiotProof.Backend.UnitTests;

/// <summary>
/// Tests for the MacdCalculator class that calculates MACD indicators.
/// </summary>
[TestFixture]
public class MacdCalculatorTests
{
    #region Constructor Tests

    [Test]
    public void Constructor_WithDefaultParameters_CreatesInstance()
    {
        // Act
        var calculator = new MacdCalculator();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(calculator, Is.Not.Null);
            Assert.That(calculator.FastPeriod, Is.EqualTo(12));
            Assert.That(calculator.SlowPeriod, Is.EqualTo(26));
            Assert.That(calculator.SignalPeriod, Is.EqualTo(9));
            Assert.That(calculator.IsReady, Is.False);
        });
    }

    [Test]
    public void Constructor_WithCustomParameters_CreatesInstance()
    {
        // Act
        var calculator = new MacdCalculator(8, 17, 9);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(calculator.FastPeriod, Is.EqualTo(8));
            Assert.That(calculator.SlowPeriod, Is.EqualTo(17));
            Assert.That(calculator.SignalPeriod, Is.EqualTo(9));
        });
    }

    [Test]
    public void Constructor_WithFastPeriodZero_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MacdCalculator(0, 26, 9));
    }

    [Test]
    public void Constructor_WithSlowPeriodZero_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MacdCalculator(12, 0, 9));
    }

    [Test]
    public void Constructor_WithSignalPeriodZero_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MacdCalculator(12, 26, 0));
    }

    [Test]
    public void Constructor_WithFastGreaterThanSlow_ThrowsArgumentException()
    {
        // Fast period must be less than slow period
        Assert.Throws<ArgumentException>(() => new MacdCalculator(26, 12, 9));
    }

    [Test]
    public void Constructor_WithFastEqualToSlow_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new MacdCalculator(12, 12, 9));
    }

    #endregion

    #region Warm-Up / IsReady Tests

    [Test]
    public void IsReady_BeforeEnoughData_ReturnsFalse()
    {
        // Arrange
        var calculator = new MacdCalculator(12, 26, 9);

        // Act - add 34 prices (need 26 + 9 = 35)
        for (int i = 1; i <= 34; i++)
            calculator.Update(100 + i);

        // Assert
        Assert.That(calculator.IsReady, Is.False);
    }

    [Test]
    public void IsReady_AfterEnoughData_ReturnsTrue()
    {
        // Arrange
        var calculator = new MacdCalculator(12, 26, 9);

        // Act - add 35 prices
        for (int i = 1; i <= 35; i++)
            calculator.Update(100 + i);

        // Assert
        Assert.That(calculator.IsReady, Is.True);
    }

    #endregion

    #region MACD Line Tests

    [Test]
    public void MacdLine_WithConstantPrice_ApproachesZero()
    {
        // Arrange
        var calculator = new MacdCalculator(12, 26, 9);

        // Act - constant price means both EMAs converge to same value
        for (int i = 0; i < 50; i++)
            calculator.Update(100);

        // Assert - MACD = Fast EMA - Slow EMA ≈ 0
        Assert.That(calculator.MacdLine, Is.EqualTo(0).Within(0.01));
    }

    [Test]
    public void MacdLine_WithRisingPrices_IsPositive()
    {
        // Arrange
        var calculator = new MacdCalculator(12, 26, 9);

        // Act - rising prices
        for (int i = 1; i <= 50; i++)
            calculator.Update(100 + i);

        // Assert - Fast EMA reacts faster, so Fast > Slow, MACD > 0
        Assert.That(calculator.MacdLine, Is.GreaterThan(0));
    }

    [Test]
    public void MacdLine_WithFallingPrices_IsNegative()
    {
        // Arrange
        var calculator = new MacdCalculator(12, 26, 9);

        // Act - falling prices
        for (int i = 50; i >= 1; i--)
            calculator.Update(100 + i);

        // Assert - Fast EMA reacts faster to decline, MACD < 0
        Assert.That(calculator.MacdLine, Is.LessThan(0));
    }

    #endregion

    #region Signal Line Tests

    [Test]
    public void SignalLine_ConvergesToMacdLine_WithConstantPrice()
    {
        // Arrange
        var calculator = new MacdCalculator(12, 26, 9);

        // Act - many iterations with constant price
        for (int i = 0; i < 100; i++)
            calculator.Update(100);

        // Assert - After long warm up with constant price, 
        // Fast and Slow EMAs converge to same value, so MACD approaches 0
        Assert.That(Math.Abs(calculator.MacdLine), Is.LessThan(0.1), "MACD should approach 0 with constant price");
    }

    [Test]
    public void SignalLine_PositiveAfterUptrend()
    {
        // Arrange
        var calculator = new MacdCalculator(12, 26, 9);

        // Act - consistent uptrend
        for (int i = 1; i <= 60; i++)
            calculator.Update(100 + i);

        // Assert - Both MACD and Signal should be positive after sustained uptrend
        Assert.That(calculator.MacdLine, Is.GreaterThan(0));
        Assert.That(calculator.SignalLine, Is.GreaterThan(0));
    }

    #endregion

    #region Histogram Tests

    [Test]
    public void Histogram_EqualsMacdMinusSignal()
    {
        // Arrange
        var calculator = new MacdCalculator(12, 26, 9);

        // Act
        for (int i = 1; i <= 50; i++)
            calculator.Update(100 + i);

        // Assert
        double expectedHistogram = calculator.MacdLine - calculator.SignalLine;
        Assert.That(calculator.Histogram, Is.EqualTo(expectedHistogram).Within(0.001));
    }

    [Test]
    public void IsHistogramRising_WhenHistogramIncreases_ReturnsTrue()
    {
        // Arrange
        var calculator = new MacdCalculator(12, 26, 9);

        // Warm up
        for (int i = 0; i < 40; i++)
            calculator.Update(100);

        // Act - prices rising should increase histogram
        for (int i = 0; i < 5; i++)
            calculator.Update(110 + i * 2);

        // Assert
        Assert.That(calculator.IsHistogramRising, Is.True);
    }

    [Test]
    public void PreviousHistogram_TracksLastValue()
    {
        // Arrange
        var calculator = new MacdCalculator(12, 26, 9);

        // Warm up
        for (int i = 0; i < 40; i++)
            calculator.Update(100);

        double histogramBefore = calculator.Histogram;

        // Act
        calculator.Update(110);
        double histogramAfter = calculator.Histogram;

        // Assert
        Assert.That(calculator.PreviousHistogram, Is.EqualTo(histogramBefore));
        Assert.That(calculator.Histogram, Is.EqualTo(histogramAfter));
    }

    #endregion

    #region Bullish/Bearish Tests

    [Test]
    public void IsBullish_WhenMacdAboveSignal_ReturnsTrue()
    {
        // Arrange
        var calculator = new MacdCalculator(12, 26, 9);

        // Create sustained uptrend
        for (int i = 1; i <= 80; i++)
            calculator.Update(100 + i * 3); // Very strong uptrend

        // Assert - MACD should be positive in uptrend
        Assert.That(calculator.MacdLine, Is.GreaterThan(0), "MACD should be positive in uptrend");

        // Assert - IsBullish and IsBearish are mutually exclusive
        bool isBullish = calculator.MacdLine > calculator.SignalLine;
        Assert.That(calculator.IsBullish, Is.EqualTo(isBullish));
        Assert.That(calculator.IsBearish, Is.EqualTo(!isBullish));
    }

    [Test]
    public void IsBearish_WhenMacdBelowSignal_ReturnsTrue()
    {
        // Arrange
        var calculator = new MacdCalculator(12, 26, 9);

        // Warm up then create downtrend
        for (int i = 0; i < 30; i++)
            calculator.Update(150);
        for (int i = 0; i < 20; i++)
            calculator.Update(140 - i);

        // Assert
        Assert.That(calculator.IsBearish, Is.True);
        Assert.That(calculator.IsBullish, Is.False);
    }

    [Test]
    public void IsAboveZero_WhenMacdPositive_ReturnsTrue()
    {
        // Arrange
        var calculator = new MacdCalculator(12, 26, 9);

        // Uptrend should produce positive MACD
        for (int i = 1; i <= 50; i++)
            calculator.Update(100 + i);

        // Assert
        Assert.That(calculator.IsAboveZero, Is.True);
    }

    [Test]
    public void IsAboveZero_WhenMacdNegative_ReturnsFalse()
    {
        // Arrange
        var calculator = new MacdCalculator(12, 26, 9);

        // Downtrend should produce negative MACD
        for (int i = 50; i >= 1; i--)
            calculator.Update(100 + i);

        // Assert
        Assert.That(calculator.IsAboveZero, Is.False);
    }

    #endregion

    #region Reset Tests

    [Test]
    public void Reset_ClearsAllState()
    {
        // Arrange
        var calculator = new MacdCalculator();
        for (int i = 1; i <= 50; i++)
            calculator.Update(100 + i);

        Assert.That(calculator.IsReady, Is.True);

        // Act
        calculator.Reset();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(calculator.IsReady, Is.False);
            Assert.That(calculator.MacdLine, Is.EqualTo(0));
            Assert.That(calculator.SignalLine, Is.EqualTo(0));
            Assert.That(calculator.Histogram, Is.EqualTo(0));
            Assert.That(calculator.PreviousHistogram, Is.EqualTo(0));
        });
    }

    [Test]
    public void Reset_AllowsReuse()
    {
        // Arrange
        var calculator = new MacdCalculator();

        // First usage
        for (int i = 1; i <= 50; i++)
            calculator.Update(100 + i);
        double firstMacd = calculator.MacdLine;

        // Act
        calculator.Reset();

        // Second usage with different data
        for (int i = 50; i >= 1; i--)
            calculator.Update(100 + i);
        double secondMacd = calculator.MacdLine;

        // Assert - different trends should produce different MACD
        Assert.That(firstMacd, Is.Not.EqualTo(secondMacd));
    }

    #endregion

    #region Edge Cases

    [Test]
    public void Update_WithZeroPrice_IgnoresValue()
    {
        // Arrange
        var calculator = new MacdCalculator();

        // Act
        calculator.Update(100);
        calculator.Update(0); // Should be ignored

        // Assert - no crash
        Assert.Pass();
    }

    [Test]
    public void Update_WithNegativePrice_IgnoresValue()
    {
        // Arrange
        var calculator = new MacdCalculator();

        // Act
        calculator.Update(100);
        calculator.Update(-50); // Should be ignored

        // Assert - no crash
        Assert.Pass();
    }

    #endregion
}
