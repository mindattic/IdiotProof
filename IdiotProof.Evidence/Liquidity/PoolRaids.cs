namespace IdiotProof.Evidence.Liquidity;

/// <summary>What happened after price came to a stop pool.</summary>
public enum PoolOutcome
{
    /// <summary>Came within reach but never traded through.</summary>
    Reject,
    /// <summary>Traded through, then was back on the near side by the end of the horizon (stops taken, move reversed).</summary>
    RaidAndReverse,
    /// <summary>Traded through and was still beyond the pool at the end of the horizon.</summary>
    BreakThrough,
}

public readonly record struct PoolKey(PoolKind Kind, PoolSide Side, int Tier);

/// <summary>One fresh approach to a pool at bar <see cref="Index"/>, resolved at <see cref="ResolvedAt"/>.</summary>
public sealed record PoolTouch(int Index, int ResolvedAt, StopPool Pool, PoolKey Key, PoolOutcome Outcome, double PierceDepth);

public sealed record PoolRaidOptions
{
    /// <summary>Bars after the approach that decide the outcome.</summary>
    public int Horizon { get; init; } = 10;
    /// <summary>How close (in ATRs, measured on the bar before) counts as reaching a pool.</summary>
    public double ProximityAtr { get; init; } = 0.25;
}

/// <summary>
/// Finds every fresh approach to a pool and grades it. A pool is taken from the map of the bar
/// before the approach (so the level existed before price reached it), an approach is fresh only
/// when the previous bar was still out of reach, and the outcome reads bars Index..Index+Horizon,
/// so <see cref="PoolTouch.ResolvedAt"/> is the first bar at which the grade is known.
/// </summary>
public static class PoolRaidDetector
{
    public static IReadOnlyList<PoolTouch> Touches(StopPoolSeries s, PoolRaidOptions? options = null)
    {
        var o = options ?? new PoolRaidOptions();
        var touches = new List<PoolTouch>();
        int n = s.Candles.Count;
        for (int i = 1; i + o.Horizon < n; i++)
        {
            double refClose = s.Close(i - 1);
            double prox = o.ProximityAtr * s.Atr(i - 1);
            int tier = StopPoolSeries.Tier(refClose);
            foreach (var pool in s.PoolsAt(i - 1))
            {
                double p = pool.Price;
                if (p > refClose)
                {
                    if (s.High(i - 1) >= p - prox || s.High(i) < p - prox) continue;
                    double maxHigh = double.MinValue;
                    for (int j = i; j <= i + o.Horizon; j++) maxHigh = Math.Max(maxHigh, s.High(j));
                    var outcome = maxHigh <= p ? PoolOutcome.Reject
                        : s.Close(i + o.Horizon) > p ? PoolOutcome.BreakThrough : PoolOutcome.RaidAndReverse;
                    touches.Add(new PoolTouch(i, i + o.Horizon, pool, new PoolKey(pool.Kind, PoolSide.Above, tier), outcome,
                        maxHigh > p ? (maxHigh - p) / p : 0));
                }
                else if (p < refClose)
                {
                    if (s.Low(i - 1) <= p + prox || s.Low(i) > p + prox) continue;
                    double minLow = double.MaxValue;
                    for (int j = i; j <= i + o.Horizon; j++) minLow = Math.Min(minLow, s.Low(j));
                    var outcome = minLow >= p ? PoolOutcome.Reject
                        : s.Close(i + o.Horizon) < p ? PoolOutcome.BreakThrough : PoolOutcome.RaidAndReverse;
                    touches.Add(new PoolTouch(i, i + o.Horizon, pool, new PoolKey(pool.Kind, PoolSide.Below, tier), outcome,
                        minLow < p ? (p - minLow) / p : 0));
                }
            }
        }
        return touches;
    }
}

/// <summary>
/// Counted pool outcomes per (kind, side, price tier). Two rates: how often a move into the pool
/// failed there (reject or raid-and-reverse) out of every approach, and how often a pierce
/// reversed out of every pierce, plus the median pierce depth of raids that reversed.
/// </summary>
public sealed class PoolRaidTable
{
    private readonly ConditionalOutcomeTable<PoolKey> failed = new();
    private readonly ConditionalOutcomeTable<PoolKey> reversed = new();
    private readonly Dictionary<PoolKey, List<double>> raidDepths = new();

    public int Approaches => failed.TotalSamples;

    public void Add(PoolTouch t)
    {
        failed.Add(t.Key, t.Outcome != PoolOutcome.BreakThrough);
        if (t.Outcome == PoolOutcome.Reject) return;
        reversed.Add(t.Key, t.Outcome == PoolOutcome.RaidAndReverse);
        if (t.Outcome == PoolOutcome.RaidAndReverse)
        {
            if (!raidDepths.TryGetValue(t.Key, out var list)) raidDepths[t.Key] = list = new List<double>();
            list.Add(t.PierceDepth);
        }
    }

    /// <summary>Learns from touches whose outcome was known before <paramref name="resolvedBefore"/> allows (null = all).</summary>
    public static PoolRaidTable Learn(IEnumerable<PoolTouch> touches, Func<PoolTouch, bool>? resolvedBefore = null)
    {
        var table = new PoolRaidTable();
        foreach (var t in touches)
            if (resolvedBefore is null || resolvedBefore(t)) table.Add(t);
        return table;
    }

    /// <summary>P(move fails at the pool | price reached it).</summary>
    public OutcomeRate FailRate(PoolKey key) => failed.Rate(key);

    /// <summary>P(pierce reverses | price traded through).</summary>
    public OutcomeRate ReverseRate(PoolKey key) => reversed.Rate(key);

    /// <summary>Median fraction by which reversed raids overshot the pool; null with no raids.</summary>
    public double? MedianRaidDepth(PoolKey key)
    {
        if (!raidDepths.TryGetValue(key, out var list) || list.Count == 0) return null;
        var sorted = list.OrderBy(x => x).ToArray();
        int m = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[m] : (sorted[m - 1] + sorted[m]) / 2;
    }

    public IEnumerable<PoolKey> Keys => failed.All().Select(x => x.Key);
}

/// <summary>A pool reading for the newest bar: whether the setup is present and the counted rate behind it.</summary>
public sealed record PoolReading(bool Present, StopPool? Pool, PoolKey? Key, OutcomeRate? Rate);

/// <summary>Reads the newest bar against a learned <see cref="PoolRaidTable"/>.</summary>
public static class PoolAssessor
{
    /// <summary>
    /// A long entry at bar i "into a pool": the nearest pool above the close lies within the proximity
    /// band. The rate is how often moves into pools of that kind and tier failed.
    /// </summary>
    public static PoolReading IntoPool(StopPoolSeries s, int i, PoolRaidTable table, PoolRaidOptions? options = null)
    {
        var o = options ?? new PoolRaidOptions();
        double close = s.Close(i), prox = o.ProximityAtr * s.Atr(i);
        StopPool? nearest = null;
        foreach (var p in s.PoolsAt(i))
            if (p.Price > close && p.Price - close <= prox && (nearest is null || p.Price < nearest.Value.Price)) nearest = p;
        if (nearest is null) return new PoolReading(false, null, null, null);
        var key = new PoolKey(nearest.Value.Kind, PoolSide.Above, StopPoolSeries.Tier(close));
        return new PoolReading(true, nearest, key, table.FailRate(key));
    }

    /// <summary>
    /// A raid below just happened: within the last <c>Horizon</c> bars some bar traded under a pool
    /// that existed before it, and bar i closed back above that pool. The rate is how often pierces of
    /// that kind and tier reversed.
    /// </summary>
    public static PoolReading RecentRaid(StopPoolSeries s, int i, PoolRaidTable table, PoolRaidOptions? options = null)
    {
        var o = options ?? new PoolRaidOptions();
        double close = s.Close(i);
        for (int j = i; j >= Math.Max(1, i - o.Horizon + 1); j--)
        {
            double refClose = s.Close(j - 1);
            foreach (var p in s.PoolsAt(j - 1))
            {
                if (p.Price >= refClose || s.Low(j) >= p.Price || close <= p.Price) continue;
                var key = new PoolKey(p.Kind, PoolSide.Below, StopPoolSeries.Tier(refClose));
                return new PoolReading(true, p, key, table.ReverseRate(key));
            }
        }
        return new PoolReading(false, null, null, null);
    }
}
