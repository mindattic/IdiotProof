using IdiotProof.Operations.Market;

namespace IdiotProof.Operations.Tests.Market;

/// <summary>market trades / market quotes (IP-T318): historical ticks, millisecond ET times.</summary>
public class MarketTicksOperationsTests
{
    [Test]
    public async Task Trades_NewestWindowByDefault_ShownOldestFirst()
    {
        var handler = new ScriptedMarketHandler().On("/v2/stocks/AAPL/trades", """
            {"trades":[{"t":"2026-10-30T17:59:59.987654321Z","x":"V","p":231.5,"s":10,"c":["@","I"],"i":9,"z":"C"},
                       {"t":"2026-10-30T17:59:58.5Z","x":"Q","p":231.4,"s":200,"c":["@"],"i":8,"z":"C"}],"symbol":"AAPL","next_page_token":"more"}
            """);

        var result = await new MarketTradesOperation().ExecuteAsync(new MarketTicksArgs { Symbol = "AAPL", Limit = 2 }, MarketDataTestKit.Context(handler));

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(handler.Requests, Has.Count.EqualTo(1));
        Assert.That(handler.Query("/v2/stocks/AAPL/trades", "sort"), Is.EqualTo("desc"));
        Assert.That(handler.Query("/v2/stocks/AAPL/trades", "feed"), Is.EqualTo(MarketData.Feed));
        var lines = result.Human!.Split('\n');
        var first = Array.FindIndex(lines, l => l.StartsWith("2026-10-30 1:59:58.500 PM ET"));
        var second = Array.FindIndex(lines, l => l.StartsWith("2026-10-30 1:59:59.987 PM ET"));
        Assert.That(first, Is.GreaterThan(0).And.LessThan(second));
        Assert.That(lines[second], Does.Contain("$231.50").And.Contain("@ I"));
    }

    [Test]
    public async Task Quotes_ShowBidAskAndSpread()
    {
        var handler = new ScriptedMarketHandler().On("/v2/stocks/SPY/quotes", """
            {"quotes":[{"t":"2026-10-29T14:00:00Z","bx":"P","bp":500.01,"bs":3,"ax":"Q","ap":500.04,"as":5,"c":["R"],"z":"B"}],"next_page_token":null}
            """);

        var result = await new MarketQuotesOperation().ExecuteAsync(
            new MarketTicksArgs { Symbol = "spy", From = new DateOnly(2026, 10, 29) }, MarketDataTestKit.Context(handler));

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(handler.Query("/v2/stocks/SPY/quotes", "sort"), Is.EqualTo("asc"));
        Assert.That(handler.Query("/v2/stocks/SPY/quotes", "start"), Is.EqualTo("2026-10-29T04:00:00Z"));
        var row = result.Human!.Split('\n').Single(l => l.StartsWith("2026-10-29"));
        Assert.That(row, Does.Contain("10:00:00.000 AM ET").And.Contain("$500.01").And.Contain("$500.04").And.Contain("$0.03"));
    }
}
