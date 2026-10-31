using IdiotProof.Operations.Market;

namespace IdiotProof.Operations.Tests.Market;

/// <summary>market fx (IP-T322): latest forex rates, or rate history with a timeframe/range.</summary>
public class MarketFxOperationTests
{
    [Test]
    public async Task Latest_ByDefault_OneRowPerPair()
    {
        var handler = new ScriptedMarketHandler().On("/v1beta1/forex/latest/rates", """
            {"rates":{"USDJPY":{"bp":151.234,"mp":151.239,"ap":151.244,"t":"2026-10-30T17:59:59Z"},
                      "EURUSD":{"bp":1.08412,"mp":1.08415,"ap":1.08418,"t":"2026-10-30T17:59:58Z"}}}
            """);

        var result = await new MarketFxOperation().ExecuteAsync(new MarketFxArgs { Pairs = ["USD/JPY", "eurusd"] }, MarketDataTestKit.Context(handler));

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(handler.Query("/v1beta1/forex/latest/rates", "currency_pairs"), Is.EqualTo("USDJPY,EURUSD"));
        var lines = result.Human!.Split('\n');
        Assert.That(lines.Single(l => l.StartsWith("USDJPY")), Does.Contain("151.234").And.Contain("1:59:59.000 PM ET"));
        Assert.That(lines.Single(l => l.StartsWith("EURUSD")), Does.Contain("1.08415"));
    }

    [Test]
    public async Task History_WhenATimeframeIsGiven_CappedPerPair_OldestFirst()
    {
        var handler = new ScriptedMarketHandler().On("/v1beta1/forex/rates", """
            {"rates":{"USDJPY":[{"t":"2026-10-30T04:00:00Z","o":150,"h":152,"l":149.5,"c":151.5},
                                {"t":"2026-10-29T04:00:00Z","o":149,"h":150.5,"l":148.8,"c":150},
                                {"t":"2026-10-28T04:00:00Z","o":148,"h":149.5,"l":147.5,"c":149}]},"next_page_token":null}
            """);

        var result = await new MarketFxOperation().ExecuteAsync(new MarketFxArgs { Pairs = ["USDJPY"], Timeframe = "1d", Limit = 2 }, MarketDataTestKit.Context(handler));

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(handler.Query("/v1beta1/forex/rates", "timeframe"), Is.EqualTo("1Day"));
        Assert.That(handler.Query("/v1beta1/forex/rates", "sort"), Is.EqualTo("desc"));
        var lines = result.Human!.Split('\n');
        Assert.That(lines.Count(l => l.StartsWith("USDJPY")), Is.EqualTo(2));
        Assert.That(Array.FindIndex(lines, l => l.Contains("2026-10-29")), Is.LessThan(Array.FindIndex(lines, l => l.Contains("2026-10-30"))));
        Assert.That(result.Human, Does.Not.Contain("2026-10-28"));
    }
}
