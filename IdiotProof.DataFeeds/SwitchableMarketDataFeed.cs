using System.Runtime.CompilerServices;
using IdiotProof.Models;

namespace IdiotProof.DataFeeds;

/// <summary>
/// Routes market data requests to the active feed based on configuration.
/// </summary>
public sealed class SwitchableMarketDataFeed : IMarketDataFeed
{
    private readonly Dictionary<string, IMarketDataFeed> feeds = new(StringComparer.OrdinalIgnoreCase);
    private string activeFeedName;

    public string FeedName => activeFeedName;

    public SwitchableMarketDataFeed(string defaultFeedName = "Polygon")
    {
        activeFeedName = defaultFeedName;
    }

    public void Register(IMarketDataFeed feed)
    {
        feeds[feed.FeedName] = feed;
    }

    public void SetActiveFeed(string feedName)
    {
        activeFeedName = feedName;
    }

    private IMarketDataFeed GetActive()
    {
        if (feeds.TryGetValue(activeFeedName, out var feed))
            return feed;
        // Fail loud rather than substituting an arbitrary feed. Falling back to
        // "whatever's first" could silently serve synthetic Mock prices into live
        // strategy evaluation and order sizing when the configured feed name is a
        // typo or its registration failed — far more dangerous than a hard error.
        throw new InvalidOperationException(
            $"Active market data feed '{activeFeedName}' is not registered. " +
            $"Registered feeds: {(feeds.Count == 0 ? "(none)" : string.Join(", ", feeds.Keys))}.");
    }

    public IAsyncEnumerable<Candle> GetHistoricalCandlesAsync(
        string symbol, DateTime startUtc, DateTime endUtc, TimeSpan candleSize, CancellationToken ct = default)
        => GetActive().GetHistoricalCandlesAsync(symbol, startUtc, endUtc, candleSize, ct);

    public Task<LatestPrice?> GetLatestPriceAsync(string symbol, CancellationToken ct = default)
        => GetActive().GetLatestPriceAsync(symbol, ct);
}
