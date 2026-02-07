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
            .Long().Quantity(100)
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
            .Long().Quantity(100)
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
            .Long().Quantity(100)
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
            .Long().Quantity(100)
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
            .Long().Quantity(100)
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
            .Long().Quantity(100)
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
            .Long().Quantity(100)
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
            .Long().Quantity(100)
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
            .Long().Quantity(100)
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
            .Long().Quantity(100)
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
            .Long().Quantity(100)
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
            .Long().Quantity(100)
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
            .Long().Quantity(100)
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
            .Long().Quantity(100)
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
            .Long().Quantity(100)
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
            .Long().Quantity(100)
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
            .Long().Quantity(100)
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
            .Long().Quantity(100)
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
            .Long().Quantity(100)
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
            .Long().Quantity(100)
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
            .Long().Quantity(100)
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
            .Long().Quantity(100)
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
            .Long().Quantity(100)
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
            .Long().Quantity(100)
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
            .Long().Quantity(100)
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
            .Long().Quantity(100)
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
            .Long().Quantity(100)
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
            .Long().Quantity(100)
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
            .Long().Quantity(100)
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
            .Long().Quantity(100)
            .Build();

        // Assert
        Assert.That(strategy.Conditions, Has.Count.EqualTo(3));
    }

    #endregion

    #region Order Methods

    [Test]
    public void Long_CreatesStrategyWithBuyOrder()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .Breakout(150)
            .Long().Quantity(100)
            .Build();

        // Assert
        Assert.That(strategy.Order.Side, Is.EqualTo(OrderSide.Buy));
        Assert.That(strategy.Order.Quantity, Is.EqualTo(100));
    }

    [Test]
    public void Long_WithPriceType_SetsPriceType()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .Breakout(150)
            .Long().Quantity(100).PriceType(Price.VWAP)
            .Build();

        // Assert
        Assert.That(strategy.Order.PriceType, Is.EqualTo(Price.VWAP));
    }

    [Test]
    public void Short_CreatesStrategyWithSellOrder()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .Breakout(150)
            .Short().Quantity(100)
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
            .Long().Quantity(100)
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
            .Long().Quantity(100)
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
            .Long().Quantity(100)
            .TrailingStopLoss(Percent.Ten)
            .Build();

        // Assert
        Assert.That(strategy.Order.EnableTrailingStopLoss, Is.True);
        Assert.That(strategy.Order.TrailingStopLossPercent, Is.EqualTo(0.10));
    }

    [Test]
    public void ExitStrategy_SetsClosePositionTime()
    {
        // Arrange
        var closeTime = new TimeOnly(6, 50);

        // Act
        var strategy = Stock.Ticker("AAPL")
            .Breakout(150)
            .Long().Quantity(100)
            .ExitStrategy(closeTime)
            .Build();

        // Assert
        Assert.That(strategy.Order.ClosePositionTime, Is.EqualTo(closeTime));
    }

    [Test]
    public void ExitStrategy_DefaultOnlyIfProfitable_IsFalse()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .Breakout(150)
            .Long().Quantity(100)
            .ExitStrategy(MarketTime.PreMarket.Ending)
            .Build();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(strategy.Order.ClosePositionTime, Is.EqualTo(MarketTime.PreMarket.Ending));
            Assert.That(strategy.Order.ClosePositionOnlyIfProfitable, Is.False);
        });
    }

    [Test]
    public void ExitStrategy_WithIsProfitable_SetsFlag()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .Breakout(150)
            .Long().Quantity(100)
            .ExitStrategy(MarketTime.PreMarket.Ending).IsProfitable()
            .Build();

        // Assert
        Assert.That(strategy.Order.ClosePositionOnlyIfProfitable, Is.True);
    }

    [Test]
    public void ExitStrategy_WithoutIsProfitable_FlagIsFalse()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .Breakout(150)
            .Long().Quantity(100)
            .ExitStrategy(MarketTime.PreMarket.Ending)
            .Build();

        // Assert
        Assert.That(strategy.Order.ClosePositionOnlyIfProfitable, Is.False);
    }

    [Test]
    public void ExitStrategy_WithAllOptions_ConfiguresCorrectly()
    {
        // Arrange
        var closeTime = new TimeOnly(9, 20);

        // Act
        var strategy = Stock.Ticker("TEST")
            .TimeFrame(TradingSession.PreMarket)
            .IsPriceAbove(5.00)
            .IsAboveVwap()
            .Long().Quantity(100)
            .AdxTakeProfit(AdxTakeProfitConfig.FromRange(6.00, 7.00))
            .StopLoss(4.50)
            .ExitStrategy(closeTime)
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
    public void ExitStrategy_UsingTimePreMarketEnding_SetsCorrectTime()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .Breakout(150)
            .Long().Quantity(100)
            .ExitStrategy(MarketTime.PreMarket.Ending)
            .Build();

        // Assert - Time.PreMarket.Ending should be 9:15 AM ET (15 min before 9:30)
        Assert.That(strategy.Order.ClosePositionTime, Is.EqualTo(new TimeOnly(9, 15)));
    }

    [Test]
    public void ExitStrategy_PremarketStrategy_OnlyIfProfitableNotSet()
    {
        // Arrange & Act - Typical premarket strategy
        var strategy = Stock.Ticker("VIVS")
            .TimeFrame(TradingSession.PreMarket)
            .IsPriceAbove(2.40)
            .IsAboveVwap()
            .Long().Quantity(100)
            .AdxTakeProfit(AdxTakeProfitConfig.FromRange(4.00, 4.80))
            .ExitStrategy(MarketTime.PreMarket.Ending)
            .Build();

        // Assert - Default should NOT only close if profitable (single-responsibility)
        Assert.Multiple(() =>
        {
            Assert.That(strategy.Order.ClosePositionTime, Is.EqualTo(MarketTime.PreMarket.Ending));
            Assert.That(strategy.Order.ClosePositionOnlyIfProfitable, Is.False);
        });
    }

    [Test]
    public void ExitStrategy_WithIsProfitable_ClosesOnlyIfProfitable()
    {
        // Arrange & Act - Strategy that only closes if profitable
        var strategy = Stock.Ticker("TEST")
            .TimeFrame(TradingSession.PreMarket)
            .IsPriceAbove(10.00)
            .Long().Quantity(100)
            .TakeProfit(12.00)
            .ExitStrategy(MarketTime.PreMarket.Ending).IsProfitable()
            .Build();

        // Assert - Will only close if profitable
        Assert.Multiple(() =>
        {
            Assert.That(strategy.Order.ClosePositionTime, Is.EqualTo(MarketTime.PreMarket.Ending));
            Assert.That(strategy.Order.ClosePositionOnlyIfProfitable, Is.True);
        });
    }

    [Test]
    public void TimeInForce_SetsTimeInForce()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .Breakout(150)
            .Long().Quantity(100)
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
            .Long().Quantity(100)
            .OutsideRTH(false).TakeProfitOutsideRTH(false)
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
            .Long().Quantity(100)
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
            .Long().Quantity(100)
            .TakeProfit(9.00)
            .StopLoss(6.50)
            .TrailingStopLoss(Percent.Ten)
            .ExitStrategy(MarketTime.PreMarket.End.AddMinutes(-10))
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
            .Long().Quantity(100)
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
        var strategy = Stock.Ticker("AAPL").Breakout(150).Long().Quantity(100).Build();
        Assert.That(strategy.Exchange, Is.EqualTo("SMART"));
    }

    [Test]
    public void Defaults_CurrencyIsUsd()
    {
        var strategy = Stock.Ticker("AAPL").Breakout(150).Long().Quantity(100).Build();
        Assert.That(strategy.Currency, Is.EqualTo("USD"));
    }

    [Test]
    public void Defaults_EnabledIsTrue()
    {
        var strategy = Stock.Ticker("AAPL").Breakout(150).Long().Quantity(100).Build();
        Assert.That(strategy.Enabled, Is.True);
    }

    [Test]
    public void Defaults_TimeInForceIsGtc()
    {
        var strategy = Stock.Ticker("AAPL").Breakout(150).Long().Quantity(100).Build();
        Assert.That(strategy.Order.TimeInForce, Is.EqualTo(TimeInForce.GoodTillCancel));
    }

    [Test]
    public void Defaults_OutsideRthIsTrue()
    {
        var strategy = Stock.Ticker("AAPL").Breakout(150).Long().Quantity(100).Build();
        Assert.That(strategy.Order.OutsideRth, Is.True);
    }

    #endregion

    #region Close Order Method Tests

    /// <summary>
    /// Tests for the CloseLong() and CloseShort() methods which create orders to exit existing positions.
    /// Uses single-responsibility pattern - each method does ONE thing.
    /// </summary>
    [TestFixture]
    public class CloseOrderTests
    {
        #region CloseLong() Basic Tests

        [Test]
        [Description("CloseLong creates SELL order to close long position")]
        public void CloseLong_CreatesSellOrder_ToCloseLong()
        {
            // Arrange & Act - CloseLong creates SELL order to exit a long position
            var strategy = Stock.Ticker("AAPL")
                .IsPriceAbove(150)
                .CloseLong().Quantity(100)
                .Build();

            // Assert
            Assert.That(strategy.Order.Side, Is.EqualTo(OrderSide.Sell));
            Assert.That(strategy.Order.Quantity, Is.EqualTo(100));
        }

        [Test]
        [Description("CloseLong with quantity creates SELL order to close long")]
        public void CloseLong_WithQuantity_CreatesSellOrder()
        {
            // Arrange & Act
            var strategy = Stock.Ticker("AAPL")
                .IsPriceAbove(155)
                .CloseLong().Quantity(100)
                .Build();

            // Assert - Closing a long position means SELL
            Assert.That(strategy.Order.Side, Is.EqualTo(OrderSide.Sell));
        }

        [Test]
        [Description("CloseShort creates BUY order to cover short position")]
        public void CloseShort_CreatesBuyOrder_ToCoverShort()
        {
            // Arrange & Act
            var strategy = Stock.Ticker("TSLA")
                .IsPriceBelow(200)
                .CloseShort().Quantity(50)
                .Build();

            // Assert - Closing a short position means BUY to cover
            Assert.That(strategy.Order.Side, Is.EqualTo(OrderSide.Buy));
        }

        [Test]
        [Description("CloseLong.Quantity sets quantity correctly")]
        public void CloseLong_Quantity_SetsQuantity()
        {
            var strategy = Stock.Ticker("AAPL")
                .IsPriceAbove(150)
                .CloseLong().Quantity(250)
                .Build();

            Assert.That(strategy.Order.Quantity, Is.EqualTo(250));
        }

        [Test]
        [Description("CloseLong.PriceType sets priceType correctly")]
        public void CloseLong_PriceType_SetsPriceType()
        {
            var strategy = Stock.Ticker("AAPL")
                .IsPriceAbove(150)
                .CloseLong().Quantity(100).PriceType(Price.VWAP)
                .Build();

            Assert.That(strategy.Order.PriceType, Is.EqualTo(Price.VWAP));
        }

        [Test]
        [Description("CloseLong.OrderType with Market sets Market order type")]
        public void CloseLong_OrderType_Market_SetsMarket()
        {
            var strategy = Stock.Ticker("AAPL")
                .IsPriceAbove(150)
                .CloseLong().Quantity(100).OrderType(OrderType.Market)
                .Build();

            Assert.That(strategy.Order.Type, Is.EqualTo(OrderType.Market));
        }

        [Test]
        [Description("CloseLong.OrderType with Limit sets Limit order type")]
        public void CloseLong_OrderType_Limit_SetsLimit()
        {
            var strategy = Stock.Ticker("AAPL")
                .IsPriceAbove(150)
                .CloseLong().Quantity(100).OrderType(OrderType.Limit)
                .Build();

            Assert.That(strategy.Order.Type, Is.EqualTo(OrderType.Limit));
        }

        #endregion

        #region CloseLong() Chained Configuration Tests

        [Test]
        [Description("CloseLong creates SELL order")]
        public void CloseLong_CreatesSellOrder()
        {
            var strategy = Stock.Ticker("AAPL")
                .IsPriceAbove(160)
                .CloseLong().Quantity(100)
                .Build();

            Assert.That(strategy.Order.Side, Is.EqualTo(OrderSide.Sell));
        }

        [Test]
        [Description("CloseLong with chained configuration")]
        public void CloseLong_ChainedConfiguration_SetsAllProperties()
        {
            var strategy = Stock.Ticker("AAPL")
                .IsPriceAbove(160)
                .CloseLong().Quantity(100).PriceType(Price.Current).OrderType(OrderType.Market)
                .Build();

            Assert.Multiple(() =>
            {
                Assert.That(strategy.Order.Side, Is.EqualTo(OrderSide.Sell));
                Assert.That(strategy.Order.Quantity, Is.EqualTo(100));
                Assert.That(strategy.Order.PriceType, Is.EqualTo(Price.Current));
                Assert.That(strategy.Order.Type, Is.EqualTo(OrderType.Market));
            });
        }

        #endregion

        #region CloseShort() Chained Configuration Tests

        [Test]
        [Description("CloseShort creates BUY order to cover")]
        public void CloseShort_CreatesBuyOrder()
        {
            var strategy = Stock.Ticker("TSLA")
                .IsPriceBelow(180)
                .CloseShort().Quantity(50)
                .Build();

            Assert.That(strategy.Order.Side, Is.EqualTo(OrderSide.Buy));
        }

        [Test]
        [Description("CloseShort with chained configuration")]
        public void CloseShort_ChainedConfiguration_SetsAllProperties()
        {
            var strategy = Stock.Ticker("TSLA")
                .IsPriceBelow(180)
                .CloseShort().Quantity(50).PriceType(Price.Bid).OrderType(OrderType.Limit)
                .Build();

            Assert.Multiple(() =>
            {
                Assert.That(strategy.Order.Side, Is.EqualTo(OrderSide.Buy));
                Assert.That(strategy.Order.Quantity, Is.EqualTo(50));
                Assert.That(strategy.Order.PriceType, Is.EqualTo(Price.Bid));
                Assert.That(strategy.Order.Type, Is.EqualTo(OrderType.Limit));
            });
        }

        #endregion

        #region CloseLong Default Values Tests

        [Test]
        [Description("CloseLong does NOT automatically set TimeInForce - uses default GTC")]
        public void CloseLong_DoesNotAutoSetTIF_UsesDefault()
        {
            var strategy = Stock.Ticker("AAPL")
                .IsPriceAbove(150)
                .CloseLong().Quantity(100)
                .Build();

            // Default is GTC, not forced by CloseLong
            Assert.That(strategy.Order.TimeInForce, Is.EqualTo(TimeInForce.GoodTillCancel));
        }

        [Test]
        [Description("CloseLong allows explicit TimeInForce setting")]
        public void CloseLong_WithExplicitTIF_SetsTIF()
        {
            var strategy = Stock.Ticker("AAPL")
                .IsPriceAbove(150)
                .CloseLong().Quantity(100)
                .TimeInForce(TIF.Day)
                .Build();

            Assert.That(strategy.Order.TimeInForce, Is.EqualTo(TimeInForce.Day));
        }

        [Test]
        [Description("CloseLong does NOT automatically set OutsideRth - uses default true")]
        public void CloseLong_DoesNotAutoSetOutsideRth_UsesDefault()
        {
            var strategy = Stock.Ticker("AAPL")
                .IsPriceAbove(150)
                .CloseLong().Quantity(100)
                .Build();

            // Default is true, not forced by CloseLong
            Assert.That(strategy.Order.OutsideRth, Is.True);
        }

        [Test]
        [Description("CloseLong allows explicit OutsideRth setting")]
        public void CloseLong_WithExplicitOutsideRth_SetsOutsideRth()
        {
            var strategy = Stock.Ticker("AAPL")
                .IsPriceAbove(150)
                .CloseLong().Quantity(100)
                .OutsideRTH(false).TakeProfitOutsideRTH(false)
                .Build();

            Assert.That(strategy.Order.OutsideRth, Is.False);
        }

        #endregion

        #region CloseLong Full Configuration Tests

        [Test]
        [Description("CloseLong with full fluent chain configuration")]
        public void CloseLong_FullFluentChain_ConfiguresCorrectly()
        {
            var strategy = Stock.Ticker("AAPL")
                .TimeFrame(TradingSession.PreMarket)
                .IsPriceAbove(155)
                .CloseLong().Quantity(100).PriceType(Price.Current).OrderType(OrderType.Market)
                .TimeInForce(TIF.GTC)
                .OutsideRTH(true).TakeProfitOutsideRTH(true)
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
        [Description("CloseLong overnight earnings play scenario")]
        public void CloseLong_OvernightEarningsPlay_ConfiguresCorrectly()
        {
            // Scenario: Close long position during after-hours if price hits target
            var strategy = Stock.Ticker("NVDA")
                .IsPriceAbove(500)
                .CloseLong().Quantity(25)
                .TimeInForce(TIF.Overnight)
                .OutsideRTH(true).TakeProfitOutsideRTH(true)
                .Build();

            Assert.Multiple(() =>
            {
                Assert.That(strategy.Order.Side, Is.EqualTo(OrderSide.Sell));
                Assert.That(strategy.Order.TimeInForce, Is.EqualTo(TimeInForce.Overnight));
                Assert.That(strategy.Order.OutsideRth, Is.True);
            });
        }

        [Test]
        [Description("CloseLong at market open scenario")]
        public void CloseLong_AtMarketOpen_ConfiguresCorrectly()
        {
            // Scenario: Close position at market open auction
            var strategy = Stock.Ticker("SPY")
                .IsPriceAbove(450)
                .CloseLong().Quantity(500)
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
        [Description("CloseLong generates correct IB action for long position")]
        public void CloseLong_GetIbAction_ReturnsSELL()
        {
            var strategy = Stock.Ticker("AAPL")
                .IsPriceAbove(150)
                .CloseLong().Quantity(100)
                .Build();

            Assert.That(strategy.Order.GetIbAction(), Is.EqualTo("SELL"));
        }

        [Test]
        [Description("CloseShort generates correct IB action for short position")]
        public void CloseShort_GetIbAction_ReturnsBUY()
        {
            var strategy = Stock.Ticker("TSLA")
                .IsPriceBelow(200)
                .CloseShort().Quantity(50)
                .Build();

            Assert.That(strategy.Order.GetIbAction(), Is.EqualTo("BUY"));
        }

        [Test]
        [Description("CloseLong generates correct IB order type for market order")]
        public void CloseLong_MarketOrder_GetIbOrderType_ReturnsMKT()
        {
            var strategy = Stock.Ticker("AAPL")
                .IsPriceAbove(150)
                .CloseLong().Quantity(100).OrderType(OrderType.Market)
                .Build();

            Assert.That(strategy.Order.GetIbOrderType(), Is.EqualTo("MKT"));
        }

        [Test]
        [Description("CloseLong generates correct IB order type for limit order")]
        public void CloseLong_LimitOrder_GetIbOrderType_ReturnsLMT()
        {
            var strategy = Stock.Ticker("AAPL")
                .IsPriceAbove(150)
                .CloseLong().Quantity(100).OrderType(OrderType.Limit)
                .Build();

            Assert.That(strategy.Order.GetIbOrderType(), Is.EqualTo("LMT"));
        }

        [Test]
        [Description("CloseLong generates correct IB TIF code")]
        public void CloseLong_WithGTC_GetIbTif_ReturnsGTC()
        {
            var strategy = Stock.Ticker("AAPL")
                .IsPriceAbove(150)
                .CloseLong().Quantity(100)
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
                .Long().Quantity(100)
                .Build();

            Assert.That(strategy.Order.AllOrNone, Is.False);
        }

        [Test]
        [Description("AllOrNone() with no parameter sets to true")]
        public void AllOrNone_NoParameter_SetsTrue()
        {
            var strategy = Stock.Ticker("AAPL")
                .Breakout(150)
                .Long().Quantity(100)
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
                .Long().Quantity(100)
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
                .Long().Quantity(100)
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
                .Short().Quantity(100)
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
                .CloseLong().Quantity(100)
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
                .Long().Quantity(100)
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
                .Long().Quantity(100)
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
                .Long().Quantity(100)
                .AdxTakeProfit(AdxTakeProfitConfig.FromRange(4.00, 4.80))
                .Build();

            var pullbackStrategy = Stock.Ticker("VIVS")
                .Pullback(4.15)
                .IsAboveVwap()
                .Long().Quantity(100)
                .AdxTakeProfit(AdxTakeProfitConfig.FromRange(4.80, 5.30))
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
                    .Long().Quantity(100)
                    .Build(),

                Stock.Ticker("CATX")
                    .Pullback(4.33)
                    .IsPriceAbove(4.30)
                    .Long().Quantity(100)
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
                .Long().Quantity(100)
                .TakeProfit(3.50)
                .Build();

            var aggressiveStrategy = Stock.Ticker("VIVS")
                .IsPriceAbove(3.00)
                .Long().Quantity(100)
                .AdxTakeProfit(AdxTakeProfitConfig.FromRange(4.50, 5.50))
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
                .Long().Quantity(100)
                .StopLoss(4.25)
                .Build();

            var trailingStopStrategy = Stock.Ticker("CATX")
                .IsPriceAbove(4.80)
                .Long().Quantity(100)
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
                .Long().Quantity(100)
                .Build();

            var rthStrategy = Stock.Ticker("VIVS")
                .TimeFrame(TradingSession.RTH)
                .IsPriceAbove(2.40)
                .Long().Quantity(100)
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
                .Long().Quantity(50)
                .Build();

            var largePositionStrategy = Stock.Ticker("CATX")
                .IsPriceAbove(4.00)
                .Long().Quantity(500)
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
                    .Long().Quantity(100)
                    .AdxTakeProfit(AdxTakeProfitConfig.FromRange(4.00, 4.80))
                    .TrailingStopLoss(Percent.TwentyFive)
                    .ExitStrategy(MarketTime.PreMarket.Ending),

                // CATX Momentum
                Stock.Ticker("CATX")
                    .TimeFrame(TradingSession.PreMarketEndEarly)
                    .IsPriceAbove(4.00)
                    .IsAboveVwap()
                    .Long().Quantity(100)
                    .AdxTakeProfit(AdxTakeProfitConfig.FromRange(5.30, 6.16))
                    .TrailingStopLoss(Percent.TwentyFive)
                    .ExitStrategy(MarketTime.PreMarket.Ending),

                // VIVS Pullback
                Stock.Ticker("VIVS")
                    .TimeFrame(TradingSession.PreMarketEndEarly)
                    .Pullback(4.15)
                    .IsAboveVwap()
                    .Long().Quantity(100)
                    .AdxTakeProfit(AdxTakeProfitConfig.FromRange(4.80, 5.30))
                    .StopLoss(3.95)
                    .ExitStrategy(MarketTime.PreMarket.Ending),

                // CATX Support
                Stock.Ticker("CATX")
                    .TimeFrame(TradingSession.PreMarketEndEarly)
                    .Pullback(4.33)
                    .IsPriceAbove(4.30)
                    .Long().Quantity(100)
                    .AdxTakeProfit(AdxTakeProfitConfig.FromRange(4.50, 4.75))
                    .StopLoss(4.25)
                    .ExitStrategy(MarketTime.PreMarket.Ending),
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
                .Long().Quantity(100)
                .Build();

            var strategy2 = Stock.Ticker("VIVS")
                .Enabled(false)
                .IsPriceAbove(3.00)
                .Long().Quantity(200)
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
                Stock.Ticker("AAPL").IsPriceAbove(150).Long().Quantity(100).Build(),
                Stock.Ticker("VIVS").IsPriceAbove(2.40).Long().Quantity(100).Build(),
                Stock.Ticker("AAPL").Pullback(145).Long().Quantity(50).Build(),
                Stock.Ticker("CATX").IsPriceAbove(4.00).Long().Quantity(100).Build(),
                Stock.Ticker("VIVS").Pullback(4.15).Long().Quantity(100).Build(),
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
                    .Long().Quantity(100)
                    .Build(),

                Stock.Ticker("VIVS")
                    .Enabled(false)  // Disabled
                    .Pullback(4.15)
                    .Long().Quantity(100)
                    .Build(),

                Stock.Ticker("VIVS")
                    .Enabled(true)
                    .IsPriceAbove(5.00)
                    .Long().Quantity(100)
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

    #region Repeat Tests

    [Test]
    [Description("Repeat() on Stock builder sets RepeatEnabled to true")]
    public void Repeat_OnStockBuilder_SetsRepeatEnabled()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("ABC")
            .IsPriceAbove(5.00)
            .Repeat()
            .Long().Quantity(100)
            .TakeProfit(6.00)
            .Build();

        // Assert
        Assert.That(strategy.RepeatEnabled, Is.True);
    }

    [Test]
    [Description("Repeat() on StrategyBuilder sets RepeatEnabled to true")]
    public void Repeat_OnStrategyBuilder_SetsRepeatEnabled()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("ABC")
            .IsPriceAbove(5.00)
            .Long().Quantity(100)
            .TakeProfit(6.00)
            .Repeat()
            .Build();

        // Assert
        Assert.That(strategy.RepeatEnabled, Is.True);
    }

    [Test]
    [Description("Without Repeat(), RepeatEnabled defaults to false")]
    public void NoRepeat_RepeatEnabledDefaultsFalse()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("ABC")
            .IsPriceAbove(5.00)
            .Long().Quantity(100)
            .TakeProfit(6.00)
            .Build();

        // Assert
        Assert.That(strategy.RepeatEnabled, Is.False);
    }

    [Test]
    [Description("Repeat(false) sets RepeatEnabled to false")]
    public void Repeat_False_SetsRepeatEnabledFalse()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("ABC")
            .IsPriceAbove(5.00)
            .Repeat(false)
            .Long().Quantity(100)
            .TakeProfit(6.00)
            .Build();

        // Assert
        Assert.That(strategy.RepeatEnabled, Is.False);
    }

    [Test]
    [Description("Full repeating strategy example with all components")]
    public void Repeat_FullScalpStrategy_WorksCorrectly()
    {
        // Arrange & Act - Example from the user's request
        var strategy = Stock.Ticker("ABC")
            .TimeFrame(TradingSession.RTH)
            .IsPriceAbove(5.00)
            .IsAboveVwap()
            .Long().Quantity(100)
            .TakeProfit(6.00)
            .StopLoss(4.50)
            .Repeat()
            .Build();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(strategy.Symbol, Is.EqualTo("ABC"));
            Assert.That(strategy.RepeatEnabled, Is.True);
            Assert.That(strategy.Order.EnableTakeProfit, Is.True);
            Assert.That(strategy.Order.TakeProfitPrice, Is.EqualTo(6.00));
            Assert.That(strategy.Order.EnableStopLoss, Is.True);
            Assert.That(strategy.Order.StopLossPrice, Is.EqualTo(4.50));
            Assert.That(strategy.Conditions, Has.Count.EqualTo(2)); // IsPriceAbove + IsAboveVwap
        });
    }

    #endregion
}


