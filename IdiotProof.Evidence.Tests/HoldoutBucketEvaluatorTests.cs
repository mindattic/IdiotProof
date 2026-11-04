using IdiotProof.Evidence.Evaluation;

namespace IdiotProof.Evidence.Tests;

public class HoldoutBucketEvaluatorTests
{
    private static readonly DateOnly Day0 = new(2026, 5, 4);

    /// <summary>Feature values carry no information about the label: up is a coin flip everywhere.</summary>
    private static FeatureSamples Noise(string name, Random rng, int days, int perDay, int dayOffset)
    {
        var obs = new List<BucketObservation>(days * perDay);
        for (int d = 0; d < days; d++)
            for (int i = 0; i < perDay; i++)
                obs.Add(new BucketObservation(Day0.AddDays(dayOffset + d), rng.NextDouble(), rng.NextDouble() < 0.5));
        return new FeatureSamples(name, Array.Empty<BucketObservation>(), obs);
    }

    private static IReadOnlyList<FeatureSamples> NoiseFeatures(int seed, int features)
    {
        var rng = new Random(seed);
        return Enumerable.Range(0, features).Select(f =>
        {
            var train = Noise($"f{f}", rng, days: 15, perDay: 40, dayOffset: 0);
            var hold = Noise($"f{f}", rng, days: 15, perDay: 40, dayOffset: 30);
            return new FeatureSamples(train.Feature, hold.Holdout.Select(o => o with { Day = o.Day.AddDays(-30) }).ToList(), hold.Holdout);
        }).ToList();
    }

    [Test]
    public void PureNoise_ManyBuckets_BhLabelsStayUnderAlpha_WhileRawPLabelsDoNot()
    {
        const double alpha = 0.05;
        int seeds = 40, bhFamilies = 0, rawFamilies = 0;
        for (int seed = 1; seed <= seeds; seed++)
        {
            var report = HoldoutBucketEvaluator.Evaluate(NoiseFeatures(seed, features: 10), bins: 5, alpha: alpha, resamples: 300, seed: seed);
            Assert.That(report.Tests, Is.EqualTo(50));
            if (report.AnySignificant) bhFamilies++;
            if (report.AllBuckets.Any(b => b.BinomialP < alpha)) rawFamilies++;
        }
        double bhRate = (double)bhFamilies / seeds, rawRate = (double)rawFamilies / seeds;
        Assert.That(bhRate, Is.LessThanOrEqualTo(alpha), $"BH any-significant rate {bhRate:P0}");
        Assert.That(rawRate, Is.GreaterThan(alpha), $"raw-p any-significant rate {rawRate:P0}");
        Assert.That(rawRate, Is.GreaterThan(0.5), "50 uncorrected tests should find a lucky bucket most of the time");
    }

    [Test]
    public void PlantedEdge_StillPasses_AndNoiseFeaturesDoNot()
    {
        var rng = new Random(77);
        var features = NoiseFeatures(77, features: 5).ToList();
        List<BucketObservation> Planted(int offset)
        {
            var obs = new List<BucketObservation>();
            for (int d = 0; d < 15; d++)
                for (int i = 0; i < 100; i++)
                {
                    double v = rng.NextDouble();
                    obs.Add(new BucketObservation(Day0.AddDays(offset + d), v, rng.NextDouble() < (v > 0.8 ? 0.8 : 0.45)));
                }
            return obs;
        }
        features.Add(new FeatureSamples("planted", Planted(0), Planted(30)));

        var report = HoldoutBucketEvaluator.Evaluate(features, bins: 5, resamples: 1000);
        var planted = report.Features.Single(f => f.Feature == "planted");
        Assert.That(planted.Buckets[^1].Significant, Is.True, $"q={planted.Buckets[^1].Q:G3}");
        Assert.That(planted.Buckets[^1].Lift, Is.GreaterThan(1.3));
        Assert.That(report.Features.Where(f => f.Feature != "planted").SelectMany(f => f.Buckets).Any(b => b.Significant), Is.False);
    }

    [Test]
    public void OneHugeDay_CannotCarrySignificance_ByItself()
    {
        var rng = new Random(5);
        var train = new List<BucketObservation>();
        var hold = new List<BucketObservation>();
        for (int d = 0; d < 10; d++)
            for (int i = 0; i < 30; i++)
            {
                train.Add(new BucketObservation(Day0.AddDays(d), rng.NextDouble(), rng.NextDouble() < 0.5));
                hold.Add(new BucketObservation(Day0.AddDays(30 + d), rng.NextDouble(), rng.NextDouble() < 0.5));
            }
        // One market-wide day: 400 bars in the top bucket, every one up.
        for (int i = 0; i < 400; i++) hold.Add(new BucketObservation(Day0.AddDays(45), 0.99, true));

        var report = HoldoutBucketEvaluator.Evaluate(new[] { new FeatureSamples("oneday", train, hold) }, bins: 2, resamples: 2000);
        var top = report.Features[0].Buckets[^1];
        Assert.That(top.BinomialP, Is.LessThan(1e-6), "counted bar by bar it looks overwhelming");
        Assert.That(top.Significant, Is.False, $"one day is one sample: clustered p={top.DayClusteredP:G3}");
    }

    [Test]
    public void TooFewHoldoutDays_IsNeverSignificant()
    {
        var train = Enumerable.Range(0, 200).Select(i => new BucketObservation(Day0, i / 200.0, i % 2 == 0)).ToList();
        var hold = Enumerable.Range(0, 200).Select(i => new BucketObservation(Day0.AddDays(1 + i / 100), 0.9, true)).ToList();
        var report = HoldoutBucketEvaluator.Evaluate(new[] { new FeatureSamples("twodays", train, hold) }, bins: 2, minDays: 5);
        Assert.That(report.AnySignificant, Is.False);
    }

    [Test]
    public void Categories_UseTheValueAsTheBucket()
    {
        var obs = new List<BucketObservation>();
        for (int d = 0; d < 20; d++)
            for (int i = 0; i < 30; i++)
            {
                int cat = i % 3;
                obs.Add(new BucketObservation(Day0.AddDays(d), cat, cat == 2 ? i % 10 != 0 : i % 2 == 0));
            }
        var report = HoldoutBucketEvaluator.Evaluate(new[] { new FeatureSamples("cat", obs, obs, Categories: 3) });
        var f = report.Features[0];
        Assert.That(f.Bins, Is.EqualTo(3));
        Assert.That(f.Buckets.Select(b => b.HoldoutSamples), Is.All.EqualTo(200));
        Assert.That(f.Buckets[2].Significant, Is.True);
    }

    [Test]
    public void TwoSidedBinomialP_MatchesExactBinomial()
    {
        // 8 of 10 at p0 = 0.5: upper tail 56/1024, doubled.
        Assert.That(HoldoutBucketEvaluator.TwoSidedBinomialP(8, 10, 0.5), Is.EqualTo(112.0 / 1024).Within(1e-12));
        Assert.That(HoldoutBucketEvaluator.TwoSidedBinomialP(5, 10, 0.5), Is.EqualTo(1.0));
        Assert.That(HoldoutBucketEvaluator.TwoSidedBinomialP(0, 0, 0.5), Is.EqualTo(1.0));
    }
}
