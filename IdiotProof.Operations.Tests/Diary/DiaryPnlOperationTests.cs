using IdiotProof.Operations.Diary;

namespace IdiotProof.Operations.Tests.Diary;

/// <summary>diary pnl (IP-T240): realized P&amp;L per ET day/week/month, optionally by strategy or symbol.</summary>
public class DiaryPnlOperationTests
{
    private static async Task<DiaryKit> SeedAsync()
    {
        var kit = new DiaryKit();
        await kit.TradeAsync("NVDA", DiaryKit.Et(2026, 10, 26, 9, 35), 100m, 105m, title: "Gap"); // Mon +50
        await kit.TradeAsync("AMD", DiaryKit.Et(2026, 10, 26, 10, 0), 50m, 48m, title: "Fade");  // Mon -20
        await kit.TradeAsync("NVDA", DiaryKit.Et(2026, 10, 28, 9, 35), 100m, 103m, title: "Gap"); // Wed +30
        await kit.TradeAsync("NVDA", DiaryKit.Et(2026, 11, 2, 9, 35), 100m, 99m, title: "Gap");   // next Mon -10
        await kit.TradeAsync("TSLA", DiaryKit.Et(2026, 11, 2, 9, 35), 100m, null);                 // open: no P&L
        // Exits at 7:50 PM ET still count on that ET day, not the next UTC day.
        await kit.TradeAsync("AMD", DiaryKit.Et(2026, 10, 27, 19, 20), 50m, 51m, title: "Fade");  // Tue +10
        return kit;
    }

    [Test]
    public async Task ByDay_UsesTheEtExitDate_WithARunningTotal()
    {
        var kit = await SeedAsync();

        var result = await new DiaryPnlOperation().ExecuteAsync(new DiaryPnlArgs(), kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        var lines = result.Human!.Split('\n');
        Assert.That(lines.Single(l => l.StartsWith("2026-10-26 Mon")), Does.Contain("+$30.00"));
        Assert.That(lines.Single(l => l.StartsWith("2026-10-27 Tue")), Does.Contain("+$10.00").And.Contain("+$40.00"));
        Assert.That(lines.Single(l => l.StartsWith("2026-11-02 Mon")), Does.Contain("-$10.00").And.Contain("+$60.00"));
        Assert.That(result.Human, Does.Contain("5 closed trades, +$60.00 over 4 days (3 green)."));
    }

    [Test]
    public async Task ByWeek_SplitByStrategy()
    {
        var kit = await SeedAsync();

        var result = await new DiaryPnlOperation().ExecuteAsync(new DiaryPnlArgs { Week = true, By = DiaryPnlBy.Strategy }, kit.Context());

        var lines = result.Human!.Split('\n');
        Assert.That(lines.Single(l => l.StartsWith("wk of 2026-10-26") && l.Contains("Gap")), Does.Contain("+$80.00"));
        Assert.That(lines.Single(l => l.StartsWith("wk of 2026-10-26") && l.Contains("Fade")), Does.Contain("-$10.00"));
        Assert.That(lines.Single(l => l.StartsWith("wk of 2026-11-02")), Does.Contain("-$10.00"));
    }

    [Test]
    public async Task ByMonth_AndTwoUnits_IsAUsageError()
    {
        var kit = await SeedAsync();

        var month = await new DiaryPnlOperation().ExecuteAsync(new DiaryPnlArgs { Month = true }, kit.Context());
        var both = await new DiaryPnlOperation().ExecuteAsync(new DiaryPnlArgs { Day = true, Month = true }, kit.Context());

        Assert.That(month.Human, Does.Contain("2026-10").And.Contain("+$70.00").And.Contain("2026-11"));
        Assert.That(both.Status, Is.EqualTo(OperationStatus.UsageError));
    }
}
