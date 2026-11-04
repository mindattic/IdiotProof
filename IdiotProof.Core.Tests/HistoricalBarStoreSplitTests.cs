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

/// <summary>IP-T410: research reads are split-adjusted; the cache and the price-level universe stay raw.</summary>
[TestFixture]
public class HistoricalBarStoreSplitTests
{
    private static readonly DateOnly ExDate = new(2026, 7, 9);

    private IDbContextFactory<AppDbContext> factory = null!;

    [SetUp]
    public void SetUp() => factory = new ServiceCollection()
        .AddDbContextFactory<AppDbContext>(o => o.UseInMemoryDatabase($"split-{Guid.NewGuid():N}"))
        .BuildServiceProvider()
        .GetRequiredService<IDbContextFactory<AppDbContext>>();

    private HistoricalBarStore Store(ISplitSource? splits) => new(factory, new ReverseSplitFeed(),
        new FakeTimeProvider(new DateTimeOffset(2026, 7, 20, 18, 0, 0, TimeSpan.Zero)), (_, _) => Task.CompletedTask, splits);

    [Test]
    public async Task AdjustedRead_RemovesTheReverseSplitJump_WhileTheCacheKeepsRawPrices()
    {
        var store = Store(new FixedSplits(new SplitEvent("PNY", ExDate, 10, 1)));

        var result = await store.GetSplitAdjustedBarsForSymbolsAsync(new[] { "PNY" }, new(2026, 7, 6), new(2026, 7, 10), BarTimeframe.Day1);
        var closes = result.Bars["PNY"].Select(b => b.Close).ToList();

        Assert.That(result.SplitDataAvailable, Is.True);
        Assert.That(result.TotalSplits, Is.EqualTo(1));
        Assert.That(closes, Is.EqualTo(new[] { 4.00m, 4.10m, 4.20m, 4.30m, 4.40m }));

        await using var db = await factory.CreateDbContextAsync();
        var cached = await db.HistoricalBars.Where(b => b.DateEt == "2026-07-08").Select(b => b.Close).SingleAsync();
        Assert.That(cached, Is.EqualTo(0.42m), "the SQL cache stores raw prices");
    }

    [Test]
    public async Task Universe_StillFiltersOnRawPrices()
    {
        var store = Store(new FixedSplits(new SplitEvent("PNY", ExDate, 10, 1)));

        // Before the split PNY traded at $0.40-0.42 raw; adjusted it would look like $4+, but the
        // universe must judge the price a trader actually saw that day.
        var members = await store.GetPointInTimeUniverseAsync(ExDate, new[] { "PNY" },
            new UniverseFilter { MinPrice = 0.10m, MaxPrice = 1m, MinSessions = 1, MinAvgDollarVolume = 0 });

        Assert.That(members.Single().LastClose, Is.EqualTo(0.42m));
    }

    [Test]
    public async Task WithoutASplitSource_TheAdjustedReadSaysSo()
    {
        var result = await Store(null).GetSplitAdjustedBarsForSymbolsAsync(new[] { "PNY" }, new(2026, 7, 6), new(2026, 7, 10), BarTimeframe.Day1);
        Assert.That(result.SplitDataAvailable, Is.False);
        Assert.That(result.Bars["PNY"].Select(b => b.Close).Max(), Is.EqualTo(4.40m));
        Assert.That(result.Bars["PNY"].Select(b => b.Close).Min(), Is.EqualTo(0.40m));
    }

    [Test]
    public async Task AlpacaSplitSource_ParsesForwardAndReverseSplits_AndCachesPerSymbol()
    {
        var handler = new StubHandler("""
            {"corporate_actions":{
              "reverse_splits":[{"symbol":"PNY","old_rate":10,"new_rate":1,"ex_date":"2026-07-09"}],
              "forward_splits":[{"symbol":"BIG","old_rate":"1","new_rate":"4","ex_date":"2026-07-08"}],
              "cash_dividends":[{"symbol":"PNY","rate":0.1,"ex_date":"2026-07-07"}]
            },"next_page_token":null}
            """);
        using var source = new AlpacaSplitSource(new HttpClient(handler) { BaseAddress = new Uri("https://data.example/") });

        var first = await source.GetSplitsAsync(new[] { "pny", "BIG", "NONE" }, new(2026, 7, 1), new(2026, 7, 31));
        await source.GetSplitsAsync(new[] { "PNY" }, new(2026, 7, 5), new(2026, 7, 20));

        Assert.That(first["PNY"].Single(), Is.EqualTo(new SplitEvent("PNY", ExDate, 10, 1)));
        Assert.That(first["BIG"].Single().PriceFactor, Is.EqualTo(0.25m));
        Assert.That(first["NONE"], Is.Empty);
        Assert.That(handler.Requests, Has.Count.EqualTo(1), "a narrower later range is served from the cache");
        Assert.That(handler.Requests[0].Query, Does.Contain("types=forward_split,reverse_split"));
    }

    [Test]
    public async Task AlpacaSplitSource_RetriesRateLimits()
    {
        var handler = new StubHandler("""{"corporate_actions":{},"next_page_token":null}""") { FailuresRemaining = 2 };
        var waits = new List<TimeSpan>();
        using var source = new AlpacaSplitSource(new HttpClient(handler) { BaseAddress = new Uri("https://data.example/") },
            (d, _) => { waits.Add(d); return Task.CompletedTask; });

        var result = await source.GetSplitsAsync(new[] { "PNY" }, new(2026, 7, 1), new(2026, 7, 31));

        Assert.That(result["PNY"], Is.Empty);
        Assert.That(waits, Is.EqualTo(new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2) }));
    }

    /// <summary>Daily closes 0.40..0.44 before a 1-for-10 reverse split on 2026-07-09, then 4.30, 4.40.</summary>
    private sealed class ReverseSplitFeed : IMarketDataFeed
    {
        public string FeedName => "fake";

        public async IAsyncEnumerable<Candle> GetHistoricalCandlesAsync(string symbol, DateTime startUtc, DateTime endUtc,
            TimeSpan candleSize, [EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            var monday = new DateOnly(2026, 7, 6);
            for (var d = DateOnly.FromDateTime(startUtc); d.ToDateTime(TimeOnly.MinValue) < endUtc; d = d.AddDays(1))
            {
                if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
                var raw = Math.Max(0.20m, 0.40m + 0.01m * (d.DayNumber - monday.DayNumber));
                var close = d >= ExDate ? raw * 10m : raw;
                yield return new Candle
                {
                    Symbol = symbol, StartUtc = d.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), EndUtc = d.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                    Open = close, High = close, Low = close, Close = close, Volume = 1_000_000m,
                };
            }
        }

        public Task<LatestPrice?> GetLatestPriceAsync(string symbol, CancellationToken ct = default) => Task.FromResult<LatestPrice?>(null);
    }

    private sealed class FixedSplits(params SplitEvent[] splits) : ISplitSource
    {
        public Task<IReadOnlyDictionary<string, IReadOnlyList<SplitEvent>>> GetSplitsAsync(
            IEnumerable<string> symbols, DateOnly from, DateOnly to, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<SplitEvent>>>(symbols.ToDictionary(
                s => s, s => (IReadOnlyList<SplitEvent>)splits.Where(x => x.Symbol == s && x.ExDate >= from && x.ExDate <= to).ToList(),
                StringComparer.OrdinalIgnoreCase));
    }

    private sealed class StubHandler(string body) : HttpMessageHandler
    {
        public int FailuresRemaining;
        public List<Uri> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request.RequestUri!);
            if (FailuresRemaining > 0)
            {
                FailuresRemaining--;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests));
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }
}
