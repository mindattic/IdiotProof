using IdiotProof.Models;
using IdiotProof.Operations.Evidence;
using static IdiotProof.Operations.Tests.Evidence.EvidenceTestKit;

namespace IdiotProof.Operations.Tests.Evidence;

/// <summary>IP-T413: tail-linked pairs are found on training bars, and contagion is only claimed if it holds out of sample.</summary>
public class EvidenceTailsOperationTests
{
    private static Candle Day(string symbol, DateOnly d, double open, double close) => new()
    {
        Symbol = symbol,
        StartUtc = new DateTime(d.Year, d.Month, d.Day, 14, 30, 0, DateTimeKind.Utc),
        EndUtc = new DateTime(d.Year, d.Month, d.Day, 21, 0, 0, DateTimeKind.Utc),
        Open = (decimal)open, Close = (decimal)close,
        High = (decimal)Math.Max(open, close), Low = (decimal)Math.Min(open, close), Volume = 1_000_000,
    };

    /// <summary>
    /// A: random daily returns. B: crashes 5% on A's bottom-decile days and loses 5% open-to-close the
    /// day after, otherwise independent. C: independent of both.
    /// </summary>
    private static FakeBarStore Market(int days, int seed)
    {
        var rng = new Random(seed);
        double Noise(double sd) => sd * Synthetic(rng);
        var dates = Weekdays(days);
        var store = new FakeBarStore();
        var a = new List<Candle>(); var b = new List<Candle>(); var c = new List<Candle>();
        double pa = 3, pb = 3, pc = 3;
        bool crashedYesterday = false;
        foreach (var d in dates)
        {
            double ra = Noise(0.02);
            bool crash = ra < -0.0256;
            double na = pa * (1 + ra);
            double openB = pb;
            double rb = crash ? -0.05 + Noise(0.002) : Noise(0.02);
            if (crashedYesterday) rb = (1 + rb) * 0.95 - 1;
            double nb = pb * (1 + rb);
            double nc = pc * (1 + Noise(0.02));
            a.Add(Day("AAA", d, pa, na)); b.Add(Day("BBB", d, openB, nb)); c.Add(Day("CCC", d, pc, nc));
            pa = na; pb = nb; pc = nc;
            crashedYesterday = crash;
        }
        store.Daily["AAA"] = a; store.Daily["BBB"] = b; store.Daily["CCC"] = c;
        return store;
    }

    private static double Synthetic(Random rng)
    {
        double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
    }

    [Test]
    public async Task FindsTheTailLinkedPair_AndConfirmsContagionOutOfSample_ButNotForTheIndependentName()
    {
        var store = Market(1200, seed: 11);
        var days = Weekdays(1200);

        var result = await new EvidenceTailsOperation().ExecuteAsync(new EvidenceTailsArgs
        {
            Symbols = ["aaa", "bbb", "ccc"], From = days[0], To = days[^1], Permutations = 299,
        }, Context(store));

        Assert.That(result.IsOk, Is.True, result.Message);
        dynamic data = result.Data!;
        var pairs = ((IEnumerable<dynamic>)data.Pairs).ToList();
        var ab = pairs.Single(p => p.A == "AAA" && p.B == "BBB");
        Assert.That((double)ab.TailQ, Is.LessThan(0.05));
        Assert.That((double)ab.LowerTail, Is.GreaterThan(0.4), "independence would be ≈ q = 0.1");
        // The training screen controls the false-discovery RATE, so an independent pair can still slip
        // through by chance (here C's λL is ~0.2 vs 0.1 expected); what must never happen is a contagion
        // claim for C, because that has to survive the held-out bars.
        Assert.That(pairs.Where(p => p.A == "CCC" || p.B == "CCC").All(p => (double)p.LowerTail < (double)ab.LowerTail / 2), Is.True);

        var links = ((IEnumerable<dynamic>)data.Contagion).ToList();
        var aToB = links.Single(l => l.Source == "AAA" && l.Target == "BBB");
        Assert.That((bool)aToB.Significant, Is.True);
        Assert.That((double)aToB.DownAfter, Is.GreaterThan(0.9));
        Assert.That(links.Where(l => l.Source == "CCC" || l.Target == "CCC").Any(l => (bool)l.Significant), Is.False,
            "an independent name never earns an out-of-sample contagion claim");
    }

    [Test]
    public async Task IndependentNames_ProduceNoContagionClaims()
    {
        var store = Market(500, seed: 12);
        store.Daily.Remove("BBB");
        var days = Weekdays(500);

        var result = await new EvidenceTailsOperation().ExecuteAsync(new EvidenceTailsArgs
        {
            Symbols = ["AAA", "CCC"], From = days[0], To = days[^1], Permutations = 299,
        }, Context(store));

        Assert.That(result.IsOk, Is.True, result.Message);
        dynamic data = result.Data!;
        Assert.That(((IEnumerable<dynamic>)data.Contagion).Count(), Is.Zero, result.Message);
    }

    [Test]
    public async Task OneSymbol_IsAUsageError()
    {
        var result = await new EvidenceTailsOperation().ExecuteAsync(new EvidenceTailsArgs { Symbols = ["AAA"] }, Context(new FakeBarStore()));
        Assert.That(result.IsOk, Is.False);
    }
}
