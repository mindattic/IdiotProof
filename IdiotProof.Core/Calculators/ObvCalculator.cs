// ============================================================================
// On-Balance Volume (OBV) Calculator - Volume Flow Analysis
// ============================================================================
//
// FORMULA:
// ========
// If Close > Previous Close: OBV = Previous OBV + Volume
// If Close < Previous Close: OBV = Previous OBV - Volume
// If Close = Previous Close: OBV = Previous OBV
//
// INTERPRETATION:
// ===============
// Rising OBV = Buying pressure (accumulation)
// Falling OBV = Selling pressure (distribution)
//
// DIVERGENCE SIGNALS:
// ===================
// Bullish: Price making lower lows, OBV making higher lows
// Bearish: Price making higher highs, OBV making lower highs
//
// ============================================================================

namespace IdiotProof.Helpers {
    /// <summary>
    /// Calculates On-Balance Volume for volume flow analysis.
    /// </summary>
    public sealed class ObvCalculator
    {
        private readonly int emaPeriod;
        private readonly Queue<double> obvValues;
        private readonly Queue<double> prices;

        private double obv;
        private double previousClose;
        private double obvEma;
        private double previousObv;
        private bool isReady;
        private int barCount;

        /// <summary>
        /// Gets the current OBV value.
        /// </summary>
        public double CurrentObv => obv;

        /// <summary>
        /// Gets the OBV EMA for smoothing.
        /// </summary>
        public double ObvEma => obvEma;

        /// <summary>
        /// Gets whether the calculator has enough data.
        /// </summary>
        public bool IsReady => isReady;

        /// <summary>
        /// Gets whether OBV is rising (accumulation).
        /// </summary>
        public bool IsRising => isReady && obv > previousObv;

        /// <summary>
        /// Gets whether OBV is falling (distribution).
        /// </summary>
        public bool IsFalling => isReady && obv < previousObv;

        /// <summary>
        /// Gets whether OBV is above its EMA (bullish volume flow).
        /// </summary>
        public bool IsAboveEma => isReady && obv > obvEma;

        /// <summary>
        /// Gets whether OBV is below its EMA (bearish volume flow).
        /// </summary>
        public bool IsBelowEma => isReady && obv < obvEma;

        /// <summary>
        /// Creates a new OBV calculator.
        /// </summary>
        /// <param name="emaPeriod">Period for OBV EMA smoothing (default: 20).</param>
        public ObvCalculator(int emaPeriod = 20)
        {
            this.emaPeriod = emaPeriod;
            obvValues = new Queue<double>(emaPeriod + 1);
            prices = new Queue<double>(10);
        }

        /// <summary>
        /// Updates OBV with a new bar.
        /// </summary>
        public void Update(double close, double volume)
        {
            if (close <= 0 || volume < 0)
                return;

            barCount++;
            previousObv = obv;

            if (previousClose > 0)
            {
                if (close > previousClose)
                    obv += volume;
                else if (close < previousClose)
                    obv -= volume;
                // If equal, OBV stays the same
            }

            previousClose = close;
            prices.Enqueue(close);
            while (prices.Count > 10) prices.Dequeue();

            // Update OBV EMA
            obvValues.Enqueue(obv);
            while (obvValues.Count > emaPeriod) obvValues.Dequeue();

            if (obvValues.Count >= emaPeriod)
            {
                // Simple moving average for smoothing
                obvEma = obvValues.Average();
                isReady = true;
            }
        }

        /// <summary>
        /// Detects divergence between price and OBV.
        /// Returns positive for bullish divergence, negative for bearish.
        /// </summary>
        public int GetDivergenceScore()
        {
            if (!isReady || prices.Count < 5)
                return 0;

            var priceList = prices.ToList();
            var recentPriceSlope = (priceList[^1] - priceList[^5]) / priceList[^5];
            var recentObvSlope = previousObv != 0 ? (obv - previousObv) / Math.Abs(previousObv) : 0;

            // Bullish divergence: price down, OBV up
            if (recentPriceSlope < -0.01 && recentObvSlope > 0.01)
                return 50;

            // Bearish divergence: price up, OBV down
            if (recentPriceSlope > 0.01 && recentObvSlope < -0.01)
                return -50;

            return 0;
        }

        /// <summary>
        /// Gets a score contribution for autonomous trading (-100 to +100).
        /// </summary>
        public int GetScore()
        {
            if (!isReady)
                return 0;

            int score = 0;

            // OBV above/below EMA
            double obvDiff = (obv - obvEma) / Math.Max(Math.Abs(obvEma), 1);
            score += (int)Math.Clamp(obvDiff * 100, -40, 40);

            // Rising/Falling OBV
            if (IsRising)
                score += 20;
            else if (IsFalling)
                score -= 20;

            // Divergence
            score += GetDivergenceScore();

            return (int)Math.Clamp(score, -100, 100);
        }
    }
}
