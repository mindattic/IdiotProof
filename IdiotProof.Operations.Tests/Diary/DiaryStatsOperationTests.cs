using IdiotProof.Blazor.Data;
using IdiotProof.Operations.Diary;

namespace IdiotProof.Operations.Tests.Diary;

/// <summary>diary stats (IP-T241): win rate, expectancy, profit factor, avg R, MFE/MAE from recorded bars, drawdown.</summary>
public class DiaryStatsOperationTests
{
    [Test]
    public async Task ComputesTheNumbers()
    {
        var kit = new DiaryKit();
        var gap = Guid.NewGuid();
        // +$40 (stop 98 → $20 risked → +2R), -$20 (stop 99 → $10 risked → -2R), +$10 (no stop), -$40 → net -$10.
        await kit.TradeAsync("NVDA", DiaryKit.Et(2026, 10, 26, 9, 35), 100m, 104m, stop: 98m, strategyId: gap, title: "Gap");
        await kit.TradeAsync("NVDA", DiaryKit.Et(2026, 10, 27, 9, 35), 100m, 98m, stop: 99m, title: "Gap");
        await kit.TradeAsync("AMD", DiaryKit.Et(2026, 10, 28, 9, 35), 50m, 51m, title: "Fade");
        await kit.TradeAsync("AMD", DiaryKit.Et(2026, 10, 29, 9, 35), 50m, 46m, title: "Fade");
        await kit.TradeAsync("TSLA", DiaryKit.Et(2026, 10, 30, 9, 35), 100m, null);
        // The Monitor's bars for the first trade (9:35-10:05 ET): high 106 (+6%), low 99 (-1%); one bar outside the window.
        await kit.AddAsync(
            Bar(gap, "2026-10-26", 9 * 60 + 40, high: 106, low: 101),
            Bar(gap, "2026-10-26", 9 * 60 + 50, high: 103, low: 99),
            Bar(gap, "2026-10-26", 11 * 60, high: 150, low: 50));

        var result = await new DiaryStatsOperation().ExecuteAsync(new DiaryStatsArgs(), kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.Contain("4 closed: 2 won, 2 lost").And.Contain("50.0%")
            .And.Contain("-$10.00 ($50.00 won, $60.00 lost)").And.Contain("-$2.50 per trade")
            .And.Contain("profit factor:").And.Contain("0.83")
            .And.Contain("+0.00R over 2 trades with a stop")
            .And.Contain("+6.00% / -1.00% avg over 1 trades")
            .And.Contain("$50.00 (closed-trade equity); longest losing streak 1"));
    }

    [Test]
    public async Task ByStrategy_BreaksItDown()
    {
        var kit = new DiaryKit();
        await kit.TradeAsync("NVDA", DiaryKit.Et(2026, 10, 26, 9, 35), 100m, 104m, title: "Gap");
        await kit.TradeAsync("AMD", DiaryKit.Et(2026, 10, 28, 9, 35), 50m, 46m, title: "Fade");

        var result = await new DiaryStatsOperation().ExecuteAsync(new DiaryStatsArgs { By = DiaryPnlBy.Strategy }, kit.Context());

        var lines = result.Human!.Split('\n');
        Assert.That(lines.Single(l => l.StartsWith("Gap")), Does.Contain("100.0%").And.Contain("+$40.00"));
        Assert.That(lines.Single(l => l.StartsWith("Fade")), Does.Contain("0.0%").And.Contain("-$40.00"));
    }

    [Test]
    public async Task NoClosedTrades_SaysSo()
    {
        var result = await new DiaryStatsOperation().ExecuteAsync(new DiaryStatsArgs(), new DiaryKit().Context());
        Assert.That(result.Human, Does.Contain("No closed trades match."));
    }

    private static LiveBar Bar(Guid strategy, string date, int min, double high, double low) => new()
    {
        StrategyId = strategy, DateEt = date, Min = min, Et = $"{min / 60}:{min % 60:00}", High = high, Low = low,
        Open = low, Close = high, WrittenUtc = DateTime.UtcNow,
    };
}
