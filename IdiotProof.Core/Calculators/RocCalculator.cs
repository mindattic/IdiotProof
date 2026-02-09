// ============================================================================
// ROC Calculator - Rate of Change Indicator
// ============================================================================
//
// Rate of Change (ROC) measures the percentage change in price over N periods.
//
// FORMULA:
//   ROC = ((Current Price - Price N periods ago) / Price N periods ago) × 100
//
// INTERPRETATION:
//   - ROC > 0: Price has increased over the period (bullish)
//   - ROC < 0: Price has decreased over the period (bearish)
//   - Rising ROC: Momentum is increasing
//   - Falling ROC: Momentum is decreasing
//
// WARM-UP:
//   Requires N+1 candles to produce first valid ROC value (default: 11 candles for ROC(10))
//
// ASCII VISUALIZATION:
//
//     ROC Oscillator (Percentage)
//     +--------------------------------------------+
//     |  +5% ────────────── Strong Bullish         |
//     |  +2% ═════════════════════════════════════ | Typical threshold
//     |   0% ──────────────────────────────────────| Zero Line
//     |  -2% ═════════════════════════════════════ |
//     |  -5% ────────────── Strong Bearish         |
//     +--------------------------------------------+
//
// ============================================================================

using System.Collections.Generic;

namespace IdiotProof.Helpers {
    /// <summary>
    /// Calculates Rate of Change (ROC) from candle close prices.
    /// </summary>
    /// <remarks>
    /// <para><b>ROC Indicator:</b></para>
    /// <para>Measures percentage price change to gauge momentum strength.</para>
    /// </remarks>
    public sealed class RocCalculator
    {
        private readonly int _period;
        private readonly Queue<double> _priceHistory;
        private double _currentRoc;

        /// <summary>
        /// Gets the ROC period.
        /// </summary>
        public int Period => _period;

        /// <summary>
        /// Gets the current ROC value (percentage).
        /// </summary>
        public double CurrentValue => _currentRoc;

        /// <summary>
        /// Gets whether the calculator has enough data to produce valid ROC.
        /// </summary>
        public bool IsReady => _priceHistory.Count >= _period;

        /// <summary>
        /// Creates a new ROC calculator.
        /// </summary>
        /// <param name="period">The lookback period (default: 10).</param>
        public RocCalculator(int period = 10)
        {
            if (period < 1)
                throw new System.ArgumentOutOfRangeException(nameof(period), "Period must be at least 1.");

            _period = period;
            _priceHistory = new Queue<double>(period + 1);
        }

        /// <summary>
        /// Updates the ROC with a new candle close price.
        /// </summary>
        /// <param name="closePrice">The closing price of the candle.</param>
        /// <returns>The current ROC value (percentage), or 0 if not enough data.</returns>
        public double Update(double closePrice)
        {
            if (closePrice <= 0)
                return _currentRoc;

            _priceHistory.Enqueue(closePrice);

            // Keep only the required history
            while (_priceHistory.Count > _period + 1)
            {
                _priceHistory.Dequeue();
            }

            // Calculate ROC once we have enough data
            if (_priceHistory.Count > _period)
            {
                // Peek at the oldest price (N periods ago)
                double oldPrice = _priceHistory.Peek();
                if (oldPrice > 0)
                {
                    _currentRoc = ((closePrice - oldPrice) / oldPrice) * 100;
                }
            }

            return _currentRoc;
        }

        /// <summary>
        /// Resets the calculator to initial state.
        /// </summary>
        public void Reset()
        {
            _priceHistory.Clear();
            _currentRoc = 0;
        }
    }
}


