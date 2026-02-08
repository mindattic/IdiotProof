// ================================================================
// IdiotProof Core - IBKR Trading Engine
// Consolidated trading engine (formerly Backend + Console)
// ================================================================

using IBApi;
using IdiotProof.Backend.Logging;
using IdiotProof.Backend.Models;
using IdiotProof.Backend.Services;
using IdiotProof.Backend.Strategy;
using IdiotProof.Core.Helpers;
using IdiotProof.Core.Models;
using IdiotProof.Core.Scripting;
using IdiotProof.Core.Services;
using IdiotProof.Core.Settings;
using IdiotProof.Core.Validation;

namespace IdiotProof.Backend
{
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
                    Settings.SilentMode = true;
                }
            }
        }

        private static void Run()
        {
            Log("IdiotProof Core starting...");
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

            // Subscribe to account updates for margin monitoring
            _wrapper.RequestAccountUpdates(Settings.AccountNumber ?? "");

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

        private static void RunInteractiveMenu()
        {
            Log("");
            Log("========================================");
            Log("  IdiotProof Core - Interactive Mode");
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
                            PrintStatus();
                            break;

                        case "2":
                            if (!_isActive)
                            {
                                ActivateTrading();
                                if (_isActive)
                                {
                                    Log("Trading activated. Monitoring markets...");
                                    Log("Press [M] for menu, [S] to stop, [P] for prices");
                                    RunActiveMonitoringLoop();
                                }
                            }
                            else
                            {
                                Log("Trading already active.");
                            }
                            break;

                        case "3":
                            if (_isActive)
                            {
                                DeactivateTrading();
                            }
                            else
                            {
                                Log("Trading not active.");
                            }
                            break;

                        case "4":
                            RunLearnPrompt();
                            break;

                        case "5":
                            PrintProfiles();
                            break;

                        case "6":
                            PrintSettings();
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
            Log("");
            Log("----------------------------------------");
            Log($"  Status: {(_isConnected ? "Connected" : "Disconnected")} | Trading: {(_isActive ? "ACTIVE" : "Paused")}");
            Log("----------------------------------------");
            Log("  1. Status          - Show connection info");
            Log("  2. Start Trading   - Activate live trading");
            Log("  3. Stop Trading    - Pause trading");
            Log("  4. Learn           - Fetch data + learn ticker");
            Log("  5. Profiles        - View learned ticker profiles");
            Log("  6. Settings        - Show current settings");
            Log("  0. Exit            - Shutdown and exit");
            Log("----------------------------------------");
            Log("");
        }

        private static void PrintStatus()
        {
            Log("");
            Log($"IBKR Connection: {(_isConnected ? "[OK] Connected" : "[--] Disconnected")}");
            Log($"Trading Status:  {(_isActive ? "[OK] Active" : "[--] Paused")}");
            Log($"Mode:            {(Settings.IsPaperTrading ? "PAPER" : "LIVE")}");
            Log($"Port:            {Settings.Port}");
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
            Log($"  Trading Mode:     {(Settings.IsPaperTrading ? "PAPER" : "LIVE")}");
            Log($"  IBKR Host:        {Settings.Host}");
            Log($"  IBKR Port:        {Settings.Port}");
            Log($"  Client ID:        {Settings.ClientId}");
            Log($"  Account:          {Settings.AccountNumber ?? "(auto)"}");
            Log($"  Strategies Dir:   {SettingsManager.GetStrategiesFolder()}");
            Log($"  Price Check:      {Settings.TickerPriceCheckIntervalSeconds}s");
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
                Log($"{p.Symbol,-8} {p.TotalTrades,8} {p.WinRate,7:F1}% {p.LongWinRate,7:F1}% {p.ShortWinRate,7:F1}% {p.ProfitFactor,8:F2} {p.NetProfit,11:C0} {p.Confidence,5}% {p.OptimalLongEntryThreshold,8} {p.OptimalShortEntryThreshold,9}");
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

        private static void RunLearnPrompt()
        {
            if (!_isConnected || _historicalDataService == null)
            {
                Log("ERROR: Not connected to IBKR. Learning requires connection to fetch historical data.");
                return;
            }

            Log("");
            Log("=== Learn Ticker ===");
            Log("Fetches 30 days of data (if needed), runs learning iterations,");
            Log("and saves a custom-tuned profile for live trading.");
            Log("");

            // Symbol(s)
            Console.Write("  Symbol(s) (e.g., NVDA or NVDA,AAPL,TSLA): ");
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

            // Iterations
            Console.Write("  Iterations [10]: ");
            var iterInput = Console.ReadLine()?.Trim();
            int iterations = string.IsNullOrEmpty(iterInput) ? 10 :
                             int.TryParse(iterInput, out var i) ? Math.Clamp(i, 1, 1000) : 10;

            Log("");
            Log($"Learning {symbols.Count} ticker(s) with {iterations} iterations each...");
            Log("");

            foreach (var symbol in symbols)
            {
                Log($"========================================");
                Log($"  LEARNING: {symbol}");
                Log($"========================================");

                try
                {
                    // Create learner with data service for auto-fetching
                    var learner = new IdiotProof.Core.Learning.LearningBacktester(_historicalDataService);

                    // Create progress reporter
                    var progress = new Progress<string>(msg => Log(msg));

                    // Run learning (auto-fetches data if needed)
                    var history = learner.LearnAsync(
                        symbol,
                        iterations,
                        30, // days per iteration
                        progress,
                        _shutdownCts.Token).GetAwaiter().GetResult();

                    // Summary
                    Log("");
                    Log($"  [OK] {symbol} LEARNING COMPLETE");
                    Log($"  Duration: {history.TotalDuration:hh\\:mm\\:ss}");

                    // Show final parameters
                    var bestParams = history.BacktestData?.BestParameters;
                    if (bestParams != null)
                    {
                        Log($"  Best Entry Threshold: {bestParams.LongEntryThreshold:F0}");
                    }

                    // Show win rate improvement
                    Log($"  Win Rate: {history.InitialWinRate:F1}% -> {history.FinalWinRate:F1}% ({history.WinRateImprovement:+0.0;-0.0}%)");
                    Log($"  Best Fitness: {history.BacktestData?.BestFitnessScore:F2}");

                    // Show profile info
                    if (history.FinalProfile != null)
                    {
                        Log($"  Profile: {history.FinalProfile.TotalTrades} trades, {history.FinalProfile.TotalPnL:F2}% P&L");
                    }
                    Log("");
                }
                catch (OperationCanceledException)
                {
                    Log($"  [--] {symbol} cancelled.");
                    break;
                }
                catch (Exception ex)
                {
                    Log($"  [ERR] {symbol} failed: {ex.Message}");
                    Log("");
                }
            }

            Log("========================================");
            Log("  LEARNING BATCH COMPLETE");
            Log($"  Profiles saved to: {SettingsManager.GetHistoryFolder()}");
            Log("========================================");
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

            // Mode
            Console.Write("  Mode (1=Conservative, 2=Balanced, 3=Aggressive) [2]: ");
            var modeInput = Console.ReadLine()?.Trim();
            IdiotProof.BackTesting.Services.AutonomousMode mode = modeInput switch
            {
                "1" => IdiotProof.BackTesting.Services.AutonomousMode.Conservative,
                "3" => IdiotProof.BackTesting.Services.AutonomousMode.Aggressive,
                _ => IdiotProof.BackTesting.Services.AutonomousMode.Balanced
            };

            Log("");
            Log($"Running aggregate backtest for {symbolInput}...");
            Log($"  Capital: ${capital:N2}, Mode: {mode}");
            Log("");
            Log("Fetching historical data and running day-by-day simulation...");
            Log("(This may take a minute for first run - data is cached for future tests)");
            Log("");

            try
            {
                // Create backtester with data cache
                var dataCache = new HistoricalDataCache();
                var backtester = new AutonomousBacktester(_historicalDataService, null, dataCache);

                // Configure
                var config = new AutonomousBacktestConfig
                {
                    StartingCapital = capital,
                    Mode = mode,
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

        private static void RunLearningBacktestPrompt()
        {
            Log("");
            Log("=== Iterative Learning Backtest ===");
            Log("Runs multiple iterations, adjusts parameters, discovers ticker fingerprint.");
            Log("Results saved to Data/SYMBOL.backtesting.json and SYMBOL.profile.json.");
            Log("");

            // Symbol
            Console.Write("  Symbol (e.g., NVDA): ");
            var symbolInput = Console.ReadLine()?.Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(symbolInput))
            {
                Log("  Cancelled.");
                return;
            }

            // Check for cached data
            var dataFolder = SettingsManager.GetHistoryFolder();
            var cacheFile = Path.Combine(dataFolder, $"{symbolInput}.history.json");
            if (!File.Exists(cacheFile))
            {
                Log($"  No cached data found for {symbolInput}.");
                Log($"  Please run Option 4 (Backtest) first to fetch historical data.");
                return;
            }

            // Iterations
            Console.Write("  Iterations [10]: ");
            var iterInput = Console.ReadLine()?.Trim();
            int iterations = string.IsNullOrEmpty(iterInput) ? 10 :
                             int.TryParse(iterInput, out var i) ? Math.Clamp(i, 1, 1000) : 10;

            Log("");
            Log($"Starting {iterations}-iteration learning for {symbolInput}...");
            Log("Each iteration processes 30 days, then adjusts parameters.");
            Log("");

            try
            {
                // Create learner with data service for auto-fetching
                var learner = new IdiotProof.Core.Learning.LearningBacktester(_historicalDataService);

                // Create progress reporter
                var progress = new Progress<string>(msg => Log(msg));

                // Run learning
                var history = learner.LearnAsync(
                    symbolInput,
                    iterations,
                    30, // days per iteration
                    progress,
                    _shutdownCts.Token).GetAwaiter().GetResult();

                // Summary
                Log("");
                Log("========================================");
                Log("  LEARNING COMPLETE");
                Log("========================================");
                Log($"  Total Iterations: {history.TotalIterations}");
                Log($"  Duration:         {history.TotalDuration:hh\\:mm\\:ss}");
                Log($"  Files Saved:");
                Log($"    - {symbolInput}.backtesting.json");
                Log($"    - {symbolInput}.profile.json");
                Log("");

                // Show final parameters
                var bestParams = history.BacktestData?.BestParameters;
                if (bestParams != null)
                {
                    Log("  Best Parameters Found:");
                    Log($"    Entry Threshold:     {bestParams.LongEntryThreshold:F0}");
                    Log($"    LOD Bounce Threshold: {bestParams.LodBounceThreshold:F0}");
                    Log($"    Near LOD %:          {bestParams.NearLodPercent:F1}%");
                    Log($"    Near HOD %:          {bestParams.NearHodPercent:F1}%");
                    Log($"    RSI Exit:            {bestParams.MomentumExitRsi:F0}");
                    Log("");
                }

                // Show win rate improvement
                Log("  Performance:");
                Log($"    Initial Win Rate: {history.InitialWinRate:F1}%");
                Log($"    Final Win Rate:   {history.FinalWinRate:F1}%");
                Log($"    Improvement:      {history.WinRateImprovement:+0.0;-0.0}%");
                Log($"    Best Fitness:     {history.BacktestData?.BestFitnessScore:F2}");
                Log("");

                // Show profile info
                if (history.FinalProfile != null)
                {
                    Log("  Profile Summary:");
                    Log($"    Total Trades:  {history.FinalProfile.TotalTrades}");
                    Log($"    Total P&L:     {history.FinalProfile.TotalPnL:F2}%");
                    Log($"    Confidence:    {history.FinalProfile.Confidence:P0}");
                }
                Log("");
            }
            catch (OperationCanceledException)
            {
                Log("Learning cancelled.");
            }
            catch (Exception ex)
            {
                Log($"Learning failed: {ex.Message}");
                Log($"Stack: {ex.StackTrace}");
            }
        }

        private static void RunStrategyBacktestPrompt()
        {
            Log("");
            Log("=== IdiotScript Strategy Backtest ===");
            Log("");

            // List available strategies
            var strategiesFolder = SettingsManager.GetStrategiesFolder();
            if (!Directory.Exists(strategiesFolder))
            {
                Log($"Scripts folder not found: {strategiesFolder}");
                return;
            }

            var scriptFiles = Directory.GetFiles(strategiesFolder, "*.idiot")
                .Select(Path.GetFileName)
                .OrderBy(f => f)
                .ToList();

            if (scriptFiles.Count == 0)
            {
                Log($"No .idiot files found in: {strategiesFolder}");
                return;
            }

            Log("Available strategies:");
            for (int i = 0; i < scriptFiles.Count; i++)
            {
                Log($"  {i + 1}. {scriptFiles[i]}");
            }
            Log("");

            // Select strategy
            Console.Write($"  Select strategy [1-{scriptFiles.Count}]: ");
            var selInput = Console.ReadLine()?.Trim();
            if (!int.TryParse(selInput, out var selIndex) || selIndex < 1 || selIndex > scriptFiles.Count)
            {
                Log("Invalid selection.");
                return;
            }

            var selectedFile = scriptFiles[selIndex - 1];
            var selectedPath = Path.Combine(strategiesFolder, selectedFile!);

            // Load and parse the strategy
            Log($"Loading {selectedFile}...");
            var strategies = StrategyLoader.LoadFromFile();
            var symbolWithoutExt = Path.GetFileNameWithoutExtension(selectedFile);

            var strategy = strategies.FirstOrDefault(s =>
                s.Symbol.Equals(symbolWithoutExt, StringComparison.OrdinalIgnoreCase) ||
                s.Name.Contains(symbolWithoutExt!, StringComparison.OrdinalIgnoreCase));

            if (strategy == null)
            {
                // Try loading just this specific file
                var content = File.ReadAllText(selectedPath);
                if (!IdiotScriptParser.TryParse(content, out var def, out var parseError, symbolWithoutExt))
                {
                    Log($"Failed to parse {selectedFile}: {parseError ?? "Unknown error"}");
                    return;
                }

                strategy = StrategyLoader.ConvertDefinition(def!);
                if (strategy == null)
                {
                    Log($"Failed to convert strategy definition.");
                    return;
                }
            }

            Log($"Loaded: {strategy.Name} ({strategy.Symbol})");
            Log($"Conditions: {strategy.Conditions.Count}");
            foreach (var cond in strategy.Conditions)
            {
                Log($"  - {cond.Name}");
            }
            Log("");

            // Days to backtest
            Console.Write("  Days to backtest [10]: ");
            var daysInput = Console.ReadLine()?.Trim();
            var days = string.IsNullOrEmpty(daysInput) ? 10 : int.TryParse(daysInput, out var d) ? d : 10;

            Log("");
            Log($"Starting IdiotScript backtest for {strategy.Symbol}...");
            Log($"  Strategy: {strategy.Name}");
            Log($"  Days: {days}");
            Log("");

            // Create the backtest service
            if (_historicalDataService == null)
            {
                Log("Historical data service not initialized. Please connect first.");
                return;
            }

            var backtestService = new StrategyBacktestService(_historicalDataService);
            var result = backtestService.RunBacktestAsync(strategy, days, verboseLogging: true).GetAwaiter().GetResult();

            Log("");
            if (result.Success)
            {
                Log("=== IdiotScript Backtest Results ===");
                Log($"Symbol:         {result.Symbol}");
                Log($"Strategy:       {result.StrategyName}");
                Log($"Bars Processed: {result.BarsProcessed:N0}");
                Log($"Date Range:     {result.FirstBarTime:yyyy-MM-dd} to {result.LastBarTime:yyyy-MM-dd}");
                Log($"Total Trades:   {result.TotalTrades}");
                Log($"Winning Trades: {result.WinningTrades}");
                Log($"Win Rate:       {result.WinRate:F1}%");
                Log($"Total P&L:      ${result.TotalPnL:F2}");
                Log($"Avg P&L:        ${result.AvgPnL:F2}");
                Log("====================================");

                // Show trade details
                if (result.Trades.Count > 0 && result.Trades.Count <= 20)
                {
                    Log("");
                    Log("Trade Details:");
                    foreach (var trade in result.Trades)
                    {
                        var pnlStr = trade.PnL >= 0 ? $"+${trade.PnL:F2}" : $"-${Math.Abs(trade.PnL):F2}";
                        Log($"  {trade.EntryTime:MM/dd HH:mm} ${trade.EntryPrice:F2} -> {trade.ExitTime:MM/dd HH:mm} ${trade.ExitPrice:F2} ({trade.ExitReason}) {pnlStr}");
                    }
                }
            }
            else
            {
                Log($"Backtest failed: {result.ErrorMessage}");
            }
        }

        private static void RunActiveMonitoringLoop()
        {
            // While trading is active, monitor for user input
            while (_isActive && !_shutdownCts.IsCancellationRequested)
            {
                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(intercept: true);
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
                        Log("Keys: [M]=Menu [S]=Stop [P]=Prices [H]=Help [Esc]=Menu");
                    }
                }
                Thread.Sleep(100);
            }
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
            return IdiotProof.Core.Scripting.IdiotScriptSerializer.Serialize(def);
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


