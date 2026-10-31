using IdiotProof.Core.Trading;
using IdiotProof.Operations.Account;

namespace IdiotProof.Operations.Tests.Account;

/// <summary>account history (IP-T300): portfolio history as a Braille equity line, with P&amp;L = equity change minus deposits/withdrawals from account activities.</summary>
public class AccountHistoryOperationTests
{
    /// <summary>Fri 10/30 9:30 AM to 4:00 PM ET every 15 minutes (EDT), a $5,000 deposit at noon that profit_loss ignores.</summary>
    private static string Intraday()
    {
        var open = new DateTimeOffset(2026, 10, 30, 13, 30, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        var ts = new List<long>();
        var eq = new List<string>();
        var pl = new List<string>();
        for (int i = 0; i <= 26; i++)
        {
            var trading = 100_000m + i * 10m;
            var deposit = i >= 10 ? 5_000m : 0m;
            ts.Add(open + i * 900);
            eq.Add((trading + deposit).ToString(System.Globalization.CultureInfo.InvariantCulture));
            pl.Add((i * 10m).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        ts.Add(open + 27 * 900);
        return $$"""
            {"timestamp":[{{string.Join(",", ts)}}],"equity":[{{string.Join(",", eq)}},null],
             "profit_loss":[{{string.Join(",", pl)}},null],"profit_loss_pct":[],"base_value":100000,"timeframe":"15Min"}
            """;
    }

    private const string Daily = """
        {"timestamp":[1759204800,1759291200,1759377600,1759464000,1759723200],
         "equity":[100000,100500,99800,101000,101250],"profit_loss":[0,500,-200,1000,1250],
         "base_value":100000,"timeframe":"1D"}
        """;

    [Test]
    public async Task Intraday_ChartsALine_AndPnlExcludesTheDeposit()
    {
        var f = new ScriptedAlpacaFactory();
        f.Paper.On("GET /v2/account/portfolio/history", Intraday())
            .On("GET /v2/account/activities", """[{"id":"a1","activity_type":"CSD","date":"2026-10-30","net_amount":"5000"}]""");

        var result = await new AccountHistoryOperation().ExecuteAsync(
            new AccountHistoryArgs { Period = "1d", Timeframe = "15min", Width = 100 }, AccountTestKit.Context(f));

        Assert.That(result.IsOk, Is.True, result.Message);
        var query = f.Paper.Requests.Single(r => r.Uri.AbsolutePath.EndsWith("history")).Uri.Query;
        Assert.That(query, Does.Contain("period=1D").And.Contain("timeframe=15Min").And.Contain("intraday_reporting=market_hours"));
        var chart = result.Human!.Split("\n\n")[0];
        Assert.That(chart.Split('\n').Select(l => l.Length).Distinct(), Is.EqualTo(new[] { 100 }));
        Assert.That(chart, Does.StartWith("Paper equity  1D  2026-10-30 -> 2026-10-30 ET   P&L +$260.00 (+0.25%) excl. deposits"));
        Assert.That(chart, Does.Contain("Fri 10/30").And.Contain("<- "));
        Assert.That(result.Human, Does.Contain("Deposits/withdrawals:").And.Contain("+$5,000.00"));
    }

    [Test]
    public async Task Daily_OneSessionWithDateTicks()
    {
        var f = new ScriptedAlpacaFactory();
        f.Live.On("GET /v2/account/portfolio/history", Daily).On("GET /v2/account/activities", "[]");

        var result = await new AccountHistoryOperation().ExecuteAsync(
            new AccountHistoryArgs { Mode = BrokerMode.Live, Width = 90 }, AccountTestKit.Context(f));

        Assert.That(result.IsOk, Is.True, result.Message);
        var chart = result.Human!.Split("\n\n")[0];
        Assert.That(chart.Split('\n').Select(l => l.Length).Distinct(), Is.EqualTo(new[] { 90 }));
        Assert.That(chart, Does.Contain("P&L +$1,250.00 (+1.25%)").And.Contain("9/30"));
        Assert.That(result.Human, Does.Not.Contain("Deposits/withdrawals"));
    }

    [Test]
    public async Task All_StartsAtTheAccountsCreation()
    {
        var f = new ScriptedAlpacaFactory();
        f.Paper.On("GET /v2/account", """{"created_at":"2025-01-15T14:00:00Z"}""").On("GET /v2/account/portfolio/history", Daily)
            .On("GET /v2/account/activities", "[]");

        var result = await new AccountHistoryOperation().ExecuteAsync(new AccountHistoryArgs { Period = "all" }, AccountTestKit.Context(f));

        Assert.That(result.IsOk, Is.True, result.Message);
        var query = f.Paper.Requests.Last(r => r.Uri.AbsolutePath.EndsWith("history")).Uri.Query;
        Assert.That(query, Does.Contain("start=2025-01-15T14%3A00%3A00Z").And.Contain("timeframe=1D").And.Not.Contain("period="));
    }

    [TestCase("7X", null)]
    [TestCase("1M", "2H")]
    public async Task BadPeriodOrTimeframe_IsUsage(string period, string? timeframe)
    {
        var result = await new AccountHistoryOperation().ExecuteAsync(
            new AccountHistoryArgs { Period = period, Timeframe = timeframe }, AccountTestKit.Context(new ScriptedAlpacaFactory()));
        Assert.That(result.Status, Is.EqualTo(OperationStatus.UsageError));
    }

    [Test]
    public void Today_UsesProfitLoss_NotEquityDelta()
    {
        using var doc = System.Text.Json.JsonDocument.Parse(Intraday());
        var h = PortfolioHistory.Parse(doc.RootElement);

        Assert.That(h.Points, Has.Count.EqualTo(27), "the trailing null point is dropped");
        Assert.That(h.ProfitLoss, Is.EqualTo(260m));
        Assert.That(h.Points[^1].Equity - h.BaseValue, Is.EqualTo(5260m));
    }

    [Test]
    public async Task TodayAsync_AsksForOneDayWithExtendedHoursAndDailyReset()
    {
        var f = new ScriptedAlpacaFactory();
        f.Paper.On("GET /v2/account/portfolio/history", Intraday());
        await using var api = await f.ForOwnerAsync(AccountTestKit.Owner, BrokerMode.Paper);

        var h = await PortfolioHistory.TodayAsync(api, CancellationToken.None);

        Assert.That(h.ProfitLoss, Is.EqualTo(260m));
        Assert.That(f.Paper.Requests.Single().Uri.Query, Does.Contain("period=1D").And.Contain("intraday_reporting=extended_hours").And.Contain("pnl_reset=per_day"));
    }
}
