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
    private static IbWrapper? _wrapper;
    private static EClientSocket? _client;
    private static readonly CancellationTokenSource _shutdownCts = new();
    private static TradeTrackingService? _tradeTrackingService;
    private static SessionLogger? _sessionLogger;

    // Historical data components
    private static HistoricalDataStore? _historicalDataStore;
    private static HistoricalDataService? _historicalDataService;
    private static BacktestService? _backtestService;
    private static TickerMetadataService? _metadataService;

    // Alert components
    private static AlertService? _alertService;
    private static SuddenMoveDetector? _suddenMoveDetector;

    // Web frontend integration
    private static WebFrontendClient? _webFrontendClient;
    private static AlertWebIntegration? _alertWebIntegration;

    // State
    private static readonly List<StrategyRunner> _runners = [];
    private static readonly List<int> _tickerIds = [];
    private static readonly Dictionary<string, Contract> _contracts = [];
    private static readonly Dictionary<string, double> _prices = [];
    private static readonly Dictionary<string, double> _previousPrices = [];
    private static List<TradingStrategy> _strategies = [];
    private static bool _isConnected;
    private static bool _isActive;
    private static Timer? _priceCheckTimer;
    private static Timer? _webFrontendTimer;
    private static Timer? _heartbeatTimer;

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
        _sessionLogger = new SessionLogger();
        _sessionLogger.LogEvent("STARTUP", $"Headless mode | {(AppSettings.IsPaperTrading ? "PAPER" : "LIVE")} | Port: {AppSettings.Port}");

        // Share session logger with all backend classes
        StrategyRunner.SessionLogger = _sessionLogger;
        IbWrapper.SessionLogger = _sessionLogger;
        StrategyManager.SessionLogger = _sessionLogger;
        StrategyValidatorHelper.SessionLogger = _sessionLogger;

        // Initialize trade tracking service
        _tradeTrackingService = new TradeTrackingService();
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
        if (_isConnected)
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
            _shutdownCts.Cancel();
        };

        // Wait for shutdown signal
        Log("Core engine running. Waiting for shutdown signal...");
        try
        {
            _shutdownCts.Token.WaitHandle.WaitOne();
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
        _heartbeatTimer = new Timer(_ =>
        {
            var status = _isConnected ? "Connected" : "Disconnected";
            var trading = _isActive ? $"Trading ({_runners.Count} strategies)" : "Idle";
            Log($"[Heartbeat] IBKR: {status} | Status: {trading}");
        }, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    private static bool ConnectToIbkr()
    {
        Log($"Connecting to IBKR at {AppSettings.Host}:{AppSettings.Port}...");

        _wrapper = new IbWrapper();
        _client = new EClientSocket(_wrapper, _wrapper.Signal);
        _wrapper.AttachClient(_client);

        _client.eConnect(AppSettings.Host, AppSettings.Port, AppSettings.ClientId);

        // Start reader thread
        var reader = new EReader(_client, _wrapper.Signal);
        reader.Start();

        var readerThread = new Thread(() =>
        {
            while (_client.IsConnected())
            {
                _wrapper.Signal.waitForSignal();
                reader.processMsgs();
            }
        })
        {
            IsBackground = true
        };
        readerThread.Start();

        // Wait for connection
        if (!_wrapper.WaitForNextValidId(TimeSpan.FromSeconds(AppSettings.ConnectionTimeoutSeconds)))
        {
            _isConnected = false;
            return false;
        }

        _isConnected = true;
        Log("Connected to IBKR successfully!");
        Log($"Trading Mode: {(AppSettings.IsPaperTrading ? "PAPER" : "LIVE")}");

        // Subscribe to account updates
        _wrapper.RequestAccountUpdates(AppSettings.AccountNumber ?? "");

        // Initialize historical data components
        _historicalDataStore = new HistoricalDataStore();
        _historicalDataService = new HistoricalDataService(_client, _wrapper, _historicalDataStore);
        _backtestService = new BacktestService(_historicalDataService);
        _metadataService = new TickerMetadataService();
        Log("Historical data service initialized");

        // Setup reconnection handlers
        _wrapper.OnConnectionLost += () =>
        {
            _isConnected = false;
            Log("*** CONNECTION LOST ***");
        };

        _wrapper.OnConnectionRestored += (dataLost) =>
        {
            _isConnected = true;
            Log("*** CONNECTION RESTORED ***");
            if (dataLost)
            {
                Log("Resubscribing to market data...");
                ResubscribeMarketData();
            }

            // Auto-activate trading if not already active
            if (!_isActive)
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
            _webFrontendClient = new WebFrontendClient(new WebFrontendConfig
            {
                BaseUrl = AppSettings.WebFrontendUrl ?? "http://localhost:5000",
                Enabled = true,
                BatchTicks = true,
                BatchSize = 10,
                BatchTimeout = TimeSpan.FromMilliseconds(100)
            });

            var connected = _webFrontendClient.TestConnectionAsync().GetAwaiter().GetResult();
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
            _webFrontendClient = null;
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

        _strategies = [];
        Log("Configuration loaded. Strategies created from watchlist on activation.");
    }

    private static void ActivateTrading()
    {
        if (_isActive)
        {
            Log("Trading already active.");
            return;
        }

        if (!_isConnected || _client == null || _wrapper == null)
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
        _strategies.Clear();
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

            _strategies.Add(strategy);
            Log($"  [{entry.Symbol}] Qty: {qty}{(hasProfile ? " (profile)" : "")}");
        }

        var enabledStrategies = _strategies.FindAll(s => s.Enabled);
        var uniqueSymbols = enabledStrategies.Select(s => s.Symbol).Distinct().ToList();

        // Fetch historical data for warm-up
        if (_historicalDataService != null && _historicalDataStore != null)
        {
            var symbolsToFetch = uniqueSymbols.Where(s => !_historicalDataStore.HasData(s)).ToList();

            if (symbolsToFetch.Count > 0)
            {
                Log($"Fetching historical data for {symbolsToFetch.Count} symbol(s)...");
                try
                {
                    var results = _historicalDataService.FetchMultipleAsync(symbolsToFetch, barCount: 200, maxConcurrency: 3)
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
        if (_metadataService != null && _historicalDataStore != null)
        {
            foreach (var symbol in uniqueSymbols)
            {
                if (_historicalDataStore.HasData(symbol))
                {
                    var bars = _historicalDataStore.GetBars(symbol);
                    if (bars.Count > 0)
                    {
                        try
                        {
                            _metadataService.BuildFromHistoricalBars(symbol, bars);
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
            if (_contracts.ContainsKey(strategy.Symbol))
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

            _contracts[strategy.Symbol] = contract;
            _prices[strategy.Symbol] = 0;

            _wrapper.RegisterTickerHandler(tickerId, (price, size) =>
            {
                _prices[strategy.Symbol] = price;
                Task.Run(async () => await (_webFrontendClient?.OnPriceTickAsync(strategy.Symbol, price, 0, 0, size) ?? Task.CompletedTask));
            });

            _client.reqMktData(tickerId, contract, "", false, false, null);
            _tickerIds.Add(tickerId);
        }

        // Wait for initial prices
        var priceWaitStart = DateTime.UtcNow;
        while ((DateTime.UtcNow - priceWaitStart).TotalSeconds < 5)
        {
            if (_prices.Values.All(p => p > 0))
                break;
            Thread.Sleep(100);
        }

        // Create strategy runners
        foreach (var strategy in enabledStrategies)
        {
            var contract = _contracts[strategy.Symbol];
            var runner = new StrategyRunner(strategy, contract, _wrapper, _client);

            if (_metadataService != null)
                runner.TickerMetadata = _metadataService.Load(strategy.Symbol);

            if (_historicalDataStore?.HasData(strategy.Symbol) == true)
                runner.WarmUpFromHistoricalData(_historicalDataStore.GetBars(strategy.Symbol));

            _runners.Add(runner);

            // Wire up ticker handler
            int tickerIndex = enabledStrategies
                .Where(s => !enabledStrategies.Take(enabledStrategies.IndexOf(s)).Any(prev => prev.Symbol == s.Symbol))
                .ToList()
                .FindIndex(s => s.Symbol == strategy.Symbol);

            if (tickerIndex >= 0)
            {
                int tickerId = 1001 + tickerIndex;
                var symbol = strategy.Symbol;
                _wrapper.RegisterTickerHandler(tickerId, (price, size) =>
                {
                    _prices[symbol] = price;
                    runner.OnLastTrade(price, size);
                });

                _wrapper.RegisterBidAskHandler(tickerId, runner.OnBidAskUpdate);
            }
        }

        _isActive = true;
        Log(">>> TRADING ACTIVATED <<<");
        _sessionLogger?.LogEvent("TRADING", "Trading activated");

        StartPriceCheckTimer();
        StartWebFrontendTimer();
    }

    private static void DeactivateTrading()
    {
        if (!_isActive)
            return;

        Log("Deactivating trading...");

        foreach (var runner in _runners)
            runner.Dispose();
        _runners.Clear();

        if (_client != null && _wrapper != null)
        {
            foreach (var tickerId in _tickerIds)
            {
                _client.cancelMktData(tickerId);
                _wrapper.UnregisterTickerHandler(tickerId);
            }
        }
        _tickerIds.Clear();
        _contracts.Clear();
        _prices.Clear();

        StopPriceCheckTimer();
        StopWebFrontendTimer();

        _isActive = false;
        Log("Trading deactivated.");
        _sessionLogger?.LogEvent("TRADING", "Trading deactivated");
    }

    private static void StartWebFrontendTimer()
    {
        if (_webFrontendClient == null) return;
        _webFrontendTimer = new Timer(OnWebFrontendTimer, null, 0, 5000);
    }

    private static void StopWebFrontendTimer()
    {
        _webFrontendTimer?.Dispose();
        _webFrontendTimer = null;
    }

    private static async void OnWebFrontendTimer(object? state)
    {
        if (_webFrontendClient == null || _wrapper == null) return;

        try
        {
            await _webFrontendClient.SendHeartbeatAsync();

            _wrapper.RequestPositionsAndWait(TimeSpan.FromSeconds(2));
            var positions = _wrapper.Positions
                .Where(p => p.Value.Quantity != 0)
                .Select(p => new PositionPayload
                {
                    Symbol = p.Key,
                    Quantity = p.Value.Quantity,
                    AvgCost = p.Value.AvgCost,
                    MarketPrice = _prices.TryGetValue(p.Key, out var price) ? price : null,
                    UnrealizedPnL = _prices.TryGetValue(p.Key, out var mktPrice)
                        ? ((double)p.Value.Quantity * mktPrice) - ((double)p.Value.Quantity * p.Value.AvgCost)
                        : null
                })
                .ToList();

            if (positions.Count > 0)
                await _webFrontendClient.SendPositionsAsync(positions);

            var orders = _wrapper.OpenOrders
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

            await _webFrontendClient.SendOrdersAsync(orders);

            // Process commands from Web UI
            var commands = await _webFrontendClient.GetPendingCommandsAsync();
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
        if (_client == null || _wrapper == null) return;

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
                    _wrapper.CancelOrder(cmd.OrderId);
                    break;

                case "CancelAllOrders":
                    Log("[Web] Cancel all orders");
                    _wrapper.CancelAllOrders();
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
                    var wasActive = _isActive;
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
        if (_wrapper == null || _client == null) return;

        if (_wrapper.Positions.TryGetValue(symbol, out var pos) && pos.Quantity != 0)
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

            var orderId = _wrapper.ConsumeNextOrderId();
            _client.placeOrder(orderId, contract, order);
            Log($"Close order: {order.Action} {order.TotalQuantity} {symbol} @ MKT (ID: {orderId})");
        }
    }

    private static void CloseAllPositions()
    {
        if (_wrapper == null) return;

        var managedTickers = _runners.Select(r => r.Strategy.Symbol).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var pos in _wrapper.Positions.Where(p => p.Value.Quantity != 0 && managedTickers.Contains(p.Key)))
        {
            ClosePositionBySymbol(pos.Key);
        }
    }

    private static void StartPriceCheckTimer()
    {
        if (AppSettings.TickerPriceCheckIntervalSeconds <= 0)
            return;

        foreach (var kvp in _prices)
            _previousPrices[kvp.Key] = kvp.Value;

        var intervalMs = AppSettings.TickerPriceCheckIntervalSeconds * 1000;
        _priceCheckTimer = new Timer(OnPriceCheckTimer, null, intervalMs, intervalMs);
    }

    private static void StopPriceCheckTimer()
    {
        _priceCheckTimer?.Dispose();
        _priceCheckTimer = null;
        _previousPrices.Clear();
    }

    private static void OnPriceCheckTimer(object? state)
    {
        if (!_isActive || _prices.Count == 0)
            return;

        var priceReports = new List<string>();

        foreach (var kvp in _prices.OrderBy(p => p.Key))
        {
            var symbol = kvp.Key;
            var currentPrice = kvp.Value;

            if (currentPrice <= 0)
                continue;

            var previousPrice = _previousPrices.TryGetValue(symbol, out var prev) ? prev : currentPrice;
            var percentChange = previousPrice > 0 ? ((currentPrice - previousPrice) / previousPrice) * 100 : 0;

            string changeIndicator;
            if (percentChange > 0.001)
                changeIndicator = $"+{percentChange:F2}%";
            else if (percentChange < -0.001)
                changeIndicator = $"{percentChange:F2}%";
            else
                changeIndicator = "0.00%";

            priceReports.Add($"{symbol}: {currentPrice:F2} ({changeIndicator})");
            _previousPrices[symbol] = currentPrice;
        }

        if (priceReports.Count > 0)
        {
            Log(string.Join(" | ", priceReports));
        }
    }

    private static double GetTickerPrice(string symbol)
    {
        if (_wrapper == null || _client == null || !_isConnected)
            return 0;

        if (_prices.TryGetValue(symbol, out var cachedPrice) && cachedPrice > 0)
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
            var tickerId = _wrapper.ConsumeNextOrderId();

            _wrapper.RegisterTickerHandler(tickerId, (p, size) =>
            {
                if (p > 0)
                {
                    price = p;
                    priceReceived.Set();
                }
            });

            _client.reqMktData(tickerId, contract, "", true, false, null);
            priceReceived.Wait(TimeSpan.FromSeconds(3));
            _wrapper.UnregisterTickerHandler(tickerId);

            return price;
        }
        catch
        {
            return 0;
        }
    }

    private static void ResubscribeMarketData()
    {
        if (_client == null) return;

        int tickerIndex = 0;
        foreach (var kvp in _contracts)
        {
            if (tickerIndex < _tickerIds.Count)
            {
                int tickerId = _tickerIds[tickerIndex];
                try { _client.cancelMktData(tickerId); } catch { }
                Thread.Sleep(100);
                _client.reqMktData(tickerId, kvp.Value, "", false, false, null);
                tickerIndex++;
            }
        }
        Log("Market data resubscribed.");
    }

    private static void Shutdown()
    {
        Log("Shutting down...");

        _heartbeatTimer?.Dispose();
        _sessionLogger?.WriteFinalLog("Shutdown");

        DeactivateTrading();

        _historicalDataService?.Dispose();
        _client?.eDisconnect();
        _wrapper?.Dispose();
        _sessionLogger?.Dispose();
        _tradeTrackingService?.Dispose();
        _alertService?.Dispose();
        _alertWebIntegration?.Dispose();
        _webFrontendClient?.Dispose();
        _shutdownCts.Dispose();

        Log("Core engine stopped.");
    }

    /// <summary>
    /// Logs a message to the session logger and sends to Web UI.
    /// </summary>
    private static void Log(string message)
    {
        // Log to session logger (writes to file)
        _sessionLogger?.LogEvent("CORE", message);

        // Send to Web UI log tab
        Task.Run(async () =>
        {
            try
            {
                await (_webFrontendClient?.SendLogMessageAsync(message) ?? Task.CompletedTask);
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
