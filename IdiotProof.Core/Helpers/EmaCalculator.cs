// ============================================================================
// EMA Calculator - Exponential Moving Average Calculation
// ============================================================================
//
// EMA (Exponential Moving Average) gives more weight to recent prices,
// making it more responsive to new information than a simple moving average.
//
// FORMULA:
//   EMA = (Price - Previous EMA) × Multiplier + Previous EMA
//   Multiplier = 2 / (Period + 1)
//
// For example, a 9-period EMA has a multiplier of 2/10 = 0.2
//
// USAGE:
//   var ema9 = new EmaCalculator(9);
//   ema9.Update(price);  // Call on each tick
//   if (ema9.IsReady)
//       Console.WriteLine($"EMA(9) = {ema9.CurrentValue}");
//
// ============================================================================

using System;

namespace IdiotProof.Helpers {
    /// <summary>
    /// Calculates Exponential Moving Average (EMA) from price ticks.
    /// </summary>
    /// <remarks>
    /// <para><b>How It Works:</b></para>
    /// <list type="number">
    ///   <item>Accumulates initial prices until period is reached (uses SMA for seed).</item>
    ///   <item>After initialization, applies EMA formula with each new price.</item>
    ///   <item>Multiplier determines how much weight is given to recent prices.</item>
    /// </list>
    /// </remarks>
    public sealed class EmaCalculator
    {
        private readonly int _period;
        private readonly double _multiplier;
        private double _currentEma;
        private double _previousEma;
        private double _priceSum;
        private int _priceCount;
        private bool _isInitialized;

        /// <summary>
        /// Gets the EMA period.
        /// </summary>
        public int Period => _period;

        /// <summary>
        /// Gets the current EMA value.
        /// </summary>
        public double CurrentValue => _currentEma;

        /// <summary>
        /// Gets the previous EMA value (1 bar ago).
        /// Used for slope/direction detection.
        /// </summary>
        public double PreviousValue => _previousEma;

        /// <summary>
        /// Gets whether the EMA calculator has enough data to provide a reliable value.
        /// Requires at least 'period' price points to initialize.
        /// </summary>
        public bool IsReady => _isInitialized;

        /// <summary>
        /// Creates a new EMA calculator.
        /// </summary>
        /// <param name="period">Number of periods for EMA calculation (e.g., 9, 21, 200).</param>
        public EmaCalculator(int period)
        {
            if (period < 1)
                throw new ArgumentOutOfRangeException(nameof(period), "Period must be at least 1.");

            _period = period;
            _multiplier = 2.0 / (period + 1);
        }

        /// <summary>
        /// Updates the EMA with a new price.
        /// </summary>
        /// <param name="price">The current price.</param>
        /// <returns>The updated EMA value, or 0 if not enough data yet.</returns>
        public double Update(double price)
        {
            if (price <= 0)
                return _currentEma;

            if (!_isInitialized)
            {
                // Accumulate prices for initial SMA
                _priceSum += price;
                _priceCount++;

                if (_priceCount >= _period)
                {
                    // Initialize EMA with SMA
                    _currentEma = _priceSum / _priceCount;
                    _isInitialized = true;
                }

                return _currentEma;
            }

            // Apply EMA formula: EMA = (Price - Previous EMA) × Multiplier + Previous EMA
            _previousEma = _currentEma;
            _currentEma = (price - _currentEma) * _multiplier + _currentEma;
            return _currentEma;
        }

        /// <summary>
        /// Resets the calculator to initial state.
        /// </summary>
        public void Reset()
        {
            _currentEma = 0;
            _previousEma = 0;
            _priceSum = 0;
            _priceCount = 0;
            _isInitialized = false;
        }
    }
}


