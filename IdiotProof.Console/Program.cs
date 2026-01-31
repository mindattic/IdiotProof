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
        var qty = 1;

        var strategies = new List<StrategyDefinition>
        {
            // ----- VIVS (Contributed by Momentum.) -----
            Stock
                .Ticker("VIVS")
                .Name("VIVS Breakout Strategy")
                .Author("Momentum.")
                .SessionDuration(TradingSession.PreMarketEndEarly)
                .IsPriceAbove(2.40)                                       // Step 1: Price >= 2.40
                .IsAboveVwap()                                            // Step 2: Price >= VWAP
                .IsEmaAbove(9)
                .Buy(quantity: qty, Price.Current)                  // Step 3: Buy @ Current Price
                .TakeProfit(4.00, 4.80)                                 // Step 4: ADX-based TakeProfit: 4.00 (weak) to 4.80 (strong)
                .TrailingStopLoss(Percent.TwentyFive)                   // 25% trailing stop loss
                .ClosePosition(MarketTime.PreMarket.Ending, false)      // Step 5: Close Position @ 9:15 AM ET
                .Build(),

            // ----- CATX (Contributed by Momentum.) -----
            Stock
                .Ticker("CATX")
                .Name("CATX Breakout Strategy")
                .Author("Momentum.")
                .SessionDuration(TradingSession.PreMarketEndEarly)
                .IsPriceAbove(4.00)                                       // Step 1: Price >= 4.00
                .IsAboveVwap()                                            // Step 2: Price >= VWAP
                .Buy(quantity: qty, Price.Current)                  // Step 3: Buy @ Current Price
                .TakeProfit(5.30, 6.16)                                 // Step 4: ADX-based TakeProfit: 5.30 (weak) to 6.16 (strong)
                .TrailingStopLoss(Percent.TwentyFive)                   // 25% trailing stop loss
                .ClosePosition(MarketTime.PreMarket.Ending, false)      // Step 5: Close Position @ 9:15 AM ET
                .Build(),

            // ----- VIVS EMA Pullback (Contributed by Claude Opus 4.5) -----
            Stock
                .Ticker("VIVS")
                .Name("VIVS EMA Pullback Entry")
                .Author("Claude Opus 4.5")
                .Description("Entry on pullback to EMA support while holding above VWAP")
                .SessionDuration(TradingSession.PreMarketEndEarly)
                .Pullback(4.15)                                         // Step 1: Pullback to EMA 12 zone ($4.13)
                .IsAboveVwap()                                            // Step 2: Still above VWAP
                .Buy(quantity: qty, Price.Current)                  // Step 3: Buy @ Current Price
                .TakeProfit(4.80, 5.30)                                 // Step 4: Target $4.80 to $5.30 on bounce
                .TrailingStopLoss(Percent.TwentyFive)                   // 25% trailing stop loss
                .ClosePosition(MarketTime.PreMarket.Ending, false)
                .Build(),

            // ----- CATX VWAP Reclaim (Contributed by Claude Opus 4.5) -----
            Stock
                .Ticker("CATX")
                .Name("CATX VWAP Reclaim Entry")
                .Author("Claude Opus 4.5")
                .Description("Entry on VWAP reclaim followed by pullback retest")
                .SessionDuration(TradingSession.PreMarketEndEarly)
                .IsAboveVwap()                                            // Step 1: Wait for VWAP reclaim (~$4.77)
                .Pullback(4.80)                                         // Step 2: Then look for pullback to VWAP
                .Buy(quantity: qty, Price.Current)                  // Step 3: Buy @ Current Price
                .TakeProfit(5.20, 5.50)                                 // Step 4: Target $5.20 to $5.50 on bounce
                .TrailingStopLoss(Percent.TwentyFive)                   // 25% trailing stop loss
                .ClosePosition(MarketTime.PreMarket.Ending, false)
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
