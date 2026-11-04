using IdiotProof.Core.Evidence;
using IdiotProof.Operations.Evidence;
using static IdiotProof.Operations.Tests.Evidence.EvidenceTestKit;

namespace IdiotProof.Operations.Tests.Evidence;

/// <summary>evidence walkforward (IP-T374): strictly out-of-sample directional scoring.</summary>
public class EvidenceWalkForwardTests
{
    [Test]
    public async Task NeverTrainsOnDayDOrLater_TheIndexHoldsExactlyThePriorSessionsWindows()
    {
        // The store hands back days 0-9 (D = day 6 and three later days included). Every indexed
        // window must come from days 0-5: with window 10, horizon 2 and 390-bar sessions whose labels
        // stay inside their session, that is 379 windows for the first session (it has no earlier
        // context) and 388 for each of the next five. One bar of day 6 would change the count.
        var days = Weekdays(10);
        var store = new FakeBarStore();
        store.Minute["LEAK"] = days.SelectMany((d, i) => Session("LEAK", d, RandomWalk(i))).ToList();

        var plan = new Dictionary<string, SortedSet<DateOnly>> { ["LEAK"] = new() { days[6] } };
        var report = await new EvidenceWalkForward(store).RunAsync(plan, new WalkForwardSettings { Window = 10, Horizon = 2, TrainDays = 20 }, CancellationToken.None);

        var fold = report.Folds.Single();
        Assert.That(fold.FirstTrainingDay, Is.EqualTo(days[0]));
        Assert.That(fold.LastTrainingDay, Is.EqualTo(days[5]));
        Assert.That(fold.LastTrainingBarUtc, Is.LessThan(fold.FirstTestBarUtc));
        Assert.That(FakeBarStore.DateOf(new IdiotProof.Models.Candle { StartUtc = fold.LastTrainingBarUtc }), Is.EqualTo(days[5]));
        Assert.That(fold.TrainingWindows, Is.EqualTo(379 + 5 * 388));
    }

    [Test]
    public async Task EveryFold_TrainsOnlyOnEarlierSessions()
    {
        var days = Weekdays(8);
        var store = new FakeBarStore();
        store.Minute["SAW"] = days.SelectMany(d => Session("SAW", d, Sawtooth)).ToList();
        var plan = new Dictionary<string, SortedSet<DateOnly>> { ["SAW"] = new(days) };

        var report = await new EvidenceWalkForward(store).RunAsync(plan, new WalkForwardSettings { Window = 10, Horizon = 2, MinTrainDays = 2 }, CancellationToken.None);

        Assert.That(report.Folds, Has.Count.EqualTo(6), "the first two days lack the minimum two prior sessions");
        Assert.That(report.Folds.All(f => f.LastTrainingDay < f.Day), Is.True);
        Assert.That(report.SkippedSymbolDays, Is.EqualTo(2));
    }

    [Test]
    public async Task Operation_LearnsARepeatingPattern_AndSaysItBeats51PercentWithClusteredSignificance()
    {
        var days = Weekdays(12);
        var store = new FakeBarStore();
        store.Minute["SAW"] = days.SelectMany(d => Session("SAW", d, Sawtooth)).ToList();

        var result = await new EvidenceWalkForwardOperation().ExecuteAsync(new EvidenceWalkForwardArgs
        {
            Symbols = ["saw"], From = days[3], To = days[11], Window = 10, Horizon = 2, Cost = 0,
        }, Context(store));

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.Contain("Beats 51% with clustered significance"), result.Human);
        Assert.That(result.Human, Does.Contain("bootstrap p = "));
        Assert.That(result.Human, Does.Contain("months"));
    }

    [Test]
    public async Task Operation_ThreePerfectDays_AreNotEnoughEvidence_OnceCallsAreClusteredByDay()
    {
        var days = Weekdays(6);
        var store = new FakeBarStore();
        store.Minute["SAW"] = days.SelectMany(d => Session("SAW", d, Sawtooth)).ToList();

        var result = await new EvidenceWalkForwardOperation().ExecuteAsync(new EvidenceWalkForwardArgs
        {
            Symbols = ["saw"], From = days[3], To = days[5], Window = 10, Horizon = 2, Cost = 0,
        }, Context(store));

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.Contain("Not significant once calls are clustered by day"), result.Human);
    }

    [Test]
    public async Task Operation_OnNoise_ReportsNotSignificant()
    {
        var days = Weekdays(12);
        var store = new FakeBarStore();
        store.Minute["RND"] = days.SelectMany((d, i) => Session("RND", d, RandomWalk(100 + i))).ToList();

        var result = await new EvidenceWalkForwardOperation().ExecuteAsync(new EvidenceWalkForwardArgs
        {
            Symbols = ["RND"], From = days[4], To = days[11], Window = 12, Horizon = 3,
        }, Context(store));

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.Not.Contain("Beats"), result.Human);
    }

    [Test]
    public async Task PennyUniverse_UsesOnlyEachDaysMembers_FromActiveAndDelistedCandidates()
    {
        var days = Weekdays(6);
        var store = new FakeBarStore();
        foreach (var s in new[] { "GONE", "LIVE", "BIG" })
            store.Minute[s] = days.SelectMany(d => Session(s, d, Sawtooth)).ToList();
        // GONE (delisted today) was a member on days 4-5; BIG was never under $5 so never a member.
        store.Universe[days[4]] = [new UniverseMember("GONE", days[3], 2m, 1_000_000m, 20), new UniverseMember("LIVE", days[3], 3m, 900_000m, 20)];
        store.Universe[days[5]] = [new UniverseMember("GONE", days[4], 2m, 1_000_000m, 20)];
        var candidates = new FakeCandidates(new CandidateAsset("GONE", false), new CandidateAsset("LIVE", true), new CandidateAsset("BIG", true));

        var result = await new EvidenceWalkForwardOperation().ExecuteAsync(new EvidenceWalkForwardArgs
        {
            Universe = EvidenceUniverse.Penny, From = days[4], To = days[5], Window = 10, Horizon = 2,
        }, Context(store, candidates));

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(store.UniverseCalls.Select(c => c.AsOf), Is.EqualTo(new[] { days[4], days[5] }));
        Assert.That(store.UniverseCalls[0].Candidates, Does.Contain("GONE"), "delisted names must stay candidates");
        Assert.That(result.Human, Does.Contain("from 3 candidates (1 inactive)"));
        Assert.That(result.Human, Does.Contain("GONE").And.Contain("LIVE"));
        Assert.That(result.Human, Does.Not.Contain("BIG"));
        Assert.That(result.Human, Does.Not.Contain("survivorship bias"));
    }

    [Test]
    public async Task PennyUniverse_WithoutDelistedCandidates_FlagsSurvivorshipBias()
    {
        var store = new FakeBarStore();
        var days = Weekdays(2);
        var result = await new EvidenceWalkForwardOperation().ExecuteAsync(new EvidenceWalkForwardArgs
        {
            Universe = EvidenceUniverse.Penny, From = days[0], To = days[1],
        }, Context(store, new FakeCandidates(new CandidateAsset("LIVE", true))));
        Assert.That(result.Human, Does.Contain("survivorship bias"));
        Assert.That(result.Human, Does.Contain("No calls were made"));
    }

    [Test]
    public async Task Usage_SymbolsAndUniverseTogether_IsRejected()
    {
        var result = await new EvidenceWalkForwardOperation().ExecuteAsync(new EvidenceWalkForwardArgs
        {
            Symbols = ["A"], Universe = EvidenceUniverse.Penny, From = new DateOnly(2026, 11, 2), To = new DateOnly(2026, 11, 3),
        }, Context(new FakeBarStore()));
        Assert.That(result.Status, Is.EqualTo(OperationStatus.UsageError));
    }
}
