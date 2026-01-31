// ============================================================================
// TimezoneHelper - Timezone Conversion Utilities
// ============================================================================
//
// BEST PRACTICES:
// 1. All internal trading times should be stored in Eastern Time (market time).
// 2. Convert to/from local timezone only for display and user input.
// 3. Use TimeZoneInfo for accurate DST handling.
// 4. Always validate timezone conversions in unit tests.
//
// USAGE:
//   // Convert Eastern Time to local timezone
//   var localTime = TimezoneHelper.ToLocal(new TimeOnly(9, 30));  // RTH open in local time
//
//   // Convert local time to Eastern Time
//   var easternTime = TimezoneHelper.ToEastern(new TimeOnly(8, 30));  // Local to Eastern
//
//   // Get timezone info
//   var info = TimezoneHelper.GetTimezoneInfo();
//   Console.WriteLine(info.DisplayString);
//
// IBKR API TIMEZONE REFERENCE:
// ============================================================================
// The Interactive Brokers TWS API uses specific timezone identifiers:
//
// WINDOWS TIMEZONE IDs (for TimeZoneInfo.FindSystemTimeZoneById):
//   • "Eastern Standard Time"  - Eastern Time (EST/EDT), New York
//   • "Central Standard Time"  - Central Time (CST/CDT), Chicago
//   • "Mountain Standard Time" - Mountain Time (MST/MDT), Denver
//   • "Pacific Standard Time"  - Pacific Time (PST/PDT), Los Angeles
//
// IBKR API TIMEZONE STRINGS (for historical data requests):
//   • "US/Eastern"  - Eastern Time zone (IBKR format)
//   • "US/Central"  - Central Time zone (IBKR format)
//   • "US/Mountain" - Mountain Time zone (IBKR format)
//   • "US/Pacific"  - Pacific Time zone (IBKR format)
//
// IBKR Historical Data Notes:
//   • Default timezone for US equities is Eastern Time
//   • reqHistoricalData endDateTime format: "YYYYMMDD HH:mm:ss" + timezone
//   • Example: "20240115 16:00:00 US/Eastern"
//
// Reference: https://interactivebrokers.github.io/tws-api/historical_bars.html
// ============================================================================

using System;
using IdiotProof.Backend.Enums;
using IdiotProof.Backend.Models;

namespace IdiotProof.Backend.Helpers
{
    /// <summary>
    /// Provides timezone conversion utilities for trading applications.
    /// Handles conversions between Eastern Time (market time) and local timezone.
    /// </summary>
    /// <remarks>
    /// <para><b>Design Philosophy:</b></para>
    /// <para>US equity markets operate on Eastern Time. This helper provides
    /// bidirectional conversion between Eastern Time and the configured local timezone.</para>
    /// 
    /// <para><b>DST Handling:</b></para>
    /// <para>Uses <see cref="TimeZoneInfo"/> for accurate daylight saving time handling.
    /// During DST transitions, the offset between timezones may change.</para>
    /// 
    /// <para><b>Thread Safety:</b></para>
    /// <para>All methods are thread-safe. Timezone configuration is read from Settings.</para>
    /// </remarks>
    public static class TimezoneHelper
    {
        // Windows timezone IDs (for TimeZoneInfo.FindSystemTimeZoneById)
        private const string EasternTimezoneId = "Eastern Standard Time";
        private const string CentralTimezoneId = "Central Standard Time";
        private const string MountainTimezoneId = "Mountain Standard Time";
        private const string PacificTimezoneId = "Pacific Standard Time";

        // IBKR API timezone strings (for historical data requests)
        // Reference: https://interactivebrokers.github.io/tws-api/historical_bars.html
        private const string IbkrEasternTimezone = "US/Eastern";
        private const string IbkrCentralTimezone = "US/Central";
        private const string IbkrMountainTimezone = "US/Mountain";
        private const string IbkrPacificTimezone = "US/Pacific";

        // Cached timezone info objects
        private static readonly TimeZoneInfo EasternTimeZone = TimeZoneInfo.FindSystemTimeZoneById(EasternTimezoneId);
        private static readonly TimeZoneInfo CentralTimeZone = TimeZoneInfo.FindSystemTimeZoneById(CentralTimezoneId);
        private static readonly TimeZoneInfo MountainTimeZone = TimeZoneInfo.FindSystemTimeZoneById(MountainTimezoneId);
        private static readonly TimeZoneInfo PacificTimeZone = TimeZoneInfo.FindSystemTimeZoneById(PacificTimezoneId);

        /// <summary>
        /// Gets the <see cref="TimeZoneInfo"/> for the specified <see cref="MarketTimeZone"/>.
        /// </summary>
        /// <param name="timezone">The market timezone enum value.</param>
        /// <returns>The corresponding <see cref="TimeZoneInfo"/> object.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown if timezone is not a valid enum value.</exception>
        public static TimeZoneInfo GetTimeZoneInfo(MarketTimeZone timezone)
        {
            return timezone switch
            {
                MarketTimeZone.EST => EasternTimeZone,
                MarketTimeZone.CST => CentralTimeZone,
                MarketTimeZone.MST => MountainTimeZone,
                MarketTimeZone.PST => PacificTimeZone,
                _ => throw new ArgumentOutOfRangeException(nameof(timezone), timezone, "Invalid timezone value.")
            };
        }

        /// <summary>
        /// Gets the Windows timezone ID string for the specified <see cref="MarketTimeZone"/>.
        /// </summary>
        /// <param name="timezone">The market timezone enum value.</param>
        /// <returns>The Windows timezone ID string.</returns>
        public static string GetTimezoneId(MarketTimeZone timezone)
        {
            return timezone switch
            {
                MarketTimeZone.EST => EasternTimezoneId,
                MarketTimeZone.CST => CentralTimezoneId,
                MarketTimeZone.MST => MountainTimezoneId,
                MarketTimeZone.PST => PacificTimezoneId,
                _ => throw new ArgumentOutOfRangeException(nameof(timezone), timezone, "Invalid timezone value.")
            };
        }

        /// <summary>
        /// Gets the IBKR API timezone string for the specified <see cref="MarketTimeZone"/>.
        /// Use this for historical data requests (reqHistoricalData endDateTime parameter).
        /// </summary>
        /// <param name="timezone">The market timezone enum value.</param>
        /// <returns>The IBKR timezone string (e.g., "US/Eastern", "US/Central").</returns>
        /// <remarks>
        /// <para><b>IBKR Timezone Format:</b></para>
        /// <list type="table">
        ///   <listheader><term>MarketTimeZone</term><description>IBKR String</description></listheader>
        ///   <item><term>EST</term><description>US/Eastern</description></item>
        ///   <item><term>CST</term><description>US/Central</description></item>
        ///   <item><term>MST</term><description>US/Mountain</description></item>
        ///   <item><term>PST</term><description>US/Pacific</description></item>
        /// </list>
        /// <para><b>Usage Example:</b></para>
        /// <code>
        /// // Format for reqHistoricalData
        /// var endDateTime = $"20240115 16:00:00 {TimezoneHelper.GetIbkrTimezoneString(MarketTimeZone.EST)}";
        /// // Result: "20240115 16:00:00 US/Eastern"
        /// </code>
        /// <para><b>Reference:</b> https://interactivebrokers.github.io/tws-api/historical_bars.html</para>
        /// </remarks>
        public static string GetIbkrTimezoneString(MarketTimeZone timezone)
        {
            return timezone switch
            {
                MarketTimeZone.EST => IbkrEasternTimezone,
                MarketTimeZone.CST => IbkrCentralTimezone,
                MarketTimeZone.MST => IbkrMountainTimezone,
                MarketTimeZone.PST => IbkrPacificTimezone,
                _ => throw new ArgumentOutOfRangeException(nameof(timezone), timezone, "Invalid timezone value.")
            };
        }

        /// <summary>
        /// Converts an IBKR timezone string to a <see cref="MarketTimeZone"/> enum value.
        /// </summary>
        /// <param name="ibkrTimezone">The IBKR timezone string (e.g., "US/Eastern").</param>
        /// <returns>The corresponding <see cref="MarketTimeZone"/> enum value.</returns>
        /// <exception cref="ArgumentException">Thrown if the IBKR timezone string is not recognized.</exception>
        public static MarketTimeZone FromIbkrTimezoneString(string ibkrTimezone)
        {
            return ibkrTimezone switch
            {
                IbkrEasternTimezone => MarketTimeZone.EST,
                IbkrCentralTimezone => MarketTimeZone.CST,
                IbkrMountainTimezone => MarketTimeZone.MST,
                IbkrPacificTimezone => MarketTimeZone.PST,
                _ => throw new ArgumentException($"Unrecognized IBKR timezone string: '{ibkrTimezone}'", nameof(ibkrTimezone))
            };
        }

        /// <summary>
        /// Converts a Windows timezone ID to a <see cref="MarketTimeZone"/> enum value.
        /// </summary>
        /// <param name="windowsTimezoneId">The Windows timezone ID (e.g., "Eastern Standard Time").</param>
        /// <returns>The corresponding <see cref="MarketTimeZone"/> enum value.</returns>
        /// <exception cref="ArgumentException">Thrown if the Windows timezone ID is not recognized.</exception>
        public static MarketTimeZone FromWindowsTimezoneId(string windowsTimezoneId)
        {
            return windowsTimezoneId switch
            {
                EasternTimezoneId => MarketTimeZone.EST,
                CentralTimezoneId => MarketTimeZone.CST,
                MountainTimezoneId => MarketTimeZone.MST,
                PacificTimezoneId => MarketTimeZone.PST,
                _ => throw new ArgumentException($"Unrecognized Windows timezone ID: '{windowsTimezoneId}'", nameof(windowsTimezoneId))
            };
        }

        /// <summary>
        /// Converts an IBKR timezone string to a Windows timezone ID.
        /// </summary>
        /// <param name="ibkrTimezone">The IBKR timezone string (e.g., "US/Eastern").</param>
        /// <returns>The corresponding Windows timezone ID (e.g., "Eastern Standard Time").</returns>
        /// <exception cref="ArgumentException">Thrown if the IBKR timezone string is not recognized.</exception>
        public static string IbkrToWindowsTimezoneId(string ibkrTimezone)
        {
            var marketTimezone = FromIbkrTimezoneString(ibkrTimezone);
            return GetTimezoneId(marketTimezone);
        }

        /// <summary>
        /// Converts a Windows timezone ID to an IBKR timezone string.
        /// </summary>
        /// <param name="windowsTimezoneId">The Windows timezone ID (e.g., "Eastern Standard Time").</param>
        /// <returns>The corresponding IBKR timezone string (e.g., "US/Eastern").</returns>
        /// <exception cref="ArgumentException">Thrown if the Windows timezone ID is not recognized.</exception>
        public static string WindowsToIbkrTimezoneString(string windowsTimezoneId)
        {
            var marketTimezone = FromWindowsTimezoneId(windowsTimezoneId);
            return GetIbkrTimezoneString(marketTimezone);
        }

        /// <summary>
        /// Gets the display abbreviation for the specified timezone (e.g., "EST", "CST").
        /// </summary>
        /// <param name="timezone">The market timezone enum value.</param>
        /// <returns>The timezone abbreviation string.</returns>
        public static string GetTimezoneAbbreviation(MarketTimeZone timezone)
        {
            return timezone.ToString();
        }

        /// <summary>
        /// Converts a <see cref="TimeOnly"/> from Eastern Time to the specified local timezone.
        /// </summary>
        /// <param name="easternTime">The time in Eastern Time.</param>
        /// <param name="localTimezone">The target local timezone.</param>
        /// <param name="referenceDate">Optional reference date for DST calculation. Defaults to today.</param>
        /// <returns>The converted time in the local timezone.</returns>
        /// <remarks>
        /// <para><b>DST Note:</b> The conversion accounts for daylight saving time based on the reference date.</para>
        /// <para><b>Example:</b></para>
        /// <code>
        /// // Convert 9:30 AM Eastern to Central Time
        /// var centralTime = TimezoneHelper.ToLocal(new TimeOnly(9, 30), MarketTimeZone.CST);
        /// // Returns 8:30 AM CT
        /// </code>
        /// </remarks>
        public static TimeOnly ToLocal(TimeOnly easternTime, MarketTimeZone localTimezone, DateOnly? referenceDate = null)
        {
            if (localTimezone == MarketTimeZone.EST)
            {
                return easternTime;
            }

            var date = referenceDate ?? DateOnly.FromDateTime(DateTime.Today);
            var easternDateTime = new DateTime(date.Year, date.Month, date.Day,
                easternTime.Hour, easternTime.Minute, easternTime.Second, DateTimeKind.Unspecified);

            var localTimeZone = GetTimeZoneInfo(localTimezone);
            var convertedDateTime = TimeZoneInfo.ConvertTime(easternDateTime, EasternTimeZone, localTimeZone);

            return TimeOnly.FromDateTime(convertedDateTime);
        }

        /// <summary>
        /// Converts a <see cref="TimeOnly"/> from the specified local timezone to Eastern Time.
        /// </summary>
        /// <param name="localTime">The time in the local timezone.</param>
        /// <param name="localTimezone">The source local timezone.</param>
        /// <param name="referenceDate">Optional reference date for DST calculation. Defaults to today.</param>
        /// <returns>The converted time in Eastern Time.</returns>
        /// <remarks>
        /// <para><b>DST Note:</b> The conversion accounts for daylight saving time based on the reference date.</para>
        /// <para><b>Example:</b></para>
        /// <code>
        /// // Convert 8:30 AM Central to Eastern Time
        /// var easternTime = TimezoneHelper.ToEastern(new TimeOnly(8, 30), MarketTimeZone.CST);
        /// // Returns 9:30 AM ET
        /// </code>
        /// </remarks>
        public static TimeOnly ToEastern(TimeOnly localTime, MarketTimeZone localTimezone, DateOnly? referenceDate = null)
        {
            if (localTimezone == MarketTimeZone.EST)
            {
                return localTime;
            }

            var date = referenceDate ?? DateOnly.FromDateTime(DateTime.Today);
            var localDateTime = new DateTime(date.Year, date.Month, date.Day,
                localTime.Hour, localTime.Minute, localTime.Second, DateTimeKind.Unspecified);

            var localTimeZone = GetTimeZoneInfo(localTimezone);
            var easternDateTime = TimeZoneInfo.ConvertTime(localDateTime, localTimeZone, EasternTimeZone);

            return TimeOnly.FromDateTime(easternDateTime);
        }

        /// <summary>
        /// Converts a <see cref="TimeOnly"/> from one timezone to another.
        /// </summary>
        /// <param name="time">The time to convert.</param>
        /// <param name="sourceTimezone">The source timezone.</param>
        /// <param name="targetTimezone">The target timezone.</param>
        /// <param name="referenceDate">Optional reference date for DST calculation. Defaults to today.</param>
        /// <returns>The converted time in the target timezone.</returns>
        public static TimeOnly Convert(TimeOnly time, MarketTimeZone sourceTimezone, MarketTimeZone targetTimezone, DateOnly? referenceDate = null)
        {
            if (sourceTimezone == targetTimezone)
            {
                return time;
            }

            var date = referenceDate ?? DateOnly.FromDateTime(DateTime.Today);
            var sourceDateTime = new DateTime(date.Year, date.Month, date.Day,
                time.Hour, time.Minute, time.Second, DateTimeKind.Unspecified);

            var sourceTimeZone = GetTimeZoneInfo(sourceTimezone);
            var targetTimeZone = GetTimeZoneInfo(targetTimezone);
            var convertedDateTime = TimeZoneInfo.ConvertTime(sourceDateTime, sourceTimeZone, targetTimeZone);

            return TimeOnly.FromDateTime(convertedDateTime);
        }

        /// <summary>
        /// Gets the current offset in hours between Eastern Time and the specified timezone.
        /// </summary>
        /// <param name="timezone">The timezone to compare against Eastern Time.</param>
        /// <param name="referenceDate">Optional reference date for DST calculation. Defaults to today.</param>
        /// <returns>The offset in hours (negative means behind Eastern, positive means ahead).</returns>
        /// <remarks>
        /// <para><b>Note:</b> This offset may change during daylight saving time transitions.</para>
        /// <para><b>Example:</b></para>
        /// <code>
        /// var offset = TimezoneHelper.GetOffsetFromEastern(MarketTimeZone.CST);
        /// // Returns -1 (Central is 1 hour behind Eastern)
        /// </code>
        /// </remarks>
        public static double GetOffsetFromEastern(MarketTimeZone timezone, DateOnly? referenceDate = null)
        {
            if (timezone == MarketTimeZone.EST)
            {
                return 0;
            }

            var date = referenceDate ?? DateOnly.FromDateTime(DateTime.Today);
            var dateTime = new DateTime(date.Year, date.Month, date.Day, 12, 0, 0, DateTimeKind.Unspecified);

            var localTimeZone = GetTimeZoneInfo(timezone);
            var easternOffset = EasternTimeZone.GetUtcOffset(dateTime);
            var localOffset = localTimeZone.GetUtcOffset(dateTime);

            return (localOffset - easternOffset).TotalHours;
        }

        /// <summary>
        /// Creates a detailed timezone information display for console output.
        /// </summary>
        /// <param name="timezone">The timezone to display information for.</param>
        /// <returns>A <see cref="TimezoneInfo"/> record with display details.</returns>
        public static TimezoneDisplayInfo GetTimezoneDisplayInfo(MarketTimeZone timezone)
        {
            var timeZoneInfo = GetTimeZoneInfo(timezone);
            var offset = GetOffsetFromEastern(timezone);
            var isDst = timeZoneInfo.IsDaylightSavingTime(DateTime.Now);

            // Market times in Eastern
            var rthOpenEastern = new TimeOnly(9, 30);
            var rthCloseEastern = new TimeOnly(16, 0);
            var preMarketOpenEastern = new TimeOnly(4, 0);
            var afterHoursCloseEastern = new TimeOnly(20, 0);

            // Convert to local
            var rthOpenLocal = ToLocal(rthOpenEastern, timezone);
            var rthCloseLocal = ToLocal(rthCloseEastern, timezone);
            var preMarketOpenLocal = ToLocal(preMarketOpenEastern, timezone);
            var afterHoursCloseLocal = ToLocal(afterHoursCloseEastern, timezone);

            return new TimezoneDisplayInfo
            {
                Timezone = timezone,
                TimeZoneInfo = timeZoneInfo,
                Abbreviation = GetTimezoneAbbreviation(timezone),
                OffsetFromEastern = offset,
                IsDaylightSavingTime = isDst,
                MarketOpenLocal = rthOpenLocal,
                MarketCloseLocal = rthCloseLocal,
                PreMarketOpenLocal = preMarketOpenLocal,
                AfterHoursCloseLocal = afterHoursCloseLocal
            };
        }

        /// <summary>
        /// Formats a <see cref="TimeOnly"/> with timezone abbreviation.
        /// </summary>
        /// <param name="time">The time to format.</param>
        /// <param name="timezone">The timezone for the abbreviation.</param>
        /// <returns>Formatted string like "9:30 AM EST".</returns>
        public static string FormatWithTimezone(TimeOnly time, MarketTimeZone timezone)
        {
            return $"{time:h:mm tt} {GetTimezoneAbbreviation(timezone)}";
        }

        /// <summary>
        /// Gets the current time in the specified timezone.
        /// </summary>
        /// <param name="timezone">The target timezone.</param>
        /// <returns>The current time in the specified timezone.</returns>
        public static TimeOnly GetCurrentTime(MarketTimeZone timezone)
        {
            var timeZoneInfo = GetTimeZoneInfo(timezone);
            var currentDateTime = TimeZoneInfo.ConvertTime(DateTime.UtcNow, timeZoneInfo);
            return TimeOnly.FromDateTime(currentDateTime);
        }

        /// <summary>
        /// Gets the current DateTime in the specified timezone.
        /// </summary>
        /// <param name="timezone">The target timezone.</param>
        /// <returns>The current DateTime in the specified timezone.</returns>
        public static DateTime GetCurrentDateTime(MarketTimeZone timezone)
        {
            var timeZoneInfo = GetTimeZoneInfo(timezone);
            return TimeZoneInfo.ConvertTime(DateTime.UtcNow, timeZoneInfo);
        }
    }
}
