// ============================================================================
// TimezoneDisplayInfo - Timezone Display Information
// ============================================================================
//
// Contains display information about a timezone configuration for console output.
//
// USAGE:
//   var info = TimezoneHelper.GetTimezoneDisplayInfo(MarketTimeZone.CST);
//   Console.WriteLine(info.ToString());
//
// ============================================================================

using System;
using MarketTimeZone = IdiotProof.Shared.Enums.MarketTimeZone;

namespace IdiotProof.Backend.Models
{
    /// <summary>
    /// Contains display information about a timezone configuration.
    /// </summary>
    public record TimezoneDisplayInfo
    {
        /// <summary>
        /// The configured market timezone.
        /// </summary>
        public required MarketTimeZone Timezone { get; init; }

        /// <summary>
        /// The system TimeZoneInfo object.
        /// </summary>
        public required TimeZoneInfo TimeZoneInfo { get; init; }

        /// <summary>
        /// The timezone abbreviation (EST, CST, MST, PST).
        /// </summary>
        public required string Abbreviation { get; init; }

        /// <summary>
        /// Hours offset from Eastern Time (negative = behind).
        /// </summary>
        public required double OffsetFromEastern { get; init; }

        /// <summary>
        /// Whether daylight saving time is currently in effect.
        /// </summary>
        public required bool IsDaylightSavingTime { get; init; }

        /// <summary>
        /// Market open time (9:30 AM ET) in local timezone.
        /// </summary>
        public required TimeOnly MarketOpenLocal { get; init; }

        /// <summary>
        /// Market close time (4:00 PM ET) in local timezone.
        /// </summary>
        public required TimeOnly MarketCloseLocal { get; init; }

        /// <summary>
        /// Pre-market open time (4:00 AM ET) in local timezone.
        /// </summary>
        public required TimeOnly PreMarketOpenLocal { get; init; }

        /// <summary>
        /// After-hours close time (8:00 PM ET) in local timezone.
        /// </summary>
        public required TimeOnly AfterHoursCloseLocal { get; init; }

        /// <summary>
        /// Returns a formatted display string for console output.
        /// </summary>
        public override string ToString()
        {
            var offsetStr = OffsetFromEastern == 0 ? "same as" :
                           OffsetFromEastern < 0 ? $"{Math.Abs(OffsetFromEastern)}h behind" :
                           $"{OffsetFromEastern}h ahead of";
            var dstStr = IsDaylightSavingTime ? " (DST active)" : "";

            return $"{Abbreviation} ({offsetStr} Eastern){dstStr}";
        }
    }
}
