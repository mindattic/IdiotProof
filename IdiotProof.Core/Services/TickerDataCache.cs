// ============================================================================
// TickerDataCache - In-memory cache for ticker price data
// ============================================================================

using IdiotProof.Constants;
using IdiotProof.Helpers;
using IdiotProof.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IdiotProof.Services;

/// <summary>
/// Cached data for a single ticker.
/// </summary>
public sealed class TickerCacheEntry
{
    public string Symbol { get; set; } = "";
    public double Price { get; set; }
    public double Lod { get; set; }
    public double Hod { get; set; }
    public double PrevClose { get; set; }
    public DateTime LastUpdated { get; set; }
}

/// <summary>
/// In-memory cache for ticker price data.
/// </summary>
public static class TickerDataCache
{
    private static readonly ConcurrentDictionary<string, TickerCacheEntry> cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets cached data for a ticker. Returns null if not cached.
    /// </summary>
    public static TickerCacheEntry? Get(string symbol)
    {
        return cache.TryGetValue(symbol, out var entry) ? entry : null;
    }

    /// <summary>
    /// Gets all cached entries.
    /// </summary>
    public static IReadOnlyDictionary<string, TickerCacheEntry> GetAll() => cache;

    /// <summary>
    /// Refreshes the cache for specified symbols.
    /// </summary>
    public static async Task RefreshAsync(
        IEnumerable<string> symbols,
        Func<string, double>? priceProvider = null,
        CancellationToken ct = default)
    {
        var symbolList = symbols.ToList();
        if (symbolList.Count == 0) return;

        ConsoleLog.Write("TickerCache", $"Refreshing {symbolList.Count} ticker(s)...");

        var prevDaySummaries = HistoricalDataHelper.GetPreviousDaySummaries(symbolList);

        foreach (var symbol in symbolList)
        {
            try
            {
                var price = priceProvider?.Invoke(symbol) ?? 0;
                prevDaySummaries.TryGetValue(symbol, out var prevDay);

                if (price <= 0 && prevDay != null)
                    price = prevDay.Close;

                cache[symbol] = new TickerCacheEntry
                {
                    Symbol = symbol.ToUpperInvariant(),
                    Price = price,
                    Lod = prevDay?.Low ?? 0,
                    Hod = prevDay?.High ?? 0,
                    PrevClose = prevDay?.Close ?? 0,
                    LastUpdated = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                ConsoleLog.Warn("TickerCache", $"Failed to fetch {symbol}: {ex.Message}");
            }
        }

        ConsoleLog.Write("TickerCache", $"Refreshed {symbolList.Count} ticker(s)");
        await Task.CompletedTask;
    }

    /// <summary>
    /// Updates a single ticker's price.
    /// </summary>
    public static void UpdatePrice(string symbol, double price)
    {
        if (cache.TryGetValue(symbol, out var entry))
        {
            entry.Price = price;
            entry.LastUpdated = DateTime.UtcNow;
        }
        else
        {
            cache[symbol] = new TickerCacheEntry
            {
                Symbol = symbol.ToUpperInvariant(),
                Price = price,
                LastUpdated = DateTime.UtcNow
            };
        }
    }

    /// <summary>
    /// Removes a ticker from the cache.
    /// </summary>
    public static void Remove(string symbol)
    {
        cache.TryRemove(symbol, out var _removed);
    }

    /// <summary>
    /// Clears the entire cache.
    /// </summary>
    public static void Clear()
    {
        cache.Clear();
    }

    /// <summary>
    /// Displays the ticker table.
    /// </summary>
    public static void PrintTable(Func<string, double>? priceProvider = null)
    {
        WatchlistManager.PrintSummaryWithPrices(priceProvider);
    }
}
