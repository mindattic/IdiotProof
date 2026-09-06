using IdiotProof.Blazor.Data;
using IdiotProof.Blazor.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace IdiotProof.Blazor.Tests;

/// <summary>
/// IndexEventScanner (RFC 0004): the hand-maintained sp-index-events.json becomes
/// ClaimType=IndexEvent research claims — idempotently, with the Pending→Realized flip
/// once the effective date passes.
/// </summary>
[TestFixture]
public sealed class IndexEventScannerTests
{
    private static readonly string DbName = $"IdiotProof_Test_{Guid.NewGuid():N}";
    private static readonly string ConnStr =
        $@"Server=(localdb)\MSSQLLocalDB;Database={DbName};Trusted_Connection=True;TrustServerCertificate=True;";

    private IDbContextFactory<AppDbContext> factory = null!;
    private string tempFile = null!;

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

    [SetUp]
    public void SetUp() => tempFile = Path.Combine(Path.GetTempPath(), $"sp-index-events-{Guid.NewGuid():N}.json");

    [TearDown]
    public void TearDown()
    {
        if (File.Exists(tempFile)) File.Delete(tempFile);
        using var db = factory.CreateDbContext();
        db.ResearchClaims.RemoveRange(db.ResearchClaims);
        db.SaveChanges();
    }

    private IndexEventScanner Build(string json)
    {
        File.WriteAllText(tempFile, json);
        return new IndexEventScanner(factory, NullLogger<IndexEventScanner>.Instance) { FilePath = tempFile };
    }

    private const string TwoEvents = """
        {"events":[
          {"ticker":"be","index":"SP500","action":"Add","announcedDate":"2026-09-05","effectiveDate":"2026-09-22","sourceUrl":"https://press.spglobal.com/x","note":"Bloom joins"},
          {"ticker":"XYZ","index":"SP100","action":"Remove","announcedDate":"2026-09-05","effectiveDate":null,"sourceUrl":null,"note":null}
        ]}
        """;

    [Test]
    public async Task ScanAsync_PersistsOneClaimPerEvent_WithPortentSemantics()
    {
        var scanner = Build(TwoEvents);
        var count = await scanner.ScanAsync(new DateOnly(2026, 9, 6));
        Assert.That(count, Is.EqualTo(2));

        await using var db = factory.CreateDbContext();
        var be = await db.ResearchClaims.SingleAsync(c => c.Ticker == "BE");
        var xyz = await db.ResearchClaims.SingleAsync(c => c.Ticker == "XYZ");
        Assert.Multiple(() =>
        {
            Assert.That(be.ClaimType, Is.EqualTo("IndexEvent"));
            Assert.That(be.IsMacro, Is.False, "an index add is about one company");
            Assert.That(be.Sentiment, Is.EqualTo("Bullish"));
            Assert.That(be.Magnitude, Is.EqualTo("High"));
            Assert.That(be.HasHappenedAlready, Is.False, "effective date is still ahead");
            Assert.That(be.Status, Is.EqualTo("Pending"));
            Assert.That(be.SourceTier, Is.EqualTo(1), "press-release URL supplied");
            Assert.That(be.ClaimSummary, Is.EqualTo("Added to the S&P 500 (effective 2026-09-22)"));
            Assert.That(be.LlmAnswer, Does.Contain("must buy BE"));

            Assert.That(xyz.Sentiment, Is.EqualTo("Bearish"));
            Assert.That(xyz.Magnitude, Is.EqualTo("Medium"));
            Assert.That(xyz.SourceTier, Is.EqualTo(3), "no source URL → unknown trust");
            Assert.That(xyz.ExpectedTimeline, Is.EqualTo("TBA"));
            Assert.That(xyz.ClaimSummary, Does.Contain("effective date TBA"));
        });
    }

    [Test]
    public async Task ScanAsync_IsIdempotent_AcrossPasses()
    {
        var scanner = Build(TwoEvents);
        Assert.That(await scanner.ScanAsync(new DateOnly(2026, 9, 6)), Is.EqualTo(2));
        Assert.That(await scanner.ScanAsync(new DateOnly(2026, 9, 6)), Is.EqualTo(0));

        await using var db = factory.CreateDbContext();
        Assert.That(await db.ResearchClaims.CountAsync(), Is.EqualTo(2));
    }

    [Test]
    public async Task ScanAsync_FlipsPendingToRealized_OnceEffectiveDatePasses()
    {
        var scanner = Build(TwoEvents);
        await scanner.ScanAsync(new DateOnly(2026, 9, 6));
        var flipped = await scanner.ScanAsync(new DateOnly(2026, 9, 23));
        Assert.That(flipped, Is.EqualTo(0), "flips are updates, not new claims");

        await using var db = factory.CreateDbContext();
        var be = await db.ResearchClaims.SingleAsync(c => c.Ticker == "BE");
        Assert.Multiple(() =>
        {
            Assert.That(be.HasHappenedAlready, Is.True);
            Assert.That(be.Status, Is.EqualTo("Realized"));
            Assert.That(be.OutcomeDate, Is.EqualTo(new DateOnly(2026, 9, 22)));
        });
    }

    [Test]
    public async Task ScanAsync_SkipsMalformedEntries_AndKeepsGoing()
    {
        var scanner = Build("""
            {"events":[
              {"ticker":"","index":"SP500","action":"Add","announcedDate":"2026-09-05"},
              {"ticker":"AAA","index":"RUSSELL","action":"Add","announcedDate":"2026-09-05"},
              {"ticker":"BBB","index":"SP500","action":"Add","announcedDate":"not-a-date"},
              {"ticker":"CCC","index":"SP500","action":"Add","announcedDate":"2026-09-05"}
            ]}
            """);
        Assert.That(await scanner.ScanAsync(new DateOnly(2026, 9, 6)), Is.EqualTo(1));
    }

    [Test]
    public async Task ScanAsync_MissingOrBrokenFile_IsZeroNotAnException()
    {
        var missing = new IndexEventScanner(factory, NullLogger<IndexEventScanner>.Instance) { FilePath = Path.Combine(Path.GetTempPath(), "nope.json") };
        Assert.That(await missing.ScanAsync(), Is.EqualTo(0));

        var broken = Build("{ this is not json");
        Assert.That(await broken.ScanAsync(), Is.EqualTo(0));
    }
}

file sealed class SqlServerDbContextFactory(string connectionString) : IDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .Options);
}
