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

    /// <summary>The return-variance floor for a price series: its estimated tick at its median price.</summary>
    public static double ReturnVarianceFloor(IReadOnlyList<decimal> prices)
    {
        if (prices.Count < 2) return 0;
        var tick = EstimateTick(prices);
        var sorted = prices.Where(p => p > 0).OrderBy(p => p).ToList();
        if (tick <= 0 || sorted.Count == 0) return 0;
        return ReturnVarianceFloor((double)tick, (double)sorted[sorted.Count / 2]);
    }
}
