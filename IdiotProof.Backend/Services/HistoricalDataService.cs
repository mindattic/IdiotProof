// ============================================================================
// Historical Data Service - Fetches Historical Data from IBKR API
// ============================================================================
//
// IBKR HISTORICAL DATA API REFERENCE:
// ===================================
// Method: reqHistoricalData(reqId, contract, endDateTime, duration, barSize, whatToShow, useRTH, formatDate, keepUpToDate, chartOptions)
//
// BAR SIZE vs DURATION LIMITS:
// ┌────────────┬─────────────────────────────────────────────────────────────┐
// │ Bar Size   │ Max Duration                                                │
// ├────────────┼─────────────────────────────────────────────────────────────┤
// │ 1 sec      │ 30 min (1800 S)                                             │
// │ 1 min      │ 1 day (1 D)                                                 │
// │ 1 hour     │ 1 month (1 M)                                               │
// │ 1 day      │ 1 year (1 Y)                                                │
// └────────────┴─────────────────────────────────────────────────────────────┘
//
// PACING RULES:
// - Max 60 requests per 10 minutes (per connection)
// - 15+ second wait between identical requests
// - Historical Farm may be "inactive" - check IB Gateway status
//
// STRATEGY:
// =========
// For 255 1-minute bars:
// - Use duration "5 D" with bar size "1 min" (gives up to ~1950 bars for extended hours)
// - Filter to most recent 255 bars
//
// ============================================================================

using IBApi;
using IdiotProof.Backend.Enums;
using IdiotProof.Backend.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IbContract = IBApi.Contract;

namespace IdiotProof.Backend.Services
{
    /// <summary>
    /// Service for fetching historical data from IBKR API.
    /// </summary>
    /// <remarks>
    /// <para><b>Separation of Concerns:</b></para>
    /// <list type="bullet">
    ///   <item>This service FETCHES data from IBKR</item>
    ///   <item><see cref="HistoricalDataStore"/> STORES data for backtesting</item>
    ///   <item><see cref="Helpers.CandlestickAggregator"/> handles LIVE data</item>
    /// </list>
    /// </remarks>
    public sealed class HistoricalDataService : IDisposable
    {
        private readonly EClientSocket _client;
        private readonly IbWrapper _wrapper;
        private readonly HistoricalDataStore _store;

        // Request tracking
        private readonly ConcurrentDictionary<int, HistoricalDataRequest> _pendingRequests = new();
        private int _nextRequestId = 9000; // Start high to avoid conflict with order IDs
        private readonly object _requestIdLock = new();

        // Pacing control
        private readonly SemaphoreSlim _pacingSemaphore = new(5); // Max 5 concurrent requests
        private DateTime _lastRequestTime = DateTime.MinValue;
        private readonly TimeSpan _minRequestInterval = TimeSpan.FromMilliseconds(500); // 500ms between requests

        private bool _disposed;

        /// <summary>
        /// Event raised when historical data fetch completes for a symbol.
        /// </summary>
        public event Action<string, int>? OnDataFetched;

        /// <summary>
        /// Event raised when historical data fetch fails.
        /// </summary>
        public event Action<string, string>? OnFetchError;

        /// <summary>
        /// Gets the historical data store.
        /// </summary>
        public HistoricalDataStore Store => _store;

        public HistoricalDataService(EClientSocket client, IbWrapper wrapper, HistoricalDataStore store)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _wrapper = wrapper ?? throw new ArgumentNullException(nameof(wrapper));
            _store = store ?? throw new ArgumentNullException(nameof(store));

            // Subscribe to historical data callbacks
            _wrapper.OnHistoricalData += HandleHistoricalData;
            _wrapper.OnHistoricalDataEnd += HandleHistoricalDataEnd;
        }

        /// <summary>
        /// Fetches historical bars for a symbol and stores them in the data store.
        /// </summary>
        /// <param name="symbol">The ticker symbol.</param>
        /// <param name="barCount">Number of bars to fetch (default: 255 for EMA200 + buffer).</param>
        /// <param name="barSize">Bar size (default: 1 minute).</param>
        /// <param name="dataType">Data type (default: TRADES).</param>
        /// <param name="useRTH">Use regular trading hours only (default: false for extended hours).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Number of bars fetched.</returns>
        public async Task<int> FetchHistoricalDataAsync(
            string symbol,
            int barCount = 255,
            BarSize barSize = BarSize.Minutes1,
            HistoricalDataType dataType = HistoricalDataType.Trades,
            bool useRTH = false,
            CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(HistoricalDataService));

            // Create contract
            var contract = new IbContract
            {
                Symbol = symbol,
                SecType = "STK",
                Currency = "USD",
                Exchange = "SMART"
            };

            // Calculate duration based on bar count and size
            // For 1-min bars, we need more duration to account for market closed hours
            string duration = CalculateDuration(barCount, barSize);

            // Pacing control
            await _pacingSemaphore.WaitAsync(cancellationToken);
            try
            {
                // Ensure minimum interval between requests
                var timeSinceLastRequest = DateTime.Now - _lastRequestTime;
                if (timeSinceLastRequest < _minRequestInterval)
                {
                    await Task.Delay(_minRequestInterval - timeSinceLastRequest, cancellationToken);
                }

                // Create request tracking
                int reqId = GetNextRequestId();
                var request = new HistoricalDataRequest
                {
                    RequestId = reqId,
                    Symbol = symbol,
                    BarCount = barCount,
                    CompletionSource = new TaskCompletionSource<int>()
                };

                _pendingRequests[reqId] = request;

                // Register cancellation
                cancellationToken.Register(() =>
                {
                    if (_pendingRequests.TryRemove(reqId, out var req))
                    {
                        req.CompletionSource.TrySetCanceled(cancellationToken);
                    }
                });

                // Format end date/time (empty string = now)
                string endDateTime = ""; // Current time

                Log($"[{symbol}] Requesting {barCount} bars (duration: {duration}, barSize: {barSize.ToIbString()})...");

                // Make the request
                _client.reqHistoricalData(
                    reqId,
                    contract,
                    endDateTime,
                    duration,
                    barSize.ToIbString(),
                    dataType.ToIbString(),
                    useRTH ? 1 : 0,
                    1, // Format date as yyyyMMdd HH:mm:ss
                    false, // keepUpToDate - false for one-time fetch
                    null // chartOptions
                );

                _lastRequestTime = DateTime.Now;

                // Wait for completion with timeout
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

                try
                {
                    return await request.CompletionSource.Task.WaitAsync(linkedCts.Token);
                }
                catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
                {
                    _pendingRequests.TryRemove(reqId, out _);
                    OnFetchError?.Invoke(symbol, "Request timed out");
                    throw new TimeoutException($"Historical data request for {symbol} timed out.");
                }
            }
            finally
            {
                _pacingSemaphore.Release();
            }
        }

        /// <summary>
        /// Fetches historical data for multiple symbols concurrently.
        /// </summary>
        /// <param name="symbols">List of symbols to fetch.</param>
        /// <param name="barCount">Number of bars per symbol.</param>
        /// <param name="maxConcurrency">Maximum concurrent requests (default: 3 to respect pacing).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Dictionary of symbol to bar count fetched.</returns>
        public async Task<Dictionary<string, int>> FetchMultipleAsync(
            IEnumerable<string> symbols,
            int barCount = 255,
            int maxConcurrency = 3,
            CancellationToken cancellationToken = default)
        {
            var results = new ConcurrentDictionary<string, int>();
            var semaphore = new SemaphoreSlim(maxConcurrency);

            var tasks = symbols.Select(async symbol =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    var count = await FetchHistoricalDataAsync(symbol, barCount, cancellationToken: cancellationToken);
                    results[symbol] = count;
                }
                catch (Exception ex)
                {
                    Log($"[{symbol}] ERROR: {ex.Message}");
                    results[symbol] = 0;
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);
            return results.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }

        /// <summary>
        /// Called by IbWrapper when historical data bars are received.
        /// </summary>
        private void HandleHistoricalData(int reqId, Bar bar)
        {
            if (!_pendingRequests.TryGetValue(reqId, out var request))
                return;

            // Parse the bar time - IBKR returns various formats:
            // - "yyyyMMdd" (daily bars)
            // - "yyyyMMdd  HH:mm:ss" (intraday bars, note double space)
            // - "yyyyMMdd HH:mm:ss US/Eastern" (intraday with timezone)
            DateTime barTime;
            string timeStr = bar.Time;

            // Strip timezone suffix if present (e.g., " US/Eastern", " America/New_York")
            int tzIndex = timeStr.LastIndexOf(" US/", StringComparison.Ordinal);
            if (tzIndex == -1)
                tzIndex = timeStr.LastIndexOf(" America/", StringComparison.Ordinal);
            if (tzIndex > 0)
                timeStr = timeStr[..tzIndex];

            if (timeStr.Contains(' '))
            {
                // Format: "yyyyMMdd  HH:mm:ss" or "yyyyMMdd HH:mm:ss"
                // Handle both single and double space between date and time
                timeStr = timeStr.Replace("  ", " ");
                barTime = DateTime.ParseExact(timeStr, "yyyyMMdd HH:mm:ss", null);
            }
            else
            {
                // Format: "yyyyMMdd" (daily bars)
                barTime = DateTime.ParseExact(timeStr, "yyyyMMdd", null);
            }

            var historicalBar = new HistoricalBar
            {
                Time = barTime,
                Open = bar.Open,
                High = bar.High,
                Low = bar.Low,
                Close = bar.Close,
                Volume = (long)bar.Volume,
                TradeCount = bar.Count
            };

            request.Bars.Add(historicalBar);
        }

        /// <summary>
        /// Called by IbWrapper when historical data request completes.
        /// </summary>
        private void HandleHistoricalDataEnd(int reqId, string start, string end)
        {
            if (!_pendingRequests.TryRemove(reqId, out var request))
                return;

            // Sort bars by time and take the requested count (most recent)
            var sortedBars = request.Bars
                .OrderBy(b => b.Time)
                .TakeLast(request.BarCount)
                .ToList();

            // Store in the data store
            _store.SetHistoricalData(request.Symbol, sortedBars);

            Log($"[{request.Symbol}] Received {sortedBars.Count} bars (range: {start} to {end})");

            // Complete the task
            request.CompletionSource.TrySetResult(sortedBars.Count);
            OnDataFetched?.Invoke(request.Symbol, sortedBars.Count);
        }

        /// <summary>
        /// Calculates the duration string needed to fetch the desired number of bars.
        /// </summary>
        private static string CalculateDuration(int barCount, BarSize barSize)
        {
            // Add 50% buffer for market closed hours
            int bufferedCount = (int)(barCount * 1.5);

            return barSize switch
            {
                BarSize.Seconds1 => $"{Math.Min(bufferedCount, 1800)} S",
                BarSize.Minutes1 => bufferedCount <= 390 ? "1 D" : bufferedCount <= 780 ? "2 D" : "5 D",
                BarSize.Minutes5 => bufferedCount <= 78 ? "1 D" : "1 W",
                BarSize.Minutes15 => bufferedCount <= 26 ? "1 D" : "2 W",
                BarSize.Hours1 => bufferedCount <= 6 ? "1 D" : "1 M",
                BarSize.Days1 => bufferedCount <= 365 ? "1 Y" : "1 Y",
                _ => "5 D"
            };
        }

        private int GetNextRequestId()
        {
            lock (_requestIdLock)
            {
                return _nextRequestId++;
            }
        }

        private static void Log(string message)
        {
            var easternZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
            var easternTime = TimeZoneInfo.ConvertTime(DateTime.Now, easternZone);
            var timestamp = easternTime.ToString("hh:mm:ss tt");
            Console.WriteLine($"[{timestamp}] [HIST] {message}");
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            _wrapper.OnHistoricalData -= HandleHistoricalData;
            _wrapper.OnHistoricalDataEnd -= HandleHistoricalDataEnd;

            _pacingSemaphore.Dispose();

            // Cancel any pending requests
            foreach (var request in _pendingRequests.Values)
            {
                request.CompletionSource.TrySetCanceled();
            }
            _pendingRequests.Clear();
        }

        /// <summary>
        /// Internal class to track pending historical data requests.
        /// </summary>
        private sealed class HistoricalDataRequest
        {
            public int RequestId { get; init; }
            public string Symbol { get; init; } = "";
            public int BarCount { get; init; }
            public List<HistoricalBar> Bars { get; } = [];
            public TaskCompletionSource<int> CompletionSource { get; init; } = null!;
        }
    }
}
