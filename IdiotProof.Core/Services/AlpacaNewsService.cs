using System.Text.Json;
using IdiotProof.Engine.Settings;

using IdiotProof.Core.Data;
namespace IdiotProof.Core.Services;

public sealed record AlpacaNewsArticle(
    string   Headline,
    string   Summary,
    string   Source,
    string   Url,
    DateTime PublishedAt);

/// <summary>
/// Pulls news articles for a ticker from the Alpaca data API.
/// Alpaca News aggregates Benzinga, Reuters, Yahoo Finance, and other editorial
/// sources — authenticated with the same key/secret already on file.
/// The endpoint returns items newest-first; we cap at 50 per call.
/// </summary>
public sealed class AlpacaNewsService(
    IHttpClientFactory httpFactory,
    AppSettings settings,
    ILogger<AlpacaNewsService> logger)
{
    private const string DataBase = "https://data.alpaca.markets/";

    public async Task<List<AlpacaNewsArticle>> GetNewsAsync(
        string ticker, int daysBack = 30, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(settings.AlpacaApiKeyId)) return [];

        var start = DateTime.UtcNow.AddDays(-daysBack).ToString("yyyy-MM-dd");
        var url = $"{DataBase}v1beta1/news?symbols={Uri.EscapeDataString(ticker.ToUpperInvariant())}&limit=50&start={start}&include_content=false&sort=DESC";

        try
        {
            using var client = httpFactory.CreateClient();
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("APCA-API-KEY-ID",     settings.AlpacaApiKeyId);
            req.Headers.Add("APCA-API-SECRET-KEY", settings.AlpacaApiSecretKey);

            using var resp = await client.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                logger.LogDebug("Alpaca news returned {Status} for {Ticker}", resp.StatusCode, ticker);
                return [];
            }

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            if (!doc.RootElement.TryGetProperty("news", out var arr)) return [];

            var articles = new List<AlpacaNewsArticle>();
            foreach (var item in arr.EnumerateArray())
            {
                var headline  = Str(item, "headline");
                if (string.IsNullOrWhiteSpace(headline)) continue;

                var summary   = Str(item, "summary");
                var source    = Str(item, "source", "Alpaca News");
                var articleUrl  = Str(item, "url");
                var published = item.TryGetProperty("created_at", out var d) && d.TryGetDateTime(out var dt)
                    ? dt : DateTime.UtcNow;

                articles.Add(new AlpacaNewsArticle(headline, summary, source, articleUrl, published));
            }

            logger.LogDebug("Alpaca news: {Count} articles for {Ticker}", articles.Count, ticker);
            return articles;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Alpaca news fetch failed for {Ticker}", ticker);
            return [];
        }
    }

    /// <summary>
    /// One page of news for <paramref name="symbols"/> (empty = every symbol) inside a date range,
    /// oldest first, with Alpaca's <c>next_page_token</c>; <paramref name="keys"/> overrides the Hub's
    /// pair (the catalyst backfill uses the owner's). Returns an empty page on any failure.
    /// </summary>
    public async Task<(List<AlpacaNewsItem> Items, string? NextPageToken)> GetNewsPageAsync(
        IReadOnlyList<string> symbols, DateTime startUtc, DateTime endUtc, int limit = 50, string? pageToken = null,
        (string KeyId, string Secret)? keys = null, CancellationToken ct = default)
    {
        var keyId = keys?.KeyId ?? settings.AlpacaApiKeyId;
        var secret = keys?.Secret ?? settings.AlpacaApiSecretKey;
        if (string.IsNullOrWhiteSpace(keyId) || string.IsNullOrWhiteSpace(secret)) return ([], null);

        var url = $"{DataBase}v1beta1/news?limit={Math.Clamp(limit, 1, 50)}&start={Iso(startUtc)}&end={Iso(endUtc)}&include_content=false&sort=ASC";
        if (symbols.Count > 0) url += "&symbols=" + Uri.EscapeDataString(string.Join(",", symbols.Select(s => s.ToUpperInvariant())));
        if (!string.IsNullOrEmpty(pageToken)) url += "&page_token=" + Uri.EscapeDataString(pageToken);

        try
        {
            using var client = httpFactory.CreateClient();
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("APCA-API-KEY-ID", keyId);
            req.Headers.Add("APCA-API-SECRET-KEY", secret);
            using var resp = await client.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                logger.LogDebug("Alpaca news page returned {Status}", resp.StatusCode);
                return ([], null);
            }
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            return ParsePage(doc.RootElement);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Alpaca news page fetch failed");
            return ([], null);
        }
    }

    /// <summary>Every item in the range, following <c>next_page_token</c> until Alpaca returns none (or <paramref name="maxPages"/>).</summary>
    public async IAsyncEnumerable<AlpacaNewsItem> GetNewsRangeAsync(IReadOnlyList<string> symbols, DateTime startUtc, DateTime endUtc,
        int maxPages = 400, (string KeyId, string Secret)? keys = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        string? token = null;
        for (var page = 0; page < maxPages; page++)
        {
            var (items, next) = await GetNewsPageAsync(symbols, startUtc, endUtc, 50, token, keys, ct);
            foreach (var item in items) yield return item;
            if (string.IsNullOrEmpty(next) || items.Count == 0) yield break;
            token = next;
        }
    }

    /// <summary>Parses a <c>{"news":[...],"next_page_token":...}</c> body.</summary>
    public static (List<AlpacaNewsItem> Items, string? NextPageToken) ParsePage(JsonElement root)
    {
        var items = new List<AlpacaNewsItem>();
        if (root.TryGetProperty("news", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in arr.EnumerateArray())
            {
                var id = e.TryGetProperty("id", out var idEl) ? idEl.ValueKind == JsonValueKind.Number ? idEl.GetRawText() : idEl.GetString() ?? "" : "";
                var headline = Str(e, "headline");
                if (id.Length == 0 || headline.Length == 0) continue;
                var symbols = e.TryGetProperty("symbols", out var s) && s.ValueKind == JsonValueKind.Array
                    ? s.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!.ToUpperInvariant()).ToList()
                    : [];
                items.Add(new AlpacaNewsItem(id, headline, Str(e, "summary"), Str(e, "author"), Str(e, "source", "Alpaca News"), Str(e, "url"),
                    Time(e, "created_at") ?? DateTime.UtcNow, Time(e, "updated_at"), symbols));
            }
        }
        var next = root.TryGetProperty("next_page_token", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
        return (items, string.IsNullOrEmpty(next) ? null : next);
    }

    private static string Iso(DateTime utc) =>
        DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);

    private static DateTime? Time(JsonElement e, string key) =>
        e.TryGetProperty(key, out var d) && d.ValueKind == JsonValueKind.String && d.TryGetDateTime(out var dt) ? dt.ToUniversalTime() : null;

    private static string Str(JsonElement e, string key, string fallback = "")
        => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? fallback
            : fallback;
}

/// <summary>A news item with the fields the catalyst intake keeps (id, full timestamps, every symbol).</summary>
public sealed record AlpacaNewsItem(
    string Id,
    string Headline,
    string Summary,
    string Author,
    string Source,
    string Url,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    IReadOnlyList<string> Symbols);
