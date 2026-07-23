using System.Text.Json;
using IdiotProof.Engine.Settings;

namespace IdiotProof.Blazor.Services;

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

    private static string Str(JsonElement e, string key, string fallback = "")
        => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? fallback
            : fallback;
}
