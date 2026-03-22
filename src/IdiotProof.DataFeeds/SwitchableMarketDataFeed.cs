using System.Runtime.CompilerServices;
using IdiotProof.Models;

namespace IdiotProof.DataFeeds;

/// <summary>
/// Routes market data requests to the active feed based on configuration.
/// </summary>
public sealed class SwitchableMarketDataFeed : IMarketDataFeed
{
    private readonly Dictionary<string, IMarketDataFeed> _feeds = new(StringComparer.OrdinalIgnoreCase);
    private string _activeFeedName;

    public string FeedName => _activeFeedName;

    public SwitchableMarketDataFeed(string defaultFeedName = "Polygon")
    {
        _activeFeedName = defaultFeedName;
    }

    public void Register(IMarketDataFeed feed)
    {
        _feeds[feed.FeedName] = feed;
    }

    public void SetActiveFeed(string feedName)
    {
        _activeFeedName = feedName;
    }

    private IMarketDataFeed GetActive()
    {
        if (_feeds.TryGetValue(_activeFeedName, out var feed))
            return feed;
        return _feeds.Values.FirstOrDefault()
            ?? throw new InvalidOperationException("No market data feeds registered.");
    }

    public IAsyncEnumerable<Candle> GetHistoricalCandlesAsync(
        string symbol, DateTime startUtc, DateTime endUtc, TimeSpan candleSize, CancellationToken ct = default)
        => GetActive().GetHistoricalCandlesAsync(symbol, startUtc, endUtc, candleSize, ct);

    public Task<LatestPrice?> GetLatestPriceAsync(string symbol, CancellationToken ct = default)
        => GetActive().GetLatestPriceAsync(symbol, ct);
}
