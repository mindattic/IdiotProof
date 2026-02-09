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
        private readonly int _emaPeriod;
        private readonly Queue<double> _obvValues;
        private readonly Queue<double> _prices;

        private double _obv;
        private double _previousClose;
        private double _obvEma;
        private double _previousObv;
        private bool _isReady;
        private int _barCount;

        /// <summary>
        /// Gets the current OBV value.
        /// </summary>
        public double CurrentObv => _obv;

        /// <summary>
        /// Gets the OBV EMA for smoothing.
        /// </summary>
        public double ObvEma => _obvEma;

        /// <summary>
        /// Gets whether the calculator has enough data.
        /// </summary>
        public bool IsReady => _isReady;

        /// <summary>
        /// Gets whether OBV is rising (accumulation).
        /// </summary>
        public bool IsRising => _isReady && _obv > _previousObv;

        /// <summary>
        /// Gets whether OBV is falling (distribution).
        /// </summary>
        public bool IsFalling => _isReady && _obv < _previousObv;

        /// <summary>
        /// Gets whether OBV is above its EMA (bullish volume flow).
        /// </summary>
        public bool IsAboveEma => _isReady && _obv > _obvEma;

        /// <summary>
        /// Gets whether OBV is below its EMA (bearish volume flow).
        /// </summary>
        public bool IsBelowEma => _isReady && _obv < _obvEma;

        /// <summary>
        /// Creates a new OBV calculator.
        /// </summary>
        /// <param name="emaPeriod">Period for OBV EMA smoothing (default: 20).</param>
        public ObvCalculator(int emaPeriod = 20)
        {
            _emaPeriod = emaPeriod;
            _obvValues = new Queue<double>(emaPeriod + 1);
            _prices = new Queue<double>(10);
        }

        /// <summary>
        /// Updates OBV with a new bar.
        /// </summary>
        public void Update(double close, double volume)
        {
            if (close <= 0 || volume < 0)
                return;

            _barCount++;
            _previousObv = _obv;

            if (_previousClose > 0)
            {
                if (close > _previousClose)
                    _obv += volume;
                else if (close < _previousClose)
                    _obv -= volume;
                // If equal, OBV stays the same
            }

            _previousClose = close;
            _prices.Enqueue(close);
            while (_prices.Count > 10) _prices.Dequeue();

            // Update OBV EMA
            _obvValues.Enqueue(_obv);
            while (_obvValues.Count > _emaPeriod) _obvValues.Dequeue();

            if (_obvValues.Count >= _emaPeriod)
            {
                // Simple moving average for smoothing
                _obvEma = _obvValues.Average();
                _isReady = true;
            }
        }

        /// <summary>
        /// Detects divergence between price and OBV.
        /// Returns positive for bullish divergence, negative for bearish.
        /// </summary>
        public int GetDivergenceScore()
        {
            if (!_isReady || _prices.Count < 5)
                return 0;

            var priceList = _prices.ToList();
            var recentPriceSlope = (priceList[^1] - priceList[^5]) / priceList[^5];
            var recentObvSlope = _previousObv != 0 ? (_obv - _previousObv) / Math.Abs(_previousObv) : 0;

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
            if (!_isReady)
                return 0;

            int score = 0;

            // OBV above/below EMA
            double obvDiff = (_obv - _obvEma) / Math.Max(Math.Abs(_obvEma), 1);
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
