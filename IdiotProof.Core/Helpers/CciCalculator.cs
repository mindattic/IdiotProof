// ============================================================================
// Commodity Channel Index (CCI) Calculator - Mean Reversion & Trend
// ============================================================================
//
// FORMULA:
// ========
// Typical Price (TP) = (High + Low + Close) / 3
// SMA = Simple Moving Average of TP over period
// Mean Deviation = Average of |TP - SMA|
// CCI = (TP - SMA) / (0.015 * Mean Deviation)
//
// INTERPRETATION:
// ===============
// > +100 = Overbought / Strong uptrend
// < -100 = Oversold / Strong downtrend
// 0 = At the statistical mean
//
// SIGNALS:
// ========
// CCI crossing above +100 = Potential uptrend beginning
// CCI crossing below -100 = Potential downtrend beginning
// CCI returning from extremes = Mean reversion opportunity
//
// ============================================================================

namespace IdiotProof.Helpers {
    /// <summary>
    /// Calculates the Commodity Channel Index for trend and mean reversion analysis.
    /// </summary>
    public sealed class CciCalculator
    {
        private readonly int _period;
        private readonly Queue<double> _typicalPrices;

        private double _cci;
        private double _previousCci;
        private bool _isReady;

        /// <summary>
        /// Gets the current CCI value.
        /// </summary>
        public double CurrentCci => _cci;

        /// <summary>
        /// Gets whether the calculator has enough data.
        /// </summary>
        public bool IsReady => _isReady;

        /// <summary>
        /// Gets whether CCI indicates overbought condition (>+100).
        /// </summary>
        public bool IsOverbought => _isReady && _cci > 100;

        /// <summary>
        /// Gets whether CCI indicates oversold condition (<-100).
        /// </summary>
        public bool IsOversold => _isReady && _cci < -100;

        /// <summary>
        /// Gets whether CCI indicates a strong uptrend (>+200).
        /// </summary>
        public bool IsStrongUptrend => _isReady && _cci > 200;

        /// <summary>
        /// Gets whether CCI indicates a strong downtrend (<-200).
        /// </summary>
        public bool IsStrongDowntrend => _isReady && _cci < -200;

        /// <summary>
        /// Gets whether CCI just crossed above +100 (potential uptrend start).
        /// </summary>
        public bool CrossedAbove100 => _isReady && _previousCci <= 100 && _cci > 100;

        /// <summary>
        /// Gets whether CCI just crossed below -100 (potential downtrend start).
        /// </summary>
        public bool CrossedBelow100 => _isReady && _previousCci >= -100 && _cci < -100;

        /// <summary>
        /// Creates a new CCI calculator.
        /// </summary>
        /// <param name="period">Period for calculation (default: 20).</param>
        public CciCalculator(int period = 20)
        {
            if (period < 2)
                throw new ArgumentOutOfRangeException(nameof(period), "Period must be at least 2.");

            _period = period;
            _typicalPrices = new Queue<double>(period + 1);
        }

        /// <summary>
        /// Updates CCI with a new OHLC bar.
        /// </summary>
        public void Update(double high, double low, double close)
        {
            if (high <= 0 || low <= 0 || close <= 0)
                return;

            double typicalPrice = (high + low + close) / 3;
            _typicalPrices.Enqueue(typicalPrice);

            while (_typicalPrices.Count > _period)
                _typicalPrices.Dequeue();

            if (_typicalPrices.Count < _period)
            {
                _isReady = false;
                return;
            }

            _isReady = true;
            _previousCci = _cci;

            // Calculate SMA of typical prices
            double sma = _typicalPrices.Average();

            // Calculate mean deviation
            double meanDeviation = _typicalPrices.Select(tp => Math.Abs(tp - sma)).Average();

            // Calculate CCI
            const double constant = 0.015;
            _cci = meanDeviation > 0 ? (typicalPrice - sma) / (constant * meanDeviation) : 0;
        }

        /// <summary>
        /// Gets a score contribution for autonomous trading (-100 to +100).
        /// </summary>
        public int GetScore()
        {
            if (!_isReady)
                return 0;

            // Strong trend signals
            if (IsStrongUptrend)
                return 80;
            if (IsStrongDowntrend)
                return -80;

            // Trend beginning signals
            if (CrossedAbove100)
                return 60;
            if (CrossedBelow100)
                return -60;

            // Mean reversion from extremes
            if (_previousCci > 100 && _cci < 100)
                return -40; // Reverting from overbought
            if (_previousCci < -100 && _cci > -100)
                return 40; // Reverting from oversold

            // General position
            if (IsOverbought)
                return (int)Math.Clamp((_cci - 100) * -0.3, -50, 0);
            if (IsOversold)
                return (int)Math.Clamp((-100 - _cci) * 0.3, 0, 50);

            // Neutral zone: scale linearly
            return (int)Math.Clamp(_cci * 0.3, -30, 30);
        }
    }
}
