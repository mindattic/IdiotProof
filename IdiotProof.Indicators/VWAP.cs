using IdiotProof.Models;

namespace IdiotProof.Indicators;

/// <summary>
/// Volume-Weighted Average Price. Resets at the start of each US equity trading session.
/// A session is defined as 4:00 AM ET (start of pre-market) through the rest of that ET date,
/// so an after-hours candle at, say, 7:00 PM ET stays grouped with the day's regular session
/// even though it crosses UTC midnight on the East Coast in winter.
/// </summary>
public static class VWAP
{
    private static readonly TimeZoneInfo EasternTimeZone = ResolveEasternTimeZone();

    public static decimal[] Calculate(IReadOnlyList<Candle> candles)
    {
        var result = new decimal[candles.Count];
        decimal cumPV = 0m, cumVol = 0m;
        DateOnly currentSessionDate = default;
        bool sessionInitialized = false;

        for (int i = 0; i < candles.Count; i++)
        {
            var c = candles[i];
            var sessionDate = SessionDate(c.StartUtc);

            if (!sessionInitialized || sessionDate != currentSessionDate)
            {
                cumPV = 0m;
                cumVol = 0m;
                currentSessionDate = sessionDate;
                sessionInitialized = true;
            }

            var tp = (c.High + c.Low + c.Close) / 3m;
            cumPV += tp * c.Volume;
            cumVol += c.Volume;
            result[i] = cumVol == 0m ? 0m : cumPV / cumVol;
        }

        return result;
    }

    /// <summary>
    /// Returns the ET session date for a UTC candle timestamp. The session "day" starts at
    /// 4:00 AM ET (pre-market open) and runs through 3:59:59 AM ET the next ET day.
    /// </summary>
    private static DateOnly SessionDate(DateTime utc)
    {
        var et = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), EasternTimeZone);
        // Anything before 4:00 AM ET belongs to the *previous* trading session (after-hours of prior day).
        if (et.Hour < 4)
            et = et.AddDays(-1);
        return DateOnly.FromDateTime(et);
    }

    private static TimeZoneInfo ResolveEasternTimeZone()
    {
        // Windows uses "Eastern Standard Time"; Linux/macOS use IANA "America/New_York".
        try { return TimeZoneInfo.FindSystemTimeZoneById("America/New_York"); }
        catch (TimeZoneNotFoundException) { }
        try { return TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"); }
        catch (TimeZoneNotFoundException) { }
        return TimeZoneInfo.Utc;
    }
}
