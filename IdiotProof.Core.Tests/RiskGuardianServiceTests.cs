using IdiotProof.Core.Data;
using Microsoft.EntityFrameworkCore;

using IdiotProof.Core.Services;
namespace IdiotProof.Blazor.Tests;

/// <summary>
/// The per-user Guardian cache must NEVER discard a Guardian instance while
/// the process lives — its in-memory daily-loss counter is the daily circuit
/// breaker (IP-LAW-2). Config refreshes via UpdateConfig precisely
/// to avoid rebuilding the instance; these tests pin that Invalidate (the
/// Settings page's post-edit hook) also preserves the instance instead of
/// dropping it and silently resetting the day's losses.
/// Integration tests against a real SQL Server (opt-in, see TestSqlServer), same harness as
/// StrategyRepositoryGuardTests.
/// </summary>
[TestFixture]
[Category(global::IdiotProof.Core.Tests.TestSqlServer.Category)]
public sealed class RiskGuardianServiceTests
{
    private static readonly string DbName = global::IdiotProof.Core.Tests.TestSqlServer.NewDatabaseName("IdiotProof_Test");
    private static string ConnStr => global::IdiotProof.Core.Tests.TestSqlServer.ConnectionStringFor(DbName);

    private IDbContextFactory<AppDbContext> factory = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        global::IdiotProof.Core.Tests.TestSqlServer.Require();
        factory = new SqlServerDbContextFactory(ConnStr);
        using var db = factory.CreateDbContext();
        db.Database.EnsureCreated();
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        if (!global::IdiotProof.Core.Tests.TestSqlServer.IsConfigured) return;
        using var db = factory.CreateDbContext();
        db.Database.EnsureDeleted();
    }

    [Test]
    public async Task Invalidate_ForcesConfigReread_ButPreservesTheDailyLossCounter()
    {
        var service = new RiskGuardianService(factory);
        var userId = Guid.NewGuid(); // no UserPreferences row → canonical defaults

        var guardian = await service.GetForUserAsync(userId, IdiotProof.Core.Trading.BrokerMode.Paper);
        guardian.RecordTradePnL(-300m);
        var remainingBefore = guardian.GetRemainingDailyRisk();

        service.Invalidate(userId);
        var after = await service.GetForUserAsync(userId, IdiotProof.Core.Trading.BrokerMode.Paper);

        Assert.Multiple(() =>
        {
            Assert.That(after, Is.SameAs(guardian),
                "Invalidate must expire the config, not drop the Guardian — dropping it resets the daily circuit breaker");
            Assert.That(after.GetRemainingDailyRisk(), Is.EqualTo(remainingBefore),
                "the -$300 recorded loss survives the invalidate");
        });
    }

    [Test]
    public async Task EachAccount_HasItsOwnDailyLossCounter()
    {
        // IP-T575: a losing Paper day must not use up Live's (or Sandbox's) daily loss room.
        var service = new RiskGuardianService(factory);
        var userId = Guid.NewGuid();
        var paper = await service.GetForUserAsync(userId, IdiotProof.Core.Trading.BrokerMode.Paper);
        var live = await service.GetForUserAsync(userId, IdiotProof.Core.Trading.BrokerMode.Live);
        var roomBefore = live.GetRemainingDailyRisk();

        paper.RecordTradePnL(-300m);

        Assert.Multiple(() =>
        {
            Assert.That(live, Is.Not.SameAs(paper));
            Assert.That(live.GetRemainingDailyRisk(), Is.EqualTo(roomBefore));
            Assert.That(paper.GetRemainingDailyRisk(), Is.EqualTo(roomBefore - 300m));
        });
    }

    [Test]
    public async Task Invalidate_UnknownUser_IsANoOp()
    {
        var service = new RiskGuardianService(factory);
        Assert.DoesNotThrow(() => service.Invalidate(Guid.NewGuid()));
        Assert.That(await service.GetForUserAsync(Guid.NewGuid(), IdiotProof.Core.Trading.BrokerMode.Paper), Is.Not.Null);
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
