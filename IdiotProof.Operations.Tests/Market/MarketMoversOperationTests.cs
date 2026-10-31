using IdiotProof.Operations.Market;

namespace IdiotProof.Operations.Tests.Market;

/// <summary>market movers (IP-T203): screener gainers and losers.</summary>
public class MarketMoversOperationTests
{
    [Test]
    public async Task GainersAndLosers_EachRanked_UpdatedInEt()
    {
        var handler = new ScriptedMarketHandler().On("/v1beta1/screener/stocks/movers", """
            {"gainers":[{"symbol":"ABCD","percent_change":45.5,"change":1.82,"price":5.82},{"symbol":"EFG","percent_change":12.25,"change":0.49,"price":4.49}],
             "losers":[{"symbol":"XYZ","percent_change":-30.1,"change":-3.01,"price":6.99}],
             "market_type":"stocks","last_updated":"2026-10-30T17:55:00.123456Z"}
            """);

        var result = await new MarketMoversOperation().ExecuteAsync(new MarketMoversArgs { Top = 2 }, MarketDataTestKit.Context(handler));

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(handler.Query("/v1beta1/screener/stocks/movers", "top"), Is.EqualTo("2"));
        Assert.That(result.Human, Does.StartWith("Movers as of 2026-10-30 1:55 PM ET"));
        var human = result.Human!;
        Assert.That(human.IndexOf("GAINERS"), Is.LessThan(human.IndexOf("ABCD")));
        Assert.That(human.IndexOf("LOSERS"), Is.LessThan(human.IndexOf("XYZ")));
        Assert.That(human.Split('\n').Single(l => l.Contains("ABCD")), Does.Contain("+45.50%").And.Contain("$5.82"));
        Assert.That(human.Split('\n').Single(l => l.Contains("XYZ")), Does.Contain("-30.10%").And.Contain("-$3.01"));
    }

    [Test]
    public async Task TopOutOfRange_IsAUsageError()
    {
        var result = await new MarketMoversOperation().ExecuteAsync(new MarketMoversArgs { Top = 0 }, MarketDataTestKit.Context(new ScriptedMarketHandler()));

        Assert.That(result.Status, Is.EqualTo(OperationStatus.UsageError));
    }
}
