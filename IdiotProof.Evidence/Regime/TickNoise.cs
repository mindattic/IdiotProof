namespace IdiotProof.Evidence.Regimes;

/// <summary>
/// How much variance price discreteness alone puts into bar returns. A price quoted on a tick grid
/// carries a rounding error of up to half a tick; a return differences two such prices. On a $0.80
/// stock one cent is 1.25%, so a model that ignores the grid fits a near-zero-variance "calm" state to
/// the bars that didn't tick and reads every 1-tick move as a regime break.
/// </summary>
public static class TickNoise
{
    /// <summary>
    /// Floor on the variance of a log return: two independent uniform rounding errors of variance tick²/12
    /// each, relative to the price, give (tick/price)²/6. No model of observed returns may claim less.
    /// </summary>
    public static double ReturnVarianceFloor(double tick, double price) =>
        tick > 0 && price > 0 ? tick * tick / (price * price) / 6 : 0;

    /// <summary>
    /// The price grid the series trades on, read from the data: a low percentile of the nonzero
    /// bar-to-bar price changes (robust to a stray sub-tick print). 0 when prices never change.
    /// </summary>
    public static decimal EstimateTick(IReadOnlyList<decimal> prices)
    {
        var steps = new List<decimal>();
        for (int i = 1; i < prices.Count; i++)
        {
            var d = Math.Abs(prices[i] - prices[i - 1]);
            if (d > 0) steps.Add(d);
        }
        if (steps.Count == 0) return 0;
        steps.Sort();
        return steps[(int)(0.1 * (steps.Count - 1))];
    }

    /// <summary>Bar multiples tried by <see cref="ChooseAggregation"/>, smallest first.</summary>
    public static readonly int[] AggregationCandidates = { 1, 2, 3, 5, 10, 15, 30 };

    /// <summary>
    /// The smallest bar multiple at which a continuous-return model is sound for this series: at most
    /// <paramref name="maxZeroShare"/> of the k-bar returns are exactly zero, and tick rounding noise is at
    /// most <paramref name="maxFloorShare"/> of their variance. Below that, a Gaussian regime model fits the
    /// grid (no-tick bars vs one-tick bars) instead of the volatility. Returns stay inside each session.
    /// Falls back to the largest candidate when none qualifies.
    /// </summary>
    public static int ChooseAggregation(IReadOnlyList<IReadOnlyList<decimal>> sessions, double maxZeroShare = 0.25, double maxFloorShare = 0.1)
    {
        var all = sessions.SelectMany(s => s).ToList();
        double floor = ReturnVarianceFloor(all);
        foreach (var k in AggregationCandidates)
        {
            var returns = new List<double>();
            int zeros = 0;
            foreach (var s in sessions)
                for (int i = k; i < s.Count; i += k)
                {
                    if (s[i - k] <= 0 || s[i] <= 0) continue;
                    double r = Math.Log((double)(s[i] / s[i - k]));
                    if (r == 0) zeros++;
                    returns.Add(r);
                }
            if (returns.Count < 30) break;
            double variance = EvidenceStats.StdDev(returns);
            variance *= variance;
            if ((double)zeros / returns.Count <= maxZeroShare && (variance <= 0 ? 1 : floor / variance) <= maxFloorShare) return k;
        }
        return AggregationCandidates[^1];
    }

    /// <summary>Reg NMS minimum quoting increment: one cent at $1.00 and up, $0.0001 below.</summary>
    public static decimal QuoteIncrement(decimal price) => price >= 1m ? 0.01m : 0.0001m;

    /// <summary>
    /// The return-variance floor for a price series: its tick at its median price. The tick is never taken
    /// below the quoting increment, because sub-penny midpoint and ATS prints in the closes would otherwise
    /// shrink the estimate and let a "calm" state collapse onto the bars that never left the quoted cent.
    /// </summary>
    public static double ReturnVarianceFloor(IReadOnlyList<decimal> prices)
    {
        if (prices.Count < 2) return 0;
        var sorted = prices.Where(p => p > 0).OrderBy(p => p).ToList();
        if (sorted.Count == 0) return 0;
        var median = sorted[sorted.Count / 2];
        var estimated = EstimateTick(prices);
        if (estimated <= 0) return 0;
        var tick = Math.Max(estimated, QuoteIncrement(median));
        return ReturnVarianceFloor((double)tick, (double)median);
    }
}
