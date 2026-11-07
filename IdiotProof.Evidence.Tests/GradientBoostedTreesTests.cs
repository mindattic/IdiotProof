using IdiotProof.Evidence.Evaluation;

namespace IdiotProof.Evidence.Tests;

/// <summary>
/// IP-T453: the pure boosted-tree learner finds a non-monotone band a logistic fit cannot, learns
/// where missing values belong, honours row weights, ranks the informative feature first and gives
/// the same model for the same inputs.
/// </summary>
[TestFixture]
public class GradientBoostedTreesTests
{
    private static (float[] X, bool[] Y, double[] W) Band(int n, int seed, Func<int, double>? weight = null)
    {
        var rng = new Random(seed);
        var x = new float[n * 2]; var y = new bool[n]; var w = new double[n];
        for (int i = 0; i < n; i++)
        {
            float a = (float)rng.NextDouble(), b = (float)rng.NextDouble();
            x[i * 2] = a; x[i * 2 + 1] = b;
            y[i] = a is > 0.4f and < 0.6f ? rng.NextDouble() < 0.9 : rng.NextDouble() < 0.05;
            w[i] = weight?.Invoke(i) ?? 1;
        }
        return (x, y, w);
    }

    private static double Auc(BoostedTreeModel m, float[] x, bool[] y)
    {
        var scores = Enumerable.Range(0, y.Length).Select(i => m.Predict(x.AsSpan(i * 2, 2))).ToList();
        return Auc_(scores, y);
    }

    private static double Auc_(IReadOnlyList<double> s, bool[] y) => WeightedAuc.Compute(s, y.ToList(), y.Select(_ => 1.0).ToList());

    [Test]
    public void FindsANonMonotoneBand_ThatALogisticFitCannot()
    {
        var (x, y, w) = Band(4000, 1);
        var model = GradientBoostedTrees.Fit(["a", "b"], x, y, w, new BoostingOptions { Trees = 60, MaxDepth = 3, LearningRate = 0.2 });
        var (tx, ty, _) = Band(2000, 2);
        var logistic = LogisticRegression.FitWeighted(["a", "b"], Enumerable.Range(0, 4000).Select(i => new double[] { x[i * 2], x[i * 2 + 1] }).ToList(), y.ToList(), w.ToList());
        var logisticAuc = Auc_(Enumerable.Range(0, 2000).Select(i => logistic.Predict(new double[] { tx[i * 2], tx[i * 2 + 1] })).ToList(), ty);
        Assert.Multiple(() =>
        {
            // The label noise (10% inside the band, 5% outside) caps any ranking at about 0.88.
            Assert.That(Auc(model, tx, ty), Is.GreaterThan(0.85), "out of sample, near the noise ceiling");
            Assert.That(logisticAuc, Is.LessThan(0.65), "a line cannot draw a band");
            Assert.That(model.Importance()[0].Feature, Is.EqualTo("a"));
            Assert.That(model.TreeCount, Is.EqualTo(60));
        });
    }

    [Test]
    public void SameInputs_SameModel()
    {
        var (x, y, w) = Band(1500, 3);
        var o = new BoostingOptions { Trees = 30 };
        var a = GradientBoostedTrees.Fit(["a", "b"], x, y, w, o);
        var b = GradientBoostedTrees.Fit(["a", "b"], x, y, w, o);
        var probe = new float[] { 0.5f, 0.3f };
        Assert.Multiple(() =>
        {
            Assert.That(a.Predict(probe), Is.EqualTo(b.Predict(probe)));
            Assert.That(a.Gain, Is.EqualTo(b.Gain));
        });
    }

    [Test]
    public void MissingValues_GoWhereTheirLabelsSay()
    {
        // Feature a is missing on every positive row and present (random) on every negative row.
        var rng = new Random(4);
        int n = 2000;
        var x = new float[n]; var y = new bool[n]; var w = new double[n];
        for (int i = 0; i < n; i++) { y[i] = i % 4 == 0; x[i] = y[i] ? float.NaN : (float)rng.NextDouble(); w[i] = 1; }
        var model = GradientBoostedTrees.Fit(["a"], x, y, w, new BoostingOptions { Trees = 40, LearningRate = 0.3 });
        Assert.Multiple(() =>
        {
            Assert.That(model.Predict([float.NaN]), Is.GreaterThan(0.8));
            Assert.That(model.Predict([0.5f]), Is.LessThan(0.2));
        });
    }

    [Test]
    public void RowWeights_MoveTheBaseRate()
    {
        var x = new float[100]; var y = new bool[100];
        for (int i = 0; i < 100; i++) { x[i] = 0.5f; y[i] = i < 50; }
        var even = GradientBoostedTrees.Fit(["a"], x, y, Enumerable.Repeat(1.0, 100).ToArray(), new BoostingOptions { Trees = 1 });
        var heavy = GradientBoostedTrees.Fit(["a"], x, y, Enumerable.Range(0, 100).Select(i => i < 50 ? 1.0 : 9.0).ToArray(), new BoostingOptions { Trees = 1 });
        Assert.Multiple(() =>
        {
            Assert.That(even.Predict([0.5f]), Is.EqualTo(0.5).Within(1e-6));
            Assert.That(heavy.Predict([0.5f]), Is.EqualTo(0.1).Within(1e-6), "negatives weighted 9:1");
        });
    }
}
