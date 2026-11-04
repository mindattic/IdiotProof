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
        edges = new double[bins - 1];
        if (sorted.Length == 0) return;
        for (int i = 1; i < bins; i++)
            edges[i - 1] = sorted[Math.Min(sorted.Length - 1, (int)((long)i * sorted.Length / bins))];
    }

    public int Bins => edges.Length + 1;

    public int Bin(double value)
    {
        int b = 0;
        while (b < edges.Length && value >= edges[b]) b++;
        return b;
    }
}
