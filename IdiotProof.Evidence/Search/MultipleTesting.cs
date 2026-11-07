namespace IdiotProof.Evidence.Search;

/// <summary>Moments of a return series that the deflated Sharpe ratio needs.</summary>
public readonly record struct ReturnMoments(int Count, double Mean, double StdDev, double Skewness, double Kurtosis)
{
    /// <summary>Per-period Sharpe ratio (mean / std), not annualized.</summary>
    public double Sharpe => StdDev > 0 ? Mean / StdDev : 0;

    public static ReturnMoments Of(IReadOnlyList<double> returns)
    {
        int n = returns.Count;
        if (n == 0) return new ReturnMoments(0, 0, 0, 0, 3);
        double mean = EvidenceStats.Mean(returns);
        double m2 = 0, m3 = 0, m4 = 0;
        foreach (var r in returns)
        {
            double d = r - mean;
            m2 += d * d; m3 += d * d * d; m4 += d * d * d * d;
        }
        m2 /= n; m3 /= n; m4 /= n;
        double sd = Math.Sqrt(m2);
        double skew = sd > 0 ? m3 / (sd * sd * sd) : 0;
        double kurt = m2 > 0 ? m4 / (m2 * m2) : 3;
        return new ReturnMoments(n, mean, sd, skew, kurt);
    }
}

/// <summary>
/// Corrections for having looked at many strategy configurations: the best of many random configs
/// always looks good, so its evidence has to be discounted by how many were tried.
/// </summary>
public static class MultipleTesting
{
    private const double EulerGamma = 0.5772156649015329;

    /// <summary>
    /// Benjamini-Hochberg adjusted p-values (q-values): rejecting every hypothesis with q ≤ α keeps the
    /// expected share of false discoveries at or below α. Returned in the input order.
    /// </summary>
    public static double[] BenjaminiHochberg(IReadOnlyList<double> pValues)
    {
        int m = pValues.Count;
        var q = new double[m];
        if (m == 0) return q;
        var order = Enumerable.Range(0, m).OrderBy(i => pValues[i]).ToArray();
        double running = 1;
        for (int rank = m; rank >= 1; rank--)
        {
            int i = order[rank - 1];
            running = Math.Min(running, pValues[i] * m / rank);
            q[i] = Math.Min(1, running);
        }
        return q;
    }

    /// <summary>
    /// Holm step-down adjusted p-values: sorted ascending, the i-th smallest is multiplied by (m - i + 1)
    /// and the running maximum is kept, so rejecting every adjusted p ≤ α controls the family-wise error
    /// rate at α. Returned in the input order.
    /// </summary>
    public static double[] Holm(IReadOnlyList<double> pValues)
    {
        int m = pValues.Count;
        var adjusted = new double[m];
        if (m == 0) return adjusted;
        var order = Enumerable.Range(0, m).OrderBy(i => pValues[i]).ToArray();
        double running = 0;
        for (int rank = 1; rank <= m; rank++)
        {
            int i = order[rank - 1];
            running = Math.Max(running, pValues[i] * (m - rank + 1));
            adjusted[i] = Math.Min(1, running);
        }
        return adjusted;
    }

    /// <summary>
    /// The Sharpe ratio the best of <paramref name="trials"/> skill-less configs would show by luck alone
    /// (Bailey &amp; López de Prado's expected maximum Sharpe), given the variance of Sharpe across trials.
    /// </summary>
    public static double ExpectedMaxSharpe(int trials, double varianceOfTrialSharpes)
    {
        if (trials <= 1) return 0;
        double sd = Math.Sqrt(Math.Max(0, varianceOfTrialSharpes));
        return sd * ((1 - EulerGamma) * Distributions.NormalQuantile(1 - 1.0 / trials)
                     + EulerGamma * Distributions.NormalQuantile(1 - 1.0 / (trials * Math.E)));
    }

    /// <summary>
    /// Probabilistic Sharpe ratio: the probability that the true Sharpe exceeds <paramref name="benchmarkSharpe"/>,
    /// accounting for sample length, skewness and fat tails of the observed returns.
    /// </summary>
    public static double ProbabilisticSharpe(ReturnMoments m, double benchmarkSharpe)
    {
        if (m.Count < 2 || m.StdDev <= 0) return double.NaN;
        double sr = m.Sharpe;
        double denom = 1 - m.Skewness * sr + (m.Kurtosis - 1) / 4 * sr * sr;
        if (denom <= 0) denom = 1e-12;
        return Distributions.NormalCdf((sr - benchmarkSharpe) * Math.Sqrt(m.Count - 1) / Math.Sqrt(denom));
    }

    /// <summary>
    /// Deflated Sharpe ratio: the probabilistic Sharpe measured against the best Sharpe expected from
    /// <paramref name="trials"/> random configs, so more configs tried means a higher bar to clear.
    /// </summary>
    public static double DeflatedSharpe(ReturnMoments m, int trials, double varianceOfTrialSharpes) =>
        ProbabilisticSharpe(m, ExpectedMaxSharpe(trials, varianceOfTrialSharpes));

    /// <summary>One-sided t-test p-value that the mean return is above zero.</summary>
    public static double MeanAboveZeroPValue(IReadOnlyList<double> returns)
    {
        var m = ReturnMoments.Of(returns);
        if (m.Count < 2) return 1;
        double sampleSd = m.StdDev * Math.Sqrt((double)m.Count / (m.Count - 1));
        if (sampleSd <= 0) return m.Mean > 0 ? 0 : 1;
        return Distributions.StudentTUpperTail(m.Mean / (sampleSd / Math.Sqrt(m.Count)), m.Count - 1);
    }
}
