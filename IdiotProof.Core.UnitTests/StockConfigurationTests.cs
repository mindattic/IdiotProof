// ============================================================================
// StockConfigurationTests - Tests for Stock builder configuration methods
// ============================================================================
//
// This file contains comprehensive unit tests for Stock configuration methods:
// 1. WithId, WithName, WithNotes
// 2. Exchange (with ContractExchange enum)
// 3. PrimaryExchange
// 4. Repeat
// 5. Start (with End via StrategyBuilder)
//
// ============================================================================

using IdiotProof.Backend.Enums;
using IdiotProof.Backend.Models;
using IdiotProof.Backend.Strategy;
using NUnit.Framework;

namespace IdiotProof.Backend.UnitTests;

/// <summary>
/// Tests for Stock builder configuration methods.
/// </summary>
[TestFixture]
public class StockConfigurationTests
{
    #region WithId Tests

    [Test]
    public void WithId_SetsStrategyId()
    {
        // Arrange
        var expectedId = Guid.NewGuid();

        // Act
        var strategy = Stock.Ticker("AAPL")
            .WithId(expectedId)
            .Breakout(150)
            .Long().Quantity(100)
            .Build();

        // Assert
        Assert.That(strategy.Id, Is.EqualTo(expectedId));
    }

    [Test]
    public void WithId_CanBeChainedWithOtherMethods()
    {
        // Arrange
        var id = Guid.NewGuid();

        // Act
        var strategy = Stock.Ticker("AAPL")
            .WithId(id)
            .WithName("Test Strategy")
            .Breakout(150)
            .Long().Quantity(100)
            .Build();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(strategy.Id, Is.EqualTo(id));
            Assert.That(strategy.Name, Is.EqualTo("Test Strategy"));
        });
    }

    [Test]
    public void WithId_DefaultIsNewGuid()
    {
        // Act
        var strategy = Stock.Ticker("AAPL")
            .Breakout(150)
            .Long().Quantity(100)
            .Build();

        // Assert - Id should be a valid GUID
        Assert.That(strategy.Id, Is.Not.EqualTo(Guid.Empty));
    }

    #endregion

    #region WithName Tests

    [Test]
    public void WithName_SetsStrategyName()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .WithName("Apple Breakout Strategy")
            .Breakout(150)
            .Long().Quantity(100)
            .Build();

        // Assert
        Assert.That(strategy.Name, Is.EqualTo("Apple Breakout Strategy"));
    }

    [Test]
    public void WithName_DefaultIsEmpty()
    {
        // Act
        var strategy = Stock.Ticker("AAPL")
            .Breakout(150)
            .Long().Quantity(100)
            .Build();

        // Assert
        Assert.That(strategy.Name, Is.EqualTo(string.Empty));
    }

    [Test]
    public void WithName_CanBeOverwritten()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .WithName("First Name")
            .WithName("Second Name")
            .Breakout(150)
            .Long().Quantity(100)
            .Build();

        // Assert - Last name wins
        Assert.That(strategy.Name, Is.EqualTo("Second Name"));
    }

    #endregion

    #region WithNotes Tests

    [Test]
    public void WithNotes_SetsStrategyNotes()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .WithNotes("Premarket gap up strategy")
            .Breakout(150)
            .Long().Quantity(100)
            .Build();

        // Assert
        Assert.That(strategy.Notes, Is.EqualTo("Premarket gap up strategy"));
    }

    [Test]
    public void Ticker_WithNotes_SetsNotes()
    {
        // Arrange & Act - Notes can be passed to Ticker
        var strategy = Stock.Ticker("AAPL", notes: "Gap and go")
            .Breakout(150)
            .Long().Quantity(100)
            .Build();

        // Assert
        Assert.That(strategy.Notes, Is.EqualTo("Gap and go"));
    }

    [Test]
    public void WithNotes_Null_SetsNull()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .WithNotes(null)
            .Breakout(150)
            .Long().Quantity(100)
            .Build();

        // Assert
        Assert.That(strategy.Notes, Is.Null);
    }

    [Test]
    public void WithNotes_DefaultIsNull()
    {
        // Act
        var strategy = Stock.Ticker("AAPL")
            .Breakout(150)
            .Long().Quantity(100)
            .Build();

        // Assert
        Assert.That(strategy.Notes, Is.Null);
    }

    #endregion

    #region Exchange with ContractExchange Enum Tests

    [Test]
    public void Exchange_ContractExchange_Smart_SetsSmart()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .Exchange(ContractExchange.Smart)
            .Breakout(150)
            .Long().Quantity(100)
            .Build();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(strategy.Exchange, Is.EqualTo("SMART"));
            Assert.That(strategy.PrimaryExchange, Is.Null);
        });
    }

    [Test]
    public void Exchange_ContractExchange_Pink_SetsSmartWithPinkPrimary()
    {
        // Arrange & Act - Pink sheets use SMART routing with PINK as primary
        var strategy = Stock.Ticker("OTCBB")
            .Exchange(ContractExchange.Pink)
            .Breakout(0.50)
            .Long().Quantity(1000)
            .Build();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(strategy.Exchange, Is.EqualTo("SMART"));
            Assert.That(strategy.PrimaryExchange, Is.EqualTo("PINK"));
        });
    }

    [Test]
    public void Exchange_String_SetsExchangeDirectly()
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
    public void Exchange_DefaultIsSmart()
    {
        // Act
        var strategy = Stock.Ticker("AAPL")
            .Breakout(150)
            .Long().Quantity(100)
            .Build();

        // Assert
        Assert.That(strategy.Exchange, Is.EqualTo("SMART"));
    }

    #endregion

    #region PrimaryExchange Tests

    [Test]
    public void PrimaryExchange_SetsPrimaryExchange()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .PrimaryExchange("NASDAQ")
            .Breakout(150)
            .Long().Quantity(100)
            .Build();

        // Assert
        Assert.That(strategy.PrimaryExchange, Is.EqualTo("NASDAQ"));
    }

    [Test]
    public void PrimaryExchange_WithSmartRouting_UsedForOtcStocks()
    {
        // Arrange & Act - OTC stocks need SMART routing with primary exchange
        var strategy = Stock.Ticker("OTCBB")
            .Exchange("SMART")
            .PrimaryExchange("PINK")
            .Breakout(0.50)
            .Long().Quantity(1000)
            .Build();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(strategy.Exchange, Is.EqualTo("SMART"));
            Assert.That(strategy.PrimaryExchange, Is.EqualTo("PINK"));
        });
    }

    [Test]
    public void PrimaryExchange_DefaultIsNull()
    {
        // Act
        var strategy = Stock.Ticker("AAPL")
            .Breakout(150)
            .Long().Quantity(100)
            .Build();

        // Assert
        Assert.That(strategy.PrimaryExchange, Is.Null);
    }

    #endregion

    #region Repeat Tests

    [Test]
    public void Repeat_EnablesRepeat()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .Breakout(150)
            .Long().Quantity(100)
            .TakeProfit(160)
            .StopLoss(145)
            .Repeat()
            .Build();

        // Assert
        Assert.That(strategy.RepeatEnabled, Is.True);
    }

    [Test]
    public void Repeat_True_EnablesRepeat()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .Breakout(150)
            .Long().Quantity(100)
            .Repeat(true)
            .Build();

        // Assert
        Assert.That(strategy.RepeatEnabled, Is.True);
    }

    [Test]
    public void Repeat_False_DisablesRepeat()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .Breakout(150)
            .Long().Quantity(100)
            .Repeat(false)
            .Build();

        // Assert
        Assert.That(strategy.RepeatEnabled, Is.False);
    }

    [Test]
    public void Repeat_DefaultIsFalse()
    {
        // Act
        var strategy = Stock.Ticker("AAPL")
            .Breakout(150)
            .Long().Quantity(100)
            .Build();

        // Assert
        Assert.That(strategy.RepeatEnabled, Is.False);
    }

    [Test]
    public void Repeat_OnStock_EnablesRepeat()
    {
        // Arrange & Act - Repeat can be called on Stock before order methods
        var strategy = Stock.Ticker("AAPL")
            .Repeat()
            .Breakout(150)
            .Long().Quantity(100)
            .Build();

        // Assert
        Assert.That(strategy.RepeatEnabled, Is.True);
    }

    [Test]
    public void Repeat_CanBeToggledOff()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .Repeat(true)
            .Breakout(150)
            .Long().Quantity(100)
            .Repeat(false)
            .Build();

        // Assert - Last value wins
        Assert.That(strategy.RepeatEnabled, Is.False);
    }

    #endregion

    #region Start Method Tests

    [Test]
    public void Start_SetsStartTime()
    {
        // Arrange
        var startTime = new TimeOnly(4, 0);

        // Act
        var strategy = Stock.Ticker("AAPL")
            .Start(startTime)
            .Breakout(150)
            .Long().Quantity(100)
            .Build();

        // Assert
        Assert.That(strategy.StartTime, Is.EqualTo(startTime));
    }

    [Test]
    public void Start_WithEndOnBuilder_SetsBothTimes()
    {
        // Arrange
        var startTime = new TimeOnly(4, 0);
        var endTime = new TimeOnly(9, 30);

        // Act
        var strategy = Stock.Ticker("AAPL")
            .Start(startTime)
            .Breakout(150)
            .Long().Quantity(100)
            .End(endTime);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(strategy.StartTime, Is.EqualTo(startTime));
            Assert.That(strategy.EndTime, Is.EqualTo(endTime));
        });
    }

    [Test]
    public void Start_WithMarketTime_Helper()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .Start(MarketTime.PreMarket.Start)
            .Breakout(150)
            .Long().Quantity(100)
            .End(MarketTime.PreMarket.End);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(strategy.StartTime, Is.EqualTo(MarketTime.PreMarket.Start));
            Assert.That(strategy.EndTime, Is.EqualTo(MarketTime.PreMarket.End));
        });
    }

    #endregion

    #region Full Configuration Chain Tests

    [Test]
    public void FullConfiguration_AllMethodsChained_SetsAllProperties()
    {
        // Arrange
        var id = Guid.NewGuid();
        var startTime = new TimeOnly(4, 0);
        var endTime = new TimeOnly(9, 30);

        // Act
        var strategy = Stock.Ticker("AAPL")
            .WithId(id)
            .WithName("Apple Premarket Breakout")
            .WithNotes("Gap and go strategy")
            .Exchange(ContractExchange.Smart)
            .Currency("USD")
            .Enabled(true)
            .TimeFrame(startTime, endTime)
            .Repeat(true)
            .Breakout(150)
            .IsAboveVwap()
            .Long().Quantity(100)
            .TakeProfit(160)
            .StopLoss(145)
            .Build();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(strategy.Id, Is.EqualTo(id));
            Assert.That(strategy.Symbol, Is.EqualTo("AAPL"));
            Assert.That(strategy.Name, Is.EqualTo("Apple Premarket Breakout"));
            Assert.That(strategy.Notes, Is.EqualTo("Gap and go strategy"));
            Assert.That(strategy.Exchange, Is.EqualTo("SMART"));
            Assert.That(strategy.Currency, Is.EqualTo("USD"));
            Assert.That(strategy.Enabled, Is.True);
            Assert.That(strategy.StartTime, Is.EqualTo(startTime));
            Assert.That(strategy.EndTime, Is.EqualTo(endTime));
            Assert.That(strategy.RepeatEnabled, Is.True);
            Assert.That(strategy.Conditions, Has.Count.EqualTo(2));
            Assert.That(strategy.Order.EnableTakeProfit, Is.True);
            Assert.That(strategy.Order.EnableStopLoss, Is.True);
        });
    }

    [Test]
    public void OtcPinkSheets_ConfiguredCorrectly()
    {
        // Arrange & Act - Typical OTC pink sheet configuration
        var strategy = Stock.Ticker("OTCBB")
            .Exchange(ContractExchange.Pink)
            .IsPriceAbove(0.50)
            .IsVolumeAbove(2.0)
            .Long().Quantity(10000)
            .Build();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(strategy.Exchange, Is.EqualTo("SMART"));
            Assert.That(strategy.PrimaryExchange, Is.EqualTo("PINK"));
            Assert.That(strategy.Symbol, Is.EqualTo("OTCBB"));
        });
    }

    #endregion
}
