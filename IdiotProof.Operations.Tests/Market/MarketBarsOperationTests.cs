using IdiotProof.Operations.Market;

namespace IdiotProof.Operations.Tests.Market;

/// <summary>market bars (IP-T201): historical bars, newest window by default, oldest-first table.</summary>
public class MarketBarsOperationTests
{
    private const string TwoDays = """
        {"bars":[{"t":"2026-10-30T04:00:00Z","o":101,"h":103,"l":100,"c":102.5,"v":2500000,"n":31000,"vw":101.9},
                 {"t":"2026-10-29T04:00:00Z","o":99,"h":101.5,"l":98.5,"c":100,"v":1800000,"n":25000,"vw":100.1}],
         "symbol":"AAPL","next_page_token":null}
        """;

    [Test]
    public async Task DefaultsToTheMostRecentDailyBars_ShownOldestFirst()
    {
        var handler = new ScriptedMarketHandler().On("/v2/stocks/AAPL/bars", TwoDays);

        var result = await new MarketBarsOperation().ExecuteAsync(new MarketBarsArgs { Symbol = "aapl", Limit = 2 }, MarketDataTestKit.Context(handler));

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(handler.Query("/v2/stocks/AAPL/bars", "timeframe"), Is.EqualTo("1Day"));
        Assert.That(handler.Query("/v2/stocks/AAPL/bars", "sort"), Is.EqualTo("desc"));
        Assert.That(handler.Query("/v2/stocks/AAPL/bars", "limit"), Is.EqualTo("2"));
        Assert.That(handler.Query("/v2/stocks/AAPL/bars", "feed"), Is.EqualTo(MarketData.Feed));
        var lines = result.Human!.Split('\n');
        var oct29 = Array.FindIndex(lines, l => l.StartsWith("2026-10-29"));
        var oct30 = Array.FindIndex(lines, l => l.StartsWith("2026-10-30"));
        Assert.That(oct29, Is.GreaterThan(0).And.LessThan(oct30));
        Assert.That(lines[oct30], Does.Contain("$102.50").And.Contain("+$2.50").And.Contain("2.5M"));
    }

    [Test]
    public async Task FromTo_AreEtDays_SortedAscending_AndShortTimeframesParse()
    {
        var handler = new ScriptedMarketHandler().On("/v2/stocks/NVDA/bars", """
            {"bars":[{"t":"2026-10-29T13:30:00Z","o":1,"h":1,"l":1,"c":1,"v":10,"n":1,"vw":1}],"next_page_token":null}
            """);

        var result = await new MarketBarsOperation().ExecuteAsync(
            new MarketBarsArgs { Symbol = "NVDA", Timeframe = "5m", From = new DateOnly(2026, 10, 29), To = new DateOnly(2026, 10, 29) },
            MarketDataTestKit.Context(handler));

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(handler.Query("/v2/stocks/NVDA/bars", "timeframe"), Is.EqualTo("5Min"));
        Assert.That(handler.Query("/v2/stocks/NVDA/bars", "start"), Is.EqualTo("2026-10-29T04:00:00Z"));
        Assert.That(handler.Query("/v2/stocks/NVDA/bars", "end"), Is.EqualTo("2026-10-30T04:00:00Z"));
        Assert.That(handler.Query("/v2/stocks/NVDA/bars", "sort"), Is.EqualTo("asc"));
        Assert.That(result.Human, Does.Contain("2026-10-29 9:30 AM ET"));
    }

    [TestCase("7Day")]
    [TestCase("banana")]
    [TestCase("60Min")]
    public async Task BadTimeframe_IsAUsageError(string tf)
    {
        var result = await new MarketBarsOperation().ExecuteAsync(new MarketBarsArgs { Symbol = "AAPL", Timeframe = tf }, MarketDataTestKit.Context(new ScriptedMarketHandler()));

        Assert.That(result.Status, Is.EqualTo(OperationStatus.UsageError));
    }
}
