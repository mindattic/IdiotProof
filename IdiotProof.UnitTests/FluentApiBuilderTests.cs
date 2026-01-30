// ============================================================================
// FluentApiBuilderTests - Tests for Stock and StrategyBuilder fluent API
// ============================================================================

using IdiotProof.Enums;
using IdiotProof.Models;
using NUnit.Framework;

namespace IdiotProof.UnitTests;

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
    public void Start_SetsStartTimeOnStrategy()
    {
        // Arrange
        var startTime = new TimeOnly(3, 0);

        // Act
        var strategy = Stock.Ticker("AAPL")
            .Start(startTime)
            .Breakout(150)
            .Buy(100, Price.Current)
            .Build();

        // Assert
        Assert.That(strategy.StartTime, Is.EqualTo(startTime));
    }

    [Test]
    public void SessionDuration_SetsBothStartAndEndTime()
    {
        // Arrange
        var startTime = new TimeOnly(4, 0);
        var endTime = new TimeOnly(9, 30);

        // Act
        var strategy = Stock.Ticker("AAPL")
            .SessionDuration(startTime, endTime)
            .Breakout(150)
            .Buy(100, Price.Current)
            .Build();

        // Assert
        Assert.That(strategy.StartTime, Is.EqualTo(startTime));
        Assert.That(strategy.EndTime, Is.EqualTo(endTime));
    }

    [Test]
    public void SessionDuration_CanBeChainedWithOtherMethods()
    {
        // Arrange
        var startTime = new TimeOnly(4, 0);
        var endTime = new TimeOnly(9, 30);

        // Act
        var strategy = Stock.Ticker("AAPL")
            .Exchange("NASDAQ")
            .SessionDuration(startTime, endTime)
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
    public void SessionDuration_WithPreMarketHours_SetsCorrectWindow()
    {
        // Arrange - Pre-market hours 4:00 AM to 9:30 AM
        var preMarketStart = new TimeOnly(4, 0);
        var preMarketEnd = new TimeOnly(9, 30);

        // Act
        var strategy = Stock.Ticker("AAPL")
            .SessionDuration(preMarketStart, preMarketEnd)
            .Breakout(150)
            .Buy(100, Price.Current)
            .Build();

        // Assert
        Assert.That(strategy.StartTime, Is.EqualTo(preMarketStart));
        Assert.That(strategy.EndTime, Is.EqualTo(preMarketEnd));
    }

    [Test]
    public void SessionDuration_WithRegularTradingHours_SetsCorrectWindow()
    {
        // Arrange - Regular trading hours 9:30 AM to 4:00 PM
        var marketOpen = new TimeOnly(9, 30);
        var marketClose = new TimeOnly(16, 0);

        // Act
        var strategy = Stock.Ticker("AAPL")
            .SessionDuration(marketOpen, marketClose)
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
            .SessionDuration(TradingSession.PreMarket)
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
            .SessionDuration(TradingSession.RTH)
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
            .SessionDuration(TradingSession.AfterHours)
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
            .SessionDuration(TradingSession.Extended)
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
        // Arrange & Act - Should end at 9:20 AM (10 min before 9:30)
        var strategy = Stock.Ticker("AAPL")
            .SessionDuration(TradingSession.PreMarketEndEarly)
            .Breakout(150)
            .Buy(100, Price.Current)
            .Build();

        // Assert
        Assert.That(strategy.StartTime, Is.EqualTo(new TimeOnly(4, 0)));
        Assert.That(strategy.EndTime, Is.EqualTo(new TimeOnly(9, 20)));
    }

    [Test]
    public void SessionDuration_WithPreMarketStartLate_SetsCorrectWindow()
    {
        // Arrange & Act - Should start at 4:10 AM (10 min after 4:00)
        var strategy = Stock.Ticker("AAPL")
            .SessionDuration(TradingSession.PreMarketStartLate)
            .Breakout(150)
            .Buy(100, Price.Current)
            .Build();

        // Assert
        Assert.That(strategy.StartTime, Is.EqualTo(new TimeOnly(4, 10)));
        Assert.That(strategy.EndTime, Is.EqualTo(new TimeOnly(9, 30)));
    }

    [Test]
    public void SessionDuration_WithRTHEndEarly_SetsCorrectWindow()
    {
        // Arrange & Act - Should end at 3:50 PM (10 min before 4:00)
        var strategy = Stock.Ticker("AAPL")
            .SessionDuration(TradingSession.RTHEndEarly)
            .Breakout(150)
            .Buy(100, Price.Current)
            .Build();

        // Assert
        Assert.That(strategy.StartTime, Is.EqualTo(new TimeOnly(9, 30)));
        Assert.That(strategy.EndTime, Is.EqualTo(new TimeOnly(15, 50)));
    }

    [Test]
    public void SessionDuration_WithRTHStartLate_SetsCorrectWindow()
    {
        // Arrange & Act - Should start at 9:40 AM (10 min after 9:30)
        var strategy = Stock.Ticker("AAPL")
            .SessionDuration(TradingSession.RTHStartLate)
            .Breakout(150)
            .Buy(100, Price.Current)
            .Build();

        // Assert
        Assert.That(strategy.StartTime, Is.EqualTo(new TimeOnly(9, 40)));
        Assert.That(strategy.EndTime, Is.EqualTo(new TimeOnly(16, 0)));
    }

    [Test]
    public void SessionDuration_WithAfterHoursEndEarly_SetsCorrectWindow()
    {
        // Arrange & Act - Should end at 7:50 PM (10 min before 8:00)
        var strategy = Stock.Ticker("AAPL")
            .SessionDuration(TradingSession.AfterHoursEndEarly)
            .Breakout(150)
            .Buy(100, Price.Current)
            .Build();

        // Assert
        Assert.That(strategy.StartTime, Is.EqualTo(new TimeOnly(16, 0)));
        Assert.That(strategy.EndTime, Is.EqualTo(new TimeOnly(19, 50)));
    }

    [Test]
    public void SessionDuration_WithAlways_ClearsTimeRestrictions()
    {
        // Arrange & Act - Should clear any time restrictions
        var strategy = Stock.Ticker("AAPL")
            .SessionDuration(TradingSession.Always)
            .Breakout(150)
            .Buy(100, Price.Current)
            .Build();

        // Assert
        Assert.That(strategy.StartTime, Is.Null);
        Assert.That(strategy.EndTime, Is.Null);
    }

    [Test]
    public void SessionDuration_AlwaysCanOverridePreviousSession()
    {
        // Arrange & Act - Always should clear previous time restrictions
        var strategy = Stock.Ticker("AAPL")
            .SessionDuration(TradingSession.PreMarket)  // Set premarket first
            .SessionDuration(TradingSession.Always)     // Then clear with Always
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
            .AboveVwap()
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
            .AboveVwap(0.05)
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
            .BelowVwap()
            .Buy(100, Price.Current)
            .Build();

        // Assert
        Assert.That(strategy.Conditions, Has.Count.EqualTo(1));
    }

    [Test]
    public void PriceAbove_AddsPriceAboveCondition()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .PriceAbove(100)
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
            .PriceBelow(200)
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
            .AboveVwap()
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
            .Start(MarketTime.PreMarket.Start)
            .PriceAbove(5.00)
            .AboveVwap()
            .Buy(100, Price.Current)
            .TakeProfit(6.00, 7.00)
            .StopLoss(4.50)
            .ClosePosition(closeTime, onlyIfProfitable: false)
            .End(MarketTime.PreMarket.End);

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

        // Assert - Time.PreMarket.Ending should be 9:20 AM ET (10 min before 9:30)
        Assert.That(strategy.Order.ClosePositionTime, Is.EqualTo(new TimeOnly(9, 20)));
    }

    [Test]
    public void ClosePosition_PremarketStrategy_OnlyIfProfitableDefault()
    {
        // Arrange & Act - Typical premarket strategy
        var strategy = Stock.Ticker("VIVS")
            .Start(MarketTime.PreMarket.Start)
            .PriceAbove(2.40)
            .AboveVwap()
            .Buy(100, Price.Current)
            .TakeProfit(4.00, 4.80)
            .ClosePosition(MarketTime.PreMarket.Ending)
            .End(MarketTime.PreMarket.End);

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
            .Start(MarketTime.PreMarket.Start)
            .PriceAbove(10.00)
            .Buy(100, Price.Current)
            .TakeProfit(12.00)
            .ClosePosition(MarketTime.PreMarket.Ending, onlyIfProfitable: false)
            .End(MarketTime.PreMarket.End);

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
        Assert.That(strategy.Order.TimeInForce, Is.EqualTo(Enums.TimeInForce.Day));
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
    public void End_SetsEndTimeAndBuildsStrategy()
    {
        // Arrange
        var endTime = new TimeOnly(7, 0);

        // Act
        var strategy = Stock.Ticker("AAPL")
            .Breakout(150)
            .Buy(100, Price.Current)
            .End(endTime);

        // Assert
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
            .Start(MarketTime.PreMarket.Start)
            .Breakout(7.10)
            .Pullback(6.80)
            .AboveVwap()
            .Buy(quantity: 100, Price.Current)
            .TakeProfit(9.00)
            .StopLoss(6.50)
            .TrailingStopLoss(Percent.Ten)
            .ClosePosition(MarketTime.PreMarket.End.AddMinutes(-10))
            .End(MarketTime.PreMarket.End);

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
        Assert.That(strategy.Order.TimeInForce, Is.EqualTo(Enums.TimeInForce.GoodTillCancel));
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
                .PriceAbove(150)
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
                .PriceAbove(155)
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
                .PriceBelow(200)
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
                .PriceAbove(150)
                .Close(quantity: 250)
                .Build();

            Assert.That(strategy.Order.Quantity, Is.EqualTo(250));
        }

        [Test]
        [Description("Close sets priceType correctly")]
        public void Close_WithPriceType_SetsPriceType()
        {
            var strategy = Stock.Ticker("AAPL")
                .PriceAbove(150)
                .Close(quantity: 100, priceType: Price.VWAP)
                .Build();

            Assert.That(strategy.Order.PriceType, Is.EqualTo(Price.VWAP));
        }

        [Test]
        [Description("Close with Market order type")]
        public void Close_WithMarketOrderType_SetsMarket()
        {
            var strategy = Stock.Ticker("AAPL")
                .PriceAbove(150)
                .Close(quantity: 100, orderType: OrderType.Market)
                .Build();

            Assert.That(strategy.Order.Type, Is.EqualTo(OrderType.Market));
        }

        [Test]
        [Description("Close with Limit order type")]
        public void Close_WithLimitOrderType_SetsLimit()
        {
            var strategy = Stock.Ticker("AAPL")
                .PriceAbove(150)
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
                .PriceAbove(160)
                .CloseLong(quantity: 100)
                .Build();

            Assert.That(strategy.Order.Side, Is.EqualTo(OrderSide.Sell));
        }

        [Test]
        [Description("CloseLong is equivalent to Close with positionSide=Buy")]
        public void CloseLong_EquivalentToCloseWithBuy()
        {
            var closeLongStrategy = Stock.Ticker("AAPL")
                .PriceAbove(160)
                .CloseLong(quantity: 100, Price.Current, OrderType.Market)
                .Build();

            var closeStrategy = Stock.Ticker("AAPL")
                .PriceAbove(160)
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
                .PriceBelow(180)
                .CloseShort(quantity: 50)
                .Build();

            Assert.That(strategy.Order.Side, Is.EqualTo(OrderSide.Buy));
        }

        [Test]
        [Description("CloseShort is equivalent to Close with positionSide=Sell")]
        public void CloseShort_EquivalentToCloseWithSell()
        {
            var closeShortStrategy = Stock.Ticker("TSLA")
                .PriceBelow(180)
                .CloseShort(quantity: 50, Price.Bid, OrderType.Limit)
                .Build();

            var closeStrategy = Stock.Ticker("TSLA")
                .PriceBelow(180)
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
                .PriceAbove(150)
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
                .PriceAbove(150)
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
                .PriceAbove(150)
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
                .PriceAbove(150)
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
                .Start(MarketTime.PreMarket.Start)
                .PriceAbove(155)
                .CloseLong(quantity: 100, Price.Current, OrderType.Market)
                .TimeInForce(TIF.GTC)
                .OutsideRTH(outsideRth: true, takeProfit: true)
                .End(MarketTime.PreMarket.End);

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
                .PriceAbove(500)
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
                .PriceAbove(450)
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
                .PriceAbove(150)
                .Close(quantity: 100, positionSide: OrderSide.Buy)
                .Build();

            Assert.That(strategy.Order.GetIbAction(), Is.EqualTo("SELL"));
        }

        [Test]
        [Description("Close generates correct IB action for short position")]
        public void Close_ShortPosition_GetIbAction_ReturnsBUY()
        {
            var strategy = Stock.Ticker("TSLA")
                .PriceBelow(200)
                .Close(quantity: 50, positionSide: OrderSide.Sell)
                .Build();

            Assert.That(strategy.Order.GetIbAction(), Is.EqualTo("BUY"));
        }

        [Test]
        [Description("Close generates correct IB order type for market order")]
        public void Close_MarketOrder_GetIbOrderType_ReturnsMKT()
        {
            var strategy = Stock.Ticker("AAPL")
                .PriceAbove(150)
                .Close(quantity: 100, orderType: OrderType.Market)
                .Build();

            Assert.That(strategy.Order.GetIbOrderType(), Is.EqualTo("MKT"));
        }

        [Test]
        [Description("Close generates correct IB order type for limit order")]
        public void Close_LimitOrder_GetIbOrderType_ReturnsLMT()
        {
            var strategy = Stock.Ticker("AAPL")
                .PriceAbove(150)
                .Close(quantity: 100, orderType: OrderType.Limit)
                .Build();

            Assert.That(strategy.Order.GetIbOrderType(), Is.EqualTo("LMT"));
        }

        [Test]
        [Description("Close generates correct IB TIF code")]
        public void Close_WithGTC_GetIbTif_ReturnsGTC()
        {
            var strategy = Stock.Ticker("AAPL")
                .PriceAbove(150)
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
                .PriceAbove(150)
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
                .PriceAbove(155)
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
}
