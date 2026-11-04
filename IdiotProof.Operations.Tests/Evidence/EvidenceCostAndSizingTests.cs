using IdiotProof.Evidence.Economics;
using IdiotProof.Operations.Evidence;
using static IdiotProof.Operations.Tests.Evidence.EvidenceTestKit;

namespace IdiotProof.Operations.Tests.Evidence;

/// <summary>IP-T403: modeled costs in walkforward/search and advisory Kelly sizing.</summary>
public class EvidenceCostAndSizingTests
{
    [Test]
    public async Task WalkForward_ModeledCost_IsRecordedPerCall_AndFlatCostStillOverrides()
    {
        var days = Weekdays(8);
        var store = new FakeBarStore();
        store.Minute["SAW"] = days.SelectMany(d => Session("SAW", d, Sawtooth)).ToList();
        var plan = new Dictionary<string, SortedSet<DateOnly>> { ["SAW"] = new(days.Skip(3)) };

        var modeled = await new EvidenceWalkForward(store).RunAsync(plan,
            new WalkForwardSettings { Window = 10, Horizon = 2, ModelCosts = true }, CancellationToken.None);
        var flat = await new EvidenceWalkForward(store).RunAsync(plan,
            new WalkForwardSettings { Window = 10, Horizon = 2, RoundTripCost = 0.01 }, CancellationToken.None);

        Assert.That(modeled.Overall.Calls, Is.GreaterThan(0));
        Assert.That(modeled.Overall.MeanCost, Is.GreaterThan(0));
        Assert.That(modeled.Overall.MeanCost, Is.Not.EqualTo(0.003), "the model, not the flat default, priced the calls");
        Assert.That(flat.Overall.MeanCost, Is.EqualTo(0.01).Within(1e-12));
    }

    [Test]
    public void CallCost_ReadsOnlyThePriorBarsItIsGiven()
    {
        var days = Weekdays(4);
        var prior = days.Take(3).SelectMany(d => Session("X", d, Sawtooth)).ToList();
        var s = new WalkForwardSettings { ModelCosts = true };
        double before = EvidenceWalkForward.CallCost(prior, 3, s);
        // Appending a wild future session to a *different* list never changes the estimate for the prior one.
        var withFuture = prior.Concat(Session("X", days[3], i => 3 + 2 * Math.Sin(i))).ToList();
        Assert.That(EvidenceWalkForward.CallCost(prior, 3, s), Is.EqualTo(before));
        Assert.That(EvidenceWalkForward.CallCost(withFuture, 4, s), Is.Not.EqualTo(before));
    }

    [Test]
    public async Task WalkForwardOperation_WithoutCost_ReportsModeledCost()
    {
        var days = Weekdays(8);
        var store = new FakeBarStore();
        store.Minute["SAW"] = days.SelectMany(d => Session("SAW", d, Sawtooth)).ToList();
        var result = await new EvidenceWalkForwardOperation().ExecuteAsync(new EvidenceWalkForwardArgs
        {
            Symbols = ["saw"], From = days[3], To = days[7], Window = 10, Horizon = 2,
        }, Context(store));
        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.Contain("modeled"), result.Human);
    }

    [Test]
    public async Task Sizing_ManualCounts_AreAdvisoryAndShrunk()
    {
        var result = await new EvidenceSizingOperation().ExecuteAsync(
            new EvidenceSizingArgs { Wins = 6, Losses = 4, Payoff = 1 }, Context(new FakeBarStore()));
        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.Contain("plug-in 20"), result.Human);
        Assert.That(result.Human, Does.Contain(KellyAdvice.Disclaimer));
    }

    [Test]
    public async Task Sizing_RejectsIncompleteManualInput_AndMissingBasis()
    {
        var partial = await new EvidenceSizingOperation().ExecuteAsync(new EvidenceSizingArgs { Wins = 3 }, Context(new FakeBarStore()));
        var none = await new EvidenceSizingOperation().ExecuteAsync(new EvidenceSizingArgs(), Context(new FakeBarStore()));
        Assert.That(partial.IsOk, Is.False);
        Assert.That(none.IsOk, Is.False);
    }
}
