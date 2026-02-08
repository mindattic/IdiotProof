// ============================================================================
// Candlestick - Open, High, Low, Close Price Bar
// ============================================================================
//
// Represents a single candlestick (price bar) for technical analysis.
// Candlesticks can be aggregated by time (1-min, 5-min, etc.) or by tick count.
//
// WARM-UP REQUIREMENTS:
// =====================
// Many indicators require historical candlesticks to calculate properly:
//
//   | Indicator      | Minimum Candles | Recommended Start Time |
//   |----------------|-----------------|------------------------|
//   | EMA(9)         | 9 candles       | 9 minutes early        |
//   | EMA(21)        | 21 candles      | 21 minutes early       |
//   | EMA(200)       | 200 candles     | 3+ hours early         |
//   | ADX(14)        | 28 candles      | 30 minutes early       |
//   | RSI(14)        | 15 candles      | 15 minutes early       |
//
// BEST PRACTICE:
// Start the backend at least 30 minutes before your strategy's
// trading window to allow indicators to warm up properly.
//
// For premarket strategies (4:00 AM), start the backend around 3:30 AM.
// For RTH strategies (9:30 AM), start the backend around 9:00 AM.
//
// ============================================================================

using System;

namespace IdiotProof.Helpers {
    /// <summary>
    /// Represents a single candlestick (OHLC price bar).
    /// </summary>
    public sealed class Candlestick
    {
        /// <summary>
        /// The timestamp when this candlestick started.
        /// </summary>
        public DateTime Timestamp { get; init; }

        /// <summary>
        /// The opening price of the candlestick.
        /// </summary>
        public double Open { get; init; }

        /// <summary>
        /// The highest price during the candlestick.
        /// </summary>
        public double High { get; init; }

        /// <summary>
        /// The lowest price during the candlestick.
        /// </summary>
        public double Low { get; init; }

        /// <summary>
        /// The closing price of the candlestick.
        /// </summary>
        public double Close { get; init; }

        /// <summary>
        /// The total volume during the candlestick.
        /// </summary>
        public long Volume { get; init; }

        /// <summary>
        /// The number of ticks that occurred during this candlestick.
        /// </summary>
        public int TickCount { get; init; }

        /// <summary>
        /// Whether this candlestick is complete (closed) or still forming.
        /// </summary>
        public bool IsComplete { get; init; }

        /// <summary>
        /// Calculates the True Range for this candlestick.
        /// TR = max(High-Low, |High-PrevClose|, |Low-PrevClose|)
        /// </summary>
        /// <param name="previousClose">The close price of the previous candlestick.</param>
        /// <returns>The True Range value.</returns>
        public double GetTrueRange(double previousClose)
        {
            double highLow = High - Low;
            double highClose = Math.Abs(High - previousClose);
            double lowClose = Math.Abs(Low - previousClose);
            return Math.Max(highLow, Math.Max(highClose, lowClose));
        }

        /// <summary>
        /// Calculates the +DM (Positive Directional Movement) for this candlestick.
        /// +DM = High - PrevHigh (if positive and > -DM, else 0)
        /// </summary>
        public double GetPlusDM(double previousHigh, double previousLow)
        {
            double upMove = High - previousHigh;
            double downMove = previousLow - Low;
            return (upMove > downMove && upMove > 0) ? upMove : 0;
        }

        /// <summary>
        /// Calculates the -DM (Negative Directional Movement) for this candlestick.
        /// -DM = PrevLow - Low (if positive and > +DM, else 0)
        /// </summary>
        public double GetMinusDM(double previousHigh, double previousLow)
        {
            double upMove = High - previousHigh;
            double downMove = previousLow - Low;
            return (downMove > upMove && downMove > 0) ? downMove : 0;
        }

        /// <summary>
        /// Gets the candlestick's range (High - Low).
        /// </summary>
        public double Range => High - Low;

        /// <summary>
        /// Gets whether this is a bullish (green) candlestick.
        /// </summary>
        public bool IsBullish => Close > Open;

        /// <summary>
        /// Gets whether this is a bearish (red) candlestick.
        /// </summary>
        public bool IsBearish => Close < Open;

        public override string ToString() =>
            $"[{Timestamp:HH:mm}] O:{Open:F2} H:{High:F2} L:{Low:F2} C:{Close:F2} V:{Volume}";
    }
}


