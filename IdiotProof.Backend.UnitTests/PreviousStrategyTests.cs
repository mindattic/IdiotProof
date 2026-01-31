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





        //// ================================================================
        //// STRATEGIES - Define your multi-step strategies here
        //// ================================================================
        //var strategies = new List<TradingStrategy>
        //{

        //    // ----- VIVS (Contributed by Momentum.) -----
        //    Stock
        //        .Ticker("VIVS")
        //        .SessionDuration(TradingSession.PreMarketEndEarly)
        //        .PriceAbove(2.40)                                       // Step 1: Price >= 2.40
        //        .AboveVwap()                                            // Step 2: Price >= VWAP
        //        .Buy(quantity: qty, Price.Current)                      // Step 3: Buy @ Current Price
        //        .TakeProfit(4.00, 4.80)                                 // Step 4: ADX-based TakeProfit: 4.00 (weak) to 4.80 (strong)
        //        .TrailingStopLoss(Percent.TwentyFive)                   // 25% trailing stop loss
        //        .ClosePosition(MarketTime.PreMarket.Ending, false),     // Step 5: Close Position @ 9:15 AM ET

        //    // ----- CATX (Contributed by Momentum.) -----
        //    Stock
        //        .Ticker("CATX")
        //        .SessionDuration(TradingSession.PreMarketEndEarly)
        //        .PriceAbove(4.00)                                       // Step 1: Price >= 4.00
        //        .AboveVwap()                                            // Step 2: Price >= VWAP
        //        .Buy(quantity: qty, Price.Current)                      // Step 3: Buy @ Current Price
        //        .TakeProfit(5.30, 6.16)                                 // Step 4: ADX-based TakeProfit: 5.30 (weak) to 6.16 (strong)
        //        .TrailingStopLoss(Percent.TwentyFive)                   // 25% trailing stop loss
        //        .ClosePosition(MarketTime.PreMarket.Ending, false),     // Step 5: Close Position @ 9:15 AM ET

        //    // ----- VIVS (Contributed by Claude Opus 4.5) -----
        //    // Entry on pullback to EMA support while holding above VWAP
        //    // Wait for dip to $4.15, confirm still above VWAP, buy the bounce
        //    Stock
        //        .Ticker("VIVS")
        //        .SessionDuration(TradingSession.PreMarketEndEarly)
        //        .Pullback(4.15)                                         // Step 1: Pullback to EMA 12 zone ($4.13)
        //        .AboveVwap()                                            // Step 2: Still above VWAP
        //        .Buy(quantity: qty, Price.Current)                      // Step 3: Buy @ Current Price
        //        .TakeProfit(4.80, 5.30)                                 // Step 4: Target $4.80 to $5.30 on bounce
        //        .TrailingStopLoss(Percent.TwentyFive)                   // 25% trailing stop loss
        //        .ClosePosition(MarketTime.PreMarket.Ending, false),

        //    // ----- (Contributed by Claude Opus 4.5) -----
        //    // Entry on VWAP reclaim followed by pullback retest
        //    // Wait for price to reclaim VWAP, then buy pullback to VWAP support
        //    Stock
        //        .Ticker("CATX")
        //        .SessionDuration(TradingSession.PreMarketEndEarly)
        //        .AboveVwap()                                            // Step 1: Wait for VWAP reclaim (~$4.77)
        //        .Pullback(4.80)                                         // Step 2: Then look for pullback to VWAP
        //        .Buy(quantity: qty, Price.Current)                      // Step 3: Buy @ Current Price
        //        .TakeProfit(5.20, 5.50)                                 // Step 4: Target $5.20 to $5.50 on bounce
        //        .TrailingStopLoss(Percent.TwentyFive)                   // 25% trailing stop loss
        //        .ClosePosition(MarketTime.PreMarket.Ending, false),
        //};











        // ================================================================
        // VIVS Strategy (Premarket)
        // ================================================================
        Console.WriteLine("Testing VIVS Strategy...");
        try
        {
            TradingStrategy vivs = Stock
                .Ticker("VIVS")
                .SessionDuration(TradingSession.PreMarketEndEarly)
                .PriceAbove(2.40)                                       // Step 1: Price >= 2.40
                .AboveVwap()                                            // Step 2: Price >= VWAP
                .Buy(quantity: 500, Price.Current)                      // Step 3: Buy 500 @ Current Price
                .TakeProfit(4.00, 4.80)                                 // Step 4: ADX-based TakeProfit: 4.00 (weak) to 4.80 (strong)
                .TrailingStopLoss(Percent.TwentyFive)                   // 25% trailing stop loss
                .ClosePosition(MarketTime.PreMarket.Ending, false);     // Step 5: Close Position @ 9:15 AM ET

            Assert.That(vivs.Symbol, Is.EqualTo("VIVS"));
            Assert.That(vivs.Enabled, Is.True);
            Assert.That(vivs.Conditions, Has.Count.EqualTo(2));
            Assert.That(vivs.Order.Side, Is.EqualTo(OrderSide.Buy));
            Assert.That(vivs.Order.Quantity, Is.EqualTo(500));
            Assert.That(vivs.Order.EnableTakeProfit, Is.True);
            Assert.That(vivs.Order.AdxTakeProfit, Is.Not.Null);
            Assert.That(vivs.Order.AdxTakeProfit!.ConservativeTarget, Is.EqualTo(4.00));
            Assert.That(vivs.Order.AdxTakeProfit.AggressiveTarget, Is.EqualTo(4.80));
            Assert.That(vivs.Order.EnableTrailingStopLoss, Is.True);
            Assert.That(vivs.Order.TrailingStopLossPercent, Is.EqualTo(0.25));
            Assert.That(vivs.Order.ClosePositionTime, Is.EqualTo(MarketTime.PreMarket.Ending));
            Assert.That(vivs.Order.ClosePositionOnlyIfProfitable, Is.False);

            Console.WriteLine("  [OK] VIVS: PriceAbove(2.40) -> AboveVwap -> Buy(500)");
            Console.WriteLine($"       TakeProfit: ${vivs.Order.AdxTakeProfit.ConservativeTarget}-${vivs.Order.AdxTakeProfit.AggressiveTarget} | TrailingStopLoss: {vivs.Order.TrailingStopLossPercent * 100}%");
        }
        catch (Exception ex)
        {
            errors.Add($"VIVS: {ex.Message}");
            Console.WriteLine($"  [FAIL] VIVS: {ex.Message}");
        }
        Console.WriteLine();

        // ================================================================
        // CATX Strategy (Premarket)
        // ================================================================
        Console.WriteLine("Testing CATX Strategy...");
        try
        {
            TradingStrategy catx = Stock
                .Ticker("CATX")
                .SessionDuration(TradingSession.PreMarketEndEarly)
                .PriceAbove(4.00)                                       // Step 1: Price >= 4.00
                .AboveVwap()                                            // Step 2: Price >= VWAP
                .Buy(quantity: 500, Price.Current)                      // Step 3: Buy 500 @ Current Price
                .TakeProfit(5.30, 6.16)                                 // Step 4: ADX-based TakeProfit: 5.30 (weak) to 6.16 (strong)
                .TrailingStopLoss(Percent.TwentyFive)                   // 25% trailing stop loss
                .ClosePosition(MarketTime.PreMarket.Ending, false);     // Step 5: Close Position @ 9:15 AM ET

            Assert.That(catx.Symbol, Is.EqualTo("CATX"));
            Assert.That(catx.Enabled, Is.True);
            Assert.That(catx.Conditions, Has.Count.EqualTo(2));
            Assert.That(catx.Order.Side, Is.EqualTo(OrderSide.Buy));
            Assert.That(catx.Order.Quantity, Is.EqualTo(500));
            Assert.That(catx.Order.EnableTakeProfit, Is.True);
            Assert.That(catx.Order.AdxTakeProfit, Is.Not.Null);
            Assert.That(catx.Order.AdxTakeProfit!.ConservativeTarget, Is.EqualTo(5.30));
            Assert.That(catx.Order.AdxTakeProfit.AggressiveTarget, Is.EqualTo(6.16));
            Assert.That(catx.Order.EnableTrailingStopLoss, Is.True);
            Assert.That(catx.Order.TrailingStopLossPercent, Is.EqualTo(0.25));
            Assert.That(catx.Order.ClosePositionTime, Is.EqualTo(MarketTime.PreMarket.Ending));
            Assert.That(catx.Order.ClosePositionOnlyIfProfitable, Is.False);

            Console.WriteLine("  [OK] CATX: PriceAbove(4.00) -> AboveVwap -> Buy(500)");
            Console.WriteLine($"       TakeProfit: ${catx.Order.AdxTakeProfit.ConservativeTarget}-${catx.Order.AdxTakeProfit.AggressiveTarget} | TrailingStopLoss: {catx.Order.TrailingStopLossPercent * 100}%");
        }
        catch (Exception ex)
        {
            errors.Add($"CATX: {ex.Message}");
            Console.WriteLine($"  [FAIL] CATX: {ex.Message}");
        }
        Console.WriteLine();

        // ================================================================
        // NAMM Strategy
        // ================================================================
        Console.WriteLine("Testing NAMM Strategy...");
        try
        {
            TradingStrategy namm = Stock
                .Ticker("NAMM")
                .Start(MarketTime.PreMarket.Start)
                .Breakout(7.10)
                .Pullback(6.80)
                .AboveVwap()
                .Buy(quantity: 100, Price.Current)
                .TakeProfit(9.00)
                .StopLoss(6.50)
                .ClosePosition(MarketTime.PreMarket.Ending)
                .End(MarketTime.PreMarket.End);

            Assert.That(namm.Symbol, Is.EqualTo("NAMM"));
            Assert.That(namm.Enabled, Is.True);
            Assert.That(namm.Conditions, Has.Count.EqualTo(3));
            Assert.That(namm.Order.Side, Is.EqualTo(OrderSide.Buy));
            Assert.That(namm.Order.Quantity, Is.EqualTo(100));
            Assert.That(namm.Order.EnableTakeProfit, Is.True);
            Assert.That(namm.Order.TakeProfitPrice, Is.EqualTo(9.00));
            Assert.That(namm.Order.EnableStopLoss, Is.True);
            Assert.That(namm.Order.StopLossPrice, Is.EqualTo(6.50));
            Assert.That(namm.StartTime, Is.EqualTo(MarketTime.PreMarket.Start));
            Assert.That(namm.EndTime, Is.EqualTo(MarketTime.PreMarket.End));
            Assert.That(namm.Order.ClosePositionTime, Is.EqualTo(MarketTime.PreMarket.Ending));

            Console.WriteLine("  [OK] NAMM: Breakout(7.10) -> Pullback(6.80) -> AboveVwap -> Buy(100)");
            Console.WriteLine($"       TakeProfit: ${namm.Order.TakeProfitPrice} | StopLoss: ${namm.Order.StopLossPrice}");
            Console.WriteLine($"       Window: {namm.StartTime:h:mm tt} - {namm.EndTime:h:mm tt} ET");
        }
        catch (Exception ex)
        {
            errors.Add($"NAMM: {ex.Message}");
            Console.WriteLine($"  [FAIL] NAMM: {ex.Message}");
        }
        Console.WriteLine();

        // ================================================================
        // FEED Strategy
        // ================================================================
        Console.WriteLine("Testing FEED Strategy...");
        try
        {
            TradingStrategy feed = Stock
                .Ticker("FEED")
                .Breakout(5.50)
                .Pullback(5.20)
                .AboveVwap()
                .Buy(quantity: 500, Price.Current)
                .TakeProfit(6.00)
                .OutsideRTH(true);

            Assert.That(feed.Symbol, Is.EqualTo("FEED"));
            Assert.That(feed.Enabled, Is.True);
            Assert.That(feed.Conditions, Has.Count.EqualTo(3));
            Assert.That(feed.Order.Side, Is.EqualTo(OrderSide.Buy));
            Assert.That(feed.Order.Quantity, Is.EqualTo(500));
            Assert.That(feed.Order.EnableTakeProfit, Is.True);
            Assert.That(feed.Order.TakeProfitPrice, Is.EqualTo(6.00));
            Assert.That(feed.Order.OutsideRth, Is.True);
            Assert.That(feed.StartTime, Is.Null);
            Assert.That(feed.EndTime, Is.Null);

            Console.WriteLine("  [OK] FEED: Breakout(5.50) -> Pullback(5.20) -> AboveVwap -> Buy(500)");
            Console.WriteLine($"       TakeProfit: ${feed.Order.TakeProfitPrice} | OutsideRTH: {feed.Order.OutsideRth}");
            Console.WriteLine("       Window: None (always active)");
        }
        catch (Exception ex)
        {
            errors.Add($"FEED: {ex.Message}");
            Console.WriteLine($"  [FAIL] FEED: {ex.Message}");
        }
        Console.WriteLine();

        // ================================================================
        // AUST Strategy
        // ================================================================
        Console.WriteLine("Testing AUST Strategy...");
        try
        {
            TradingStrategy aust = Stock
                .Ticker("AUST")
                .Breakout(3.00)
                .Pullback(2.80)
                .AboveVwap()
                .Buy(quantity: 2000, Price.Current)
                .TakeProfit(3.25)
                .OutsideRTH(true);

            Assert.That(aust.Symbol, Is.EqualTo("AUST"));
            Assert.That(aust.Enabled, Is.True);
            Assert.That(aust.Conditions, Has.Count.EqualTo(3));
            Assert.That(aust.Order.Side, Is.EqualTo(OrderSide.Buy));
            Assert.That(aust.Order.Quantity, Is.EqualTo(2000));
            Assert.That(aust.Order.EnableTakeProfit, Is.True);
            Assert.That(aust.Order.TakeProfitPrice, Is.EqualTo(3.25));
            Assert.That(aust.Order.OutsideRth, Is.True);

            Console.WriteLine("  [OK] AUST: Breakout(3.00) -> Pullback(2.80) -> AboveVwap -> Buy(2000)");
            Console.WriteLine($"       TakeProfit: ${aust.Order.TakeProfitPrice} | OutsideRTH: {aust.Order.OutsideRth}");
        }
        catch (Exception ex)
        {
            errors.Add($"AUST: {ex.Message}");
            Console.WriteLine($"  [FAIL] AUST: {ex.Message}");
        }
        Console.WriteLine();

        // ================================================================
        // EXAMPLE (Disabled) Strategy
        // ================================================================
        Console.WriteLine("Testing EXAMPLE (Disabled) Strategy...");
        try
        {
            TradingStrategy example = Stock
                .Ticker("EXAMPLE")
                .Enabled(false)
                .Breakout(10.00)
                .Pullback(9.50)
                .AboveVwap()
                .Buy(quantity: 100, Price.Current);

            Assert.That(example.Symbol, Is.EqualTo("EXAMPLE"));
            Assert.That(example.Enabled, Is.False, "EXAMPLE must be disabled");
            Assert.That(example.Conditions, Has.Count.EqualTo(3));

            Console.WriteLine("  [OK] EXAMPLE: Breakout(10.00) -> Pullback(9.50) -> AboveVwap -> Buy(100)");
            Console.WriteLine($"       Enabled: {example.Enabled} (correctly disabled)");
        }
        catch (Exception ex)
        {
            errors.Add($"EXAMPLE: {ex.Message}");
            Console.WriteLine($"  [FAIL] EXAMPLE: {ex.Message}");
        }
        Console.WriteLine();

        // ================================================================
        // CUSTOM Strategy (with When condition)
        // ================================================================
        Console.WriteLine("Testing CUSTOM Strategy (custom When condition)...");
        try
        {
            TradingStrategy custom = Stock
                .Ticker("CUSTOM")
                .Breakout(5.00)
                .When("Price between 4.50-4.80", (price, vwap) => price >= 4.50 && price <= 4.80)
                .AboveVwap(buffer: 0.02)
                .Buy(quantity: 500, Price.Current)
                .TakeProfit(6.00);

            Assert.That(custom.Symbol, Is.EqualTo("CUSTOM"));
            Assert.That(custom.Enabled, Is.True);
            Assert.That(custom.Conditions, Has.Count.EqualTo(3));
            Assert.That(custom.Conditions[1].Name, Does.Contain("Price between 4.50-4.80"));

            // Test the custom condition logic
            var whenCondition = custom.Conditions[1];
            Assert.That(whenCondition.Evaluate(4.50, 0), Is.True, "4.50 in range");
            Assert.That(whenCondition.Evaluate(4.80, 0), Is.True, "4.80 in range");
            Assert.That(whenCondition.Evaluate(4.49, 0), Is.False, "4.49 below range");
            Assert.That(whenCondition.Evaluate(4.81, 0), Is.False, "4.81 above range");

            // Test AboveVwap with buffer
            var vwapCondition = custom.Conditions[2];
            Assert.That(vwapCondition.Evaluate(5.02, 5.00), Is.True, "5.02 >= 5.00 + 0.02");
            Assert.That(vwapCondition.Evaluate(5.01, 5.00), Is.False, "5.01 < 5.00 + 0.02");

            Console.WriteLine("  [OK] CUSTOM: Breakout(5.00) -> When(4.50-4.80) -> AboveVwap(+0.02) -> Buy(500)");
            Console.WriteLine($"       TakeProfit: ${custom.Order.TakeProfitPrice}");
            Console.WriteLine("       Custom condition: Price between 4.50-4.80 [VALIDATED]");
            Console.WriteLine("       AboveVwap buffer: 0.02 [VALIDATED]");
        }
        catch (Exception ex)
        {
            errors.Add($"CUSTOM: {ex.Message}");
            Console.WriteLine($"  [FAIL] CUSTOM: {ex.Message}");
        }
        Console.WriteLine();

        // ================================================================
        // Collection Test - All strategies together
        // ================================================================
        Console.WriteLine("Testing All Strategies Collection...");
        try
        {
            var strategies = new List<TradingStrategy>
            {
                Stock.Ticker("NAMM").Start(MarketTime.PreMarket.Start).Breakout(7.10).Pullback(6.80).AboveVwap()
                    .Buy(quantity: 100, Price.Current).TakeProfit(9.00).StopLoss(6.50)
                    .ClosePosition(MarketTime.PreMarket.Ending).End(MarketTime.PreMarket.End),

                Stock.Ticker("FEED").Breakout(5.50).Pullback(5.20).AboveVwap()
                    .Buy(quantity: 500, Price.Current).TakeProfit(6.00).OutsideRTH(true).Build(),

                Stock.Ticker("AUST").Breakout(3.00).Pullback(2.80).AboveVwap()
                    .Buy(quantity: 2000, Price.Current).TakeProfit(3.25).OutsideRTH(true).Build(),

                Stock.Ticker("EXAMPLE").Enabled(false).Breakout(10.00).Pullback(9.50).AboveVwap()
                    .Buy(quantity: 100, Price.Current).Build(),

                Stock.Ticker("CUSTOM").Breakout(5.00)
                    .When("Price between 4.50-4.80", (price, vwap) => price >= 4.50 && price <= 4.80)
                    .AboveVwap(buffer: 0.02).Buy(quantity: 500, Price.Current).TakeProfit(6.00).Build(),
            };

            Assert.That(strategies, Has.Count.EqualTo(5));

            var enabled = strategies.FindAll(s => s.Enabled);
            Assert.That(enabled, Has.Count.EqualTo(4), "4 strategies should be enabled");
            Assert.That(enabled.Any(s => s.Symbol == "EXAMPLE"), Is.False, "EXAMPLE filtered out");

            Console.WriteLine($"  [OK] Total strategies: {strategies.Count}");
            Console.WriteLine($"  [OK] Enabled strategies: {enabled.Count}");
            Console.WriteLine($"  [OK] Disabled strategies: {strategies.Count - enabled.Count} (EXAMPLE)");
        }
        catch (Exception ex)
        {
            errors.Add($"Collection: {ex.Message}");
            Console.WriteLine($"  [FAIL] Collection: {ex.Message}");
        }
        Console.WriteLine();

        // ================================================================
        // Summary
        // ================================================================
        Console.WriteLine("================================================================");
        if (errors.Count == 0)
        {
            Console.WriteLine("  ALL PREVIOUS STRATEGIES COMPLIANT");
            Console.WriteLine("================================================================");
        }
        else
        {
            Console.WriteLine($"  {errors.Count} STRATEGY FAILED:");
            foreach (var error in errors)
            {
                Console.WriteLine($"    - {error}");
            }
            Console.WriteLine("================================================================");
            Assert.Fail($"{errors.Count} strategies failed compliance check");
        }
    }
}
