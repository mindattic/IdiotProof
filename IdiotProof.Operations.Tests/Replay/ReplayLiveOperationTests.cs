using IdiotProof.Core.Data;
using IdiotProof.Operations.Replay;

namespace IdiotProof.Operations.Tests.Replay;

/// <summary>replay live (IP-T177): the trade diary's real fills drawn on the day's bars as a LIVE run, re-runs replace it.</summary>
public class ReplayLiveOperationTests
{
    [Test]
    public async Task RendersTheDiaryTrades_AsOneLiveRun_AndReRunningReplacesIt()
    {
        using var w = new ReplayWorld();
        var s = await w.AddGapperAsync();
        await using (var db = await w.Db.CreateDbContextAsync())
        {
            db.TradeDiary.Add(new TradeDiaryEntry
            {
                StrategyId = s.Id, OwnerUserId = w.Owner, StrategyTitle = s.Title, Symbol = "GAPT", Broker = "Alpaca", IsPaper = true,
                EntryUtc = new DateTime(2026, 7, 17, 8, 30, 0, DateTimeKind.Utc), EntryPrice = 10m, Quantity = 100,
                ExitUtc = new DateTime(2026, 7, 17, 13, 20, 0, DateTimeKind.Utc), ExitPrice = 11m, ExitReason = "PeakGiveback",
                RealizedPnL = 100m, ReturnPercent = 10m, Status = TradeDiaryStatus.Closed,
            });
            db.TradeDiary.Add(new TradeDiaryEntry
            {
                StrategyId = s.Id, OwnerUserId = Guid.NewGuid(), StrategyTitle = "someone else", Symbol = "GAPT", Broker = "Alpaca",
                EntryUtc = new DateTime(2026, 7, 17, 8, 30, 0, DateTimeKind.Utc), EntryPrice = 10m, Quantity = 1, Status = TradeDiaryStatus.Closed,
            });
            await db.SaveChangesAsync();
        }

        var args = new ReplayLiveArgs { Date = ReplayWorld.Day, Out = w.Pages };
        var first = await new ReplayLiveOperation().ExecuteAsync(args, w.Context());
        var second = await new ReplayLiveOperation().ExecuteAsync(args, w.Context());

        Assert.That(first.IsOk, Is.True, first.Message);
        Assert.That(second.IsOk, Is.True, second.Message);
        var run = (await w.RunsAsync()).Single();
        Assert.Multiple(() =>
        {
            Assert.That(run.Kind, Is.EqualTo("live"));
            Assert.That(run.PayoffCount, Is.EqualTo(1), "only the owner's trade");
            Assert.That(run.FirstFireEt, Is.EqualTo("04:30"), "entry minute in ET");
            Assert.That(run.TotalPnl, Is.EqualTo(10));
            Assert.That(first.Human, Does.Contain("1 diary trade(s) → 1 live run(s)").And.Contain("+10.00%"));
            Assert.That(File.Exists(Path.Combine(w.Pages, "gapt", run.Stamp, "index.htm")), Is.True);
        });
    }

    [Test]
    public async Task ADayWithoutTrades_SaysSo()
    {
        using var w = new ReplayWorld();
        var result = await new ReplayLiveOperation().ExecuteAsync(new ReplayLiveArgs { Symbol = "GAPT", Date = ReplayWorld.Day, Pages = false }, w.Context());
        Assert.That(result.IsOk, Is.True);
        Assert.That(result.Human, Does.Contain("No live trades"));
    }
}
