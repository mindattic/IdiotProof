using IdiotProof.Operations.Strategies;
using IdiotProof.Scripting;

namespace IdiotProof.Operations.Tests.Strategies;

/// <summary>strategy resync-canon (IP-T152): dry run by default, owner-scoped, never regresses.</summary>
public class StrategyResyncCanonOperationTests
{
    private const string Rich = "Stock.Ticker(\"NVDA\").IsAboveVwap().IsHigherLow().Long().StopLoss(170).Build()";
    private const string Poor = "Stock.Ticker(\"NVDA\").IsAboveVwap().Long().StopLoss(170).Build()";

    [Test]
    public async Task DryRun_ReportsButWritesNothing_ApplyRepairs_OnlyMine()
    {
        var kit = new StrategyKit();
        var poorCanon = StrategyJson.Serialize(ScriptParser.ParseScript(Poor)!);
        var mine = await kit.AddAsync("NVDA", script: Rich, rawJson: poorCanon);
        var theirs = await kit.AddAsync("NVDA", script: Rich, rawJson: poorCanon, owner: Guid.NewGuid());
        var op = new StrategyResyncCanonOperation();

        var dry = await op.ExecuteAsync(new StrategyResyncCanonArgs(), kit.Context());
        Assert.That(dry.IsOk, Is.True, dry.Message);
        Assert.That(dry.Human, Does.Contain("dry run").And.Contain("would fix 1").And.Contain("scanned 1"));
        Assert.That((await kit.ReloadAsync(mine.Id)).ScriptJson, Is.EqualTo(poorCanon));

        var applied = await op.ExecuteAsync(new StrategyResyncCanonArgs { Apply = true }, kit.Context());
        Assert.That(applied.Human, Does.Contain("APPLIED").And.Contain("fixed 1"));
        Assert.That(StrategyJson.Deserialize((await kit.ReloadAsync(mine.Id)).ScriptJson!).EntryConditions, Has.Count.EqualTo(2));
        Assert.That((await kit.ReloadAsync(theirs.Id)).ScriptJson, Is.EqualTo(poorCanon), "another user's rows are never touched");
    }

    [Test]
    public async Task Resync_NeverRegressesARicherCanon()
    {
        var kit = new StrategyKit();
        var richCanon = StrategyJson.Serialize(ScriptParser.ParseScript(Rich)!);
        var s = await kit.AddAsync("NVDA", script: Poor, rawJson: richCanon);

        var result = await new StrategyResyncCanonOperation().ExecuteAsync(new StrategyResyncCanonArgs { Apply = true }, kit.Context());

        Assert.That(result.Human, Does.Contain("REGRESS"));
        Assert.That((await kit.ReloadAsync(s.Id)).ScriptJson, Is.EqualTo(richCanon));
    }
}
