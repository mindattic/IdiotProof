using System.Globalization;
using IdiotProof.Models;
using IdiotProof.Strategies.Backtesting;

namespace IdiotProof.Blazor.Services;

/// <summary>A horizontal level drawn from a fork until the next fork of the same block: solid when the branch was taken, dashed when not.</summary>
public sealed record GhostLevel(long From, long To, decimal Price, string Color, bool Dashed, string Label);

/// <summary>A fork marker with its hover detail lines.</summary>
public sealed record GhostForkMark(long Time, string Title, IReadOnlyList<string> Lines);

/// <summary>A point of the ghost path (the active stop while in a position).</summary>
public sealed record GhostPoint(long Time, decimal Value);

/// <summary>Everything ipChart.ghost draws: the day's bars, every marker, the forks, branch levels and the stop path.</summary>
public sealed record GhostChartPayload(
    string Symbol, IReadOnlyList<ChartBar> Bars, IReadOnlyList<ChartMarker> Markers,
    IReadOnlyList<GhostForkMark> Forks, IReadOnlyList<GhostLevel> Levels, IReadOnlyList<GhostPoint> StopPath);

/// <summary>Maps a <see cref="GhostTimeline"/> to the ghost overlay's chart payload.</summary>
public static class GhostChart
{
    public static GhostChartPayload Payload(GhostTimeline ghost, IEnumerable<Candle> candles)
    {
        var bars = ChartSeries.Bars(candles);
        var markers = ChartSeries.Markers(ghost.Report, bars).ToList();
        var forks = new List<GhostForkMark>();
        var levels = new List<GhostLevel>();
        if (bars.Count == 0) return new GhostChartPayload(ghost.Symbol, bars, markers, forks, levels, []);

        long last = bars[^1].Time;
        foreach (var f in ghost.Forks)
        {
            long t = Unix(f.Utc);
            var taken = f.Branches.FirstOrDefault(b => b.Taken);
            var lines = new List<string> { $"{Et(f.Utc)} · If block {f.Block + 1} → {taken?.Label ?? "no branch"}" };
            foreach (var b in f.Branches)
                lines.Add($"{(b.Taken ? "▶" : "·")} {b.Label} {(b.Condition ?? "otherwise")}: {(b.Passed ? "pass" : "fail")}"
                          + Level(" stop ", b.Stop) + Level(" target ", b.Target));
            lines.Add(string.Join("  ", f.Indicators.Select(kv => $"{kv.Key} {Fmt(kv.Value)}")));
            forks.Add(new GhostForkMark(t, $"If {f.Block + 1}: {taken?.Label ?? "none"}", lines));
            markers.Add(new ChartMarker(t, "aboveBar", "square", "#7b5cd6", $"If{f.Block + 1}→{taken?.Label ?? "none"}"));

            var next = ghost.Forks.FirstOrDefault(n => n.Block == f.Block && n.Index > f.Index);
            long to = next is null ? last : Unix(next.Utc);
            foreach (var b in f.Branches)
            {
                if (b.Stop is { } s) levels.Add(new GhostLevel(t, to, s, "#e0483d", !b.Taken, $"{b.Label} stop"));
                if (b.Target is { } g) levels.Add(new GhostLevel(t, to, g, "#0f9d78", !b.Taken, $"{b.Label} target"));
            }
        }
        markers.Sort((a, b) => a.Time.CompareTo(b.Time));

        var stopPath = ghost.Frames.Where(f => f.ActiveStop is not null)
            .Select(f => new GhostPoint(Unix(f.Utc), f.ActiveStop!.Value))
            .GroupBy(p => p.Time).Select(g => g.Last()).ToList();

        return new GhostChartPayload(ghost.Symbol, bars, markers, forks, levels, stopPath);
    }

    public static string Et(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), IdiotProof.Scripting.MarketTime.Eastern)
            .ToString("HH:mm", CultureInfo.InvariantCulture) + " ET";

    private static string Level(string name, decimal? v) => v is { } x ? name + x.ToString("0.00##", CultureInfo.InvariantCulture) : "";
    private static string Fmt(double v) => v.ToString(Math.Abs(v) >= 1000 ? "0" : "0.00##", CultureInfo.InvariantCulture);
    private static long Unix(DateTime utc) => new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeSeconds();
}
