using IdiotProof.Evidence.Search;

namespace IdiotProof.Evidence.Dependence;

public enum CopulaFamily { Independence, Gaussian, StudentT, Clayton, Gumbel }

/// <summary>A fitted copula with its log-likelihood, AIC and the tail dependence it implies.</summary>
public sealed record CopulaFit(CopulaFamily Family, double Parameter, double Nu, double LogLikelihood, int Parameters,
    double LowerTail, double UpperTail)
{
    public double Aic => 2 * Parameters - 2 * LogLikelihood;
}

/// <summary>Empirical tail dependence at quantile q with a permutation p-value against tail independence.</summary>
public sealed record TailEstimate(double Q, double Lambda, int JointExceedances, int MarginalExceedances, double PValue);

/// <summary>
/// Which names crash together: rank-based (scale-free) dependence measures for a pair of return
/// series. Ordinary correlation averages the whole joint distribution; tail dependence asks only
/// "when one has a bottom-q day, how often does the other?", which is what contagion is about.
/// Copula parameters come from Kendall's τ inversion (Clayton θ=2τ/(1-τ), Gumbel θ=1/(1-τ),
/// elliptical ρ=sin(πτ/2)); Student-t ν is chosen by pseudo-likelihood over a grid.
/// </summary>
public static class Copulas
{
    private static readonly double[] NuGrid = { 2, 3, 4, 5, 6, 8, 10, 15, 20, 30 };

    /// <summary>Rank/(n+1) pseudo-observations: uniform on (0,1), invariant to any monotone transform of the data.</summary>
    public static double[] PseudoObservations(IReadOnlyList<double> xs)
    {
        int n = xs.Count;
        var order = Enumerable.Range(0, n).OrderBy(i => xs[i]).ToArray();
        var u = new double[n];
        int k = 0;
        while (k < n)
        {
            int j = k;
            while (j + 1 < n && xs[order[j + 1]] == xs[order[k]]) j++;
            double rank = (k + j) / 2.0 + 1;
            for (int m = k; m <= j; m++) u[order[m]] = rank / (n + 1);
            k = j + 1;
        }
        return u;
    }

    /// <summary>Kendall's τ-b (tie-adjusted), O(n²).</summary>
    public static double KendallTau(IReadOnlyList<double> x, IReadOnlyList<double> y)
    {
        int n = Math.Min(x.Count, y.Count);
        long concordant = 0, discordant = 0, tiesX = 0, tiesY = 0;
        for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
            {
                int sx = Math.Sign(x[i] - x[j]), sy = Math.Sign(y[i] - y[j]);
                if (sx == 0 && sy == 0) continue;
                if (sx == 0) { tiesX++; continue; }
                if (sy == 0) { tiesY++; continue; }
                if (sx == sy) concordant++; else discordant++;
            }
        double denom = Math.Sqrt((double)(concordant + discordant + tiesX) * (concordant + discordant + tiesY));
        return denom <= 0 ? 0 : (concordant - discordant) / denom;
    }

    /// <summary>λ_L(q) = P(V ≤ q | U ≤ q) estimated from pseudo-observations; seeded permutation test against independence.</summary>
    public static TailEstimate LowerTail(IReadOnlyList<double> x, IReadOnlyList<double> y, double q = 0.1, int permutations = 999, int seed = 1337) =>
        Tail(PseudoObservations(x), PseudoObservations(y), q, lower: true, permutations, seed);

    /// <summary>λ_U(q) = P(V &gt; 1-q | U &gt; 1-q).</summary>
    public static TailEstimate UpperTail(IReadOnlyList<double> x, IReadOnlyList<double> y, double q = 0.1, int permutations = 999, int seed = 1337) =>
        Tail(PseudoObservations(x), PseudoObservations(y), q, lower: false, permutations, seed);

    private static TailEstimate Tail(double[] u, double[] v, double q, bool lower, int permutations, int seed)
    {
        int n = Math.Min(u.Length, v.Length);
        bool InTail(double p) => lower ? p <= q : p > 1 - q;
        int Joint(double[] vv)
        {
            int c = 0;
            for (int i = 0; i < n; i++) if (InTail(u[i]) && InTail(vv[i])) c++;
            return c;
        }
        int marginal = 0;
        for (int i = 0; i < n; i++) if (InTail(u[i])) marginal++;
        int joint = Joint(v);
        double lambda = marginal == 0 ? double.NaN : (double)joint / marginal;

        var rng = new Random(seed);
        var shuffled = v.Take(n).ToArray();
        int atLeast = 0;
        for (int p = 0; p < permutations; p++)
        {
            for (int i = n - 1; i > 0; i--) { int j = rng.Next(i + 1); (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]); }
            if (Joint(shuffled) >= joint) atLeast++;
        }
        double pValue = permutations == 0 ? double.NaN : (1.0 + atLeast) / (1.0 + permutations);
        return new TailEstimate(q, lambda, joint, marginal, pValue);
    }

    /// <summary>Fits every family to the pair and returns them best (lowest AIC) first.</summary>
    public static IReadOnlyList<CopulaFit> Fit(IReadOnlyList<double> x, IReadOnlyList<double> y)
    {
        var u = PseudoObservations(x);
        var v = PseudoObservations(y);
        double tau = KendallTau(x, y);
        var fits = new List<CopulaFit> { new(CopulaFamily.Independence, 0, 0, 0, 0, 0, 0) };

        double rho = Math.Clamp(Math.Sin(Math.PI * tau / 2), -0.99, 0.99);
        fits.Add(new CopulaFit(CopulaFamily.Gaussian, rho, 0, Sum(u, v, (a, b) => GaussianLogDensity(a, b, rho)), 1, 0, 0));

        CopulaFit? bestT = null;
        foreach (var nu in NuGrid)
        {
            var xs = u.Select(p => StudentTQuantile(p, nu)).ToArray();
            var ys = v.Select(p => StudentTQuantile(p, nu)).ToArray();
            double ll = 0;
            for (int i = 0; i < xs.Length; i++) ll += StudentTLogDensity(xs[i], ys[i], rho, nu);
            double lambda = StudentTTailDependence(rho, nu);
            if (bestT is null || ll > bestT.LogLikelihood) bestT = new CopulaFit(CopulaFamily.StudentT, rho, nu, ll, 2, lambda, lambda);
        }
        fits.Add(bestT!);

        if (tau > 0.01)
        {
            double clayton = 2 * tau / (1 - tau);
            fits.Add(new CopulaFit(CopulaFamily.Clayton, clayton, 0, Sum(u, v, (a, b) => ClaytonLogDensity(a, b, clayton)), 1, Math.Pow(2, -1 / clayton), 0));
            double gumbel = 1 / (1 - tau);
            fits.Add(new CopulaFit(CopulaFamily.Gumbel, gumbel, 0, Sum(u, v, (a, b) => GumbelLogDensity(a, b, gumbel)), 1, 0, 2 - Math.Pow(2, 1 / gumbel)));
        }
        return fits.Where(f => !double.IsNaN(f.LogLikelihood) && !double.IsInfinity(f.LogLikelihood)).OrderBy(f => f.Aic).ToList();
    }

    public static double ClaytonLogDensity(double u, double v, double theta) =>
        Math.Log(1 + theta) + (-1 - theta) * (Math.Log(u) + Math.Log(v))
        + (-2 - 1 / theta) * Math.Log(Math.Pow(u, -theta) + Math.Pow(v, -theta) - 1);

    public static double GumbelLogDensity(double u, double v, double theta)
    {
        double x = -Math.Log(u), y = -Math.Log(v);
        double a = Math.Pow(x, theta) + Math.Pow(y, theta);
        double w = Math.Pow(a, 1 / theta);
        return -w - Math.Log(u) - Math.Log(v) + (theta - 1) * (Math.Log(x) + Math.Log(y))
             + (1 / theta - 2) * Math.Log(a) + Math.Log(w + theta - 1);
    }

    public static double GaussianLogDensity(double u, double v, double rho)
    {
        double x = Distributions.NormalQuantile(u), y = Distributions.NormalQuantile(v);
        double r2 = 1 - rho * rho;
        return -0.5 * Math.Log(r2) - (rho * rho * (x * x + y * y) - 2 * rho * x * y) / (2 * r2);
    }

    /// <summary>Log density of the t copula at quantiles (x, y) = t_ν⁻¹(u), t_ν⁻¹(v).</summary>
    public static double StudentTLogDensity(double x, double y, double rho, double nu)
    {
        double r2 = 1 - rho * rho;
        double joint = EvidenceStats.LogGamma((nu + 2) / 2) - EvidenceStats.LogGamma(nu / 2) - Math.Log(nu * Math.PI) - 0.5 * Math.Log(r2)
                       - (nu + 2) / 2 * Math.Log(1 + (x * x - 2 * rho * x * y + y * y) / (nu * r2));
        return joint - StudentTLogPdf(x, nu) - StudentTLogPdf(y, nu);
    }

    /// <summary>λ = 2·t_{ν+1}(−√((ν+1)(1−ρ)/(1+ρ))): the t copula's (symmetric) tail dependence.</summary>
    public static double StudentTTailDependence(double rho, double nu) =>
        2 * Distributions.StudentTUpperTail(Math.Sqrt((nu + 1) * (1 - rho) / (1 + rho)), nu + 1);

    public static double StudentTQuantile(double p, double nu)
    {
        double lo = -1e3, hi = 1e3;
        for (int i = 0; i < 100; i++)
        {
            double mid = 0.5 * (lo + hi);
            if (1 - Distributions.StudentTUpperTail(mid, nu) < p) lo = mid; else hi = mid;
        }
        return 0.5 * (lo + hi);
    }

    private static double StudentTLogPdf(double x, double nu) =>
        EvidenceStats.LogGamma((nu + 1) / 2) - EvidenceStats.LogGamma(nu / 2) - 0.5 * Math.Log(nu * Math.PI)
        - (nu + 1) / 2 * Math.Log(1 + x * x / nu);

    private static double Sum(double[] u, double[] v, Func<double, double, double> logDensity)
    {
        double s = 0;
        for (int i = 0; i < Math.Min(u.Length, v.Length); i++) s += logDensity(u[i], v[i]);
        return s;
    }
}
