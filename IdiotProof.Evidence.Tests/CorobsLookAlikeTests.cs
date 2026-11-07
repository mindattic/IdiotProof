using IdiotProof.Evidence.Gappers;

namespace IdiotProof.Evidence.Tests;

/// <summary>
/// IP-T451 amendment 03: the Corobs look-alike detector. Rows near positive examples score high,
/// leave-one-day-out never lets a row see its own day, novelty grows with distance from the training
/// set, a constant or missing feature carries no information, and the same inputs give identical
/// scores.
/// </summary>
[TestFixture]
public class CorobsLookAlikeTests
{
    private static readonly string[] Features = ["a", "b", "c"];

    /// <summary>Positives sit in a band of feature a (0.4..0.6), a non-monotone pattern; b is noise; c is constant.</summary>
    private static (List<double[]> X, List<bool> Y, List<double> W, List<int> D) Planted(int n, int seed)
    {
        var rng = new Random(seed);
        var x = new List<double[]>(); var y = new List<bool>(); var w = new List<double>(); var d = new List<int>();
        for (int i = 0; i < n; i++)
        {
            double a = rng.NextDouble();
            x.Add([a, rng.NextDouble(), 7.0]);
            y.Add(a is > 0.4 and < 0.6);
            w.Add(i % 2 == 0 ? 1 : 10);
            d.Add(i / 20);
        }
        return (x, y, w, d);
    }

    [Test]
    public void RowsInsideThePositiveBand_ScoreHigher_ThanRowsOutsideIt()
    {
        var (x, y, w, d) = Planted(2000, 1);
        var model = CorobsLookAlike.Fit(Features, x, y, w, d, seed: 1337, k: 25);
        var s = model.Score([[0.5, 0.5, 7.0], [0.05, 0.5, 7.0], [0.95, 0.5, 7.0]]);
        Assert.Multiple(() =>
        {
            Assert.That(model.Dimensions, Is.EqualTo(3 * CorobsLookAlike.DimensionsPerPly));
            Assert.That(s[0].Score, Is.GreaterThan(0.6), "inside the band");
            Assert.That(s[1].Score, Is.LessThan(0.2), "below it");
            Assert.That(s[2].Score, Is.LessThan(0.2), "above it");
            Assert.That(s.All(c => c.Neighbours == 25));
            Assert.That(model.Ranges[2].Low, Is.EqualTo(model.Ranges[2].High), "a constant feature has an empty range and converts as missing");
        });
    }

    [Test]
    public void SameInputs_GiveIdenticalScores_AndADifferentSeedDoesNot()
    {
        var (x, y, w, d) = Planted(600, 2);
        var a = CorobsLookAlike.Fit(Features, x, y, w, d, seed: 1337, k: 20).Score(x, d);
        var b = CorobsLookAlike.Fit(Features, x, y, w, d, seed: 1337, k: 20).Score(x, d);
        var c = CorobsLookAlike.Fit(Features, x, y, w, d, seed: 99, k: 20).Score(x, d);
        Assert.Multiple(() =>
        {
            Assert.That(a.Select(v => v.Score), Is.EqualTo(b.Select(v => v.Score)));
            Assert.That(a.Select(v => v.Novelty), Is.EqualTo(b.Select(v => v.Novelty)));
            Assert.That(a.Select(v => v.Novelty), Is.Not.EqualTo(c.Select(v => v.Novelty)), "the seed draws the string COs");
        });
    }

    [Test]
    public void LeaveOneDayOut_NeverCountsTheRowsOwnDay()
    {
        // Two days; every row of day 0 is a positive at a = 0.2, every row of day 1 a negative at a = 0.8.
        var x = new List<double[]>(); var y = new List<bool>(); var w = new List<double>(); var d = new List<int>();
        for (int i = 0; i < 40; i++) { x.Add([i < 20 ? 0.2 : 0.8, 0.5, 1.0 + i]); y.Add(i < 20); w.Add(1); d.Add(i < 20 ? 0 : 1); }
        var model = CorobsLookAlike.Fit(Features, x, y, w, d, seed: 1, k: 5, priorWeight: 0);
        var loo = model.Score(x, d);
        var open = model.Score(x);
        Assert.Multiple(() =>
        {
            Assert.That(open[0].Score, Is.EqualTo(1.0), "without exclusion a day-0 row finds its own day's positives");
            Assert.That(loo[0].Score, Is.EqualTo(0.0), "with exclusion only day 1 (all negatives) is left");
            Assert.That(loo[39].Score, Is.EqualTo(1.0));
        });
    }

    [Test]
    public void Novelty_GrowsWithDistanceFromTheTrainingSet_AndShrinkageUsesTheBaseRate()
    {
        var (x, y, w, d) = Planted(1000, 3);
        var narrow = x.Select(r => new[] { 0.3 + 0.4 * r[0], r[1], 7.0 }).ToList();
        var model = CorobsLookAlike.Fit(Features, narrow, y, w, d, seed: 5, k: 10, priorWeight: 1e9);
        var s = model.Score([[0.5, 0.5, 7.0], [0.7, 1.0, 7.0]]);
        Assert.Multiple(() =>
        {
            Assert.That(s[0].Novelty, Is.LessThan(s[1].Novelty), "a corner of the range is farther from its nearest training rows than the middle");
            Assert.That(s[0].Score, Is.EqualTo(model.BaseRate).Within(1e-6), "an overwhelming prior pulls every score to the base rate");
        });
    }

    [Test]
    public void WeightedQuantile_CountsWeights()
    {
        var v = new List<(double, double)> { (1, 1), (2, 1), (3, 8) };
        Assert.Multiple(() =>
        {
            Assert.That(CorobsLookAlike.WeightedQuantile(v, 0.1), Is.EqualTo(1));
            Assert.That(CorobsLookAlike.WeightedQuantile(v, 0.2), Is.EqualTo(2));
            Assert.That(CorobsLookAlike.WeightedQuantile(v, 0.5), Is.EqualTo(3), "the weight-8 value holds most of the mass");
            Assert.That(double.IsNaN(CorobsLookAlike.WeightedQuantile([], 0.5)));
        });
    }
}
