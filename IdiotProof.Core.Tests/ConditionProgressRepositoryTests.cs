using IdiotProof.Blazor.Data;
using IdiotProof.Blazor.Services;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Blazor.Tests;

[TestFixture]
public sealed class ConditionProgressRepositoryTests
{
    private static readonly string DbName = $"IdiotProof_Test_{Guid.NewGuid():N}";
    private static readonly string ConnStr =
        $@"Server=(localdb)\MSSQLLocalDB;Database={DbName};Trusted_Connection=True;TrustServerCertificate=True;";

    private IDbContextFactory<AppDbContext> factory = null!;
    private ConditionProgressRepository repo = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        factory = new SqlServerDbContextFactory(ConnStr);
        using var db = factory.CreateDbContext();
        db.Database.EnsureCreated();
        // FK ConditionProgress.StrategyId → Strategy is not under test here;
        // disable it so arbitrary Guids can be used without seeding the full hierarchy.
        db.Database.ExecuteSqlRaw(
            "ALTER TABLE ConditionProgress NOCHECK CONSTRAINT ALL;");
        repo = new ConditionProgressRepository(factory);
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
        db.ConditionProgress.RemoveRange(db.ConditionProgress);
        db.SaveChanges();
    }

    // ── Insert / update ──────────────────────────────────────────────────────

    [Test]
    public async Task UpsertAsync_FirstCall_InsertsRow()
    {
        var id = Guid.NewGuid();
        await repo.UpsertAsync(id, 3, 5, "IsAboveVwap()");

        var row = await repo.GetAsync(id);
        Assert.That(row, Is.Not.Null);
        Assert.That(row!.PassedCount, Is.EqualTo(3));
        Assert.That(row.TotalCount, Is.EqualTo(5));
        Assert.That(row.FirstFailingVerb, Is.EqualTo("IsAboveVwap()"));
    }

    [Test]
    public async Task UpsertAsync_SecondCall_UpdatesExistingRow()
    {
        var id = Guid.NewGuid();
        await repo.UpsertAsync(id, 2, 5, "OnReclaim(9)");
        await repo.UpsertAsync(id, 4, 5, "IsAboveVwap()");

        var row = await repo.GetAsync(id);
        Assert.That(row!.PassedCount, Is.EqualTo(4));
        Assert.That(row.TotalCount, Is.EqualTo(5));
        Assert.That(row.FirstFailingVerb, Is.EqualTo("IsAboveVwap()"));
    }

    [Test]
    public async Task UpsertAsync_FullPass_ClearsFirstFailingVerb()
    {
        var id = Guid.NewGuid();
        await repo.UpsertAsync(id, 3, 5, "RequireAdxAbove(20)");
        await repo.UpsertAsync(id, 5, 5, null);

        var row = await repo.GetAsync(id);
        Assert.That(row!.PassedCount, Is.EqualTo(5));
        Assert.That(row.FirstFailingVerb, Is.Null);
        Assert.That(row.IsFullPass, Is.True);
    }

    [Test]
    public async Task UpsertAsync_ZeroConditions_Stores_0_0_Null()
    {
        var id = Guid.NewGuid();
        await repo.UpsertAsync(id, 0, 0, null);

        var row = await repo.GetAsync(id);
        Assert.That(row!.PassedCount, Is.EqualTo(0));
        Assert.That(row.TotalCount, Is.EqualTo(0));
        Assert.That(row.FirstFailingVerb, Is.Null);
        Assert.That(row.IsFullPass, Is.False);
    }

    [Test]
    public async Task UpsertAsync_TwoStrategies_TrackIndependently()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        await repo.UpsertAsync(id1, 1, 3, "RequireAdxAbove(20)");
        await repo.UpsertAsync(id2, 2, 2, null);

        var row1 = await repo.GetAsync(id1);
        var row2 = await repo.GetAsync(id2);
        Assert.That(row1!.PassedCount, Is.EqualTo(1));
        Assert.That(row2!.PassedCount, Is.EqualTo(2));
        Assert.That(row2.IsFullPass, Is.True);
    }

    // ── Bulk read ────────────────────────────────────────────────────────────

    [Test]
    public async Task GetForStrategyIdsAsync_ReturnsDictionary_KeyedById()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var missing = Guid.NewGuid();

        await repo.UpsertAsync(id1, 1, 3, "X");
        await repo.UpsertAsync(id2, 3, 3, null);

        var dict = await repo.GetForStrategyIdsAsync([id1, id2, missing]);
        Assert.That(dict.ContainsKey(id1), Is.True);
        Assert.That(dict.ContainsKey(id2), Is.True);
        Assert.That(dict.ContainsKey(missing), Is.False);
        Assert.That(dict[id2].IsFullPass, Is.True);
    }

    [Test]
    public async Task GetForStrategyIdsAsync_EmptyList_ReturnsEmptyDictionary()
    {
        var dict = await repo.GetForStrategyIdsAsync([]);
        Assert.That(dict, Is.Empty);
    }

    [Test]
    public async Task GetAsync_UnknownId_ReturnsNull()
    {
        var row = await repo.GetAsync(Guid.NewGuid());
        Assert.That(row, Is.Null);
    }
}

file sealed class SqlServerDbContextFactory(string connectionString) : IDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .Options);
}
