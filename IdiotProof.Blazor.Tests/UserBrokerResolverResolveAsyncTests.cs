using IdiotProof.Blazor.Data;
using IdiotProof.Blazor.Services;
using IdiotProof.Brokers;
using IdiotProof.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace IdiotProof.Blazor.Tests;

/// <summary>
/// Integration coverage for UserBrokerResolver.ResolveAsync's Vault-aware path
/// (LocalDB + a real UserKeyService, same harness as UserKeyServiceTests).
/// UserBrokerResolverTests only exercises the pure Choose()/HasPair() helpers,
/// which ResolveAsync no longer even calls now that it checks MindAttic.Vault's
/// Brokers bucket first — this pins the rule an adversarial review caught as
/// a real regression: Vault supplies the CREDENTIAL PAIR only, never the
/// DefaultBroker opt-in, so a user who hasn't enabled Alpaca routing can't be
/// silently upgraded off Sandbox just because a providers.json exists.
/// </summary>
[TestFixture]
public sealed class UserBrokerResolverResolveAsyncTests
{
    private static readonly string DbName = $"IdiotProof_Test_{Guid.NewGuid():N}";
    private static readonly string ConnStr =
        $@"Server=(localdb)\MSSQLLocalDB;Database={DbName};Trusted_Connection=True;TrustServerCertificate=True;";

    private IDbContextFactory<AppDbContext> factory = null!;
    private UserKeyService keys = null!;
    private BrokerRouter router = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        factory = new SqlServerDbContextFactory(ConnStr);
        using var db = factory.CreateDbContext();
        db.Database.EnsureCreated();
        db.Database.ExecuteSqlRaw("ALTER TABLE UserApiKeys NOCHECK CONSTRAINT ALL;");
        keys = new UserKeyService(factory, new EphemeralDataProtectionProvider(), NullLogger<UserKeyService>.Instance);

        router = new BrokerRouter();
        router.Register(new SandboxBrokerClient());
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        using var db = factory.CreateDbContext();
        db.Database.EnsureDeleted();
    }

    private static IConfiguration VaultConfig(string? paperKeyId = null, string? paperSecret = null) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["MindAttic:Vault:Brokers:alpaca-paper:apiKey"] = paperKeyId,
            ["MindAttic:Vault:Brokers:alpaca-paper:secret"] = paperSecret,
        }!).Build();

    [Test]
    public async Task VaultHasKeys_ButUserNeverOptedIntoAlpaca_StillRoutesToSandbox()
    {
        var userId = Guid.NewGuid();
        await keys.SaveAsync(userId, new UserApiKeys { UserId = userId, DefaultBroker = "Sandbox" });

        var resolver = new UserBrokerResolver(keys, router, VaultConfig("PKVAULT", "vaultsecret"), NullLogger<UserBrokerResolver>.Instance);
        var broker = await resolver.ResolveAsync(userId, "Paper");

        Assert.That(broker.BrokerType, Is.EqualTo(BrokerType.Sandbox),
            "a Vault-supplied credential pair must never bypass the DefaultBroker opt-in — that's the sole consent gate for real-money routing.");
    }

    [Test]
    public async Task VaultHasKeys_UserOptedIn_ButDbHasNoKeys_RoutesToUserAlpaca()
    {
        var userId = Guid.NewGuid();
        await keys.SaveAsync(userId, new UserApiKeys { UserId = userId, DefaultBroker = "alpaca" });

        var resolver = new UserBrokerResolver(keys, router, VaultConfig("PKVAULT", "vaultsecret"), NullLogger<UserBrokerResolver>.Instance);
        var broker = await resolver.ResolveAsync(userId, "Paper");

        Assert.That(broker.BrokerType, Is.EqualTo(BrokerType.Alpaca),
            "once opted in, Vault should supply the pair when the DB has none.");
    }

    [Test]
    public async Task NoVaultAndNoDbKeys_OptedIn_FallsBackToSandbox()
    {
        var userId = Guid.NewGuid();
        await keys.SaveAsync(userId, new UserApiKeys { UserId = userId, DefaultBroker = "alpaca" });

        var resolver = new UserBrokerResolver(keys, router, VaultConfig(), NullLogger<UserBrokerResolver>.Instance);
        var broker = await resolver.ResolveAsync(userId, "Paper");

        Assert.That(broker.BrokerType, Is.EqualTo(BrokerType.Sandbox));
    }
}

// Same shape as the file-scoped factory in UserKeyServiceTests/
// StrategyRepositoryGuardTests — file classes are intentionally not shared
// across files.
file sealed class SqlServerDbContextFactory(string connectionString) : IDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .Options);
}
