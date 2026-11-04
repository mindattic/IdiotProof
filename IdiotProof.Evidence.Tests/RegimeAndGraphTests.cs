namespace IdiotProof.Evidence.Tests;

public class RegimeAndGraphTests
{
    [Test]
    public void Hurst_RandomWalkNearHalf_PersistentSeriesClearlyHigher()
    {
        var rw = Synthetic.RandomWalkReturns(4096, seed: 1);
        var persistent = Synthetic.Ar1Returns(4096, phi: 0.6, seed: 1);
        double hRw = RegimeStats.Hurst(rw);
        double hP = RegimeStats.Hurst(persistent);
        Assert.That(hRw, Is.InRange(0.4, 0.65));
        // AR(1) is short-memory, so R/S drifts back toward 0.5 at large windows; the gap stays clear but modest.
        Assert.That(hP, Is.GreaterThan(hRw + 0.05));
    }

    [Test]
    public void VarianceRatio_RandomWalkNearOne_TrendAbove_ReversionBelow()
    {
        Assert.That(RegimeStats.VarianceRatio(Synthetic.RandomWalkReturns(5000, 2), 4), Is.InRange(0.9, 1.1));
        Assert.That(RegimeStats.VarianceRatio(Synthetic.Ar1Returns(5000, 0.4, 2), 4), Is.GreaterThan(1.3));
        Assert.That(RegimeStats.VarianceRatio(Synthetic.Ar1Returns(5000, -0.4, 2), 4), Is.LessThan(0.75));
    }

    [Test]
    public void EfficiencyRatio_StraightLineIsOne_ChopIsNearZero()
    {
        Assert.That(RegimeStats.EfficiencyRatio(new double[] { 1, 2, 3, 4 }), Is.EqualTo(1).Within(1e-12));
        Assert.That(RegimeStats.EfficiencyRatio(new double[] { 1, 2, 1, 2, 1 }), Is.EqualTo(0).Within(1e-12));
    }

    [Test]
    public void Read_ClassifiesBySignificance_NotByCutoff()
    {
        var trend = RegimeStats.Read(Synthetic.PricesFromReturns(Synthetic.Ar1Returns(2000, 0.5, 4)));
        var revert = RegimeStats.Read(Synthetic.PricesFromReturns(Synthetic.Ar1Returns(2000, -0.5, 4)));
        var walk = RegimeStats.Read(Synthetic.PricesFromReturns(Synthetic.RandomWalkReturns(2000, 4)));
        Assert.That(trend.Regime, Is.EqualTo(Regime.Trending));
        Assert.That(revert.Regime, Is.EqualTo(Regime.MeanReverting));
        Assert.That(walk.Regime, Is.EqualTo(Regime.RandomWalk));
        Assert.That(RegimeStats.Read(new double[] { 1, 2, 3 }).Regime, Is.EqualTo(Regime.Unknown));
    }

    [Test]
    public void Influence_DecaysWithEveryHop_OnAChain()
    {
        var symbols = new[] { "A", "B", "C", "D", "E" };
        var edges = new List<(string, string, double)>();
        for (int i = 0; i + 1 < symbols.Length; i++)
        {
            edges.Add((symbols[i], symbols[i + 1], 0.8));
            edges.Add((symbols[i + 1], symbols[i], 0.8));
        }
        var g = InfluenceGraph.FromEdges(symbols, edges);
        var inf = g.Influence("A", beta: 0.5, maxHops: 12);
        Assert.That(inf["B"], Is.GreaterThan(inf["C"]));
        Assert.That(inf["C"], Is.GreaterThan(inf["D"]));
        Assert.That(inf["D"], Is.GreaterThan(inf["E"]));
        Assert.That(inf["E"], Is.GreaterThan(0));
        Assert.That(g.HopDistance("A", "E"), Is.EqualTo(4));
    }

    [Test]
    public void Influence_CarriesSign_AnInverseLinkPushesTheOtherWay()
    {
        var g = InfluenceGraph.FromEdges(new[] { "X", "Y" }, new[] { ("X", "Y", -0.9), ("Y", "X", -0.9) });
        Assert.That(g.Influence("X")["Y"], Is.LessThan(0));
    }

    [Test]
    public void FromReturns_LagOneEdge_CapturesALeaderAndItsFollower()
    {
        var leader = Synthetic.RandomWalkReturns(1000, seed: 9);
        var rng = new Random(10);
        var follower = new double[1000];
        for (int i = 1; i < 1000; i++) follower[i] = 0.8 * leader[i - 1] + 0.004 * Synthetic.Gaussian(rng);
        var g = InfluenceGraph.FromReturns(new Dictionary<string, double[]> { ["LEAD"] = leader, ["FOLL"] = follower }, lag: 1);
        Assert.That(g.Edge("LEAD", "FOLL"), Is.GreaterThan(0.8));
        Assert.That(g.Edge("FOLL", "LEAD"), Is.EqualTo(0), "the follower does not predict the leader");
    }

    [Test]
    public void OutcomeTable_SmoothsSparseBuckets_AndReportsLift()
    {
        var table = new ConditionalOutcomeTable<int>();
        for (int i = 0; i < 80; i++) table.Add(0, i % 2 == 0);
        for (int i = 0; i < 20; i++) table.Add(1, i < 16);
        var hot = table.Rate(1);
        Assert.That(hot.Samples, Is.EqualTo(20));
        Assert.That(hot.Probability, Is.EqualTo(17.0 / 22).Within(1e-12));
        Assert.That(hot.Lift, Is.GreaterThan(1.3));
        var empty = table.Rate(99);
        Assert.That(empty.Probability, Is.EqualTo(0.5));
        Assert.That(empty.Samples, Is.EqualTo(0));
    }

    [Test]
    public void QuantileBinner_SplitsTrainingIntoEqualFrequencyBins()
    {
        var binner = new QuantileBinner(Enumerable.Range(0, 100).Select(i => (double)i), 4);
        Assert.That(binner.Bin(-5), Is.EqualTo(0));
        Assert.That(binner.Bin(30), Is.EqualTo(1));
        Assert.That(binner.Bin(60), Is.EqualTo(2));
        Assert.That(binner.Bin(1000), Is.EqualTo(3));
    }
}
