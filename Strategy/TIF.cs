// ============================================================================
// TIF - Time In Force Shorthand Alias
// ============================================================================
//
// BEST PRACTICES:
// 1. Use TIF.GTC for orders that should persist until filled or cancelled.
// 2. Use TIF.Day for orders that should expire at end of trading day.
// 3. Use TIF.IOC/FOK for orders requiring immediate execution.
// 4. Use TIF.Overnight for after-hours/pre-market trading only.
// 5. Use TIF.OvernightPlusDay for continuous overnight-to-day coverage.
// 6. Use TIF.AtTheOpening to catch the market open auction.
//
// USAGE:
//   .TimeInForce(TIF.GTC)              // Good Till Cancelled
//   .TimeInForce(TIF.Day)              // Day order
//   .TimeInForce(TIF.Overnight)        // Extended hours only
//   .TimeInForce(TIF.OvernightPlusDay) // Overnight + next day
//   .TimeInForce(TIF.AtTheOpening)     // Opening auction only
//
// ============================================================================

using IdiotProof.Enums;

namespace IdiotProof.Models
{
    /// <summary>
    /// Shorthand alias for <see cref="TimeInForce"/> enum values.
    /// Provides cleaner, more readable code when configuring orders.
    /// </summary>
    /// <remarks>
    /// <para><b>Best Practices:</b></para>
    /// <list type="bullet">
    ///   <item><see cref="GTC"/>: Default choice for most strategies; persists until filled.</item>
    ///   <item><see cref="Day"/>: Use when order should not carry over to next session.</item>
    ///   <item><see cref="IOC"/>: Use for partial fills acceptable; unfilled portion cancelled.</item>
    ///   <item><see cref="FOK"/>: Use when all-or-nothing execution is required.</item>
    ///   <item><see cref="Overnight"/>: Use for extended hours trading (after-hours/pre-market).</item>
    ///   <item><see cref="OvernightPlusDay"/>: Use for continuous overnight-to-day coverage.</item>
    ///   <item><see cref="AtTheOpening"/>: Use to catch opening auction price.</item>
    /// </list>
    /// 
    /// <para><b>Session Times (Eastern):</b></para>
    /// <list type="bullet">
    ///   <item>Pre-market: 4:00 AM - 9:30 AM</item>
    ///   <item>Regular: 9:30 AM - 4:00 PM</item>
    ///   <item>After-hours: 4:00 PM - 8:00 PM</item>
    /// </list>
    /// 
    /// <para><b>Example:</b></para>
    /// <code>
    /// .TimeInForce(TIF.GTC)              // Stays active until filled
    /// .TimeInForce(TIF.Day)              // Expires at 4:00 PM EST
    /// .TimeInForce(TIF.Overnight)        // Active during extended hours only
    /// .TimeInForce(TIF.OvernightPlusDay) // Extended hours + next day
    /// .TimeInForce(TIF.AtTheOpening)     // Opening auction only
    /// </code>
    /// </remarks>
    public static class TIF
    {
        #region Primary TIF Values

        /// <summary>
        /// Day order - expires at end of trading day.
        /// </summary>
        /// <remarks>
        /// <para><b>Use when:</b> You only want the order active for today's session.</para>
        /// <para><b>Behavior:</b> Automatically cancelled at market close (4:00 PM EST).</para>
        /// <para><b>IB Code:</b> "DAY"</para>
        /// </remarks>
        public static TimeInForce Day => TimeInForce.Day;

        /// <summary>
        /// Good Till Cancelled - remains active until filled or cancelled.
        /// </summary>
        /// <remarks>
        /// <para><b>Use when:</b> Order should persist across trading sessions.</para>
        /// <para><b>Behavior:</b> Can last weeks or months; required for pre-market/after-hours.</para>
        /// <para><b>Default:</b> Recommended default for most strategies.</para>
        /// <para><b>IB Code:</b> "GTC"</para>
        /// </remarks>
        public static TimeInForce GoodTillCancel => TimeInForce.GoodTillCancel;

        /// <summary>
        /// Immediate or Cancel - fill immediately or cancel unfilled portion.
        /// </summary>
        /// <remarks>
        /// <para><b>Use when:</b> You want to fill what's available now; don't wait.</para>
        /// <para><b>Behavior:</b> Partial fills allowed; unfilled quantity cancelled.</para>
        /// <para><b>Risk:</b> May result in partial fills.</para>
        /// <para><b>IB Code:</b> "IOC"</para>
        /// </remarks>
        public static TimeInForce ImmediateOrCancel => TimeInForce.ImmediateOrCancel;

        /// <summary>
        /// Fill or Kill - fill entire order immediately or cancel completely.
        /// </summary>
        /// <remarks>
        /// <para><b>Use when:</b> All-or-nothing execution is required.</para>
        /// <para><b>Behavior:</b> If full quantity not available, entire order cancelled.</para>
        /// <para><b>Risk:</b> Order may be cancelled if full quantity not available.</para>
        /// <para><b>IB Code:</b> "FOK"</para>
        /// </remarks>
        public static TimeInForce FillOrKill => TimeInForce.FillOrKill;

        /// <summary>
        /// Overnight - active only during after-hours/overnight trading sessions.
        /// </summary>
        /// <remarks>
        /// <para><b>Use when:</b> Trading news events outside regular hours.</para>
        /// <para><b>Behavior:</b> Active in after-hours (4 PM - 8 PM) and pre-market (4 AM - 9:30 AM).</para>
        /// <para><b>Note:</b> Cancelled if not filled before regular session opens.</para>
        /// <para><b>Requirement:</b> Set OutsideRth = true.</para>
        /// <para><b>IB Code:</b> "GTC" (with time limits)</para>
        /// </remarks>
        public static TimeInForce Overnight => TimeInForce.Overnight;

        /// <summary>
        /// Overnight + Day - spans overnight session and next regular day session.
        /// </summary>
        /// <remarks>
        /// <para><b>Use when:</b> You want continuous coverage from extended hours into tomorrow.</para>
        /// <para><b>Behavior:</b> Active through overnight + next regular session; then cancels.</para>
        /// <para><b>Note:</b> Provides seamless coverage across session boundaries.</para>
        /// <para><b>Requirement:</b> Set OutsideRth = true for overnight portion.</para>
        /// <para><b>IB Code:</b> "DTC"</para>
        /// </remarks>
        public static TimeInForce OvernightPlusDay => TimeInForce.OvernightPlusDay;

        /// <summary>
        /// At the Opening - order executes only at market open auction.
        /// </summary>
        /// <remarks>
        /// <para><b>Use when:</b> Trying to catch the opening price move or gap plays.</para>
        /// <para><b>Behavior:</b> Participates in opening auction; cancelled if not filled.</para>
        /// <para><b>Warning:</b> Price may differ significantly from pre-market prices.</para>
        /// <para><b>Note:</b> Cannot be cancelled during pre-open period.</para>
        /// <para><b>IB Code:</b> "OPG"</para>
        /// </remarks>
        public static TimeInForce AtTheOpening => TimeInForce.AtTheOpening;

        #endregion

        #region Shorthand Aliases

        /// <summary>
        /// Alias for <see cref="GoodTillCancel"/> - Good Till Cancelled.
        /// </summary>
        /// <remarks>
        /// <para>Shorthand for cleaner code: <c>.TimeInForce(TIF.GTC)</c></para>
        /// <para><b>IB Code:</b> "GTC"</para>
        /// </remarks>
        public static TimeInForce GTC => TimeInForce.GoodTillCancel;

        /// <summary>
        /// Alias for <see cref="ImmediateOrCancel"/> - Immediate or Cancel.
        /// </summary>
        /// <remarks>
        /// <para>Shorthand for cleaner code: <c>.TimeInForce(TIF.IOC)</c></para>
        /// <para><b>IB Code:</b> "IOC"</para>
        /// </remarks>
        public static TimeInForce IOC => TimeInForce.ImmediateOrCancel;

        /// <summary>
        /// Alias for <see cref="FillOrKill"/> - Fill or Kill.
        /// </summary>
        /// <remarks>
        /// <para>Shorthand for cleaner code: <c>.TimeInForce(TIF.FOK)</c></para>
        /// <para><b>IB Code:</b> "FOK"</para>
        /// </remarks>
        public static TimeInForce FOK => TimeInForce.FillOrKill;

        /// <summary>
        /// Alias for <see cref="Overnight"/> - Overnight/Extended Hours.
        /// </summary>
        /// <remarks>
        /// <para>Shorthand for cleaner code: <c>.TimeInForce(TIF.OVN)</c></para>
        /// <para><b>Use for:</b> After-hours and pre-market trading.</para>
        /// </remarks>
        public static TimeInForce OVN => TimeInForce.Overnight;

        /// <summary>
        /// Alias for <see cref="OvernightPlusDay"/> - Overnight + Day.
        /// </summary>
        /// <remarks>
        /// <para>Shorthand for cleaner code: <c>.TimeInForce(TIF.OVNDAY)</c></para>
        /// <para><b>Use for:</b> Continuous overnight-to-day coverage.</para>
        /// </remarks>
        public static TimeInForce OVNDAY => TimeInForce.OvernightPlusDay;

        /// <summary>
        /// Alias for <see cref="AtTheOpening"/> - At the Opening.
        /// </summary>
        /// <remarks>
        /// <para>Shorthand for cleaner code: <c>.TimeInForce(TIF.OPG)</c></para>
        /// <para><b>Use for:</b> Opening auction participation.</para>
        /// </remarks>
        public static TimeInForce OPG => TimeInForce.AtTheOpening;

        #endregion
    }
}
