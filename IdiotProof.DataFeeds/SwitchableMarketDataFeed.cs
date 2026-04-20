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
        return feeds.Values.FirstOrDefault()
            ?? throw new InvalidOperationException("No market data feeds registered.");
    }

    public IAsyncEnumerable<Candle> GetHistoricalCandlesAsync(
        string symbol, DateTime startUtc, DateTime endUtc, TimeSpan candleSize, CancellationToken ct = default)
        => GetActive().GetHistoricalCandlesAsync(symbol, startUtc, endUtc, candleSize, ct);

    public Task<LatestPrice?> GetLatestPriceAsync(string symbol, CancellationToken ct = default)
        => GetActive().GetLatestPriceAsync(symbol, ct);
}
