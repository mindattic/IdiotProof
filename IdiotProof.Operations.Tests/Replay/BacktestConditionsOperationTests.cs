using IdiotProof.Operations.Replay;

namespace IdiotProof.Operations.Tests.Replay;

/// <summary>backtest conditions (IP-T185): a saved backtest day's per-candle condition truth table.</summary>
public class BacktestConditionsOperationTests
{
    [Test]
    public async Task ShowsEachConditionsTruth_PerCandle_WithALegend()
    {
        using var w = new ReplayWorld();
        await w.AddGapperAsync();
        var bt = await new BacktestRunOperation().ExecuteAsync(new BacktestRunArgs { Strategy = "GAPT", From = ReplayWorld.Day }, w.Context());
        Assert.That(bt.IsOk, Is.True, bt.Message);
        var id = (string)bt.Data!.GetType().GetProperty("Id")!.GetValue(bt.Data)!;

        var changes = await new BacktestConditionsOperation().ExecuteAsync(new BacktestConditionsArgs { Run = id }, w.Context());
        var all = await new BacktestConditionsOperation().ExecuteAsync(new BacktestConditionsArgs { Run = id, Date = ReplayWorld.Day, All = true }, w.Context());

        Assert.That(changes.IsOk, Is.True, changes.Message);
        Assert.Multiple(() =>
        {
            Assert.That(changes.Human, Does.Contain("c1 = ").And.Contain("IsGap").And.Contain("✓").And.Contain("where something changed"));
            Assert.That(changes.Human, Does.Match(@"\d{1,2}:\d{2} [AP]M"), "candle times in ET");
            Assert.That(all.Human!.Split('\n').Length, Is.GreaterThan(changes.Human!.Split('\n').Length));
        });
    }

    [Test]
    public async Task AnotherOwnersOrUnknownRun_IsAUsageError()
    {
        using var w = new ReplayWorld();
        var ex = Assert.ThrowsAsync<OperationException>(() => new BacktestConditionsOperation().ExecuteAsync(new BacktestConditionsArgs { Run = "bt-00000000" }, w.Context()));
        Assert.That(ex!.Status, Is.EqualTo(OperationStatus.UsageError));
        await Task.CompletedTask;
    }
}
