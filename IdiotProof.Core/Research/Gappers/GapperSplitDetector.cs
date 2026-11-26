using IdiotProof.Core.Data;

namespace IdiotProof.Core.Research.Gappers;

/// <summary>
/// Finds reverse splits that <c>GapperStudySplits</c> has no row for, from one symbol's raw daily bars. A day is a reverse split when
/// its open is at least <see cref="MinRatio"/> times the prior close and within <see cref="RatioTolerance"/> of a whole number, the
/// level holds (the median close of the next five sessions is at least twice, or at most half, the prior close) and the volume steps
/// (the median volume of the next five sessions is at most half, or at least twice, the prior five). The ratio recorded is the
/// whole number nearest the open over the prior close, so it is an estimate: the bars do not carry the true terms.
/// </summary>
public static class GapperSplitDetector
{
    public const decimal MinRatio = 3m;
    public const decimal RatioTolerance = 0.25m;
    public const int Window = 5;
    public const int RecordedWithinDays = 5;

    public sealed record Detected(DateOnly ExDate, decimal OldRate, decimal NewRate, decimal OpenOverPriorClose);

    public static List<Detected> Detect(IReadOnlyList<GapperStudyDailyBar> bars, IReadOnlyCollection<DateOnly> recorded)
    {
        var b = bars.OrderBy(x => x.DateEt).ToList();
        var found = new List<Detected>();
        for (var i = Math.Max(1, Window); i + Window < b.Count; i++)
        {
            var pc = b[i - 1].Close;
            if (pc <= 0m || b[i].Open < MinRatio * pc) continue;
            var ratio = b[i].Open / pc;
            var whole = Math.Round(ratio, MidpointRounding.AwayFromZero);
            if (Math.Abs(ratio - whole) / whole > RatioTolerance) continue;
            var day = b[i].DateEt;
            if (recorded.Any(e => Math.Abs(e.DayNumber - day.DayNumber) <= RecordedWithinDays)) continue;
            var cNext = Median(b.Skip(i + 1).Take(Window).Select(x => x.Close));
            if (cNext < 2m * pc && cNext > 0.5m * pc) continue;
            var vPrev = Median(b.Skip(i - Window).Take(Window).Select(x => x.Volume));
            var vNext = Median(b.Skip(i + 1).Take(Window).Select(x => x.Volume));
            if (vPrev <= 0m || (vNext > 0.5m * vPrev && vNext < 2m * vPrev)) continue;
            found.Add(new Detected(day, whole, 1m, ratio));
        }
        return found;
    }

    private static decimal Median(IEnumerable<decimal> values)
    {
        var v = values.OrderBy(x => x).ToList();
        return v.Count % 2 == 1 ? v[v.Count / 2] : (v[v.Count / 2 - 1] + v[v.Count / 2]) / 2m;
    }
}
