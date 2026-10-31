using IdiotProof.Operations.Strategies;

namespace IdiotProof.Operations.Tests.Strategies;

/// <summary>strategy delete (IP-T149): soft-disable, restore, explicit hard delete; refuses positions.</summary>
public class StrategyDeleteOperationTests
{
    [Test]
    public async Task Delete_DeactivatesAndMarks_HidesFromList_AndRestores()
    {
        var kit = new StrategyKit();
        var s = await kit.AddAsync("NVDA", active: true);
        var canon = s.ScriptJson;
        var op = new StrategyDeleteOperation();

        var deleted = await op.ExecuteAsync(new StrategyDeleteArgs { Strategy = "NVDA" }, kit.Context());

        Assert.That(deleted.IsOk, Is.True, deleted.Message);
        var row = await kit.ReloadAsync(s.Id);
        Assert.Multiple(() =>
        {
            Assert.That(row.IsActive, Is.False);
            Assert.That(row.Title, Is.EqualTo("[deleted] NVDA test"));
            Assert.That(row.ScriptJson, Is.EqualTo(canon), "the canon is kept byte-for-byte");
        });
        var list = await new StrategyListOperation().ExecuteAsync(new StrategyListArgs(), kit.Context());
        Assert.That(list.Human, Does.Not.Contain("NVDA test"));
        var listAll = await new StrategyListOperation().ExecuteAsync(new StrategyListArgs { Deleted = true }, kit.Context());
        Assert.That(listAll.Human, Does.Contain("[deleted] NVDA test"));

        var restored = await op.ExecuteAsync(new StrategyDeleteArgs { Strategy = "NVDA", Restore = true }, kit.Context());
        Assert.That(restored.IsOk, Is.True, restored.Message);
        Assert.That((await kit.ReloadAsync(s.Id)).Title, Is.EqualTo("NVDA test"));
    }

    [Test]
    public async Task Delete_RefusesAnOpenPosition()
    {
        var kit = new StrategyKit();
        var s = await kit.AddAsync("NVDA", active: true, positionQty: 4);

        var result = await new StrategyDeleteOperation().ExecuteAsync(new StrategyDeleteArgs { Strategy = "NVDA" }, kit.Context());

        Assert.That(result.Status, Is.EqualTo(OperationStatus.Blocked));
        var row = await kit.ReloadAsync(s.Id);
        Assert.That(row.IsActive, Is.True);
        Assert.That(row.Title, Is.EqualTo("NVDA test"));
    }

    [Test]
    public async Task Hard_OnlyRemovesAnAlreadyDeletedStrategy()
    {
        var kit = new StrategyKit();
        var s = await kit.AddAsync("NVDA");
        var op = new StrategyDeleteOperation();

        var refused = await op.ExecuteAsync(new StrategyDeleteArgs { Strategy = "NVDA", Hard = true }, kit.Context());
        Assert.That(refused.Status, Is.EqualTo(OperationStatus.Blocked));
        Assert.That(await kit.AllAsync(), Has.Count.EqualTo(1));

        await op.ExecuteAsync(new StrategyDeleteArgs { Strategy = "NVDA" }, kit.Context());
        var removed = await op.ExecuteAsync(new StrategyDeleteArgs { Strategy = s.Id.ToString(), Hard = true }, kit.Context());
        Assert.That(removed.IsOk, Is.True, removed.Message);
        Assert.That(await kit.AllAsync(), Is.Empty);
    }
}
