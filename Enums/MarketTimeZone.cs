// ============================================================================
// MarketTimeZone - Trading Timezone Enum
// ============================================================================
//
// BEST PRACTICES:
// 1. Use this enum to specify the timezone for your trading strategies.
// 2. All market times (RTH, PreMarket, etc.) are defined in Eastern Time (EST/EDT)
//    as this is the standard for US equity markets.
// 3. The TimezoneHelper converts times between your local timezone and Eastern.
// 4. Be aware of daylight saving time transitions.
//
// USAGE:
//   Settings.Timezone = MarketTimeZone.CST;  // Set your local timezone
//   var localTime = TimezoneHelper.ToLocal(Time.RTH.Start);  // Convert to local
//
// ============================================================================

namespace IdiotProof.Enums
{
    /// <summary>
    /// Supported US timezone identifiers for trading strategy configuration.
    /// </summary>
    /// <remarks>
    /// <para><b>Important:</b> US equity markets operate on Eastern Time (EST/EDT).</para>
    /// <para>Market hours are always defined in Eastern Time:</para>
    /// <list type="bullet">
    ///   <item>Pre-Market: 4:00 AM - 9:30 AM ET</item>
    ///   <item>Regular Trading Hours (RTH): 9:30 AM - 4:00 PM ET</item>
    ///   <item>After-Hours: 4:00 PM - 8:00 PM ET</item>
    /// </list>
    /// <para>Use <see cref="Helpers.TimezoneHelper"/> to convert between Eastern Time and your local timezone.</para>
    /// <para><b>Default:</b> EST (Eastern Standard Time) is the default and recommended timezone setting.</para>
    /// </remarks>
    public enum MarketTimeZone
    {
        /// <summary>
        /// Eastern Time (US) - The standard timezone for US equity markets.
        /// Windows ID: "Eastern Standard Time"
        /// IBKR API ID: "US/Eastern"
        /// </summary>
        /// <remarks>
        /// <para><b>Default timezone.</b> No conversion needed when using Eastern Time.</para>
        /// <para>Market open: 9:30 AM ET, Market close: 4:00 PM ET.</para>
        /// <para>This is the recommended setting for most US equity trading.</para>
        /// </remarks>
        EST,

        /// <summary>
        /// Central Time (US) - 1 hour behind Eastern.
        /// Windows ID: "Central Standard Time"
        /// IBKR API ID: "US/Central"
        /// </summary>
        /// <remarks>
        /// Market open: 8:30 AM CT, Market close: 3:00 PM CT.
        /// </remarks>
        CST,

        /// <summary>
        /// Mountain Time (US) - 2 hours behind Eastern.
        /// Windows ID: "Mountain Standard Time"
        /// IBKR API ID: "US/Mountain"
        /// </summary>
        /// <remarks>
        /// Market open: 7:30 AM MT, Market close: 2:00 PM MT.
        /// </remarks>
        MST,

        /// <summary>
        /// Pacific Time (US) - 3 hours behind Eastern.
        /// Windows ID: "Pacific Standard Time"
        /// IBKR API ID: "US/Pacific"
        /// </summary>
        /// <remarks>
        /// Market open: 6:30 AM PT, Market close: 1:00 PM PT.
        /// </remarks>
        PST
    }
}
