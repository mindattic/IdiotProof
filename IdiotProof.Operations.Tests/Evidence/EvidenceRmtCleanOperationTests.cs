using System.Text.RegularExpressions;
using IdiotProof.Evidence.Information;
using IdiotProof.Evidence.Leaders;
using IdiotProof.Models;
using IdiotProof.Operations.Evidence;
using static IdiotProof.Operations.Tests.Evidence.EvidenceTestKit;

namespace IdiotProof.Operations.Tests.Evidence;

/// <summary>--clean rmt on evidence graph and walkforward --model leaders (IP-T408).</summary>
public class EvidenceRmtCleanOperationTests
{
    private static readonly string[] NoiseSymbols = ["N1", "N2", "N3", "N4", "N5", "N6", "N7", "N8"];

    /// <summary>Eight independent random walks plus PAIRA/PAIRB sharing one strong factor.</summary>
    private static FakeBarStore NoiseAndPair(DateOnly day)
    {
        var store = new FakeBarStore();
        for (int k = 0; k < NoiseSymbols.Length; k++)
            store.Minute[NoiseSymbols[k]] = Session(NoiseSymbols[k], day, Walk(new Random(900 + k), 3.0)).ToList();
        var rng = new Random(77);
        double[] a = new double[390], b = new double[390];
        a[0] = 2; b[0] = 4;
        for (int i = 1; i < 390; i++)
        {
            double f = 0.004 * (rng.NextDouble() * 2 - 1);
            a[i] = a[i - 1] * (1 + f + 0.001 * (rng.NextDouble() * 2 - 1));
            b[i] = b[i - 1] * (1 + f + 0.001 * (rng.NextDouble() * 2 - 1));
        }
        store.Minute["PAIRA"] = Session("PAIRA", day, i => a[i]).ToList();
        store.Minute["PAIRB"] = Session("PAIRB", day, i => b[i]).ToList();
        return store;
    }

    private static Func<int, double> Walk(Random rng, double start)
    {
        var p = new double[390];
        p[0] = start;
        for (int i = 1; i < p.Length; i++) p[i] = p[i - 1] * (1 + 0.004 * (rng.NextDouble() * 2 - 1));
        return i => p[i];
    }

    private static int EdgeRows(string human) =>
        Regex.Matches(human.Split("Shock at")[0], @"^\s*\w+\s+\w+\s+[+-]\d\.\d{3}\s*$", RegexOptions.Multiline).Count;

    private static EvidenceGraphArgs GraphArgs(DateOnly day, EvidenceClean clean) => new()
    {
        Symbols = [.. NoiseSymbols, "PAIRA", "PAIRB"], Source = "PAIRA", MinCorr = 0.05, Clean = clean, From = day, To = day,
    };

    [Test]
    public async Task Graph_CleanRmt_DropsChanceEdges_ButKeepsTheRealPair()
    {
        var day = Weekdays(1)[0];
        var store = NoiseAndPair(day);
        var raw = await new EvidenceGraphOperation().ExecuteAsync(GraphArgs(day, EvidenceClean.None), Context(store));
        var clean = await new EvidenceGraphOperation().ExecuteAsync(GraphArgs(day, EvidenceClean.Rmt), Context(store));

        Assert.That(raw.IsOk && clean.IsOk, Is.True, raw.Message + clean.Message);
        Assert.That(clean.Human, Does.Contain("RMT-cleaned"), clean.Human);
        Assert.That(EdgeRows(raw.Human), Is.GreaterThan(EdgeRows(clean.Human) + 3), raw.Human + "\n---\n" + clean.Human);
        Assert.That(clean.Human, Does.Match(@"PAIRA\s+PAIRB\s+\+0\.\d{3}"), clean.Human);
    }

    [Test]
    public async Task Graph_CleanRmt_WithLagOne_UsesTheNoiseFloor()
    {
        var day = Weekdays(1)[0];
        var args = GraphArgs(day, EvidenceClean.Rmt) with { Lag = 1 };
        var result = await new EvidenceGraphOperation().ExecuteAsync(args, Context(NoiseAndPair(day)));
        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.Contain("noise floor"), result.Human);
    }

    [Test]
    public async Task Graph_RejectsCleanRmtWithTransferEntropy()
    {
        var result = await new EvidenceGraphOperation().ExecuteAsync(new EvidenceGraphArgs
        {
            Symbols = ["A", "B"], Edge = EvidenceEdge.Te, Clean = EvidenceClean.Rmt,
        }, Context(new FakeBarStore()));
        Assert.That(result.IsOk, Is.False);
        Assert.That(result.Message, Does.Contain("--clean rmt"));
    }

    /// <summary>FOLL repeats LEAD's previous-minute move every day; NOISE wanders.</summary>
    private static FakeBarStore LeaderStore(IReadOnlyList<DateOnly> days)
    {
        var store = new FakeBarStore();
        store.Minute["LEAD"] = []; store.Minute["FOLL"] = []; store.Minute["NOISE"] = [];
        for (int d = 0; d < days.Count; d++)
        {
            var rng = new Random(300 + d);
            var lead = new double[390]; var foll = new double[390]; var noise = new double[390];
            lead[0] = 50; foll[0] = 2.5; noise[0] = 4;
            for (int i = 1; i < 390; i++)
            {
                lead[i] = lead[i - 1] * (1 + 0.004 * (rng.NextDouble() * 2 - 1));
                noise[i] = noise[i - 1] * (1 + 0.004 * (rng.NextDouble() * 2 - 1));
                double leadRet = lead[i - 1] / (i >= 2 ? lead[i - 2] : lead[0]) - 1;
                foll[i] = foll[i - 1] * (1 + 0.9 * leadRet + 0.0002 * (rng.NextDouble() - 0.5));
            }
            store.Minute["LEAD"].AddRange(Session("LEAD", days[d], i => lead[i]));
            store.Minute["NOISE"].AddRange(Session("NOISE", days[d], i => noise[i]));
            store.Minute["FOLL"].AddRange(Session("FOLL", days[d], i => foll[i]));
        }
        return store;
    }

    [Test]
    public async Task WalkForward_LeadersWithCleanRmt_StillFindsTheRealLeader()
    {
        var days = Weekdays(8);
        var result = await new EvidenceWalkForwardOperation().ExecuteAsync(new EvidenceWalkForwardArgs
        {
            Symbols = ["foll"], From = days[3], To = days[7], Model = EvidenceModel.Leaders, Leaders = "LEAD,NOISE",
            Horizon = 1, Cost = 0, Clean = EvidenceClean.Rmt,
        }, Context(LeaderStore(days)));

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.Contain("RMT-cleaned lag-1 correlation"), result.Human);
        Assert.That(result.Human, Does.Not.Contain("0/0"), result.Human);
    }

    [Test]
    public async Task WalkForward_RejectsCleanRmt_OutsideTheLeadersModel_OrWithTransferEntropy()
    {
        var day = Weekdays(1)[0];
        var analog = await new EvidenceWalkForwardOperation().ExecuteAsync(new EvidenceWalkForwardArgs
        {
            Symbols = ["foll"], From = day, Clean = EvidenceClean.Rmt,
        }, Context(new FakeBarStore()));
        var te = await new EvidenceWalkForwardOperation().ExecuteAsync(new EvidenceWalkForwardArgs
        {
            Symbols = ["foll"], From = day, Model = EvidenceModel.Leaders, Edge = EvidenceEdge.Te, Clean = EvidenceClean.Rmt,
        }, Context(new FakeBarStore()));

        Assert.That(analog.IsOk, Is.False);
        Assert.That(analog.Message, Does.Contain("--model leaders"));
        Assert.That(te.IsOk, Is.False);
        Assert.That(te.Message, Does.Contain("shuffled surrogates"));
    }

    [Test]
    public void LeaderDiscovery_RefusesCleanRmtWithTransferEntropy()
    {
        var history = new Dictionary<string, IReadOnlyList<Candle>>();
        Assert.Throws<ArgumentException>(() =>
            LeaderDiscovery.Discover("A", history, edge: EdgeMeasure.TransferEntropy, cleanRmt: true));
    }
}
