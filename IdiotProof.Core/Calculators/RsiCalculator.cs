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

namespace IdiotProof.Helpers {
    /// <summary>
    /// Calculates the Relative Strength Index (RSI) from candle close prices.
    /// </summary>
    public sealed class RsiCalculator
    {
        private readonly int period;
        private double avgGain;
        private double avgLoss;
        private double previousClose;
        private int dataPoints;
        private double currentRsi;

        /// <summary>
        /// Gets the RSI period.
        /// </summary>
        public int Period => period;

        /// <summary>
        /// Gets the current RSI value (0-100).
        /// </summary>
        public double CurrentValue => currentRsi;

        /// <summary>
        /// Gets whether the calculator has enough data to produce valid RSI.
        /// </summary>
        public bool IsReady => dataPoints > period;

        /// <summary>
        /// Creates a new RSI calculator.
        /// </summary>
        /// <param name="period">The RSI period (default: 14).</param>
        public RsiCalculator(int period = 14)
        {
            if (period < 1)
                throw new ArgumentOutOfRangeException(nameof(period), "Period must be at least 1.");

            this.period = period;
        }

        /// <summary>
        /// Updates the RSI with a new candle close price.
        /// </summary>
        /// <param name="closePrice">The closing price of the candle.</param>
        /// <returns>The current RSI value, or 50 if not enough data.</returns>
        public double Update(double closePrice)
        {
            if (closePrice <= 0)
                return currentRsi;

            dataPoints++;

            if (dataPoints == 1)
            {
                // First data point - just store it
                previousClose = closePrice;
                currentRsi = 50; // Neutral default
                return currentRsi;
            }

            // Calculate change
            double change = closePrice - previousClose;
            double gain = change > 0 ? change : 0;
            double loss = change < 0 ? -change : 0;

            if (dataPoints <= period + 1)
            {
                // Initial period - use simple average
                avgGain = ((avgGain * (dataPoints - 2)) + gain) / (dataPoints - 1);
                avgLoss = ((avgLoss * (dataPoints - 2)) + loss) / (dataPoints - 1);
            }
            else
            {
                // Smoothed average (Wilder's smoothing)
                avgGain = ((avgGain * (period - 1)) + gain) / period;
                avgLoss = ((avgLoss * (period - 1)) + loss) / period;
            }

            previousClose = closePrice;

            // Calculate RSI
            if (avgLoss == 0)
            {
                currentRsi = 100; // No losses = maximum RSI
            }
            else if (avgGain == 0)
            {
                currentRsi = 0; // No gains = minimum RSI
            }
            else
            {
                double rs = avgGain / avgLoss;
                currentRsi = 100 - (100 / (1 + rs));
            }

            return currentRsi;
        }

        /// <summary>
        /// Resets the calculator for a new session.
        /// </summary>
        public void Reset()
        {
            avgGain = 0;
            avgLoss = 0;
            previousClose = 0;
            dataPoints = 0;
            currentRsi = 0;
        }
    }
}


