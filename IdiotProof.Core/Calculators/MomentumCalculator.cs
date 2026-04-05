// ============================================================================
// Momentum Calculator - Price Momentum Indicator
// ============================================================================
//
// Momentum measures the rate of price change by comparing the current price
// to a price from N periods ago.
//
// FORMULA:
//   Momentum = Current Price - Price N periods ago
//
// INTERPRETATION:
//   - Momentum > 0: Price is higher than N periods ago (bullish)
//   - Momentum < 0: Price is lower than N periods ago (bearish)
//   - Rising Momentum: Trend is accelerating
//   - Falling Momentum: Trend is weakening
//
// WARM-UP:
//   Requires N+1 candles to produce first valid momentum value (default: 11 candles for Momentum(10))
//
// ASCII VISUALIZATION:
//
//     Momentum Oscillator
//     +--------------------------------------------+
//     |     /\                    /\               |
//     |    /  \                  /  \    Price     |
//     |   /    \                /    \             |
//     |--/------\--------------/------\----------- |  Zero Line
//     |          \            /        \           |
//     |           \          /          \          |
//     |            \________/                      |
//     +--------------------------------------------+
//           ^ Momentum > 0     ^ Momentum > 0
//
// ============================================================================

using System.Collections.Generic;

namespace IdiotProof.Helpers {
    /// <summary>
    /// Calculates price momentum from candle close prices.
    /// </summary>
    /// <remarks>
    /// <para><b>Momentum Indicator:</b></para>
    /// <para>Compares current price to price N periods ago to measure trend strength.</para>
    /// </remarks>
    public sealed class MomentumCalculator
    {
        private readonly int period;
        private readonly Queue<double> priceHistory;
        private double currentMomentum;

        /// <summary>
        /// Gets the momentum period.
        /// </summary>
        public int Period => period;

        /// <summary>
        /// Gets the current momentum value.
        /// </summary>
        public double CurrentValue => currentMomentum;

        /// <summary>
        /// Gets whether the calculator has enough data to produce valid momentum.
        /// </summary>
        public bool IsReady => priceHistory.Count >= period;

        /// <summary>
        /// Creates a new momentum calculator.
        /// </summary>
        /// <param name="period">The lookback period (default: 10).</param>
        public MomentumCalculator(int period = 10)
        {
            if (period < 1)
                throw new System.ArgumentOutOfRangeException(nameof(period), "Period must be at least 1.");

            this.period = period;
            priceHistory = new Queue<double>(period + 1);
        }

        /// <summary>
        /// Updates the momentum with a new candle close price.
        /// </summary>
        /// <param name="closePrice">The closing price of the candle.</param>
        /// <returns>The current momentum value, or 0 if not enough data.</returns>
        public double Update(double closePrice)
        {
            if (closePrice <= 0)
                return currentMomentum;

            priceHistory.Enqueue(closePrice);

            // Keep only the required history
            while (priceHistory.Count > period + 1)
            {
                priceHistory.Dequeue();
            }

            // Calculate momentum once we have enough data
            if (priceHistory.Count > period)
            {
                // Peek at the oldest price (N periods ago)
                double oldPrice = priceHistory.Peek();
                currentMomentum = closePrice - oldPrice;
            }

            return currentMomentum;
        }

        /// <summary>
        /// Resets the calculator to initial state.
        /// </summary>
        public void Reset()
        {
            priceHistory.Clear();
            currentMomentum = 0;
        }
    }
}


