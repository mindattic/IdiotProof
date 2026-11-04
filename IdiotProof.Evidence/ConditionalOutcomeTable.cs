namespace IdiotProof.Evidence;

/// <summary>The measured rate of an outcome in one bucket, with how much evidence backs it.</summary>
public readonly record struct OutcomeRate(int Successes, int Samples, double Probability, double Low, double High, double Lift);

/// <summary>
/// Counts how often an outcome followed each feature bucket. The only way a detector in this
/// toolkit may produce a probability: (successes + 1) / (samples + 2) with a Wilson interval and
/// the lift over the overall base rate, so every number traces back to counted history.
/// </summary>
public sealed class ConditionalOutcomeTable<TKey> where TKey : notnull
{
    private readonly Dictionary<TKey, (int Successes, int Samples)> cells = new();
    public int TotalSuccesses { get; private set; }
    public int TotalSamples { get; private set; }

    public void Add(TKey key, bool success)
    {
        cells.TryGetValue(key, out var c);
        cells[key] = (c.Successes + (success ? 1 : 0), c.Samples + 1);
        TotalSamples++;
        if (success) TotalSuccesses++;
    }

    public double BaseRate => TotalSamples == 0 ? double.NaN : (double)TotalSuccesses / TotalSamples;

    public OutcomeRate Rate(TKey key)
    {
        cells.TryGetValue(key, out var c);
        double p = (c.Successes + 1.0) / (c.Samples + 2.0);
        var (lo, hi) = EvidenceStats.WilsonInterval(c.Successes, c.Samples);
        double baseRate = BaseRate;
        double lift = double.IsNaN(baseRate) || baseRate <= 0 ? double.NaN : p / baseRate;
        return new OutcomeRate(c.Successes, c.Samples, p, lo, hi, lift);
    }

    public IEnumerable<(TKey Key, OutcomeRate Rate)> All() => cells.Keys.Select(k => (k, Rate(k)));
}

/// <summary>Equal-frequency bucket edges learned from training data (quantile bins), so buckets are never hand-picked.</summary>
public sealed class QuantileBinner
{
    private readonly double[] edges;

    public QuantileBinner(IEnumerable<double> trainingValues, int bins)
    {
        if (bins < 2) throw new ArgumentOutOfRangeException(nameof(bins));
        var sorted = trainingValues.Where(v => !double.IsNaN(v)).OrderBy(v => v).ToArray();
        var cuts = new SortedSet<double>();
        for (int i = 1; i < bins && sorted.Length > 1; i++)
        {
            int idx = Math.Clamp((int)((long)i * sorted.Length / bins), 1, sorted.Length - 1);
            double below = sorted[idx - 1], at = sorted[idx];
            if (below < at) { cuts.Add((below + at) / 2); continue; }
            // The quantile falls inside a run of ties: cut just above the run so tied values share a bucket.
            int j = idx + 1;
            while (j < sorted.Length && sorted[j] <= at) j++;
            if (j < sorted.Length) cuts.Add((at + sorted[j]) / 2);
        }
        edges = cuts.ToArray();
    }

    public int Bins => edges.Length + 1;

    public int Bin(double value)
    {
        int b = 0;
        while (b < edges.Length && value >= edges[b]) b++;
        return b;
    }
}
