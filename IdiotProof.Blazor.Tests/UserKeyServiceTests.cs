using IdiotProof.Blazor.Data;
using IdiotProof.Blazor.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace IdiotProof.Blazor.Tests;

/// <summary>
/// Per-user key persistence. Every sensitive field must survive the
/// encrypt→persist→decrypt round trip.
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
    public async Task SaveAndReload_RoundTripsEverySensitiveField()
    {
        var userId = Guid.NewGuid();
        await svc.SaveAsync(userId, new UserApiKeys
        {
            UserId             = userId,
            AlpacaApiKeyId     = "PK-ALPACA-ID",
            AlpacaApiSecretKey = "alpaca-secret-xyz",
            ClaudeApiKey       = "sk-ant-abc",
            ClaudeModel        = "claude-sonnet-5",
            LlmVotingEnabled   = true,
            DefaultBroker      = "alpaca",
            DefaultDataFeed    = "Alpaca",
        });

        var reloaded = await svc.GetOrCreateAsync(userId);

        Assert.Multiple(() =>
        {
            Assert.That(reloaded.AlpacaApiKeyId, Is.EqualTo("PK-ALPACA-ID"));
            Assert.That(reloaded.AlpacaApiSecretKey, Is.EqualTo("alpaca-secret-xyz"));
            Assert.That(reloaded.ClaudeApiKey, Is.EqualTo("sk-ant-abc"));
            Assert.That(reloaded.DefaultBroker, Is.EqualTo("alpaca"));
            Assert.That(reloaded.DefaultDataFeed, Is.EqualTo("Alpaca"));
        });
    }

    [Test]
    public async Task ResavingUnrelatedField_DoesNotWipeOtherKeys()
    {
        var userId = Guid.NewGuid();
        await svc.SaveAsync(userId, new UserApiKeys { UserId = userId, AlpacaApiKeyId = "KEEP-ME" });

        var current = await svc.GetOrCreateAsync(userId);
        current.ClaudeApiKey = "sk-ant-new";
        await svc.SaveAsync(userId, current);

        var reloaded = await svc.GetOrCreateAsync(userId);
        Assert.That(reloaded.AlpacaApiKeyId, Is.EqualTo("KEEP-ME"), "an unrelated save must not wipe the Alpaca key");
    }

    [Test]
    public void StoredSecrets_AreEncryptedAtRest()
    {
        var userId = Guid.NewGuid();
        svc.SaveAsync(userId, new UserApiKeys { UserId = userId, ClaudeApiKey = "PLAINTEXT-CLAUDE" }).GetAwaiter().GetResult();

        using var db = factory.CreateDbContext();
        var raw = db.UserApiKeys.AsNoTracking().First(k => k.UserId == userId);
        Assert.That(raw.ClaudeApiKey, Is.Not.EqualTo("PLAINTEXT-CLAUDE"),
            "the Claude key must be encrypted at rest, not stored in the clear");
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
