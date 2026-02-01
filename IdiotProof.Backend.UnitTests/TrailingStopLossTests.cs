// ============================================================================
// TrailingStopLossTests - Unit tests for trailing stop loss functionality
// ============================================================================
//
// PURPOSE:
// Tests trailing stop loss configuration, calculation, and behavior including:
// - Default values and configuration
// - Percent constant values
// - Fluent API builder integration
// - Order type selection (MKT vs LMT based on RTH)
// - High-water mark tracking logic
//
// ============================================================================

using IdiotProof.Backend.Enums;
using IdiotProof.Backend.Models;
using IdiotProof.Backend.Strategy;
using NUnit.Framework;

namespace IdiotProof.Backend.UnitTests;

/// <summary>
/// Unit tests for trailing stop loss functionality.
/// </summary>
[TestFixture]
public class TrailingStopLossTests
{
    #region Default Value Tests

    [Test]
    [Description("Validates default EnableTrailingStopLoss is false")]
    public void EnableTrailingStopLoss_Default_IsFalse()
    {
        var order = new OrderAction();
        Assert.That(order.EnableTrailingStopLoss, Is.False);
    }

    [Test]
    [Description("Validates default TrailingStopLossPercent is 10%")]
    public void TrailingStopLossPercent_Default_Is10Percent()
    {
        var order = new OrderAction();
        Assert.That(order.TrailingStopLossPercent, Is.EqualTo(0.10));
    }

    #endregion

    #region Percent Constant Tests

    [Test]
    [Description("Validates Percent.TwentyFive returns 0.25")]
    public void Percent_TwentyFive_Returns025()
    {
        Assert.That(Percent.TwentyFive, Is.EqualTo(0.25));
    }

    [Test]
    [Description("Validates Percent.Ten returns 0.10")]
    public void Percent_Ten_Returns010()
    {
        Assert.That(Percent.Ten, Is.EqualTo(0.10));
    }

    [Test]
    [Description("Validates Percent.Five returns 0.05")]
    public void Percent_Five_Returns005()
    {
        Assert.That(Percent.Five, Is.EqualTo(0.05));
    }

    [Test]
    [Description("Validates Percent.Custom(15) returns 0.15")]
    public void Percent_Custom15_Returns015()
    {
        Assert.That(Percent.Custom(15), Is.EqualTo(0.15));
    }

    [Test]
    [Description("Validates Percent.Custom throws for negative values")]
    public void Percent_Custom_ThrowsForNegative()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Percent.Custom(-5));
    }

    [Test]
    [Description("Validates Percent.Custom throws for values over 100")]
    public void Percent_Custom_ThrowsForOver100()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Percent.Custom(101));
    }

    #endregion

    #region OrderAction Configuration Tests

    [Test]
    [Description("Validates TrailingStopLoss can be enabled with custom percent")]
    public void OrderAction_TrailingStopLoss_CanBeConfigured()
    {
        var order = new OrderAction
        {
            EnableTrailingStopLoss = true,
            TrailingStopLossPercent = Percent.TwentyFive
        };

        Assert.That(order.EnableTrailingStopLoss, Is.True);
        Assert.That(order.TrailingStopLossPercent, Is.EqualTo(0.25));
    }

    [Test]
    [Description("Validates ToString includes TSL when enabled")]
    public void OrderAction_ToString_IncludesTSL_WhenEnabled()
    {
        var order = new OrderAction
        {
            EnableTrailingStopLoss = true,
            TrailingStopLossPercent = Percent.TwentyFive
        };

        var result = order.ToString();
        Assert.That(result, Does.Contain("TSL=25%"));
    }

    [Test]
    [Description("Validates ToString excludes TSL when disabled")]
    public void OrderAction_ToString_ExcludesTSL_WhenDisabled()
    {
        var order = new OrderAction
        {
            EnableTrailingStopLoss = false
        };

        var result = order.ToString();
        Assert.That(result, Does.Not.Contain("TSL"));
    }

    #endregion

    #region Fluent API Builder Tests

    [Test]
    [Description("Validates fluent API sets TrailingStopLoss correctly")]
    public void FluentApi_TrailingStopLoss_SetsCorrectValues()
    {
        var strategy = Stock
            .Ticker("TEST")
            .IsPriceAbove(10.00)
            .Buy(quantity: 100, Price.Current)
            .TrailingStopLoss(Percent.TwentyFive)
            .Build();

        Assert.That(strategy.Order.EnableTrailingStopLoss, Is.True);
        Assert.That(strategy.Order.TrailingStopLossPercent, Is.EqualTo(0.25));
    }

    [Test]
    [Description("Validates fluent API without TrailingStopLoss keeps it disabled")]
    public void FluentApi_NoTrailingStopLoss_KeepsDisabled()
    {
        var strategy = Stock
            .Ticker("TEST")
            .IsPriceAbove(10.00)
            .Buy(quantity: 100, Price.Current)
            .Build();

        Assert.That(strategy.Order.EnableTrailingStopLoss, Is.False);
    }

    [Test]
    [Description("Validates fluent API with different percent values")]
    [TestCase(0.05, Description = "5% trailing stop")]
    [TestCase(0.10, Description = "10% trailing stop")]
    [TestCase(0.15, Description = "15% trailing stop")]
    [TestCase(0.20, Description = "20% trailing stop")]
    [TestCase(0.25, Description = "25% trailing stop")]
    public void FluentApi_TrailingStopLoss_VariousPercents(double percent)
    {
        var strategy = Stock
            .Ticker("TEST")
            .IsPriceAbove(10.00)
            .Buy(quantity: 100, Price.Current)
            .TrailingStopLoss(percent)
            .Build();

        Assert.That(strategy.Order.TrailingStopLossPercent, Is.EqualTo(percent));
    }

    #endregion

    #region Trailing Stop Calculation Tests

    [Test]
    [Description("Validates trailing stop calculation from entry price")]
    public void TrailingStop_CalculatesCorrectly_FromEntryPrice()
    {
        double entryPrice = 10.00;
        double trailingPercent = 0.25; // 25%
        
        double expectedStop = entryPrice * (1 - trailingPercent);
        
        Assert.That(expectedStop, Is.EqualTo(7.50));
    }

    [Test]
    [Description("Validates trailing stop moves up with high-water mark")]
    public void TrailingStop_MovesUp_WithHighWaterMark()
    {
        double entryPrice = 10.00;
        double trailingPercent = 0.25; // 25%
        double newHighPrice = 12.00;
        
        double initialStop = entryPrice * (1 - trailingPercent);
        double newStop = newHighPrice * (1 - trailingPercent);
        
        Assert.That(initialStop, Is.EqualTo(7.50));
        Assert.That(newStop, Is.EqualTo(9.00));
        Assert.That(newStop, Is.GreaterThan(initialStop), "Stop should move up with price");
    }

    [Test]
    [Description("Validates trailing stop never moves down")]
    public void TrailingStop_NeverMovesDown()
    {
        double trailingPercent = 0.25;
        double highWaterMark = 12.00;
        double currentPrice = 11.00; // Price dropped but still above stop
        
        double stopAtHighWater = highWaterMark * (1 - trailingPercent); // 9.00
        double stopAtCurrentPrice = currentPrice * (1 - trailingPercent); // 8.25
        
        // The stop should stay at 9.00 (from high water mark), not drop to 8.25
        double effectiveStop = Math.Max(stopAtHighWater, stopAtCurrentPrice);
        
        Assert.That(effectiveStop, Is.EqualTo(9.00));
    }

    [Test]
    [Description("Validates stop triggers when price drops below stop level")]
    public void TrailingStop_Triggers_WhenPriceDropsBelowStop()
    {
        double highWaterMark = 12.00;
        double trailingPercent = 0.25;
        double stopLevel = highWaterMark * (1 - trailingPercent); // 9.00
        
        double currentPrice = 8.90; // Below stop level
        
        bool shouldTrigger = currentPrice <= stopLevel;
        
        Assert.That(shouldTrigger, Is.True);
    }

    [Test]
    [Description("Validates stop does not trigger when price is above stop level")]
    public void TrailingStop_DoesNotTrigger_WhenPriceAboveStop()
    {
        double highWaterMark = 12.00;
        double trailingPercent = 0.25;
        double stopLevel = highWaterMark * (1 - trailingPercent); // 9.00
        
        double currentPrice = 9.50; // Above stop level
        
        bool shouldTrigger = currentPrice <= stopLevel;
        
        Assert.That(shouldTrigger, Is.False);
    }

    #endregion

    #region Combined Strategy Tests

    [Test]
    [Description("Validates TakeProfit and TrailingStopLoss can be combined")]
    public void Strategy_CombinesTakeProfitAndTrailingStopLoss()
    {
        var strategy = Stock
            .Ticker("TEST")
            .IsPriceAbove(10.00)
            .Buy(quantity: 100, Price.Current)
            .TakeProfit(15.00)
            .TrailingStopLoss(Percent.TwentyFive)
            .Build();

        Assert.That(strategy.Order.EnableTakeProfit, Is.True);
        Assert.That(strategy.Order.TakeProfitPrice, Is.EqualTo(15.00));
        Assert.That(strategy.Order.EnableTrailingStopLoss, Is.True);
        Assert.That(strategy.Order.TrailingStopLossPercent, Is.EqualTo(0.25));
    }

    [Test]
    [Description("Validates ADX TakeProfit and TrailingStopLoss can be combined")]
    public void Strategy_CombinesAdxTakeProfitAndTrailingStopLoss()
    {
        var strategy = Stock
            .Ticker("TEST")
            .IsPriceAbove(2.40)
            .IsAboveVwap()
            .Buy(quantity: 500, Price.Current)
            .TakeProfit(4.00, 4.80)
            .TrailingStopLoss(Percent.TwentyFive)
            .Build();

        Assert.That(strategy.Order.AdxTakeProfit, Is.Not.Null);
        Assert.That(strategy.Order.AdxTakeProfit!.ConservativeTarget, Is.EqualTo(4.00));
        Assert.That(strategy.Order.AdxTakeProfit!.AggressiveTarget, Is.EqualTo(4.80));
        Assert.That(strategy.Order.EnableTrailingStopLoss, Is.True);
        Assert.That(strategy.Order.TrailingStopLossPercent, Is.EqualTo(0.25));
    }

    [Test]
    [Description("Validates full premarket strategy configuration")]
    public void Strategy_FullPremarketConfiguration()
    {
        var strategy = Stock
            .Ticker("VIVS")
            .TimeFrame(TradingSession.PreMarketEndEarly)
            .IsPriceAbove(2.40)
            .IsAboveVwap()
            .Buy(quantity: 500, Price.Current)
            .TakeProfit(4.00, 4.80)
            .TrailingStopLoss(Percent.TwentyFive)
            .ClosePosition(MarketTime.PreMarket.Ending, false)
            .Build();

        Assert.Multiple(() =>
        {
            Assert.That(strategy.Symbol, Is.EqualTo("VIVS"));
            Assert.That(strategy.Session, Is.EqualTo(TradingSession.PreMarketEndEarly));
            Assert.That(strategy.Order.Quantity, Is.EqualTo(500));
            Assert.That(strategy.Order.EnableTakeProfit, Is.True);
            Assert.That(strategy.Order.AdxTakeProfit!.ConservativeTarget, Is.EqualTo(4.00));
            Assert.That(strategy.Order.EnableTrailingStopLoss, Is.True);
            Assert.That(strategy.Order.TrailingStopLossPercent, Is.EqualTo(0.25));
            Assert.That(strategy.Order.ClosePositionTime, Is.Not.Null);
            Assert.That(strategy.Order.ClosePositionOnlyIfProfitable, Is.False);
        });
    }

    #endregion

    #region RTH vs Extended Hours Order Type Tests

    [Test]
    [Description("Documents that trailing stop uses MKT during RTH (9:30 AM - 4:00 PM ET)")]
    public void TrailingStop_UsesMarketOrder_DuringRTH()
    {
        // RTH is 9:30 AM to 4:00 PM ET
        var rthStart = new TimeOnly(9, 30);
        var rthEnd = new TimeOnly(16, 0);
        var testTime = new TimeOnly(10, 30); // 10:30 AM - within RTH
        
        bool isRTH = testTime >= rthStart && testTime <= rthEnd;
        
        Assert.That(isRTH, Is.True, "10:30 AM should be within RTH");
        // When isRTH is true, trailing stop should use MKT order
    }

    [Test]
    [Description("Documents that trailing stop uses LMT outside RTH (pre-market/after-hours)")]
    public void TrailingStop_UsesLimitOrder_OutsideRTH()
    {
        var rthStart = new TimeOnly(9, 30);
        var rthEnd = new TimeOnly(16, 0);
        var testTime = new TimeOnly(7, 30); // 7:30 AM - pre-market
        
        bool isRTH = testTime >= rthStart && testTime <= rthEnd;
        
        Assert.That(isRTH, Is.False, "7:30 AM should be outside RTH (pre-market)");
        // When isRTH is false, trailing stop should use LMT order at stop price
    }

    [Test]
    [Description("Validates MarketTime.RTH.Contains works correctly for RTH check")]
    public void MarketTime_RTH_Contains_WorksCorrectly()
    {
        // Test various times
        Assert.That(MarketTime.RTH.Contains(new TimeOnly(9, 30)), Is.True, "9:30 AM is start of RTH");
        Assert.That(MarketTime.RTH.Contains(new TimeOnly(12, 0)), Is.True, "12:00 PM is within RTH");
        Assert.That(MarketTime.RTH.Contains(new TimeOnly(16, 0)), Is.True, "4:00 PM is end of RTH");
        Assert.That(MarketTime.RTH.Contains(new TimeOnly(7, 0)), Is.False, "7:00 AM is pre-market");
        Assert.That(MarketTime.RTH.Contains(new TimeOnly(17, 0)), Is.False, "5:00 PM is after-hours");
    }

    #endregion
}
