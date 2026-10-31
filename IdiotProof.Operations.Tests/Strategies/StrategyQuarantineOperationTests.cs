using IdiotProof.Operations.Strategies;

namespace IdiotProof.Operations.Tests.Strategies;

/// <summary>strategy quarantine (IP-T151).</summary>
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
    }

    [Test]
    public async Task Quarantine_NoneFound()
    {
        var kit = new StrategyKit();
        await kit.AddAsync("NVDA");
        var result = await new StrategyQuarantineOperation().ExecuteAsync(new StrategyQuarantineArgs(), kit.Context());
        Assert.That(result.Human, Does.Contain("No quarantined strategies (1 checked)"));
    }
}
