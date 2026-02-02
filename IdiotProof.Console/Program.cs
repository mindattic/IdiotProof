// ================================================================
// IdiotProof Multi-Stage Strategy Builder
// Console Client - Powered by IBKR API via Backend
// ================================================================
//
// This console application connects to the IdiotProof.Backend
// service via IPC and allows you to define trading strategies
// using the fluent API:
//
//   Stock.Ticker("SYMBOL")
//       .Breakout(level)              // Price >= level
//       .Pullback(level)              // Price <= level
//       .IsPriceAbove(level)          // Price >= level
//       .IsPriceBelow(level)          // Price <= level
//       .IsAboveVwap()                // Price >= VWAP
//       .IsBelowVwap()                // Price <= VWAP
//       .IsEmaAbove(period)           // Price >= EMA
//       .IsEmaBelow(period)           // Price <= EMA
//       .IsEmaBetween(lower, upper)   // Price between two EMAs
//       .Buy(quantity)
//       .TakeProfit(low, high)
//       .TrailingStopLoss(Percent.TwentyFive)
//       .ClosePosition(MarketTime.PreMarket.Ending, false)
//
// ================================================================

using IdiotProof.Console.Management;
using IdiotProof.Console.Scripting;
using IdiotProof.Console.Services;
using IdiotProof.Console.UI;
using IdiotProof.Shared.Models;
using IdiotProof.Shared.Scripting;

namespace IdiotProof.Console;

internal sealed class Program
{
    private static BackendClient? _client;
    private static bool _isTradingActive;
    private static readonly List<StrategyDefinition> _localStrategies = [];

    public static async Task Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            ConsoleUI.Error($"Unhandled exception: {e.ExceptionObject}");
        };

        try
        {
            await RunAsync();
        }
        catch (Exception ex)
        {
            ConsoleUI.Error($"Fatal error: {ex.Message}");
            throw;
        }
    }

    private static async Task RunAsync()
    {
        // ================================================================
        // SAMPLE STRATEGIES - Define your multi-step strategies here
        // ================================================================

        // NOTE: This app previously seeded a hardcoded sample PLTR strategy here.
        // That made it look like strategies were being loaded from disk when they weren't.
        // Strategies should come from the backend / strategy files instead.









        // ================================================================
        // LOAD STRATEGIES FROM DISK
        // ================================================================
        try
        {
            var folder = IdiotScriptFileManager.GetDefaultFolder();
            IdiotScriptFileManager.EnsureFolderExists(folder);

            var loaded = await IdiotScriptFileManager.LoadStrategiesFromFolderAsync(folder);
            _localStrategies.Clear();
            _localStrategies.AddRange(loaded.OrderBy(s => s.Name));
        }
        catch
        {
            // Best-effort: start even if strategy load fails.
        }

        // ================================================================
        // STARTUP
        // ================================================================
        ConsoleUI.ConfigureConsole();
        ConsoleUI.DisplayBanner();

        // ================================================================
        // CONNECT TO BACKEND
        // ================================================================
        _client = new BackendClient();

        // Subscribe to backend events
        _client.ConsoleOutputReceived += (_, msg) => ConsoleUI.DisplayBackendOutput(msg);
        _client.OrderUpdated += (_, order) =>
        {
            ConsoleUI.Info($"[ORDER] {order.Action} {order.Symbol} x{order.Quantity} - {order.StatusText}");
        };
        _client.TradeUpdated += (_, trade) =>
        {
            ConsoleUI.Info($"[TRADE] {trade.Symbol} - {trade.Status}");
        };
        _client.ConnectionStatusChanged += (_, connected) =>
        {
            if (!connected)
            {
                ConsoleUI.Warning("Lost connection to backend");
            }
        };

        ConsoleUI.Info("Connecting to IdiotProof.Backend...");

        if (!await _client.ConnectAsync())
        {
            ConsoleUI.Error("Failed to connect to backend. Make sure IdiotProof.Backend is running.");
            ConsoleUI.Info("Press any key to exit...");
            System.Console.ReadKey(intercept: true);
            return;
        }

        // Get initial status
        var status = await _client.GetStatusAsync();
        ConsoleUI.DisplayConnectionStatus(_client.IsConnected, status);

        if (status != null)
        {
            ConsoleUI.DisplayTradingMode(status.IsPaperTrading);
            _isTradingActive = status.IsTradingActive;
        }

        // Send strategies to backend
        if (_localStrategies.Count > 0)
        {
            ConsoleUI.Info($"Sending {_localStrategies.Count} strategies to backend...");
            var result = await _client.SetStrategiesAsync(_localStrategies);
            if (result?.Success == true)
            {
                ConsoleUI.Success(result.Message ?? "Strategies sent to backend");
            }
            else
            {
                ConsoleUI.Warning(result?.ErrorMessage ?? "Failed to send strategies to backend");
            }
        }

        // Display open orders
        var orders = await _client.GetOrdersAsync();
        ConsoleUI.DisplayOpenOrders(orders);

        // Display positions
        var positions = await _client.GetPositionsAsync();
        ConsoleUI.DisplayPositions(positions);

        // Display local strategies
        ConsoleUI.DisplayStrategies(_localStrategies);

        // Show help
        ConsoleUI.DisplayHelp();

        // Show activation prompt
        if (!_isTradingActive)
        {
            ConsoleUI.DisplayActivationPrompt();
        }

        // ================================================================
        // HEARTBEAT TIMER
        // ================================================================
        using var heartbeatTimer = new Timer(async _ =>
        {
            if (_client?.IsConnected != true) return;

            try
            {
                var easternZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
                var easternTime = TimeZoneInfo.ConvertTime(DateTime.Now, easternZone);
                var timestamp = easternTime.ToString("hh:mm:ss tt");

                var heartbeatStatus = await _client.GetStatusAsync();
                ConsoleUI.DisplayHeartbeat(timestamp, heartbeatStatus);
            }
            catch
            {
                // Ignore heartbeat errors
            }
        }, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));

        // ================================================================
        // MAIN INPUT LOOP
        // ================================================================
        ConsoleUI.Info("Running... (H for help)");

        while (true)
        {
            var key = System.Console.ReadKey(intercept: true);

            // CTRL+ALT+ENTER for trading activation (requires modifier for safety)
            if (key.Modifiers == (ConsoleModifiers.Control | ConsoleModifiers.Alt) && key.Key == ConsoleKey.Enter)
            {
                await ToggleTradingAsync();
                continue;
            }

            // Simple key shortcuts (no modifiers required)
            if (key.Modifiers == 0)
            {
                switch (key.Key)
                {
                    case ConsoleKey.C:
                        await CancelAllOrdersAsync();
                        break;

                    case ConsoleKey.R:
                        await ReloadStrategiesAsync();
                        break;

                    case ConsoleKey.S:
                        await ShowStatusAsync();
                        break;

                    case ConsoleKey.O:
                        await ShowOrdersAsync();
                        break;

                    case ConsoleKey.P:
                        await ShowPositionsAsync();
                        break;

                    case ConsoleKey.M:
                        await OpenStrategyManagerAsync();
                        break;

                    case ConsoleKey.N:
                        await QuickCreateStrategyAsync();
                        break;

                    case ConsoleKey.H:
                        ConsoleUI.DisplayHelp();
                        break;

                    // ESC does nothing at main level - user can close window with X
                    case ConsoleKey.Escape:
                        // Ignored at main menu level
                        break;
                }
            }
        }
    }

    private static async Task ToggleTradingAsync()
    {
        if (_client == null || !_client.IsConnected)
        {
            ConsoleUI.Error("Not connected to backend");
            return;
        }

        if (_isTradingActive)
        {
            // Confirm deactivation
            System.Console.ForegroundColor = ConsoleColor.Yellow;
            System.Console.Write("Deactivate trading? (y/n): ");
            System.Console.ResetColor();

            var confirm = System.Console.ReadKey(intercept: true).Key;
            System.Console.WriteLine();

            if (confirm != ConsoleKey.Y)
            {
                ConsoleUI.Info("Cancelled.");
                return;
            }

            var result = await _client.DeactivateTradingAsync();
            if (result?.Success == true)
            {
                _isTradingActive = false;
                ConsoleUI.DisplayTradingDeactivated();
            }
            else
            {
                ConsoleUI.Error(result?.ErrorMessage ?? "Failed to deactivate trading");
            }
        }
        else
        {
            // Confirm activation
            System.Console.ForegroundColor = ConsoleColor.Red;
            System.Console.Write("ACTIVATE TRADING? This will execute real orders! (y/n): ");
            System.Console.ResetColor();

            var confirm = System.Console.ReadKey(intercept: true).Key;
            System.Console.WriteLine();

            if (confirm != ConsoleKey.Y)
            {
                ConsoleUI.Info("Cancelled.");
                return;
            }

            var result = await _client.ActivateTradingAsync();
            if (result?.Success == true)
            {
                _isTradingActive = true;
                ConsoleUI.DisplayTradingActivated();
            }
            else
            {
                ConsoleUI.Error(result?.ErrorMessage ?? "Failed to activate trading");
            }
        }
    }

    private static async Task CancelAllOrdersAsync()
    {
        if (_client == null || !_client.IsConnected)
        {
            ConsoleUI.Error("Not connected to backend");
            return;
        }

        // Confirm cancellation
        System.Console.ForegroundColor = ConsoleColor.Yellow;
        System.Console.Write("Cancel ALL open orders? (y/n): ");
        System.Console.ResetColor();

        var confirm = System.Console.ReadKey(intercept: true).Key;
        System.Console.WriteLine();

        if (confirm != ConsoleKey.Y)
        {
            ConsoleUI.Info("Cancelled.");
            return;
        }

        ConsoleUI.Info("Cancelling all orders...");
        var result = await _client.CancelAllOrdersAsync();
        ConsoleUI.DisplayCancelOrdersResult(result);
    }

    private static async Task ReloadStrategiesAsync()
    {
        if (_client == null || !_client.IsConnected)
        {
            ConsoleUI.Error("Not connected to backend");
            return;
        }

        ConsoleUI.Info("Reloading strategies from disk...");

        try
        {
            var folder = IdiotScriptFileManager.GetDefaultFolder();
            var loaded = await IdiotScriptFileManager.LoadStrategiesFromFolderAsync(folder);
            _localStrategies.Clear();
            _localStrategies.AddRange(loaded.OrderBy(s => s.Name));

            ConsoleUI.Info($"Loaded {_localStrategies.Count} strategies from disk");

            // Send to backend
            var result = await _client.SetStrategiesAsync(_localStrategies);
            if (result?.Success == true)
            {
                ConsoleUI.Success(result.Message ?? "Strategies sent to backend");
            }
            else
            {
                ConsoleUI.Warning(result?.ErrorMessage ?? "Failed to send strategies to backend");
            }

            // Display updated strategies
            ConsoleUI.DisplayStrategies(_localStrategies);
        }
        catch (Exception ex)
        {
            ConsoleUI.Error($"Failed to reload strategies: {ex.Message}");
        }
    }

    private static async Task ShowStatusAsync()
    {
        if (_client == null)
        {
            ConsoleUI.Error("Client not initialized");
            return;
        }

        var status = await _client.GetStatusAsync();
        ConsoleUI.DisplayConnectionStatus(_client.IsConnected, status);
    }

    private static async Task ShowOrdersAsync()
    {
        if (_client == null || !_client.IsConnected)
        {
            ConsoleUI.Error("Not connected to backend");
            return;
        }

        var orders = await _client.GetOrdersAsync();
        ConsoleUI.DisplayOpenOrders(orders);
    }

    private static async Task ShowPositionsAsync()
    {
        if (_client == null || !_client.IsConnected)
        {
            ConsoleUI.Error("Not connected to backend");
            return;
        }

        var positions = await _client.GetPositionsAsync();
        ConsoleUI.DisplayPositions(positions);
    }

    private static async Task OpenStrategyManagerAsync()
    {
        ConsoleUI.Info("Opening Strategy Manager...");

        var manager = new StrategyConsoleManager(_client, _localStrategies);
        await manager.RunAsync();

        // Sync strategies back
        _localStrategies.Clear();
        _localStrategies.AddRange(manager.Strategies);

        // Sync with backend in case changes were made
        if (_client?.IsConnected == true && _localStrategies.Count > 0)
        {
            var result = await _client.SetStrategiesAsync(_localStrategies);
            if (result?.Success == true)
            {
                ConsoleUI.Success(result.Message ?? "Strategies synced with backend");
            }
        }

        // Redraw main UI
        ConsoleUI.ConfigureConsole();
        ConsoleUI.DisplayBanner();
        ConsoleUI.DisplayStrategies(_localStrategies);
        ConsoleUI.DisplayHelp();
        ConsoleUI.Info("Running... (CTRL+ALT+H for help, CTRL+ALT+Q to quit)");
    }

    private static async Task QuickCreateStrategyAsync()
    {
        System.Console.WriteLine();
        System.Console.ForegroundColor = ConsoleColor.Cyan;
        System.Console.WriteLine("┌─────────────────────────────────────────────────────────────────┐");
        System.Console.WriteLine("│                    QUICK CREATE STRATEGY                       │");
        System.Console.WriteLine("└─────────────────────────────────────────────────────────────────┘");
        System.Console.ResetColor();
        System.Console.WriteLine();
        System.Console.ForegroundColor = ConsoleColor.DarkGray;
        System.Console.WriteLine("Enter script (Example: SYM(PLTR); QTY(10); OPEN(148.75); TP($158); TSL(15%); VWAP)");
        System.Console.ResetColor();
        System.Console.Write("\nScript: ");
        System.Console.ForegroundColor = ConsoleColor.Yellow;

        var script = System.Console.ReadLine() ?? "";
        System.Console.ResetColor();

        if (string.IsNullOrWhiteSpace(script))
        {
            ConsoleUI.Warning("Cancelled - no script entered.");
            return;
        }

        if (StrategyScriptParser.TryParse(script, out var strategy, out var error))
        {
            _localStrategies.Add(strategy!);

            // Save to disk
            try
            {
                var folder = IdiotScriptFileManager.GetDefaultFolder();
                IdiotScriptFileManager.EnsureFolderExists(folder);
                var fileName = IdiotScriptFileManager.GetSafeFileName(strategy!.Name, strategy.Symbol);
                var savedPath = Path.Combine(folder, fileName);
                await IdiotScriptFileManager.SaveToFileAsync(strategy, savedPath);
                ConsoleUI.Info($"Saved: {savedPath}");
            }
            catch
            {
                // Best-effort persistence
            }

            ConsoleUI.Success($"Strategy '{strategy!.Name}' created!");

            // Sync with backend
            if (_client?.IsConnected == true)
            {
                var result = await _client.SetStrategiesAsync(_localStrategies);
                if (result?.Success == true)
                {
                    ConsoleUI.Success(result.Message ?? "Strategies synced with backend");
                }
            }

            ConsoleUI.DisplayStrategies(_localStrategies);
        }
        else
        {
            ConsoleUI.Error($"Parse error: {error}");
            System.Console.ForegroundColor = ConsoleColor.DarkGray;
            System.Console.WriteLine("Press CTRL+ALT+M to open Strategy Manager for help.");
            System.Console.ResetColor();
        }

        await Task.CompletedTask;
    }

    private static async Task ShutdownAsync()
    {
        ConsoleUI.Info("Shutting down...");

        if (_client != null)
        {
            await _client.DisconnectAsync();
            _client.Dispose();
        }

        ConsoleUI.Info("Disconnected. Goodbye!");
    }
}
