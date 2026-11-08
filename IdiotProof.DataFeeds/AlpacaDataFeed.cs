using System.Runtime.CompilerServices;
using System.Text.Json;
using IdiotProof.Models;

namespace IdiotProof.DataFeeds;

/// <summary>One multi-symbol bars pull: bars per symbol (ascending), pages fetched, and the data tier that served them.</summary>
public sealed record MultiSymbolBars(IReadOnlyDictionary<string, IReadOnlyList<Candle>> Bars, int Pages, string Tier);

/// <summary>One trade print: time, price, size and the reporting exchange code ("D" = FINRA TRF, off-exchange).</summary>
public sealed record TradePrintRecord(DateTime Utc, double Price, double Size, string Exchange);

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
    private volatile string feedTier;

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
            var (bars, nextToken, tier) = await FetchPageAsync(symbol, timeframe, start, end, pageToken, ct).ConfigureAwait(false);
            pageToken = nextToken;
            var source = SourceLabel(tier);

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
                    Note     = source,
                };
            }
        }
        while (!string.IsNullOrEmpty(pageToken));
    }

    /// <summary>
    /// The provenance stamped on each historical bar: <c>Alpaca:sip</c> or <c>Alpaca:iex</c>, the tier
    /// that actually served the page (a refused SIP request silently falls back to IEX).
    /// </summary>
    public static string SourceLabel(string tier) => $"Alpaca:{tier}";

    /// <summary>Largest symbol list the multi-symbol bars endpoint accepts in one request.</summary>
    public const int MaxSymbolsPerRequest = 100;

    /// <summary>
    /// Bars for many symbols in one request stream (<c>/v2/stocks/bars?symbols=A,B,...</c>, paged at
    /// <paramref name="limit"/> bars per page, <c>adjustment=raw</c>). Every bar is a
    /// <see cref="DetailedCandle"/> with trade count and VWAP. <paramref name="onPage"/> runs before
    /// each request so a caller can pace to the data API's rate limit. A 429 or gateway error backs
    /// off exponentially up to five attempts; any other failure throws with status and body.
    /// </summary>
    public async Task<MultiSymbolBars> GetHistoricalCandlesForSymbolsAsync(
        IReadOnlyList<string> symbols, TimeSpan candleSize, DateTime startUtc, DateTime endUtc,
        int limit = 10000, Func<CancellationToken, Task>? onPage = null, CancellationToken ct = default)
    {
        var list = symbols.Select(s => s.Trim().ToUpperInvariant()).Where(s => s.Length > 0).Distinct().ToList();
        var bars = new Dictionary<string, List<Candle>>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in list) bars[s] = [];
        if (list.Count == 0) return new MultiSymbolBars(Freeze(bars), 0, feedTier);
        if (list.Count > MaxSymbolsPerRequest)
            throw new ArgumentException($"At most {MaxSymbolsPerRequest} symbols per request, got {list.Count}.", nameof(symbols));

        var timeframe = ToAlpacaTimeframe(candleSize);
        var query = $"v2/stocks/bars?symbols={Uri.EscapeDataString(string.Join(',', list))}&timeframe={timeframe}"
                  + $"&start={Uri.EscapeDataString(startUtc.ToString("O"))}&end={Uri.EscapeDataString(endUtc.ToString("O"))}"
                  + $"&limit={Math.Clamp(limit, 1, 10000)}&adjustment=raw";
        string? pageToken = null;
        int pages = 0;
        string tierUsed = feedTier;
        do
        {
            if (onPage is not null) await onPage(ct).ConfigureAwait(false);
            var tier = feedTier;
            var url = query + $"&feed={tier}" + (pageToken is null ? "" : $"&page_token={Uri.EscapeDataString(pageToken)}");
            using var response = await SendWithBackoffAsync(url, ct).ConfigureAwait(false);
            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden && TryDowngradeTier()) continue;
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                throw new HttpRequestException(
                    $"Alpaca multi-symbol bars request failed ({(int)response.StatusCode} {response.StatusCode}): {Truncate(body)}",
                    null, response.StatusCode);
            }
            pages++;
            tierUsed = tier;
            var source = SourceLabel(tier);
            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            pageToken = doc.RootElement.TryGetProperty("next_page_token", out var npt) && npt.ValueKind == JsonValueKind.String ? npt.GetString() : null;
            if (doc.RootElement.TryGetProperty("bars", out var bySymbol) && bySymbol.ValueKind == JsonValueKind.Object)
            {
                foreach (var entry in bySymbol.EnumerateObject())
                {
                    if (entry.Value.ValueKind != JsonValueKind.Array) continue;
                    if (!bars.TryGetValue(entry.Name, out var target)) bars[entry.Name] = target = [];
                    foreach (var bar in entry.Value.EnumerateArray())
                    {
                        var start = bar.GetProperty("t").GetDateTime();
                        target.Add(new DetailedCandle
                        {
                            Symbol = entry.Name.ToUpperInvariant(),
                            StartUtc = DateTime.SpecifyKind(start, DateTimeKind.Utc),
                            EndUtc = DateTime.SpecifyKind(start, DateTimeKind.Utc) + candleSize,
                            Open = bar.GetProperty("o").GetDecimal(),
                            High = bar.GetProperty("h").GetDecimal(),
                            Low = bar.GetProperty("l").GetDecimal(),
                            Close = bar.GetProperty("c").GetDecimal(),
                            Volume = bar.GetProperty("v").GetDecimal(),
                            TradeCount = bar.TryGetProperty("n", out var n) && n.ValueKind == JsonValueKind.Number ? n.GetInt64() : 0,
                            Vwap = bar.TryGetProperty("vw", out var vw) && vw.ValueKind == JsonValueKind.Number ? vw.GetDecimal() : 0,
                            Note = source,
                        });
                    }
                }
            }
        }
        while (!string.IsNullOrEmpty(pageToken));
        return new MultiSymbolBars(Freeze(bars), pages, tierUsed);
    }

    /// <summary>
    /// Trade prints for many symbols over one window (<c>/v2/stocks/trades?symbols=...</c>, paged at 10,000
    /// prints): time, price, size and reporting exchange per print. <paramref name="onPage"/> runs before each
    /// request so a caller can pace it; the same backoff and tier downgrade as the multi-symbol bars apply.
    /// </summary>
    public async Task<(IReadOnlyDictionary<string, List<TradePrintRecord>> Trades, int Pages, string Tier)> GetTradesForSymbolsAsync(
        IReadOnlyList<string> symbols, DateTime startUtc, DateTime endUtc, Func<CancellationToken, Task>? onPage = null, CancellationToken ct = default)
    {
        var list = symbols.Select(s => s.Trim().ToUpperInvariant()).Where(s => s.Length > 0).Distinct().ToList();
        var trades = new Dictionary<string, List<TradePrintRecord>>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in list) trades[s] = [];
        if (list.Count == 0) return (trades, 0, feedTier);
        if (list.Count > MaxSymbolsPerRequest)
            throw new ArgumentException($"At most {MaxSymbolsPerRequest} symbols per request, got {list.Count}.", nameof(symbols));
        var query = $"v2/stocks/trades?symbols={Uri.EscapeDataString(string.Join(',', list))}"
                  + $"&start={Uri.EscapeDataString(startUtc.ToString("O"))}&end={Uri.EscapeDataString(endUtc.ToString("O"))}&limit=10000";
        string? pageToken = null;
        int pages = 0;
        string tierUsed = feedTier;
        do
        {
            if (onPage is not null) await onPage(ct).ConfigureAwait(false);
            var tier = feedTier;
            var url = query + $"&feed={tier}" + (pageToken is null ? "" : $"&page_token={Uri.EscapeDataString(pageToken)}");
            using var response = await SendWithBackoffAsync(url, ct).ConfigureAwait(false);
            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden && TryDowngradeTier()) continue;
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                throw new HttpRequestException($"Alpaca multi-symbol trades request failed ({(int)response.StatusCode} {response.StatusCode}): {Truncate(body)}", null, response.StatusCode);
            }
            pages++;
            tierUsed = tier;
            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
            pageToken = doc.RootElement.TryGetProperty("next_page_token", out var npt) && npt.ValueKind == JsonValueKind.String ? npt.GetString() : null;
            if (doc.RootElement.TryGetProperty("trades", out var bySymbol) && bySymbol.ValueKind == JsonValueKind.Object)
                foreach (var entry in bySymbol.EnumerateObject())
                {
                    if (entry.Value.ValueKind != JsonValueKind.Array) continue;
                    if (!trades.TryGetValue(entry.Name, out var target)) trades[entry.Name] = target = [];
                    foreach (var t in entry.Value.EnumerateArray())
                    {
                        if (!t.TryGetProperty("t", out var time) || !t.TryGetProperty("p", out var p) || !t.TryGetProperty("s", out var s)) continue;
                        target.Add(new TradePrintRecord(DateTime.SpecifyKind(time.GetDateTime().ToUniversalTime(), DateTimeKind.Utc), p.GetDouble(), s.GetDouble(),
                            t.TryGetProperty("x", out var x) && x.ValueKind == JsonValueKind.String ? x.GetString() ?? "" : ""));
                    }
                }
        }
        while (!string.IsNullOrEmpty(pageToken));
        return (trades, pages, tierUsed);
    }

    private async Task<HttpResponseMessage> SendWithBackoffAsync(string url, CancellationToken ct)
    {
        for (int attempt = 1; ; attempt++)
        {
            var response = await httpClient.GetAsync(url, ct).ConfigureAwait(false);
            var transient = response.StatusCode is System.Net.HttpStatusCode.TooManyRequests or System.Net.HttpStatusCode.BadGateway
                or System.Net.HttpStatusCode.ServiceUnavailable or System.Net.HttpStatusCode.GatewayTimeout;
            if (!transient || attempt >= 5) return response;
            response.Dispose();
            await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, attempt))), ct).ConfigureAwait(false);
        }
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<Candle>> Freeze(Dictionary<string, List<Candle>> bars)
    {
        var result = new Dictionary<string, IReadOnlyList<Candle>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (symbol, list) in bars)
        {
            list.Sort((a, b) => a.StartUtc.CompareTo(b.StartUtc));
            result[symbol] = list;
        }
        return result;
    }

    private readonly record struct RawBar(DateTime Start, decimal Open, decimal High, decimal Low, decimal Close, decimal Volume);

    private async Task<(List<RawBar> Bars, string? NextToken, string Tier)> FetchPageAsync(
        string symbol, string timeframe, string start, string end, string? pageToken, CancellationToken ct)
    {
        while (true)
        {
            var tier = feedTier;
            var url = $"v2/stocks/{Uri.EscapeDataString(symbol)}/bars"
                    + $"?timeframe={timeframe}&start={Uri.EscapeDataString(start)}&end={Uri.EscapeDataString(end)}"
                    + $"&limit=1000&adjustment=raw&feed={tier}"
                    + (pageToken is not null ? $"&page_token={Uri.EscapeDataString(pageToken)}" : "");

            using var response = await httpClient.GetAsync(url, ct).ConfigureAwait(false);

            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden && TryDowngradeTier())
                continue; // one retry on the downgraded (IEX) tier

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                throw new HttpRequestException(
                    $"Alpaca bars request failed for {symbol} ({(int)response.StatusCode} {response.StatusCode}): {Truncate(body)}",
                    null, response.StatusCode);
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
            return (list, next, tier);
        }
    }

    private readonly Lock feedTierLock = new();

    /// <summary>
    /// Switches the data tier for every later request (<c>idiotproof market feed switch sip|iex</c>).
    /// Only <c>sip</c> and <c>iex</c> exist; anything else throws. A SIP request that Alpaca refuses
    /// still downgrades to IEX once, exactly as at startup.
    /// </summary>
    public void SetTier(string tier)
    {
        var t = (tier ?? "").Trim().ToLowerInvariant();
        if (t is not ("sip" or "iex")) throw new ArgumentException($"Alpaca's stock data tiers are sip and iex, not '{tier}'.", nameof(tier));
        lock (feedTierLock) feedTier = t;
    }

    private bool TryDowngradeTier()
    {
        lock (feedTierLock)
        {
            if (feedTier == "iex") return false;
            feedTier = "iex";
            return true;
        }
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
