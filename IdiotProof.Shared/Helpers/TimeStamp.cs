// ============================================================================
// TimeStamp - Centralized Timestamp Formatting
// ============================================================================
// Provides consistent timestamp formatting across the application.
// All timestamps use Eastern Time (ET) with 12-hour format (AM/PM).
// ============================================================================

namespace IdiotProof.Shared.Helpers;

/// <summary>
/// Provides centralized timestamp formatting for logging throughout the application.
/// All timestamps are in Eastern Time with 12-hour format.
/// </summary>
public static class TimeStamp
{
    private static readonly TimeZoneInfo EasternZone = 
        TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

    /// <summary>
    /// Gets the current Eastern Time.
    /// </summary>
    public static DateTime NowEastern => TimeZoneInfo.ConvertTime(DateTime.Now, EasternZone);

    /// <summary>
    /// Gets the current timestamp formatted for logging.
    /// Format: "hh:mm:ss tt" (e.g., "07:03:01 AM")
    /// </summary>
    /// <returns>Formatted timestamp string.</returns>
    public static string Now => NowEastern.ToString("hh:mm:ss tt");

    /// <summary>
    /// Gets the current timestamp wrapped in brackets for log output.
    /// Format: "[h:mm:ss tt]" (e.g., "[10:42:13 PM]")
    /// </summary>
    /// <returns>Formatted timestamp string with brackets.</returns>
    public static string NowBracketed => $"[{Now}]";

    /// <summary>
    /// Formats a DateTime to the standard log timestamp format.
    /// Assumes the input is already in Eastern Time.
    /// </summary>
    /// <param name="dateTime">The datetime to format (assumed Eastern Time).</param>
    /// <returns>Formatted timestamp string.</returns>
    public static string Format(DateTime dateTime) => dateTime.ToString("hh:mm:ss tt");

    /// <summary>
    /// Formats a DateTime to the standard log timestamp format with brackets.
    /// Assumes the input is already in Eastern Time.
    /// </summary>
    /// <param name="dateTime">The datetime to format (assumed Eastern Time).</param>
    /// <returns>Formatted timestamp string with brackets.</returns>
    public static string FormatBracketed(DateTime dateTime) => $"[{Format(dateTime)}]";

    /// <summary>
    /// Converts a UTC DateTime to Eastern Time and formats it.
    /// </summary>
    /// <param name="utcDateTime">The UTC datetime to convert and format.</param>
    /// <returns>Formatted timestamp string in Eastern Time.</returns>
    public static string FromUtc(DateTime utcDateTime)
    {
        var eastern = TimeZoneInfo.ConvertTimeFromUtc(utcDateTime, EasternZone);
        return Format(eastern);
    }
}


