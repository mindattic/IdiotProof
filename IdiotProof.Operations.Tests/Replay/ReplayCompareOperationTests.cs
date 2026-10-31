using IdiotProof.Blazor.Data;
using IdiotProof.Operations.Replay;

namespace IdiotProof.Operations.Tests.Replay;

/// <summary>replay compare (IP-T183): two runs side by side with the P&amp;L difference.</summary>
public class ReplayCompareOperationTests
{
    [Test]
    public async Task ComparesTwoRuns_AndSaysWhichDidBetter()
    {
        using var w = new ReplayWorld();
        var a = await ReplayListOperationTests.SeedRunAsync(w, "NVDA", "2026-10-29", fired: true, pnl: 1.5);
        var b = await ReplayListOperationTests.SeedRunAsync(w, "NVDA", "2026-10-29", fired: true, pnl: 4.25);
        await using (var db = await w.Db.CreateDbContextAsync())
        {
            db.ReplayTrades.Add(new ReplayTrade { ReplayRunId = a.Id, Symbol = "NVDA", EntryEt = "04:31", ExitEt = "05:01", HoldMin = 30, PnlPct = 1.5, Won = true, Reason = "TrailingStop" });
            db.ReplayTrades.Add(new ReplayTrade { ReplayRunId = b.Id, Symbol = "NVDA", EntryEt = "04:31", ExitEt = "06:01", HoldMin = 90, PnlPct = 5.25, Won = true, Reason = "PeakGiveback" });
            db.ReplayTrades.Add(new ReplayTrade { ReplayRunId = b.Id, Symbol = "NVDA", EntryEt = "07:00", ExitEt = "07:10", HoldMin = 10, PnlPct = -1, Won = false, Reason = "StopLoss" });
            await db.SaveChangesAsync();
        }

        var result = await new ReplayCompareOperation().ExecuteAsync(new ReplayCompareArgs { RunA = a.Id.ToString(), RunB = b.Id.ToString() }, w.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.Multiple(() =>
        {
            Assert.That(result.Human, Does.Contain("+1.50%").And.Contain("+4.25%").And.Contain("+2.75%"));
            Assert.That(result.Human, Does.Contain("100%").And.Contain("50%"), "win rates");
            Assert.That(result.Human, Does.Contain("B did better by 2.75%"));
        });

        var same = await new ReplayCompareOperation().ExecuteAsync(new ReplayCompareArgs { RunA = a.Id.ToString(), RunB = a.Id.ToString() }, w.Context());
        Assert.That(same.Status, Is.EqualTo(OperationStatus.UsageError));
    }
}
