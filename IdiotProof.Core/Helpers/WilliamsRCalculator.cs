// ============================================================================
// Williams %R Calculator - Momentum Oscillator
// ============================================================================
//
// FORMULA:
// ========
// %R = (Highest High - Close) / (Highest High - Lowest Low) * -100
//
// INTERPRETATION:
// ===============
// Range: -100 to 0
// -100 to -80 = Oversold (bullish potential)
// -20 to 0 = Overbought (bearish potential)
//
// Note: Williams %R is inverted compared to other oscillators:
// - Near 0 = overbought (price near high)
// - Near -100 = oversold (price near low)
//
// SIGNALS:
// ========
// Bullish: %R crosses above -80 from oversold
// Bearish: %R crosses below -20 from overbought
//
// ============================================================================

namespace IdiotProof.Helpers {
    /// <summary>
    /// Calculates Williams %R momentum oscillator.
    /// </summary>
    public sealed class WilliamsRCalculator
    {
        private readonly int _period;
        private readonly Queue<double> _highs;
        private readonly Queue<double> _lows;

        private double _williamsR;
        private double _previousWilliamsR;
        private bool _isReady;

        /// <summary>
        /// Gets the current Williams %R value (-100 to 0).
        /// </summary>
        public double CurrentValue => _williamsR;

        /// <summary>
        /// Gets whether the calculator has enough data.
        /// </summary>
        public bool IsReady => _isReady;

        /// <summary>
        /// Gets whether Williams %R indicates overbought (-20 to 0).
        /// </summary>
        public bool IsOverbought => _isReady && _williamsR > -20;

        /// <summary>
        /// Gets whether Williams %R indicates oversold (-100 to -80).
        /// </summary>
        public bool IsOversold => _isReady && _williamsR < -80;

        /// <summary>
        /// Gets whether there's a bullish crossover (leaving oversold zone).
        /// </summary>
        public bool IsBullishCrossover => _isReady &&
            _previousWilliamsR < -80 && _williamsR >= -80;

        /// <summary>
        /// Gets whether there's a bearish crossover (entering overbought zone).
        /// </summary>
        public bool IsBearishCrossover => _isReady &&
            _previousWilliamsR > -20 && _williamsR <= -20;

        /// <summary>
        /// Creates a new Williams %R calculator.
        /// </summary>
        /// <param name="period">Period for calculation (default: 14).</param>
        public WilliamsRCalculator(int period = 14)
        {
            if (period < 2)
                throw new ArgumentOutOfRangeException(nameof(period), "Period must be at least 2.");

            _period = period;
            _highs = new Queue<double>(period + 1);
            _lows = new Queue<double>(period + 1);
        }

        /// <summary>
        /// Updates Williams %R with a new OHLC bar.
        /// </summary>
        public void Update(double high, double low, double close)
        {
            if (high <= 0 || low <= 0 || close <= 0)
                return;

            _highs.Enqueue(high);
            _lows.Enqueue(low);

            while (_highs.Count > _period) _highs.Dequeue();
            while (_lows.Count > _period) _lows.Dequeue();

            if (_highs.Count < _period)
            {
                _isReady = false;
                return;
            }

            _isReady = true;
            _previousWilliamsR = _williamsR;

            double highestHigh = _highs.Max();
            double lowestLow = _lows.Min();
            double range = highestHigh - lowestLow;

            _williamsR = range > 0
                ? ((highestHigh - close) / range) * -100
                : -50; // Neutral if no range
        }

        /// <summary>
        /// Gets a score contribution for autonomous trading (-100 to +100).
        /// </summary>
        public int GetScore()
        {
            if (!_isReady)
                return 0;

            // Bullish crossover from oversold
            if (IsBullishCrossover)
                return 70;

            // Bearish crossover from overbought
            if (IsBearishCrossover)
                return -70;

            // Oversold = bullish potential
            if (IsOversold)
            {
                // More oversold = more bullish
                double depth = -80 - _williamsR; // 0 to 20
                return (int)Math.Clamp(depth * 3, 0, 60);
            }

            // Overbought = bearish potential
            if (IsOverbought)
            {
                // More overbought = more bearish
                double depth = _williamsR + 20; // 0 to 20
                return (int)Math.Clamp(depth * -3, -60, 0);
            }

            // Neutral zone: slight bias based on position
            // -80 to -20 maps to roughly -30 to +30
            return (int)((_williamsR + 50) * 0.75);
        }
    }
}
