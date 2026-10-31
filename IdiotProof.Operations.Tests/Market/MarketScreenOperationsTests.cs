using System.Runtime.CompilerServices;
using System.Text.Json;
using IdiotProof.Blazor.Data;
using IdiotProof.Core.Trading;
using IdiotProof.DataFeeds;
using IdiotProof.Models;
using IdiotProof.Operations.Market;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IdiotProof.Operations.Tests.Market;

/// <summary>market scan-universe (IP-T214) and market candlesticks (IP-T215).</summary>
public class MarketScreenOperationsTests
{
    private static string Snap(decimal last, decimal volume, decimal prevClose) =>
        $$$"""{"latestTrade":{"t":"2026-10-30T17:59:00Z","p":{{{last}}}},"dailyBar":{"o":{{{prevClose}}},"h":{{{last}}},"l":{{{prevClose}}},"c":{{{last}}},"v":{{{volume}}}},"prevDailyBar":{"c":{{{prevClose}}},"v":1000}}""";

    private static async Task<OperationContext> UniverseContextAsync(ScriptedMarketHandler handler)
    {
        var services = new ServiceCollection()
            .AddSingleton<AlpacaApiFactory>(new ScriptedMarketFactory(handler))
            .AddDbContextFactory<AppDbContext>(o => o.UseInMemoryDatabase($"universe-{Guid.NewGuid():N}"))
            .BuildServiceProvider();
        await using var db = await services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        db.TrackedTickers.AddRange(
            new TrackedTicker { Symbol = "AAA", Exchange = "NASDAQ", IsTradable = true, LastPrice = 5m },
            new TrackedTicker { Symbol = "BBB", Exchange = "NYSE", IsTradable = true, LastPrice = 12m },
            new TrackedTicker { Symbol = "CCC", Exchange = "NASDAQ", IsTradable = true, LastPrice = 400m },  // prefiltered out by --max-price
            new TrackedTicker { Symbol = "DDD", Exchange = "NASDAQ", IsTradable = false, LastPrice = 6m },   // not tradable
            new TrackedTicker { Symbol = "EEE", Exchange = "NASDAQ", IsTradable = true, LastPrice = 8m });
        await db.SaveChangesAsync();
        return new OperationContext { Services = services, Clock = new Fixtures.FixedClock(MarketDataTestKit.Now) };
    }

    [Test]
    public async Task ScanUniverse_PrefiltersTheStoredUniverse_ThenScreensLiveSnapshots()
    {
        var handler = new ScriptedMarketHandler().On("/v2/stocks/snapshots",
            "{" + $"\"AAA\":{Snap(5.50m, 2_000_000m, 5m)},\"BBB\":{Snap(11m, 9_000_000m, 12m)},\"EEE\":{Snap(8.10m, 50_000m, 8m)}" + "}");
        var ctx = await UniverseContextAsync(handler);

        var result = await new MarketScanUniverseOperation().ExecuteAsync(
            new MarketScanUniverseArgs { MaxPrice = 50m, MinVolume = 100_000m, Sort = UniverseSort.Change }, ctx);

        Assert.That(result.IsOk, Is.True, result.Message);
        var symbols = handler.Query("/v2/stocks/snapshots", "symbols")!.Split(',');
        Assert.That(symbols, Is.EquivalentTo(new[] { "AAA", "BBB", "EEE" }), "CCC (over --max-price) and DDD (not tradable) are never quoted");
        var hits = JsonSerializer.SerializeToElement(result.Data, OperationJson.Options).GetProperty("hits");
        Assert.That(hits.GetArrayLength(), Is.EqualTo(2), "EEE is under --min-volume");
        Assert.Multiple(() =>
        {
            Assert.That(hits[0].GetProperty("symbol").GetString(), Is.EqualTo("AAA"), "sorted by change: +10% first");
            Assert.That(hits[0].GetProperty("change_percent").GetDecimal(), Is.EqualTo(10m));
            Assert.That(hits[1].GetProperty("change_percent").GetDecimal(), Is.EqualTo(-8.33m));
            Assert.That(result.Human, Does.Contain("2 of 3 quoted tickers pass"));
        });

        var gainers = await new MarketScanUniverseOperation().ExecuteAsync(new MarketScanUniverseArgs { MinChange = 5m }, ctx);
        Assert.That(JsonSerializer.SerializeToElement(gainers.Data, OperationJson.Options).GetProperty("matched").GetInt32(), Is.EqualTo(1));
    }

    /// <summary>Daily bars: a down bar, a bullish engulfing, a doji, then a hammer.</summary>
    private sealed class PatternFeed : IMarketDataFeed
    {
        public string FeedName => "Test";

        public async IAsyncEnumerable<Candle> GetHistoricalCandlesAsync(string symbol, DateTime startUtc, DateTime endUtc, TimeSpan candleSize,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask;
            var d = new DateTime(2026, 10, 27, 0, 0, 0, DateTimeKind.Utc);
            Candle Bar(int day, decimal o, decimal h, decimal l, decimal c) =>
                new() { Symbol = symbol, StartUtc = d.AddDays(day), EndUtc = d.AddDays(day + 1), Open = o, High = h, Low = l, Close = c, Volume = 1000 };
            yield return Bar(0, 10m, 10.2m, 9.4m, 9.5m);   // down
            yield return Bar(1, 9.4m, 10.4m, 9.3m, 10.3m);  // engulfs the down body
            yield return Bar(2, 10.3m, 10.8m, 9.8m, 10.31m); // doji
            yield return Bar(3, 10.0m, 10.12m, 9.0m, 10.1m); // hammer: long lower shadow, small top body
        }

        public Task<LatestPrice?> GetLatestPriceAsync(string symbol, CancellationToken ct = default) => Task.FromResult<LatestPrice?>(null);
    }

    [Test]
    public async Task Candlesticks_FindsEachPatternOnItsBar()
    {
        var ctx = new OperationContext
        {
            Services = new ServiceCollection().AddSingleton<IMarketDataFeed>(new PatternFeed()).BuildServiceProvider(),
            Clock = new Fixtures.FixedClock(MarketDataTestKit.Now),
        };

        var result = await new MarketCandlesticksOperation().ExecuteAsync(new MarketCandlesticksArgs { Symbol = "nvda", Bars = 4 }, ctx);

        Assert.That(result.IsOk, Is.True, result.Message);
        var bars = JsonSerializer.SerializeToElement(result.Data, OperationJson.Options).GetProperty("bars");
        var byDay = bars.EnumerateArray().ToDictionary(b => b.GetProperty("time_et").GetString()!, b => b.GetProperty("patterns").EnumerateArray().Select(p => p.GetString()).ToList());
        Assert.Multiple(() =>
        {
            Assert.That(byDay["2026-10-28"], Does.Contain("bullish engulfing"));
            Assert.That(byDay["2026-10-29"], Does.Contain("doji"));
            Assert.That(byDay["2026-10-30"], Does.Contain("hammer"));
            Assert.That(byDay.ContainsKey("2026-10-27"), Is.False, "a bar with no pattern is listed only with --all");
            Assert.That(result.Human, Does.StartWith("NVDA: 4 1Day bars scanned").And.Contain("Last bar 2026-10-30 ET: hammer"));
        });
    }
}
