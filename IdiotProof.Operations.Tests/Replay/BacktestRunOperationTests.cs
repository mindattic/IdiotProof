using IdiotProof.Operations.Replay;
using Microsoft.Extensions.DependencyInjection;

namespace IdiotProof.Operations.Tests.Replay;

/// <summary>backtest run (IP-T184): StrategyBacktester day by day over a range, saved for backtest conditions.</summary>
public class BacktestRunOperationTests
{
    [Test]
    public async Task BacktestsEachWeekday_TotalsTheTrades_AndSavesTheRun()
    {
        using var w = new ReplayWorld();
        await w.AddGapperAsync();

        var result = await new BacktestRunOperation().ExecuteAsync(
            new BacktestRunArgs { Strategy = "GAPT", From = new DateOnly(2026, 7, 16), To = new DateOnly(2026, 7, 18) }, w.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.Multiple(() =>
        {
            Assert.That(result.Human, Does.Contain("Thu 2026-07-16").And.Contain("Fri 2026-07-17").And.Not.Contain("Sat 2026-07-18"), "weekends skipped");
            Assert.That(result.Human, Does.Contain("trade(s)").And.Contain("total").And.Contain("backtest conditions bt-"));
        });
        var id = (string)result.Data!.GetType().GetProperty("Id")!.GetValue(result.Data)!;
        var saved = await w.Services.GetRequiredService<SavedRunStore>().LoadAsync<BacktestRunRecord>(id, "bt", w.Owner);
        Assert.That(saved!.Payload.Days, Has.Count.EqualTo(2));
        Assert.That(saved.Payload.Days[1].Conditions, Is.Not.Empty);
    }

    [Test]
    public async Task AScriptBacktests_WithoutASavedStrategy()
    {
        using var w = new ReplayWorld();
        var result = await new BacktestRunOperation().ExecuteAsync(new BacktestRunArgs
        {
            Script = "Ticker(\"GAPT\").IsAboveVwap().StopLoss(2).TakeProfit(4)", From = ReplayWorld.Day,
        }, w.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.Contain("GAPT").And.Contain("Fri 2026-07-17"));
    }

    [Test]
    public async Task RangeAndInputRules_AreUsageErrors()
    {
        using var w = new ReplayWorld();
        var none = await new BacktestRunOperation().ExecuteAsync(new BacktestRunArgs(), w.Context());
        var backwards = await new BacktestRunOperation().ExecuteAsync(new BacktestRunArgs { Script = "Ticker(\"GAPT\").IsAboveVwap().StopLoss(2)", From = new DateOnly(2026, 7, 17), To = new DateOnly(2026, 7, 10) }, w.Context());
        var tooLong = await new BacktestRunOperation().ExecuteAsync(new BacktestRunArgs { Script = "Ticker(\"GAPT\").IsAboveVwap().StopLoss(2)", From = new DateOnly(2026, 5, 1), To = new DateOnly(2026, 7, 17) }, w.Context());
        Assert.That(new[] { none.Status, backwards.Status, tooLong.Status }, Is.All.EqualTo(OperationStatus.UsageError));
    }
}
