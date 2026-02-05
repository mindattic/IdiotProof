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
//       .Long(quantity)
//       .TakeProfit(low, high)
//       .TrailingStopLoss(Percent.TwentyFive)
//       .ClosePosition(MarketTime.PreMarket.Ending, false)
//
// ================================================================

using IdiotProof.Console.Services;
using IdiotProof.Console.UI;
using IdiotProof.Shared.Models;
using IdiotProof.Shared.Scripting;

namespace IdiotProof.Console;

internal sealed class Program
{
    private static BackendClient? _client;
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
        _client.PingReceived += (_, _) =>
        {
            ConsoleUI.Info("[PING] Backend heartbeat received");
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

        // Display local strategies
        ConsoleUI.DisplayStrategies(_localStrategies);

        // ================================================================
        // WAIT INDEFINITELY - No input, just receive backend messages
        // ================================================================
        ConsoleUI.Info("Running... (Ctrl+C to exit)");
        ConsoleUI.Info("Listening for backend messages...");

        // Wait forever - allows pings and console output to display without input blocking
        await Task.Delay(Timeout.Infinite, CancellationToken.None);
    }
}


