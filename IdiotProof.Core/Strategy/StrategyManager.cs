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
using IdiotProof.Core.Models;
using IdiotProof.Helpers;
using IdiotProof.Logging;
using IdiotProof.Models;
using IdiotProof.Strategy;
using System.Collections.Concurrent;

namespace IdiotProof.Strategy {
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

        private readonly IbWrapper wrapper;
        private readonly EClientSocket client;
        private readonly ConcurrentDictionary<Guid, StrategyRunnerInfo> runners = new();
        private readonly ConcurrentDictionary<string, Contract> contracts = new();
        private readonly SemaphoreSlim loadLock = new(1, 1);
        private readonly object stateLock = new();

        private volatile bool disposed;
        private volatile bool isRunning;
        private int nextTickerId = 2000; // Start high to avoid conflicts with other subscriptions

        /// <summary>
        /// Logs a message to both console and session log file.
        /// </summary>
        private static void Log(string message)
        {
            ConsoleLog.Manager(message);
            SessionLogger?.LogEvent("MANAGER", message);
        }

        /// <summary>
        /// Gets whether the manager is currently running strategies.
        /// </summary>
        public bool IsRunning => isRunning;

        /// <summary>
        /// Gets the count of active strategy runners.
        /// </summary>
        public int ActiveCount => runners.Count(r => !r.Value.Runner.IsComplete);

        /// <summary>
        /// Gets the total count of strategy runners.
        /// </summary>
        public int TotalCount => runners.Count;

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
            wrapper = wrapper ?? throw new ArgumentNullException(nameof(wrapper));
            client = client ?? throw new ArgumentNullException(nameof(client));
        }

        /// <summary>
        /// Loads strategies from a list of TradingStrategy objects.
        /// </summary>
        /// <param name="strategies">The strategies to load.</param>
        /// <returns>The number of strategies loaded.</returns>
        public async Task<int> LoadStrategiesAsync(IEnumerable<TradingStrategy> strategies)
        {
            await loadLock.WaitAsync();
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
                loadLock.Release();
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
            var runner = new StrategyRunner(strategy, contract, wrapper, client);

            // Allocate ticker ID and register handler
            int tickerId = Interlocked.Increment(ref nextTickerId);

            var info = new StrategyRunnerInfo
            {
                Id = strategyId,
                Runner = runner,
                Contract = contract,
                TickerId = tickerId,
                CreatedAt = DateTime.UtcNow
            };

            if (!runners.TryAdd(strategyId, info))
            {
                runner.Dispose();
                throw new InvalidOperationException($"Strategy with ID {strategyId} already exists.");
            }

            // Register market data handler
            wrapper.RegisterTickerHandler(tickerId, runner.OnLastTrade);

            // If already running, subscribe to market data immediately
            if (isRunning)
            {
                await Task.Run(() => client.reqMktData(tickerId, contract, "", false, false, null));
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

            if (runners.TryRemove(id, out var info))
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

            lock (stateLock)
            {
                if (isRunning)
                    return;
                isRunning = true;
            }

            Log("Starting all strategies...");

            // Subscribe to market data for all strategies
            await Task.Run(() =>
            {
                foreach (var kvp in runners)
                {
                    var info = kvp.Value;
                    client.reqMktData(info.TickerId, info.Contract, "", false, false, null);
                }
            });

            Log($"Started {runners.Count} strategies");
        }

        /// <summary>
        /// Stops all strategies and cancels market data subscriptions.
        /// </summary>
        public async Task StopAllAsync()
        {
            lock (stateLock)
            {
                if (!isRunning)
                    return;
                isRunning = false;
            }

            Log("Stopping all strategies...");

            // Cancel market data subscriptions
            await Task.Run(() =>
            {
                foreach (var kvp in runners)
                {
                    var info = kvp.Value;
                    try
                    {
                        client.cancelMktData(info.TickerId);
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
        /// Reloads strategies from IdiotScript files, replacing current strategies.
        /// </summary>
        public async Task ReloadStrategiesAsync()
        {
            await loadLock.WaitAsync();
            try
            {
                ThrowIfDisposed();

                Log("Reloading strategies...");

                // Stop current strategies
                bool wasRunning = isRunning;
                if (wasRunning)
                    await StopAllAsync();

                // Clear existing runners
                foreach (var kvp in runners.ToArray())
                {
                    if (runners.TryRemove(kvp.Key, out var info))
                    {
                        await CleanupRunnerAsync(info);
                    }
                }

                // Load new strategies from .idiot files
                // TODO: IdiotScript and StrategyLoader removed - strategies now come from WatchlistManager
                var strategies = new List<TradingStrategy>();
                await LoadStrategiesAsync(strategies);

                // Restart if was running
                if (wasRunning)
                    await StartAllAsync();

                Log("Reload complete");
            }
            finally
            {
                loadLock.Release();
            }
        }

        /// <summary>
        /// Gets the status of all strategies.
        /// </summary>
        /// <returns>List of strategy status information.</returns>
        public IReadOnlyList<StrategyStatusInfo> GetAllStatus()
        {
            return runners.Values
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
            if (runners.TryGetValue(id, out var info))
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
            return contracts.GetOrAdd(strategy.Symbol, _ => new Contract
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
            // TODO: StrategyLoader removed - this method is deprecated
            // Autonomous trading no longer uses StrategyDefinition conversion
            return null;
        }

        private async Task CleanupRunnerAsync(StrategyRunnerInfo info)
        {
            try
            {
                // Cancel market data subscription
                client.cancelMktData(info.TickerId);
            }
            catch
            {
                // Ignore errors
            }

            // Unregister ticker handler
            wrapper.UnregisterTickerHandler(info.TickerId);

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
            ObjectDisposedException.ThrowIf(disposed, this);
        }

        public void Dispose()
        {
            DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        public async ValueTask DisposeAsync()
        {
            if (disposed)
                return;

            disposed = true;

            // Stop all strategies
            await StopAllAsync();

            // Cleanup all runners
            foreach (var kvp in runners.ToArray())
            {
                if (runners.TryRemove(kvp.Key, out var info))
                {
                    await CleanupRunnerAsync(info);
                }
            }

            loadLock.Dispose();

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


