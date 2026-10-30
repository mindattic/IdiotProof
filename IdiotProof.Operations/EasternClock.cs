using System.Globalization;
using IdiotProof.Scripting;

namespace IdiotProof.Operations;

/// <summary>
/// The one UTC → Eastern conversion operations use for anything a person reads. Storage and the
/// clock stay UTC; every displayed time is ET with an explicit "ET" label, never raw UTC and never
/// the host's local zone. Formatting is culture-invariant so output is identical on every box.
/// </summary>
public static class EasternClock
{
    /// <summary>A UTC instant as ET. <c>Unspecified</c> kind is taken as UTC (that is how SQL hands it back).</summary>
    public static DateTime ToEastern(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(AsUtc(utc), MarketTime.Eastern);

    /// <summary>The ET offset at that instant (−4h in EDT, −5h in EST), for ISO strings with an offset.</summary>
    public static DateTimeOffset ToEasternOffset(DateTime utc)
    {
        var asUtc = AsUtc(utc);
        return new DateTimeOffset(asUtc).ToOffset(MarketTime.Eastern.GetUtcOffset(asUtc));
    }

    /// <summary><c>2026-10-30 9:31 AM ET</c>.</summary>
    public static string Format(DateTime utc) =>
        ToEastern(utc).ToString("yyyy-MM-dd h:mm tt", CultureInfo.InvariantCulture) + " ET";

    /// <summary><c>9:31 AM ET</c>.</summary>
    public static string FormatTime(DateTime utc) =>
        ToEastern(utc).ToString("h:mm tt", CultureInfo.InvariantCulture) + " ET";

    /// <summary><c>2026-10-30</c> — the ET calendar date (which can differ from the UTC date after 8 PM ET).</summary>
    public static string FormatDate(DateTime utc) =>
        ToEastern(utc).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    internal static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}
