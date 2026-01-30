// ================================================================
// IdiotProof Multi-Stage Strategy Bot
// Powered by IBKR API
// ================================================================
//
// Define your strategies using the fluent API:
//
//   Stock.Ticker("SYMBOL")
//       .Breakout(level)      // Price >= level
//       .Pullback(level)      // Price <= level
//       .AboveVwap()          // Price >= VWAP
//       .Buy(quantity, takeProfit: price)
//
// ================================================================

using IBApi;
using IdiotProof.Enums;
using IdiotProof.Helpers;
using IdiotProof.Logging;
using IdiotProof.Models;

namespace IdiotProof
{
    internal sealed class Program
    {

        public static void Main(string[] args)
        {
            // Setup crash handler and console capture
            CrashHandler.Setup();

            try
            {
                Run();
            }
            catch (Exception ex)
            {
                CrashHandler.WriteCrashDump(ex, "Main Thread Exception");
                throw; // Re-throw to let the OS know the app crashed
            }
        }

        private static void Run()
        {

            var qty = 1;

            // ================================================================
            // STRATEGIES - Define your multi-step strategies here
            // ================================================================
            var strategies = new List<TradingStrategy>
            {

                // ----- VIVS (Contributed by Momentum.) -----
                Stock
                    .Ticker("VIVS")
                    .SessionDuration(TradingSession.PreMarketEndEarly)
                    .PriceAbove(2.40)                                       // Step 1: Price >= 2.40
                    .AboveVwap()                                            // Step 2: Price >= VWAP
                    .Buy(quantity: qty, Price.Current)                      // Step 3: Buy @ Current Price
                    .TakeProfit(4.00, 4.80)                                 // Step 4: ADX-based TakeProfit: 4.00 (weak) to 4.80 (strong)
                    .TrailingStopLoss(Percent.TwentyFive)                   // 25% trailing stop loss
                    .ClosePosition(MarketTime.PreMarket.Ending, false),     // Step 5: Close Position @ 9:15 AM ET

                // ----- CATX (Contributed by Momentum.) -----
                Stock
                    .Ticker("CATX")
                    .SessionDuration(TradingSession.PreMarketEndEarly)
                    .PriceAbove(4.00)                                       // Step 1: Price >= 4.00
                    .AboveVwap()                                            // Step 2: Price >= VWAP
                    .Buy(quantity: qty, Price.Current)                      // Step 3: Buy @ Current Price
                    .TakeProfit(5.30, 6.16)                                 // Step 4: ADX-based TakeProfit: 5.30 (weak) to 6.16 (strong)
                    .TrailingStopLoss(Percent.TwentyFive)                   // 25% trailing stop loss
                    .ClosePosition(MarketTime.PreMarket.Ending, false),     // Step 5: Close Position @ 9:15 AM ET

                // ----- VIVS (Contributed by Claude Opus 4.5) -----
                // Entry on pullback to EMA support while holding above VWAP
                // Wait for dip to $4.15, confirm still above VWAP, buy the bounce
                Stock
                    .Ticker("VIVS")
                    .SessionDuration(TradingSession.PreMarketEndEarly)
                    .Pullback(4.15)                                         // Step 1: Pullback to EMA 12 zone ($4.13)
                    .AboveVwap()                                            // Step 2: Still above VWAP
                    .Buy(quantity: qty, Price.Current)                      // Step 3: Buy @ Current Price
                    .TakeProfit(4.80, 5.30)                                 // Step 4: Target $4.80 to $5.30 on bounce
                    .TrailingStopLoss(Percent.TwentyFive)                   // 25% trailing stop loss
                    .ClosePosition(MarketTime.PreMarket.Ending, false),

                // ----- (Contributed by Claude Opus 4.5) -----
                // Entry on VWAP reclaim followed by pullback retest
                // Wait for price to reclaim VWAP, then buy pullback to VWAP support
                Stock
                    .Ticker("CATX")
                    .SessionDuration(TradingSession.PreMarketEndEarly)
                    .AboveVwap()                                            // Step 1: Wait for VWAP reclaim (~$4.77)
                    .Pullback(4.80)                                         // Step 2: Then look for pullback to VWAP
                    .Buy(quantity: qty, Price.Current)                      // Step 3: Buy @ Current Price
                    .TakeProfit(5.20, 5.50)                                 // Step 4: Target $5.20 to $5.50 on bounce
                    .TrailingStopLoss(Percent.TwentyFive)                   // 25% trailing stop loss
                    .ClosePosition(MarketTime.PreMarket.Ending, false),
            };

            if (!IsValid())
            {
                Console.WriteLine("ERROR: Strategy validation failed. Check configuration.");
                return;
            }

            // ================================================================
            // STARTUP
            // ================================================================
            Gui.ConfigureConsole();
            Gui.DisplayBanner();

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

            Console.WriteLine($"Connecting to IBKR at {Settings.Host}:{Settings.Port}...");
            client.eConnect(Settings.Host, Settings.Port, Settings.ClientId);

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

            if (!wrapper.WaitForNextValidId(TimeSpan.FromSeconds(Settings.ConnectionTimeoutSeconds)))
            {
                Console.WriteLine("ERROR: Connection failed. Check TWS/Gateway.");
                Shutdown(client, wrapper);
                return;
            }

            Console.WriteLine("Connected successfully!");
            Gui.DisplayTradingMode();

            // Display existing open orders from IBKR
            Gui.DisplayOpenOrders(wrapper);

            // ================================================================
            // SUBSCRIBE TO MARKET DATA (before displaying strategies)
            // ================================================================
            var tickerIds = new List<int>();
            var contracts = new Dictionary<string, Contract>();
            var prices = new Dictionary<string, double>();
            int baseTickerId = 1001;

            Console.WriteLine("Fetching current prices...");

            foreach (var strategy in enabledStrategies)
            {
                // Skip if we already subscribed to this symbol
                if (contracts.ContainsKey(strategy.Symbol))
                    continue;

                int tickerId = baseTickerId++;

                var contract = new Contract
                {
                    Symbol = strategy.Symbol,
                    SecType = strategy.SecType,
                    Exchange = strategy.Exchange,
                    PrimaryExch = strategy.PrimaryExchange ?? "",
                    Currency = strategy.Currency
                };

                contracts[strategy.Symbol] = contract;
                prices[strategy.Symbol] = 0;

                // Register handler to capture price
                wrapper.RegisterTickerHandler(tickerId, (price, size) =>
                {
                    prices[strategy.Symbol] = price;
                });

                // Request market data
                client.reqMktData(tickerId, contract, "", false, false, null);
                tickerIds.Add(tickerId);
            }

            // Wait briefly for prices to arrive (up to 10 seconds)
            var priceWaitStart = DateTime.UtcNow;
            while ((DateTime.UtcNow - priceWaitStart).TotalSeconds < Settings.ConnectionTimeoutSeconds)
            {
                if (prices.Values.All(p => p > 0))
                    break;
                Thread.Sleep(100);
            }

            int pricesReceived = prices.Values.Count(p => p > 0);
            Gui.DisplayPriceStatus(pricesReceived, prices.Count);

            // Display strategies for review WITH current prices
            Gui.DisplayStrategies(enabledStrategies, prices);
            Console.WriteLine();

            // Trading is paused until user explicitly activates
            bool isActive = false;

            Gui.DisplayActivationPrompt();

            // Wait for CTRL+ALT+ENTER to activate trading
            while (!isActive)
            {
                var key = Console.ReadKey(intercept: true);
                if (key.Modifiers == (ConsoleModifiers.Control | ConsoleModifiers.Alt))
                {
                    if (key.Key == ConsoleKey.Enter)
                    {
                        isActive = true;
                        Gui.DisplayTradingActivated();
                    }
                    else if (key.Key == ConsoleKey.C)
                    {
                        CancelAllOpenOrders(wrapper);
                    }
                    else if (key.Key == ConsoleKey.Q)
                    {
                        Shutdown(client, wrapper, tickerIds);
                        return;
                    }
                }
            }

            // ================================================================
            // START STRATEGIES
            // ================================================================
            var runners = new List<StrategyRunner>();

            foreach (var strategy in enabledStrategies)
            {
                var contract = contracts[strategy.Symbol];
                var runner = new StrategyRunner(strategy, contract, wrapper, client);
                runners.Add(runner);

                // Find the tickerId for this symbol and re-route to the runner
                int tickerIndex = enabledStrategies
                    .Where(s => !enabledStrategies.Take(enabledStrategies.IndexOf(s)).Any(prev => prev.Symbol == s.Symbol))
                    .ToList()
                    .FindIndex(s => s.Symbol == strategy.Symbol);

                if (tickerIndex >= 0)
                {
                    int tickerId = 1001 + tickerIndex;
                    wrapper.RegisterTickerHandler(tickerId, runner.OnLastTrade);
                }
            }

            // ================================================================
            // SETUP RECONNECTION HANDLERS
            // ================================================================
            // These handlers respond to IBKR connectivity events:
            // - Error 1100: Connection lost - display warning, strategies pause
            // - Error 1101: Connection restored but data lost - resubscribe to market data
            // - Error 1102: Connection restored with data maintained - resume immediately
            //
            // Strategy state (positions, trailing stops, condition progress) is preserved
            // during disconnects. Only market data subscriptions need to be restored.
            // ================================================================

            wrapper.OnConnectionLost += () =>
            {
                Gui.DisplayConnectionLost();
            };

            wrapper.OnConnectionRestored += (dataLost) =>
            {
                Gui.DisplayConnectionRestored(dataLost);

                if (dataLost)
                {
                    // Resubscribe to market data when data was lost during disconnect
                    // Error 1101 indicates subscriptions were invalidated
                    ResubscribeMarketData(client, contracts, tickerIds);
                }
            };

            // Display strategies with initial progress (step 0 = waiting on first condition)
            Console.WriteLine($"Running... (CTRL+ALT+C to cancel orders, CTRL+ALT+Q to quit)");
            Console.WriteLine();

            // Start heartbeat timer to verify API connection and show current prices
            using var heartbeatTimer = StartHeartbeat(wrapper, runners);

            // Wait for CTRL+ALT+Q to stop
            while (true)
            {
                var key = Console.ReadKey(intercept: true);
                if (key.Modifiers == (ConsoleModifiers.Control | ConsoleModifiers.Alt))
                {
                    if (key.Key == ConsoleKey.C)
                    {
                        CancelAllOpenOrders(wrapper);
                    }
                    else if (key.Key == ConsoleKey.Q)
                    {
                        break;
                    }
                }
            }

            // ================================================================
            // CLEANUP
            // ================================================================
            Shutdown(client, wrapper, tickerIds, runners);
        }

        private static bool IsValid()
        {
            //TODO: Add exhausive error handling/logging here to verify strategies are valid before starting...
            return true;
        }

        /// <summary>
        /// Configures the console window size and buffer.
        /// Note: Only works in legacy Command Prompt (conhost.exe), not Windows Terminal.
        /// </summary>
        /// <param name="preferredRows">Desired window height in rows.</param>
        /// <param name="preferredColumns">Desired window width in columns.</param>
        private static void ConfigureConsole(int preferredRows, int preferredColumns)
        {
            try
            {
                // Only works on Windows
                if (!OperatingSystem.IsWindows())
                    return;

                // Set console title
                Console.Title = "IdiotProof Strategy Bot";

                // Get the largest possible window size for the current display
                int maxWidth = Console.LargestWindowWidth;
                int maxHeight = Console.LargestWindowHeight;

                // Clamp to screen limits (leave some margin)
                int width = Math.Min(preferredColumns, maxWidth - 2);
                int height = Math.Min(preferredRows, maxHeight - 2);

                // Ensure minimum size
                width = Math.Max(width, 80);
                height = Math.Max(height, 25);

                // First shrink window to minimum to allow buffer resize
                Console.SetWindowSize(1, 1);

                // Set buffer size (must be >= window size)
                Console.BufferWidth = width;
                Console.BufferHeight = 9999; // Large buffer for scrollback

                // Now set window size
                Console.SetWindowSize(width, height);
            }
            catch
            {
                // Windows Terminal doesn't support SetWindowSize - that's OK
                try
                {
                    Console.Title = "IdiotProof Strategy Bot";
                }
                catch
                {
                    // Ignore
                }
            }
        }

        /// <summary>
        /// Starts a heartbeat timer that periodically pings the IBKR API to verify connection.
        /// When ping succeeds, displays open orders from IBKR.
        /// </summary>
        /// <param name="wrapper">The IB wrapper instance.</param>
        /// <param name="runners">List of active strategy runners.</param>
        /// <returns>A Timer that should be disposed when no longer needed.</returns>
        private static Timer StartHeartbeat(IbWrapper wrapper, List<StrategyRunner> runners)
        {
            var interval = Settings.Heartbeat;

            void HeartbeatCallback(object? state)
            {
                try
                {
                    var pingResult = wrapper.Ping(TimeSpan.FromSeconds(10));

                    var easternZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
                    var easternTime = TimeZoneInfo.ConvertTime(DateTime.Now, easternZone);
                    var timestamp = easternTime.ToString("hh:mm:ss tt");

                    if (pingResult.Success)
                    {
                        Gui.DisplayHeartbeatSuccess(timestamp, pingResult.LatencyMs, pingResult.ServerTimeUtc);

                        // Display open orders from IBKR
                        Gui.DisplayOpenOrders(wrapper);
                    }
                    else
                    {
                        Gui.DisplayHeartbeatFailed(timestamp);
                    }
                }
                catch (Exception ex)
                {
                    Gui.DisplayHeartbeatError(ex.Message);
                }
            }

            // Start the timer - first tick after interval, then repeats at interval
            var timer = new Timer(HeartbeatCallback, null, interval, interval);

            Gui.DisplayHeartbeatEnabled(interval.TotalMinutes);

            return timer;
        }

        /// <summary>
        /// Performs graceful shutdown of all resources.
        /// </summary>
        private static void Shutdown(EClientSocket client, IbWrapper wrapper, List<int>? tickerIds = null, List<StrategyRunner>? runners = null)
        {
            Console.WriteLine("Shutting down...");

            if (tickerIds != null)
            {
                foreach (int tickerId in tickerIds)
                {
                    client.cancelMktData(tickerId);
                    wrapper.UnregisterTickerHandler(tickerId);
                }
            }

            if (runners != null)
            {
                foreach (var runner in runners)
                {
                    runner.Dispose();
                }
            }

            client.eDisconnect();
            wrapper.Dispose();

            Console.WriteLine("Disconnected. Goodbye!");
        }

        /// <summary>
        /// Resubscribes to market data for all contracts after connection is restored.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This method is called when IBKR connectivity is restored with data loss (error code 1101).
        /// When the API reports error 1101, all market data subscriptions are invalidated and must
        /// be re-established to resume receiving price updates.
        /// </para>
        /// 
        /// <para><b>IBKR Error Codes:</b></para>
        /// <list type="bullet">
        ///   <item><term>1100</term><description>Connectivity lost - no action needed here</description></item>
        ///   <item><term>1101</term><description>Connectivity restored, data lost - THIS triggers resubscription</description></item>
        ///   <item><term>1102</term><description>Connectivity restored, data maintained - no resubscription needed</description></item>
        /// </list>
        /// 
        /// <para><b>Process:</b></para>
        /// <list type="number">
        ///   <item>Cancel any stale market data subscriptions (may already be gone)</item>
        ///   <item>Wait 100ms between cancel and resubscribe to avoid rate limiting</item>
        ///   <item>Re-request market data using the same ticker IDs</item>
        /// </list>
        /// 
        /// <para><b>Important:</b> The ticker handler registrations (set up in <c>Run()</c>) are preserved
        /// during disconnects, so the <see cref="StrategyRunner"/> instances will automatically receive
        /// data once the resubscription completes.</para>
        /// </remarks>
        /// <param name="client">The EClientSocket instance used to communicate with IBKR.</param>
        /// <param name="contracts">Dictionary mapping symbol names to their Contract definitions.</param>
        /// <param name="tickerIds">List of ticker IDs corresponding to each contract subscription.</param>
        /// <seealso cref="IbWrapper.OnConnectionRestored"/>
        private static void ResubscribeMarketData(EClientSocket client, Dictionary<string, Contract> contracts, List<int> tickerIds)
        {
            Gui.DisplayResubscribingMarketData(contracts.Count);

            int tickerIndex = 0;
            foreach (var kvp in contracts)
            {
                if (tickerIndex < tickerIds.Count)
                {
                    int tickerId = tickerIds[tickerIndex];
                    // Cancel existing subscription first (in case it's stale)
                    try
                    {
                        client.cancelMktData(tickerId);
                    }
                    catch
                    {
                        // Ignore errors when cancelling - subscription may already be gone
                    }

                    // Small delay between cancel and resubscribe
                    Thread.Sleep(100);

                    // Resubscribe
                    client.reqMktData(tickerId, kvp.Value, "", false, false, null);
                    tickerIndex++;
                }
            }

            Gui.DisplayResubscriptionComplete();
        }

        /// <summary>
        /// Cancels all open orders in IBKR.
        /// </summary>
        private static void CancelAllOpenOrders(IbWrapper wrapper)
        {
            // Get current order count before cancelling
            wrapper.RequestOpenOrdersAndWait(TimeSpan.FromSeconds(3));
            int orderCount = wrapper.OpenOrders.Count;

            if (orderCount == 0)
            {
                Gui.DisplayCancelOrdersResult(0, 0, 0);
                return;
            }

            Gui.DisplayCancellingOrders(orderCount);
            wrapper.CancelAllOrders();

            // Wait a moment for cancellations to process, then refresh
            Thread.Sleep(500);
            wrapper.RequestOpenOrdersAndWait(TimeSpan.FromSeconds(3));

            int remainingCount = wrapper.OpenOrders.Count;
            int cancelledCount = orderCount - remainingCount;

            Gui.DisplayCancelOrdersResult(orderCount, cancelledCount, remainingCount);
        }
    }
}
