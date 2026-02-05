// ============================================================================
// RSI Calculator - Relative Strength Index
// ============================================================================
//
// RSI (Relative Strength Index) is a momentum oscillator that measures the
// speed and magnitude of price changes. It ranges from 0 to 100.
//
// INTERPRETATION:
//   - RSI >= 70: Overbought (potential sell signal)
//   - RSI <= 30: Oversold (potential buy signal)
//   - RSI 40-60: Neutral zone
//
// FORMULA:
//   RSI = 100 - (100 / (1 + RS))
//   RS = Average Gain / Average Loss (over N periods)
//
// WARM-UP:
//   Requires N+1 candles to produce first valid RSI (default: 15 candles for RSI(14))
//
// ============================================================================

using System;

namespace IdiotProof.Backend.Helpers
{
    /// <summary>
    /// Calculates the Relative Strength Index (RSI) from candle close prices.
    /// </summary>
    public sealed class RsiCalculator
    {
        private readonly int _period;
        private double _avgGain;
        private double _avgLoss;
        private double _previousClose;
        private int _dataPoints;
        private double _currentRsi;

        /// <summary>
        /// Gets the RSI period.
        /// </summary>
        public int Period => _period;

        /// <summary>
        /// Gets the current RSI value (0-100).
        /// </summary>
        public double CurrentValue => _currentRsi;

        /// <summary>
        /// Gets whether the calculator has enough data to produce valid RSI.
        /// </summary>
        public bool IsReady => _dataPoints > _period;

        /// <summary>
        /// Creates a new RSI calculator.
        /// </summary>
        /// <param name="period">The RSI period (default: 14).</param>
        public RsiCalculator(int period = 14)
        {
            if (period < 1)
                throw new ArgumentOutOfRangeException(nameof(period), "Period must be at least 1.");

            _period = period;
        }

        /// <summary>
        /// Updates the RSI with a new candle close price.
        /// </summary>
        /// <param name="closePrice">The closing price of the candle.</param>
        /// <returns>The current RSI value, or 50 if not enough data.</returns>
        public double Update(double closePrice)
        {
            if (closePrice <= 0)
                return _currentRsi;

            _dataPoints++;

            if (_dataPoints == 1)
            {
                // First data point - just store it
                _previousClose = closePrice;
                _currentRsi = 50; // Neutral default
                return _currentRsi;
            }

            // Calculate change
            double change = closePrice - _previousClose;
            double gain = change > 0 ? change : 0;
            double loss = change < 0 ? -change : 0;

            if (_dataPoints <= _period + 1)
            {
                // Initial period - use simple average
                _avgGain = ((_avgGain * (_dataPoints - 2)) + gain) / (_dataPoints - 1);
                _avgLoss = ((_avgLoss * (_dataPoints - 2)) + loss) / (_dataPoints - 1);
            }
            else
            {
                // Smoothed average (Wilder's smoothing)
                _avgGain = ((_avgGain * (_period - 1)) + gain) / _period;
                _avgLoss = ((_avgLoss * (_period - 1)) + loss) / _period;
            }

            _previousClose = closePrice;

            // Calculate RSI
            if (_avgLoss == 0)
            {
                _currentRsi = 100; // No losses = maximum RSI
            }
            else if (_avgGain == 0)
            {
                _currentRsi = 0; // No gains = minimum RSI
            }
            else
            {
                double rs = _avgGain / _avgLoss;
                _currentRsi = 100 - (100 / (1 + rs));
            }

            return _currentRsi;
        }

        /// <summary>
        /// Resets the calculator for a new session.
        /// </summary>
        public void Reset()
        {
            _avgGain = 0;
            _avgLoss = 0;
            _previousClose = 0;
            _dataPoints = 0;
            _currentRsi = 0;
        }
    }
}


