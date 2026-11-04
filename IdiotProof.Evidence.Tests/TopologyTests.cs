using IdiotProof.Evidence.Topology;

namespace IdiotProof.Evidence.Tests;

public class TopologyTests
{
    private static double[][] Circle(int n, double radius = 1)
    {
        var pts = new double[n][];
        for (int i = 0; i < n; i++)
        {
            double a = 2 * Math.PI * i / n;
            pts[i] = [radius * Math.Cos(a), radius * Math.Sin(a)];
        }
        return pts;
    }

    [Test]
    public void Circle_HasExactlyOneLongLivedLoop()
    {
        var bars = PersistentHomology.Compute(Circle(30));
        var h1 = bars.Where(b => b.Dimension == 1).ToList();
        Assert.That(h1, Is.Not.Empty);
        var longest = h1.MaxBy(b => b.Persistence);
        // Born when neighbors connect (2·sin(π/30) ≈ 0.209), dies once a triangle spans the center (≈ √3).
        Assert.That(longest.Birth, Is.EqualTo(2 * Math.Sin(Math.PI / 30)).Within(1e-9));
        Assert.That(longest.Death, Is.GreaterThan(1.6).And.LessThanOrEqualTo(2.0));
        Assert.That(h1.Count(b => b.Persistence > 0.5), Is.EqualTo(1), "only one significant loop");
    }

    [Test]
    public void Blobs_H0LongBarsMatchClusterCount()
    {
        var rng = new Random(3);
        var centers = new[] { (0.0, 0.0), (10.0, 0.0), (0.0, 10.0), (10.0, 10.0) };
        var pts = new List<double[]>();
        foreach (var (x, y) in centers)
            for (int i = 0; i < 10; i++) pts.Add([x + 0.3 * rng.NextDouble(), y + 0.3 * rng.NextDouble()]);
        var h0 = PersistentHomology.Compute(pts, includeH1: false).Where(b => b.Dimension == 0).ToList();
        Assert.That(h0.Count(b => b.IsInfinite), Is.EqualTo(1));
        Assert.That(h0.Count(b => b.Persistence > 5), Is.EqualTo(centers.Length), "3 long finite bars + 1 infinite = 4 clusters");
    }

    [Test]
    public void TwoPoints_OneMergeAtTheirDistance()
    {
        var bars = PersistentHomology.Compute(new[] { new[] { 0.0, 0.0 }, new[] { 3.0, 4.0 } });
        Assert.That(bars.Single(b => b.Dimension == 0 && !b.IsInfinite).Death, Is.EqualTo(5).Within(1e-12));
        Assert.That(bars.Any(b => b.Dimension == 1), Is.False);
    }

    [Test]
    public void Embedding_HasTheRightShape()
    {
        var pts = DelayEmbedding.Embed(new double[] { 1, 2, 3, 4, 5, 6 }, dimension: 3, delay: 2);
        Assert.That(pts, Has.Length.EqualTo(2));
        Assert.That(pts[0], Is.EqualTo(new double[] { 1, 3, 5 }));
        Assert.That(pts[1], Is.EqualTo(new double[] { 2, 4, 6 }));
    }

    [Test]
    public void SineWaveEmbedding_TracesALoop_NoiseDoesNot()
    {
        var sine = Enumerable.Range(0, 48).Select(i => Math.Sin(2 * Math.PI * i / 16)).ToArray();
        var loop = PersistentHomology.Compute(DelayEmbedding.Embed(sine, 2, 4));
        double sineH1 = PersistenceLandscape.MaxPersistence(loop, 1);
        Assert.That(sineH1, Is.GreaterThan(0.8), "sin(t) vs sin(t + π/2) is a circle");

        var rng = new Random(9);
        var noise = Enumerable.Range(0, 48).Select(_ => rng.NextDouble() * 2 - 1).ToArray();
        double noiseH1 = PersistenceLandscape.MaxPersistence(PersistentHomology.Compute(DelayEmbedding.Embed(noise, 2, 4)), 1);
        Assert.That(sineH1, Is.GreaterThan(2 * noiseH1));
    }

    [Test]
    public void Landscape_SingleBarIsATent()
    {
        var bars = new[] { new PersistenceBar(1, 0, 2) };
        var (l1, l2) = PersistenceLandscape.Norms(bars, 1, levels: 1, samples: 2001);
        Assert.That(l1, Is.EqualTo(1.0).Within(1e-3), "area of a tent with half-width 1 and height 1");
        Assert.That(l2, Is.EqualTo(Math.Sqrt(2.0 / 3)).Within(1e-3));
        Assert.That(PersistenceLandscape.TotalPersistence(bars, 1), Is.EqualTo(2));
    }

    [Test]
    public void Features_HaveNoLookahead()
    {
        var rng = new Random(4);
        var a = new List<double> { 3 };
        for (int i = 1; i < 120; i++) a.Add(a[^1] * (1 + 0.01 * (rng.NextDouble() - 0.5)));
        var b = a.ToList();
        for (int i = 61; i < b.Count; i++) b[i] *= 5;
        var s = new TopologySettings(Window: 40);
        var ra = TopologyFeatures.At(a, 60, s)!;
        var rb = TopologyFeatures.At(b, 60, s)!;
        Assert.That(rb, Is.EqualTo(ra), "bars after the reading's index must not change it");
        Assert.That(TopologyFeatures.At(a, 38, s), Is.Null, "a window that would start before bar 0 is not read");
    }

    [Test]
    public void Evaluator_OnPureNoise_PromotesNothing()
    {
        var rng = new Random(21);
        var sessions = new List<SessionSeries>();
        var day = new DateOnly(2026, 6, 1);
        for (int d = 0; d < 20; d++)
        {
            var closes = new List<double> { 3 };
            for (int i = 1; i < 200; i++) closes.Add(closes[^1] * (1 + 0.004 * Synthetic.Gaussian(rng)));
            sessions.Add(new SessionSeries(day.AddDays(d), closes));
        }
        var result = TopologyEvaluator.Evaluate(sessions, new TopologySettings(Window: 30), horizon: 5, minBucketSamples: 20);
        Assert.That(result.Features, Is.Not.Empty);
        Assert.That(result.Features.Any(f => f.Promoted), Is.False,
            string.Join("; ", result.Features.Select(f => $"{f.Feature} p={f.BinomialP:G3}")));
        Assert.That(result.TrainSessions + result.HoldoutSessions, Is.EqualTo(20));
    }

    [Test]
    public void Evaluator_PromotesAPlantedEdge_ThatTopologyCanSee()
    {
        // Whenever the recent returns oscillate (a loop in delay space), the next 5 bars rise; otherwise
        // they wander. H1 persistence should see the oscillation and the holdout should confirm it.
        var rng = new Random(33);
        var sessions = new List<SessionSeries>();
        var day = new DateOnly(2026, 6, 1);
        for (int d = 0; d < 30; d++)
        {
            var closes = new List<double> { 3 };
            int i = 1;
            while (closes.Count < 300)
            {
                bool oscillate = rng.NextDouble() < 0.5;
                for (int k = 0; k < 30 && closes.Count < 300; k++, i++)
                {
                    double r = oscillate ? 0.01 * Math.Sin(2 * Math.PI * k / 6) : 0.006 * Synthetic.Gaussian(rng);
                    closes.Add(closes[^1] * (1 + r));
                }
                double drift = oscillate ? 0.004 : 0.004 * Synthetic.Gaussian(rng);
                for (int k = 0; k < 5 && closes.Count < 300; k++) closes.Add(closes[^1] * (1 + drift + 0.0005 * Synthetic.Gaussian(rng)));
            }
            sessions.Add(new SessionSeries(day.AddDays(d), closes));
        }
        var result = TopologyEvaluator.Evaluate(sessions, new TopologySettings(Window: 30, Dimension: 2, Delay: 2), horizon: 5, minBucketSamples: 20);
        Assert.That(result.Features.Any(f => f.Promoted && f.UpBucket), Is.True,
            string.Join("; ", result.Features.Select(f => $"{f.Feature} hold {f.HoldoutHits}/{f.HoldoutSamples} vs {f.HoldoutBaseRate:P1} p={f.BinomialP:G3}")));
    }
}
