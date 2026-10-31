using IdiotProof.Core.Trading;
using IdiotProof.Operations.Strategies;
using IdiotProof.Scripting;

namespace IdiotProof.Operations.Tests.Strategies;

/// <summary>strategy set (IP-T145): field edits re-canonicalized and re-validated; Live needs a code.</summary>
public class StrategySetOperationTests
{
    [Test]
    public async Task Set_RewritesTheCanon_AndKeepsPositionBookkeeping()
    {
        var kit = new StrategyKit();
        var s = await kit.AddAsync("NVDA", positionQty: 3, active: true);
        var before = StrategyJson.Deserialize(s.ScriptJson!).ShortId();

        var result = await new StrategySetOperation().ExecuteAsync(
            new StrategySetArgs { Strategy = "NVDA", Stop = 168.5m, Target = 195m, Qty = 2, SellBy = "15:45" }, kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        var row = await kit.ReloadAsync(s.Id);
        var def = StrategyJson.Deserialize(row.ScriptJson!);
        Assert.Multiple(() =>
        {
            Assert.That(def.StopLossPrice, Is.EqualTo(168.5));
            Assert.That(def.TakeProfitPrice, Is.EqualTo(195));
            Assert.That(def.Quantity, Is.EqualTo(2));
            Assert.That(def.ExitTime, Is.EqualTo(TimeSpan.FromHours(15.75)));
            Assert.That(def.ShortId(), Is.Not.EqualTo(before));
            Assert.That(ScriptParser.ParseScript(row.ScriptText)!.StopLossPrice, Is.EqualTo(168.5), "the text view follows the canon");
            Assert.That(row.PositionQty, Is.EqualTo(3), "the Monitor's bookkeeping is untouched");
            Assert.That(row.IsActive, Is.True);
            Assert.That(result.Human, Does.Contain("next entry"));
        });
    }

    [Test]
    public async Task Set_ThatBreaksValidation_ChangesNothing()
    {
        var kit = new StrategyKit();
        var s = await kit.AddAsync("NVDA");

        var result = await new StrategySetOperation().ExecuteAsync(new StrategySetArgs { Strategy = "NVDA", Stop = 200m }, kit.Context());

        Assert.That(result.Status, Is.EqualTo(OperationStatus.Failed));
        Assert.That((await kit.ReloadAsync(s.Id)).ScriptJson, Is.EqualTo(s.ScriptJson));
    }

    [Test]
    public async Task Set_OnALiveStrategy_NeedsACodeBoundToTheChange()
    {
        var kit = new StrategyKit();
        var s = await kit.AddAsync("NVDA", mode: BrokerMode.Live);
        var op = new StrategySetOperation();

        var first = await op.ExecuteAsync(new StrategySetArgs { Strategy = "NVDA", Qty = 5 }, kit.Context());
        Assert.That(first.Status, Is.EqualTo(OperationStatus.ConfirmationRequired));
        Assert.That((await kit.ReloadAsync(s.Id)).ScriptJson, Is.EqualTo(s.ScriptJson));

        var other = await op.ExecuteAsync(new StrategySetArgs { Strategy = "NVDA", Qty = 50 }, kit.Context(confirm: first.Confirmation!.Code));
        Assert.That(other.Status, Is.EqualTo(OperationStatus.Blocked), "a code for qty 5 can't approve qty 50");

        var second = await op.ExecuteAsync(new StrategySetArgs { Strategy = "NVDA", Qty = 5 }, kit.Context());
        var ok = await op.ExecuteAsync(new StrategySetArgs { Strategy = "NVDA", Qty = 5 }, kit.Context(confirm: second.Confirmation!.Code));
        Assert.That(ok.IsOk, Is.True, ok.Message);
        Assert.That(StrategyJson.Deserialize((await kit.ReloadAsync(s.Id)).ScriptJson!).Quantity, Is.EqualTo(5));
    }

    [Test]
    public async Task Set_WithNothingOrConflicts_IsAUsageError()
    {
        var kit = new StrategyKit();
        await kit.AddAsync("NVDA");
        var op = new StrategySetOperation();

        Assert.That((await op.ExecuteAsync(new StrategySetArgs { Strategy = "NVDA" }, kit.Context())).Status, Is.EqualTo(OperationStatus.UsageError));
        Assert.That(async () => await op.ExecuteAsync(new StrategySetArgs { Strategy = "NVDA", Qty = 1, Notional = 100m }, kit.Context()),
            Throws.TypeOf<OperationException>());
    }
}
