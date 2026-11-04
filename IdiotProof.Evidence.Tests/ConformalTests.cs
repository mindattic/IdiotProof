using IdiotProof.Evidence.Conformal;

namespace IdiotProof.Evidence.Tests;

public class ConformalTests
{
    [Test]
    public void Quantile_UsesTheFiniteSampleRank_AndIsInfiniteWithTooFewScores()
    {
        var scores = Enumerable.Range(1, 9).Select(i => (double)i).ToList();
        // n = 9, α = 0.2: rank ⌈10 · 0.8⌉ = 8.
        Assert.That(ConformalMath.Quantile(scores, 0.2), Is.EqualTo(8));
        // n = 5, α = 0.1: rank ⌈6 · 0.9⌉ = 6 > 5, so no finite threshold can promise 90%.
        Assert.That(ConformalMath.Quantile(scores.Take(5).ToList(), 0.1), Is.EqualTo(double.PositiveInfinity));
        Assert.That(ConformalMath.Quantile(scores, 0), Is.EqualTo(double.PositiveInfinity));
    }

    [Test]
    public void SplitConformal_CoversAtLeastNominal_OnExchangeableData_AcrossSeeds()
    {
        const double alpha = 0.1;
        var coverages = new List<double>();
        for (int seed = 1; seed <= 20; seed++)
        {
            var rng = new Random(seed);
            var cp = new SplitConformal(alpha);
            for (int i = 0; i < 200; i++) cp.Add(0, 2 * Synthetic.Gaussian(rng));
            int covered = 0;
            const int test = 2000;
            for (int i = 0; i < test; i++)
                if (cp.Interval(0).Contains(2 * Synthetic.Gaussian(rng))) covered++;
            coverages.Add((double)covered / test);
        }
        // Expected coverage is ⌈201 · 0.9⌉ / 201 = 0.9005; the seed-to-seed spread is about 0.02.
        Assert.That(coverages.Average(), Is.GreaterThanOrEqualTo(0.89), string.Join(", ", coverages.Select(c => c.ToString("0.000"))));
        Assert.That(coverages.Min(), Is.GreaterThan(0.84));
    }

    [Test]
    public void AdaptiveConformal_RestoresCoverage_AfterAVarianceShift_WhereSplitUndercovers()
    {
        var rng = new Random(42);
        var t0 = new DateTime(2026, 8, 3, 13, 30, 0, DateTimeKind.Utc);
        var points = new List<ConformalPoint>();
        for (int i = 0; i < 4000; i++)
        {
            double sigma = i < 2000 ? 1 : 3;
            var at = t0.AddMinutes(i);
            points.Add(new ConformalPoint(DateOnly.FromDateTime(at), at, at.AddSeconds(30), 0, sigma * Synthetic.Gaussian(rng)));
        }

        var results = ConformalRun.Run(points, alpha: 0.1, gamma: 0.02);
        var after = ConformalSummary.Of(results.Skip(2000).ToList());

        Assert.That(after.SplitCoverage, Is.LessThan(0.85), "split conformal trusts the calm history and undercovers");
        // ACI bound over any 2000 outcomes: |miss rate − α| ≤ (1 + 2γ) / (γ · 2000) ≈ 0.026.
        Assert.That(after.AdaptiveCoverage, Is.GreaterThanOrEqualTo(0.87));
    }

    [Test]
    public void Run_NeverCalibratesOnAnOutcomeBeforeItResolves()
    {
        var t0 = new DateTime(2026, 8, 3, 13, 30, 0, DateTimeKind.Utc);
        var day = DateOnly.FromDateTime(t0);
        // Ten calls a minute apart whose outcomes all resolve an hour later: none can calibrate another.
        var points = Enumerable.Range(0, 10).Select(i => new ConformalPoint(day, t0.AddMinutes(i), t0.AddHours(1), 0, 0.01 * (i + 1))).ToList();

        var results = ConformalRun.Run(points, alpha: 0.1, gamma: 0.01);

        Assert.That(results.All(r => !r.Split.IsFinite && !r.Adaptive.IsFinite), Is.True);
        Assert.That(results.All(r => r.Call == Direction.Flat), Is.True);
    }

    [Test]
    public void AbstainRule_OnPureNoise_IsNotSignificant_ButFindsAPlantedEdge()
    {
        static List<ConformalPoint> Days(int seed, double signal)
        {
            var rng = new Random(seed);
            var points = new List<ConformalPoint>();
            var start = new DateTime(2026, 8, 3, 13, 30, 0, DateTimeKind.Utc);
            for (int d = 0; d < 60; d++)
            for (int i = 0; i < 50; i++)
            {
                var at = start.AddDays(d).AddMinutes(i * 5);
                double driver = Synthetic.Gaussian(rng);
                double forecast = driver;
                double outcome = signal * driver + Synthetic.Gaussian(rng);
                points.Add(new ConformalPoint(DateOnly.FromDateTime(start.AddDays(d)), at, at.AddMinutes(4), forecast, outcome));
            }
            return points;
        }

        foreach (var seed in new[] { 3, 7, 11 })
        {
            var noise = ConformalSummary.Of(ConformalRun.Run(Days(seed, signal: 0), 0.5, 0.005));
            var clustered = ClusteredSignificance.Evaluate(noise.DayTallies);
            Assert.That(clustered.IsSignificant(), Is.False, $"seed {seed}: {noise.Abstain}");
        }

        var edge = ConformalSummary.Of(ConformalRun.Run(Days(5, signal: 3), 0.5, 0.005));
        Assert.That(ClusteredSignificance.Evaluate(edge.DayTallies).IsSignificant(), Is.True, edge.Abstain.ToString());
        Assert.That(edge.Abstain.Coverage, Is.LessThan(1), "the rule abstains when the interval straddles zero");
    }
}
