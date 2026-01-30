// ============================================================================
// TimeInForce - Time In Force Options for Orders
// ============================================================================

namespace IdiotProof.Enums
{
    /// <summary>
    /// Time in force options for orders, controlling how long an order remains active.
    /// </summary>
    /// <remarks>
    /// <para><b>⚠️ IBKR API MAPPING - Values must match IB API string codes ⚠️</b></para>
    /// <para>Reference: https://interactivebrokers.github.io/tws-api/classIBApi_1_1Order.html</para>
    /// 
    /// <para><b>IB API Tif Codes:</b></para>
    /// <list type="table">
    ///   <listheader><term>Enum</term><description>IB Code</description></listheader>
    ///   <item><term><see cref="Day"/></term><description>"DAY"</description></item>
    ///   <item><term><see cref="GoodTillCancel"/></term><description>"GTC"</description></item>
    ///   <item><term><see cref="ImmediateOrCancel"/></term><description>"IOC"</description></item>
    ///   <item><term><see cref="FillOrKill"/></term><description>"FOK"</description></item>
    ///   <item><term><see cref="Overnight"/></term><description>"GTC" (with time limits)</description></item>
    ///   <item><term><see cref="OvernightPlusDay"/></term><description>"DTC"</description></item>
    ///   <item><term><see cref="AtTheOpening"/></term><description>"OPG"</description></item>
    /// </list>
    /// 
    /// <para><b>Session Times (Eastern):</b></para>
    /// <list type="bullet">
    ///   <item>Pre-market: 4:00 AM - 9:30 AM</item>
    ///   <item>Regular: 9:30 AM - 4:00 PM</item>
    ///   <item>After-hours: 4:00 PM - 8:00 PM</item>
    /// </list>
    /// </remarks>
    public enum TimeInForce
    {
        /// <summary>
        /// Day order - expires at end of trading day (4:00 PM EST).
        /// </summary>
        /// <remarks>
        /// <para><b>Behavior:</b></para>
        /// <list type="bullet">
        ///   <item>Order is only valid for today's trading session.</item>
        ///   <item>If not filled by market close, automatically cancelled.</item>
        ///   <item>Does not carry over to the next trading day.</item>
        /// </list>
        /// <para><b>Best for:</b> Normal trades you only care about today.</para>
        /// <para><b>IB API Code:</b> "DAY"</para>
        /// </remarks>
        Day,

        /// <summary>
        /// Good Till Cancelled - remains active until filled or cancelled.
        /// </summary>
        /// <remarks>
        /// <para><b>Behavior:</b></para>
        /// <list type="bullet">
        ///   <item>Order stays open until it fills or you manually cancel it.</item>
        ///   <item>Can last weeks or months depending on broker settings.</item>
        ///   <item>Persists across multiple trading sessions.</item>
        /// </list>
        /// <para><b>Best for:</b> Long-term limit orders you want sitting in the market.</para>
        /// <para><b>IB API Code:</b> "GTC"</para>
        /// </remarks>
        GoodTillCancel,

        /// <summary>
        /// Immediate or Cancel - fill what's available immediately, cancel rest.
        /// </summary>
        /// <remarks>
        /// <para><b>Behavior:</b></para>
        /// <list type="bullet">
        ///   <item>Attempts to fill as much as possible immediately.</item>
        ///   <item>Any unfilled portion is cancelled.</item>
        ///   <item>May result in partial fills.</item>
        /// </list>
        /// <para><b>Best for:</b> When you want immediate execution; partial fills acceptable.</para>
        /// <para><b>IB API Code:</b> "IOC"</para>
        /// </remarks>
        ImmediateOrCancel,

        /// <summary>
        /// Fill or Kill - fill entire order immediately or cancel completely.
        /// </summary>
        /// <remarks>
        /// <para><b>Behavior:</b></para>
        /// <list type="bullet">
        ///   <item>Order must be filled completely and immediately.</item>
        ///   <item>If full quantity not available, entire order is cancelled.</item>
        ///   <item>No partial fills allowed.</item>
        /// </list>
        /// <para><b>Best for:</b> When all-or-nothing execution is required.</para>
        /// <para><b>IB API Code:</b> "FOK"</para>
        /// </remarks>
        FillOrKill,

        /// <summary>
        /// Overnight - active only during after-hours/overnight trading sessions.
        /// </summary>
        /// <remarks>
        /// <para><b>Behavior:</b></para>
        /// <list type="bullet">
        ///   <item>Order is active in after-hours and pre-market sessions.</item>
        ///   <item>Used when the regular market is closed but broker supports extended hours.</item>
        ///   <item>Typically covers 4:00 PM - 9:30 AM EST (after-hours + pre-market).</item>
        ///   <item>Cancelled if not filled before regular session opens.</item>
        /// </list>
        /// <para><b>Best for:</b> Trading news events outside regular hours; earnings plays.</para>
        /// <para><b>IB API Code:</b> "OPG" (Note: IB uses OPG for extended hours context)</para>
        /// <para><b>Note:</b> Requires OutsideRth = true.</para>
        /// </remarks>
        Overnight,

        /// <summary>
        /// Overnight + Day - spans overnight session and next regular day session.
        /// </summary>
        /// <remarks>
        /// <para><b>Behavior:</b></para>
        /// <list type="bullet">
        ///   <item>Order stays active through overnight session AND the next regular day.</item>
        ///   <item>Provides continuous coverage from extended hours into regular trading.</item>
        ///   <item>If still not filled by end of next regular session, order cancels.</item>
        /// </list>
        /// <para><b>Best for:</b> One continuous order spanning extended hours into tomorrow.</para>
        /// <para><b>IB API Code:</b> "DTC" (Day Till Cancelled variant)</para>
        /// <para><b>Note:</b> Requires OutsideRth = true for overnight portion.</para>
        /// </remarks>
        OvernightPlusDay,

        /// <summary>
        /// At the Opening - order executes only at market open auction.
        /// </summary>
        /// <remarks>
        /// <para><b>Behavior:</b></para>
        /// <list type="bullet">
        ///   <item>Order is only active at market open (9:30 AM EST).</item>
        ///   <item>Participates in the opening auction/cross.</item>
        ///   <item>If not filled in the opening auction, order is cancelled.</item>
        ///   <item>Cannot be cancelled or modified during pre-open period.</item>
        /// </list>
        /// <para><b>Best for:</b> Trying to catch the opening price move; gap plays.</para>
        /// <para><b>IB API Code:</b> "OPG"</para>
        /// <para><b>Warning:</b> Price may differ significantly from pre-market prices.</para>
        /// </remarks>
        AtTheOpening
    }
}
