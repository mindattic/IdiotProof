using System.Runtime.CompilerServices;
using IdiotProof.DataFeeds;
using IdiotProof.Indicators;
using IdiotProof.Models;
using IdiotProof.Operations.Indicators;
using Microsoft.Extensions.DependencyInjection;

namespace IdiotProof.Operations.Tests.Indicators;

/// <summary>A deterministic feed: 1-minute bars all of Fri 10/30 (8:00-24:00 UTC) and a $90 daily close on Thu 10/29.</summary>
internal sealed class StepFeed : IMarketDataFeed
{
    public string FeedName => "Test";

    public List<(DateTime Start, DateTime End, TimeSpan Size)> Requests { get; } = [];

    public static List<Candle> Minutes()
    {
        var list = new List<Candle>();
        decimal p = 100m;
        var start = new DateTime(2026, 10, 30, 8, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < 600; i++)
        {
            var t = start.AddMinutes(i);
            var o = p;
            p += ((i % 9) - 3) * 0.05m; // a gentle up-drift with chop
            list.Add(new Candle
            {
                Symbol = "NVDA", StartUtc = t, EndUtc = t.AddMinutes(1), Open = o, High = Math.Max(o, p) + 0.03m,
                Low = Math.Min(o, p) - 0.03m, Close = p, Volume = 1000 + i % 50 * 10,
            });
        }
        return list;
    }

    public async IAsyncEnumerable<Candle> GetHistoricalCandlesAsync(string symbol, DateTime startUtc, DateTime endUtc, TimeSpan candleSize,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.CompletedTask;
        Requests.Add((startUtc, endUtc, candleSize));
        if (candleSize >= TimeSpan.FromDays(1))
        {
            yield return new Candle { Symbol = symbol, StartUtc = new DateTime(2026, 10, 29, 0, 0, 0, DateTimeKind.Utc), EndUtc = new DateTime(2026, 10, 30, 0, 0, 0, DateTimeKind.Utc), Open = 89, High = 91, Low = 88, Close = 90, Volume = 1_000_000 };
            yield break;
        }
        foreach (var c in Minutes()) if (c.StartUtc >= startUtc && c.StartUtc < endUtc) yield return c;
    }

    public Task<LatestPrice?> GetLatestPriceAsync(string symbol, CancellationToken ct = default) => Task.FromResult<LatestPrice?>(null);
}

/// <summary>indicators compute (IP-T207) and indicators snapshot (IP-T208), on the Monitor's own math.</summary>
public class IndicatorsOperationTests
{
    private static OperationContext Context(IMarketDataFeed feed) => new()
    {
        Services = new ServiceCollection().AddSingleton(feed).BuildServiceProvider(),
        Clock = new Fixtures.FixedClock(new DateTime(2026, 10, 30, 20, 0, 0, DateTimeKind.Utc)), // Fri 4:00 PM ET
    };

    [Test]
    public async Task Rsi_MatchesTheIndicatorLibrary_OnTheNewestBars()
    {
        var feed = new StepFeed();
        var result = await new IndicatorsComputeOperation().ExecuteAsync(new IndicatorsComputeArgs { Symbol = "nvda", Indicator = "RSI", Last = 5 }, Context(feed));

        Assert.That(result.IsOk, Is.True, result.Message);
        var window = StepFeed.Minutes().Where(c => c.StartUtc < new DateTime(2026, 10, 30, 20, 0, 0, DateTimeKind.Utc)).TakeLast(240).ToList();
        var expected = Math.Round(RSI.Calculate(window, 14)[^1], 6);
        var data = OptionsLikeData(result);
        Assert.Multiple(() =>
        {
            Assert.That(data.GetProperty("latest").GetProperty("RSI").GetDecimal(), Is.EqualTo(expected));
            Assert.That(data.GetProperty("points").GetArrayLength(), Is.EqualTo(5));
            Assert.That(data.GetProperty("bars").GetInt32(), Is.EqualTo(240));
            Assert.That(result.Human, Does.StartWith("NVDA RSI(14) on 1Min bars").And.Contain("1:59 PM"));
        });
    }

    [Test]
    public async Task Macd_PrintsAllThreeLines_AndEmaHonoursThePeriod()
    {
        var macd = await new IndicatorsComputeOperation().ExecuteAsync(new IndicatorsComputeArgs { Symbol = "NVDA", Indicator = "macd" }, Context(new StepFeed()));
        var ema = await new IndicatorsComputeOperation().ExecuteAsync(new IndicatorsComputeArgs { Symbol = "NVDA", Indicator = "ema", Period = 21 }, Context(new StepFeed()));

        Assert.That(macd.Human, Does.Contain("MACD").And.Contain("Signal").And.Contain("Hist"));
        Assert.That(ema.Human, Does.Contain("EMA(21)").And.Contain("EMA21"));
    }

    [Test]
    public async Task UnknownIndicator_AndTheMockFeed_AreRefused()
    {
        var unknown = await new IndicatorsComputeOperation().ExecuteAsync(new IndicatorsComputeArgs { Symbol = "NVDA", Indicator = "ichimoku" }, Context(new StepFeed()));
        Assert.That(unknown.Status, Is.EqualTo(OperationStatus.UsageError));
        Assert.That(unknown.Message, Does.Contain("rsi").And.Contain("vwap"));

        var mock = new SwitchableMarketDataFeed("Mock");
        mock.Register(new MockDataFeed());
        var ex = Assert.ThrowsAsync<OperationException>(() =>
            new IndicatorsComputeOperation().ExecuteAsync(new IndicatorsComputeArgs { Symbol = "NVDA", Indicator = "rsi" }, Context(mock)));
        Assert.That(ex!.Status, Is.EqualTo(OperationStatus.Blocked));
    }

    [Test]
    public async Task Snapshot_IsTheMonitorsSnapshot_WithGapAndWindowExtremes()
    {
        var result = await new IndicatorsSnapshotOperation().ExecuteAsync(new IndicatorsSnapshotArgs { Symbol = "NVDA", Ema = "7,65" }, Context(new StepFeed()));

        Assert.That(result.IsOk, Is.True, result.Message);
        var data = OptionsLikeData(result);
        var snap = data.GetProperty("snapshot");
        var window = StepFeed.Minutes().Where(c => c.StartUtc < new DateTime(2026, 10, 30, 20, 0, 0, DateTimeKind.Utc)).TakeLast(240).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(data.GetProperty("previous_close").GetDecimal(), Is.EqualTo(90m));
            Assert.That(snap.GetProperty("window_high").GetDouble(), Is.EqualTo((double)window.Max(c => c.High)).Within(1e-9));
            Assert.That(snap.GetProperty("window_low").GetDouble(), Is.EqualTo((double)window.Min(c => c.Low)).Within(1e-9));
            Assert.That(snap.GetProperty("emas").TryGetProperty("65", out _), Is.True, "extra EMA periods are included");
            Assert.That(result.Human, Does.Contain("Previous day close").And.Contain("gap").And.Contain("Pivots").And.Contain("Window high/low"));
        });
    }

    private static System.Text.Json.JsonElement OptionsLikeData(OperationResult result) =>
        System.Text.Json.JsonSerializer.SerializeToElement(result.Data, OperationJson.Options);
}
