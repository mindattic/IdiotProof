// ================================================================
// IBKR Multi-Stage Strategy Bot
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
using IdiotProof.Models;

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

                // ----- VIVS (Premarket) -----
                Stock
                    .Ticker("VIVS")
                    .SessionDuration(TradingSession.PreMarketEndEarly)
                    .PriceAbove(2.40)                                       // Step 1: Price >= 2.40
                    .AboveVwap()                                            // Step 2: Price >= VWAP
                    .Buy(quantity: 500, Price.Current)                      // Step 3: Buy 500 @ Current Price
                    .TakeProfit(4.00, 4.80)                                 // Step 4: ADX-based TakeProfit: 4.00 (weak) to 4.80 (strong)
                    .TrailingStopLoss(Percent.TwentyFive)                   // 25% trailing stop loss
                    .ClosePosition(MarketTime.PreMarket.Ending, false),     // Step 5: Close Position @ 9:15 AM ET


                // ----- CATX (Premarket) -----
                Stock
                    .Ticker("CATX")
                    .SessionDuration(TradingSession.PreMarketEndEarly)
                    .PriceAbove(4.00)                                       // Step 1: Price >= 4.00
                    .AboveVwap()                                            // Step 2: Price >= VWAP
                    .Buy(quantity: 500, Price.Current)                      // Step 3: Buy 500 @ Current Price
                    .TakeProfit(5.30, 6.16)                                 // Step 4: ADX-based TakeProfit: 5.30 (weak) to 6.16 (strong)
                    .TrailingStopLoss(Percent.TwentyFive)                   // 25% trailing stop loss
                    .ClosePosition(MarketTime.PreMarket.Ending, false),     // Step 5: Close Position @ 9:15 AM ET

            };

            if (!IsValid())
            {
                Console.WriteLine("ERROR: Strategy validation failed. Check configuration.");
                return;
            }

            // ================================================================
            // STARTUP
            // ================================================================
            ConfigureConsole(preferredRows: 100, preferredColumns: 80);

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("===============================================================");
            Console.WriteLine("                    IdiotProof Strategy Bot                    ");
            Console.WriteLine("===============================================================");
            Console.WriteLine();
            Console.ResetColor();

            // Display timezone configuration
            //DisplayTimezoneInfo();
            //Console.WriteLine();

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
                Shutdown(client, wrapper);
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

            // Display existing open orders from IBKR
            DisplayOpenOrders(wrapper);

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

            // Wait briefly for prices to arrive (up to 3 seconds)
            var priceWaitStart = DateTime.UtcNow;
            while ((DateTime.UtcNow - priceWaitStart).TotalSeconds < 3)
            {
                if (prices.Values.All(p => p > 0))
                    break;
                Thread.Sleep(100);
            }

            int pricesReceived = prices.Values.Count(p => p > 0);
            if (pricesReceived == prices.Count)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"Received prices for {pricesReceived} symbol(s).");
                Console.WriteLine();
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"Received prices for {pricesReceived}/{prices.Count} symbol(s). Some may still be loading...");
                Console.WriteLine();
                Console.ResetColor();
            }

            // Display strategies for review WITH current prices
            DisplayStrategies(enabledStrategies, prices);
            Console.WriteLine();

            // Trading is paused until user explicitly activates
            bool isActive = false;

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("================================================================");
            Console.WriteLine("  PAUSED - Review strategies above before starting.            ");
            Console.WriteLine("  Press CTRL+ALT+ENTER to activate trading.                    ");
            Console.WriteLine("  Press CTRL+ALT+C to cancel all open orders.                  ");
            Console.WriteLine("  Press CTRL+ALT+Q to quit.                                    ");
            Console.WriteLine("================================================================");
            Console.ResetColor();
            Console.WriteLine();

            // Wait for CTRL+ALT+ENTER to activate trading
            while (!isActive)
            {
                var key = Console.ReadKey(intercept: true);
                if (key.Modifiers == (ConsoleModifiers.Control | ConsoleModifiers.Alt))
                {
                    if (key.Key == ConsoleKey.Enter)
                    {
                        isActive = true;
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine(">>> TRADING ACTIVATED <<<");
                        Console.ResetColor();
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

            // Display strategies with initial progress (step 0 = waiting on first condition)
            Console.WriteLine($"Running... (CTRL+ALT+C to cancel orders, CTRL+ALT+Q to quit)");
            Console.WriteLine();

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
        /// Displays enabled strategies for review before trading starts.
        /// </summary>
        private static void DisplayStrategies(List<TradingStrategy> strategies, Dictionary<string, double> prices)
        {
            var strategyWord = strategies.Count == 1 ? "strategy" : "strategies";
            Console.WriteLine($"Loading {strategies.Count} {strategyWord} for review...");

            foreach (var strategy in strategies)
            {
                Console.WriteLine();
                double currentPrice = prices.TryGetValue(strategy.Symbol, out var price) ? price : 0;
                strategy.WriteProgress(currentStep: 0, entryFilled: false, takeProfitFilled: false, 
                    currentPrice: currentPrice, entryPrice: 0, takeProfitTarget: 0);
            }
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
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("No open orders to cancel.");
                Console.ResetColor();
                Console.WriteLine();
                return;
            }

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"Cancelling {orderCount} open order(s)...");
            Console.ResetColor();

            wrapper.CancelAllOrders();

            // Wait a moment for cancellations to process, then refresh
            Thread.Sleep(500);
            wrapper.RequestOpenOrdersAndWait(TimeSpan.FromSeconds(3));

            int remainingCount = wrapper.OpenOrders.Count;
            int cancelledCount = orderCount - remainingCount;

            if (remainingCount == 0)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"Successfully cancelled {cancelledCount} order(s).");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"Cancelled {cancelledCount} order(s). {remainingCount} order(s) still pending.");
                Console.ResetColor();
            }
            Console.WriteLine();
        }

        /// <summary>
        /// Displays all open orders currently in IBKR.
        /// </summary>
        private static void DisplayOpenOrders(IbWrapper wrapper)
        {
            Console.WriteLine();
            Console.WriteLine("Fetching existing orders from IBKR...");

            // Request and wait for open orders
            wrapper.RequestOpenOrdersAndWait(TimeSpan.FromSeconds(5));

            var orders = wrapper.OpenOrders;

            if (orders.Count == 0)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("No open orders.");
                Console.WriteLine();
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"Found {orders.Count} existing order(s):");
                Console.WriteLine("┌─────────┬────────┬────────┬───────┬────────┬──────────┬──────────────┐");
                Console.WriteLine("│ OrderId │ Symbol │ Action │  Qty  │  Type  │   Price  │    Status    │");
                Console.WriteLine("├─────────┼────────┼────────┼───────┼────────┼──────────┼──────────────┤");

                foreach (var (orderId, order) in orders)
                {
                    var priceStr = order.LmtPrice > 0 ? $"${order.LmtPrice:F2}" : "MKT";
                    Console.WriteLine($"│ {orderId,7} │ {order.Symbol,-6} │ {order.Action,-6} │ {order.Qty,5} │ {order.Type,-6} │ {priceStr,8} │ {order.Status,-12} │");
                }

                Console.WriteLine("└─────────┴────────┴────────┴───────┴────────┴──────────┴──────────────┘");
                Console.ResetColor();
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Consider canceling old orders to avoid duplicate fills!");
                Console.ResetColor();
            }
        }

        /// <summary>
        /// Displays the current timezone configuration and market hours.
        /// </summary>
        private static void DisplayTimezoneInfo()
        {
            var info = TimezoneHelper.GetTimezoneDisplayInfo(Settings.Timezone);
            var currentTime = TimezoneHelper.GetCurrentTime(Settings.Timezone);
            var currentEastern = TimezoneHelper.GetCurrentTime(MarketTimeZone.EST);
            var tz = info.Abbreviation;

            Console.WriteLine($"{"Timezone:",-14} {tz} ({info.TimeZoneInfo.DisplayName})");
            Console.WriteLine($"{"Current Time:",-14} {currentTime:h:mm:ss tt} {tz,-4} ({currentEastern:h:mm:ss tt} ET)");
            Console.WriteLine($"{"Pre-Market:",-14} {MarketTime.PreMarket.StartLocal:h:mm tt} - {MarketTime.PreMarket.EndLocal:h:mm tt} {tz,-4} ({MarketTime.PreMarket.Start:h:mm tt} - {MarketTime.PreMarket.End:h:mm tt} ET)");
            Console.WriteLine($"{"Market Open:",-14} {MarketTime.RTH.StartLocal:h:mm tt} {tz,-16} ({MarketTime.RTH.Start:h:mm tt} ET)");
            Console.WriteLine($"{"Market Close:",-14} {MarketTime.RTH.EndLocal:h:mm tt} {tz,-16} ({MarketTime.RTH.End:h:mm tt} ET)");
            Console.WriteLine($"{"After-Hours:",-14} {MarketTime.AfterHours.StartLocal:h:mm tt} - {MarketTime.AfterHours.EndLocal:h:mm tt} {tz,-4} ({MarketTime.AfterHours.Start:h:mm tt} - {MarketTime.AfterHours.End:h:mm tt} ET)");

            // Validation check - ensure market open correlates correctly
            var marketOpenLocal = MarketTime.RTH.StartLocal;
            var expectedEasternOpen = new TimeOnly(9, 30);
            var convertedBack = TimezoneHelper.ToEastern(marketOpenLocal, Settings.Timezone);

            if (convertedBack != expectedEasternOpen)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"WARNING: Timezone conversion mismatch! Expected {expectedEasternOpen:h:mm tt} ET, got {convertedBack:h:mm tt} ET");
                Console.ResetColor();
            }
        }
    }
}
