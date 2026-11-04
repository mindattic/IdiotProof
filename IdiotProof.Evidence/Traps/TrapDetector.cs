using IdiotProof.Models;

namespace IdiotProof.Evidence.Traps;

/// <summary>A detector's verdict on one bar: the measured chance that a long entered here gets hit.</summary>
public sealed record TrapAssessment(string Detector, string Bucket, double[] Features, OutcomeRate Rate)
{
    /// <summary>True when enough history backs the rate and it is at or below <paramref name="maxProbability"/>.</summary>
    public bool IsClear(double maxProbability, int minSamples) => Rate.Samples >= minSamples && Rate.Probability <= maxProbability;
}

/// <summary>
/// A trap is a setup that looks like an entry but tends to punish a long. Each detector is a
/// causal feature extractor (null when the setup isn't present at that bar) plus a table it
/// learns from history: per quantile bucket of the features, how often a long entered at the
/// bar's close saw price fall <see cref="AdverseAtr"/> ATRs below entry within <see cref="Horizon"/>
/// bars. The probability it reports is that counted rate, never a weight someone chose.
/// </summary>
public sealed class TrapDetector
{
    private readonly Func<CausalSeries, int, double[]?> extract;
    private QuantileBinner[]? binners;
    private ConditionalOutcomeTable<string> table = new();

    public string Name { get; }
    /// <summary>Bars of history a feature needs before it can be read.</summary>
    public int Warmup { get; }
    public int Horizon { get; }
    public double AdverseAtr { get; }
    public int Bins { get; }

    public TrapDetector(string name, int warmup, Func<CausalSeries, int, double[]?> extract,
        int horizon = 10, double adverseAtr = 1.0, int bins = 3)
    {
        if (horizon < 1) throw new ArgumentOutOfRangeException(nameof(horizon));
        if (bins < 2) throw new ArgumentOutOfRangeException(nameof(bins));
        Name = name;
        Warmup = warmup;
        this.extract = extract;
        Horizon = horizon;
        AdverseAtr = adverseAtr;
        Bins = bins;
    }

    public bool IsTrained => binners is not null;
    public int Samples => table.TotalSamples;
    public double BaseRate => table.BaseRate;

    /// <summary>The setup's features at bar i (bars 0..i only), or null when the setup isn't present.</summary>
    public double[]? Features(CausalSeries s, int i) => i < Warmup || i >= s.Candles.Count ? null : extract(s, i);

    /// <summary>
    /// Did a long entered at bar i's close fall AdverseAtr ATRs below entry within the next Horizon bars?
    /// Null when the outcome hasn't resolved inside the series yet.
    /// </summary>
    public bool? Adverse(CausalSeries s, int i)
    {
        if (i + Horizon >= s.Candles.Count) return null;
        double stop = s.Close(i) - AdverseAtr * s.Atr[i];
        return s.LowestLow(i + 1, i + Horizon) <= stop;
    }

    /// <summary>
    /// Learns bucket edges and outcome counts from history. Only bars whose outcome resolves inside
    /// their own series are used, so a label never reaches past the data it was given.
    /// </summary>
    public void Learn(IEnumerable<IReadOnlyList<Candle>> histories)
    {
        var rows = new List<(double[] Features, bool Adverse)>();
        foreach (var candles in histories)
        {
            var s = new CausalSeries(candles);
            for (int i = Warmup; i < candles.Count; i++)
            {
                var outcome = Adverse(s, i);
                if (outcome is null) break;
                var f = Features(s, i);
                if (f is not null) rows.Add((f, outcome.Value));
            }
        }

        int dims = rows.Count == 0 ? 0 : rows[0].Features.Length;
        binners = new QuantileBinner[dims];
        for (int d = 0; d < dims; d++)
        {
            int dim = d;
            binners[d] = new QuantileBinner(rows.Select(r => r.Features[dim]), Bins);
        }
        table = new ConditionalOutcomeTable<string>();
        foreach (var (features, adverse) in rows) table.Add(Bucket(features), adverse);
    }

    public void Learn(IReadOnlyList<Candle> history) => Learn(new[] { history });

    /// <summary>The learned verdict for bar i, or null when the setup isn't present there.</summary>
    public TrapAssessment? Assess(CausalSeries s, int i)
    {
        if (binners is null) throw new InvalidOperationException($"{Name} has not learned from any history.");
        var f = Features(s, i);
        if (f is null) return null;
        var bucket = Bucket(f);
        return new TrapAssessment(Name, bucket, f, table.Rate(bucket));
    }

    public TrapAssessment? Assess(IReadOnlyList<Candle> candles, int i) => Assess(new CausalSeries(candles), i);

    public IEnumerable<(string Bucket, OutcomeRate Rate)> Table() => table.All();

    private string Bucket(double[] features)
    {
        if (binners is null || binners.Length != features.Length) return "?";
        var parts = new string[features.Length];
        for (int d = 0; d < features.Length; d++) parts[d] = binners[d].Bin(features[d]).ToString();
        return string.Join(",", parts);
    }
}
