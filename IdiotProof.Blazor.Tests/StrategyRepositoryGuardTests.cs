using IdiotProof.Blazor.Data;
using IdiotProof.Blazor.Services;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Blazor.Tests;

/// <summary>
/// The guarded strategy mutators (IP-A16): SetActive/Delete enforce ownership
/// (no caller may flip another user's row) and open-position safety (a row
/// holding shares must stay active so the Monitor keeps managing the exit;
/// deleting it would discard the exit rules for a live position).
/// Integration tests against SQL Server LocalDB, same harness as
/// ConditionProgressRepositoryTests.
/// </summary>
[TestFixture]
public sealed class StrategyRepositoryGuardTests
{
    private static readonly string DbName = $"IdiotProof_Test_{Guid.NewGuid():N}";
    private static readonly string ConnStr =
        $@"Server=(localdb)\MSSQLLocalDB;Database={DbName};Trusted_Connection=True;TrustServerCertificate=True;";

    private IDbContextFactory<AppDbContext> factory = null!;
    private StrategyRepository repo = null!;
    private readonly Guid owner = Guid.NewGuid();
    private readonly Guid stranger = Guid.NewGuid();

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        factory = new SqlServerDbContextFactory(ConnStr);
        using var db = factory.CreateDbContext();
        db.Database.EnsureCreated();
        // FK Strategy.OwnerUserId → AspNetUsers is not under test; disable so
        // arbitrary owner Guids work without seeding Identity users.
        db.Database.ExecuteSqlRaw("ALTER TABLE Strategies NOCHECK CONSTRAINT ALL;");
        repo = new StrategyRepository(factory);
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
        db.Strategies.RemoveRange(db.Strategies);
        db.SaveChanges();
    }

    private Task<Strategy> CreateAsync(string symbol = "TEST") =>
        repo.CreateAsync(owner, title: $"{symbol} guard test", symbol: symbol,
            scriptText: $"Stock.Ticker(\"{symbol}\").Long().StopLoss(1).TakeProfit(2).Build()");

    [Test]
    public async Task SetActiveAsync_WrongOwner_IsRefused()
    {
        var s = await CreateAsync();

        var result = await repo.SetActiveAsync(s.Id, true, stranger);

        Assert.Multiple(async () =>
        {
            Assert.That(result, Is.EqualTo(StrategyMutation.NotOwner));
            var row = await repo.GetByIdAsync(s.Id);
            Assert.That(row!.IsActive, Is.False, "the stranger's toggle must not stick");
        });
    }

    [Test]
    public async Task SetActiveAsync_DeactivatingAHoldingRow_IsRefused()
    {
        var s = await CreateAsync();
        await repo.SetActiveAsync(s.Id, true, owner);
        await repo.RecordEntryFillAsync(s.Id, quantity: 100, fillPrice: 5.25m, DateTime.UtcNow);

        var result = await repo.SetActiveAsync(s.Id, false, owner);

        Assert.Multiple(async () =>
        {
            Assert.That(result, Is.EqualTo(StrategyMutation.PositionOpen));
            var row = await repo.GetByIdAsync(s.Id);
            Assert.That(row!.IsActive, Is.True,
                "the Monitor only manages exits on active rows — pausing a holding row would orphan the position");
        });
    }

    [Test]
    public async Task DeleteAsync_HoldingRow_IsRefused()
    {
        var s = await CreateAsync();
        await repo.SetActiveAsync(s.Id, true, owner);
        await repo.RecordEntryFillAsync(s.Id, quantity: 50, fillPrice: 3.10m, DateTime.UtcNow);

        var result = await repo.DeleteAsync(s.Id, owner);

        Assert.Multiple(async () =>
        {
            Assert.That(result, Is.EqualTo(StrategyMutation.PositionOpen));
            Assert.That(await repo.GetByIdAsync(s.Id), Is.Not.Null, "the row must survive");
        });
    }

    [Test]
    public async Task DeleteAsync_WrongOwner_IsRefused()
    {
        var s = await CreateAsync();

        var result = await repo.DeleteAsync(s.Id, stranger);

        Assert.Multiple(async () =>
        {
            Assert.That(result, Is.EqualTo(StrategyMutation.NotOwner));
            Assert.That(await repo.GetByIdAsync(s.Id), Is.Not.Null);
        });
    }

    [Test]
    public async Task DeleteAsync_FlatOwnedRow_Succeeds()
    {
        var s = await CreateAsync();

        var result = await repo.DeleteAsync(s.Id, owner);

        Assert.Multiple(async () =>
        {
            Assert.That(result, Is.EqualTo(StrategyMutation.Ok));
            Assert.That(await repo.GetByIdAsync(s.Id), Is.Null);
        });
    }

    [Test]
    public async Task UpdateAsync_NeverClobbersTheMonitorsPositionBookkeeping()
    {
        // IP-A21: the editor's Save used to write the WHOLE detached row —
        // a snapshot loaded before the Monitor filled the position stomped
        // PositionQty back to 0: orphaned shares AND a re-armed duplicate fire.
        var s = await CreateAsync("BOOK");
        var stale = await repo.GetByIdAsync(s.Id); // editor's snapshot (flat)

        await repo.SetActiveAsync(s.Id, true, owner);
        await repo.RecordEntryFillAsync(s.Id, quantity: 25, fillPrice: 7.77m, DateTime.UtcNow); // Monitor fills

        stale!.Title = "Edited title";
        stale.IsActive = true;
        var result = await repo.UpdateAsync(stale);

        var row = await repo.GetByIdAsync(s.Id);
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(StrategyMutation.Ok));
            Assert.That(row!.Title, Is.EqualTo("Edited title"), "editor-owned field saved");
            Assert.That(row.PositionQty, Is.EqualTo(25), "the Monitor's live position survives the Save");
            Assert.That(row.LastEntryPrice, Is.EqualTo(7.77m));
        });
    }

    [Test]
    public async Task UpdateAsync_DeactivatingAHoldingRow_IsRefused()
    {
        var s = await CreateAsync("HOLD2");
        await repo.SetActiveAsync(s.Id, true, owner);
        await repo.RecordEntryFillAsync(s.Id, quantity: 10, fillPrice: 4.20m, DateTime.UtcNow);

        var edited = await repo.GetByIdAsync(s.Id);
        edited!.IsActive = false; // user unchecks Active in the editor

        var result = await repo.UpdateAsync(edited);

        Assert.Multiple(async () =>
        {
            Assert.That(result, Is.EqualTo(StrategyMutation.PositionOpen));
            var row = await repo.GetByIdAsync(s.Id);
            Assert.That(row!.IsActive, Is.True, "same guard as SetActiveAsync, enforced against the FRESH row");
        });
    }

    [Test]
    public async Task DeleteAsync_TakesTheConditionProgressRowWithIt()
    {
        // IP-A21: no FK links ConditionProgress to Strategies, so deleted
        // strategies used to leave badge rows orphaned forever.
        var s = await CreateAsync("PROG");
        var progressRepo = new ConditionProgressRepository(factory);
        await progressRepo.UpsertAsync(s.Id, 2, 5, "IsGapUp(5)");
        Assert.That(await progressRepo.GetAsync(s.Id), Is.Not.Null, "sanity: progress row exists");

        var result = await repo.DeleteAsync(s.Id, owner);

        Assert.Multiple(async () =>
        {
            Assert.That(result, Is.EqualTo(StrategyMutation.Ok));
            Assert.That(await progressRepo.GetAsync(s.Id), Is.Null, "progress row deleted alongside");
        });
    }

    [Test]
    public async Task CountActiveForSymbolAsync_CountsOnlyActiveRowsForThatSymbolAndOwner()
    {
        var a = await CreateAsync("ACME");           // will be active
        await CreateAsync("ACME");                   // inactive — not counted
        var other = await CreateAsync("OTHER");      // active, different symbol
        await repo.SetActiveAsync(a.Id, true, owner);
        await repo.SetActiveAsync(other.Id, true, owner);

        Assert.Multiple(async () =>
        {
            Assert.That(await repo.CountActiveForSymbolAsync(owner, "ACME"), Is.EqualTo(1));
            Assert.That(await repo.CountActiveForSymbolAsync(owner, "acme"), Is.EqualTo(1), "symbol match is case-insensitive");
            Assert.That(await repo.CountActiveForSymbolAsync(stranger, "ACME"), Is.EqualTo(0), "scoped to the owner");
        });
    }
}

// Same shape as the file-scoped factory in ConditionProgressRepositoryTests —
// file classes are intentionally not shared across files.
file sealed class SqlServerDbContextFactory(string connectionString) : IDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .Options);
}
