// ================================================================
// IdiotProof Core - Headless IBKR Trading Engine
// Silent background service - all UI via IdiotProof.Web
// ================================================================

using IBApi;
using IdiotProof.Alerts;
using IdiotProof.Constants;
using IdiotProof.Core.Models;
using IdiotProof.Helpers;
using IdiotProof.Integration;
using IdiotProof.Logging;
using IdiotProof.Models;
using IdiotProof.Services;
using IdiotProof.Strategy;
using IdiotProof.Settings;
using IdiotProof.Validation;

namespace IdiotProof;

/// <summary>
/// Headless trading engine service.
/// All user interaction happens via IdiotProof.Web.
/// This service connects to IBKR, executes strategies, and streams data to the Web UI.
/// </summary>
internal sealed class Program
{
    // Core components
    private static IbWrapper? wrapper;
    private static EClientSocket? client;
    private static readonly CancellationTokenSource shutdownCts = new();
    private static TradeTrackingService? tradeTrackingService;
    private static SessionLogger? sessionLogger;

    // Historical data components
    private static HistoricalDataStore? historicalDataStore;
    private static HistoricalDataService? historicalDataService;
    private static BacktestService? backtestService;
    private static TickerMetadataService? metadataService;

    // Alert components
    private static AlertService? alertService;
    private static SuddenMoveDetector? suddenMoveDetector;

    // Web frontend integration
    private static WebFrontendClient? webFrontendClient;
    private static AlertWebIntegration? alertWebIntegration;

    // State
    private static readonly List<StrategyRunner> runners = [];
    private static readonly List<int> tickerIds = [];
    private static readonly Dictionary<string, Contract> contracts = [];
    private static readonly Dictionary<string, double> prices = [];
    private static readonly Dictionary<string, double> previousPrices = [];
    private static List<TradingStrategy> strategies = [];
    private static bool isConnected;
    private static bool isActive;
    private static Timer? priceCheckTimer;
    private static Timer? webFrontendTimer;
    private static Timer? heartbeatTimer;

    public static void Main(string[] args)
    {
        // Always run in silent mode - this is a headless service
        AppSettings.SilentMode = true;

        // Setup crash handler
        CrashHandler.Setup();

        try
        {
            RunHeadless();
        }
        catch (Exception ex)
        {
            CrashHandler.WriteCrashDump(ex, "Main Thread Exception");
            throw;
        }
    }

    /// <summary>
    /// Runs the trading engine as a headless background service.
    /// </summary>
    private static void RunHeadless()
    {
        Log("IdiotProof Core starting (headless mode)...");
        Log($"Mode: {(AppSettings.IsPaperTrading ? "PAPER" : "LIVE")} | Port: {AppSettings.Port}");

        // Initialize session logger
        sessionLogger = new SessionLogger();
        sessionLogger.LogEvent("STARTUP", $"Headless mode | {(AppSettings.IsPaperTrading ? "PAPER" : "LIVE")} | Port: {AppSettings.Port}");

        // Share session logger with all backend classes
        StrategyRunner.SessionLogger = sessionLogger;
        IbWrapper.SessionLogger = sessionLogger;
        StrategyManager.SessionLogger = sessionLogger;
        StrategyValidatorHelper.SessionLogger = sessionLogger;

        // Initialize trade tracking service
        tradeTrackingService = new TradeTrackingService();
        Log("Trade tracking service initialized");

        // Initialize web frontend client for live data streaming
        InitializeWebFrontendClient();

        // Connect to IBKR
        if (!ConnectToIbkr())
        {
            Log("ERROR: Failed to connect to IBKR. Will retry on reconnection...");
        }

        // Load strategies from watchlist
        LoadStrategies();

        // Auto-activate trading if we have tickers configured
        if (isConnected)
        {
            var watchlist = WatchlistManager.Load();
            if (watchlist.EnabledTickers.Any())
            {
                Log("Auto-activating trading with enabled tickers...");
                ActivateTrading();
            }
            else
            {
                Log("No enabled tickers. Configure watchlist via Web UI to start trading.");
            }
        }

        // Start heartbeat timer (logs status every 5 minutes)
        StartHeartbeatTimer();

        // Handle Ctrl+C for graceful shutdown
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            Log("Shutdown signal received...");
            shutdownCts.Cancel();
        };

        // Wait for shutdown signal
        Log("Core engine running. Waiting for shutdown signal...");
        try
        {
            shutdownCts.Token.WaitHandle.WaitOne();
        }
        catch (ObjectDisposedException)
        {
            // Expected during shutdown
        }

        // Cleanup
        Shutdown();
    }

    private static void StartHeartbeatTimer()
    {
        // Log heartbeat every 5 minutes
        heartbeatTimer = new Timer(async _ =>
        {
            var status = isConnected ? "Connected" : "Disconnected";
            var trading = isActive ? $"Trading ({runners.Count} strategies)" : "Idle";
            Log($"[Heartbeat] IBKR: {status} | Status: {trading}");

            // Send heartbeat to Web frontend to maintain connection status
            if (webFrontendClient != null)
            {
                await webFrontendClient.SendHeartbeatAsync();
            }
        }, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));  // Send every 10 seconds
    }

    private static bool ConnectToIbkr()
    {
        Log($"Connecting to IBKR at {AppSettings.Host}:{AppSettings.Port}...");

        wrapper = new IbWrapper();
        client = new EClientSocket(wrapper, wrapper.Signal);
        wrapper.AttachClient(client);

        client.eConnect(AppSettings.Host, AppSettings.Port, AppSettings.ClientId);

        // Start reader thread
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

        // Wait for connection
        if (!wrapper.WaitForNextValidId(TimeSpan.FromSeconds(AppSettings.ConnectionTimeoutSeconds)))
        {
            isConnected = false;
            return false;
        }

        isConnected = true;
        Log("Connected to IBKR successfully!");
        Log($"Trading Mode: {(AppSettings.IsPaperTrading ? "PAPER" : "LIVE")}");

        // Subscribe to account updates
        wrapper.RequestAccountUpdates(AppSettings.AccountNumber ?? "");

        // Initialize historical data components
        historicalDataStore = new HistoricalDataStore();
        historicalDataService = new HistoricalDataService(client, wrapper, historicalDataStore);
        backtestService = new BacktestService(historicalDataService);
        metadataService = new TickerMetadataService();
        Log("Historical data service initialized");

        // Setup reconnection handlers
        wrapper.OnConnectionLost += () =>
        {
            isConnected = false;
            Log("*** CONNECTION LOST ***");
        };

        wrapper.OnConnectionRestored += (dataLost) =>
        {
            isConnected = true;
            Log("*** CONNECTION RESTORED ***");
            if (dataLost)
            {
                Log("Resubscribing to market data...");
                ResubscribeMarketData();
            }

            // Auto-activate trading if not already active
            if (!isActive)
            {
                var watchlist = WatchlistManager.Load();
                if (watchlist.EnabledTickers.Any())
                {
                    Log("IBKR reconnected - auto-activating trading...");
                    ActivateTrading();
                }
            }
        };

        return true;
    }

    private static void InitializeWebFrontendClient()
    {
        try
        {
            webFrontendClient = new WebFrontendClient(new WebFrontendConfig
            {
                BaseUrl = AppSettings.WebFrontendUrl ?? "http://localhost:5000",
                Enabled = true,
                BatchTicks = true,
                BatchSize = 10,
                BatchTimeout = TimeSpan.FromMilliseconds(100)
            });

            var connected = webFrontendClient.TestConnectionAsync().GetAwaiter().GetResult();
            if (connected)
            {
                Log("Web frontend connected - streaming to browser");
            }
            else
            {
                Log("Web frontend not available - start IdiotProof.Web for UI");
            }
        }
        catch (Exception ex)
        {
            Log($"Web frontend init failed: {ex.Message}");
            webFrontendClient = null;
        }
    }

    private static void LoadStrategies()
    {
        Log("Loading configuration...");

        // Ensure required folders exist
        SettingsManager.EnsureFoldersExist();

        // Create sample strategy rules if needed
        StrategyRulesManager.CreateSampleIfNotExists();

        // Load custom strategy rules
        var rules = StrategyRulesManager.Load();
        if (rules.Enabled && rules.EnabledRules.Any())
        {
            Log($"[StrategyRules] {rules.EnabledRules.Count()} custom rules loaded");
        }

        strategies = [];
        Log("Configuration loaded. Strategies created from watchlist on activation.");
    }

    private static void ActivateTrading()
    {
        if (isActive)
        {
            Log("Trading already active.");
            return;
        }

        if (!isConnected || client == null || wrapper == null)
        {
            Log("Cannot activate: Not connected to IBKR.");
            return;
        }

        var watchlist = WatchlistManager.Load();
        var enabledTickers = watchlist.EnabledTickers.ToList();
        if (enabledTickers.Count == 0)
        {
            Log("No enabled tickers in watchlist.");
            return;
        }

        Log($"Activating trading with {enabledTickers.Count} ticker(s)...");

        // Create strategies for each enabled ticker
        strategies.Clear();
        foreach (var entry in enabledTickers)
        {
            var hasProfile = File.Exists(Path.Combine(SettingsManager.GetTickerDataFolder(entry.Symbol), $"{entry.Symbol}.profile.json"));
            var price = GetTickerPrice(entry.Symbol);
            var qty = entry.GetQuantityForPrice(price > 0 ? price : 10.0);

            var strategy = new TradingStrategy
            {
                Symbol = entry.Symbol,
                Name = entry.Name ?? $"{entry.Symbol} Auto",
                Conditions = [],
                Order = new OrderAction
                {
                    Side = IdiotProof.Enums.OrderSide.Buy,
                    Quantity = qty
                },
                Session = watchlist.DefaultSession,
                Enabled = true
            };

            strategies.Add(strategy);
            Log($"  [{entry.Symbol}] Qty: {qty}{(hasProfile ? " (profile)" : "")}");
        }

        var enabledStrategies = strategies.FindAll(s => s.Enabled);
        var uniqueSymbols = enabledStrategies.Select(s => s.Symbol).Distinct().ToList();

        // Fetch historical data for warm-up
        if (historicalDataService != null && historicalDataStore != null)
        {
            var symbolsToFetch = uniqueSymbols.Where(s => !historicalDataStore.HasData(s)).ToList();

            if (symbolsToFetch.Count > 0)
            {
                Log($"Fetching historical data for {symbolsToFetch.Count} symbol(s)...");
                try
                {
                    var results = historicalDataService.FetchMultipleAsync(symbolsToFetch, barCount: 200, maxConcurrency: 3)
                        .GetAwaiter().GetResult();

                    foreach (var kvp in results)
                    {
                        if (kvp.Value > 0)
                            Log($"  [{kvp.Key}] {kvp.Value} bars loaded");
                        else
                            Log($"  [{kvp.Key}] WARNING: No historical data");
                    }
                }
                catch (Exception ex)
                {
                    Log($"WARNING: Historical data fetch failed: {ex.Message}");
                }
            }
        }

        // Build metadata from historical data
        if (metadataService != null && historicalDataStore != null)
        {
            foreach (var symbol in uniqueSymbols)
            {
                if (historicalDataStore.HasData(symbol))
                {
                    var bars = historicalDataStore.GetBars(symbol);
                    if (bars.Count > 0)
                    {
                        try
                        {
                            metadataService.BuildFromHistoricalBars(symbol, bars);
                        }
                        catch (Exception ex)
                        {
                            Log($"  [{symbol}] Metadata failed: {ex.Message}");
                        }
                    }
                }
            }
        }

        // Subscribe to market data
        int baseTickerId = 1001;
        foreach (var strategy in enabledStrategies)
        {
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

            wrapper.RegisterTickerHandler(tickerId, (price, size) =>
            {
                prices[strategy.Symbol] = price;
                Task.Run(async () => await (webFrontendClient?.OnPriceTickAsync(strategy.Symbol, price, 0, 0, size) ?? Task.CompletedTask));
            });

            client.reqMktData(tickerId, contract, "", false, false, null);
            tickerIds.Add(tickerId);
        }

        // Wait for initial prices
        var priceWaitStart = DateTime.UtcNow;
        while ((DateTime.UtcNow - priceWaitStart).TotalSeconds < 5)
        {
            if (prices.Values.All(p => p > 0))
                break;
            Thread.Sleep(100);
        }

        // Create strategy runners
        foreach (var strategy in enabledStrategies)
        {
            var contract = contracts[strategy.Symbol];
            var runner = new StrategyRunner(strategy, contract, wrapper, client);

            if (metadataService != null)
                runner.TickerMetadata = metadataService.Load(strategy.Symbol);

            if (historicalDataStore?.HasData(strategy.Symbol) == true)
                runner.WarmUpFromHistoricalData(historicalDataStore.GetBars(strategy.Symbol));

            runners.Add(runner);

            // Wire up ticker handler
            int tickerIndex = enabledStrategies
                .Where(s => !enabledStrategies.Take(enabledStrategies.IndexOf(s)).Any(prev => prev.Symbol == s.Symbol))
                .ToList()
                .FindIndex(s => s.Symbol == strategy.Symbol);

            if (tickerIndex >= 0)
            {
                int tickerId = 1001 + tickerIndex;
                var symbol = strategy.Symbol;
                wrapper.RegisterTickerHandler(tickerId, (price, size) =>
                {
                    prices[symbol] = price;
                    runner.OnLastTrade(price, size);
                });

                wrapper.RegisterBidAskHandler(tickerId, runner.OnBidAskUpdate);
            }
        }

        isActive = true;
        Log(">>> TRADING ACTIVATED <<<");
        sessionLogger?.LogEvent("TRADING", "Trading activated");

        StartPriceCheckTimer();
        StartWebFrontendTimer();
    }

    private static void DeactivateTrading()
    {
        if (!isActive)
            return;

        Log("Deactivating trading...");

        foreach (var runner in runners)
            runner.Dispose();
        runners.Clear();

        if (client != null && wrapper != null)
        {
            foreach (var tickerId in tickerIds)
            {
                client.cancelMktData(tickerId);
                wrapper.UnregisterTickerHandler(tickerId);
            }
        }
        tickerIds.Clear();
        contracts.Clear();
        prices.Clear();

        StopPriceCheckTimer();
        StopWebFrontendTimer();

        isActive = false;
        Log("Trading deactivated.");
        sessionLogger?.LogEvent("TRADING", "Trading deactivated");
    }

    private static void StartWebFrontendTimer()
    {
        if (webFrontendClient == null) return;
        webFrontendTimer = new Timer(OnWebFrontendTimer, null, 0, 5000);
    }

    private static void StopWebFrontendTimer()
    {
        webFrontendTimer?.Dispose();
        webFrontendTimer = null;
    }

    private static async void OnWebFrontendTimer(object? state)
    {
        if (webFrontendClient == null || wrapper == null) return;

        try
        {
            await webFrontendClient.SendHeartbeatAsync();

            wrapper.RequestPositionsAndWait(TimeSpan.FromSeconds(2));
            var positions = wrapper.Positions
                .Where(p => p.Value.Quantity != 0)
                .Select(p => new PositionPayload
                {
                    Symbol = p.Key,
                    Quantity = p.Value.Quantity,
                    AvgCost = p.Value.AvgCost,
                    MarketPrice = prices.TryGetValue(p.Key, out var price) ? price : null,
                    UnrealizedPnL = prices.TryGetValue(p.Key, out var mktPrice)
                        ? ((double)p.Value.Quantity * mktPrice) - ((double)p.Value.Quantity * p.Value.AvgCost)
                        : null
                })
                .ToList();

            if (positions.Count > 0)
                await webFrontendClient.SendPositionsAsync(positions);

            var orders = wrapper.OpenOrders
                .Select(o => new OrderPayload
                {
                    OrderId = o.Key,
                    Symbol = o.Value.Symbol,
                    Direction = o.Value.Action,
                    Quantity = (int)o.Value.Qty,
                    OrderType = o.Value.Type,
                    LimitPrice = o.Value.LmtPrice > 0 ? o.Value.LmtPrice : null,
                    StopPrice = null,
                    Status = o.Value.Status
                })
                .ToList();

            await webFrontendClient.SendOrdersAsync(orders);

            // Process commands from Web UI
            var commands = await webFrontendClient.GetPendingCommandsAsync();
            foreach (var cmd in commands)
            {
                ProcessWebCommand(cmd);
            }
        }
        catch
        {
            // Silent fail - don't interrupt trading
        }
    }

    private static void ProcessWebCommand(TradingCommandPayload cmd)
    {
        if (client == null || wrapper == null) return;

        try
        {
            switch (cmd.Type)
            {
                case "ActivateTrading":
                    Log("[Web] Activate trading");
                    ActivateTrading();
                    break;

                case "DeactivateTrading":
                    Log("[Web] Deactivate trading");
                    DeactivateTrading();
                    break;

                case "CancelOrder":
                    Log($"[Web] Cancel order {cmd.OrderId}");
                    wrapper.CancelOrder(cmd.OrderId);
                    break;

                case "CancelAllOrders":
                    Log("[Web] Cancel all orders");
                    wrapper.CancelAllOrders();
                    break;

                case "ClosePosition":
                    if (!string.IsNullOrEmpty(cmd.Symbol))
                    {
                        Log($"[Web] Close position {cmd.Symbol}");
                        ClosePositionBySymbol(cmd.Symbol);
                    }
                    break;

                case "CloseAllPositions":
                    Log("[Web] Close all positions");
                    CloseAllPositions();
                    break;

                case "ReloadWatchlist":
                    Log("[Web] Reload watchlist");
                    var wasActive = isActive;
                    if (wasActive) DeactivateTrading();
                    LoadStrategies();
                    if (wasActive) ActivateTrading();
                    break;
            }
        }
        catch (Exception ex)
        {
            Log($"[Web] Command error: {ex.Message}");
        }
    }

    private static void ClosePositionBySymbol(string symbol)
    {
        if (wrapper == null || client == null) return;

        if (wrapper.Positions.TryGetValue(symbol, out var pos) && pos.Quantity != 0)
        {
            var contract = new Contract
            {
                Symbol = symbol,
                SecType = "STK",
                Exchange = "SMART",
                Currency = "USD"
            };

            var order = new Order
            {
                Action = pos.Quantity > 0 ? "SELL" : "BUY",
                OrderType = "MKT",
                TotalQuantity = Math.Abs(pos.Quantity),
                Transmit = true,
                OutsideRth = true
            };

            var orderId = wrapper.ConsumeNextOrderId();
            client.placeOrder(orderId, contract, order);
            Log($"Close order: {order.Action} {order.TotalQuantity} {symbol} @ MKT (ID: {orderId})");
        }
    }

    private static void CloseAllPositions()
    {
        if (wrapper == null) return;

        var managedTickers = runners.Select(r => r.Strategy.Symbol).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var pos in wrapper.Positions.Where(p => p.Value.Quantity != 0 && managedTickers.Contains(p.Key)))
        {
            ClosePositionBySymbol(pos.Key);
        }
    }

    private static void StartPriceCheckTimer()
    {
        if (AppSettings.TickerPriceCheckIntervalSeconds <= 0)
            return;

        foreach (var kvp in prices)
            previousPrices[kvp.Key] = kvp.Value;

        var intervalMs = AppSettings.TickerPriceCheckIntervalSeconds * 1000;
        priceCheckTimer = new Timer(OnPriceCheckTimer, null, intervalMs, intervalMs);
    }

    private static void StopPriceCheckTimer()
    {
        priceCheckTimer?.Dispose();
        priceCheckTimer = null;
        previousPrices.Clear();
    }

    private static void OnPriceCheckTimer(object? state)
    {
        if (!isActive || prices.Count == 0)
            return;

        var priceReports = new List<string>();

        foreach (var kvp in prices.OrderBy(p => p.Key))
        {
            var symbol = kvp.Key;
            var currentPrice = kvp.Value;

            if (currentPrice <= 0)
                continue;

            var previousPrice = previousPrices.TryGetValue(symbol, out var prev) ? prev : currentPrice;
            var percentChange = previousPrice > 0 ? ((currentPrice - previousPrice) / previousPrice) * 100 : 0;

            string changeIndicator;
            if (percentChange > 0.001)
                changeIndicator = $"+{percentChange:F2}%";
            else if (percentChange < -0.001)
                changeIndicator = $"{percentChange:F2}%";
            else
                changeIndicator = "0.00%";

            priceReports.Add($"{symbol}: {currentPrice:F2} ({changeIndicator})");
            previousPrices[symbol] = currentPrice;
        }

        if (priceReports.Count > 0)
        {
            Log(string.Join(" | ", priceReports));
        }
    }

    private static double GetTickerPrice(string symbol)
    {
        if (wrapper == null || client == null || !isConnected)
            return 0;

        if (prices.TryGetValue(symbol, out var cachedPrice) && cachedPrice > 0)
            return cachedPrice;

        try
        {
            var contract = new Contract
            {
                Symbol = symbol,
                SecType = "STK",
                Currency = "USD",
                Exchange = "SMART"
            };

            double price = 0;
            var priceReceived = new ManualResetEventSlim(false);
            var tickerId = wrapper.ConsumeNextOrderId();

            wrapper.RegisterTickerHandler(tickerId, (p, size) =>
            {
                if (p > 0)
                {
                    price = p;
                    priceReceived.Set();
                }
            });

            client.reqMktData(tickerId, contract, "", true, false, null);
            priceReceived.Wait(TimeSpan.FromSeconds(3));
            wrapper.UnregisterTickerHandler(tickerId);

            return price;
        }
        catch
        {
            return 0;
        }
    }

    private static void ResubscribeMarketData()
    {
        if (client == null) return;

        int tickerIndex = 0;
        foreach (var kvp in contracts)
        {
            if (tickerIndex < tickerIds.Count)
            {
                int tickerId = tickerIds[tickerIndex];
                try { client.cancelMktData(tickerId); } catch { }
                Thread.Sleep(100);
                client.reqMktData(tickerId, kvp.Value, "", false, false, null);
                tickerIndex++;
            }
        }
        Log("Market data resubscribed.");
    }

    private static void Shutdown()
    {
        Log("Shutting down...");

        heartbeatTimer?.Dispose();
        sessionLogger?.WriteFinalLog("Shutdown");

        DeactivateTrading();

        historicalDataService?.Dispose();
        client?.eDisconnect();
        wrapper?.Dispose();
        sessionLogger?.Dispose();
        tradeTrackingService?.Dispose();
        alertService?.Dispose();
        alertWebIntegration?.Dispose();
        webFrontendClient?.Dispose();
        shutdownCts.Dispose();

        Log("Core engine stopped.");
    }

    /// <summary>
    /// Logs a message to the session logger and sends to Web UI.
    /// </summary>
    private static void Log(string message)
    {
        // Log to session logger (writes to file)
        sessionLogger?.LogEvent("CORE", message);

        // Send to Web UI log tab
        Task.Run(async () =>
        {
            try
            {
                await (webFrontendClient?.SendLogMessageAsync(message) ?? Task.CompletedTask);
            }
            catch
            {
                // Silent fail
            }
        });

        // Also write to console for debugging (minimal output)
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
    }
}
