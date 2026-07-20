using IdiotProof.Blazor.Data;
using IdiotProof.Blazor.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace IdiotProof.Blazor.Tests;

/// <summary>
/// Per-user key persistence (bring-your-own-Alpaca-key is the whole product).
/// IP-A22 regression: EVERY sensitive field must survive the
/// encrypt→persist→decrypt round trip — PolygonApiKey was silently dropped by
/// both Encrypt and Decrypt, so a saved Polygon key was wiped to null every
/// time and the Backtest page's real-data feed could never activate.
/// LocalDB integration test, same harness as StrategyRepositoryGuardTests.
/// </summary>
[TestFixture]
public sealed class UserKeyServiceTests
{
    private static readonly string DbName = $"IdiotProof_Test_{Guid.NewGuid():N}";
    private static readonly string ConnStr =
        $@"Server=(localdb)\MSSQLLocalDB;Database={DbName};Trusted_Connection=True;TrustServerCertificate=True;";

    private IDbContextFactory<AppDbContext> factory = null!;
    private UserKeyService svc = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        factory = new SqlServerDbContextFactory(ConnStr);
        using var db = factory.CreateDbContext();
        db.Database.EnsureCreated();
        db.Database.ExecuteSqlRaw("ALTER TABLE UserApiKeys NOCHECK CONSTRAINT ALL;");
        svc = new UserKeyService(factory, new EphemeralDataProtectionProvider(), NullLogger<UserKeyService>.Instance);
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        using var db = factory.CreateDbContext();
        db.Database.EnsureDeleted();
    }

    [Test]
    public async Task SaveAndReload_RoundTripsEverySensitiveField_IncludingPolygon()
    {
        var userId = Guid.NewGuid();
        await svc.SaveAsync(userId, new UserApiKeys
        {
            UserId             = userId,
            AlpacaApiKeyId     = "PK-ALPACA-ID",
            AlpacaApiSecretKey = "alpaca-secret-xyz",
            AlpacaIsPaper      = false,
            ClaudeApiKey       = "sk-ant-abc",
            ClaudeModel        = "claude-sonnet-5",
            LlmVotingEnabled   = true,
            PolygonApiKey      = "POLY-KEY-123",
            DefaultBroker      = "alpaca",
            DefaultDataFeed    = "Polygon",
        });

        var reloaded = await svc.GetOrCreateAsync(userId);

        Assert.Multiple(() =>
        {
            Assert.That(reloaded.AlpacaApiKeyId, Is.EqualTo("PK-ALPACA-ID"));
            Assert.That(reloaded.AlpacaApiSecretKey, Is.EqualTo("alpaca-secret-xyz"));
            Assert.That(reloaded.AlpacaIsPaper, Is.False);
            Assert.That(reloaded.ClaudeApiKey, Is.EqualTo("sk-ant-abc"));
            Assert.That(reloaded.PolygonApiKey, Is.EqualTo("POLY-KEY-123"), "Polygon key must survive the round trip");
            Assert.That(reloaded.DefaultBroker, Is.EqualTo("alpaca"));
            Assert.That(reloaded.DefaultDataFeed, Is.EqualTo("Polygon"));
        });
    }

    [Test]
    public async Task ResavingWithoutTouchingPolygon_DoesNotWipeIt()
    {
        var userId = Guid.NewGuid();
        await svc.SaveAsync(userId, new UserApiKeys { UserId = userId, PolygonApiKey = "KEEP-ME" });

        // Simulate an unrelated edit that carries the reloaded value forward.
        var current = await svc.GetOrCreateAsync(userId);
        current.ClaudeApiKey = "sk-ant-new";
        await svc.SaveAsync(userId, current);

        var reloaded = await svc.GetOrCreateAsync(userId);
        Assert.That(reloaded.PolygonApiKey, Is.EqualTo("KEEP-ME"), "an unrelated save must not wipe the Polygon key");
    }

    [Test]
    public void StoredSecrets_AreEncryptedAtRest()
    {
        var userId = Guid.NewGuid();
        svc.SaveAsync(userId, new UserApiKeys { UserId = userId, PolygonApiKey = "PLAINTEXT-POLY" }).GetAwaiter().GetResult();

        using var db = factory.CreateDbContext();
        var raw = db.UserApiKeys.AsNoTracking().First(k => k.UserId == userId);
        Assert.That(raw.PolygonApiKey, Is.Not.EqualTo("PLAINTEXT-POLY"),
            "the Polygon key must be encrypted at rest, not stored in the clear");
    }
}

// Same shape as the file-scoped factory in StrategyRepositoryGuardTests —
// file classes are intentionally not shared across files.
file sealed class SqlServerDbContextFactory(string connectionString) : IDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .Options);
}
