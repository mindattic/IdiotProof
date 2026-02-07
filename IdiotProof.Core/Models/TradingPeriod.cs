// ============================================================================
// TradingPeriod - Trading Session Time Period
// ============================================================================
//
// BEST PRACTICES:
// 1. All times are defined in Eastern Time (ET) - the standard for US equity markets.
// 2. Use TimezoneHelper to convert to/from your local timezone.
// 3. Use TimeOnly.AddMinutes() for offsets (e.g., End.AddMinutes(-15)).
// 4. These are market session definitions - actual trading hours may vary
//    by broker, security type, and market conditions.
// 5. The TimezoneHelper automatically handles daylight saving time.
//
// USAGE:
//   var period = new TradingPeriod(new TimeOnly(9, 30), new TimeOnly(16, 0));
//   period.Contains(new TimeOnly(10, 0))  // true - 10:00 AM is within RTH
//
// ============================================================================

using System;
using IdiotProof.Backend.Helpers;
using IdiotProof.Shared.Enums;
using IdiotProof.Shared.Settings;

namespace IdiotProof.Backend.Models
{
    /// <summary>
    /// Represents a trading session time period with start and end times.
    /// All times are in Eastern Time (ET) - the standard for US equity markets.
    /// </summary>
    /// <remarks>
    /// <para><b>Best Practices:</b></para>
    /// <list type="bullet">
    ///   <item>Use the static <see cref="MarketTime"/> class for predefined periods.</item>
    ///   <item>All times are in Eastern Time (ET) - market standard.</item>
    ///   <item>Use <see cref="TimezoneHelper"/> to convert to local timezone.</item>
    ///   <item>Use <see cref="TimeOnly.AddMinutes"/> for custom offsets.</item>
    /// </list>
    /// </remarks>
    public sealed class TradingPeriod
    {
        /// <summary>
        /// Start time of the trading period (Eastern Time).
        /// </summary>
        public TimeOnly Start { get; init; }

        /// <summary>
        /// End time of the trading period (Eastern Time).
        /// </summary>
        public TimeOnly End { get; init; }

        /// <summary>
        /// Start time converted to the configured local timezone (Settings.Timezone).
        /// </summary>
        public TimeOnly StartLocal => TimezoneHelper.ToLocal(Start, Settings.Timezone);

        /// <summary>
        /// End time converted to the configured local timezone (Settings.Timezone).
        /// </summary>
        public TimeOnly EndLocal => TimezoneHelper.ToLocal(End, Settings.Timezone);

        /// <summary>
        /// A safe buffer time after the period starts (15 minutes after Start).
        /// Useful for waiting for initial volatility to settle.
        /// </summary>
        /// <remarks>
        /// <para><b>Usage:</b></para>
        /// <code>
        /// .Start(Time.PreMarket.Starting)  // 4:15 AM ET (15 min after 4:00)
        /// .Start(Time.RTH.Starting)        // 9:45 AM ET (15 min after 9:30)
        /// </code>
        /// </remarks>
        public TimeOnly Starting => Start.AddMinutes(15);

        /// <summary>
        /// Starting time converted to the configured local timezone (Settings.Timezone).
        /// </summary>
        public TimeOnly StartingLocal => TimezoneHelper.ToLocal(Starting, Settings.Timezone);

        /// <summary>
        /// A safe buffer time before the period ends (15 minutes before End).
        /// Useful for closing positions before the session ends.
        /// </summary>
        /// <remarks>
        /// <para><b>Usage:</b></para>
        /// <code>
        /// .ClosePosition(Time.PreMarket.Ending)  // 9:15 AM ET (15 min before 9:30)
        /// .ClosePosition(Time.RTH.Ending)        // 3:45 PM ET (15 min before 4:00)
        /// </code>
        /// </remarks>
        public TimeOnly Ending => End.AddMinutes(-15);

        /// <summary>
        /// Ending time converted to the configured local timezone (Settings.Timezone).
        /// </summary>
        public TimeOnly EndingLocal => TimezoneHelper.ToLocal(Ending, Settings.Timezone);

        /// <summary>
        /// Initializes a new trading period with the specified start and end times.
        /// </summary>
        /// <param name="start">The start time of the period (Eastern Time).</param>
        /// <param name="end">The end time of the period (Eastern Time).</param>
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
        /// Time is assumed to be in Eastern Time.
        /// </summary>
        /// <param name="time">The time to check (Eastern Time).</param>
        /// <returns>True if time is between Start and End (inclusive).</returns>
        public bool Contains(TimeOnly time) => time >= Start && time <= End;

        /// <summary>
        /// Determines if the specified local time falls within this trading period.
        /// Converts the local time to Eastern Time before checking.
        /// </summary>
        /// <param name="localTime">The time in local timezone.</param>
        /// <param name="localTimezone">The local timezone.</param>
        /// <returns>True if the time (converted to ET) is between Start and End (inclusive).</returns>
        public bool ContainsLocal(TimeOnly localTime, MarketTimeZone localTimezone)
        {
            var easternTime = TimezoneHelper.ToEastern(localTime, localTimezone);
            return Contains(easternTime);
        }

        /// <summary>
        /// Returns a string representation of the trading period in Eastern Time.
        /// </summary>
        public override string ToString() => $"{Start:HH:mm} - {End:HH:mm} ET";

        /// <summary>
        /// Returns a string representation with both Eastern and local times.
        /// </summary>
        /// <param name="localTimezone">The local timezone for display.</param>
        public string ToString(MarketTimeZone localTimezone)
        {
            var startLocal = TimezoneHelper.ToLocal(Start, localTimezone);
            var endLocal = TimezoneHelper.ToLocal(End, localTimezone);
            var abbrev = TimezoneHelper.GetTimezoneAbbreviation(localTimezone);
            return $"{startLocal:HH:mm} - {endLocal:HH:mm} {abbrev} ({Start:HH:mm} - {End:HH:mm} ET)";
        }
    }
}


