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

    /// <summary>
    /// Gets realistic slippage percentage based on stock price tier.
    /// Low-priced stocks have wider bid-ask spreads, so slippage is much higher.
    /// For a $0.60 stock, $0.01 spread = 1.67% - this must be accounted for.
    /// </summary>
    public static double GetSlippagePercent(double price)
    {
        return price switch
        {
            < 1.0 => 0.015,    // 1.5% slippage for sub-$1 (typical $0.01-$0.02 spread on $0.50-$1.00)
            < 5.0 => 0.005,    // 0.5% slippage for $1-$5
            < 25.0 => 0.002,   // 0.2% slippage for $5-$25
            < 100.0 => 0.001,  // 0.1% slippage for $25-$100
            _ => 0.0005        // 0.05% slippage for $100+
        };
    }

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
    // PRICE-TIER-AWARE MINIMUM SL/TP DISTANCES
    // Penny stocks need wider stops to survive normal micro-volatility.
    // These are MINIMUM percentage distances from entry price.
    // ========================================================================

    /// <summary>
    /// Gets the minimum stop loss distance as a percentage of entry price.
    /// Penny stocks get wider minimums because their tick-to-tick noise is
    /// a larger percentage of price.
    /// </summary>
    public static double GetMinSlPercent(double entryPrice)
    {
        return entryPrice switch
        {
            < 1.0 => 0.05,    // 5% min SL for sub-$1 stocks
            < 5.0 => 0.035,   // 3.5% min SL for $1-$5 stocks
            < 25.0 => 0.025,  // 2.5% min SL for $5-$25 stocks
            < 100.0 => 0.015, // 1.5% min SL for $25-$100 stocks
            _ => 0.01         // 1% min SL for $100+ stocks
        };
    }

    /// <summary>
    /// Gets the minimum take profit distance as a percentage of entry price.
    /// Ensures TP is always a meaningful target relative to price.
    /// </summary>
    public static double GetMinTpPercent(double entryPrice)
    {
        return entryPrice switch
        {
            < 1.0 => 0.07,    // 7% min TP for sub-$1 stocks
            < 5.0 => 0.05,    // 5% min TP for $1-$5 stocks
            < 25.0 => 0.035,  // 3.5% min TP for $5-$25 stocks
            < 100.0 => 0.02,  // 2% min TP for $25-$100 stocks
            _ => 0.015        // 1.5% min TP for $100+ stocks
        };
    }

    /// <summary>
    /// Gets an ATR multiplier scaling factor for the stock's price tier.
    /// Low-priced stocks get larger multipliers because 1-minute ATR is tiny
    /// relative to the noise they experience.
    /// </summary>
    public static double GetAtrMultiplierScale(double entryPrice)
    {
        return entryPrice switch
        {
            < 1.0 => 3.0,    // Triple the ATR multiplier for sub-$1
            < 5.0 => 2.0,    // Double for $1-$5
            < 25.0 => 1.5,   // 1.5x for $5-$25
            _ => 1.0          // Standard for $25+
        };
    }

    /// <summary>
    /// Enforces minimum TP/SL distances based on price tier.
    /// Call this after calculating raw ATR-based TP/SL distances.
    /// </summary>
    public static (double tpDistance, double slDistance) EnforceMinimumDistances(
        double entryPrice, double rawTpDistance, double rawSlDistance)
    {
        double minTp = entryPrice * GetMinTpPercent(entryPrice);
        double minSl = entryPrice * GetMinSlPercent(entryPrice);

        return (Math.Max(rawTpDistance, minTp), Math.Max(rawSlDistance, minSl));
    }

    // ========================================================================
    // MINIMUM HOLD TIME
    // Prevents premature score-based exits on noisy stocks.
    // SL/TP still fire immediately - this only delays score exits.
    // ========================================================================

    /// <summary>
    /// Minimum number of bars to hold a position before allowing score-based
    /// exits. SL and TP can still trigger immediately. This prevents the
    /// system from entering and exiting on the next bar due to score noise.
    /// </summary>
    public const int MinHoldBarsBeforeScoreExit = 5;

    /// <summary>
    /// Minimum hold time in minutes before score-based exit (for live trading).
    /// </summary>
    public const int MinHoldMinutesBeforeScoreExit = 5;

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

    // ========================================================================
    // POSITION SIZING
    // ========================================================================
    // Default allocation: $1,000 per trade.
    // Quantity is calculated as: allocation / current_price
    // ========================================================================

    /// <summary>
    /// Default dollar allocation per trade when not specified.
    /// $1,000 = reasonable size for most stocks.
    /// </summary>
    public const double DefaultAllocationDollars = 1000.0;

    /// <summary>
    /// Calculates a sensible default quantity based on stock price.
    /// Formula: price × 3, rounded up to nearest multiple of 5.
    /// </summary>
    /// <param name="price">Current stock price.</param>
    /// <returns>Recommended quantity (minimum 5 shares).</returns>
    /// <example>
    /// GetDefaultQuantityForPrice(1.50)  → 5   ($1.50 × 3 = 4.5 → round up to 5)
    /// GetDefaultQuantityForPrice(2.00)  → 10  ($2.00 × 3 = 6 → round up to 10)
    /// GetDefaultQuantityForPrice(6.00)  → 20  ($6.00 × 3 = 18 → round up to 20)
    /// GetDefaultQuantityForPrice(10.00) → 30  ($10.00 × 3 = 30 → already multiple of 5)
    /// GetDefaultQuantityForPrice(25.00) → 75  ($25.00 × 3 = 75 → already multiple of 5)
    /// </example>
    public static int GetDefaultQuantityForPrice(double price)
    {
        if (price <= 0) return 5;

        // price × 3, rounded up to nearest multiple of 5
        double raw = price * 3;
        int quantity = (int)(Math.Ceiling(raw / 5.0) * 5);
        return Math.Max(5, quantity);
    }

    /// <summary>
    /// Gets a description of the quantity calculation for display purposes.
    /// </summary>
    public static string GetPriceTierName(double price)
    {
        int qty = GetDefaultQuantityForPrice(price);
        return $"{qty} shares";
    }

    /// <summary>
    /// Gets the estimated position value for a given price.
    /// </summary>
    public static double GetTargetPositionValue(double price)
    {
        int qty = GetDefaultQuantityForPrice(price);
        return qty * price;
    }
}
