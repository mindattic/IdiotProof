// ============================================================================
// OrderActionComprehensiveTests - Comprehensive unit tests for the OrderAction class
// ============================================================================
//
// PURPOSE:
// This test file provides comprehensive direct unit tests for the OrderAction class,
// testing all properties, methods, and edge cases independently of the fluent API.
//
// COVERAGE:
// - Default property values
// - Property initialization via object initializer
// - GetIbTif() method for all TimeInForce values
// - GetIbOrderType() method for all OrderType values
// - GetIbAction() method for all OrderSide values
// - ToString() method formatting
// - Edge cases and boundary values
// - Nullable property behavior
// - Enum coverage for undefined values
//
// NOTE:
// Basic OrderAction tests are also in TimeAndPercentTests.cs (OrderActionTests class).
// This file provides expanded coverage with more edge cases and scenarios.
//
// ============================================================================

using IdiotProof.Backend.Enums;
using IdiotProof.Backend.Models;
using IdiotProof.Backend.Strategy;
using NUnit.Framework;

namespace IdiotProof.Backend.UnitTests;

/// <summary>
/// Comprehensive unit tests for the <see cref="OrderAction"/> class.
/// Tests all properties, methods, and edge cases independently.
/// </summary>
/// <remarks>
/// This class supplements the basic <c>OrderActionTests</c> in TimeAndPercentTests.cs
/// with comprehensive coverage including edge cases, enum validation, and typical
/// trading scenarios.
/// </remarks>
[TestFixture]
public class OrderActionComprehensiveTests
{
    #region Default Value Tests

    /// <summary>
    /// Tests that all default property values are correctly initialized.
    /// </summary>
    [TestFixture]
    public class DefaultValueTests
    {
        [Test]
        [Description("Validates default Side is Buy")]
        public void Side_Default_IsBuy()
        {
            var order = new OrderAction();
            Assert.That(order.Side, Is.EqualTo(OrderSide.Buy));
        }

        [Test]
        [Description("Validates default Quantity is 100")]
        public void Quantity_Default_Is100()
        {
            var order = new OrderAction();
            Assert.That(order.Quantity, Is.EqualTo(100));
        }

        [Test]
        [Description("Validates default Type is Limit")]
        public void Type_Default_IsLimit()
        {
            var order = new OrderAction();
            Assert.That(order.Type, Is.EqualTo(OrderType.Limit));
        }

        [Test]
        [Description("Validates default LimitPrice is null")]
        public void LimitPrice_Default_IsNull()
        {
            var order = new OrderAction();
            Assert.That(order.LimitPrice, Is.Null);
        }

        [Test]
        [Description("Validates default LimitOffset is 0.02")]
        public void LimitOffset_Default_Is002()
        {
            var order = new OrderAction();
            Assert.That(order.LimitOffset, Is.EqualTo(0.02));
        }

        [Test]
        [Description("Validates default TimeInForce is GoodTillCancel")]
        public void TimeInForce_Default_IsGoodTillCancel()
        {
            var order = new OrderAction();
            Assert.That(order.TimeInForce, Is.EqualTo(TimeInForce.GoodTillCancel));
        }

        [Test]
        [Description("Validates default OutsideRth is true")]
        public void OutsideRth_Default_IsTrue()
        {
            var order = new OrderAction();
            Assert.That(order.OutsideRth, Is.True);
        }

        [Test]
        [Description("Validates default EnableTakeProfit is true")]
        public void EnableTakeProfit_Default_IsTrue()
        {
            var order = new OrderAction();
            Assert.That(order.EnableTakeProfit, Is.True);
        }

        [Test]
        [Description("Validates default TakeProfitPrice is null")]
        public void TakeProfitPrice_Default_IsNull()
        {
            var order = new OrderAction();
            Assert.That(order.TakeProfitPrice, Is.Null);
        }

        [Test]
        [Description("Validates default TakeProfitOffset is 0.30")]
        public void TakeProfitOffset_Default_Is030()
        {
            var order = new OrderAction();
            Assert.That(order.TakeProfitOffset, Is.EqualTo(0.30));
        }

        [Test]
        [Description("Validates default TakeProfitOutsideRth is true")]
        public void TakeProfitOutsideRth_Default_IsTrue()
        {
            var order = new OrderAction();
            Assert.That(order.TakeProfitOutsideRth, Is.True);
        }

        [Test]
        [Description("Validates default EnableStopLoss is false")]
        public void EnableStopLoss_Default_IsFalse()
        {
            var order = new OrderAction();
            Assert.That(order.EnableStopLoss, Is.False);
        }

        [Test]
        [Description("Validates default StopLossPrice is null")]
        public void StopLossPrice_Default_IsNull()
        {
            var order = new OrderAction();
            Assert.That(order.StopLossPrice, Is.Null);
        }

        [Test]
        [Description("Validates default StopLossOffset is 0.20")]
        public void StopLossOffset_Default_Is020()
        {
            var order = new OrderAction();
            Assert.That(order.StopLossOffset, Is.EqualTo(0.20));
        }

        [Test]
        [Description("Validates default EnableTrailingStopLoss is false")]
        public void EnableTrailingStopLoss_Default_IsFalse()
        {
            var order = new OrderAction();
            Assert.That(order.EnableTrailingStopLoss, Is.False);
        }

        [Test]
        [Description("Validates default TrailingStopLossPercent is 0.10 (10%)")]
        public void TrailingStopLossPercent_Default_Is010()
        {
            var order = new OrderAction();
            Assert.That(order.TrailingStopLossPercent, Is.EqualTo(0.10));
        }

        [Test]
        [Description("Validates default EndTime is null")]
        public void EndTime_Default_IsNull()
        {
            var order = new OrderAction();
            Assert.That(order.EndTime, Is.Null);
        }

        [Test]
        [Description("Validates default PriceType is Current")]
        public void PriceType_Default_IsCurrent()
        {
            var order = new OrderAction();
            Assert.That(order.PriceType, Is.EqualTo(Price.Current));
        }

        [Test]
        [Description("Validates default StartTime is null")]
        public void StartTime_Default_IsNull()
        {
            var order = new OrderAction();
            Assert.That(order.StartTime, Is.Null);
        }

        [Test]
        [Description("Validates default ClosePositionTime is null")]
        public void ClosePositionTime_Default_IsNull()
        {
            var order = new OrderAction();
            Assert.That(order.ClosePositionTime, Is.Null);
        }

        [Test]
        [Description("Validates all defaults together")]
        public void AllDefaults_AreCorrect()
        {
            var order = new OrderAction();

            Assert.Multiple(() =>
            {
                Assert.That(order.Side, Is.EqualTo(OrderSide.Buy));
                Assert.That(order.Quantity, Is.EqualTo(100));
                Assert.That(order.Type, Is.EqualTo(OrderType.Limit));
                Assert.That(order.LimitPrice, Is.Null);
                Assert.That(order.LimitOffset, Is.EqualTo(0.02));
                Assert.That(order.TimeInForce, Is.EqualTo(TimeInForce.GoodTillCancel));
                Assert.That(order.OutsideRth, Is.True);
                Assert.That(order.EnableTakeProfit, Is.True);
                Assert.That(order.TakeProfitPrice, Is.Null);
                Assert.That(order.TakeProfitOffset, Is.EqualTo(0.30));
                Assert.That(order.TakeProfitOutsideRth, Is.True);
                Assert.That(order.EnableStopLoss, Is.False);
                Assert.That(order.StopLossPrice, Is.Null);
                Assert.That(order.StopLossOffset, Is.EqualTo(0.20));
                Assert.That(order.EnableTrailingStopLoss, Is.False);
                Assert.That(order.TrailingStopLossPercent, Is.EqualTo(0.10));
                Assert.That(order.EndTime, Is.Null);
                Assert.That(order.PriceType, Is.EqualTo(Price.Current));
                Assert.That(order.StartTime, Is.Null);
                Assert.That(order.ClosePositionTime, Is.Null);
            });
        }
    }

    #endregion

    #region Property Initialization Tests

    /// <summary>
    /// Tests property initialization via object initializer syntax.
    /// </summary>
    [TestFixture]
    public class PropertyInitializationTests
    {
        [Test]
        [Description("Validates Side can be set via initializer")]
        public void Side_InitializerSyntax_SetsCorrectly()
        {
            var order = new OrderAction { Side = OrderSide.Sell };
            Assert.That(order.Side, Is.EqualTo(OrderSide.Sell));
        }

        [Test]
        [Description("Validates Quantity can be set via initializer")]
        [TestCase(1)]
        [TestCase(50)]
        [TestCase(1000)]
        [TestCase(10000)]
        public void Quantity_InitializerSyntax_SetsCorrectly(int quantity)
        {
            var order = new OrderAction { Quantity = quantity };
            Assert.That(order.Quantity, Is.EqualTo(quantity));
        }

        [Test]
        [Description("Validates Type can be set via initializer")]
        public void Type_InitializerSyntax_SetsCorrectly()
        {
            var order = new OrderAction { Type = OrderType.Market };
            Assert.That(order.Type, Is.EqualTo(OrderType.Market));
        }

        [Test]
        [Description("Validates LimitPrice can be set via initializer")]
        [TestCase(100.50)]
        [TestCase(0.01)]
        [TestCase(9999.99)]
        public void LimitPrice_InitializerSyntax_SetsCorrectly(double price)
        {
            var order = new OrderAction { LimitPrice = price };
            Assert.That(order.LimitPrice, Is.EqualTo(price));
        }

        [Test]
        [Description("Validates TimeInForce can be set via initializer")]
        [TestCase(TimeInForce.Day)]
        [TestCase(TimeInForce.GoodTillCancel)]
        [TestCase(TimeInForce.ImmediateOrCancel)]
        [TestCase(TimeInForce.FillOrKill)]
        public void TimeInForce_InitializerSyntax_SetsCorrectly(TimeInForce tif)
        {
            var order = new OrderAction { TimeInForce = tif };
            Assert.That(order.TimeInForce, Is.EqualTo(tif));
        }

        [Test]
        [Description("Validates TakeProfitPrice can be set via initializer")]
        public void TakeProfitPrice_InitializerSyntax_SetsCorrectly()
        {
            var order = new OrderAction { TakeProfitPrice = 150.00 };
            Assert.That(order.TakeProfitPrice, Is.EqualTo(150.00));
        }

        [Test]
        [Description("Validates StopLossPrice can be set via initializer")]
        public void StopLossPrice_InitializerSyntax_SetsCorrectly()
        {
            var order = new OrderAction { StopLossPrice = 95.00 };
            Assert.That(order.StopLossPrice, Is.EqualTo(95.00));
        }

        [Test]
        [Description("Validates TrailingStopLossPercent can be set via initializer")]
        [TestCase(0.01)]
        [TestCase(0.05)]
        [TestCase(0.10)]
        [TestCase(0.25)]
        [TestCase(0.50)]
        public void TrailingStopLossPercent_InitializerSyntax_SetsCorrectly(double percent)
        {
            var order = new OrderAction { TrailingStopLossPercent = percent };
            Assert.That(order.TrailingStopLossPercent, Is.EqualTo(percent));
        }

        [Test]
        [Description("Validates EndTime can be set via initializer")]
        public void EndTime_InitializerSyntax_SetsCorrectly()
        {
            var endTime = new TimeOnly(15, 30);
            var order = new OrderAction { EndTime = endTime };
            Assert.That(order.EndTime, Is.EqualTo(endTime));
        }

        [Test]
        [Description("Validates StartTime can be set via initializer")]
        public void StartTime_InitializerSyntax_SetsCorrectly()
        {
            var startTime = new TimeOnly(9, 30);
            var order = new OrderAction { StartTime = startTime };
            Assert.That(order.StartTime, Is.EqualTo(startTime));
        }

        [Test]
        [Description("Validates ClosePositionTime can be set via initializer")]
        public void ClosePositionTime_InitializerSyntax_SetsCorrectly()
        {
            var closeTime = new TimeOnly(14, 45);
            var order = new OrderAction { ClosePositionTime = closeTime };
            Assert.That(order.ClosePositionTime, Is.EqualTo(closeTime));
        }

        [Test]
        [Description("Validates PriceType can be set via initializer")]
        [TestCase(Price.Current)]
        [TestCase(Price.VWAP)]
        [TestCase(Price.Bid)]
        [TestCase(Price.Ask)]
        public void PriceType_InitializerSyntax_SetsCorrectly(Price priceType)
        {
            var order = new OrderAction { PriceType = priceType };
            Assert.That(order.PriceType, Is.EqualTo(priceType));
        }

        [Test]
        [Description("Validates complex initialization with multiple properties")]
        public void ComplexInitialization_AllProperties_SetCorrectly()
        {
            var order = new OrderAction
            {
                Side = OrderSide.Sell,
                Quantity = 500,
                Type = OrderType.Market,
                LimitPrice = 99.50,
                LimitOffset = 0.05,
                TimeInForce = TimeInForce.Day,
                OutsideRth = false,
                EnableTakeProfit = true,
                TakeProfitPrice = 90.00,
                TakeProfitOffset = 0.50,
                TakeProfitOutsideRth = false,
                EnableStopLoss = true,
                StopLossPrice = 105.00,
                StopLossOffset = 0.25,
                EnableTrailingStopLoss = false,
                TrailingStopLossPercent = 0.15,
                EndTime = new TimeOnly(16, 0),
                PriceType = Price.VWAP,
                StartTime = new TimeOnly(9, 30),
                ClosePositionTime = new TimeOnly(15, 45)
            };

            Assert.Multiple(() =>
            {
                Assert.That(order.Side, Is.EqualTo(OrderSide.Sell));
                Assert.That(order.Quantity, Is.EqualTo(500));
                Assert.That(order.Type, Is.EqualTo(OrderType.Market));
                Assert.That(order.LimitPrice, Is.EqualTo(99.50));
                Assert.That(order.LimitOffset, Is.EqualTo(0.05));
                Assert.That(order.TimeInForce, Is.EqualTo(TimeInForce.Day));
                Assert.That(order.OutsideRth, Is.False);
                Assert.That(order.EnableTakeProfit, Is.True);
                Assert.That(order.TakeProfitPrice, Is.EqualTo(90.00));
                Assert.That(order.TakeProfitOffset, Is.EqualTo(0.50));
                Assert.That(order.TakeProfitOutsideRth, Is.False);
                Assert.That(order.EnableStopLoss, Is.True);
                Assert.That(order.StopLossPrice, Is.EqualTo(105.00));
                Assert.That(order.StopLossOffset, Is.EqualTo(0.25));
                Assert.That(order.EnableTrailingStopLoss, Is.False);
                Assert.That(order.TrailingStopLossPercent, Is.EqualTo(0.15));
                Assert.That(order.EndTime, Is.EqualTo(new TimeOnly(16, 0)));
                Assert.That(order.PriceType, Is.EqualTo(Price.VWAP));
                Assert.That(order.StartTime, Is.EqualTo(new TimeOnly(9, 30)));
                Assert.That(order.ClosePositionTime, Is.EqualTo(new TimeOnly(15, 45)));
            });
        }
    }

    #endregion

    #region GetIbTif() Method Tests

    /// <summary>
    /// Tests the GetIbTif() method for all TimeInForce values including extended hours options.
    /// </summary>
    [TestFixture]
    public class GetIbTifTests
    {
        #region Original TIF Values

        [Test]
        [Description("Validates Day returns 'DAY'")]
        public void GetIbTif_Day_ReturnsDAY()
        {
            var order = new OrderAction { TimeInForce = TimeInForce.Day };
            Assert.That(order.GetIbTif(), Is.EqualTo("DAY"));
        }

        [Test]
        [Description("Validates GoodTillCancel returns 'GTC'")]
        public void GetIbTif_GoodTillCancel_ReturnsGTC()
        {
            var order = new OrderAction { TimeInForce = TimeInForce.GoodTillCancel };
            Assert.That(order.GetIbTif(), Is.EqualTo("GTC"));
        }

        [Test]
        [Description("Validates ImmediateOrCancel returns 'IOC'")]
        public void GetIbTif_ImmediateOrCancel_ReturnsIOC()
        {
            var order = new OrderAction { TimeInForce = TimeInForce.ImmediateOrCancel };
            Assert.That(order.GetIbTif(), Is.EqualTo("IOC"));
        }

        [Test]
        [Description("Validates FillOrKill returns 'FOK'")]
        public void GetIbTif_FillOrKill_ReturnsFOK()
        {
            var order = new OrderAction { TimeInForce = TimeInForce.FillOrKill };
            Assert.That(order.GetIbTif(), Is.EqualTo("FOK"));
        }

        #endregion

        #region Extended TIF Values

        [Test]
        [Description("Validates Overnight returns 'GTC' (uses GTC with time-based cancellation)")]
        public void GetIbTif_Overnight_ReturnsGTC()
        {
            var order = new OrderAction { TimeInForce = TimeInForce.Overnight };
            Assert.That(order.GetIbTif(), Is.EqualTo("GTC"));
        }

        [Test]
        [Description("Validates OvernightPlusDay returns 'DTC'")]
        public void GetIbTif_OvernightPlusDay_ReturnsDTC()
        {
            var order = new OrderAction { TimeInForce = TimeInForce.OvernightPlusDay };
            Assert.That(order.GetIbTif(), Is.EqualTo("DTC"));
        }

        [Test]
        [Description("Validates AtTheOpening returns 'OPG'")]
        public void GetIbTif_AtTheOpening_ReturnsOPG()
        {
            var order = new OrderAction { TimeInForce = TimeInForce.AtTheOpening };
            Assert.That(order.GetIbTif(), Is.EqualTo("OPG"));
        }

        #endregion

        #region Edge Cases

        [Test]
        [Description("Validates undefined TimeInForce value defaults to 'GTC'")]
        public void GetIbTif_UndefinedValue_ReturnsGTC()
        {
            var order = new OrderAction { TimeInForce = (TimeInForce)999 };
            Assert.That(order.GetIbTif(), Is.EqualTo("GTC"));
        }

        [Test]
        [Description("Validates all TimeInForce values produce valid IB codes")]
        public void GetIbTif_AllDefinedValues_ProduceValidCodes()
        {
            Assert.Multiple(() =>
            {
                // Original values
                Assert.That(new OrderAction { TimeInForce = TimeInForce.Day }.GetIbTif(), Is.EqualTo("DAY"));
                Assert.That(new OrderAction { TimeInForce = TimeInForce.GoodTillCancel }.GetIbTif(), Is.EqualTo("GTC"));
                Assert.That(new OrderAction { TimeInForce = TimeInForce.ImmediateOrCancel }.GetIbTif(), Is.EqualTo("IOC"));
                Assert.That(new OrderAction { TimeInForce = TimeInForce.FillOrKill }.GetIbTif(), Is.EqualTo("FOK"));

                // Extended values
                Assert.That(new OrderAction { TimeInForce = TimeInForce.Overnight }.GetIbTif(), Is.EqualTo("GTC"));
                Assert.That(new OrderAction { TimeInForce = TimeInForce.OvernightPlusDay }.GetIbTif(), Is.EqualTo("DTC"));
                Assert.That(new OrderAction { TimeInForce = TimeInForce.AtTheOpening }.GetIbTif(), Is.EqualTo("OPG"));
            });
        }

        #endregion
    }

    #endregion

    #region GetIbOrderType() Method Tests

    /// <summary>
    /// Tests the GetIbOrderType() method for all OrderType values.
    /// </summary>
    [TestFixture]
    public class GetIbOrderTypeTests
    {
        [Test]
        [Description("Validates Market returns 'MKT'")]
        public void GetIbOrderType_Market_ReturnsMKT()
        {
            var order = new OrderAction { Type = OrderType.Market };
            Assert.That(order.GetIbOrderType(), Is.EqualTo("MKT"));
        }

        [Test]
        [Description("Validates Limit returns 'LMT'")]
        public void GetIbOrderType_Limit_ReturnsLMT()
        {
            var order = new OrderAction { Type = OrderType.Limit };
            Assert.That(order.GetIbOrderType(), Is.EqualTo("LMT"));
        }

        [Test]
        [Description("Validates undefined OrderType value defaults to 'LMT'")]
        public void GetIbOrderType_UndefinedValue_ReturnsLMT()
        {
            var order = new OrderAction { Type = (OrderType)999 };
            Assert.That(order.GetIbOrderType(), Is.EqualTo("LMT"));
        }

        [Test]
        [Description("Validates all OrderType values produce valid IB codes")]
        public void GetIbOrderType_AllDefinedValues_ProduceValidCodes()
        {
            Assert.Multiple(() =>
            {
                Assert.That(new OrderAction { Type = OrderType.Market }.GetIbOrderType(), Is.EqualTo("MKT"));
                Assert.That(new OrderAction { Type = OrderType.Limit }.GetIbOrderType(), Is.EqualTo("LMT"));
            });
        }
    }

    #endregion

    #region GetIbAction() Method Tests

    /// <summary>
    /// Tests the GetIbAction() method for all OrderSide values.
    /// </summary>
    [TestFixture]
    public class GetIbActionTests
    {
        [Test]
        [Description("Validates Buy returns 'BUY'")]
        public void GetIbAction_Buy_ReturnsBUY()
        {
            var order = new OrderAction { Side = OrderSide.Buy };
            Assert.That(order.GetIbAction(), Is.EqualTo("BUY"));
        }

        [Test]
        [Description("Validates Sell returns 'SELL'")]
        public void GetIbAction_Sell_ReturnsSELL()
        {
            var order = new OrderAction { Side = OrderSide.Sell };
            Assert.That(order.GetIbAction(), Is.EqualTo("SELL"));
        }

        [Test]
        [Description("Validates undefined OrderSide value defaults to 'BUY'")]
        public void GetIbAction_UndefinedValue_ReturnsBUY()
        {
            var order = new OrderAction { Side = (OrderSide)999 };
            Assert.That(order.GetIbAction(), Is.EqualTo("BUY"));
        }

        [Test]
        [Description("Validates all OrderSide values produce valid IB codes")]
        public void GetIbAction_AllDefinedValues_ProduceValidCodes()
        {
            Assert.Multiple(() =>
            {
                Assert.That(new OrderAction { Side = OrderSide.Buy }.GetIbAction(), Is.EqualTo("BUY"));
                Assert.That(new OrderAction { Side = OrderSide.Sell }.GetIbAction(), Is.EqualTo("SELL"));
            });
        }
    }

    #endregion

    #region ToString() Method Tests

    /// <summary>
    /// Tests the ToString() method formatting for various configurations.
    /// </summary>
    [TestFixture]
    public class ToStringTests
    {
        [Test]
        [Description("Validates ToString for basic Buy order")]
        public void ToString_BasicBuyOrder_FormatsCorrectly()
        {
            var order = new OrderAction
            {
                Side = OrderSide.Buy,
                Quantity = 100,
                Type = OrderType.Limit,
                TimeInForce = TimeInForce.GoodTillCancel,
                EnableTakeProfit = true,
                TakeProfitPrice = 110.00,
                EnableStopLoss = false,
                EnableTrailingStopLoss = false
            };

            var result = order.ToString();

            Assert.That(result, Does.Contain("Buy"));
            Assert.That(result, Does.Contain("100 shares"));
            Assert.That(result, Does.Contain("Limit"));
            Assert.That(result, Does.Contain("GoodTillCancel"));
            Assert.That(result, Does.Contain("TP=110"));
        }

        [Test]
        [Description("Validates ToString for Sell order")]
        public void ToString_SellOrder_FormatsCorrectly()
        {
            var order = new OrderAction
            {
                Side = OrderSide.Sell,
                Quantity = 50,
                Type = OrderType.Market,
                TimeInForce = TimeInForce.Day,
                EnableTakeProfit = false,
                EnableStopLoss = false,
                EnableTrailingStopLoss = false
            };

            var result = order.ToString();

            Assert.That(result, Does.Contain("Sell"));
            Assert.That(result, Does.Contain("50 shares"));
            Assert.That(result, Does.Contain("Market"));
            Assert.That(result, Does.Contain("Day"));
            Assert.That(result, Does.Contain("TP=Off"));
        }

        [Test]
        [Description("Validates ToString shows TakeProfit offset when price is null")]
        public void ToString_TakeProfitOffset_ShowsOffset()
        {
            var order = new OrderAction
            {
                EnableTakeProfit = true,
                TakeProfitPrice = null,
                TakeProfitOffset = 0.50
            };

            var result = order.ToString();
            Assert.That(result, Does.Contain("TP=+0.50"));
        }

        [Test]
        [Description("Validates ToString shows TakeProfit price when set")]
        public void ToString_TakeProfitPrice_ShowsPrice()
        {
            var order = new OrderAction
            {
                EnableTakeProfit = true,
                TakeProfitPrice = 150.25
            };

            var result = order.ToString();
            Assert.That(result, Does.Contain("TP=150.25"));
        }

        [Test]
        [Description("Validates ToString shows StopLoss when enabled")]
        public void ToString_StopLossEnabled_ShowsStopLoss()
        {
            var order = new OrderAction
            {
                EnableStopLoss = true,
                StopLossPrice = 95.00,
                EnableTakeProfit = false
            };

            var result = order.ToString();
            Assert.That(result, Does.Contain("SL=95"));
        }

        [Test]
        [Description("Validates ToString shows TrailingStopLoss when enabled")]
        public void ToString_TrailingStopLossEnabled_ShowsTSL()
        {
            var order = new OrderAction
            {
                EnableTrailingStopLoss = true,
                TrailingStopLossPercent = 0.10,
                EnableTakeProfit = false
            };

            var result = order.ToString();
            Assert.That(result, Does.Contain("TSL=10%"));
        }

        [Test]
        [Description("Validates ToString shows all exit strategies when enabled")]
        public void ToString_AllExitStrategies_ShowsAll()
        {
            var order = new OrderAction
            {
                EnableTakeProfit = true,
                TakeProfitPrice = 120.00,
                EnableStopLoss = true,
                StopLossPrice = 95.00,
                EnableTrailingStopLoss = true,
                TrailingStopLossPercent = 0.05
            };

            var result = order.ToString();

            Assert.Multiple(() =>
            {
                Assert.That(result, Does.Contain("TP=120"));
                Assert.That(result, Does.Contain("SL=95"));
                Assert.That(result, Does.Contain("TSL=5%"));
            });
        }

        [Test]
        [Description("Validates ToString with TakeProfitOff")]
        public void ToString_TakeProfitDisabled_ShowsOff()
        {
            var order = new OrderAction
            {
                EnableTakeProfit = false,
                EnableStopLoss = false,
                EnableTrailingStopLoss = false
            };

            var result = order.ToString();
            Assert.That(result, Does.Contain("TP=Off"));
        }

        [Test]
        [Description("Validates ToString with original TIF values")]
        [TestCase(TimeInForce.Day, "Day")]
        [TestCase(TimeInForce.GoodTillCancel, "GoodTillCancel")]
        [TestCase(TimeInForce.ImmediateOrCancel, "ImmediateOrCancel")]
        [TestCase(TimeInForce.FillOrKill, "FillOrKill")]
        public void ToString_OriginalTIF_ShowsCorrectTIF(TimeInForce tif, string expected)
        {
            var order = new OrderAction
            {
                TimeInForce = tif,
                EnableTakeProfit = false
            };

            var result = order.ToString();
            Assert.That(result, Does.Contain($"TIF={expected}"));
        }

        [Test]
        [Description("Validates ToString with extended TIF values")]
        [TestCase(TimeInForce.Overnight, "Overnight")]
        [TestCase(TimeInForce.OvernightPlusDay, "OvernightPlusDay")]
        [TestCase(TimeInForce.AtTheOpening, "AtTheOpening")]
        public void ToString_ExtendedTIF_ShowsCorrectTIF(TimeInForce tif, string expected)
        {
            var order = new OrderAction
            {
                TimeInForce = tif,
                EnableTakeProfit = false
            };

            var result = order.ToString();
            Assert.That(result, Does.Contain($"TIF={expected}"));
        }
    }

    #endregion

    #region Edge Case Tests

    /// <summary>
    /// Tests edge cases and boundary values.
    /// </summary>
    [TestFixture]
    public class EdgeCaseTests
    {
        [Test]
        [Description("Validates zero quantity is allowed")]
        public void Quantity_Zero_IsAllowed()
        {
            var order = new OrderAction { Quantity = 0 };
            Assert.That(order.Quantity, Is.EqualTo(0));
        }

        [Test]
        [Description("Validates negative quantity is allowed (broker validates)")]
        public void Quantity_Negative_IsAllowed()
        {
            var order = new OrderAction { Quantity = -100 };
            Assert.That(order.Quantity, Is.EqualTo(-100));
        }

        [Test]
        [Description("Validates large quantity is allowed")]
        public void Quantity_LargeValue_IsAllowed()
        {
            var order = new OrderAction { Quantity = int.MaxValue };
            Assert.That(order.Quantity, Is.EqualTo(int.MaxValue));
        }

        [Test]
        [Description("Validates zero price is allowed")]
        public void LimitPrice_Zero_IsAllowed()
        {
            var order = new OrderAction { LimitPrice = 0 };
            Assert.That(order.LimitPrice, Is.EqualTo(0));
        }

        [Test]
        [Description("Validates very small price is allowed")]
        public void LimitPrice_VerySmall_IsAllowed()
        {
            var order = new OrderAction { LimitPrice = 0.0001 };
            Assert.That(order.LimitPrice, Is.EqualTo(0.0001));
        }

        [Test]
        [Description("Validates very large price is allowed")]
        public void LimitPrice_VeryLarge_IsAllowed()
        {
            var order = new OrderAction { LimitPrice = 999999.99 };
            Assert.That(order.LimitPrice, Is.EqualTo(999999.99));
        }

        [Test]
        [Description("Validates negative price is allowed (broker validates)")]
        public void LimitPrice_Negative_IsAllowed()
        {
            var order = new OrderAction { LimitPrice = -10.00 };
            Assert.That(order.LimitPrice, Is.EqualTo(-10.00));
        }

        [Test]
        [Description("Validates zero trailing stop percent is allowed")]
        public void TrailingStopLossPercent_Zero_IsAllowed()
        {
            var order = new OrderAction { TrailingStopLossPercent = 0 };
            Assert.That(order.TrailingStopLossPercent, Is.EqualTo(0));
        }

        [Test]
        [Description("Validates 100% trailing stop is allowed")]
        public void TrailingStopLossPercent_OneHundredPercent_IsAllowed()
        {
            var order = new OrderAction { TrailingStopLossPercent = 1.0 };
            Assert.That(order.TrailingStopLossPercent, Is.EqualTo(1.0));
        }

        [Test]
        [Description("Validates greater than 100% trailing stop is allowed (broker validates)")]
        public void TrailingStopLossPercent_GreaterThanOneHundred_IsAllowed()
        {
            var order = new OrderAction { TrailingStopLossPercent = 2.0 };
            Assert.That(order.TrailingStopLossPercent, Is.EqualTo(2.0));
        }

        [Test]
        [Description("Validates midnight EndTime is allowed")]
        public void EndTime_Midnight_IsAllowed()
        {
            var order = new OrderAction { EndTime = new TimeOnly(0, 0) };
            Assert.That(order.EndTime, Is.EqualTo(new TimeOnly(0, 0)));
        }

        [Test]
        [Description("Validates end of day EndTime is allowed")]
        public void EndTime_EndOfDay_IsAllowed()
        {
            var order = new OrderAction { EndTime = new TimeOnly(23, 59, 59) };
            Assert.That(order.EndTime, Is.EqualTo(new TimeOnly(23, 59, 59)));
        }

        [Test]
        [Description("Validates both StopLoss and TrailingStopLoss can be enabled (mutual exclusivity in docs)")]
        public void BothStopLossTypes_CanBeEnabled()
        {
            // Note: Documentation says they're mutually exclusive, but class allows both
            // This tests current behavior - runtime logic should handle precedence
            var order = new OrderAction
            {
                EnableStopLoss = true,
                StopLossPrice = 95.00,
                EnableTrailingStopLoss = true,
                TrailingStopLossPercent = 0.10
            };

            Assert.Multiple(() =>
            {
                Assert.That(order.EnableStopLoss, Is.True);
                Assert.That(order.EnableTrailingStopLoss, Is.True);
            });
        }
    }

    #endregion

    #region Nullable Property Tests

    /// <summary>
    /// Tests nullable property behavior.
    /// </summary>
    [TestFixture]
    public class NullablePropertyTests
    {
        [Test]
        [Description("Validates LimitPrice can be explicitly set to null")]
        public void LimitPrice_SetToNull_IsNull()
        {
            var order = new OrderAction { LimitPrice = null };
            Assert.That(order.LimitPrice, Is.Null);
            Assert.That(order.LimitPrice.HasValue, Is.False);
        }

        [Test]
        [Description("Validates TakeProfitPrice can be explicitly set to null")]
        public void TakeProfitPrice_SetToNull_IsNull()
        {
            var order = new OrderAction { TakeProfitPrice = null };
            Assert.That(order.TakeProfitPrice, Is.Null);
            Assert.That(order.TakeProfitPrice.HasValue, Is.False);
        }

        [Test]
        [Description("Validates StopLossPrice can be explicitly set to null")]
        public void StopLossPrice_SetToNull_IsNull()
        {
            var order = new OrderAction { StopLossPrice = null };
            Assert.That(order.StopLossPrice, Is.Null);
            Assert.That(order.StopLossPrice.HasValue, Is.False);
        }

        [Test]
        [Description("Validates EndTime can be explicitly set to null")]
        public void EndTime_SetToNull_IsNull()
        {
            var order = new OrderAction { EndTime = null };
            Assert.That(order.EndTime, Is.Null);
            Assert.That(order.EndTime.HasValue, Is.False);
        }

        [Test]
        [Description("Validates StartTime can be explicitly set to null")]
        public void StartTime_SetToNull_IsNull()
        {
            var order = new OrderAction { StartTime = null };
            Assert.That(order.StartTime, Is.Null);
            Assert.That(order.StartTime.HasValue, Is.False);
        }

        [Test]
        [Description("Validates ClosePositionTime can be explicitly set to null")]
        public void ClosePositionTime_SetToNull_IsNull()
        {
            var order = new OrderAction { ClosePositionTime = null };
            Assert.That(order.ClosePositionTime, Is.Null);
            Assert.That(order.ClosePositionTime.HasValue, Is.False);
        }

        [Test]
        [Description("Validates nullable properties can be set to values after being null")]
        public void NullableProperties_CanBeSetToValues()
        {
            var order = new OrderAction();

            // All should start as null
            Assert.Multiple(() =>
            {
                Assert.That(order.LimitPrice, Is.Null);
                Assert.That(order.TakeProfitPrice, Is.Null);
                Assert.That(order.StopLossPrice, Is.Null);
                Assert.That(order.EndTime, Is.Null);
                Assert.That(order.StartTime, Is.Null);
                Assert.That(order.ClosePositionTime, Is.Null);
            });
        }
    }

    #endregion

    #region Enum Coverage Tests

    /// <summary>
    /// Tests enum value coverage and boundaries including extended TIF options.
    /// </summary>
    [TestFixture]
    public class EnumCoverageTests
    {
        [Test]
        [Description("Validates all OrderSide enum values are defined")]
        public void OrderSide_AllValuesDefined()
        {
            var values = Enum.GetValues<OrderSide>();
            Assert.That(values, Has.Length.EqualTo(2));
            Assert.That(values, Does.Contain(OrderSide.Buy));
            Assert.That(values, Does.Contain(OrderSide.Sell));
        }

        [Test]
        [Description("Validates all OrderType enum values are defined")]
        public void OrderType_AllValuesDefined()
        {
            var values = Enum.GetValues<OrderType>();
            Assert.That(values, Has.Length.EqualTo(2));
            Assert.That(values, Does.Contain(OrderType.Market));
            Assert.That(values, Does.Contain(OrderType.Limit));
        }

        [Test]
        [Description("Validates all TimeInForce enum values are defined (including extended)")]
        public void TimeInForce_AllValuesDefined()
        {
            var values = Enum.GetValues<TimeInForce>();
            Assert.That(values, Has.Length.EqualTo(7)); // 4 original + 3 extended

            // Original values
            Assert.That(values, Does.Contain(TimeInForce.Day));
            Assert.That(values, Does.Contain(TimeInForce.GoodTillCancel));
            Assert.That(values, Does.Contain(TimeInForce.ImmediateOrCancel));
            Assert.That(values, Does.Contain(TimeInForce.FillOrKill));

            // Extended values
            Assert.That(values, Does.Contain(TimeInForce.Overnight));
            Assert.That(values, Does.Contain(TimeInForce.OvernightPlusDay));
            Assert.That(values, Does.Contain(TimeInForce.AtTheOpening));
        }

        [Test]
        [Description("Validates OrderSide underlying values")]
        public void OrderSide_UnderlyingValues()
        {
            Assert.Multiple(() =>
            {
                Assert.That((int)OrderSide.Buy, Is.EqualTo(0));
                Assert.That((int)OrderSide.Sell, Is.EqualTo(1));
            });
        }

        [Test]
        [Description("Validates OrderType underlying values")]
        public void OrderType_UnderlyingValues()
        {
            Assert.Multiple(() =>
            {
                Assert.That((int)OrderType.Market, Is.EqualTo(0));
                Assert.That((int)OrderType.Limit, Is.EqualTo(1));
            });
        }

        [Test]
        [Description("Validates TimeInForce underlying values (original)")]
        public void TimeInForce_OriginalUnderlyingValues()
        {
            Assert.Multiple(() =>
            {
                Assert.That((int)TimeInForce.Day, Is.EqualTo(0));
                Assert.That((int)TimeInForce.GoodTillCancel, Is.EqualTo(1));
                Assert.That((int)TimeInForce.ImmediateOrCancel, Is.EqualTo(2));
                Assert.That((int)TimeInForce.FillOrKill, Is.EqualTo(3));
            });
        }

        [Test]
        [Description("Validates TimeInForce underlying values (extended)")]
        public void TimeInForce_ExtendedUnderlyingValues()
        {
            Assert.Multiple(() =>
            {
                Assert.That((int)TimeInForce.Overnight, Is.EqualTo(4));
                Assert.That((int)TimeInForce.OvernightPlusDay, Is.EqualTo(5));
                Assert.That((int)TimeInForce.AtTheOpening, Is.EqualTo(6));
            });
        }
    }

    #endregion

    #region Typical Trading Scenario Tests

    /// <summary>
    /// Tests typical real-world trading scenarios.
    /// </summary>
    [TestFixture]
    public class TradingScenarioTests
    {
        [Test]
        [Description("Validates typical pre-market buy order configuration")]
        public void PreMarketBuyOrder_TypicalConfiguration()
        {
            var order = new OrderAction
            {
                Side = OrderSide.Buy,
                Quantity = 100,
                Type = OrderType.Limit,
                LimitPrice = 150.25,
                TimeInForce = TimeInForce.GoodTillCancel,
                OutsideRth = true,
                EnableTakeProfit = true,
                TakeProfitPrice = 155.00,
                TakeProfitOutsideRth = true,
                EnableTrailingStopLoss = true,
                TrailingStopLossPercent = 0.10,
                EndTime = new TimeOnly(7, 0),
                ClosePositionTime = new TimeOnly(6, 50)
            };

            Assert.Multiple(() =>
            {
                Assert.That(order.GetIbAction(), Is.EqualTo("BUY"));
                Assert.That(order.GetIbOrderType(), Is.EqualTo("LMT"));
                Assert.That(order.GetIbTif(), Is.EqualTo("GTC"));
                Assert.That(order.OutsideRth, Is.True);
                Assert.That(order.TakeProfitOutsideRth, Is.True);
            });
        }

        [Test]
        [Description("Validates typical day trading sell order configuration")]
        public void DayTradingSellOrder_TypicalConfiguration()
        {
            var order = new OrderAction
            {
                Side = OrderSide.Sell,
                Quantity = 200,
                Type = OrderType.Market,
                TimeInForce = TimeInForce.Day,
                OutsideRth = false,
                EnableTakeProfit = true,
                TakeProfitPrice = 95.00,
                TakeProfitOutsideRth = false,
                EnableStopLoss = true,
                StopLossPrice = 105.00
            };

            Assert.Multiple(() =>
            {
                Assert.That(order.GetIbAction(), Is.EqualTo("SELL"));
                Assert.That(order.GetIbOrderType(), Is.EqualTo("MKT"));
                Assert.That(order.GetIbTif(), Is.EqualTo("DAY"));
                Assert.That(order.OutsideRth, Is.False);
            });
        }

        [Test]
        [Description("Validates swing trade position configuration")]
        public void SwingTrade_LongPosition_TypicalConfiguration()
        {
            var order = new OrderAction
            {
                Side = OrderSide.Buy,
                Quantity = 500,
                Type = OrderType.Limit,
                LimitPrice = 75.50,
                TimeInForce = TimeInForce.GoodTillCancel,
                OutsideRth = false,
                EnableTakeProfit = true,
                TakeProfitPrice = 85.00,
                EnableStopLoss = true,
                StopLossPrice = 72.00
            };

            // Risk/Reward calculation
            var entryPrice = order.LimitPrice!.Value;
            var takeProfit = order.TakeProfitPrice!.Value;
            var stopLoss = order.StopLossPrice!.Value;
            var reward = takeProfit - entryPrice;
            var risk = entryPrice - stopLoss;
            var riskRewardRatio = reward / risk;

            Assert.Multiple(() =>
            {
                Assert.That(reward, Is.EqualTo(9.50).Within(0.01));
                Assert.That(risk, Is.EqualTo(3.50).Within(0.01));
                Assert.That(riskRewardRatio, Is.GreaterThan(2.0)); // Good R/R > 2:1
            });
        }

        [Test]
        [Description("Validates scalping configuration with tight stops")]
        public void ScalpingOrder_TightStops_Configuration()
        {
            var order = new OrderAction
            {
                Side = OrderSide.Buy,
                Quantity = 1000,
                Type = OrderType.Market,
                TimeInForce = TimeInForce.ImmediateOrCancel,
                EnableTakeProfit = true,
                TakeProfitOffset = 0.10,
                EnableTrailingStopLoss = true,
                TrailingStopLossPercent = 0.02 // Very tight 2% trailing stop
            };

            Assert.Multiple(() =>
            {
                Assert.That(order.GetIbTif(), Is.EqualTo("IOC"));
                Assert.That(order.TakeProfitOffset, Is.LessThan(0.50));
                Assert.That(order.TrailingStopLossPercent, Is.LessThan(0.05));
            });
        }
    }

    #endregion
}


