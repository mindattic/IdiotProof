using System.Net;
using System.Runtime.CompilerServices;
using IdiotProof.Core.Data;
using IdiotProof.Core.Evidence;
using IdiotProof.DataFeeds;
using IdiotProof.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace IdiotProof.Core.Tests;

[TestFixture]
public class HistoricalBarStoreTests
{
    private IDbContextFactory<AppDbContext> factory = null!;
    private FakeFeed feed = null!;
    private FakeTimeProvider clock = null!;
    private List<TimeSpan> delays = null!;

    [SetUp]
    public void SetUp()
    {
        factory = new ServiceCollection()
            .AddDbContextFactory<AppDbContext>(o => o.UseInMemoryDatabase($"bars-{Guid.NewGuid():N}"))
            .BuildServiceProvider()
            .GetRequiredService<IDbContextFactory<AppDbContext>>();
        feed = new FakeFeed();
        // Wednesday 2026-07-15 18:00 UTC = 14:00 ET.
        clock = new FakeTimeProvider(new DateTimeOffset(2026, 7, 15, 18, 0, 0, TimeSpan.Zero));
        delays = new List<TimeSpan>();
    }

    private HistoricalBarStore Store() => new(factory, feed, clock, (d, _) => { delays.Add(d); return Task.CompletedTask; });

    [Test]
    public async Task CompletedDays_AreFetchedOnce_ThenServedFromSql()
    {
        var store = Store();
        var from = new DateOnly(2026, 7, 6);
        var to = new DateOnly(2026, 7, 10);

        var first = await store.GetBarsAsync("abcd", from, to, BarTimeframe.Minute1);
        var callsAfterFirst = feed.Calls;
        var second = await store.GetBarsAsync("ABCD", from, to, BarTimeframe.Minute1);

        Assert.That(callsAfterFirst, Is.EqualTo(1), "five consecutive sessions should be one request");
        Assert.That(feed.Calls, Is.EqualTo(callsAfterFirst), "second read must come from the cache");
        Assert.That(second.Select(c => c.StartUtc), Is.EqualTo(first.Select(c => c.StartUtc)));
        Assert.That(second, Has.Count.EqualTo(5 * FakeFeed.MinuteBarsPerDay));
        Assert.That(second, Is.Ordered.By(nameof(Candle.StartUtc)));
    }

    [Test]
    public async Task EmptyCompletedDay_IsCoveredAndNotRefetched()
    {
        feed.EmptyDays.Add(new DateOnly(2026, 7, 8));
        var store = Store();
        var day = new DateOnly(2026, 7, 8);

        var first = await store.GetBarsAsync("HALT", day, day, BarTimeframe.Minute1);
        await store.GetBarsAsync("HALT", day, day, BarTimeframe.Minute1);

        Assert.That(first, Is.Empty);
        Assert.That(feed.Calls, Is.EqualTo(1));
    }

    [Test]
    public async Task Today_IsFetchedLive_AndNeverCached()
    {
        var store = Store();
        var today = new DateOnly(2026, 7, 15);

        var a = await store.GetBarsAsync("LIVE", today, today, BarTimeframe.Minute1);
        await store.GetBarsAsync("LIVE", today, today, BarTimeframe.Minute1);

        Assert.That(a, Is.Not.Empty);
        Assert.That(feed.Calls, Is.EqualTo(2), "a still-forming day must be re-fetched each time");
        await using var db = await factory.CreateDbContextAsync();
        Assert.That(await db.HistoricalBars.CountAsync(), Is.Zero);
        Assert.That(await db.HistoricalBarCoverage.CountAsync(), Is.Zero);
    }

    [Test]
    public async Task Weekend_IsNeverRequested_AndRangeSplitsAroundIt()
    {
        var store = Store();
        // Fri 7/10 .. Mon 7/13: Fri and Mon are consecutive sessions, so still one request.
        await store.GetBarsAsync("WKND", new DateOnly(2026, 7, 11), new DateOnly(2026, 7, 12), BarTimeframe.Minute1);
        Assert.That(feed.Calls, Is.Zero, "Saturday and Sunday need no request");

        await store.GetBarsAsync("WKND", new DateOnly(2026, 7, 10), new DateOnly(2026, 7, 13), BarTimeframe.Minute1);
        Assert.That(feed.Calls, Is.EqualTo(1));
    }

    [Test]
    public async Task RateLimited_RetriesWithBackoff_ThenSucceeds()
    {
        feed.FailuresRemaining = 2;
        feed.FailureStatus = HttpStatusCode.TooManyRequests;
        var store = Store();

        var bars = await store.GetBarsAsync("RATE", new DateOnly(2026, 7, 9), new DateOnly(2026, 7, 9), BarTimeframe.Minute1);

        Assert.That(bars, Has.Count.EqualTo(FakeFeed.MinuteBarsPerDay));
        Assert.That(delays, Is.EqualTo(new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2) }));
    }

    [Test]
    public void NonTransientError_IsNotRetried()
    {
        feed.FailuresRemaining = 1;
        feed.FailureStatus = HttpStatusCode.BadRequest;
        var store = Store();

        Assert.ThrowsAsync<HttpRequestException>(() =>
            store.GetBarsAsync("BAD", new DateOnly(2026, 7, 9), new DateOnly(2026, 7, 9), BarTimeframe.Minute1));
        Assert.That(delays, Is.Empty);
    }

    [Test]
    public async Task Batch_FetchesEverySymbolOnce()
    {
        var store = Store();
        var result = await store.GetBarsForSymbolsAsync(new[] { "AAA", "bbb", "AAA", "CCC" },
            new DateOnly(2026, 7, 9), new DateOnly(2026, 7, 9), BarTimeframe.Minute1, maxConcurrency: 2);

        Assert.That(result.Keys, Is.EquivalentTo(new[] { "AAA", "BBB", "CCC" }));
        Assert.That(feed.Calls, Is.EqualTo(3));
    }

    [Test]
    public async Task PointInTimeUniverse_UsesOnlyBarsBeforeAsOfDate()
    {
        // FALL trades at $8 until 7/10, then collapses to $3 from 7/13 on.
        feed.DailyClose = (sym, d) => sym switch
        {
            "FALL" => d < new DateOnly(2026, 7, 13) ? 8m : 3m,
            "PENNY" => 2m,
            "DIME" => 0.05m,
            "THIN" => 4m,
            _ => 50m,
        };
        feed.DailyVolume = (sym, _) => sym == "THIN" ? 1_000m : 1_000_000m;
        var store = Store();
        var candidates = new[] { "FALL", "PENNY", "DIME", "THIN", "BIG" };

        var beforeCollapse = await store.GetPointInTimeUniverseAsync(new DateOnly(2026, 7, 13), candidates);
        var afterCollapse = await store.GetPointInTimeUniverseAsync(new DateOnly(2026, 7, 15), candidates);

        Assert.That(beforeCollapse.Select(m => m.Symbol), Is.EqualTo(new[] { "PENNY" }),
            "on 7/13 FALL's last prior close was $8; knowing it later fell would be look-ahead");
        Assert.That(afterCollapse.Select(m => m.Symbol), Is.EqualTo(new[] { "FALL", "PENNY" }));
        Assert.That(afterCollapse.Single(m => m.Symbol == "FALL").LastSessionEt, Is.EqualTo(new DateOnly(2026, 7, 14)));
    }

    private sealed class FakeFeed : IMarketDataFeed
    {
        public const int MinuteBarsPerDay = 3;
        public int Calls;
        public int FailuresRemaining;
        public HttpStatusCode FailureStatus = HttpStatusCode.TooManyRequests;
        public HashSet<DateOnly> EmptyDays { get; } = new();
        public Func<string, DateOnly, decimal> DailyClose = (_, _) => 10m;
        public Func<string, DateOnly, decimal> DailyVolume = (_, _) => 1_000_000m;

        public string FeedName => "fake";

        public async IAsyncEnumerable<Candle> GetHistoricalCandlesAsync(string symbol, DateTime startUtc, DateTime endUtc,
            TimeSpan candleSize, [EnumeratorCancellation] CancellationToken ct = default)
        {
            Interlocked.Increment(ref Calls);
            await Task.Yield();
            if (FailuresRemaining > 0)
            {
                FailuresRemaining--;
                throw new HttpRequestException("fake failure", null, FailureStatus);
            }

            if (candleSize >= TimeSpan.FromDays(1))
            {
                for (var d = DateOnly.FromDateTime(startUtc); d.ToDateTime(TimeOnly.MinValue) < endUtc; d = d.AddDays(1))
                {
                    if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
                    var close = DailyClose(symbol, d);
                    yield return Bar(symbol, d.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), close, DailyVolume(symbol, d));
                }
                yield break;
            }

            var et = IdiotProof.Scripting.MarketTime.Eastern;
            var first = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(startUtc, et));
            var last = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(endUtc, et));
            for (var d = first; d <= last; d = d.AddDays(1))
            {
                if (EmptyDays.Contains(d)) continue;
                var open = TimeZoneInfo.ConvertTimeToUtc(d.ToDateTime(new TimeOnly(9, 30)), et);
                for (int m = 0; m < MinuteBarsPerDay; m++)
                    yield return Bar(symbol, open.AddMinutes(m), 5m, 100m);
            }
        }

        public Task<LatestPrice?> GetLatestPriceAsync(string symbol, CancellationToken ct = default) => Task.FromResult<LatestPrice?>(null);

        private static Candle Bar(string symbol, DateTime start, decimal close, decimal volume) => new()
        {
            Symbol = symbol, StartUtc = start, EndUtc = start.AddMinutes(1),
            Open = close, High = close, Low = close, Close = close, Volume = volume,
        };
    }
}
