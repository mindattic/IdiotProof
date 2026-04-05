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
        private readonly int period;
        private readonly double multiplier;
        private double currentEma;
        private double previousEma;
        private double priceSum;
        private int priceCount;
        private bool isInitialized;

        /// <summary>
        /// Gets the EMA period.
        /// </summary>
        public int Period => period;

        /// <summary>
        /// Gets the current EMA value.
        /// </summary>
        public double CurrentValue => currentEma;

        /// <summary>
        /// Gets the previous EMA value (1 bar ago).
        /// Used for slope/direction detection.
        /// </summary>
        public double PreviousValue => previousEma;

        /// <summary>
        /// Gets whether the EMA calculator has enough data to provide a reliable value.
        /// Requires at least 'period' price points to initialize.
        /// </summary>
        public bool IsReady => isInitialized;

        /// <summary>
        /// Creates a new EMA calculator.
        /// </summary>
        /// <param name="period">Number of periods for EMA calculation (e.g., 9, 21, 200).</param>
        public EmaCalculator(int period)
        {
            if (period < 1)
                throw new ArgumentOutOfRangeException(nameof(period), "Period must be at least 1.");

            this.period = period;
            multiplier = 2.0 / (period + 1);
        }

        /// <summary>
        /// Updates the EMA with a new price.
        /// </summary>
        /// <param name="price">The current price.</param>
        /// <returns>The updated EMA value, or 0 if not enough data yet.</returns>
        public double Update(double price)
        {
            if (price <= 0)
                return currentEma;

            if (!isInitialized)
            {
                // Accumulate prices for initial SMA
                priceSum += price;
                priceCount++;

                if (priceCount >= period)
                {
                    // Initialize EMA with SMA
                    currentEma = priceSum / priceCount;
                    isInitialized = true;
                }

                return currentEma;
            }

            // Apply EMA formula: EMA = (Price - Previous EMA) × Multiplier + Previous EMA
            previousEma = currentEma;
            currentEma = (price - currentEma) * multiplier + currentEma;
            return currentEma;
        }

        /// <summary>
        /// Resets the calculator to initial state.
        /// </summary>
        public void Reset()
        {
            currentEma = 0;
            previousEma = 0;
            priceSum = 0;
            priceCount = 0;
            isInitialized = false;
        }
    }
}


