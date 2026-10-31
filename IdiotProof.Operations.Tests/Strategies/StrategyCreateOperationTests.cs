using IdiotProof.Core.Trading;
using IdiotProof.Operations.Strategies;
using IdiotProof.Scripting;

namespace IdiotProof.Operations.Tests.Strategies;

/// <summary>strategy create (IP-T143): validated, inactive, Sandbox by default, 1 share when unsized.</summary>
public class StrategyCreateOperationTests
{
    [Test]
    public async Task Create_FromScript_SavesInactiveSandbox_WithOneShareDefault()
    {
        var kit = new StrategyKit();

        var result = await new StrategyCreateOperation().ExecuteAsync(new StrategyCreateArgs { Script = StrategyKit.Script }, kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        var row = (await kit.AllAsync()).Single();
        Assert.Multiple(() =>
        {
            Assert.That(row.IsActive, Is.False);
            Assert.That(row.BrokerMode, Is.EqualTo("Sandbox"));
            Assert.That(row.Symbol, Is.EqualTo("NVDA"));
            Assert.That(StrategyJson.Deserialize(row.ScriptJson!).Quantity, Is.EqualTo(1), "an unsized strategy buys 1 share");
            Assert.That(result.Human, Does.Contain("quantity 1 share").And.Contain("Created NVDA"));
        });
    }

    [Test]
    public async Task Create_FromJson_KeepsTheCanon_AndHonoursPaper()
    {
        var kit = new StrategyKit();
        var def = ScriptParser.ParseScript(StrategyKit.Script)!;
        def.Quantity = 5;
        var json = StrategyJson.Serialize(def);

        var result = await new StrategyCreateOperation().ExecuteAsync(
            new StrategyCreateArgs { Canon = json, Mode = BrokerMode.Paper, Title = "From JSON" }, kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        var row = (await kit.AllAsync()).Single();
        Assert.That(row.BrokerMode, Is.EqualTo("Paper"));
        Assert.That(row.Title, Is.EqualTo("From JSON"));
        Assert.That(StrategyJson.Deserialize(row.ScriptJson!).ShortId(), Is.EqualTo(def.ShortId()));
    }

    [Test]
    public async Task Create_WithoutAStop_IsRefused_AndSavesNothing()
    {
        var kit = new StrategyKit();

        var result = await new StrategyCreateOperation().ExecuteAsync(
            new StrategyCreateArgs { Script = "Stock.Ticker(\"AMD\").IsAboveVwap().Long().TakeProfit(200).Build()" }, kit.Context());

        Assert.That(result.Status, Is.EqualTo(OperationStatus.Failed));
        Assert.That(result.Message, Does.Contain("No stop loss"));
        Assert.That(await kit.AllAsync(), Is.Empty);
    }

    [Test]
    public async Task Create_StopAboveTargetOnALong_IsRefused()
    {
        var kit = new StrategyKit();

        var result = await new StrategyCreateOperation().ExecuteAsync(
            new StrategyCreateArgs { Script = "Stock.Ticker(\"AMD\").Long().StopLoss(210).TakeProfit(200).Build()" }, kit.Context());

        Assert.That(result.Status, Is.EqualTo(OperationStatus.Failed));
        Assert.That(result.Message, Does.Contain("must sit below its target"));
    }

    [Test]
    public async Task Create_Live_ScriptAndJson_AndDryRun_AreHandled()
    {
        var kit = new StrategyKit();
        var op = new StrategyCreateOperation();

        Assert.That(async () => await op.ExecuteAsync(new StrategyCreateArgs { Script = StrategyKit.Script, Mode = BrokerMode.Live }, kit.Context()),
            Throws.TypeOf<OperationException>().With.Property("Status").EqualTo(OperationStatus.UsageError));
        var both = await op.ExecuteAsync(new StrategyCreateArgs { Script = StrategyKit.Script, Canon = "{}" }, kit.Context());
        Assert.That(both.Status, Is.EqualTo(OperationStatus.UsageError));

        var dry = await op.ExecuteAsync(new StrategyCreateArgs { Script = StrategyKit.Script, DryRun = true }, kit.Context());
        Assert.That(dry.IsOk, Is.True, dry.Message);
        Assert.That(dry.Human, Does.Contain("Dry run"));
        Assert.That(await kit.AllAsync(), Is.Empty);
    }
}
