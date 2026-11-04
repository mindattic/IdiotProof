namespace IdiotProof.Evidence;

/// <summary>One held-out bucket's up-rate tested against the held-out base rate.</summary>
public readonly record struct BucketTest(int Bin, int Successes, int Samples, double Rate, double Lift, double PValue, double AdjustedPValue);

/// <summary>
/// Decides whether any bucket of a feature really differs from the base rate on held-out data:
/// an exact two-sided binomial test per bucket with a Bonferroni correction across buckets, so
/// scanning five buckets for a lucky one doesn't count as a discovery.
/// </summary>
public static class BucketSignificance
{
    public static IReadOnlyList<BucketTest> Test(IReadOnlyList<(int Bin, int Successes, int Samples)> buckets, double baseRate)
    {
        int tested = buckets.Count(b => b.Samples > 0);
        var results = new List<BucketTest>(buckets.Count);
        foreach (var (bin, k, n) in buckets)
        {
            if (n == 0 || double.IsNaN(baseRate) || baseRate <= 0 || baseRate >= 1)
            {
                results.Add(new BucketTest(bin, k, n, double.NaN, double.NaN, 1, 1));
                continue;
            }
            double rate = (double)k / n;
            double p = TwoSided(k, n, baseRate);
            results.Add(new BucketTest(bin, k, n, rate, rate / baseRate, p, Math.Min(1, p * Math.Max(1, tested))));
        }
        return results;
    }

    /// <summary>True when at least one bucket differs from the base rate at <paramref name="alpha"/> after correction.</summary>
    public static bool AnySignificant(IReadOnlyList<BucketTest> tests, double alpha = 0.05) => tests.Any(t => t.AdjustedPValue < alpha);

    public static double TwoSided(int k, int n, double p)
    {
        double upper = EvidenceStats.BinomialUpperTail(k, n, p);
        double lower = 1 - EvidenceStats.BinomialUpperTail(k + 1, n, p);
        return Math.Min(1, 2 * Math.Min(upper, lower));
    }
}
