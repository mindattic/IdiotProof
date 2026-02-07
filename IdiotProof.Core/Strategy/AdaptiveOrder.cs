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
        public int BollingerScore { get; init; }

        /// <summary>Time-of-day weight multiplier applied to score (0.4-1.2).</summary>
        public double TimeWeight { get; init; } = 1.0;

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

        /// <summary>Human-readable time quality.</summary>
        public string TimeQuality => TimeWeight switch
        {
            >= 1.15 => "Prime",
            >= 1.0 => "Good",
            >= 0.7 => "Fair",
            _ => "Poor"
        };

        public override string ToString() =>
            $"Score: {TotalScore} ({Condition}) | Time: {TimeQuality} (×{TimeWeight:F2}) | TP×{TakeProfitMultiplier:F2} | SL×{StopLossMultiplier:F2}";
    }

    // ========================================================================
    // AUTONOMOUS TRADING - AI-driven entry and exit decisions
    // ========================================================================
    //
    // OVERVIEW:
    // AutonomousTrading enables the system to independently decide when to
    // enter and exit positions based on real-time indicator analysis.
    // When enabled, the system monitors all indicators and:
    //   - Enters LONG when market score >= EntryThreshold (default: 70)
    //   - Enters SHORT when market score <= -EntryThreshold (default: -70)
    //   - Exits when score reverses past ExitThreshold
    //   - Auto-calculates TP/SL based on ATR or percentage
    //
    // USAGE:
    //   Ticker(AAPL).AutonomousTrading()                    // Default balanced mode
    //   Ticker(AAPL).AutonomousTrading(IS.AGGRESSIVE)       // More trades, tighter thresholds
    //   Ticker(AAPL).AutonomousTrading(IS.CONSERVATIVE)     // Fewer trades, wider thresholds
    //
    // IDIOTSCRIPT:
    //   Ticker(NVDA).AutonomousTrading()   # AI monitors and trades NVDA independently
    //
    // ========================================================================

    /// <summary>
    /// Trading aggressiveness mode for autonomous trading.
    /// </summary>
    public enum AutonomousMode
    {
        /// <summary>
        /// Conservative: Fewer trades, higher confidence thresholds.
        /// Only enters on very strong signals (score >= 80 or <= -80).
        /// Wider TP targets, wider SL for fewer stop-outs.
        /// </summary>
        Conservative,

        /// <summary>
        /// Balanced: Standard thresholds and risk management.
        /// Enters on strong signals (score >= 70 or <= -70).
        /// </summary>
        Balanced,

        /// <summary>
        /// Aggressive: More trades, lower confidence thresholds.
        /// Enters on moderate signals (score >= 60 or <= -60).
        /// Tighter TP targets, tighter SL for quick profits.
        /// </summary>
        Aggressive
    }

    /// <summary>
    /// Configuration for autonomous trading mode where the system
    /// independently decides entry and exit points based on indicator analysis.
    /// </summary>
    /// <remarks>
    /// <para><b>How Autonomous Trading Works:</b></para>
    /// <list type="number">
    ///   <item>System monitors all indicators (VWAP, EMA, RSI, MACD, ADX, Volume).</item>
    ///   <item>Calculates a Market Score (-100 to +100) from weighted indicators.</item>
    ///   <item>Enters LONG when score >= EntryThreshold (bullish).</item>
    ///   <item>Enters SHORT when score <= -EntryThreshold (bearish).</item>
    ///   <item>Auto-sets TP/SL based on ATR multiplier or percentage.</item>
    ///   <item>Exits when score reverses past ExitThreshold.</item>
    ///   <item>Can flip direction (exit long, enter short) on reversal.</item>
    /// </list>
    /// 
    /// <para><b>Indicator Weights (same as AdaptiveOrder):</b></para>
    /// <list type="table">
    ///   <item><term>VWAP</term><description>15% - Price position relative to VWAP</description></item>
    ///   <item><term>EMA Stack</term><description>20% - Short/medium/long EMA alignment</description></item>
    ///   <item><term>RSI</term><description>15% - Overbought/oversold momentum</description></item>
    ///   <item><term>MACD</term><description>20% - Trend momentum and direction</description></item>
    ///   <item><term>ADX</term><description>20% - Trend strength</description></item>
    ///   <item><term>Volume</term><description>10% - Move confirmation</description></item>
    /// </list>
    /// </remarks>
    public sealed class AutonomousTradingConfig
    {
        /// <summary>The trading mode (Conservative, Balanced, Aggressive).</summary>
        public AutonomousMode Mode { get; init; } = AutonomousMode.Balanced;

        // ========================================================================
        // ENTRY THRESHOLDS
        // ========================================================================

        /// <summary>
        /// Minimum market score to enter a LONG position (0 to 100).
        /// Score must be >= this value to go long.
        /// </summary>
        /// <remarks>
        /// Conservative: 80, Balanced: 70, Aggressive: 60
        /// </remarks>
        public int LongEntryThreshold { get; init; } = 70;

        /// <summary>
        /// Maximum market score to enter a SHORT position (-100 to 0).
        /// Score must be <= this value to go short.
        /// </summary>
        /// <remarks>
        /// Conservative: -80, Balanced: -70, Aggressive: -60
        /// </remarks>
        public int ShortEntryThreshold { get; init; } = -70;

        // ========================================================================
        // EXIT THRESHOLDS
        // ========================================================================

        /// <summary>
        /// Score threshold to exit a LONG position (0 to 100).
        /// If score drops below this while long, consider exiting.
        /// </summary>
        /// <remarks>
        /// Exit long if score falls below 30 (lost bullish momentum).
        /// </remarks>
        public int LongExitThreshold { get; init; } = 30;

        /// <summary>
        /// Score threshold to exit a SHORT position (-100 to 0).
        /// If score rises above this while short, consider exiting.
        /// </summary>
        /// <remarks>
        /// Exit short if score rises above -30 (lost bearish momentum).
        /// </remarks>
        public int ShortExitThreshold { get; init; } = -30;

        // ========================================================================
        // POSITION SIZING AND RISK
        // ========================================================================

        /// <summary>
        /// Default quantity if not specified in strategy.
        /// </summary>
        public int DefaultQuantity { get; init; } = 100;

        /// <summary>
        /// ATR multiplier for take profit calculation.
        /// TP = Entry ± (ATR × TakeProfitAtrMultiplier)
        /// </summary>
        /// <remarks>
        /// Conservative: 3.0, Balanced: 2.5, Aggressive: 2.0
        /// </remarks>
        public double TakeProfitAtrMultiplier { get; init; } = 2.5;

        /// <summary>
        /// ATR multiplier for stop loss calculation.
        /// SL = Entry ∓ (ATR × StopLossAtrMultiplier)
        /// </summary>
        /// <remarks>
        /// Conservative: 2.0, Balanced: 1.5, Aggressive: 1.0
        /// </remarks>
        public double StopLossAtrMultiplier { get; init; } = 1.5;

        /// <summary>
        /// Fallback take profit percentage if ATR is not available.
        /// </summary>
        public double TakeProfitPercent { get; init; } = 0.02; // 2%

        /// <summary>
        /// Fallback stop loss percentage if ATR is not available.
        /// </summary>
        public double StopLossPercent { get; init; } = 0.01; // 1%

        // ========================================================================
        // BEHAVIOR OPTIONS
        // ========================================================================

        /// <summary>
        /// Whether to automatically flip direction on reversal.
        /// If true: Exit long and enter short when score becomes bearish.
        /// If false: Just exit without entering opposite direction.
        /// </summary>
        public bool AllowDirectionFlip { get; init; } = true;

        /// <summary>
        /// Whether to allow shorting.
        /// If false, only long positions are taken.
        /// </summary>
        public bool AllowShort { get; init; } = true;

        /// <summary>
        /// Whether to use Flip Mode - stay in market and pivot on reversals.
        /// When enabled:
        /// - No TP/SL orders are placed (monitors in code instead)
        /// - Flips direction immediately when score reverses
        /// - Always in market (long or short)
        /// - Lower thresholds for faster pivoting
        /// </summary>
        /// <remarks>
        /// <para>
        /// Flip Mode is designed for highly liquid, trending stocks where you want
        /// to capture moves in both directions without missing reversals.
        /// </para>
        /// <para>
        /// ⚠️ WARNING: Flip Mode can result in rapid position changes and increased
        /// commission costs. Best used on volatile stocks with clear trends.
        /// </para>
        /// </remarks>
        public bool UseFlipMode { get; init; } = false;

        /// <summary>
        /// Minimum seconds between position changes to avoid over-trading.
        /// </summary>
        public int MinSecondsBetweenTrades { get; init; } = 60;

        /// <summary>
        /// Minimum score change required before considering a new trade.
        /// Prevents whipsawing on small score fluctuations.
        /// </summary>
        public int MinScoreChangeForTrade { get; init; } = 15;

        /// <summary>
        /// Whether to only trade during Regular Trading Hours (9:30 AM - 4:00 PM ET).
        /// When true, avoids premarket/afterhours where signals are noisier.
        /// </summary>
        public bool TradingHoursOnly { get; init; } = false;

        /// <summary>
        /// Gets a human-readable description of this configuration.
        /// </summary>
        public string Description => $"Autonomous ({Mode}): Long>={LongEntryThreshold}, Short<={ShortEntryThreshold}, " +
                                    $"TP×{TakeProfitAtrMultiplier:F1}ATR, SL×{StopLossAtrMultiplier:F1}ATR";
    }

    /// <summary>
    /// Provides preset configurations for autonomous trading.
    /// </summary>
    public static class Autonomous
    {
        /// <summary>
        /// Conservative: Fewer trades, highest confidence required. Target: 70%+ win rate.
        /// </summary>
        public static AutonomousTradingConfig Conservative => new()
        {
            Mode = AutonomousMode.Conservative,
            LongEntryThreshold = 75,       // High confidence entry (was 90 - too strict)
            ShortEntryThreshold = -75,
            LongExitThreshold = 45,        // Exit when momentum fades
            ShortExitThreshold = -45,
            TakeProfitAtrMultiplier = 2.0,  // Smaller TP for frequent wins
            StopLossAtrMultiplier = 4.0,    // Wide SL to avoid noise
            TakeProfitPercent = 0.015,      // 1.5% TP
            StopLossPercent = 0.03,         // 3% SL (2:1 risk for high win rate)
            MinSecondsBetweenTrades = 300,  // 5 minutes between trades (was 600)
            MinScoreChangeForTrade = 15,
            TradingHoursOnly = true         // RTH only, avoid premarket noise
        };

        /// <summary>
        /// Balanced: Standard risk/reward balance. Target: 65%+ win rate.
        /// </summary>
        public static AutonomousTradingConfig Balanced => new()
        {
            Mode = AutonomousMode.Balanced,
            LongEntryThreshold = 65,       // Moderate confidence (was 85 - too strict)
            ShortEntryThreshold = -65,
            LongExitThreshold = 35,        // Exit on fading momentum
            ShortExitThreshold = -35,
            TakeProfitAtrMultiplier = 1.8,  // Smaller TP for more wins
            StopLossAtrMultiplier = 3.0,    // Wider SL to avoid noise
            TakeProfitPercent = 0.012,      // 1.2% TP
            StopLossPercent = 0.025,        // 2.5% SL
            MinSecondsBetweenTrades = 180,  // 3 minutes between trades (was 300)
            MinScoreChangeForTrade = 12,
            TradingHoursOnly = true         // RTH only
        };

        /// <summary>
        /// Aggressive: More trades, lower confidence thresholds. Target: 55%+ win rate.
        /// </summary>
        public static AutonomousTradingConfig Aggressive => new()
        {
            Mode = AutonomousMode.Aggressive,
            LongEntryThreshold = 55,       // Lower threshold for more entries (was 75)
            ShortEntryThreshold = -55,
            LongExitThreshold = 25,        // Stay in longer
            ShortExitThreshold = -25,
            TakeProfitAtrMultiplier = 1.5,  // Quick TP
            StopLossAtrMultiplier = 2.5,    // Reasonable SL
            TakeProfitPercent = 0.01,       // 1% TP
            StopLossPercent = 0.02,         // 2% SL
            MinSecondsBetweenTrades = 120,  // 2 minutes between trades (was 180)
            MinScoreChangeForTrade = 10,
            TradingHoursOnly = false        // Allow extended hours
        };

        /// <summary>
        /// FlipTrader: Always in market, pivots on reversals - no TP/SL orders.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This mode is designed for liquid, volatile stocks where you want to
        /// capture moves in both directions. The system stays in the market
        /// continuously and flips direction when momentum reverses.
        /// </para>
        /// <para>
        /// Key characteristics:
        /// - No TP/SL bracket orders placed (monitors in code instead)
        /// - Rapid direction changes based on score reversals
        /// - Lower thresholds for faster entry/exit
        /// - Minimal time between trades (10 seconds)
        /// - Always long or short - never flat (except briefly during flip)
        /// </para>
        /// </remarks>
        public static AutonomousTradingConfig FlipTrader => new()
        {
            Mode = AutonomousMode.Aggressive,
            LongEntryThreshold = 50,   // Lower threshold for faster entry
            ShortEntryThreshold = -50, // Lower threshold for faster entry
            LongExitThreshold = 0,     // Exit as soon as momentum neutralizes
            ShortExitThreshold = 0,    // Exit as soon as momentum neutralizes
            TakeProfitAtrMultiplier = 0, // Not used in flip mode
            StopLossAtrMultiplier = 0,   // Not used in flip mode
            TakeProfitPercent = 0,       // Not used in flip mode
            StopLossPercent = 0,         // Not used in flip mode
            AllowDirectionFlip = true,
            AllowShort = true,
            UseFlipMode = true,
            MinSecondsBetweenTrades = 10,  // Fast pivoting
            MinScoreChangeForTrade = 10    // React to smaller changes
        };

        /// <summary>
        /// Creates a custom autonomous trading configuration.
        /// </summary>
        public static AutonomousTradingConfig Custom(
            int longEntry = 70,
            int shortEntry = -70,
            double tpAtr = 2.5,
            double slAtr = 1.5,
            bool allowFlip = true,
            bool allowShort = true)
        {
            return new AutonomousTradingConfig
            {
                Mode = AutonomousMode.Balanced,
                LongEntryThreshold = longEntry,
                ShortEntryThreshold = shortEntry,
                TakeProfitAtrMultiplier = tpAtr,
                StopLossAtrMultiplier = slAtr,
                AllowDirectionFlip = allowFlip,
                AllowShort = allowShort
            };
        }
    }
}


