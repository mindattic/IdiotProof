namespace IdiotProof.Evidence;

/// <summary>
/// Significance and calibration math used to decide whether a measured edge is real.
/// </summary>
public static class EvidenceStats
{
    /// <summary>P(X &gt;= k) for X ~ Binomial(n, p): the chance of at least k hits by luck alone.</summary>
    public static double BinomialUpperTail(int k, int n, double p = 0.5)
    {
        if (n < 0 || k < 0) throw new ArgumentOutOfRangeException(nameof(n));
        if (k == 0) return 1.0;
        if (k > n) return 0.0;
        if (p <= 0) return 0.0;
        if (p >= 1) return 1.0;

        double logP = Math.Log(p), logQ = Math.Log(1 - p);
        double maxLog = double.NegativeInfinity;
        var terms = new double[n - k + 1];
        for (int i = k; i <= n; i++)
        {
            double t = LogChoose(n, i) + i * logP + (n - i) * logQ;
            terms[i - k] = t;
            if (t > maxLog) maxLog = t;
        }
        double sum = 0;
        foreach (var t in terms) sum += Math.Exp(t - maxLog);
        return Math.Min(1.0, Math.Exp(maxLog) * sum);
    }

    /// <summary>Wilson score interval for a proportion; well-behaved for small n and extreme rates.</summary>
    public static (double Low, double High) WilsonInterval(int successes, int n, double z = 1.959963984540054)
    {
        if (n <= 0) return (0, 1);
        double p = (double)successes / n;
        double z2 = z * z;
        double denom = 1 + z2 / n;
        double center = (p + z2 / (2.0 * n)) / denom;
        double half = z * Math.Sqrt(p * (1 - p) / n + z2 / (4.0 * n * n)) / denom;
        return (Math.Max(0, center - half), Math.Min(1, center + half));
    }

    /// <summary>Mean squared error of probability forecasts against 0/1 outcomes (lower is better; 0.25 = coin flip).</summary>
    public static double BrierScore(IReadOnlyList<double> probabilities, IReadOnlyList<bool> outcomes)
    {
        if (probabilities.Count != outcomes.Count) throw new ArgumentException("Length mismatch.");
        if (probabilities.Count == 0) return double.NaN;
        double sum = 0;
        for (int i = 0; i < probabilities.Count; i++)
        {
            double d = probabilities[i] - (outcomes[i] ? 1 : 0);
            sum += d * d;
        }
        return sum / probabilities.Count;
    }

    /// <summary>Mean negative log-likelihood of probability forecasts (ln 2 ≈ 0.693 = coin flip).</summary>
    public static double LogLoss(IReadOnlyList<double> probabilities, IReadOnlyList<bool> outcomes)
    {
        if (probabilities.Count != outcomes.Count) throw new ArgumentException("Length mismatch.");
        if (probabilities.Count == 0) return double.NaN;
        const double eps = 1e-12;
        double sum = 0;
        for (int i = 0; i < probabilities.Count; i++)
        {
            double p = Math.Clamp(probabilities[i], eps, 1 - eps);
            sum -= outcomes[i] ? Math.Log(p) : Math.Log(1 - p);
        }
        return sum / probabilities.Count;
    }

    public static double Mean(IReadOnlyList<double> xs)
    {
        if (xs.Count == 0) return double.NaN;
        double s = 0;
        foreach (var x in xs) s += x;
        return s / xs.Count;
    }

    /// <summary>Sample standard deviation (n-1 denominator).</summary>
    public static double StdDev(IReadOnlyList<double> xs)
    {
        if (xs.Count < 2) return 0;
        double m = Mean(xs), s = 0;
        foreach (var x in xs) s += (x - m) * (x - m);
        return Math.Sqrt(s / (xs.Count - 1));
    }

    public static double Correlation(IReadOnlyList<double> a, IReadOnlyList<double> b)
    {
        int n = Math.Min(a.Count, b.Count);
        if (n < 2) return 0;
        double ma = 0, mb = 0;
        for (int i = 0; i < n; i++) { ma += a[i]; mb += b[i]; }
        ma /= n; mb /= n;
        double cov = 0, va = 0, vb = 0;
        for (int i = 0; i < n; i++)
        {
            double da = a[i] - ma, db = b[i] - mb;
            cov += da * db; va += da * da; vb += db * db;
        }
        return va <= 0 || vb <= 0 ? 0 : cov / Math.Sqrt(va * vb);
    }

    public static double LogChoose(int n, int k) => LogGamma(n + 1) - LogGamma(k + 1) - LogGamma(n - k + 1);

    /// <summary>Lanczos approximation of ln Γ(x), accurate to ~1e-15 for x &gt; 0.</summary>
    public static double LogGamma(double x)
    {
        if (x < 0.5) return Math.Log(Math.PI / Math.Sin(Math.PI * x)) - LogGamma(1 - x);
        x -= 1;
        double a = Coefficients[0];
        double t = x + 7.5;
        for (int i = 1; i < 9; i++) a += Coefficients[i] / (x + i);
        return 0.5 * Math.Log(2 * Math.PI) + (x + 0.5) * Math.Log(t) - t + Math.Log(a);
    }

    private static readonly double[] Coefficients =
    {
        0.99999999999980993, 676.5203681218851, -1259.1392167224028, 771.32342877765313,
        -176.61502916214059, 12.507343278686905, -0.13857109526572012,
        9.9843695780195716e-6, 1.5056327351493116e-7,
    };
}
