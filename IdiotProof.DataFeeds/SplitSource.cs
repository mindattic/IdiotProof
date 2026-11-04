using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text.Json;
using IdiotProof.Models;

namespace IdiotProof.DataFeeds;

/// <summary>Stock splits (forward and reverse) per symbol over a date range.</summary>
public interface ISplitSource
{
    Task<IReadOnlyDictionary<string, IReadOnlyList<SplitEvent>>> GetSplitsAsync(
        IEnumerable<string> symbols, DateOnly from, DateOnly to, CancellationToken ct = default);
}

/// <summary>No split data (Mock feed, no keys): every symbol has no splits.</summary>
public sealed class NullSplitSource : ISplitSource
{
    public Task<IReadOnlyDictionary<string, IReadOnlyList<SplitEvent>>> GetSplitsAsync(
        IEnumerable<string> symbols, DateOnly from, DateOnly to, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<SplitEvent>>>(new Dictionary<string, IReadOnlyList<SplitEvent>>());
}

/// <summary>
/// Splits from Alpaca Market Data <c>/v1/corporate-actions</c>, cached in memory per symbol for the
/// widest range fetched so far.
/// </summary>
public sealed class AlpacaSplitSource : ISplitSource, IDisposable
{
    private const int SymbolsPerRequest = 50;
    private const int MaxAttempts = 5;
    private readonly HttpClient http;
    private readonly Func<TimeSpan, CancellationToken, Task> delay;
    private readonly ConcurrentDictionary<string, (DateOnly From, DateOnly To, List<SplitEvent> Splits)> cache = new(StringComparer.OrdinalIgnoreCase);

    public AlpacaSplitSource(string apiKeyId, string apiSecretKey)
        : this(CreateClient(apiKeyId, apiSecretKey), Task.Delay) { }

    public AlpacaSplitSource(HttpClient http, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        this.http = http;
        this.delay = delay ?? Task.Delay;
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<SplitEvent>>> GetSplitsAsync(
        IEnumerable<string> symbols, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var wanted = symbols.Select(s => s.Trim().ToUpperInvariant()).Where(s => s.Length > 0).Distinct().ToList();
        var missing = wanted.Where(s => !cache.TryGetValue(s, out var c) || c.From > from || c.To < to).ToList();

        foreach (var chunk in missing.Chunk(SymbolsPerRequest))
        {
            var fetched = await FetchAsync(chunk, from, to, ct).ConfigureAwait(false);
            foreach (var s in chunk)
                cache[s] = (from, to, fetched.TryGetValue(s, out var list) ? list : new List<SplitEvent>());
        }

        var result = new Dictionary<string, IReadOnlyList<SplitEvent>>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in wanted)
            result[s] = cache.TryGetValue(s, out var c)
                ? c.Splits.Where(x => x.ExDate >= from && x.ExDate <= to).ToList()
                : Array.Empty<SplitEvent>();
        return result;
    }

    private async Task<Dictionary<string, List<SplitEvent>>> FetchAsync(string[] symbols, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var result = new Dictionary<string, List<SplitEvent>>(StringComparer.OrdinalIgnoreCase);
        string? page = null;
        do
        {
            var url = "v1/corporate-actions?symbols=" + Uri.EscapeDataString(string.Join(',', symbols))
                    + "&types=forward_split,reverse_split"
                    + "&start=" + from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    + "&end=" + to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    + "&limit=1000" + (page is null ? "" : "&page_token=" + Uri.EscapeDataString(page));
            using var doc = await GetJsonAsync(url, ct).ConfigureAwait(false);
            var root = doc.RootElement;
            if (root.TryGetProperty("corporate_actions", out var groups) && groups.ValueKind == JsonValueKind.Object)
                foreach (var kind in new[] { "forward_splits", "reverse_splits" })
                    if (groups.TryGetProperty(kind, out var arr) && arr.ValueKind == JsonValueKind.Array)
                        foreach (var e in arr.EnumerateArray())
                            if (TryParse(e, out var split))
                            {
                                if (!result.TryGetValue(split.Symbol, out var list)) result[split.Symbol] = list = new List<SplitEvent>();
                                list.Add(split);
                            }
            page = root.TryGetProperty("next_page_token", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
        }
        while (!string.IsNullOrEmpty(page));
        return result;
    }

    internal static bool TryParse(JsonElement e, out SplitEvent split)
    {
        split = null!;
        if (!e.TryGetProperty("symbol", out var sym) || sym.ValueKind != JsonValueKind.String) return false;
        if (!e.TryGetProperty("ex_date", out var ex) || !DateOnly.TryParse(ex.GetString(), CultureInfo.InvariantCulture, out var exDate)) return false;
        if (!TryRate(e, "old_rate", out var oldRate) || !TryRate(e, "new_rate", out var newRate)) return false;
        if (oldRate <= 0 || newRate <= 0) return false;
        split = new SplitEvent(sym.GetString()!.ToUpperInvariant(), exDate, oldRate, newRate);
        return true;
    }

    private static bool TryRate(JsonElement e, string name, out decimal rate)
    {
        rate = 0;
        if (!e.TryGetProperty(name, out var v)) return false;
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.TryGetDecimal(out rate),
            JsonValueKind.String => decimal.TryParse(v.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out rate),
            _ => false,
        };
    }

    private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken ct)
    {
        for (int attempt = 1; ; attempt++)
        {
            using var response = await http.GetAsync(url, ct).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
                return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            bool transient = response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.BadGateway
                or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;
            if (!transient || attempt >= MaxAttempts)
                throw new HttpRequestException($"Alpaca corporate-actions request failed ({(int)response.StatusCode} {response.StatusCode}).", null, response.StatusCode);
            await delay(TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, attempt - 1))), ct).ConfigureAwait(false);
        }
    }

    private static HttpClient CreateClient(string apiKeyId, string apiSecretKey)
    {
        var client = new HttpClient { BaseAddress = new Uri("https://data.alpaca.markets/"), Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.Add("APCA-API-KEY-ID", apiKeyId ?? "");
        client.DefaultRequestHeaders.Add("APCA-API-SECRET-KEY", apiSecretKey ?? "");
        return client;
    }

    public void Dispose() => http.Dispose();
}
