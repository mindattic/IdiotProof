using IdiotProof.Models;

namespace IdiotProof.DataFeeds;

/// <summary>
/// Abstraction for any market data provider.
/// </summary>
public interface IMarketDataFeed
{
    string FeedName { get; }

    IAsyncEnumerable<Candle> GetHistoricalCandlesAsync(
        string symbol,
        DateTime startUtc,
        DateTime endUtc,
        TimeSpan candleSize,
        CancellationToken ct = default);

    Task<LatestPrice?> GetLatestPriceAsync(string symbol, CancellationToken ct = default);
}
