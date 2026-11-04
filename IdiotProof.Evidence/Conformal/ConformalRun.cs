namespace IdiotProof.Evidence.Conformal;

/// <summary>One forecast: made at <see cref="CallTime"/>, its outcome known only at <see cref="ResolveTime"/>.</summary>
public readonly record struct ConformalPoint(DateOnly Day, DateTime CallTime, DateTime ResolveTime, double Forecast, double Outcome);

/// <summary>What was issued for one point and how it turned out.</summary>
public readonly record struct ConformalResult(ConformalPoint Point, ConformalInterval Split, ConformalInterval Adaptive)
{
    public Direction Call => Adaptive.Call();
}

/// <summary>
/// Coverage and abstain-rule scoring over a set of results. Coverage counts every point, an unbounded
/// interval (too little calibration, or ACI's α_t below zero) as covering, exactly as the guarantees
/// define it; the unbounded counts say how often that happened, and widths average finite intervals only.
/// </summary>
public sealed record ConformalSummary(
    int Points, double SplitCoverage, int SplitUnbounded, double SplitMeanWidth,
    double AdaptiveCoverage, int AdaptiveUnbounded, double AdaptiveMeanWidth,
    DirectionalScorecard Abstain, IReadOnlyList<DayTally> DayTallies)
{
    public static ConformalSummary Of(IReadOnlyList<ConformalResult> results)
    {
        var split = results.Where(r => r.Split.IsFinite).ToList();
        var adaptive = results.Where(r => r.Adaptive.IsFinite).ToList();
        var all = results.ToList();
        var card = new DirectionalScorecard();
        var tallies = new Dictionary<DateOnly, (int Calls, int Hits)>();
        foreach (var r in results)
        {
            var call = r.Call;
            card.Record(call, r.Point.Outcome);
            if (call == Direction.Flat) continue;
            bool hit = (call == Direction.Up ? r.Point.Outcome : -r.Point.Outcome) > 0;
            var t = tallies.GetValueOrDefault(r.Point.Day);
            tallies[r.Point.Day] = (t.Calls + 1, t.Hits + (hit ? 1 : 0));
        }
        return new ConformalSummary(results.Count,
            Rate(all, r => r.Split.Contains(r.Point.Outcome)), results.Count - split.Count, split.Count == 0 ? double.NaN : split.Average(r => r.Split.Width),
            Rate(all, r => r.Adaptive.Contains(r.Point.Outcome)), results.Count - adaptive.Count, adaptive.Count == 0 ? double.NaN : adaptive.Average(r => r.Adaptive.Width),
            card, tallies.OrderBy(kv => kv.Key).Select(kv => new DayTally(kv.Key, kv.Value.Calls, kv.Value.Hits)).ToList());
    }

    private static double Rate(List<ConformalResult> rs, Func<ConformalResult, bool> f) => rs.Count == 0 ? double.NaN : (double)rs.Count(f) / rs.Count;
}

/// <summary>
/// Runs split and adaptive conformal online, strictly in time: each point's intervals are issued at
/// its call time from the outcomes resolved by then, and its own outcome joins the calibration only at
/// its resolve time. Nothing a point is scored with can depend on its own or any later outcome.
/// </summary>
public static class ConformalRun
{
    public static IReadOnlyList<ConformalResult> Run(IEnumerable<ConformalPoint> points, double alpha, double gamma)
    {
        var split = new SplitConformal(alpha);
        var aci = new AdaptiveConformal(alpha, gamma);
        var ordered = points.OrderBy(p => p.CallTime).ToList();
        var pending = new PriorityQueue<ConformalResult, DateTime>();
        var results = new List<ConformalResult>(ordered.Count);

        foreach (var p in ordered)
        {
            while (pending.TryPeek(out var due, out var at) && at <= p.CallTime)
            {
                pending.Dequeue();
                split.Add(due.Point.Forecast, due.Point.Outcome);
                aci.Update(due.Adaptive, due.Point.Forecast, due.Point.Outcome);
            }
            var r = new ConformalResult(p, split.Interval(p.Forecast), aci.Interval(p.Forecast));
            results.Add(r);
            pending.Enqueue(r, p.ResolveTime);
        }
        return results;
    }
}
