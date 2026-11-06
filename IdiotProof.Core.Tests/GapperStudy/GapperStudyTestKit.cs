using IdiotProof.Core.Research.Gappers;
using IdiotProof.Evidence.Gappers;
using IdiotProof.Models;

namespace IdiotProof.Core.Tests.GapperStudy;

/// <summary>The frozen params as committed, plus synthetic bar builders. 2026-07-17 is EDT: 04:00 ET = 08:00 UTC, 09:30 ET = 13:30 UTC.</summary>
internal static class GapperStudyTestKit
{
    public static readonly DateOnly Day = new(2026, 7, 17);

    private static readonly Lazy<GapperStudyParams> parameters = new(() => GapperStudyParams.Load(Paths));

    public static GapperStudyPaths Paths => GapperStudyPaths.Resolve();

    public static GapperStudyParams Params => parameters.Value;

    public static DateTime Et(DateOnly day, int hour, int minute) => MinuteBarMath.ToUtc(day, new TimeSpan(hour, minute, 0));

    public static DetailedCandle Bar(DateOnly day, int hour, int minute, double close, double volume, double? high = null, double? low = null, double? open = null, string note = "Alpaca:sip")
    {
        var start = Et(day, hour, minute);
        var o = open ?? close;
        return new DetailedCandle
        {
            Symbol = "T", StartUtc = start, EndUtc = start.AddMinutes(1),
            Open = (decimal)o, High = (decimal)(high ?? Math.Max(o, close)), Low = (decimal)(low ?? Math.Min(o, close)), Close = (decimal)close,
            Volume = (decimal)volume, TradeCount = volume > 0 ? 3 : 0, Vwap = (decimal)close, Note = note,
        };
    }

    /// <summary>A full 04:00-20:00 session where close(minute index) and volume(minute index) are given by functions.</summary>
    public static List<Candle> Session(DateOnly day, Func<int, double> close, Func<int, double> volume, string note = "Alpaca:sip")
    {
        var bars = new List<Candle>();
        double prev = close(0);
        for (int i = 0; i < 16 * 60; i++)
        {
            var c = close(i);
            var v = volume(i);
            if (v <= 0) { prev = c; continue; }
            bars.Add(Bar(day, 4 + i / 60, i % 60, c, v, Math.Max(prev, c), Math.Min(prev, c), prev, note));
            prev = c;
        }
        return bars;
    }

    public static StudyDailyBar Daily(DateOnly d, double close, double volume, double? high = null) =>
        new(d, (decimal)close, (decimal)(high ?? close), (decimal)close, (decimal)close, (decimal)volume);
}
