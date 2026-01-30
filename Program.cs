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
 
                // ----- VIVS (Premarket): Buy 208x$2.40=$500, TakeProfit $4.00-$4.80, Exit $832-$998 -----
                Stock
                    .Ticker("VIVS")
                    .SessionDuration(TradingSession.Always)
                    .PriceAbove(2.40)                                       // Step 1: Price > 2.40
                    .AboveVwap()                                            // Step 2: Price >= VWAP
                    .Buy(quantity: 1, Price.Current)                        // Step 3: Buy 208 @ Current Price
                    .TakeProfit(4.00, 4.80)                                 // Step 4: ADX-based TakeProfit: 4.00 (weak) to 4.80 (strong)
                    .ClosePosition(MarketTime.PreMarket.Ending),            // Step 5: Close Position @ 9:20 AM ET (if profitable)


                // ----- CATX (Premarket): Buy 125x$4.00=$500, TakeProfit $5.30-$6.16, Exit $663-$770 -----
                Stock
                    .Ticker("CATX")
                    .SessionDuration(TradingSession.Always)
                    .PriceAbove(4.00)                                       // Step 1: Price > 4.00
                    .AboveVwap()                                            // Step 2: Price >= VWAP
                    .Buy(quantity: 1, Price.Current)                        // Step 3: Buy 125 @ Current Price
                    .TakeProfit(5.30, 6.16)                                 // Step 4: ADX-based TakeProfit: 5.30 (weak) to 6.16 (strong)
                    .ClosePosition(MarketTime.PreMarket.Ending),            // Step 5: Close Position @ 9:20 AM ET (if profitable)


                // ----- RPGL (Premarket): Buy 568x$0.88=$500, TakeProfit $1.30-$1.70, Exit $738-$966 -----
                Stock
                    .Ticker("RPGL")
                    .SessionDuration(TradingSession.Always)
                    .Exchange(ContractExchange.Pink)                        // Pink Sheets
                    .PriceAbove(0.88)                                       // Step 1: Price > 0.88
                    .AboveVwap()                                            // Step 2: Price >= VWAP
                    .Buy(quantity: 1, Price.Current)                        // Step 3: Buy 568 @ Current Price
                    .TakeProfit(1.30, 1.70),                                // ADX-based TakeProfit: 1.30 (weak) to 1.70 (strong)

            };

            if (!IsValid())
            {
                Console.WriteLine("ERROR: Strategy validation failed. Check configuration.");
                return;
            }

            // ================================================================
            // STARTUP
            // ================================================================
            Console.WriteLine("===============================================================");
            Console.WriteLine("                    IdiotProof Strategy Bot                    ");
            Console.WriteLine("===============================================================");
            Console.WriteLine();

            // Display timezone configuration
            DisplayTimezoneInfo();
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

            // Display strategies for review
            DisplayStrategies(enabledStrategies);
            Console.WriteLine();

            // Trading is paused until user explicitly activates
            bool isActive = false;

            Console.WriteLine("================================================================");
            Console.WriteLine("  PAUSED - Review strategies above before starting.             ");
            Console.WriteLine("  Press CTRL+ALT+ENTER to activate trading.                     ");
            Console.WriteLine("  Press CTRL+ALT+Q to quit.                                     ");
            Console.WriteLine("================================================================");
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
                    else if (key.Key == ConsoleKey.Q)
                    {
                        Shutdown(client, wrapper);
                        return;
                    }
                }
            }

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
                    PrimaryExch = strategy.PrimaryExchange ?? "",
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
            Console.WriteLine($"Running...");
            Console.WriteLine();

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
            Shutdown(client, wrapper, tickerIds, runners);
        }

        private static bool IsValid()
        {
            //TODO: Add exhausive error handling/logging here to verify strategies are valid before starting...
            return true;
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
        private static void DisplayStrategies(List<TradingStrategy> strategies)
        {
            var strategyWord = strategies.Count == 1 ? "strategy" : "strategies";
            Console.WriteLine($"Loaded {strategies.Count} {strategyWord} for review:");

            foreach (var strategy in strategies)
            {
                Console.WriteLine();
                strategy.WriteProgress(currentStep: 0);
            }
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
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("No existing open orders found.");
                Console.WriteLine();
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
                Console.WriteLine("!!! Consider canceling old orders to avoid duplicate fills !!!");
                Console.ResetColor();
            }
            Console.WriteLine();
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
