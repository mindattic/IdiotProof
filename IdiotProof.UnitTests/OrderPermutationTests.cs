// ============================================================================
// OrderPermutationTests - Exhaustive tests for all order permutations
// ============================================================================
//
// PURPOSE:
// This test file provides comprehensive coverage for all valid combinations
// of order parameters to ensure the trading strategy builder correctly
// handles every permutation of:
//   - Order Sides (Buy, Sell)
//   - Order Types (Market, Limit)
//   - Time In Force (Day, GTC, IOC, FOK)
//   - Price Types (Current, VWAP, Bid, Ask)
//   - Exit Strategies (TakeProfit, StopLoss, TrailingStopLoss, combinations)
//   - Conditions (single, multiple, chained)
//   - Time Windows (Start, End, ClosePosition)
//   - Outside RTH Settings
//
// COVERAGE MATRIX:
// - 2 Order Sides × 2 Order Types × 4 TIF × 4 Price Types = 64 base combinations
// - Each combination tested with various exit strategy configurations
using IdiotProof.Enums;
// - Edge cases for boundary values and validation
//
// ============================================================================

using IdiotProof.Models;
using NUnit.Framework;

namespace IdiotProof.UnitTests;

/// <summary>
/// Exhaustive tests covering all permutations of order configurations.
/// Validates that every valid combination of order parameters produces
/// the expected strategy configuration.
/// </summary>
[TestFixture]
public class OrderPermutationTests
{
    #region Order Side Permutations

    /// <summary>
    /// Tests all Order Side values (Buy, Sell) with basic configuration.
    /// </summary>
    [TestFixture]
    public class OrderSideTests
    {
        [Test]
        [Description("Validates Buy order creates strategy with OrderSide.Buy")]
        public void Buy_CreatesOrderWithBuySide()
        {
            // Arrange & Act
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .Build();

            // Assert
            Assert.That(strategy.Order.Side, Is.EqualTo(OrderSide.Buy));
        }

        [Test]
        [Description("Validates Sell order creates strategy with OrderSide.Sell")]
        public void Sell_CreatesOrderWithSellSide()
        {
            // Arrange & Act
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Sell(100, Price.Current)
                .Build();

            // Assert
            Assert.That(strategy.Order.Side, Is.EqualTo(OrderSide.Sell));
        }

        [Test]
        [Description("Validates GetIbAction returns BUY for Buy orders")]
        public void Buy_GetIbAction_ReturnsBUY()
        {
            // Arrange
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .Build();

            // Act & Assert
            Assert.That(strategy.Order.GetIbAction(), Is.EqualTo("BUY"));
        }

        [Test]
        [Description("Validates GetIbAction returns SELL for Sell orders")]
        public void Sell_GetIbAction_ReturnsSELL()
        {
            // Arrange
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Sell(100, Price.Current)
                .Build();

            // Act & Assert
            Assert.That(strategy.Order.GetIbAction(), Is.EqualTo("SELL"));
        }
    }

    #endregion

    #region Order Type Permutations

    /// <summary>
    /// Tests all Order Type values (Market, Limit) with various configurations.
    /// </summary>
    [TestFixture]
    public class OrderTypeTests
    {
        [Test]
        [Description("Validates Market order type is set correctly")]
        public void OrderType_Market_SetsMarketType()
        {
            // Arrange & Act
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .OrderType(OrderType.Market)
                .Build();

            // Assert
            Assert.That(strategy.Order.Type, Is.EqualTo(OrderType.Market));
        }

        [Test]
        [Description("Validates Limit order type is set correctly")]
        public void OrderType_Limit_SetsLimitType()
        {
            // Arrange & Act
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .OrderType(OrderType.Limit)
                .Build();

            // Assert
            Assert.That(strategy.Order.Type, Is.EqualTo(OrderType.Limit));
        }

        [Test]
        [Description("Validates GetIbOrderType returns MKT for Market orders")]
        public void Market_GetIbOrderType_ReturnsMKT()
        {
            // Arrange
            var order = new OrderAction { Type = OrderType.Market };

            // Act & Assert
            Assert.That(order.GetIbOrderType(), Is.EqualTo("MKT"));
        }

        [Test]
        [Description("Validates GetIbOrderType returns LMT for Limit orders")]
        public void Limit_GetIbOrderType_ReturnsLMT()
        {
            // Arrange
            var order = new OrderAction { Type = OrderType.Limit };

            // Act & Assert
            Assert.That(order.GetIbOrderType(), Is.EqualTo("LMT"));
        }

        [Test]
        [Description("Validates default order type is Market via fluent builder")]
        public void DefaultOrderType_IsMarket()
        {
            // Arrange & Act
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .Build();

            // Assert
            Assert.That(strategy.Order.Type, Is.EqualTo(OrderType.Market));
        }
    }

    #endregion

    #region Time In Force Permutations

    /// <summary>
    /// Tests all Time In Force values (Day, GTC, IOC, FOK, Overnight, OvernightPlusDay, AtTheOpening).
    /// </summary>
    [TestFixture]
    public class TimeInForcePermutationTests
    {
        #region Basic TIF Tests

        [Test]
        [Description("Validates Day time in force is set correctly")]
        public void TimeInForce_Day_SetsDay()
        {
            // Arrange & Act
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .TimeInForce(TIF.Day)
                .Build();

            // Assert
            Assert.That(strategy.Order.TimeInForce, Is.EqualTo(TimeInForce.Day));
        }

        [Test]
        [Description("Validates GTC time in force is set correctly")]
        public void TimeInForce_GTC_SetsGoodTillCancel()
        {
            // Arrange & Act
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .TimeInForce(TIF.GTC)
                .Build();

            // Assert
            Assert.That(strategy.Order.TimeInForce, Is.EqualTo(TimeInForce.GoodTillCancel));
        }

        [Test]
        [Description("Validates IOC time in force is set correctly")]
        public void TimeInForce_IOC_SetsImmediateOrCancel()
        {
            // Arrange & Act
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .TimeInForce(TIF.IOC)
                .Build();

            // Assert
            Assert.That(strategy.Order.TimeInForce, Is.EqualTo(TimeInForce.ImmediateOrCancel));
        }

        [Test]
        [Description("Validates FOK time in force is set correctly")]
        public void TimeInForce_FOK_SetsFillOrKill()
        {
            // Arrange & Act
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .TimeInForce(TIF.FOK)
                .Build();

            // Assert
            Assert.That(strategy.Order.TimeInForce, Is.EqualTo(TimeInForce.FillOrKill));
        }

        [Test]
        [Description("Validates default time in force is GTC")]
        public void DefaultTimeInForce_IsGTC()
        {
            // Arrange & Act
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .Build();

            // Assert
            Assert.That(strategy.Order.TimeInForce, Is.EqualTo(TimeInForce.GoodTillCancel));
        }

        #endregion

        #region Extended TIF Tests (Overnight, OvernightPlusDay, AtTheOpening)

        [Test]
        [Description("Validates Overnight time in force is set correctly")]
        public void TimeInForce_Overnight_SetsOvernight()
        {
            // Arrange & Act
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .TimeInForce(TIF.Overnight)
                .Build();

            // Assert
            Assert.That(strategy.Order.TimeInForce, Is.EqualTo(TimeInForce.Overnight));
        }

        [Test]
        [Description("Validates OvernightPlusDay time in force is set correctly")]
        public void TimeInForce_OvernightPlusDay_SetsOvernightPlusDay()
        {
            // Arrange & Act
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .TimeInForce(TIF.OvernightPlusDay)
                .Build();

            // Assert
            Assert.That(strategy.Order.TimeInForce, Is.EqualTo(TimeInForce.OvernightPlusDay));
        }

        [Test]
        [Description("Validates AtTheOpening time in force is set correctly")]
        public void TimeInForce_AtTheOpening_SetsAtTheOpening()
        {
            // Arrange & Act
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .TimeInForce(TIF.AtTheOpening)
                .Build();

            // Assert
            Assert.That(strategy.Order.TimeInForce, Is.EqualTo(TimeInForce.AtTheOpening));
        }

        [Test]
        [Description("Validates Overnight alias OVN works correctly")]
        public void TimeInForce_OVN_Alias_SetsOvernight()
        {
            // Arrange & Act
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .TimeInForce(TIF.OVN)
                .Build();

            // Assert
            Assert.That(strategy.Order.TimeInForce, Is.EqualTo(TimeInForce.Overnight));
        }

        [Test]
        [Description("Validates OvernightPlusDay alias OVNDAY works correctly")]
        public void TimeInForce_OVNDAY_Alias_SetsOvernightPlusDay()
        {
            // Arrange & Act
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .TimeInForce(TIF.OVNDAY)
                .Build();

            // Assert
            Assert.That(strategy.Order.TimeInForce, Is.EqualTo(TimeInForce.OvernightPlusDay));
        }

        [Test]
        [Description("Validates AtTheOpening alias OPG works correctly")]
        public void TimeInForce_OPG_Alias_SetsAtTheOpening()
        {
            // Arrange & Act
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .TimeInForce(TIF.OPG)
                .Build();

            // Assert
            Assert.That(strategy.Order.TimeInForce, Is.EqualTo(TimeInForce.AtTheOpening));
        }

        #endregion

        #region IB API Code Tests

        [Test]
        [Description("Validates all TIF values produce correct IB codes")]
        public void AllTimeInForceValues_GetIbTif_ReturnsCorrectCodes()
        {
            Assert.Multiple(() =>
            {
                // Original TIF values
                Assert.That(new OrderAction { TimeInForce = TimeInForce.Day }.GetIbTif(), Is.EqualTo("DAY"));
                Assert.That(new OrderAction { TimeInForce = TimeInForce.GoodTillCancel }.GetIbTif(), Is.EqualTo("GTC"));
                Assert.That(new OrderAction { TimeInForce = TimeInForce.ImmediateOrCancel }.GetIbTif(), Is.EqualTo("IOC"));
                Assert.That(new OrderAction { TimeInForce = TimeInForce.FillOrKill }.GetIbTif(), Is.EqualTo("FOK"));

                // New extended TIF values
                Assert.That(new OrderAction { TimeInForce = TimeInForce.Overnight }.GetIbTif(), Is.EqualTo("GTC"));
                Assert.That(new OrderAction { TimeInForce = TimeInForce.OvernightPlusDay }.GetIbTif(), Is.EqualTo("DTC"));
                Assert.That(new OrderAction { TimeInForce = TimeInForce.AtTheOpening }.GetIbTif(), Is.EqualTo("OPG"));
            });
        }

        [Test]
        [Description("Validates Overnight TIF returns GTC for IB API (with time-based cancellation)")]
        public void Overnight_GetIbTif_ReturnsGTC()
        {
            var order = new OrderAction { TimeInForce = TimeInForce.Overnight };
            Assert.That(order.GetIbTif(), Is.EqualTo("GTC"));
        }

        [Test]
        [Description("Validates OvernightPlusDay TIF returns DTC for IB API")]
        public void OvernightPlusDay_GetIbTif_ReturnsDTC()
        {
            var order = new OrderAction { TimeInForce = TimeInForce.OvernightPlusDay };
            Assert.That(order.GetIbTif(), Is.EqualTo("DTC"));
        }

        [Test]
        [Description("Validates AtTheOpening TIF returns OPG for IB API")]
        public void AtTheOpening_GetIbTif_ReturnsOPG()
        {
            var order = new OrderAction { TimeInForce = TimeInForce.AtTheOpening };
            Assert.That(order.GetIbTif(), Is.EqualTo("OPG"));
        }

        #endregion

        #region TIF Helper Class Tests

        [Test]
        [Description("Validates all TIF helper properties return correct enum values")]
        public void TIF_AllProperties_ReturnCorrectEnums()
        {
            Assert.Multiple(() =>
            {
                // Primary values
                Assert.That(TIF.Day, Is.EqualTo(TimeInForce.Day));
                Assert.That(TIF.GoodTillCancel, Is.EqualTo(TimeInForce.GoodTillCancel));
                Assert.That(TIF.ImmediateOrCancel, Is.EqualTo(TimeInForce.ImmediateOrCancel));
                Assert.That(TIF.FillOrKill, Is.EqualTo(TimeInForce.FillOrKill));
                Assert.That(TIF.Overnight, Is.EqualTo(TimeInForce.Overnight));
                Assert.That(TIF.OvernightPlusDay, Is.EqualTo(TimeInForce.OvernightPlusDay));
                Assert.That(TIF.AtTheOpening, Is.EqualTo(TimeInForce.AtTheOpening));

                // Shorthand aliases
                Assert.That(TIF.GTC, Is.EqualTo(TimeInForce.GoodTillCancel));
                Assert.That(TIF.IOC, Is.EqualTo(TimeInForce.ImmediateOrCancel));
                Assert.That(TIF.FOK, Is.EqualTo(TimeInForce.FillOrKill));
                Assert.That(TIF.OVN, Is.EqualTo(TimeInForce.Overnight));
                Assert.That(TIF.OVNDAY, Is.EqualTo(TimeInForce.OvernightPlusDay));
                Assert.That(TIF.OPG, Is.EqualTo(TimeInForce.AtTheOpening));
            });
        }

        #endregion

        #region Typical Scenario Tests

        [Test]
        [Description("Validates typical overnight trading configuration")]
        public void OvernightTradingScenario_ConfiguresCorrectly()
        {
            // Arrange & Act - Typical earnings play overnight order
            var strategy = Stock.Ticker("AAPL")
                .Breakout(150)
                .Buy(100, Price.Current)
                .TimeInForce(TIF.Overnight)
                .OutsideRTH(outsideRth: true, takeProfit: true)
                .Build();

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(strategy.Order.TimeInForce, Is.EqualTo(TimeInForce.Overnight));
                Assert.That(strategy.Order.OutsideRth, Is.True);
                Assert.That(strategy.Order.GetIbTif(), Is.EqualTo("GTC")); // IB uses GTC with OutsideRth
            });
        }

        [Test]
        [Description("Validates typical opening auction configuration")]
        public void OpeningAuctionScenario_ConfiguresCorrectly()
        {
            // Arrange & Act - Gap play at market open
            var strategy = Stock.Ticker("TSLA")
                .Breakout(200)
                .Buy(50, Price.Current)
                .TimeInForce(TIF.AtTheOpening)
                .Build();

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(strategy.Order.TimeInForce, Is.EqualTo(TimeInForce.AtTheOpening));
                Assert.That(strategy.Order.GetIbTif(), Is.EqualTo("OPG"));
            });
        }

        [Test]
        [Description("Validates overnight-to-day continuous coverage configuration")]
        public void OvernightPlusDayScenario_ConfiguresCorrectly()
        {
            // Arrange & Act - Continuous coverage from pre-market through regular session
            var strategy = Stock.Ticker("NVDA")
                .Start(MarketTime.PreMarket.Start)
                .Breakout(300)
                .Buy(25, Price.Current)
                .TimeInForce(TIF.OvernightPlusDay)
                .OutsideRTH(outsideRth: true, takeProfit: true)
                .TakeProfit(310)
                .Build();

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(strategy.Order.TimeInForce, Is.EqualTo(TimeInForce.OvernightPlusDay));
                Assert.That(strategy.Order.OutsideRth, Is.True);
                Assert.That(strategy.Order.GetIbTif(), Is.EqualTo("DTC"));
                Assert.That(strategy.Order.EnableTakeProfit, Is.True);
            });
        }

        #endregion
    }

    #endregion

    #region Price Type Permutations

    /// <summary>
    /// Tests all Price Type values (Current, VWAP, Bid, Ask) with order configurations.
    /// </summary>
    [TestFixture]
    public class PriceTypePermutationTests
    {
        [Test]
        [Description("Validates Current price type is set correctly")]
        public void PriceType_Current_SetsCurrent()
        {
            // Arrange & Act
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .Build();

            // Assert
            Assert.That(strategy.Order.PriceType, Is.EqualTo(Price.Current));
        }

        [Test]
        [Description("Validates VWAP price type is set correctly")]
        public void PriceType_VWAP_SetsVWAP()
        {
            // Arrange & Act
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.VWAP)
                .Build();

            // Assert
            Assert.That(strategy.Order.PriceType, Is.EqualTo(Price.VWAP));
        }

        [Test]
        [Description("Validates Bid price type is set correctly")]
        public void PriceType_Bid_SetsBid()
        {
            // Arrange & Act
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Bid)
                .Build();

            // Assert
            Assert.That(strategy.Order.PriceType, Is.EqualTo(Price.Bid));
        }

        [Test]
        [Description("Validates Ask price type is set correctly")]
        public void PriceType_Ask_SetsAsk()
        {
            // Arrange & Act
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Ask)
                .Build();

            // Assert
            Assert.That(strategy.Order.PriceType, Is.EqualTo(Price.Ask));
        }

        [Test]
        [Description("Validates all price types for Buy orders")]
        [TestCase(Price.Current)]
        [TestCase(Price.VWAP)]
        [TestCase(Price.Bid)]
        [TestCase(Price.Ask)]
        public void Buy_WithAllPriceTypes_SetsCorrectly(Price priceType)
        {
            // Arrange & Act
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, priceType)
                .Build();

            // Assert
            Assert.That(strategy.Order.PriceType, Is.EqualTo(priceType));
        }

        [Test]
        [Description("Validates all price types for Sell orders")]
        [TestCase(Price.Current)]
        [TestCase(Price.VWAP)]
        [TestCase(Price.Bid)]
        [TestCase(Price.Ask)]
        public void Sell_WithAllPriceTypes_SetsCorrectly(Price priceType)
        {
            // Arrange & Act
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Sell(100, priceType)
                .Build();

            // Assert
            Assert.That(strategy.Order.PriceType, Is.EqualTo(priceType));
        }
    }

    #endregion

    #region Exit Strategy Permutations

    /// <summary>
    /// Tests all exit strategy combinations (TakeProfit, StopLoss, TrailingStopLoss).
    /// </summary>
    [TestFixture]
    public class ExitStrategyPermutationTests
    {
        #region Single Exit Strategy Tests

        [Test]
        [Description("Validates TakeProfit only configuration")]
        public void ExitStrategy_TakeProfitOnly_ConfiguresCorrectly()
        {
            // Arrange & Act
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .TakeProfit(110)
                .Build();

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(strategy.Order.EnableTakeProfit, Is.True);
                Assert.That(strategy.Order.TakeProfitPrice, Is.EqualTo(110));
                Assert.That(strategy.Order.EnableStopLoss, Is.False);
                Assert.That(strategy.Order.EnableTrailingStopLoss, Is.False);
            });
        }

        [Test]
        [Description("Validates StopLoss only configuration")]
        public void ExitStrategy_StopLossOnly_ConfiguresCorrectly()
        {
            // Arrange & Act
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .StopLoss(90)
                .Build();

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(strategy.Order.EnableStopLoss, Is.True);
                Assert.That(strategy.Order.StopLossPrice, Is.EqualTo(90));
                Assert.That(strategy.Order.EnableTakeProfit, Is.False);
                Assert.That(strategy.Order.EnableTrailingStopLoss, Is.False);
            });
        }

        [Test]
        [Description("Validates TrailingStopLoss only configuration")]
        public void ExitStrategy_TrailingStopLossOnly_ConfiguresCorrectly()
        {
            // Arrange & Act
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .TrailingStopLoss(Percent.Ten)
                .Build();

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(strategy.Order.EnableTrailingStopLoss, Is.True);
                Assert.That(strategy.Order.TrailingStopLossPercent, Is.EqualTo(0.10));
                Assert.That(strategy.Order.EnableTakeProfit, Is.False);
                Assert.That(strategy.Order.EnableStopLoss, Is.False);
            });
        }

        #endregion

        #region Dual Exit Strategy Tests

        [Test]
        [Description("Validates TakeProfit + StopLoss combination")]
        public void ExitStrategy_TakeProfitAndStopLoss_ConfiguresBoth()
        {
            // Arrange & Act
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .TakeProfit(110)
                .StopLoss(90)
                .Build();

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(strategy.Order.EnableTakeProfit, Is.True);
                Assert.That(strategy.Order.TakeProfitPrice, Is.EqualTo(110));
                Assert.That(strategy.Order.EnableStopLoss, Is.True);
                Assert.That(strategy.Order.StopLossPrice, Is.EqualTo(90));
                Assert.That(strategy.Order.EnableTrailingStopLoss, Is.False);
            });
        }

        [Test]
        [Description("Validates TakeProfit + TrailingStopLoss combination")]
        public void ExitStrategy_TakeProfitAndTrailingStopLoss_ConfiguresBoth()
        {
            // Arrange & Act
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .TakeProfit(110)
                .TrailingStopLoss(Percent.Ten)
                .Build();

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(strategy.Order.EnableTakeProfit, Is.True);
                Assert.That(strategy.Order.TakeProfitPrice, Is.EqualTo(110));
                Assert.That(strategy.Order.EnableTrailingStopLoss, Is.True);
                Assert.That(strategy.Order.TrailingStopLossPercent, Is.EqualTo(0.10));
            });
        }

        [Test]
        [Description("Validates StopLoss + TrailingStopLoss combination (both enabled)")]
        public void ExitStrategy_StopLossAndTrailingStopLoss_ConfiguresBoth()
        {
            // Arrange & Act
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .StopLoss(90)
                .TrailingStopLoss(Percent.Ten)
                .Build();

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(strategy.Order.EnableStopLoss, Is.True);
                Assert.That(strategy.Order.StopLossPrice, Is.EqualTo(90));
                Assert.That(strategy.Order.EnableTrailingStopLoss, Is.True);
                Assert.That(strategy.Order.TrailingStopLossPercent, Is.EqualTo(0.10));
            });
        }

        #endregion

        #region Triple Exit Strategy Tests

        [Test]
        [Description("Validates TakeProfit + StopLoss + TrailingStopLoss combination (all enabled)")]
        public void ExitStrategy_AllThree_ConfiguresAll()
        {
            // Arrange & Act
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .TakeProfit(110)
                .StopLoss(90)
                .TrailingStopLoss(Percent.Five)
                .Build();

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(strategy.Order.EnableTakeProfit, Is.True);
                Assert.That(strategy.Order.TakeProfitPrice, Is.EqualTo(110));
                Assert.That(strategy.Order.EnableStopLoss, Is.True);
                Assert.That(strategy.Order.StopLossPrice, Is.EqualTo(90));
                Assert.That(strategy.Order.EnableTrailingStopLoss, Is.True);
                Assert.That(strategy.Order.TrailingStopLossPercent, Is.EqualTo(0.05));
            });
        }

        #endregion

        #region No Exit Strategy Tests

        [Test]
        [Description("Validates no exit strategy configuration")]
        public void ExitStrategy_None_AllDisabled()
        {
            // Arrange & Act
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .Build();

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(strategy.Order.EnableTakeProfit, Is.False);
                Assert.That(strategy.Order.EnableStopLoss, Is.False);
                Assert.That(strategy.Order.EnableTrailingStopLoss, Is.False);
            });
        }

        #endregion

        #region Trailing Stop Loss Percentage Tests

        [Test]
        [Description("Validates all standard trailing stop loss percentages")]
        [TestCase(0.01, Description = "1%")]
        [TestCase(0.05, Description = "5%")]
        [TestCase(0.10, Description = "10%")]
        [TestCase(0.15, Description = "15%")]
        [TestCase(0.20, Description = "20%")]
        [TestCase(0.25, Description = "25%")]
        [TestCase(0.50, Description = "50%")]
        public void TrailingStopLoss_WithVariousPercentages_SetsCorrectly(double percent)
        {
            // Arrange & Act
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .TrailingStopLoss(percent)
                .Build();

            // Assert
            Assert.That(strategy.Order.TrailingStopLossPercent, Is.EqualTo(percent));
        }

        [Test]
        [Description("Validates trailing stop loss with Percent helper constants")]
        public void TrailingStopLoss_WithPercentHelpers_SetsCorrectly()
        {
            Assert.Multiple(() =>
            {
                var s1 = Stock.Ticker("T").Breakout(100).Buy(100, Price.Current).TrailingStopLoss(Percent.One).Build();
                var s5 = Stock.Ticker("T").Breakout(100).Buy(100, Price.Current).TrailingStopLoss(Percent.Five).Build();
                var s10 = Stock.Ticker("T").Breakout(100).Buy(100, Price.Current).TrailingStopLoss(Percent.Ten).Build();
                var s15 = Stock.Ticker("T").Breakout(100).Buy(100, Price.Current).TrailingStopLoss(Percent.Fifteen).Build();
                var s20 = Stock.Ticker("T").Breakout(100).Buy(100, Price.Current).TrailingStopLoss(Percent.Twenty).Build();
                var s25 = Stock.Ticker("T").Breakout(100).Buy(100, Price.Current).TrailingStopLoss(Percent.TwentyFive).Build();
                var s50 = Stock.Ticker("T").Breakout(100).Buy(100, Price.Current).TrailingStopLoss(Percent.Fifty).Build();

                Assert.That(s1.Order.TrailingStopLossPercent, Is.EqualTo(0.01));
                Assert.That(s5.Order.TrailingStopLossPercent, Is.EqualTo(0.05));
                Assert.That(s10.Order.TrailingStopLossPercent, Is.EqualTo(0.10));
                Assert.That(s15.Order.TrailingStopLossPercent, Is.EqualTo(0.15));
                Assert.That(s20.Order.TrailingStopLossPercent, Is.EqualTo(0.20));
                Assert.That(s25.Order.TrailingStopLossPercent, Is.EqualTo(0.25));
                Assert.That(s50.Order.TrailingStopLossPercent, Is.EqualTo(0.50));
            });
        }

        #endregion
    }

    #endregion

    #region Quantity Permutations

    /// <summary>
    /// Tests various quantity configurations for orders.
    /// </summary>
    [TestFixture]
    public class QuantityPermutationTests
    {
        [Test]
        [Description("Validates small quantity orders")]
        [TestCase(1)]
        [TestCase(10)]
        [TestCase(50)]
        public void Quantity_SmallValues_SetsCorrectly(int quantity)
        {
            // Arrange & Act
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(quantity, Price.Current)
                .Build();

            // Assert
            Assert.That(strategy.Order.Quantity, Is.EqualTo(quantity));
        }

        [Test]
        [Description("Validates standard quantity orders")]
        [TestCase(100)]
        [TestCase(500)]
        [TestCase(1000)]
        public void Quantity_StandardValues_SetsCorrectly(int quantity)
        {
            // Arrange & Act
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(quantity, Price.Current)
                .Build();

            // Assert
            Assert.That(strategy.Order.Quantity, Is.EqualTo(quantity));
        }

        [Test]
        [Description("Validates large quantity orders")]
        [TestCase(5000)]
        [TestCase(10000)]
        [TestCase(100000)]
        public void Quantity_LargeValues_SetsCorrectly(int quantity)
        {
            // Arrange & Act
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(quantity, Price.Current)
                .Build();

            // Assert
            Assert.That(strategy.Order.Quantity, Is.EqualTo(quantity));
        }

        [Test]
        [Description("Validates zero quantity (edge case - broker validates)")]
        public void Quantity_Zero_IsAllowed()
        {
            // Note: Zero quantity is technically allowed by builder; IBKR will reject
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(0, Price.Current)
                .Build();

            Assert.That(strategy.Order.Quantity, Is.EqualTo(0));
        }

        [Test]
        [Description("Validates negative quantity (edge case - broker validates)")]
        public void Quantity_Negative_IsAllowed()
        {
            // Note: Negative quantity is allowed by builder; broker validates
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(-100, Price.Current)
                .Build();

            Assert.That(strategy.Order.Quantity, Is.EqualTo(-100));
        }
    }

    #endregion

    #region Outside RTH Permutations

    /// <summary>
    /// Tests all combinations of Outside Regular Trading Hours settings.
    /// </summary>
    [TestFixture]
    public class OutsideRthPermutationTests
    {
        [Test]
        [Description("Validates OutsideRTH enabled for both entry and take profit")]
        public void OutsideRTH_BothEnabled_SetsCorrectly()
        {
            // Arrange & Act
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .OutsideRTH(outsideRth: true, takeProfit: true)
                .Build();

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(strategy.Order.OutsideRth, Is.True);
                Assert.That(strategy.Order.TakeProfitOutsideRth, Is.True);
            });
        }

        [Test]
        [Description("Validates OutsideRTH disabled for both entry and take profit")]
        public void OutsideRTH_BothDisabled_SetsCorrectly()
        {
            // Arrange & Act
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .OutsideRTH(outsideRth: false, takeProfit: false)
                .Build();

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(strategy.Order.OutsideRth, Is.False);
                Assert.That(strategy.Order.TakeProfitOutsideRth, Is.False);
            });
        }

        [Test]
        [Description("Validates OutsideRTH enabled only for entry")]
        public void OutsideRTH_EntryOnlyEnabled_SetsCorrectly()
        {
            // Arrange & Act
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .OutsideRTH(outsideRth: true, takeProfit: false)
                .Build();

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(strategy.Order.OutsideRth, Is.True);
                Assert.That(strategy.Order.TakeProfitOutsideRth, Is.False);
            });
        }

        [Test]
        [Description("Validates OutsideRTH enabled only for take profit")]
        public void OutsideRTH_TakeProfitOnlyEnabled_SetsCorrectly()
        {
            // Arrange & Act
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .OutsideRTH(outsideRth: false, takeProfit: true)
                .Build();

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(strategy.Order.OutsideRth, Is.False);
                Assert.That(strategy.Order.TakeProfitOutsideRth, Is.True);
            });
        }

        [Test]
        [Description("Validates default OutsideRTH settings")]
        public void OutsideRTH_Default_BothEnabled()
        {
            // Arrange & Act
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .Build();

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(strategy.Order.OutsideRth, Is.True);
                Assert.That(strategy.Order.TakeProfitOutsideRth, Is.True);
            });
        }
    }

    #endregion

    #region Time Window Permutations

    /// <summary>
    /// Tests all time window configurations (Start, End, ClosePosition).
    /// </summary>
    [TestFixture]
    public class TimeWindowPermutationTests
    {
        [Test]
        [Description("Validates Start time only configuration")]
        public void TimeWindow_StartTimeOnly_SetsCorrectly()
        {
            // Arrange
            var startTime = new TimeOnly(3, 0);

            // Act
            var strategy = Stock.Ticker("TEST")
                .Start(startTime)
                .Breakout(100)
                .Buy(100, Price.Current)
                .Build();

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(strategy.StartTime, Is.EqualTo(startTime));
                Assert.That(strategy.EndTime, Is.Null);
            });
        }

        [Test]
        [Description("Validates End time via End() method")]
        public void TimeWindow_EndTimeOnly_SetsCorrectly()
        {
            // Arrange
            var endTime = new TimeOnly(7, 0);

            // Act
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .End(endTime);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(strategy.StartTime, Is.Null);
                Assert.That(strategy.EndTime, Is.EqualTo(endTime));
            });
        }

        [Test]
        [Description("Validates Start and End time configuration")]
        public void TimeWindow_StartAndEnd_SetsCorrectly()
        {
            // Arrange
            var startTime = new TimeOnly(3, 0);
            var endTime = new TimeOnly(7, 0);

            // Act
            var strategy = Stock.Ticker("TEST")
                .Start(startTime)
                .Breakout(100)
                .Buy(100, Price.Current)
                .End(endTime);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(strategy.StartTime, Is.EqualTo(startTime));
                Assert.That(strategy.EndTime, Is.EqualTo(endTime));
            });
        }

        [Test]
        [Description("Validates ClosePosition time configuration")]
        public void TimeWindow_ClosePositionTime_SetsCorrectly()
        {
            // Arrange
            var closeTime = new TimeOnly(6, 50);

            // Act
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .ClosePosition(closeTime)
                .Build();

            // Assert
            Assert.That(strategy.Order.ClosePositionTime, Is.EqualTo(closeTime));
        }

        [Test]
        [Description("Validates all time configurations together")]
        public void TimeWindow_AllTimes_SetsCorrectly()
        {
            // Arrange
            var startTime = new TimeOnly(3, 0);
            var endTime = new TimeOnly(7, 0);
            var closeTime = new TimeOnly(6, 50);

            // Act
            var strategy = Stock.Ticker("TEST")
                .Start(startTime)
                .Breakout(100)
                .Buy(100, Price.Current)
                .ClosePosition(closeTime)
                .End(endTime);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(strategy.StartTime, Is.EqualTo(startTime));
                Assert.That(strategy.EndTime, Is.EqualTo(endTime));
                Assert.That(strategy.Order.ClosePositionTime, Is.EqualTo(closeTime));
            });
        }

        [Test]
        [Description("Validates PreMarket time constants (Eastern Time)")]
        public void TimeWindow_PreMarketConstants_AreCorrect()
        {
            // Arrange & Act
            var strategy = Stock.Ticker("TEST")
                .Start(MarketTime.PreMarket.Start)
                .Breakout(100)
                .Buy(100, Price.Current)
                .ClosePosition(MarketTime.PreMarket.End.AddMinutes(-10))
                .End(MarketTime.PreMarket.End);

            // Assert - All times are in Eastern Time (ET)
            Assert.Multiple(() =>
            {
                Assert.That(strategy.StartTime, Is.EqualTo(new TimeOnly(4, 0)));   // 4:00 AM ET
                Assert.That(strategy.EndTime, Is.EqualTo(new TimeOnly(9, 30)));    // 9:30 AM ET
                Assert.That(strategy.Order.ClosePositionTime, Is.EqualTo(new TimeOnly(9, 20))); // 9:20 AM ET
            });
        }

        [Test]
        [Description("Validates RTH time constants (Eastern Time)")]
        public void TimeWindow_RTHConstants_AreCorrect()
        {
            // Arrange & Act
            var strategy = Stock.Ticker("TEST")
                .Start(MarketTime.RTH.Start)
                .Breakout(100)
                .Buy(100, Price.Current)
                .End(MarketTime.RTH.End);

            // Assert - All times are in Eastern Time (ET)
            Assert.Multiple(() =>
            {
                Assert.That(strategy.StartTime, Is.EqualTo(new TimeOnly(9, 30)));  // 9:30 AM ET (Market Open)
                Assert.That(strategy.EndTime, Is.EqualTo(new TimeOnly(16, 0)));    // 4:00 PM ET (Market Close)
            });
        }

        [Test]
        [Description("Validates AfterHours time constants (Eastern Time)")]
        public void TimeWindow_AfterHoursConstants_AreCorrect()
        {
            // Arrange & Act
            var strategy = Stock.Ticker("TEST")
                .Start(MarketTime.AfterHours.Start)
                .Breakout(100)
                .Buy(100, Price.Current)
                .End(MarketTime.AfterHours.End);

            // Assert - All times are in Eastern Time (ET)
            Assert.Multiple(() =>
            {
                Assert.That(strategy.StartTime, Is.EqualTo(new TimeOnly(16, 0)));  // 4:00 PM ET
                Assert.That(strategy.EndTime, Is.EqualTo(new TimeOnly(20, 0)));    // 8:00 PM ET
            });
        }
    }

    #endregion

    #region Condition Chain Permutations

    /// <summary>
    /// Tests various condition chain configurations.
    /// </summary>
    [TestFixture]
    public class ConditionChainPermutationTests
    {
        [Test]
        [Description("Validates single Breakout condition")]
        public void ConditionChain_SingleBreakout_CreatesCorrectly()
        {
            // Arrange & Act
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .Build();

            // Assert
            Assert.That(strategy.Conditions, Has.Count.EqualTo(1));
            Assert.That(strategy.Conditions[0], Is.InstanceOf<BreakoutCondition>());
        }

        [Test]
        [Description("Validates single Pullback condition")]
        public void ConditionChain_SinglePullback_CreatesCorrectly()
        {
            // Arrange & Act
            var strategy = Stock.Ticker("TEST")
                .Pullback(100)
                .Buy(100, Price.Current)
                .Build();

            // Assert
            Assert.That(strategy.Conditions, Has.Count.EqualTo(1));
            Assert.That(strategy.Conditions[0], Is.InstanceOf<PullbackCondition>());
        }

        [Test]
        [Description("Validates single AboveVwap condition")]
        public void ConditionChain_SingleAboveVwap_CreatesCorrectly()
        {
            // Arrange & Act
            var strategy = Stock.Ticker("TEST")
                .AboveVwap()
                .Buy(100, Price.Current)
                .Build();

            // Assert
            Assert.That(strategy.Conditions, Has.Count.EqualTo(1));
            Assert.That(strategy.Conditions[0], Is.InstanceOf<AboveVwapCondition>());
        }

        [Test]
        [Description("Validates single BelowVwap condition")]
        public void ConditionChain_SingleBelowVwap_CreatesCorrectly()
        {
            // Arrange & Act
            var strategy = Stock.Ticker("TEST")
                .BelowVwap()
                .Buy(100, Price.Current)
                .Build();

            // Assert
            Assert.That(strategy.Conditions, Has.Count.EqualTo(1));
            Assert.That(strategy.Conditions[0], Is.InstanceOf<BelowVwapCondition>());
        }

        [Test]
        [Description("Validates single PriceAbove condition")]
        public void ConditionChain_SinglePriceAbove_CreatesCorrectly()
        {
            // Arrange & Act
            var strategy = Stock.Ticker("TEST")
                .PriceAbove(100)
                .Buy(100, Price.Current)
                .Build();

            // Assert
            Assert.That(strategy.Conditions, Has.Count.EqualTo(1));
            Assert.That(strategy.Conditions[0], Is.InstanceOf<PriceAboveCondition>());
        }

        [Test]
        [Description("Validates single PriceBelow condition")]
        public void ConditionChain_SinglePriceBelow_CreatesCorrectly()
        {
            // Arrange & Act
            var strategy = Stock.Ticker("TEST")
                .PriceBelow(100)
                .Buy(100, Price.Current)
                .Build();

            // Assert
            Assert.That(strategy.Conditions, Has.Count.EqualTo(1));
            Assert.That(strategy.Conditions[0], Is.InstanceOf<PriceBelowCondition>());
        }

        [Test]
        [Description("Validates single Custom condition")]
        public void ConditionChain_SingleCustom_CreatesCorrectly()
        {
            // Arrange & Act
            var strategy = Stock.Ticker("TEST")
                .When("Custom test", (p, v) => p > 100)
                .Buy(100, Price.Current)
                .Build();

            // Assert
            Assert.That(strategy.Conditions, Has.Count.EqualTo(1));
            Assert.That(strategy.Conditions[0], Is.InstanceOf<CustomCondition>());
            Assert.That(strategy.Conditions[0].Name, Is.EqualTo("Custom test"));
        }

        [Test]
        [Description("Validates dual condition chain (Breakout + Pullback)")]
        public void ConditionChain_BreakoutThenPullback_CreatesInOrder()
        {
            // Arrange & Act
            var strategy = Stock.Ticker("TEST")
                .Breakout(110)
                .Pullback(100)
                .Buy(100, Price.Current)
                .Build();

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(strategy.Conditions, Has.Count.EqualTo(2));
                Assert.That(strategy.Conditions[0], Is.InstanceOf<BreakoutCondition>());
                Assert.That(strategy.Conditions[1], Is.InstanceOf<PullbackCondition>());
            });
        }

        [Test]
        [Description("Validates triple condition chain (Breakout + Pullback + AboveVwap)")]
        public void ConditionChain_BreakoutPullbackAboveVwap_CreatesInOrder()
        {
            // Arrange & Act
            var strategy = Stock.Ticker("TEST")
                .Breakout(110)
                .Pullback(100)
                .AboveVwap()
                .Buy(100, Price.Current)
                .Build();

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(strategy.Conditions, Has.Count.EqualTo(3));
                Assert.That(strategy.Conditions[0], Is.InstanceOf<BreakoutCondition>());
                Assert.That(strategy.Conditions[1], Is.InstanceOf<PullbackCondition>());
                Assert.That(strategy.Conditions[2], Is.InstanceOf<AboveVwapCondition>());
            });
        }

        [Test]
        [Description("Validates all condition types chained together")]
        public void ConditionChain_AllConditionTypes_CreatesInOrder()
        {
            // Arrange & Act
            var strategy = Stock.Ticker("TEST")
                .Breakout(120)
                .Pullback(110)
                .AboveVwap(0.05)
                .PriceAbove(105)
                .When("Custom", (p, v) => true)
                .Buy(100, Price.Current)
                .Build();

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(strategy.Conditions, Has.Count.EqualTo(5));
                Assert.That(strategy.Conditions[0], Is.InstanceOf<BreakoutCondition>());
                Assert.That(strategy.Conditions[1], Is.InstanceOf<PullbackCondition>());
                Assert.That(strategy.Conditions[2], Is.InstanceOf<AboveVwapCondition>());
                Assert.That(strategy.Conditions[3], Is.InstanceOf<PriceAboveCondition>());
                Assert.That(strategy.Conditions[4], Is.InstanceOf<CustomCondition>());
            });
        }

        [Test]
        [Description("Validates multiple same-type conditions")]
        public void ConditionChain_MultipleBreakouts_AllAdded()
        {
            // Arrange & Act
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Breakout(110)
                .Breakout(120)
                .Buy(100, Price.Current)
                .Build();

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(strategy.Conditions, Has.Count.EqualTo(3));
                Assert.That(strategy.Conditions.All(c => c is BreakoutCondition), Is.True);
            });
        }
    }

    #endregion

    #region Complete Order Permutation Matrix Tests

    /// <summary>
    /// Tests complete permutation combinations of order parameters.
    /// </summary>
    [TestFixture]
    public class CompletePermutationMatrixTests
    {
        /// <summary>
        /// Tests all combinations of OrderSide × OrderType.
        /// </summary>
        [Test]
        [Description("Validates Buy + Market combination")]
        public void BuyMarket_ConfiguresCorrectly()
        {
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .OrderType(OrderType.Market)
                .Build();

            Assert.Multiple(() =>
            {
                Assert.That(strategy.Order.Side, Is.EqualTo(OrderSide.Buy));
                Assert.That(strategy.Order.Type, Is.EqualTo(OrderType.Market));
            });
        }

        [Test]
        [Description("Validates Buy + Limit combination")]
        public void BuyLimit_ConfiguresCorrectly()
        {
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .OrderType(OrderType.Limit)
                .Build();

            Assert.Multiple(() =>
            {
                Assert.That(strategy.Order.Side, Is.EqualTo(OrderSide.Buy));
                Assert.That(strategy.Order.Type, Is.EqualTo(OrderType.Limit));
            });
        }

        [Test]
        [Description("Validates Sell + Market combination")]
        public void SellMarket_ConfiguresCorrectly()
        {
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Sell(100, Price.Current)
                .OrderType(OrderType.Market)
                .Build();

            Assert.Multiple(() =>
            {
                Assert.That(strategy.Order.Side, Is.EqualTo(OrderSide.Sell));
                Assert.That(strategy.Order.Type, Is.EqualTo(OrderType.Market));
            });
        }

        [Test]
        [Description("Validates Sell + Limit combination")]
        public void SellLimit_ConfiguresCorrectly()
        {
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Sell(100, Price.Current)
                .OrderType(OrderType.Limit)
                .Build();

            Assert.Multiple(() =>
            {
                Assert.That(strategy.Order.Side, Is.EqualTo(OrderSide.Sell));
                Assert.That(strategy.Order.Type, Is.EqualTo(OrderType.Limit));
            });
        }

        /// <summary>
        /// Tests OrderSide × TimeInForce combinations.
        /// </summary>
        [Test]
        [Description("Validates Buy with all TIF options")]
        [TestCase(TimeInForce.Day)]
        [TestCase(TimeInForce.GoodTillCancel)]
        [TestCase(TimeInForce.ImmediateOrCancel)]
        [TestCase(TimeInForce.FillOrKill)]
        public void Buy_WithAllTIF_ConfiguresCorrectly(TimeInForce tif)
        {
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .TimeInForce(tif)
                .Build();

            Assert.Multiple(() =>
            {
                Assert.That(strategy.Order.Side, Is.EqualTo(OrderSide.Buy));
                Assert.That(strategy.Order.TimeInForce, Is.EqualTo(tif));
            });
        }

        [Test]
        [Description("Validates Sell with all TIF options")]
        [TestCase(TimeInForce.Day)]
        [TestCase(TimeInForce.GoodTillCancel)]
        [TestCase(TimeInForce.ImmediateOrCancel)]
        [TestCase(TimeInForce.FillOrKill)]
        public void Sell_WithAllTIF_ConfiguresCorrectly(TimeInForce tif)
        {
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Sell(100, Price.Current)
                .TimeInForce(tif)
                .Build();

            Assert.Multiple(() =>
            {
                Assert.That(strategy.Order.Side, Is.EqualTo(OrderSide.Sell));
                Assert.That(strategy.Order.TimeInForce, Is.EqualTo(tif));
            });
        }

        /// <summary>
        /// Complete matrix test: Side × Type × TIF × Price.
        /// </summary>
        [Test]
        [Description("Validates complete Buy + Market + GTC + Current configuration")]
        public void CompleteConfig_BuyMarketGTCCurrent_AllSet()
        {
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .OrderType(OrderType.Market)
                .TimeInForce(TIF.GTC)
                .TakeProfit(110)
                .StopLoss(90)
                .Build();

            Assert.Multiple(() =>
            {
                Assert.That(strategy.Order.Side, Is.EqualTo(OrderSide.Buy));
                Assert.That(strategy.Order.Type, Is.EqualTo(OrderType.Market));
                Assert.That(strategy.Order.TimeInForce, Is.EqualTo(TimeInForce.GoodTillCancel));
                Assert.That(strategy.Order.PriceType, Is.EqualTo(Price.Current));
                Assert.That(strategy.Order.EnableTakeProfit, Is.True);
                Assert.That(strategy.Order.EnableStopLoss, Is.True);
            });
        }

        [Test]
        [Description("Validates complete Sell + Limit + Day + VWAP configuration")]
        public void CompleteConfig_SellLimitDayVWAP_AllSet()
        {
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Sell(100, Price.VWAP)
                .OrderType(OrderType.Limit)
                .TimeInForce(TIF.Day)
                .TakeProfit(90)
                .StopLoss(110)
                .Build();

            Assert.Multiple(() =>
            {
                Assert.That(strategy.Order.Side, Is.EqualTo(OrderSide.Sell));
                Assert.That(strategy.Order.Type, Is.EqualTo(OrderType.Limit));
                Assert.That(strategy.Order.TimeInForce, Is.EqualTo(TimeInForce.Day));
                Assert.That(strategy.Order.PriceType, Is.EqualTo(Price.VWAP));
                Assert.That(strategy.Order.EnableTakeProfit, Is.True);
                Assert.That(strategy.Order.EnableStopLoss, Is.True);
            });
        }

        [Test]
        [Description("Validates full pre-market strategy configuration")]
        public void CompleteConfig_PreMarketStrategy_AllSet()
        {
            var strategy = Stock.Ticker("AAPL")
                .Start(MarketTime.PreMarket.Start)
                .Breakout(150)
                .Pullback(148)
                .AboveVwap()
                .Buy(100, Price.Current)
                .OrderType(OrderType.Market)
                .TimeInForce(TIF.GTC)
                .OutsideRTH(outsideRth: true, takeProfit: true)
                .TakeProfit(155)
                .TrailingStopLoss(Percent.Ten)
                .ClosePosition(MarketTime.PreMarket.End.AddMinutes(-10))
                .End(MarketTime.PreMarket.End);

            Assert.Multiple(() =>
            {
                // Symbol configuration
                Assert.That(strategy.Symbol, Is.EqualTo("AAPL"));

                // Time configuration (all times in Eastern Time)
                Assert.That(strategy.StartTime, Is.EqualTo(new TimeOnly(4, 0)));   // 4:00 AM ET
                Assert.That(strategy.EndTime, Is.EqualTo(new TimeOnly(9, 30)));    // 9:30 AM ET

                // Conditions
                Assert.That(strategy.Conditions, Has.Count.EqualTo(3));

                // Order configuration
                Assert.That(strategy.Order.Side, Is.EqualTo(OrderSide.Buy));
                Assert.That(strategy.Order.Quantity, Is.EqualTo(100));
                Assert.That(strategy.Order.Type, Is.EqualTo(OrderType.Market));
                Assert.That(strategy.Order.TimeInForce, Is.EqualTo(TimeInForce.GoodTillCancel));
                Assert.That(strategy.Order.OutsideRth, Is.True);
                Assert.That(strategy.Order.TakeProfitOutsideRth, Is.True);

                // Exit configuration
                Assert.That(strategy.Order.EnableTakeProfit, Is.True);
                Assert.That(strategy.Order.TakeProfitPrice, Is.EqualTo(155));
                Assert.That(strategy.Order.EnableTrailingStopLoss, Is.True);
                Assert.That(strategy.Order.TrailingStopLossPercent, Is.EqualTo(0.10));
                Assert.That(strategy.Order.ClosePositionTime, Is.EqualTo(new TimeOnly(9, 20))); // 9:20 AM ET
            });
        }
    }

    #endregion

    #region Symbol and Exchange Configuration Tests

    /// <summary>
    /// Tests symbol, exchange, and currency configurations.
    /// </summary>
    [TestFixture]
    public class SymbolConfigurationTests
    {
        [Test]
        [Description("Validates symbol is set correctly")]
        [TestCase("AAPL")]
        [TestCase("MSFT")]
        [TestCase("GOOGL")]
        [TestCase("TEST")]
        public void Symbol_VariousValues_SetsCorrectly(string symbol)
        {
            var strategy = Stock.Ticker(symbol)
                .Breakout(100)
                .Buy(100, Price.Current)
                .Build();

            Assert.That(strategy.Symbol, Is.EqualTo(symbol));
        }

        [Test]
        [Description("Validates default exchange is SMART")]
        public void Exchange_Default_IsSMART()
        {
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .Build();

            Assert.That(strategy.Exchange, Is.EqualTo("SMART"));
        }

        [Test]
        [Description("Validates exchange can be customized")]
        [TestCase("NASDAQ")]
        [TestCase("NYSE")]
        [TestCase("ARCA")]
        public void Exchange_CustomValues_SetsCorrectly(string exchange)
        {
            var strategy = Stock.Ticker("TEST")
                .Exchange(exchange)
                .Breakout(100)
                .Buy(100, Price.Current)
                .Build();

            Assert.That(strategy.Exchange, Is.EqualTo(exchange));
        }

        [Test]
        [Description("Validates default currency is USD")]
        public void Currency_Default_IsUSD()
        {
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .Build();

            Assert.That(strategy.Currency, Is.EqualTo("USD"));
        }

        [Test]
        [Description("Validates currency can be customized")]
        [TestCase("EUR")]
        [TestCase("GBP")]
        [TestCase("CAD")]
        public void Currency_CustomValues_SetsCorrectly(string currency)
        {
            var strategy = Stock.Ticker("TEST")
                .Currency(currency)
                .Breakout(100)
                .Buy(100, Price.Current)
                .Build();

            Assert.That(strategy.Currency, Is.EqualTo(currency));
        }

        [Test]
        [Description("Validates default SecType is STK")]
        public void SecType_Default_IsSTK()
        {
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .Build();

            Assert.That(strategy.SecType, Is.EqualTo("STK"));
        }
    }

    #endregion

    #region Enabled/Disabled Strategy Tests

    /// <summary>
    /// Tests strategy enabled/disabled state.
    /// </summary>
    [TestFixture]
    public class StrategyEnabledTests
    {
        [Test]
        [Description("Validates default enabled state is true")]
        public void Enabled_Default_IsTrue()
        {
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .Build();

            Assert.That(strategy.Enabled, Is.True);
        }

        [Test]
        [Description("Validates strategy can be disabled")]
        public void Enabled_SetFalse_DisablesStrategy()
        {
            var strategy = Stock.Ticker("TEST")
                .Enabled(false)
                .Breakout(100)
                .Buy(100, Price.Current)
                .Build();

            Assert.That(strategy.Enabled, Is.False);
        }

        [Test]
        [Description("Validates strategy can be explicitly enabled")]
        public void Enabled_SetTrue_EnablesStrategy()
        {
            var strategy = Stock.Ticker("TEST")
                .Enabled(true)
                .Breakout(100)
                .Buy(100, Price.Current)
                .Build();

            Assert.That(strategy.Enabled, Is.True);
        }

        [Test]
        [Description("Validates disabled strategy preserves all other settings")]
        public void Enabled_Disabled_PreservesOtherSettings()
        {
            var strategy = Stock.Ticker("TEST")
                .Enabled(false)
                .Start(MarketTime.PreMarket.Start)
                .Exchange("NASDAQ")
                .Breakout(100)
                .Pullback(95)
                .Buy(200, Price.VWAP)
                .TakeProfit(110)
                .Build();

            Assert.Multiple(() =>
            {
                Assert.That(strategy.Enabled, Is.False);
                Assert.That(strategy.Symbol, Is.EqualTo("TEST"));
                Assert.That(strategy.Exchange, Is.EqualTo("NASDAQ"));
                Assert.That(strategy.StartTime, Is.EqualTo(MarketTime.PreMarket.Start));
                Assert.That(strategy.Conditions, Has.Count.EqualTo(2));
                Assert.That(strategy.Order.Quantity, Is.EqualTo(200));
                Assert.That(strategy.Order.PriceType, Is.EqualTo(Price.VWAP));
                Assert.That(strategy.Order.TakeProfitPrice, Is.EqualTo(110));
            });
        }
    }

    #endregion

    #region OrderAction ToString Tests

    /// <summary>
    /// Tests OrderAction.ToString() for various configurations.
    /// </summary>
    [TestFixture]
    public class OrderActionToStringTests
    {
        [Test]
        [Description("Validates ToString for basic Buy order")]
        public void ToString_BasicBuy_FormatsCorrectly()
        {
            var order = new OrderAction
            {
                Side = OrderSide.Buy,
                Quantity = 100,
                Type = OrderType.Market,
                TimeInForce = TimeInForce.GoodTillCancel,
                EnableTakeProfit = false
            };

            var result = order.ToString();

            Assert.That(result, Does.Contain("Buy"));
            Assert.That(result, Does.Contain("100 shares"));
            Assert.That(result, Does.Contain("Market"));
        }

        [Test]
        [Description("Validates ToString includes TakeProfit when enabled with price")]
        public void ToString_WithTakeProfitPrice_IncludesTP()
        {
            var order = new OrderAction
            {
                Side = OrderSide.Buy,
                Quantity = 100,
                Type = OrderType.Market,
                TimeInForce = TimeInForce.GoodTillCancel,
                EnableTakeProfit = true,
                TakeProfitPrice = 110.50
            };

            var result = order.ToString();

            Assert.That(result, Does.Contain("TP=110.50"));
        }

        [Test]
        [Description("Validates ToString includes StopLoss when enabled")]
        public void ToString_WithStopLoss_IncludesSL()
        {
            var order = new OrderAction
            {
                Side = OrderSide.Buy,
                Quantity = 100,
                Type = OrderType.Market,
                TimeInForce = TimeInForce.GoodTillCancel,
                EnableTakeProfit = false,
                EnableStopLoss = true,
                StopLossPrice = 95.00
            };

            var result = order.ToString();

            Assert.That(result, Does.Contain("SL=95.00"));
        }

        [Test]
        [Description("Validates ToString includes TrailingStopLoss when enabled")]
        public void ToString_WithTrailingStopLoss_IncludesTSL()
        {
            var order = new OrderAction
            {
                Side = OrderSide.Buy,
                Quantity = 100,
                Type = OrderType.Market,
                TimeInForce = TimeInForce.GoodTillCancel,
                EnableTakeProfit = false,
                EnableTrailingStopLoss = true,
                TrailingStopLossPercent = 0.10
            };

            var result = order.ToString();

            Assert.That(result, Does.Contain("TSL=10%"));
        }
    }

    #endregion

    #region Builder Method Chaining Order Tests

    /// <summary>
    /// Tests that builder methods can be called in various orders.
    /// </summary>
    [TestFixture]
    public class BuilderChainingOrderTests
    {
        [Test]
        [Description("Validates exit strategies can be chained in any order")]
        public void ExitStrategies_ChainedDifferentOrders_ProduceSameResult()
        {
            // Order 1: TakeProfit -> StopLoss -> TrailingStopLoss
            var strategy1 = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .TakeProfit(110)
                .StopLoss(90)
                .TrailingStopLoss(Percent.Ten)
                .Build();

            // Order 2: TrailingStopLoss -> TakeProfit -> StopLoss
            var strategy2 = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .TrailingStopLoss(Percent.Ten)
                .TakeProfit(110)
                .StopLoss(90)
                .Build();

            // Order 3: StopLoss -> TrailingStopLoss -> TakeProfit
            var strategy3 = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .StopLoss(90)
                .TrailingStopLoss(Percent.Ten)
                .TakeProfit(110)
                .Build();

            Assert.Multiple(() =>
            {
                // All should have same exit configuration
                Assert.That(strategy1.Order.EnableTakeProfit, Is.EqualTo(strategy2.Order.EnableTakeProfit));
                Assert.That(strategy1.Order.EnableTakeProfit, Is.EqualTo(strategy3.Order.EnableTakeProfit));
                Assert.That(strategy1.Order.TakeProfitPrice, Is.EqualTo(strategy2.Order.TakeProfitPrice));
                Assert.That(strategy1.Order.TakeProfitPrice, Is.EqualTo(strategy3.Order.TakeProfitPrice));
                Assert.That(strategy1.Order.EnableStopLoss, Is.EqualTo(strategy2.Order.EnableStopLoss));
                Assert.That(strategy1.Order.EnableStopLoss, Is.EqualTo(strategy3.Order.EnableStopLoss));
                Assert.That(strategy1.Order.StopLossPrice, Is.EqualTo(strategy2.Order.StopLossPrice));
                Assert.That(strategy1.Order.StopLossPrice, Is.EqualTo(strategy3.Order.StopLossPrice));
                Assert.That(strategy1.Order.EnableTrailingStopLoss, Is.EqualTo(strategy2.Order.EnableTrailingStopLoss));
                Assert.That(strategy1.Order.EnableTrailingStopLoss, Is.EqualTo(strategy3.Order.EnableTrailingStopLoss));
            });
        }

        [Test]
        [Description("Validates TimeInForce and OrderType can be chained in any order")]
        public void TimeInForceAndOrderType_ChainedDifferentOrders_ProduceSameResult()
        {
            // Order 1: TimeInForce -> OrderType
            var strategy1 = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .TimeInForce(TIF.IOC)
                .OrderType(OrderType.Limit)
                .Build();

            // Order 2: OrderType -> TimeInForce
            var strategy2 = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .OrderType(OrderType.Limit)
                .TimeInForce(TIF.IOC)
                .Build();

            Assert.Multiple(() =>
            {
                Assert.That(strategy1.Order.TimeInForce, Is.EqualTo(strategy2.Order.TimeInForce));
                Assert.That(strategy1.Order.Type, Is.EqualTo(strategy2.Order.Type));
            });
        }
    }

    #endregion

    #region OrderType Parameter in Buy/Sell Tests

    /// <summary>
    /// Tests the OrderType parameter added to Buy() and Sell() methods.
    /// </summary>
    [TestFixture]
    public class OrderTypeParameterTests
    {
        [Test]
        [Description("Validates Buy with explicit Market OrderType parameter")]
        public void Buy_WithMarketOrderTypeParameter_SetsCorrectly()
        {
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current, OrderType.Market)
                .Build();

            Assert.That(strategy.Order.Type, Is.EqualTo(OrderType.Market));
        }

        [Test]
        [Description("Validates Buy with explicit Limit OrderType parameter")]
        public void Buy_WithLimitOrderTypeParameter_SetsCorrectly()
        {
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current, OrderType.Limit)
                .Build();

            Assert.That(strategy.Order.Type, Is.EqualTo(OrderType.Limit));
        }

        [Test]
        [Description("Validates Sell with explicit Market OrderType parameter")]
        public void Sell_WithMarketOrderTypeParameter_SetsCorrectly()
        {
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Sell(100, Price.Current, OrderType.Market)
                .Build();

            Assert.That(strategy.Order.Type, Is.EqualTo(OrderType.Market));
        }

        [Test]
        [Description("Validates Sell with explicit Limit OrderType parameter")]
        public void Sell_WithLimitOrderTypeParameter_SetsCorrectly()
        {
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Sell(100, Price.Current, OrderType.Limit)
                .Build();

            Assert.That(strategy.Order.Type, Is.EqualTo(OrderType.Limit));
        }

        [Test]
        [Description("Validates Buy OrderType parameter can be overridden by builder method")]
        public void Buy_OrderTypeParameterOverriddenByMethod_UsesMethodValue()
        {
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current, OrderType.Market)
                .OrderType(OrderType.Limit)
                .Build();

            Assert.That(strategy.Order.Type, Is.EqualTo(OrderType.Limit));
        }

        [Test]
        [Description("Validates all combinations of Side × OrderType parameter")]
        [TestCase(true, OrderType.Market, OrderType.Market)]
        [TestCase(true, OrderType.Limit, OrderType.Limit)]
        [TestCase(false, OrderType.Market, OrderType.Market)]
        [TestCase(false, OrderType.Limit, OrderType.Limit)]
        public void SideAndOrderTypeParameter_AllCombinations_SetCorrectly(bool isBuy, OrderType inputType, OrderType expectedType)
        {
            var builder = Stock.Ticker("TEST").Breakout(100);
            var strategy = isBuy
                ? builder.Buy(100, Price.Current, inputType).Build()
                : builder.Sell(100, Price.Current, inputType).Build();

            Assert.Multiple(() =>
            {
                Assert.That(strategy.Order.Side, Is.EqualTo(isBuy ? OrderSide.Buy : OrderSide.Sell));
                Assert.That(strategy.Order.Type, Is.EqualTo(expectedType));
            });
        }
    }

    #endregion

    #region Fluent API Order Enforcement Tests

    /// <summary>
    /// Tests that the fluent API enforces correct method ordering.
    /// Ensures nonsense orders cannot be produced by validating:
    /// 1. Conditions must come before Buy/Sell
    /// 2. At least one condition is required
    /// 3. Build/End must be called to produce a strategy
    /// </summary>
    [TestFixture]
    public class FluentApiOrderEnforcementTests
    {
        [Test]
        [Description("Validates that conditions must be added before Buy")]
        public void NoConditions_BeforeBuy_ThrowsOnBuild()
        {
            Assert.Throws<InvalidOperationException>(() =>
            {
                Stock.Ticker("TEST")
                    .Buy(100, Price.Current)
                    .Build();
            });
        }

        [Test]
        [Description("Validates that conditions must be added before Sell")]
        public void NoConditions_BeforeSell_ThrowsOnBuild()
        {
            Assert.Throws<InvalidOperationException>(() =>
            {
                Stock.Ticker("TEST")
                    .Sell(100, Price.Current)
                    .Build();
            });
        }

        [Test]
        [Description("Validates that End() also enforces conditions")]
        public void NoConditions_BeforeEnd_ThrowsOnEnd()
        {
            Assert.Throws<InvalidOperationException>(() =>
            {
                Stock.Ticker("TEST")
                    .Buy(100, Price.Current)
                    .End(new TimeOnly(7, 0));
            });
        }

        [Test]
        [Description("Validates Stock configuration methods return Stock for chaining")]
        public void StockConfigMethods_ReturnStock_ForChaining()
        {
            // This test ensures the type system enforces correct ordering
            // If any method doesn't return Stock, this won't compile
            var stock = Stock.Ticker("TEST")
                .Exchange("NASDAQ")
                .Currency("USD")
                .Enabled(true)
                .Start(new TimeOnly(3, 0))
                .Breakout(100)
                .Pullback(95)
                .AboveVwap()
                .BelowVwap()
                .PriceAbove(90)
                .PriceBelow(110)
                .When("Custom", (p, v) => true);

            // Verify it's still a Stock instance that can call Buy/Sell
            var strategy = stock.Buy(100, Price.Current).Build();
            Assert.That(strategy, Is.Not.Null);
        }

        [Test]
        [Description("Validates StrategyBuilder methods return StrategyBuilder for chaining")]
        public void StrategyBuilderMethods_ReturnStrategyBuilder_ForChaining()
        {
            var builder = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .TakeProfit(110)
                .StopLoss(90)
                .TrailingStopLoss(Percent.Ten)
                .ClosePosition(new TimeOnly(6, 50))
                .TimeInForce(TIF.GTC)
                .OutsideRTH(true, true)
                .OrderType(OrderType.Market);

            // Verify it's still a StrategyBuilder that can Build
            var strategy = builder.Build();
            Assert.That(strategy, Is.Not.Null);
        }

        [Test]
        [Description("Validates implicit conversion works correctly")]
        public void ImplicitConversion_StrategyBuilderToStrategy_Works()
        {
            TradingStrategy strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .TakeProfit(110);

            Assert.That(strategy, Is.Not.Null);
            Assert.That(strategy.Symbol, Is.EqualTo("TEST"));
        }

        [Test]
        [Description("Validates conditions are added in order and preserved")]
        public void Conditions_AddedInOrder_PreservedCorrectly()
        {
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Pullback(95)
                .AboveVwap()
                .PriceAbove(90)
                .Buy(100, Price.Current)
                .Build();

            Assert.Multiple(() =>
            {
                Assert.That(strategy.Conditions, Has.Count.EqualTo(4));
                Assert.That(strategy.Conditions[0], Is.InstanceOf<BreakoutCondition>());
                Assert.That(strategy.Conditions[1], Is.InstanceOf<PullbackCondition>());
                Assert.That(strategy.Conditions[2], Is.InstanceOf<AboveVwapCondition>());
                Assert.That(strategy.Conditions[3], Is.InstanceOf<PriceAboveCondition>());
            });
        }
    }

    #endregion

    #region Full Cartesian Product Tests

    /// <summary>
    /// Tests all combinations in a full cartesian product of major parameters.
    /// This ensures every valid combination produces a working strategy.
    /// </summary>
    [TestFixture]
    public class CartesianProductTests
    {
        private static readonly OrderSide[] AllOrderSides = [OrderSide.Buy, OrderSide.Sell];
        private static readonly OrderType[] AllOrderTypes = [OrderType.Market, OrderType.Limit];
        private static readonly TimeInForce[] AllTimeInForce = 
        [
            TimeInForce.Day,
            TimeInForce.GoodTillCancel,
            TimeInForce.ImmediateOrCancel,
            TimeInForce.FillOrKill
        ];
        private static readonly Price[] AllPriceTypes = [Price.Current, Price.VWAP, Price.Bid, Price.Ask];

        [Test]
        [Description("Tests all 64 combinations of Side × OrderType × TIF × Price")]
        public void AllBasePermutations_ProduceValidStrategies()
        {
            int successCount = 0;
            var failures = new List<string>();

            foreach (var side in AllOrderSides)
            {
                foreach (var orderType in AllOrderTypes)
                {
                    foreach (var tif in AllTimeInForce)
                    {
                        foreach (var price in AllPriceTypes)
                        {
                            try
                            {
                                var stock = Stock.Ticker("TEST").Breakout(100);
                                var builder = side == OrderSide.Buy
                                    ? stock.Buy(100, price, orderType == OrderType.Market ? OrderType.Market : OrderType.Limit)
                                    : stock.Sell(100, price, orderType == OrderType.Market ? OrderType.Market : OrderType.Limit);

                                var strategy = builder
                                    .TimeInForce(tif)
                                    .Build();

                                Assert.Multiple(() =>
                                {
                                    Assert.That(strategy.Order.Side, Is.EqualTo(side));
                                    Assert.That(strategy.Order.Type, Is.EqualTo(orderType));
                                    Assert.That(strategy.Order.TimeInForce, Is.EqualTo(tif));
                                    Assert.That(strategy.Order.PriceType, Is.EqualTo(price));
                                });

                                successCount++;
                            }
                            catch (Exception ex)
                            {
                                failures.Add($"{side}/{orderType}/{tif}/{price}: {ex.Message}");
                            }
                        }
                    }
                }
            }

            Assert.Multiple(() =>
            {
                Assert.That(successCount, Is.EqualTo(64), $"Expected 64 successful combinations. Failures: {string.Join("; ", failures)}");
                Assert.That(failures, Is.Empty);
            });
        }

        [Test]
        [Description("Tests all exit strategy combinations with Buy orders")]
        public void AllExitStrategyCombinations_Buy_ProduceValidStrategies()
        {
            // 8 combinations: TP (on/off) × SL (on/off) × TSL (on/off)
            var combinations = new[]
            {
                (tp: false, sl: false, tsl: false),
                (tp: true, sl: false, tsl: false),
                (tp: false, sl: true, tsl: false),
                (tp: true, sl: true, tsl: false),
                (tp: false, sl: false, tsl: true),
                (tp: true, sl: false, tsl: true),
                (tp: false, sl: true, tsl: true),
                (tp: true, sl: true, tsl: true),
            };

            foreach (var (tp, sl, tsl) in combinations)
            {
                var builder = Stock.Ticker("TEST")
                    .Breakout(100)
                    .Buy(100, Price.Current);

                if (tp) builder = builder.TakeProfit(110);
                if (sl) builder = builder.StopLoss(90);
                if (tsl) builder = builder.TrailingStopLoss(Percent.Ten);

                var strategy = builder.Build();

                Assert.Multiple(() =>
                {
                    Assert.That(strategy.Order.EnableTakeProfit, Is.EqualTo(tp), $"TP={tp}, SL={sl}, TSL={tsl}");
                    Assert.That(strategy.Order.EnableStopLoss, Is.EqualTo(sl), $"TP={tp}, SL={sl}, TSL={tsl}");
                    Assert.That(strategy.Order.EnableTrailingStopLoss, Is.EqualTo(tsl), $"TP={tp}, SL={sl}, TSL={tsl}");
                });
            }
        }

        [Test]
        [Description("Tests all OutsideRTH combinations")]
        public void AllOutsideRthCombinations_ProduceValidStrategies()
        {
            var combinations = new[]
            {
                (entry: false, tp: false),
                (entry: true, tp: false),
                (entry: false, tp: true),
                (entry: true, tp: true),
            };

            foreach (var (entry, tp) in combinations)
            {
                var strategy = Stock.Ticker("TEST")
                    .Breakout(100)
                    .Buy(100, Price.Current)
                    .OutsideRTH(entry, tp)
                    .Build();

                Assert.Multiple(() =>
                {
                    Assert.That(strategy.Order.OutsideRth, Is.EqualTo(entry), $"Entry={entry}, TP={tp}");
                    Assert.That(strategy.Order.TakeProfitOutsideRth, Is.EqualTo(tp), $"Entry={entry}, TP={tp}");
                });
            }
        }
    }

    #endregion

    #region Condition Type Permutation Tests

    /// <summary>
    /// Tests all permutations of condition types.
    /// </summary>
    [TestFixture]
    public class ConditionTypePermutationTests
    {
        [Test]
        [Description("Tests all single condition types")]
        public void SingleConditionTypes_AllValid()
        {
            var conditions = new Action<Stock>[]
            {
                s => s.Breakout(100),
                s => s.Pullback(95),
                s => s.AboveVwap(),
                s => s.AboveVwap(0.5),
                s => s.BelowVwap(),
                s => s.BelowVwap(0.5),
                s => s.PriceAbove(100),
                s => s.PriceBelow(100),
                s => s.When("Custom", (p, v) => true),
            };

            foreach (var addCondition in conditions)
            {
                var stock = Stock.Ticker("TEST");
                addCondition(stock);
                var strategy = stock.Buy(100, Price.Current).Build();

                Assert.That(strategy.Conditions, Has.Count.EqualTo(1));
            }
        }

        [Test]
        [Description("Tests all pairs of condition types")]
        public void ConditionPairs_AllValid()
        {
            var conditionAdders = new (string Name, Action<Stock> Add)[]
            {
                ("Breakout", s => s.Breakout(100)),
                ("Pullback", s => s.Pullback(95)),
                ("AboveVwap", s => s.AboveVwap()),
                ("BelowVwap", s => s.BelowVwap()),
                ("PriceAbove", s => s.PriceAbove(100)),
                ("PriceBelow", s => s.PriceBelow(100)),
            };

            foreach (var first in conditionAdders)
            {
                foreach (var second in conditionAdders)
                {
                    var stock = Stock.Ticker("TEST");
                    first.Add(stock);
                    second.Add(stock);
                    var strategy = stock.Buy(100, Price.Current).Build();

                    Assert.That(strategy.Conditions, Has.Count.EqualTo(2),
                        $"Failed for {first.Name} -> {second.Name}");
                }
            }
        }

        [Test]
        [Description("Tests condition with IStrategyCondition interface")]
        public void Condition_WithInterface_AddsCorrectly()
        {
            var customCondition = new BreakoutCondition(100);
            var strategy = Stock.Ticker("TEST")
                .Condition(customCondition)
                .Buy(100, Price.Current)
                .Build();

            Assert.That(strategy.Conditions[0], Is.SameAs(customCondition));
        }

        [Test]
        [Description("Validates Condition throws on null")]
        public void Condition_WithNull_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
            {
                Stock.Ticker("TEST").Condition(null!);
            });
        }
    }

    #endregion

    #region Builder Method Overwriting Tests

    /// <summary>
    /// Tests that calling builder methods multiple times overwrites previous values.
    /// </summary>
    [TestFixture]
    public class BuilderOverwriteTests
    {
        [Test]
        [Description("Validates TakeProfit can be overwritten")]
        public void TakeProfit_CalledTwice_UsesLastValue()
        {
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .TakeProfit(110)
                .TakeProfit(120)
                .Build();

            Assert.That(strategy.Order.TakeProfitPrice, Is.EqualTo(120));
        }

        [Test]
        [Description("Validates StopLoss can be overwritten")]
        public void StopLoss_CalledTwice_UsesLastValue()
        {
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .StopLoss(90)
                .StopLoss(85)
                .Build();

            Assert.That(strategy.Order.StopLossPrice, Is.EqualTo(85));
        }

        [Test]
        [Description("Validates TrailingStopLoss can be overwritten")]
        public void TrailingStopLoss_CalledTwice_UsesLastValue()
        {
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .TrailingStopLoss(Percent.Ten)
                .TrailingStopLoss(Percent.Twenty)
                .Build();

            Assert.That(strategy.Order.TrailingStopLossPercent, Is.EqualTo(0.20));
        }

        [Test]
        [Description("Validates TimeInForce can be overwritten")]
        public void TimeInForce_CalledTwice_UsesLastValue()
        {
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .TimeInForce(TIF.Day)
                .TimeInForce(TIF.GTC)
                .Build();

            Assert.That(strategy.Order.TimeInForce, Is.EqualTo(TimeInForce.GoodTillCancel));
        }

        [Test]
        [Description("Validates OrderType can be overwritten")]
        public void OrderType_CalledTwice_UsesLastValue()
        {
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .OrderType(OrderType.Market)
                .OrderType(OrderType.Limit)
                .Build();

            Assert.That(strategy.Order.Type, Is.EqualTo(OrderType.Limit));
        }

        [Test]
        [Description("Validates OutsideRTH can be overwritten")]
        public void OutsideRTH_CalledTwice_UsesLastValue()
        {
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .OutsideRTH(false, false)
                .OutsideRTH(true, true)
                .Build();

            Assert.Multiple(() =>
            {
                Assert.That(strategy.Order.OutsideRth, Is.True);
                Assert.That(strategy.Order.TakeProfitOutsideRth, Is.True);
            });
        }

        [Test]
        [Description("Validates ClosePosition can be overwritten")]
        public void ClosePosition_CalledTwice_UsesLastValue()
        {
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .ClosePosition(new TimeOnly(6, 0))
                .ClosePosition(new TimeOnly(6, 50))
                .Build();

            Assert.That(strategy.Order.ClosePositionTime, Is.EqualTo(new TimeOnly(6, 50)));
        }

        [Test]
        [Description("Validates Stock.Exchange can be overwritten")]
        public void Exchange_CalledTwice_UsesLastValue()
        {
            var strategy = Stock.Ticker("TEST")
                .Exchange("NASDAQ")
                .Exchange("NYSE")
                .Breakout(100)
                .Buy(100, Price.Current)
                .Build();

            Assert.That(strategy.Exchange, Is.EqualTo("NYSE"));
        }

        [Test]
        [Description("Validates Stock.Currency can be overwritten")]
        public void Currency_CalledTwice_UsesLastValue()
        {
            var strategy = Stock.Ticker("TEST")
                .Currency("EUR")
                .Currency("GBP")
                .Breakout(100)
                .Buy(100, Price.Current)
                .Build();

            Assert.That(strategy.Currency, Is.EqualTo("GBP"));
        }

        [Test]
        [Description("Validates Stock.Enabled can be overwritten")]
        public void Enabled_CalledTwice_UsesLastValue()
        {
            var strategy = Stock.Ticker("TEST")
                .Enabled(false)
                .Enabled(true)
                .Breakout(100)
                .Buy(100, Price.Current)
                .Build();

            Assert.That(strategy.Enabled, Is.True);
        }

        [Test]
        [Description("Validates Stock.Start can be overwritten")]
        public void Start_CalledTwice_UsesLastValue()
        {
            var strategy = Stock.Ticker("TEST")
                .Start(new TimeOnly(3, 0))
                .Start(new TimeOnly(4, 0))
                .Breakout(100)
                .Buy(100, Price.Current)
                .Build();

            Assert.That(strategy.StartTime, Is.EqualTo(new TimeOnly(4, 0)));
        }
    }

    #endregion

    #region Edge Case and Boundary Tests

    /// <summary>
    /// Tests edge cases and boundary conditions.
    /// </summary>
    [TestFixture]
    public class EdgeCaseTests
    {
        [Test]
        [Description("Validates minimum viable strategy")]
        public void MinimumViableStrategy_OnlyRequiredElements()
        {
            var strategy = Stock.Ticker("X")
                .Breakout(1)
                .Buy(1, Price.Current)
                .Build();

            Assert.Multiple(() =>
            {
                Assert.That(strategy.Symbol, Is.EqualTo("X"));
                Assert.That(strategy.Conditions, Has.Count.EqualTo(1));
                Assert.That(strategy.Order.Quantity, Is.EqualTo(1));
            });
        }

        [Test]
        [Description("Validates strategy with many conditions")]
        public void ManyConditions_AllPreserved()
        {
            var stock = Stock.Ticker("TEST");
            for (int i = 0; i < 100; i++)
            {
                stock.Breakout(100 + i);
            }

            var strategy = stock.Buy(100, Price.Current).Build();

            Assert.That(strategy.Conditions, Has.Count.EqualTo(100));
        }

        [Test]
        [Description("Validates very large price values")]
        public void LargePriceValues_Handled()
        {
            var strategy = Stock.Ticker("BRK.A")
                .Breakout(500000)
                .Buy(1, Price.Current)
                .TakeProfit(550000)
                .StopLoss(450000)
                .Build();

            Assert.Multiple(() =>
            {
                Assert.That(strategy.Order.TakeProfitPrice, Is.EqualTo(550000));
                Assert.That(strategy.Order.StopLossPrice, Is.EqualTo(450000));
            });
        }

        [Test]
        [Description("Validates very small price values (penny stocks)")]
        public void SmallPriceValues_Handled()
        {
            var strategy = Stock.Ticker("PENNY")
                .Breakout(0.01)
                .Buy(10000, Price.Current)
                .TakeProfit(0.02)
                .StopLoss(0.005)
                .Build();

            Assert.Multiple(() =>
            {
                Assert.That(strategy.Order.TakeProfitPrice, Is.EqualTo(0.02));
                Assert.That(strategy.Order.StopLossPrice, Is.EqualTo(0.005));
            });
        }

        [Test]
        [Description("Validates decimal precision is maintained")]
        public void DecimalPrecision_Maintained()
        {
            var strategy = Stock.Ticker("TEST")
                .Breakout(100.123456)
                .Buy(100, Price.Current)
                .TakeProfit(110.987654)
                .StopLoss(90.111111)
                .TrailingStopLoss(0.0999)
                .Build();

            Assert.Multiple(() =>
            {
                Assert.That(strategy.Order.TakeProfitPrice, Is.EqualTo(110.987654).Within(0.000001));
                Assert.That(strategy.Order.StopLossPrice, Is.EqualTo(90.111111).Within(0.000001));
                Assert.That(strategy.Order.TrailingStopLossPercent, Is.EqualTo(0.0999).Within(0.0001));
            });
        }

        [Test]
        [Description("Validates empty string symbol is allowed")]
        public void EmptySymbol_IsAllowed()
        {
            var strategy = Stock.Ticker("")
                .Breakout(100)
                .Buy(100, Price.Current)
                .Build();

            Assert.That(strategy.Symbol, Is.EqualTo(""));
        }

        [Test]
        [Description("Validates whitespace symbol is allowed")]
        public void WhitespaceSymbol_IsAllowed()
        {
            var strategy = Stock.Ticker("   ")
                .Breakout(100)
                .Buy(100, Price.Current)
                .Build();

            Assert.That(strategy.Symbol, Is.EqualTo("   "));
        }

        [Test]
        [Description("Validates special characters in symbol")]
        public void SpecialCharacterSymbol_IsAllowed()
        {
            var strategy = Stock.Ticker("BRK.B")
                .Breakout(100)
                .Buy(100, Price.Current)
                .Build();

            Assert.That(strategy.Symbol, Is.EqualTo("BRK.B"));
        }

        [Test]
        [Description("Validates time at midnight boundary")]
        public void MidnightTime_Handled()
        {
            var strategy = Stock.Ticker("TEST")
                .Start(new TimeOnly(0, 0))
                .Breakout(100)
                .Buy(100, Price.Current)
                .ClosePosition(new TimeOnly(0, 0))
                .End(new TimeOnly(23, 59, 59));

            Assert.Multiple(() =>
            {
                Assert.That(strategy.StartTime, Is.EqualTo(new TimeOnly(0, 0)));
                Assert.That(strategy.Order.ClosePositionTime, Is.EqualTo(new TimeOnly(0, 0)));
                Assert.That(strategy.EndTime, Is.EqualTo(new TimeOnly(23, 59, 59)));
            });
        }
    }

    #endregion

    #region Strategy Immutability Tests

    /// <summary>
    /// Tests that built strategies are immutable.
    /// </summary>
    [TestFixture]
    public class StrategyImmutabilityTests
    {
        [Test]
        [Description("Validates built strategy conditions are read-only")]
        public void BuiltStrategy_Conditions_AreReadOnly()
        {
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .Build();

            Assert.That(strategy.Conditions, Is.InstanceOf<IReadOnlyList<IStrategyCondition>>());
        }

        [Test]
        [Description("Validates multiple builds from same Stock produce independent strategies")]
        public void MultipleBuildsSameStock_ProduceIndependentStrategies()
        {
            var stock = Stock.Ticker("TEST").Breakout(100);

            var strategy1 = stock.Buy(100, Price.Current).TakeProfit(110).Build();
            var strategy2 = stock.Buy(200, Price.VWAP).TakeProfit(120).Build();

            Assert.Multiple(() =>
            {
                // Verify they have different configurations
                Assert.That(strategy1.Order.Quantity, Is.EqualTo(100));
                Assert.That(strategy2.Order.Quantity, Is.EqualTo(200));
                Assert.That(strategy1.Order.TakeProfitPrice, Is.EqualTo(110));
                Assert.That(strategy2.Order.TakeProfitPrice, Is.EqualTo(120));
            });
        }
    }

    #endregion
}
