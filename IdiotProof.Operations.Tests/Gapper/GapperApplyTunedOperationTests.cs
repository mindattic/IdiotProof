using IdiotProof.Operations.Gapper;
using IdiotProof.Scripting;

namespace IdiotProof.Operations.Tests.Gapper;

/// <summary>gapper apply-tuned (IP-T174): a saved backtest's tuned profile becomes a new gapper, only on command.</summary>
public class GapperApplyTunedOperationTests
{
    [Test]
    public async Task SavesTheTunedProfile_Inactive_ThenArmsWhenAsked()
    {
        using var kit = new GapperKit();
        var bt = await new GapperBacktestDayOperation().ExecuteAsync(GapperBacktestDayOperationTests.Args(), kit.Context());
        Assert.That(bt.IsOk, Is.True, bt.Message);
        var id = (string)bt.Data!.GetType().GetProperty("Id")!.GetValue(bt.Data)!;
        Assert.That(await kit.StrategiesAsync(), Is.Empty, "a backtest never queues anything");

        var applied = await new GapperApplyTunedOperation().ExecuteAsync(new GapperApplyTunedArgs { Run = id }, kit.Context());

        Assert.That(applied.IsOk, Is.True, applied.Message);
        var row = (await kit.StrategiesAsync()).Single();
        Assert.Multiple(() =>
        {
            Assert.That(row.Symbol, Is.EqualTo("GAPT"));
            Assert.That(row.IsActive, Is.False);
            Assert.That(row.Title, Does.Contain("tuned 07-17"));
            Assert.That(StrategyLoader.Load(row.ScriptJson, row.ScriptText).Definition!.PeakGivebackPercent, Is.Not.Null);
        });

        var armed = await new GapperApplyTunedOperation().ExecuteAsync(new GapperApplyTunedArgs { Run = id, Arm = true }, kit.Context());
        Assert.That(armed.IsOk, Is.True, armed.Message);
        Assert.That((await kit.StrategiesAsync()).Count(s => s.IsActive), Is.EqualTo(1));

        var again = await new GapperApplyTunedOperation().ExecuteAsync(new GapperApplyTunedArgs { Run = id, Arm = true }, kit.Context());
        Assert.That(again.Status, Is.EqualTo(OperationStatus.Blocked), "GAPT already has an active gapper — one per symbol");
        Assert.That(await kit.StrategiesAsync(), Has.Count.EqualTo(2), "a refused arm writes nothing");
    }

    [Test]
    public void AnUnknownRun_IsAUsageError()
    {
        using var kit = new GapperKit();
        var ex = Assert.ThrowsAsync<OperationException>(() => new GapperApplyTunedOperation().ExecuteAsync(new GapperApplyTunedArgs { Run = "gb-00000000" }, kit.Context()));
        Assert.That(ex!.Status, Is.EqualTo(OperationStatus.UsageError));
    }
}
