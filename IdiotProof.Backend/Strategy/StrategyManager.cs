// ============================================================================
// StrategyManager - Centralized manager for strategy lifecycle
// ============================================================================
//
// RESPONSIBILITIES:
// - Load strategies from JSON files or programmatic definitions
// - Manage strategy runners lifecycle (create, start, stop, dispose)
// - Handle thread-safe access to strategies
// - Provide status reporting and monitoring
// - Support hot-reload of strategies
//
// THREAD SAFETY:
// - Uses ConcurrentDictionary for runner storage
// - Uses SemaphoreSlim for async coordination
// - All public methods are thread-safe
//
// ============================================================================

using IBApi;
using IdiotProof.Backend.Helpers;
using IdiotProof.Backend.Logging;
using IdiotProof.Backend.Strategy;
using IdiotProof.Shared.Helpers;
using IdiotProof.Shared.Models;
using IdiotProof.Shared.Services;
using System.Collections.Concurrent;

namespace IdiotProof.Backend.Models
{
    /// <summary>
    /// Centralized manager for trading strategy lifecycle.
    /// Handles loading, running, and monitoring of all active strategies.
    /// </summary>
    /// <remarks>
    /// <para><b>Thread Safety:</b></para>
    /// <list type="bullet">
    ///   <item>All public methods are thread-safe.</item>
    ///   <item>Uses <see cref="ConcurrentDictionary{TKey, TValue}"/> for runner storage.</item>
    ///   <item>Uses <see cref="SemaphoreSlim"/> for async coordination.</item>
    /// </list>
    /// 
    /// <para><b>Usage:</b></para>
    /// <code>
    /// await using var manager = new StrategyManager(wrapper, client);
    /// await manager.LoadStrategiesFromJsonAsync();
    /// await manager.StartAllAsync();
    /// </code>
    /// </remarks>
    public sealed class StrategyManager : IAsyncDisposable, IDisposable
    {
        /// <summary>
        /// Shared session logger instance (set from Program.cs).
        /// </summary>
        public static SessionLogger? SessionLogger { get; set; }

        private readonly IbWrapper _wrapper;
        private readonly EClientSocket _client;
        private readonly ConcurrentDictionary<Guid, StrategyRunnerInfo> _runners = new();
        private readonly ConcurrentDictionary<string, Contract> _contracts = new();
        private readonly SemaphoreSlim _loadLock = new(1, 1);
        private readonly object _stateLock = new();

        private volatile bool _disposed;
        private volatile bool _isRunning;
        private int _nextTickerId = 2000; // Start high to avoid conflicts with other subscriptions

        /// <summary>
        /// Logs a message to both console and session log file.
        /// </summary>
        private static void Log(string message)
        {
            Console.WriteLine($"{TimeStamp.NowBracketed} [StrategyManager] {message}");
            SessionLogger?.LogEvent("MANAGER", message);
        }

        /// <summary>
        /// Gets whether the manager is currently running strategies.
        /// </summary>
        public bool IsRunning => _isRunning;

        /// <summary>
        /// Gets the count of active strategy runners.
        /// </summary>
        public int ActiveCount => _runners.Count(r => !r.Value.Runner.IsComplete);

        /// <summary>
        /// Gets the total count of strategy runners.
        /// </summary>
        public int TotalCount => _runners.Count;

        /// <summary>
        /// Event fired when a strategy completes (success or failure).
        /// </summary>
        public event EventHandler<StrategyCompletedEventArgs>? StrategyCompleted;

        /// <summary>
        /// Event fired when a strategy encounters an error.
        /// </summary>
        public event EventHandler<StrategyErrorEventArgs>? StrategyError;

        /// <summary>
        /// Initializes a new instance of the <see cref="StrategyManager"/> class.
        /// </summary>
        /// <param name="wrapper">The IB API wrapper instance.</param>
        /// <param name="client">The IB API client socket.</param>
        /// <exception cref="ArgumentNullException">Thrown if wrapper or client is null.</exception>
        public StrategyManager(IbWrapper wrapper, EClientSocket client)
        {
            _wrapper = wrapper ?? throw new ArgumentNullException(nameof(wrapper));
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        /// <summary>
        /// Loads strategies from JSON files for today's date.
        /// </summary>
        /// <returns>The number of strategies loaded.</returns>
        public async Task<int> LoadStrategiesFromJsonAsync()
        {
            return await LoadStrategiesFromJsonAsync(DateOnly.FromDateTime(DateTime.Today));
        }

        /// <summary>
        /// Loads strategies from JSON files for the specified date.
        /// </summary>
        /// <param name="date">The date to load strategies for.</param>
        /// <returns>The number of strategies loaded.</returns>
        public async Task<int> LoadStrategiesFromJsonAsync(DateOnly date)
        {
            await _loadLock.WaitAsync();
            try
            {
                ThrowIfDisposed();

                var definitions = StrategyJsonParser.LoadStrategiesForDate(date);
                var enabledDefinitions = definitions.Where(d => d.Enabled).ToList();

                int loaded = 0;
                foreach (var definition in enabledDefinitions)
                {
                    try
                    {
                        var strategy = ConvertDefinitionToStrategy(definition);
                        if (strategy != null)
                        {
                            await AddStrategyAsync(strategy, definition.Id);
                            loaded++;
                        }
                    }
                    catch (Exception ex)
                    {
                        OnStrategyError(definition.Id, definition.Name, $"Failed to load: {ex.Message}");
                    }
                }

                Log($"Loaded {loaded} strategies from {date:yyyy-MM-dd}");
                return loaded;
            }
            finally
            {
                _loadLock.Release();
            }
        }

        /// <summary>
        /// Loads strategies from a list of TradingStrategy objects.
        /// </summary>
        /// <param name="strategies">The strategies to load.</param>
        /// <returns>The number of strategies loaded.</returns>
        public async Task<int> LoadStrategiesAsync(IEnumerable<TradingStrategy> strategies)
        {
            await _loadLock.WaitAsync();
            try
            {
                ThrowIfDisposed();

                int loaded = 0;
                foreach (var strategy in strategies.Where(s => s.Enabled))
                {
                    try
                    {
                        await AddStrategyAsync(strategy);
                        loaded++;
                    }
                    catch (Exception ex)
                    {
                        Log($"Failed to load strategy for {strategy.Symbol}: {ex.Message}");
                    }
                }

                return loaded;
            }
            finally
            {
                _loadLock.Release();
            }
        }

        /// <summary>
        /// Adds a single strategy to the manager.
        /// </summary>
        /// <param name="strategy">The strategy to add.</param>
        /// <param name="id">Optional ID for the strategy. If not provided, a new GUID is generated.</param>
        public async Task AddStrategyAsync(TradingStrategy strategy, Guid? id = null)
        {
            ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(strategy);

            var strategyId = id ?? Guid.NewGuid();

            // Get or create contract
            var contract = GetOrCreateContract(strategy);

            // Create runner
            var runner = new StrategyRunner(strategy, contract, _wrapper, _client);

            // Allocate ticker ID and register handler
            int tickerId = Interlocked.Increment(ref _nextTickerId);

            var info = new StrategyRunnerInfo
            {
                Id = strategyId,
                Runner = runner,
                Contract = contract,
                TickerId = tickerId,
                CreatedAt = DateTime.UtcNow
            };

            if (!_runners.TryAdd(strategyId, info))
            {
                runner.Dispose();
                throw new InvalidOperationException($"Strategy with ID {strategyId} already exists.");
            }

            // Register market data handler
            _wrapper.RegisterTickerHandler(tickerId, runner.OnLastTrade);

            // If already running, subscribe to market data immediately
            if (_isRunning)
            {
                await Task.Run(() => _client.reqMktData(tickerId, contract, "", false, false, null));
            }

            Log($"Added strategy: {strategy.Symbol} (ID: {strategyId})");
        }

        /// <summary>
        /// Removes a strategy by its ID.
        /// </summary>
        /// <param name="id">The strategy ID to remove.</param>
        /// <returns>True if the strategy was removed, false if not found.</returns>
        public async Task<bool> RemoveStrategyAsync(Guid id)
        {
            ThrowIfDisposed();

            if (_runners.TryRemove(id, out var info))
            {
                await CleanupRunnerAsync(info);
                Log($"Removed strategy: {info.Runner.Symbol} (ID: {id})");
                return true;
            }

            return false;
        }

        /// <summary>
        /// Starts all strategies and begins monitoring.
        /// </summary>
        public async Task StartAllAsync()
        {
            ThrowIfDisposed();

            lock (_stateLock)
            {
                if (_isRunning)
                    return;
                _isRunning = true;
            }

            Log("Starting all strategies...");

            // Subscribe to market data for all strategies
            await Task.Run(() =>
            {
                foreach (var kvp in _runners)
                {
                    var info = kvp.Value;
                    _client.reqMktData(info.TickerId, info.Contract, "", false, false, null);
                }
            });

            Log($"Started {_runners.Count} strategies");
        }

        /// <summary>
        /// Stops all strategies and cancels market data subscriptions.
        /// </summary>
        public async Task StopAllAsync()
        {
            lock (_stateLock)
            {
                if (!_isRunning)
                    return;
                _isRunning = false;
            }

            Log("Stopping all strategies...");

            // Cancel market data subscriptions
            await Task.Run(() =>
            {
                foreach (var kvp in _runners)
                {
                    var info = kvp.Value;
                    try
                    {
                        _client.cancelMktData(info.TickerId);
                    }
                    catch
                    {
                        // Ignore errors during shutdown
                    }
                }
            });

            Log("All strategies stopped");
        }

        /// <summary>
        /// Reloads strategies from JSON files, replacing current strategies.
        /// </summary>
        public async Task ReloadStrategiesAsync()
        {
            await ReloadStrategiesAsync(DateOnly.FromDateTime(DateTime.Today));
        }

        /// <summary>
        /// Reloads strategies from JSON files for the specified date.
        /// </summary>
        /// <param name="date">The date to load strategies for.</param>
        public async Task ReloadStrategiesAsync(DateOnly date)
        {
            await _loadLock.WaitAsync();
            try
            {
                ThrowIfDisposed();

                Log("Reloading strategies...");

                // Stop current strategies
                bool wasRunning = _isRunning;
                if (wasRunning)
                    await StopAllAsync();

                // Clear existing runners
                foreach (var kvp in _runners.ToArray())
                {
                    if (_runners.TryRemove(kvp.Key, out var info))
                    {
                        await CleanupRunnerAsync(info);
                    }
                }

                // Load new strategies
                await LoadStrategiesFromJsonAsync(date);

                // Restart if was running
                if (wasRunning)
                    await StartAllAsync();

                Log("Reload complete");
            }
            finally
            {
                _loadLock.Release();
            }
        }

        /// <summary>
        /// Gets the status of all strategies.
        /// </summary>
        /// <returns>List of strategy status information.</returns>
        public IReadOnlyList<StrategyStatusInfo> GetAllStatus()
        {
            return _runners.Values
                .Select(info => new StrategyStatusInfo
                {
                    Id = info.Id,
                    Symbol = info.Runner.Symbol,
                    CurrentStep = info.Runner.CurrentStep,
                    TotalSteps = info.Runner.TotalSteps,
                    IsComplete = info.Runner.IsComplete,
                    EntryFilled = info.Runner.EntryFilled,
                    EntryPrice = info.Runner.EntryFillPrice,
                    CurrentPrice = info.Runner.LastPrice,
                    Result = info.Runner.Result,
                    CreatedAt = info.CreatedAt
                })
                .ToList();
        }

        /// <summary>
        /// Gets the status of a specific strategy.
        /// </summary>
        /// <param name="id">The strategy ID.</param>
        /// <returns>The status info, or null if not found.</returns>
        public StrategyStatusInfo? GetStatus(Guid id)
        {
            if (_runners.TryGetValue(id, out var info))
            {
                return new StrategyStatusInfo
                {
                    Id = info.Id,
                    Symbol = info.Runner.Symbol,
                    CurrentStep = info.Runner.CurrentStep,
                    TotalSteps = info.Runner.TotalSteps,
                    IsComplete = info.Runner.IsComplete,
                    EntryFilled = info.Runner.EntryFilled,
                    EntryPrice = info.Runner.EntryFillPrice,
                    CurrentPrice = info.Runner.LastPrice,
                    Result = info.Runner.Result,
                    CreatedAt = info.CreatedAt
                };
            }
            return null;
        }

        private Contract GetOrCreateContract(TradingStrategy strategy)
        {
            return _contracts.GetOrAdd(strategy.Symbol, _ => new Contract
            {
                Symbol = strategy.Symbol,
                SecType = strategy.SecType,
                Exchange = strategy.Exchange,
                PrimaryExch = strategy.PrimaryExchange ?? "",
                Currency = strategy.Currency
            });
        }

        private static TradingStrategy? ConvertDefinitionToStrategy(StrategyDefinition definition)
        {
            // Use the existing StrategyLoader conversion logic
            // This delegates to the existing parser infrastructure
            return StrategyLoader.ConvertDefinition(definition);
        }

        private async Task CleanupRunnerAsync(StrategyRunnerInfo info)
        {
            try
            {
                // Cancel market data subscription
                _client.cancelMktData(info.TickerId);
            }
            catch
            {
                // Ignore errors
            }

            // Unregister ticker handler
            _wrapper.UnregisterTickerHandler(info.TickerId);

            // Dispose runner
            await Task.Run(() => info.Runner.Dispose());
        }

        private void OnStrategyCompleted(Guid id, string symbol, Enums.StrategyResult result)
        {
            StrategyCompleted?.Invoke(this, new StrategyCompletedEventArgs
            {
                StrategyId = id,
                Symbol = symbol,
                Result = result
            });
        }

        private void OnStrategyError(Guid id, string symbol, string error)
        {
            StrategyError?.Invoke(this, new StrategyErrorEventArgs
            {
                StrategyId = id,
                Symbol = symbol,
                ErrorMessage = error
            });
        }

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }

        public void Dispose()
        {
            DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
                return;

            _disposed = true;

            // Stop all strategies
            await StopAllAsync();

            // Cleanup all runners
            foreach (var kvp in _runners.ToArray())
            {
                if (_runners.TryRemove(kvp.Key, out var info))
                {
                    await CleanupRunnerAsync(info);
                }
            }

            _loadLock.Dispose();

            Log("Disposed");
        }

        /// <summary>
        /// Internal class to track runner information.
        /// </summary>
        private sealed class StrategyRunnerInfo
        {
            public required Guid Id { get; init; }
            public required StrategyRunner Runner { get; init; }
            public required Contract Contract { get; init; }
            public required int TickerId { get; init; }
            public required DateTime CreatedAt { get; init; }
        }
    }

    /// <summary>
    /// Status information for a strategy.
    /// </summary>
    public sealed class StrategyStatusInfo
    {
        public required Guid Id { get; init; }
        public required string Symbol { get; init; }
        public required int CurrentStep { get; init; }
        public required int TotalSteps { get; init; }
        public required bool IsComplete { get; init; }
        public required bool EntryFilled { get; init; }
        public required double EntryPrice { get; init; }
        public required double CurrentPrice { get; init; }
        public required Enums.StrategyResult Result { get; init; }
        public required DateTime CreatedAt { get; init; }
    }

    /// <summary>
    /// Event args for strategy completion.
    /// </summary>
    public sealed class StrategyCompletedEventArgs : EventArgs
    {
        public required Guid StrategyId { get; init; }
        public required string Symbol { get; init; }
        public required Enums.StrategyResult Result { get; init; }
    }

    /// <summary>
    /// Event args for strategy errors.
    /// </summary>
    public sealed class StrategyErrorEventArgs : EventArgs
    {
        public required Guid StrategyId { get; init; }
        public required string Symbol { get; init; }
        public required string ErrorMessage { get; init; }
    }
}
