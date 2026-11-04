using IdiotProof.Evidence.Regimes;

namespace IdiotProof.Evidence.Tests;

/// <summary>IP-T435: on a 1-cent grid the regime models judge real volatility, not which bars didn't tick.</summary>
public class RegimeTickTests
{
    /// <summary>
    /// A $0.80 stock printed on a 1-cent grid. The latent price takes log-steps of sd <paramref name="calmSd"/>
    /// for <paramref name="calmBars"/> bars, then <paramref name="volatileSd"/>; most calm bars show no change.
    /// </summary>
    private static (List<decimal> Prices, List<double> Returns) TickSeries(int calmBars, int volatileBars, int seed,
        double calmSd = 0.002, double volatileSd = 0.015)
    {
        var rng = new Random(seed);
        double latent = 0.80;
        var prices = new List<decimal>();
        for (int i = 0; i < calmBars + volatileBars; i++)
        {
            double sd = i < calmBars ? calmSd : volatileSd;
            latent *= Math.Exp(sd * Synthetic.Gaussian(rng));
            prices.Add(Math.Max(0.01m, Math.Round((decimal)latent, 2)));
        }
        var returns = new List<double>();
        for (int i = 1; i < prices.Count; i++) returns.Add(Math.Log((double)(prices[i] / prices[i - 1])));
        return (prices, returns);
    }

    [Test]
    public void Floor_IsTheTickRoundingNoise()
    {
        // One cent on $0.80 is 1.25%; two rounding errors of variance tick²/12 give (0.0125)²/6.
        Assert.That(TickNoise.ReturnVarianceFloor(0.01, 0.80), Is.EqualTo(0.0125 * 0.0125 / 6).Within(1e-15));
        var (prices, _) = TickSeries(500, 0, seed: 1);
        Assert.That(TickNoise.EstimateTick(prices), Is.EqualTo(0.01m));
        Assert.That(TickNoise.EstimateTick(new[] { 1m, 1m, 1m }), Is.Zero);
    }

    [Test]
    public void Hmm_SeparatesTheVolatilityRegimes_NotTheBarsThatDidntTick()
    {
        var (trainPrices, train) = TickSeries(1500, 1500, seed: 2);
        var (_, live) = TickSeries(1000, 1000, seed: 3);
        double floor = TickNoise.ReturnVarianceFloor(trainPrices);

        double Accuracy(GaussianHmm hmm)
        {
            var filtered = hmm.Filter(live);
            int right = 0;
            for (int t = 0; t < live.Count; t++)
            {
                int state = filtered[t][1] > filtered[t][0] ? 1 : 0;
                if (state == (t < 999 ? 0 : 1)) right++;
            }
            return (double)right / live.Count;
        }

        var tickAware = GaussianHmm.Fit(train, 2, minVariance: floor);
        var naive = GaussianHmm.Fit(train, 2);

        Assert.That(tickAware.States[0].StdDev, Is.GreaterThanOrEqualTo(Math.Sqrt(floor) - 1e-12), "the calm state can't be tighter than the rounding noise");
        Assert.That(Accuracy(tickAware), Is.GreaterThan(0.85));
        Assert.That(naive.States[0].StdDev, Is.LessThan(Math.Sqrt(floor) / 10), "without the floor the calm state collapses onto unchanged bars");
        Assert.That(Accuracy(tickAware), Is.GreaterThan(Accuracy(naive)));
    }

    [Test]
    public void Bocpd_StaysQuietOnStationaryTicks_ButStillCatchesTheVolatilityJump()
    {
        var (prices, calm) = TickSeries(4000, 0, seed: 4);
        double floor = TickNoise.ReturnVarianceFloor(prices);
        var prior = NormalGammaPrior.FromSample(calm.Take(50).ToList(), floor);

        var tickAware = Bocpd.Detect(calm, prior, minVariance: floor);
        var naive = Bocpd.Detect(calm, NormalGammaPrior.FromSample(calm.Take(50).ToList()));
        double perThousand = tickAware.Count * 1000.0 / calm.Count;

        Assert.That(perThousand, Is.LessThanOrEqualTo(1.5), $"{tickAware.Count} false alarms in {calm.Count} stationary tick bars");
        Assert.That(naive.Count, Is.GreaterThan(tickAware.Count), "the unfloored model over-flags unchanged-then-tick bars");

        var (switchPrices, switching) = TickSeries(600, 400, seed: 5);
        double switchFloor = TickNoise.ReturnVarianceFloor(switchPrices.Take(600).ToList());
        var events = Bocpd.Detect(switching, NormalGammaPrior.FromSample(switching.Take(50).ToList(), switchFloor), minVariance: switchFloor);
        Assert.That(events.Any(e => e.DetectedAt >= 599 && e.DetectedAt <= 599 + 30), Is.True,
            "detected: " + string.Join(", ", events.Select(e => e.DetectedAt)));
    }
}
