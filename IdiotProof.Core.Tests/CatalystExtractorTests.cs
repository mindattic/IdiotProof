using System.Net;
using System.Text;
using System.Text.Json;
using IdiotProof.Blazor.Services;
using IdiotProof.Engine.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using MindAttic.Legion;

namespace IdiotProof.Blazor.Tests;

/// <summary>
/// IP-T7: <see cref="CatalystExtractor"/>, the entry point for every research claim:
/// <see cref="ExtractedCatalyst.ComposeSentence"/> and <see cref="CatalystExtractor.ExtractAsync(string,string,string,string?,CancellationToken)"/>
/// parsing an Anthropic-shaped reply from a scripted Legion handler (no network).
/// </summary>
[TestFixture]
public sealed class CatalystExtractorTests
{
    // ── ComposeSentence ─────────────────────────────────────────────────────────

    [Test]
    public void ComposeSentence_NoMechanism_ReturnsSummaryUnchanged()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ExtractedCatalyst.ComposeSentence("Q3 beat", "aapl", "", "weeks"), Is.EqualTo("Q3 beat"));
            Assert.That(ExtractedCatalyst.ComposeSentence("Q3 beat", "aapl", "   ", null), Is.EqualTo("Q3 beat"));
        });
    }

    [Test]
    public void ComposeSentence_UpperCasesTicker_AndUsesTimeline() =>
        Assert.That(ExtractedCatalyst.ComposeSentence("Contract pending", "lmt", "adds revenue", "weeks"),
            Is.EqualTo("Contract pending. Affects LMT because adds revenue. Expected impact: weeks."));

    [Test]
    public void ComposeSentence_BlankTicker_UsesAffectedTickers_NullTimeline_ReadsAlreadyPricedIn() =>
        Assert.That(ExtractedCatalyst.ComposeSentence("Rate cut", " ", "lowers financing costs", null),
            Is.EqualTo("Rate cut. Affects the affected tickers because lowers financing costs. Expected impact: already priced in."));

    // ── ExtractAsync ────────────────────────────────────────────────────────────

    private const string FullJson = """
        {"ticker":"NVDA","catalysts":[
          {"summary":"Preliminary order pending signature","type":"Contract","sentiment":"Bullish","magnitude":"High",
           "has_happened_already":false,"pending_trigger":"Signature","expected_timeline":"weeks",
           "trigger_confidence":"High","mechanism":"adds multi-year revenue"}],
         "source_assessment":"Editorial","source_tier":2}
        """;

    private static (CatalystExtractor Extractor, ScriptedClaude Llm) Make(string reply, string? hostKey = "sk-host")
    {
        var llm = new ScriptedClaude(reply);
        var extractor = new CatalystExtractor(new LegionClient(new HttpClient(llm), options: null),
            new AppSettings { ClaudeApiKey = hostKey ?? "" }, NullLogger<CatalystExtractor>.Instance);
        return (extractor, llm);
    }

    [Test]
    public async Task Extract_ParsesAFullReply()
    {
        var (x, _) = Make(FullJson);
        var r = await x.ExtractAsync("nvda", "article", "Reuters");

        Assert.That(r, Is.Not.Null);
        var c = r!.Catalysts.Single();
        Assert.Multiple(() =>
        {
            Assert.That(r.Ticker, Is.EqualTo("NVDA"));
            Assert.That(r.SourceAssessment, Is.EqualTo("Editorial"));
            Assert.That(r.SourceTierSuggestion, Is.EqualTo(2));
            Assert.That(c.Summary, Is.EqualTo("Preliminary order pending signature"));
            Assert.That(c.Type, Is.EqualTo("Contract"));
            Assert.That(c.Sentiment, Is.EqualTo("Bullish"));
            Assert.That(c.Magnitude, Is.EqualTo("High"));
            Assert.That(c.HasHappenedAlready, Is.False);
            Assert.That(c.PendingTrigger, Is.EqualTo("Signature"));
            Assert.That(c.ExpectedTimeline, Is.EqualTo("weeks"));
            Assert.That(c.TriggerConfidence, Is.EqualTo("High"));
            Assert.That(c.Mechanism, Is.EqualTo("adds multi-year revenue"));
        });
    }

    [TestCase("```json\n{0}\n```")]
    [TestCase("Here is the extraction you asked for:\n{0}\nLet me know if you need more.")]
    public async Task Extract_StripsMarkdownOrProseAroundTheJson(string wrapper)
    {
        var (x, _) = Make(wrapper.Replace("{0}", FullJson));
        var r = await x.ExtractAsync("NVDA", "article", "src");
        Assert.That(r?.Catalysts, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Extract_MissingFields_FallBackToNewsNeutralLowTier3()
    {
        var (x, _) = Make("""{"catalysts":[{"summary":"Something"}]}""");
        var r = await x.ExtractAsync("amd", "article", "src");

        Assert.That(r, Is.Not.Null);
        var c = r!.Catalysts.Single();
        Assert.Multiple(() =>
        {
            Assert.That(r.Ticker, Is.EqualTo("amd"), "ticker falls back to the input");
            Assert.That(r.SourceAssessment, Is.EqualTo("Unknown"));
            Assert.That(r.SourceTierSuggestion, Is.EqualTo(3));
            Assert.That(c.Type, Is.EqualTo("News"));
            Assert.That(c.Sentiment, Is.EqualTo("Neutral"));
            Assert.That(c.Magnitude, Is.EqualTo("Low"));
            Assert.That(c.HasHappenedAlready, Is.False);
            Assert.That(c.PendingTrigger, Is.Null);
            Assert.That(c.ExpectedTimeline, Is.Null);
            Assert.That(c.TriggerConfidence, Is.Null);
            Assert.That(c.Mechanism, Is.EqualTo(""));
        });
    }

    [Test]
    public async Task Extract_NoCatalystsArray_ReturnsEmptyList()
    {
        var (x, _) = Make("""{"ticker":"AMD"}""");
        var r = await x.ExtractAsync("AMD", "a", "s");
        Assert.That(r?.Catalysts, Is.Empty);
    }

    [TestCase("true", true)]
    [TestCase("false", false)]
    [TestCase("\"true\"", false)]
    [TestCase("1", false)]
    [TestCase("null", false)]
    public async Task Extract_HasHappenedAlready_OnlyTrueForJsonTrue(string literal, bool expected)
    {
        var (x, _) = Make($$"""{"catalysts":[{"summary":"s","has_happened_already":{{literal}}}]}""");
        var r = await x.ExtractAsync("T", "a", "s");
        Assert.That(r!.Catalysts.Single().HasHappenedAlready, Is.EqualTo(expected));
    }

    [Test]
    public async Task Extract_NonStringFieldsFallBack()
    {
        var (x, _) = Make("""{"catalysts":[{"summary":5,"type":null,"pending_trigger":7}]}""");
        var c = (await x.ExtractAsync("T", "a", "s"))!.Catalysts.Single();
        Assert.Multiple(() =>
        {
            Assert.That(c.Summary, Is.EqualTo(""));
            Assert.That(c.Type, Is.EqualTo("News"));
            Assert.That(c.PendingTrigger, Is.Null);
        });
    }

    [Test]
    public async Task Extract_NullTickerInReply_FallsBackToInput()
    {
        var (x, _) = Make("""{"ticker":null,"catalysts":[]}""");
        Assert.That((await x.ExtractAsync("KO", "a", "s"))!.Ticker, Is.EqualTo("KO"));
    }

    [Test]
    public async Task Extract_TruncatesArticleTextTo8000Chars()
    {
        var (x, llm) = Make(FullJson);
        var article = new string('a', 7990) + new string('Z', 500);
        await x.ExtractAsync("NVDA", article, "src");

        var sent = llm.UserMessages.Single();
        Assert.Multiple(() =>
        {
            Assert.That(sent, Does.Contain(new string('a', 7990) + new string('Z', 10)));
            Assert.That(sent, Does.Not.Contain(new string('Z', 11)));
            Assert.That(sent, Does.StartWith("Ticker: NVDA\nSource: src"));
        });
    }

    [TestCase("not json at all")]
    [TestCase("{ this is : broken }")]
    [TestCase("""{"catalysts":"oops"}""")]
    [TestCase("""{"catalysts":[],"source_tier":"two"}""")]
    public async Task Extract_MalformedJson_ReturnsNull(string reply)
    {
        var (x, _) = Make(reply);
        Assert.That(await x.ExtractAsync("T", "a", "s"), Is.Null);
    }

    [Test]
    public async Task Extract_NoApiKey_ReturnsNull_WithoutCallingTheLlm()
    {
        using var noVault = new EmptyLlmVault();
        var (x, llm) = Make(FullJson, hostKey: null);
        Assert.Multiple(async () =>
        {
            Assert.That(await x.ExtractAsync("T", "a", "s"), Is.Null);
            Assert.That(await x.ExtractAsync("T", "a", "s", claudeApiKey: "  "), Is.Null);
            Assert.That(llm.UserMessages, Is.Empty);
        });
    }

    [Test]
    public async Task Extract_UserKeyWorksWithoutAHostKey()
    {
        using var noVault = new EmptyLlmVault();
        var (x, llm) = Make(FullJson, hostKey: null);
        Assert.Multiple(async () =>
        {
            Assert.That(await x.ExtractAsync("T", "a", "s", claudeApiKey: "sk-user"), Is.Not.Null);
            Assert.That(llm.Keys.Single(), Is.EqualTo("sk-user"));
        });
    }
}

/// <summary>
/// An Anthropic-shaped responder for Legion: returns <c>reply</c> as the text block and records the
/// x-api-key header and the user message of every call.
/// </summary>
internal sealed class ScriptedClaude(string reply) : HttpMessageHandler
{
    public List<string> Keys { get; } = [];

    public List<string> UserMessages { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Keys.Add(request.Headers.TryGetValues("x-api-key", out var k) ? k.Single() : "");
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
        UserMessages.Add(ExtractUserMessage(body));
        var payload = JsonSerializer.Serialize(new { content = new[] { new { type = "text", text = reply } }, stop_reason = "end_turn" });
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(payload, Encoding.UTF8, "application/json") };
    }

    private static string ExtractUserMessage(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            foreach (var m in doc.RootElement.GetProperty("messages").EnumerateArray())
            {
                if (m.GetProperty("role").GetString() != "user") continue;
                var c = m.GetProperty("content");
                return c.ValueKind == JsonValueKind.String
                    ? c.GetString() ?? ""
                    : string.Concat(c.EnumerateArray().Select(p => p.TryGetProperty("text", out var t) ? t.GetString() : ""));
            }
        }
        catch (JsonException) { }
        return body;
    }
}
