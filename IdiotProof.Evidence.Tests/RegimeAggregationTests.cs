using IdiotProof.Evidence.Regimes;

namespace IdiotProof.Evidence.Tests;

/// <summary>IP-T437: regime models fit on bars long enough that the 1-cent grid isn't the signal.</summary>
public class RegimeAggregationTests
{
    /// <summary>Latent log-random-walk printed on a 1-cent grid; sd per bar switches at <paramref name="switchAt"/>.</summary>
    private static List<decimal> Prices(int bars, double start, double sd, int seed, int switchAt = int.MaxValue, double sdAfter = 0)
    {
        var rng = new Random(seed);
        double latent = start;
        var prices = new List<decimal>(bars);
        for (int i = 0; i < bars; i++)
        {
            latent *= Math.Exp((i < switchAt ? sd : sdAfter) * Synthetic.Gaussian(rng));
            prices.Add(Math.Max(0.01m, Math.Round((decimal)latent, 2)));
        }
        return prices;
    }

    private static List<double> Returns(IReadOnlyList<decimal> prices, int k)
    {
        var r = new List<double>();
        for (int i = k; i < prices.Count; i += k) r.Add(Math.Log((double)(prices[i] / prices[i - k])));
        return r;
    }

    [Test]
    public void QuietPennyStock_IsFitOnLongerBars()
    {
        // $0.80, latent sd 0.6%/minute: a cent is 1.25%, so many single minutes print no change.
        var quiet = Prices(6000, 0.80, 0.006, seed: 1);
        int k = TickNoise.ChooseAggregation(new[] { (IReadOnlyList<decimal>)quiet });

        Assert.That(k, Is.GreaterThan(1));
        Assert.That(k, Is.LessThan(30), "a qualifying multiple exists, so the fallback must not be used");
        double ZeroShare(int m) { var r = Returns(quiet, m); return r.Count(x => x == 0) / (double)r.Count; }
        Assert.That(ZeroShare(k), Is.LessThanOrEqualTo(0.25));
        Assert.That(ZeroShare(1), Is.GreaterThan(0.25), "raw minutes would have been dominated by no-tick bars");
    }

    [Test]
    public void NearlyFrozenStock_FallsBackToTheLongestBar()
    {
        // 0.05%/minute on $0.80: even 30-minute bars rarely move a cent; the longest candidate is the best available.
        var frozen = Prices(6000, 0.80, 0.0005, seed: 6);
        Assert.That(TickNoise.ChooseAggregation(new[] { (IReadOnlyList<decimal>)frozen }), Is.EqualTo(30));
    }

    [Test]
    public void LiquidStock_StaysOnMinuteBars()
    {
        // $50 with 0.3%/minute moves: a cent is 0.02%, far below the volatility.
        var liquid = Prices(3000, 50, 0.003, seed: 2);
        Assert.That(TickNoise.ChooseAggregation(new[] { (IReadOnlyList<decimal>)liquid }), Is.EqualTo(1));
    }

    [Test]
    public void Aggregation_StaysInsideEachSession()
    {
        // Two sessions with a large overnight gap: a cross-session return would be a huge outlier.
        var day1 = Prices(400, 2.00, 0.004, seed: 3);
        var day2 = Prices(400, 4.00, 0.004, seed: 4);
        int k = TickNoise.ChooseAggregation(new[] { (IReadOnlyList<decimal>)day1, day2 });
        Assert.That(k, Is.LessThan(30), "the 2x overnight gap must not inflate the variance test");
    }

    [Test]
    public void SubPennyPrints_DoNotShrinkTheFloorBelowTheQuotedCent()
    {
        // A $3.77 stock quoted in cents whose minute closes include midpoint prints half a cent apart.
        var prices = new List<decimal>();
        for (int i = 0; i < 400; i++) prices.Add(3.77m + (i % 4 == 1 ? 0.005m : i % 4 == 3 ? 0.01m : 0m));
        double floor = TickNoise.ReturnVarianceFloor(prices);
        Assert.That(TickNoise.EstimateTick(prices), Is.EqualTo(0.005m));
        Assert.That(floor, Is.EqualTo(TickNoise.ReturnVarianceFloor(0.01, 3.775)).Within(1e-9 * floor), "a cent at the series' median price");
        Assert.That(TickNoise.QuoteIncrement(0.80m), Is.EqualTo(0.0001m));
    }

    [Test]
    public void Hmm_OnTheChosenBars_FindsPersistentRegimes_NotTickFlipping()
    {
        var prices = Prices(12000, 0.80, 0.0015, seed: 5, switchAt: 6000, sdAfter: 0.006);
        int k = TickNoise.ChooseAggregation(new[] { (IReadOnlyList<decimal>)prices });
        var returns = Returns(prices, k);
        var hmm = GaussianHmm.Fit(returns, 2, minVariance: TickNoise.ReturnVarianceFloor(prices));

        Assert.That(k, Is.GreaterThan(1));
        Assert.That(Math.Min(hmm.Persistence(0), hmm.Persistence(1)), Is.GreaterThan(0.8),
            $"k={k}: calm {hmm.States[0].StdDev:P3} stay {hmm.Persistence(0):0.00}, volatile {hmm.States[1].StdDev:P3} stay {hmm.Persistence(1):0.00}");
        Assert.That(hmm.States[1].StdDev / hmm.States[0].StdDev, Is.GreaterThan(2));
    }
}
