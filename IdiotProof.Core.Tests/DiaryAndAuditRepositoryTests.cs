using IdiotProof.Blazor.Data;
using IdiotProof.Blazor.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace IdiotProof.Blazor.Tests;

/// <summary>
/// IP-T10: <see cref="TradeDiaryRepository"/> (the realized-P&amp;L record) and <see cref="AuditLogRepository"/>
/// reads and prune cutoff, on an in-memory database. The batched <c>DELETE TOP</c> itself is SQL Server
/// syntax, so the prune tests cover <see cref="AuditLogRepository.PruneCutoffAsync"/> (which rows survive)
/// and the early exits that never reach the delete.
/// </summary>
[TestFixture]
public sealed class DiaryAndAuditRepositoryTests
{
    private IDbContextFactory<AppDbContext> factory = null!;

    [SetUp]
    public void SetUp() =>
        factory = new ServiceCollection()
            .AddDbContextFactory<AppDbContext>(o => o.UseInMemoryDatabase($"diary-{Guid.NewGuid():N}"))
            .BuildServiceProvider()
            .GetRequiredService<IDbContextFactory<AppDbContext>>();

    private TradeDiaryRepository Diary() => new(factory);

    private AuditLogRepository Audit() => new(factory, NullLogger<AuditLogRepository>.Instance);

    private static TradeDiaryEntry Entry(Guid strategy, DateTime entryUtc, decimal price = 10m, int qty = 10, Guid? owner = null) => new()
    {
        StrategyId = strategy, OwnerUserId = owner ?? Guid.Empty, Symbol = "ABC", Broker = "Alpaca", IsPaper = true,
        EntryUtc = entryUtc, EntryPrice = price, Quantity = qty,
    };

    private async Task<List<TradeDiaryEntry>> RowsAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.TradeDiary.AsNoTracking().OrderBy(t => t.EntryUtc).ToListAsync();
    }

    // ── TradeDiaryRepository ───────────────────────────────────────────────────

    [Test]
    public async Task Open_MarksAnExistingOpenRowForTheSameStrategyOrphaned()
    {
        var s = Guid.NewGuid();
        var other = Guid.NewGuid();
        var t0 = DateTime.UtcNow.AddHours(-2);
        var first = await Diary().OpenAsync(Entry(s, t0));
        var otherId = await Diary().OpenAsync(Entry(other, t0));
        var second = await Diary().OpenAsync(Entry(s, t0.AddHours(1)));

        var rows = (await RowsAsync()).ToDictionary(r => r.Id);
        Assert.Multiple(() =>
        {
            Assert.That(rows[first].Status, Is.EqualTo(TradeDiaryStatus.Orphaned));
            Assert.That(rows[second].Status, Is.EqualTo(TradeDiaryStatus.Open));
            Assert.That(rows[otherId].Status, Is.EqualTo(TradeDiaryStatus.Open), "another strategy is untouched");
        });
    }

    [Test]
    public async Task Close_ClosesOnlyTheMostRecentOpenRow_AndComputesReturn()
    {
        var s = Guid.NewGuid();
        var t0 = DateTime.UtcNow.AddHours(-3);
        // Two Open rows for one strategy can only exist via a direct write (OpenAsync orphans); seed them.
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.TradeDiary.Add(Entry(s, t0));
            db.TradeDiary.Add(Entry(s, t0.AddHours(1), price: 20m, qty: 5));
            await db.SaveChangesAsync();
        }
        var exit = DateTime.UtcNow;

        await Diary().CloseAsync(s, 22m, "TakeProfit", "ord-9", realizedPnL: 10m, soldQty: 5, exit);

        var rows = await RowsAsync();
        Assert.Multiple(() =>
        {
            Assert.That(rows[0].Status, Is.EqualTo(TradeDiaryStatus.Open), "older row untouched");
            Assert.That(rows[1].Status, Is.EqualTo(TradeDiaryStatus.Closed));
            Assert.That(rows[1].ExitPrice, Is.EqualTo(22m));
            Assert.That(rows[1].ExitReason, Is.EqualTo("TakeProfit"));
            Assert.That(rows[1].ExitOrderId, Is.EqualTo("ord-9"));
            Assert.That(rows[1].ExitUtc, Is.EqualTo(exit));
            Assert.That(rows[1].RealizedPnL, Is.EqualTo(10m));
            Assert.That(rows[1].ReturnPercent, Is.EqualTo(10m), "10 / (20 x 5) = 10%");
            Assert.That(rows[1].Quantity, Is.EqualTo(5));
        });
    }

    [Test]
    public async Task Close_PartialFill_OverwritesQuantity_AndReturnUsesIt()
    {
        var s = Guid.NewGuid();
        await Diary().OpenAsync(Entry(s, DateTime.UtcNow.AddMinutes(-5), price: 10m, qty: 10));

        await Diary().CloseAsync(s, 11m, "Exit", null, realizedPnL: 4m, soldQty: 4, DateTime.UtcNow);

        var row = (await RowsAsync()).Single();
        Assert.Multiple(() =>
        {
            Assert.That(row.Quantity, Is.EqualTo(4));
            Assert.That(row.ReturnPercent, Is.EqualTo(10m), "4 / (10 x 4), not 4 / (10 x 10)");
        });
    }

    [Test]
    public async Task Close_ZeroSoldQty_KeepsRecordedQuantity()
    {
        var s = Guid.NewGuid();
        await Diary().OpenAsync(Entry(s, DateTime.UtcNow.AddMinutes(-5), price: 10m, qty: 10));
        await Diary().CloseAsync(s, 11m, "Exit", null, realizedPnL: 10m, soldQty: 0, DateTime.UtcNow);

        var row = (await RowsAsync()).Single();
        Assert.Multiple(() =>
        {
            Assert.That(row.Quantity, Is.EqualTo(10));
            Assert.That(row.ReturnPercent, Is.EqualTo(10m));
        });
    }

    [Test]
    public async Task Close_ZeroCost_ReturnIsZero()
    {
        var s = Guid.NewGuid();
        await Diary().OpenAsync(Entry(s, DateTime.UtcNow.AddMinutes(-5), price: 0m, qty: 10));
        await Diary().CloseAsync(s, 1m, "Exit", null, realizedPnL: 10m, soldQty: 10, DateTime.UtcNow);
        Assert.That((await RowsAsync()).Single().ReturnPercent, Is.EqualTo(0m));
    }

    [Test]
    public async Task Close_NoOpenRow_IsANoOp()
    {
        var s = Guid.NewGuid();
        await Diary().OpenAsync(Entry(s, DateTime.UtcNow.AddMinutes(-5)));
        await Diary().CloseAsync(s, 11m, "first", null, 1m, 10, DateTime.UtcNow);

        Assert.DoesNotThrowAsync(() => Diary().CloseAsync(s, 99m, "second", null, 999m, 10, DateTime.UtcNow));
        Assert.DoesNotThrowAsync(() => Diary().CloseAsync(Guid.NewGuid(), 99m, "foreign", null, 1m, 1, DateTime.UtcNow));

        var row = (await RowsAsync()).Single();
        Assert.Multiple(() =>
        {
            Assert.That(row.ExitReason, Is.EqualTo("first"));
            Assert.That(row.ExitPrice, Is.EqualTo(11m));
        });
    }

    [Test]
    public async Task MarkNotFilled_VoidsTheOpenRowWithZeroPnl()
    {
        var s = Guid.NewGuid();
        await Diary().OpenAsync(Entry(s, DateTime.UtcNow.AddMinutes(-5)));
        var when = DateTime.UtcNow;

        await Diary().MarkNotFilledAsync(s, when);
        await Diary().MarkNotFilledAsync(Guid.NewGuid(), when); // nothing open: no-op

        var row = (await RowsAsync()).Single();
        Assert.Multiple(() =>
        {
            Assert.That(row.Status, Is.EqualTo(TradeDiaryStatus.NotFilled));
            Assert.That(row.ExitUtc, Is.EqualTo(when));
            Assert.That(row.ExitReason, Is.EqualTo("NotFilled"));
            Assert.That(row.RealizedPnL, Is.EqualTo(0m));
            Assert.That(row.ReturnPercent, Is.EqualTo(0m));
        });
    }

    [Test]
    public async Task GetForUser_FiltersByOwner_NewestFirst_AndHonorsLimit()
    {
        var me = Guid.NewGuid();
        var t0 = DateTime.UtcNow.AddDays(-1);
        for (var i = 0; i < 5; i++)
            await Diary().OpenAsync(Entry(Guid.NewGuid(), t0.AddMinutes(i), owner: me));
        await Diary().OpenAsync(Entry(Guid.NewGuid(), t0.AddHours(5), owner: Guid.NewGuid()));

        var all = await Diary().GetForUserAsync(me);
        var three = await Diary().GetForUserAsync(me, limit: 3);
        var clamped = await Diary().GetForUserAsync(me, limit: 0);

        Assert.Multiple(() =>
        {
            Assert.That(all, Has.Count.EqualTo(5));
            Assert.That(all.Select(r => r.EntryUtc), Is.Ordered.Descending);
            Assert.That(three.Select(r => r.EntryUtc), Is.EqualTo(all.Take(3).Select(r => r.EntryUtc)));
            Assert.That(clamped, Has.Count.EqualTo(1), "limit clamps to at least 1");
        });
    }

    // ── AuditLogRepository ─────────────────────────────────────────────────────

    private async Task SeedAuditAsync(params (string Category, DateTime At)[] rows)
    {
        await using var db = await factory.CreateDbContextAsync();
        foreach (var (category, at) in rows)
            db.AuditLogs.Add(new AuditLog { Category = category, Message = category, TimestampUtc = at });
        await db.SaveChangesAsync();
    }

    [Test]
    public async Task GetByCategory_FiltersNewestFirst_AndClampsLimitTo1Through1000()
    {
        var t0 = DateTime.UtcNow.AddHours(-1);
        await SeedAuditAsync(Enumerable.Range(0, 1005).Select(i => ("signal", t0.AddSeconds(i))).Append(("other", t0)).ToArray());

        var two = await Audit().GetByCategoryAsync("signal", limit: 2);
        var zero = await Audit().GetByCategoryAsync("signal", limit: 0);
        var huge = await Audit().GetByCategoryAsync("signal", limit: 50_000);

        Assert.Multiple(() =>
        {
            Assert.That(two.Select(a => a.TimestampUtc), Is.EqualTo(new[] { t0.AddSeconds(1004), t0.AddSeconds(1003) }));
            Assert.That(zero, Has.Count.EqualTo(1));
            Assert.That(huge, Has.Count.EqualTo(1000));
            Assert.That(huge.All(a => a.Category == "signal"), Is.True);
        });
    }

    [Test]
    public async Task GetErrorCountSince_CountsOnlyStrategyErrorAndOrderRejectedAfterTheCutoff()
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-10);
        await SeedAuditAsync(
            ("strategy-error", cutoff.AddMinutes(1)),
            ("order-rejected", cutoff.AddMinutes(2)),
            ("order-rejected", cutoff),                 // exactly at the cutoff: not after
            ("strategy-error", cutoff.AddMinutes(-1)),  // before
            ("order-placed", cutoff.AddMinutes(3)),     // not an error category
            ("Strategy-Error", cutoff.AddMinutes(4)));  // categories are exact

        Assert.That(await Audit().GetErrorCountSinceAsync(cutoff), Is.EqualTo(2));
    }

    [Test]
    public async Task Prune_NothingToDoWhenTotalIsAtMostMinKeep()
    {
        var old = DateTime.UtcNow.AddDays(-90);
        await SeedAuditAsync(Enumerable.Range(0, 5).Select(i => ("x", old.AddMinutes(i))).ToArray());

        // Returns before the SQL Server-only DELETE runs, so this works on the in-memory provider.
        Assert.That(await Audit().PruneAsync(retentionDays: 30, minKeep: 5), Is.Zero);
        await using var db = await factory.CreateDbContextAsync();
        Assert.That(await db.AuditLogs.CountAsync(), Is.EqualTo(5));
    }

    [Test]
    public async Task PruneCutoff_EnoughRecentRows_IsTheRetentionBoundary()
    {
        var now = DateTime.UtcNow;
        await SeedAuditAsync(
            Enumerable.Range(0, 4).Select(i => ("old", now.AddDays(-60).AddMinutes(i)))
                .Concat(Enumerable.Range(0, 6).Select(i => ("new", now.AddDays(-1).AddMinutes(i)))).ToArray());

        await using var db = await factory.CreateDbContextAsync();
        var cutoff = await AuditLogRepository.PruneCutoffAsync(db, retentionDays: 30, minKeep: 5, now);

        Assert.Multiple(async () =>
        {
            Assert.That(cutoff, Is.EqualTo(now.AddDays(-30)));
            Assert.That(await db.AuditLogs.CountAsync(a => a.TimestampUtc >= cutoff), Is.EqualTo(6));
        });
    }

    [Test]
    public async Task PruneCutoff_FewerRecentThanMinKeep_WalksBackToKeepExactlyMinKeep()
    {
        var now = DateTime.UtcNow;
        await SeedAuditAsync(
            Enumerable.Range(0, 8).Select(i => ("old", now.AddDays(-60).AddMinutes(i)))
                .Concat(Enumerable.Range(0, 2).Select(i => ("new", now.AddDays(-1).AddMinutes(i)))).ToArray());

        await using var db = await factory.CreateDbContextAsync();
        var cutoff = await AuditLogRepository.PruneCutoffAsync(db, retentionDays: 30, minKeep: 5, now);

        Assert.Multiple(async () =>
        {
            Assert.That(cutoff, Is.EqualTo(now.AddDays(-60).AddMinutes(5)), "the 5th-newest row is the anchor");
            Assert.That(await db.AuditLogs.CountAsync(a => a.TimestampUtc >= cutoff), Is.EqualTo(5), "rows kept (DELETE is TimestampUtc < cutoff)");
        });
    }

    [Test]
    public async Task PruneCutoff_AtOrBelowMinKeep_IsNull()
    {
        await SeedAuditAsync(("a", DateTime.UtcNow.AddDays(-90)), ("b", DateTime.UtcNow.AddDays(-90)));
        await using var db = await factory.CreateDbContextAsync();
        Assert.That(await AuditLogRepository.PruneCutoffAsync(db, 30, minKeep: 2, DateTime.UtcNow), Is.Null);
    }
}
