using System.Runtime.CompilerServices;
using System.Text.Json;
using IdiotProof.Models;

namespace IdiotProof.DataFeeds;

/// <summary>
/// Polygon.io (Massive) market data feed.
/// </summary>
public sealed class PolygonDataFeed : IMarketDataFeed, IAsyncDisposable
{
    private readonly string apiKey;
    private readonly HttpClient httpClient;

    public string FeedName => "Polygon";

    public PolygonDataFeed(string apiKey)
    {
        this.apiKey = apiKey ?? string.Empty;
        httpClient = new HttpClient
        {
            BaseAddress = new Uri("https://api.polygon.io/"),
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    public async Task<LatestPrice?> GetLatestPriceAsync(string symbol, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Polygon API key not configured.");
        if (string.IsNullOrWhiteSpace(symbol)) return null;

        var url = $"v2/last/trade/{symbol}?apiKey={apiKey}";
        using var response = await httpClient.GetAsync(url, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var data = JsonSerializer.Deserialize<PolygonLastTradeResponse>(json, JsonOptions);
        if (data?.Results?.P is null || data.Results.T is null) return null;

        return new LatestPrice(
            symbol,
            data.Results.P.Value,
            DateTimeOffset.FromUnixTimeMilliseconds(data.Results.T.Value).UtcDateTime,
            "Polygon.io");
    }

    public async IAsyncEnumerable<Candle> GetHistoricalCandlesAsync(
        string symbol, DateTime startUtc, DateTime endUtc, TimeSpan candleSize,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Polygon API key not configured.");
        if (string.IsNullOrWhiteSpace(symbol)) yield break;

        // Use millisecond-epoch bounds, not date-only strings. A date-only `to` is
        // interpreted by Polygon as the START of that day (midnight), so an intraday
        // window like 13:00Z–20:00Z on the same day collapsed to from==to==that-date
        // and dropped the entire session. Epoch-ms preserves the time-of-day for both
        // daily and intraday requests.
        var from = ((DateTimeOffset)DateTime.SpecifyKind(startUtc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
        var to = ((DateTimeOffset)DateTime.SpecifyKind(endUtc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
        var (multiplier, timespan) = ToPolygonSpan(candleSize);

        // Follow next_url pagination: the aggregates endpoint caps each page
        // (limit=50000); without following next_url a larger window silently
        // truncated to the first page. Non-2xx responses now throw with the
        // status + body — the old silent `yield break` made an auth failure or
        // rate-limit indistinguishable from "no data that day".
        var url = $"v2/aggs/ticker/{symbol}/range/{multiplier}/{timespan}/{from}/{to}?adjusted=true&sort=asc&limit=50000&apiKey={apiKey}";
        while (url is not null)
        {
            var (results, nextUrl) = await FetchPageAsync(url, symbol, ct).ConfigureAwait(false);
            foreach (var r in results)
            {
                ct.ThrowIfCancellationRequested();
                var start = DateTimeOffset.FromUnixTimeMilliseconds(r.T).UtcDateTime;
                yield return new Candle
                {
                    Symbol = symbol,
                    StartUtc = start,
                    EndUtc = start + candleSize,
                    Open = r.O, High = r.H, Low = r.L, Close = r.C, Volume = r.V,
                    Note = "Polygon.io"
                };
            }
            url = nextUrl;
        }
    }

    private async Task<(PolygonAgg[] Results, string? NextUrl)> FetchPageAsync(string url, string symbol, CancellationToken ct)
    {
        using var response = await httpClient.GetAsync(url, ct).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"Polygon aggregates request failed for {symbol} ({(int)response.StatusCode} {response.StatusCode}): {(json.Length <= 300 ? json : json[..300] + "…")}");

        var data = JsonSerializer.Deserialize<PolygonAggsResponse>(json, JsonOptions);

        // next_url comes back without the apiKey; re-append it.
        var next = data?.NextUrl;
        if (!string.IsNullOrWhiteSpace(next) && !next.Contains("apiKey=", StringComparison.OrdinalIgnoreCase))
            next += (next.Contains('?') ? "&" : "?") + "apiKey=" + apiKey;

        return (data?.Results ?? [], string.IsNullOrWhiteSpace(next) ? null : next);
    }

    private static (int multiplier, string timespan) ToPolygonSpan(TimeSpan candleSize)
    {
        if (candleSize == TimeSpan.FromMinutes(1)) return (1, "minute");
        if (candleSize == TimeSpan.FromMinutes(5)) return (5, "minute");
        if (candleSize == TimeSpan.FromMinutes(15)) return (15, "minute");
        if (candleSize == TimeSpan.FromHours(1)) return (1, "hour");
        if (candleSize >= TimeSpan.FromDays(1)) return (1, "day");
        return (1, "minute");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ValueTask DisposeAsync()
    {
        httpClient.Dispose();
        return ValueTask.CompletedTask;
    }
}

internal sealed class PolygonLastTradeResponse
{
    public PolygonLastTradeResult? Results { get; init; }
}

internal sealed class PolygonLastTradeResult
{
    public decimal? P { get; init; }
    public long? T { get; init; }
}

internal sealed class PolygonAggsResponse
{
    public PolygonAgg[]? Results { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("next_url")]
    public string? NextUrl { get; init; }
}

internal sealed class PolygonAgg
{
    public long T { get; init; }
    public decimal O { get; init; }
    public decimal H { get; init; }
    public decimal L { get; init; }
    public decimal C { get; init; }
    public decimal V { get; init; }
}
