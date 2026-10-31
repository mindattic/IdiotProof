using IdiotProof.Operations.Market;

namespace IdiotProof.Operations.Tests.Market;

/// <summary>market news (IP-T204): Alpaca news newest first, paged up to --limit.</summary>
public class MarketNewsOperationTests
{
    [Test]
    public async Task PagesUntilTheLimit_NewestFirst_TimesInEt()
    {
        var handler = new ScriptedMarketHandler().On("/v1beta1/news", """
            {"news":[{"id":3,"headline":"Apple beats","author":"A","created_at":"2026-10-30T13:05:00Z","updated_at":"2026-10-30T13:05:00Z",
                      "summary":"Revenue up.","url":"https://example.com/3","symbols":["AAPL"],"source":"benzinga"},
                     {"id":2,"headline":"Apple event","created_at":"2026-10-29T20:00:00Z","symbols":["AAPL","MSFT"],"source":"benzinga"}],
             "next_page_token":"p2"}
            """);

        var result = await new MarketNewsOperation().ExecuteAsync(new MarketNewsArgs { Symbols = ["aapl"], Limit = 2 }, MarketDataTestKit.Context(handler));

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(handler.Requests, Has.Count.EqualTo(1), "stops paging once --limit articles are in");
        Assert.That(handler.Query("/v1beta1/news", "symbols"), Is.EqualTo("AAPL"));
        Assert.That(handler.Query("/v1beta1/news", "sort"), Is.EqualTo("desc"));
        var lines = result.Human!.Split('\n');
        Assert.That(lines[0], Is.EqualTo("News for AAPL (2, newest first)"));
        Assert.That(lines.Single(l => l.Contains("Apple beats")), Does.StartWith("2026-10-30 9:05 AM ET"));
        Assert.That(lines.Single(l => l.Contains("Apple event")), Does.Contain("AAPL,MSFT"));
    }

    [Test]
    public async Task Summary_PrintsEachArticleAsABlock()
    {
        var handler = new ScriptedMarketHandler().On("/v1beta1/news", """
            {"news":[{"id":3,"headline":"Apple beats","created_at":"2026-10-30T13:05:00Z","summary":"Revenue up.","url":"https://example.com/3","symbols":["AAPL"],"source":"benzinga"}]}
            """);

        var result = await new MarketNewsOperation().ExecuteAsync(new MarketNewsArgs { Summary = true }, MarketDataTestKit.Context(handler));

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(handler.Query("/v1beta1/news", "symbols"), Is.Null);
        Assert.That(result.Human, Does.Contain("Summary:").And.Contain("Revenue up.").And.Contain("https://example.com/3"));
    }
}
