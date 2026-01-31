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

using IdiotProof.Console.Services;
using IdiotProof.Console.Strategies;
using IdiotProof.Console.UI;
using IdiotProof.Shared.Enums;
using IdiotProof.Shared.Helpers;
using IdiotProof.Shared.Models;

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

        var strategies = new List<StrategyDefinition>
        {
            // ============================================================
            // STRATEGY STEPS:
            // 1. Stock: PLTR stock
            // 2. Session: Pre-market (end early to avoid thin liquidity at open)
            // 3. Wait for breakout above $148.75 (above chop zone)
            // 4. Confirm price is above VWAP (buyers controlling tape)
            // 5. Confirm price is above 9 EMA (short-term bullish)
            // 6. Confirm price is above 200 EMA (trend context - reclaim required)
            // 7. Execute BUY order at current price
            // 8. Set take profit at $153.50 (~1.5R realistic for premarket)
            // 9. Set stop loss at $145.50 (structure-based, below VWAP/consolidation)
            // 10. Enable 5% trailing stop (tighter for short-term momentum play)
            // 11. Auto-close position right before market open if profitable
            //
            // RISK/REWARD CALCULATION:
            // Entry: ~$148.75 | Target: $153.50 (+$4.75) | Stop: $145.50 (-$3.25)
            // R:R ratio = 1.46:1 (acceptable for high-probability setup)
            // ============================================================
            Stock
                .Ticker("PLTR")
                .Name("Palantir Premarket Momentum")
                .Author("Ryan DeBraal")
                .Description("Breakout play with VWAP + EMA confirmation. Entry: ~$148.75 | Target: $153.50 (+$4.75) | Stop: $145.50 (-$3.25) | R:R: 1.46:1")
                .SessionDuration(TradingSession.PreMarketEndEarly)
                .IsPriceAbove(148.75)                               // Breakout trigger
                .IsAboveVwap()                                      // Buyers in control
                .IsEmaAbove(9)                                      // Short-term bullish momentum
                .IsEmaAbove(200)                                    // Trend context - above 200 EMA
                .Buy(quantity: 10, Price.Current)                  
                .TakeProfit(153.50)                                 // Realistic premarket target (~1.5R)
                .StopLoss(145.50)                                   // Structure-based: below VWAP/consolidation
                .TrailingStopLoss(Percent.Five)                     // Tight 5% trail for momentum scalp
                .ClosePosition(MarketTime.PreMarket.RightBeforeBell, onlyIfProfitable: true)  
                .Build(),
        };

        _localStrategies.AddRange(strategies);

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
        ConsoleUI.Info("Running... (CTRL+ALT+H for help, CTRL+ALT+Q to quit)");

        while (true)
        {
            var key = System.Console.ReadKey(intercept: true);

            if (key.Modifiers == (ConsoleModifiers.Control | ConsoleModifiers.Alt))
            {
                switch (key.Key)
                {
                    case ConsoleKey.Enter:
                        await ToggleTradingAsync();
                        break;

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

                    case ConsoleKey.H:
                        ConsoleUI.DisplayHelp();
                        break;

                    case ConsoleKey.Q:
                        await ShutdownAsync();
                        return;
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

        ConsoleUI.Info("Reloading strategies from backend...");
        await _client.ReloadStrategiesAsync();
        ConsoleUI.Success("Strategies reloaded");
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
