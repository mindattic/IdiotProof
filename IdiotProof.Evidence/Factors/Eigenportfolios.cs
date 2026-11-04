namespace IdiotProof.Evidence.Factors;

/// <summary>
/// The common factors behind a set of return series (principal components of their correlation
/// matrix) and what is left of each series once those factors are taken out. A stock's residual
/// is its own story: the move the market and its sector don't explain.
/// </summary>
public sealed class Eigenportfolios
{
    private readonly double[][] factorReturns;

    public EigenResult Eigen { get; }
    public int Observations { get; }

    private Eigenportfolios(EigenResult eigen, double[][] factorReturns, int observations)
    {
        Eigen = eigen;
        this.factorReturns = factorReturns;
        Observations = observations;
    }

    /// <summary>Eigenvalues above the Marchenko-Pastur upper edge: how many factors the data supports.</summary>
    public int SignificantFactors => Eigen.Values.Count(v => v > RandomMatrix.MarchenkoPasturUpper(Eigen.Size, Observations));

    public static Eigenportfolios Fit(IReadOnlyList<double[]> series)
    {
        if (series.Count < 2) throw new ArgumentException("Need at least two series.", nameof(series));
        int t = series.Min(s => s.Length);
        if (t < 3) throw new ArgumentException("Need at least three observations.", nameof(series));
        var z = series.Select(s => Standardize(s, t)).ToArray();
        var eigen = SymmetricEigen.Decompose(RandomMatrix.Correlation(z));

        int n = series.Count;
        var factors = new double[n][];
        for (int k = 0; k < n; k++)
        {
            var f = new double[t];
            for (int i = 0; i < n; i++)
            {
                double w = eigen.Vectors[i, k];
                for (int x = 0; x < t; x++) f[x] += w * z[i][x];
            }
            factors[k] = f;
        }
        return new Eigenportfolios(eigen, factors, t);
    }

    /// <summary>The return series of the k-th eigenportfolio (0 = largest eigenvalue, usually the market).</summary>
    public double[] FactorReturns(int k) => (double[])factorReturns[k].Clone();

    /// <summary>
    /// Each input series minus its least-squares exposure to the top <paramref name="factors"/>
    /// eigenportfolios. The factors are mutually uncorrelated in-sample, so per-factor betas are the
    /// joint regression's betas, and every residual is uncorrelated with every removed factor.
    /// </summary>
    public double[][] Residuals(IReadOnlyList<double[]> series, int factors)
    {
        if (factors < 0 || factors > Eigen.Size) throw new ArgumentOutOfRangeException(nameof(factors));
        int t = Observations;
        var result = new double[series.Count][];
        for (int i = 0; i < series.Count; i++)
        {
            var r = series[i].Take(t).ToArray();
            double mean = r.Average();
            var res = r.Select(x => x - mean).ToArray();
            for (int k = 0; k < factors; k++)
            {
                var f = factorReturns[k];
                double fm = f.Average(), cov = 0, var = 0;
                for (int x = 0; x < t; x++) { cov += res[x] * (f[x] - fm); var += (f[x] - fm) * (f[x] - fm); }
                if (var <= 0) continue;
                double beta = cov / var;
                for (int x = 0; x < t; x++) res[x] -= beta * (f[x] - fm);
            }
            result[i] = res;
        }
        return result;
    }

    private static double[] Standardize(double[] s, int t)
    {
        var x = s.Take(t).ToArray();
        double m = x.Average();
        double sd = Math.Sqrt(x.Sum(v => (v - m) * (v - m)) / t);
        return sd <= 0 ? new double[t] : x.Select(v => (v - m) / sd).ToArray();
    }
}
