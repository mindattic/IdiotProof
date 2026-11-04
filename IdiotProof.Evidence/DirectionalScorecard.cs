namespace IdiotProof.Evidence;

/// <summary>
/// Keeps score of directional calls against what really happened. Abstentions are counted but
/// never scored, so a model can't look accurate by only calling the easy bars without it showing
/// in <see cref="Coverage"/>.
/// </summary>
public sealed class DirectionalScorecard
{
    private readonly List<double> actedReturns = new();
    public int Calls { get; private set; }
    public int Hits { get; private set; }
    public int Abstentions { get; private set; }

    /// <param name="predicted">Up or Down to act, Flat to abstain.</param>
    /// <param name="forwardReturn">The realized forward return of the bar.</param>
    /// <param name="roundTripCost">This call's estimated round-trip cost as a fraction (0 when unknown).</param>
    public void Record(Direction predicted, double forwardReturn, double roundTripCost = 0)
    {
        if (predicted == Direction.Flat) { Abstentions++; return; }
        Calls++;
        double signed = predicted == Direction.Up ? forwardReturn : -forwardReturn;
        if (signed > 0) Hits++;
        actedReturns.Add(signed);
        costs.Add(roundTripCost);
    }

    public void Merge(DirectionalScorecard other)
    {
        Calls += other.Calls; Hits += other.Hits; Abstentions += other.Abstentions;
        actedReturns.AddRange(other.actedReturns);
        costs.AddRange(other.costs);
    }

    private readonly List<double> costs = new();

    /// <summary>Mean recorded round-trip cost per call.</summary>
    public double MeanCost => costs.Count == 0 ? 0 : EvidenceStats.Mean(costs);

    /// <summary>Mean signed return per call after each call's own recorded cost.</summary>
    public double MeanEdgeAfterCost() => MeanEdge - MeanCost;

    public double HitRate => Calls == 0 ? double.NaN : (double)Hits / Calls;
    public double Coverage => Calls + Abstentions == 0 ? 0 : (double)Calls / (Calls + Abstentions);

    /// <summary>One-sided p-value against a fair coin: the chance of this many hits or more with no edge.</summary>
    public double PValue => Calls == 0 ? 1 : EvidenceStats.BinomialUpperTail(Hits, Calls, 0.5);

    public (double Low, double High) HitRateInterval => EvidenceStats.WilsonInterval(Hits, Calls);

    /// <summary>Mean signed return per call, before costs.</summary>
    public double MeanEdge => actedReturns.Count == 0 ? 0 : EvidenceStats.Mean(actedReturns);

    /// <summary>Mean signed return per call after a round-trip cost (e.g. spread + slippage as a fraction).</summary>
    public double MeanEdgeAfterCost(double roundTripCost) => MeanEdge - roundTripCost;

    /// <summary>True only when the hit rate beats the coin at the given significance level.</summary>
    public bool IsSignificant(double alpha = 0.05) => Calls > 0 && PValue < alpha;

    public override string ToString()
    {
        var (lo, hi) = HitRateInterval;
        return $"{Hits}/{Calls} hits ({HitRate:P2}, 95% CI {lo:P1}–{hi:P1}), p={PValue:G3}, coverage {Coverage:P1}, edge {MeanEdge:P3}/call";
    }
}
