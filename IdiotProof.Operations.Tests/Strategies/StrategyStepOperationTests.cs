using IdiotProof.Core.Data;
using IdiotProof.Operations.Strategies;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Operations.Tests.Strategies;

/// <summary>strategy show / strategy step / strategy explain on a sequenced strategy (IP-T445).</summary>
public class StrategyStepOperationTests
{
    private const string TwoStep =
        "Stock.Ticker(\"NVDA\").Step(\"short-into-earnings\").IsBelowVwap().Short().StopLossPercent(3).TakeProfitPercent(6)" +
        ".ThenWhen(HigherLows(3).And(OnVwapReclaim())).Within(7).Step(\"buy-back\").IsAboveVwap().Long().StopLossPercent(2).TakeProfitPercent(5).Build()";

    private static readonly DateTime ExitUtc = new(2026, 7, 14, 15, 20, 0, DateTimeKind.Utc); // 11:20 ET

    private static async Task<Strategy> AddAwaitingAsync(StrategyKit kit, int stepIndex = 0, bool awaiting = true, int positionQty = 0)
    {
        var s = await kit.AddAsync("NVDA", TwoStep, active: true, positionQty: positionQty);
        await using var db = await kit.Db.CreateDbContextAsync();
        var row = await db.Strategies.SingleAsync(x => x.Id == s.Id);
        row.StepIndex = stepIndex;
        row.AwaitingTransition = awaiting;
        row.LastExitedUtc = awaiting ? ExitUtc : null;
        row.StepArmedUtc = stepIndex > 0 ? ExitUtc.AddMinutes(5) : null;
        await db.SaveChangesAsync();
        return s;
    }

    [Test]
    public async Task Show_PrintsTheSequenceRow_AndTheJsonCarriesTheStepState()
    {
        var kit = new StrategyKit();
        await AddAwaitingAsync(kit, stepIndex: 0, awaiting: true);

        var result = await new StrategyShowOperation().ExecuteAsync(new StrategyRefArgs { Strategy = "NVDA" }, kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.Contain("sequence").And.Contain("step 1/2 \"short-into-earnings\" (awaiting transition since").And.Contain("11:20"));
        var json = System.Text.Json.JsonSerializer.Serialize(result.Data);
        Assert.That(json, Does.Contain("\"StepIndex\":0").And.Contain("\"StepCount\":2").And.Contain("\"AwaitingTransition\":true"));
    }

    [Test]
    public async Task Show_OnAnArmedLaterStep_SaysSo()
    {
        var kit = new StrategyKit();
        await AddAwaitingAsync(kit, stepIndex: 1, awaiting: false);

        var result = await new StrategyShowOperation().ExecuteAsync(new StrategyRefArgs { Strategy = "NVDA" }, kit.Context());

        Assert.That(result.Human, Does.Contain("step 2/2 \"buy-back\" (armed"));
    }

    [Test]
    public async Task Show_OnASingleStepStrategy_HasNoSequenceRow()
    {
        var kit = new StrategyKit();
        await kit.AddAsync("NVDA");

        var result = await new StrategyShowOperation().ExecuteAsync(new StrategyRefArgs { Strategy = "NVDA" }, kit.Context());

        Assert.That(result.Human, Does.Not.Contain("sequence"));
        Assert.That(System.Text.Json.JsonSerializer.Serialize(result.Data), Does.Contain("\"StepCount\":1"));
    }

    [Test]
    public async Task Step_ReportsWhereTheSequenceStands()
    {
        var kit = new StrategyKit();
        await AddAwaitingAsync(kit, stepIndex: 1, awaiting: false);

        var result = await new StrategyStepOperation().ExecuteAsync(new StrategyStepArgs { Strategy = "NVDA" }, kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.Contain("step 2/2 \"buy-back\" (armed"));
    }

    [Test]
    public async Task StepReset_PutsTheSequenceBackAtStepOne()
    {
        var kit = new StrategyKit();
        var s = await AddAwaitingAsync(kit, stepIndex: 1, awaiting: false);

        var result = await new StrategyStepOperation().ExecuteAsync(new StrategyStepArgs { Strategy = "NVDA", Reset = true }, kit.Context());
        var row = await kit.ReloadAsync(s.Id);

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.Contain("back at step 1/2 \"short-into-earnings\"").And.Contain("next tick"));
        Assert.Multiple(() =>
        {
            Assert.That(row.StepIndex, Is.EqualTo(0));
            Assert.That(row.AwaitingTransition, Is.False);
            Assert.That(row.StepArmedUtc, Is.Null);
            Assert.That(row.IsActive, Is.True, "a reset never changes the active flag");
        });
    }

    [Test]
    public async Task StepReset_IsRefusedWhileTheStepHoldsAPosition()
    {
        var kit = new StrategyKit();
        var s = await AddAwaitingAsync(kit, stepIndex: 1, awaiting: false, positionQty: 3);

        var result = await new StrategyStepOperation().ExecuteAsync(new StrategyStepArgs { Strategy = "NVDA", Reset = true }, kit.Context());
        var row = await kit.ReloadAsync(s.Id);

        Assert.That(result.Status, Is.EqualTo(OperationStatus.Blocked));
        Assert.That(result.Message, Does.Contain("holds 3 shares in step 2"));
        Assert.That(row.StepIndex, Is.EqualTo(1));
    }

    [Test]
    public async Task StepReset_OnASingleStepStrategy_SaysThereIsNoSequence()
    {
        var kit = new StrategyKit();
        await kit.AddAsync("NVDA");

        var result = await new StrategyStepOperation().ExecuteAsync(new StrategyStepArgs { Strategy = "NVDA", Reset = true }, kit.Context());

        Assert.That(result.IsOk, Is.True);
        Assert.That(result.Human, Does.Contain("single-step"));
    }

    [Test]
    public async Task Explain_NarratesEachStep_AndTheTransition()
    {
        var kit = new StrategyKit();
        await AddAwaitingAsync(kit, stepIndex: 0, awaiting: true);

        var result = await new StrategyExplainOperation().ExecuteAsync(new StrategyRefArgs { Strategy = "NVDA" }, kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.Contain("Step     1 of 2 \"short-into-earnings\"").And.Contain("Step     2 of 2 \"buy-back\""));
        Assert.That(result.Human, Does.Contain("Sells short 1 share").And.Contain("Buys 1 share"));
        Assert.That(result.Human, Does.Contain("Then when").And.Contain("price has made at least 3 higher lows in a row and price reclaims VWAP")
            .And.Contain("within 7 days of the exit, the sequence ends"));
        Assert.That(result.Human, Does.Contain("sequence is complete and the strategy deactivates itself"));
        Assert.That(result.Human, Does.Contain("Sequence: step 1/2 \"short-into-earnings\" (awaiting transition since"));
    }
}
