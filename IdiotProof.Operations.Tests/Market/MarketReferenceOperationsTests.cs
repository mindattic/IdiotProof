using IdiotProof.Operations.Market;

namespace IdiotProof.Operations.Tests.Market;

/// <summary>market auctions / conditions / exchanges / meta (IP-T320).</summary>
public class MarketReferenceOperationsTests
{
    [Test]
    public async Task Auctions_PickTheOfficialCrosses_PagesAcrossTokens()
    {
        var handler = new ScriptedMarketHandler().On("/v2/stocks/auctions", """
            {"auctions":{"AAPL":[{"d":"2026-10-29",
                "o":[{"t":"2026-10-29T13:30:00.1Z","x":"P","p":229.9,"s":100,"c":"O"},{"t":"2026-10-29T13:30:00.5Z","x":"Q","p":230.1,"s":250000,"c":"Q"}],
                "c":[{"t":"2026-10-29T20:00:00.2Z","x":"Q","p":231.25,"s":4000000,"c":"M"},{"t":"2026-10-29T20:00:01Z","x":"P","p":231.2,"s":50,"c":"6"}]}]},
             "next_page_token":null}
            """);

        var result = await new MarketAuctionsOperation().ExecuteAsync(new MarketAuctionsArgs { Symbols = ["AAPL"] }, MarketDataTestKit.Context(handler));

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(handler.Query("/v2/stocks/auctions", "feed"), Is.EqualTo("sip"));
        var row = result.Human!.Split('\n').Single(l => l.StartsWith("AAPL"));
        Assert.That(row, Does.Contain("2026-10-29").And.Contain("$230.10").And.Contain("250,000").And.Contain("9:30 AM ET")
            .And.Contain("$231.25").And.Contain("4,000,000").And.Contain("4:00 PM ET"));
    }

    [Test]
    public async Task Conditions_ListCodesForTheTickTypeAndTape()
    {
        var handler = new ScriptedMarketHandler().On("/v2/stocks/meta/conditions/quote", """{"R":"Regular","A":"Slow on Ask"}""");

        var result = await new MarketConditionsOperation().ExecuteAsync(
            new MarketConditionsArgs { TickType = TickType.Quote, Tape = Tape.C }, MarketDataTestKit.Context(handler));

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(handler.Query("/v2/stocks/meta/conditions/quote", "tape"), Is.EqualTo("C"));
        var lines = result.Human!.Split('\n');
        Assert.That(Array.FindIndex(lines, l => l.StartsWith("A ")), Is.LessThan(Array.FindIndex(lines, l => l.StartsWith("R "))));
        Assert.That(result.Human, Does.Contain("Slow on Ask"));
    }

    [Test]
    public async Task Exchanges_AndMeta_ReadTheReferenceTables()
    {
        var handler = new ScriptedMarketHandler()
            .On("/v2/stocks/meta/exchanges", """{"Q":"NASDAQ Int","V":"IEX"}""")
            .On("/v2/stocks/meta/conditions/trade", """{"@":"Regular Sale"}""")
            .On("/v2/stocks/meta/conditions/quote", """{"R":"Regular"}""");

        var exchanges = await new MarketExchangesOperation().ExecuteAsync(new NoArgs(), MarketDataTestKit.Context(handler));
        var meta = await new MarketMetaOperation().ExecuteAsync(new MarketMetaArgs(), MarketDataTestKit.Context(handler));

        Assert.That(exchanges.IsOk, Is.True, exchanges.Message);
        Assert.That(exchanges.Human, Does.Contain("IEX").And.Contain("NASDAQ Int"));
        Assert.That(meta.IsOk, Is.True, meta.Message);
        Assert.That(meta.Human, Does.Contain("EXCHANGES").And.Contain("Regular Sale").And.Contain("QUOTE CONDITIONS (tape A)"));
    }
}
