using IdiotProof.Blazor.Data;
using IdiotProof.Blazor.Services;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Blazor.Tests;

/// <summary>
/// The per-user Guardian cache must NEVER discard a Guardian instance while
/// the process lives — its in-memory daily-loss counter is the daily circuit
/// breaker (IP-LAW-2). IP-A16 added config refresh via UpdateConfig precisely
/// to avoid rebuilding the instance; these tests pin that Invalidate (the
/// Settings page's post-edit hook) also preserves the instance instead of
/// dropping it and silently resetting the day's losses.
/// Integration tests against SQL Server LocalDB, same harness as
/// StrategyRepositoryGuardTests.
/// </summary>
[TestFixture]
public sealed class RiskGuardianServiceTests
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
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        using var db = factory.CreateDbContext();
        db.Database.EnsureDeleted();
    }

    [Test]
    public async Task Invalidate_ForcesConfigReread_ButPreservesTheDailyLossCounter()
    {
        var service = new RiskGuardianService(factory);
        var userId = Guid.NewGuid(); // no UserPreferences row → canonical defaults

        var guardian = await service.GetForUserAsync(userId);
        guardian.RecordTradePnL(-300m);
        var remainingBefore = guardian.GetRemainingDailyRisk();

        service.Invalidate(userId);
        var after = await service.GetForUserAsync(userId);

        Assert.Multiple(() =>
        {
            Assert.That(after, Is.SameAs(guardian),
                "Invalidate must expire the config, not drop the Guardian — dropping it resets the daily circuit breaker");
            Assert.That(after.GetRemainingDailyRisk(), Is.EqualTo(remainingBefore),
                "the -$300 recorded loss survives the invalidate");
        });
    }

    [Test]
    public async Task Invalidate_UnknownUser_IsANoOp()
    {
        var service = new RiskGuardianService(factory);
        Assert.DoesNotThrow(() => service.Invalidate(Guid.NewGuid()));
        Assert.That(await service.GetForUserAsync(Guid.NewGuid()), Is.Not.Null);
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
