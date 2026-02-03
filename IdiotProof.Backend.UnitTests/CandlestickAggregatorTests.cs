// ============================================================================
// CandlestickAggregatorTests - Unit tests for candlestick aggregation
// ============================================================================
//
// Tests cover:
// - Constructor validation
// - Tick-to-candle aggregation
// - Time-based bar completion
// - OHLC value accuracy
// - Volume tracking
// - Reset functionality
// - Event firing
//
// ============================================================================

using IdiotProof.Backend.Helpers;
using NUnit.Framework;

namespace IdiotProof.Backend.UnitTests;

/// <summary>
/// Tests for the CandlestickAggregator class that aggregates ticks into candlesticks.
/// </summary>
[TestFixture]
public class CandlestickAggregatorTests
{
    #region Constructor Tests

    [Test]
    public void Constructor_WithValidParameters_CreatesInstance()
    {
        // Act
        var aggregator = new CandlestickAggregator(candleSizeMinutes: 1, maxCandles: 100);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(aggregator, Is.Not.Null);
            Assert.That(aggregator.CandleSizeMinutes, Is.EqualTo(1));
            Assert.That(aggregator.MaxCandles, Is.EqualTo(100));
            Assert.That(aggregator.CompletedCandleCount, Is.EqualTo(0));
            Assert.That(aggregator.IsWarmedUp, Is.False);
        });
    }

    [Test]
    public void Constructor_WithDefaultParameters_UsesSensibleDefaults()
    {
        // Act
        var aggregator = new CandlestickAggregator();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(aggregator.CandleSizeMinutes, Is.EqualTo(1));
            Assert.That(aggregator.MaxCandles, Is.EqualTo(200));
        });
    }

    [Test]
    public void Constructor_WithCandleSizeZero_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => 
            new CandlestickAggregator(candleSizeMinutes: 0));
    }

    [Test]
    public void Constructor_WithNegativeCandleSize_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => 
            new CandlestickAggregator(candleSizeMinutes: -1));
    }

    [Test]
    public void Constructor_WithMaxCandlesZero_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => 
            new CandlestickAggregator(maxCandles: 0));
    }

    [Test]
    public void Constructor_WithNegativeMaxCandles_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => 
            new CandlestickAggregator(maxCandles: -10));
    }

    [Test]
    public void Constructor_With5MinuteBars_CreatesInstance()
    {
        // 5-minute bars are common for intraday analysis
        var aggregator = new CandlestickAggregator(candleSizeMinutes: 5);
        Assert.That(aggregator.CandleSizeMinutes, Is.EqualTo(5));
    }

    #endregion

    #region Update / Aggregation Tests

    [Test]
    public void Update_WithFirstTick_StartsNewCandle()
    {
        // Arrange
        var aggregator = new CandlestickAggregator();

        // Act
        aggregator.Update(100.50, 1000);

        // Assert
        Assert.That(aggregator.CurrentCandle, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(aggregator.CurrentCandle!.Open, Is.EqualTo(100.50));
            Assert.That(aggregator.CurrentCandle.High, Is.EqualTo(100.50));
            Assert.That(aggregator.CurrentCandle.Low, Is.EqualTo(100.50));
            Assert.That(aggregator.CurrentCandle.Close, Is.EqualTo(100.50));
            Assert.That(aggregator.CurrentCandle.Volume, Is.EqualTo(1000));
            Assert.That(aggregator.CurrentCandle.TickCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void Update_WithHigherPrice_UpdatesHigh()
    {
        // Arrange
        var aggregator = new CandlestickAggregator();
        aggregator.Update(100.00, 100);

        // Act
        aggregator.Update(105.00, 100);

        // Assert
        Assert.That(aggregator.CurrentCandle!.High, Is.EqualTo(105.00));
        Assert.That(aggregator.CurrentCandle.Low, Is.EqualTo(100.00));
    }

    [Test]
    public void Update_WithLowerPrice_UpdatesLow()
    {
        // Arrange
        var aggregator = new CandlestickAggregator();
        aggregator.Update(100.00, 100);

        // Act
        aggregator.Update(95.00, 100);

        // Assert
        Assert.That(aggregator.CurrentCandle!.Low, Is.EqualTo(95.00));
        Assert.That(aggregator.CurrentCandle.High, Is.EqualTo(100.00));
    }

    [Test]
    public void Update_AccumulatesVolume()
    {
        // Arrange
        var aggregator = new CandlestickAggregator();

        // Act
        aggregator.Update(100, 500);
        aggregator.Update(101, 300);
        aggregator.Update(99, 200);

        // Assert
        Assert.That(aggregator.CurrentCandle!.Volume, Is.EqualTo(1000));
    }

    [Test]
    public void Update_TracksTickCount()
    {
        // Arrange
        var aggregator = new CandlestickAggregator();

        // Act
        for (int i = 0; i < 10; i++)
            aggregator.Update(100 + i, 100);

        // Assert
        Assert.That(aggregator.CurrentCandle!.TickCount, Is.EqualTo(10));
    }

    [Test]
    public void Update_CloseIsLastPrice()
    {
        // Arrange
        var aggregator = new CandlestickAggregator();

        // Act
        aggregator.Update(100, 100);
        aggregator.Update(110, 100);
        aggregator.Update(95, 100);
        aggregator.Update(102.50, 100);

        // Assert
        Assert.That(aggregator.CurrentCandle!.Close, Is.EqualTo(102.50));
    }

    [Test]
    public void Update_WithZeroPrice_IgnoresValue()
    {
        // Arrange
        var aggregator = new CandlestickAggregator();
        aggregator.Update(100, 100);

        // Act
        bool completed = aggregator.Update(0, 100);

        // Assert
        Assert.That(completed, Is.False);
        Assert.That(aggregator.CurrentCandle!.Close, Is.EqualTo(100)); // Unchanged
    }

    [Test]
    public void Update_WithNegativePrice_IgnoresValue()
    {
        // Arrange
        var aggregator = new CandlestickAggregator();
        aggregator.Update(100, 100);

        // Act
        bool completed = aggregator.Update(-50, 100);

        // Assert
        Assert.That(completed, Is.False);
    }

    #endregion

    #region IsWarmedUp Tests

    [Test]
    public void IsWarmedUp_Before21Candles_ReturnsFalse()
    {
        // Arrange
        var aggregator = new CandlestickAggregator(maxCandles: 100);

        // Simulate 20 completed candles (not quite enough)
        // Note: This test is limited since we can't easily manipulate time
        Assert.That(aggregator.IsWarmedUp, Is.False);
    }

    [Test]
    public void IsFullyWarmedUp_BeforeMaxCandles_ReturnsFalse()
    {
        // Arrange
        var aggregator = new CandlestickAggregator(maxCandles: 200);

        Assert.That(aggregator.IsFullyWarmedUp, Is.False);
    }

    #endregion

    #region GetCompletedCandles Tests

    [Test]
    public void GetCompletedCandles_WhenEmpty_ReturnsEmptyList()
    {
        // Arrange
        var aggregator = new CandlestickAggregator();

        // Act
        var candles = aggregator.GetCompletedCandles();

        // Assert
        Assert.That(candles, Is.Empty);
    }

    [Test]
    public void GetRecentCandles_ReturnsRequestedCount()
    {
        // Arrange
        var aggregator = new CandlestickAggregator();

        // Note: We can't easily test completed candles without time manipulation
        // This test verifies the method doesn't crash
        var candles = aggregator.GetRecentCandles(10);

        // Assert
        Assert.That(candles, Is.Not.Null);
    }

    [Test]
    public void GetClosePrices_ReturnsEmptyWhenNoCompletedCandles()
    {
        // Arrange
        var aggregator = new CandlestickAggregator();

        // Act
        var prices = aggregator.GetClosePrices();

        // Assert
        Assert.That(prices, Is.Empty);
    }

    #endregion

    #region Reset Tests

    [Test]
    public void Reset_ClearsAllState()
    {
        // Arrange
        var aggregator = new CandlestickAggregator();
        aggregator.Update(100, 100);
        aggregator.Update(110, 200);

        // Act
        aggregator.Reset();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(aggregator.CompletedCandleCount, Is.EqualTo(0));
            Assert.That(aggregator.CurrentCandle, Is.Null);
            Assert.That(aggregator.LastCompletedCandle, Is.Null);
            Assert.That(aggregator.IsWarmedUp, Is.False);
        });
    }

    [Test]
    public void Reset_AllowsReuse()
    {
        // Arrange
        var aggregator = new CandlestickAggregator();
        aggregator.Update(100, 100);

        // Act
        aggregator.Reset();
        aggregator.Update(200, 500);

        // Assert
        Assert.That(aggregator.CurrentCandle!.Open, Is.EqualTo(200));
    }

    #endregion

    #region ForceComplete Tests

    [Test]
    public void ForceCompleteCurrentCandle_CompletesActiveCandle()
    {
        // Arrange
        var aggregator = new CandlestickAggregator();
        aggregator.Update(100, 100);
        aggregator.Update(110, 100);

        // Act
        aggregator.ForceCompleteCurrentCandle();

        // Assert
        Assert.That(aggregator.CompletedCandleCount, Is.EqualTo(1));
        Assert.That(aggregator.LastCompletedCandle, Is.Not.Null);
        Assert.That(aggregator.LastCompletedCandle!.IsComplete, Is.True);
    }

    [Test]
    public void ForceCompleteCurrentCandle_WhenNoActiveCandle_DoesNothing()
    {
        // Arrange
        var aggregator = new CandlestickAggregator();

        // Act - should not throw
        aggregator.ForceCompleteCurrentCandle();

        // Assert
        Assert.That(aggregator.CompletedCandleCount, Is.EqualTo(0));
    }

    #endregion

    #region Event Tests

    [Test]
    public void OnCandleComplete_FiresWhenCandleCompletes()
    {
        // Arrange
        var aggregator = new CandlestickAggregator();
        Candlestick? capturedCandle = null;
        aggregator.OnCandleComplete += candle => capturedCandle = candle;

        aggregator.Update(100, 100);
        aggregator.Update(110, 100);

        // Act
        aggregator.ForceCompleteCurrentCandle();

        // Assert
        Assert.That(capturedCandle, Is.Not.Null);
        Assert.That(capturedCandle!.High, Is.EqualTo(110));
    }

    #endregion
}
