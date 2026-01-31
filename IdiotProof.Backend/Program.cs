// ================================================================
// IdiotProof Backend Service
// Handles IBKR API communication, controlled by frontend via IPC
// ================================================================

using IBApi;
using IdiotProof.Backend.Helpers;
using IdiotProof.Backend.Ipc;
using IdiotProof.Backend.Logging;
using IdiotProof.Backend.Models;
using IdiotProof.Shared.Models;
using IdiotProof.Shared.Services;
using IdiotProof.Shared.Validation;

namespace IdiotProof.Backend
{
    internal sealed class Program
    {
        // Core components
        private static IbWrapper? _wrapper;
        private static EClientSocket? _client;
        private static IpcServer? _ipcServer;
        private static Timer? _heartbeatTimer;
        private static readonly CancellationTokenSource _shutdownCts = new();
        private static TradeTrackingService? _tradeTrackingService;

        // State
        private static readonly List<StrategyRunner> _runners = [];
        private static readonly List<int> _tickerIds = [];
        private static readonly Dictionary<string, Contract> _contracts = [];
        private static readonly Dictionary<string, double> _prices = [];
        private static List<TradingStrategy> _strategies = [];
        private static bool _isConnected;
        private static bool _isActive;

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
                else if (arg.Equals("--autostart", StringComparison.OrdinalIgnoreCase) ||
                         arg.Equals("-a", StringComparison.OrdinalIgnoreCase))
                {
                    Settings.AutoStart = true;
                }
            }
        }

        private static void Run()
        {
            Log("IdiotProof Backend starting...");
            Log($"Mode: {(Settings.IsPaperTrading ? "PAPER" : "LIVE")} | Port: {Settings.Port}");

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

            // Start heartbeat
            StartHeartbeat();

            // If auto-start is enabled and we have strategies, activate trading
            if (Settings.AutoStart && _strategies.Count > 0 && _isConnected)
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
            };

            return true;
        }

        private static void LoadStrategies()
        {
            Log("Loading strategies from disk...");

            var today = DateOnly.FromDateTime(DateTime.Now);
            _strategies = StrategyLoader.LoadFromJson(today);

            var enabledCount = _strategies.Count(s => s.Enabled);
            Log($"Loaded {_strategies.Count} strategies ({enabledCount} enabled)");

            // Validate using backend's StrategyValidator
            if (_strategies.Count > 0)
            {
                var result = IdiotProof.Backend.Models.StrategyValidator.ValidateAll(_strategies);
                if (!result.IsValid)
                {
                    foreach (var error in result.Errors)
                        Log($"ERROR: {error}");
                }
                foreach (var warning in result.Warnings)
                    Log($"WARN: {warning}");
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

            _isActive = false;
            Log("Trading deactivated.");
        }

        private static void StartHeartbeat()
        {
            _heartbeatTimer = new Timer(HeartbeatCallback, null, Settings.Heartbeat, Settings.Heartbeat);

            if (!Settings.SilentMode)
                Log($"Heartbeat enabled: every {Settings.Heartbeat.TotalMinutes} minutes");
        }

        private static void HeartbeatCallback(object? state)
        {
            if (_wrapper == null || !_isConnected)
            {
                if (!Settings.SilentMode)
                    Log("*Blip* (disconnected)");
                return;
            }

            try
            {
                var pingResult = _wrapper.Ping(TimeSpan.FromSeconds(10));

                if (Settings.SilentMode)
                {
                    // Minimal output in silent mode
                    Console.WriteLine("*Blip*");
                }
                else
                {
                    var timestamp = GetEasternTimeStamp();
                    if (pingResult.Success)
                    {
                        Log($"[{timestamp}] HEARTBEAT OK | Latency: {pingResult.LatencyMs}ms | Active: {_runners.Count} strategies");
                    }
                    else
                    {
                        Log($"[{timestamp}] HEARTBEAT FAILED - Connection may be lost!");
                    }
                }
            }
            catch (Exception ex)
            {
                if (!Settings.SilentMode)
                    Log($"HEARTBEAT ERROR: {ex.Message}");
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
            DeactivateTrading();

            _client?.eDisconnect();
            _wrapper?.Dispose();
            _ipcServer?.Dispose();

            Log("Goodbye!");
        }

        // ----- IPC Handlers -----

        private static Task<StatusResponsePayload> GetStatusAsync()
        {
            return Task.FromResult(new StatusResponsePayload
            {
                IsRunning = true,
                IsConnectedToIbkr = _isConnected,
                IsTradingActive = _isActive,
                ActiveStrategies = _runners.Count,
                LastHeartbeat = DateTime.Now,
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
            // TODO: Implement position tracking
            return Task.FromResult(new List<PositionInfo>());
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
            // TODO: Implement position closing
            Log($"Close position requested for {symbol}");
            return Task.FromResult(new OperationResultPayload { Success = true, Message = "Close requested" });
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

        private static Task<OperationResultPayload> ActivateStrategyAsync(Guid strategyId)
        {
            // TODO: Strategy activation requires reloading from disk with Enabled=true
            // For now, just acknowledge the request
            Log($"Activate strategy requested: {strategyId}");
            return Task.FromResult(new OperationResultPayload { Success = true, Message = "Strategy activation requested" });
        }

        private static Task<OperationResultPayload> DeactivateStrategyAsync(Guid strategyId)
        {
            // TODO: Strategy deactivation requires stopping the runner
            // For now, just acknowledge the request
            Log($"Deactivate strategy requested: {strategyId}");
            return Task.FromResult(new OperationResultPayload { Success = true, Message = "Strategy deactivation requested" });
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
            var timestamp = GetEasternTimeStamp();
            var formatted = $"[{timestamp}] {message}";
            Console.WriteLine(formatted);

            // Also broadcast to frontend
            _ipcServer?.BroadcastConsoleOutput(formatted);
        }

        private static string GetEasternTimeStamp()
        {
            var easternZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
            var easternTime = TimeZoneInfo.ConvertTime(DateTime.Now, easternZone);
            return easternTime.ToString("HH:mm:ss");
        }
    }
}
