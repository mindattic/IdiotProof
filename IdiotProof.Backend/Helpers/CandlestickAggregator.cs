// ============================================================================
// Candlestick Aggregator - Aggregates Ticks into Time-Based Candlesticks
// ============================================================================
//
// Aggregates raw price ticks into candlesticks (OHLC bars) for indicator
// calculations. Supports time-based candlesticks (1-minute, 5-minute, etc.).
//
// USAGE:
//   var aggregator = new CandlestickAggregator(candleSizeMinutes: 1, maxCandles: 200);
//   
//   // On each tick:
//   aggregator.Update(price, volume);
//   
//   // Get completed candlesticks for indicator calculations:
//   var candles = aggregator.GetCompletedCandles();
//
// WARM-UP:
// ========
// The aggregator maintains a rolling window of completed candlesticks.
// Indicators should check IsWarmedUp before triggering conditions.
//
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;

namespace IdiotProof.Backend.Helpers
{
    /// <summary>
    /// Aggregates price ticks into candlesticks (OHLC bars).
    /// </summary>
    /// <remarks>
    /// <para><b>How It Works:</b></para>
    /// <list type="number">
    ///   <item>Receives price ticks via <see cref="Update"/>.</item>
    ///   <item>Aggregates ticks into time-based candlesticks (e.g., 1-minute).</item>
    ///   <item>Maintains a rolling window of completed candlesticks.</item>
    ///   <item>Provides candlesticks to indicator calculators.</item>
    /// </list>
    /// 
    /// <para><b>Warm-Up:</b></para>
    /// <para>Start the backend early to collect candlesticks before trading.</para>
    /// </remarks>
    public sealed class CandlestickAggregator
    {
        private readonly int _candleSizeMinutes;
        private readonly int _maxCandles;
        private readonly Queue<Candlestick> _completedCandles;

        // Current candlestick being formed
        private DateTime _currentCandleStart;
        private double _currentOpen;
        private double _currentHigh;
        private double _currentLow;
        private double _currentClose;
        private long _currentVolume;
        private int _currentTickCount;
        private bool _hasCurrentCandle;

        /// <summary>
        /// Gets the candlestick size in minutes.
        /// </summary>
        public int CandleSizeMinutes => _candleSizeMinutes;

        /// <summary>
        /// Gets the maximum number of candlesticks to retain.
        /// </summary>
        public int MaxCandles => _maxCandles;

        /// <summary>
        /// Gets the number of completed candlesticks.
        /// </summary>
        public int CompletedCandleCount => _completedCandles.Count;

        /// <summary>
        /// Gets whether the aggregator has enough candlesticks for basic indicators (21+).
        /// </summary>
        public bool IsWarmedUp => _completedCandles.Count >= 21;

        /// <summary>
        /// Gets whether the aggregator has reached maximum capacity.
        /// </summary>
        public bool IsFullyWarmedUp => _completedCandles.Count >= _maxCandles;

        /// <summary>
        /// Gets the most recent completed candlestick, or null if none.
        /// </summary>
        public Candlestick? LastCompletedCandle => _completedCandles.Count > 0 ? _completedCandles.Last() : null;

        /// <summary>
        /// Gets the current forming candlestick (not yet complete).
        /// </summary>
        public Candlestick? CurrentCandle => _hasCurrentCandle ? new Candlestick
        {
            Timestamp = _currentCandleStart,
            Open = _currentOpen,
            High = _currentHigh,
            Low = _currentLow,
            Close = _currentClose,
            Volume = _currentVolume,
            TickCount = _currentTickCount,
            IsComplete = false
        } : null;

        /// <summary>
        /// Event raised when a new candlestick completes.
        /// </summary>
        public event Action<Candlestick>? OnCandleComplete;

        /// <summary>
        /// Creates a new candlestick aggregator.
        /// </summary>
        /// <param name="candleSizeMinutes">The size of each candlestick in minutes (default: 1).</param>
        /// <param name="maxCandles">Maximum number of candlesticks to retain (default: 200 for EMA200).</param>
        public CandlestickAggregator(int candleSizeMinutes = 1, int maxCandles = 200)
        {
            if (candleSizeMinutes < 1)
                throw new ArgumentOutOfRangeException(nameof(candleSizeMinutes), "Candlestick size must be at least 1 minute.");
            if (maxCandles < 1)
                throw new ArgumentOutOfRangeException(nameof(maxCandles), "Max candlesticks must be at least 1.");

            _candleSizeMinutes = candleSizeMinutes;
            _maxCandles = maxCandles;
            _completedCandles = new Queue<Candlestick>(maxCandles + 1);
        }

        /// <summary>
        /// Updates the aggregator with a new price tick.
        /// </summary>
        /// <param name="price">The current price.</param>
        /// <param name="volume">The volume of the tick (default: 1).</param>
        /// <returns>True if a candlestick was completed, false otherwise.</returns>
        public bool Update(double price, long volume = 1)
        {
            if (price <= 0)
                return false;

            var now = DateTime.Now;
            var candleStart = GetCandleStartTime(now);

            // Check if we need to complete the current candlestick and start a new one
            if (_hasCurrentCandle && candleStart > _currentCandleStart)
            {
                CompleteCurrentCandle();
                StartNewCandle(candleStart, price, volume);
                return true;
            }

            // Update current candlestick or start first candlestick
            if (!_hasCurrentCandle)
            {
                StartNewCandle(candleStart, price, volume);
            }
            else
            {
                UpdateCurrentCandle(price, volume);
            }

            return false;
        }

        /// <summary>
        /// Gets all completed candlesticks in chronological order.
        /// </summary>
        public IReadOnlyList<Candlestick> GetCompletedCandles()
        {
            return _completedCandles.ToList();
        }

        /// <summary>
        /// Gets the N most recent completed candlesticks.
        /// </summary>
        /// <param name="count">Number of candlesticks to retrieve.</param>
        public IReadOnlyList<Candlestick> GetRecentCandles(int count)
        {
            return _completedCandles.TakeLast(Math.Min(count, _completedCandles.Count)).ToList();
        }

        /// <summary>
        /// Gets the close prices of completed candlesticks.
        /// </summary>
        public IReadOnlyList<double> GetClosePrices()
        {
            return _completedCandles.Select(c => c.Close).ToList();
        }

        /// <summary>
        /// Gets the close prices of the N most recent candlesticks.
        /// </summary>
        public IReadOnlyList<double> GetRecentClosePrices(int count)
        {
            return _completedCandles.TakeLast(Math.Min(count, _completedCandles.Count))
                .Select(c => c.Close)
                .ToList();
        }

        /// <summary>
        /// Gets the low prices of the N most recent candlesticks (most recent first).
        /// Used for Higher Lows pattern detection.
        /// </summary>
        /// <param name="count">Number of low prices to retrieve.</param>
        /// <returns>Array of low prices, most recent first.</returns>
        public double[] GetRecentLows(int count)
        {
            return _completedCandles.TakeLast(Math.Min(count, _completedCandles.Count))
                .Select(c => c.Low)
                .Reverse()  // Most recent first for pattern detection
                .ToArray();
        }

        /// <summary>
        /// Gets the high prices of the N most recent candlesticks (most recent first).
        /// Used for Lower Highs pattern detection.
        /// </summary>
        /// <param name="count">Number of high prices to retrieve.</param>
        /// <returns>Array of high prices, most recent first.</returns>
        public double[] GetRecentHighs(int count)
        {
            return _completedCandles.TakeLast(Math.Min(count, _completedCandles.Count))
                .Select(c => c.High)
                .Reverse()  // Most recent first for pattern detection
                .ToArray();
        }

        /// <summary>
        /// Resets the aggregator for a new trading session.
        /// </summary>
        public void Reset()
        {
            _completedCandles.Clear();
            _hasCurrentCandle = false;
            _currentOpen = 0;
            _currentHigh = 0;
            _currentLow = 0;
            _currentClose = 0;
            _currentVolume = 0;
            _currentTickCount = 0;
        }

        private DateTime GetCandleStartTime(DateTime time)
        {
            // Round down to the nearest candlestick boundary
            var minutes = time.Minute - (time.Minute % _candleSizeMinutes);
            return new DateTime(time.Year, time.Month, time.Day, time.Hour, minutes, 0);
        }

        private void StartNewCandle(DateTime candleStart, double price, long volume)
        {
            _currentCandleStart = candleStart;
            _currentOpen = price;
            _currentHigh = price;
            _currentLow = price;
            _currentClose = price;
            _currentVolume = volume;
            _currentTickCount = 1;
            _hasCurrentCandle = true;
        }

        private void UpdateCurrentCandle(double price, long volume)
        {
            if (price > _currentHigh)
                _currentHigh = price;
            if (price < _currentLow)
                _currentLow = price;
            _currentClose = price;
            _currentVolume += volume;
            _currentTickCount++;
        }

        private void CompleteCurrentCandle()
        {
            if (!_hasCurrentCandle)
                return;

            var completedCandle = new Candlestick
            {
                Timestamp = _currentCandleStart,
                Open = _currentOpen,
                High = _currentHigh,
                Low = _currentLow,
                Close = _currentClose,
                Volume = _currentVolume,
                TickCount = _currentTickCount,
                IsComplete = true
            };

            _completedCandles.Enqueue(completedCandle);

            // Maintain max size
            while (_completedCandles.Count > _maxCandles)
            {
                _completedCandles.Dequeue();
            }

            // Raise event
            OnCandleComplete?.Invoke(completedCandle);

            _hasCurrentCandle = false;
        }

        /// <summary>
        /// Forces completion of the current candlestick (e.g., at end of session).
        /// </summary>
        public void ForceCompleteCurrentCandle()
        {
            CompleteCurrentCandle();
        }

        /// <summary>
        /// Seeds the aggregator with historical candlesticks for indicator warm-up.
        /// This does NOT fire OnCandleComplete events - use for pre-loading only.
        /// </summary>
        /// <param name="historicalCandles">Historical candlesticks in chronological order.</param>
        /// <param name="fireEvents">Whether to fire OnCandleComplete events (default: false).</param>
        /// <remarks>
        /// <para><b>Usage:</b></para>
        /// <para>Call this method before live trading starts to warm up indicators:</para>
        /// <code>
        /// // Fetch historical bars from IBKR
        /// var historicalBars = await historicalDataService.FetchHistoricalDataAsync("AAPL", 255);
        /// 
        /// // Convert to candlesticks and seed the aggregator
        /// var candles = historicalBars.Select(bar => new Candlestick { ... });
        /// aggregator.SeedWithHistoricalData(candles);
        /// </code>
        /// </remarks>
        public void SeedWithHistoricalData(IEnumerable<Candlestick> historicalCandles, bool fireEvents = false)
        {
            foreach (var candle in historicalCandles)
            {
                // Add directly to the queue
                _completedCandles.Enqueue(candle);

                // Maintain max size
                while (_completedCandles.Count > _maxCandles)
                {
                    _completedCandles.Dequeue();
                }

                // Optionally fire events (for indicator warm-up)
                if (fireEvents)
                {
                    OnCandleComplete?.Invoke(candle);
                }
            }
        }

        /// <summary>
        /// Gets the number of bars needed to warm up for a specific EMA period.
        /// </summary>
        public static int GetWarmUpBarsNeeded(int emaPeriod) => emaPeriod + 10; // Buffer for accuracy
    }
}
