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
//       .Buy(100, Price.Current)
//       .Build();
//
// ============================================================================

using System;
using IdiotProof.Backend.Enums;

namespace IdiotProof.Backend.Models
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
        /// <remarks>
        /// <para><b>Note:</b> This method always returns true as RSI evaluation requires
        /// indicator data not available in the standard Evaluate signature. The actual
        /// RSI check is performed by the StrategyRunner using indicator data.</para>
        /// </remarks>
        public bool Evaluate(double currentPrice, double vwap)
        {
            // RSI conditions cannot be evaluated with just price/vwap
            // Return true to allow the condition chain to continue
            // Actual RSI evaluation happens in StrategyRunner with indicator data
            return true;
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
            // ADX conditions cannot be evaluated with just price/vwap
            return true;
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
            // MACD conditions cannot be evaluated with just price/vwap
            return true;
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
    /// </list>
    /// 
    /// <para><b>Common Strategy:</b></para>
    /// <para>Combine with ADX for confirmation:</para>
    /// <code>
    /// Stock.Ticker("AAPL")
    ///     .IsAdx(Comparison.Gte, 25)        // Strong trend
    ///     .IsDI(DiDirection.Positive)       // Bullish direction
    ///     .Buy(100, Price.Current)
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
        /// </summary>
        public double MinDifference { get; }

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
        /// <param name="minDifference">Minimum difference between +DI and -DI (default: 0).</param>
        public DiCondition(DiDirection direction, double minDifference = 0)
        {
            Direction = direction;
            MinDifference = Math.Max(0, minDifference);
        }

        /// <inheritdoc/>
        public bool Evaluate(double currentPrice, double vwap)
        {
            // DI conditions cannot be evaluated with just price/vwap
            return true;
        }

        /// <summary>
        /// Evaluates the DI condition against the provided +DI and -DI values.
        /// </summary>
        /// <param name="plusDI">The +DI (positive directional indicator) value.</param>
        /// <param name="minusDI">The -DI (negative directional indicator) value.</param>
        /// <returns>True if the condition is met.</returns>
        public bool EvaluateDI(double plusDI, double minusDI)
        {
            return Direction switch
            {
                DiDirection.Positive => (plusDI - minusDI) >= MinDifference,
                DiDirection.Negative => (minusDI - plusDI) >= MinDifference,
                _ => false
            };
        }
    }
}
