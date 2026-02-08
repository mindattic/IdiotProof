// ============================================================================
// Time - Trading Session Time Definitions
// ============================================================================
// 
// BEST PRACTICES:
// 1. All times are defined in Eastern Time (ET) - the standard for US equity markets.
// 2. Use TimezoneHelper to convert to/from your local timezone.
// 3. Use TradingPeriod.Start and .End properties for clarity.
// 4. Use TimeOnly.AddMinutes() for offsets (e.g., End.AddMinutes(-10)).
// 5. These are market session definitions - actual trading hours may vary
//    by broker, security type, and market conditions.
// 6. The TimezoneHelper automatically handles daylight saving time.
//
// MARKET HOURS (Eastern Time):
//   Pre-Market:   4:00 AM - 9:30 AM ET
//   RTH:          9:30 AM - 4:00 PM ET
//   After-Hours:  4:00 PM - 8:00 PM ET
//
// USAGE:
//   .Start(Time.PreMarket.Start)              // 4:00 AM ET
//   .End(Time.PreMarket.End)                  // 9:30 AM ET
//   .ClosePosition(Time.RTH.Start.AddMinutes(-10))  // 9:20 AM ET
//
//   // Convert to local timezone
//   var localOpen = Time.RTH.StartLocal;      // Uses AppSettings.Timezone
//
// ============================================================================

using IdiotProof.Helpers;
using IdiotProof.Settings;
using System;

namespace IdiotProof.Models {
    /// <summary>
    /// Trading time definitions for different market sessions.
    /// All times are in Eastern Time (ET) - the standard for US equity markets.
    /// </summary>
    /// <remarks>
    /// <para><b>Market Hours (Eastern Time):</b></para>
    /// <list type="bullet">
    ///   <item>Pre-Market: 4:00 AM - 9:30 AM ET</item>
    ///   <item>Regular Trading Hours (RTH): 9:30 AM - 4:00 PM ET</item>
    ///   <item>After-Hours: 4:00 PM - 8:00 PM ET</item>
    /// </list>
    /// 
    /// <para><b>Best Practices:</b></para>
    /// <list type="bullet">
    ///   <item>Use <c>Time.PreMarket.Start</c> instead of hardcoding times.</item>
    ///   <item>Use <c>AddMinutes()</c> for offsets from period boundaries.</item>
    ///   <item>Use <c>StartLocal</c> and <c>EndLocal</c> for local timezone display.</item>
    ///   <item>Verify broker supports extended hours trading before using.</item>
    /// </list>
    /// 
    /// <para><b>Example:</b></para>
    /// <code>
    /// .Start(Time.PreMarket.Start)                    // 4:00 AM ET
    /// .ClosePosition(Time.RTH.Start.AddMinutes(-10))  // 9:20 AM ET
    /// .End(Time.PreMarket.End)                        // 9:30 AM ET
    /// 
    /// // Display in local timezone
    /// Console.WriteLine(Time.RTH.ToString(AppSettings.Timezone));
    /// </code>
    /// </remarks>
    public static class MarketTime
    {
        /// <summary>
        /// Pre-market trading session: 4:00 AM - 9:30 AM ET.
        /// </summary>
        /// <remarks>
        /// <para><b>Note:</b> Pre-market liquidity is typically lower. Use limit orders
        /// and be cautious with market orders during this session.</para>
        /// <para><b>Local Times:</b></para>
        /// <list type="bullet">
        ///   <item>EST: 4:00 AM - 9:30 AM</item>
        ///   <item>CST: 3:00 AM - 8:30 AM</item>
        ///   <item>MST: 2:00 AM - 7:30 AM</item>
        ///   <item>PST: 1:00 AM - 6:30 AM</item>
        /// </list>
        /// </remarks>
        public static TradingPeriod PreMarket { get; } = new(
            new TimeOnly(4, 0),   // 4:00 AM ET
            new TimeOnly(9, 30)   // 9:30 AM ET
        );

        /// <summary>
        /// Regular trading hours: 9:30 AM - 4:00 PM ET.
        /// </summary>
        /// <remarks>
        /// <para><b>Note:</b> Highest liquidity period. Best for market orders and
        /// strategies requiring fast execution.</para>
        /// <para><b>Local Times:</b></para>
        /// <list type="bullet">
        ///   <item>EST: 9:30 AM - 4:00 PM</item>
        ///   <item>CST: 8:30 AM - 3:00 PM</item>
        ///   <item>MST: 7:30 AM - 2:00 PM</item>
        ///   <item>PST: 6:30 AM - 1:00 PM</item>
        /// </list>
        /// </remarks>
        public static TradingPeriod RTH { get; } = new(
            new TimeOnly(9, 30),  // 9:30 AM ET (Market Open)
            new TimeOnly(16, 0)   // 4:00 PM ET (Market Close)
        );

        /// <summary>
        /// After-hours trading session: 4:00 PM - 8:00 PM ET.
        /// </summary>
        /// <remarks>
        /// <para><b>Note:</b> After-hours liquidity can be very thin. Spreads may be
        /// wider than during RTH. Use caution with large orders.</para>
        /// <para><b>Local Times:</b></para>
        /// <list type="bullet">
        ///   <item>EST: 4:00 PM - 8:00 PM</item>
        ///   <item>CST: 3:00 PM - 7:00 PM</item>
        ///   <item>MST: 2:00 PM - 6:00 PM</item>
        ///   <item>PST: 1:00 PM - 5:00 PM</item>
        /// </list>
        /// </remarks>
        public static TradingPeriod AfterHours { get; } = new(
            new TimeOnly(16, 0),  // 4:00 PM ET
            new TimeOnly(20, 0)   // 8:00 PM ET
        );

        /// <summary>
        /// Extended hours (pre-market + RTH + after-hours): 4:00 AM - 8:00 PM ET.
        /// </summary>
        /// <remarks>
        /// <para><b>Note:</b> Covers all possible trading times. Useful when you want
        /// a strategy to run across multiple sessions.</para>
        /// <para><b>Local Times:</b></para>
        /// <list type="bullet">
        ///   <item>EST: 4:00 AM - 8:00 PM</item>
        ///   <item>CST: 3:00 AM - 7:00 PM</item>
        ///   <item>MST: 2:00 AM - 6:00 PM</item>
        ///   <item>PST: 1:00 AM - 5:00 PM</item>
        /// </list>
        /// </remarks>
        public static TradingPeriod Extended { get; } = new(
            new TimeOnly(4, 0),   // 4:00 AM ET
            new TimeOnly(20, 0)   // 8:00 PM ET
        );

        /// <summary>
        /// Converts an Eastern Time to the configured local timezone.
        /// </summary>
        /// <param name="easternTime">The time in Eastern Time.</param>
        /// <returns>The time in the configured local timezone (AppSettings.Timezone).</returns>
        public static TimeOnly ToLocal(TimeOnly easternTime) =>
            TimezoneHelper.ToLocal(easternTime, AppSettings.Timezone);

        /// <summary>
        /// Converts a local time to Eastern Time.
        /// </summary>
        /// <param name="localTime">The time in local timezone.</param>
        /// <returns>The time in Eastern Time.</returns>
        public static TimeOnly ToEastern(TimeOnly localTime) =>
            TimezoneHelper.ToEastern(localTime, AppSettings.Timezone);
    }
}


