namespace IdiotProof.Evidence.Regimes;

/// <summary>One hidden state: the mean and spread of the returns it produces.</summary>
public readonly record struct HmmState(double Mean, double StdDev);

/// <summary>
/// A Gaussian hidden Markov model over one-dimensional observations (e.g. bar returns). Trained with
/// Baum-Welch on past sessions; applied live with forward filtering only, so the state probability at
/// bar t uses bars 0..t and never the smoothing pass that would read later bars. States are ordered by
/// standard deviation, calmest first, so state 0 is always the quiet regime.
/// </summary>
public sealed class GaussianHmm
{
    public IReadOnlyList<HmmState> States { get; }
    public double[,] Transition { get; }
    public IReadOnlyList<double> Initial { get; }
    public double LogLikelihood { get; }

    private GaussianHmm(HmmState[] states, double[,] transition, double[] initial, double logLikelihood)
    {
        States = states;
        Transition = transition;
        Initial = initial;
        LogLikelihood = logLikelihood;
    }

    public int StateCount => States.Count;

    /// <summary>P(stay in state k for the next bar).</summary>
    public double Persistence(int k) => Transition[k, k];

    public static GaussianHmm Fit(IReadOnlyList<double> obs, int states = 2, int maxIterations = 200, double tolerance = 1e-6)
    {
        if (states < 2 || states > 5) throw new ArgumentOutOfRangeException(nameof(states));
        if (obs.Count < states * 10) throw new ArgumentException("Too few observations to fit the model.", nameof(obs));
        int n = obs.Count, k = states;

        // Deterministic start: states split by |deviation| quantile so they begin calm-to-volatile.
        double globalMean = EvidenceStats.Mean(obs);
        var byDeviation = obs.OrderBy(x => Math.Abs(x - globalMean)).ToArray();
        var means = new double[k];
        var vars = new double[k];
        for (int s = 0; s < k; s++)
        {
            int lo = s * n / k, hi = (s + 1) * n / k;
            var slice = new ArraySegment<double>(byDeviation, lo, Math.Max(2, hi - lo));
            means[s] = globalMean;
            double sd = Math.Sqrt(slice.Select(x => (x - globalMean) * (x - globalMean)).Average());
            vars[s] = Math.Max(sd * sd, 1e-12);
        }
        var trans = new double[k, k];
        for (int i = 0; i < k; i++)
        for (int j = 0; j < k; j++)
            trans[i, j] = i == j ? 0.9 : 0.1 / (k - 1);
        var init = Enumerable.Repeat(1.0 / k, k).ToArray();
        double varFloor = Math.Max(1e-12, EvidenceStats.StdDev(obs) * EvidenceStats.StdDev(obs) * 1e-4);

        double prevLl = double.NegativeInfinity, ll = double.NegativeInfinity;
        for (int iter = 0; iter < maxIterations; iter++)
        {
            var (alphaHat, scale) = Forward(obs, means, vars, trans, init);
            ll = scale.Sum(Math.Log);
            var betaHat = Backward(obs, means, vars, trans, scale);

            var gamma = new double[n, k];
            for (int t = 0; t < n; t++)
            {
                double sum = 0;
                for (int s = 0; s < k; s++) { gamma[t, s] = alphaHat[t, s] * betaHat[t, s]; sum += gamma[t, s]; }
                for (int s = 0; s < k; s++) gamma[t, s] /= sum;
            }

            var xiSum = new double[k, k];
            for (int t = 0; t + 1 < n; t++)
            {
                double norm = 0;
                var xi = new double[k, k];
                for (int i = 0; i < k; i++)
                for (int j = 0; j < k; j++)
                {
                    xi[i, j] = alphaHat[t, i] * trans[i, j] * Density(obs[t + 1], means[j], vars[j]) * betaHat[t + 1, j];
                    norm += xi[i, j];
                }
                if (norm <= 0) continue;
                for (int i = 0; i < k; i++)
                for (int j = 0; j < k; j++)
                    xiSum[i, j] += xi[i, j] / norm;
            }

            for (int s = 0; s < k; s++) init[s] = gamma[0, s];
            for (int i = 0; i < k; i++)
            {
                double row = 0;
                for (int j = 0; j < k; j++) row += xiSum[i, j];
                for (int j = 0; j < k; j++) trans[i, j] = row > 0 ? xiSum[i, j] / row : (i == j ? 1 : 0);
            }
            for (int s = 0; s < k; s++)
            {
                double w = 0, m = 0;
                for (int t = 0; t < n; t++) { w += gamma[t, s]; m += gamma[t, s] * obs[t]; }
                if (w <= 0) continue;
                m /= w;
                double v = 0;
                for (int t = 0; t < n; t++) v += gamma[t, s] * (obs[t] - m) * (obs[t] - m);
                means[s] = m;
                vars[s] = Math.Max(v / w, varFloor);
            }

            if (Math.Abs(ll - prevLl) < tolerance * Math.Max(1, Math.Abs(ll))) break;
            prevLl = ll;
        }

        var order = Enumerable.Range(0, k).OrderBy(s => vars[s]).ToArray();
        var sortedStates = order.Select(s => new HmmState(means[s], Math.Sqrt(vars[s]))).ToArray();
        var sortedTrans = new double[k, k];
        for (int i = 0; i < k; i++)
        for (int j = 0; j < k; j++)
            sortedTrans[i, j] = trans[order[i], order[j]];
        var sortedInit = order.Select(s => init[s]).ToArray();
        return new GaussianHmm(sortedStates, sortedTrans, sortedInit, ll);
    }

    /// <summary>
    /// Forward-only filtering: row t is P(state | obs[0..t]). Changing any observation after t
    /// leaves row t unchanged.
    /// </summary>
    public double[][] Filter(IReadOnlyList<double> obs)
    {
        int k = StateCount;
        var means = States.Select(s => s.Mean).ToArray();
        var vars = States.Select(s => Math.Max(s.StdDev * s.StdDev, 1e-12)).ToArray();
        var result = new double[obs.Count][];
        var prev = Initial.ToArray();
        for (int t = 0; t < obs.Count; t++)
        {
            var cur = new double[k];
            double sum = 0;
            for (int j = 0; j < k; j++)
            {
                double prior = 0;
                if (t == 0) prior = prev[j];
                else for (int i = 0; i < k; i++) prior += prev[i] * Transition[i, j];
                cur[j] = prior * Density(obs[t], means[j], vars[j]);
                sum += cur[j];
            }
            if (!(sum > 0)) { for (int j = 0; j < k; j++) cur[j] = 1.0 / k; }
            else for (int j = 0; j < k; j++) cur[j] /= sum;
            result[t] = cur;
            prev = cur;
        }
        return result;
    }

    /// <summary>
    /// Regime switches seen live: the filtered most-likely state changes and the new state holds at
    /// least <paramref name="minProbability"/>. Switches within <paramref name="refractory"/> bars of
    /// the previous one are merged.
    /// </summary>
    public IReadOnlyList<ChangePoint> DetectSwitches(IReadOnlyList<double> obs, double minProbability = 0.8, int refractory = 10)
    {
        var filtered = Filter(obs);
        var events = new List<ChangePoint>();
        int current = -1, lastEvent = int.MinValue / 2;
        for (int t = 0; t < filtered.Length; t++)
        {
            int map = 0;
            for (int s = 1; s < StateCount; s++) if (filtered[t][s] > filtered[t][map]) map = s;
            if (filtered[t][map] < minProbability) continue;
            if (current < 0) { current = map; continue; }
            if (map == current) continue;
            current = map;
            if (t - lastEvent >= refractory) events.Add(new ChangePoint(t, t, filtered[t][map]));
            lastEvent = t;
        }
        return events;
    }

    private static (double[,] Alpha, double[] Scale) Forward(IReadOnlyList<double> obs, double[] means, double[] vars, double[,] trans, double[] init)
    {
        int n = obs.Count, k = means.Length;
        var a = new double[n, k];
        var scale = new double[n];
        for (int t = 0; t < n; t++)
        {
            double sum = 0;
            for (int j = 0; j < k; j++)
            {
                double prior = 0;
                if (t == 0) prior = init[j];
                else for (int i = 0; i < k; i++) prior += a[t - 1, i] * trans[i, j];
                a[t, j] = prior * Density(obs[t], means[j], vars[j]);
                sum += a[t, j];
            }
            if (!(sum > 0)) sum = 1e-300;
            scale[t] = sum;
            for (int j = 0; j < k; j++) a[t, j] /= sum;
        }
        return (a, scale);
    }

    private static double[,] Backward(IReadOnlyList<double> obs, double[] means, double[] vars, double[,] trans, double[] scale)
    {
        int n = obs.Count, k = means.Length;
        var b = new double[n, k];
        for (int j = 0; j < k; j++) b[n - 1, j] = 1;
        for (int t = n - 2; t >= 0; t--)
        for (int i = 0; i < k; i++)
        {
            double sum = 0;
            for (int j = 0; j < k; j++) sum += trans[i, j] * Density(obs[t + 1], means[j], vars[j]) * b[t + 1, j];
            b[t, i] = sum / scale[t + 1];
        }
        return b;
    }

    private static double Density(double x, double mean, double variance)
    {
        double d = x - mean;
        return Math.Exp(-d * d / (2 * variance)) / Math.Sqrt(2 * Math.PI * variance);
    }
}
