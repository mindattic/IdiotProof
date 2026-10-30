using System.Text.Json;
using IdiotProof.Blazor.Data;
using IdiotProof.Engine.Settings;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Blazor.Services;

/// <summary>
/// Maintains the cached universe of tradable NASDAQ/NYSE equities that the
/// autonomous market-event scanner screens against — e.g. "which tickers are
/// near a market-value listing threshold" — without hitting Alpaca once per
/// ticker on every scan. Refreshed on a staleness timer (<see cref="RefreshIfStaleAsync"/>);
/// callers just read <see cref="TrackedTicker"/> rows out of the cache.
/// </summary>
public sealed class TickerUniverseService(
    IHttpClientFactory httpFactory,
    AppSettings settings,
    IDbContextFactory<AppDbContext> dbFactory,
    ILogger<TickerUniverseService> logger)
{
    private const string DataBase = "https://data.alpaca.markets/";
    private const int PriceBatchSize = 200;
    private const int SaveChunkSize = 500;

    /// <summary>
    /// Refreshes the ticker universe (asset list + latest prices) if the newest
    /// <see cref="TrackedTicker.LastRefreshedUtc"/> on file is older than
    /// <paramref name="maxAge"/>, or if the table is empty. No-ops otherwise.
    /// </summary>
    public async Task RefreshIfStaleAsync(TimeSpan maxAge, CancellationToken ct = default)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var newest = await db.TrackedTickers
                .OrderByDescending(t => t.LastRefreshedUtc)
                .Select(t => (DateTime?)t.LastRefreshedUtc)
                .FirstOrDefaultAsync(ct);

            if (newest is not null && DateTime.UtcNow - newest.Value < maxAge)
            {
                logger.LogDebug("Ticker universe fresh as of {LastRefreshed}; skipping refresh", newest);
                return;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed checking ticker universe staleness; refreshing anyway");
        }

        await RefreshAsync(ct);
    }

    /// <summary>Returns every tracked ticker currently cached.</summary>
    public async Task<List<TrackedTicker>> GetUniverseAsync(CancellationToken ct = default)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            return await db.TrackedTickers.ToListAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed loading ticker universe");
            return [];
        }
    }

    /// <summary>Batch lookup of tracked tickers by symbol.</summary>
    public async Task<Dictionary<string, TrackedTicker>> GetBySymbolsAsync(
        IEnumerable<string> symbols, CancellationToken ct = default)
    {
        var wanted = symbols
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.ToUpperInvariant())
            .Distinct()
            .ToList();
        if (wanted.Count == 0) return new Dictionary<string, TrackedTicker>();

        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var rows = await db.TrackedTickers
                .Where(t => wanted.Contains(t.Symbol))
                .ToListAsync(ct);
            return rows.ToDictionary(t => t.Symbol, t => t);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed looking up tracked tickers by symbol");
            return new Dictionary<string, TrackedTicker>();
        }
    }

    // ── Refresh pipeline ─────────────────────────────────────────────────

    private async Task RefreshAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(settings.AlpacaApiKeyId)) return;

        var symbols = await UpsertAssetListAsync(ct);
        if (symbols.Count == 0) return;

        await RefreshPricesAsync(symbols, ct);
        logger.LogInformation("Ticker universe refreshed: {Count} symbols", symbols.Count);
    }

    /// <summary>
    /// Fetches the active, tradable NASDAQ/NYSE asset list from Alpaca's Trading
    /// API and upserts it into <see cref="TrackedTicker"/>. Returns the upserted
    /// symbols so the caller can batch-fetch prices for exactly those rows.
    /// </summary>
    private async Task<List<string>> UpsertAssetListAsync(CancellationToken ct)
    {
        var tradingBase = settings.AlpacaIsPaper
            ? "https://paper-api.alpaca.markets"
            : "https://api.alpaca.markets";
        var url = $"{tradingBase}/v2/assets?status=active&asset_class=us_equity";

        List<(string Symbol, string Exchange)> assets;
        try
        {
            using var client = httpFactory.CreateClient();
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("APCA-API-KEY-ID", settings.AlpacaApiKeyId);
            req.Headers.Add("APCA-API-SECRET-KEY", settings.AlpacaApiSecretKey);

            using var resp = await client.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                logger.LogWarning("Alpaca assets returned {Status}", resp.StatusCode);
                return [];
            }

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return [];

            assets = [];
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var exchange = Str(item, "exchange");
                if (exchange != "NASDAQ" && exchange != "NYSE") continue;

                var tradable = item.TryGetProperty("tradable", out var t) && t.ValueKind == JsonValueKind.True;
                if (!tradable) continue;

                var symbol = Str(item, "symbol");
                if (string.IsNullOrWhiteSpace(symbol)) continue;

                assets.Add((symbol.ToUpperInvariant(), exchange));
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Alpaca asset list fetch failed");
            return [];
        }

        if (assets.Count == 0) return [];

        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var existing = await db.TrackedTickers.ToDictionaryAsync(t => t.Symbol, t => t, ct);
            var now = DateTime.UtcNow;
            var touched = 0;

            foreach (var (symbol, exchange) in assets)
            {
                if (!existing.TryGetValue(symbol, out var row))
                {
                    row = new TrackedTicker { Symbol = symbol };
                    db.TrackedTickers.Add(row);
                    existing[symbol] = row;
                }

                row.Exchange = exchange;
                row.IsTradable = true;
                row.LastRefreshedUtc = now;
                // SharesOutstanding intentionally left null here — future enhancement is a
                // best-effort EDGAR company-facts XBRL lookup, not built in this pass.

                touched++;
                if (touched % SaveChunkSize == 0)
                    await db.SaveChangesAsync(ct);
            }

            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed upserting ticker universe");
            return [];
        }

        return assets.Select(a => a.Symbol).ToList();
    }

    /// <summary>
    /// Batch-fetches latest trade prices from Alpaca's Data API in chunks and
    /// updates <see cref="TrackedTicker.LastPrice"/>. A failed batch is logged
    /// and skipped — it never aborts the rest of the refresh.
    /// </summary>
    private async Task RefreshPricesAsync(List<string> symbols, CancellationToken ct)
    {
        for (var i = 0; i < symbols.Count; i += PriceBatchSize)
        {
            var batch = symbols.Skip(i).Take(PriceBatchSize).ToList();
            var prices = await FetchLatestPricesAsync(batch, ct);
            if (prices.Count == 0) continue;

            try
            {
                await using var db = await dbFactory.CreateDbContextAsync(ct);
                var rows = await db.TrackedTickers
                    .Where(t => batch.Contains(t.Symbol))
                    .ToListAsync(ct);

                foreach (var row in rows)
                {
                    if (prices.TryGetValue(row.Symbol, out var price))
                        row.LastPrice = price;
                }

                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed saving price batch starting at symbol {First}", batch[0]);
            }
        }
    }

    private async Task<Dictionary<string, decimal>> FetchLatestPricesAsync(
        List<string> batch, CancellationToken ct)
    {
        var symbolParam = string.Join(",", batch);
        var url = $"{DataBase}v2/stocks/trades/latest?symbols={Uri.EscapeDataString(symbolParam)}&feed=iex";

        try
        {
            using var client = httpFactory.CreateClient();
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("APCA-API-KEY-ID", settings.AlpacaApiKeyId);
            req.Headers.Add("APCA-API-SECRET-KEY", settings.AlpacaApiSecretKey);

            using var resp = await client.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                logger.LogWarning("Alpaca latest trades returned {Status} for batch starting {First}", resp.StatusCode, batch[0]);
                return new Dictionary<string, decimal>();
            }

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            if (!doc.RootElement.TryGetProperty("trades", out var trades) || trades.ValueKind != JsonValueKind.Object)
                return new Dictionary<string, decimal>();

            var result = new Dictionary<string, decimal>();
            foreach (var prop in trades.EnumerateObject())
            {
                if (prop.Value.TryGetProperty("p", out var p) && p.TryGetDecimal(out var price))
                    result[prop.Name.ToUpperInvariant()] = price;
            }

            return result;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Alpaca latest-trades fetch failed for batch starting {First}", batch[0]);
            return new Dictionary<string, decimal>();
        }
    }

    private static string Str(JsonElement e, string key, string fallback = "")
        => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? fallback
            : fallback;
}
