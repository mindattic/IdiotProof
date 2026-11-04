using System.Globalization;
using IdiotProof.Models;
using IdiotProof.Strategies.Backtesting;

namespace IdiotProof.Blazor.Services;

/// <summary>A horizontal level drawn from a fork until the next fork of the same block: solid when the branch was taken, dashed when not.</summary>
public sealed record GhostLevel(long From, long To, decimal Price, string Color, bool Dashed, string Label);

/// <summary>A segment marker with its hover detail lines; hovering any bar from Time to Until shows them.</summary>
public sealed record GhostForkMark(long Time, string Title, IReadOnlyList<string> Lines, long Until);

/// <summary>A point of the ghost path (the active stop while in a position).</summary>
public sealed record GhostPoint(long Time, decimal Value);

/// <summary>Everything ipChart.ghost draws: the day's bars, every marker, the forks, branch levels and the stop path.</summary>
public sealed record GhostChartPayload(
    string Symbol, IReadOnlyList<ChartBar> Bars, IReadOnlyList<ChartMarker> Markers,
    IReadOnlyList<GhostForkMark> Forks, IReadOnlyList<GhostLevel> Levels, IReadOnlyList<GhostPoint> StopPath);

/// <summary>Maps a <see cref="GhostTimeline"/> to the ghost overlay's chart payload.</summary>
public static class GhostChart
{
    /// <summary>The default shortest branch run drawn as its own segment; shorter runs merge into a chop segment.</summary>
    public const int DefaultMinRunLength = 3;

    /// <param name="minRunLength">1 draws every fork; larger values collapse noisy flipping into chop segments.</param>
    public static GhostChartPayload Payload(GhostTimeline ghost, IEnumerable<Candle> candles, int minRunLength = DefaultMinRunLength)
    {
        var bars = ChartSeries.Bars(candles);
        var markers = ChartSeries.Markers(ghost.Report, bars).ToList();
        var forks = new List<GhostForkMark>();
        var levels = new List<GhostLevel>();
        if (bars.Count == 0 || ghost.Frames.Count == 0) return new GhostChartPayload(ghost.Symbol, bars, markers, forks, levels, []);

        var segments = ghost.Segments(minRunLength);
        foreach (var seg in segments)
        {
            var startUtc = ghost.Frames[seg.StartIndex].Utc;
            var endUtc = ghost.Frames[seg.EndIndex].Utc;
            long t = Unix(startUtc), until = Unix(endUtc);
            var next = segments.FirstOrDefault(n => n.Block == seg.Block && n.StartIndex > seg.StartIndex);
            long to = next is null ? bars[^1].Time : Unix(ghost.Frames[next.StartIndex].Utc);
            var block = ghost.Tree.Blocks[seg.Block];
            string Label(int k) => k >= 0 && k < block.Branches.Count ? block.Branches[k].Label : "no branch";
            string counts = string.Join(" · ", seg.BarsByBranch.OrderByDescending(kv => kv.Value).Select(kv => $"{Label(kv.Key)} {kv.Value} bar(s)"));

            if (seg.IsChop)
            {
                var lines = new List<string>
                {
                    $"{Et(startUtc)}–{Et(endUtc)} · If block {seg.Block + 1} chop: {seg.Flips} flip(s) over {seg.Bars} bars",
                    counts,
                };
                forks.Add(new GhostForkMark(t, $"If {seg.Block + 1}: chop", lines, until));
                markers.Add(new ChartMarker(t, "aboveBar", "square", "#9a8f6a", $"If{seg.Block + 1} chop ×{seg.Flips}"));
                continue;
            }

            var fork = ghost.Forks.FirstOrDefault(f => f.Block == seg.Block && f.Index == seg.StartIndex);
            var head = new List<string> { $"{Et(startUtc)}–{Et(endUtc)} · If block {seg.Block + 1} → {Label(seg.Branch)} for {seg.Bars} bars" };
            if (fork is not null)
            {
                foreach (var b in fork.Branches)
                {
                    head.Add($"{(b.Taken ? "▶" : "·")} {b.Label} {(b.Condition ?? "otherwise")}: {(b.Passed ? "pass" : "fail")}"
                             + Level(" stop ", b.Stop) + Level(" target ", b.Target));
                    if (b.Stop is { } s) levels.Add(new GhostLevel(t, to, s, "#e0483d", !b.Taken, $"{b.Label} stop"));
                    if (b.Target is { } g) levels.Add(new GhostLevel(t, to, g, "#0f9d78", !b.Taken, $"{b.Label} target"));
                }
                head.Add(string.Join("  ", fork.Indicators.Select(kv => $"{kv.Key} {Fmt(kv.Value)}")));
            }
            forks.Add(new GhostForkMark(t, $"If {seg.Block + 1}: {Label(seg.Branch)}", head, until));
            markers.Add(new ChartMarker(t, "aboveBar", "square", "#7b5cd6", $"If{seg.Block + 1}→{Label(seg.Branch)}"));
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
