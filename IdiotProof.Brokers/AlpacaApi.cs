using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace IdiotProof.Brokers;

/// <summary>An Alpaca REST call that came back non-2xx: the HTTP status plus Alpaca's own code and message.</summary>
public sealed class AlpacaApiException(HttpStatusCode status, int? code, string message, string path)
    : Exception($"Alpaca {(int)status} {status} on {path}: {message}")
{
    public HttpStatusCode Status { get; } = status;

    /// <summary>Alpaca's numeric error code from the body (e.g. 40310000), when it sent one.</summary>
    public int? AlpacaCode { get; } = code;

    public string AlpacaMessage { get; } = message;

    public string Path { get; } = path;
}

/// <summary>
/// The raw Alpaca REST surface for one account: the Trading API (paper or live) and the Market
/// Data API, every verb, JSON in and out. Typed callers (<see cref="AlpacaBrokerClient"/>, the
/// Hub's operations) build on this so that every endpoint Alpaca offers is one call away.
/// Retries 429s (honoring Retry-After, three tries); any other non-2xx throws
/// <see cref="AlpacaApiException"/>. Order placement does NOT belong here for callers: every
/// order goes through the TransactionGateway.
/// </summary>
public sealed class AlpacaApi : IAsyncDisposable
{
    public const string PaperTradingBase = "https://paper-api.alpaca.markets";
    public const string LiveTradingBase = "https://api.alpaca.markets";
    public const string DataBase = "https://data.alpaca.markets";

    private const int MaxAttempts = 3;
    private readonly HttpClient trading;
    private readonly HttpClient data;
    private readonly bool ownsClients;

    public AlpacaApi(string apiKeyId, string apiSecretKey, bool isPaper, HttpMessageHandler? handler = null)
    {
        IsPaper = isPaper;
        trading = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        data = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        trading.BaseAddress = new Uri(isPaper ? PaperTradingBase : LiveTradingBase);
        data.BaseAddress = new Uri(DataBase);
        foreach (var client in new[] { trading, data })
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Add("APCA-API-KEY-ID", apiKeyId);
            client.DefaultRequestHeaders.Add("APCA-API-SECRET-KEY", apiSecretKey);
        }
        ownsClients = true;
    }

    /// <summary>True for the paper Trading API; the Market Data API is the same for both.</summary>
    public bool IsPaper { get; }

    /// <summary>GET on the Trading API, e.g. <c>/v2/account</c>.</summary>
    public Task<JsonElement> GetAsync(string path, IReadOnlyDictionary<string, string?>? query = null, CancellationToken ct = default) =>
        SendAsync(trading, HttpMethod.Get, path, query, null, ct);

    /// <summary>Any verb on the Trading API; <paramref name="body"/> is serialized as JSON (snake_case is the caller's job).</summary>
    public Task<JsonElement> SendAsync(HttpMethod method, string path, object? body = null,
        IReadOnlyDictionary<string, string?>? query = null, CancellationToken ct = default) =>
        SendAsync(trading, method, path, query, body, ct);

    /// <summary>GET on the Market Data API, e.g. <c>/v2/stocks/AAPL/snapshot</c>.</summary>
    public Task<JsonElement> GetDataAsync(string path, IReadOnlyDictionary<string, string?>? query = null, CancellationToken ct = default) =>
        SendAsync(data, HttpMethod.Get, path, query, null, ct);

    /// <summary>
    /// Follows <c>next_page_token</c> on a single-symbol Market Data list endpoint and yields every
    /// element of the <paramref name="itemsProperty"/> array (e.g. "bars", "trades", "news").
    /// Multi-symbol endpoints key that property by symbol; page those with <see cref="GetDataAsync"/>.
    /// </summary>
    public async IAsyncEnumerable<JsonElement> GetDataPagedAsync(string path, string itemsProperty,
        IReadOnlyDictionary<string, string?>? query = null, int maxPages = 100, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var q = new Dictionary<string, string?>(query ?? new Dictionary<string, string?>());
        for (int page = 0; page < maxPages; page++)
        {
            var root = await GetDataAsync(path, q, ct).ConfigureAwait(false);
            if (root.TryGetProperty(itemsProperty, out var items) && items.ValueKind == JsonValueKind.Array)
                foreach (var item in items.EnumerateArray()) yield return item;
            if (!root.TryGetProperty("next_page_token", out var token) || token.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(token.GetString()))
                yield break;
            q["page_token"] = token.GetString();
        }
    }

    /// <summary><c>?a=1&amp;b=x</c> from the non-null entries, values URL-escaped.</summary>
    public static string Query(IReadOnlyDictionary<string, string?>? query)
    {
        if (query is null) return "";
        var parts = query.Where(kv => !string.IsNullOrEmpty(kv.Value))
            .Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value!)}").ToList();
        return parts.Count == 0 ? "" : "?" + string.Join("&", parts);
    }

    private static async Task<JsonElement> SendAsync(HttpClient client, HttpMethod method, string path,
        IReadOnlyDictionary<string, string?>? query, object? body, CancellationToken ct)
    {
        var url = path + Query(query);
        for (int attempt = 1; ; attempt++)
        {
            using var request = new HttpRequestMessage(method, url);
            if (body is not null) request.Content = JsonContent.Create(body, body.GetType());
            using var response = await client.SendAsync(request, ct).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < MaxAttempts)
            {
                var wait = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(attempt * 2);
                await Task.Delay(wait, ct).ConfigureAwait(false);
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                int? code = null;
                var message = text.Length <= 300 ? text : text[..300] + "...";
                try
                {
                    using var err = JsonDocument.Parse(text);
                    if (err.RootElement.TryGetProperty("code", out var c) && c.TryGetInt32(out var n)) code = n;
                    if (err.RootElement.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String) message = m.GetString()!;
                }
                catch (JsonException) { }
                throw new AlpacaApiException(response.StatusCode, code, message, path);
            }

            if (string.IsNullOrWhiteSpace(text)) return default; // 204 No Content (e.g. cancel)
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.Clone();
        }
    }

    public ValueTask DisposeAsync()
    {
        if (ownsClients)
        {
            trading.Dispose();
            data.Dispose();
        }
        return ValueTask.CompletedTask;
    }
}
