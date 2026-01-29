// ============================================================================
// TimeAndPercentTests - Tests for Time, Percent, TIF, and Price helper classes
// ============================================================================

using IdiotProof.Models;
using NUnit.Framework;

namespace IdiotProof.UnitTests;

/// <summary>
/// Tests for the Time helper class and TradingPeriod.
/// </summary>
[TestFixture]
public class TimeTests
{
    #region TradingPeriod Tests

    [Test]
    public void TradingPeriod_Duration_CalculatesCorrectly()
    {
        // Arrange
        var period = new TradingPeriod(
            new TimeOnly(8, 0),
            new TimeOnly(10, 30));

        // Act
        var duration = period.Duration;

        // Assert
        Assert.That(duration, Is.EqualTo(TimeSpan.FromHours(2.5)));
    }

    [Test]
    public void TradingPeriod_Contains_TimeInRange_ReturnsTrue()
    {
        // Arrange
        var period = new TradingPeriod(
            new TimeOnly(8, 0),
            new TimeOnly(10, 0));

        // Act & Assert
        Assert.That(period.Contains(new TimeOnly(9, 0)), Is.True);
    }

    [Test]
    public void TradingPeriod_Contains_TimeAtStart_ReturnsTrue()
    {
        // Arrange
        var period = new TradingPeriod(
            new TimeOnly(8, 0),
            new TimeOnly(10, 0));

        // Act & Assert
        Assert.That(period.Contains(new TimeOnly(8, 0)), Is.True);
    }

    [Test]
    public void TradingPeriod_Contains_TimeAtEnd_ReturnsTrue()
    {
        // Arrange
        var period = new TradingPeriod(
            new TimeOnly(8, 0),
            new TimeOnly(10, 0));

        // Act & Assert
        Assert.That(period.Contains(new TimeOnly(10, 0)), Is.True);
    }

    [Test]
    public void TradingPeriod_Contains_TimeBeforeStart_ReturnsFalse()
    {
        // Arrange
        var period = new TradingPeriod(
            new TimeOnly(8, 0),
            new TimeOnly(10, 0));

        // Act & Assert
        Assert.That(period.Contains(new TimeOnly(7, 59)), Is.False);
    }

    [Test]
    public void TradingPeriod_Contains_TimeAfterEnd_ReturnsFalse()
    {
        // Arrange
        var period = new TradingPeriod(
            new TimeOnly(8, 0),
            new TimeOnly(10, 0));

        // Act & Assert
        Assert.That(period.Contains(new TimeOnly(10, 1)), Is.False);
    }

    [Test]
    public void TradingPeriod_ToString_FormatsCorrectly()
    {
        // Arrange
        var period = new TradingPeriod(
            new TimeOnly(8, 30),
            new TimeOnly(15, 0));

        // Act
        var result = period.ToString();

        // Assert
        Assert.That(result, Is.EqualTo("08:30 - 15:00 CST"));
    }

    #endregion

    #region Time Static Periods Tests

    [Test]
    public void Time_PreMarket_HasCorrectTimes()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Time.PreMarket.Start, Is.EqualTo(new TimeOnly(3, 0)));
            Assert.That(Time.PreMarket.End, Is.EqualTo(new TimeOnly(7, 0)));
        });
    }

    [Test]
    public void Time_RTH_HasCorrectTimes()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Time.RTH.Start, Is.EqualTo(new TimeOnly(8, 30)));
            Assert.That(Time.RTH.End, Is.EqualTo(new TimeOnly(15, 0)));
        });
    }

    [Test]
    public void Time_AfterHours_HasCorrectTimes()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Time.AfterHours.Start, Is.EqualTo(new TimeOnly(15, 0)));
            Assert.That(Time.AfterHours.End, Is.EqualTo(new TimeOnly(18, 0)));
        });
    }

    [Test]
    public void Time_Extended_HasCorrectTimes()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Time.Extended.Start, Is.EqualTo(new TimeOnly(3, 0)));
            Assert.That(Time.Extended.End, Is.EqualTo(new TimeOnly(18, 0)));
        });
    }

    [Test]
    public void Time_PreMarket_Duration_Is4Hours()
    {
        Assert.That(Time.PreMarket.Duration, Is.EqualTo(TimeSpan.FromHours(4)));
    }

    [Test]
    public void Time_RTH_Duration_Is6Point5Hours()
    {
        Assert.That(Time.RTH.Duration, Is.EqualTo(TimeSpan.FromHours(6.5)));
    }

    #endregion

    #region TimeOnly AddMinutes Tests

    [Test]
    public void TimeOnly_AddMinutes_WorksCorrectly()
    {
        // Arrange
        var endTime = Time.PreMarket.End; // 7:00 AM

        // Act
        var tenMinutesBefore = endTime.AddMinutes(-10);

        // Assert
        Assert.That(tenMinutesBefore, Is.EqualTo(new TimeOnly(6, 50)));
    }

    [Test]
    public void TimeOnly_AddMinutes_CanAddHours()
    {
        // Arrange
        var startTime = Time.PreMarket.Start; // 3:00 AM

        // Act
        var twoHoursLater = startTime.AddMinutes(120);

        // Assert
        Assert.That(twoHoursLater, Is.EqualTo(new TimeOnly(5, 0)));
    }

    #endregion
}

/// <summary>
/// Tests for the Percent helper class.
/// </summary>
[TestFixture]
public class PercentTests
{
    #region Named Percentage Tests

    [Test]
    public void Percent_One_Returns0Point01()
    {
        Assert.That(Percent.One, Is.EqualTo(0.01));
    }

    [Test]
    public void Percent_Five_Returns0Point05()
    {
        Assert.That(Percent.Five, Is.EqualTo(0.05));
    }

    [Test]
    public void Percent_Ten_Returns0Point10()
    {
        Assert.That(Percent.Ten, Is.EqualTo(0.10));
    }

    [Test]
    public void Percent_Fifteen_Returns0Point15()
    {
        Assert.That(Percent.Fifteen, Is.EqualTo(0.15));
    }

    [Test]
    public void Percent_Twenty_Returns0Point20()
    {
        Assert.That(Percent.Twenty, Is.EqualTo(0.20));
    }

    [Test]
    public void Percent_TwentyFive_Returns0Point25()
    {
        Assert.That(Percent.TwentyFive, Is.EqualTo(0.25));
    }

    [Test]
    public void Percent_Fifty_Returns0Point50()
    {
        Assert.That(Percent.Fifty, Is.EqualTo(0.50));
    }

    #endregion

    #region Percent.Custom Tests

    [TestCase(1, 0.01)]
    [TestCase(5, 0.05)]
    [TestCase(10, 0.10)]
    [TestCase(12.5, 0.125)]
    [TestCase(25, 0.25)]
    [TestCase(50, 0.50)]
    [TestCase(100, 1.0)]
    public void Percent_Custom_ReturnsCorrectDecimal(double input, double expected)
    {
        Assert.That(Percent.Custom(input), Is.EqualTo(expected).Within(0.0001));
    }

    [Test]
    public void Percent_Custom_Zero_ReturnsZero()
    {
        Assert.That(Percent.Custom(0), Is.EqualTo(0));
    }

    [Test]
    public void Percent_Custom_NegativeValue_ThrowsException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Percent.Custom(-1));
    }

    [Test]
    public void Percent_Custom_Over100_ThrowsException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Percent.Custom(100.1));
    }

    #endregion
}

/// <summary>
/// Tests for the TIF (Time In Force) helper class.
/// </summary>
[TestFixture]
public class TIFTests
{
    [Test]
    public void TIF_Day_ReturnsTimeInForceDay()
    {
        Assert.That(TIF.Day, Is.EqualTo(TimeInForce.Day));
    }

    [Test]
    public void TIF_GoodTillCancel_ReturnsTimeInForceGoodTillCancel()
    {
        Assert.That(TIF.GoodTillCancel, Is.EqualTo(TimeInForce.GoodTillCancel));
    }

    [Test]
    public void TIF_GTC_IsAliasForGoodTillCancel()
    {
        Assert.That(TIF.GTC, Is.EqualTo(TIF.GoodTillCancel));
    }

    [Test]
    public void TIF_ImmediateOrCancel_ReturnsTimeInForceImmediateOrCancel()
    {
        Assert.That(TIF.ImmediateOrCancel, Is.EqualTo(TimeInForce.ImmediateOrCancel));
    }

    [Test]
    public void TIF_IOC_IsAliasForImmediateOrCancel()
    {
        Assert.That(TIF.IOC, Is.EqualTo(TIF.ImmediateOrCancel));
    }

    [Test]
    public void TIF_FillOrKill_ReturnsTimeInForceFillOrKill()
    {
        Assert.That(TIF.FillOrKill, Is.EqualTo(TimeInForce.FillOrKill));
    }

    [Test]
    public void TIF_FOK_IsAliasForFillOrKill()
    {
        Assert.That(TIF.FOK, Is.EqualTo(TIF.FillOrKill));
    }
}

/// <summary>
/// Tests for the Price enum.
/// </summary>
[TestFixture]
public class PriceTests
{
    [Test]
    public void Price_Current_IsDefined()
    {
        Assert.That(Enum.IsDefined(typeof(Price), Price.Current), Is.True);
    }

    [Test]
    public void Price_VWAP_IsDefined()
    {
        Assert.That(Enum.IsDefined(typeof(Price), Price.VWAP), Is.True);
    }

    [Test]
    public void Price_Bid_IsDefined()
    {
        Assert.That(Enum.IsDefined(typeof(Price), Price.Bid), Is.True);
    }

    [Test]
    public void Price_Ask_IsDefined()
    {
        Assert.That(Enum.IsDefined(typeof(Price), Price.Ask), Is.True);
    }

    [Test]
    public void Price_AllValuesAreDifferent()
    {
        var values = Enum.GetValues<Price>();
        var uniqueValues = values.Distinct().ToArray();
        Assert.That(uniqueValues, Has.Length.EqualTo(values.Length));
    }
}

/// <summary>
/// Tests for OrderAction helper methods.
/// </summary>
[TestFixture]
public class OrderActionTests
{
    #region Original TIF Tests

    [Test]
    public void GetIbTif_Day_ReturnsDAY()
    {
        var order = new OrderAction { TimeInForce = TimeInForce.Day };
        Assert.That(order.GetIbTif(), Is.EqualTo("DAY"));
    }

    [Test]
    public void GetIbTif_GoodTillCancel_ReturnsGTC()
    {
        var order = new OrderAction { TimeInForce = TimeInForce.GoodTillCancel };
        Assert.That(order.GetIbTif(), Is.EqualTo("GTC"));
    }

    [Test]
    public void GetIbTif_ImmediateOrCancel_ReturnsIOC()
    {
        var order = new OrderAction { TimeInForce = TimeInForce.ImmediateOrCancel };
        Assert.That(order.GetIbTif(), Is.EqualTo("IOC"));
    }

    [Test]
    public void GetIbTif_FillOrKill_ReturnsFOK()
    {
        var order = new OrderAction { TimeInForce = TimeInForce.FillOrKill };
        Assert.That(order.GetIbTif(), Is.EqualTo("FOK"));
    }

    #endregion

    #region Extended TIF Tests

    [Test]
    public void GetIbTif_Overnight_ReturnsGTC()
    {
        var order = new OrderAction { TimeInForce = TimeInForce.Overnight };
        Assert.That(order.GetIbTif(), Is.EqualTo("GTC"));
    }

    [Test]
    public void GetIbTif_OvernightPlusDay_ReturnsDTC()
    {
        var order = new OrderAction { TimeInForce = TimeInForce.OvernightPlusDay };
        Assert.That(order.GetIbTif(), Is.EqualTo("DTC"));
    }

    [Test]
    public void GetIbTif_AtTheOpening_ReturnsOPG()
    {
        var order = new OrderAction { TimeInForce = TimeInForce.AtTheOpening };
        Assert.That(order.GetIbTif(), Is.EqualTo("OPG"));
    }

    #endregion

    #region Order Type and Action Tests

    [Test]
    public void GetIbOrderType_Market_ReturnsMKT()
    {
        var order = new OrderAction { Type = OrderType.Market };
        Assert.That(order.GetIbOrderType(), Is.EqualTo("MKT"));
    }

    [Test]
    public void GetIbOrderType_Limit_ReturnsLMT()
    {
        var order = new OrderAction { Type = OrderType.Limit };
        Assert.That(order.GetIbOrderType(), Is.EqualTo("LMT"));
    }

    [Test]
    public void GetIbAction_Buy_ReturnsBUY()
    {
        var order = new OrderAction { Side = OrderSide.Buy };
        Assert.That(order.GetIbAction(), Is.EqualTo("BUY"));
    }

    [Test]
    public void GetIbAction_Sell_ReturnsSELL()
    {
        var order = new OrderAction { Side = OrderSide.Sell };
        Assert.That(order.GetIbAction(), Is.EqualTo("SELL"));
    }

    #endregion

    #region ToString Tests

    [Test]
    public void ToString_ContainsAllRelevantInfo()
    {
        var order = new OrderAction
        {
            Side = OrderSide.Buy,
            Quantity = 100,
            Type = OrderType.Limit,
            TimeInForce = TimeInForce.GoodTillCancel,
            EnableTakeProfit = true,
            TakeProfitPrice = 150
        };

        var result = order.ToString();

        Assert.Multiple(() =>
        {
            Assert.That(result, Does.Contain("Buy"));
            Assert.That(result, Does.Contain("100"));
            Assert.That(result, Does.Contain("Limit"));
            Assert.That(result, Does.Contain("150"));
        });
    }

    #endregion
}
