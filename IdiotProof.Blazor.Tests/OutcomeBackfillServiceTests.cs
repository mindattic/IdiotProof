using IdiotProof.Blazor.Data;
using IdiotProof.Blazor.Services;
using IdiotProof.Engine.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace IdiotProof.Blazor.Tests;

/// <summary>
/// Proves the claim outlined in RFC 0003 / IP-A32: that the pipeline actually checks whether
/// "the news and the price are inter-related" instead of assuming it. A Bullish claim followed
/// by a real price rise should backfill as Realized with a positive OutcomePctChange and bump
/// the source's ImmediateCorrect/PortentsRealized count; a Bullish claim followed by a price
/// drop should not.
/// </summary>
[TestFixture]
public sealed class OutcomeBackfillServiceTests
{
    private static readonly string DbName = $"IdiotProof_Test_{Guid.NewGuid():N}";
    private static readonly string ConnStr =
        $@"Server=(localdb)\MSSQLLocalDB;Database={DbName};Trusted_Connection=True;TrustServerCertificate=True;";

    private IDbContextFactory<AppDbContext> factory = null!;

    private static readonly DateOnly ArticleDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-10);
    private static readonly DateOnly OutcomeDate  = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-2);

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
        db.SourceTrustScores.RemoveRange(db.SourceTrustScores);
        db.SaveChanges();
    }

    private static string BarsJson(decimal claimClose, decimal outcomeClose) => $$"""
        {"bars":[
            {"t":"{{ArticleDate:yyyy-MM-dd}}T04:00:00Z","o":1,"h":1,"l":1,"c":{{claimClose}},"v":1000},
            {"t":"{{OutcomeDate:yyyy-MM-dd}}T04:00:00Z","o":1,"h":1,"l":1,"c":{{outcomeClose}},"v":1000}
        ],"symbol":"AAPL","next_page_token":null}
        """;

    private ResearchClaim SeedClaim(string sentiment, string sourceName = "benzinga", bool hasHappened = true)
    {
        using var db = factory.CreateDbContext();
        var trust = db.SourceTrustScores.Find(sourceName) ?? new SourceTrustScore { SourceName = sourceName, SourceTier = 2 };
        if (db.Entry(trust).State == EntityState.Detached) db.SourceTrustScores.Add(trust);
        trust.TotalClaims++;
        if (hasHappened) trust.ImmediateClaims++; else trust.PortentsClaimed++;

        var claim = new ResearchClaim
        {
            Ticker = "AAPL",
            SourceName = sourceName,
            ArticleDate = ArticleDate,
            ClaimSummary = "test claim",
            Sentiment = sentiment,
            Magnitude = "High",
            HasHappenedAlready = hasHappened,
            Status = hasHappened ? "Realized" : "Pending",
        };
        db.ResearchClaims.Add(claim);
        db.SaveChanges();
        return claim;
    }

    private OutcomeBackfillService BuildService(string barsJson)
    {
        var handler = new StubHandler(barsJson);
        var settings = new AppSettings { AlpacaApiKeyId = "key", AlpacaApiSecretKey = "secret" };
        return new OutcomeBackfillService(new StubHttpClientFactory(handler), settings, factory, NullLogger<OutcomeBackfillService>.Instance);
    }

    [Test]
    public async Task BackfillAsync_NoApiKey_NoOpsWithoutError()
    {
        SeedClaim("Bullish");
        var svc = new OutcomeBackfillService(new StubHttpClientFactory(new StubHandler(BarsJson(100, 110))),
            new AppSettings(), factory, NullLogger<OutcomeBackfillService>.Instance);

        var count = await svc.BackfillAsync();
        Assert.That(count, Is.EqualTo(0));
    }

    [Test]
    public async Task BackfillAsync_BullishClaimWithPriceRise_MarksRealizedWithPositiveChange()
    {
        var claim = SeedClaim("Bullish");
        var svc = BuildService(BarsJson(claimClose: 100m, outcomeClose: 110m));

        var count = await svc.BackfillAsync();
        Assert.That(count, Is.EqualTo(1));

        using var db = factory.CreateDbContext();
        var updated = db.ResearchClaims.Single(c => c.Id == claim.Id);
        Assert.Multiple(() =>
        {
            Assert.That(updated.PriceAtClaim, Is.EqualTo(100m));
            Assert.That(updated.PriceAtOutcome, Is.EqualTo(110m));
            Assert.That(updated.OutcomePctChange, Is.EqualTo(10m));
            Assert.That(updated.OutcomeDate, Is.EqualTo(OutcomeDate));

            var trust = db.SourceTrustScores.Single(s => s.SourceName == "benzinga");
            Assert.That(trust.ImmediateCorrect, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task BackfillAsync_BullishClaimWithPriceDrop_DoesNotCountAsCorrect()
    {
        SeedClaim("Bullish");
        var svc = BuildService(BarsJson(claimClose: 100m, outcomeClose: 90m));

        await svc.BackfillAsync();

        using var db = factory.CreateDbContext();
        var trust = db.SourceTrustScores.Single(s => s.SourceName == "benzinga");
        Assert.That(trust.ImmediateCorrect, Is.EqualTo(0));
    }

    [Test]
    public async Task BackfillAsync_PendingPortent_BullishConfirmedByRise_SetsStatusRealized()
    {
        var claim = SeedClaim("Bullish", hasHappened: false);
        var svc = BuildService(BarsJson(claimClose: 100m, outcomeClose: 110m));

        await svc.BackfillAsync();

        using var db = factory.CreateDbContext();
        var updated = db.ResearchClaims.Single(c => c.Id == claim.Id);
        Assert.That(updated.Status, Is.EqualTo("Realized"));

        var trust = db.SourceTrustScores.Single(s => s.SourceName == "benzinga");
        Assert.That(trust.PortentsRealized, Is.EqualTo(1));
    }

    [Test]
    public async Task BackfillAsync_PendingPortent_BullishDisprovenByDrop_SetsStatusDisproven()
    {
        var claim = SeedClaim("Bullish", hasHappened: false);
        var svc = BuildService(BarsJson(claimClose: 100m, outcomeClose: 90m));

        await svc.BackfillAsync();

        using var db = factory.CreateDbContext();
        var updated = db.ResearchClaims.Single(c => c.Id == claim.Id);
        Assert.That(updated.Status, Is.EqualTo("Disproven"));
    }

    [Test]
    public async Task BackfillAsync_NeutralSentiment_IsNotCountedEitherWay()
    {
        SeedClaim("Neutral");
        var svc = BuildService(BarsJson(claimClose: 100m, outcomeClose: 110m));

        var count = await svc.BackfillAsync();
        Assert.That(count, Is.EqualTo(1)); // still backfills price fields

        using var db = factory.CreateDbContext();
        var trust = db.SourceTrustScores.Single(s => s.SourceName == "benzinga");
        Assert.That(trust.ImmediateCorrect, Is.EqualTo(0));
    }

    [Test]
    public async Task BackfillAsync_ClaimTooRecent_IsLeftForALaterPass()
    {
        using (var db = factory.CreateDbContext())
        {
            db.ResearchClaims.Add(new ResearchClaim
            {
                Ticker = "AAPL", SourceName = "benzinga",
                ArticleDate = DateOnly.FromDateTime(DateTime.UtcNow), // today — window hasn't elapsed
                ClaimSummary = "too fresh", Sentiment = "Bullish", Magnitude = "High",
                HasHappenedAlready = true, Status = "Realized",
            });
            db.SaveChanges();
        }

        var svc = BuildService(BarsJson(100, 110));
        var count = await svc.BackfillAsync(minWaitDays: 5);
        Assert.That(count, Is.EqualTo(0));
    }

    [Test]
    public async Task BackfillAsync_MacroClaim_IsSkipped()
    {
        using (var db = factory.CreateDbContext())
        {
            db.ResearchClaims.Add(new ResearchClaim
            {
                Ticker = "", IsMacro = true, SourceName = "Federal Register / SEC",
                ArticleDate = ArticleDate, ClaimSummary = "macro", Sentiment = "Neutral", Magnitude = "High",
                HasHappenedAlready = true, Status = "Realized",
            });
            db.SaveChanges();
        }

        var svc = BuildService(BarsJson(100, 110));
        var count = await svc.BackfillAsync();
        Assert.That(count, Is.EqualTo(0));
    }

    private sealed class StubHandler(string barsJson) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(barsJson) });
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
