using IdiotProof.Operations.Strategies;

namespace IdiotProof.Operations.Tests.Strategies;

/// <summary>strategy rename (IP-T157): title only; canon and state untouched.</summary>
public class StrategyRenameOperationTests
{
    [Test]
    public async Task Rename_ChangesOnlyTheTitle()
    {
        var kit = new StrategyKit();
        var s = await kit.AddAsync("NVDA", active: true, positionQty: 2);

        var result = await new StrategyRenameOperation().ExecuteAsync(new StrategyRenameArgs { Strategy = "NVDA", Title = "NVDA VWAP reclaim" }, kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        var row = await kit.ReloadAsync(s.Id);
        Assert.Multiple(() =>
        {
            Assert.That(row.Title, Is.EqualTo("NVDA VWAP reclaim"));
            Assert.That(row.ScriptJson, Is.EqualTo(s.ScriptJson));
            Assert.That(row.IsActive, Is.True);
            Assert.That(row.PositionQty, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task Rename_RefusesEmptyAndTheDeletedMarker()
    {
        var kit = new StrategyKit();
        await kit.AddAsync("NVDA");
        var op = new StrategyRenameOperation();

        Assert.That((await op.ExecuteAsync(new StrategyRenameArgs { Strategy = "NVDA", Title = " " }, kit.Context())).Status, Is.EqualTo(OperationStatus.UsageError));
        Assert.That((await op.ExecuteAsync(new StrategyRenameArgs { Strategy = "NVDA", Title = "[deleted] x" }, kit.Context())).Status, Is.EqualTo(OperationStatus.UsageError));
    }
}
