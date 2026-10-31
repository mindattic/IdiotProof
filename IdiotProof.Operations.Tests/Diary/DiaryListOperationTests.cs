using IdiotProof.Core.Trading;
using IdiotProof.Operations.Diary;

namespace IdiotProof.Operations.Tests.Diary;

/// <summary>diary list (IP-T238): the owner's trades, newest first, filtered by date, mode, symbol and status.</summary>
public class DiaryListOperationTests
{
    [Test]
    public async Task ListsOnlyTheOwnersTrades_NewestFirst_InEt()
    {
        var kit = new DiaryKit();
        await kit.TradeAsync("NVDA", DiaryKit.Et(2026, 10, 28, 9, 35), 100m, 104m);
        await kit.TradeAsync("AMD", DiaryKit.Et(2026, 10, 29, 10, 0), 50m, 49m);
        await kit.TradeAsync("TSLA", DiaryKit.Et(2026, 10, 30, 9, 31), 200m, null);
        await kit.TradeAsync("MSFT", DiaryKit.Et(2026, 10, 30, 9, 40), 300m, 310m, owner: Guid.NewGuid());

        var result = await new DiaryListOperation().ExecuteAsync(new DiaryListArgs(), kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        var human = result.Human!;
        Assert.That(human.IndexOf("TSLA"), Is.LessThan(human.IndexOf("AMD")));
        Assert.That(human.IndexOf("AMD"), Is.LessThan(human.IndexOf("NVDA")));
        Assert.That(human, Does.Not.Contain("MSFT"));
        Assert.That(human, Does.Contain("2026-10-28 9:35 AM ET").And.Contain("+$40.00").And.Contain("-$10.00"));
        Assert.That(human, Does.Contain("3 trades (1 open, 2 closed); realized +$30.00."));
    }

    [Test]
    public async Task FiltersByModeSymbolSinceAndStatus()
    {
        var kit = new DiaryKit();
        await kit.TradeAsync("NVDA", DiaryKit.Et(2026, 10, 1, 9, 35), 100m, 101m, broker: "Sandbox");
        await kit.TradeAsync("NVDA", DiaryKit.Et(2026, 10, 20, 9, 35), 100m, 102m, paper: true);
        await kit.TradeAsync("NVDA", DiaryKit.Et(2026, 10, 21, 9, 35), 100m, 103m, paper: false);
        await kit.TradeAsync("AMD", DiaryKit.Et(2026, 10, 22, 9, 35), 50m, null, paper: false);

        var live = await new DiaryListOperation().ExecuteAsync(new DiaryListArgs { Mode = BrokerMode.Live, Symbol = "nvda" }, kit.Context());
        var since = await new DiaryListOperation().ExecuteAsync(new DiaryListArgs { Since = new DateOnly(2026, 10, 15), Status = "closed" }, kit.Context());
        var sandbox = await new DiaryListOperation().ExecuteAsync(new DiaryListArgs { Mode = BrokerMode.Sandbox }, kit.Context());
        var bad = await new DiaryListOperation().ExecuteAsync(new DiaryListArgs { Status = "won" }, kit.Context());

        Assert.That(live.Human, Does.Contain("+$30.00").And.Contain("live").And.Not.Contain("AMD").And.Contain("1 trades"));
        Assert.That(since.Human, Does.Contain("2 trades").And.Not.Contain("AMD"));
        Assert.That(sandbox.Human, Does.Contain("sandbox").And.Contain("1 trades"));
        Assert.That(bad.Status, Is.EqualTo(OperationStatus.UsageError));
    }
}
