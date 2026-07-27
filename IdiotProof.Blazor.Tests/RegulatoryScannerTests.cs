using System.Net;
using System.Text;
using System.Text.Json;
using IdiotProof.Blazor.Data;
using IdiotProof.Blazor.Services;
using IdiotProof.Engine.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MindAttic.Legion;

namespace IdiotProof.Blazor.Tests;

[TestFixture]
public sealed class RegulatoryScannerTests
{
    private static readonly string DbName = $"IdiotProof_Test_{Guid.NewGuid():N}";
    private static readonly string ConnStr =
        $@"Server=(localdb)\MSSQLLocalDB;Database={DbName};Trusted_Connection=True;TrustServerCertificate=True;";

    private IDbContextFactory<AppDbContext> factory = null!;

    // Modeled on the real 2026-07-27 Nasdaq MVLS Federal Register notice.
    private const string OneSroNotice = """
        {"results":[{
            "title":"Self-Regulatory Organizations; The Nasdaq Stock Market LLC; Order Granting Approval of a Proposed Rule Change, as Modified by Amendment No. 1, To Adopt a New Continued Listing Requirement",
            "document_number":"2026-15060",
            "html_url":"https://www.federalregister.gov/documents/2026/07/27/2026-15060/self-regulatory-organizations",
            "publication_date":"2026-07-27",
            "excerpts":"Nasdaq is adopting a new $5,000,000 Market Value of Listed Securities requirement."
        }]}
        """;

    private const string NonSroNotice = """
        {"results":[{
            "title":"Notice of Application for a Change in Fishing Regulations",
            "document_number":"2026-99999",
            "html_url":"https://www.federalregister.gov/documents/2026/07/27/2026-99999/fishing",
            "publication_date":"2026-07-27",
            "excerpts":"unrelated"
        }]}
        """;

    private const string OldSroNotice = """
        {"results":[{
            "title":"Self-Regulatory Organizations; Old Notice",
            "document_number":"2020-00001",
            "html_url":"https://www.federalregister.gov/documents/2020/01/01/2020-00001/old",
            "publication_date":"2020-01-01",
            "excerpts":"stale"
        }]}
        """;

    private const string SubstantiveAssessment = """
        {"is_substantive":true,
         "summary":"Nasdaq adopts a $5,000,000 continued-listing market value requirement",
         "mechanism":"Issuers below the threshold face immediate suspension and delisting with no cure period",
         "affected_description":"Nasdaq Capital Market issuers",
         "exchange":"Nasdaq",
         "threshold_value":5000000,
         "threshold_description":"Market Value of Listed Securities (MVLS)",
         "expected_timeline":"immediate",
         "sentiment":"Bearish",
         "magnitude":"High"}
        """;

    private const string NonSubstantiveAssessment = """
        {"is_substantive":false}
        """;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        factory = new SqlServerDbContextFactory(ConnStr);
        using var db = factory.CreateDbContext();
        db.Database.EnsureCreated();
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        using var db = factory.CreateDbContext();
        db.Database.EnsureDeleted();
    }

    [TearDown]
    public void TearDown()
    {
        using var db = factory.CreateDbContext();
        db.ResearchClaims.RemoveRange(db.ResearchClaims);
        db.SaveChanges();
    }

    private RegulatoryScanner BuildScanner(string federalRegisterBody, string llmAssessmentJson)
    {
        var handler = new RoutingHandler(federalRegisterBody, llmAssessmentJson);
        var legion = new LegionClient(new HttpClient(handler), options: null);
        var settings = new AppSettings { ClaudeApiKey = "test-key" };
        return new RegulatoryScanner(legion, settings, new StubHttpClientFactory(handler), factory, NullLogger<RegulatoryScanner>.Instance);
    }

    [Test]
    public async Task ScanAsync_SubstantiveNotice_PersistsMacroClaim()
    {
        var scanner = BuildScanner(OneSroNotice, SubstantiveAssessment);
        var count = await scanner.ScanAsync(DateTime.UtcNow.AddDays(-1));

        Assert.That(count, Is.EqualTo(1));

        await using var db = factory.CreateDbContext();
        var claim = await db.ResearchClaims.SingleAsync();
        Assert.Multiple(() =>
        {
            Assert.That(claim.IsMacro, Is.True);
            Assert.That(claim.Ticker, Is.Empty);
            Assert.That(claim.ClaimType, Is.EqualTo("Regulatory"));
            Assert.That(claim.SourceTier, Is.EqualTo(1));
            Assert.That(claim.LlmAnswer, Does.Contain("Nasdaq Capital Market issuers"));
            Assert.That(claim.LlmAnswer, Does.Contain("Expected impact: immediate"));
        });
    }

    [Test]
    public async Task ScanAsync_NonSubstantiveNotice_PersistsNothing()
    {
        var scanner = BuildScanner(OneSroNotice, NonSubstantiveAssessment);
        var count = await scanner.ScanAsync(DateTime.UtcNow.AddDays(-1));

        Assert.That(count, Is.EqualTo(0));
        await using var db = factory.CreateDbContext();
        Assert.That(await db.ResearchClaims.CountAsync(), Is.EqualTo(0));
    }

    [Test]
    public async Task ScanAsync_NonSroTitledNotice_IsFilteredOutBeforeLlmCall()
    {
        var scanner = BuildScanner(NonSroNotice, SubstantiveAssessment);
        var count = await scanner.ScanAsync(DateTime.UtcNow.AddDays(-1));
        Assert.That(count, Is.EqualTo(0));
    }

    [Test]
    public async Task ScanAsync_NoticeOlderThanSince_IsFilteredOut()
    {
        var scanner = BuildScanner(OldSroNotice, SubstantiveAssessment);
        var count = await scanner.ScanAsync(DateTime.UtcNow.AddDays(-1));
        Assert.That(count, Is.EqualTo(0));
    }

    [Test]
    public async Task ScanAsync_AlreadySeenSourceUrl_IsSkippedOnSecondScan()
    {
        var scanner = BuildScanner(OneSroNotice, SubstantiveAssessment);

        var first = await scanner.ScanAsync(DateTime.UtcNow.AddDays(-1));
        var second = await scanner.ScanAsync(DateTime.UtcNow.AddDays(-1));

        Assert.That(first, Is.EqualTo(1));
        Assert.That(second, Is.EqualTo(0)); // same html_url already persisted — dedup, not a duplicate claim

        await using var db = factory.CreateDbContext();
        Assert.That(await db.ResearchClaims.CountAsync(), Is.EqualTo(1));
    }

    private sealed class RoutingHandler(string federalRegisterBody, string llmAssessmentJson) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var isFederalRegister = request.RequestUri!.Host.Contains("federalregister.gov", StringComparison.OrdinalIgnoreCase);
            if (isFederalRegister)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(federalRegisterBody) });

            // Anthropic-Messages-shaped response — the only field LegionClient's claude
            // provider reads is content[0].text (same convention as FakeLlmHandler.cs).
            var payload = JsonSerializer.Serialize(new
            {
                content = new[] { new { type = "text", text = llmAssessmentJson } },
                stop_reason = "end_turn",
            });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}

file sealed class SqlServerDbContextFactory(string connectionString) : IDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .Options);
}
