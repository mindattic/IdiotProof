// ============================================================================
// IBKR Multi-Stage Strategy Bot
// ============================================================================
//
// Define your strategies using the fluent API:
//
//   Stock.Ticker("SYMBOL")
//       .Breakout(level)      // Price >= level
//       .Pullback(level)      // Price <= level
//       .AboveVwap()          // Price >= VWAP
//       .Buy(quantity, takeProfit: price)
//
// ============================================================================

using IBApi;
using IdiotProof.Models;
using System;
using System.Collections.Generic;
using System.Threading;

namespace IdiotProof
{
    internal sealed class Program
    {
        public static void Main(string[] args)
        {
            // ================================================================
            // STRATEGIES - Define your multi-step strategies here
            // ================================================================
            var strategies = new List<TradingStrategy>
            {
                // ----- NAMM Strategy -----
                Stock
                    .Ticker("NAMM")
                    .Start(Time.PreMarket.Start)                            // Start monitoring at 3:00 AM CST
                    .Breakout(7.10)                                         // Step 1: Price >= 7.10
                    .Pullback(6.80)                                         // Step 2: Price <= 6.80
                    .AboveVwap()                                            // Step 3: Price >= VWAP
                    .Buy(quantity: 100, Price.Current)                      // Step 4: Buy 100 @ Current Price
                    .TakeProfit(9.00)                                       // Take profit >= 9.00
                    .StopLoss(6.50)                                         // Stop loss <= 6.50
                    .ClosePosition(Time.PreMarket.End.AddMinutes(-10))      // Close Position @ 6:50 AM CST
                    .End(Time.PreMarket.End),                               // Stop monitoring @ 7:00 AM CST

                //// ----- FEED Strategy -----
                //Stock.Ticker("FEED")
                //    .Breakout(5.50)          // Step 1: Price >= 5.50
                //    .Pullback(5.20)          // Step 2: Price <= 5.20
                //    .AboveVwap()             // Step 3: Price >= VWAP
                //    .Buy(
                //        quantity: 500,
                //        takeProfit: 6.00,
                //        outsideRth: true
                //    ),

                //// ----- AUST Strategy -----
                //Stock.Ticker("AUST")
                //    .Breakout(3.00)          // Step 1: Price >= 3.00
                //    .Pullback(2.80)          // Step 2: Price <= 2.80
                //    .AboveVwap()             // Step 3: Price >= VWAP
                //    .Buy(
                //        quantity: 2000,
                //        takeProfitOffset: 0.25,  // Take profit at entry + $0.25
                //        outsideRth: true
                //    ),

                //// ----- Example: Disabled strategy -----
                //Stock.Ticker("EXAMPLE")
                //    .Enabled(false)          // Disabled - won't run
                //    .Breakout(10.00)
                //    .Pullback(9.50)
                //    .AboveVwap()
                //    .Buy(quantity: 100),

                // ----- Example: Custom condition -----
                // Stock.Ticker("CUSTOM")
                //     .Breakout(5.00)
                //     .When("Price between 4.50-4.80", (price, vwap) => price >= 4.50 && price <= 4.80)
                //     .AboveVwap(buffer: 0.02)  // Price >= VWAP + 0.02
                //     .Buy(quantity: 500, takeProfit: 6.00),
            };

            // ================================================================
            // STARTUP
            // ================================================================
            Console.WriteLine("╔═══════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║                    IdiotProof Strategy Bot                    ║");
            Console.WriteLine("╚═══════════════════════════════════════════════════════════════╝");
            Console.WriteLine();

            // Filter to enabled strategies only
            var enabledStrategies = strategies.FindAll(s => s.Enabled);

            if (enabledStrategies.Count == 0)
            {
                Console.WriteLine("ERROR: No enabled strategies found.");
                return;
            }

            // ================================================================
            // IB CONNECTION
            // ================================================================
            var wrapper = new IbWrapper();
            var client = new EClientSocket(wrapper, wrapper.Signal);

            wrapper.AttachClient(client);

            Console.WriteLine($"Connecting to IBKR at {Settings.IB_HOST}:{Settings.IB_PORT}...");
            client.eConnect(Settings.IB_HOST, Settings.IB_PORT, Settings.IB_CLIENT_ID);

            // EReader must be created and started AFTER eConnect
            var reader = new EReader(client, wrapper.Signal);
            reader.Start();

            var readerThread = new Thread(() =>
            {
                while (client.IsConnected())
                {
                    wrapper.Signal.waitForSignal();
                    reader.processMsgs();
                }
            })
            {
                IsBackground = true
            };
            readerThread.Start();

            if (!wrapper.WaitForNextValidId(TimeSpan.FromSeconds(Settings.CONNECTION_TIMEOUT_SECONDS)))
            {
                Console.WriteLine("ERROR: Connection failed. Check TWS/Gateway.");
                return;
            }

            Console.WriteLine("Connected successfully!");

            if (Settings.IsPaperTrading)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Mode: PAPER TRADING");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Mode: LIVE TRADING");
                Console.ResetColor();
            }
            Console.WriteLine();

            // ================================================================
            // START STRATEGIES
            // ================================================================
            var runners = new List<StrategyRunner>();
            var tickerIds = new List<int>();
            int baseTickerId = 1001;

            foreach (var strategy in enabledStrategies)
            {
                int tickerId = baseTickerId++;

                var contract = new Contract
                {
                    Symbol = strategy.Symbol,
                    SecType = strategy.SecType,
                    Exchange = strategy.Exchange,
                    Currency = strategy.Currency
                };

                var runner = new StrategyRunner(strategy, contract, wrapper, client);
                runners.Add(runner);

                // Route market data to this runner
                wrapper.RegisterTickerHandler(tickerId, runner.OnLastTrade);

                // Request market data
                client.reqMktData(tickerId, contract, "", false, false, null);
                tickerIds.Add(tickerId);
            }

            // Display strategies with initial progress (step 0 = waiting on first condition)
            var strategyWord = enabledStrategies.Count == 1 ? "strategy" : "strategies";
            Console.WriteLine($"Loaded {enabledStrategies.Count} {strategyWord}:");
            foreach (var runner in runners)
            {
                Console.WriteLine();
                runner.Strategy.WriteProgress(runner.CurrentStep, runner.EntryFilled, runner.TakeProfitFilled, runner.LastPrice, runner.EntryFillPrice, runner.TakeProfitTarget);
            }
            Console.WriteLine();
            Console.WriteLine("================================================================");
            Console.WriteLine("All strategies running. Press CTRL+ALT+Q to stop.");
            Console.WriteLine("================================================================");

            // Wait for CTRL+ALT+Q to stop
            while (true)
            {
                var key = Console.ReadKey(intercept: true);
                if (key.Modifiers == (ConsoleModifiers.Control | ConsoleModifiers.Alt) && key.Key == ConsoleKey.Q)
                {
                    break;
                }
            }

            // ================================================================
            // CLEANUP
            // ================================================================
            Console.WriteLine("Shutting down...");

            foreach (int tickerId in tickerIds)
            {
                client.cancelMktData(tickerId);
                wrapper.UnregisterTickerHandler(tickerId);
            }

            foreach (var runner in runners)
            {
                runner.Dispose();
            }

            client.eDisconnect();
            Console.WriteLine("Disconnected. Goodbye!");
        }
    }
}
