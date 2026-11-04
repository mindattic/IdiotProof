namespace IdiotProof.Evidence.Regimes;

/// <summary>Normal-Gamma prior over an unknown mean and precision.</summary>
public readonly record struct NormalGammaPrior(double Mu, double Kappa, double Alpha, double Beta)
{
    /// <summary>
    /// A weak prior scaled to a warm-up sample: centered on its mean, worth one observation, with a
    /// prior variance equal to the sample variance (never below <paramref name="minVariance"/>, the
    /// tick-rounding floor from <see cref="TickNoise"/>). Pass only data that precedes what will be scored.
    /// </summary>
    public static NormalGammaPrior FromSample(IReadOnlyList<double> warmup, double minVariance = 0)
    {
        double mean = warmup.Count == 0 ? 0 : EvidenceStats.Mean(warmup);
        double sd = warmup.Count < 2 ? 0 : EvidenceStats.StdDev(warmup);
        double variance = Math.Max(sd > 0 ? sd * sd : 1e-8, minVariance);
        return new NormalGammaPrior(mean, 1, 2, variance);
    }
}

/// <summary>One detected change: when it was flagged and where the new regime is estimated to begin.</summary>
public readonly record struct ChangePoint(int DetectedAt, int StartsAt, double Probability);

/// <summary>
/// Bayesian online change-point detection (Adams &amp; MacKay 2007) with a Normal-Gamma conjugate model
/// and a constant hazard. After each observation it holds P(run length = r | data so far): how many
/// observations ago the current regime began. It only ever conditions on data already seen.
/// </summary>
public sealed class Bocpd
{
    private readonly NormalGammaPrior prior;
    private readonly double hazard;
    private readonly int maxRunLength;
    private readonly double minVariance;
    private readonly double halfTick;
    private double[] runProbs;
    private double[] mu, kappa, alpha, beta;

    /// <param name="minVariance">Floor on the predictive variance of every run hypothesis: the tick-rounding
    /// noise (<see cref="TickNoise"/>), so a run of unchanged prices can't make a 1-tick move look like a break.</param>
    public Bocpd(NormalGammaPrior prior, double hazard = 1.0 / 250, int maxRunLength = 1000, double minVariance = 0)
    {
        if (hazard <= 0 || hazard >= 1) throw new ArgumentOutOfRangeException(nameof(hazard));
        if (maxRunLength < 2) throw new ArgumentOutOfRangeException(nameof(maxRunLength));
        this.prior = prior;
        this.hazard = hazard;
        this.maxRunLength = maxRunLength;
        this.minVariance = Math.Max(0, minVariance);
        // TickNoise's floor is (tick/price)²/6, so half a tick in return units is √(6·floor)/2.
        halfTick = Math.Sqrt(6 * this.minVariance) / 2;
        runProbs = [1.0];
        mu = [prior.Mu];
        kappa = [prior.Kappa];
        alpha = [prior.Alpha];
        beta = [prior.Beta];
    }

    public int Observations { get; private set; }

    /// <summary>P(run length = r) for r = 0..; index r means the regime began r observations before the latest one.</summary>
    public IReadOnlyList<double> RunLengthPosterior => runProbs;

    public int MapRunLength
    {
        get
        {
            int best = 0;
            for (int r = 1; r < runProbs.Length; r++) if (runProbs[r] > runProbs[best]) best = r;
            return best;
        }
    }

    /// <summary>P(the regime began within the last <paramref name="window"/> observations).</summary>
    public double ShortRunProbability(int window)
    {
        double p = 0;
        for (int r = 0; r < Math.Min(window, runProbs.Length); r++) p += runProbs[r];
        return p;
    }

    public void Update(double x)
    {
        int n = runProbs.Length;
        var growth = new double[n + 1];
        double changeMass = 0;
        for (int r = 0; r < n; r++)
        {
            // A hypothesis with no posterior mass contributes nothing; skipping it keeps the binned likelihood cheap.
            if (runProbs[r] < 1e-15) continue;
            double pred = Math.Exp(StudentTLogPdf(x, r));
            double joint = runProbs[r] * pred;
            growth[r + 1] = joint * (1 - hazard);
            changeMass += joint * hazard;
        }
        growth[0] = changeMass;

        double total = 0;
        foreach (var g in growth) total += g;
        if (!(total > 0) || double.IsNaN(total))
        {
            // Every hypothesis assigned ~0 density (an extreme outlier): treat it as a certain change.
            Array.Clear(growth);
            growth[0] = 1;
            total = 1;
        }
        for (int i = 0; i < growth.Length; i++) growth[i] /= total;

        var newMu = new double[n + 1];
        var newKappa = new double[n + 1];
        var newAlpha = new double[n + 1];
        var newBeta = new double[n + 1];
        newMu[0] = prior.Mu; newKappa[0] = prior.Kappa; newAlpha[0] = prior.Alpha; newBeta[0] = prior.Beta;
        for (int r = 0; r < n; r++)
        {
            double k = kappa[r];
            newMu[r + 1] = (k * mu[r] + x) / (k + 1);
            newKappa[r + 1] = k + 1;
            newAlpha[r + 1] = alpha[r] + 0.5;
            newBeta[r + 1] = beta[r] + k * (x - mu[r]) * (x - mu[r]) / (2 * (k + 1));
        }

        if (growth.Length > maxRunLength)
        {
            // Fold the oldest hypothesis into its neighbour so memory stays bounded.
            int last = growth.Length - 1;
            growth[last - 1] += growth[last];
            Array.Resize(ref growth, last);
            Array.Resize(ref newMu, last);
            Array.Resize(ref newKappa, last);
            Array.Resize(ref newAlpha, last);
            Array.Resize(ref newBeta, last);
        }

        runProbs = growth;
        mu = newMu; kappa = newKappa; alpha = newAlpha; beta = newBeta;
        Observations++;
    }

    /// <summary>
    /// Streams the series and reports each change once. Two signals, either suffices:
    /// <list type="bullet">
    /// <item>an abrupt change: the probability that the regime began within the last <paramref name="window"/>
    /// observations crosses <paramref name="threshold"/> (fires once per episode);</item>
    /// <item>a gradual one (e.g. volatility calming down, where the new run needs more than
    /// <paramref name="window"/> observations to win): the most likely run length drops by at least
    /// <paramref name="minMapDrop"/> and at least <paramref name="threshold"/> of the mass lies on runs
    /// no longer than the new one.</item>
    /// </list>
    /// Events within 2 × window of the previous one are merged. The first <paramref name="warmup"/>
    /// observations are never flagged (the run-length posterior is still unsettled then).
    /// Measured trade-off (RegimeDetectionTests): the gradual rule is what catches a volatility
    /// decrease, at the price of roughly 1–1.5 false alarms per 1,000 bars of stationary Gaussian noise
    /// (a calm patch looks like a calmer regime); pass a large <paramref name="minMapDrop"/> to disable it.
    /// </summary>
    public static IReadOnlyList<ChangePoint> Detect(IReadOnlyList<double> xs, NormalGammaPrior prior, double hazard = 1.0 / 250,
        int window = 5, double threshold = 0.5, int warmup = 50, int minMapDrop = 10, double minVariance = 0)
    {
        var model = new Bocpd(prior, hazard, minVariance: minVariance);
        var events = new List<ChangePoint>();
        bool inEpisode = false;
        int prevMap = 0, lastEvent = int.MinValue / 2;
        for (int t = 0; t < xs.Count; t++)
        {
            model.Update(xs[t]);
            int map = model.MapRunLength;
            if (t < Math.Max(warmup, 2 * window)) { prevMap = map; continue; }

            double p = model.ShortRunProbability(window);
            bool abrupt = p >= threshold && !inEpisode;
            // A run-length posterior spread thin over old hypotheses can shuffle its peak on noise;
            // only count the drop when most of the mass really sits on the newer run.
            bool gradual = prevMap - map >= minMapDrop && model.ShortRunProbability(map + window) >= threshold;
            inEpisode = p >= threshold;
            prevMap = map;
            if (!(abrupt || gradual) || t - lastEvent <= 2 * window) continue;

            int r = abrupt ? ShortMap(model, window) : map;
            events.Add(new ChangePoint(t, t - r, abrupt ? p : model.runProbs[map]));
            lastEvent = t;
        }
        return events;
    }

    private static int ShortMap(Bocpd model, int window)
    {
        var probs = model.runProbs;
        int best = 0;
        for (int r = 1; r < Math.Min(window, probs.Length); r++) if (probs[r] > probs[best]) best = r;
        return best;
    }

    private double StudentTLogPdf(double x, int r)
    {
        double nu = 2 * alpha[r];
        double scale2 = Math.Max(beta[r] * (kappa[r] + 1) / (alpha[r] * kappa[r]), minVariance);
        if (halfTick > 0)
        {
            // Prices print on a tick grid, so a return is only known to its tick bin: score the predictive
            // mass of the bin (interval-censored likelihood) per unit width, not a density at a point.
            double s = Math.Sqrt(scale2);
            double mass = BinMass((x - halfTick - mu[r]) / s, (x + halfTick - mu[r]) / s, nu);
            return Math.Log(Math.Max(mass, 1e-300) / (2 * halfTick));
        }
        double z = (x - mu[r]) * (x - mu[r]) / (nu * scale2);
        return EvidenceStats.LogGamma((nu + 1) / 2) - EvidenceStats.LogGamma(nu / 2)
               - 0.5 * Math.Log(nu * Math.PI * scale2) - (nu + 1) / 2 * Math.Log(1 + z);
    }

    /// <summary>P(a &lt; T ≤ b) for Student's t, computed from the tail that avoids cancellation.</summary>
    private static double BinMass(double a, double b, double nu)
    {
        double U(double t) => Search.Distributions.StudentTUpperTail(t, nu);
        if (a >= 0) return U(a) - U(b);
        if (b <= 0) return U(-b) - U(-a);
        return 1 - U(b) - U(-a);
    }
}
