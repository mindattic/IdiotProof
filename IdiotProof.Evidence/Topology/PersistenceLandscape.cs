namespace IdiotProof.Evidence.Topology;

/// <summary>
/// Persistence landscapes (Bubenik): λ_k(t) is the k-th largest tent max(0, min(t − b, d − t)) over
/// the finite bars of one dimension. Their L1/L2 norms turn a diagram into numbers that can be
/// averaged, bucketed and tested, unlike the diagram itself.
/// </summary>
public static class PersistenceLandscape
{
    /// <summary>L1 and L2 norms summed over the first <paramref name="levels"/> landscape functions, by trapezoid on a uniform grid.</summary>
    public static (double L1, double L2) Norms(IEnumerable<PersistenceBar> bars, int dimension, int levels = 3, int samples = 256)
    {
        var finite = bars.Where(b => b.Dimension == dimension && !b.IsInfinite && b.Death > b.Birth).ToList();
        if (finite.Count == 0) return (0, 0);
        double lo = finite.Min(b => b.Birth), hi = finite.Max(b => b.Death);
        if (hi <= lo) return (0, 0);
        double step = (hi - lo) / (samples - 1);

        double l1 = 0, l2sq = 0;
        var tents = new double[finite.Count];
        double[] prev = new double[levels];
        for (int s = 0; s < samples; s++)
        {
            double t = lo + s * step;
            for (int i = 0; i < finite.Count; i++)
                tents[i] = Math.Max(0, Math.Min(t - finite[i].Birth, finite[i].Death - t));
            Array.Sort(tents);
            var cur = new double[levels];
            for (int k = 0; k < levels && k < tents.Length; k++) cur[k] = tents[tents.Length - 1 - k];
            if (s > 0)
                for (int k = 0; k < levels; k++)
                {
                    l1 += step * (prev[k] + cur[k]) / 2;
                    l2sq += step * (prev[k] * prev[k] + cur[k] * cur[k]) / 2;
                }
            prev = cur;
        }
        return (l1, Math.Sqrt(l2sq));
    }

    /// <summary>Σ (death − birth) over the finite bars of one dimension.</summary>
    public static double TotalPersistence(IEnumerable<PersistenceBar> bars, int dimension) =>
        bars.Where(b => b.Dimension == dimension && !b.IsInfinite).Sum(b => b.Death - b.Birth);

    public static double MaxPersistence(IEnumerable<PersistenceBar> bars, int dimension)
    {
        var finite = bars.Where(b => b.Dimension == dimension && !b.IsInfinite).ToList();
        return finite.Count == 0 ? 0 : finite.Max(b => b.Death - b.Birth);
    }
}
