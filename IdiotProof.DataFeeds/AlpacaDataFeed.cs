using System.Runtime.CompilerServices;
using System.Text.Json;
using IdiotProof.Models;

namespace IdiotProof.DataFeeds;

/// <summary>
/// Alpaca Market Data API historical bar feed. Uses the same API key + secret the
/// user already has for the broker.
///
/// Data tier: requests start on the configured feed (default SIP). If Alpaca
/// rejects with 403 (free/IEX-only key), the client downgrades itself to IEX
/// once and retries — a free key works out of the box instead of silently
/// returning nothing. All other non-success responses throw with status + body
/// so callers (SupervisedLoop / per-symbol catch in the Monitor) can log the
/// real reason instead of seeing an inexplicable empty candle window.
/// The data endpoint is separate from the broker endpoint
/// (data.alpaca.markets vs api.alpaca.markets / paper-api.alpaca.markets).
/// </summary>
public sealed class AlpacaDataFeed : IMarketDataFeed, IAsyncDisposable
{
    private const string DataBaseUrl = "https://data.alpaca.markets/";

    private readonly HttpClient httpClient;
    private string feedTier;

    public string FeedName => "Alpaca";

    /// <summary>The data tier currently in use ("sip" or "iex"). Downgrades once on 403.</summary>
    public string FeedTier => feedTier;

    public AlpacaDataFeed(string apiKeyId, string apiSecretKey, string feed = "sip")
    {
        httpClient = new HttpClient { BaseAddress = new Uri(DataBaseUrl), Timeout = TimeSpan.FromSeconds(30) };
        httpClient.DefaultRequestHeaders.Add("APCA-API-KEY-ID",     apiKeyId     ?? "");
        httpClient.DefaultRequestHeaders.Add("APCA-API-SECRET-KEY", apiSecretKey ?? "");
        feedTier = string.IsNullOrWhiteSpace(feed) ? "sip" : feed.ToLowerInvariant();
    }

    public async Task<LatestPrice?> GetLatestPriceAsync(string symbol, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(symbol)) return null;

        // Latest *trade*, not quote: premarket quotes can have a zero ask on
        // thin books, and the gapper logic keys off last traded price anyway.
        var url = $"v2/stocks/{Uri.EscapeDataString(symbol)}/trades/latest?feed={feedTier}";
        using var response = await httpClient.GetAsync(url, ct).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.Forbidden && TryDowngradeTier())
            return await GetLatestPriceAsync(symbol, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("trade", out var t)) return null;
        if (!t.TryGetProperty("p", out var priceEl)) return null;
        var price = priceEl.GetDecimal();
        var ts = t.TryGetProperty("t", out var tEl) ? tEl.GetDateTime() : DateTime.UtcNow;
        return new LatestPrice(symbol, price, ts, "Alpaca");
    }

    public async IAsyncEnumerable<Candle> GetHistoricalCandlesAsync(
        string symbol, DateTime startUtc, DateTime endUtc, TimeSpan candleSize,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(symbol)) yield break;

        var timeframe = ToAlpacaTimeframe(candleSize);
        var start = startUtc.ToString("O");
        var end   = endUtc.ToString("O");
        string? pageToken = null;

        do
        {
            var (bars, nextToken) = await FetchPageAsync(symbol, timeframe, start, end, pageToken, ct).ConfigureAwait(false);
            pageToken = nextToken;

            foreach (var bar in bars)
            {
                ct.ThrowIfCancellationRequested();
                yield return new Candle
                {
                    Symbol   = symbol,
                    StartUtc = bar.Start,
                    EndUtc   = bar.Start + candleSize,
                    Open     = bar.Open,
                    High     = bar.High,
                    Low      = bar.Low,
                    Close    = bar.Close,
                    Volume   = bar.Volume,
                    Note     = "Alpaca",
                };
            }
        }
        while (!string.IsNullOrEmpty(pageToken));
    }

    private readonly record struct RawBar(DateTime Start, decimal Open, decimal High, decimal Low, decimal Close, decimal Volume);

    private async Task<(List<RawBar> Bars, string? NextToken)> FetchPageAsync(
        string symbol, string timeframe, string start, string end, string? pageToken, CancellationToken ct)
    {
        while (true)
        {
            var url = $"v2/stocks/{Uri.EscapeDataString(symbol)}/bars"
                    + $"?timeframe={timeframe}&start={Uri.EscapeDataString(start)}&end={Uri.EscapeDataString(end)}"
                    + $"&limit=1000&adjustment=raw&feed={feedTier}"
                    + (pageToken is not null ? $"&page_token={Uri.EscapeDataString(pageToken)}" : "");

            using var response = await httpClient.GetAsync(url, ct).ConfigureAwait(false);

            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden && TryDowngradeTier())
                continue; // one retry on the downgraded (IEX) tier

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                throw new HttpRequestException(
                    $"Alpaca bars request failed for {symbol} ({(int)response.StatusCode} {response.StatusCode}): {Truncate(body)}");
            }

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);

            var next = doc.RootElement.TryGetProperty("next_page_token", out var npt)
                && npt.ValueKind == JsonValueKind.String ? npt.GetString() : null;

            var list = new List<RawBar>();
            if (doc.RootElement.TryGetProperty("bars", out var bars) && bars.ValueKind == JsonValueKind.Array)
            {
                foreach (var bar in bars.EnumerateArray())
                {
                    list.Add(new RawBar(
                        bar.GetProperty("t").GetDateTime(),
                        bar.GetProperty("o").GetDecimal(),
                        bar.GetProperty("h").GetDecimal(),
                        bar.GetProperty("l").GetDecimal(),
                        bar.GetProperty("c").GetDecimal(),
                        bar.GetProperty("v").GetDecimal()));
                }
            }
            return (list, next);
        }
    }

    private bool TryDowngradeTier()
    {
        if (feedTier == "iex") return false;
        feedTier = "iex"; // free key — remember for all subsequent requests
        return true;
    }

    private static string Truncate(string s) => s.Length <= 300 ? s : s[..300] + "…";

    private static string ToAlpacaTimeframe(TimeSpan span)
    {
        if (span == TimeSpan.FromMinutes(1))  return "1Min";
        if (span == TimeSpan.FromMinutes(5))  return "5Min";
        if (span == TimeSpan.FromMinutes(15)) return "15Min";
        if (span == TimeSpan.FromHours(1))    return "1Hour";
        if (span >= TimeSpan.FromDays(1))     return "1Day";
        return "1Min";
    }

    public ValueTask DisposeAsync()
    {
        httpClient.Dispose();
        return ValueTask.CompletedTask;
    }
}
