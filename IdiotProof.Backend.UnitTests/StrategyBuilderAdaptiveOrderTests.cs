// ============================================================================
// StrategyBuilderAdaptiveOrderTests - Tests for AdaptiveOrder Fluent API
// ============================================================================
//
// This file contains unit tests for the AdaptiveOrder fluent API method which
// enables smart dynamic order management on the StrategyBuilder.
//
// ============================================================================

using IdiotProof.Backend.Enums;
using IdiotProof.Backend.Models;
using IdiotProof.Backend.Strategy;
using NUnit.Framework;

namespace IdiotProof.Backend.UnitTests;

/// <summary>
/// Tests for the AdaptiveOrder fluent API method on StrategyBuilder.
/// </summary>
[TestFixture]
public class StrategyBuilderAdaptiveOrderTests
{
    #region Basic AdaptiveOrder Tests

    [Test]
    public void AdaptiveOrder_DefaultMode_EnablesBalanced()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .Breakout(150)
            .Long().Quantity(100)
            .TakeProfit(160)
            .StopLoss(145)
            .AdaptiveOrder()
            .Build();

        // Assert
        Assert.That(strategy.Order.AdaptiveOrder, Is.Not.Null);
        Assert.That(strategy.Order.AdaptiveOrder!.Mode, Is.EqualTo(AdaptiveMode.Balanced));
    }

    [Test]
    public void AdaptiveOrder_Balanced_SetsBalancedMode()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .Breakout(150)
            .Long().Quantity(100)
            .TakeProfit(160)
            .StopLoss(145)
            .AdaptiveOrder("Balanced")
            .Build();

        // Assert
        Assert.That(strategy.Order.AdaptiveOrder, Is.Not.Null);
        Assert.That(strategy.Order.AdaptiveOrder!.Mode, Is.EqualTo(AdaptiveMode.Balanced));
    }

    [Test]
    public void AdaptiveOrder_Conservative_SetsConservativeMode()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .Breakout(150)
            .Long().Quantity(100)
            .TakeProfit(160)
            .StopLoss(145)
            .AdaptiveOrder("Conservative")
            .Build();

        // Assert
        Assert.That(strategy.Order.AdaptiveOrder, Is.Not.Null);
        Assert.That(strategy.Order.AdaptiveOrder!.Mode, Is.EqualTo(AdaptiveMode.Conservative));
    }

    [Test]
    public void AdaptiveOrder_Aggressive_SetsAggressiveMode()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .Breakout(150)
            .Long().Quantity(100)
            .TakeProfit(160)
            .StopLoss(145)
            .AdaptiveOrder("Aggressive")
            .Build();

        // Assert
        Assert.That(strategy.Order.AdaptiveOrder, Is.Not.Null);
        Assert.That(strategy.Order.AdaptiveOrder!.Mode, Is.EqualTo(AdaptiveMode.Aggressive));
    }

    #endregion

    #region Case Insensitivity Tests

    [Test]
    public void AdaptiveOrder_LowercaseBalanced_SetsBalancedMode()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .Breakout(150)
            .Long().Quantity(100)
            .TakeProfit(160)
            .AdaptiveOrder("balanced")
            .Build();

        // Assert
        Assert.That(strategy.Order.AdaptiveOrder!.Mode, Is.EqualTo(AdaptiveMode.Balanced));
    }

    [Test]
    public void AdaptiveOrder_UppercaseAGGRESSIVE_SetsAggressiveMode()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .Breakout(150)
            .Long().Quantity(100)
            .TakeProfit(160)
            .AdaptiveOrder("AGGRESSIVE")
            .Build();

        // Assert
        Assert.That(strategy.Order.AdaptiveOrder!.Mode, Is.EqualTo(AdaptiveMode.Aggressive));
    }

    [Test]
    public void AdaptiveOrder_MixedCaseConservative_SetsConservativeMode()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .Breakout(150)
            .Long().Quantity(100)
            .TakeProfit(160)
            .AdaptiveOrder("CoNsErVaTiVe")
            .Build();

        // Assert
        Assert.That(strategy.Order.AdaptiveOrder!.Mode, Is.EqualTo(AdaptiveMode.Conservative));
    }

    [Test]
    public void AdaptiveOrder_InvalidMode_DefaultsToBalanced()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .Breakout(150)
            .Long().Quantity(100)
            .TakeProfit(160)
            .AdaptiveOrder("InvalidMode")
            .Build();

        // Assert - Unknown mode defaults to Balanced
        Assert.That(strategy.Order.AdaptiveOrder!.Mode, Is.EqualTo(AdaptiveMode.Balanced));
    }

    #endregion

    #region AdaptiveOrder with Config Object Tests

    [Test]
    public void AdaptiveOrder_WithConfigObject_SetsConfig()
    {
        // Arrange
        var config = new AdaptiveOrderConfig { Mode = AdaptiveMode.Aggressive };

        // Act
        var strategy = Stock.Ticker("AAPL")
            .Breakout(150)
            .Long().Quantity(100)
            .TakeProfit(160)
            .AdaptiveOrder(config)
            .Build();

        // Assert
        Assert.That(strategy.Order.AdaptiveOrder, Is.SameAs(config));
        Assert.That(strategy.Order.AdaptiveOrder!.Mode, Is.EqualTo(AdaptiveMode.Aggressive));
    }

    [Test]
    public void AdaptiveOrder_NullConfig_ThrowsException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
        {
            Stock.Ticker("AAPL")
                .Breakout(150)
                .Long().Quantity(100)
                .TakeProfit(160)
                .AdaptiveOrder((AdaptiveOrderConfig)null!)
                .Build();
        });
    }

    #endregion

    #region AdaptiveOrder with Presets Tests

    [Test]
    public void AdaptiveOrder_UsingAdaptiveBalanced_SetsCorrectly()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .Breakout(150)
            .Long().Quantity(100)
            .TakeProfit(160)
            .AdaptiveOrder(Adaptive.Balanced)
            .Build();

        // Assert
        Assert.That(strategy.Order.AdaptiveOrder, Is.Not.Null);
        Assert.That(strategy.Order.AdaptiveOrder!.Mode, Is.EqualTo(AdaptiveMode.Balanced));
    }

    [Test]
    public void AdaptiveOrder_UsingAdaptiveAggressive_SetsCorrectly()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .Breakout(150)
            .Long().Quantity(100)
            .TakeProfit(160)
            .AdaptiveOrder(Adaptive.Aggressive)
            .Build();

        // Assert
        Assert.That(strategy.Order.AdaptiveOrder!.Mode, Is.EqualTo(AdaptiveMode.Aggressive));
    }

    [Test]
    public void AdaptiveOrder_UsingAdaptiveConservative_SetsCorrectly()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .Breakout(150)
            .Long().Quantity(100)
            .TakeProfit(160)
            .AdaptiveOrder(Adaptive.Conservative)
            .Build();

        // Assert
        Assert.That(strategy.Order.AdaptiveOrder!.Mode, Is.EqualTo(AdaptiveMode.Conservative));
    }

    #endregion

    #region Default Value Tests

    [Test]
    public void AdaptiveOrder_NotCalled_IsNull()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .Breakout(150)
            .Long().Quantity(100)
            .TakeProfit(160)
            .Build();

        // Assert - Without calling AdaptiveOrder, it should be null
        Assert.That(strategy.Order.AdaptiveOrder, Is.Null);
    }

    #endregion

    #region Chaining Tests

    [Test]
    public void AdaptiveOrder_CanBeChainedWithTakeProfit()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .Breakout(150)
            .Long().Quantity(100)
            .TakeProfit(160)
            .AdaptiveOrder("Aggressive")
            .StopLoss(145)
            .Build();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(strategy.Order.EnableTakeProfit, Is.True);
            Assert.That(strategy.Order.TakeProfitPrice, Is.EqualTo(160));
            Assert.That(strategy.Order.EnableStopLoss, Is.True);
            Assert.That(strategy.Order.StopLossPrice, Is.EqualTo(145));
            Assert.That(strategy.Order.AdaptiveOrder, Is.Not.Null);
            Assert.That(strategy.Order.AdaptiveOrder!.Mode, Is.EqualTo(AdaptiveMode.Aggressive));
        });
    }

    [Test]
    public void AdaptiveOrder_CanBeChainedWithTrailingStopLoss()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .Breakout(150)
            .Long().Quantity(100)
            .TakeProfit(160)
            .TrailingStopLoss(Percent.Ten)
            .AdaptiveOrder("Conservative")
            .Build();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(strategy.Order.EnableTrailingStopLoss, Is.True);
            Assert.That(strategy.Order.AdaptiveOrder!.Mode, Is.EqualTo(AdaptiveMode.Conservative));
        });
    }

    [Test]
    public void AdaptiveOrder_LastCallWins()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .Breakout(150)
            .Long().Quantity(100)
            .TakeProfit(160)
            .AdaptiveOrder("Conservative")
            .AdaptiveOrder("Aggressive")
            .Build();

        // Assert - Last call wins
        Assert.That(strategy.Order.AdaptiveOrder!.Mode, Is.EqualTo(AdaptiveMode.Aggressive));
    }

    #endregion

    #region Complex Strategy Integration Tests

    [Test]
    public void AdaptiveOrder_CompleteStrategy_FromCopilotInstructions()
    {
        // Arrange & Act - Example from copilot-instructions.md
        // Note: TakeProfit/StopLoss must come after Long() since they're on StrategyBuilder
        var strategy = Stock.Ticker("AAPL")
            .IsPriceAbove(150)
            .IsAboveVwap()
            .IsEmaAbove(9)
            .IsDI(DiDirection.Positive)
            .Long().Quantity(100)
            .TakeProfit(160)
            .StopLoss(145)
            .AdaptiveOrder("Aggressive")
            .Build();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(strategy.Symbol, Is.EqualTo("AAPL"));
            Assert.That(strategy.Order.AdaptiveOrder, Is.Not.Null);
            Assert.That(strategy.Order.AdaptiveOrder!.Mode, Is.EqualTo(AdaptiveMode.Aggressive));
            Assert.That(strategy.Order.EnableTakeProfit, Is.True);
            Assert.That(strategy.Order.EnableStopLoss, Is.True);
            Assert.That(strategy.Conditions, Has.Count.EqualTo(4)); // PriceAbove, AboveVwap, EmaAbove, Di
        });
    }

    [Test]
    public void AdaptiveOrder_WithAllExitOptions_ConfiguresCorrectly()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("NVDA")
            .TimeFrame(TradingSession.PreMarket)
            .GapUp(5)
            .IsAboveVwap()
            .Long().Quantity(100)
            .TakeProfit(500)
            .StopLoss(480)
            .ExitStrategy(MarketTime.PreMarket.Ending).IsProfitable()
            .AdaptiveOrder(Adaptive.Aggressive)
            .Build();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(strategy.Order.AdaptiveOrder, Is.Not.Null);
            Assert.That(strategy.Order.ClosePositionTime, Is.EqualTo(MarketTime.PreMarket.Ending));
            Assert.That(strategy.Order.ClosePositionOnlyIfProfitable, Is.True);
            Assert.That(strategy.Order.EnableTakeProfit, Is.True);
            Assert.That(strategy.Order.EnableStopLoss, Is.True);
        });
    }

    #endregion

    #region Short Position Tests

    [Test]
    public void AdaptiveOrder_WithShortPosition_SetsCorrectly()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("TSLA")
            .IsBelowVwap()
            .IsLowerHighs()
            .Short().Quantity(50)
            .TakeProfit(180)
            .StopLoss(210)
            .AdaptiveOrder("Conservative")
            .Build();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(strategy.Order.Side, Is.EqualTo(OrderSide.Sell));
            Assert.That(strategy.Order.AdaptiveOrder, Is.Not.Null);
            Assert.That(strategy.Order.AdaptiveOrder!.Mode, Is.EqualTo(AdaptiveMode.Conservative));
        });
    }

    #endregion
}
