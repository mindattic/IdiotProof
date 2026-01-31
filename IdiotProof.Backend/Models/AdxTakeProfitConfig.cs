// ============================================================================
// ADX Take Profit Configuration - Dynamic TP based on trend strength
// ============================================================================
//
// ADX (Average Directional Index) measures trend strength, not direction.
// Use it to determine how aggressively to take profits:
//
// ADX < 15:      No trend (choppy) → Take profit quickly at conservative target
// ADX 15-25:     Developing trend  → Partial at midpoint, rest at range high
// ADX 25-35:     Strong trend      → Target range high or let runner go
// ADX > 35:      Very strong       → Scale out into strength, watch for reversal
//
// BEST PRACTICE:
// The most useful TP trigger is when ADX peaks and turns down - momentum fading.
//
// ============================================================================

namespace IdiotProof.Backend.Models
{
    /// <summary>
    /// Configuration for ADX-based dynamic take profit levels.
    /// </summary>
    /// <remarks>
    /// <para><b>How ADX Affects Take Profit:</b></para>
    /// <list type="table">
    ///   <listheader><term>ADX Range</term><description>Take Profit Strategy</description></listheader>
    ///   <item><term>&lt; 15 (No Trend)</term><description>Take profit at <see cref="ConservativeTarget"/> (usually midpoint)</description></item>
    ///   <item><term>15-25 (Developing)</term><description>Partial at midpoint, final at <see cref="AggressiveTarget"/></description></item>
    ///   <item><term>25-35 (Strong)</term><description>Target <see cref="AggressiveTarget"/> or use trailing stop</description></item>
    ///   <item><term>&gt; 35 (Very Strong)</term><description>Scale out aggressively, watch for ADX rollover</description></item>
    /// </list>
    /// 
    /// <para><b>ADX Rollover Signal:</b></para>
    /// <para>When ADX stops rising and begins to fall, momentum is fading. This is often
    /// a better exit signal than hitting a fixed price target.</para>
    /// </remarks>
    public sealed class AdxTakeProfitConfig
    {
        /// <summary>
        /// Conservative take profit target (used when ADX indicates weak/no trend).
        /// Typically the midpoint of the expected price range.
        /// </summary>
        /// <remarks>
        /// <para><b>When Used:</b> ADX &lt; <see cref="WeakTrendThreshold"/></para>
        /// <para><b>Rationale:</b> In choppy markets, take what you can get quickly.</para>
        /// </remarks>
        public required double ConservativeTarget { get; init; }

        /// <summary>
        /// Aggressive take profit target (used when ADX indicates strong trend).
        /// Typically the high end of the expected price range or beyond.
        /// </summary>
        /// <remarks>
        /// <para><b>When Used:</b> ADX &gt; <see cref="StrongTrendThreshold"/></para>
        /// <para><b>Rationale:</b> Strong trends can exceed expected ranges.</para>
        /// </remarks>
        public required double AggressiveTarget { get; init; }

        /// <summary>
        /// ADX threshold below which trend is considered weak/absent.
        /// Default: 15
        /// </summary>
        /// <remarks>
        /// <para>ADX below this value indicates choppy, range-bound price action.</para>
        /// <para>Take profit at <see cref="ConservativeTarget"/>.</para>
        /// </remarks>
        public double WeakTrendThreshold { get; init; } = 15.0;

        /// <summary>
        /// ADX threshold above which trend is considered developing.
        /// Default: 25
        /// </summary>
        /// <remarks>
        /// <para>ADX between <see cref="WeakTrendThreshold"/> and this value indicates a developing trend.</para>
        /// <para>Consider partial profit at midpoint, hold rest for <see cref="AggressiveTarget"/>.</para>
        /// </remarks>
        public double DevelopingTrendThreshold { get; init; } = 25.0;

        /// <summary>
        /// ADX threshold above which trend is considered strong.
        /// Default: 35
        /// </summary>
        /// <remarks>
        /// <para>ADX above this value indicates a strong, possibly extended trend.</para>
        /// <para>Target <see cref="AggressiveTarget"/> or beyond with trailing stop.</para>
        /// </remarks>
        public double StrongTrendThreshold { get; init; } = 35.0;

        /// <summary>
        /// Whether to exit when ADX peaks and begins falling (momentum fading).
        /// Default: true
        /// </summary>
        /// <remarks>
        /// <para><b>Best Practice:</b> This is often a better exit signal than fixed targets.</para>
        /// <para>When ADX stops rising, the trend is losing steam regardless of price.</para>
        /// </remarks>
        public bool ExitOnAdxRollover { get; init; } = true;

        /// <summary>
        /// Minimum ADX drop from peak to trigger rollover exit.
        /// Default: 2.0 (ADX must drop 2 points from its high)
        /// </summary>
        public double AdxRolloverThreshold { get; init; } = 2.0;

        /// <summary>
        /// Calculates the appropriate take profit target based on current ADX value.
        /// </summary>
        /// <param name="currentAdx">The current ADX reading.</param>
        /// <returns>The recommended take profit price.</returns>
        public double GetTargetForAdx(double currentAdx)
        {
            if (currentAdx < WeakTrendThreshold)
            {
                // Weak/no trend - take conservative target
                return ConservativeTarget;
            }
            else if (currentAdx < DevelopingTrendThreshold)
            {
                // Developing trend - interpolate between conservative and aggressive
                double ratio = (currentAdx - WeakTrendThreshold) / (DevelopingTrendThreshold - WeakTrendThreshold);
                return ConservativeTarget + (ratio * (AggressiveTarget - ConservativeTarget));
            }
            else
            {
                // Strong trend - use aggressive target
                return AggressiveTarget;
            }
        }

        /// <summary>
        /// Gets the trend strength description for the given ADX value.
        /// </summary>
        /// <param name="currentAdx">The current ADX reading.</param>
        /// <returns>A description of the trend strength.</returns>
        public string GetTrendStrength(double currentAdx)
        {
            if (currentAdx < WeakTrendThreshold)
                return "Weak/No Trend";
            else if (currentAdx < DevelopingTrendThreshold)
                return "Developing Trend";
            else if (currentAdx < StrongTrendThreshold)
                return "Strong Trend";
            else
                return "Very Strong Trend";
        }

        /// <summary>
        /// Creates an ADX take profit configuration from a price range.
        /// </summary>
        /// <param name="lowTarget">The conservative (low) end of the target range.</param>
        /// <param name="highTarget">The aggressive (high) end of the target range.</param>
        /// <returns>A new <see cref="AdxTakeProfitConfig"/> with default ADX thresholds.</returns>
        public static AdxTakeProfitConfig FromRange(double lowTarget, double highTarget)
        {
            return new AdxTakeProfitConfig
            {
                ConservativeTarget = lowTarget,
                AggressiveTarget = highTarget
            };
        }

        /// <summary>
        /// Creates an ADX take profit configuration from a price range with midpoint as conservative target.
        /// </summary>
        /// <param name="lowTarget">The low end of the target range (used to calculate midpoint).</param>
        /// <param name="highTarget">The aggressive (high) end of the target range.</param>
        /// <returns>A new <see cref="AdxTakeProfitConfig"/> where conservative target is the midpoint.</returns>
        public static AdxTakeProfitConfig FromRangeWithMidpoint(double lowTarget, double highTarget)
        {
            double midpoint = (lowTarget + highTarget) / 2.0;
            return new AdxTakeProfitConfig
            {
                ConservativeTarget = midpoint,
                AggressiveTarget = highTarget
            };
        }

        public override string ToString()
        {
            return $"ADX TP: Conservative={ConservativeTarget:F2}, Aggressive={AggressiveTarget:F2}, " +
                   $"Thresholds=[<{WeakTrendThreshold}=Weak, <{DevelopingTrendThreshold}=Dev, <{StrongTrendThreshold}=Strong]";
        }
    }
}
