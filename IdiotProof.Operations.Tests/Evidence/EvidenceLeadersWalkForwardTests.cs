using IdiotProof.Operations.Evidence;
using static IdiotProof.Operations.Tests.Evidence.EvidenceTestKit;

namespace IdiotProof.Operations.Tests.Evidence;

/// <summary>evidence walkforward --model leaders (IP-T391): leaders discovered on prior sessions only.</summary>
public class EvidenceLeadersWalkForwardTests
{
    /// <summary>LEAD wanders; FOLL repeats LEAD's previous-minute move on coupled days, wanders on its own otherwise; NOISE wanders.</summary>
    private static FakeBarStore Store(IReadOnlyList<DateOnly> days, Func<int, bool> coupledOnDay)
    {
        var store = new FakeBarStore();
        store.Minute["LEAD"] = []; store.Minute["FOLL"] = []; store.Minute["NOISE"] = [];
        for (int d = 0; d < days.Count; d++)
        {
            var rng = new Random(100 + d);
            double[] lead = Walk(rng, 50), noise = Walk(rng, 4), own = Walk(rng, 2.5);
            var foll = new double[400];
            foll[0] = 2.5;
            for (int i = 1; i < foll.Length; i++)
            {
                double leadRet = lead[i - 1] / (i >= 2 ? lead[i - 2] : lead[0]) - 1;
                double ownRet = own[i] / own[i - 1] - 1;
                foll[i] = foll[i - 1] * (1 + (coupledOnDay(d) ? 0.9 * leadRet + 0.0002 * (rng.NextDouble() - 0.5) : ownRet));
            }
            store.Minute["LEAD"].AddRange(Session("LEAD", days[d], i => lead[i]));
            store.Minute["NOISE"].AddRange(Session("NOISE", days[d], i => noise[i]));
            store.Minute["FOLL"].AddRange(Session("FOLL", days[d], i => foll[i]));
        }
        return store;
    }

    private static double[] Walk(Random rng, double start)
    {
        var p = new double[400];
        p[0] = start;
        for (int i = 1; i < p.Length; i++) p[i] = p[i - 1] * (1 + 0.004 * (rng.NextDouble() * 2 - 1));
        return p;
    }

    private static readonly WalkForwardSettings Leaders = new()
    {
        Model = EvidenceModel.Leaders, LeaderCandidates = ["LEAD", "NOISE"], LeaderLookback = 1, Horizon = 1, MinTrainDays = 2,
    };

    [Test]
    public async Task FollowsAMeasuredLeader_AndCallsTheFollowerAlmostAlwaysRight()
    {
        var days = Weekdays(8);
        var plan = new Dictionary<string, SortedSet<DateOnly>> { ["FOLL"] = new(days.Skip(3)) };
        var report = await new EvidenceWalkForward(Store(days, _ => true)).RunAsync(plan, Leaders, CancellationToken.None);

        Assert.That(report.Folds, Has.Count.EqualTo(5));
        Assert.That(report.Folds.All(f => f.LastTrainingDay < f.Day && f.LastTrainingBarUtc < f.FirstTestBarUtc), Is.True);
        Assert.That(report.Overall.HitRate, Is.GreaterThan(0.9), report.Overall.ToString());
    }

    [Test]
    public async Task ACouplingThatOnlyExistsOnDayD_IsNeverDiscovered()
    {
        // Prior days are independent; only the evaluated day is coupled. A leader found here would mean
        // discovery read day D's tape.
        var days = Weekdays(6);
        var plan = new Dictionary<string, SortedSet<DateOnly>> { ["FOLL"] = new() { days[5] } };
        var report = await new EvidenceWalkForward(Store(days, d => d == 5)).RunAsync(plan, Leaders, CancellationToken.None);

        Assert.That(report.Folds, Is.Empty);
        Assert.That(report.SkippedSymbolDays, Is.EqualTo(1));
    }

    [Test]
    public async Task Operation_ReportsTheLeadersModel()
    {
        var days = Weekdays(8);
        var result = await new EvidenceWalkForwardOperation().ExecuteAsync(new EvidenceWalkForwardArgs
        {
            Symbols = ["foll"], From = days[3], To = days[7], Model = EvidenceModel.Leaders, Leaders = "LEAD,NOISE",
            Horizon = 1, Cost = 0,
        }, Context(Store(days, _ => true)));

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.Contain("leaders: among LEAD,NOISE"), result.Human);
    }

    [Test]
    public async Task Operation_RejectsLeadersModelWithoutCandidates()
    {
        var result = await new EvidenceWalkForwardOperation().ExecuteAsync(new EvidenceWalkForwardArgs
        {
            Symbols = ["foll"], From = Weekdays(1)[0], Model = EvidenceModel.Leaders, Leaders = " ",
        }, Context(new FakeBarStore()));
        Assert.That(result.IsOk, Is.False);
    }
}
