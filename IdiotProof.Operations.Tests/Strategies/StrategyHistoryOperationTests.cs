using IdiotProof.Core.Data;
using IdiotProof.Operations.Strategies;

namespace IdiotProof.Operations.Tests.Strategies;

/// <summary>strategy history (IP-T155): audit + diary + ledger trail, newest first.</summary>
public class StrategyHistoryOperationTests
{
    [Test]
    public async Task History_GathersAuditDiaryAndLedger_ForThisStrategyOnly()
    {
        var kit = new StrategyKit();
        var s = await kit.AddAsync("NVDA", title: "VWAP");
        await kit.AddAsync("NVDA", title: "Other");
        var t0 = new DateTime(2026, 10, 29, 13, 35, 0, DateTimeKind.Utc);
        await kit.AddAuditAsync("signal-fire", "[VWAP] NVDA all 1 conditions met — entering gate checks", t0);
        await kit.AddAuditAsync("signal-fire", "[Other] NVDA all 1 conditions met — entering gate checks", t0);
        await kit.AddAuditAsync("signal-fire", "[VWAP] NVDA someone else's", t0, owner: Guid.NewGuid());
        await using (var db = await kit.Db.CreateDbContextAsync())
        {
            db.TradeDiary.Add(new TradeDiaryEntry
            {
                StrategyId = s.Id, OwnerUserId = kit.Owner, StrategyTitle = "VWAP", Symbol = "NVDA", Broker = "Alpaca", IsPaper = true,
                EntryUtc = t0.AddMinutes(1), EntryPrice = 175m, Quantity = 1, Status = TradeDiaryStatus.Closed,
                ExitUtc = t0.AddMinutes(30), ExitPrice = 178m, ExitReason = "TakeProfit", RealizedPnL = 3m,
            });
            db.CommandLedger.Add(new CommandLedgerEntry
            {
                OwnerUserId = kit.Owner, Operation = "strategy activate", Caller = "Cli", ArgsJson = $"{{\"strategy\":\"{s.Id:N}\"}}",
                Status = "ok", TimestampUtc = t0.AddMinutes(-5),
            });
            await db.SaveChangesAsync();
        }

        var result = await new StrategyHistoryOperation().ExecuteAsync(new StrategyHistoryArgs { Strategy = s.Id.ToString() }, kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.Contain("signal-fire").And.Contain("trade-entry").And.Contain("trade-exit").And.Contain("strategy activate"));
        Assert.That(result.Human, Does.Contain("+$3.00"));
        Assert.That(result.Human, Does.Not.Contain("someone else's"));
        Assert.That(result.Human, Does.Contain("ET"));
        Assert.That(result.Human!.IndexOf("trade-exit", StringComparison.Ordinal), Is.LessThan(result.Human.IndexOf("strategy activate", StringComparison.Ordinal)), "newest first");
    }
}
