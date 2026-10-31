using IdiotProof.Operations.Market;

namespace IdiotProof.Operations.Tests.Market;

/// <summary>market most-actives (IP-T321): screener ranked by volume or trades.</summary>
public class MarketMostActivesOperationTests
{
    [Test]
    public async Task RanksByTheChosenMeasure()
    {
        var handler = new ScriptedMarketHandler().On("/v1beta1/screener/stocks/most-actives", """
            {"most_actives":[{"symbol":"NVDA","volume":250000000,"trade_count":2100000},{"symbol":"TSLA","volume":98000000,"trade_count":1900000}],
             "last_updated":"2026-10-30T17:50:00Z"}
            """);

        var result = await new MarketMostActivesOperation().ExecuteAsync(new MarketMostActivesArgs { By = MostActiveBy.Trades, Top = 2 }, MarketDataTestKit.Context(handler));

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(handler.Query("/v1beta1/screener/stocks/most-actives", "by"), Is.EqualTo("trades"));
        Assert.That(handler.Query("/v1beta1/screener/stocks/most-actives", "top"), Is.EqualTo("2"));
        Assert.That(result.Human, Does.StartWith("Most active by trades as of 2026-10-30 1:50 PM ET"));
        Assert.That(result.Human!.Split('\n').Single(l => l.Contains("NVDA")), Does.Contain("250M").And.Contain("2.1M"));
    }
}
