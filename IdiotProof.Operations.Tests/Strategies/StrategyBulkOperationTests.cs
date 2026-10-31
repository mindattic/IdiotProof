using IdiotProof.Core.Trading;
using IdiotProof.Operations.Strategies;

namespace IdiotProof.Operations.Tests.Strategies;

/// <summary>strategy bulk (IP-T161): quick actions with the position guard and a batch Live code.</summary>
public class StrategyBulkOperationTests
{
    [Test]
    public async Task BulkDeactivate_SkipsHoldingStrategies()
    {
        var kit = new StrategyKit();
        var a = await kit.AddAsync("NVDA", active: true);
        var held = await kit.AddAsync("AMD", active: true, positionQty: 3);

        var result = await new StrategyBulkOperation().ExecuteAsync(new StrategyBulkArgs { Action = StrategyBulkAction.Deactivate, All = true }, kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That((await kit.ReloadAsync(a.Id)).IsActive, Is.False);
        Assert.That((await kit.ReloadAsync(held.Id)).IsActive, Is.True, "a holding strategy keeps its exit brain");
        Assert.That(result.Human, Does.Contain("skipped: holds 3").And.Contain("1 deactivated of 2"));
    }

    [Test]
    public async Task BulkActivate_BySymbol_SkipsQuarantined()
    {
        var kit = new StrategyKit();
        var nvda = await kit.AddAsync("NVDA");
        var bad = await kit.AddAsync("NVDA", rawJson: "{\"schemaVersion\":99}", title: "bad");
        var amd = await kit.AddAsync("AMD");

        var result = await new StrategyBulkOperation().ExecuteAsync(new StrategyBulkArgs { Action = StrategyBulkAction.Activate, Symbol = "nvda" }, kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That((await kit.ReloadAsync(nvda.Id)).IsActive, Is.True);
        Assert.That((await kit.ReloadAsync(bad.Id)).IsActive, Is.False);
        Assert.That((await kit.ReloadAsync(amd.Id)).IsActive, Is.False);
    }

    [Test]
    public async Task BulkActivate_WithALiveStrategy_NeedsOneCodeForThatExactBatch()
    {
        var kit = new StrategyKit();
        var paper = await kit.AddAsync("NVDA", mode: BrokerMode.Paper);
        var live = await kit.AddAsync("AMD", mode: BrokerMode.Live);
        var op = new StrategyBulkOperation();
        var args = new StrategyBulkArgs { Action = StrategyBulkAction.Activate, All = true };

        var first = await op.ExecuteAsync(args, kit.Context());
        Assert.That(first.Status, Is.EqualTo(OperationStatus.ConfirmationRequired));
        Assert.That(first.Confirmation!.Preview, Does.Contain("LIVE").And.Contain("AMD"));
        Assert.That((await kit.AllAsync()).Any(s => s.IsActive), Is.False, "nothing happens before the code");

        var wrongBatch = await op.ExecuteAsync(new StrategyBulkArgs { Action = StrategyBulkAction.Activate, Symbol = "AMD" }, kit.Context(confirm: first.Confirmation.Code));
        Assert.That(wrongBatch.Status, Is.EqualTo(OperationStatus.Blocked));

        var again = await op.ExecuteAsync(args, kit.Context());
        var done = await op.ExecuteAsync(args, kit.Context(confirm: again.Confirmation!.Code));
        Assert.That(done.IsOk, Is.True, done.Message);
        Assert.That((await kit.ReloadAsync(paper.Id)).IsActive, Is.True);
        Assert.That((await kit.ReloadAsync(live.Id)).IsActive, Is.True);
    }

    [Test]
    public async Task Bulk_NeedsASelector()
    {
        var kit = new StrategyKit();
        var r = await new StrategyBulkOperation().ExecuteAsync(new StrategyBulkArgs { Action = StrategyBulkAction.Activate }, kit.Context());
        Assert.That(r.Status, Is.EqualTo(OperationStatus.UsageError));
    }
}
