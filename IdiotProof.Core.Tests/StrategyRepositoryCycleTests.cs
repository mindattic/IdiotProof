using IdiotProof.Core.Data;
using IdiotProof.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Blazor.Tests;

/// <summary>
/// Repeat(n) counts entries per ET day on the Strategy row (IP-T370). Integration tests against
/// SQL Server LocalDB, same harness as StrategyRepositoryGuardTests.
/// </summary>
[TestFixture]
public sealed class StrategyRepositoryCycleTests
{
    private static readonly string DbName = $"IdiotProof_Test_{Guid.NewGuid():N}";
    private static readonly string ConnStr =
        $@"Server=(localdb)\MSSQLLocalDB;Database={DbName};Trusted_Connection=True;TrustServerCertificate=True;";

    private IDbContextFactory<AppDbContext> factory = null!;
    private StrategyRepository repo = null!;
    private readonly Guid owner = Guid.NewGuid();

    // 2026-06-01 14:00 UTC = 10:00 ET; 2026-06-02 03:30 UTC = 23:30 ET on 06-01 (still the same ET day).
    private static readonly DateTime Morning = new(2026, 6, 1, 14, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime LateSameEtDay = new(2026, 6, 2, 3, 30, 0, DateTimeKind.Utc);
    private static readonly DateTime NextEtDay = new(2026, 6, 2, 14, 0, 0, DateTimeKind.Utc);

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        factory = new SqlServerDbContextFactory(ConnStr);
        using var db = factory.CreateDbContext();
        db.Database.EnsureCreated();
        db.Database.ExecuteSqlRaw("ALTER TABLE Strategies NOCHECK CONSTRAINT ALL;");
        repo = new StrategyRepository(factory);
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        using var db = factory.CreateDbContext();
        db.Database.EnsureDeleted();
    }

    private Task<Strategy> CreateAsync() =>
        repo.CreateAsync(owner, title: "cycle test", symbol: "TEST",
            scriptText: "Stock.Ticker(\"TEST\").Long().StopLoss(1).TakeProfit(2).Repeat(2).Build()");

    [Test]
    public async Task EntryFills_CountPerEtDay_AndResetOnANewDay()
    {
        var s = await CreateAsync();

        await repo.RecordEntryFillAsync(s.Id, 1, 10m, Morning);
        await repo.RecordExitFillAsync(s.Id, 11m, "Target", Morning.AddMinutes(5));
        await repo.RecordEntryFillAsync(s.Id, 1, 10m, LateSameEtDay);
        var sameDay = (await repo.GetByIdAsync(s.Id))!;

        await repo.RecordExitFillAsync(s.Id, 11m, "Target", LateSameEtDay.AddMinutes(5));
        await repo.RecordEntryFillAsync(s.Id, 1, 10m, NextEtDay);
        var nextDay = (await repo.GetByIdAsync(s.Id))!;

        Assert.Multiple(() =>
        {
            Assert.That(sameDay.CyclesToday, Is.EqualTo(2));
            Assert.That(sameDay.CycleDateEt, Is.EqualTo("2026-06-01"));
            Assert.That(sameDay.CyclesOn(LateSameEtDay), Is.EqualTo(2));
            Assert.That(sameDay.CyclesOn(NextEtDay), Is.EqualTo(0), "yesterday's count never gates today");
            Assert.That(nextDay.CyclesToday, Is.EqualTo(1));
            Assert.That(nextDay.CycleDateEt, Is.EqualTo("2026-06-02"));
        });
    }

    [Test]
    public async Task UnfilledEntry_GivesItsCycleBack()
    {
        var s = await CreateAsync();

        await repo.RecordEntryFillAsync(s.Id, 1, 10m, Morning);
        await repo.ClearUnfilledEntryAsync(s.Id);
        var row = (await repo.GetByIdAsync(s.Id))!;

        Assert.That(row.CyclesToday, Is.EqualTo(0), "a phantom fill must not use up a Repeat(n) cycle");
    }
}

file sealed class SqlServerDbContextFactory(string connectionString) : IDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .Options);
}
