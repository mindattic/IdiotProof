using IdiotProof.Operations.Evidence;
using static IdiotProof.Operations.Tests.Evidence.EvidenceTestKit;

namespace IdiotProof.Operations.Tests.Evidence;

/// <summary>evidence label / similar / regime / graph (IP-T374).</summary>
public class EvidenceAnalysisOperationTests
{
    [Test]
    public async Task Label_CountsSawtoothOutcomes_WithinEachSession()
    {
        var days = Weekdays(2);
        var store = new FakeBarStore();
        store.Minute["SAW"] = days.SelectMany(d => Session("SAW", d, Sawtooth, minutes: 100)).ToList();

        var result = await new EvidenceLabelOperation().ExecuteAsync(new EvidenceLabelArgs
        {
            Symbol = "SAW", From = days[0], To = days[1], Horizon = 1, DeadBand = 0.0001,
        }, Context(store));

        Assert.That(result.IsOk, Is.True, result.Message);
        // 99 labels per session (no label crosses the overnight gap); 2 of every 10 bars fall next.
        Assert.That(result.Human, Does.Contain("198 labeled"));
        Assert.That(result.Human, Does.Contain("up 158 · down 40"));
    }

    [Test]
    public async Task Similar_FindsEarlierLookAlikes_AndTheirOutcomes()
    {
        var days = Weekdays(3);
        var store = new FakeBarStore();
        store.Minute["SAW"] = days.SelectMany(d => Session("SAW", d, Sawtooth, minutes: 200)).ToList();

        var result = await new EvidenceSimilarOperation().ExecuteAsync(new EvidenceSimilarArgs
        {
            Symbol = "SAW", Date = days[2], Window = 10, Horizon = 2, K = 5, MinSimilarity = 0.95,
        }, Context(store));

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.Contain("past windows indexed over 2 session(s)"));
        Assert.That(result.Human, Does.Contain("rose").And.Contain("fell"));
    }

    [Test]
    public async Task Regime_TellsATrendFromChop()
    {
        var days = Weekdays(1);
        var store = new FakeBarStore();
        var rng = new Random(5);
        double r = 0, p = 3;
        var trend = new double[390];
        for (int i = 0; i < trend.Length; i++) { r = 0.7 * r + 0.002 * (rng.NextDouble() * 2 - 1); p *= 1 + r; trend[i] = p; }
        store.Minute["TRND"] = Session("TRND", days[0], i => trend[i]).ToList();
        store.Minute["CHOP"] = Session("CHOP", days[0], i => i % 2 == 0 ? 3.00 : 3.02).ToList();

        var result = await new EvidenceRegimeOperation().ExecuteAsync(new EvidenceRegimeArgs
        {
            Symbols = ["TRND", "CHOP"], From = days[0], To = days[0],
        }, Context(store));

        Assert.That(result.IsOk, Is.True, result.Message);
        var lines = result.Human!.Split('\n');
        Assert.That(lines.Single(l => l.StartsWith("TRND")), Does.Contain("trending"));
        Assert.That(lines.Single(l => l.StartsWith("CHOP")), Does.Contain("mean-reverting"));
    }

    [Test]
    public async Task Graph_LagOne_FindsTheLeaderFollowerEdge_AndInfluenceFromTheLeader()
    {
        var days = Weekdays(1);
        var store = new FakeBarStore();
        var rng = new Random(8);
        var lead = new double[390];
        var foll = new double[390];
        lead[0] = foll[0] = 3;
        double lastLeadRet = 0;
        for (int i = 1; i < 390; i++)
        {
            double lr = 0.004 * (rng.NextDouble() * 2 - 1);
            lead[i] = lead[i - 1] * (1 + lr);
            foll[i] = foll[i - 1] * (1 + 0.9 * lastLeadRet + 0.0005 * (rng.NextDouble() * 2 - 1));
            lastLeadRet = lr;
        }
        store.Minute["LEAD"] = Session("LEAD", days[0], i => lead[i]).ToList();
        store.Minute["FOLL"] = Session("FOLL", days[0], i => foll[i]).ToList();

        var result = await new EvidenceGraphOperation().ExecuteAsync(new EvidenceGraphArgs
        {
            Symbols = ["LEAD", "FOLL"], Source = "LEAD", Lag = 1, From = days[0], To = days[0],
        }, Context(store));

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.Match(@"LEAD\s+FOLL\s+\+0\.9"));
        Assert.That(result.Human, Does.Not.Match(@"FOLL\s+LEAD\s+[+-]"));
        Assert.That(result.Human, Does.Match(@"FOLL\s+\+0\.\d+\s+1"));
    }

    [Test]
    public async Task Graph_SourceMustBeOneOfTheSymbols()
    {
        var result = await new EvidenceGraphOperation().ExecuteAsync(new EvidenceGraphArgs { Symbols = ["A", "B"], Source = "C" }, Context(new FakeBarStore()));
        Assert.That(result.Status, Is.EqualTo(OperationStatus.UsageError));
    }
}
