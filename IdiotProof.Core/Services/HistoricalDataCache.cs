// ============================================================================
// Historical Data Cache - Saves/Loads IBKR Historical Data to JSON Files
// ============================================================================
//
// PURPOSE:
// Caches historical data in /History/{SYMBOL}.json files to avoid expensive
// IBKR API calls on every backtest/optimization run. Data is fetched once
// and reused until manually refreshed or expired.
//
// FILE STRUCTURE:
// /History/
//   NVDA.json  - 30 days of 1-minute bars for NVDA
//   AAPL.json  - 30 days of 1-minute bars for AAPL
//   META.json  - ...
//
// USAGE:
//   var cache = new HistoricalDataCache();
//   var bars = await cache.GetOrFetchAsync(symbol, histService);
//
// ============================================================================

using IdiotProof.Backend.Enums;
using IdiotProof.Backend.Models;
using IdiotProof.Shared.Constants;
using IdiotProof.Shared.Settings;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace IdiotProof.Backend.Services;

/// <summary>
/// Caches historical bar data in JSON files to avoid repeated IBKR API calls.
/// </summary>
public sealed class HistoricalDataCache
{
    private static readonly string HistoryDir = SettingsManager.GetHistoryFolder();
    
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Number of days of historical data to fetch (default: 30).
    /// </summary>
    public int DaysToFetch { get; set; } = 30;

    /// <summary>
    /// Whether to automatically refresh data if it's older than the specified age.
    /// </summary>
    public TimeSpan? AutoRefreshAge { get; set; } = null; // No auto-refresh by default

    /// <summary>
    /// Event raised when cache is hit (data loaded from file).
    /// </summary>
    public event Action<string, int>? OnCacheHit;

    /// <summary>
    /// Event raised when cache is missed (data fetched from API).
    /// </summary>
    public event Action<string>? OnCacheMiss;

    public HistoricalDataCache()
    {
        // Ensure History directory exists
        if (!Directory.Exists(HistoryDir))
        {
            Directory.CreateDirectory(HistoryDir);
        }
    }

    /// <summary>
    /// Gets the path to the cache file for a symbol.
    /// </summary>
    public static string GetCacheFilePath(string symbol)
    {
        return Path.Combine(HistoryDir, $"{symbol.ToUpperInvariant()}.json");
    }

    /// <summary>
    /// Checks if cached data exists for a symbol.
    /// </summary>
    public bool HasCachedData(string symbol)
    {
        return File.Exists(GetCacheFilePath(symbol));
    }

    /// <summary>
    /// Gets cached data info without loading the full data.
    /// </summary>
    public CachedDataInfo? GetCacheInfo(string symbol)
    {
        var path = GetCacheFilePath(symbol);
        if (!File.Exists(path))
            return null;

        try
        {
            var fileInfo = new FileInfo(path);
            var json = File.ReadAllText(path);
            var data = JsonSerializer.Deserialize<CachedHistoricalData>(json, JsonOptions);
            
            return new CachedDataInfo
            {
                Symbol = symbol,
                BarCount = data?.Bars?.Count ?? 0,
                FetchedAt = data?.FetchedAtUtc ?? DateTime.MinValue,
                FileSize = fileInfo.Length,
                FirstBar = data?.Bars?.FirstOrDefault()?.Time,
                LastBar = data?.Bars?.LastOrDefault()?.Time
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Loads cached data from disk if available.
    /// </summary>
    /// <param name="symbol">The ticker symbol.</param>
    /// <returns>List of historical bars, or null if not cached.</returns>
    public List<HistoricalBar>? LoadFromCache(string symbol)
    {
        var path = GetCacheFilePath(symbol);
        if (!File.Exists(path))
            return null;

        try
        {
            var json = File.ReadAllText(path);
            var data = JsonSerializer.Deserialize<CachedHistoricalData>(json, JsonOptions);

            if (data?.Bars == null || data.Bars.Count == 0)
                return null;

            // Check if auto-refresh is needed
            if (AutoRefreshAge.HasValue)
            {
                var age = DateTime.UtcNow - data.FetchedAtUtc;
                if (age > AutoRefreshAge.Value)
                {
                    Log($"[{symbol}] Cache expired (age: {age.TotalHours:F1}h > {AutoRefreshAge.Value.TotalHours:F1}h)");
                    return null;
                }
            }

            OnCacheHit?.Invoke(symbol, data.Bars.Count);
            Log($"[{symbol}] Loaded {data.Bars.Count} bars from cache (fetched: {data.FetchedAtUtc:yyyy-MM-dd HH:mm})");
            
            return data.Bars;
        }
        catch (Exception ex)
        {
            Log($"[{symbol}] Error loading cache: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Saves historical data to disk cache.
    /// </summary>
    /// <param name="symbol">The ticker symbol.</param>
    /// <param name="bars">The bars to cache.</param>
    public void SaveToCache(string symbol, List<HistoricalBar> bars)
    {
        var path = GetCacheFilePath(symbol);

        try
        {
            var data = new CachedHistoricalData
            {
                Symbol = symbol.ToUpperInvariant(),
                FetchedAtUtc = DateTime.UtcNow,
                BarCount = bars.Count,
                FirstBarTime = bars.FirstOrDefault()?.Time,
                LastBarTime = bars.LastOrDefault()?.Time,
                Bars = bars
            };

            var json = JsonSerializer.Serialize(data, JsonOptions);
            File.WriteAllText(path, json);

            var fileInfo = new FileInfo(path);
            Log($"[{symbol}] Saved {bars.Count} bars to cache ({fileInfo.Length / 1024.0:F1} KB)");
        }
        catch (Exception ex)
        {
            Log($"[{symbol}] Error saving cache: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets historical data from cache or fetches from API if not available.
    /// </summary>
    /// <param name="symbol">The ticker symbol.</param>
    /// <param name="histService">Historical data service for API calls.</param>
    /// <param name="forceRefresh">Force re-fetch even if cached.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of historical bars.</returns>
    public async Task<List<HistoricalBar>> GetOrFetchAsync(
        string symbol,
        HistoricalDataService histService,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        if (histService == null)
            throw new ArgumentNullException(nameof(histService));

        // Try loading from cache first (unless force refresh)
        if (!forceRefresh)
        {
            var cached = LoadFromCache(symbol);
            if (cached != null && cached.Count > 0)
            {
                return cached;
            }
        }

        // Cache miss - fetch from API
        OnCacheMiss?.Invoke(symbol);
        Log($"[{symbol}] Cache miss - fetching {DaysToFetch} days from IBKR API...");

        // Calculate bars needed: 16 hours/day * 60 min * DaysToFetch
        // But IBKR limits 1-min bars to max 1 day per request
        // We need to fetch day by day for multi-day data
        int barsPerDay = 960; // 16 hours * 60 minutes (4 AM to 8 PM)
        int totalBars = barsPerDay * DaysToFetch;

        // For longer durations, use multi-day fetch
        await histService.FetchHistoricalDataAsync(
            symbol,
            totalBars,
            BarSize.Minutes1,
            HistoricalDataType.Trades,
            useRTH: false,
            cancellationToken: cancellationToken);

        var bars = histService.Store.GetBars(symbol);

        if (bars.Count > 0)
        {
            // Save to cache for next time
            SaveToCache(symbol, bars.ToList());
        }

        return bars.ToList();
    }

    /// <summary>
    /// Gets historical data for a specific date range from cache.
    /// </summary>
    /// <param name="symbol">The ticker symbol.</param>
    /// <param name="startDate">Start date (inclusive).</param>
    /// <param name="endDate">End date (inclusive).</param>
    /// <returns>Filtered list of bars, or null if not cached.</returns>
    public List<HistoricalBar>? GetDateRange(string symbol, DateOnly startDate, DateOnly endDate)
    {
        var allBars = LoadFromCache(symbol);
        if (allBars == null)
            return null;

        return allBars
            .Where(b => DateOnly.FromDateTime(b.Time) >= startDate && DateOnly.FromDateTime(b.Time) <= endDate)
            .OrderBy(b => b.Time)
            .ToList();
    }

    /// <summary>
    /// Gets all available dates in the cache for a symbol.
    /// </summary>
    public List<DateOnly>? GetAvailableDates(string symbol)
    {
        var allBars = LoadFromCache(symbol);
        if (allBars == null)
            return null;

        return allBars
            .Select(b => DateOnly.FromDateTime(b.Time))
            .Distinct()
            .OrderBy(d => d)
            .ToList();
    }

    /// <summary>
    /// Deletes cached data for a symbol.
    /// </summary>
    public void ClearCache(string symbol)
    {
        var path = GetCacheFilePath(symbol);
        if (File.Exists(path))
        {
            File.Delete(path);
            Log($"[{symbol}] Cache cleared");
        }
    }

    /// <summary>
    /// Deletes all cached data.
    /// </summary>
    public void ClearAllCache()
    {
        if (Directory.Exists(HistoryDir))
        {
            foreach (var file in Directory.GetFiles(HistoryDir, "*.json"))
            {
                File.Delete(file);
            }
            Log("All cache cleared");
        }
    }

    /// <summary>
    /// Lists all cached symbols with info.
    /// </summary>
    public List<CachedDataInfo> ListCachedSymbols()
    {
        var results = new List<CachedDataInfo>();

        if (!Directory.Exists(HistoryDir))
            return results;

        foreach (var file in Directory.GetFiles(HistoryDir, "*.json"))
        {
            var symbol = Path.GetFileNameWithoutExtension(file);
            var info = GetCacheInfo(symbol);
            if (info != null)
            {
                results.Add(info);
            }
        }

        return results.OrderBy(i => i.Symbol).ToList();
    }

    private static void Log(string message)
    {
        Console.WriteLine($"[HistoryCache] {message}");
    }
}

/// <summary>
/// Cached historical data structure for JSON serialization.
/// </summary>
public sealed class CachedHistoricalData
{
    public string Symbol { get; set; } = "";
    public DateTime FetchedAtUtc { get; set; }
    public int BarCount { get; set; }
    public DateTime? FirstBarTime { get; set; }
    public DateTime? LastBarTime { get; set; }
    public List<HistoricalBar> Bars { get; set; } = [];
}

/// <summary>
/// Info about cached data without loading full bar list.
/// </summary>
public sealed class CachedDataInfo
{
    public string Symbol { get; set; } = "";
    public int BarCount { get; set; }
    public DateTime FetchedAt { get; set; }
    public long FileSize { get; set; }
    public DateTime? FirstBar { get; set; }
    public DateTime? LastBar { get; set; }

    public string FileSizeFormatted => FileSize switch
    {
        < 1024 => $"{FileSize} B",
        < 1024 * 1024 => $"{FileSize / 1024.0:F1} KB",
        _ => $"{FileSize / (1024.0 * 1024.0):F1} MB"
    };

    public int DaysCovered => FirstBar.HasValue && LastBar.HasValue
        ? (int)(LastBar.Value - FirstBar.Value).TotalDays + 1
        : 0;

    public override string ToString()
    {
        return $"{Symbol}: {BarCount} bars, {DaysCovered} days ({FirstBar:yyyy-MM-dd} to {LastBar:yyyy-MM-dd}), fetched {FetchedAt:yyyy-MM-dd HH:mm}, {FileSizeFormatted}";
    }
}
