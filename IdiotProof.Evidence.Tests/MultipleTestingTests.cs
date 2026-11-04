using IdiotProof.Evidence.Search;

namespace IdiotProof.Evidence.Tests;

public class MultipleTestingTests
{
    [Test]
    public void NormalCdfAndQuantile_MatchKnownValues_AndInvertEachOther()
    {
        Assert.That(Distributions.NormalCdf(0), Is.EqualTo(0.5).Within(1e-7));
        Assert.That(Distributions.NormalCdf(1.959963984540054), Is.EqualTo(0.975).Within(1e-7));
        Assert.That(Distributions.NormalQuantile(0.975), Is.EqualTo(1.959963984540054).Within(1e-6));
        Assert.That(Distributions.NormalQuantile(0.001), Is.EqualTo(-3.090232306167813).Within(1e-6));
        foreach (var p in new[] { 0.01, 0.3, 0.5, 0.8, 0.999 })
            Assert.That(Distributions.NormalCdf(Distributions.NormalQuantile(p)), Is.EqualTo(p).Within(1e-6));
    }

    [Test]
    public void StudentT_MatchesTableValues()
    {
        // t(10) one-sided 5% critical value is 1.812; t(1) is Cauchy: P(T >= 1) = 0.25.
        Assert.That(Distributions.StudentTUpperTail(1.812461, 10), Is.EqualTo(0.05).Within(1e-5));
        Assert.That(Distributions.StudentTUpperTail(1, 1), Is.EqualTo(0.25).Within(1e-9));
        Assert.That(Distributions.StudentTUpperTail(0, 7), Is.EqualTo(0.5).Within(1e-12));
        Assert.That(Distributions.StudentTUpperTail(-1.812461, 10), Is.EqualTo(0.95).Within(1e-5));
    }

    [Test]
    public void BenjaminiHochberg_ReproducesThe1995PaperExample()
    {
        // Benjamini & Hochberg (1995), 15 p-values: at FDR 0.05 exactly the four smallest are rejected.
        var p = new[] { 0.0001, 0.0004, 0.0019, 0.0095, 0.0201, 0.0278, 0.0298, 0.0344, 0.0459, 0.3240, 0.4262, 0.5719, 0.6528, 0.7590, 1.0 };
        var q = MultipleTesting.BenjaminiHochberg(p);
        Assert.That(q.Count(v => v <= 0.05), Is.EqualTo(4));
        Assert.That(q[3], Is.EqualTo(0.0095 * 15 / 4).Within(1e-12));
        Assert.That(q[4], Is.EqualTo(0.0201 * 15 / 5).Within(1e-12));
        Assert.That(q[14], Is.EqualTo(1.0));
    }

    [Test]
    public void BenjaminiHochberg_KeepsInputOrder_AndIsMonotoneInP()
    {
        var p = new[] { 0.04, 0.01, 0.03, 0.005 };
        var q = MultipleTesting.BenjaminiHochberg(p);
        Assert.That(q, Is.EqualTo(new[] { 0.04, 0.02, 0.04, 0.02 }).Within(1e-12));
    }

    [Test]
    public void DeflatedSharpe_ShrinksAsMoreConfigsAreTried()
    {
        var rng = new Random(5);
        var returns = Enumerable.Range(0, 250).Select(_ => 0.1 + Synthetic.Gaussian(rng)).ToArray();
        var m = ReturnMoments.Of(returns);
        double one = MultipleTesting.DeflatedSharpe(m, 1, 0.01);
        double ten = MultipleTesting.DeflatedSharpe(m, 10, 0.01);
        double thousand = MultipleTesting.DeflatedSharpe(m, 1000, 0.01);
        Assert.That(one, Is.EqualTo(MultipleTesting.ProbabilisticSharpe(m, 0)).Within(1e-12));
        Assert.That(ten, Is.LessThan(one));
        Assert.That(thousand, Is.LessThan(ten));
        Assert.That(MultipleTesting.ExpectedMaxSharpe(1000, 0.01), Is.GreaterThan(MultipleTesting.ExpectedMaxSharpe(10, 0.01)));
    }

    [Test]
    public void ReturnMoments_NormalDataHasNoSkewAndKurtosisNearThree()
    {
        var rng = new Random(8);
        var m = ReturnMoments.Of(Enumerable.Range(0, 20000).Select(_ => Synthetic.Gaussian(rng)).ToArray());
        Assert.That(m.Skewness, Is.EqualTo(0).Within(0.05));
        Assert.That(m.Kurtosis, Is.EqualTo(3).Within(0.1));
    }

    [Test]
    public void MeanAboveZeroPValue_SeparatesAnEdgeFromNoise()
    {
        var rng = new Random(2);
        var edge = Enumerable.Range(0, 400).Select(_ => 0.3 + Synthetic.Gaussian(rng)).ToArray();
        var loss = Enumerable.Range(0, 400).Select(_ => -0.3 + Synthetic.Gaussian(rng)).ToArray();
        Assert.That(MultipleTesting.MeanAboveZeroPValue(edge), Is.LessThan(1e-6));
        Assert.That(MultipleTesting.MeanAboveZeroPValue(loss), Is.GreaterThan(0.99));
    }

    [Test]
    public void RealityCheck_ManyNoiseConfigs_BestIsNotSignificant()
    {
        // Each config alone has a 5% chance to look "significant"; across 50 the best one usually does, naively.
        var rng = new Random(21);
        var configs = Enumerable.Range(0, 50)
            .Select(_ => Enumerable.Range(0, 300).Select(_ => Synthetic.Gaussian(rng)).ToArray()).ToList();
        double bestNaive = configs.Min(MultipleTesting.MeanAboveZeroPValue);
        var rc = RealityCheck.Run(configs, resamples: 500, meanBlockLength: 3, seed: 1);
        Assert.That(bestNaive, Is.LessThan(0.05), "the naive best of 50 noise configs looks like an edge");
        Assert.That(rc.WhitePValue, Is.GreaterThan(0.05), "the reality check sees through it");
        Assert.That(rc.SpaPValue, Is.GreaterThan(0.05));
    }

    [Test]
    public void RealityCheck_PlantedEdgeAmongNoise_IsFound()
    {
        var rng = new Random(22);
        var configs = Enumerable.Range(0, 49)
            .Select(_ => Enumerable.Range(0, 300).Select(_ => Synthetic.Gaussian(rng)).ToArray()).ToList();
        configs.Add(Enumerable.Range(0, 300).Select(_ => 0.4 + Synthetic.Gaussian(rng)).ToArray());
        var rc = RealityCheck.Run(configs, resamples: 500, meanBlockLength: 3, seed: 1);
        Assert.That(rc.BestConfig, Is.EqualTo(49));
        Assert.That(rc.WhitePValue, Is.LessThan(0.01));
        Assert.That(rc.SpaPValue, Is.LessThan(0.01));
    }

    [Test]
    public void RealityCheck_IsDeterministicForASeed_AndRejectsMisalignedSeries()
    {
        var a = new[] { new[] { 0.1, -0.2, 0.3, 0.0 }, new[] { 0.0, 0.1, -0.1, 0.2 } };
        var r1 = RealityCheck.Run(a, 200, 2, seed: 9);
        var r2 = RealityCheck.Run(a, 200, 2, seed: 9);
        Assert.That(r1, Is.EqualTo(r2));
        Assert.Throws<ArgumentException>(() => RealityCheck.Run(new[] { new[] { 1.0, 2.0 }, new[] { 1.0 } }));
    }
}
