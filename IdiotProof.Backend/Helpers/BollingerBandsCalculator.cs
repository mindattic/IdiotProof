// ============================================================================
// Bollinger Bands Calculator - Mean Reversion and Volatility Analysis
// ============================================================================
//
// FORMULA:
// ========
// Middle Band = SMA(close, period)
// Upper Band = Middle + (stdDev * multiplier)
// Lower Band = Middle - (stdDev * multiplier)
// %B = (price - Lower) / (Upper - Lower)  -- Position within bands
// Bandwidth = (Upper - Lower) / Middle * 100  -- Band width as %
//
// INTERPRETATION:
// ===============
// %B > 1.0  = Price above upper band (overbought, potential reversal)
// %B < 0.0  = Price below lower band (oversold, potential reversal)
// %B = 0.5  = Price at middle band
//
// Bandwidth high = high volatility, expect mean reversion
// Bandwidth low = low volatility (squeeze), expect breakout
//
// AUTONOMOUS TRADING SIGNALS:
// ===========================
// 1. %B < 0.0 (oversold) + RSI < 30 → Strong bullish reversal signal
// 2. %B > 1.0 (overbought) + RSI > 70 → Strong bearish reversal signal
// 3. Bandwidth squeeze (<= 5%) → Breakout imminent, wait for direction
// 4. Price touching middle band with trend → Continuation signal
//
// ============================================================================

namespace IdiotProof.Backend.Helpers
{
    /// <summary>
    /// Calculates Bollinger Bands for volatility and mean reversion analysis.
    /// </summary>
    public sealed class BollingerBandsCalculator
    {
        private readonly int _period;
        private readonly double _multiplier;
        private readonly Queue<double> _prices;
        
        private double _sma;
        private double _upperBand;
        private double _lowerBand;
        private double _percentB;
        private double _bandwidth;
        private bool _isReady;

        /// <summary>
        /// Gets the current Simple Moving Average (middle band).
        /// </summary>
        public double MiddleBand => _sma;

        /// <summary>
        /// Gets the upper Bollinger Band.
        /// </summary>
        public double UpperBand => _upperBand;

        /// <summary>
        /// Gets the lower Bollinger Band.
        /// </summary>
        public double LowerBand => _lowerBand;

        /// <summary>
        /// Gets the %B value indicating position within the bands.
        /// &lt; 0.0 = below lower band (oversold)
        /// 0.0-0.5 = lower half
        /// 0.5-1.0 = upper half
        /// &gt; 1.0 = above upper band (overbought)
        /// </summary>
        public double PercentB => _percentB;

        /// <summary>
        /// Gets the bandwidth as a percentage of the middle band.
        /// Low values indicate a squeeze (potential breakout).
        /// High values indicate high volatility.
        /// </summary>
        public double Bandwidth => _bandwidth;

        /// <summary>
        /// Gets whether the calculator has enough data for reliable values.
        /// </summary>
        public bool IsReady => _isReady;

        /// <summary>
        /// Gets whether price is above the upper band (overbought).
        /// </summary>
        public bool IsAboveUpperBand => _isReady && _percentB > 1.0;

        /// <summary>
        /// Gets whether price is below the lower band (oversold).
        /// </summary>
        public bool IsBelowLowerBand => _isReady && _percentB < 0.0;

        /// <summary>
        /// Gets whether bands are in a squeeze (low volatility, potential breakout).
        /// Typically when bandwidth &lt; 5%.
        /// </summary>
        public bool IsInSqueeze => _isReady && _bandwidth < 5.0;

        /// <summary>
        /// Gets whether bands show high volatility (bandwidth &gt; 15%).
        /// </summary>
        public bool IsHighVolatility => _isReady && _bandwidth > 15.0;

        /// <summary>
        /// Creates a new Bollinger Bands calculator.
        /// </summary>
        /// <param name="period">Period for SMA calculation (default: 20).</param>
        /// <param name="multiplier">Standard deviation multiplier (default: 2.0).</param>
        public BollingerBandsCalculator(int period = 20, double multiplier = 2.0)
        {
            if (period < 2)
                throw new ArgumentOutOfRangeException(nameof(period), "Period must be at least 2.");
            if (multiplier <= 0)
                throw new ArgumentOutOfRangeException(nameof(multiplier), "Multiplier must be positive.");

            _period = period;
            _multiplier = multiplier;
            _prices = new Queue<double>(period + 1);
        }

        /// <summary>
        /// Updates the Bollinger Bands with a new price.
        /// </summary>
        /// <param name="price">The current price (typically close).</param>
        public void Update(double price)
        {
            if (price <= 0)
                return;

            _prices.Enqueue(price);

            // Maintain window size
            while (_prices.Count > _period)
                _prices.Dequeue();

            // Need full period for calculation
            if (_prices.Count < _period)
            {
                _isReady = false;
                return;
            }

            _isReady = true;

            // Calculate SMA
            double sum = 0;
            foreach (var p in _prices)
                sum += p;
            _sma = sum / _period;

            // Calculate standard deviation
            double sumSquaredDiff = 0;
            foreach (var p in _prices)
            {
                double diff = p - _sma;
                sumSquaredDiff += diff * diff;
            }
            double stdDev = Math.Sqrt(sumSquaredDiff / _period);

            // Calculate bands
            _upperBand = _sma + (_multiplier * stdDev);
            _lowerBand = _sma - (_multiplier * stdDev);

            // Calculate %B
            double bandWidth = _upperBand - _lowerBand;
            _percentB = bandWidth > 0 ? (price - _lowerBand) / bandWidth : 0.5;

            // Calculate bandwidth percentage
            _bandwidth = _sma > 0 ? (bandWidth / _sma) * 100 : 0;
        }

        /// <summary>
        /// Gets a score contribution for autonomous trading (-100 to +100).
        /// Positive = bullish (mean reversion from oversold)
        /// Negative = bearish (mean reversion from overbought)
        /// Near zero = neutral (price in middle of bands)
        /// </summary>
        public int GetScore()
        {
            if (!_isReady)
                return 0;

            // Below lower band = oversold, bullish reversal potential
            if (_percentB <= 0.0)
            {
                // The further below, the more bullish (capped at -0.5)
                double deviation = Math.Max(-0.5, _percentB);
                return (int)Math.Clamp(-deviation * 200, 0, 100);
            }

            // Above upper band = overbought, bearish reversal potential
            if (_percentB >= 1.0)
            {
                // The further above, the more bearish (capped at 1.5)
                double deviation = Math.Min(1.5, _percentB);
                return (int)Math.Clamp((1 - deviation) * 200, -100, 0);
            }

            // Within bands - slight mean reversion bias toward middle
            // %B 0.5 = 0 score, 0.0 = +25, 1.0 = -25
            return (int)((_percentB - 0.5) * -50);
        }

        /// <summary>
        /// Gets a human-readable description of the current state.
        /// </summary>
        public string GetDescription()
        {
            if (!_isReady)
                return "Insufficient data";

            string position = _percentB switch
            {
                < 0 => "OVERSOLD (below lower band)",
                <= 0.2 => "Lower band zone",
                <= 0.4 => "Lower-mid zone",
                <= 0.6 => "Middle zone",
                <= 0.8 => "Upper-mid zone",
                <= 1.0 => "Upper band zone",
                _ => "OVERBOUGHT (above upper band)"
            };

            string volatility = _bandwidth switch
            {
                < 3 => "Very tight squeeze",
                < 5 => "Squeeze",
                < 10 => "Normal",
                < 15 => "Elevated",
                _ => "High volatility"
            };

            return $"{position} | %B: {_percentB:F2} | Band: {volatility} ({_bandwidth:F1}%)";
        }

        public override string ToString() =>
            $"BB({_period},{_multiplier}): Lower={_lowerBand:F2} Mid={_sma:F2} Upper={_upperBand:F2} | %B={_percentB:F2} | BW={_bandwidth:F1}%";
    }
}
