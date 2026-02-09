// ============================================================================
// Stochastic Oscillator Calculator - Momentum Analysis
// ============================================================================
//
// FORMULA:
// ========
// %K = 100 * (Close - Lowest Low) / (Highest High - Lowest Low)
// %D = SMA(%K, smoothPeriod)
//
// INTERPRETATION:
// ===============
// > 80 = Overbought (potential sell signal when %K crosses below %D)
// < 20 = Oversold (potential buy signal when %K crosses above %D)
//
// SIGNALS:
// ========
// Bullish: %K crosses above %D in oversold zone (<20)
// Bearish: %K crosses below %D in overbought zone (>80)
//
// ============================================================================

namespace IdiotProof.Helpers {
    /// <summary>
    /// Calculates the Stochastic Oscillator for momentum analysis.
    /// </summary>
    public sealed class StochasticCalculator
    {
        private readonly int _kPeriod;
        private readonly int _dPeriod;
        private readonly Queue<double> _highs;
        private readonly Queue<double> _lows;
        private readonly Queue<double> _closes;
        private readonly Queue<double> _kValues;

        private double _percentK;
        private double _percentD;
        private double _previousK;
        private double _previousD;
        private bool _isReady;

        /// <summary>
        /// Gets the %K value (fast stochastic).
        /// </summary>
        public double PercentK => _percentK;

        /// <summary>
        /// Gets the %D value (slow stochastic / signal line).
        /// </summary>
        public double PercentD => _percentD;

        /// <summary>
        /// Gets whether the calculator has enough data.
        /// </summary>
        public bool IsReady => _isReady;

        /// <summary>
        /// Gets whether stochastic is in overbought territory (>80).
        /// </summary>
        public bool IsOverbought => _isReady && _percentK > 80;

        /// <summary>
        /// Gets whether stochastic is in oversold territory (<20).
        /// </summary>
        public bool IsOversold => _isReady && _percentK < 20;

        /// <summary>
        /// Gets whether there's a bullish crossover (%K crosses above %D in oversold zone).
        /// </summary>
        public bool IsBullishCrossover => _isReady &&
            _previousK <= _previousD && _percentK > _percentD && _percentK < 30;

        /// <summary>
        /// Gets whether there's a bearish crossover (%K crosses below %D in overbought zone).
        /// </summary>
        public bool IsBearishCrossover => _isReady &&
            _previousK >= _previousD && _percentK < _percentD && _percentK > 70;

        /// <summary>
        /// Creates a new Stochastic Oscillator calculator.
        /// </summary>
        /// <param name="kPeriod">Period for %K calculation (default: 14).</param>
        /// <param name="dPeriod">Period for %D smoothing (default: 3).</param>
        public StochasticCalculator(int kPeriod = 14, int dPeriod = 3)
        {
            if (kPeriod < 2)
                throw new ArgumentOutOfRangeException(nameof(kPeriod), "K period must be at least 2.");
            if (dPeriod < 1)
                throw new ArgumentOutOfRangeException(nameof(dPeriod), "D period must be at least 1.");

            _kPeriod = kPeriod;
            _dPeriod = dPeriod;
            _highs = new Queue<double>(kPeriod + 1);
            _lows = new Queue<double>(kPeriod + 1);
            _closes = new Queue<double>(kPeriod + 1);
            _kValues = new Queue<double>(dPeriod + 1);
        }

        /// <summary>
        /// Updates the stochastic with a new OHLC bar.
        /// </summary>
        public void Update(double high, double low, double close)
        {
            if (high <= 0 || low <= 0 || close <= 0)
                return;

            _highs.Enqueue(high);
            _lows.Enqueue(low);
            _closes.Enqueue(close);

            // Maintain window size
            while (_highs.Count > _kPeriod) _highs.Dequeue();
            while (_lows.Count > _kPeriod) _lows.Dequeue();
            while (_closes.Count > _kPeriod) _closes.Dequeue();

            if (_highs.Count < _kPeriod)
            {
                _isReady = false;
                return;
            }

            // Calculate %K
            double highestHigh = _highs.Max();
            double lowestLow = _lows.Min();
            double range = highestHigh - lowestLow;

            _previousK = _percentK;
            _percentK = range > 0 ? 100 * (close - lowestLow) / range : 50;

            // Calculate %D (SMA of %K)
            _kValues.Enqueue(_percentK);
            while (_kValues.Count > _dPeriod) _kValues.Dequeue();

            _previousD = _percentD;
            _percentD = _kValues.Average();

            _isReady = _kValues.Count >= _dPeriod;
        }

        /// <summary>
        /// Gets a score contribution for autonomous trading (-100 to +100).
        /// </summary>
        public int GetScore()
        {
            if (!_isReady)
                return 0;

            // Bullish crossover in oversold = strong buy signal
            if (IsBullishCrossover)
                return 80;

            // Bearish crossover in overbought = strong sell signal
            if (IsBearishCrossover)
                return -80;

            // Oversold = bullish potential
            if (IsOversold)
                return (int)((20 - _percentK) * 3); // Max +60

            // Overbought = bearish potential
            if (IsOverbought)
                return (int)((_percentK - 80) * -3); // Max -60

            // Neutral zone: slight bias based on position relative to %D
            return (int)((_percentK - _percentD) * 1.5);
        }
    }
}
