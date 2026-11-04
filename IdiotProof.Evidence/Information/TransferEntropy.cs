namespace IdiotProof.Evidence.Information;

/// <summary>How an edge between two symbols is measured.</summary>
public enum EdgeMeasure { Correlation, TransferEntropy }

/// <summary>Transfer entropy of a source onto a target, with its shuffled-surrogate baseline and significance.</summary>
public readonly record struct TransferEntropyResult(double Bits, double SurrogateMean, double SurrogateStdDev, double PValue, int Surrogates)
{
    /// <summary>Information beyond what a source with the same values in random order would show (bias-corrected).</summary>
    public double Effective => Bits - SurrogateMean;

    /// <summary>How many surrogate standard deviations the real TE sits above the surrogates.</summary>
    public double ZScore => SurrogateStdDev > 0 ? Effective / SurrogateStdDev : Effective > 0 ? double.PositiveInfinity : 0;

    /// <summary>
    /// Normal-approximation upper-tail p from <see cref="ZScore"/>. Unlike the empirical
    /// <see cref="PValue"/> it is not floored at 1/(surrogates+1), so it can survive a
    /// multiple-testing correction across many pairs.
    /// </summary>
    public double ParametricPValue => 1 - Search.Distributions.NormalCdf(ZScore);
}

/// <summary>
/// Shannon information measures on quantile-binned series, in bits. Transfer entropy
/// TE(X→Y) = H(Y⁺ | Yᵏ) − H(Y⁺ | Yᵏ, Xᵏ): how much knowing X's recent past reduces uncertainty
/// about Y's next value beyond what Y's own past already tells. It is directional and catches
/// nonlinear lead-lag that correlation misses (Y = X²: zero correlation, plenty of information).
/// </summary>
public static class TransferEntropy
{
    /// <summary>Entropy of a discrete sequence in bits.</summary>
    public static double Entropy(IReadOnlyList<int> symbols)
    {
        if (symbols.Count == 0) return 0;
        var counts = new Dictionary<int, int>();
        foreach (var s in symbols) counts[s] = counts.GetValueOrDefault(s) + 1;
        return EntropyOfCounts(counts.Values, symbols.Count);
    }

    /// <summary>I(X;Y) = H(X) + H(Y) − H(X,Y), in bits.</summary>
    public static double MutualInformation(IReadOnlyList<int> x, IReadOnlyList<int> y)
    {
        if (x.Count != y.Count) throw new ArgumentException("Length mismatch.");
        int stride = Math.Max(x.Count == 0 ? 1 : x.Max(), 0) + 1;
        var joint = new int[x.Count];
        for (int i = 0; i < x.Count; i++) joint[i] = y[i] * stride + x[i];
        return Entropy(x) + Entropy(y) - Entropy(joint);
    }

    /// <summary>Equal-frequency bin index of every value, with edges learned from the series itself.</summary>
    public static int[] Discretize(IReadOnlyList<double> xs, int bins)
    {
        var binner = new QuantileBinner(xs, bins);
        var result = new int[xs.Count];
        for (int i = 0; i < xs.Count; i++) result[i] = binner.Bin(xs[i]);
        return result;
    }

    /// <summary>
    /// TE(source→target) in bits, with history length <paramref name="k"/> for both series: the
    /// target's next value at t+1 against the target's and source's values at t-k+1..t.
    /// </summary>
    public static double Compute(IReadOnlyList<int> source, IReadOnlyList<int> target, int k = 1)
    {
        if (source.Count != target.Count) throw new ArgumentException("Length mismatch.");
        if (k < 1) throw new ArgumentOutOfRangeException(nameof(k));
        int n = target.Count;
        if (n <= k + 1) return 0;
        int stride = Math.Max(MaxOrZero(source), MaxOrZero(target)) + 1;

        var next = new Dictionary<(long Y1, long Yk, long Xk), int>();
        int samples = 0;
        for (int t = k - 1; t + 1 < n; t++)
        {
            long yk = 0, xk = 0;
            for (int j = 0; j < k; j++)
            {
                yk = yk * stride + target[t - j];
                xk = xk * stride + source[t - j];
            }
            var key = ((long)target[t + 1], yk, xk);
            next[key] = next.GetValueOrDefault(key) + 1;
            samples++;
        }

        var yNextYk = new Dictionary<(long, long), int>();
        var yk_ = new Dictionary<long, int>();
        var ykXk = new Dictionary<(long, long), int>();
        foreach (var ((y1, yk, xk), c) in next)
        {
            yNextYk[(y1, yk)] = yNextYk.GetValueOrDefault((y1, yk)) + c;
            yk_[yk] = yk_.GetValueOrDefault(yk) + c;
            ykXk[(yk, xk)] = ykXk.GetValueOrDefault((yk, xk)) + c;
        }

        double hYnextYk = EntropyOfCounts(yNextYk.Values, samples);
        double hYk = EntropyOfCounts(yk_.Values, samples);
        double hAll = EntropyOfCounts(next.Values, samples);
        double hYkXk = EntropyOfCounts(ykXk.Values, samples);
        return Math.Max(0, (hYnextYk - hYk) - (hAll - hYkXk));
    }

    /// <summary>
    /// TE on continuous series (quantile-binned into <paramref name="bins"/>), with a one-sided
    /// p-value from <paramref name="surrogates"/> seeded shuffles of the source: shuffling keeps the
    /// source's values but destroys any timing link, so p is the chance a timing-free source shows this much.
    /// </summary>
    public static TransferEntropyResult Test(IReadOnlyList<double> source, IReadOnlyList<double> target,
        int bins = 3, int k = 1, int surrogates = 100, int seed = 1337)
    {
        if (surrogates < 1) throw new ArgumentOutOfRangeException(nameof(surrogates));
        var x = Discretize(source, bins);
        var y = Discretize(target, bins);
        double te = Compute(x, y, k);

        var rng = new Random(seed);
        var shuffled = (int[])x.Clone();
        double sum = 0, sumSq = 0;
        int atLeast = 0;
        for (int s = 0; s < surrogates; s++)
        {
            for (int i = shuffled.Length - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
            }
            double ts = Compute(shuffled, y, k);
            sum += ts;
            sumSq += ts * ts;
            if (ts >= te) atLeast++;
        }
        double mean = sum / surrogates;
        double sd = surrogates > 1 ? Math.Sqrt(Math.Max(0, (sumSq - surrogates * mean * mean) / (surrogates - 1))) : 0;
        return new TransferEntropyResult(te, mean, sd, (atLeast + 1.0) / (surrogates + 1.0), surrogates);
    }

    private static double EntropyOfCounts(IEnumerable<int> counts, int total)
    {
        if (total <= 0) return 0;
        double h = 0;
        foreach (var c in counts)
        {
            if (c <= 0) continue;
            double p = (double)c / total;
            h -= p * Math.Log2(p);
        }
        return h;
    }

    private static int MaxOrZero(IReadOnlyList<int> xs)
    {
        int m = 0;
        foreach (var v in xs) if (v > m) m = v;
        return m;
    }
}
