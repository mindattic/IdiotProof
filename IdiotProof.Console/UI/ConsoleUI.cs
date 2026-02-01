// ============================================================================
// ConsoleUI - Console display helpers for the Strategy Builder
// ============================================================================

using IdiotProof.Shared.Models;

namespace IdiotProof.Console.UI;

/// <summary>
/// Static helper class for console display formatting.
/// </summary>
public static class ConsoleUI
{
    private static readonly object _consoleLock = new();

    /// <summary>
    /// Configures console window settings.
    /// </summary>
    public static void ConfigureConsole()
    {
        try
        {
            if (!OperatingSystem.IsWindows())
                return;

            System.Console.Title = "IdiotProof Strategy Builder";

            int maxWidth = System.Console.LargestWindowWidth;
            int maxHeight = System.Console.LargestWindowHeight;

            int width = Math.Min(140, maxWidth - 2);
            int height = Math.Min(45, maxHeight - 2);

            width = Math.Max(width, 80);
            height = Math.Max(height, 25);

            System.Console.SetWindowSize(1, 1);
            System.Console.BufferWidth = width;
            System.Console.BufferHeight = 9999;
            System.Console.SetWindowSize(width, height);
        }
        catch
        {
            try
            {
                System.Console.Title = "IdiotProof Strategy Builder";
            }
            catch { }
        }
    }

    /// <summary>
    /// Displays the application banner.
    /// </summary>
    public static void DisplayBanner()
    {
        lock (_consoleLock)
        {
            System.Console.ForegroundColor = ConsoleColor.Cyan;
            System.Console.WriteLine();
            System.Console.WriteLine("================================================================");
            System.Console.WriteLine(" IdiotProof Multi-Stage Strategy Builder");
            System.Console.WriteLine(" Console Client - Powered by IBKR API");
            System.Console.WriteLine("================================================================");
            System.Console.ResetColor();
            System.Console.WriteLine();
            System.Console.ForegroundColor = ConsoleColor.DarkGray;
            System.Console.WriteLine(" Define your strategies using the fluent API:");
            System.Console.WriteLine();
            System.Console.WriteLine("   Stock.Ticker(\"SYMBOL\")");
            System.Console.WriteLine("       .Breakout(level)      // Price >= level");
            System.Console.WriteLine("       .Pullback(level)      // Price <= level");
            System.Console.WriteLine("       .AboveVwap()          // Price >= VWAP");
            System.Console.WriteLine("       .Buy(quantity, takeProfit: price)");
            System.Console.WriteLine();
            System.Console.ResetColor();
            System.Console.WriteLine("================================================================");
            System.Console.WriteLine();
        }
    }

    /// <summary>
    /// Displays backend connection status.
    /// </summary>
    public static void DisplayConnectionStatus(bool connected, StatusResponsePayload? status)
    {
        lock (_consoleLock)
        {
            if (!connected)
            {
                System.Console.ForegroundColor = ConsoleColor.Red;
                System.Console.WriteLine("[DISCONNECTED] Backend not available");
                System.Console.ResetColor();
                return;
            }

            System.Console.ForegroundColor = ConsoleColor.Green;
            System.Console.Write("[CONNECTED] ");
            System.Console.ResetColor();
            System.Console.Write("Backend running");

            if (status != null)
            {
                if (status.IsConnectedToIbkr)
                {
                    System.Console.ForegroundColor = ConsoleColor.Green;
                    System.Console.Write(" | IBKR: Connected");
                }
                else
                {
                    System.Console.ForegroundColor = ConsoleColor.Yellow;
                    System.Console.Write(" | IBKR: Disconnected");
                }

                System.Console.ResetColor();
                System.Console.Write($" | Strategies: {status.ActiveStrategies}");

                if (status.IsTradingActive)
                {
                    System.Console.ForegroundColor = ConsoleColor.Green;
                    System.Console.Write(" | Trading: ACTIVE");
                }
                else
                {
                    System.Console.ForegroundColor = ConsoleColor.Yellow;
                    System.Console.Write(" | Trading: PAUSED");
                }

                if (status.IsPaperTrading)
                {
                    System.Console.ForegroundColor = ConsoleColor.Cyan;
                    System.Console.Write(" [PAPER]");
                }
            }

            System.Console.ResetColor();
            System.Console.WriteLine();
        }
    }

    /// <summary>
    /// Displays trading mode.
    /// </summary>
    public static void DisplayTradingMode(bool isPaper)
    {
        lock (_consoleLock)
        {
            System.Console.Write("Trading Mode: ");
            if (isPaper)
            {
                System.Console.ForegroundColor = ConsoleColor.Cyan;
                System.Console.WriteLine("PAPER TRADING (Simulated)");
            }
            else
            {
                System.Console.ForegroundColor = ConsoleColor.Red;
                System.Console.WriteLine("LIVE TRADING (Real Money!)");
            }
            System.Console.ResetColor();
            System.Console.WriteLine();
        }
    }

    /// <summary>
    /// Displays open orders.
    /// </summary>
    public static void DisplayOpenOrders(List<OrderInfo> orders)
    {
        lock (_consoleLock)
        {
            System.Console.WriteLine();
            System.Console.ForegroundColor = ConsoleColor.White;
            System.Console.WriteLine("=== Open Orders ===");
            System.Console.ResetColor();

            if (orders.Count == 0)
            {
                System.Console.ForegroundColor = ConsoleColor.DarkGray;
                System.Console.WriteLine("  No open orders");
                System.Console.ResetColor();
                return;
            }

            foreach (var order in orders)
            {
                var actionColor = order.Action.ToUpperInvariant() == "BUY" ? ConsoleColor.Green : ConsoleColor.Red;
                System.Console.ForegroundColor = actionColor;
                System.Console.Write($"  [{order.Action}] ");

                System.Console.ForegroundColor = ConsoleColor.White;
                System.Console.Write($"{order.Symbol} ");

                System.Console.ForegroundColor = ConsoleColor.Gray;
                System.Console.Write($"x{order.Quantity} @ ");

                if (order.LimitPrice.HasValue)
                    System.Console.Write($"${order.LimitPrice:F2}");
                else
                    System.Console.Write("MKT");

                System.Console.Write($" | Status: ");

                var statusColor = order.Status switch
                {
                    OrderStatus.Filled => ConsoleColor.Green,
                    OrderStatus.PartiallyFilled => ConsoleColor.Yellow,
                    OrderStatus.Submitted or OrderStatus.PreSubmitted => ConsoleColor.Cyan,
                    OrderStatus.Cancelled or OrderStatus.ApiCancelled => ConsoleColor.DarkGray,
                    OrderStatus.Error => ConsoleColor.Red,
                    _ => ConsoleColor.Gray
                };

                System.Console.ForegroundColor = statusColor;
                System.Console.Write(order.StatusText);

                System.Console.ForegroundColor = ConsoleColor.DarkGray;
                System.Console.WriteLine($" | ID: {order.OrderId}");
            }

            System.Console.ResetColor();
        }
    }

    /// <summary>
    /// Displays positions.
    /// </summary>
    public static void DisplayPositions(List<PositionInfo> positions)
    {
        lock (_consoleLock)
        {
            System.Console.WriteLine();
            System.Console.ForegroundColor = ConsoleColor.White;
            System.Console.WriteLine("=== Positions ===");
            System.Console.ResetColor();

            if (positions.Count == 0)
            {
                System.Console.ForegroundColor = ConsoleColor.DarkGray;
                System.Console.WriteLine("  No open positions");
                System.Console.ResetColor();
                return;
            }

            foreach (var pos in positions)
            {
                var sideColor = pos.Quantity > 0 ? ConsoleColor.Green : ConsoleColor.Red;
                var side = pos.Quantity > 0 ? "LONG" : "SHORT";

                System.Console.ForegroundColor = sideColor;
                System.Console.Write($"  [{side}] ");

                System.Console.ForegroundColor = ConsoleColor.White;
                System.Console.Write($"{pos.Symbol} ");

                System.Console.ForegroundColor = ConsoleColor.Gray;
                System.Console.Write($"x{Math.Abs(pos.Quantity)} @ ${pos.AvgCost:F2}");

                if (pos.UnrealizedPnL != null && pos.UnrealizedPnL != 0)
                {
                    System.Console.ForegroundColor = pos.UnrealizedPnL >= 0 ? ConsoleColor.Green : ConsoleColor.Red;
                    System.Console.Write($" | P/L: ${pos.UnrealizedPnL:F2}");
                }

                System.Console.WriteLine();
            }

            System.Console.ResetColor();
        }
    }

    /// <summary>
    /// Displays strategies.
    /// </summary>
    public static void DisplayStrategies(List<StrategyDefinition> strategies)
    {
        lock (_consoleLock)
        {
            System.Console.WriteLine();
            System.Console.ForegroundColor = ConsoleColor.White;
            System.Console.WriteLine("=== Defined Strategies ===");
            System.Console.ResetColor();

            if (strategies.Count == 0)
            {
                System.Console.ForegroundColor = ConsoleColor.DarkGray;
                System.Console.WriteLine("  No strategies defined");
                System.Console.ResetColor();
                return;
            }

            foreach (var strategy in strategies)
            {
                var enabledColor = strategy.Enabled ? ConsoleColor.Green : ConsoleColor.DarkGray;
                var enabledText = strategy.Enabled ? "ON " : "OFF";

                System.Console.ForegroundColor = enabledColor;
                System.Console.Write($"  [{enabledText}] ");

                System.Console.ForegroundColor = ConsoleColor.White;
                System.Console.Write($"{strategy.Symbol} ");

                System.Console.ForegroundColor = ConsoleColor.Gray;
                System.Console.Write($"- {strategy.Name}");

                if (!string.IsNullOrEmpty(strategy.Author))
                {
                    System.Console.ForegroundColor = ConsoleColor.DarkCyan;
                    System.Console.Write($" (by {strategy.Author})");
                }

                System.Console.WriteLine();

                // Display calculated stats
                var stats = strategy.GetStats();
                if (stats.Quantity > 0 || stats.Price > 0)
                {
                    System.Console.ForegroundColor = ConsoleColor.Yellow;
                    System.Console.Write("      ");

                    var statParts = new List<string>();

                    if (stats.Quantity > 0)
                        statParts.Add($"Qty: {stats.Quantity}");

                    if (stats.Price > 0)
                        statParts.Add($"Price: ${stats.Price:F2}");

                    if (stats.BuyIn > 0)
                        statParts.Add($"BuyIn: ${stats.BuyIn:F2}");

                    if (stats.TakeProfit > 0)
                        statParts.Add($"TP: ${stats.TakeProfit:F2}");

                    if (stats.TrailingStopLossPercent > 0)
                        statParts.Add($"TSL: {stats.TrailingStopLossPercent * 100:F0}%");

                    if (stats.PotentialLoss > 0)
                    {
                        System.Console.Write(string.Join(", ", statParts));
                        System.Console.ForegroundColor = ConsoleColor.Red;
                        System.Console.Write($", Loss: -${stats.PotentialLoss:F2}");
                    }
                    else
                    {
                        System.Console.Write(string.Join(", ", statParts));
                    }

                    if (stats.PotentialGain > 0)
                    {
                        System.Console.ForegroundColor = ConsoleColor.Green;
                        System.Console.Write($", Gain: +${stats.PotentialGain:F2}");
                    }

                    System.Console.WriteLine();
                }

                // Display fluent code
                System.Console.ForegroundColor = ConsoleColor.DarkGray;
                var code = strategy.ToFluentCode();
                var lines = code.Split('\n');
                foreach (var line in lines)
                {
                    System.Console.WriteLine($"      {line}");
                }

                System.Console.WriteLine();
            }

            System.Console.ResetColor();
        }
    }

    /// <summary>
    /// Displays keyboard shortcuts help.
    /// </summary>
    public static void DisplayHelp()
    {
        lock (_consoleLock)
        {
            System.Console.WriteLine();
            System.Console.ForegroundColor = ConsoleColor.Cyan;
            System.Console.WriteLine("=== Keyboard Shortcuts ===");
            System.Console.ResetColor();

            System.Console.ForegroundColor = ConsoleColor.White;
            System.Console.WriteLine("  Strategy Management:");
            System.Console.ResetColor();
            System.Console.WriteLine("    M               - Open Strategy Manager (view/create/toggle/cancel)");
            System.Console.WriteLine("    N               - Quick create strategy (script mode)");
            System.Console.WriteLine();

            System.Console.ForegroundColor = ConsoleColor.White;
            System.Console.WriteLine("  Trading Controls:");
            System.Console.ResetColor();
            System.Console.WriteLine("    CTRL+ALT+ENTER  - Activate/Deactivate trading");
            System.Console.WriteLine("    C               - Cancel all open orders");
            System.Console.WriteLine("    R               - Reload strategies from backend");
            System.Console.WriteLine();

            System.Console.ForegroundColor = ConsoleColor.White;
            System.Console.WriteLine("  Information:");
            System.Console.ResetColor();
            System.Console.WriteLine("    S               - Show status");
            System.Console.WriteLine("    O               - Show open orders");
            System.Console.WriteLine("    P               - Show positions");
            System.Console.WriteLine("    H               - Show this help");
            System.Console.WriteLine("    ESC             - Go back / Exit submenu");
            System.Console.WriteLine();

            System.Console.ForegroundColor = ConsoleColor.DarkGray;
            System.Console.WriteLine("  Script Syntax Example:");
            System.Console.ForegroundColor = ConsoleColor.Yellow;
            System.Console.WriteLine("    SYM(PLTR); QTY(10); TP($158); TSL(15%); CLOSE(9:29, Y); BREAKOUT(148) > PULLBACK(145) > ABOVE_VWAP > EMA_BETWEEN(9, 21)");
            System.Console.ResetColor();
            System.Console.WriteLine();
        }
    }

    /// <summary>
    /// Displays activation prompt.
    /// </summary>
    public static void DisplayActivationPrompt()
    {
        lock (_consoleLock)
        {
            System.Console.ForegroundColor = ConsoleColor.Yellow;
            System.Console.WriteLine();
            System.Console.WriteLine("╔═══════════════════════════════════════════════════════════════╗");
            System.Console.WriteLine("║  Trading is PAUSED - Press CTRL+ALT+ENTER to activate        ║");
            System.Console.WriteLine("║  Press H for help                                            ║");
            System.Console.WriteLine("╚═══════════════════════════════════════════════════════════════╝");
            System.Console.ResetColor();
            System.Console.WriteLine();
        }
    }

    /// <summary>
    /// Displays trading activated message.
    /// </summary>
    public static void DisplayTradingActivated()
    {
        lock (_consoleLock)
        {
            System.Console.ForegroundColor = ConsoleColor.Green;
            System.Console.WriteLine();
            System.Console.WriteLine("╔═══════════════════════════════════════════════════════════════╗");
            System.Console.WriteLine("║  >>> TRADING ACTIVATED <<<                                    ║");
            System.Console.WriteLine("║  Strategies are now executing. Be careful!                    ║");
            System.Console.WriteLine("╚═══════════════════════════════════════════════════════════════╝");
            System.Console.ResetColor();
            System.Console.WriteLine();
        }
    }

    /// <summary>
    /// Displays trading deactivated message.
    /// </summary>
    public static void DisplayTradingDeactivated()
    {
        lock (_consoleLock)
        {
            System.Console.ForegroundColor = ConsoleColor.Yellow;
            System.Console.WriteLine();
            System.Console.WriteLine("╔═══════════════════════════════════════════════════════════════╗");
            System.Console.WriteLine("║  >>> TRADING PAUSED <<<                                       ║");
            System.Console.WriteLine("║  Strategies have been paused.                                 ║");
            System.Console.WriteLine("╚═══════════════════════════════════════════════════════════════╝");
            System.Console.ResetColor();
            System.Console.WriteLine();
        }
    }

    /// <summary>
    /// Displays cancel orders result.
    /// </summary>
    public static void DisplayCancelOrdersResult(OperationResultPayload? result)
    {
        lock (_consoleLock)
        {
            if (result == null)
            {
                System.Console.ForegroundColor = ConsoleColor.Red;
                System.Console.WriteLine("Failed to cancel orders - no response from backend");
                System.Console.ResetColor();
                return;
            }

            if (result.Success)
            {
                System.Console.ForegroundColor = ConsoleColor.Green;
                System.Console.WriteLine($"✓ {result.Message ?? "Orders cancelled successfully"}");
            }
            else
            {
                System.Console.ForegroundColor = ConsoleColor.Red;
                System.Console.WriteLine($"✗ {result.ErrorMessage ?? "Failed to cancel orders"}");
            }

            System.Console.ResetColor();
        }
    }

    /// <summary>
    /// Displays an info message.
    /// </summary>
    public static void Info(string message)
    {
        lock (_consoleLock)
        {
            System.Console.ForegroundColor = ConsoleColor.Gray;
            System.Console.WriteLine(message);
            System.Console.ResetColor();
        }
    }

    /// <summary>
    /// Displays a success message.
    /// </summary>
    public static void Success(string message)
    {
        lock (_consoleLock)
        {
            System.Console.ForegroundColor = ConsoleColor.Green;
            System.Console.WriteLine($"✓ {message}");
            System.Console.ResetColor();
        }
    }

    /// <summary>
    /// Displays a warning message.
    /// </summary>
    public static void Warning(string message)
    {
        lock (_consoleLock)
        {
            System.Console.ForegroundColor = ConsoleColor.Yellow;
            System.Console.WriteLine($"⚠ {message}");
            System.Console.ResetColor();
        }
    }

    /// <summary>
    /// Displays an error message.
    /// </summary>
    public static void Error(string message)
    {
        lock (_consoleLock)
        {
            System.Console.ForegroundColor = ConsoleColor.Red;
            System.Console.WriteLine($"✗ {message}");
            System.Console.ResetColor();
        }
    }

    /// <summary>
    /// Displays console output from backend.
    /// </summary>
    public static void DisplayBackendOutput(ConsoleOutputMessage output)
    {
        lock (_consoleLock)
        {
            var color = output.Level switch
            {
                "Error" => ConsoleColor.Red,
                "Warning" => ConsoleColor.Yellow,
                "Success" => ConsoleColor.Green,
                _ => ConsoleColor.DarkGray
            };

            System.Console.ForegroundColor = color;
            System.Console.Write($"[BACKEND] ");
            System.Console.ResetColor();
            System.Console.WriteLine(output.Text.TrimEnd());
        }
    }

    /// <summary>
    /// Displays heartbeat status.
    /// </summary>
    public static void DisplayHeartbeat(string timestamp, StatusResponsePayload? status)
    {
        lock (_consoleLock)
        {
            System.Console.ForegroundColor = ConsoleColor.DarkGray;
            System.Console.Write($"[{timestamp}] Heartbeat: ");

            if (status == null)
            {
                System.Console.ForegroundColor = ConsoleColor.Red;
                System.Console.WriteLine("No response");
            }
            else
            {
                System.Console.ForegroundColor = ConsoleColor.Green;
                System.Console.Write("OK");

                System.Console.ForegroundColor = ConsoleColor.DarkGray;
                System.Console.Write($" | IBKR: {(status.IsConnectedToIbkr ? "Connected" : "Disconnected")}");
                System.Console.Write($" | Trading: {(status.IsTradingActive ? "Active" : "Paused")}");
                System.Console.Write($" | Strategies: {status.ActiveStrategies}");
                System.Console.WriteLine();
            }

            System.Console.ResetColor();
        }
    }
}
