using System.Text.Json;
using IdiotProof.Core.Data;
using IdiotProof.Core.Services;
using IdiotProof.Core.Trading;
using IdiotProof.Scripting;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Operations.Tests.Strategies;

/// <summary>
/// Quarantine auto-repair (IP-T525): the "safe to repair" rule (<see cref="CanonRepair"/>) and the
/// repository write that applies it (<see cref="StrategyRepository.RepairQuarantinedCanonAsync"/>).
/// </summary>
public class CanonRepairTests
{
    private const string Script = StrategyKit.Script;

    private static string Canon(string script) => StrategyJson.Serialize(ScriptParser.ParseScript(script)!);

    private static Strategy Row(string script, string? canon, string mode = "Sandbox", int positionQty = 0) => new()
    {
        OwnerUserId = Guid.NewGuid(), Title = "NVDA test", Symbol = "NVDA", ScriptText = script, ScriptJson = canon,
        BrokerMode = mode, PositionQty = positionQty, IsActive = true,
    };

    // ── The rule ────────────────────────────────────────────────────────

    [Test]
    public void Plan_IsNull_ForAStrategyThatIsNotQuarantined()
    {
        Assert.That(CanonRepair.Plan(Row(Script, Canon(Script))), Is.Null);
        Assert.That(CanonRepair.Plan(Row(Script, null)), Is.Null, "a legacy row whose script parses is not quarantined");
    }

    [Test]
    public void Plan_IsSafe_WhenTheScriptSaysEverythingTheUnreadableCanonSaid()
    {
        var plan = CanonRepair.Plan(Row(Script, StrategyKit.UnreadableCanon(Script)))!;

        Assert.Multiple(() =>
        {
            Assert.That(plan.Safe, Is.True, plan.Blocker);
            Assert.That(plan.Reason, Does.Contain("legacyNote"));
            Assert.That(plan.ProposedCanon, Is.EqualTo(Canon(Script)));
            Assert.That(StrategyLoader.Load(plan.ProposedCanon, null).CanonicalError, Is.Null, "the proposal passes the strict reader");
            Assert.That(plan.Changes, Is.Not.Empty);
        });
    }

    [Test]
    public void Plan_Refuses_WhenTheScriptHasFewerEntryConditionsThanTheCanon()
    {
        // The stored canon has a condition of a type this build does not know; the script lacks it.
        var canon = Canon(Script).Replace("\"entryConditions\": [", "\"entryConditions\": [\n    { \"type\": \"moonPhase\" },");

        var plan = CanonRepair.Plan(Row(Script, canon))!;

        Assert.Multiple(() =>
        {
            Assert.That(plan.Safe, Is.False);
            Assert.That(plan.ContentBlocker, Does.Contain("would lose").And.Contain("entry conditions 2 → 1"));
            Assert.That(plan.ProposedCanon, Is.Not.Null, "the proposal is still shown for the owner to judge");
        });
    }

    [Test]
    public void Plan_Refuses_WhenAnExitTheCanonSetIsMissingFromTheScript()
    {
        var withoutStop = "Stock.Ticker(\"NVDA\").IsAboveVwap().Long().TakeProfit(190).Build()";

        var plan = CanonRepair.Plan(Row(withoutStop, StrategyKit.UnreadableCanon(Script)))!;

        Assert.That(plan.Safe, Is.False);
        Assert.That(plan.ContentBlocker, Does.Contain("stopLossPrice dropped"));
    }

    [Test]
    public void Plan_Refuses_WhenTheScriptWouldChangeDirectionOrSize()
    {
        var shortScript = "Stock.Ticker(\"NVDA\").IsAboveVwap().Short().StopLoss(170).TakeProfit(190).Build()";
        var bigger = "Stock.Ticker(\"NVDA\").Quantity(500).IsAboveVwap().Long().StopLoss(170).TakeProfit(190).Build()";

        Assert.That(CanonRepair.Plan(Row(shortScript, StrategyKit.UnreadableCanon(Script)))!.ContentBlocker, Does.Contain("direction Long → Short"));
        Assert.That(CanonRepair.Plan(Row(bigger, StrategyKit.UnreadableCanon(Script)))!.ContentBlocker, Does.Contain("quantity"));
    }

    [Test]
    public void Plan_Refuses_WhenTheScriptHasAnErrorDiagnostic()
    {
        var typo = "Stock.Ticker(\"NVDA\").IsAboveVwapp().Long().StopLoss(170).TakeProfit(190).Build()";

        var plan = CanonRepair.Plan(Row(typo, StrategyKit.UnreadableCanon(Script)))!;

        Assert.Multiple(() =>
        {
            Assert.That(plan.Safe, Is.False);
            Assert.That(plan.ContentBlocker, Does.Contain("IPS001"));
            Assert.That(plan.ProposedCanon, Is.Null);
        });
    }

    [Test]
    public void Plan_Refuses_WhenTheStoredCanonCannotBeCounted_OrThereIsNoCanon()
    {
        Assert.That(CanonRepair.Plan(Row(Script, "{not json"))!.ContentBlocker, Does.Contain("not JSON"));
        Assert.That(CanonRepair.Plan(Row(Script, "{\"schemaVersion\":99}"))!.ContentBlocker, Does.Contain("schema version"));
        Assert.That(CanonRepair.Plan(Row(Script, "[]"))!.ContentBlocker, Does.Contain("not a JSON object"));
        Assert.That(CanonRepair.Plan(Row("this is not idiotscript", null))!.ContentBlocker, Does.Contain("nothing to derive"));
    }

    [Test]
    public void Plan_Refuses_ASequenceThatWouldLoseAStep()
    {
        var two = Stock.Ticker("NVDA").Step("ride").IsAboveVwap().StopLossPercent(10).TakeProfitPercent(20)
            .ThenWhen(c => c.IsBelowVwap())
            .Step("fade").IsBelowVwap().Short().StopLossPercent(10).TakeProfitPercent(20).Build();
        var one = Stock.Ticker("NVDA").IsAboveVwap().StopLossPercent(10).TakeProfitPercent(20).Build();
        var canon = StrategyJson.Serialize(two).Replace("\"schemaVersion\": 1,", "\"schemaVersion\": 1,\n  \"legacyNote\": \"x\",");

        var plan = CanonRepair.Plan(Row(one.ToScript(), canon))!;

        Assert.That(plan.Safe, Is.False);
        Assert.That(plan.ContentBlocker, Does.Contain("steps 2 → 1"));
    }

    [Test]
    public void Plan_BlocksOnState_WhenHoldingOrMidSequence()
    {
        var holding = Row(Script, StrategyKit.UnreadableCanon(Script), positionQty: 7);
        var midway = Row(Script, StrategyKit.UnreadableCanon(Script));
        midway.AwaitingTransition = true;

        Assert.Multiple(() =>
        {
            Assert.That(CanonRepair.Plan(holding)!.Safe, Is.False);
            Assert.That(CanonRepair.Plan(holding)!.StateBlocker, Does.Contain("holds a position"));
            Assert.That(CanonRepair.Plan(midway)!.StateBlocker, Does.Contain("sequence"));
        });
    }

    [Test]
    public void Policy_DefaultsOnForPaperAndSandbox_OffForLive()
    {
        var policy = CanonRepairPolicy.Default;
        Assert.Multiple(() =>
        {
            Assert.That(policy.Blocker(Row(Script, null, "Paper")), Is.Null);
            Assert.That(policy.Blocker(Row(Script, null, "Sandbox")), Is.Null);
            Assert.That(policy.Blocker(Row(Script, null, "Live")), Does.Contain(CanonRepairPolicy.LiveKey));
            Assert.That(policy.Blocker(Row(Script, null, "somewhere else")), Is.Not.Null, "an unknown route is treated as Live");
            Assert.That((policy with { Live = true }).Blocker(Row(Script, null, "Live")), Is.Null);
            Assert.That(new CanonRepairPolicy(false, true).Blocker(Row(Script, null, "Paper")), Does.Contain(CanonRepairPolicy.EnabledKey));
        });
    }

    [Test]
    public async Task Policy_ReadsItsTwoSettings_AndBothAreRegistered()
    {
        var kit = new StrategyKit();
        Assert.That(await CanonRepairPolicy.ReadAsync(kit.Settings), Is.EqualTo(CanonRepairPolicy.Default));

        await kit.Settings.SetAsync(CanonRepairPolicy.EnabledKey, "false");
        await kit.Settings.SetAsync(CanonRepairPolicy.LiveKey, "true");

        Assert.Multiple(async () =>
        {
            Assert.That(await CanonRepairPolicy.ReadAsync(kit.Settings), Is.EqualTo(new CanonRepairPolicy(false, true)));
            Assert.That(SettingsMetadata.Find(CanonRepairPolicy.EnabledKey)?.Default, Is.EqualTo("true"));
            Assert.That(SettingsMetadata.Find(CanonRepairPolicy.LiveKey)?.Default, Is.EqualTo("false"));
        });
    }

    // ── The repository write ────────────────────────────────────────────

    [Test]
    public async Task Repair_RewritesOnlyTheCanon_AndWritesTheAuditRowWithBeforeAndAfter()
    {
        var kit = new StrategyKit();
        var broken = StrategyKit.UnreadableCanon(Script);
        var s = await kit.AddAsync("NVDA", rawJson: broken, active: true);

        var result = await kit.Repo.RepairQuarantinedCanonAsync(s.Id, CanonRepairPolicy.Default, "the Monitor");

        var row = await kit.ReloadAsync(s.Id);
        await using var db = await kit.Db.CreateDbContextAsync();
        var audit = await db.AuditLogs.AsNoTracking().SingleAsync(a => a.Category == CanonRepair.AuditCategory);
        using var data = JsonDocument.Parse(audit.DataJson!);
        Assert.Multiple(() =>
        {
            Assert.That(result.Repaired, Is.True, result.Blocker);
            Assert.That(row.ScriptJson, Is.EqualTo(Canon(Script)));
            Assert.That(StrategyLoader.Load(row.ScriptJson, row.ScriptText).Definition, Is.Not.Null, "re-read, it passes the strict reader");
            Assert.That(row.ScriptText, Is.EqualTo(s.ScriptText));
            Assert.That(row.IsActive, Is.True);
            Assert.That(row.BrokerMode, Is.EqualTo(s.BrokerMode));
            Assert.That(row.PositionQty, Is.EqualTo(0));
            Assert.That(audit.UserId, Is.EqualTo(kit.Owner));
            Assert.That(audit.Message, Does.Contain("NVDA").And.Contain("quarantine lifted"));
            Assert.That(data.RootElement.GetProperty("quarantineReason").GetString(), Does.Contain("legacyNote"));
            Assert.That(data.RootElement.GetProperty("canonBefore").GetString(), Is.EqualTo(broken));
            Assert.That(data.RootElement.GetProperty("canonAfter").GetString(), Is.EqualTo(row.ScriptJson));
            Assert.That(data.RootElement.GetProperty("strategyId").GetString(), Is.EqualTo(s.Id.ToString()));
        });
    }

    [Test]
    public async Task Repair_LeavesTheRowAlone_AndWritesNoAudit_WhenAnythingBlocksIt()
    {
        var kit = new StrategyKit();
        var broken = StrategyKit.UnreadableCanon(Script);
        var holding = await kit.AddAsync("NVDA", rawJson: broken, active: true, positionQty: 5);
        var live = await kit.AddAsync("NVDA", rawJson: broken, active: true, mode: BrokerMode.Live);
        var lossy = await kit.AddAsync("NVDA", rawJson: Canon(Script).Replace("\"entryConditions\": [", "\"entryConditions\": [\n    { \"type\": \"moonPhase\" },"), active: true);
        var healthy = await kit.AddAsync("NVDA", active: true);
        var flat = await kit.AddAsync("NVDA", rawJson: broken, active: true);

        var onHolding = await kit.Repo.RepairQuarantinedCanonAsync(holding.Id, CanonRepairPolicy.Default, "test");
        var onLive = await kit.Repo.RepairQuarantinedCanonAsync(live.Id, CanonRepairPolicy.Default, "test");
        var onLossy = await kit.Repo.RepairQuarantinedCanonAsync(lossy.Id, CanonRepairPolicy.Default, "test");
        var onHealthy = await kit.Repo.RepairQuarantinedCanonAsync(healthy.Id, CanonRepairPolicy.Default, "test");
        var switchedOff = await kit.Repo.RepairQuarantinedCanonAsync(flat.Id, new CanonRepairPolicy(false, false), "test");
        var missing = await kit.Repo.RepairQuarantinedCanonAsync(Guid.NewGuid(), CanonRepairPolicy.Default, "test");

        await using var db = await kit.Db.CreateDbContextAsync();
        Assert.Multiple(async () =>
        {
            Assert.That(onHolding.Repaired, Is.False);
            Assert.That(onHolding.Blocker, Does.Contain("holds a position"));
            Assert.That(onHolding.Retryable, Is.True);
            Assert.That(onLive.Repaired, Is.False);
            Assert.That(onLive.Blocker, Does.Contain("Live"));
            Assert.That(onLossy.Repaired, Is.False);
            Assert.That(onLossy.Retryable, Is.False, "a script that would lose a condition is not asked again until it changes");
            Assert.That(onHealthy.Repaired, Is.False);
            Assert.That(switchedOff.Repaired, Is.False);
            Assert.That(switchedOff.Blocker, Does.Contain(CanonRepairPolicy.EnabledKey));
            Assert.That(missing.Repaired, Is.False);
            Assert.That((await kit.ReloadAsync(holding.Id)).ScriptJson, Is.EqualTo(broken));
            Assert.That((await kit.ReloadAsync(live.Id)).ScriptJson, Is.EqualTo(broken));
            Assert.That((await kit.ReloadAsync(lossy.Id)).ScriptJson, Is.EqualTo(lossy.ScriptJson));
            Assert.That((await kit.ReloadAsync(flat.Id)).ScriptJson, Is.EqualTo(broken));
            Assert.That(await db.AuditLogs.CountAsync(), Is.EqualTo(0));
        });
    }

    [Test]
    public async Task Repair_OfALiveStrategy_NeedsTheLiveSwitch()
    {
        var kit = new StrategyKit();
        var live = await kit.AddAsync("NVDA", rawJson: StrategyKit.UnreadableCanon(Script), active: true, mode: BrokerMode.Live);

        var result = await kit.Repo.RepairQuarantinedCanonAsync(live.Id, new CanonRepairPolicy(true, true), "test");

        Assert.That(result.Repaired, Is.True, result.Blocker);
        Assert.That((await kit.ReloadAsync(live.Id)).ScriptJson, Is.EqualTo(Canon(Script)));
    }

    [Test]
    public void Diff_ShowsOnlyTheLinesThatChange()
    {
        var diff = CanonRepair.Diff(StrategyKit.UnreadableCanon(Script), Canon(Script));

        Assert.That(diff, Is.EqualTo(new[] { "-   \"legacyNote\": \"x\"," }));
    }
}
