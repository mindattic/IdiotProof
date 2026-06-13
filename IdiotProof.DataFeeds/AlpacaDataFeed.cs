using System.Runtime.CompilerServices;
using System.Text.Json;
using IdiotProof.Models;

namespace IdiotProof.DataFeeds;

/// <summary>
/// Alpaca Market Data API historical bar feed. Uses the same API key + secret the
/// user already has for the broker — no separate Polygon account required.
///
/// Free tier delivers IEX data (15-min delayed). Paid/Unlimited SIP feed is used
/// automatically when available. The data endpoint is separate from the broker
/// endpoint (data.alpaca.markets vs api.alpaca.markets / paper-api.alpaca.markets).
/// </summary>
public sealed class AlpacaDataFeed : IMarketDataFeed, IAsyncDisposable
{
    private const string DataBaseUrl = "https://data.alpaca.markets/";

    private readonly HttpClient httpClient;

    public string FeedName => "Alpaca";

    public AlpacaDataFeed(string apiKeyId, string apiSecretKey)
    {
        httpClient = new HttpClient { BaseAddress = new Uri(DataBaseUrl), Timeout = TimeSpan.FromSeconds(30) };
        httpClient.DefaultRequestHeaders.Add("APCA-API-KEY-ID",     apiKeyId     ?? "");
        httpClient.DefaultRequestHeaders.Add("APCA-API-SECRET-KEY", apiSecretKey ?? "");
    }

    public async Task<LatestPrice?> GetLatestPriceAsync(string symbol, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(symbol)) return null;
        var url = $"v2/stocks/{Uri.EscapeDataString(symbol)}/quotes/latest";
        using var response = await httpClient.GetAsync(url, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("quote", out var q)) return null;
        if (!q.TryGetProperty("ap", out var askEl)) return null;
        var price = askEl.GetDecimal();
        var ts = q.TryGetProperty("t", out var tEl) ? tEl.GetDateTime() : DateTime.UtcNow;
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
            var url = $"v2/stocks/{Uri.EscapeDataString(symbol)}/bars"
                    + $"?timeframe={timeframe}&start={Uri.EscapeDataString(start)}&end={Uri.EscapeDataString(end)}"
                    + $"&limit=1000&adjustment=raw&feed=sip"
                    + (pageToken is not null ? $"&page_token={Uri.EscapeDataString(pageToken)}" : "");

            using var response = await httpClient.GetAsync(url, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) yield break;

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);

            pageToken = doc.RootElement.TryGetProperty("next_page_token", out var npt)
                && npt.ValueKind == JsonValueKind.String ? npt.GetString() : null;

            if (!doc.RootElement.TryGetProperty("bars", out var bars) || bars.ValueKind != JsonValueKind.Array)
                yield break;

            foreach (var bar in bars.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();
                var barStart = bar.GetProperty("t").GetDateTime();
                yield return new Candle
                {
                    Symbol   = symbol,
                    StartUtc = barStart,
                    EndUtc   = barStart + candleSize,
                    Open     = bar.GetProperty("o").GetDecimal(),
                    High     = bar.GetProperty("h").GetDecimal(),
                    Low      = bar.GetProperty("l").GetDecimal(),
                    Close    = bar.GetProperty("c").GetDecimal(),
                    Volume   = bar.GetProperty("v").GetDecimal(),
                    Note     = "Alpaca",
                };
            }
        }
        while (!string.IsNullOrEmpty(pageToken));
    }

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
