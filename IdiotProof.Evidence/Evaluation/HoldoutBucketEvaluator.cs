using IdiotProof.Evidence.Search;

namespace IdiotProof.Evidence.Evaluation;

/// <summary>One scored bar: which day it belongs to, the feature's value (NaN = no reading), and whether price went up.</summary>
public readonly record struct BucketObservation(DateOnly Day, double Value, bool Up);

/// <summary>
/// A feature's observations split into training and held-out parts. <paramref name="Categories"/> set means the
/// value already is a bucket index 0..Categories-1; otherwise quantile edges are learned from training values.
/// </summary>
public sealed record FeatureSamples(string Feature, IReadOnlyList<BucketObservation> Train,
    IReadOnlyList<BucketObservation> Holdout, int? Categories = null);

/// <summary>One bucket re-measured on held-out days.</summary>
public sealed record BucketVerdict(
    string Feature,
    int Bin,
    OutcomeRate Train,
    int HoldoutUp,
    int HoldoutSamples,
    double HoldoutRate,
    double HoldoutLow,
    double HoldoutHigh,
    double HoldoutBaseRate,
    double Lift,
    int Days,
    double BinomialP,
    double DayClusteredP,
    double P,
    double Q,
    bool Significant)
{
    public string Label => HoldoutSamples == 0 ? "no data" : Significant ? "significant" : "not significant";
}

public sealed record FeatureReport(string Feature, int Bins, double TrainBaseRate, double HoldoutBaseRate,
    int TrainSamples, int HoldoutSamples, IReadOnlyList<BucketVerdict> Buckets);

public sealed record HoldoutReport(double Alpha, int Tests, IReadOnlyList<FeatureReport> Features)
{
    public IEnumerable<BucketVerdict> AllBuckets => Features.SelectMany(f => f.Buckets);
    public bool AnySignificant => AllBuckets.Any(b => b.Significant);
}

/// <summary>
/// The one way a bucketed feature earns the word "significant". Bucket edges and training rates come from
/// training data only; each bucket's held-out up-rate is tested against the held-out base rate twice — an
/// exact two-sided binomial over bars, and a day-clustered test (block bootstrap over days plus a t-test on
/// daily rates) because bars from one day ride one market move — and the larger p is kept. Those p-values are
/// then Benjamini-Hochberg adjusted across every bucket of every feature in the report, and a bucket is
/// significant only when its q is below alpha, so scanning many buckets for a lucky one doesn't count.
/// </summary>
public static class HoldoutBucketEvaluator
{
    public static HoldoutReport Evaluate(IReadOnlyList<FeatureSamples> features, int bins = 5, double alpha = 0.05,
        int minDays = 5, int resamples = 2000, int seed = 1337)
    {
        if (bins < 2) throw new ArgumentOutOfRangeException(nameof(bins));
        var drafts = new List<(string Feature, int Bins, double TrainBase, double HoldBase, int TrainN, int HoldN, List<Draft> Buckets)>();
        foreach (var f in features)
        {
            Func<double, int> binOf;
            int count;
            if (f.Categories is int categories)
            {
                count = categories;
                binOf = v => v >= 0 && v < categories ? (int)v : -1;
            }
            else
            {
                var binner = new QuantileBinner(f.Train.Where(o => !double.IsNaN(o.Value)).Select(o => o.Value), bins);
                count = binner.Bins;
                binOf = binner.Bin;
            }

            var train = new ConditionalOutcomeTable<int>();
            foreach (var o in f.Train)
                if (!double.IsNaN(o.Value) && binOf(o.Value) is var b and >= 0) train.Add(b, o.Up);

            var holdout = f.Holdout.Where(o => !double.IsNaN(o.Value) && binOf(o.Value) >= 0).ToList();
            double holdBase = holdout.Count == 0 ? double.NaN : holdout.Count(o => o.Up) / (double)holdout.Count;

            var buckets = new List<Draft>(count);
            for (int b = 0; b < count; b++)
            {
                var inBin = holdout.Where(o => binOf(o.Value) == b).ToList();
                buckets.Add(Score(b, train.Rate(b), inBin, holdBase, minDays, resamples, seed));
            }
            drafts.Add((f.Feature, count, train.BaseRate, holdBase, train.TotalSamples, holdout.Count, buckets));
        }

        var tested = drafts.SelectMany(d => d.Buckets).Where(b => b.Samples > 0).ToList();
        var q = MultipleTesting.BenjaminiHochberg(tested.Select(b => b.P).ToList());
        var qOf = new Dictionary<Draft, double>(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < tested.Count; i++) qOf[tested[i]] = q[i];

        var reports = drafts.Select(d => new FeatureReport(d.Feature, d.Bins, d.TrainBase, d.HoldBase, d.TrainN, d.HoldN,
            d.Buckets.Select(b =>
            {
                double qv = qOf.TryGetValue(b, out var v) ? v : 1;
                return new BucketVerdict(d.Feature, b.Bin, b.Train, b.Up, b.Samples, b.Rate, b.Low, b.High, d.HoldBase,
                    b.Lift, b.Days, b.BinomialP, b.ClusteredP, b.P, qv, b.Samples > 0 && qv < alpha);
            }).ToList())).ToList();
        return new HoldoutReport(alpha, tested.Count, reports);
    }

    private sealed record Draft(int Bin, OutcomeRate Train, int Up, int Samples, double Rate, double Low, double High,
        double Lift, int Days, double BinomialP, double ClusteredP)
    {
        public double P => Math.Max(BinomialP, ClusteredP);
    }

    private static Draft Score(int bin, OutcomeRate train, List<BucketObservation> obs, double baseRate, int minDays, int resamples, int seed)
    {
        int n = obs.Count, up = obs.Count(o => o.Up);
        int days = obs.Select(o => o.Day).Distinct().Count();
        if (n == 0 || double.IsNaN(baseRate) || baseRate <= 0 || baseRate >= 1)
            return new Draft(bin, train, up, n, n == 0 ? double.NaN : (double)up / n, 0, 1, double.NaN, days, 1, 1);

        double rate = (double)up / n;
        var (lo, hi) = EvidenceStats.WilsonInterval(up, n);
        double binomial = TwoSidedBinomialP(up, n, baseRate);

        // Day-clustered, in the direction observed: a "hit" is the move this bucket leans toward.
        bool leansUp = rate >= baseRate;
        var tallies = obs.GroupBy(o => o.Day)
            .Select(g => new DayTally(g.Key, g.Count(), g.Count(o => o.Up == leansUp))).ToList();
        var clustered = ClusteredSignificance.Evaluate(tallies, leansUp ? baseRate : 1 - baseRate, resamples, seed, minDays);
        double clusteredP = Math.Min(1, 2 * Math.Max(clustered.BootstrapPValue, clustered.TPValue));

        return new Draft(bin, train, up, n, rate, lo, hi, rate / baseRate, days, binomial, clusteredP);
    }

    /// <summary>Exact two-sided binomial p of k successes in n trials against rate p0 (doubling the smaller tail).</summary>
    public static double TwoSidedBinomialP(int k, int n, double p0)
    {
        if (n == 0 || double.IsNaN(p0)) return 1;
        double upper = EvidenceStats.BinomialUpperTail(k, n, p0);
        double lower = 1 - EvidenceStats.BinomialUpperTail(k + 1, n, p0);
        return Math.Min(1, 2 * Math.Min(upper, lower));
    }
}
