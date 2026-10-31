using System.Runtime.CompilerServices;
using IdiotProof.DataFeeds;
using IdiotProof.Models;
using IdiotProof.Operations.Market;
using Microsoft.Extensions.DependencyInjection;

namespace IdiotProof.Operations.Tests;

/// <summary>market chart (IP-T297): bars from the Hub's feed, drawn across each day's three sessions.</summary>
public class MarketChartOperationTests
{
    /// <summary>A feed with a 1-minute walk from Thu 4:00 AM to Fri 8:00 PM ET (EDT, UTC-4).</summary>
    private sealed class WalkFeed : IMarketDataFeed
    {
        public string FeedName => "Test";

        public async IAsyncEnumerable<Candle> GetHistoricalCandlesAsync(string symbol, DateTime startUtc, DateTime endUtc,
            TimeSpan candleSize, [EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask;
            decimal p = 92m;
            foreach (var day in new[] { new DateTime(2026, 10, 29, 8, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 30, 8, 0, 0, DateTimeKind.Utc) })
            {
                for (var t = day; t < day.AddHours(16); t = t.AddMinutes(1))
                {
                    var o = p;
                    p += (t.Minute % 7 - 3) * 0.01m;
                    yield return new Candle { Symbol = symbol, StartUtc = t, EndUtc = t.AddMinutes(1), Open = o, High = Math.Max(o, p) + 0.02m, Low = Math.Min(o, p) - 0.02m, Close = p, Volume = 1000 };
                }
            }
        }

        public Task<LatestPrice?> GetLatestPriceAsync(string symbol, CancellationToken ct = default) => Task.FromResult<LatestPrice?>(null);
    }

    private static OperationContext Context(IMarketDataFeed feed) => new()
    {
        Services = new ServiceCollection().AddSingleton(feed).BuildServiceProvider(),
        Clock = new Fixtures.FixedClock(new DateTime(2026, 10, 31, 1, 0, 0, DateTimeKind.Utc)), // Fri 9:00 PM ET
    };

    [Test]
    public async Task ChartsBothDaysAcrossAllSixSessions_EveryLineTheSameWidth()
    {
        var result = await new MarketChartOperation().ExecuteAsync(new MarketChartArgs { Symbol = "intc", Width = 118 }, Context(new WalkFeed()));

        Assert.That(result.IsOk, Is.True, result.Message);
        var chart = result.Human!.Split("\n\n")[0];
        Assert.That(chart.Split('\n').Select(l => l.Length).Distinct(), Is.EqualTo(new[] { 118 }));
        Assert.That(chart, Does.StartWith("INTC  Thu 10/29 4:00 AM -> Fri 10/30 8:00 PM ET"));
        Assert.That(chart, Does.Contain(" Thu 10/29 ").And.Contain(" Fri 10/30 ").And.Contain("<- Thu close"));
        Assert.That(result.Human, Does.Contain("AFTER"));
    }

    [Test]
    public async Task RefusesTheMockFeed()
    {
        var result = await new MarketChartOperation().ExecuteAsync(new MarketChartArgs { Symbol = "INTC" }, Context(new MockDataFeed()));

        Assert.That(result.Status, Is.EqualTo(OperationStatus.Blocked));
    }
}
