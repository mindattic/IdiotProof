using System.Runtime.CompilerServices;
using IdiotProof.DataFeeds;
using IdiotProof.Models;

namespace IdiotProof.Strategies.Tests;

/// <summary>
/// IP-T14: <see cref="SwitchableMarketDataFeed"/> routes to the registered active feed (case-insensitive),
/// switches with <see cref="SwitchableMarketDataFeed.SetActiveFeed"/>, and refuses loudly (naming the
/// registered feeds) instead of falling back to Mock when the active name is not registered: a silent
/// fallback would feed synthetic prices into live strategy evaluation and sizing.
/// </summary>
[TestFixture]
public sealed class SwitchableMarketDataFeedTests
{
    /// <summary>A feed that answers with its own name so a test can see where a call went.</summary>
    private sealed class NamedFeed(string name, decimal price) : IMarketDataFeed
    {
        public string FeedName => name;

        public int Calls { get; private set; }

        public async IAsyncEnumerable<Candle> GetHistoricalCandlesAsync(string symbol, DateTime startUtc, DateTime endUtc,
            TimeSpan candleSize, [EnumeratorCancellation] CancellationToken ct = default)
        {
            Calls++;
            await Task.Yield();
            yield return new Candle { Symbol = symbol, StartUtc = startUtc, EndUtc = startUtc + candleSize, Open = price, High = price, Low = price, Close = price, Note = name };
        }

        public Task<LatestPrice?> GetLatestPriceAsync(string symbol, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult<LatestPrice?>(new LatestPrice(symbol, price, DateTime.UtcNow, name));
        }
    }

    private static (SwitchableMarketDataFeed Feed, NamedFeed Alpaca, NamedFeed Mock) Make(string active = "Alpaca")
    {
        var alpaca = new NamedFeed("Alpaca", 100m);
        var mock = new NamedFeed("Mock", 1m);
        var feed = new SwitchableMarketDataFeed(active);
        feed.Register(alpaca);
        feed.Register(mock);
        return (feed, alpaca, mock);
    }

    [Test]
    public async Task RoutesToTheActiveFeed()
    {
        var (feed, alpaca, mock) = Make();
        var price = await feed.GetLatestPriceAsync("AAPL");
        Assert.Multiple(() =>
        {
            Assert.That(price!.Source, Is.EqualTo("Alpaca"));
            Assert.That(feed.FeedName, Is.EqualTo("Alpaca"));
            Assert.That(feed.Active, Is.SameAs(alpaca));
            Assert.That(alpaca.Calls, Is.EqualTo(1));
            Assert.That(mock.Calls, Is.Zero);
        });
    }

    [Test]
    public async Task ActiveNameIsCaseInsensitive()
    {
        var (feed, alpaca, _) = Make(active: "aLPACA");
        Assert.Multiple(async () =>
        {
            Assert.That((await feed.GetLatestPriceAsync("AAPL"))!.Source, Is.EqualTo("Alpaca"));
            Assert.That(feed.Active, Is.SameAs(alpaca));
            Assert.That(feed.Feeds.ContainsKey("mock"), Is.True);
        });
    }

    [Test]
    public async Task SetActiveFeed_SwitchesRouting_ForPricesAndCandles()
    {
        var (feed, _, mock) = Make();
        feed.SetActiveFeed("mock");

        var price = await feed.GetLatestPriceAsync("AAPL");
        var candles = new List<Candle>();
        await foreach (var c in feed.GetHistoricalCandlesAsync("AAPL", DateTime.UtcNow.AddHours(-1), DateTime.UtcNow, TimeSpan.FromMinutes(1)))
            candles.Add(c);

        Assert.Multiple(() =>
        {
            Assert.That(price!.Source, Is.EqualTo("Mock"));
            Assert.That(candles.Single().Note, Is.EqualTo("Mock"));
            Assert.That(feed.Active, Is.SameAs(mock));
            Assert.That(feed.FeedName, Is.EqualTo("mock"));
        });
    }

    [Test]
    public void UnregisteredActiveName_ThrowsListingRegisteredFeeds_InsteadOfFallingBackToMock()
    {
        var (feed, alpaca, mock) = Make(active: "Polygon");

        var ex = Assert.ThrowsAsync<InvalidOperationException>(() => feed.GetLatestPriceAsync("AAPL"));
        Assert.Multiple(() =>
        {
            Assert.That(ex!.Message, Does.Contain("'Polygon' is not registered"));
            Assert.That(ex.Message, Does.Contain("Alpaca, Mock"));
            Assert.That(feed.Active, Is.Null);
            Assert.That(alpaca.Calls + mock.Calls, Is.Zero, "no feed was asked");
        });
    }

    [Test]
    public void UnregisteredActiveName_CandlesThrowToo()
    {
        var (feed, _, mock) = Make();
        feed.SetActiveFeed("Alpacca");
        Assert.Multiple(() =>
        {
            Assert.That(() => feed.GetHistoricalCandlesAsync("AAPL", DateTime.UtcNow.AddHours(-1), DateTime.UtcNow, TimeSpan.FromMinutes(1)),
                Throws.InvalidOperationException);
            Assert.That(mock.Calls, Is.Zero);
        });
    }

    [Test]
    public void NothingRegistered_SaysNone()
    {
        var feed = new SwitchableMarketDataFeed();
        var ex = Assert.ThrowsAsync<InvalidOperationException>(() => feed.GetLatestPriceAsync("AAPL"));
        Assert.That(ex!.Message, Does.Contain("Registered feeds: (none)"));
    }

    [Test]
    public async Task Register_SameNameReplacesThePriorFeed()
    {
        var (feed, alpaca, _) = Make();
        var replacement = new NamedFeed("ALPACA", 200m);
        feed.Register(replacement);

        var price = await feed.GetLatestPriceAsync("AAPL");
        Assert.Multiple(() =>
        {
            Assert.That(price!.Price, Is.EqualTo(200m));
            Assert.That(feed.Feeds, Has.Count.EqualTo(2));
            Assert.That(alpaca.Calls, Is.Zero);
        });
    }
}
