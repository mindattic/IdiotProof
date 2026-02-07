// ============================================================================
// Indicator Conditions - Technical indicator-based strategy conditions
// ============================================================================
//
// These conditions integrate with external indicator data providers.
// The conditions assume indicator values are provided externally via
// the TradingStrategy's IndicatorData dictionary.
//
// SUPPORTED INDICATORS:
// - RSI (Relative Strength Index): Momentum oscillator (0-100)
// - ADX (Average Directional Index): Trend strength indicator (0-100)
// - MACD (Moving Average Convergence Divergence): Trend/momentum indicator
// - DI (+DI/-DI): Directional movement indicators
//
// USAGE IN FLUENT API:
//   Stock.Ticker("AAPL")
//       .IsRsi(RsiState.Oversold)           // RSI <= 30
//       .IsAdx(Comparison.Gte, 25)          // ADX >= 25 (strong trend)
//       .IsMacd(MacdState.Bullish)          // MACD > Signal line
//       .IsDI(DiDirection.Positive)         // +DI > -DI
//       .Long(100, Price.Current)
//       .Build();
//
// ============================================================================

using System;
using IdiotProof.Backend.Enums;

namespace IdiotProof.Backend.Strategy
{
    /// <summary>
    /// Condition: RSI is in the specified state (Overbought or Oversold).
    /// </summary>
    /// <remarks>
    /// <para><b>RSI (Relative Strength Index):</b></para>
    /// <para>A momentum oscillator that measures the speed and magnitude of price changes.</para>
    /// <list type="bullet">
    ///   <item><b>Overbought (RSI >= 70):</b> May indicate the asset is overvalued and due for pullback.</item>
    ///   <item><b>Oversold (RSI &lt;= 30):</b> May indicate the asset is undervalued and due for bounce.</item>
    /// </list>
    /// 
    /// <para><b>Note:</b> This condition requires RSI data to be provided via the strategy's
    /// <see cref="TradingStrategy.GetIndicatorValue"/> method.</para>
    /// </remarks>
    public sealed class RsiCondition : IStrategyCondition
    {
        /// <summary>
        /// Default RSI overbought threshold.
        /// </summary>
        public const double DefaultOverboughtThreshold = 70.0;

        /// <summary>
        /// Default RSI oversold threshold.
        /// </summary>
        public const double DefaultOversoldThreshold = 30.0;

        /// <summary>
        /// Gets the RSI state being checked.
        /// </summary>
        public RsiState State { get; }

        /// <summary>
        /// Gets the threshold value for the condition.
        /// </summary>
        public double Threshold { get; }

        /// <summary>
        /// Gets or sets the callback to retrieve the current RSI value.
        /// This is set by the StrategyRunner when it initializes the RSI calculator.
        /// </summary>
        public Func<double>? GetRsiValue { get; set; }

        /// <inheritdoc/>
        public string Name => State switch
        {
            RsiState.Overbought => $"RSI >= {Threshold:F0} (Overbought)",
            RsiState.Oversold => $"RSI <= {Threshold:F0} (Oversold)",
            _ => $"RSI {State}"
        };

        /// <summary>
        /// Creates a new RSI condition.
        /// </summary>
        /// <param name="state">The RSI state to check for.</param>
        /// <param name="threshold">Custom threshold (default: 70 for overbought, 30 for oversold).</param>
        public RsiCondition(RsiState state, double? threshold = null)
        {
            State = state;
            Threshold = threshold ?? (state == RsiState.Overbought ? DefaultOverboughtThreshold : DefaultOversoldThreshold);
        }

        /// <inheritdoc/>
        public bool Evaluate(double currentPrice, double vwap)
        {
            // If no RSI callback is set, return false (condition not met)
            if (GetRsiValue == null)
                return false;

            double rsiValue = GetRsiValue();

            // RSI not ready yet (not enough data)
            if (rsiValue <= 0)
                return false;

            return EvaluateRsi(rsiValue);
        }

        /// <summary>
        /// Evaluates the RSI condition against the provided RSI value.
        /// </summary>
        /// <param name="rsiValue">The current RSI value (0-100).</param>
        /// <returns>True if the condition is met.</returns>
        public bool EvaluateRsi(double rsiValue)
        {
            return State switch
            {
                RsiState.Overbought => rsiValue >= Threshold,
                RsiState.Oversold => rsiValue <= Threshold,
                _ => false
            };
        }
    }


    /// <summary>
    /// Condition: ADX (Average Directional Index) comparison against a threshold.
    /// </summary>
    /// <remarks>
    /// <para><b>ADX (Average Directional Index):</b></para>
    /// <para>Measures trend strength (not direction) on a scale of 0-100.</para>
    /// <list type="bullet">
    ///   <item><b>ADX &lt; 20:</b> Weak or no trend (ranging market)</item>
    ///   <item><b>ADX 20-25:</b> Trend may be developing</item>
    ///   <item><b>ADX 25-50:</b> Strong trend</item>
    ///   <item><b>ADX 50-75:</b> Very strong trend</item>
    ///   <item><b>ADX > 75:</b> Extremely strong trend (rare)</item>
    /// </list>
    /// 
    /// <para><b>Common Use Cases:</b></para>
    /// <list type="bullet">
    ///   <item><c>.IsAdx(Comparison.Gte, 25)</c> - Only trade when trend is strong</item>
    ///   <item><c>.IsAdx(Comparison.Lte, 20)</c> - Only trade in ranging markets</item>
    /// </list>
    /// </remarks>
    public sealed class AdxCondition : IStrategyCondition
    {
        /// <summary>
        /// Gets the comparison operator.
        /// </summary>
        public Comparison Comparison { get; }

        /// <summary>
        /// Gets the threshold value.
        /// </summary>
        public double Threshold { get; }

        /// <summary>
        /// Gets or sets the callback to retrieve the current ADX value.
        /// This is set by the StrategyRunner when it initializes the ADX calculator.
        /// </summary>
        public Func<double>? GetAdxValue { get; set; }

        /// <inheritdoc/>
        public string Name => Comparison switch
        {
            Comparison.Gte => $"ADX >= {Threshold:F0}",
            Comparison.Lte => $"ADX <= {Threshold:F0}",
            Comparison.Gt => $"ADX > {Threshold:F0}",
            Comparison.Lt => $"ADX < {Threshold:F0}",
            Comparison.Eq => $"ADX == {Threshold:F0}",
            _ => $"ADX {Comparison} {Threshold:F0}"
        };

        /// <summary>
        /// Creates a new ADX condition.
        /// </summary>
        /// <param name="comparison">The comparison operator.</param>
        /// <param name="threshold">The ADX threshold value (0-100).</param>
        /// <exception cref="ArgumentOutOfRangeException">If threshold is outside valid range.</exception>
        public AdxCondition(Comparison comparison, double threshold)
        {
            if (threshold < 0 || threshold > 100)
                throw new ArgumentOutOfRangeException(nameof(threshold), "ADX threshold must be between 0 and 100.");

            Comparison = comparison;
            Threshold = threshold;
        }

        /// <inheritdoc/>
        public bool Evaluate(double currentPrice, double vwap)
        {
            // If no ADX callback is set, return false (condition not met)
            if (GetAdxValue == null)
                return false;

            double adxValue = GetAdxValue();

            // ADX not ready yet (not enough data)
            if (adxValue <= 0)
                return false;

            return EvaluateAdx(adxValue);
        }

        /// <summary>
        /// Evaluates the ADX condition against the provided ADX value.
        /// </summary>
        /// <param name="adxValue">The current ADX value (0-100).</param>
        /// <returns>True if the condition is met.</returns>
        public bool EvaluateAdx(double adxValue)
        {
            return Comparison switch
            {
                Comparison.Gte => adxValue >= Threshold,
                Comparison.Lte => adxValue <= Threshold,
                Comparison.Gt => adxValue > Threshold,
                Comparison.Lt => adxValue < Threshold,
                Comparison.Eq => Math.Abs(adxValue - Threshold) < 0.001,
                _ => false
            };
        }
    }

    /// <summary>
    /// Condition: MACD is in the specified state.
    /// </summary>
    /// <remarks>
    /// <para><b>MACD (Moving Average Convergence Divergence):</b></para>
    /// <para>A trend-following momentum indicator showing the relationship between two EMAs.</para>
    /// 
    /// <para><b>Components:</b></para>
    /// <list type="bullet">
    ///   <item><b>MACD Line:</b> 12-period EMA - 26-period EMA</item>
    ///   <item><b>Signal Line:</b> 9-period EMA of MACD line</item>
    ///   <item><b>Histogram:</b> MACD Line - Signal Line</item>
    /// </list>
    /// 
    /// <para><b>Signals:</b></para>
    /// <list type="bullet">
    ///   <item><b>Bullish:</b> MACD crosses above signal (buy)</item>
    ///   <item><b>Bearish:</b> MACD crosses below signal (sell)</item>
    ///   <item><b>AboveZero:</b> MACD positive (uptrend)</item>
    ///   <item><b>BelowZero:</b> MACD negative (downtrend)</item>
    /// </list>
    /// </remarks>
    public sealed class MacdCondition : IStrategyCondition
    {
        /// <summary>
        /// Gets the MACD state being checked.
        /// </summary>
        public MacdState State { get; }

        /// <summary>
        /// Gets or sets the callback to retrieve MACD values.
        /// Returns tuple of (MacdLine, SignalLine, Histogram, PreviousHistogram).
        /// </summary>
        public Func<(double MacdLine, double SignalLine, double Histogram, double PreviousHistogram)>? GetMacdValues { get; set; }

        /// <inheritdoc/>
        public string Name => State switch
        {
            MacdState.Bullish => "MACD > Signal (Bullish)",
            MacdState.Bearish => "MACD < Signal (Bearish)",
            MacdState.AboveZero => "MACD > 0 (Uptrend)",
            MacdState.BelowZero => "MACD < 0 (Downtrend)",
            MacdState.HistogramRising => "MACD Histogram Rising",
            MacdState.HistogramFalling => "MACD Histogram Falling",
            _ => $"MACD {State}"
        };

        /// <summary>
        /// Creates a new MACD condition.
        /// </summary>
        /// <param name="state">The MACD state to check for.</param>
        public MacdCondition(MacdState state)
        {
            State = state;
        }

        /// <inheritdoc/>
        public bool Evaluate(double currentPrice, double vwap)
        {
            // If no MACD callback is set, return false (condition not met)
            if (GetMacdValues == null)
                return false;

            var (macdLine, signalLine, histogram, previousHistogram) = GetMacdValues();

            return EvaluateMacd(macdLine, signalLine, histogram, previousHistogram);
        }

        /// <summary>
        /// Evaluates the MACD condition against the provided MACD values.
        /// </summary>
        /// <param name="macdLine">The MACD line value.</param>
        /// <param name="signalLine">The signal line value.</param>
        /// <param name="histogram">The histogram value (optional, for histogram conditions).</param>
        /// <param name="previousHistogram">Previous histogram value (for rising/falling checks).</param>
        /// <returns>True if the condition is met.</returns>
        public bool EvaluateMacd(double macdLine, double signalLine, double histogram = 0, double? previousHistogram = null)
        {
            return State switch
            {
                MacdState.Bullish => macdLine > signalLine,
                MacdState.Bearish => macdLine < signalLine,
                MacdState.AboveZero => macdLine > 0,
                MacdState.BelowZero => macdLine < 0,
                MacdState.HistogramRising => previousHistogram.HasValue && histogram > previousHistogram.Value,
                MacdState.HistogramFalling => previousHistogram.HasValue && histogram < previousHistogram.Value,
                _ => false
            };
        }
    }

    /// <summary>
    /// Condition: Directional Indicator (+DI/-DI) relationship.
    /// </summary>
    /// <remarks>
    /// <para><b>Directional Indicators (+DI/-DI):</b></para>
    /// <para>Components of the ADX system that show trend direction.</para>
    /// 
    /// <para><b>Interpretation:</b></para>
    /// <list type="bullet">
    ///   <item><b>+DI > -DI (Positive):</b> Bullish pressure dominates, upward trend</item>
    ///   <item><b>-DI > +DI (Negative):</b> Bearish pressure dominates, downward trend</item>
    ///   <item><b>Equal values:</b> No direction dominates, condition returns false</item>
    /// </list>
    /// 
    /// <para><b>MinDifference Parameter:</b></para>
    /// <para>When MinDifference is specified, the dominant DI must exceed the other by at least that amount.</para>
    /// <para>For example, with MinDifference=5 and DiDirection.Positive:</para>
    /// <list type="bullet">
    ///   <item>+DI=30, -DI=25: Returns true (difference of 5 meets threshold)</item>
    ///   <item>+DI=29, -DI=25: Returns false (difference of 4 below threshold)</item>
    ///   <item>+DI=25, -DI=25: Returns false (no dominance, equal values)</item>
    /// </list>
    /// 
    /// <para><b>Common Strategy:</b></para>
    /// <para>Combine with ADX for confirmation:</para>
    /// <code>
    /// Stock.Ticker("AAPL")
    ///     .IsAdx(Comparison.Gte, 25)        // Strong trend
    ///     .IsDI(DiDirection.Positive)       // Bullish direction (+DI > -DI)
    ///     .Long(100, Price.Current)
    ///     .Build();
    /// 
    /// // With minimum difference requirement:
    /// Stock.Ticker("AAPL")
    ///     .IsAdx(Comparison.Gte, 25)        // Strong trend
    ///     .IsDI(DiDirection.Positive, 5)    // +DI must exceed -DI by at least 5
    ///     .Long(100, Price.Current)
    ///     .Build();
    /// </code>
    /// </remarks>
    public sealed class DiCondition : IStrategyCondition
    {
        /// <summary>
        /// Gets the DI direction being checked.
        /// </summary>
        public DiDirection Direction { get; }

        /// <summary>
        /// Gets the minimum difference required between +DI and -DI.
        /// The dominant DI must exceed the other by at least this amount.
        /// </summary>
        public double MinDifference { get; }

        /// <summary>
        /// Gets or sets the callback to retrieve +DI and -DI values.
        /// Returns tuple of (PlusDI, MinusDI).
        /// </summary>
        public Func<(double PlusDI, double MinusDI)>? GetDiValues { get; set; }

        /// <inheritdoc/>
        public string Name => Direction switch
        {
            DiDirection.Positive when MinDifference > 0 => $"+DI > -DI by {MinDifference:F0}+ (Bullish)",
            DiDirection.Positive => "+DI > -DI (Bullish)",
            DiDirection.Negative when MinDifference > 0 => $"-DI > +DI by {MinDifference:F0}+ (Bearish)",
            DiDirection.Negative => "-DI > +DI (Bearish)",
            _ => $"DI {Direction}"
        };

        /// <summary>
        /// Creates a new DI condition.
        /// </summary>
        /// <param name="direction">The DI direction to check for.</param>
        /// <param name="minDifference">
        /// Minimum difference between +DI and -DI (default: 0).
        /// The dominant DI must be strictly greater than the other,
        /// and the difference must be at least this value.
        /// Negative values are clamped to 0.
        /// </param>
        public DiCondition(DiDirection direction, double minDifference = 0)
        {
            Direction = direction;
            MinDifference = Math.Max(0, minDifference);
        }

        /// <inheritdoc/>
        public bool Evaluate(double currentPrice, double vwap)
        {
            // If no DI callback is set, return false (condition not met)
            if (GetDiValues == null)
                return false;

            var (plusDI, minusDI) = GetDiValues();

            // DI not ready yet (values are 0)
            if (plusDI <= 0 && minusDI <= 0)
                return false;

            return EvaluateDI(plusDI, minusDI);
        }

        /// <summary>
        /// Evaluates the DI condition against the provided +DI and -DI values.
        /// </summary>
        /// <param name="plusDI">The +DI (positive directional indicator) value.</param>
        /// <param name="minusDI">The -DI (negative directional indicator) value.</param>
        /// <returns>
        /// True if the specified direction dominates (strictly greater) and the difference
        /// meets the MinDifference threshold. Returns false when values are equal.
        /// </returns>
        public bool EvaluateDI(double plusDI, double minusDI)
        {
            return Direction switch
            {
                DiDirection.Positive => plusDI > minusDI && (plusDI - minusDI) >= MinDifference,
                DiDirection.Negative => minusDI > plusDI && (minusDI - plusDI) >= MinDifference,
                _ => false
            };
        }
    }

    /// <summary>
    /// Condition: Price is above or equal to the specified EMA (Exponential Moving Average).
    /// </summary>
    /// <remarks>
    /// <para><b>EMA (Exponential Moving Average):</b></para>
    /// <para>A moving average that gives more weight to recent prices, making it more responsive.</para>
    /// 
    /// <para><b>Common Periods:</b></para>
    /// <list type="bullet">
    ///   <item><b>9 EMA:</b> Short-term trend, very responsive</item>
    ///   <item><b>21 EMA:</b> Medium-term trend</item>
    ///   <item><b>50 EMA:</b> Intermediate trend</item>
    ///   <item><b>200 EMA:</b> Long-term trend, major support/resistance</item>
    /// </list>
    /// 
    /// <para><b>Trading Signals:</b></para>
    /// <list type="bullet">
    ///   <item>Price above EMA: Bullish bias</item>
    ///   <item>Price below EMA: Bearish bias</item>
    ///   <item>Price at EMA: Potential support/resistance</item>
    /// </list>
    /// </remarks>
    public sealed class EmaAboveCondition : IStrategyCondition
    {
        /// <summary>
        /// Gets the EMA period for this condition.
        /// </summary>
        public int Period { get; }

        /// <summary>
        /// Gets or sets the callback to retrieve the current EMA value.
        /// This is set by the StrategyRunner when it initializes EMA calculators.
        /// </summary>
        public Func<double>? GetEmaValue { get; set; }

        /// <inheritdoc/>
        public string Name => $"Price >= EMA({Period})";

        /// <summary>
        /// Creates a new EMA above condition.
        /// </summary>
        /// <param name="period">The EMA period (e.g., 9, 21, 200).</param>
        public EmaAboveCondition(int period)
        {
            if (period < 1)
                throw new ArgumentOutOfRangeException(nameof(period), "Period must be at least 1.");
            Period = period;
        }

        /// <inheritdoc/>
        public bool Evaluate(double currentPrice, double vwap)
        {
            // If no EMA callback is set, return false (condition not met)
            // This ensures we don't accidentally trigger on uninitialized conditions
            if (GetEmaValue == null)
                return false;

            double emaValue = GetEmaValue();

            // EMA not ready yet (not enough data)
            if (emaValue <= 0)
                return false;

            return currentPrice >= emaValue;
        }
    }

    /// <summary>
    /// Condition: Price is below or equal to the specified EMA (Exponential Moving Average).
    /// </summary>
    public sealed class EmaBelowCondition : IStrategyCondition
    {
        /// <summary>
        /// Gets the EMA period for this condition.
        /// </summary>
        public int Period { get; }

        /// <summary>
        /// Gets or sets the callback to retrieve the current EMA value.
        /// This is set by the StrategyRunner when it initializes EMA calculators.
        /// </summary>
        public Func<double>? GetEmaValue { get; set; }

        /// <inheritdoc/>
        public string Name => $"Price <= EMA({Period})";

        /// <summary>
        /// Creates a new EMA below condition.
        /// </summary>
        /// <param name="period">The EMA period (e.g., 9, 21, 200).</param>
        public EmaBelowCondition(int period)
        {
            if (period < 1)
                throw new ArgumentOutOfRangeException(nameof(period), "Period must be at least 1.");
            Period = period;
        }

        /// <inheritdoc/>
        public bool Evaluate(double currentPrice, double vwap)
        {
            if (GetEmaValue == null)
                return false;

            double emaValue = GetEmaValue();

            if (emaValue <= 0)
                return false;

            return currentPrice <= emaValue;
        }
    }

    /// <summary>
    /// Condition: Price is between two EMAs.
    /// </summary>
    public sealed class EmaBetweenCondition : IStrategyCondition
    {
        /// <summary>
        /// Gets the lower EMA period.
        /// </summary>
        public int LowerPeriod { get; }

        /// <summary>
        /// Gets the upper EMA period.
        /// </summary>
        public int UpperPeriod { get; }

        /// <summary>
        /// Gets or sets the callback to retrieve the lower EMA value.
        /// </summary>
        public Func<double>? GetLowerEmaValue { get; set; }

        /// <summary>
        /// Gets or sets the callback to retrieve the upper EMA value.
        /// </summary>
        public Func<double>? GetUpperEmaValue { get; set; }

        /// <inheritdoc/>
        public string Name => $"Price between EMA({LowerPeriod}) and EMA({UpperPeriod})";

        /// <summary>
        /// Creates a new EMA between condition.
        /// </summary>
        public EmaBetweenCondition(int lowerPeriod, int upperPeriod)
        {
            if (lowerPeriod < 1)
                throw new ArgumentOutOfRangeException(nameof(lowerPeriod), "Period must be at least 1.");
            if (upperPeriod < 1)
                throw new ArgumentOutOfRangeException(nameof(upperPeriod), "Period must be at least 1.");

            LowerPeriod = lowerPeriod;
            UpperPeriod = upperPeriod;
        }

        /// <inheritdoc/>
        public bool Evaluate(double currentPrice, double vwap)
        {
            if (GetLowerEmaValue == null || GetUpperEmaValue == null)
                return false;

            double lowerEma = GetLowerEmaValue();
            double upperEma = GetUpperEmaValue();

            if (lowerEma <= 0 || upperEma <= 0)
                return false;

            // Price should be above the lower EMA and below the upper EMA
            double minEma = Math.Min(lowerEma, upperEma);
            double maxEma = Math.Max(lowerEma, upperEma);

            return currentPrice >= minEma && currentPrice <= maxEma;
        }
    }

    /// <summary>
    /// Condition: EMA is turning up (slope is positive).
    /// Detects when a shorter-term moving average is flattening or starting to rise.
    /// </summary>
    public sealed class EmaTurningUpCondition : IStrategyCondition
    {
        /// <summary>
        /// Gets the EMA period for this condition.
        /// </summary>
        public int Period { get; }

        /// <summary>
        /// Gets or sets the callback to retrieve the current EMA value.
        /// </summary>
        public Func<double>? GetCurrentEmaValue { get; set; }

        /// <summary>
        /// Gets or sets the callback to retrieve the previous EMA value (1 bar ago).
        /// </summary>
        public Func<double>? GetPreviousEmaValue { get; set; }

        /// <inheritdoc/>
        public string Name => $"EMA({Period}) Turning Up";

        /// <summary>
        /// Creates a new EMA turning up condition.
        /// </summary>
        public EmaTurningUpCondition(int period)
        {
            if (period < 1)
                throw new ArgumentOutOfRangeException(nameof(period), "Period must be at least 1.");
            Period = period;
        }

        /// <inheritdoc/>
        public bool Evaluate(double currentPrice, double vwap)
        {
            if (GetCurrentEmaValue == null || GetPreviousEmaValue == null)
                return false;

            double currentEma = GetCurrentEmaValue();
            double previousEma = GetPreviousEmaValue();

            if (currentEma <= 0 || previousEma <= 0)
                return false;

            // EMA is turning up when current value >= previous value (slope >= 0)
            return currentEma >= previousEma;
        }
    }

    /// <summary>
    /// Condition: Higher lows are forming (ascending support pattern).
    /// Looks at recent price action to detect a bullish pattern.
    /// </summary>
    public sealed class HigherLowsCondition : IStrategyCondition
    {
        /// <summary>
        /// Number of bars to analyze for the pattern.
        /// </summary>
        public int LookbackBars { get; }

        /// <summary>
        /// Gets or sets the callback to retrieve recent low values.
        /// Returns an array of low prices, most recent first.
        /// </summary>
        public Func<double[]>? GetRecentLows { get; set; }

        /// <inheritdoc/>
        public string Name => "Higher Lows Forming";

        /// <summary>
        /// Creates a new higher lows condition.
        /// </summary>
        public HigherLowsCondition(int lookbackBars = 3)
        {
            if (lookbackBars < 2)
                throw new ArgumentOutOfRangeException(nameof(lookbackBars), "Lookback must be at least 2.");
            LookbackBars = lookbackBars;
        }

        /// <inheritdoc/>
        public bool Evaluate(double currentPrice, double vwap)
        {
            if (GetRecentLows == null)
                return false;

            var lows = GetRecentLows();
            if (lows == null || lows.Length < 2)
                return false;

            // Check if each low is higher than the previous (ascending pattern)
            for (int i = 0; i < lows.Length - 1; i++)
            {
                if (lows[i] <= lows[i + 1])
                    return false;
            }

            return true;
        }
    }

    /// <summary>
    /// Condition: Lower highs are forming (descending resistance pattern).
    /// Looks at recent price action to detect a bearish pattern.
    /// </summary>
    public sealed class LowerHighsCondition : IStrategyCondition
    {
        /// <summary>
        /// Number of bars to analyze for the pattern.
        /// </summary>
        public int LookbackBars { get; }

        /// <summary>
        /// Gets or sets the callback to retrieve recent high values.
        /// Returns an array of high prices, most recent first.
        /// </summary>
        public Func<double[]>? GetRecentHighs { get; set; }

        /// <inheritdoc/>
        public string Name => "Lower Highs Forming";

        /// <summary>
        /// Creates a new lower highs condition.
        /// </summary>
        public LowerHighsCondition(int lookbackBars = 3)
        {
            if (lookbackBars < 2)
                throw new ArgumentOutOfRangeException(nameof(lookbackBars), "Lookback must be at least 2.");
            LookbackBars = lookbackBars;
        }

        /// <inheritdoc/>
        public bool Evaluate(double currentPrice, double vwap)
        {
            if (GetRecentHighs == null)
                return false;

            var highs = GetRecentHighs();
            if (highs == null || highs.Length < 2)
                return false;

            // Check if each high is lower than the previous (descending pattern)
            for (int i = 0; i < highs.Length - 1; i++)
            {
                if (highs[i] >= highs[i + 1])
                    return false;
            }

            return true;
        }
    }

    /// <summary>
    /// Condition: Volume is above a specified multiple of the average volume.
    /// Detects volume spikes which often confirm breakouts.
    /// </summary>
    public sealed class VolumeAboveCondition : IStrategyCondition
    {
        /// <summary>
        /// The volume multiplier threshold.
        /// </summary>
        public double Multiplier { get; }

        /// <summary>
        /// Gets or sets the callback to retrieve the current volume.
        /// </summary>
        public Func<long>? GetCurrentVolume { get; set; }

        /// <summary>
        /// Gets or sets the callback to retrieve the average volume.
        /// </summary>
        public Func<double>? GetAverageVolume { get; set; }

        /// <inheritdoc/>
        public string Name => $"Volume >= {Multiplier:F1}x Average";

        /// <summary>
        /// Creates a new volume above condition.
        /// </summary>
        public VolumeAboveCondition(double multiplier)
        {
            if (multiplier <= 0)
                throw new ArgumentOutOfRangeException(nameof(multiplier), "Multiplier must be positive.");
            Multiplier = multiplier;
        }

        /// <inheritdoc/>
        public bool Evaluate(double currentPrice, double vwap)
        {
            if (GetCurrentVolume == null || GetAverageVolume == null)
                return false;

            long currentVolume = GetCurrentVolume();
            double averageVolume = GetAverageVolume();

            if (currentVolume <= 0 || averageVolume <= 0)
                return false;

            return currentVolume >= averageVolume * Multiplier;
        }
    }

    /// <summary>
    /// Condition: The candle closed above VWAP (not just current price).
    /// Stronger signal than just price above VWAP.
    /// </summary>
    public sealed class CloseAboveVwapCondition : IStrategyCondition
    {
        /// <summary>
        /// Gets or sets the callback to retrieve the last candle close price.
        /// </summary>
        public Func<double>? GetLastClose { get; set; }

        /// <inheritdoc/>
        public string Name => "Close Above VWAP";

        /// <inheritdoc/>
        public bool Evaluate(double currentPrice, double vwap)
        {
            if (GetLastClose == null)
                return false;

            double lastClose = GetLastClose();

            if (lastClose <= 0 || vwap <= 0)
                return false;

            return lastClose > vwap;
        }
    }

    /// <summary>
    /// Condition: VWAP rejection detected (wick above VWAP but close below).
    /// Indicates a failed VWAP reclaim - bearish signal.
    /// </summary>
    public sealed class VwapRejectionCondition : IStrategyCondition
    {
        /// <summary>
        /// Gets or sets the callback to retrieve the last candle high.
        /// </summary>
        public Func<double>? GetLastHigh { get; set; }

        /// <summary>
        /// Gets or sets the callback to retrieve the last candle close.
        /// </summary>
        public Func<double>? GetLastClose { get; set; }

        /// <inheritdoc/>
        public string Name => "VWAP Rejection";

        /// <inheritdoc/>
        public bool Evaluate(double currentPrice, double vwap)
        {
            if (GetLastHigh == null || GetLastClose == null)
                return false;

            double lastHigh = GetLastHigh();
            double lastClose = GetLastClose();

            if (lastHigh <= 0 || lastClose <= 0 || vwap <= 0)
                return false;

            // VWAP rejection: high went above VWAP but close is below VWAP
            return lastHigh > vwap && lastClose < vwap;
        }
    }

    // ========================================================================
    // MOMENTUM CONDITIONS
    // ========================================================================

    /// <summary>
    /// Condition: Momentum is above the specified threshold.
    /// </summary>
    /// <remarks>
    /// <para><b>Momentum Indicator:</b></para>
    /// <para>Measures the rate of price change by comparing current price to a previous price.</para>
    /// <para>Formula: Momentum = Current Price - Price N periods ago</para>
    /// 
    /// <para><b>Interpretation:</b></para>
    /// <list type="bullet">
    ///   <item><b>Positive Momentum (> 0):</b> Price is higher than N periods ago (uptrend)</item>
    ///   <item><b>Negative Momentum (&lt; 0):</b> Price is lower than N periods ago (downtrend)</item>
    ///   <item><b>Increasing Momentum:</b> Trend is accelerating</item>
    ///   <item><b>Decreasing Momentum:</b> Trend is weakening</item>
    /// </list>
    /// 
    /// <para><b>ASCII Visualization:</b></para>
    /// <code>
    ///     Momentum Above 0 (Bullish)
    ///     ┌────────────────────────────────────────┐
    ///     │     /\                    /\           │
    ///     │    /  \                  /  \    Price │
    ///     │   /    \                /    \         │
    ///     │──/──────\──────────────/──────\────────│ Zero Line
    ///     │          \            /        \       │
    ///     │           \          /          \      │
    ///     │            \________/                  │
    ///     └────────────────────────────────────────┘
    ///           ↑ Momentum > 0     ↑ Momentum > 0
    /// </code>
    /// </remarks>
    public sealed class MomentumAboveCondition : IStrategyCondition
    {
        /// <summary>
        /// Gets the momentum threshold.
        /// </summary>
        public double Threshold { get; }

        /// <summary>
        /// Gets or sets the callback to retrieve the current momentum value.
        /// </summary>
        public Func<double>? GetMomentumValue { get; set; }

        /// <inheritdoc/>
        public string Name => $"Momentum >= {Threshold:F2}";

        /// <summary>
        /// Creates a new momentum above condition.
        /// </summary>
        public MomentumAboveCondition(double threshold)
        {
            Threshold = threshold;
        }

        /// <inheritdoc/>
        public bool Evaluate(double currentPrice, double vwap)
        {
            if (GetMomentumValue == null)
                return false;

            double momentum = GetMomentumValue();
            return momentum >= Threshold;
        }
    }

    /// <summary>
    /// Condition: Momentum is below the specified threshold.
    /// </summary>
    /// <remarks>
    /// <para><b>Momentum Indicator (Bearish):</b></para>
    /// <para>Used to detect downward price momentum or weakening bullish momentum.</para>
    /// 
    /// <para><b>ASCII Visualization:</b></para>
    /// <code>
    ///     Momentum Below 0 (Bearish)
    ///     ┌────────────────────────────────────────┐
    ///     │            /\                          │
    ///     │           /  \                         │
    ///     │──────────/────\────────────────────────│ Zero Line
    ///     │         /      \                       │
    ///     │    ____/        \____       Momentum   │
    ///     │   /                  \      Below 0    │
    ///     │  /                    \                │
    ///     └────────────────────────────────────────┘
    ///              ↑ Bearish Momentum
    /// </code>
    /// </remarks>
    public sealed class MomentumBelowCondition : IStrategyCondition
    {
        /// <summary>
        /// Gets the momentum threshold.
        /// </summary>
        public double Threshold { get; }

        /// <summary>
        /// Gets or sets the callback to retrieve the current momentum value.
        /// </summary>
        public Func<double>? GetMomentumValue { get; set; }

        /// <inheritdoc/>
        public string Name => $"Momentum <= {Threshold:F2}";

        /// <summary>
        /// Creates a new momentum below condition.
        /// </summary>
        public MomentumBelowCondition(double threshold)
        {
            Threshold = threshold;
        }

        /// <inheritdoc/>
        public bool Evaluate(double currentPrice, double vwap)
        {
            if (GetMomentumValue == null)
                return false;

            double momentum = GetMomentumValue();
            return momentum <= Threshold;
        }
    }

    /// <summary>
    /// Condition: Rate of Change (ROC) is above the specified threshold.
    /// </summary>
    /// <remarks>
    /// <para><b>Rate of Change (ROC):</b></para>
    /// <para>Measures the percentage change in price over N periods.</para>
    /// <para>Formula: ROC = ((Current Price - Price N periods ago) / Price N periods ago) × 100</para>
    /// 
    /// <para><b>Interpretation:</b></para>
    /// <list type="bullet">
    ///   <item><b>ROC > 0:</b> Price has increased over the period</item>
    ///   <item><b>ROC &lt; 0:</b> Price has decreased over the period</item>
    ///   <item><b>Rising ROC:</b> Momentum is increasing</item>
    ///   <item><b>Falling ROC:</b> Momentum is decreasing</item>
    /// </list>
    /// 
    /// <para><b>ASCII Visualization:</b></para>
    /// <code>
    ///     ROC Above 2% (Strong Bullish Momentum)
    ///     ┌────────────────────────────────────────┐
    ///     │  +5% ──────────────── Strong bullish   │
    ///     │  +2% ═════════════════════════════════ │ ← Threshold
    ///     │   0% ──────────────────────────────────│ Zero Line
    ///     │  -2% ═════════════════════════════════ │
    ///     │  -5% ──────────────── Strong bearish   │
    ///     └────────────────────────────────────────┘
    /// </code>
    /// </remarks>
    public sealed class RocAboveCondition : IStrategyCondition
    {
        /// <summary>
        /// Gets the ROC threshold (percentage).
        /// </summary>
        public double Threshold { get; }

        /// <summary>
        /// Gets or sets the callback to retrieve the current ROC value.
        /// </summary>
        public Func<double>? GetRocValue { get; set; }

        /// <inheritdoc/>
        public string Name => $"ROC >= {Threshold:F1}%";

        /// <summary>
        /// Creates a new ROC above condition.
        /// </summary>
        public RocAboveCondition(double threshold)
        {
            Threshold = threshold;
        }

        /// <inheritdoc/>
        public bool Evaluate(double currentPrice, double vwap)
        {
            if (GetRocValue == null)
                return false;

            double roc = GetRocValue();
            return roc >= Threshold;
        }
    }

    /// <summary>
    /// Condition: Rate of Change (ROC) is below the specified threshold.
    /// </summary>
    /// <remarks>
    /// <para><b>ROC Below Threshold (Bearish):</b></para>
    /// <para>Detects negative or weakening momentum.</para>
    /// 
    /// <para><b>ASCII Visualization:</b></para>
    /// <code>
    ///     ROC Below -2% (Strong Bearish Momentum)
    ///     ┌────────────────────────────────────────┐
    ///     │  +5% ──────────────────────────────────│
    ///     │   0% ──────────────────────────────────│ Zero Line
    ///     │  -2% ═════════════════════════════════ │ ← Threshold
    ///     │  -5% ──────────────────────────────────│
    ///     │       ↓ ROC must be below this line    │
    ///     └────────────────────────────────────────┘
    /// </code>
    /// </remarks>
    public sealed class RocBelowCondition : IStrategyCondition
    {
        /// <summary>
        /// Gets the ROC threshold (percentage).
        /// </summary>
        public double Threshold { get; }

        /// <summary>
        /// Gets or sets the callback to retrieve the current ROC value.
        /// </summary>
        public Func<double>? GetRocValue { get; set; }

        /// <inheritdoc/>
        public string Name => $"ROC <= {Threshold:F1}%";

        /// <summary>
        /// Creates a new ROC below condition.
        /// </summary>
        public RocBelowCondition(double threshold)
        {
            Threshold = threshold;
        }

        /// <inheritdoc/>
        public bool Evaluate(double currentPrice, double vwap)
        {
            if (GetRocValue == null)
                return false;

            double roc = GetRocValue();
            return roc <= Threshold;
        }
    }
}


