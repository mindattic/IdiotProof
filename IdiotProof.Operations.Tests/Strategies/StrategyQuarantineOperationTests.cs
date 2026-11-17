using IdiotProof.Core.Services;
using IdiotProof.Core.Trading;
using IdiotProof.Operations.Strategies;

namespace IdiotProof.Operations.Tests.Strategies;

/// <summary>strategy quarantine (IP-T151), with why each was not repaired automatically and the proposed repair (IP-T525).</summary>
public class StrategyQuarantineOperationTests
{
    [Test]
    public async Task Quarantine_ListsUnreadableCanons_HoldingOnesFirst()
    {
        var kit = new StrategyKit();
        await kit.AddAsync("NVDA");
        await kit.AddAsync("BAD", rawJson: "{\"schemaVersion\":99}");
        await kit.AddAsync("HELD", rawJson: "{not json", active: true, positionQty: 7);
        await kit.AddAsync("OLD", script: "this is not idiotscript", noCanon: true);

        var result = await new StrategyQuarantineOperation().ExecuteAsync(new StrategyQuarantineArgs(), kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.Contain("3 of 4 strategies quarantined").And.Contain("WARNING: 1 hold a position"));
        Assert.That(result.Human, Does.Not.Contain("NVDA"));
        Assert.That(result.Human!.IndexOf("HELD", StringComparison.Ordinal), Is.LessThan(result.Human.IndexOf("BAD", StringComparison.Ordinal)));
        Assert.That(result.Human, Does.Contain("script doesn't parse"));
        Assert.That(result.Human, Does.Contain("not repaired automatically").And.Contain("holds a position").And.Contain("nothing to derive"));
    }

    [Test]
    public async Task Quarantine_NoneFound()
    {
        var kit = new StrategyKit();
        await kit.AddAsync("NVDA");
        var result = await new StrategyQuarantineOperation().ExecuteAsync(new StrategyQuarantineArgs(), kit.Context());
        Assert.That(result.Human, Does.Contain("No quarantined strategies (1 checked)"));
    }

    [Test]
    public async Task Quarantine_ShowsTheProposedRepair_AndWhatBlocksTheMonitor_WithoutChangingAnything()
    {
        var kit = new StrategyKit();
        var script = StrategyKit.Script.Replace("NVDA", "AAA");
        var broken = StrategyKit.UnreadableCanon(script);
        var eligible = await kit.AddAsync("AAA", rawJson: broken, active: true);
        await kit.AddAsync("BBB", rawJson: StrategyKit.UnreadableCanon(StrategyKit.Script.Replace("NVDA", "BBB")), active: true, mode: BrokerMode.Live);
        await kit.AddAsync("CCC", rawJson: StrategyKit.UnreadableCanon(StrategyKit.Script.Replace("NVDA", "CCC")));

        var result = await new StrategyQuarantineOperation().ExecuteAsync(new StrategyQuarantineArgs { Diff = true }, kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.Contain("nothing blocks it; the Monitor repairs it the next time it evaluates AAA"));
        Assert.That(result.Human, Does.Contain(CanonRepairPolicy.LiveKey), "the Live strategy names the switch that keeps it quarantined");
        Assert.That(result.Human, Does.Contain("it is not active"), "the inactive strategy is never seen by the Monitor");
        Assert.That(result.Human, Does.Contain("proposed repair (canon re-derived from the script)").And.Contain("-   \"legacyNote\": \"x\","));
        Assert.That(result.Human, Does.Contain("strategy resync-canon"));
        Assert.That((await kit.ReloadAsync(eligible.Id)).ScriptJson, Is.EqualTo(broken), "a read: nothing is repaired by listing");
    }

    [Test]
    public async Task Quarantine_SaysWhenAutomaticRepairIsSwitchedOff()
    {
        var kit = new StrategyKit();
        await kit.Settings.SetAsync(CanonRepairPolicy.EnabledKey, "false");
        await kit.AddAsync("AAA", rawJson: StrategyKit.UnreadableCanon(StrategyKit.Script.Replace("NVDA", "AAA")), active: true);

        var result = await new StrategyQuarantineOperation().ExecuteAsync(new StrategyQuarantineArgs(), kit.Context());

        Assert.That(result.Human, Does.Contain(CanonRepairPolicy.EnabledKey).And.Contain("Re-run with --diff"));
    }
}
