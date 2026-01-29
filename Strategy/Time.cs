// ============================================================================
// Time - Trading Session Time Definitions
// ============================================================================
// 
// BEST PRACTICES:
// 1. All times are in Central Standard Time (CST) - ensure your system clock
//    is configured correctly or convert from UTC as needed.
// 2. Use TradingPeriod.Start and .End properties for clarity.
// 3. Use TimeOnly.AddMinutes() for offsets (e.g., End.AddMinutes(-10)).
// 4. These are market session definitions - actual trading hours may vary
//    by broker, security type, and market conditions.
// 5. Consider daylight saving time when scheduling strategies.
//
// USAGE:
//   .Start(Time.PreMarket.Start)              // 3:00 AM CST
//   .End(Time.PreMarket.End)                  // 7:00 AM CST
//   .ClosePosition(Time.RTH.Start.AddMinutes(-10))  // 8:20 AM CST
//
// ============================================================================

using System;

namespace IdiotProof.Models
{
    /// <summary>
    /// Represents a trading session time period with start and end times.
    /// </summary>
    /// <remarks>
    /// <para><b>Best Practices:</b></para>
    /// <list type="bullet">
    ///   <item>Use the static <see cref="Time"/> class for predefined periods.</item>
    ///   <item>All times are in Central Standard Time (CST).</item>
    ///   <item>Use <see cref="TimeOnly.AddMinutes"/> for custom offsets.</item>
    /// </list>
    /// </remarks>
    public sealed class TradingPeriod
    {
        /// <summary>
        /// Start time of the trading period (CST).
        /// </summary>
        public TimeOnly Start { get; init; }

        /// <summary>
        /// End time of the trading period (CST).
        /// </summary>
        public TimeOnly End { get; init; }

        /// <summary>
        /// Initializes a new trading period with the specified start and end times.
        /// </summary>
        /// <param name="start">The start time of the period (CST).</param>
        /// <param name="end">The end time of the period (CST).</param>
        /// <exception cref="ArgumentException">Thrown if start is after end.</exception>
        /// <remarks>
        /// <b>Best Practice:</b> Validate that start &lt; end to avoid invalid time windows.
        /// </remarks>
        public TradingPeriod(TimeOnly start, TimeOnly end)
        {
            // Best Practice: Validate time ordering
            if (start >= end)
            {
                throw new ArgumentException($"Start time ({start}) must be before end time ({end}).", nameof(start));
            }

            Start = start;
            End = end;
        }

        /// <summary>
        /// Gets the duration of the trading period.
        /// </summary>
        /// <returns>The time span between start and end.</returns>
        public TimeSpan Duration => End.ToTimeSpan() - Start.ToTimeSpan();

        /// <summary>
        /// Determines if the specified time falls within this trading period.
        /// </summary>
        /// <param name="time">The time to check.</param>
        /// <returns>True if time is between Start and End (inclusive).</returns>
        public bool Contains(TimeOnly time) => time >= Start && time <= End;

        /// <summary>
        /// Returns a string representation of the trading period.
        /// </summary>
        public override string ToString() => $"{Start:HH:mm} - {End:HH:mm} CST";
    }

    /// <summary>
    /// Trading time definitions for different market sessions.
    /// All times are in Central Standard Time (CST).
    /// </summary>
    /// <remarks>
    /// <para><b>Best Practices:</b></para>
    /// <list type="bullet">
    ///   <item>Use <c>Time.PreMarket.Start</c> instead of hardcoding times.</item>
    ///   <item>Use <c>AddMinutes()</c> for offsets from period boundaries.</item>
    ///   <item>Be aware of daylight saving time transitions.</item>
    ///   <item>Verify broker supports extended hours trading before using.</item>
    /// </list>
    /// 
    /// <para><b>Example:</b></para>
    /// <code>
    /// .Start(Time.PreMarket.Start)                    // 3:00 AM CST
    /// .ClosePosition(Time.PreMarket.End.AddMinutes(-10))  // 6:50 AM CST
    /// .End(Time.PreMarket.End)                        // 7:00 AM CST
    /// </code>
    /// </remarks>
    public static class Time
    {
        /// <summary>
        /// Pre-market trading session: 3:00 AM - 7:00 AM CST.
        /// </summary>
        /// <remarks>
        /// <b>Note:</b> Pre-market liquidity is typically lower. Use limit orders
        /// and be cautious with market orders during this session.
        /// </remarks>
        public static TradingPeriod PreMarket { get; } = new(
            new TimeOnly(3, 0),   // 3:00 AM CST
            new TimeOnly(7, 0)    // 7:00 AM CST
        );

        /// <summary>
        /// Regular trading hours: 8:30 AM - 3:00 PM CST.
        /// </summary>
        /// <remarks>
        /// <b>Note:</b> Highest liquidity period. Best for market orders and
        /// strategies requiring fast execution.
        /// </remarks>
        public static TradingPeriod RTH { get; } = new(
            new TimeOnly(8, 30),  // 8:30 AM CST (9:30 AM EST)
            new TimeOnly(15, 0)   // 3:00 PM CST (4:00 PM EST)
        );

        /// <summary>
        /// After-hours trading session: 3:00 PM - 6:00 PM CST.
        /// </summary>
        /// <remarks>
        /// <b>Note:</b> After-hours liquidity can be very thin. Spreads may be
        /// wider than during RTH. Use caution with large orders.
        /// </remarks>
        public static TradingPeriod AfterHours { get; } = new(
            new TimeOnly(15, 0),  // 3:00 PM CST
            new TimeOnly(18, 0)   // 6:00 PM CST
        );

        /// <summary>
        /// Extended hours (pre-market + RTH + after-hours): 3:00 AM - 6:00 PM CST.
        /// </summary>
        /// <remarks>
        /// <b>Note:</b> Covers all possible trading times. Useful when you want
        /// a strategy to run across multiple sessions.
        /// </remarks>
        public static TradingPeriod Extended { get; } = new(
            new TimeOnly(3, 0),   // 3:00 AM CST
            new TimeOnly(18, 0)   // 6:00 PM CST
        );
    }
}
