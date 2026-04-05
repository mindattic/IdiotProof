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

namespace IdiotProof.Helpers {
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
        private readonly int period;
        private readonly int ticksPerBar;

        // Current bar tracking
        private double currentHigh;
        private double currentLow;
        private double currentOpen;
        private int ticksInBar;

        // Previous bar values
        private double previousHigh;
        private double previousLow;
        private double previousClose;

        // Smoothed values (Wilder's smoothing)
        private double smoothedTR;
        private double smoothedPlusDM;
        private double smoothedMinusDM;
        private double smoothedDX;

        // Current indicator values
        private double currentPlusDI;
        private double currentMinusDI;
        private double currentAdx;

        // Initialization tracking
        private bool isInitialized;
        private int barsCompleted;

        /// <summary>
        /// Gets the ADX period.
        /// </summary>
        public int Period => period;

        /// <summary>
        /// Gets the current ADX value (0-100).
        /// </summary>
        public double CurrentAdx => currentAdx;

        /// <summary>
        /// Gets the current +DI value (0-100).
        /// </summary>
        public double PlusDI => currentPlusDI;

        /// <summary>
        /// Gets the current -DI value (0-100).
        /// </summary>
        public double MinusDI => currentMinusDI;

        /// <summary>
        /// Gets whether the ADX calculator has enough data to provide reliable values.
        /// Requires at least 2× period bars to fully initialize.
        /// </summary>
        public bool IsReady => barsCompleted >= period * 2;

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

            this.period = period;
            this.ticksPerBar = ticksPerBar;
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
                return currentAdx;

            // Initialize on first tick
            if (!isInitialized)
            {
                currentHigh = price;
                currentLow = price;
                currentOpen = price;
                previousHigh = price;
                previousLow = price;
                previousClose = price;
                isInitialized = true;
                return 0;
            }

            // Update current bar's high/low
            if (price > currentHigh)
                currentHigh = price;
            if (price < currentLow)
                currentLow = price;

            ticksInBar++;

            // Complete the bar after N ticks
            if (ticksInBar >= ticksPerBar)
            {
                CompleteBar(price);
                
                // Store for next bar
                previousHigh = currentHigh;
                previousLow = currentLow;
                previousClose = price;
                
                ResetBar();
            }

            return currentAdx;
        }

        private void CompleteBar(double closePrice)
        {
            barsCompleted++;

            // Calculate True Range
            double highLowRange = currentHigh - currentLow;
            double highCloseRange = Math.Abs(currentHigh - previousClose);
            double lowCloseRange = Math.Abs(currentLow - previousClose);
            double trueRange = Math.Max(highLowRange, Math.Max(highCloseRange, lowCloseRange));

            // Calculate Directional Movement
            double upMove = currentHigh - previousHigh;
            double downMove = previousLow - currentLow;

            double plusDM = 0;
            double minusDM = 0;

            if (upMove > downMove && upMove > 0)
                plusDM = upMove;
            if (downMove > upMove && downMove > 0)
                minusDM = downMove;

            // Apply Wilder's smoothing
            if (barsCompleted == 1)
            {
                // First bar - initialize smoothed values
                smoothedTR = trueRange;
                smoothedPlusDM = plusDM;
                smoothedMinusDM = minusDM;
            }
            else
            {
                // Wilder's smoothing: Smoothed = Prior - (Prior / n) + Current
                smoothedTR = smoothedTR - (smoothedTR / period) + trueRange;
                smoothedPlusDM = smoothedPlusDM - (smoothedPlusDM / period) + plusDM;
                smoothedMinusDM = smoothedMinusDM - (smoothedMinusDM / period) + minusDM;
            }

            // Calculate +DI and -DI
            if (smoothedTR > 0)
            {
                currentPlusDI = (smoothedPlusDM / smoothedTR) * 100;
                currentMinusDI = (smoothedMinusDM / smoothedTR) * 100;
            }

            // Calculate DX
            double diSum = currentPlusDI + currentMinusDI;
            double dx = 0;
            if (diSum > 0)
            {
                dx = (Math.Abs(currentPlusDI - currentMinusDI) / diSum) * 100;
            }

            // Calculate ADX using Wilder's smoothing of DX
            if (barsCompleted <= period)
            {
                // Accumulate DX for initial average
                smoothedDX += dx;
                if (barsCompleted == period)
                {
                    currentAdx = smoothedDX / period;
                }
            }
            else
            {
                // Wilder's smoothing: ADX = ((Prior ADX × (n-1)) + Current DX) / n
                currentAdx = ((currentAdx * (period - 1)) + dx) / period;
            }
        }

        private void ResetBar()
        {
            currentHigh = double.MinValue;
            currentLow = double.MaxValue;
            currentOpen = 0;
            ticksInBar = 0;
        }

        /// <summary>
        /// Updates the ADX with a completed candlestick (OHLC data).
        /// Use this method when feeding data from a CandlestickAggregatorHelper.
        /// </summary>
        /// <param name="high">The candle high price.</param>
        /// <param name="low">The candle low price.</param>
        /// <param name="close">The candle close price.</param>
        /// <returns>The updated ADX value, or 0 if not enough data yet.</returns>
        /// <remarks>
        /// This method bypasses tick aggregation and directly processes candle data.
        /// Useful when the upstream data source already provides candlesticks.
        /// </remarks>
        public double UpdateFromCandle(double high, double low, double close)
        {
            if (high <= 0 || low <= 0 || close <= 0)
                return currentAdx;

            // Initialize on first candle
            if (!isInitialized)
            {
                previousHigh = high;
                previousLow = low;
                previousClose = close;
                isInitialized = true;
                return 0;
            }

            // Set current bar values from the candle
            currentHigh = high;
            currentLow = low;

            // Complete the bar immediately (each candle = one bar)
            CompleteBar(close);

            // Store for next candle
            previousHigh = high;
            previousLow = low;
            previousClose = close;

            return currentAdx;
        }

        /// <summary>
        /// Resets the ADX calculator for a new trading session.
        /// </summary>
        public void Reset()
        {
            smoothedTR = 0;
            smoothedPlusDM = 0;
            smoothedMinusDM = 0;
            smoothedDX = 0;
            currentPlusDI = 0;
            currentMinusDI = 0;
            currentAdx = 0;
            barsCompleted = 0;
            isInitialized = false;
            previousHigh = 0;
            previousLow = 0;
            previousClose = 0;
            ResetBar();
        }
    }
}


