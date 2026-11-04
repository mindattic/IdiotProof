namespace IdiotProof.Evidence.Adaptation;

/// <summary>
/// Thompson sampling over a fixed set of arms (parameter variants) with Beta(α, β) posteriors on
/// each arm's win rate. Choosing draws one sample from every posterior and takes the largest, so
/// an arm is picked in proportion to the probability that it is truly the best: well-evidenced
/// winners dominate while thinly-tested arms still get explored. Seeded, so a decision replays exactly.
/// </summary>
public sealed class ThompsonBandit
{
    private readonly double[] alpha;
    private readonly double[] beta;
    private readonly Random rng;

    public ThompsonBandit(int arms, int seed, double priorAlpha = 1, double priorBeta = 1)
    {
        if (arms < 1) throw new ArgumentOutOfRangeException(nameof(arms));
        if (priorAlpha <= 0 || priorBeta <= 0) throw new ArgumentOutOfRangeException(nameof(priorAlpha));
        alpha = Enumerable.Repeat(priorAlpha, arms).ToArray();
        beta = Enumerable.Repeat(priorBeta, arms).ToArray();
        rng = new Random(seed);
    }

    public int Arms => alpha.Length;

    public void Update(int arm, bool win)
    {
        if (win) alpha[arm] += 1; else beta[arm] += 1;
    }

    public (double Alpha, double Beta) Posterior(int arm) => (alpha[arm], beta[arm]);

    public double PosteriorMean(int arm) => alpha[arm] / (alpha[arm] + beta[arm]);

    public int Choose()
    {
        int best = 0;
        double bestDraw = double.NegativeInfinity;
        for (int a = 0; a < alpha.Length; a++)
        {
            double draw = SampleBeta(alpha[a], beta[a]);
            if (draw > bestDraw) { bestDraw = draw; best = a; }
        }
        return best;
    }

    /// <summary>Monte-Carlo estimate of P(arm is the best) for every arm, from the current posteriors.</summary>
    public double[] ProbabilityBest(int samples = 4000)
    {
        var wins = new int[alpha.Length];
        for (int s = 0; s < samples; s++)
        {
            int best = 0;
            double bestDraw = double.NegativeInfinity;
            for (int a = 0; a < alpha.Length; a++)
            {
                double d = SampleBeta(alpha[a], beta[a]);
                if (d > bestDraw) { bestDraw = d; best = a; }
            }
            wins[best]++;
        }
        return wins.Select(w => (double)w / samples).ToArray();
    }

    private double SampleBeta(double a, double b)
    {
        double x = SampleGamma(a), y = SampleGamma(b);
        return x / (x + y);
    }

    /// <summary>Marsaglia–Tsang gamma sampler; shapes below 1 use the U^(1/a) boost.</summary>
    private double SampleGamma(double shape)
    {
        if (shape < 1)
            return SampleGamma(shape + 1) * Math.Pow(1.0 - rng.NextDouble(), 1.0 / shape);
        double d = shape - 1.0 / 3, c = 1 / Math.Sqrt(9 * d);
        while (true)
        {
            double x, v;
            do
            {
                double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble();
                x = Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
                v = 1 + c * x;
            } while (v <= 0);
            v = v * v * v;
            double u = 1.0 - rng.NextDouble();
            if (u < 1 - 0.0331 * x * x * x * x) return d * v;
            if (Math.Log(u) < 0.5 * x * x + d * (1 - v + Math.Log(v))) return d * v;
        }
    }
}
