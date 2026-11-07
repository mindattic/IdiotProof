using IdiotProof.Core.Evidence;
using IdiotProof.Core.History;
using IdiotProof.DataFeeds;
using IdiotProof.Models;
using Microsoft.Data.SqlClient;

namespace IdiotProof.Core.Tests.History;

/// <summary>
/// IP-T454 against the local SQL Server (skipped when none answers): the schema applies once and
/// again as a no-op, minute and daily days round-trip through the columnstore and the range
/// coverage, a covered day is never written twice, and the file-store import is idempotent.
/// Each test uses a throwaway database dropped afterwards.
/// </summary>
[TestFixture]
[NonParallelizable]
public class HistoryDatabaseTests
{
    private HistoryDatabase db = null!;
    private string name = "";

    [SetUp]
    public async Task SetUp()
    {
        name = $"IdiotProof_History_Test_{Guid.NewGuid():N}";
        db = new HistoryDatabase($"Server=localhost;Database={name};Integrated Security=True;TrustServerCertificate=True;Connect Timeout=3;");
        try { await db.EnsureSchemaAsync(CancellationToken.None); }
        catch (SqlException ex) { Assert.Ignore($"No local SQL Server: {ex.Message}"); }
    }

    [TearDown]
    public async Task TearDown()
    {
        SqlConnection.ClearAllPools();
        try
        {
            await using var c = new SqlConnection("Server=localhost;Database=master;Integrated Security=True;TrustServerCertificate=True;Connect Timeout=3;");
            await c.OpenAsync();
            await using var q = new SqlCommand($"IF DB_ID('{name}') IS NOT NULL BEGIN ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]; END", c);
            await q.ExecuteNonQueryAsync();
        }
        catch (SqlException) { }
    }

    private static DetailedCandle Minute(DateOnly day, int hour, int minute, decimal close) => new()
    {
        Symbol = "ABC", StartUtc = FileBackedBarStore.EtToUtc(day, new TimeOnly(hour, minute)), EndUtc = FileBackedBarStore.EtToUtc(day, new TimeOnly(hour, minute)).AddMinutes(1),
        Open = close, High = close + 0.01m, Low = close - 0.01m, Close = close, Volume = 1234, TradeCount = 7, Vwap = close, Note = AlpacaDataFeed.SourceLabel("sip"),
    };

    [Test]
    public async Task Schema_AppliesOnce()
    {
        var again = await db.EnsureSchemaAsync(CancellationToken.None);
        var status = await HistoryStatus.ReadAsync(db, CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(again, Is.Empty);
            Assert.That(status.SchemaVersion, Is.EqualTo(HistoryDatabase.Steps.Max(s => s.Version)));
            Assert.That(status.Rows.Select(r => r.Table), Does.Contain("MinuteBars").And.Contain("DailyBars").And.Contain("News").And.Contain("CoverageRanges"));
        });
    }

    [Test]
    public async Task MinuteDays_RoundTrip_AndACoveredDayIsNeverWrittenTwice()
    {
        var cache = new SqlHistoryBarCache(db);
        var d1 = new DateOnly(2024, 3, 5);
        var d2 = new DateOnly(2024, 3, 6);
        IReadOnlyList<Candle> bars = [Minute(d1, 4, 0, 1.5m), Minute(d1, 9, 30, 1.75m), Minute(d1, 19, 59, 1.6m)];
        await cache.SaveDaysAsync("ABC", BarTimeframe.Minute1, [(d1, bars), (d2, [])], "sip", DateTime.UtcNow, CancellationToken.None);
        await cache.SaveDaysAsync("ABC", BarTimeframe.Minute1, [(d1, bars)], "sip", DateTime.UtcNow, CancellationToken.None);
        var read = await cache.ReadAsync("ABC", BarTimeframe.Minute1, d1, d2, CancellationToken.None);
        var covered = await cache.CoveredDaysAsync("ABC", BarTimeframe.Minute1, d1, d2, CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(read, Has.Count.EqualTo(3), "the second save of a covered day wrote nothing");
            Assert.That(read.Select(b => b.Close), Is.EqualTo(new[] { 1.5m, 1.75m, 1.6m }));
            Assert.That(((DetailedCandle)read[1]).TradeCount, Is.EqualTo(7));
            Assert.That(read[0].StartUtc.Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(covered, Is.EquivalentTo(new[] { d1, d2 }), "the empty day is covered too");
        });
    }

    [Test]
    public async Task DailyDays_UseRangeCoverage_SoGapsInsideARangeAreNotRefetched()
    {
        var cache = new SqlHistoryBarCache(db);
        var mon = new DateOnly(2024, 3, 4);
        var wed = new DateOnly(2024, 3, 6);
        Candle Day(DateOnly d, decimal c) => new() { Symbol = "ABC", StartUtc = d.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), EndUtc = d.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), Open = c, High = c, Low = c, Close = c, Volume = 10 };
        await cache.SaveDaysAsync("ABC", BarTimeframe.Day1, [(mon, [Day(mon, 2)]), (mon.AddDays(1), []), (wed, [Day(wed, 3)])], "sip", DateTime.UtcNow, CancellationToken.None);
        var covered = await cache.CoveredDaysAsync("ABC", BarTimeframe.Day1, mon, wed.AddDays(2), CancellationToken.None);
        var read = await cache.ReadAsync("ABC", BarTimeframe.Day1, mon, wed, CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(covered, Is.EquivalentTo(new[] { mon, mon.AddDays(1), wed }), "the halted Tuesday is inside the range; Thursday is not");
            Assert.That(read.Select(b => b.Close), Is.EqualTo(new[] { 2m, 3m }));
        });
    }

    [Test]
    public async Task ReadThroughStore_FetchesOnlyMissingDays()
    {
        var feed = new CountingFeed();
        var store = new HistoricalBarStore(new SqlHistoryBarCache(db), feed, clock: new Microsoft.Extensions.Time.Testing.FakeTimeProvider(new DateTimeOffset(2024, 4, 1, 12, 0, 0, TimeSpan.Zero)));
        var day = new DateOnly(2024, 3, 5);
        var first = await store.GetBarsAsync("ABC", day, day, BarTimeframe.Minute1);
        var second = await store.GetBarsAsync("ABC", day, day, BarTimeframe.Minute1);
        Assert.Multiple(() =>
        {
            Assert.That(feed.Calls, Is.EqualTo(1), "the second read is served from the history database");
            Assert.That(second, Has.Count.EqualTo(first.Count));
            Assert.That(first, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public async Task FileStoreImport_IsIdempotent()
    {
        var root = Path.Combine(Path.GetTempPath(), $"history-import-{Guid.NewGuid():N}");
        try
        {
            var store = new FileBackedBarStore(root);
            var d = new DateOnly(2024, 3, 5);
            store.AppendDay("XYZ", d, [Minute(d, 7, 0, 2m), Minute(d, 7, 1, 2.1m)]);
            store.AppendDay("XYZ", d.AddDays(1), []);
            var importer = new HistoryImporter(db);
            var first = await importer.ImportMinuteFilesAsync(root, 2, CancellationToken.None);
            var second = await importer.ImportMinuteFilesAsync(root, 2, CancellationToken.None);
            var covered = await new SqlHistoryBarCache(db).CoveredDaysAsync("XYZ", BarTimeframe.Minute1, d, d.AddDays(1), CancellationToken.None);
            Assert.Multiple(() =>
            {
                Assert.That(first.Rows, Is.EqualTo(2));
                Assert.That(first.Note, Is.Null);
                Assert.That(second.Rows, Is.Zero);
                Assert.That(second.Skipped, Is.EqualTo(2), "both days already held");
                Assert.That(covered, Has.Count.EqualTo(2));
            });
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Test]
    public async Task Backfill_QueuesOnce_RunsResumably_AndSkipsWhatIsHeld()
    {
        var feed = new CountingFeed();
        var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(new DateTimeOffset(2024, 3, 7, 12, 0, 0, TimeSpan.Zero));
        var svc = new HistoryBackfillService(db, feed, null!, clock, Microsoft.Extensions.Logging.Abstractions.NullLogger<HistoryBackfillService>.Instance) { MonthPause = TimeSpan.Zero };
        var first = await svc.EnqueueAsync(["abc", "ABC"], "test", years: 1);
        var again = await svc.EnqueueAsync(["ABC"], "test", years: 1);
        var job = await svc.ClaimAsync(CancellationToken.None);
        var none = await svc.ClaimAsync(CancellationToken.None);
        var run = await svc.RunAsync(job!, CancellationToken.None);
        int callsAfterFirst = feed.Calls;
        await svc.EnqueueAsync(["ABC"], "test", years: 1);
        var second = await svc.ClaimAsync(CancellationToken.None);
        if (second is not null) await svc.RunAsync(second, CancellationToken.None);
        var status = await HistoryStatus.ReadAsync(db, CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo(new[] { "ABC" }), "case-folded and de-duplicated");
            Assert.That(again, Is.Empty, "a pending job is not queued twice");
            Assert.That(job!.Symbol, Is.EqualTo("ABC"));
            Assert.That(none, Is.Null, "a running job is not claimed twice");
            Assert.That(run.Error, Is.Null);
            Assert.That(run.MonthsFetched, Is.EqualTo(13));
            Assert.That(run.MinuteBars, Is.EqualTo(2), "the fake feed returns two bars on 2024-03-05");
            Assert.That(feed.Calls - callsAfterFirst, Is.Zero, "a second pass over the same window fetches nothing");
            Assert.That(status.Jobs["done"], Is.GreaterThanOrEqualTo(1));
        });
    }

    private sealed class CountingFeed : IMarketDataFeed
    {
        public int Calls;
        public string FeedName => "sip";

        public async IAsyncEnumerable<Candle> GetHistoricalCandlesAsync(string symbol, DateTime startUtc, DateTime endUtc, TimeSpan candleSize,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            Calls++;
            var day = new DateOnly(2024, 3, 5);
            yield return Minute(day, 8, 0, 1m);
            yield return Minute(day, 8, 1, 1.1m);
            await Task.CompletedTask;
        }

        public Task<LatestPrice?> GetLatestPriceAsync(string symbol, CancellationToken ct = default) => Task.FromResult<LatestPrice?>(null);
    }
}
