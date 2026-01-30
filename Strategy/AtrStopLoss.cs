// ============================================================================
// ATR Stop Loss - Volatility-Based Stop Loss Configuration
// ============================================================================
//
// BEST PRACTICES:
// 1. Use ATR-based stops instead of fixed percentages for adaptive risk management.
// 2. The stop adjusts to current volatility - tighter in calm markets, wider in volatile ones.
// 3. Choose multiplier based on strategy type:
//    - 1.5x ATR: Tight (scalping, quick trades, more stops but less loss per trade)
//    - 2.0x ATR: Balanced (swing trading, good risk/reward)
//    - 3.0x ATR: Loose (trend following, fewer stops but larger drawdowns)
//
// USAGE:
//   .TrailingStopLoss(Atr.Multiplier(2.0))     // 2x ATR trailing stop
//   .TrailingStopLoss(Atr.Tight)               // 1.5x ATR (tight)
//   .TrailingStopLoss(Atr.Balanced)            // 2.0x ATR (balanced)
//   .TrailingStopLoss(Atr.Loose)               // 3.0x ATR (loose/trend-following)
//
// EXAMPLE:
//   ATR = $1.20
//   Multiplier = 2.0
//   Stop Distance = 2.0 × $1.20 = $2.40 below high water mark
//
// ============================================================================

namespace IdiotProof.Models
{
    /// <summary>
    /// Configuration for ATR-based (Average True Range) stop loss.
    /// ATR measures volatility and adapts stops to market conditions automatically.
    /// </summary>
    /// <remarks>
    /// <para><b>How ATR Stop Loss Works:</b></para>
    /// <list type="number">
    ///   <item>ATR is calculated from price bars (high/low/close over N periods).</item>
    ///   <item>Stop distance = ATR × Multiplier (e.g., ATR $1.20 × 2.0 = $2.40).</item>
    ///   <item>For trailing stops: Stop = HighWaterMark - (ATR × Multiplier).</item>
    ///   <item>ATR updates continuously, so stop distance adapts to volatility.</item>
    /// </list>
    /// 
    /// <para><b>Multiplier Guidelines:</b></para>
    /// <list type="table">
    ///   <item><term>1.0-1.5×</term><description>Tight - More stops, smaller losses. Good for scalping.</description></item>
    ///   <item><term>2.0×</term><description>Balanced - Standard swing trading. Good risk/reward.</description></item>
    ///   <item><term>2.5-3.0×</term><description>Loose - Trend following. Fewer stops, larger swings.</description></item>
    ///   <item><term>3.0+×</term><description>Very Loose - Long-term positions. Maximum room to breathe.</description></item>
    /// </list>
    /// </remarks>
    public sealed class AtrStopLossConfig
    {
        /// <summary>
        /// Multiplier applied to ATR to calculate stop distance.
        /// </summary>
        /// <remarks>
        /// <para><b>Formula:</b> Stop Distance = ATR × Multiplier</para>
        /// <para><b>Example:</b> If ATR = $1.20 and Multiplier = 2.0, stop is $2.40 below price.</para>
        /// </remarks>
        public double Multiplier { get; init; } = 2.0;

        /// <summary>
        /// Number of periods to use for ATR calculation.
        /// </summary>
        /// <remarks>
        /// <para><b>Common values:</b></para>
        /// <list type="bullet">
        ///   <item>14 (default) - Standard setting, good for most timeframes.</item>
        ///   <item>7-10 - More responsive to recent volatility changes.</item>
        ///   <item>20-21 - Smoother, less reactive to short-term spikes.</item>
        /// </list>
        /// </remarks>
        public int Period { get; init; } = 14;

        /// <summary>
        /// Whether this is a trailing stop (moves up with price) or fixed stop.
        /// </summary>
        public bool IsTrailing { get; init; } = true;

        /// <summary>
        /// Minimum stop distance as a percentage of price (floor).
        /// Prevents stops from being too tight in low-volatility conditions.
        /// </summary>
        /// <remarks>
        /// <para><b>Example:</b> MinStopPercent = 0.02 means stop is at least 2% away from price.</para>
        /// </remarks>
        public double MinStopPercent { get; init; } = 0.01; // 1% minimum

        /// <summary>
        /// Maximum stop distance as a percentage of price (ceiling).
        /// Prevents stops from being too wide in high-volatility conditions.
        /// </summary>
        /// <remarks>
        /// <para><b>Example:</b> MaxStopPercent = 0.25 means stop is at most 25% away from price.</para>
        /// </remarks>
        public double MaxStopPercent { get; init; } = 0.25; // 25% maximum

        /// <summary>
        /// Gets a human-readable description of this ATR stop configuration.
        /// </summary>
        public string Description => $"{Multiplier:F1}× ATR ({Period} periods){(IsTrailing ? " trailing" : " fixed")}";
    }

    /// <summary>
    /// Factory for creating ATR-based stop loss configurations.
    /// Provides preset configurations and custom multiplier support.
    /// </summary>
    /// <remarks>
    /// <para><b>Usage:</b></para>
    /// <code>
    /// .TrailingStopLoss(Atr.Balanced)           // 2.0× ATR
    /// .TrailingStopLoss(Atr.Tight)              // 1.5× ATR
    /// .TrailingStopLoss(Atr.Loose)              // 3.0× ATR
    /// .TrailingStopLoss(Atr.Multiplier(2.5))    // Custom 2.5× ATR
    /// </code>
    /// </remarks>
    public static class Atr
    {
        /// <summary>
        /// Tight ATR stop (1.5× ATR).
        /// Good for scalping and quick trades. More frequent stops but smaller losses.
        /// </summary>
        public static AtrStopLossConfig Tight => new()
        {
            Multiplier = 1.5,
            Period = 14,
            IsTrailing = true
        };

        /// <summary>
        /// Balanced ATR stop (2.0× ATR).
        /// Standard setting for swing trading. Good risk/reward balance.
        /// </summary>
        public static AtrStopLossConfig Balanced => new()
        {
            Multiplier = 2.0,
            Period = 14,
            IsTrailing = true
        };

        /// <summary>
        /// Loose ATR stop (3.0× ATR).
        /// Good for trend following. Fewer stops, allows for larger price swings.
        /// </summary>
        public static AtrStopLossConfig Loose => new()
        {
            Multiplier = 3.0,
            Period = 14,
            IsTrailing = true
        };

        /// <summary>
        /// Very loose ATR stop (4.0× ATR).
        /// For long-term positions. Maximum room for price movement.
        /// </summary>
        public static AtrStopLossConfig VeryLoose => new()
        {
            Multiplier = 4.0,
            Period = 14,
            IsTrailing = true
        };

        /// <summary>
        /// Creates a custom ATR stop loss configuration with the specified multiplier.
        /// </summary>
        /// <param name="multiplier">ATR multiplier (e.g., 2.0 for 2× ATR).</param>
        /// <param name="period">ATR period (default: 14).</param>
        /// <param name="isTrailing">Whether stop should trail price upward (default: true).</param>
        /// <returns>A configured <see cref="AtrStopLossConfig"/>.</returns>
        /// <remarks>
        /// <para><b>Example:</b></para>
        /// <code>
        /// .TrailingStopLoss(Atr.Multiplier(2.5))  // 2.5× ATR trailing stop
        /// .StopLoss(Atr.Multiplier(2.0, isTrailing: false))  // 2× ATR fixed stop
        /// </code>
        /// </remarks>
        public static AtrStopLossConfig Multiplier(double multiplier, int period = 14, bool isTrailing = true)
        {
            if (multiplier <= 0)
                throw new ArgumentOutOfRangeException(nameof(multiplier), "ATR multiplier must be positive.");
            if (period < 1)
                throw new ArgumentOutOfRangeException(nameof(period), "ATR period must be at least 1.");

            return new AtrStopLossConfig
            {
                Multiplier = multiplier,
                Period = period,
                IsTrailing = isTrailing
            };
        }

        /// <summary>
        /// Creates an ATR stop with custom min/max bounds.
        /// </summary>
        /// <param name="multiplier">ATR multiplier.</param>
        /// <param name="minStopPercent">Minimum stop distance as percentage (e.g., 0.02 for 2%).</param>
        /// <param name="maxStopPercent">Maximum stop distance as percentage (e.g., 0.20 for 20%).</param>
        /// <param name="period">ATR period (default: 14).</param>
        /// <param name="isTrailing">Whether stop should trail (default: true).</param>
        /// <returns>A configured <see cref="AtrStopLossConfig"/> with bounds.</returns>
        public static AtrStopLossConfig WithBounds(
            double multiplier,
            double minStopPercent,
            double maxStopPercent,
            int period = 14,
            bool isTrailing = true)
        {
            return new AtrStopLossConfig
            {
                Multiplier = multiplier,
                Period = period,
                IsTrailing = isTrailing,
                MinStopPercent = minStopPercent,
                MaxStopPercent = maxStopPercent
            };
        }
    }
}
