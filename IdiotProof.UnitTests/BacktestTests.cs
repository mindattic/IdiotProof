// ============================================================================
// Backtest Tests - Unit tests for strategy backtesting
// ============================================================================
//
// USAGE:
//   These tests demonstrate how to backtest strategies against historical data.
//   No IB Gateway connection required - fully offline testing.
//
// TEST PATTERNS:
//   1. GenerateTestScenario() - Creates realistic price patterns
//   2. GenerateBarsFromPrices() - Creates bars from simple price arrays
//   3. RunWithPrices() - Shorthand for simple price-based tests
//
// ============================================================================

using IdiotProof.Models;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace IdiotProof.UnitTests;

/// <summary>
/// Tests for the offline backtester.
/// </summary>
[TestFixture]
public class BacktestTests
{
    #region Basic Functionality Tests

    [Test]
    [Description("Backtest with empty data returns valid empty result")]
    public void Backtest_EmptyData_ReturnsEmptyResult()
    {
        // Arrange
        var strategy = Stock.Ticker("AAPL")
            .Breakout(150)
            .Buy(100, Price.Current)
            .TakeProfit(155)
            .Build();

        var bars = new List<HistoricalBar>();

        // Act
        var result = Backtester.Run(strategy, bars);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.TotalTrades, Is.EqualTo(0));
            Assert.That(result.NetPnL, Is.EqualTo(0));
            Assert.That(result.FinalEquity, Is.EqualTo(100_000));
        });
    }

    [Test]
    [Description("Backtest with no conditions met returns no trades")]
    public void Backtest_ConditionsNeverMet_NoTrades()
    {
        // Arrange - breakout at 150, but price never reaches it
        var strategy = Stock.Ticker("AAPL")
            .Breakout(150)
            .Buy(100, Price.Current)
            .TakeProfit(155)
            .Build();

        double[] prices = [140, 141, 142, 143, 142, 141, 140]; // Never hits 150

        // Act
        var result = Backtester.RunWithPrices(
            strategy, prices,
            new DateTime(2024, 1, 15, 4, 0, 0),
            TimeSpan.FromMinutes(1));

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.TotalTrades, Is.EqualTo(0));
            Assert.That(result.BarsProcessed, Is.EqualTo(7));
        });
    }

    #endregion

    #region Single Condition Tests

    [Test]
    [Description("Simple breakout strategy hits take profit")]
    public void Backtest_BreakoutTakeProfit_RecordsProfitableTrade()
    {
        // Arrange
        var strategy = Stock.Ticker("AAPL")
            .Breakout(150)
            .Buy(100, Price.Current)
            .TakeProfit(155)
            .Build();

        // Price: rises to 150 (triggers entry), then to 155 (hits TP), stops there
        double[] prices = [145, 147, 150, 152, 155];

        // Act
        var result = Backtester.RunWithPrices(
            strategy, prices,
            new DateTime(2024, 1, 15, 4, 0, 0),
            TimeSpan.FromMinutes(1),
            config: new BacktestConfig { VerboseLogging = false });

        // Assert - Check first trade (there may be more if conditions re-trigger)
        Assert.Multiple(() =>
        {
            Assert.That(result.TotalTrades, Is.GreaterThanOrEqualTo(1));
            Assert.That(result.Trades[0].EntryPrice, Is.EqualTo(150));
            Assert.That(result.Trades[0].ExitPrice, Is.EqualTo(155));
            Assert.That(result.Trades[0].ExitReason, Is.EqualTo("TakeProfit"));
            Assert.That(result.Trades[0].GrossPnL, Is.EqualTo(500)); // (155-150) * 100 = 500
        });
    }

    [Test]
    [Description("Breakout strategy hits stop loss")]
    public void Backtest_BreakoutStopLoss_RecordsLosingTrade()
    {
        // Arrange
        var strategy = Stock.Ticker("AAPL")
            .Breakout(150)
            .Buy(100, Price.Current)
            .TakeProfit(160)
            .StopLoss(145)
            .Build();

        // Price: rises to 150 (triggers entry), then falls to 145 (hits SL)
        double[] prices = [145, 147, 150, 148, 146, 145, 144];

        // Act
        var result = Backtester.RunWithPrices(
            strategy, prices,
            new DateTime(2024, 1, 15, 4, 0, 0),
            TimeSpan.FromMinutes(1),
            config: new BacktestConfig { VerboseLogging = false });

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.TotalTrades, Is.EqualTo(1));
            Assert.That(result.LosingTrades, Is.EqualTo(1));
            Assert.That(result.Trades[0].ExitReason, Is.EqualTo("StopLoss"));
            Assert.That(result.Trades[0].GrossPnL, Is.EqualTo(-500)); // (145-150) * 100 = -500
        });
    }

    #endregion

    #region Multi-Condition Tests

    [Test]
    [Description("Classic pullback strategy: Breakout -> Pullback -> AboveVWAP -> Entry")]
    public void Backtest_ClassicPullback_ExecutesCorrectly()
    {
        // Arrange
        var strategy = Stock.Ticker("NAMM")
            .Breakout(7.10)
            .Pullback(6.80)
            .AboveVwap()
            .Buy(1000, Price.Current)
            .TakeProfit(9.00)
            .StopLoss(6.50)
            .Build();

        // Generate realistic pullback scenario
        var bars = Backtester.GenerateTestScenario(
            startPrice: 6.50,
            breakoutPrice: 7.10,
            pullbackPrice: 6.80,
            finalPrice: 9.00,
            barsToBreakout: 15,
            barsToPullback: 8,
            barsToFinal: 30);

        // Act
        var result = Backtester.Run(strategy, bars, new BacktestConfig { VerboseLogging = false });

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.TotalTrades, Is.GreaterThanOrEqualTo(1), "Should have at least one trade");
            // The strategy should trigger after all 3 conditions are met
            Console.WriteLine(result);
        });
    }

    [Test]
    [Description("Multiple conditions must fire in sequence")]
    public void Backtest_ConditionsInSequence_RequiresCorrectOrder()
    {
        // Arrange - Breakout then Pullback
        var strategy = Stock.Ticker("TEST")
            .Breakout(100)
            .Pullback(95)
            .Buy(100, Price.Current)
            .TakeProfit(110)
            .Build();

        // Price: 95 first (pullback level), then 100 (breakout) - wrong order!
        double[] wrongOrderPrices = [90, 95, 90, 100, 105, 110]; // Pullback before breakout
        
        // Act
        var result = Backtester.RunWithPrices(
            strategy, wrongOrderPrices,
            new DateTime(2024, 1, 15, 4, 0, 0),
            TimeSpan.FromMinutes(1),
            config: new BacktestConfig { VerboseLogging = false });

        // Assert - Should still work because conditions check sequentially
        // First Breakout(100) triggers at price 100
        // Then Pullback(95) - never triggers because we're already past that
        Assert.That(result.TotalTrades, Is.EqualTo(0), "Pullback never met after breakout");
    }

    #endregion

    #region Trailing Stop Tests

    [Test]
    [Description("Trailing stop follows price up and triggers on pullback")]
    public void Backtest_TrailingStop_FollowsPriceAndTriggers()
    {
        // Arrange - 10% trailing stop
        var strategy = Stock.Ticker("AAPL")
            .Breakout(100)
            .Buy(100, Price.Current)
            .TrailingStopLoss(Percent.Ten)
            .Build();

        // Price: 100 (entry), rises to 120, then drops to 108 (10% below 120), then ends
        double[] prices = [95, 100, 105, 110, 115, 120, 115, 108];

        // Act
        var result = Backtester.RunWithPrices(
            strategy, prices,
            new DateTime(2024, 1, 15, 4, 0, 0),
            TimeSpan.FromMinutes(1),
            config: new BacktestConfig { VerboseLogging = false });

        // Assert - Check first trade exits via trailing stop
        Assert.Multiple(() =>
        {
            Assert.That(result.TotalTrades, Is.GreaterThanOrEqualTo(1));
            Assert.That(result.Trades[0].ExitReason, Is.EqualTo("TrailingStop"));
            Assert.That(result.Trades[0].EntryPrice, Is.EqualTo(100));
            // Stop should be at 120 * 0.90 = 108
            Assert.That(result.Trades[0].ExitPrice, Is.EqualTo(108));
            Assert.That(result.Trades[0].GrossPnL, Is.EqualTo(800)); // (108-100) * 100
        });
    }

    [Test]
    [Description("Trailing stop protects profit even when TP not reached")]
    public void Backtest_TrailingStop_ProtectsProfit()
    {
        // Arrange - TP at 130, but trailing stop catches profit at 108
        var strategy = Stock.Ticker("AAPL")
            .Breakout(100)
            .Buy(100, Price.Current)
            .TakeProfit(130)
            .TrailingStopLoss(Percent.Ten)
            .Build();

        double[] prices = [95, 100, 110, 120, 115, 108]; // Never reaches TP, ends at stop

        // Act
        var result = Backtester.RunWithPrices(
            strategy, prices,
            new DateTime(2024, 1, 15, 4, 0, 0),
            TimeSpan.FromMinutes(1),
            config: new BacktestConfig { VerboseLogging = false });

        // Assert - First trade should show profit from trailing stop
        Assert.Multiple(() =>
        {
            Assert.That(result.TotalTrades, Is.GreaterThanOrEqualTo(1));
            Assert.That(result.Trades[0].ExitReason, Is.EqualTo("TrailingStop"));
            Assert.That(result.Trades[0].NetPnL, Is.GreaterThan(0), "Should have locked in profit");
        });
    }

    #endregion

    #region Statistics Tests

    [Test]
    [Description("Win rate calculated correctly")]
    public void Backtest_WinRate_CalculatedCorrectly()
    {
        // Arrange - Create scenario with known win/loss ratio
        var strategy = Stock.Ticker("TEST")
            .Breakout(100)
            .Buy(100, Price.Current)
            .TakeProfit(105)
            .StopLoss(95)
            .Build();

        // Create multiple trades: 2 wins, 1 loss
        // Trade 1: Entry 100, Exit 105 (win)
        // Reset conditions
        // Trade 2: Entry 100, Exit 95 (loss)
        // Trade 3: Entry 100, Exit 105 (win)
        double[] prices = [
            95, 100, 102, 105,     // Trade 1: Win
            90, 95, 100, 97, 95,   // Trade 2: Loss
            90, 95, 100, 103, 105  // Trade 3: Win
        ];

        // Act
        var result = Backtester.RunWithPrices(
            strategy, prices,
            new DateTime(2024, 1, 15, 4, 0, 0),
            TimeSpan.FromMinutes(1),
            config: new BacktestConfig { VerboseLogging = false });

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.TotalTrades, Is.EqualTo(3));
            Assert.That(result.WinningTrades, Is.EqualTo(2));
            Assert.That(result.LosingTrades, Is.EqualTo(1));
            Assert.That(result.WinRate, Is.EqualTo(200.0 / 3).Within(0.1)); // 66.7%
        });
    }

    [Test]
    [Description("Profit factor calculated correctly")]
    public void Backtest_ProfitFactor_CalculatedCorrectly()
    {
        // Arrange - 2 wins of $500 each, 1 loss of $500
        var strategy = Stock.Ticker("TEST")
            .Breakout(100)
            .Buy(100, Price.Current)
            .TakeProfit(105)
            .StopLoss(95)
            .Build();

        double[] prices = [
            95, 100, 105,           // Win: +$500
            90, 100, 95,            // Loss: -$500
            90, 100, 105            // Win: +$500
        ];

        // Act
        var result = Backtester.RunWithPrices(
            strategy, prices,
            new DateTime(2024, 1, 15, 4, 0, 0),
            TimeSpan.FromMinutes(1),
            config: new BacktestConfig { VerboseLogging = false });

        // Assert
        // Gross Profit = 1000, Gross Loss = 500, PF = 2.0
        Assert.That(result.ProfitFactor, Is.EqualTo(2.0).Within(0.01));
    }

    [Test]
    [Description("Max drawdown tracked correctly")]
    public void Backtest_MaxDrawdown_TrackedCorrectly()
    {
        // Arrange
        var strategy = Stock.Ticker("TEST")
            .Breakout(100)
            .Buy(100, Price.Current)
            .TakeProfit(110)
            .StopLoss(90)
            .Build();

        // Win, then loss, then win
        // Equity: 100k -> 101k -> 100k (DD = 1k)
        double[] prices = [
            95, 100, 110,           // Win: +$1000
            95, 100, 90,            // Loss: -$1000
            95, 100, 110            // Win: +$1000
        ];

        // Act
        var result = Backtester.RunWithPrices(
            strategy, prices,
            new DateTime(2024, 1, 15, 4, 0, 0),
            TimeSpan.FromMinutes(1),
            config: new BacktestConfig { VerboseLogging = false, CommissionPerTrade = 0 });

        // Assert
        // Peak at 101k after first win, drops to 100k after loss
        Assert.That(result.MaxDrawdown, Is.EqualTo(1000).Within(10));
    }

    #endregion

    #region Commission Tests

    [Test]
    [Description("Commission deducted from trade P&L")]
    public void Backtest_Commission_DeductedFromPnL()
    {
        // Arrange
        var strategy = Stock.Ticker("AAPL")
            .Breakout(100)
            .Buy(100, Price.Current)
            .TakeProfit(105)
            .Build();

        double[] prices = [95, 100, 105];

        var config = new BacktestConfig
        {
            CommissionPerTrade = 5.00, // $5 per trade
            VerboseLogging = false
        };

        // Act
        var result = Backtester.RunWithPrices(
            strategy, prices,
            new DateTime(2024, 1, 15, 4, 0, 0),
            TimeSpan.FromMinutes(1),
            config: config);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Trades[0].GrossPnL, Is.EqualTo(500)); // (105-100)*100
            Assert.That(result.Trades[0].Commission, Is.EqualTo(10)); // Entry + Exit
            Assert.That(result.Trades[0].NetPnL, Is.EqualTo(490)); // 500 - 10
        });
    }

    #endregion

    #region Close Order Tests

    [Test]
    [Description("Close order works correctly in backtest")]
    public void Backtest_CloseOrder_WorksCorrectly()
    {
        // Arrange - Strategy to close a long position when price drops
        var strategy = Stock.Ticker("AAPL")
            .PriceBelow(95)  // Close when price drops below 95
            .CloseLong(100)  // Sell 100 shares
            .Build();

        double[] prices = [100, 98, 96, 94, 92];

        // Act
        var result = Backtester.RunWithPrices(
            strategy, prices,
            new DateTime(2024, 1, 15, 4, 0, 0),
            TimeSpan.FromMinutes(1),
            config: new BacktestConfig { VerboseLogging = false });

        // Assert - Close triggers at price 94 (first price below 95)
        Assert.Multiple(() =>
        {
            Assert.That(result.TotalTrades, Is.EqualTo(1));
            Assert.That(result.Trades[0].Side, Is.EqualTo(OrderSide.Sell));
            Assert.That(result.Trades[0].EntryPrice, Is.EqualTo(94)); // "Entry" for close is sell price
        });
    }

    #endregion

    #region Result Output Tests

    [Test]
    [Description("Result ToString produces formatted output")]
    public void Backtest_ResultToString_ProducesFormattedOutput()
    {
        // Arrange
        var strategy = Stock.Ticker("AAPL")
            .Breakout(100)
            .Buy(100, Price.Current)
            .TakeProfit(110)
            .Build();

        double[] prices = [95, 100, 105, 110];

        // Act
        var result = Backtester.RunWithPrices(
            strategy, prices,
            new DateTime(2024, 1, 15, 4, 0, 0),
            TimeSpan.FromMinutes(1),
            config: new BacktestConfig { VerboseLogging = false });

        string output = result.ToString();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(output, Does.Contain("BACKTEST RESULTS"));
            Assert.That(output, Does.Contain("AAPL"));
            Assert.That(output, Does.Contain("TRADES"));
            Assert.That(output, Does.Contain("PROFIT & LOSS"));
            Assert.That(output, Does.Contain("RISK METRICS"));
        });
    }

    #endregion
}

/// <summary>
/// Integration tests that demonstrate full strategy backtesting scenarios.
/// </summary>
[TestFixture]
public class BacktestScenarioTests
{
    [Test]
    [Description("Pre-market gap strategy backtest")]
    public void Scenario_PreMarketGapStrategy()
    {
        // Arrange - Strategy for pre-market gap plays
        var strategy = Stock.Ticker("NVDA")
            .Breakout(500)
            .Pullback(495)
            .AboveVwap()
            .Buy(50, Price.Current)
            .TakeProfit(520)
            .StopLoss(490)
            .TrailingStopLoss(Percent.Five)
            .Build();

        // Generate pre-market scenario starting at 4:00 AM
        var bars = Backtester.GenerateTestScenario(
            startPrice: 480,
            breakoutPrice: 500,
            pullbackPrice: 495,
            finalPrice: 525,
            startTime: new DateTime(2024, 1, 15, 4, 0, 0),
            barInterval: TimeSpan.FromMinutes(1));

        // Act
        var result = Backtester.Run(strategy, bars, new BacktestConfig { VerboseLogging = true });

        // Assert
        Console.WriteLine(result);
        Assert.That(result.BarsProcessed, Is.GreaterThan(0));
    }

    [Test]
    [Description("Mean reversion strategy backtest")]
    public void Scenario_MeanReversionStrategy()
    {
        // Arrange - Buy when price drops below VWAP, sell when it returns
        var strategy = Stock.Ticker("SPY")
            .BelowVwap(-0.50)  // Price at least $0.50 below VWAP
            .Buy(100, Price.Current)
            .TakeProfit(2.00)  // $2 profit target
            .StopLoss(1.00)    // $1 stop
            .Build();

        // Generate choppy mean-reverting data
        double[] prices = [
            450.00, 449.50, 449.00, 448.50, 448.00, // Drift down
            447.50, 448.00, 448.50, 449.00, 449.50, // Bounce
            450.00, 450.50, 451.00, 450.50, 450.00  // Continue up then flat
        ];

        // Act
        var result = Backtester.RunWithPrices(
            strategy, prices,
            new DateTime(2024, 1, 15, 9, 30, 0),
            TimeSpan.FromMinutes(1),
            config: new BacktestConfig { VerboseLogging = true });

        // Assert
        Console.WriteLine(result);
    }
}
