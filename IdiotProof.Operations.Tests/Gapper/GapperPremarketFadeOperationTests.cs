using System.Runtime.CompilerServices;
using IdiotProof.DataFeeds;
using IdiotProof.Models;
using IdiotProof.Operations.Gapper;
using Microsoft.Extensions.DependencyInjection;

using IdiotProof.Core.Services;
namespace IdiotProof.Operations.Tests.Gapper;

/// <summary>gapper premarket-fade (IP-T172): scripted movers + scripted bars through the real fade detector; detection only.</summary>
public class GapperPremarketFadeOperationTests
{
    /// <summary>FADE runs $1 → $2 then gives half back; CALM drifts; every other symbol has no bars.</summary>
    private sealed class FadeFeed : IMarketDataFeed
    {
        public string FeedName => "Test";

        public async IAsyncEnumerable<Candle> GetHistoricalCandlesAsync(string symbol, DateTime startUtc, DateTime endUtc, TimeSpan candleSize,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask;
            decimal[] path = symbol switch
            {
                "FADE" => [1.00m, 1.20m, 1.60m, 2.00m, 1.80m, 1.50m],
                "CALM" => [10.00m, 10.10m, 10.05m, 10.20m],
                _ => [],
            };
            for (var i = 0; i < path.Length; i++)
                yield return new Candle { Symbol = symbol, StartUtc = startUtc.AddMinutes(i), EndUtc = startUtc.AddMinutes(i + 1), Open = path[i], High = path[i], Low = path[i], Close = path[i], Volume = 1000 };
        }

        public Task<LatestPrice?> GetLatestPriceAsync(string symbol, CancellationToken ct = default) => Task.FromResult<LatestPrice?>(null);
    }

    [Test]
    public async Task Run_FlagsTheSurgeThatIsFading_AndNothingElse()
    {
        using var kit = new GapperKit(new FadeFeed());
        kit.Market.On("/v1beta1/screener/stocks/movers",
            """{"gainers":[{"symbol":"FADE","percent_change":80,"price":1.5},{"symbol":"CALM","percent_change":2,"price":10.2},{"symbol":"NONE","percent_change":30,"price":3}],"losers":[],"last_updated":"2026-10-30T13:10:00Z"}""");

        var result = await new GapperPremarketFadeOperation().ExecuteAsync(new GapperPremarketFadeArgs { Action = PremarketFadeAction.Run }, kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.Multiple(() =>
        {
            Assert.That(result.Human, Does.Contain("3 movers screened").And.Contain("1 flagged"));
            Assert.That(result.Human, Does.Contain("FADE").And.Contain("+100.0%").And.Contain("ESPECIALLY").And.Not.Contain("CALM  "));
            Assert.That(kit.Market.Query("/v1beta1/screener/stocks/movers", "top"), Is.EqualTo("50"));
        });
    }

    [Test]
    public async Task Run_RefusesTheMockFeed()
    {
        using var kit = new GapperKit(new MockDataFeed());
        var result = await new GapperPremarketFadeOperation().ExecuteAsync(new GapperPremarketFadeArgs { Action = PremarketFadeAction.Run }, kit.Context());
        Assert.That(result.Status, Is.EqualTo(OperationStatus.Blocked));
    }

    [Test]
    public async Task Status_ListsTodaysAlertsForYou()
    {
        using var kit = new GapperKit(new FadeFeed());
        var audit = kit.Services.GetRequiredService<AuditLogRepository>();
        await audit.LogAsync("premarket-fade-alert", "ESPECIALLY FADE: surged +100% premarket", userId: kit.Owner);
        await audit.LogAsync("premarket-fade-alert", "SOMEONE ELSE's alert", userId: Guid.NewGuid());

        var now = new DateTimeOffset(DateTime.UtcNow.AddMinutes(5));
        var result = await new GapperPremarketFadeOperation().ExecuteAsync(new GapperPremarketFadeArgs(), kit.Context(now: now));

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.Contain("ESPECIALLY FADE").And.Not.Contain("SOMEONE ELSE").And.Contain("1 alert(s) today"));
    }
}
