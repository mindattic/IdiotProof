// ============================================================================
// ADX Calculator - Average Directional Index Calculation
// ============================================================================
//
// ADX (Average Directional Index) measures trend strength (not direction).
// It ranges from 0-100 where:
//   - ADX < 20:  Weak or no trend (ranging market)
//   - ADX 20-25: Trend may be developing
//   - ADX 25-50: Strong trend
//   - ADX 50-75: Very strong trend
//   - ADX > 75:  Extremely strong trend (rare)
//
// COMPONENTS:
//   +DI (Positive Directional Indicator): Measures upward movement
//   -DI (Negative Directional Indicator): Measures downward movement
//   DX  (Directional Index): |+DI - -DI| / |+DI + -DI| × 100
//   ADX: Smoothed average of DX (Wilder's smoothing)
//
// FORMULA:
//   True Range (TR) = max(High-Low, |High-PrevClose|, |Low-PrevClose|)
//   +DM = High - PrevHigh (if positive and > -DM, else 0)
//   -DM = PrevLow - Low (if positive and > +DM, else 0)
//   Smoothed TR, +DM, -DM using Wilder's smoothing
//   +DI = (Smoothed +DM / Smoothed TR) × 100
//   -DI = (Smoothed -DM / Smoothed TR) × 100
//   DX = |+DI - -DI| / (+DI + -DI) × 100
//   ADX = Wilder's smooth of DX
//
// USAGE:
//   var adx = new AdxCalculator(14);
//   adx.Update(price);  // Call on each tick
//   if (adx.IsReady)
//       Console.WriteLine($"ADX={adx.CurrentAdx:F1}, +DI={adx.PlusDI:F1}, -DI={adx.MinusDI:F1}");
//
// ============================================================================

using System;

namespace IdiotProof.Backend.Helpers
{
    /// <summary>
    /// Calculates Average Directional Index (ADX) and Directional Indicators (+DI/-DI) from price ticks.
    /// </summary>
    /// <remarks>
    /// <para><b>How It Works:</b></para>
    /// <list type="number">
    ///   <item>Aggregates ticks into bars (configurable ticks per bar).</item>
    ///   <item>Calculates True Range and Directional Movement for each bar.</item>
    ///   <item>Applies Wilder's smoothing to get +DI, -DI, and ADX.</item>
    /// </list>
    /// </remarks>
    public sealed class AdxCalculator
    {
        private readonly int _period;
        private readonly int _ticksPerBar;

        // Current bar tracking
        private double _currentHigh;
        private double _currentLow;
        private double _currentOpen;
        private int _ticksInBar;

        // Previous bar values
        private double _previousHigh;
        private double _previousLow;
        private double _previousClose;

        // Smoothed values (Wilder's smoothing)
        private double _smoothedTR;
        private double _smoothedPlusDM;
        private double _smoothedMinusDM;
        private double _smoothedDX;

        // Current indicator values
        private double _currentPlusDI;
        private double _currentMinusDI;
        private double _currentAdx;

        // Initialization tracking
        private bool _isInitialized;
        private int _barsCompleted;

        /// <summary>
        /// Gets the ADX period.
        /// </summary>
        public int Period => _period;

        /// <summary>
        /// Gets the current ADX value (0-100).
        /// </summary>
        public double CurrentAdx => _currentAdx;

        /// <summary>
        /// Gets the current +DI value (0-100).
        /// </summary>
        public double PlusDI => _currentPlusDI;

        /// <summary>
        /// Gets the current -DI value (0-100).
        /// </summary>
        public double MinusDI => _currentMinusDI;

        /// <summary>
        /// Gets whether the ADX calculator has enough data to provide reliable values.
        /// Requires at least 2× period bars to fully initialize.
        /// </summary>
        public bool IsReady => _barsCompleted >= _period * 2;

        /// <summary>
        /// Creates a new ADX calculator.
        /// </summary>
        /// <param name="period">Number of periods for ADX calculation (default: 14).</param>
        /// <param name="ticksPerBar">Number of ticks to aggregate into one "bar" (default: 50).</param>
        public AdxCalculator(int period = 14, int ticksPerBar = 50)
        {
            if (period < 1)
                throw new ArgumentOutOfRangeException(nameof(period), "Period must be at least 1.");
            if (ticksPerBar < 1)
                throw new ArgumentOutOfRangeException(nameof(ticksPerBar), "Ticks per bar must be at least 1.");

            _period = period;
            _ticksPerBar = ticksPerBar;
            ResetBar();
        }

        /// <summary>
        /// Updates the ADX with a new price tick.
        /// </summary>
        /// <param name="price">The current price.</param>
        /// <returns>The updated ADX value, or 0 if not enough data yet.</returns>
        public double Update(double price)
        {
            if (price <= 0)
                return _currentAdx;

            // Initialize on first tick
            if (!_isInitialized)
            {
                _currentHigh = price;
                _currentLow = price;
                _currentOpen = price;
                _previousHigh = price;
                _previousLow = price;
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
                
                // Store for next bar
                _previousHigh = _currentHigh;
                _previousLow = _currentLow;
                _previousClose = price;
                
                ResetBar();
            }

            return _currentAdx;
        }

        private void CompleteBar(double closePrice)
        {
            _barsCompleted++;

            // Calculate True Range
            double highLowRange = _currentHigh - _currentLow;
            double highCloseRange = Math.Abs(_currentHigh - _previousClose);
            double lowCloseRange = Math.Abs(_currentLow - _previousClose);
            double trueRange = Math.Max(highLowRange, Math.Max(highCloseRange, lowCloseRange));

            // Calculate Directional Movement
            double upMove = _currentHigh - _previousHigh;
            double downMove = _previousLow - _currentLow;

            double plusDM = 0;
            double minusDM = 0;

            if (upMove > downMove && upMove > 0)
                plusDM = upMove;
            if (downMove > upMove && downMove > 0)
                minusDM = downMove;

            // Apply Wilder's smoothing
            if (_barsCompleted == 1)
            {
                // First bar - initialize smoothed values
                _smoothedTR = trueRange;
                _smoothedPlusDM = plusDM;
                _smoothedMinusDM = minusDM;
            }
            else
            {
                // Wilder's smoothing: Smoothed = Prior - (Prior / n) + Current
                _smoothedTR = _smoothedTR - (_smoothedTR / _period) + trueRange;
                _smoothedPlusDM = _smoothedPlusDM - (_smoothedPlusDM / _period) + plusDM;
                _smoothedMinusDM = _smoothedMinusDM - (_smoothedMinusDM / _period) + minusDM;
            }

            // Calculate +DI and -DI
            if (_smoothedTR > 0)
            {
                _currentPlusDI = (_smoothedPlusDM / _smoothedTR) * 100;
                _currentMinusDI = (_smoothedMinusDM / _smoothedTR) * 100;
            }

            // Calculate DX
            double diSum = _currentPlusDI + _currentMinusDI;
            double dx = 0;
            if (diSum > 0)
            {
                dx = (Math.Abs(_currentPlusDI - _currentMinusDI) / diSum) * 100;
            }

            // Calculate ADX using Wilder's smoothing of DX
            if (_barsCompleted <= _period)
            {
                // Accumulate DX for initial average
                _smoothedDX += dx;
                if (_barsCompleted == _period)
                {
                    _currentAdx = _smoothedDX / _period;
                }
            }
            else
            {
                // Wilder's smoothing: ADX = ((Prior ADX × (n-1)) + Current DX) / n
                _currentAdx = ((_currentAdx * (_period - 1)) + dx) / _period;
            }
        }

        private void ResetBar()
        {
            _currentHigh = double.MinValue;
            _currentLow = double.MaxValue;
            _currentOpen = 0;
            _ticksInBar = 0;
        }

        /// <summary>
        /// Resets the ADX calculator for a new trading session.
        /// </summary>
        public void Reset()
        {
            _smoothedTR = 0;
            _smoothedPlusDM = 0;
            _smoothedMinusDM = 0;
            _smoothedDX = 0;
            _currentPlusDI = 0;
            _currentMinusDI = 0;
            _currentAdx = 0;
            _barsCompleted = 0;
            _isInitialized = false;
            _previousHigh = 0;
            _previousLow = 0;
            _previousClose = 0;
            ResetBar();
        }
    }
}
