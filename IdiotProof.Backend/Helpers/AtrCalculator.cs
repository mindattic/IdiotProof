// ============================================================================
// ATR Calculator - Average True Range Calculation
// ============================================================================
//
// ATR (Average True Range) measures market volatility by decomposing the entire
// range of a price for a period. For real-time trading, we use a modified
// approach that works with tick data instead of OHLC bars.
//
// TRUE RANGE is the greatest of:
//   1. Current High - Current Low
//   2. |Current High - Previous Close|
//   3. |Current Low - Previous Close|
//
// For tick-based calculation, we approximate using:
//   - Rolling high/low over N ticks
//   - Exponential smoothing for responsiveness
//
// ============================================================================

using System;
using System.Collections.Generic;

namespace IdiotProof.Backend.Helpers
{
    /// <summary>
    /// Calculates Average True Range (ATR) from price ticks for volatility-based stop losses.
    /// </summary>
    /// <remarks>
    /// <para><b>How It Works:</b></para>
    /// <list type="number">
    ///   <item>Tracks rolling high/low prices over a configurable window.</item>
    ///   <item>Calculates True Range at each price update.</item>
    ///   <item>Smooths TR using exponential moving average (EMA) to get ATR.</item>
    /// </list>
    /// 
    /// <para><b>Note:</b> This is a tick-based approximation of traditional bar-based ATR.
    /// It provides a reasonable volatility estimate for real-time trailing stops.</para>
    /// </remarks>
    public sealed class AtrCalculator
    {
        private readonly int _period;
        private readonly Queue<double> _highs;
        private readonly Queue<double> _lows;
        private readonly Queue<double> _trueRanges;
        
        private double _currentHigh;
        private double _currentLow;
        private double _previousClose;
        private double _currentAtr;
        private int _ticksInBar;
        private readonly int _ticksPerBar;
        private bool _isInitialized;

        /// <summary>
        /// Gets the current ATR value.
        /// </summary>
        public double CurrentAtr => _currentAtr;

        /// <summary>
        /// Gets whether the ATR calculator has enough data to provide a reliable value.
        /// </summary>
        public bool IsReady => _isInitialized && _trueRanges.Count >= _period / 2;

        /// <summary>
        /// Creates a new ATR calculator.
        /// </summary>
        /// <param name="period">Number of periods for ATR calculation (default: 14).</param>
        /// <param name="ticksPerBar">Number of ticks to aggregate into one "bar" (default: 50).</param>
        public AtrCalculator(int period = 14, int ticksPerBar = 50)
        {
            if (period < 1)
                throw new ArgumentOutOfRangeException(nameof(period), "Period must be at least 1.");
            if (ticksPerBar < 1)
                throw new ArgumentOutOfRangeException(nameof(ticksPerBar), "Ticks per bar must be at least 1.");

            _period = period;
            _ticksPerBar = ticksPerBar;
            _highs = new Queue<double>(period + 1);
            _lows = new Queue<double>(period + 1);
            _trueRanges = new Queue<double>(period + 1);
            
            ResetBar();
        }

        /// <summary>
        /// Updates the ATR with a new price tick.
        /// </summary>
        /// <param name="price">The current price.</param>
        /// <returns>The updated ATR value, or 0 if not enough data yet.</returns>
        public double Update(double price)
        {
            if (price <= 0)
                return _currentAtr;

            // Initialize on first tick
            if (!_isInitialized)
            {
                _currentHigh = price;
                _currentLow = price;
                _previousClose = price;
                _isInitialized = true;
                return 0;
            }

            // Update current bar's high/low
            if (price > _currentHigh)
                _currentHigh = price;
            if (price < _currentLow)
                _currentLow = price;

            _ticksInBar++;

            // Complete the bar after N ticks
            if (_ticksInBar >= _ticksPerBar)
            {
                CompleteBar(price);
                ResetBar();
                _previousClose = price;
            }

            return _currentAtr;
        }

        /// <summary>
        /// Calculates the stop loss price based on current ATR and configuration.
        /// </summary>
        /// <param name="referencePrice">The price to calculate stop from (e.g., high water mark).</param>
        /// <param name="multiplier">ATR multiplier (e.g., 2.0 for 2× ATR).</param>
        /// <param name="isLong">True for long positions (stop below), false for short (stop above).</param>
        /// <param name="minPercent">Minimum stop distance as percentage.</param>
        /// <param name="maxPercent">Maximum stop distance as percentage.</param>
        /// <returns>The calculated stop loss price.</returns>
        public double CalculateStopPrice(
            double referencePrice, 
            double multiplier, 
            bool isLong = true,
            double minPercent = 0.01,
            double maxPercent = 0.25)
        {
            if (!IsReady || _currentAtr <= 0)
            {
                // Fall back to percentage-based stop if ATR not ready
                double fallbackDistance = referencePrice * 0.10; // 10% fallback
                return isLong 
                    ? Math.Round(referencePrice - fallbackDistance, 2)
                    : Math.Round(referencePrice + fallbackDistance, 2);
            }

            // Calculate ATR-based stop distance
            double atrDistance = _currentAtr * multiplier;

            // Apply min/max bounds
            double minDistance = referencePrice * minPercent;
            double maxDistance = referencePrice * maxPercent;
            
            double boundedDistance = Math.Max(minDistance, Math.Min(maxDistance, atrDistance));

            return isLong
                ? Math.Round(referencePrice - boundedDistance, 2)
                : Math.Round(referencePrice + boundedDistance, 2);
        }

        /// <summary>
        /// Gets the current ATR as a percentage of the given price.
        /// </summary>
        /// <param name="price">Reference price for percentage calculation.</param>
        /// <returns>ATR as percentage (e.g., 0.05 for 5%).</returns>
        public double GetAtrPercent(double price)
        {
            if (price <= 0 || !IsReady)
                return 0;
            return _currentAtr / price;
        }

        private void CompleteBar(double closePrice)
        {
            // Calculate True Range
            double highLowRange = _currentHigh - _currentLow;
            double highCloseRange = Math.Abs(_currentHigh - _previousClose);
            double lowCloseRange = Math.Abs(_currentLow - _previousClose);

            double trueRange = Math.Max(highLowRange, Math.Max(highCloseRange, lowCloseRange));

            // Add to queue
            _trueRanges.Enqueue(trueRange);
            _highs.Enqueue(_currentHigh);
            _lows.Enqueue(_currentLow);

            // Keep queue at period size
            while (_trueRanges.Count > _period)
            {
                _trueRanges.Dequeue();
                _highs.Dequeue();
                _lows.Dequeue();
            }

            // Calculate ATR using Wilder's smoothing (EMA)
            if (_trueRanges.Count == 1)
            {
                _currentAtr = trueRange;
            }
            else
            {
                // Wilder's smoothing: ATR = ((Prior ATR × (n-1)) + Current TR) / n
                _currentAtr = ((_currentAtr * (_period - 1)) + trueRange) / _period;
            }
        }

        private void ResetBar()
        {
            _currentHigh = double.MinValue;
            _currentLow = double.MaxValue;
            _ticksInBar = 0;
        }

        /// <summary>
        /// Resets the ATR calculator for a new trading session.
        /// </summary>
        public void Reset()
        {
            _highs.Clear();
            _lows.Clear();
            _trueRanges.Clear();
            _currentAtr = 0;
            _isInitialized = false;
            ResetBar();
        }
    }
}
