using System.Runtime.CompilerServices;
using IdiotProof.Models;

namespace IdiotProof.DataFeeds;

/// <summary>
/// Synthetic data feed for sandbox/demo mode — generates a realistic random-walk
/// price series without requiring any API keys.
/// </summary>
public sealed class MockDataFeed : IMarketDataFeed
{
    public string FeedName => "Mock";

    public async IAsyncEnumerable<Candle> GetHistoricalCandlesAsync(
        string symbol,
        DateTime startUtc,
        DateTime endUtc,
        TimeSpan candleSize,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.Yield();

        var seed = symbol.GetHashCode();
        var rng = new Random(seed ^ startUtc.DayOfYear);

        // Seed a base price per symbol
        var basePrice = symbol.ToUpperInvariant() switch
        {
            "TSLA" => 220m,
            "AAPL" => 185m,
            "NVDA" => 480m,
            "SPY"  => 520m,
            "QQQ"  => 440m,
            "MSFT" => 415m,
            _      => 100m + (Math.Abs(seed) % 400)
        };

        var price = basePrice;
        var candleSizeSeconds = (int)candleSize.TotalSeconds;
        if (candleSizeSeconds <= 0) candleSizeSeconds = 60;

        var current = startUtc;
        while (current < endUtc && !ct.IsCancellationRequested)
        {
            var candleEnd = current.Add(candleSize);

            // Random walk: small drift + noise
            var drift = (decimal)(rng.NextDouble() - 0.498) * 0.003m;
            var volatility = (decimal)(rng.NextDouble() * 0.008 + 0.002);

            var open = price;
            var close = Math.Round(price * (1m + drift + (decimal)(rng.NextDouble() - 0.5) * volatility), 2);
            var high = Math.Round(Math.Max(open, close) * (1m + (decimal)rng.NextDouble() * 0.003m), 2);
            var low  = Math.Round(Math.Min(open, close) * (1m - (decimal)rng.NextDouble() * 0.003m), 2);
            low  = Math.Max(low, 0.01m);

            // Volume higher near market open (9:30 ET = 14:30 UTC) and close (16:00 ET = 21:00 UTC)
            var hourUtc = current.Hour;
            var volumeMultiplier = (hourUtc is >= 14 and <= 15 or >= 20 and <= 21) ? 3.0 : 1.0;
            var volume = (decimal)(rng.Next(50_000, 500_000) * volumeMultiplier);

            yield return new Candle
            {
                Open     = open,
                High     = high,
                Low      = low,
                Close    = close,
                Volume   = volume,
                StartUtc = current,
                EndUtc   = candleEnd,
            };

            price = close;
            current = candleEnd;
        }
    }

    public Task<LatestPrice?> GetLatestPriceAsync(string symbol, CancellationToken ct = default)
    {
        var seed = symbol.GetHashCode();
        var rng = new Random(seed ^ DateTime.UtcNow.Second);

        var basePrice = symbol.ToUpperInvariant() switch
        {
            "TSLA" => 220m,
            "AAPL" => 185m,
            "NVDA" => 480m,
            "SPY"  => 520m,
            "QQQ"  => 440m,
            "MSFT" => 415m,
            _      => 100m + (Math.Abs(seed) % 400)
        };

        var price = Math.Round(basePrice * (1m + (decimal)(rng.NextDouble() - 0.5) * 0.01m), 2);
        LatestPrice? result = new(symbol, price, DateTime.UtcNow, FeedName);
        return Task.FromResult(result);
    }
}
