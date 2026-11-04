using IdiotProof.Evidence.Information;
using IdiotProof.Evidence.Leaders;
using IdiotProof.Models;

namespace IdiotProof.Evidence.Tests;

public class TransferEntropyTests
{
    private static (double[] X, double[] Y) Squared(int n, int seed)
    {
        var x = Synthetic.RandomWalkReturns(n, seed);
        var rng = new Random(seed + 1);
        var y = new double[n];
        for (int t = 1; t < n; t++) y[t] = 50 * (x[t - 1] * x[t - 1] - 1e-4) + 0.001 * Synthetic.Gaussian(rng);
        return (x, y);
    }

    [Test]
    public void Entropy_UniformAndConstant()
    {
        Assert.That(TransferEntropy.Entropy(new[] { 0, 1, 2, 3 }), Is.EqualTo(2).Within(1e-12));
        Assert.That(TransferEntropy.Entropy(new[] { 5, 5, 5 }), Is.EqualTo(0).Within(1e-12));
    }

    [Test]
    public void MutualInformation_IdenticalIsEntropy_IndependentIsZero()
    {
        var x = new[] { 0, 1, 0, 1 };
        Assert.That(TransferEntropy.MutualInformation(x, x), Is.EqualTo(1).Within(1e-12));
        Assert.That(TransferEntropy.MutualInformation(new[] { 0, 0, 1, 1 }, new[] { 0, 1, 0, 1 }), Is.EqualTo(0).Within(1e-12));
    }

    [Test]
    public void Compute_ACopiedSeriesCarriesItsFullEntropy()
    {
        var rng = new Random(5);
        var x = Enumerable.Range(0, 4000).Select(_ => rng.Next(2)).ToArray();
        var y = new int[x.Length];
        for (int t = 1; t < x.Length; t++) y[t] = x[t - 1];
        Assert.That(TransferEntropy.Compute(x, y), Is.EqualTo(1).Within(0.01));
        Assert.That(TransferEntropy.Compute(y, x), Is.LessThan(0.01));
    }

    [Test]
    public void Test_DetectsANonlinearLead_ThatLagOneCorrelationMisses()
    {
        var (x, y) = Squared(3000, seed: 41);
        double corr = EvidenceStats.Correlation(x.Take(x.Length - 1).ToArray(), y.Skip(1).ToArray());
        Assert.That(Math.Abs(corr), Is.LessThan(0.1), "the planted lead is invisible to correlation");

        var te = TransferEntropy.Test(x, y, surrogates: 50);
        Assert.That(te.PValue, Is.LessThan(0.05));
        Assert.That(te.ParametricPValue, Is.LessThan(1e-6));
        Assert.That(te.Effective, Is.GreaterThan(0.1));
    }

    [Test]
    public void Test_OneWayCoupling_FlowsOneWay()
    {
        var (x, y) = Squared(3000, seed: 42);
        var forward = TransferEntropy.Test(x, y, surrogates: 50);
        var backward = TransferEntropy.Test(y, x, surrogates: 50);
        Assert.That(forward.Effective, Is.GreaterThan(10 * Math.Max(backward.Effective, 0.001)));
        Assert.That(backward.PValue, Is.GreaterThan(0.01));
    }

    [Test]
    public void Test_IndependentSeries_AreNotSignificant()
    {
        int significant = 0;
        for (int s = 0; s < 10; s++)
        {
            var a = Synthetic.RandomWalkReturns(1500, seed: 100 + s);
            var b = Synthetic.RandomWalkReturns(1500, seed: 200 + s);
            if (TransferEntropy.Test(a, b, surrogates: 50, seed: s).PValue < 0.05) significant++;
        }
        Assert.That(significant, Is.LessThanOrEqualTo(2), "false positives should stay near the 5% level");
    }

    [Test]
    public void FromTransferEntropy_ManyIndependentSymbols_YieldNoEdges_AfterMultipleTestingCorrection()
    {
        var returns = Enumerable.Range(0, 6).ToDictionary(i => $"S{i}", i => Synthetic.RandomWalkReturns(1200, seed: 300 + i));
        var g = InfluenceGraph.FromTransferEntropy(returns, surrogates: 40);
        int edges = 0;
        foreach (var a in g.Symbols) foreach (var b in g.Symbols) if (a != b && g.Edge(a, b) != 0) edges++;
        Assert.That(edges, Is.EqualTo(0), "30 pairs tested; BH keeps chance hits out");
    }

    [Test]
    public void FromTransferEntropy_BuildsTheDirectedEdge_AndDropsTheReverse()
    {
        var (x, y) = Squared(2000, seed: 43);
        var g = InfluenceGraph.FromTransferEntropy(new Dictionary<string, double[]> { ["LEAD"] = x, ["FOLL"] = y }, surrogates: 40);
        Assert.That(g.Edge("LEAD", "FOLL"), Is.Not.EqualTo(0));
        Assert.That(g.Edge("FOLL", "LEAD"), Is.EqualTo(0));
        Assert.That(g.Lag, Is.EqualTo(1));
    }

    private static Dictionary<string, IReadOnlyList<Candle>> Market(double[] lead, double[] foll, double[] noise) => new(StringComparer.Ordinal)
    {
        ["LEAD"] = Synthetic.Candles(Synthetic.PricesFromReturns(lead, 50), "LEAD"),
        ["FOLL"] = Synthetic.Candles(Synthetic.PricesFromReturns(foll, 2.5), "FOLL"),
        ["NOISE"] = Synthetic.Candles(Synthetic.PricesFromReturns(noise, 4), "NOISE"),
    };

    [Test]
    public void Discover_WithTransferEntropy_FindsTheNonlinearLeader_CorrelationDoesNot()
    {
        var (x, y) = Squared(1500, seed: 44);
        // NOISE must not share the follower's noise generator (Squared uses seed + 1), or it truly leads.
        var history = Market(x, y, Synthetic.RandomWalkReturns(1500, seed: 9045));
        var te = LeaderDiscovery.Discover("FOLL", history, minInfluence: 0, edge: EdgeMeasure.TransferEntropy);
        var corr = LeaderDiscovery.Discover("FOLL", history, minInfluence: 0);
        Assert.That(te.Select(l => l.Symbol), Does.Contain("LEAD"));
        Assert.That(te.Select(l => l.Symbol), Does.Not.Contain("NOISE"));
        Assert.That(corr.Select(l => l.Symbol), Does.Not.Contain("LEAD"));
    }

    [Test]
    public void Discover_WithTransferEntropy_NeverSeesACouplingThatOnlyExistsAfterTheHistory()
    {
        // The coupling exists only in the second half; discovery is handed only the first half,
        // as callers do for "sessions before the traded day", so it must not find the leader.
        int n = 3000, half = n / 2;
        var x = Synthetic.RandomWalkReturns(n, seed: 46);
        var rng = new Random(47);
        var y = new double[n];
        for (int t = 1; t < n; t++)
            y[t] = t >= half ? 50 * (x[t - 1] * x[t - 1] - 1e-4) : 0.01 * Synthetic.Gaussian(rng);
        var full = Market(x, y, Synthetic.RandomWalkReturns(n, seed: 48));
        var before = full.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<Candle>)kv.Value.Take(half).ToList());

        Assert.That(LeaderDiscovery.Discover("FOLL", before, minInfluence: 0, edge: EdgeMeasure.TransferEntropy)
            .Select(l => l.Symbol), Does.Not.Contain("LEAD"));
        Assert.That(LeaderDiscovery.Discover("FOLL", full, minInfluence: 0, edge: EdgeMeasure.TransferEntropy)
            .Select(l => l.Symbol), Does.Contain("LEAD"), "the same coupling is found once it is in the history");
    }
}
