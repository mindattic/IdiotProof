// ============================================================================
// Candlestick Aggregator - Aggregates Ticks into Time-Based Candlesticks
// ============================================================================
//
// Aggregates raw price ticks into candlesticks (OHLC bars) for indicator
// calculations. Supports time-based candlesticks (1-minute, 5-minute, etc.).
//
// USAGE:
//   var aggregator = new CandlestickAggregatorHelper(candleSizeMinutes: 1, maxCandles: 200);
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
using IdiotProof.Core.Models;

namespace IdiotProof.Helpers {
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
    public sealed class CandlestickAggregatorHelper
    {
        private readonly int candleSizeMinutes;
        private readonly int maxCandles;
        private readonly Queue<Candlestick> completedCandles;

        // Current candlestick being formed
        private DateTime currentCandleStart;
        private double currentOpen;
        private double currentHigh;
        private double currentLow;
        private double currentClose;
        private long currentVolume;
        private int currentTickCount;
        private bool hasCurrentCandle;

        /// <summary>
        /// Gets the candlestick size in minutes.
        /// </summary>
        public int CandleSizeMinutes => candleSizeMinutes;

        /// <summary>
        /// Gets the maximum number of candlesticks to retain.
        /// </summary>
        public int MaxCandles => maxCandles;

        /// <summary>
        /// Gets the number of completed candlesticks.
        /// </summary>
        public int CompletedCandleCount => completedCandles.Count;

        /// <summary>
        /// Gets whether the aggregator has enough candlesticks for basic indicators (21+).
        /// </summary>
        public bool IsWarmedUp => completedCandles.Count >= 21;

        /// <summary>
        /// Gets whether the aggregator has reached maximum capacity.
        /// </summary>
        public bool IsFullyWarmedUp => completedCandles.Count >= maxCandles;

        /// <summary>
        /// Gets the most recent completed candlestick, or null if none.
        /// </summary>
        public Candlestick? LastCompletedCandle => completedCandles.Count > 0 ? completedCandles.Last() : null;

        /// <summary>
        /// Gets the current forming candlestick (not yet complete).
        /// </summary>
        public Candlestick? CurrentCandle => hasCurrentCandle ? new Candlestick
        {
            Timestamp = currentCandleStart,
            Open = currentOpen,
            High = currentHigh,
            Low = currentLow,
            Close = currentClose,
            Volume = currentVolume,
            TickCount = currentTickCount,
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
        public CandlestickAggregatorHelper(int candleSizeMinutes = 1, int maxCandles = 200)
        {
            if (candleSizeMinutes < 1)
                throw new ArgumentOutOfRangeException(nameof(candleSizeMinutes), "Candlestick size must be at least 1 minute.");
            if (maxCandles < 1)
                throw new ArgumentOutOfRangeException(nameof(maxCandles), "Max candlesticks must be at least 1.");

            this.candleSizeMinutes = candleSizeMinutes;
            this.maxCandles = maxCandles;
            completedCandles = new Queue<Candlestick>(maxCandles + 1);
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
            if (hasCurrentCandle && candleStart > currentCandleStart)
            {
                CompleteCurrentCandle();
                StartNewCandle(candleStart, price, volume);
                return true;
            }

            // Update current candlestick or start first candlestick
            if (!hasCurrentCandle)
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
            return completedCandles.ToList();
        }

        /// <summary>
        /// Gets the N most recent completed candlesticks.
        /// </summary>
        /// <param name="count">Number of candlesticks to retrieve.</param>
        public IReadOnlyList<Candlestick> GetRecentCandles(int count)
        {
            return completedCandles.TakeLast(Math.Min(count, completedCandles.Count)).ToList();
        }

        /// <summary>
        /// Gets the close prices of completed candlesticks.
        /// </summary>
        public IReadOnlyList<double> GetClosePrices()
        {
            return completedCandles.Select(c => c.Close).ToList();
        }

        /// <summary>
        /// Gets the close prices of the N most recent candlesticks.
        /// </summary>
        public IReadOnlyList<double> GetRecentClosePrices(int count)
        {
            return completedCandles.TakeLast(Math.Min(count, completedCandles.Count))
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
            return completedCandles.TakeLast(Math.Min(count, completedCandles.Count))
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
            return completedCandles.TakeLast(Math.Min(count, completedCandles.Count))
                .Select(c => c.High)
                .Reverse()  // Most recent first for pattern detection
                .ToArray();
        }

        /// <summary>
        /// Resets the aggregator for a new trading session.
        /// </summary>
        public void Reset()
        {
            completedCandles.Clear();
            hasCurrentCandle = false;
            currentOpen = 0;
            currentHigh = 0;
            currentLow = 0;
            currentClose = 0;
            currentVolume = 0;
            currentTickCount = 0;
        }

        private DateTime GetCandleStartTime(DateTime time)
        {
            // Round down to the nearest candlestick boundary
            var minutes = time.Minute - (time.Minute % candleSizeMinutes);
            return new DateTime(time.Year, time.Month, time.Day, time.Hour, minutes, 0);
        }

        private void StartNewCandle(DateTime candleStart, double price, long volume)
        {
            currentCandleStart = candleStart;
            currentOpen = price;
            currentHigh = price;
            currentLow = price;
            currentClose = price;
            currentVolume = volume;
            currentTickCount = 1;
            hasCurrentCandle = true;
        }

        private void UpdateCurrentCandle(double price, long volume)
        {
            if (price > currentHigh)
                currentHigh = price;
            if (price < currentLow)
                currentLow = price;
            currentClose = price;
            currentVolume += volume;
            currentTickCount++;
        }

        private void CompleteCurrentCandle()
        {
            if (!hasCurrentCandle)
                return;

            var completedCandle = new Candlestick
            {
                Timestamp = currentCandleStart,
                Open = currentOpen,
                High = currentHigh,
                Low = currentLow,
                Close = currentClose,
                Volume = currentVolume,
                TickCount = currentTickCount,
                IsComplete = true
            };

            completedCandles.Enqueue(completedCandle);

            // Maintain max size
            while (completedCandles.Count > maxCandles)
            {
                completedCandles.Dequeue();
            }

            // Raise event
            OnCandleComplete?.Invoke(completedCandle);

            hasCurrentCandle = false;
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
                completedCandles.Enqueue(candle);

                // Maintain max size
                while (completedCandles.Count > maxCandles)
                {
                    completedCandles.Dequeue();
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

        // ========================================================================
        // OPENING BELL PATTERN DETECTION
        // ========================================================================
        // 
        // Patterns around the RTH open (9:30 AM ET):
        // 
        // 1. FIRST CANDLE TRAP: The 9:30-9:31 candle is extremely volatile.
        //    Even if it goes way up, chances of closing profitable are low.
        //    AVOID TRADING the first RTH candle.
        // 
        // 2. PREMARKET GREEN RUSH WARNING: If a stock shows lots of green candles
        //    in the last 5 minutes of premarket (9:25-9:30), it's likely to crash
        //    after RTH opens. EXIT any long position before the bell.
        // 
        // 3. CLEAN ROCKET PATTERN: If there are no warning signs at end of
        //    premarket AND the stock rockets up after open, BUY for the move
        //    to HOD, then SHORT the fade.
        // ========================================================================

        /// <summary>
        /// Gets candlesticks from the last N minutes of premarket (before 9:30 AM ET).
        /// </summary>
        /// <param name="minutes">Number of minutes to look back from 9:30 AM (default: 5).</param>
        /// <returns>Premarket candles in the specified window.</returns>
        public IReadOnlyList<Candlestick> GetPremarketEndCandles(int minutes = 5)
        {
            var rthOpen = new TimeOnly(9, 30);
            var cutoffTime = rthOpen.AddMinutes(-minutes);
            
            return completedCandles
                .Where(c => {
                    var time = TimeOnly.FromDateTime(c.Timestamp);
                    return time >= cutoffTime && time < rthOpen;
                })
                .ToList();
        }

        /// <summary>
        /// Detects "Green Rush" warning pattern: multiple consecutive green candles
        /// in the last 5 minutes of premarket, signaling a likely crash after RTH open.
        /// </summary>
        /// <param name="minGreenCandles">Minimum green candles to trigger warning (default: 3).</param>
        /// <returns>True if green rush warning is detected.</returns>
        /// <remarks>
        /// When detected: EXIT any long position before RTH bell (9:30 AM).
        /// </remarks>
        public bool HasPremarketGreenRushWarning(int minGreenCandles = 3)
        {
            var premarketCandles = GetPremarketEndCandles(5);
            if (premarketCandles.Count < minGreenCandles)
                return false;

            // Count consecutive green candles at the end of premarket
            int consecutiveGreen = 0;
            foreach (var candle in premarketCandles.Reverse())
            {
                if (candle.IsBullish)
                    consecutiveGreen++;
                else
                    break; // Streak broken
            }

            return consecutiveGreen >= minGreenCandles;
        }

        /// <summary>
        /// Gets the percentage gain in the last 5 minutes of premarket.
        /// High gains (>2%) with green candles are a strong crash warning.
        /// </summary>
        public double GetPremarketEndGainPercent()
        {
            var premarketCandles = GetPremarketEndCandles(5);
            if (premarketCandles.Count < 2)
                return 0;

            double firstPrice = premarketCandles[0].Open;
            double lastPrice = premarketCandles[^1].Close;
            
            if (firstPrice <= 0)
                return 0;

            return ((lastPrice - firstPrice) / firstPrice) * 100;
        }

        /// <summary>
        /// Checks if we're currently in the volatile first RTH candle (9:30-9:31 AM ET).
        /// This candle is often a trap - avoid trading it.
        /// </summary>
        public bool IsFirstRthCandle()
        {
            var now = TimeOnly.FromDateTime(DateTime.Now);
            return now >= new TimeOnly(9, 30) && now < new TimeOnly(9, 31);
        }

        /// <summary>
        /// Checks if we're in the initial RTH volatility window (first 2 minutes).
        /// Reduced confidence during 9:30-9:32 AM.
        /// </summary>
        public bool IsRthVolatilityWindow()
        {
            var now = TimeOnly.FromDateTime(DateTime.Now);
            return now >= new TimeOnly(9, 30) && now < new TimeOnly(9, 32);
        }

        /// <summary>
        /// Detects "Clean Premarket" pattern: no green rush warning AND price
        /// is above VWAP, indicating potential for clean breakout after RTH open.
        /// </summary>
        /// <param name="currentPrice">Current price.</param>
        /// <param name="vwap">Current VWAP.</param>
        /// <returns>True if premarket is clean (no warning signs).</returns>
        public bool HasCleanPremarket(double currentPrice, double vwap)
        {
            // No green rush warning
            if (HasPremarketGreenRushWarning())
                return false;

            // Price should be above VWAP (not extended below)
            if (currentPrice < vwap * 0.98)
                return false;

            // Premarket gain should be moderate (<2%), not overextended
            double premarketGain = GetPremarketEndGainPercent();
            if (premarketGain > 2.0)
                return false;

            return true;
        }

        /// <summary>
        /// Gets an Opening Bell analysis summary for trading decisions.
        /// </summary>
        public OpeningBellAnalysis GetOpeningBellAnalysis(double currentPrice, double vwap)
        {
            var now = TimeOnly.FromDateTime(DateTime.Now);
            var premarketCandles = GetPremarketEndCandles(5);
            int greenCount = premarketCandles.Count(c => c.IsBullish);
            int redCount = premarketCandles.Count(c => c.IsBearish);

            return new OpeningBellAnalysis
            {
                IsFirstRthCandle = IsFirstRthCandle(),
                IsRthVolatilityWindow = IsRthVolatilityWindow(),
                HasGreenRushWarning = HasPremarketGreenRushWarning(),
                PremarketGainPercent = GetPremarketEndGainPercent(),
                PremarketCandleCount = premarketCandles.Count,
                PremarketGreenCandles = greenCount,
                PremarketRedCandles = redCount,
                HasCleanPremarket = HasCleanPremarket(currentPrice, vwap),
                IsAboveVwap = currentPrice > vwap,
                Recommendation = GetOpeningBellRecommendation(currentPrice, vwap, now)
            };
        }

        private OpeningBellAction GetOpeningBellRecommendation(double currentPrice, double vwap, TimeOnly now)
        {
            // During premarket (before 9:30)
            if (now < new TimeOnly(9, 30))
            {
                if (HasPremarketGreenRushWarning())
                    return OpeningBellAction.ExitBeforeBell; // Green rush = likely crash
                
                return OpeningBellAction.HoldAndWatch;
            }

            // First RTH candle (9:30-9:31) - avoid trading
            if (IsFirstRthCandle())
                return OpeningBellAction.AvoidFirstCandle;

            // RTH volatility window (9:30-9:32) - reduced confidence
            if (IsRthVolatilityWindow())
                return OpeningBellAction.ReducedConfidence;

            // After volatility window - check for clean rocket
            if (HasCleanPremarket(currentPrice, vwap) && currentPrice > vwap)
                return OpeningBellAction.CleanRocketBuy;

            return OpeningBellAction.NormalTrading;
        }
    }

    /// <summary>
    /// Analysis of opening bell patterns for trading decisions.
    /// </summary>
    public sealed class OpeningBellAnalysis
    {
        /// <summary>Whether we're in the first RTH candle (9:30-9:31).</summary>
        public bool IsFirstRthCandle { get; init; }

        /// <summary>Whether we're in the RTH volatility window (9:30-9:32).</summary>
        public bool IsRthVolatilityWindow { get; init; }

        /// <summary>Whether green rush warning is detected (exit signal).</summary>
        public bool HasGreenRushWarning { get; init; }

        /// <summary>Percentage gain in last 5 minutes of premarket.</summary>
        public double PremarketGainPercent { get; init; }

        /// <summary>Number of candles in premarket analysis window.</summary>
        public int PremarketCandleCount { get; init; }

        /// <summary>Number of green (bullish) premarket candles.</summary>
        public int PremarketGreenCandles { get; init; }

        /// <summary>Number of red (bearish) premarket candles.</summary>
        public int PremarketRedCandles { get; init; }

        /// <summary>Whether premarket is clean (no warning signs).</summary>
        public bool HasCleanPremarket { get; init; }

        /// <summary>Whether price is above VWAP.</summary>
        public bool IsAboveVwap { get; init; }

        /// <summary>Recommended action based on analysis.</summary>
        public OpeningBellAction Recommendation { get; init; }

        /// <summary>Whether it's safe to enter a new position.</summary>
        public bool IsSafeToEnter => Recommendation == OpeningBellAction.NormalTrading 
                                   || Recommendation == OpeningBellAction.CleanRocketBuy;

        /// <summary>Whether we should exit existing positions.</summary>
        public bool ShouldExit => Recommendation == OpeningBellAction.ExitBeforeBell;

        public override string ToString()
        {
            var parts = new List<string>();
            if (HasGreenRushWarning) parts.Add("GREEN RUSH WARNING");
            if (IsFirstRthCandle) parts.Add("FIRST CANDLE (TRAP)");
            if (IsRthVolatilityWindow) parts.Add("VOL WINDOW");
            if (HasCleanPremarket) parts.Add("CLEAN PM");
            if (PremarketGainPercent != 0) parts.Add($"PM Gain: {PremarketGainPercent:+0.0;-0.0}%");
            parts.Add($"[{Recommendation}]");
            return string.Join(" | ", parts);
        }
    }

    /// <summary>
    /// Recommended action based on opening bell analysis.
    /// </summary>
    public enum OpeningBellAction
    {
        /// <summary>Normal trading conditions.</summary>
        NormalTrading,

        /// <summary>Hold position and watch for signals.</summary>
        HoldAndWatch,

        /// <summary>Exit long position before RTH bell due to green rush warning.</summary>
        ExitBeforeBell,

        /// <summary>Avoid trading the first RTH candle (9:30-9:31) - too volatile.</summary>
        AvoidFirstCandle,

        /// <summary>Reduced confidence during RTH volatility window (9:30-9:32).</summary>
        ReducedConfidence,

        /// <summary>Clean premarket + above VWAP = buy for rocket to HOD, then short fade.</summary>
        CleanRocketBuy
    }
}


