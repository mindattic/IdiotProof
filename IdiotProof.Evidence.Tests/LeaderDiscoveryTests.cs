using IdiotProof.Evidence.Leaders;
using IdiotProof.Models;

namespace IdiotProof.Evidence.Tests;

public class LeaderDiscoveryTests
{
    private static Dictionary<string, IReadOnlyList<Candle>> LeaderFollowerNoise(int n = 400, double coupling = 0.8)
    {
        var lead = Synthetic.RandomWalkReturns(n, seed: 21);
        var noise = Synthetic.RandomWalkReturns(n, seed: 22);
        var rng = new Random(23);
        var foll = new double[n];
        for (int i = 1; i < n; i++) foll[i] = coupling * lead[i - 1] + 0.003 * Synthetic.Gaussian(rng);
        return new Dictionary<string, IReadOnlyList<Candle>>(StringComparer.Ordinal)
        {
            ["LEAD"] = Synthetic.Candles(Synthetic.PricesFromReturns(lead, 50), "LEAD"),
            ["FOLL"] = Synthetic.Candles(Synthetic.PricesFromReturns(foll, 2.5), "FOLL"),
            ["NOISE"] = Synthetic.Candles(Synthetic.PricesFromReturns(noise, 4), "NOISE"),
        };
    }

    [Test]
    public void Discover_FindsTheSymbolWhoseMovesPrecedeTheTargets_AndIgnoresUnrelatedNoise()
    {
        var links = LeaderDiscovery.Discover("FOLL", LeaderFollowerNoise());
        Assert.That(links.Select(l => l.Symbol), Does.Contain("LEAD"));
        Assert.That(links.Select(l => l.Symbol), Does.Not.Contain("NOISE"));
        var lead = links.Single(l => l.Symbol == "LEAD");
        Assert.That(lead.Hops, Is.EqualTo(1));
        Assert.That(lead.Influence, Is.GreaterThan(0));
    }

    [Test]
    public void Discover_TheFollowerDoesNotLeadTheLeader()
    {
        Assert.That(LeaderDiscovery.Discover("LEAD", LeaderFollowerNoise()).Select(l => l.Symbol), Does.Not.Contain("FOLL"));
    }

    [Test]
    public void Discover_InverseLeaderGetsNegativeInfluence()
    {
        var links = LeaderDiscovery.Discover("FOLL", LeaderFollowerNoise(coupling: -0.8));
        Assert.That(links.Single(l => l.Symbol == "LEAD").Influence, Is.LessThan(0));
    }

    [Test]
    public void Discover_ThinHistoryOrMissingTarget_FindsNoLeaders()
    {
        var all = LeaderFollowerNoise();
        Assert.That(LeaderDiscovery.Discover("ABSENT", all), Is.Empty);
        var thin = all.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<Candle>)kv.Value.Take(20).ToList());
        Assert.That(LeaderDiscovery.Discover("FOLL", thin), Is.Empty);
    }

    [Test]
    public void AlignReturns_NoReturnSpansASessionGap()
    {
        var day1 = Synthetic.Candles(new[] { 1.0, 1.1 }, "X");
        var day2 = Synthetic.Candles(new[] { 2.0, 2.2 }, "X")
            .Select(c => new Candle { Symbol = c.Symbol, StartUtc = c.StartUtc.AddDays(1), EndUtc = c.EndUtc.AddDays(1), Open = c.Open, High = c.High, Low = c.Low, Close = c.Close, Volume = c.Volume })
            .ToList();
        var r = LeaderDiscovery.AlignReturns(new Dictionary<string, IReadOnlyList<Candle>> { ["X"] = day1.Concat(day2).ToList() })["X"];
        Assert.That(r[2], Is.EqualTo(0), "the overnight jump 1.1 -> 2.0 is not a return");
        Assert.That(r[3], Is.EqualTo(0.1).Within(1e-9));
    }

    [Test]
    public void RecentMove_ReadsOnlyBarsClosedByAsOf()
    {
        var bars = Synthetic.Candles(new[] { 1.0, 1.0, 1.0, 1.1, 5.0 });
        var asOf = bars[3].EndUtc;
        Assert.That(LeaderSignal.RecentMove(bars, asOf, 3), Is.EqualTo(0.1).Within(1e-9), "bar 4 (still forming at asOf) must not be read");
        Assert.That(LeaderSignal.RecentMove(bars, bars[1].EndUtc, 3), Is.Null, "too few closed bars");
    }

    [Test]
    public void WeightedMove_FiltersByHopsAndInfluence_AndInverseLeadersFlipSign()
    {
        var leaders = new (int, double, double?)[] { (1, 0.5, 0.02), (1, -0.5, -0.02), (3, 0.9, -1.0), (1, 0.01, -1.0), (1, 0.4, null) };
        Assert.That(LeaderSignal.WeightedMove(leaders, maxHops: 2, minInfluence: 0.05), Is.EqualTo(0.02).Within(1e-12));
        Assert.That(LeaderSignal.WeightedMove(leaders, maxHops: 2, minInfluence: 0.95), Is.Null);
    }
}
