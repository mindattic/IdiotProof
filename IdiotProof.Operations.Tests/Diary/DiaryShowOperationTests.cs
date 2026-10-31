using IdiotProof.Operations.Diary;

namespace IdiotProof.Operations.Tests.Diary;

/// <summary>diary show (IP-T239): one trade in full, by id prefix, never another owner's.</summary>
public class DiaryShowOperationTests
{
    [Test]
    public async Task ShowsEntryExitRiskPlanAndR()
    {
        var kit = new DiaryKit();
        var t = await kit.TradeAsync("NVDA", DiaryKit.Et(2026, 10, 28, 9, 35), 100m, 104m, qty: 10, stop: 98m, holdMinutes: 125);

        var result = await new DiaryShowOperation().ExecuteAsync(new DiaryShowArgs { Trade = t.Id.ToString("N")[..8] }, kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.Contain("Long 10 NVDA").And.Contain("paper via Alpaca")
            .And.Contain("$100.00 at 2026-10-28 9:35 AM ET").And.Contain("$104.00 at 2026-10-28 11:40 AM ET — TargetHit")
            .And.Contain("2h 05m").And.Contain("stop $98.00").And.Contain("risked:").And.Contain("$20.00")
            .And.Contain("+$40.00 (+4.00%), +2.00R"));
    }

    [Test]
    public async Task AnotherOwnersTrade_IsNotFound()
    {
        var kit = new DiaryKit();
        var t = await kit.TradeAsync("NVDA", DateTime.UtcNow, 100m, 101m, owner: Guid.NewGuid());

        Assert.ThrowsAsync<OperationException>(() =>
            new DiaryShowOperation().ExecuteAsync(new DiaryShowArgs { Trade = t.Id.ToString() }, kit.Context()));
    }
}
