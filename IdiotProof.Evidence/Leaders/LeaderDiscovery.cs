using IdiotProof.Models;

namespace IdiotProof.Evidence.Leaders;

/// <summary>A symbol whose moves have measurably preceded the target's, and how strongly.</summary>
public readonly record struct LeaderLink(string Symbol, int Hops, double Influence);

/// <summary>
/// Finds a symbol's leaders: the candidates whose minute returns at bar t correlate with the
/// target's at bar t+1 (lag-1 <see cref="InfluenceGraph"/> edges), directly or through up to
/// <c>maxHops</c> intermediaries, with the hop-decayed influence of each. Callers pass history
/// that ends before the day being traded, so a leader is never chosen with hindsight.
/// </summary>
public static class LeaderDiscovery
{
    /// <summary>A gap longer than this between grid timestamps starts a new session; no return spans it.</summary>
    public static readonly TimeSpan SessionGap = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Per-bar returns on the union of every symbol's bar timestamps. A symbol with no bar at a
    /// grid time gets a 0 return there (no trade, no move), and no return spans a session gap.
    /// </summary>
    public static IReadOnlyDictionary<string, double[]> AlignReturns(IReadOnlyDictionary<string, IReadOnlyList<Candle>> bars)
    {
        var grid = bars.Values.SelectMany(b => b.Select(c => c.StartUtc)).Distinct().OrderBy(t => t).ToArray();
        var result = new Dictionary<string, double[]>(StringComparer.Ordinal);
        foreach (var (symbol, list) in bars)
        {
            var closeAt = new Dictionary<DateTime, decimal>(list.Count);
            foreach (var c in list) closeAt[c.StartUtc] = c.Close;
            var r = new double[grid.Length];
            double? last = null;
            for (int i = 0; i < grid.Length; i++)
            {
                if (i > 0 && grid[i] - grid[i - 1] > SessionGap) last = null;
                if (!closeAt.TryGetValue(grid[i], out var close) || close <= 0) continue;
                double c = (double)close;
                if (last is { } prev && prev > 0) r[i] = c / prev - 1;
                last = c;
            }
            result[symbol] = r;
        }
        return result;
    }

    public static IReadOnlyList<LeaderLink> Discover(string target, IReadOnlyDictionary<string, IReadOnlyList<Candle>> history,
        int maxHops = 2, double minAbsCorrelation = 0.1, double minInfluence = 0.02, double beta = 0.5, int maxLeaders = 5,
        int minBars = 60)
    {
        if (!history.TryGetValue(target, out var own) || own.Count < minBars || history.Count < 2) return [];
        var graph = InfluenceGraph.FromReturns(AlignReturns(history), lag: 1, minAbsCorrelation);
        var links = new List<LeaderLink>();
        foreach (var s in graph.Symbols)
        {
            if (s == target) continue;
            int hops = graph.HopDistance(s, target);
            if (hops < 1 || hops > maxHops) continue;
            double influence = graph.Influence(s, beta, maxHops)[target];
            if (Math.Abs(influence) < minInfluence) continue;
            links.Add(new LeaderLink(s, hops, influence));
        }
        return links.OrderByDescending(l => Math.Abs(l.Influence)).ThenBy(l => l.Symbol, StringComparer.Ordinal).Take(maxLeaders).ToList();
    }
}

/// <summary>What the leaders are doing right now, read only from bars that have closed.</summary>
public static class LeaderSignal
{
    /// <summary>
    /// Return over the last <paramref name="lookbackBars"/> bars that closed at or before
    /// <paramref name="asOfUtc"/>; a bar still forming (or in the future) is never read. Null when
    /// there aren't enough closed bars.
    /// </summary>
    public static double? RecentMove(IReadOnlyList<Candle> bars, DateTime asOfUtc, int lookbackBars)
    {
        if (lookbackBars < 1) throw new ArgumentOutOfRangeException(nameof(lookbackBars));
        int last = -1;
        for (int i = bars.Count - 1; i >= 0; i--)
            if (bars[i].EndUtc <= asOfUtc) { last = i; break; }
        if (last < lookbackBars) return null;
        double then = (double)bars[last - lookbackBars].Close;
        return then > 0 ? (double)bars[last].Close / then - 1 : null;
    }

    /// <summary>
    /// Influence-weighted mean move of the leaders within <paramref name="maxHops"/> whose
    /// |influence| is at least <paramref name="minInfluence"/>: Σ influence·move / Σ |influence|, so
    /// an inverse leader falling counts as a push up. Null when no leader qualifies.
    /// </summary>
    public static double? WeightedMove(IEnumerable<(int Hops, double Influence, double? Move)> leaders, int maxHops, double minInfluence)
    {
        double num = 0, den = 0;
        foreach (var (hops, influence, move) in leaders)
        {
            if (hops < 1 || hops > maxHops || Math.Abs(influence) < minInfluence || move is not { } m) continue;
            num += influence * m;
            den += Math.Abs(influence);
        }
        return den > 0 ? num / den : null;
    }
}
