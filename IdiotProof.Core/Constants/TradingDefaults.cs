// ============================================================================
// TradingDefaults - Central source for trading configuration constants
// ============================================================================
//
// THIS IS THE SINGLE SOURCE OF TRUTH for trading thresholds and multipliers.
// All live trading, backtesting, and training code should reference these.
//
// ============================================================================

namespace IdiotProof.Constants;

/// <summary>
/// Central source of truth for trading configuration constants.
/// Used by live trading, backtesting, and training to ensure consistency.
/// </summary>
public static class TradingDefaults
{
    // ========================================================================
    // ENTRY/EXIT THRESHOLDS
    // Market score ranges from -100 to +100
    // ========================================================================

    /// <summary>
    /// Default threshold for entering a LONG position.
    /// Score must be >= this value.
    /// </summary>
    /// <remarks>65 = moderate confidence (not too aggressive, not too conservative)</remarks>
    public const int LongEntryThreshold = 65;

    /// <summary>
    /// Default threshold for entering a SHORT position.
    /// Score must be <= this value.
    /// </summary>
    public const int ShortEntryThreshold = -65;

    /// <summary>
    /// Default threshold for exiting a LONG position.
    /// When score drops below this, exit the long.
    /// </summary>
    public const int LongExitThreshold = 40;

    /// <summary>
    /// Default threshold for exiting a SHORT position.
    /// When score rises above this, exit the short.
    /// </summary>
    public const int ShortExitThreshold = -40;

    /// <summary>
    /// Emergency exit threshold for LONG positions.
    /// If score drops below this, exit immediately regardless of P&L.
    /// </summary>
    public const int EmergencyExitThresholdLong = -70;

    /// <summary>
    /// Emergency exit threshold for SHORT positions.
    /// If score rises above this, exit immediately regardless of P&L.
    /// </summary>
    public const int EmergencyExitThresholdShort = 70;

    // ========================================================================
    // ATR MULTIPLIERS FOR TAKE PROFIT / STOP LOSS
    // ========================================================================

    /// <summary>
    /// Default ATR multiplier for take profit.
    /// Higher = wider target = more risk for more reward.
    /// </summary>
    public const double TpAtrMultiplier = 2.0;

    /// <summary>
    /// Default ATR multiplier for stop loss.
    /// Higher = wider stop = more room for volatility.
    /// </summary>
    public const double SlAtrMultiplier = 1.5;

    /// <summary>
    /// Minimum allowed ATR multiplier for take profit.
    /// </summary>
    public const double MinTpAtrMultiplier = 1.2;

    /// <summary>
    /// Maximum allowed ATR multiplier for take profit.
    /// </summary>
    public const double MaxTpAtrMultiplier = 3.5;

    /// <summary>
    /// Minimum allowed ATR multiplier for stop loss.
    /// </summary>
    public const double MinSlAtrMultiplier = 1.0;

    /// <summary>
    /// Maximum allowed ATR multiplier for stop loss.
    /// </summary>
    public const double MaxSlAtrMultiplier = 3.0;

    // ========================================================================
    // ATR MULTIPLIERS BY MARKET CONDITION
    // ========================================================================

    /// <summary>ATR multipliers for strong trending market (ADX >= 40).</summary>
    public static class StrongTrend
    {
        public const double TpMultiplier = 2.5;
        public const double SlMultiplier = 1.2;
    }

    /// <summary>ATR multipliers for moderate trending market (ADX 25-40).</summary>
    public static class ModerateTrend
    {
        public const double TpMultiplier = 2.2;
        public const double SlMultiplier = 1.4;
    }

    /// <summary>ATR multipliers for ranging/weak market (ADX &lt; 20).</summary>
    public static class RangingMarket
    {
        public const double TpMultiplier = 1.5;
        public const double SlMultiplier = 2.0;
    }

    // ========================================================================
    // SLIPPAGE
    // ========================================================================

    /// <summary>
    /// Default slippage as a decimal percentage.
    /// 0.0005 = 0.05% slippage per trade.
    /// </summary>
    public const double SlippagePercent = 0.0005;

    /// <summary>
    /// Slippage for backtesting (decimal).
    /// Set to 0 for pure price movement analysis, or SlippagePercent for realistic sim.
    /// </summary>
    public const decimal BacktestSlippage = 0.0m;

    // ========================================================================
    // ADX THRESHOLDS FOR DYNAMIC ADJUSTMENTS
    // ========================================================================

    /// <summary>ADX level indicating a strong trend.</summary>
    public const double AdxStrongTrend = 40.0;

    /// <summary>ADX level indicating a moderate trend.</summary>
    public const double AdxModerateTrend = 25.0;

    /// <summary>ADX level indicating a ranging/weak market.</summary>
    public const double AdxRangingMarket = 20.0;

    // ========================================================================
    // TIMING
    // ========================================================================

    /// <summary>
    /// Minimum seconds between order adjustments to prevent churn.
    /// </summary>
    public const int MinSecondsBetweenAdjustments = 30;

    /// <summary>
    /// Minimum score change required to trigger an adjustment.
    /// </summary>
    public const int MinScoreChangeForAdjustment = 15;
}
