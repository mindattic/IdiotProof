namespace IdiotProof.Evidence.PointProcess;

/// <summary>
/// Exponential-kernel Hawkes parameters: λ(t) = μ + Σ α·e^(−β(t − tᵢ)). Each event spawns on average
/// n = α/β children, so n is the share of activity that is endogenous (trades begetting trades)
/// rather than driven by outside news; the process is stationary only while n &lt; 1.
/// </summary>
public readonly record struct HawkesParams(double Mu, double Alpha, double Beta)
{
    public double BranchingRatio => Beta > 0 ? Alpha / Beta : double.PositiveInfinity;
    public bool IsStationary => Mu > 0 && Alpha >= 0 && Beta > 0 && BranchingRatio < 1;

    /// <summary>Long-run event rate μ / (1 − n); infinite when the process is explosive.</summary>
    public double UnconditionalRate => IsStationary ? Mu / (1 - BranchingRatio) : double.PositiveInfinity;
}

public sealed record HawkesFit(HawkesParams Params, double LogLikelihood, double PoissonLogLikelihood, int Events, double Horizon)
{
    /// <summary>Likelihood-ratio statistic against a constant-rate Poisson fit (2 extra parameters).</summary>
    public double LikelihoodRatio => 2 * (LogLikelihood - PoissonLogLikelihood);
}

/// <summary>Univariate Hawkes process: exact O(n) likelihood, maximum-likelihood fit, intensity and Ogata simulation.</summary>
public static class HawkesProcess
{
    /// <summary>
    /// Exact log-likelihood on [0, horizon] for sorted event times, via the recursion
    /// Aᵢ = e^(−β(tᵢ − tᵢ₋₁))·(1 + Aᵢ₋₁): LL = Σ log(μ + α·Aᵢ) − μ·T − (α/β)·Σ(1 − e^(−β(T − tᵢ))).
    /// </summary>
    public static double LogLikelihood(IReadOnlyList<double> times, double horizon, HawkesParams p)
    {
        if (p.Mu <= 0 || p.Alpha < 0 || p.Beta <= 0) return double.NegativeInfinity;
        double ll = 0, a = 0, compensator = p.Mu * horizon;
        for (int i = 0; i < times.Count; i++)
        {
            if (i > 0) a = Math.Exp(-p.Beta * (times[i] - times[i - 1])) * (1 + a);
            double lambda = p.Mu + p.Alpha * a;
            if (lambda <= 0) return double.NegativeInfinity;
            ll += Math.Log(lambda);
            compensator += p.Alpha / p.Beta * (1 - Math.Exp(-p.Beta * (horizon - times[i])));
        }
        return ll - compensator;
    }

    /// <summary>Conditional intensity at t from events strictly before t (never one at or after t).</summary>
    public static double Intensity(IReadOnlyList<double> times, double t, HawkesParams p)
    {
        double s = 0;
        for (int i = 0; i < times.Count && times[i] < t; i++) s += Math.Exp(-p.Beta * (t - times[i]));
        return p.Mu + p.Alpha * s;
    }

    /// <summary>
    /// Maximum likelihood over (log μ, logit n, log β), so every candidate is stationary by construction.
    /// Several β starts spanning the typical inter-event gap guard against a local optimum.
    /// </summary>
    public static HawkesFit Fit(IReadOnlyList<double> times, double horizon, int maxEvaluations = 1500)
    {
        if (horizon <= 0) throw new ArgumentOutOfRangeException(nameof(horizon));
        int n = times.Count;
        double poissonRate = Math.Max(n, 1) / horizon;
        double poissonLl = n == 0 ? -poissonRate * horizon : n * Math.Log(poissonRate) - poissonRate * horizon;
        if (n < 3)
            return new HawkesFit(new HawkesParams(poissonRate, 0, 1), poissonLl, poissonLl, n, horizon);

        double meanGap = horizon / n;
        HawkesParams best = new(poissonRate, 0, 1 / meanGap);
        double bestLl = poissonLl;
        foreach (var gapMultiple in new[] { 0.2, 2.0, 20.0 })
        {
            double beta0 = 1 / (meanGap * gapMultiple);
            var start = new[] { Math.Log(poissonRate * 0.5), 0.0, Math.Log(beta0) };
            var (x, value, _) = NelderMead.Minimize(v => -LogLikelihood(times, horizon, Decode(v)), start, 0.7, maxEvaluations);
            double ll = -value;
            if (ll > bestLl) { bestLl = ll; best = Decode(x); }
        }
        return new HawkesFit(best, bestLl, poissonLl, n, horizon);
    }

    /// <summary>Ogata thinning: between events the exponential intensity only decays, so the current λ bounds it.</summary>
    public static List<double> Simulate(HawkesParams p, double horizon, int seed)
    {
        var rng = new Random(seed);
        var events = new List<double>();
        double t = 0, s = 0;
        while (true)
        {
            double bound = p.Mu + p.Alpha * s;
            double w = -Math.Log(1 - rng.NextDouble()) / bound;
            t += w;
            if (t >= horizon) break;
            s *= Math.Exp(-p.Beta * w);
            double lambda = p.Mu + p.Alpha * s;
            if (rng.NextDouble() * bound <= lambda)
            {
                events.Add(t);
                s += 1;
            }
        }
        return events;
    }

    private static HawkesParams Decode(double[] v)
    {
        double mu = Math.Exp(v[0]);
        double branching = 1 / (1 + Math.Exp(-v[1]));
        double beta = Math.Exp(v[2]);
        return new HawkesParams(mu, branching * beta, beta);
    }
}
