using IdiotProof.Operations.Research;

namespace IdiotProof.Operations.Tests.Research;

/// <summary>research analyze (IP-T236): CatalystExtractor through a scripted LLM; news and pages are scripted too.</summary>
public class ResearchAnalyzeOperationTests
{
    [Test]
    public async Task PastedText_IsExtracted_OnTheOwnersKey_AndNotSaved()
    {
        var kit = new ResearchKit();

        var result = await new ResearchAnalyzeOperation().ExecuteAsync(
            new ResearchAnalyzeArgs { Target = "nvda", Text = "NVIDIA nears a $5B order" }, kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.Contain("NVDA: 1 catalysts from 1 document(s) (not saved")
            .And.Contain("Preliminary $5B data-center order").And.Contain("portent: Contract signature, weeks, High confidence")
            .And.Contain("why: adds a large multi-year revenue stream"));
        Assert.That(kit.Llm.Requests.Single(), Does.Contain("NVIDIA nears a $5B order"));
        Assert.That(kit.Llm.Headers.Single(), Does.Contain("owner-key"));
        await using var db = await kit.Db.CreateDbContextAsync();
        Assert.That(db.ResearchClaims.Count(), Is.Zero);
    }

    [Test]
    public async Task Url_IsFetched_StrippedOfHtml_AndNeedsATicker()
    {
        var kit = new ResearchKit();
        kit.Http.On("GET /news/story", "<html><head><script>var x=1;</script></head><body><h1>Deal</h1><p>NVIDIA &amp; partner sign</p></body></html>");

        var noTicker = await new ResearchAnalyzeOperation().ExecuteAsync(
            new ResearchAnalyzeArgs { Target = "https://news.example/news/story" }, kit.Context());
        var result = await new ResearchAnalyzeOperation().ExecuteAsync(
            new ResearchAnalyzeArgs { Target = "https://news.example/news/story", Ticker = "NVDA" }, kit.Context());

        Assert.That(noTicker.Status, Is.EqualTo(OperationStatus.UsageError));
        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(kit.Llm.Requests.Single(), Does.Contain("Deal NVIDIA").And.Contain("partner sign").And.Not.Contain("var x").And.Not.Contain("amp;"));
        Assert.That(result.Human, Does.Contain("from: news.example https://news.example/news/story"));
    }

    [Test]
    public async Task Ticker_AnalyzesRecentAlpacaNews()
    {
        var kit = new ResearchKit();
        kit.Http.On("GET /v1beta1/news", """
            {"news":[{"headline":"NVDA wins order","summary":"Big one","source":"benzinga","url":"https://b.example/1","created_at":"2026-10-29T14:00:00Z"}]}
            """);

        var result = await new ResearchAnalyzeOperation().ExecuteAsync(new ResearchAnalyzeArgs { Target = "NVDA" }, kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(kit.Llm.Requests.Single(), Does.Contain("NVDA wins order"));
        Assert.That(result.Human, Does.Contain("from: benzinga https://b.example/1"));
    }
}
