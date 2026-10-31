using IdiotProof.Operations.Market;

namespace IdiotProof.Operations.Tests.Market;

/// <summary>market snapshot / market latest (IP-T319): one aligned row per symbol.</summary>
public class MarketSnapshotOperationsTests
{
    [Test]
    public async Task Snapshot_OneRowPerSymbol_ChangeVsPriorClose()
    {
        var handler = new ScriptedMarketHandler().On("/v2/stocks/snapshots", """
            {"AAPL":{"latestTrade":{"t":"2026-10-30T17:59:00Z","p":110,"s":5},"latestQuote":{"t":"2026-10-30T17:59:00Z","bp":109.99,"bs":1,"ap":110.01,"as":2},
                     "minuteBar":{"t":"2026-10-30T17:58:00Z","o":110,"h":110,"l":110,"c":110,"v":100},
                     "dailyBar":{"t":"2026-10-30T04:00:00Z","o":101,"h":111,"l":100,"c":110,"v":3200000},
                     "prevDailyBar":{"t":"2026-10-29T04:00:00Z","o":99,"h":101,"l":98,"c":100,"v":1000}},
             "MSFT":{"latestTrade":{"t":"2026-10-30T17:59:00Z","p":400,"s":5},"prevDailyBar":{"c":410}}}
            """);

        var result = await new MarketSnapshotOperation().ExecuteAsync(new MarketSnapshotArgs { Symbols = ["AAPL", "MSFT"] }, MarketDataTestKit.Context(handler));

        Assert.That(result.IsOk, Is.True, result.Message);
        var lines = result.Human!.Split('\n');
        Assert.That(lines.Single(l => l.StartsWith("AAPL")), Does.Contain("$110.00").And.Contain("+$10.00").And.Contain("+10.00%").And.Contain("3.2M").And.Contain("1:59 PM ET"));
        Assert.That(lines.Single(l => l.StartsWith("MSFT")), Does.Contain("-$10.00").And.Contain("-2.44%"));
    }

    [TestCase(LatestKind.Bar, "/v2/stocks/bars/latest", """{"bars":{"AAPL":{"t":"2026-10-30T17:58:00Z","o":1.5,"h":2,"l":1,"c":1.75,"v":12000,"n":40,"vw":1.6}}}""", "$1.75")]
    [TestCase(LatestKind.Trade, "/v2/stocks/trades/latest", """{"trades":{"AAPL":{"t":"2026-10-30T17:58:00Z","p":231.11,"s":7,"x":"V","c":["@"]}}}""", "$231.11")]
    [TestCase(LatestKind.Quote, "/v2/stocks/quotes/latest", """{"quotes":{"AAPL":{"t":"2026-10-30T17:58:00Z","bp":10,"bs":1,"bx":"V","ap":10.05,"as":1,"ax":"Q"}}}""", "$10.05")]
    public async Task Latest_HitsTheRightEndpoint(LatestKind kind, string path, string json, string expected)
    {
        var handler = new ScriptedMarketHandler().On(path, json);

        var result = await new MarketLatestOperation().ExecuteAsync(new MarketLatestArgs { Kind = kind, Symbols = ["aapl", "nope"] }, MarketDataTestKit.Context(handler));

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(handler.Query(path, "symbols"), Is.EqualTo("AAPL,NOPE"));
        Assert.That(result.Human!.Split('\n').Single(l => l.StartsWith("AAPL")), Does.Contain(expected).And.Contain("2026-10-30 1:58"));
        Assert.That(result.Human, Does.Contain("No data for: NOPE"));
    }
}
