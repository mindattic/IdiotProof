using IdiotProof.Backend.Models;
using IdiotProof.Backend.Strategy;
using IdiotProof.Shared.Settings;
using System;
using System.Collections.Generic;

namespace IdiotProof.Backend
{
    /// <summary>
    /// Console UI display methods for the IdiotProof trading bot.
    /// </summary>
    public static class Gui
    {
        /// <summary>
        /// Configures the console window size and buffer.
        /// Note: Only works in conhost.exe, not Windows Terminal.
        /// </summary>
        /// <param name="preferredRows">Desired window height in rows.</param>
        /// <param name="preferredColumns">Desired window width in columns.</param>
        public static void ConfigureConsole(int preferredRows = 100, int preferredColumns = 80)
        {
            try
            {
                // Only works on Windows
                if (!OperatingSystem.IsWindows())
                    return;

                // Set console title
                Console.Title = "IdiotProof Backend";

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
                    Console.Title = "IdiotProof Backend";
                }
                catch
                {
                    // Ignore
                }
            }
        }

        /// <summary>
        /// Displays the startup banner.
        /// </summary>
        public static void DisplayBanner()
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("===============================================================");
            Console.WriteLine("              IdiotProof Multi-Stage Strategy Bot              ");
            Console.WriteLine("===============================================================");
            Console.WriteLine();
            Console.ResetColor();
        }

        /// <summary>
        /// Displays the trading mode (Paper or Live).
        /// </summary>
        public static void DisplayTradingMode()
        {
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
        }

        /// <summary>
        /// Displays the activation prompt and instructions.
        /// </summary>
        public static void DisplayActivationPrompt()
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("================================================================");
            Console.WriteLine("  Review strategies above before starting.                      ");
            Console.WriteLine("  Press CTRL+ALT+ENTER to activate trading.                     ");
            Console.WriteLine("  Press CTRL+ALT+C to cancel all open orders.                   ");
            Console.WriteLine("  Press CTRL+ALT+Q to quit.                                     ");
            Console.WriteLine("================================================================");
            Console.ResetColor();
            Console.WriteLine();
        }

        /// <summary>
        /// Displays the trading activated message.
        /// </summary>
        public static void DisplayTradingActivated()
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(">>> TRADING ACTIVATED <<<");
            Console.ResetColor();
        }

        /// <summary>
        /// Displays enabled strategies for review before trading starts.
        /// </summary>
        public static void DisplayStrategies(List<TradingStrategy> strategies, Dictionary<string, double> prices)
        {
            var strategyWord = strategies.Count == 1 ? "strategy" : "strategies";
            Console.WriteLine($"Fetching {strategies.Count} {strategyWord} for review...");

            foreach (var strategy in strategies)
            {
                Console.WriteLine();
                double currentPrice = prices.TryGetValue(strategy.Symbol, out var price) ? price : 0;
                strategy.WriteProgress(currentStep: 0, entryFilled: false, takeProfitFilled: false,
                    currentPrice: currentPrice, entryPrice: 0, takeProfitTarget: 0);
            }
        }

        /// <summary>
        /// Displays all open orders currently in IBKR.
        /// </summary>
        public static void DisplayOpenOrders(IbWrapper wrapper)
        {
            Console.WriteLine();
            Console.WriteLine("Fetching existing orders from IBKR...");

            // Request and wait for open orders
            wrapper.RequestOpenOrdersAndWait(TimeSpan.FromSeconds(Settings.ConnectionTimeoutSeconds));

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
                Console.WriteLine("+----------------------------------------------------------------------+");
                Console.WriteLine("¦ OrderId ¦ Symbol ¦ Action ¦  Qty  ¦  Type  ¦   Price  ¦    Status    ¦");
                Console.WriteLine("+---------+--------+--------+-------+--------+----------+--------------¦");

                foreach (var (orderId, order) in orders)
                {
                    var priceStr = order.LmtPrice > 0 ? $"${order.LmtPrice:F2}" : "MKT";
                    Console.WriteLine($"¦ {orderId,7} ¦ {order.Symbol,-6} ¦ {order.Action,-6} ¦ {order.Qty,5} ¦ {order.Type,-6} ¦ {priceStr,8} ¦ {order.Status,-12} ¦");
                }

                Console.WriteLine("+----------------------------------------------------------------------+");
                Console.ResetColor();
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Consider canceling old orders to avoid duplicate fills!");
                Console.ResetColor();
            }
        }

      

        /// <summary>
        /// Displays price received status.
        /// </summary>
        public static void DisplayPriceStatus(int received, int total)
        {
            if (received == total)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"Received prices for {received} symbol(s).");
                Console.WriteLine();
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"Received prices for {received}/{total} symbol(s). Some may still be loading...");
                Console.WriteLine();
                Console.ResetColor();
            }
        }

        /// <summary>
        /// Displays order cancellation results.
        /// </summary>
        public static void DisplayCancelOrdersResult(int orderCount, int cancelledCount, int remainingCount)
        {
            if (orderCount == 0)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("No open orders to cancel.");
                Console.ResetColor();
                Console.WriteLine();
                return;
            }

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
        /// Displays cancelling orders message.
        /// </summary>
        public static void DisplayCancellingOrders(int orderCount)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"Cancelling {orderCount} open order(s)...");
            Console.ResetColor();
        }

        /// <summary>
        /// Displays connection lost warning.
        /// </summary>
        public static void DisplayConnectionLost()
        {
            var timestamp = GetEasternTimeStamp();
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine();
            Console.WriteLine($"[{timestamp}] *** CONNECTION LOST - Waiting for reconnection... ***");
            Console.ResetColor();
        }

        /// <summary>
        /// Displays connection restored message.
        /// </summary>
        /// <param name="dataLost">True if market data was lost and resubscription is needed.</param>
        public static void DisplayConnectionRestored(bool dataLost)
        {
            var timestamp = GetEasternTimeStamp();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[{timestamp}] *** CONNECTION RESTORED ***");
            if (dataLost)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"[{timestamp}] Data was lost during disconnect - resubscribing to market data...");
            }
            else
            {
                Console.WriteLine($"[{timestamp}] Data maintained - continuing normal operation.");
            }
            Console.ResetColor();
            Console.WriteLine();
        }

        /// <summary>
        /// Displays market data resubscription status.
        /// </summary>
        /// <param name="symbolCount">Number of symbols being resubscribed.</param>
        public static void DisplayResubscribingMarketData(int symbolCount)
        {
            var timestamp = GetEasternTimeStamp();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"[{timestamp}] Resubscribing to market data for {symbolCount} symbol(s)...");
            Console.ResetColor();
        }

        /// <summary>
        /// Displays market data resubscription complete.
        /// </summary>
        public static void DisplayResubscriptionComplete()
        {
            var timestamp = GetEasternTimeStamp();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[{timestamp}] Market data resubscription complete. Resuming trading.");
            Console.ResetColor();
            Console.WriteLine();
        }

        /// <summary>
        /// Gets the current time formatted in Eastern timezone.
        /// </summary>
        private static string GetEasternTimeStamp()
        {
            var easternZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
            var easternTime = TimeZoneInfo.ConvertTime(DateTime.Now, easternZone);
            return easternTime.ToString("hh:mm:ss tt");
        }
    }
}


