using IdiotProof.Blazor.Data;
using IdiotProof.Blazor.Services;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Blazor.Tests;

/// <summary>
/// IP-A19: the Settings page's risk editor persists through
/// <see cref="UserPreferencesService.SetRiskConfigAsync"/>, which promises the
/// basic sanity invariants (daily ≥ per-trade, max stop ≥ min stop,
/// non-negative amounts) so RiskGuardian config loads never see nonsense.
/// Integration tests against SQL Server LocalDB, same harness as
/// StrategyRepositoryGuardTests.
/// </summary>
[TestFixture]
public sealed class UserPreferencesServiceTests
{
    private static readonly string DbName = $"IdiotProof_Test_{Guid.NewGuid():N}";
    private static readonly string ConnStr =
        $@"Server=(localdb)\MSSQLLocalDB;Database={DbName};Trusted_Connection=True;TrustServerCertificate=True;";

    private IDbContextFactory<AppDbContext> factory = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        factory = new SqlServerDbContextFactory(ConnStr);
        using var db = factory.CreateDbContext();
        db.Database.EnsureCreated();
        // FK UserPreferences.UserId → AspNetUsers is not under test; disable
        // so arbitrary user Guids work without seeding Identity users.
        db.Database.ExecuteSqlRaw("ALTER TABLE UserPreferences NOCHECK CONSTRAINT ALL;");
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        using var db = factory.CreateDbContext();
        db.Database.EnsureDeleted();
    }

    [Test]
    public async Task SetRiskConfigAsync_ClampsInvariants_AndPersists()
    {
        var service = new UserPreferencesService(factory);
        var userId = Guid.NewGuid();

        // Deliberately inverted/invalid inputs: daily < per-trade,
        // max stop < min stop, negative balance, >100% account risk.
        var saved = await service.SetRiskConfigAsync(userId,
            maxLossPerTrade: 200m, maxLossPerDay: 50m,
            minStopPct: 2m, maxStopPct: 1m,
            accountBalance: -5m, maxAccountRiskPercent: 150m);

        Assert.Multiple(() =>
        {
            Assert.That(saved.RiskMaxLossPerTrade, Is.EqualTo(200m));
            Assert.That(saved.RiskMaxLossPerDay, Is.EqualTo(200m), "daily cap raised to at least the per-trade cap");
            Assert.That(saved.RiskMinStopLossPercent, Is.EqualTo(2m));
            Assert.That(saved.RiskMaxStopLossPercent, Is.EqualTo(2m), "max stop raised to at least the min stop");
            Assert.That(saved.RiskAccountBalance, Is.EqualTo(0m), "negative balance clamped to zero");
            Assert.That(saved.RiskMaxAccountRiskPercent, Is.EqualTo(100m), "account risk capped at 100%");
        });

        // And the row round-trips from SQL, not just the in-memory object.
        var reloaded = await service.GetOrCreateAsync(userId);
        Assert.That(reloaded.RiskMaxLossPerDay, Is.EqualTo(200m));
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
