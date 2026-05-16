namespace IdiotProof.Blazor.Services;

/// <summary>
/// All UI clocks/timestamps render in US Eastern Time. Storage stays UTC; this
/// helper is the one-way conversion at the render boundary. The Windows zone id
/// "Eastern Standard Time" is the same row as IANA "America/New_York" and
/// auto-handles DST, so the display label is "ET" rather than the literal "EST"
/// — that catches both EST (winter) and EDT (summer) without lying about which
/// the clock is currently showing.
/// </summary>
public static class EasternTime
{
    private static readonly TimeZoneInfo Et =
        TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

    public static DateTime FromUtc(DateTime utc)
    {
        var asUtc = utc.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(utc, DateTimeKind.Utc)
            : utc;
        return TimeZoneInfo.ConvertTimeFromUtc(asUtc, Et);
    }

    /// <summary>Convenience overload: format a UTC DateTime as ET in one call.</summary>
    public static string Format(DateTime utc, string format) =>
        FromUtc(utc).ToString(format);
}
