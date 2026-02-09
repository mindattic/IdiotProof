// ================================================================
// IdiotProof Core - IBKR Trading Engine
// Consolidated trading engine (formerly Backend + Console)
// ================================================================

using IBApi;
using IdiotProof.Core.Models;
using IdiotProof.Helpers;
using IdiotProof.Logging;
using IdiotProof.Models;
using IdiotProof.Services;
using IdiotProof.Strategy;
using IdiotProof.Settings;
using IdiotProof.Validation;

namespace IdiotProof {
    internal sealed class Program
    {
        // Core components
        private static IbWrapper? _wrapper;
        private static EClientSocket? _client;
        // IPC removed - now running as consolidated console app
        private static readonly CancellationTokenSource _shutdownCts = new();
        private static TradeTrackingService? _tradeTrackingService;
        private static SessionLogger? _sessionLogger;

        // Historical data components
        private static HistoricalDataStore? _historicalDataStore;
        private static HistoricalDataService? _historicalDataService;
        private static BacktestService? _backtestService;
        private static TickerMetadataService? _metadataService;

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
                    AppSettings.SilentMode = true;
                }
            }
        }

        private static void Run()
        {
            Log("IdiotProof Core starting...");
            Log($"Mode: {(AppSettings.IsPaperTrading ? "PAPER" : "LIVE")} | Port: {AppSettings.Port}");

            // Initialize session logger (logs every 20 min, on crash, and on close)
            _sessionLogger = new SessionLogger();
            _sessionLogger.LogEvent("STARTUP", $"Mode: {(AppSettings.IsPaperTrading ? "PAPER" : "LIVE")} | Port: {AppSettings.Port}");

            // Share session logger with all backend classes
            StrategyRunner.SessionLogger = _sessionLogger;
            IbWrapper.SessionLogger = _sessionLogger;
            StrategyManager.SessionLogger = _sessionLogger;
            // StrategyLoader removed - no longer needed
            IdiotProof.Helpers.StrategyValidator.SessionLogger = _sessionLogger;

            // Initialize trade tracking service
            _tradeTrackingService = new TradeTrackingService();
            Log("Trade tracking service initialized");

            // Connect to IBKR
            if (!ConnectToIbkr())
            {
                Log("ERROR: Failed to connect to IBKR. Backend will wait for retry...");
            }

            // Load strategies from disk
            LoadStrategies();

            // Handle Ctrl+C
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                _shutdownCts.Cancel();
            };

            // Start paused - show interactive menu
            RunInteractiveMenu();

            // Cleanup
            Shutdown();
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

            // Subscribe to account updates for margin monitoring
            _wrapper.RequestAccountUpdates(AppSettings.AccountNumber ?? "");

            // Initialize historical data components
            _historicalDataStore = new HistoricalDataStore();
            _historicalDataService = new HistoricalDataService(_client, _wrapper, _historicalDataStore);
            _backtestService = new BacktestService(_historicalDataService);
            _metadataService = new TickerMetadataService();
            Log("Historical data service initialized");
            Log($"Metadata directory: {_metadataService.MetadataDirectory}");

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

            // Ensure all required folders exist
            SettingsManager.EnsureFoldersExist();
            
            // Create sample strategy rules file if it doesn't exist
            StrategyRulesManager.CreateSampleIfNotExists();
            
            // Load and display any custom strategy rules
            var rules = StrategyRulesManager.Load();
            if (rules.Enabled && rules.EnabledRules.Any())
            {
                Log($"[StrategyRules] {rules.EnabledRules.Count()} custom rules loaded (ChatGPT will evaluate entries against these)");
            }

            // TODO: StrategyLoader removed - strategies now come from autonomous trading
            // Use WatchlistManager for autonomous trading, or leave empty for learning mode
            _strategies = new List<TradingStrategy>();

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
                var result = IdiotProof.Helpers.StrategyValidator.ValidateAll(_strategies);
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

            // Load tickers from watchlist
            var tickers = LoadTickers();
            if (tickers.Count == 0)
            {
                Log("No tickers to trade. Press 1 to add tickers first.");
                return;
            }
            
            // Check which tickers have learned profiles
            var profileFolder = SettingsManager.GetDataFolder();
            var tickersWithProfiles = tickers
                .Where(t => File.Exists(Path.Combine(profileFolder, $"{t}.profile.json")))
                .ToList();
            
            if (tickersWithProfiles.Count == 0)
            {
                Log("No learned profiles found. Press 2 to learn tickers first.");
                return;
            }
            
            Log($"Starting trading with {tickersWithProfiles.Count} ticker(s)...");
            
            // Create autonomous strategies for each ticker with a profile
            _strategies.Clear();
            foreach (var symbol in tickersWithProfiles)
            {
                // Create TradingStrategy directly (Stock fluent API removed)
                var strategy = new TradingStrategy
                {
                    Symbol = symbol,
                    Name = $"{symbol} Auto",
                    Conditions = Array.Empty<IStrategyCondition>(),
                    Order = new OrderAction
                    {
                        Side = IdiotProof.Enums.OrderSide.Buy,
                        Quantity = 100
                    },
                    Session = IdiotProof.Enums.TradingSession.Extended,
                    Enabled = true
                };
                
                _strategies.Add(strategy);
                Log($"  [{symbol}] Loaded with learned profile");
            }

            var enabledStrategies = _strategies.FindAll(s => s.Enabled);

            // Fetch historical data for indicator warm-up (200 bars)
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
                            barCount: 200,
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

            // Build metadata for each symbol from historical data
            // This gives strategies insights about how the stock typically behaves
            if (_metadataService != null && _historicalDataStore != null)
            {
                Log("Building ticker metadata from historical data...");
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
                                Log($"  [{symbol}] Metadata build failed: {ex.Message}");
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

                // Attach metadata for informed trading decisions
                if (_metadataService != null)
                {
                    runner.TickerMetadata = _metadataService.Load(strategy.Symbol);
                }

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
                    var symbol = strategy.Symbol;
                    _wrapper.RegisterTickerHandler(tickerId, (price, size) =>
                    {
                        _prices[symbol] = price; // Update price for periodic price check
                        runner.OnLastTrade(price, size); // Forward to strategy runner
                    });

                    // Register bid/ask handler for PriceType support
                    _wrapper.RegisterBidAskHandler(tickerId, (bid, ask) =>
                    {
                        runner.OnBidAskUpdate(bid, ask);
                    });
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
            if (AppSettings.TickerPriceCheckIntervalSeconds <= 0)
                return;

            // Initialize previous prices with current prices
            foreach (var kvp in _prices)
            {
                _previousPrices[kvp.Key] = kvp.Value;
            }

            var intervalMs = AppSettings.TickerPriceCheckIntervalSeconds * 1000;
            _priceCheckTimer = new Timer(OnPriceCheckTimer, null, intervalMs, intervalMs);
            Log($"Price check enabled: every {AppSettings.TickerPriceCheckIntervalSeconds} second(s)");
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

        private static void RunInteractiveMenu()
        {
            Log("");
            Log("========================================");
            Log("  IdiotProof - Make Money Trading");
            Log("========================================");

            while (!_shutdownCts.IsCancellationRequested)
            {
                PrintMenu();
                Console.Write("\nSelect option: ");
                var input = Console.ReadLine()?.Trim();

                if (_shutdownCts.IsCancellationRequested || input == null)
                    break;

                if (string.IsNullOrEmpty(input))
                    continue;

                try
                {
                    switch (input)
                    {
                        case "1":
                            RunTickersMenu();
                            break;

                        case "2":
                            RunAiLearn();
                            break;

                        case "3":
                            RunBacktestPrompt();
                            break;

                        case "4":
                            if (!_isActive)
                            {
                                if (ConfirmActivateTrading())
                                {
                                    ActivateTrading();
                                    if (_isActive)
                                    {
                                        Log("Trading activated. Monitoring markets...");
                                        Log("Press [M] for menu, [S] to stop, [P] for prices, [X] to close all");
                                        RunActiveMonitoringLoop();
                                    }
                                }
                            }
                            else
                            {
                                Log("Trading already active. Press [S] to stop first.");
                            }
                            break;

                        case "s":
                        case "S":
                            if (_isActive)
                            {
                                DeactivateTrading();
                                Log("Trading stopped.");
                            }
                            else
                            {
                                Log("Trading is not active.");
                            }
                            break;

                        case "p":
                        case "P":
                            if (_isActive)
                            {
                                PrintCurrentPrices();
                            }
                            else
                            {
                                Log("No trading session active.");
                            }
                            break;

                        case "m":
                        case "M":
                            if (_isActive)
                            {
                                Log("Returning to monitoring... (trading still active)");
                                Log("Press [M] for menu, [S] to stop, [P] for prices, [X] to close all");
                                RunActiveMonitoringLoop();
                            }
                            else
                            {
                                Log("No trading session to monitor.");
                            }
                            break;

                        case "x":
                        case "X":
                            if (_isActive)
                            {
                                if (ConfirmCloseAllPositions())
                                {
                                    ExecuteEmergencyClose();
                                }
                            }
                            else
                            {
                                Log("No trading session active.");
                            }
                            break;

                        case "0":
                        case "q":
                            _shutdownCts.Cancel();
                            break;

                        default:
                            Log($"Invalid option: {input}");
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Log($"Error: {ex.Message}");
                }
            }
        }

        private static void PrintMenu()
        {
            var tickers = LoadTickers();
            var tickerCount = tickers.Count;
            var profileCount = CountProfiles(tickers);
            var weightsCount = CountWeights(tickers);
            
            Log("");
            Log("========================================");
            Log($"  Tickers: {tickerCount} | Profiles: {profileCount} | Weights: {weightsCount} | {(_isActive ? "TRADING" : "Idle")}");
            Log("========================================");
            
            if (_isActive)
            {
                // Show trading-specific menu
                Log("  S. Stop      - Stop live trading");
                Log("  P. Prices    - Show current prices");
                Log("  M. Monitor   - Return to monitoring");
                Log("  X. Close All - Close all positions");
                Log("  0. Exit");
            }
            else
            {
                Log("  1. Tickers   - Manage ticker watchlist");
                Log("  2. Learn     - AI learning (150+ weights)");
                Log("  3. Backtest  - Test learned weights");
                Log("  4. Live      - Start live trading");
                Log("  0. Exit");
            }
            Log("========================================");
        }
        
        private static int CountWeights(List<string> tickers)
        {
            var folder = SettingsManager.GetDataFolder();
            return tickers.Count(t => File.Exists(Path.Combine(folder, $"{t}.weights.json")));
        }
        
        private static int CountLearnableWeights()
        {
            // LearnedWeights has: 16 indicator base + 16 trending mults + 16 ranging mults 
            // + 16 time weights + 256 interaction matrix + 8 entry biases + 8 exit sensitivity + 16 patterns
            // = 352 total weights
            return 16 + 16 + 16 + 16 + 256 + 8 + 8 + 16;
        }

        // ====================================================================
        // TICKERS MANAGEMENT
        // ====================================================================
        
        private static string GetTickersPath() => 
            Path.Combine(SettingsManager.GetDataFolder(), "tickers.json");
        
        /// <summary>
        /// Ticker configuration with capital allocation
        /// </summary>
        private class TickerConfig
        {
            public string Symbol { get; set; } = "";
            public decimal Capital { get; set; } = 1000m; // Default $1000 per ticker
        }
        
        private static Dictionary<string, decimal> LoadTickerConfigs()
        {
            var path = GetTickersPath();
            if (!File.Exists(path))
                return new Dictionary<string, decimal>();
            
            try
            {
                var json = File.ReadAllText(path);
                
                // Try new format first (dictionary with capital)
                try
                {
                    var configs = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, decimal>>(json);
                    if (configs != null)
                        return configs;
                }
                catch { }
                
                // Fall back to old format (list of symbols)
                var oldFormat = System.Text.Json.JsonSerializer.Deserialize<List<string>>(json);
                if (oldFormat != null)
                {
                    // Migrate to new format with default capital
                    var migrated = oldFormat.ToDictionary(s => s, _ => 1000m);
                    SaveTickerConfigs(migrated);
                    return migrated;
                }
                
                return new Dictionary<string, decimal>();
            }
            catch
            {
                return new Dictionary<string, decimal>();
            }
        }
        
        private static void SaveTickerConfigs(Dictionary<string, decimal> configs)
        {
            var path = GetTickersPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var json = System.Text.Json.JsonSerializer.Serialize(configs, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        
        private static List<string> LoadTickers()
        {
            return LoadTickerConfigs().Keys.ToList();
        }
        
        private static void SaveTickers(List<string> tickers)
        {
            var existing = LoadTickerConfigs();
            var newConfigs = new Dictionary<string, decimal>();
            foreach (var t in tickers)
            {
                newConfigs[t] = existing.TryGetValue(t, out var cap) ? cap : 1000m;
            }
            SaveTickerConfigs(newConfigs);
        }
        
        private static double GetTickerPrice(string symbol)
        {
            if (_wrapper == null || _client == null || !_isConnected)
                return 0;
            
            // Check if we already have a price from active trading
            if (_prices.TryGetValue(symbol, out var cachedPrice) && cachedPrice > 0)
                return cachedPrice;
            
            // Request snapshot price
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
                
                _client.reqMktData(tickerId, contract, "", true, false, null); // snapshot = true
                
                priceReceived.Wait(TimeSpan.FromSeconds(3));
                
                _wrapper.UnregisterTickerHandler(tickerId);
                
                return price;
            }
            catch
            {
                return 0;
            }
        }
        
        private static int CountProfiles(List<string> tickers)
        {
            var profileFolder = SettingsManager.GetDataFolder();
            return tickers.Count(t => File.Exists(Path.Combine(profileFolder, $"{t}.profile.json")));
        }
        
        private static void RunTickersMenu()
        {
            Log("");
            Log("=== Ticker Watchlist ===");
            Log("Commands: [A]dd, [D]elete, [C]lear, [S]et Allocation, [R]efresh Prices, [ESC] return");
            Log("");
            
            while (true)
            {
                var configs = LoadTickerConfigs();
                
                if (configs.Count == 0)
                {
                    Log("  (no tickers - press A to add)");
                }
                else
                {
                    var profileFolder = SettingsManager.GetDataFolder();
                    Log("  #   Symbol   Allocation   Price      Shares   Status");
                    Log("  --- -------- ------------ ---------- -------- --------");
                    
                    int i = 1;
                    foreach (var kvp in configs.OrderBy(k => k.Key))
                    {
                        var symbol = kvp.Key;
                        var capital = kvp.Value;
                        var hasProfile = File.Exists(Path.Combine(profileFolder, $"{symbol}.profile.json"));
                        var hasWeights = File.Exists(Path.Combine(profileFolder, $"{symbol}.weights.json"));
                        
                        var price = GetTickerPrice(symbol);
                        var priceStr = price > 0 ? $"${price:F2}" : "--";
                        var sharesStr = price > 0 ? ((int)(capital / (decimal)price)).ToString() : "--";
                        
                        var status = hasWeights ? "LEARNED" : (hasProfile ? "PROFILE" : "NEW");
                        
                        Log($"  {i,-3} {symbol,-8} ${capital,-10:N0} {priceStr,-10} {sharesStr,-8} [{status}]");
                        i++;
                    }
                }
                Log("");
                
                Console.Write("Command (A/D/S/C/R/ESC): ");
                var key = Console.ReadKey(intercept: true);
                Console.WriteLine();
                
                if (key.Key == ConsoleKey.Escape)
                {
                    Log("Returning to menu...");
                    break;
                }
                
                switch (char.ToUpper(key.KeyChar))
                {
                    case 'A':
                        Console.Write("  Add ticker(s) (e.g., NVDA or NVDA,AAPL,TSLA): ");
                        var addInput = Console.ReadLine()?.Trim().ToUpperInvariant();
                        if (!string.IsNullOrEmpty(addInput))
                        {
                            var newTickers = addInput
                                .Split(new[] { ',', ';', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                                .Select(s => s.Trim().ToUpperInvariant())
                                .Where(s => !string.IsNullOrWhiteSpace(s) && !configs.ContainsKey(s))
                                .ToList();
                            
                            if (newTickers.Count > 0)
                            {
                                foreach (var t in newTickers)
                                    configs[t] = 1000m; // Default capital
                                SaveTickerConfigs(configs);
                                Log($"  Added: {string.Join(", ", newTickers)} (default $1,000 each)");
                            }
                        }
                        break;
                        
                    case 'D':
                        Console.Write("  Delete ticker (number or symbol): ");
                        var delInput = Console.ReadLine()?.Trim().ToUpperInvariant();
                        if (!string.IsNullOrEmpty(delInput))
                        {
                            var tickers = configs.Keys.OrderBy(k => k).ToList();
                            string? toRemove = null;
                            if (int.TryParse(delInput, out var idx) && idx >= 1 && idx <= tickers.Count)
                            {
                                toRemove = tickers[idx - 1];
                            }
                            else if (configs.ContainsKey(delInput))
                            {
                                toRemove = delInput;
                            }
                            
                            if (toRemove != null)
                            {
                                configs.Remove(toRemove);
                                SaveTickerConfigs(configs);
                                Log($"  Removed: {toRemove}");
                            }
                            else
                            {
                                Log($"  Not found: {delInput}");
                            }
                        }
                        break;
                    
                    case 'S':
                        Console.Write("  Set allocation for ticker (number or symbol): ");
                        var capTickerInput = Console.ReadLine()?.Trim().ToUpperInvariant();
                        if (!string.IsNullOrEmpty(capTickerInput))
                        {
                            var tickers = configs.Keys.OrderBy(k => k).ToList();
                            string? targetTicker = null;
                            if (int.TryParse(capTickerInput, out var idx) && idx >= 1 && idx <= tickers.Count)
                            {
                                targetTicker = tickers[idx - 1];
                            }
                            else if (configs.ContainsKey(capTickerInput))
                            {
                                targetTicker = capTickerInput;
                            }
                            
                            if (targetTicker != null)
                            {
                                var currentCap = configs[targetTicker];
                                Console.Write($"  Allocation for {targetTicker} (current ${currentCap:N0}): $");
                                var capInput = Console.ReadLine()?.Trim().Replace("$", "").Replace(",", "");
                                if (decimal.TryParse(capInput, out var newCap) && newCap > 0)
                                {
                                    configs[targetTicker] = newCap;
                                    SaveTickerConfigs(configs);
                                    Log($"  {targetTicker} allocation set to ${newCap:N0}");
                                }
                                else
                                {
                                    Log($"  Invalid amount.");
                                }
                            }
                            else
                            {
                                Log($"  Not found: {capTickerInput}");
                            }
                        }
                        break;
                        
                    case 'C':
                        Console.Write("  Clear all tickers? (Y/N): ");
                        var confirm = Console.ReadKey(intercept: true);
                        Console.WriteLine();
                        if (char.ToUpper(confirm.KeyChar) == 'Y')
                        {
                            configs.Clear();
                            SaveTickerConfigs(configs);
                            Log("  All tickers cleared.");
                        }
                        break;
                    
                    case 'R':
                        Log("  Refreshing prices...");
                        // Just re-display the list (prices will be fetched)
                        break;
                        
                    default:
                        Log("  Unknown command. Use A/D/S/C/R or ESC.");
                        break;
                }
                
                Log("");
            }
        }

        private static void RunAiLearn()
        {
            if (!_isConnected || _historicalDataService == null)
            {
                Log("ERROR: Not connected to IBKR. AI Learning requires connection to fetch data.");
                return;
            }
            
            var tickers = LoadTickers();
            if (tickers.Count == 0)
            {
                Log("No tickers to learn. Press 1 to add tickers first.");
                return;
            }
            
            Log("");
            Log("========================================");
            Log($"  AI LEARNING: {tickers.Count} TICKER(S)");
            Log($"  {CountLearnableWeights()} learnable weights");
            Log("  3 methods: Genetic, Neural, Gradient");
            Log("  Train/Validation split: 70%/30%");
            Log("========================================");
            Log("");
            
            int completed = 0;
            int failed = 0;
            
            foreach (var symbol in tickers)
            {
                Log($"[{completed + failed + 1}/{tickers.Count}] AI Learning {symbol}...");
                
                try
                {
                    var learner = new IdiotProof.Learning.MultiMethodLearner(_historicalDataService);
                    var progress = new Progress<string>(msg => Log($"  {msg}"));
                    
                    var result = learner.LearnAndCompareAsync(
                        symbol,
                        generationsPerMethod: 50,
                        progress,
                        _shutdownCts.Token).GetAwaiter().GetResult();
                    
                    // MultiMethodLearner already prints detailed comparison via progress
                    Log($"  --> Best method: {result.BestMethod}");
                    Log($"  --> Saved to: {symbol}.weights.json");
                    
                    completed++;
                }
                catch (OperationCanceledException)
                {
                    Log($"  [--] Cancelled.");
                    break;
                }
                catch (Exception ex)
                {
                    Log($"  [ERR] {symbol}: {ex.Message}");
                    failed++;
                }
                
                Log("");
            }
            
            Log("========================================");
            Log($"  AI LEARNING COMPLETE: {completed} OK, {failed} failed");
            Log($"  Weight files saved to: {SettingsManager.GetDataFolder()}");
            Log("========================================");
        }

        private static void PrintStatus()
        {
            Log("");
            Log($"IBKR Connection: {(_isConnected ? "[OK] Connected" : "[--] Disconnected")}");
            Log($"Trading Status:  {(_isActive ? "[OK] Active" : "[--] Paused")}");
            Log($"Mode:            {(AppSettings.IsPaperTrading ? "PAPER" : "LIVE")}");
            Log($"Port:            {AppSettings.Port}");
            Log($"Strategies:      {_strategies.Count} loaded ({_strategies.Count(s => s.Enabled)} enabled)");
            if (_isActive)
            {
                Log($"Active Runners:  {_runners.Count}");
            }
            Log("");
        }

        private static void PrintSettings()
        {
            Log("");
            Log("Current Settings:");
            Log($"  Trading Mode:     {(AppSettings.IsPaperTrading ? "PAPER" : "LIVE")}");
            Log($"  IBKR Host:        {AppSettings.Host}");
            Log($"  IBKR Port:        {AppSettings.Port}");
            Log($"  Client ID:        {AppSettings.ClientId}");
            Log($"  Account:          {AppSettings.AccountNumber ?? "(auto)"}");
            Log($"  Strategies Dir:   {SettingsManager.GetStrategiesFolder()}");
            Log($"  Price Check:      {AppSettings.TickerPriceCheckIntervalSeconds}s");
            Log("");
        }

        private static void PrintStrategies()
        {
            Log("");
            if (_strategies.Count == 0)
            {
                Log("No strategies loaded.");
                return;
            }

            Log($"Loaded Strategies ({_strategies.Count}):");
            foreach (var s in _strategies)
            {
                var status = s.Enabled ? "[*]" : "[o]";
                Log($"  {status} {s.Symbol,-8} {s.Name,-30}");
            }
            Log("");
            Log("Legend: [*] = enabled, [o] = disabled");
            Log("");
        }

        private static void PrintProfiles()
        {
            Log("");
            Log("=== Learned Ticker Profiles ===");
            Log($"Profile Directory: {SettingsManager.GetProfilesFolder()}");
            Log("");

            var profiles = StrategyRunner.ProfileManager.GetAllProfiles()
                .OrderByDescending(p => p.TotalTrades)
                .ToList();

            if (profiles.Count == 0)
            {
                Log("No profiles found. Run backtests to start learning!");
                Log("");
                return;
            }

            Log($"{"Symbol",-8} {"Trades",8} {"Win%",8} {"Long%",8} {"Short%",8} {"PF",8} {"Net P&L",12} {"Conf",6} {"OptLong",8} {"OptShort",9}");
            Log(new string('-', 95));

            foreach (var p in profiles)
            {
                Console.ForegroundColor = p.WinRate >= 60 ? ConsoleColor.Green :
                                          p.WinRate >= 50 ? ConsoleColor.Yellow :
                                          ConsoleColor.Red;
                Log($"{p.Symbol,-8} {p.TotalTrades,8} {p.WinRate,7:F1}% {p.LongWinRate,7:F1}% {p.ShortWinRate,7:F1}% {p.ProfitFactor,8:F2} {p.TotalPnL,11:C0} {p.Confidence,5}% {p.OptimalLongEntryThreshold,8} {p.OptimalShortEntryThreshold,9}");
                Console.ResetColor();
            }

            Log("");
            Log("Profile Details:");
            foreach (var p in profiles.Take(5))  // Show top 5 details
            {
                Log("");
                Log($"  {p.Symbol}:");
                Log($"    Stats:     {p.TotalTrades} trades, {p.TotalWins}W/{p.TotalLosses}L, PF={p.ProfitFactor:F2}");
                Log($"    Streak:    Current={p.CurrentStreak}, LongestWin={p.LongestWinStreak}, LongestLoss={p.LongestLossStreak}");
                Log($"    Avg Trade: Duration={p.AverageTradeDurationMinutes:F1}min, Win=${p.AverageWinAmount:F2}, Loss=${p.AverageLossAmount:F2}");
                Log($"    Optimal:   Entry>={p.OptimalLongEntryThreshold} (L), <={p.OptimalShortEntryThreshold} (S)");

                // Best time windows
                if (p.TimeWindowStats.Count > 0)
                {
                    var bestTime = p.TimeWindowStats
                        .Where(t => t.Value.TotalTrades >= 3)
                        .OrderByDescending(t => t.Value.WinRate)
                        .FirstOrDefault();

                    if (bestTime.Value != null)
                    {
                        Log($"    Best Time: {bestTime.Key} ({bestTime.Value.WinRate:F0}% win rate)");
                    }
                }

                // Best indicator
                if (p.IndicatorCorrelations.Count > 0)
                {
                    var bestInd = p.IndicatorCorrelations
                        .Where(c => c.Occurrences >= 3)
                        .OrderByDescending(c => c.WinRate)
                        .FirstOrDefault();

                    if (bestInd != null)
                    {
                        Log($"    Best Signal: {bestInd.IndicatorName} {bestInd.Condition} ({bestInd.WinRate:F0}%)");
                    }
                }
            }

            Log("");
            Log("Run backtest (option 4) to build/update profiles.");
            Log("");
        }

        private static void RunBacktestPrompt()
        {
            if (!_isConnected || _backtestService == null)
            {
                Log("ERROR: Not connected to IBKR. Backtest requires live connection for historical data.");
                return;
            }

            Log("=== Backtest Configuration ===");
            Log("");

            // Symbol
            Console.Write("  Symbol(s) (e.g., AAPL or AAPL,MSFT): ");
            var symbolInput = Console.ReadLine()?.Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(symbolInput))
            {
                Log("  Cancelled.");
                return;
            }
            var symbols = symbolInput
                .Split(new[] { ',', ';', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim().ToUpperInvariant())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct()
                .ToList();

            if (symbols.Count == 0)
            {
                Log("  Cancelled.");
                return;
            }

            // Days
            Console.Write("  Days to backtest [30]: ");
            var daysInput = Console.ReadLine()?.Trim();
            var days = string.IsNullOrEmpty(daysInput) ? 30 : int.TryParse(daysInput, out var d) ? d : 30;

            // Quantity
            Console.Write("  Total Shares [100]: ");
            var qtyInput = Console.ReadLine()?.Trim();
            var qty = string.IsNullOrEmpty(qtyInput) ? 100 : int.TryParse(qtyInput, out var q) ? q : 100;

            Log("");
            Log($"Starting backtest for {symbols.Count} ticker(s)...");
            Log($"  Days: {days}, Quantity: {qty}");
            Log("");

            var summaryResults = new List<BacktestResponsePayload>();

            foreach (var symbol in symbols)
            {
                Log($"--- {symbol} ---");

                var request = new RunBacktestRequest
                {
                    Symbol = symbol,
                    Days = days,
                    Mode = "adaptive", // Ignored - simulator adapts dynamically
                    Quantity = qty,
                    SaveProfile = true
                };

                var result = RunBacktestAsync(request).GetAwaiter().GetResult();
                summaryResults.Add(result);

                if (!result.Success)
                {
                    Log($"Backtest failed: {result.ErrorMessage}");
                    Log("");
                    continue;
                }

                Log("");
                Log("=== Backtest Results ===");
                Log($"Symbol:         {result.Symbol}");
                Log($"Position:       {result.PositionSize:N0} shares @ ~${result.AvgEntryPrice:F2}");
                Log($"Bars Processed: {result.BarsProcessed:N0}");
                Log("");
                Log("--- Account Summary ---");
                Log($"Starting Value: ${result.StartingCapital:N2}");
                Log($"Profit/Loss:    {(result.TotalPnL >= 0 ? "+" : "")}${result.TotalPnL:N2}");
                Log($"Ending Value:   ${result.EndingCapital:N2}");
                Log($"Return:         {(result.ReturnPercent >= 0 ? "+" : "")}{result.ReturnPercent:F2}%");
                Log("");
                Log("--- Trade Statistics ---");
                Log($"Total Trades:   {result.TotalTrades}");
                Log($"Winning Trades: {result.WinningTrades}");
                Log($"Win Rate:       {result.WinRate:F1}%");
                Log($"Avg P&L/Trade:  ${result.AvgPnL:F2}");
                Log($"Confidence:     {result.Confidence:F0}%");
                Log("========================");

                // Show profile learning summary
                if (result.ProfileSaved)
                {
                    Log("");
                    Log("=== Profile Learning ===");
                    var profile = StrategyRunner.ProfileManager.GetProfile(symbol);
                    if (profile != null)
                    {
                        Log($"Profile saved: {SettingsManager.GetProfilesFolder()}\\{symbol}.json");
                        if (profile.BacktestStartDate.HasValue && profile.BacktestEndDate.HasValue)
                        {
                            Log($"Backtest range: {profile.BacktestStartDate:yyyy-MM-dd} to {profile.BacktestEndDate:yyyy-MM-dd} ({profile.BacktestDays} days)");
                        }
                        Log("");
                        Log("Learned Adjustments:");
                        Log($"  Optimal Long Entry:  Score >= {profile.OptimalLongEntryThreshold} (default: 70)");
                        Log($"  Optimal Short Entry: Score <= {profile.OptimalShortEntryThreshold} (default: -70)");
                        Log($"  Optimal Long Exit:   Score <  {profile.OptimalLongExitThreshold} (default: 40)");
                        Log($"  Optimal Short Exit:  Score >  {profile.OptimalShortExitThreshold} (default: -40)");
                        Log("");
                        Log("Statistics:");
                        Log($"  Total Trades:  {profile.TotalTrades}");
                        Log($"  Long Win Rate: {profile.LongWinRate:F1}%");
                        Log($"  Short Win Rate: {profile.ShortWinRate:F1}%");
                        Log($"  Profit Factor: {profile.ProfitFactor:F2}");
                        Log($"  Avg Duration:  {profile.AverageTradeDurationMinutes:F1} min");
                    }
                    Log("========================");
                }
                
                // Show detailed trade log
                if (!string.IsNullOrEmpty(result.DetailedTradeLog))
                {
                    Log("");
                    Log("=== Detailed Trade Log ===");
                    foreach (var line in result.DetailedTradeLog.Split('\n'))
                    {
                        Log(line.TrimEnd());
                    }
                }

                Log("");
            }

            if (symbols.Count > 1)
            {
                Log("=== Batch Summary ===");
                foreach (var r in summaryResults)
                {
                    if (!r.Success)
                    {
                        Log($"{r.Symbol,-8} [ERR] {r.ErrorMessage}");
                    }
                    else
                    {
                        Log($"{r.Symbol,-8} Trades={r.TotalTrades,-4} Win={r.WinRate,5:F1}%  P&L=${r.TotalPnL,10:F2}");
                    }
                }
                Log("=====================");
            }
        }

        private static void RunAggregateBacktestPrompt()
        {
            if (!_isConnected || _historicalDataService == null)
            {
                Log("ERROR: Not connected to IBKR. Aggregate backtest requires live connection for historical data.");
                return;
            }

            Log("=== Aggregate Backtest (30-Day Analysis) ===");
            Log("This runs backtests across ALL available days and provides statistical insights.");
            Log("");

            // Symbol
            Console.Write("  Symbol (e.g., AAPL): ");
            var symbolInput = Console.ReadLine()?.Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(symbolInput))
            {
                Log("  Cancelled.");
                return;
            }

            // Starting capital
            Console.Write("  Starting capital [$1000]: ");
            var capitalInput = Console.ReadLine()?.Trim();
            decimal capital = string.IsNullOrEmpty(capitalInput) ? 1000m :
                              decimal.TryParse(capitalInput, out var c) ? c : 1000m;

            // Threshold
            Console.Write("  Entry threshold (30-70, lower=more trades) [50]: ");
            var thresholdInput = Console.ReadLine()?.Trim();
            int threshold = string.IsNullOrEmpty(thresholdInput) ? 50 :
                           int.TryParse(thresholdInput, out var t) ? Math.Clamp(t, 30, 70) : 50;

            Log("");
            Log($"Running aggregate backtest for {symbolInput}...");
            Log($"  Capital: ${capital:N2}, Threshold: {threshold}");
            Log("");
            Log("Fetching historical data and running day-by-day simulation...");
            Log("(This may take a minute for first run - data is cached for future tests)");
            Log("");

            try
            {
                // Create backtester with data cache
                var dataCache = new HistoricalDataCache();
                var backtester = new Backtester(_historicalDataService, null, dataCache);

                // Configure
                var config = new AutonomousBacktestConfig
                {
                    StartingCapital = capital,
                    BaseEntryThreshold = threshold,
                    AllowShort = true,
                    EnableSelfCalibration = false, // Consistent metrics across days
                    UseTickerMetadata = true
                };

                // Create progress reporter
                var progress = new Progress<string>(msg => Log($"  {msg}"));

                // Run multi-day backtest
                var result = backtester.RunMultiDayAsync(
                    symbolInput,
                    capital,
                    config,
                    progress,
                    _shutdownCts.Token).GetAwaiter().GetResult();

                // Display results
                Log("");
                Console.WriteLine(result.ToString());

                // Display insights
                Log("");
                Console.WriteLine(result.GetInsights());
            }
            catch (OperationCanceledException)
            {
                Log("Backtest cancelled.");
            }
            catch (Exception ex)
            {
                Log($"Backtest failed: {ex.Message}");
            }
        }

        private static void RunStrategyBacktestPrompt()
        {
            // IdiotScript backtest removed - StrategyLoader and StrategyBacktestService are no longer available
            Log("");
            Log("=== IdiotScript Strategy Backtest ===");
            Log("");
            Log("This feature has been removed. Use the autonomous learning system instead.");
            Log("Press 2 to learn a ticker's market behavior, then press 3 to trade.");
            Log("");
        }

        private static void RunActiveMonitoringLoop()
        {
            // While trading is active, monitor for user input
            while (_isActive && !_shutdownCts.IsCancellationRequested)
            {
                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(intercept: true);
                    
                    // Check for X to close all positions
                    if (key.Key == ConsoleKey.X)
                    {
                        if (ConfirmCloseAllPositions())
                        {
                            ExecuteEmergencyClose();
                        }
                        continue;
                    }
                    
                    if (key.Key == ConsoleKey.M || key.Key == ConsoleKey.Escape)
                    {
                        Log("Returning to menu... (trading still active)");
                        break;
                    }
                    else if (key.Key == ConsoleKey.S)
                    {
                        DeactivateTrading();
                        Log("Trading stopped.");
                        break;
                    }
                    else if (key.Key == ConsoleKey.P)
                    {
                        // Print prices
                        PrintCurrentPrices();
                    }
                    else if (key.Key == ConsoleKey.H)
                    {
                        Log("Keys: [M]=Menu [S]=Stop [P]=Prices [X]=Close All [H]=Help");
                    }
                }
                Thread.Sleep(100);
            }
        }

        private static bool ConfirmActivateTrading()
        {
            var configs = LoadTickerConfigs();
            
            if (configs.Count == 0)
            {
                Log("No tickers configured. Add tickers first (option 1).");
                return false;
            }

            Console.ForegroundColor = ConsoleColor.Yellow;
            Log("");
            Log("+==============================================================+");
            Log("|  ACTIVATE LIVE TRADING                                       |");
            Log("+==============================================================+");
            Console.ResetColor();
            
            Log($"  Mode: {(AppSettings.IsPaperTrading ? "PAPER TRADING" : "*** LIVE TRADING ***")}");
            Log("+--------------------------------------------------------------+");
            Log("  Symbol     Capital      Price      Shares");
            Log("  --------   ----------   --------   ------");
            
            decimal totalCapital = 0;
            foreach (var kvp in configs.OrderBy(k => k.Key))
            {
                var symbol = kvp.Key;
                var capital = kvp.Value;
                totalCapital += capital;
                
                var price = GetTickerPrice(symbol);
                var priceStr = price > 0 ? $"${price:F2}" : "--";
                var sharesStr = price > 0 ? ((int)(capital / (decimal)price)).ToString() : "--";
                
                Log($"  {symbol,-10} ${capital,-10:N0} {priceStr,-10} {sharesStr}");
            }
            
            Log("+--------------------------------------------------------------+");
            Log($"  Total Capital: ${totalCapital:N0}");
            Log("+--------------------------------------------------------------+");
            
            Console.Write("  Start trading? (y/n): ");
            var response = Console.ReadLine()?.Trim().ToLowerInvariant();
            Log("");
            
            return response == "y" || response == "yes";
        }

        private static bool ConfirmCloseAllPositions()
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Log("");
            Log("+==============================================================+");
            Log("|  WARNING: CLOSE IDIOTPROOF POSITIONS                        |");
            Log("+==============================================================+");
            Console.ResetColor();
            
            // Get tickers that IdiotProof is managing
            var managedTickers = _runners.Select(r => r.Strategy.Symbol).ToHashSet(StringComparer.OrdinalIgnoreCase);
            
            if (managedTickers.Count == 0)
            {
                Log("  No active IdiotProof strategies - nothing to close.");
                Log("+==============================================================+");
                Log("");
                return false;
            }
            
            // Show current positions for managed tickers only
            if (_wrapper != null)
            {
                _wrapper.RequestPositionsAndWait(TimeSpan.FromSeconds(2));
                var positions = _wrapper.Positions
                    .Where(p => p.Value.Quantity != 0 && managedTickers.Contains(p.Key))
                    .ToList();
                
                if (positions.Count == 0)
                {
                    Log("  No open IdiotProof positions to close.");
                    Log("+==============================================================+");
                    Log("");
                    return false;
                }
                
                Log($"  Managed tickers: {string.Join(", ", managedTickers)}");
                Log("");
                foreach (var kvp in positions)
                {
                    var posType = kvp.Value.Quantity > 0 ? "LONG" : "SHORT";
                    Log($"  {kvp.Key}: {posType} {Math.Abs(kvp.Value.Quantity)} shares @ ${kvp.Value.AvgCost:F2}");
                }
            }
            
            Console.ForegroundColor = ConsoleColor.Yellow;
            Log("+--------------------------------------------------------------+");
            Log("  This will cancel orders and close IdiotProof positions at MARKET");
            Log("  (Other positions in your account will NOT be affected)");
            Console.ResetColor();
            Console.Write("  Are you sure? (y/n): ");
            
            var response = Console.ReadLine()?.Trim().ToLowerInvariant();
            Log("");
            
            return response == "y" || response == "yes";
        }

        private static void ExecuteEmergencyClose()
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Log("[!!!] EMERGENCY CLOSE ALL POSITIONS [!!!]");
            Console.ResetColor();
            
            // Cancel all open orders first
            _wrapper?.CancelAllOrders();
            Thread.Sleep(500); // Wait for cancels to process
            
            // Close all positions
            _ = CloseAllPositionsAsync();
        }

        private static void PrintCurrentPrices()
        {
            if (_prices.Count == 0)
            {
                Log("No price data available.");
                return;
            }

            var priceReport = string.Join(" | ", _prices
                .Where(p => p.Value > 0)
                .OrderBy(p => p.Key)
                .Select(p => $"{p.Key}: ${p.Value:F2}"));

            Log(priceReport);
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
            _sessionLogger?.Dispose();
            _tradeTrackingService?.Dispose();
            _shutdownCts.Dispose();

            Log("Goodbye!");
        }

        // ----- Helper Methods for direct access (previously IPC handlers) -----

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
                IsPaperTrading = AppSettings.IsPaperTrading
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

        private static Task<OperationResultPayload> CloseAllPositionsAsync()
        {
            if (_wrapper == null || _client == null)
                return Task.FromResult(new OperationResultPayload { Success = false, ErrorMessage = "Not connected" });

            try
            {
                // Get tickers that IdiotProof is managing
                var managedTickers = _runners.Select(r => r.Strategy.Symbol).ToHashSet(StringComparer.OrdinalIgnoreCase);
                
                if (managedTickers.Count == 0)
                {
                    Log("No active IdiotProof strategies to close.");
                    return Task.FromResult(new OperationResultPayload { Success = true, Message = "No managed strategies" });
                }
                
                // Refresh positions to get current state
                _wrapper.RequestPositionsAndWait(TimeSpan.FromSeconds(3));

                // Only close positions for tickers IdiotProof is managing
                var positions = _wrapper.Positions
                    .Where(p => p.Value.Quantity != 0 && managedTickers.Contains(p.Key))
                    .ToList();
                
                if (positions.Count == 0)
                {
                    Log("No open IdiotProof positions to close.");
                    return Task.FromResult(new OperationResultPayload { Success = true, Message = "No positions to close" });
                }

                Log("");
                Console.ForegroundColor = ConsoleColor.Red;
                Log("+==============================================================+");
                Log("|  EMERGENCY LIQUIDATION - CLOSING IDIOTPROOF POSITIONS       |");
                Log("+==============================================================+");
                Console.ResetColor();

                var closedCount = 0;
                var errors = new List<string>();

                foreach (var kvp in positions)
                {
                    var symbol = kvp.Key;
                    var position = kvp.Value;

                    try
                    {
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
                            Action = position.Quantity > 0 ? "SELL" : "BUY",
                            OrderType = "MKT",
                            TotalQuantity = Math.Abs(position.Quantity),
                            Tif = "GTC",
                            OutsideRth = true
                        };

                        var orderId = _wrapper.ConsumeNextOrderId();
                        var positionType = position.Quantity > 0 ? "LONG" : "SHORT";
                        
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Log($"  [{symbol}] Closing {positionType} {Math.Abs(position.Quantity)} shares @ MKT (Order #{orderId})");
                        Console.ResetColor();
                        
                        _client.placeOrder(orderId, contract, order);
                        closedCount++;
                        
                        Thread.Sleep(50); // Small delay between orders
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"{symbol}: {ex.Message}");
                        Log($"  [ERR] Failed to close {symbol}: {ex.Message}");
                    }
                }

                Log("+--------------------------------------------------------------+");
                
                if (errors.Count == 0)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Log($"  [OK] Close orders sent for {closedCount} position(s)");
                    Console.ResetColor();
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Log($"  Sent {closedCount} orders, {errors.Count} failed");
                    Console.ResetColor();
                }
                
                Log("+==============================================================+");
                Log("");

                return Task.FromResult(new OperationResultPayload 
                { 
                    Success = errors.Count == 0, 
                    Message = $"Close orders sent for {closedCount} positions",
                    ErrorMessage = errors.Count > 0 ? string.Join("; ", errors) : null
                });
            }
            catch (Exception ex)
            {
                Log($"[ERR] Emergency close failed: {ex.Message}");
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
                            // TODO: StrategyLoader removed - this code path is deprecated
                            // Autonomous trading no longer uses StrategyDefinition conversion
                            Log($"Cannot update strategy: {def.Name} ({def.Symbol}) - StrategyLoader removed");
                        }
                    }
                    else
                    {
                        // New strategy - create instance
                        // TODO: StrategyLoader removed - this code path is deprecated
                        // Autonomous trading no longer uses StrategyDefinition conversion
                        Log($"Cannot add strategy: {def.Name} ({def.Symbol}) - StrategyLoader removed");
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
            // IdiotScriptSerializer removed - use a simple fingerprint based on key properties
            var stats = def.GetStats();
            var parts = new List<string>
            {
                def.Symbol,
                def.Name ?? "",
                stats.Quantity.ToString(),
                stats.TakeProfit.ToString(),
                stats.StopLoss.ToString(),
                def.Segments.Count.ToString(),
                def.Enabled.ToString()
            };
            return string.Join("|", parts);
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

                // Attach metadata for informed trading decisions
                if (_metadataService != null)
                {
                    runner.TickerMetadata = _metadataService.Load(strategy.Symbol);
                }

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
                    int tickerId = _tickerIds[symbolIndex];
                    _wrapper.RegisterTickerHandler(tickerId, runner.OnLastTrade);

                    // Register bid/ask handler for PriceType support
                    _wrapper.RegisterBidAskHandler(tickerId, runner.OnBidAskUpdate);
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
                // ValidateForExecution extension removed - use the built-in Validate method
                var result = strategy.Validate();

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

        private static async Task<BacktestResponsePayload> RunBacktestAsync(RunBacktestRequest request)
        {
            if (_backtestService == null || _historicalDataService == null)
            {
                return new BacktestResponsePayload
                {
                    Success = false,
                    Symbol = request.Symbol,
                    ErrorMessage = "Backtest service not initialized. Ensure IBKR connection is established."
                };
            }

            Log($"Running backtest for {request.Symbol}...");
            return await _backtestService.RunBacktestAsync(request);
        }

        private static async Task<HistoricalDataResponse> GetHistoricalDataAsync(HistoricalDataRequest request)
        {
            if (_historicalDataService == null)
            {
                return new HistoricalDataResponse
                {
                    Success = false,
                    Symbol = request.Symbol,
                    Date = request.Date,
                    ErrorMessage = "Historical data service not initialized. Ensure IBKR connection is established."
                };
            }

            try
            {
                Log($"Fetching historical data for {request.Symbol} on {request.Date:yyyy-MM-dd}...");

                // Fetch data from IBKR (request 5 days to ensure we get the specific date)
                int barsNeeded = 960; // Full extended day
                await _historicalDataService.FetchHistoricalDataAsync(
                    request.Symbol,
                    barsNeeded,
                    Enums.BarSize.Minutes1,
                    Enums.HistoricalDataType.Trades,
                    useRTH: !request.IncludeExtendedHours);

                var allBars = _historicalDataService.Store.GetBars(request.Symbol);

                if (allBars.Count == 0)
                {
                    return new HistoricalDataResponse
                    {
                        Success = false,
                        Symbol = request.Symbol,
                        Date = request.Date,
                        ErrorMessage = $"No historical data received for {request.Symbol}"
                    };
                }

                // Filter to the requested date
                var dateBars = allBars
                    .Where(b => DateOnly.FromDateTime(b.Time) == request.Date)
                    .OrderBy(b => b.Time)
                    .ToList();

                if (dateBars.Count == 0)
                {
                    var availableDates = allBars
                        .Select(b => DateOnly.FromDateTime(b.Time))
                        .Distinct()
                        .OrderByDescending(d => d)
                        .Take(10);

                    return new HistoricalDataResponse
                    {
                        Success = false,
                        Symbol = request.Symbol,
                        Date = request.Date,
                        ErrorMessage = $"No data for {request.Date:yyyy-MM-dd}. Available: {string.Join(", ", availableDates)}"
                    };
                }

                Log($"Returning {dateBars.Count} bars for {request.Symbol} on {request.Date:yyyy-MM-dd}");

                return new HistoricalDataResponse
                {
                    Success = true,
                    Symbol = request.Symbol,
                    Date = request.Date,
                    Bars = dateBars.Select(b => new HistoricalBarInfo
                    {
                        Time = b.Time,
                        Open = b.Open,
                        High = b.High,
                        Low = b.Low,
                        Close = b.Close,
                        Volume = b.Volume,
                        Vwap = b.Vwap
                    }).ToList()
                };
            }
            catch (Exception ex)
            {
                return new HistoricalDataResponse
                {
                    Success = false,
                    Symbol = request.Symbol,
                    Date = request.Date,
                    ErrorMessage = $"Error fetching data: {ex.Message}"
                };
            }
        }

        // ----- Helpers -----

        private static void Log(string message)
        {
            var formatted = $"{TimeStamp.NowBracketed} {message}";
            Console.WriteLine(formatted);
        }
    }
}


