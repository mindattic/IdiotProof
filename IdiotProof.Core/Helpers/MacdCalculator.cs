// ============================================================================
// MACD Calculator - Moving Average Convergence Divergence
// ============================================================================
//
// MACD is a trend-following momentum indicator showing the relationship
// between two EMAs of a security's price.
//
// COMPONENTS:
//   - MACD Line: 12-period EMA - 26-period EMA
//   - Signal Line: 9-period EMA of the MACD line
//   - Histogram: MACD Line - Signal Line
//
// SIGNALS:
//   - Bullish: MACD crosses above signal line
//   - Bearish: MACD crosses below signal line
//   - Above Zero: Uptrend momentum
//   - Below Zero: Downtrend momentum
//
// WARM-UP:
//   Requires 26 candles for MACD line + 9 more for signal = 35 candles
//
// ============================================================================

using System;

namespace IdiotProof.Backend.Helpers
{
    /// <summary>
    /// Calculates MACD (Moving Average Convergence Divergence) from candle close prices.
    /// </summary>
    public sealed class MacdCalculator
    {
        private readonly EmaCalculator _fastEma;
        private readonly EmaCalculator _slowEma;
        private readonly EmaCalculator _signalEma;
        private int _dataPoints;
        private double _previousHistogram;

        /// <summary>
        /// Gets the fast EMA period (default: 12).
        /// </summary>
        public int FastPeriod { get; }

        /// <summary>
        /// Gets the slow EMA period (default: 26).
        /// </summary>
        public int SlowPeriod { get; }

        /// <summary>
        /// Gets the signal line period (default: 9).
        /// </summary>
        public int SignalPeriod { get; }

        /// <summary>
        /// Gets the current MACD line value (Fast EMA - Slow EMA).
        /// </summary>
        public double MacdLine { get; private set; }

        /// <summary>
        /// Gets the current signal line value (EMA of MACD line).
        /// </summary>
        public double SignalLine { get; private set; }

        /// <summary>
        /// Gets the current histogram value (MACD - Signal).
        /// </summary>
        public double Histogram { get; private set; }

        /// <summary>
        /// Gets the previous histogram value (for rising/falling detection).
        /// </summary>
        public double PreviousHistogram => _previousHistogram;

        /// <summary>
        /// Gets whether the MACD is bullish (MACD > Signal).
        /// </summary>
        public bool IsBullish => MacdLine > SignalLine;

        /// <summary>
        /// Gets whether the MACD is bearish (MACD &lt; Signal).
        /// </summary>
        public bool IsBearish => MacdLine < SignalLine;

        /// <summary>
        /// Gets whether the MACD line is above zero.
        /// </summary>
        public bool IsAboveZero => MacdLine > 0;

        /// <summary>
        /// Gets whether the histogram is rising.
        /// </summary>
        public bool IsHistogramRising => Histogram > _previousHistogram;

        /// <summary>
        /// Gets whether the calculator has enough data to produce valid MACD.
        /// </summary>
        public bool IsReady => _dataPoints >= SlowPeriod + SignalPeriod;

        /// <summary>
        /// Creates a new MACD calculator with default parameters (12, 26, 9).
        /// </summary>
        public MacdCalculator() : this(12, 26, 9)
        {
        }

        /// <summary>
        /// Creates a new MACD calculator with custom parameters.
        /// </summary>
        /// <param name="fastPeriod">Fast EMA period (default: 12).</param>
        /// <param name="slowPeriod">Slow EMA period (default: 26).</param>
        /// <param name="signalPeriod">Signal line period (default: 9).</param>
        public MacdCalculator(int fastPeriod, int slowPeriod, int signalPeriod)
        {
            if (fastPeriod < 1)
                throw new ArgumentOutOfRangeException(nameof(fastPeriod), "Fast period must be at least 1.");
            if (slowPeriod < 1)
                throw new ArgumentOutOfRangeException(nameof(slowPeriod), "Slow period must be at least 1.");
            if (signalPeriod < 1)
                throw new ArgumentOutOfRangeException(nameof(signalPeriod), "Signal period must be at least 1.");
            if (fastPeriod >= slowPeriod)
                throw new ArgumentException("Fast period must be less than slow period.");

            FastPeriod = fastPeriod;
            SlowPeriod = slowPeriod;
            SignalPeriod = signalPeriod;

            _fastEma = new EmaCalculator(fastPeriod);
            _slowEma = new EmaCalculator(slowPeriod);
            _signalEma = new EmaCalculator(signalPeriod);
        }

        /// <summary>
        /// Updates the MACD with a new candle close price.
        /// </summary>
        /// <param name="closePrice">The closing price of the candle.</param>
        public void Update(double closePrice)
        {
            if (closePrice <= 0)
                return;

            _dataPoints++;

            // Update fast and slow EMAs
            _fastEma.Update(closePrice);
            _slowEma.Update(closePrice);

            // Calculate MACD line
            MacdLine = _fastEma.CurrentValue - _slowEma.CurrentValue;

            // Update signal line (EMA of MACD)
            _signalEma.Update(MacdLine);
            SignalLine = _signalEma.CurrentValue;

            // Calculate histogram
            _previousHistogram = Histogram;
            Histogram = MacdLine - SignalLine;
        }

        /// <summary>
        /// Resets the calculator for a new session.
        /// </summary>
        public void Reset()
        {
            _fastEma.Reset();
            _slowEma.Reset();
            _signalEma.Reset();
            _dataPoints = 0;
            MacdLine = 0;
            SignalLine = 0;
            Histogram = 0;
            _previousHistogram = 0;
        }
    }
}


