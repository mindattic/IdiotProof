// ============================================================================
// Historical Data Store - Stores Historical Candlesticks Separately from Live
// ============================================================================
//
// DESIGN PHILOSOPHY:
// ==================
// Historical data is SEPARATE from live data to enable future backtesting:
//   - Historical: Fetched from IBKR reqHistoricalData() - immutable, for backtesting
//   - Live: Aggregated from tick data in CandlestickAggregator - mutable, real-time
//
// STORAGE STRUCTURE:
// ==================
// Historical data is stored per symbol with metadata:
//   - Symbol -> HistoricalDataSet (bars, last fetch time, date range)
//
// FUTURE BACKTESTING:
// ===================
// This store is designed to support backtesting between startDate and endDate:
//   var bars = _store.GetBarsInRange("AAPL", startDate, endDate);
//
// ============================================================================

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using IdiotProof.Models;

namespace IdiotProof.Services {
    /// <summary>
    /// Represents a set of historical data for a single symbol.
    /// </summary>
    public sealed class HistoricalDataSet
    {
        /// <summary>Symbol ticker (e.g., "AAPL").</summary>
        public required string Symbol { get; init; }

        /// <summary>When the data was fetched from IBKR.</summary>
        public DateTime FetchedAtUtc { get; init; } = DateTime.UtcNow;

        /// <summary>Earliest bar timestamp in the set.</summary>
        public DateTime? StartDate => Bars.Count > 0 ? Bars[0].Time : null;

        /// <summary>Latest bar timestamp in the set.</summary>
        public DateTime? EndDate => Bars.Count > 0 ? Bars[^1].Time : null;

        /// <summary>Historical bars in chronological order.</summary>
        public List<HistoricalBar> Bars { get; init; } = [];

        /// <summary>Number of bars in the set.</summary>
        public int BarCount => Bars.Count;

        /// <summary>Whether the data is warm (has enough bars for indicators).</summary>
        public bool IsWarmedUp => Bars.Count >= 21;

        /// <summary>Whether the data is fully warm (has 200+ bars for EMA200).</summary>
        public bool IsFullyWarmedUp => Bars.Count >= 200;
    }

    /// <summary>
    /// Thread-safe store for historical candlestick data.
    /// Keeps historical data separate from live data for future backtesting.
    /// </summary>
    /// <remarks>
    /// <para><b>Usage:</b></para>
    /// <code>
    /// // Store historical data after fetch
    /// _store.SetHistoricalData("AAPL", bars);
    /// 
    /// // Retrieve for indicators
    /// var dataset = _store.GetHistoricalData("AAPL");
    /// 
    /// // For future backtesting
    /// var backtestBars = _store.GetBarsInRange("AAPL", startDate, endDate);
    /// </code>
    /// </remarks>
    public sealed class HistoricalDataStore
    {
        private readonly ConcurrentDictionary<string, HistoricalDataSet> _data = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Gets all symbols that have historical data loaded.
        /// </summary>
        public IEnumerable<string> Symbols => _data.Keys;

        /// <summary>
        /// Gets the total number of symbols with historical data.
        /// </summary>
        public int SymbolCount => _data.Count;

        /// <summary>
        /// Sets (replaces) historical data for a symbol.
        /// </summary>
        /// <param name="symbol">The ticker symbol.</param>
        /// <param name="bars">The historical bars to store.</param>
        public void SetHistoricalData(string symbol, IEnumerable<HistoricalBar> bars)
        {
            var barList = bars.OrderBy(b => b.Time).ToList();

            _data[symbol] = new HistoricalDataSet
            {
                Symbol = symbol,
                FetchedAtUtc = DateTime.UtcNow,
                Bars = barList
            };
        }

        /// <summary>
        /// Appends a new bar to the existing historical data for a symbol.
        /// Used for recording new candles going forward.
        /// </summary>
        /// <param name="symbol">The ticker symbol.</param>
        /// <param name="bar">The new bar to append.</param>
        /// <param name="maxBars">Maximum bars to retain (default: 500).</param>
        /// <returns>True if bar was appended, false if symbol has no existing data.</returns>
        public bool AppendBar(string symbol, HistoricalBar bar, int maxBars = 500)
        {
            if (!_data.TryGetValue(symbol, out var dataset))
                return false;

            // Don't append if bar already exists (duplicate)
            if (dataset.Bars.Count > 0 && bar.Time <= dataset.Bars[^1].Time)
                return false;

            dataset.Bars.Add(bar);

            // Trim oldest bars if over max
            while (dataset.Bars.Count > maxBars)
            {
                dataset.Bars.RemoveAt(0);
            }

            return true;
        }

        /// <summary>
        /// Gets the historical data set for a symbol.
        /// </summary>
        /// <param name="symbol">The ticker symbol.</param>
        /// <returns>The data set, or null if no data exists.</returns>
        public HistoricalDataSet? GetHistoricalData(string symbol)
        {
            return _data.TryGetValue(symbol, out var dataset) ? dataset : null;
        }

        /// <summary>
        /// Gets the historical bars for a symbol.
        /// </summary>
        /// <param name="symbol">The ticker symbol.</param>
        /// <returns>List of bars, or empty list if no data.</returns>
        public IReadOnlyList<HistoricalBar> GetBars(string symbol)
        {
            return _data.TryGetValue(symbol, out var dataset) ? dataset.Bars : [];
        }

        /// <summary>
        /// Gets the most recent N bars for a symbol.
        /// </summary>
        /// <param name="symbol">The ticker symbol.</param>
        /// <param name="count">Number of bars to retrieve.</param>
        /// <returns>List of bars, or empty list if no data.</returns>
        public IReadOnlyList<HistoricalBar> GetRecentBars(string symbol, int count)
        {
            if (!_data.TryGetValue(symbol, out var dataset))
                return [];

            return dataset.Bars.TakeLast(Math.Min(count, dataset.Bars.Count)).ToList();
        }

        /// <summary>
        /// Gets bars within a date range for backtesting.
        /// </summary>
        /// <param name="symbol">The ticker symbol.</param>
        /// <param name="startDate">Start of the date range (inclusive).</param>
        /// <param name="endDate">End of the date range (inclusive).</param>
        /// <returns>List of bars in the range, or empty list if no data.</returns>
        public IReadOnlyList<HistoricalBar> GetBarsInRange(string symbol, DateTime startDate, DateTime endDate)
        {
            if (!_data.TryGetValue(symbol, out var dataset))
                return [];

            return dataset.Bars
                .Where(b => b.Time >= startDate && b.Time <= endDate)
                .ToList();
        }

        /// <summary>
        /// Gets the close prices for a symbol.
        /// </summary>
        /// <param name="symbol">The ticker symbol.</param>
        /// <returns>List of close prices in chronological order.</returns>
        public IReadOnlyList<double> GetClosePrices(string symbol)
        {
            if (!_data.TryGetValue(symbol, out var dataset))
                return [];

            return dataset.Bars.Select(b => b.Close).ToList();
        }

        /// <summary>
        /// Gets the most recent close prices for a symbol.
        /// </summary>
        /// <param name="symbol">The ticker symbol.</param>
        /// <param name="count">Number of prices to retrieve.</param>
        /// <returns>List of close prices.</returns>
        public IReadOnlyList<double> GetRecentClosePrices(string symbol, int count)
        {
            if (!_data.TryGetValue(symbol, out var dataset))
                return [];

            return dataset.Bars.TakeLast(Math.Min(count, dataset.Bars.Count))
                .Select(b => b.Close)
                .ToList();
        }

        /// <summary>
        /// Gets the last bar for a symbol.
        /// </summary>
        /// <param name="symbol">The ticker symbol.</param>
        /// <returns>The last bar, or null if no data.</returns>
        public HistoricalBar? GetLastBar(string symbol)
        {
            if (!_data.TryGetValue(symbol, out var dataset) || dataset.Bars.Count == 0)
                return null;

            return dataset.Bars[^1];
        }

        /// <summary>
        /// Checks if a symbol has historical data loaded.
        /// </summary>
        public bool HasData(string symbol)
        {
            return _data.ContainsKey(symbol);
        }

        /// <summary>
        /// Checks if a symbol has enough data for indicators (21+ bars).
        /// </summary>
        public bool IsWarmedUp(string symbol)
        {
            return _data.TryGetValue(symbol, out var dataset) && dataset.IsWarmedUp;
        }

        /// <summary>
        /// Checks if a symbol has enough data for EMA200 (200+ bars).
        /// </summary>
        public bool IsFullyWarmedUp(string symbol)
        {
            return _data.TryGetValue(symbol, out var dataset) && dataset.IsFullyWarmedUp;
        }

        /// <summary>
        /// Removes historical data for a symbol.
        /// </summary>
        public bool Remove(string symbol)
        {
            return _data.TryRemove(symbol, out _);
        }

        /// <summary>
        /// Clears all historical data.
        /// </summary>
        public void Clear()
        {
            _data.Clear();
        }

        /// <summary>
        /// Gets a summary of all stored data.
        /// </summary>
        public IReadOnlyDictionary<string, (int BarCount, DateTime? StartDate, DateTime? EndDate)> GetSummary()
        {
            return _data.ToDictionary(
                kvp => kvp.Key,
                kvp => (kvp.Value.BarCount, kvp.Value.StartDate, kvp.Value.EndDate)
            );
        }
    }
}


