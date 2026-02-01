// ============================================================================
// FluentApiBuilderTests - Tests for Stock and StrategyBuilder fluent API
// ============================================================================

using IdiotProof.Backend.Enums;
using IdiotProof.Backend.Models;
using IdiotProof.Backend.Strategy;
using NUnit.Framework;

namespace IdiotProof.Backend.UnitTests;

/// <summary>
/// Tests for the Stock fluent builder and StrategyBuilder classes.
/// Validates all fluent API methods work correctly and chain properly.
/// </summary>
[TestFixture]
public class FluentApiBuilderTests
{
    #region Stock.Ticker Tests

    [Test]
    public void Ticker_WithValidSymbol_CreatesBuilder()
    {
        // Arrange & Act
        var builder = Stock.Ticker("AAPL");

        // Assert
        Assert.That(builder, Is.Not.Null);
    }

    [Test]
    public void Ticker_WithNullSymbol_ThrowsArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => Stock.Ticker(null!));
    }

    [Test]
    public void Ticker_WithEmptySymbol_CreatesBuilder()
    {
        // Empty string is technically valid (no business validation)
        var builder = Stock.Ticker("");
        Assert.That(builder, Is.Not.Null);
    }

    #endregion

    #region Stock Configuration Methods

    [Test]
    public void Exchange_SetsExchangeOnStrategy()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .Exchange("NASDAQ")
            .Breakout(150)
            .Buy(100, Price.Current)
            .Build();

        // Assert
        Assert.That(strategy.Exchange, Is.EqualTo("NASDAQ"));
    }

    [Test]
    public void Currency_SetsCurrencyOnStrategy()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .Currency("EUR")
            .Breakout(150)
            .Buy(100, Price.Current)
            .Build();

        // Assert
        Assert.That(strategy.Currency, Is.EqualTo("EUR"));
    }

    [Test]
    public void Enabled_False_DisablesStrategy()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .Enabled(false)
            .Breakout(150)
            .Buy(100, Price.Current)
            .Build();

        // Assert
        Assert.That(strategy.Enabled, Is.False);
    }

    [Test]
    public void Enabled_True_EnablesStrategy()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .Enabled(true)
            .Breakout(150)
            .Buy(100, Price.Current)
            .Build();

        // Assert
        Assert.That(strategy.Enabled, Is.True);
    }

    [Test]
    public void TimeFrame_WithStartAndEndTime_SetsStartTime()
    {
        // Arrange
        var startTime = new TimeOnly(3, 0);
        var endTime = new TimeOnly(9, 30);

        // Act
        var strategy = Stock.Ticker("AAPL")
            .TimeFrame(startTime, endTime)
            .Breakout(150)
            .Buy(100, Price.Current)
            .Build();

        // Assert
        Assert.That(strategy.StartTime, Is.EqualTo(startTime));
    }

    [Test]
    public void TimeFrame_SetsBothStartAndEndTime()
    {
        // Arrange
        var startTime = new TimeOnly(4, 0);
        var endTime = new TimeOnly(9, 30);

        // Act
        var strategy = Stock.Ticker("AAPL")
            .TimeFrame(startTime, endTime)
            .Breakout(150)
            .Buy(100, Price.Current)
            .Build();

        // Assert
        Assert.That(strategy.StartTime, Is.EqualTo(startTime));
        Assert.That(strategy.EndTime, Is.EqualTo(endTime));
    }

    [Test]
    public void TimeFrame_CanBeChainedWithOtherMethods()
    {
        // Arrange
        var startTime = new TimeOnly(4, 0);
        var endTime = new TimeOnly(9, 30);

        // Act
        var strategy = Stock.Ticker("AAPL")
            .Exchange("NASDAQ")
            .TimeFrame(startTime, endTime)
            .Enabled(true)
            .Breakout(150)
            .Buy(100, Price.Current)
            .TakeProfit(155)
            .Build();

        // Assert
        Assert.That(strategy.Exchange, Is.EqualTo("NASDAQ"));
        Assert.That(strategy.StartTime, Is.EqualTo(startTime));
        Assert.That(strategy.EndTime, Is.EqualTo(endTime));
        Assert.That(strategy.Enabled, Is.True);
    }

    [Test]
    public void TimeFrame_WithPreMarketHours_SetsCorrectWindow()
    {
        // Arrange - Pre-market hours 4:00 AM to 9:30 AM
        var preMarketStart = new TimeOnly(4, 0);
        var preMarketEnd = new TimeOnly(9, 30);

        // Act
        var strategy = Stock.Ticker("AAPL")
            .TimeFrame(preMarketStart, preMarketEnd)
            .Breakout(150)
            .Buy(100, Price.Current)
            .Build();

        // Assert
        Assert.That(strategy.StartTime, Is.EqualTo(preMarketStart));
        Assert.That(strategy.EndTime, Is.EqualTo(preMarketEnd));
    }

    [Test]
    public void TimeFrame_WithRegularTradingHours_SetsCorrectWindow()
    {
        // Arrange - Regular trading hours 9:30 AM to 4:00 PM
        var marketOpen = new TimeOnly(9, 30);
        var marketClose = new TimeOnly(16, 0);

        // Act
        var strategy = Stock.Ticker("AAPL")
            .TimeFrame(marketOpen, marketClose)
            .Breakout(150)
            .Buy(100, Price.Current)
            .Build();

        // Assert
        Assert.That(strategy.StartTime, Is.EqualTo(marketOpen));
        Assert.That(strategy.EndTime, Is.EqualTo(marketClose));
    }

    [Test]
    public void SessionDuration_WhenOmitted_BothTimesAreNull()
    {
        // Arrange & Act - No SessionDuration called
        var strategy = Stock.Ticker("AAPL")
            .Breakout(150)
            .Buy(100, Price.Current)
            .Build();

        // Assert
        Assert.That(strategy.StartTime, Is.Null);
        Assert.That(strategy.EndTime, Is.Null);
    }

    [Test]
    public void SessionDuration_WithTradingSessionPreMarket_SetsCorrectWindow()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .TimeFrame(TradingSession.PreMarket)
            .Breakout(150)
            .Buy(100, Price.Current)
            .Build();

        // Assert
        Assert.That(strategy.StartTime, Is.EqualTo(new TimeOnly(4, 0)));
        Assert.That(strategy.EndTime, Is.EqualTo(new TimeOnly(9, 30)));
    }

    [Test]
    public void SessionDuration_WithTradingSessionRTH_SetsCorrectWindow()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .TimeFrame(TradingSession.RTH)
            .Breakout(150)
            .Buy(100, Price.Current)
            .Build();

        // Assert
        Assert.That(strategy.StartTime, Is.EqualTo(new TimeOnly(9, 30)));
        Assert.That(strategy.EndTime, Is.EqualTo(new TimeOnly(16, 0)));
    }

    [Test]
    public void SessionDuration_WithTradingSessionAfterHours_SetsCorrectWindow()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .TimeFrame(TradingSession.AfterHours)
            .Breakout(150)
            .Buy(100, Price.Current)
            .Build();

        // Assert
        Assert.That(strategy.StartTime, Is.EqualTo(new TimeOnly(16, 0)));
        Assert.That(strategy.EndTime, Is.EqualTo(new TimeOnly(20, 0)));
    }

    [Test]
    public void SessionDuration_WithTradingSessionExtended_SetsCorrectWindow()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .TimeFrame(TradingSession.Extended)
            .Breakout(150)
            .Buy(100, Price.Current)
            .Build();

        // Assert
        Assert.That(strategy.StartTime, Is.EqualTo(new TimeOnly(4, 0)));
        Assert.That(strategy.EndTime, Is.EqualTo(new TimeOnly(20, 0)));
    }

    [Test]
    public void SessionDuration_WithPreMarketEndEarly_SetsCorrectWindow()
    {
        // Arrange & Act - Should end at 9:15 AM (15 min before 9:30)
        var strategy = Stock.Ticker("AAPL")
            .TimeFrame(TradingSession.PreMarketEndEarly)
            .Breakout(150)
            .Buy(100, Price.Current)
            .Build();

        // Assert
        Assert.That(strategy.StartTime, Is.EqualTo(new TimeOnly(4, 0)));
        Assert.That(strategy.EndTime, Is.EqualTo(new TimeOnly(9, 15)));
    }

    [Test]
    public void SessionDuration_WithPreMarketStartLate_SetsCorrectWindow()
    {
        // Arrange & Act - Should start at 4:15 AM (15 min after 4:00)
        var strategy = Stock.Ticker("AAPL")
            .TimeFrame(TradingSession.PreMarketStartLate)
            .Breakout(150)
            .Buy(100, Price.Current)
            .Build();

        // Assert
        Assert.That(strategy.StartTime, Is.EqualTo(new TimeOnly(4, 15)));
        Assert.That(strategy.EndTime, Is.EqualTo(new TimeOnly(9, 30)));
    }

    [Test]
    public void SessionDuration_WithRTHEndEarly_SetsCorrectWindow()
    {
        // Arrange & Act - Should end at 3:45 PM (15 min before 4:00)
        var strategy = Stock.Ticker("AAPL")
            .TimeFrame(TradingSession.RTHEndEarly)
            .Breakout(150)
            .Buy(100, Price.Current)
            .Build();

        // Assert
        Assert.That(strategy.StartTime, Is.EqualTo(new TimeOnly(9, 30)));
        Assert.That(strategy.EndTime, Is.EqualTo(new TimeOnly(15, 45)));
    }

    [Test]
    public void SessionDuration_WithRTHStartLate_SetsCorrectWindow()
    {
        // Arrange & Act - Should start at 9:45 AM (15 min after 9:30)
        var strategy = Stock.Ticker("AAPL")
            .TimeFrame(TradingSession.RTHStartLate)
            .Breakout(150)
            .Buy(100, Price.Current)
            .Build();

        // Assert
        Assert.That(strategy.StartTime, Is.EqualTo(new TimeOnly(9, 45)));
        Assert.That(strategy.EndTime, Is.EqualTo(new TimeOnly(16, 0)));
    }

    [Test]
    public void SessionDuration_WithAfterHoursEndEarly_SetsCorrectWindow()
    {
        // Arrange & Act - Should end at 7:45 PM (15 min before 8:00)
        var strategy = Stock.Ticker("AAPL")
            .TimeFrame(TradingSession.AfterHoursEndEarly)
            .Breakout(150)
            .Buy(100, Price.Current)
            .Build();

        // Assert
        Assert.That(strategy.StartTime, Is.EqualTo(new TimeOnly(16, 0)));
        Assert.That(strategy.EndTime, Is.EqualTo(new TimeOnly(19, 45)));
    }

    [Test]
    public void SessionDuration_WithActive_ClearsTimeRestrictions()
    {
        // Arrange & Act - Should clear any time restrictions
        var strategy = Stock.Ticker("AAPL")
            .TimeFrame(TradingSession.Active)
            .Breakout(150)
            .Buy(100, Price.Current)
            .Build();

        // Assert
        Assert.That(strategy.StartTime, Is.Null);
        Assert.That(strategy.EndTime, Is.Null);
    }

    [Test]
    public void SessionDuration_ActiveCanOverridePreviousSession()
    {
        // Arrange & Act - Active should clear previous time restrictions
        var strategy = Stock.Ticker("AAPL")
            .TimeFrame(TradingSession.PreMarket)  // Set premarket first
            .TimeFrame(TradingSession.Active)     // Then clear with Active
            .Breakout(150)
            .Buy(100, Price.Current)
            .Build();

        // Assert
        Assert.That(strategy.StartTime, Is.Null);
        Assert.That(strategy.EndTime, Is.Null);
    }

    #endregion

    #region Condition Methods

    [Test]
    public void Breakout_AddsBreakoutCondition()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .Breakout(150.00)
            .Buy(100, Price.Current)
            .Build();

        // Assert
        Assert.That(strategy.Conditions, Has.Count.EqualTo(1));
        Assert.That(strategy.Conditions[0].Name, Does.Contain("150"));
    }

    [Test]
    public void Pullback_AddsPullbackCondition()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .Pullback(148.00)
            .Buy(100, Price.Current)
            .Build();

        // Assert
        Assert.That(strategy.Conditions, Has.Count.EqualTo(1));
        Assert.That(strategy.Conditions[0].Name, Does.Contain("148"));
    }

    [Test]
    public void AboveVwap_AddsAboveVwapCondition()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .IsAboveVwap()
            .Buy(100, Price.Current)
            .Build();

        // Assert
        Assert.That(strategy.Conditions, Has.Count.EqualTo(1));
        Assert.That(strategy.Conditions[0].Name, Does.Contain("VWAP").IgnoreCase);
    }

    [Test]
    public void AboveVwap_WithBuffer_AddsBufferedCondition()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .IsAboveVwap(0.05)
            .Buy(100, Price.Current)
            .Build();

        // Assert
        Assert.That(strategy.Conditions, Has.Count.EqualTo(1));
    }

    [Test]
    public void BelowVwap_AddsBelowVwapCondition()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .IsBelowVwap()
            .Buy(100, Price.Current)
            .Build();

        // Assert
        Assert.That(strategy.Conditions, Has.Count.EqualTo(1));
    }

    [Test]
    public void PriceAbove_AddsPriceAtOrAboveCondition()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .IsPriceAbove(100)
            .Buy(100, Price.Current)
            .Build();

        // Assert
        Assert.That(strategy.Conditions, Has.Count.EqualTo(1));
    }

    [Test]
    public void PriceBelow_AddsPriceBelowCondition()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .IsPriceBelow(200)
            .Buy(100, Price.Current)
            .Build();

        // Assert
        Assert.That(strategy.Conditions, Has.Count.EqualTo(1));
    }

    [Test]
    public void When_AddsCustomCondition()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .When("Custom condition", (price, vwap) => price > vwap)
            .Buy(100, Price.Current)
            .Build();

        // Assert
        Assert.That(strategy.Conditions, Has.Count.EqualTo(1));
        Assert.That(strategy.Conditions[0].Name, Is.EqualTo("Custom condition"));
    }

    [Test]
    public void MultipleConditions_AddsAllInOrder()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .Breakout(150)
            .Pullback(148)
            .IsAboveVwap()
            .Buy(100, Price.Current)
            .Build();

        // Assert
        Assert.That(strategy.Conditions, Has.Count.EqualTo(3));
    }

    #endregion

    #region Order Methods

    [Test]
    public void Buy_CreatesStrategyWithBuyOrder()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .Breakout(150)
            .Buy(100, Price.Current)
            .Build();

        // Assert
        Assert.That(strategy.Order.Side, Is.EqualTo(OrderSide.Buy));
        Assert.That(strategy.Order.Quantity, Is.EqualTo(100));
    }

    [Test]
    public void Buy_WithPriceType_SetsPriceType()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .Breakout(150)
            .Buy(100, Price.VWAP)
            .Build();

        // Assert
        Assert.That(strategy.Order.PriceType, Is.EqualTo(Price.VWAP));
    }

    [Test]
    public void Sell_CreatesStrategyWithSellOrder()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .Breakout(150)
            .Sell(100, Price.Current)
            .Build();

        // Assert
        Assert.That(strategy.Order.Side, Is.EqualTo(OrderSide.Sell));
    }

    #endregion

    #region StrategyBuilder Exit Methods

    [Test]
    public void TakeProfit_SetsTakeProfitPrice()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .Breakout(150)
            .Buy(100, Price.Current)
            .TakeProfit(160)
            .Build();

        // Assert
        Assert.That(strategy.Order.EnableTakeProfit, Is.True);
        Assert.That(strategy.Order.TakeProfitPrice, Is.EqualTo(160));
    }

    [Test]
    public void StopLoss_SetsStopLossPrice()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .Breakout(150)
            .Buy(100, Price.Current)
            .StopLoss(145)
            .Build();

        // Assert
        Assert.That(strategy.Order.EnableStopLoss, Is.True);
        Assert.That(strategy.Order.StopLossPrice, Is.EqualTo(145));
    }

    [Test]
    public void TrailingStopLoss_EnablesTrailingStop()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .Breakout(150)
            .Buy(100, Price.Current)
            .TrailingStopLoss(Percent.Ten)
            .Build();

        // Assert
        Assert.That(strategy.Order.EnableTrailingStopLoss, Is.True);
        Assert.That(strategy.Order.TrailingStopLossPercent, Is.EqualTo(0.10));
    }

    [Test]
    public void ClosePosition_SetsClosePositionTime()
    {
        // Arrange
        var closeTime = new TimeOnly(6, 50);

        // Act
        var strategy = Stock.Ticker("AAPL")
            .Breakout(150)
            .Buy(100, Price.Current)
            .ClosePosition(closeTime)
            .Build();

        // Assert
        Assert.That(strategy.Order.ClosePositionTime, Is.EqualTo(closeTime));
    }

    [Test]
    public void ClosePosition_DefaultOnlyIfProfitable_IsTrue()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .Breakout(150)
            .Buy(100, Price.Current)
            .ClosePosition(MarketTime.PreMarket.Ending)
            .Build();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(strategy.Order.ClosePositionTime, Is.EqualTo(MarketTime.PreMarket.Ending));
            Assert.That(strategy.Order.ClosePositionOnlyIfProfitable, Is.True);
        });
    }

    [Test]
    public void ClosePosition_OnlyIfProfitableTrue_SetsFlag()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .Breakout(150)
            .Buy(100, Price.Current)
            .ClosePosition(MarketTime.PreMarket.Ending, onlyIfProfitable: true)
            .Build();

        // Assert
        Assert.That(strategy.Order.ClosePositionOnlyIfProfitable, Is.True);
    }

    [Test]
    public void ClosePosition_OnlyIfProfitableFalse_SetsFlag()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .Breakout(150)
            .Buy(100, Price.Current)
            .ClosePosition(MarketTime.PreMarket.Ending, onlyIfProfitable: false)
            .Build();

        // Assert
        Assert.That(strategy.Order.ClosePositionOnlyIfProfitable, Is.False);
    }

    [Test]
    public void ClosePosition_WithAllOptions_ConfiguresCorrectly()
    {
        // Arrange
        var closeTime = new TimeOnly(9, 20);

        // Act
        var strategy = Stock.Ticker("TEST")
            .TimeFrame(TradingSession.PreMarket)
            .IsPriceAbove(5.00)
            .IsAboveVwap()
            .Buy(100, Price.Current)
            .TakeProfit(6.00, 7.00)
            .StopLoss(4.50)
            .ClosePosition(closeTime, onlyIfProfitable: false)
            .Build();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(strategy.Order.ClosePositionTime, Is.EqualTo(closeTime));
            Assert.That(strategy.Order.ClosePositionOnlyIfProfitable, Is.False);
            Assert.That(strategy.Order.EnableTakeProfit, Is.True);
            Assert.That(strategy.Order.EnableStopLoss, Is.True);
        });
    }

    [Test]
    public void ClosePosition_UsingTimePreMarketEnding_SetsCorrectTime()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .Breakout(150)
            .Buy(100, Price.Current)
            .ClosePosition(MarketTime.PreMarket.Ending)
            .Build();

        // Assert - Time.PreMarket.Ending should be 9:15 AM ET (15 min before 9:30)
        Assert.That(strategy.Order.ClosePositionTime, Is.EqualTo(new TimeOnly(9, 15)));
    }

    [Test]
    public void ClosePosition_PremarketStrategy_OnlyIfProfitableDefault()
    {
        // Arrange & Act - Typical premarket strategy
        var strategy = Stock.Ticker("VIVS")
            .TimeFrame(TradingSession.PreMarket)
            .IsPriceAbove(2.40)
            .IsAboveVwap()
            .Buy(100, Price.Current)
            .TakeProfit(4.00, 4.80)
            .ClosePosition(MarketTime.PreMarket.Ending)
            .Build();

        // Assert - Default should only close if profitable
        Assert.Multiple(() =>
        {
            Assert.That(strategy.Order.ClosePositionTime, Is.EqualTo(MarketTime.PreMarket.Ending));
            Assert.That(strategy.Order.ClosePositionOnlyIfProfitable, Is.True);
        });
    }

    [Test]
    public void ClosePosition_ForceCloseAtLoss_OnlyIfProfitableFalse()
    {
        // Arrange & Act - Strategy that must close regardless of P&L
        var strategy = Stock.Ticker("TEST")
            .TimeFrame(TradingSession.PreMarket)
            .IsPriceAbove(10.00)
            .Buy(100, Price.Current)
            .TakeProfit(12.00)
            .ClosePosition(MarketTime.PreMarket.Ending, onlyIfProfitable: false)
            .Build();

        // Assert - Will close even at a loss
        Assert.Multiple(() =>
        {
            Assert.That(strategy.Order.ClosePositionTime, Is.EqualTo(MarketTime.PreMarket.Ending));
            Assert.That(strategy.Order.ClosePositionOnlyIfProfitable, Is.False);
        });
    }

    [Test]
    public void TimeInForce_SetsTimeInForce()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .Breakout(150)
            .Buy(100, Price.Current)
            .TimeInForce(TIF.Day)
            .Build();

        // Assert
        Assert.That(strategy.Order.TimeInForce, Is.EqualTo(TimeInForce.Day));
    }

    [Test]
    public void OutsideRTH_SetsOutsideRthFlags()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .Breakout(150)
            .Buy(100, Price.Current)
            .OutsideRTH(outsideRth: false, takeProfit: false)
            .Build();

        // Assert
        Assert.That(strategy.Order.OutsideRth, Is.False);
        Assert.That(strategy.Order.TakeProfitOutsideRth, Is.False);
    }

    [Test]
    public void SessionDuration_CustomTimes_SetsBothStartAndEnd()
    {
        // Arrange
        var startTime = new TimeOnly(4, 0);
        var endTime = new TimeOnly(7, 0);

        // Act
        var strategy = Stock.Ticker("AAPL")
            .TimeFrame(startTime, endTime)
            .Breakout(150)
            .Buy(100, Price.Current)
            .Build();

        // Assert
        Assert.That(strategy.StartTime, Is.EqualTo(startTime));
        Assert.That(strategy.EndTime, Is.EqualTo(endTime));
    }

    #endregion

    #region Chaining Tests

    [Test]
    public void FullFluentChain_CreatesCompleteStrategy()
    {
        // Arrange & Act
        var strategy = Stock
            .Ticker("NAMM")
            .TimeFrame(TradingSession.PreMarket)
            .Breakout(7.10)
            .Pullback(6.80)
            .IsAboveVwap()
            .Buy(quantity: 100, Price.Current)
            .TakeProfit(9.00)
            .StopLoss(6.50)
            .TrailingStopLoss(Percent.Ten)
            .ClosePosition(MarketTime.PreMarket.End.AddMinutes(-10))
            .Build();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(strategy.Symbol, Is.EqualTo("NAMM"));
            Assert.That(strategy.StartTime, Is.EqualTo(MarketTime.PreMarket.Start));
            Assert.That(strategy.EndTime, Is.EqualTo(MarketTime.PreMarket.End));
            Assert.That(strategy.Conditions, Has.Count.EqualTo(3));
            Assert.That(strategy.Order.Quantity, Is.EqualTo(100));
            Assert.That(strategy.Order.EnableTakeProfit, Is.True);
            Assert.That(strategy.Order.TakeProfitPrice, Is.EqualTo(9.00));
            Assert.That(strategy.Order.EnableStopLoss, Is.True);
            Assert.That(strategy.Order.StopLossPrice, Is.EqualTo(6.50));
            Assert.That(strategy.Order.EnableTrailingStopLoss, Is.True);
        });
    }

    [Test]
    public void ImplicitConversion_StrategyBuilderToTradingStrategy()
    {
        // Arrange & Act - implicit conversion via assignment
        TradingStrategy strategy = Stock
            .Ticker("AAPL")
            .Breakout(150)
            .Buy(100, Price.Current)
            .TakeProfit(160);

        // Assert
        Assert.That(strategy, Is.Not.Null);
        Assert.That(strategy.Symbol, Is.EqualTo("AAPL"));
    }

    #endregion

    #region Default Values Tests

    [Test]
    public void Defaults_ExchangeIsSmart()
    {
        var strategy = Stock.Ticker("AAPL").Breakout(150).Buy(100, Price.Current).Build();
        Assert.That(strategy.Exchange, Is.EqualTo("SMART"));
    }

    [Test]
    public void Defaults_CurrencyIsUsd()
    {
        var strategy = Stock.Ticker("AAPL").Breakout(150).Buy(100, Price.Current).Build();
        Assert.That(strategy.Currency, Is.EqualTo("USD"));
    }

    [Test]
    public void Defaults_EnabledIsTrue()
    {
        var strategy = Stock.Ticker("AAPL").Breakout(150).Buy(100, Price.Current).Build();
        Assert.That(strategy.Enabled, Is.True);
    }

    [Test]
    public void Defaults_TimeInForceIsGtc()
    {
        var strategy = Stock.Ticker("AAPL").Breakout(150).Buy(100, Price.Current).Build();
        Assert.That(strategy.Order.TimeInForce, Is.EqualTo(TimeInForce.GoodTillCancel));
    }

    [Test]
    public void Defaults_OutsideRthIsTrue()
    {
        var strategy = Stock.Ticker("AAPL").Breakout(150).Buy(100, Price.Current).Build();
        Assert.That(strategy.Order.OutsideRth, Is.True);
    }

    #endregion

    #region Close Order Method Tests

    /// <summary>
    /// Tests for the Close() method which creates orders to exit existing positions.
    /// </summary>
    [TestFixture]
    public class CloseOrderTests
    {
        #region Close() Basic Tests

        [Test]
        [Description("Close with default positionSide (Buy) creates SELL order to close long")]
        public void Close_DefaultPositionSide_CreatesSellOrder()
        {
            // Arrange & Act - Default positionSide is Buy (long position), so Close creates SELL
            var strategy = Stock.Ticker("AAPL")
                .IsPriceAbove(150)
                .Close(quantity: 100)
                .Build();

            // Assert
            Assert.That(strategy.Order.Side, Is.EqualTo(OrderSide.Sell));
            Assert.That(strategy.Order.Quantity, Is.EqualTo(100));
        }

        [Test]
        [Description("Close with positionSide=Buy creates SELL order to close long")]
        public void Close_LongPosition_CreatesSellOrder()
        {
            // Arrange & Act
            var strategy = Stock.Ticker("AAPL")
                .IsPriceAbove(155)
                .Close(quantity: 100, positionSide: OrderSide.Buy)
                .Build();

            // Assert - Closing a long (Buy) position means SELL
            Assert.That(strategy.Order.Side, Is.EqualTo(OrderSide.Sell));
        }

        [Test]
        [Description("Close with positionSide=Sell creates BUY order to close short")]
        public void Close_ShortPosition_CreatesBuyOrder()
        {
            // Arrange & Act
            var strategy = Stock.Ticker("TSLA")
                .IsPriceBelow(200)
                .Close(quantity: 50, positionSide: OrderSide.Sell)
                .Build();

            // Assert - Closing a short (Sell) position means BUY to cover
            Assert.That(strategy.Order.Side, Is.EqualTo(OrderSide.Buy));
        }

        [Test]
        [Description("Close sets quantity correctly")]
        public void Close_WithQuantity_SetsQuantity()
        {
            var strategy = Stock.Ticker("AAPL")
                .IsPriceAbove(150)
                .Close(quantity: 250)
                .Build();

            Assert.That(strategy.Order.Quantity, Is.EqualTo(250));
        }

        [Test]
        [Description("Close sets priceType correctly")]
        public void Close_WithPriceType_SetsPriceType()
        {
            var strategy = Stock.Ticker("AAPL")
                .IsPriceAbove(150)
                .Close(quantity: 100, priceType: Price.VWAP)
                .Build();

            Assert.That(strategy.Order.PriceType, Is.EqualTo(Price.VWAP));
        }

        [Test]
        [Description("Close with Market order type")]
        public void Close_WithMarketOrderType_SetsMarket()
        {
            var strategy = Stock.Ticker("AAPL")
                .IsPriceAbove(150)
                .Close(quantity: 100, orderType: OrderType.Market)
                .Build();

            Assert.That(strategy.Order.Type, Is.EqualTo(OrderType.Market));
        }

        [Test]
        [Description("Close with Limit order type")]
        public void Close_WithLimitOrderType_SetsLimit()
        {
            var strategy = Stock.Ticker("AAPL")
                .IsPriceAbove(150)
                .Close(quantity: 100, orderType: OrderType.Limit)
                .Build();

            Assert.That(strategy.Order.Type, Is.EqualTo(OrderType.Limit));
        }

        #endregion

        #region CloseLong() Tests

        [Test]
        [Description("CloseLong creates SELL order")]
        public void CloseLong_CreatesSellOrder()
        {
            var strategy = Stock.Ticker("AAPL")
                .IsPriceAbove(160)
                .CloseLong(quantity: 100)
                .Build();

            Assert.That(strategy.Order.Side, Is.EqualTo(OrderSide.Sell));
        }

        [Test]
        [Description("CloseLong is equivalent to Close with positionSide=Buy")]
        public void CloseLong_EquivalentToCloseWithBuy()
        {
            var closeLongStrategy = Stock.Ticker("AAPL")
                .IsPriceAbove(160)
                .CloseLong(quantity: 100, Price.Current, OrderType.Market)
                .Build();

            var closeStrategy = Stock.Ticker("AAPL")
                .IsPriceAbove(160)
                .Close(quantity: 100, positionSide: OrderSide.Buy, Price.Current, OrderType.Market)
                .Build();

            Assert.Multiple(() =>
            {
                Assert.That(closeLongStrategy.Order.Side, Is.EqualTo(closeStrategy.Order.Side));
                Assert.That(closeLongStrategy.Order.Quantity, Is.EqualTo(closeStrategy.Order.Quantity));
                Assert.That(closeLongStrategy.Order.Type, Is.EqualTo(closeStrategy.Order.Type));
            });
        }

        #endregion

        #region CloseShort() Tests

        [Test]
        [Description("CloseShort creates BUY order to cover")]
        public void CloseShort_CreatesBuyOrder()
        {
            var strategy = Stock.Ticker("TSLA")
                .IsPriceBelow(180)
                .CloseShort(quantity: 50)
                .Build();

            Assert.That(strategy.Order.Side, Is.EqualTo(OrderSide.Buy));
        }

        [Test]
        [Description("CloseShort is equivalent to Close with positionSide=Sell")]
        public void CloseShort_EquivalentToCloseWithSell()
        {
            var closeShortStrategy = Stock.Ticker("TSLA")
                .IsPriceBelow(180)
                .CloseShort(quantity: 50, Price.Bid, OrderType.Limit)
                .Build();

            var closeStrategy = Stock.Ticker("TSLA")
                .IsPriceBelow(180)
                .Close(quantity: 50, positionSide: OrderSide.Sell, Price.Bid, OrderType.Limit)
                .Build();

            Assert.Multiple(() =>
            {
                Assert.That(closeShortStrategy.Order.Side, Is.EqualTo(closeStrategy.Order.Side));
                Assert.That(closeShortStrategy.Order.Quantity, Is.EqualTo(closeStrategy.Order.Quantity));
                Assert.That(closeShortStrategy.Order.PriceType, Is.EqualTo(closeStrategy.Order.PriceType));
                Assert.That(closeShortStrategy.Order.Type, Is.EqualTo(closeStrategy.Order.Type));
            });
        }

        #endregion

        #region Close Does NOT Auto-Set TIF/OutsideRth Tests

        [Test]
        [Description("Close does NOT automatically set TimeInForce - uses default GTC")]
        public void Close_DoesNotAutoSetTIF_UsesDefault()
        {
            var strategy = Stock.Ticker("AAPL")
                .IsPriceAbove(150)
                .Close(quantity: 100)
                .Build();

            // Default is GTC, not forced by Close
            Assert.That(strategy.Order.TimeInForce, Is.EqualTo(TimeInForce.GoodTillCancel));
        }

        [Test]
        [Description("Close allows explicit TimeInForce setting")]
        public void Close_WithExplicitTIF_SetsTIF()
        {
            var strategy = Stock.Ticker("AAPL")
                .IsPriceAbove(150)
                .Close(quantity: 100)
                .TimeInForce(TIF.Day)
                .Build();

            Assert.That(strategy.Order.TimeInForce, Is.EqualTo(TimeInForce.Day));
        }

        [Test]
        [Description("Close does NOT automatically set OutsideRth - uses default true")]
        public void Close_DoesNotAutoSetOutsideRth_UsesDefault()
        {
            var strategy = Stock.Ticker("AAPL")
                .IsPriceAbove(150)
                .Close(quantity: 100)
                .Build();

            // Default is true, not forced by Close
            Assert.That(strategy.Order.OutsideRth, Is.True);
        }

        [Test]
        [Description("Close allows explicit OutsideRth setting")]
        public void Close_WithExplicitOutsideRth_SetsOutsideRth()
        {
            var strategy = Stock.Ticker("AAPL")
                .IsPriceAbove(150)
                .Close(quantity: 100)
                .OutsideRTH(outsideRth: false, takeProfit: false)
                .Build();

            Assert.That(strategy.Order.OutsideRth, Is.False);
        }

        #endregion

        #region Close with Full Configuration Tests

        [Test]
        [Description("Close with full fluent chain configuration")]
        public void Close_FullFluentChain_ConfiguresCorrectly()
        {
            var strategy = Stock.Ticker("AAPL")
                .TimeFrame(TradingSession.PreMarket)
                .IsPriceAbove(155)
                .CloseLong(quantity: 100, Price.Current, OrderType.Market)
                .TimeInForce(TIF.GTC)
                .OutsideRTH(outsideRth: true, takeProfit: true)
                .Build();

            Assert.Multiple(() =>
            {
                Assert.That(strategy.Symbol, Is.EqualTo("AAPL"));
                Assert.That(strategy.Order.Side, Is.EqualTo(OrderSide.Sell));
                Assert.That(strategy.Order.Quantity, Is.EqualTo(100));
                Assert.That(strategy.Order.Type, Is.EqualTo(OrderType.Market));
                Assert.That(strategy.Order.TimeInForce, Is.EqualTo(TimeInForce.GoodTillCancel));
                Assert.That(strategy.Order.OutsideRth, Is.True);
                Assert.That(strategy.StartTime, Is.EqualTo(MarketTime.PreMarket.Start));
                Assert.That(strategy.EndTime, Is.EqualTo(MarketTime.PreMarket.End));
            });
        }

        [Test]
        [Description("Close overnight earnings play scenario")]
        public void Close_OvernightEarningsPlay_ConfiguresCorrectly()
        {
            // Scenario: Close long position during after-hours if price hits target
            var strategy = Stock.Ticker("NVDA")
                .IsPriceAbove(500)
                .CloseLong(quantity: 25)
                .TimeInForce(TIF.Overnight)
                .OutsideRTH(outsideRth: true, takeProfit: true)
                .Build();

            Assert.Multiple(() =>
            {
                Assert.That(strategy.Order.Side, Is.EqualTo(OrderSide.Sell));
                Assert.That(strategy.Order.TimeInForce, Is.EqualTo(TimeInForce.Overnight));
                Assert.That(strategy.Order.OutsideRth, Is.True);
            });
        }

        [Test]
        [Description("Close at market open scenario")]
        public void Close_AtMarketOpen_ConfiguresCorrectly()
        {
            // Scenario: Close position at market open auction
            var strategy = Stock.Ticker("SPY")
                .IsPriceAbove(450)
                .CloseLong(quantity: 500)
                .TimeInForce(TIF.AtTheOpening)
                .Build();

            Assert.Multiple(() =>
            {
                Assert.That(strategy.Order.Side, Is.EqualTo(OrderSide.Sell));
                Assert.That(strategy.Order.TimeInForce, Is.EqualTo(TimeInForce.AtTheOpening));
            });
        }

        #endregion

        #region IBKR API Compatibility Tests

        [Test]
        [Description("Close generates correct IB action for long position")]
        public void Close_LongPosition_GetIbAction_ReturnsSELL()
        {
            var strategy = Stock.Ticker("AAPL")
                .IsPriceAbove(150)
                .Close(quantity: 100, positionSide: OrderSide.Buy)
                .Build();

            Assert.That(strategy.Order.GetIbAction(), Is.EqualTo("SELL"));
        }

        [Test]
        [Description("Close generates correct IB action for short position")]
        public void Close_ShortPosition_GetIbAction_ReturnsBUY()
        {
            var strategy = Stock.Ticker("TSLA")
                .IsPriceBelow(200)
                .Close(quantity: 50, positionSide: OrderSide.Sell)
                .Build();

            Assert.That(strategy.Order.GetIbAction(), Is.EqualTo("BUY"));
        }

        [Test]
        [Description("Close generates correct IB order type for market order")]
        public void Close_MarketOrder_GetIbOrderType_ReturnsMKT()
        {
            var strategy = Stock.Ticker("AAPL")
                .IsPriceAbove(150)
                .Close(quantity: 100, orderType: OrderType.Market)
                .Build();

            Assert.That(strategy.Order.GetIbOrderType(), Is.EqualTo("MKT"));
        }

        [Test]
        [Description("Close generates correct IB order type for limit order")]
        public void Close_LimitOrder_GetIbOrderType_ReturnsLMT()
        {
            var strategy = Stock.Ticker("AAPL")
                .IsPriceAbove(150)
                .Close(quantity: 100, orderType: OrderType.Limit)
                .Build();

            Assert.That(strategy.Order.GetIbOrderType(), Is.EqualTo("LMT"));
        }

        [Test]
        [Description("Close generates correct IB TIF code")]
        public void Close_WithGTC_GetIbTif_ReturnsGTC()
        {
            var strategy = Stock.Ticker("AAPL")
                .IsPriceAbove(150)
                .Close(quantity: 100)
                .TimeInForce(TIF.GTC)
                .Build();

            Assert.That(strategy.Order.GetIbTif(), Is.EqualTo("GTC"));
        }

        #endregion
    }

    #endregion

    #region AllOrNone Tests

    /// <summary>
    /// Tests for the AllOrNone order attribute.
    /// </summary>
    [TestFixture]
    public class AllOrNoneTests
    {
        [Test]
        [Description("Default AllOrNone is false")]
        public void AllOrNone_Default_IsFalse()
        {
            var strategy = Stock.Ticker("AAPL")
                .Breakout(150)
                .Buy(100, Price.Current)
                .Build();

            Assert.That(strategy.Order.AllOrNone, Is.False);
        }

        [Test]
        [Description("AllOrNone() with no parameter sets to true")]
        public void AllOrNone_NoParameter_SetsTrue()
        {
            var strategy = Stock.Ticker("AAPL")
                .Breakout(150)
                .Buy(100, Price.Current)
                .AllOrNone()
                .Build();

            Assert.That(strategy.Order.AllOrNone, Is.True);
        }

        [Test]
        [Description("AllOrNone(true) sets to true")]
        public void AllOrNone_True_SetsTrue()
        {
            var strategy = Stock.Ticker("AAPL")
                .Breakout(150)
                .Buy(100, Price.Current)
                .AllOrNone(true)
                .Build();

            Assert.That(strategy.Order.AllOrNone, Is.True);
        }

        [Test]
        [Description("AllOrNone(false) sets to false")]
        public void AllOrNone_False_SetsFalse()
        {
            var strategy = Stock.Ticker("AAPL")
                .Breakout(150)
                .Buy(100, Price.Current)
                .AllOrNone(false)
                .Build();

            Assert.That(strategy.Order.AllOrNone, Is.False);
        }

        [Test]
        [Description("AllOrNone works with Sell orders")]
        public void AllOrNone_WithSell_SetsCorrectly()
        {
            var strategy = Stock.Ticker("AAPL")
                .IsPriceAbove(150)
                .Sell(100, Price.Current)
                .AllOrNone()
                .Build();

            Assert.Multiple(() =>
            {
                Assert.That(strategy.Order.Side, Is.EqualTo(OrderSide.Sell));
                Assert.That(strategy.Order.AllOrNone, Is.True);
            });
        }

        [Test]
        [Description("AllOrNone works with Close orders")]
        public void AllOrNone_WithClose_SetsCorrectly()
        {
            var strategy = Stock.Ticker("AAPL")
                .IsPriceAbove(155)
                .CloseLong(100)
                .AllOrNone()
                .Build();

            Assert.Multiple(() =>
            {
                Assert.That(strategy.Order.Side, Is.EqualTo(OrderSide.Sell));
                Assert.That(strategy.Order.AllOrNone, Is.True);
            });
        }

        [Test]
        [Description("AllOrNone can be chained with other methods")]
        public void AllOrNone_ChainedWithOtherMethods_AllSet()
        {
            var strategy = Stock.Ticker("AAPL")
                .Breakout(150)
                .Buy(100, Price.Current)
                .AllOrNone()
                .TimeInForce(TIF.Day)
                .OutsideRTH(false)
                .TakeProfit(160)
                .Build();

            Assert.Multiple(() =>
            {
                Assert.That(strategy.Order.AllOrNone, Is.True);
                Assert.That(strategy.Order.TimeInForce, Is.EqualTo(TimeInForce.Day));
                Assert.That(strategy.Order.OutsideRth, Is.False);
                Assert.That(strategy.Order.EnableTakeProfit, Is.True);
                Assert.That(strategy.Order.TakeProfitPrice, Is.EqualTo(160));
            });
        }

        [Test]
        [Description("AllOrNone can be toggled off after being set")]
        public void AllOrNone_CanBeToggledOff()
        {
            var strategy = Stock.Ticker("AAPL")
                .Breakout(150)
                .Buy(100, Price.Current)
                .AllOrNone(true)
                .AllOrNone(false)  // Toggle off
                .Build();

            Assert.That(strategy.Order.AllOrNone, Is.False);
        }

        [Test]
        [Description("OrderAction AllOrNone default via direct instantiation")]
        public void OrderAction_AllOrNone_DefaultIsFalse()
        {
            var order = new OrderAction();
            Assert.That(order.AllOrNone, Is.False);
        }

        [Test]
        [Description("OrderAction AllOrNone can be set via initializer")]
        public void OrderAction_AllOrNone_InitializerSyntax()
        {
            var order = new OrderAction { AllOrNone = true };
            Assert.That(order.AllOrNone, Is.True);
        }
    }

    #endregion

    #region Multiple Strategies Per Symbol Tests

    /// <summary>
    /// Tests verifying multiple strategies can be created for the same stock symbol.
    /// This is a common use case where different entry conditions target the same ticker.
    /// </summary>
    [TestFixture]
    public class MultipleStrategiesPerSymbolTests
    {
        [Test]
        [Description("Multiple strategies for same symbol can be created independently")]
        public void MultipleStrategies_SameSymbol_CreateIndependently()
        {
            // Arrange & Act - Create two different strategies for VIVS
            var momentumStrategy = Stock.Ticker("VIVS")
                .IsPriceAbove(2.40)
                .IsAboveVwap()
                .Buy(100, Price.Current)
                .TakeProfit(4.00, 4.80)
                .Build();

            var pullbackStrategy = Stock.Ticker("VIVS")
                .Pullback(4.15)
                .IsAboveVwap()
                .Buy(100, Price.Current)
                .TakeProfit(4.80, 5.30)
                .Build();

            // Assert - Both strategies exist with same symbol but different conditions
            Assert.Multiple(() =>
            {
                Assert.That(momentumStrategy.Symbol, Is.EqualTo("VIVS"));
                Assert.That(pullbackStrategy.Symbol, Is.EqualTo("VIVS"));
                Assert.That(momentumStrategy, Is.Not.SameAs(pullbackStrategy));
                Assert.That(momentumStrategy.Conditions[0].Name, Does.Contain(">="));
                Assert.That(pullbackStrategy.Conditions[0].Name, Does.Contain("<="));
            });
        }

        [Test]
        [Description("Multiple strategies for same symbol can have different entry conditions")]
        public void MultipleStrategies_SameSymbol_DifferentEntryConditions()
        {
            // Arrange
            var strategies = new List<TradingStrategy>
            {
                Stock.Ticker("CATX")
                    .IsPriceAbove(4.00)
                    .IsAboveVwap()
                    .Buy(100, Price.Current)
                    .Build(),

                Stock.Ticker("CATX")
                    .Pullback(4.33)
                    .IsPriceAbove(4.30)
                    .Buy(100, Price.Current)
                    .Build()
            };

            // Act
            var catxStrategies = strategies.FindAll(s => s.Symbol == "CATX");

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(catxStrategies, Has.Count.EqualTo(2));
                Assert.That(catxStrategies[0].Conditions[0], Is.TypeOf<PriceAtOrAboveCondition>());
                Assert.That(catxStrategies[1].Conditions[0], Is.TypeOf<PullbackCondition>());
            });
        }

        [Test]
        [Description("Multiple strategies for same symbol can have different take profit targets")]
        public void MultipleStrategies_SameSymbol_DifferentTakeProfitTargets()
        {
            // Arrange & Act
            var conservativeStrategy = Stock.Ticker("VIVS")
                .IsPriceAbove(3.00)
                .Buy(100, Price.Current)
                .TakeProfit(3.50)
                .Build();

            var aggressiveStrategy = Stock.Ticker("VIVS")
                .IsPriceAbove(3.00)
                .Buy(100, Price.Current)
                .TakeProfit(4.50, 5.50)
                .Build();

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(conservativeStrategy.Symbol, Is.EqualTo(aggressiveStrategy.Symbol));
                Assert.That(conservativeStrategy.Order.TakeProfitPrice, Is.EqualTo(3.50));
                Assert.That(aggressiveStrategy.Order.AdxTakeProfit!.ConservativeTarget, Is.EqualTo(4.50));
                Assert.That(aggressiveStrategy.Order.AdxTakeProfit!.AggressiveTarget, Is.EqualTo(5.50));
            });
        }

        [Test]
        [Description("Multiple strategies for same symbol can have different stop loss configurations")]
        public void MultipleStrategies_SameSymbol_DifferentStopLossConfig()
        {
            // Arrange & Act
            var fixedStopStrategy = Stock.Ticker("CATX")
                .IsPriceAbove(4.80)
                .Buy(100, Price.Current)
                .StopLoss(4.25)
                .Build();

            var trailingStopStrategy = Stock.Ticker("CATX")
                .IsPriceAbove(4.80)
                .Buy(100, Price.Current)
                .TrailingStopLoss(Percent.TwentyFive)
                .Build();

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(fixedStopStrategy.Symbol, Is.EqualTo(trailingStopStrategy.Symbol));
                Assert.That(fixedStopStrategy.Order.EnableStopLoss, Is.True);
                Assert.That(fixedStopStrategy.Order.EnableTrailingStopLoss, Is.False);
                Assert.That(trailingStopStrategy.Order.EnableStopLoss, Is.False);
                Assert.That(trailingStopStrategy.Order.EnableTrailingStopLoss, Is.True);
            });
        }

        [Test]
        [Description("Multiple strategies for same symbol can have different session durations")]
        public void MultipleStrategies_SameSymbol_DifferentSessionDurations()
        {
            // Arrange & Act
            var preMarketStrategy = Stock.Ticker("VIVS")
                .TimeFrame(TradingSession.PreMarket)
                .IsPriceAbove(2.40)
                .Buy(100, Price.Current)
                .Build();

            var rthStrategy = Stock.Ticker("VIVS")
                .TimeFrame(TradingSession.RTH)
                .IsPriceAbove(2.40)
                .Buy(100, Price.Current)
                .Build();

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(preMarketStrategy.Symbol, Is.EqualTo(rthStrategy.Symbol));
                Assert.That(preMarketStrategy.Session, Is.EqualTo(TradingSession.PreMarket));
                Assert.That(rthStrategy.Session, Is.EqualTo(TradingSession.RTH));
            });
        }

        [Test]
        [Description("Multiple strategies for same symbol can have different quantities")]
        public void MultipleStrategies_SameSymbol_DifferentQuantities()
        {
            // Arrange & Act
            var smallPositionStrategy = Stock.Ticker("CATX")
                .IsPriceAbove(4.00)
                .Buy(50, Price.Current)
                .Build();

            var largePositionStrategy = Stock.Ticker("CATX")
                .IsPriceAbove(4.00)
                .Buy(500, Price.Current)
                .Build();

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(smallPositionStrategy.Symbol, Is.EqualTo(largePositionStrategy.Symbol));
                Assert.That(smallPositionStrategy.Order.Quantity, Is.EqualTo(50));
                Assert.That(largePositionStrategy.Order.Quantity, Is.EqualTo(500));
            });
        }

        [Test]
        [Description("Strategy list with multiple symbols and multiple strategies per symbol")]
        public void MultipleStrategies_MixedSymbols_AllIndependent()
        {
            // Arrange - Real-world scenario from Program.cs
            var strategies = new List<TradingStrategy>
            {
                // VIVS Momentum
                Stock.Ticker("VIVS")
                    .TimeFrame(TradingSession.PreMarketEndEarly)
                    .IsPriceAbove(2.40)
                    .IsAboveVwap()
                    .Buy(100, Price.Current)
                    .TakeProfit(4.00, 4.80)
                    .TrailingStopLoss(Percent.TwentyFive)
                    .ClosePosition(MarketTime.PreMarket.Ending, false),

                // CATX Momentum
                Stock.Ticker("CATX")
                    .TimeFrame(TradingSession.PreMarketEndEarly)
                    .IsPriceAbove(4.00)
                    .IsAboveVwap()
                    .Buy(100, Price.Current)
                    .TakeProfit(5.30, 6.16)
                    .TrailingStopLoss(Percent.TwentyFive)
                    .ClosePosition(MarketTime.PreMarket.Ending, false),

                // VIVS Pullback
                Stock.Ticker("VIVS")
                    .TimeFrame(TradingSession.PreMarketEndEarly)
                    .Pullback(4.15)
                    .IsAboveVwap()
                    .Buy(100, Price.Current)
                    .TakeProfit(4.80, 5.30)
                    .StopLoss(3.95)
                    .ClosePosition(MarketTime.PreMarket.Ending, false),

                // CATX Support
                Stock.Ticker("CATX")
                    .TimeFrame(TradingSession.PreMarketEndEarly)
                    .Pullback(4.33)
                    .IsPriceAbove(4.30)
                    .Buy(100, Price.Current)
                    .TakeProfit(4.50, 4.75)
                    .StopLoss(4.25)
                    .ClosePosition(MarketTime.PreMarket.Ending, false),
            };

            // Act
            var vivsStrategies = strategies.FindAll(s => s.Symbol == "VIVS");
            var catxStrategies = strategies.FindAll(s => s.Symbol == "CATX");
            var enabledStrategies = strategies.FindAll(s => s.Enabled);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(strategies, Has.Count.EqualTo(4));
                Assert.That(vivsStrategies, Has.Count.EqualTo(2));
                Assert.That(catxStrategies, Has.Count.EqualTo(2));
                Assert.That(enabledStrategies, Has.Count.EqualTo(4));

                // Verify each strategy is unique
                Assert.That(strategies.Distinct().Count(), Is.EqualTo(4));

                // Verify VIVS strategies have different first conditions
                Assert.That(vivsStrategies[0].Conditions[0], Is.TypeOf<PriceAtOrAboveCondition>());
                Assert.That(vivsStrategies[1].Conditions[0], Is.TypeOf<PullbackCondition>());

                // Verify CATX strategies have different first conditions
                Assert.That(catxStrategies[0].Conditions[0], Is.TypeOf<PriceAtOrAboveCondition>());
                Assert.That(catxStrategies[1].Conditions[0], Is.TypeOf<PullbackCondition>());
            });
        }

        [Test]
        [Description("Multiple strategies with same symbol don't share state")]
        public void MultipleStrategies_SameSymbol_NoSharedState()
        {
            // Arrange & Act
            var strategy1 = Stock.Ticker("VIVS")
                .Enabled(true)
                .IsPriceAbove(2.40)
                .Buy(100, Price.Current)
                .Build();

            var strategy2 = Stock.Ticker("VIVS")
                .Enabled(false)
                .IsPriceAbove(3.00)
                .Buy(200, Price.Current)
                .Build();

            // Assert - Changing strategy2 doesn't affect strategy1
            Assert.Multiple(() =>
            {
                Assert.That(strategy1.Enabled, Is.True);
                Assert.That(strategy2.Enabled, Is.False);
                Assert.That(strategy1.Order.Quantity, Is.EqualTo(100));
                Assert.That(strategy2.Order.Quantity, Is.EqualTo(200));
            });
        }

        [Test]
        [Description("Filter strategies by symbol correctly")]
        public void MultipleStrategies_FilterBySymbol_ReturnsCorrectStrategies()
        {
            // Arrange
            var strategies = new List<TradingStrategy>
            {
                Stock.Ticker("AAPL").IsPriceAbove(150).Buy(100, Price.Current).Build(),
                Stock.Ticker("VIVS").IsPriceAbove(2.40).Buy(100, Price.Current).Build(),
                Stock.Ticker("AAPL").Pullback(145).Buy(50, Price.Current).Build(),
                Stock.Ticker("CATX").IsPriceAbove(4.00).Buy(100, Price.Current).Build(),
                Stock.Ticker("VIVS").Pullback(4.15).Buy(100, Price.Current).Build(),
            };

            // Act
            var aaplStrategies = strategies.Where(s => s.Symbol == "AAPL").ToList();
            var vivsStrategies = strategies.Where(s => s.Symbol == "VIVS").ToList();
            var catxStrategies = strategies.Where(s => s.Symbol == "CATX").ToList();
            var uniqueSymbols = strategies.Select(s => s.Symbol).Distinct().ToList();

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(aaplStrategies, Has.Count.EqualTo(2));
                Assert.That(vivsStrategies, Has.Count.EqualTo(2));
                Assert.That(catxStrategies, Has.Count.EqualTo(1));
                Assert.That(uniqueSymbols, Has.Count.EqualTo(3));
                Assert.That(uniqueSymbols, Does.Contain("AAPL"));
                Assert.That(uniqueSymbols, Does.Contain("VIVS"));
                Assert.That(uniqueSymbols, Does.Contain("CATX"));
            });
        }

        [Test]
        [Description("Strategies with same symbol can be enabled/disabled independently")]
        public void MultipleStrategies_SameSymbol_IndependentEnableDisable()
        {
            // Arrange
            var strategies = new List<TradingStrategy>
            {
                Stock.Ticker("VIVS")
                    .Enabled(true)
                    .IsPriceAbove(2.40)
                    .Buy(100, Price.Current)
                    .Build(),

                Stock.Ticker("VIVS")
                    .Enabled(false)  // Disabled
                    .Pullback(4.15)
                    .Buy(100, Price.Current)
                    .Build(),

                Stock.Ticker("VIVS")
                    .Enabled(true)
                    .IsPriceAbove(5.00)
                    .Buy(100, Price.Current)
                    .Build(),
            };

            // Act
            var enabledStrategies = strategies.FindAll(s => s.Enabled);
            var disabledStrategies = strategies.FindAll(s => !s.Enabled);
            var vivsEnabled = strategies.FindAll(s => s.Symbol == "VIVS" && s.Enabled);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(enabledStrategies, Has.Count.EqualTo(2));
                Assert.That(disabledStrategies, Has.Count.EqualTo(1));
                Assert.That(vivsEnabled, Has.Count.EqualTo(2));
            });
        }
    }

    #endregion
}
