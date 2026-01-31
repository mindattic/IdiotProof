// ============================================================================
// PreviousStrategyTests - Compliance tests for Previous Strategies
// ============================================================================
//
// These tests ensure that all strategies in the "Previous Strategies" region
// of Program.cs remain compliant with the current fluent API and build correctly.
//
// If any of these tests fail after an API change, it indicates that the
// corresponding strategy in Program.cs needs to be updated.
//
// ============================================================================

using IdiotProof.Backend.Enums;
using IdiotProof.Backend.Models;
using NUnit.Framework;

namespace IdiotProof.Backend.UnitTests;

/// <summary>
/// Compliance tests for all Previous Strategies in Program.cs.
/// Ensures strategies build correctly and have valid configuration.
/// </summary>
[TestFixture]
public class PreviousStrategyTests
{
    [Test]
    public void AllPreviousStrategies_RemainCompliant()
    {
        Console.WriteLine("================================================================");
        Console.WriteLine("              Previous Strategies Compliance Test               ");
        Console.WriteLine("================================================================");
        Console.WriteLine();

        var errors = new List<string>();
        const int qty = 100; // Default quantity for testing

        //// ================================================================
        //// STRATEGIES - Define your multi-step strategies here
        //// ================================================================
        var strategies = new List<TradingStrategy>
        {
            // ----- VIVS (Contributed by Momentum.) -----
            Stock
                .Ticker("VIVS")
                .SessionDuration(TradingSession.PreMarketEndEarly)
                .IsPriceAbove(2.40)                                       // Step 1: Price >= 2.40
                .IsAboveVwap()                                            // Step 2: Price >= VWAP
                .Buy(quantity: qty, Price.Current)                      // Step 3: Buy @ Current Price
                .TakeProfit(4.00, 4.80)                                 // Step 4: ADX-based TakeProfit: 4.00 (weak) to 4.80 (strong)
                .TrailingStopLoss(Percent.TwentyFive)                   // 25% trailing stop loss
                .ClosePosition(MarketTime.PreMarket.Ending, false)      // Step 5: Close Position @ 9:15 AM ET
                .Build(),

            // ----- CATX (Contributed by Momentum.) -----
            Stock
                .Ticker("CATX")
                .SessionDuration(TradingSession.PreMarketEndEarly)
                .IsPriceAbove(4.00)                                       // Step 1: Price >= 4.00
                .IsAboveVwap()                                            // Step 2: Price >= VWAP
                .Buy(quantity: qty, Price.Current)                      // Step 3: Buy @ Current Price
                .TakeProfit(5.30, 6.16)                                 // Step 4: ADX-based TakeProfit: 5.30 (weak) to 6.16 (strong)
                .TrailingStopLoss(Percent.TwentyFive)                   // 25% trailing stop loss
                .ClosePosition(MarketTime.PreMarket.Ending, false)      // Step 5: Close Position @ 9:15 AM ET
                .Build(),

            // ----- VIVS (Contributed by Claude Opus 4.5) -----
            // Entry on pullback to EMA support while holding above VWAP
            // Wait for dip to $4.15, confirm still above VWAP, buy the bounce
            Stock
                .Ticker("VIVS")
                .SessionDuration(TradingSession.PreMarketEndEarly)
                .Pullback(4.15)                                         // Step 1: Pullback to EMA 12 zone ($4.13)
                .IsAboveVwap()                                            // Step 2: Still above VWAP
                .Buy(quantity: qty, Price.Current)                      // Step 3: Buy @ Current Price
                .TakeProfit(4.80, 5.30)                                 // Step 4: Target $4.80 to $5.30 on bounce
                .TrailingStopLoss(Percent.TwentyFive)                   // 25% trailing stop loss
                .ClosePosition(MarketTime.PreMarket.Ending, false)
                .Build(),

            // ----- CATX (Contributed by Claude Opus 4.5) -----
            // Entry on VWAP reclaim followed by pullback retest
            // Wait for price to reclaim VWAP, then buy pullback to VWAP support
            Stock
                .Ticker("CATX")
                .SessionDuration(TradingSession.PreMarketEndEarly)
                .IsAboveVwap()                                            // Step 1: Wait for VWAP reclaim (~$4.77)
                .Pullback(4.80)                                         // Step 2: Then look for pullback to VWAP
                .Buy(quantity: qty, Price.Current)                      // Step 3: Buy @ Current Price
                .TakeProfit(5.20, 5.50)                                 // Step 4: Target $5.20 to $5.50 on bounce
                .TrailingStopLoss(Percent.TwentyFive)                   // 25% trailing stop loss
                .ClosePosition(MarketTime.PreMarket.Ending, false)
                .Build(),

            // ----- VIVS Full Strategy -----
            Stock
                .Ticker("VIVS")
                .SessionDuration(TradingSession.PreMarketEndEarly)
                .IsPriceAbove(2.40)                                       // Step 1: Price >= 2.40
                .IsAboveVwap()                                            // Step 2: Price >= VWAP
                .Buy(quantity: 500, Price.Current)                      // Step 3: Buy 500 @ Current Price
                .TakeProfit(4.00, 4.80)                                 // Step 4: ADX-based TakeProfit: 4.00 (weak) to 4.80 (strong)
                .TrailingStopLoss(Percent.TwentyFive)                   // 25% trailing stop loss
                .ClosePosition(MarketTime.PreMarket.Ending, false)      // Step 5: Close Position @ 9:15 AM ET
                .Build(),

            // ----- CATX Full Strategy -----
            Stock
                .Ticker("CATX")
                .SessionDuration(TradingSession.PreMarketEndEarly)
                .IsPriceAbove(4.00)                                       // Step 1: Price >= 4.00
                .IsAboveVwap()                                            // Step 2: Price >= VWAP
                .Buy(quantity: 500, Price.Current)                      // Step 3: Buy 500 @ Current Price
                .TakeProfit(5.30, 6.16)                                 // Step 4: ADX-based TakeProfit: 5.30 (weak) to 6.16 (strong)
                .TrailingStopLoss(Percent.TwentyFive)                   // 25% trailing stop loss
                .ClosePosition(MarketTime.PreMarket.Ending, false)      // Step 5: Close Position @ 9:15 AM ET
                .Build(),

            // ----- NAMM Breakout Strategy -----
            Stock
                .Ticker("NAMM")
                .Start(MarketTime.PreMarket.Start)
                .Breakout(7.10)
                .Pullback(6.80)
                .IsAboveVwap()
                .Buy(quantity: 100, Price.Current)
                .TakeProfit(9.00)
                .StopLoss(6.50)
                .ClosePosition(MarketTime.PreMarket.Ending)
                .End(MarketTime.PreMarket.End),

            // ----- FEED Breakout Strategy -----
            Stock
                .Ticker("FEED")
                .Breakout(5.50)
                .Pullback(5.20)
                .IsAboveVwap()
                .Buy(quantity: 500, Price.Current)
                .TakeProfit(6.00)
                .OutsideRTH(true)
                .Build(),

            // ----- AUST Breakout Strategy -----
            Stock
                .Ticker("AUST")
                .Breakout(3.00)
                .Pullback(2.80)
                .IsAboveVwap()
                .Buy(quantity: 2000, Price.Current)
                .TakeProfit(3.25)
                .OutsideRTH(true)
                .Build(),
        };

        // Iterate and validate each strategy
        Console.WriteLine($"Testing {strategies.Count} strategies...");
        Console.WriteLine();

        for (int i = 0; i < strategies.Count; i++)
        {
            var strategy = strategies[i];
            try
            {
                Assert.That(strategy, Is.Not.Null, $"Strategy {i + 1} should not be null");
                Assert.That(strategy.Symbol, Is.Not.Null.And.Not.Empty, $"Strategy {i + 1} should have a ticker symbol");
                Assert.That(strategy.Conditions, Is.Not.Null, $"Strategy {i + 1} should have conditions");
                Assert.That(strategy.Order, Is.Not.Null, $"Strategy {i + 1} should have an order");

                Console.WriteLine($"  ✓ Strategy {i + 1}: {strategy.Symbol} - Valid ({strategy.Conditions.Count} conditions)");
            }
            catch (Exception ex)
            {
                var error = $"Strategy {i + 1} failed: {ex.Message}";
                errors.Add(error);
                Console.WriteLine($"  ✗ {error}");
            }
        }

        Console.WriteLine();
        Console.WriteLine("================================================================");

        if (errors.Count > 0)
        {
            Console.WriteLine($"FAILED: {errors.Count} strategy/strategies failed compliance.");
            Assert.Fail(string.Join(Environment.NewLine, errors));
        }
        else
        {
            Console.WriteLine($"PASSED: All {strategies.Count} strategies are compliant.");
        }
    }
}
