namespace IdiotProof.Evidence.Survival;

/// <summary>One covariate's fitted effect: hazard ratio per unit, its 95% interval and Wald p-value.</summary>
public sealed record CoxCoefficient(string Name, double Beta, double StandardError, double HazardRatio, double Low, double High, double PValue);

public sealed record CoxFit(IReadOnlyList<CoxCoefficient> Coefficients, int Observations, int Events, int Iterations, bool Converged, double LogLikelihood);

/// <summary>
/// Cox proportional-hazards model, h(t|x) = h0(t)·exp(β·x), fitted by Newton-Raphson on the
/// partial likelihood with Breslow's handling of tied times. A hazard ratio above 1 means the
/// covariate makes that exit come sooner.
/// </summary>
public static class CoxModel
{
    public static CoxFit Fit(IReadOnlyList<double> times, IReadOnlyList<bool> events, IReadOnlyList<double[]> covariates,
        IReadOnlyList<string>? names = null, int maxIterations = 50, double tolerance = 1e-9)
    {
        int n = times.Count;
        if (events.Count != n || covariates.Count != n) throw new ArgumentException("Length mismatch.");
        if (n == 0) throw new ArgumentException("No observations.");
        int p = covariates[0].Length;
        if (covariates.Any(x => x.Length != p)) throw new ArgumentException("Every covariate vector needs the same length.");

        var order = Enumerable.Range(0, n).OrderBy(i => times[i]).ToArray();
        var beta = new double[p];
        double ll = LogLikelihood(beta, times, events, covariates, order, out var grad, out var info);
        int iter = 0;
        bool converged = false;
        for (; iter < maxIterations; iter++)
        {
            var step = Solve(info, grad);
            if (step is null) break;
            double scale = 1;
            double[] candidate = beta;
            double candidateLl = double.NegativeInfinity;
            double[] g2 = grad; double[,] i2 = info;
            for (int half = 0; half < 30; half++)
            {
                candidate = beta.Select((b, k) => b + scale * step[k]).ToArray();
                candidateLl = LogLikelihood(candidate, times, events, covariates, order, out g2, out i2);
                if (candidateLl >= ll - 1e-12) break;
                scale /= 2;
            }
            double change = Math.Abs(candidateLl - ll);
            beta = candidate; ll = candidateLl; grad = g2; info = i2;
            if (change < tolerance) { converged = true; iter++; break; }
        }

        var cov = Invert(info);
        var coefficients = new List<CoxCoefficient>(p);
        for (int k = 0; k < p; k++)
        {
            double se = cov is null ? double.NaN : Math.Sqrt(Math.Max(0, cov[k, k]));
            double z = beta[k] / se;
            double pValue = double.IsNaN(z) ? double.NaN : 2 * (1 - NormalCdf(Math.Abs(z)));
            coefficients.Add(new CoxCoefficient(names is not null && k < names.Count ? names[k] : $"x{k}", beta[k], se,
                Math.Exp(beta[k]), Math.Exp(beta[k] - 1.959963984540054 * se), Math.Exp(beta[k] + 1.959963984540054 * se), pValue));
        }
        return new CoxFit(coefficients, n, events.Count(e => e), iter, converged, ll);
    }

    /// <summary>Breslow partial log-likelihood with its gradient and observed information (negative Hessian).</summary>
    private static double LogLikelihood(double[] beta, IReadOnlyList<double> times, IReadOnlyList<bool> events,
        IReadOnlyList<double[]> x, int[] order, out double[] grad, out double[,] info)
    {
        int n = order.Length, p = beta.Length;
        grad = new double[p];
        info = new double[p, p];
        var risk = new double[n];
        for (int i = 0; i < n; i++)
        {
            double eta = 0;
            for (int k = 0; k < p; k++) eta += beta[k] * x[i][k];
            risk[i] = Math.Exp(Math.Clamp(eta, -700, 700));
        }

        // Accumulate risk-set sums from the latest time backwards so each tie group sees everyone still at risk.
        double s0 = 0;
        var s1 = new double[p];
        var s2 = new double[p, p];
        double ll = 0;
        int idx = n - 1;
        while (idx >= 0)
        {
            double t = times[order[idx]];
            int groupEnd = idx;
            while (idx >= 0 && times[order[idx]] == t)
            {
                int i = order[idx];
                s0 += risk[i];
                for (int a = 0; a < p; a++)
                {
                    s1[a] += risk[i] * x[i][a];
                    for (int b = 0; b < p; b++) s2[a, b] += risk[i] * x[i][a] * x[i][b];
                }
                idx--;
            }
            for (int g = groupEnd; g > idx; g--)
            {
                int i = order[g];
                if (!events[i]) continue;
                ll += Math.Log(risk[i]) - Math.Log(s0);
                for (int a = 0; a < p; a++)
                {
                    double mean = s1[a] / s0;
                    grad[a] += x[i][a] - mean;
                    for (int b = 0; b < p; b++) info[a, b] += s2[a, b] / s0 - mean * (s1[b] / s0);
                }
            }
        }
        return ll;
    }

    private static double[]? Solve(double[,] a, double[] b)
    {
        var inv = Invert(a);
        if (inv is null) return null;
        int p = b.Length;
        var x = new double[p];
        for (int i = 0; i < p; i++)
            for (int j = 0; j < p; j++) x[i] += inv[i, j] * b[j];
        return x;
    }

    /// <summary>Gauss-Jordan inverse with partial pivoting; null when singular.</summary>
    internal static double[,]? Invert(double[,] a)
    {
        int n = a.GetLength(0);
        var m = new double[n, 2 * n];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++) m[i, j] = a[i, j];
            m[i, n + i] = 1;
        }
        for (int col = 0; col < n; col++)
        {
            int pivot = col;
            for (int r = col + 1; r < n; r++) if (Math.Abs(m[r, col]) > Math.Abs(m[pivot, col])) pivot = r;
            if (Math.Abs(m[pivot, col]) < 1e-12) return null;
            if (pivot != col)
                for (int j = 0; j < 2 * n; j++) (m[col, j], m[pivot, j]) = (m[pivot, j], m[col, j]);
            double d = m[col, col];
            for (int j = 0; j < 2 * n; j++) m[col, j] /= d;
            for (int r = 0; r < n; r++)
            {
                if (r == col || m[r, col] == 0) continue;
                double f = m[r, col];
                for (int j = 0; j < 2 * n; j++) m[r, j] -= f * m[col, j];
            }
        }
        var inv = new double[n, n];
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++) inv[i, j] = m[i, n + j];
        return inv;
    }

    /// <summary>Standard normal CDF (Abramowitz-Stegun 7.1.26 erf, |error| &lt; 1.5e-7).</summary>
    internal static double NormalCdf(double z)
    {
        double x = Math.Abs(z) / Math.Sqrt(2);
        double t = 1 / (1 + 0.3275911 * x);
        double erf = 1 - (((((1.061405429 * t - 1.453152027) * t) + 1.421413741) * t - 0.284496736) * t + 0.254829592) * t * Math.Exp(-x * x);
        return z >= 0 ? 0.5 * (1 + erf) : 0.5 * (1 - erf);
    }
}
