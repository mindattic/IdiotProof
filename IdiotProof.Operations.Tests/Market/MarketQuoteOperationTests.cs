using IdiotProof.Operations.Market;

namespace IdiotProof.Operations.Tests.Market;

/// <summary>market quote (IP-T200): latest trade + latest quote for several symbols in one table.</summary>
public class MarketQuoteOperationTests
{
    [Test]
    public async Task OneRowPerSymbol_TimesInEt_FeedFromTheEnvironment()
    {
        var handler = new ScriptedMarketHandler()
            .On("/v2/stocks/trades/latest", """
                {"trades":{"AAPL":{"t":"2026-10-30T17:59:58.123456789Z","x":"V","p":231.45,"s":100,"c":["@"],"i":1,"z":"C"},
                           "MSFT":{"t":"2026-10-30T17:59:59Z","x":"Q","p":0.5123,"s":12,"c":["@"],"i":2,"z":"C"}}}
                """)
            .On("/v2/stocks/quotes/latest", """
                {"quotes":{"AAPL":{"t":"2026-10-30T17:59:58.5Z","ax":"V","ap":231.47,"as":3,"bx":"V","bp":231.43,"bs":2,"c":["R"],"z":"C"}}}
                """);

        var result = await new MarketQuoteOperation().ExecuteAsync(new MarketQuoteArgs { Symbols = ["aapl", "msft,ZZZZ"] }, MarketDataTestKit.Context(handler));

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(handler.Query("/v2/stocks/trades/latest", "symbols"), Is.EqualTo("AAPL,MSFT,ZZZZ"));
        Assert.That(handler.Query("/v2/stocks/quotes/latest", "feed"), Is.EqualTo(MarketData.Feed));
        var lines = result.Human!.Split('\n');
        Assert.That(lines[1], Does.StartWith("Symbol"));
        var aapl = lines.Single(l => l.StartsWith("AAPL"));
        Assert.That(aapl, Does.Contain("$231.45").And.Contain("2026-10-30 1:59 PM ET").And.Contain("$231.43").And.Contain("$0.04"));
        Assert.That(lines.Single(l => l.StartsWith("MSFT")), Does.Contain("$0.5123"));
        Assert.That(result.Human, Does.Contain("No data for: ZZZZ"));
    }

    [Test]
    public async Task NoSymbols_IsAUsageError()
    {
        var result = await new MarketQuoteOperation().ExecuteAsync(new MarketQuoteArgs(), MarketDataTestKit.Context(new ScriptedMarketHandler()));

        Assert.That(result.Status, Is.EqualTo(OperationStatus.UsageError));
    }
}
