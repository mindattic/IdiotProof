using IdiotProof.Scripting;
using IdiotProof.Shared;

namespace IdiotProof.Strategies.Tests;

/// <summary>LeaderConfirms / NotLeaderDiverging: guards on measured lag-1 leaders, fail-closed (IP-T391).</summary>
public class LeaderGuardTests
{
    private static bool Eval(IndicatorType type, List<LeaderReading>? leaders, int maxHops = 2, double minInfluence = 0.1) =>
        new IndicatorCondition(type, maxHops, minInfluence).Evaluate(new IndicatorSnapshot { Symbol = "FOLL", Price = 2.0, Leaders = leaders });

    [TestCase(IndicatorType.LeaderConfirms)]
    [TestCase(IndicatorType.NotLeaderDiverging)]
    public void FailsClosed_WhenLeadersUnknownOrNoneQualify(IndicatorType type)
    {
        Assert.Multiple(() =>
        {
            Assert.That(Eval(type, null), Is.False, "leaders not computed");
            Assert.That(Eval(type, []), Is.False, "no measured leader");
            Assert.That(Eval(type, [new("SPY", 1, 0.05, 0.01)]), Is.False, "too weak to count");
            Assert.That(Eval(type, [new("SPY", 3, 0.6, 0.01)]), Is.False, "too many hops away");
            Assert.That(Eval(type, [new("SPY", 1, 0.6, null)]), Is.False, "leader move unknown");
        });
    }

    [Test]
    public void LeaderConfirms_PassesOnlyWhenTheLeaderMovedUpFirst()
    {
        Assert.That(Eval(IndicatorType.LeaderConfirms, [new("LEAD", 1, 0.5, 0.004)]), Is.True);
        Assert.That(Eval(IndicatorType.LeaderConfirms, [new("LEAD", 1, 0.5, -0.004)]), Is.False);
        Assert.That(Eval(IndicatorType.LeaderConfirms, [new("LEAD", 1, 0.5, 0)]), Is.False, "flat is not confirmation");
    }

    [Test]
    public void NotLeaderDiverging_BlocksOnlyWhenLeadersMoveDown()
    {
        Assert.That(Eval(IndicatorType.NotLeaderDiverging, [new("LEAD", 1, 0.5, 0)]), Is.True);
        Assert.That(Eval(IndicatorType.NotLeaderDiverging, [new("LEAD", 1, 0.5, 0.01)]), Is.True);
        Assert.That(Eval(IndicatorType.NotLeaderDiverging, [new("LEAD", 1, 0.5, -0.01)]), Is.False);
    }

    [Test]
    public void InverseLeaderFalling_CountsAsAPushUp_AndInfluenceWeightsTheVote()
    {
        Assert.That(Eval(IndicatorType.LeaderConfirms, [new("VIXY", 1, -0.5, -0.02)]), Is.True);
        // strong leader up 1%, weak leader down 1%: weighted move is up.
        Assert.That(Eval(IndicatorType.LeaderConfirms, [new("A", 1, 0.8, 0.01), new("B", 2, 0.2, -0.01)]), Is.True);
        Assert.That(Eval(IndicatorType.LeaderConfirms, [new("A", 1, 0.2, 0.01), new("B", 2, 0.8, -0.01)]), Is.False);
    }

    [TestCase("LeaderConfirms", IndicatorType.LeaderConfirms)]
    [TestCase("NotLeaderDiverging", IndicatorType.NotLeaderDiverging)]
    public void RoundTrips_AndDefaults(string verb, IndicatorType type)
    {
        var parsed = ScriptParser.ParseScript($"Stock.Ticker(\"X\").{verb}(3, 0.25).Long()")!;
        var cond = parsed.EntryConditions.OfType<IndicatorCondition>().Single(c => c.Type == type);
        Assert.That((cond.Parameter, cond.Parameter2), Is.EqualTo(((double?)3, (double?)0.25)));
        Assert.That(parsed.ToScript(), Does.Contain($"{verb}(3, 0.25)"));

        var defaults = ScriptParser.ParseScript($"Stock.Ticker(\"X\").{verb}().Long()")!
            .EntryConditions.OfType<IndicatorCondition>().Single(c => c.Type == type);
        Assert.That((defaults.Parameter, defaults.Parameter2), Is.EqualTo(((double?)2, (double?)0.1)));
        Assert.That(ScriptDiagnostics.Analyze($"Stock.Ticker(\"X\").{verb}(2, 0.1).Long()").Where(d => d.IsError), Is.Empty);
    }

    [Test]
    public void Builder_EmitsBothGuards()
    {
        var text = Stock.Ticker("X").LeaderConfirms(1, 0.2).NotLeaderDiverging(2, 0.1).Long().ToScript();
        Assert.That(text, Does.Contain("LeaderConfirms(1, 0.2)").And.Contain("NotLeaderDiverging(2, 0.1)"));
    }

    [Test]
    public void SnapshotBuilder_NeverComputesLeaders_SoWithoutTheMonitorTheGuardsBlock()
    {
        var snap = IndicatorSnapshotBuilder.Build("X", []);
        Assert.That(snap.Leaders, Is.Null);
    }
}
