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
        private readonly int kPeriod;
        private readonly int dPeriod;
        private readonly Queue<double> highs;
        private readonly Queue<double> lows;
        private readonly Queue<double> closes;
        private readonly Queue<double> kValues;

        private double percentK;
        private double percentD;
        private double previousK;
        private double previousD;
        private bool isReady;

        /// <summary>
        /// Gets the %K value (fast stochastic).
        /// </summary>
        public double PercentK => percentK;

        /// <summary>
        /// Gets the %D value (slow stochastic / signal line).
        /// </summary>
        public double PercentD => percentD;

        /// <summary>
        /// Gets whether the calculator has enough data.
        /// </summary>
        public bool IsReady => isReady;

        /// <summary>
        /// Gets whether stochastic is in overbought territory (>80).
        /// </summary>
        public bool IsOverbought => isReady && percentK > 80;

        /// <summary>
        /// Gets whether stochastic is in oversold territory (<20).
        /// </summary>
        public bool IsOversold => isReady && percentK < 20;

        /// <summary>
        /// Gets whether there's a bullish crossover (%K crosses above %D in oversold zone).
        /// </summary>
        public bool IsBullishCrossover => isReady &&
            previousK <= previousD && percentK > percentD && percentK < 30;

        /// <summary>
        /// Gets whether there's a bearish crossover (%K crosses below %D in overbought zone).
        /// </summary>
        public bool IsBearishCrossover => isReady &&
            previousK >= previousD && percentK < percentD && percentK > 70;

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

            this.kPeriod = kPeriod;
            this.dPeriod = dPeriod;
            highs = new Queue<double>(kPeriod + 1);
            lows = new Queue<double>(kPeriod + 1);
            closes = new Queue<double>(kPeriod + 1);
            kValues = new Queue<double>(dPeriod + 1);
        }

        /// <summary>
        /// Updates the stochastic with a new OHLC bar.
        /// </summary>
        public void Update(double high, double low, double close)
        {
            if (high <= 0 || low <= 0 || close <= 0)
                return;

            highs.Enqueue(high);
            lows.Enqueue(low);
            closes.Enqueue(close);

            // Maintain window size
            while (highs.Count > kPeriod) highs.Dequeue();
            while (lows.Count > kPeriod) lows.Dequeue();
            while (closes.Count > kPeriod) closes.Dequeue();

            if (highs.Count < kPeriod)
            {
                isReady = false;
                return;
            }

            // Calculate %K
            double highestHigh = highs.Max();
            double lowestLow = lows.Min();
            double range = highestHigh - lowestLow;

            previousK = percentK;
            percentK = range > 0 ? 100 * (close - lowestLow) / range : 50;

            // Calculate %D (SMA of %K)
            kValues.Enqueue(percentK);
            while (kValues.Count > dPeriod) kValues.Dequeue();

            previousD = percentD;
            percentD = kValues.Average();

            isReady = kValues.Count >= dPeriod;
        }

        /// <summary>
        /// Gets a score contribution for autonomous trading (-100 to +100).
        /// </summary>
        public int GetScore()
        {
            if (!isReady)
                return 0;

            // Bullish crossover in oversold = strong buy signal
            if (IsBullishCrossover)
                return 80;

            // Bearish crossover in overbought = strong sell signal
            if (IsBearishCrossover)
                return -80;

            // Oversold = bullish potential
            if (IsOversold)
                return (int)((20 - percentK) * 3); // Max +60

            // Overbought = bearish potential
            if (IsOverbought)
                return (int)((percentK - 80) * -3); // Max -60

            // Neutral zone: slight bias based on position relative to %D
            return (int)((percentK - percentD) * 1.5);
        }
    }
}
