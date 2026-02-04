// ============================================================================
// IdiotScriptConstants - Predefined constants for IdiotScript language
// ============================================================================
//
// NOMENCLATURE:
// - IdiotScript: The text-based DSL for defining trading strategies
// - IS. Prefix: IdiotScript constant prefix (reads naturally as "is")
// - Constant: A predefined value with semantic meaning (IS.PREMARKET, IS.TRUE)
// - Boolean Constant: IS.TRUE or IS.FALSE for boolean parameters
// - Session Constant: Trading session identifiers (IS.PREMARKET, IS.RTH, etc.)
// - Time Constant: Specific market times (IS.BELL, IS.OPEN, IS.CLOSE)
// - TSL Constant: Trailing stop loss percentages (IS.TIGHT, IS.MODERATE, etc.)
//
// BOOLEAN VALUES:
// - Truthy: Y, YES, TRUE, T, 1, IS.TRUE, IS.Y, IS.YES, IS.T
// - Falsy: N, NO, FALSE, F, 0, IS.FALSE, IS.N, IS.NO, IS.F
//
// SESSION CONSTANTS:
// - IS.PREMARKET: Pre-market session (4:00 AM - 9:30 AM ET)
// - IS.RTH: Regular Trading Hours (9:30 AM - 4:00 PM ET)
// - IS.AFTERHOURS: After-hours session (4:00 PM - 8:00 PM ET)
// - IS.EXTENDED: Extended hours (4:00 AM - 8:00 PM ET)
// - IS.ACTIVE: Always active, no time restrictions
// - IS.PREMARKET_END_EARLY: Pre-market ending early (4:00 AM - 9:20 AM ET)
// - IS.PREMARKET_START_LATE: Pre-market starting late (4:10 AM - 9:30 AM ET)
//
// TIME CONSTANTS:
// - IS.BELL: Last minute before session end (session-aware):
//     * Premarket: 9:29 AM (1 min before open)
//     * RTH: 3:59 PM (1 min before close)
//     * AfterHours: 7:59 PM (1 min before AH end)
//     * Extended: 7:59 PM (1 min before extended end)
//     * Default fallback: 3:59 PM
// - IS.OPEN: Market open (9:30 AM ET)
// - IS.CLOSE: Market close (4:00 PM ET)
// - IS.EOD: End of day (same as IS.CLOSE)
// - IS.PM_START: Pre-market start (4:00 AM ET)
// - IS.AH_END: After-hours end (8:00 PM ET)
//
// TRAILING STOP LOSS CONSTANTS:
// - IS.TIGHT: 5% trailing stop
// - IS.MODERATE: 10% trailing stop
// - IS.STANDARD: 15% trailing stop
// - IS.LOOSE: 20% trailing stop
// - IS.WIDE: 25% trailing stop
//
// USAGE EXAMPLES:
//   SESSION(IS.PREMARKET)           // Trading session constant
//   CLOSE(IS.BELL)                  // Last minute before session end (session-aware)
//   TSL(IS.MODERATE)                // 10% trailing stop
//   ENABLED(IS.TRUE)                // Boolean constant
//   ENABLED(Y)                      // Short boolean form
//
// ============================================================================

namespace IdiotProof.Shared.Scripting;

/// <summary>
/// Predefined constants for IdiotScript with IS. prefix notation.
/// IS = "IdiotScript" and also reads naturally as "is" (e.g., SESSION IS.PREMARKET).
/// These provide semantic shortcuts for common trading values.
/// </summary>
public static class IdiotScriptConstants
{
    /// <summary>Prefix used for all IdiotScript constants.</summary>
    public const string Prefix = "IS.";

    // ========================================================================
    // SESSION CONSTANTS
    // ========================================================================

    /// <summary>Pre-market session: 4:00 AM - 9:30 AM ET</summary>
    public const string PREMARKET = "IS.PREMARKET";

    /// <summary>Regular Trading Hours: 9:30 AM - 4:00 PM ET</summary>
    public const string RTH = "IS.RTH";

    /// <summary>After-hours session: 4:00 PM - 8:00 PM ET</summary>
    public const string AFTERHOURS = "IS.AFTERHOURS";

    /// <summary>Extended hours: 4:00 AM - 8:00 PM ET</summary>
    public const string EXTENDED = "IS.EXTENDED";

    /// <summary>Always active, no time restrictions</summary>
    public const string ACTIVE = "IS.ACTIVE";

    /// <summary>Pre-market ending early: 4:00 AM - 9:20 AM ET</summary>
    public const string PREMARKET_END_EARLY = "IS.PREMARKET_END_EARLY";

    /// <summary>Pre-market starting late: 4:10 AM - 9:30 AM ET</summary>
    public const string PREMARKET_START_LATE = "IS.PREMARKET_START_LATE";

    // ========================================================================
    // TIME CONSTANTS (Eastern Time)
    // ========================================================================

    /// <summary>Last minute before session end (session-aware). Use ResolveBellTime() with session context.</summary>
    public const string BELL = "IS.BELL";

    /// <summary>Pre-market bell: 9:29 AM ET (1 minute before market open)</summary>
    public const string PREMARKET_BELL = "IS.PREMARKET.BELL";

    /// <summary>RTH bell: 3:59 PM ET (1 minute before market close)</summary>
    public const string RTH_BELL = "IS.RTH.BELL";

    /// <summary>After-hours bell: 7:59 PM ET (1 minute before AH end)</summary>
    public const string AFTERHOURS_BELL = "IS.AFTERHOURS.BELL";

    /// <summary>Market open: 9:30 AM ET</summary>
    public const string OPEN = "IS.OPEN";

    /// <summary>Market close: 4:00 PM ET</summary>
    public const string CLOSE_TIME = "IS.CLOSE";

    /// <summary>End of day: 4:00 PM ET</summary>
    public const string EOD = "IS.EOD";

    /// <summary>Pre-market start: 4:00 AM ET</summary>
    public const string PREMARKET_START = "IS.PM_START";

    /// <summary>After-hours end: 8:00 PM ET</summary>
    public const string AFTERHOURS_END = "IS.AH_END";

    // ========================================================================
    // TRAILING STOP LOSS PERCENTAGES
    // ========================================================================

    /// <summary>Conservative trailing stop: 5%</summary>
    public const string TSL_TIGHT = "IS.TIGHT";

    /// <summary>Moderate trailing stop: 10%</summary>
    public const string TSL_MODERATE = "IS.MODERATE";

    /// <summary>Standard trailing stop: 15%</summary>
    public const string TSL_STANDARD = "IS.STANDARD";

    /// <summary>Loose trailing stop: 20%</summary>
    public const string TSL_LOOSE = "IS.LOOSE";

    /// <summary>Wide trailing stop: 25%</summary>
    public const string TSL_WIDE = "IS.WIDE";

    // ========================================================================
    // ADAPTIVE ORDER MODES
    // ========================================================================

    /// <summary>Conservative adaptive mode: protect gains, quick to take profits</summary>
    public const string ADAPTIVE_CONSERVATIVE = "IS.CONSERVATIVE";

    /// <summary>Balanced adaptive mode: equal priority to profit and protection</summary>
    public const string ADAPTIVE_BALANCED = "IS.BALANCED";

    /// <summary>Aggressive adaptive mode: maximize profit potential in strong trends</summary>
    public const string ADAPTIVE_AGGRESSIVE = "IS.AGGRESSIVE";

    // ========================================================================
    // ORDER DIRECTION
    // ========================================================================

    /// <summary>Buy order</summary>
    public const string BUY = "IS.BUY";

    /// <summary>Sell order</summary>
    public const string SELL = "IS.SELL";

    /// <summary>Close long position</summary>
    public const string CLOSE_LONG = "IS.CLOSE_LONG";

    /// <summary>Close short position</summary>
    public const string CLOSE_SHORT = "IS.CLOSE_SHORT";

    // ========================================================================
    // INDICATOR THRESHOLDS
    // ========================================================================

    /// <summary>RSI oversold threshold: 30</summary>
    public const string RSI_OVERSOLD = "IS.RSI_OVERSOLD";

    /// <summary>RSI overbought threshold: 70</summary>
    public const string RSI_OVERBOUGHT = "IS.RSI_OVERBOUGHT";

    /// <summary>ADX strong trend threshold: 25</summary>
    public const string ADX_STRONG = "IS.ADX_STRONG";

    /// <summary>ADX weak trend threshold: 20</summary>
    public const string ADX_WEAK = "IS.ADX_WEAK";

    // ========================================================================
    // BOOLEAN CONSTANTS
    // ========================================================================

    /// <summary>Boolean true constant. Valid inputs: Y, YES, yes, true, TRUE, 1</summary>
    public const string TRUE = "IS.TRUE";

    /// <summary>Boolean false constant. Valid inputs: N, NO, no, false, FALSE, 0</summary>
    public const string FALSE = "IS.FALSE";

    /// <summary>Boolean true constant for ClosePosition - closes only if position is profitable</summary>
    public const string PROFITABLE = "IS.PROFITABLE";

    /// <summary>Boolean false constant for ClosePosition - closes regardless of profit/loss</summary>
    public const string NOTPROFITABLE = "IS.NOTPROFITABLE";

    // ========================================================================
    // RESOLVER METHODS
    // ========================================================================

    /// <summary>
    /// Resolves a constant to its actual value.
    /// </summary>
    public static string? ResolveConstant(string input)
    {
        if (string.IsNullOrWhiteSpace(input) || !input.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            return null;

        return input.ToUpperInvariant() switch
        {
            // Sessions
            "IS.PREMARKET" => "PreMarket",
            "IS.RTH" => "RTH",
            "IS.AFTERHOURS" => "AfterHours",
            "IS.EXTENDED" => "Extended",
            "IS.ACTIVE" => "Active",
            "IS.PREMARKET_END_EARLY" => "PreMarketEndEarly",
            "IS.PREMARKET_START_LATE" => "PreMarketStartLate",

            // Times - Bell times are session-specific (use ResolveBellTime for IS.BELL with context)
            "IS.BELL" => "15:59",  // Default to RTH bell, use ResolveBellTime() for session-aware resolution
            "IS.PREMARKET.BELL" => "9:29",
            "IS.RTH.BELL" => "15:59",
            "IS.AFTERHOURS.BELL" => "19:59",
            "IS.OPEN" => "9:30",
            "IS.CLOSE" => "16:00",
            "IS.EOD" => "16:00",
            "IS.PM_START" => "4:00",
            "IS.AH_END" => "20:00",

            // Trailing stop percentages
            "IS.TIGHT" => "0.05",
            "IS.MODERATE" => "0.10",
            "IS.STANDARD" => "0.15",
            "IS.LOOSE" => "0.20",
            "IS.WIDE" => "0.25",

            // Adaptive order modes
            "IS.CONSERVATIVE" => "Conservative",
            "IS.BALANCED" => "Balanced",
            "IS.AGGRESSIVE" => "Aggressive",
            "IS.SAFE" => "Conservative",      // Alias
            "IS.NORMAL" => "Balanced",        // Alias
            "IS.RISKY" => "Aggressive",       // Alias

            // RSI
            "IS.RSI_OVERSOLD" => "30",
            "IS.RSI_OVERBOUGHT" => "70",

            // ADX
            "IS.ADX_STRONG" => "25",
            "IS.ADX_WEAK" => "20",

            // Boolean constants - Truthy
            "IS.TRUE" => "true",
            "IS.Y" => "true",
            "IS.YES" => "true",
            "IS.T" => "true",
            "IS.PROFITABLE" => "true",

            // Boolean constants - Falsy
            "IS.FALSE" => "false",
            "IS.N" => "false",
            "IS.NO" => "false",
            "IS.F" => "false",
            "IS.NOTPROFITABLE" => "false",

            _ => null
        };
    }

    /// <summary>
    /// Resolves a constant to a time value.
    /// </summary>
    public static TimeOnly? ResolveTime(string input)
    {
        var resolved = ResolveConstant(input);
        if (resolved == null)
            return null;

        if (TimeOnly.TryParse(resolved, out var time))
            return time;

        return null;
    }

    /// <summary>
    /// Resolves IS.BELL to the last minute before the session ends.
    /// </summary>
    /// <param name="input">The time constant (IS.BELL or session-specific bell)</param>
    /// <param name="session">The trading session context (e.g., "PreMarket", "RTH", "AfterHours")</param>
    /// <returns>The bell time for the specified session, or default RTH bell if no session specified</returns>
    public static TimeOnly ResolveBellTime(string input, string? session = null)
    {
        var upper = input.ToUpperInvariant();

        // Handle explicit session bells
        if (upper == "IS.PREMARKET.BELL") return new TimeOnly(9, 29);
        if (upper == "IS.RTH.BELL") return new TimeOnly(15, 59);
        if (upper == "IS.AFTERHOURS.BELL") return new TimeOnly(19, 59);

        // For IS.BELL, resolve based on session context
        if (upper == "IS.BELL" && !string.IsNullOrEmpty(session))
        {
            return session.ToUpperInvariant() switch
            {
                "PREMARKET" => new TimeOnly(9, 29),    // 1 min before 9:30 open
                "RTH" => new TimeOnly(15, 59),          // 1 min before 4:00 close
                "AFTERHOURS" => new TimeOnly(19, 59),   // 1 min before 8:00 AH end
                "EXTENDED" => new TimeOnly(19, 59),     // 1 min before extended end
                "PREMARKETENDEARLY" => new TimeOnly(9, 19),  // 1 min before 9:20 early end
                "PREMARKETSTARTLATE" => new TimeOnly(9, 29), // Still ends at open
                "ACTIVE" => new TimeOnly(15, 59),       // Default to RTH close
                _ => new TimeOnly(15, 59)               // Default fallback
            };
        }

        // Default to RTH bell
        return new TimeOnly(15, 59);
    }

    /// <summary>
    /// Checks if a time constant is a bell constant that requires session context.
    /// </summary>
    public static bool IsBellConstant(string? input)
    {
        if (string.IsNullOrEmpty(input)) return false;
        var upper = input.ToUpperInvariant();
        return upper is "IS.BELL" or "IS.PREMARKET.BELL" or "IS.RTH.BELL" or "IS.AFTERHOURS.BELL";
    }

    /// <summary>
    /// Resolves a constant to a double value.
    /// </summary>
    public static double? ResolveDouble(string input)
    {
        var resolved = ResolveConstant(input);
        if (resolved == null)
            return null;

        if (double.TryParse(resolved, out var value))
            return value;

        return null;
    }

    /// <summary>
    /// Checks if a string is a constant (starts with IS.).
    /// </summary>
    public static bool IsConstant(string? input)
        => input?.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>
    /// Resolves a constant to a boolean value.
    /// Accepts: IS.TRUE, IS.FALSE, IS.Y, IS.YES, IS.N, IS.NO, IS.T, IS.F,
    ///          Y, YES, TRUE, T, 1, N, NO, FALSE, F, 0
    /// </summary>
    /// <param name="input">The input string to resolve.</param>
    /// <returns>True, False, or null if the value cannot be resolved.</returns>
    public static bool? ResolveBoolean(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        // Handle IS. prefixed constants
        if (IsConstant(input))
        {
            var resolved = ResolveConstant(input);
            if (resolved != null)
                return resolved.Equals("true", StringComparison.OrdinalIgnoreCase);
            return null;
        }

        // Handle direct boolean values
        var upper = input.Trim().ToUpperInvariant();
        return upper switch
        {
            "Y" or "YES" or "TRUE" or "T" or "1" => true,
            "N" or "NO" or "FALSE" or "F" or "0" => false,
            _ => null
        };
    }

    /// <summary>
    /// Checks if a string represents a valid boolean value.
    /// Valid: Y, YES, TRUE, T, 1, N, NO, FALSE, F, 0, IS.TRUE, IS.FALSE, IS.Y, IS.YES, IS.N, IS.NO, IS.T, IS.F
    /// </summary>
    public static bool IsValidBoolean(string? input) => ResolveBoolean(input).HasValue;

    /// <summary>
    /// Array of valid truthy values for boolean parsing.
    /// Valid inputs: Y, YES, TRUE, T, 1, IS.TRUE, IS.Y, IS.YES, IS.T
    /// </summary>
    public static readonly string[] TruthyValues = ["Y", "YES", "TRUE", "T", "1", "IS.TRUE", "IS.Y", "IS.YES", "IS.T"];

    /// <summary>
    /// Array of valid falsy values for boolean parsing.
    /// Valid inputs: N, NO, FALSE, F, 0, IS.FALSE, IS.N, IS.NO, IS.F, IS.NOTPROFITABLE
    /// </summary>
    public static readonly string[] FalsyValues = ["N", "NO", "FALSE", "F", "0", "IS.FALSE", "IS.N", "IS.NO", "IS.F", "IS.NOTPROFITABLE"];

    /// <summary>
    /// All valid boolean input strings (case-insensitive comparison recommended).
    /// </summary>
    public static readonly HashSet<string> AllBooleanValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "Y", "YES", "TRUE", "T", "1", "IS.TRUE", "IS.Y", "IS.YES", "IS.T", "IS.PROFITABLE",
        "N", "NO", "FALSE", "F", "0", "IS.FALSE", "IS.N", "IS.NO", "IS.F", "IS.NOTPROFITABLE"
    };

    /// <summary>
    /// Converts a boolean to its canonical IdiotScript representation.
    /// </summary>
    /// <param name="value">The boolean value to convert.</param>
    /// <returns>IS.TRUE or IS.FALSE</returns>
    public static string ToIdiotScriptBoolean(bool value) => value ? TRUE : FALSE;

    /// <summary>
    /// Normalizes a boolean input to its canonical form (IS.TRUE or IS.FALSE).
    /// </summary>
    /// <param name="input">Any valid boolean input string.</param>
    /// <returns>IS.TRUE, IS.FALSE, or null if invalid.</returns>
    public static string? NormalizeBoolean(string? input)
    {
        var resolved = ResolveBoolean(input);
        return resolved.HasValue ? ToIdiotScriptBoolean(resolved.Value) : null;
    }
}
