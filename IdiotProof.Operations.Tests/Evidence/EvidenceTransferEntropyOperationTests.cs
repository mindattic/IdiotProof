using IdiotProof.Operations.Evidence;
using static IdiotProof.Operations.Tests.Evidence.EvidenceTestKit;

namespace IdiotProof.Operations.Tests.Evidence;

/// <summary>--edge te on evidence graph and walkforward --model leaders (IP-T400).</summary>
public class EvidenceTransferEntropyOperationTests
{
    /// <summary>FOLL's next move scales with the size (not the sign) of LEAD's last move: invisible to correlation.</summary>
    private static FakeBarStore SquaredLead(IReadOnlyList<DateOnly> days, Func<int, bool> coupledOnDay)
    {
        var store = new FakeBarStore();
        store.Minute["LEAD"] = []; store.Minute["FOLL"] = []; store.Minute["NOISE"] = [];
        for (int d = 0; d < days.Count; d++)
        {
            var rng = new Random(500 + d);
            var lead = new double[390]; var foll = new double[390]; var noise = new double[390];
            lead[0] = 50; foll[0] = 2.5; noise[0] = 4;
            double lastLead = 0;
            for (int i = 1; i < 390; i++)
            {
                double lr = 0.004 * (rng.NextDouble() * 2 - 1);
                lead[i] = lead[i - 1] * (1 + lr);
                noise[i] = noise[i - 1] * (1 + 0.004 * (rng.NextDouble() * 2 - 1));
                double fr = coupledOnDay(d)
                    ? 300 * (lastLead * lastLead - 0.004 * 0.004 / 3) + 0.0002 * (rng.NextDouble() - 0.5)
                    : 0.004 * (rng.NextDouble() * 2 - 1);
                foll[i] = foll[i - 1] * (1 + fr);
                lastLead = lr;
            }
            store.Minute["LEAD"].AddRange(Session("LEAD", days[d], i => lead[i]));
            store.Minute["FOLL"].AddRange(Session("FOLL", days[d], i => foll[i]));
            store.Minute["NOISE"].AddRange(Session("NOISE", days[d], i => noise[i]));
        }
        return store;
    }

    [Test]
    public async Task Graph_EdgeTe_FindsTheNonlinearLead_ThatCorrelationMisses()
    {
        var days = Weekdays(3);
        var store = SquaredLead(days, _ => true);
        var te = await new EvidenceGraphOperation().ExecuteAsync(new EvidenceGraphArgs
        {
            Symbols = ["LEAD", "FOLL", "NOISE"], Source = "LEAD", Edge = EvidenceEdge.Te, From = days[0], To = days[2],
        }, Context(store));
        var corr = await new EvidenceGraphOperation().ExecuteAsync(new EvidenceGraphArgs
        {
            Symbols = ["LEAD", "FOLL", "NOISE"], Source = "LEAD", Lag = 1, From = days[0], To = days[2],
        }, Context(store));

        Assert.That(te.IsOk, Is.True, te.Message);
        Assert.That(te.Human, Does.Contain("transfer entropy"));
        Assert.That(te.Human, Does.Match(@"LEAD\s+FOLL\s+[+-]0\.\d+"), te.Human);
        Assert.That(te.Human, Does.Not.Match(@"FOLL\s+LEAD\s+[+-]0"), te.Human);
        Assert.That(te.Human, Does.Not.Match(@"NOISE\s+\w+\s+[+-]0\.\d+\n"), te.Human);
        Assert.That(corr.Human, Does.Not.Match(@"LEAD\s+FOLL\s+[+-]"), corr.Human);
    }

    [Test]
    public async Task Graph_RejectsAnAlphaOutsideZeroToOne()
    {
        var result = await new EvidenceGraphOperation().ExecuteAsync(new EvidenceGraphArgs
        {
            Symbols = ["A", "B"], Edge = EvidenceEdge.Te, Alpha = 1.5,
        }, Context(new FakeBarStore()));
        Assert.That(result.IsOk, Is.False);
    }

    [Test]
    public async Task WalkForward_EdgeTe_DiscoversOnPriorSessionsOnly()
    {
        // Only the evaluated day is coupled; a TE leader found would mean discovery read day D.
        var days = Weekdays(6);
        var plan = new Dictionary<string, SortedSet<DateOnly>> { ["FOLL"] = new() { days[5] } };
        var settings = new WalkForwardSettings
        {
            Model = EvidenceModel.Leaders, LeaderCandidates = ["LEAD", "NOISE"], LeaderLookback = 1, Horizon = 1, MinTrainDays = 2,
            LeaderEdge = IdiotProof.Evidence.Information.EdgeMeasure.TransferEntropy,
        };
        var report = await new EvidenceWalkForward(SquaredLead(days, d => d == 5)).RunAsync(plan, settings, CancellationToken.None);
        Assert.That(report.Folds, Is.Empty);
        Assert.That(report.SkippedSymbolDays, Is.EqualTo(1));
    }

    [Test]
    public async Task WalkForward_EdgeTe_FindsTheLeader_AndSaysWhichEdgeItUsed()
    {
        var days = Weekdays(6);
        var result = await new EvidenceWalkForwardOperation().ExecuteAsync(new EvidenceWalkForwardArgs
        {
            Symbols = ["foll"], From = days[3], To = days[5], Model = EvidenceModel.Leaders, Leaders = "LEAD,NOISE",
            Edge = EvidenceEdge.Te, Horizon = 1, Cost = 0,
        }, Context(SquaredLead(days, _ => true)));
        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.Contain("transfer-entropy edges"), result.Human);
        Assert.That(result.Human, Does.Contain("leaders: among LEAD,NOISE"), result.Human);
    }
}
