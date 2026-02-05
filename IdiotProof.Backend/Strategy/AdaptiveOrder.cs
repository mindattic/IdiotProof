// ============================================================================
// Adaptive Order - Smart Dynamic Order Management
// ============================================================================
//
// OVERVIEW:
// AdaptiveOrder is an intelligent order management system that monitors market
// conditions in real-time and dynamically adjusts take profit and stop loss
// levels to maximize potential profit while managing risk.
//
// HOW IT WORKS:
// ╔═══════════════════════════════════════════════════════════════════════════╗
// ║  MARKET ANALYSIS                                                          ║
// ║                                                                           ║
// ║  The system continuously evaluates multiple indicators:                   ║
// ║                                                                           ║
// ║  1. VWAP Position: Bullish above, bearish below                          ║
// ║  2. EMA Stack: Short-term vs long-term trend alignment                   ║
// ║  3. RSI: Overbought/oversold for reversal risk                           ║
// ║  4. MACD: Momentum direction and strength                                ║
// ║  5. ADX: Trend strength (strong trends get wider targets)                ║
// ║  6. Volume: Confirmation of price moves                                  ║
// ║                                                                           ║
// ║  These are combined into a Market Score (-100 to +100)                   ║
// ║  Positive = bullish, Negative = bearish                                  ║
// ╚═══════════════════════════════════════════════════════════════════════════╝
//
// ADAPTIVE BEHAVIOR:
// ╔═══════════════════════════════════════════════════════════════════════════╗
// ║  SCENARIO               │  TAKE PROFIT         │  STOP LOSS              ║
// ║─────────────────────────┼──────────────────────┼─────────────────────────║
// ║  Strong bullish (70+)   │  Extend +50%         │  Tighten (protect gain) ║
// ║  Moderate bull (30-70)  │  Keep original       │  Keep original          ║
// ║  Neutral (-30 to 30)    │  Reduce 25%          │  Widen (allow bounce)   ║
// ║  Moderate bear (-70-30) │  Reduce 50%          │  Keep original          ║
// ║  Strong bearish (<-70)  │  EXIT IMMEDIATELY    │  N/A - Emergency exit   ║
// ╚═══════════════════════════════════════════════════════════════════════════╝
//
// USAGE:
//   .AdaptiveOrder()                    // Enable with defaults
//   .AdaptiveOrder(IS.AGGRESSIVE)       // Wider profit targets, tighter stops
//   .AdaptiveOrder(IS.CONSERVATIVE)     // Tighter profit targets, wider stops
//
// IDIOTSCRIPT:
//   Ticker(AAPL).Entry(150).TakeProfit(160).StopLoss(145).AdaptiveOrder()
//
// NOTE: Requires TakeProfit and StopLoss to be set. AdaptiveOrder modifies
// these values dynamically but needs starting points for calculation.
//
// ============================================================================

namespace IdiotProof.Backend.Models
{
    /// <summary>
    /// Configuration for adaptive order management that dynamically adjusts
    /// take profit and stop loss based on real-time market conditions.
    /// </summary>
    /// <remarks>
    /// <para><b>How Adaptive Orders Work:</b></para>
    /// <list type="number">
    ///   <item>System calculates a Market Score from multiple indicators.</item>
    ///   <item>Score determines adjustment multipliers for TP/SL.</item>
    ///   <item>Orders are modified via IB API when conditions change significantly.</item>
    ///   <item>Emergency exit triggered if score drops below threshold.</item>
    /// </list>
    /// 
    /// <para><b>Indicator Weights (default):</b></para>
    /// <list type="table">
    ///   <item><term>VWAP</term><description>15% - Price position relative to VWAP</description></item>
    ///   <item><term>EMA Stack</term><description>20% - Short/medium/long EMA alignment</description></item>
    ///   <item><term>RSI</term><description>15% - Overbought/oversold momentum</description></item>
    ///   <item><term>MACD</term><description>20% - Trend momentum and direction</description></item>
    ///   <item><term>ADX</term><description>20% - Trend strength</description></item>
    ///   <item><term>Volume</term><description>10% - Move confirmation</description></item>
    /// </list>
    /// </remarks>
    public sealed class AdaptiveOrderConfig
    {
        /// <summary>
        /// The adaptive strategy mode.
        /// </summary>
        public AdaptiveMode Mode { get; init; } = AdaptiveMode.Balanced;

        /// <summary>
        /// Minimum time (in seconds) between order modifications.
        /// Prevents excessive API calls and order churn.
        /// </summary>
        /// <remarks>
        /// <para><b>Default:</b> 30 seconds</para>
        /// <para><b>Note:</b> Emergency exits ignore this threshold.</para>
        /// </remarks>
        public int MinSecondsBetweenAdjustments { get; init; } = 30;

        /// <summary>
        /// Minimum score change required to trigger an adjustment.
        /// Prevents modifications on minor fluctuations.
        /// </summary>
        /// <remarks>
        /// <para><b>Default:</b> 15 points (on -100 to +100 scale)</para>
        /// </remarks>
        public int MinScoreChangeForAdjustment { get; init; } = 15;

        /// <summary>
        /// Score threshold for emergency exit (strong bearish conditions).
        /// If score drops below this, position is closed immediately.
        /// </summary>
        /// <remarks>
        /// <para><b>Default:</b> -70</para>
        /// <para>Only applies to long positions. For shorts, the threshold is +70.</para>
        /// </remarks>
        public int EmergencyExitThreshold { get; init; } = -70;

        /// <summary>
        /// Maximum percentage to extend take profit in strong trends.
        /// </summary>
        /// <remarks>
        /// <para><b>Example:</b> 0.50 means TP can be extended up to 50% beyond original.</para>
        /// <para>Original TP = $155, Extension = 50% → Max TP = $157.50 (on $150 entry)</para>
        /// </remarks>
        public double MaxTakeProfitExtension { get; init; } = 0.50;

        /// <summary>
        /// Maximum percentage to reduce take profit in weak conditions.
        /// </summary>
        /// <remarks>
        /// <para><b>Example:</b> 0.50 means TP can be reduced by up to 50%.</para>
        /// <para>Original TP = $155, Reduction = 50% → Min TP = $152.50 (on $150 entry)</para>
        /// </remarks>
        public double MaxTakeProfitReduction { get; init; } = 0.50;

        /// <summary>
        /// Maximum percentage to tighten stop loss (move closer to entry).
        /// </summary>
        /// <remarks>
        /// <para><b>Example:</b> 0.50 means SL can be tightened by up to 50%.</para>
        /// <para>Original SL = $145, Tighten = 50% → New SL = $147.50 (on $150 entry)</para>
        /// </remarks>
        public double MaxStopLossTighten { get; init; } = 0.50;

        /// <summary>
        /// Maximum percentage to widen stop loss (move further from entry).
        /// </summary>
        /// <remarks>
        /// <para><b>Example:</b> 0.25 means SL can be widened by up to 25%.</para>
        /// <para>Original SL = $145, Widen = 25% → New SL = $143.75 (on $150 entry)</para>
        /// </remarks>
        public double MaxStopLossWiden { get; init; } = 0.25;

        // ========================================================================
        // INDICATOR WEIGHTS (must sum to 1.0)
        // ========================================================================

        /// <summary>Weight for VWAP position in score calculation.</summary>
        public double WeightVwap { get; init; } = 0.15;

        /// <summary>Weight for EMA stack alignment in score calculation.</summary>
        public double WeightEma { get; init; } = 0.20;

        /// <summary>Weight for RSI in score calculation.</summary>
        public double WeightRsi { get; init; } = 0.15;

        /// <summary>Weight for MACD in score calculation.</summary>
        public double WeightMacd { get; init; } = 0.20;

        /// <summary>Weight for ADX trend strength in score calculation.</summary>
        public double WeightAdx { get; init; } = 0.20;

        /// <summary>Weight for volume confirmation in score calculation.</summary>
        public double WeightVolume { get; init; } = 0.10;

        /// <summary>
        /// Gets a human-readable description of this configuration.
        /// </summary>
        public string Description => $"Adaptive ({Mode}): TP ext/red {MaxTakeProfitExtension * 100:F0}%/{MaxTakeProfitReduction * 100:F0}%, " +
                                    $"SL tight/wide {MaxStopLossTighten * 100:F0}%/{MaxStopLossWiden * 100:F0}%";
    }

    /// <summary>
    /// Adaptive order strategy modes.
    /// </summary>
    public enum AdaptiveMode
    {
        /// <summary>
        /// Conservative mode - prioritizes protecting gains.
        /// Tighter take profit targets, quicker to take profits.
        /// Wider stop losses to avoid being stopped out on noise.
        /// </summary>
        Conservative,

        /// <summary>
        /// Balanced mode - equal priority to profit and protection.
        /// Standard adjustments based on market score.
        /// </summary>
        Balanced,

        /// <summary>
        /// Aggressive mode - prioritizes maximizing profits.
        /// Wider take profit targets in strong trends.
        /// Tighter stop losses to protect capital.
        /// </summary>
        Aggressive
    }

    /// <summary>
    /// Provides preset configurations for adaptive order management.
    /// </summary>
    /// <remarks>
    /// <para><b>Usage in IdiotScript:</b></para>
    /// <code>
    /// .AdaptiveOrder(IS.CONSERVATIVE)
    /// .AdaptiveOrder(IS.BALANCED)
    /// .AdaptiveOrder(IS.AGGRESSIVE)
    /// </code>
    /// </remarks>
    public static class Adaptive
    {
        /// <summary>
        /// Conservative: Protect gains, quick to take profits.
        /// </summary>
        public static AdaptiveOrderConfig Conservative => new()
        {
            Mode = AdaptiveMode.Conservative,
            MaxTakeProfitExtension = 0.25,      // Only extend TP 25% max
            MaxTakeProfitReduction = 0.60,      // Can reduce TP by 60%
            MaxStopLossTighten = 0.30,          // Only tighten SL 30% max
            MaxStopLossWiden = 0.40,            // Allow 40% wider SL
            EmergencyExitThreshold = -60,       // Exit sooner on bearish
            MinScoreChangeForAdjustment = 10    // More responsive
        };

        /// <summary>
        /// Balanced: Standard risk/reward balance.
        /// </summary>
        public static AdaptiveOrderConfig Balanced => new()
        {
            Mode = AdaptiveMode.Balanced,
            MaxTakeProfitExtension = 0.50,
            MaxTakeProfitReduction = 0.50,
            MaxStopLossTighten = 0.50,
            MaxStopLossWiden = 0.25,
            EmergencyExitThreshold = -70,
            MinScoreChangeForAdjustment = 15
        };

        /// <summary>
        /// Aggressive: Maximize profit potential in strong trends.
        /// </summary>
        public static AdaptiveOrderConfig Aggressive => new()
        {
            Mode = AdaptiveMode.Aggressive,
            MaxTakeProfitExtension = 0.75,      // Extend TP up to 75%
            MaxTakeProfitReduction = 0.30,      // Only reduce TP 30% max
            MaxStopLossTighten = 0.60,          // Can tighten SL 60%
            MaxStopLossWiden = 0.15,            // Only allow 15% wider
            EmergencyExitThreshold = -80,       // Stay in longer
            MinScoreChangeForAdjustment = 20    // Less responsive to noise
        };

        /// <summary>
        /// Creates a custom adaptive configuration.
        /// </summary>
        public static AdaptiveOrderConfig Custom(
            double tpExtension = 0.50,
            double tpReduction = 0.50,
            double slTighten = 0.50,
            double slWiden = 0.25,
            int emergencyThreshold = -70,
            int minScoreChange = 15)
        {
            return new AdaptiveOrderConfig
            {
                Mode = AdaptiveMode.Balanced,
                MaxTakeProfitExtension = tpExtension,
                MaxTakeProfitReduction = tpReduction,
                MaxStopLossTighten = slTighten,
                MaxStopLossWiden = slWiden,
                EmergencyExitThreshold = emergencyThreshold,
                MinScoreChangeForAdjustment = minScoreChange
            };
        }
    }

    /// <summary>
    /// Represents the current market analysis score and recommended adjustments.
    /// </summary>
    public sealed class MarketScore
    {
        /// <summary>Overall market score (-100 to +100).</summary>
        public int TotalScore { get; init; }

        /// <summary>Individual component scores.</summary>
        public int VwapScore { get; init; }
        public int EmaScore { get; init; }
        public int RsiScore { get; init; }
        public int MacdScore { get; init; }
        public int AdxScore { get; init; }
        public int VolumeScore { get; init; }

        /// <summary>Recommended take profit multiplier (1.0 = no change).</summary>
        public double TakeProfitMultiplier { get; init; }

        /// <summary>Recommended stop loss multiplier (1.0 = no change).</summary>
        public double StopLossMultiplier { get; init; }

        /// <summary>Whether conditions warrant emergency exit.</summary>
        public bool ShouldEmergencyExit { get; init; }

        /// <summary>Human-readable market condition description.</summary>
        public string Condition => TotalScore switch
        {
            >= 70 => "Strong Bullish",
            >= 30 => "Moderate Bullish",
            >= -30 => "Neutral",
            >= -70 => "Moderate Bearish",
            _ => "Strong Bearish"
        };

        public override string ToString() =>
            $"Score: {TotalScore} ({Condition}) | TP×{TakeProfitMultiplier:F2} | SL×{StopLossMultiplier:F2}";
    }
}


