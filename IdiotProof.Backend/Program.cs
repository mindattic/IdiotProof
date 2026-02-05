// ================================================================
// IdiotProof Backend Service
// Handles IBKR API communication, controlled by frontend via IPC
// ================================================================

using IBApi;
using IdiotProof.Backend.Helpers;
using IdiotProof.Backend.Ipc;
using IdiotProof.Backend.Logging;
using IdiotProof.Backend.Models;
using IdiotProof.Backend.Services;
using IdiotProof.Backend.Strategy;
using IdiotProof.Shared.Helpers;
using IdiotProof.Shared.Models;
using IdiotProof.Shared.Services;
using IdiotProof.Shared.Settings;
using IdiotProof.Shared.Validation;

namespace IdiotProof.Backend
{
    internal sealed class Program
    {
        // Core components
        private static IbWrapper? _wrapper;
        private static EClientSocket? _client;
        private static IpcServer? _ipcServer;
        private static readonly CancellationTokenSource _shutdownCts = new();
        private static TradeTrackingService? _tradeTrackingService;
        private static SessionLogger? _sessionLogger;

        // Historical data components
        private static HistoricalDataStore? _historicalDataStore;
        private static HistoricalDataService? _historicalDataService;

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

        public static void Main(string[] args)
        {
            // Parse command line args
            ParseArgs(args);

            // Setup crash handler
            CrashHandler.Setup();

            try
            {
                Run();
            }
            catch (Exception ex)
            {
                CrashHandler.WriteCrashDump(ex, "Main Thread Exception");
                throw;
            }
        }

        private static void ParseArgs(string[] args)
        {
            foreach (var arg in args)
            {
                if (arg.Equals("--silent", StringComparison.OrdinalIgnoreCase) ||
                    arg.Equals("-s", StringComparison.OrdinalIgnoreCase))
                {
                    Settings.SilentMode = true;
                }
            }
        }

        private static void Run()
        {
            Log("IdiotProof Backend starting...");
            Log($"Mode: {(Settings.IsPaperTrading ? "PAPER" : "LIVE")} | Port: {Settings.Port}");

            // Initialize session logger (logs every 20 min, on crash, and on close)
            _sessionLogger = new SessionLogger();
            _sessionLogger.LogEvent("STARTUP", $"Mode: {(Settings.IsPaperTrading ? "PAPER" : "LIVE")} | Port: {Settings.Port}");

            // Share session logger with all backend classes
            StrategyRunner.SessionLogger = _sessionLogger;
            IbWrapper.SessionLogger = _sessionLogger;
            StrategyManager.SessionLogger = _sessionLogger;
            StrategyLoader.SessionLogger = _sessionLogger;
            IdiotProof.Backend.Models.StrategyValidator.SessionLogger = _sessionLogger;
            IpcServer.SessionLogger = _sessionLogger;
            IpcLogger.SessionLogger = _sessionLogger;

            // Initialize trade tracking service
            _tradeTrackingService = new TradeTrackingService();
            Log("Trade tracking service initialized");

            // Start IPC server for frontend communication
            StartIpcServer();

            // Connect to IBKR
            if (!ConnectToIbkr())
            {
                Log("ERROR: Failed to connect to IBKR. Backend will wait for retry...");
            }

            // Load strategies from disk
            LoadStrategies();

            // Auto-start trading when strategies are loaded and IBKR is connected
            if (_strategies.Count > 0 && _isConnected)
            {
                ActivateTrading();
            }

            Log("Backend ready. Waiting for commands from frontend...");

            // Wait for shutdown signal
            _shutdownCts.Token.WaitHandle.WaitOne();

            // Cleanup
            Shutdown();
        }

        private static void StartIpcServer()
        {
            _ipcServer = new IpcServer();

            // Wire up IPC handlers
            _ipcServer.GetStatusHandler = GetStatusAsync;
            _ipcServer.GetOrdersHandler = GetOrdersAsync;
            _ipcServer.GetIdiotProofOrdersHandler = GetIdiotProofOrdersAsync;
            _ipcServer.GetPositionsHandler = GetPositionsAsync;
            _ipcServer.CancelOrderHandler = CancelOrderAsync;
            _ipcServer.CancelAllOrdersHandler = CancelAllOrdersAsync;
            _ipcServer.ClosePositionHandler = ClosePositionAsync;
            _ipcServer.ReloadStrategiesHandler = ReloadStrategiesAsync;
            _ipcServer.SetStrategiesHandler = SetStrategiesAsync;
            _ipcServer.ActivateStrategyHandler = ActivateStrategyAsync;
            _ipcServer.DeactivateStrategyHandler = DeactivateStrategyAsync;
            _ipcServer.ActivateTradingHandler = ActivateTradingAsync;
            _ipcServer.DeactivateTradingHandler = DeactivateTradingAsync;
            _ipcServer.ValidateStrategyHandler = ValidateStrategyAsync;
            _ipcServer.GetTradesHandler = GetTradesAsync;

            _ipcServer.Start();
        }

        private static bool ConnectToIbkr()
        {
            Log($"Connecting to IBKR at {Settings.Host}:{Settings.Port}...");

            _wrapper = new IbWrapper();
            _client = new EClientSocket(_wrapper, _wrapper.Signal);
            _wrapper.AttachClient(_client);

            _client.eConnect(Settings.Host, Settings.Port, Settings.ClientId);

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
            if (!_wrapper.WaitForNextValidId(TimeSpan.FromSeconds(Settings.ConnectionTimeoutSeconds)))
            {
                _isConnected = false;
                return false;
            }

            _isConnected = true;
            Log("Connected to IBKR successfully!");
            Log($"Trading Mode: {(Settings.IsPaperTrading ? "PAPER" : "LIVE")}");

            // Initialize historical data components
            _historicalDataStore = new HistoricalDataStore();
            _historicalDataService = new HistoricalDataService(_client, _wrapper, _historicalDataStore);
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

                // Auto-activate trading if we have strategies and aren't already active
                if (!_isActive && _strategies.Count > 0)
                {
                    Log("IBKR connected - auto-activating trading...");
                    ActivateTrading();
                }
            };

            return true;
        }

        private static void LoadStrategies()
        {
            Log("Loading strategies from disk...");

            _strategies = StrategyLoader.LoadFromFile();

            var enabledCount = _strategies.Count(s => s.Enabled);
            Log($"Loaded {_strategies.Count} strategies ({enabledCount} enabled)");

            // Register strategies with session logger
            foreach (var strategy in _strategies)
            {
                _sessionLogger?.RegisterStrategy(strategy.Symbol, strategy.Name ?? strategy.Symbol, strategy.Enabled);
            }

            // Validate using backend's StrategyValidator
            if (_strategies.Count > 0)
            {
                var result = IdiotProof.Backend.Models.StrategyValidator.ValidateAll(_strategies);
                if (!result.IsValid)
                {
                    foreach (var error in result.Errors)
                    {
                        Log($"ERROR: {error}");
                        _sessionLogger?.LogEvent("VALIDATION", $"ERROR: {error}");
                    }
                }
                foreach (var warning in result.Warnings)
                {
                    Log($"WARN: {warning}");
                    _sessionLogger?.LogEvent("VALIDATION", $"WARN: {warning}");
                }
            }
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

            var enabledStrategies = _strategies.FindAll(s => s.Enabled);
            if (enabledStrategies.Count == 0)
            {
                Log("No enabled strategies to run.");
                return;
            }

            Log($"Activating trading with {enabledStrategies.Count} strategies...");

            // Fetch historical data for indicator warm-up (60 bars = 1 hour of 1-minute candles)
            // The HistoricalDataStore avoids duplicate fetches by storing per-symbol
            var uniqueSymbols = enabledStrategies.Select(s => s.Symbol).Distinct().ToList();
            if (_historicalDataService != null && _historicalDataStore != null)
            {
                // Filter to only symbols that don't already have data
                var symbolsToFetch = uniqueSymbols
                    .Where(s => !_historicalDataStore.HasData(s))
                    .ToList();

                if (symbolsToFetch.Count > 0)
                {
                    Log($"Fetching historical data for {symbolsToFetch.Count} symbols (1 hour of 1-min bars)...");
                    try
                    {
                        var results = _historicalDataService.FetchMultipleAsync(
                            symbolsToFetch,
                            barCount: 60, // 1 hour of 1-minute bars
                            maxConcurrency: 3
                        ).GetAwaiter().GetResult();

                        foreach (var kvp in results)
                        {
                            if (kvp.Value > 0)
                                Log($"  [{kvp.Key}] Loaded {kvp.Value} historical bars");
                            else
                                Log($"  [{kvp.Key}] WARNING: Failed to load historical data");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"WARNING: Historical data fetch failed: {ex.Message}");
                        Log("Continuing without historical data warm-up...");
                    }
                }
                else
                {
                    Log($"Historical data already loaded for all {uniqueSymbols.Count} symbols");
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
                });

                _client.reqMktData(tickerId, contract, "", false, false, null);
                _tickerIds.Add(tickerId);
            }

            // Wait for prices
            var priceWaitStart = DateTime.UtcNow;
            while ((DateTime.UtcNow - priceWaitStart).TotalSeconds < 5)
            {
                if (_prices.Values.All(p => p > 0))
                    break;
                Thread.Sleep(100);
            }

            // Create runners
            foreach (var strategy in enabledStrategies)
            {
                var contract = _contracts[strategy.Symbol];
                var runner = new StrategyRunner(strategy, contract, _wrapper, _client);

                // Warm up indicators from historical data if available
                if (_historicalDataStore != null && _historicalDataStore.HasData(strategy.Symbol))
                {
                    var historicalBars = _historicalDataStore.GetBars(strategy.Symbol);
                    runner.WarmUpFromHistoricalData(historicalBars);
                }

                _runners.Add(runner);

                // Wire up ticker handler
                int tickerIndex = enabledStrategies
                    .Where(s => !enabledStrategies.Take(enabledStrategies.IndexOf(s)).Any(prev => prev.Symbol == s.Symbol))
                    .ToList()
                    .FindIndex(s => s.Symbol == strategy.Symbol);

                if (tickerIndex >= 0)
                {
                    int tickerId = 1001 + tickerIndex;
                    _wrapper.RegisterTickerHandler(tickerId, runner.OnLastTrade);
                }
            }

            _isActive = true;
            Log(">>> TRADING ACTIVATED <<<");
            _sessionLogger?.LogEvent("TRADING", "Trading activated");

            // Start periodic price check timer
            StartPriceCheckTimer();
        }

        private static void StartPriceCheckTimer()
        {
            if (Settings.TickerPriceCheckIntervalSeconds <= 0)
                return;

            // Initialize previous prices with current prices
            foreach (var kvp in _prices)
            {
                _previousPrices[kvp.Key] = kvp.Value;
            }

            var intervalMs = Settings.TickerPriceCheckIntervalSeconds * 1000;
            _priceCheckTimer = new Timer(OnPriceCheckTimer, null, intervalMs, intervalMs);
            Log($"Price check enabled: every {Settings.TickerPriceCheckIntervalSeconds} second(s)");
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

                // Update previous price for next interval
                _previousPrices[symbol] = currentPrice;
            }

            if (priceReports.Count > 0)
            {
                Log(string.Join(" | ", priceReports));
            }
        }

        private static void DeactivateTrading()
        {
            if (!_isActive)
                return;

            Log("Deactivating trading...");

            // Dispose runners
            foreach (var runner in _runners)
                runner.Dispose();
            _runners.Clear();

            // Cancel market data
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

            // Stop price check timer
            StopPriceCheckTimer();

            _isActive = false;
            Log("Trading deactivated.");
            _sessionLogger?.LogEvent("TRADING", "Trading deactivated");
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

            // Write final session log
            _sessionLogger?.WriteFinalLog("Shutdown");

            DeactivateTrading();

            _historicalDataService?.Dispose();
            _client?.eDisconnect();
            _wrapper?.Dispose();
            _ipcServer?.Dispose();
            _sessionLogger?.Dispose();

            Log("Goodbye!");
        }

        // ----- IPC Handlers -----

        private static Task<StatusResponsePayload> GetStatusAsync()
        {
            // Show loaded strategies count (from _strategies), not just active runners
            // When trading is active, show runner count; otherwise show loaded strategy count
            var strategyCount = _isActive ? _runners.Count : _strategies.Count(s => s.Enabled);

            return Task.FromResult(new StatusResponsePayload
            {
                IsRunning = true,
                IsConnectedToIbkr = _isConnected,
                IsTradingActive = _isActive,
                ActiveStrategies = strategyCount,
                IsPaperTrading = Settings.IsPaperTrading
            });
        }

        private static Task<List<OrderInfo>> GetOrdersAsync()
        {
            if (_wrapper == null)
                return Task.FromResult(new List<OrderInfo>());

            _wrapper.RequestOpenOrdersAndWait(TimeSpan.FromSeconds(3));

            var tradeTracker = _tradeTrackingService;
            var orders = _wrapper.OpenOrders.Select(kvp => 
            {
                var tradeId = tradeTracker?.GetTradeByOrderIdAsync(kvp.Key).Result?.TradeId;
                return new OrderInfo
                {
                    OrderId = kvp.Key,
                    Symbol = kvp.Value.Symbol,
                    Action = kvp.Value.Action,
                    Quantity = (int)kvp.Value.Qty,
                    OrderType = kvp.Value.Type,
                    LimitPrice = kvp.Value.LmtPrice,
                    StatusText = kvp.Value.Status,
                    IdiotProofTradeId = tradeId
                };
            }).ToList();

            return Task.FromResult(orders);
        }

        private static Task<List<PositionInfo>> GetPositionsAsync()
        {
            if (_wrapper == null)
                return Task.FromResult(new List<PositionInfo>());

            try
            {
                // Request positions and wait for response
                _wrapper.RequestPositionsAndWait(TimeSpan.FromSeconds(3));

                // Convert to PositionInfo list
                var positions = _wrapper.Positions.Select(kvp => new PositionInfo
                {
                    Symbol = kvp.Key,
                    Quantity = kvp.Value.Quantity,
                    AvgCost = kvp.Value.AvgCost,
                    // Market price/value will be filled if we have the price cached
                    MarketPrice = _prices.TryGetValue(kvp.Key, out var price) ? price : null,
                    MarketValue = _prices.TryGetValue(kvp.Key, out var p) ? (double)kvp.Value.Quantity * p : null,
                    UnrealizedPnL = _prices.TryGetValue(kvp.Key, out var mktPrice) 
                        ? ((double)kvp.Value.Quantity * mktPrice) - ((double)kvp.Value.Quantity * kvp.Value.AvgCost) 
                        : null
                }).ToList();

                return Task.FromResult(positions);
            }
            catch (Exception ex)
            {
                Log($"Error getting positions: {ex.Message}");
                return Task.FromResult(new List<PositionInfo>());
            }
        }

        private static Task<OperationResultPayload> CancelOrderAsync(int orderId)
        {
            if (_client == null)
                return Task.FromResult(new OperationResultPayload { Success = false, ErrorMessage = "Not connected" });

            try
            {
                _client.cancelOrder(orderId, new IBApi.OrderCancel());
                Log($"Cancel requested for order {orderId}");
                return Task.FromResult(new OperationResultPayload { Success = true, Message = $"Cancel requested for order {orderId}" });
            }
            catch (Exception ex)
            {
                return Task.FromResult(new OperationResultPayload { Success = false, ErrorMessage = ex.Message });
            }
        }

        private static Task<OperationResultPayload> ClosePositionAsync(string symbol)
        {
            if (_wrapper == null || _client == null)
                return Task.FromResult(new OperationResultPayload { Success = false, ErrorMessage = "Not connected" });

            try
            {
                // Refresh positions to get current state
                _wrapper.RequestPositionsAndWait(TimeSpan.FromSeconds(3));

                if (!_wrapper.Positions.TryGetValue(symbol, out var position))
                {
                    return Task.FromResult(new OperationResultPayload { Success = false, ErrorMessage = $"No position found for {symbol}" });
                }

                if (position.Quantity == 0)
                {
                    return Task.FromResult(new OperationResultPayload { Success = true, Message = $"No position to close for {symbol}" });
                }

                // Create contract
                var contract = new Contract
                {
                    Symbol = symbol,
                    SecType = "STK",
                    Currency = "USD",
                    Exchange = "SMART"
                };

                // Create order to close position (opposite side)
                var order = new Order
                {
                    Action = position.Quantity > 0 ? "SELL" : "BUY", // Sell to close long, buy to close short
                    OrderType = "MKT",
                    TotalQuantity = Math.Abs(position.Quantity),
                    Tif = "GTC",
                    OutsideRth = true
                };

                // Get next order ID
                var orderId = _wrapper.ConsumeNextOrderId();

                Log($"Closing position: {order.Action} {order.TotalQuantity} {symbol} @ MKT (OrderId: {orderId})");
                _client.placeOrder(orderId, contract, order);

                return Task.FromResult(new OperationResultPayload { Success = true, Message = $"Close order placed for {symbol}: {order.Action} {order.TotalQuantity} shares" });
            }
            catch (Exception ex)
            {
                Log($"Error closing position for {symbol}: {ex.Message}");
                return Task.FromResult(new OperationResultPayload { Success = false, ErrorMessage = ex.Message });
            }
        }

        private static Task ReloadStrategiesAsync()
        {
            var wasActive = _isActive;
            if (wasActive)
                DeactivateTrading();

            LoadStrategies();

            if (wasActive && _strategies.Count > 0)
                ActivateTrading();

            return Task.CompletedTask;
        }

        private static Task<OperationResultPayload> SetStrategiesAsync(List<StrategyDefinition> definitions)
        {
            try
            {
                var wasActive = _isActive;
                if (wasActive)
                    DeactivateTrading();

                // Build a lookup of existing strategies by ID for smart merging
                var existingById = _strategies.ToDictionary(s => s.Id);
                var newStrategies = new List<TradingStrategy>();
                var keptCount = 0;
                var updatedCount = 0;
                var addedCount = 0;

                foreach (var def in definitions.Where(d => d.Enabled))
                {
                    // Check if we already have this strategy
                    if (existingById.TryGetValue(def.Id, out var existing))
                    {
                        // Compare if the definition has changed
                        // For now, compare by serializing to IdiotScript (canonical form)
                        var existingScript = GetStrategyFingerprint(existing);
                        var newScript = GetDefinitionFingerprint(def);

                        if (existingScript == newScript)
                        {
                            // Strategy unchanged - keep existing instance with its state
                            newStrategies.Add(existing);
                            keptCount++;
                            Log($"Kept unchanged: {def.Name} ({def.Symbol})");
                        }
                        else
                        {
                            // Strategy changed - create new instance
                            var strategy = StrategyLoader.ConvertDefinition(def);
                            if (strategy != null)
                            {
                                newStrategies.Add(strategy);
                                updatedCount++;
                                Log($"Updated strategy: {def.Name} ({def.Symbol})");
                            }
                        }
                    }
                    else
                    {
                        // New strategy - create instance
                        var strategy = StrategyLoader.ConvertDefinition(def);
                        if (strategy != null)
                        {
                            newStrategies.Add(strategy);
                            addedCount++;
                            Log($"Added new strategy: {def.Name} ({def.Symbol})");
                        }
                    }
                }

                // Calculate removed count
                var removedCount = _strategies.Count - keptCount;

                // Replace strategy list
                _strategies.Clear();
                _strategies.AddRange(newStrategies);

                var message = $"Strategies: {keptCount} kept, {updatedCount} updated, {addedCount} added, {removedCount} removed";
                Log(message);

                if (wasActive && _strategies.Count > 0)
                    ActivateTrading();

                return Task.FromResult(new OperationResultPayload 
                { 
                    Success = true, 
                    Message = message 
                });
            }
            catch (Exception ex)
            {
                Log($"Error setting strategies: {ex.Message}");
                return Task.FromResult(new OperationResultPayload 
                { 
                    Success = false, 
                    ErrorMessage = ex.Message 
                });
            }
        }

        /// <summary>
        /// Gets a fingerprint for a TradingStrategy to detect changes.
        /// Uses key properties that define the strategy behavior.
        /// </summary>
        private static string GetStrategyFingerprint(TradingStrategy strategy)
        {
            // Create a fingerprint based on key strategy properties
            var parts = new List<string>
            {
                strategy.Symbol,
                strategy.Order.Quantity.ToString(),
                strategy.Order.Side.ToString(),
                strategy.Order.TakeProfitPrice?.ToString() ?? "",
                strategy.Order.StopLossPrice?.ToString() ?? "",
                strategy.Order.TrailingStopLossPercent.ToString(),
                strategy.StartTime?.ToString() ?? "",
                strategy.EndTime?.ToString() ?? "",
                strategy.ClosePositionTime?.ToString() ?? "",
                strategy.RepeatEnabled.ToString(),
                strategy.Conditions.Count.ToString()
            };

            // Add condition fingerprints
            foreach (var condition in strategy.Conditions)
            {
                parts.Add(condition.Name);
            }

            return string.Join("|", parts);
        }

        /// <summary>
        /// Gets a fingerprint for a StrategyDefinition to detect changes.
        /// </summary>
        private static string GetDefinitionFingerprint(StrategyDefinition def)
        {
            // Serialize to IdiotScript for canonical comparison
            return IdiotProof.Shared.Scripting.IdiotScriptSerializer.Serialize(def);
        }

        private static Task<OperationResultPayload> ActivateStrategyAsync(Guid strategyId)
        {
            try
            {
                // Find the strategy
                var strategy = _strategies.FirstOrDefault(s => s.Id == strategyId);
                if (strategy == null)
                {
                    return Task.FromResult(new OperationResultPayload { Success = false, ErrorMessage = $"Strategy {strategyId} not found" });
                }

                // Check if already has a runner (already active)
                var existingRunner = _runners.FirstOrDefault(r => r.Strategy.Id == strategyId);
                if (existingRunner != null)
                {
                    return Task.FromResult(new OperationResultPayload { Success = true, Message = $"Strategy '{strategy.Name}' is already active" });
                }

                // Need to be connected and trading to add a new runner
                if (!_isConnected || _wrapper == null || _client == null)
                {
                    return Task.FromResult(new OperationResultPayload { Success = false, ErrorMessage = "Not connected to IBKR" });
                }

                if (!_isActive)
                {
                    return Task.FromResult(new OperationResultPayload { Success = false, ErrorMessage = "Trading is not active. Start trading first." });
                }

                // Create contract if not already subscribed
                if (!_contracts.ContainsKey(strategy.Symbol))
                {
                    var contract = new Contract
                    {
                        Symbol = strategy.Symbol,
                        SecType = strategy.SecType,
                        Currency = strategy.Currency,
                        Exchange = strategy.Exchange
                    };
                    if (!string.IsNullOrEmpty(strategy.PrimaryExchange))
                        contract.PrimaryExch = strategy.PrimaryExchange;

                    _contracts[strategy.Symbol] = contract;

                    // Subscribe to market data
                    int tickerId = 1001 + _tickerIds.Count;
                    _tickerIds.Add(tickerId);
                    _prices[strategy.Symbol] = 0;
                    _client.reqMktData(tickerId, contract, "", false, false, null);
                }

                // Create and add runner
                var newContract = _contracts[strategy.Symbol];
                var runner = new StrategyRunner(strategy, newContract, _wrapper, _client);

                // Warm up from historical data if available
                if (_historicalDataStore != null && _historicalDataStore.HasData(strategy.Symbol))
                {
                    var historicalBars = _historicalDataStore.GetBars(strategy.Symbol);
                    runner.WarmUpFromHistoricalData(historicalBars);
                }

                _runners.Add(runner);

                // Register ticker handler
                int existingTickerIndex = _strategies
                    .Where(s => s.Symbol != strategy.Symbol)
                    .Select(s => s.Symbol)
                    .Distinct()
                    .ToList()
                    .Count;

                // Find the ticker ID for this symbol
                var symbolIndex = _contracts.Keys.ToList().IndexOf(strategy.Symbol);
                if (symbolIndex >= 0 && symbolIndex < _tickerIds.Count)
                {
                    _wrapper.RegisterTickerHandler(_tickerIds[symbolIndex], runner.OnLastTrade);
                }

                Log($"Strategy '{strategy.Name}' activated");
                _sessionLogger?.LogEvent("STRATEGY", $"Strategy activated: {strategy.Name}");

                return Task.FromResult(new OperationResultPayload { Success = true, Message = $"Strategy '{strategy.Name}' activated" });
            }
            catch (Exception ex)
            {
                Log($"Error activating strategy: {ex.Message}");
                return Task.FromResult(new OperationResultPayload { Success = false, ErrorMessage = ex.Message });
            }
        }

        private static Task<OperationResultPayload> DeactivateStrategyAsync(Guid strategyId)
        {
            try
            {
                // Find the runner for this strategy
                var runner = _runners.FirstOrDefault(r => r.Strategy.Id == strategyId);
                if (runner == null)
                {
                    // Check if strategy exists at all
                    var strategy = _strategies.FirstOrDefault(s => s.Id == strategyId);
                    if (strategy == null)
                    {
                        return Task.FromResult(new OperationResultPayload { Success = false, ErrorMessage = $"Strategy {strategyId} not found" });
                    }
                    return Task.FromResult(new OperationResultPayload { Success = true, Message = $"Strategy '{strategy.Name}' is not currently active" });
                }

                var strategyName = runner.Strategy.Name;
                var symbol = runner.Strategy.Symbol;

                // Unregister ticker handler
                if (_wrapper != null)
                {
                    var symbolIndex = _contracts.Keys.ToList().IndexOf(symbol);
                    if (symbolIndex >= 0 && symbolIndex < _tickerIds.Count)
                    {
                        _wrapper.UnregisterTickerHandler(_tickerIds[symbolIndex]);
                    }
                }

                // Dispose and remove the runner
                runner.Dispose();
                _runners.Remove(runner);

                // Check if any other strategies use this symbol
                var otherStrategiesUsingSym = _runners.Any(r => r.Strategy.Symbol == symbol);
                if (!otherStrategiesUsingSym && _wrapper != null && _client != null)
                {
                    // Optionally cancel market data for this symbol
                    var symbolIndex = _contracts.Keys.ToList().IndexOf(symbol);
                    if (symbolIndex >= 0 && symbolIndex < _tickerIds.Count)
                    {
                        _client.cancelMktData(_tickerIds[symbolIndex]);
                        _tickerIds.RemoveAt(symbolIndex);
                    }
                    _contracts.Remove(symbol);
                    _prices.Remove(symbol);
                }

                Log($"Strategy '{strategyName}' deactivated");
                _sessionLogger?.LogEvent("STRATEGY", $"Strategy deactivated: {strategyName}");

                return Task.FromResult(new OperationResultPayload { Success = true, Message = $"Strategy '{strategyName}' deactivated" });
            }
            catch (Exception ex)
            {
                Log($"Error deactivating strategy: {ex.Message}");
                return Task.FromResult(new OperationResultPayload { Success = false, ErrorMessage = ex.Message });
            }
        }

        private static Task<OperationResultPayload> CancelAllOrdersAsync()
        {
            if (_wrapper == null || _client == null)
                return Task.FromResult(new OperationResultPayload { Success = false, ErrorMessage = "Not connected" });

            try
            {
                _wrapper.RequestOpenOrdersAndWait(TimeSpan.FromSeconds(3));
                int orderCount = _wrapper.OpenOrders.Count;

                if (orderCount == 0)
                {
                    Log("No open orders to cancel");
                    return Task.FromResult(new OperationResultPayload { Success = true, Message = "No open orders to cancel" });
                }

                _wrapper.CancelAllOrders();
                Log($"Cancel requested for {orderCount} orders");
                return Task.FromResult(new OperationResultPayload { Success = true, Message = $"Cancel requested for {orderCount} orders" });
            }
            catch (Exception ex)
            {
                return Task.FromResult(new OperationResultPayload { Success = false, ErrorMessage = ex.Message });
            }
        }

        private static Task<OperationResultPayload> ActivateTradingAsync()
        {
            try
            {
                ActivateTrading();
                return Task.FromResult(new OperationResultPayload { Success = true, Message = "Trading activated" });
            }
            catch (Exception ex)
            {
                return Task.FromResult(new OperationResultPayload { Success = false, ErrorMessage = ex.Message });
            }
        }

        private static Task<OperationResultPayload> DeactivateTradingAsync()
        {
            try
            {
                DeactivateTrading();
                return Task.FromResult(new OperationResultPayload { Success = true, Message = "Trading deactivated" });
            }
            catch (Exception ex)
            {
                return Task.FromResult(new OperationResultPayload { Success = false, ErrorMessage = ex.Message });
            }
        }

        private static Task<List<OrderInfo>> GetIdiotProofOrdersAsync()
        {
            if (_wrapper == null)
                return Task.FromResult(new List<OrderInfo>());

            _wrapper.RequestOpenOrdersAndWait(TimeSpan.FromSeconds(3));

            // Get the trade tracking service to filter IdiotProof orders
            var tradeTracker = _tradeTrackingService;
            var idiotProofOrderIds = tradeTracker?.GetAllIdiotProofOrderIds() ?? [];

            var orders = _wrapper.OpenOrders
                .Where(kvp => idiotProofOrderIds.Contains(kvp.Key))
                .Select(kvp =>
                {
                    var tradeId = tradeTracker?.GetTradeByOrderIdAsync(kvp.Key).Result?.TradeId;
                    return new OrderInfo
                    {
                        OrderId = kvp.Key,
                        Symbol = kvp.Value.Symbol,
                        Action = kvp.Value.Action,
                        Quantity = (int)kvp.Value.Qty,
                        OrderType = kvp.Value.Type,
                        LimitPrice = kvp.Value.LmtPrice,
                        StatusText = kvp.Value.Status,
                        IdiotProofTradeId = tradeId
                    };
                }).ToList();

            return Task.FromResult(orders);
        }

        private static Task<ValidationResponsePayload> ValidateStrategyAsync(StrategyDefinition strategy)
        {
            try
            {
                // Use extension methods from ValidationExtensions
                var result = strategy.ValidateForExecution();

                return Task.FromResult(new ValidationResponsePayload
                {
                    IsValid = result.IsValid,
                    Errors = result.Errors.Select(e => new ValidationErrorInfo
                    {
                        Code = e.Code,
                        Message = e.Message,
                        FieldName = e.FieldName
                    }).ToList(),
                    Warnings = result.Warnings.Select(w => new ValidationWarningInfo
                    {
                        Code = w.Code,
                        Message = w.Message,
                        FieldName = w.FieldName
                    }).ToList()
                });
            }
            catch (Exception ex)
            {
                return Task.FromResult(new ValidationResponsePayload
                {
                    IsValid = false,
                    Errors = [new ValidationErrorInfo { Code = "VALIDATION_ERROR", Message = ex.Message }]
                });
            }
        }

        private static Task<List<IdiotProofTrade>> GetTradesAsync()
        {
            if (_tradeTrackingService == null)
                return Task.FromResult(new List<IdiotProofTrade>());

            return _tradeTrackingService.GetAllTradesAsync();
        }

        // ----- Helpers -----

        private static void Log(string message)
        {
            var formatted = $"{TimeStamp.NowBracketed} {message}";
            Console.WriteLine(formatted);

            // Also broadcast to frontend
            _ipcServer?.BroadcastConsoleOutput(formatted);
        }
    }
}


