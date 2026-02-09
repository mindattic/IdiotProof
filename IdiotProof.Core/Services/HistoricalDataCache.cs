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

using IdiotProof.Enums;
using IdiotProof.Logging;
using IdiotProof.Models;
using IdiotProof.Constants;
using IdiotProof.Settings;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace IdiotProof.Services;

/// <summary>
/// Caches historical bar data in JSON files to avoid repeated IBKR API calls.
/// </summary>
public sealed class HistoricalDataCache
{
    private static readonly string DataDir = SettingsManager.GetDataFolder();
    
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
    /// Maximum concurrent API requests when fetching multiple days.
    /// Keep low to avoid overwhelming IBKR pacing limits.
    /// </summary>
    public int MaxConcurrentFetches { get; set; } = 3;

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
        if (!Directory.Exists(DataDir))
        {
            Directory.CreateDirectory(DataDir);
        }
    }

    /// <summary>
    /// Gets the path to the cache file for a symbol.
    /// </summary>
    public static string GetCacheFilePath(string symbol)
    {
        return Path.Combine(DataDir, $"{symbol.ToUpperInvariant()}.history.json");
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
    /// Fetches day-by-day in parallel to avoid timeout issues with large requests.
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

        // Cache miss - fetch from API day by day
        OnCacheMiss?.Invoke(symbol);
        Log($"[{symbol}] Cache miss - fetching {DaysToFetch} days from IBKR API (day-by-day)...");

        // Build list of dates to fetch (going backwards from today)
        var datesToFetch = new List<DateTime>();
        var today = DateTime.Today;
        for (int i = 0; i < DaysToFetch; i++)
        {
            var date = today.AddDays(-i);
            // Skip weekends (Saturday = 6, Sunday = 0)
            if (date.DayOfWeek != DayOfWeek.Saturday && date.DayOfWeek != DayOfWeek.Sunday)
            {
                // End of trading day (8 PM Eastern)
                datesToFetch.Add(date.AddHours(20));
            }
        }
        
        Log($"[{symbol}] Fetching {datesToFetch.Count} trading days in parallel (max {MaxConcurrentFetches} concurrent)...");

        // Fetch each day in parallel with limited concurrency
        var semaphore = new SemaphoreSlim(MaxConcurrentFetches);
        var allBars = new System.Collections.Concurrent.ConcurrentBag<(DateTime Date, List<HistoricalBar> Bars)>();
        int completedDays = 0;
        
        var tasks = datesToFetch.Select(async endDate =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                // Fetch 1 day of data (960 bars = 16 hours * 60 minutes)
                await histService.FetchHistoricalDataAsync(
                    symbol,
                    960,
                    BarSize.Minutes1,
                    HistoricalDataType.Trades,
                    useRTH: false,
                    endDate: endDate,
                    cancellationToken: cancellationToken);

                // Get the bars from the store
                var bars = histService.Store.GetBars(symbol).ToList();
                
                // Filter to only bars from this specific day
                var dayBars = bars
                    .Where(b => b.Time.Date == endDate.Date)
                    .ToList();
                
                if (dayBars.Count > 0)
                {
                    allBars.Add((endDate.Date, dayBars));
                }
                
                var done = Interlocked.Increment(ref completedDays);
                Log($"[{symbol}] Day {done}/{datesToFetch.Count}: {endDate:yyyy-MM-dd} - {dayBars.Count} bars");
            }
            finally
            {
                semaphore.Release();
            }
        }).ToList();

        await Task.WhenAll(tasks);

        // Combine all bars in chronological order
        var combinedBars = allBars
            .OrderBy(x => x.Date)
            .SelectMany(x => x.Bars)
            .OrderBy(b => b.Time)
            .ToList();

        Log($"[{symbol}] Total: {combinedBars.Count} bars from {datesToFetch.Count} trading days");

        if (combinedBars.Count > 0)
        {
            // Save to cache for next time
            SaveToCache(symbol, combinedBars);
        }

        return combinedBars;
    }

    /// <summary>
    /// Gets cached data and fetches any missing days incrementally.
    /// This allows the cache to grow over time beyond the initial DaysToFetch.
    /// </summary>
    /// <param name="symbol">The ticker symbol.</param>
    /// <param name="histService">Historical data service for API calls.</param>
    /// <param name="daysBack">How many days back to check for missing data (default: 30).</param>
    /// <param name="maxDaysToFetch">Maximum new days to fetch per call to avoid overwhelming API (default: 5).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of historical bars including any newly fetched data.</returns>
    public async Task<List<HistoricalBar>> GetOrFetchIncrementalAsync(
        string symbol,
        HistoricalDataService histService,
        int daysBack = 30,
        int maxDaysToFetch = 5,
        CancellationToken cancellationToken = default)
    {
        if (histService == null)
            throw new ArgumentNullException(nameof(histService));

        // Load existing cached data
        var existingBars = LoadFromCache(symbol) ?? new List<HistoricalBar>();
        
        // Find what dates we already have
        var existingDates = existingBars
            .Select(b => b.Time.Date)
            .Distinct()
            .ToHashSet();
        
        Log($"[{symbol}] Existing cache has {existingBars.Count} bars covering {existingDates.Count} trading days");

        // Build list of all trading days we want (going backwards from today)
        var today = DateTime.Today;
        var desiredDates = new List<DateTime>();
        for (int i = 0; i < daysBack; i++)
        {
            var date = today.AddDays(-i);
            // Skip weekends
            if (date.DayOfWeek != DayOfWeek.Saturday && date.DayOfWeek != DayOfWeek.Sunday)
            {
                desiredDates.Add(date);
            }
        }
        
        // Find missing dates
        var missingDates = desiredDates
            .Where(d => !existingDates.Contains(d))
            .OrderByDescending(d => d) // Most recent first
            .Take(maxDaysToFetch)      // Limit to avoid overwhelming API
            .ToList();
        
        if (missingDates.Count == 0)
        {
            Log($"[{symbol}] No missing days - cache is up to date");
            return existingBars;
        }
        
        Log($"[{symbol}] Fetching {missingDates.Count} missing days: {string.Join(", ", missingDates.Select(d => d.ToString("MM/dd")))}");

        // Fetch missing days in parallel
        var semaphore = new SemaphoreSlim(MaxConcurrentFetches);
        var newBars = new System.Collections.Concurrent.ConcurrentBag<HistoricalBar>();
        int completedDays = 0;
        
        var tasks = missingDates.Select(async date =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                var endDate = date.AddHours(20); // 8 PM Eastern
                
                await histService.FetchHistoricalDataAsync(
                    symbol,
                    960, // 16 hours * 60 minutes
                    BarSize.Minutes1,
                    HistoricalDataType.Trades,
                    useRTH: false,
                    endDate: endDate,
                    cancellationToken: cancellationToken);

                var bars = histService.Store.GetBars(symbol).ToList();
                var dayBars = bars.Where(b => b.Time.Date == date).ToList();
                
                foreach (var bar in dayBars)
                {
                    newBars.Add(bar);
                }
                
                var done = Interlocked.Increment(ref completedDays);
                Log($"[{symbol}] Incremental {done}/{missingDates.Count}: {date:yyyy-MM-dd} - {dayBars.Count} bars");
            }
            finally
            {
                semaphore.Release();
            }
        }).ToList();

        await Task.WhenAll(tasks);

        // Merge existing and new bars
        var allBars = existingBars
            .Concat(newBars)
            .OrderBy(b => b.Time)
            .ToList();
        
        // Remove duplicates (same timestamp)
        allBars = allBars
            .GroupBy(b => b.Time)
            .Select(g => g.First())
            .OrderBy(b => b.Time)
            .ToList();

        Log($"[{symbol}] Total after merge: {allBars.Count} bars covering {allBars.Select(b => b.Time.Date).Distinct().Count()} trading days");

        // Save merged data
        if (newBars.Count > 0)
        {
            SaveToCache(symbol, allBars);
        }

        return allBars;
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
        if (Directory.Exists(DataDir))
        {
            foreach (var file in Directory.GetFiles(DataDir, "*.history.json"))
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

        if (!Directory.Exists(DataDir))
            return results;

        foreach (var file in Directory.GetFiles(DataDir, "*.history.json"))
        {
            // Extract symbol from SYMBOL.history.json
            var fileName = Path.GetFileNameWithoutExtension(file); // SYMBOL.history
            var symbol = fileName.Replace(".history", "");
            var info = GetCacheInfo(symbol);
            if (info != null)
            {
                results.Add(info);
            }
        }

        return results.OrderBy(i => i.Symbol).ToList();
    }

    /// <summary>
    /// Incrementally updates cached data by only fetching missing days.
    /// If cache exists with data through Feb 5, and today is Feb 7, 
    /// it only fetches Feb 6-7 and merges with cached data.
    /// </summary>
    /// <param name="symbol">The ticker symbol.</param>
    /// <param name="histService">Historical data service for API calls.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of new bars fetched (0 if cache was up-to-date).</returns>
    public async Task<int> IncrementalUpdateAsync(
        string symbol,
        HistoricalDataService histService,
        CancellationToken cancellationToken = default)
    {
        if (histService == null)
            throw new ArgumentNullException(nameof(histService));

        // Load existing cache
        var cached = LoadFromCache(symbol);
        
        if (cached == null || cached.Count == 0)
        {
            // No cache - do full fetch
            Log($"[{symbol}] No cache found - doing full fetch");
            var fetched = await GetOrFetchAsync(symbol, histService, forceRefresh: false, cancellationToken);
            return fetched.Count;
        }

        // Find the last bar date in cache
        var lastBarTime = cached.Max(b => b.Time);
        var lastBarDate = DateOnly.FromDateTime(lastBarTime);
        var today = DateOnly.FromDateTime(DateTime.Now);

        // Check if cache is already up-to-date (last bar is from today)
        if (lastBarDate >= today)
        {
            Log($"[{symbol}] Cache is up-to-date (last bar: {lastBarTime:yyyy-MM-dd HH:mm})");
            return 0;
        }

        // Calculate days missing
        int daysMissing = today.DayNumber - lastBarDate.DayNumber;
        Log($"[{symbol}] Cache has data through {lastBarDate}, missing {daysMissing} day(s)");

        // Fetch only the missing days (add 1 day buffer for overlap handling)
        int barsPerDay = 960; // 16 hours * 60 minutes
        int barsToFetch = barsPerDay * (daysMissing + 1);

        // Fetch from API - the end date is now, and we fetch enough bars to cover missing days
        await histService.FetchHistoricalDataAsync(
            symbol,
            barsToFetch,
            BarSize.Minutes1,
            HistoricalDataType.Trades,
            useRTH: false,
            cancellationToken: cancellationToken);

        var newBars = histService.Store.GetBars(symbol);
        if (newBars.Count == 0)
        {
            Log($"[{symbol}] No new bars fetched");
            return 0;
        }

        // Filter to only bars AFTER our last cached bar (avoid duplicates)
        var trulyNewBars = newBars.Where(b => b.Time > lastBarTime).ToList();

        if (trulyNewBars.Count == 0)
        {
            Log($"[{symbol}] No new bars after {lastBarTime:yyyy-MM-dd HH:mm}");
            return 0;
        }

        Log($"[{symbol}] Adding {trulyNewBars.Count} new bars (from {trulyNewBars.Min(b => b.Time):yyyy-MM-dd HH:mm} to {trulyNewBars.Max(b => b.Time):yyyy-MM-dd HH:mm})");

        // Merge new bars with cached bars
        var merged = cached.Concat(trulyNewBars)
            .OrderBy(b => b.Time)
            .ToList();

        // Trim to keep only the most recent DaysToFetch days worth of data
        var cutoffDate = DateTime.Now.AddDays(-DaysToFetch);
        var trimmed = merged.Where(b => b.Time >= cutoffDate).ToList();

        // Save merged data
        SaveToCache(symbol, trimmed);

        Log($"[{symbol}] Updated cache: {trimmed.Count} total bars ({trulyNewBars.Count} new)");
        return trulyNewBars.Count;
    }

    /// <summary>
    /// Gets historical data, using incremental update strategy.
    /// - If no cache: fetches full 30 days
    /// - If cache exists: only fetches missing days and merges
    /// This minimizes API calls.
    /// </summary>
    /// <param name="symbol">The ticker symbol.</param>
    /// <param name="histService">Historical data service for API calls.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of historical bars (up-to-date).</returns>
    public async Task<List<HistoricalBar>> GetWithIncrementalUpdateAsync(
        string symbol,
        HistoricalDataService histService,
        CancellationToken cancellationToken = default)
    {
        // First, do incremental update to ensure cache is current
        await IncrementalUpdateAsync(symbol, histService, cancellationToken);

        // Now load from cache (guaranteed to be up-to-date)
        var cached = LoadFromCache(symbol);
        return cached ?? [];
    }

    private static void Log(string message)
    {
        ConsoleLog.HistoryCache(message);
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

/// <summary>
/// Summary of a single trading day's price action.
/// </summary>
public sealed class DaySummary
{
    public string Symbol { get; set; } = "";
    public DateTime Date { get; set; }
    public double Open { get; set; }
    public double High { get; set; }  // HOD
    public double Low { get; set; }   // LOD
    public double Close { get; set; }
    public long Volume { get; set; }
    
    /// <summary>Range (HOD - LOD)</summary>
    public double Range => High - Low;
    
    /// <summary>Range as percentage of close</summary>
    public double RangePercent => Close > 0 ? (Range / Close) * 100 : 0;

    public override string ToString() => $"{Symbol} {Date:yyyy-MM-dd}: O={Open:F2} H={High:F2} L={Low:F2} C={Close:F2}";
}

/// <summary>
/// Static helpers for getting previous day data.
/// </summary>
public static class HistoricalDataHelper
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Gets the previous trading day's summary (HOD, LOD, Close) from cached history.
    /// </summary>
    /// <param name="symbol">Ticker symbol.</param>
    /// <param name="referenceDate">Date to look back from (default: today).</param>
    /// <returns>DaySummary or null if no data.</returns>
    public static DaySummary? GetPreviousDaySummary(string symbol, DateTime? referenceDate = null)
    {
        var path = HistoricalDataCache.GetCacheFilePath(symbol);
        if (!File.Exists(path))
            return null;

        try
        {
            var json = File.ReadAllText(path);
            var data = JsonSerializer.Deserialize<CachedHistoricalData>(json, JsonOptions);

            if (data?.Bars == null || data.Bars.Count == 0)
                return null;

            var refDate = (referenceDate ?? DateTime.Now).Date;
            
            // Find the most recent complete trading day before reference date
            var previousDayBars = data.Bars
                .Where(b => b.Time.Date < refDate)
                .GroupBy(b => b.Time.Date)
                .OrderByDescending(g => g.Key)
                .FirstOrDefault();

            if (previousDayBars == null)
                return null;

            var bars = previousDayBars.ToList();
            return new DaySummary
            {
                Symbol = symbol.ToUpperInvariant(),
                Date = previousDayBars.Key,
                Open = bars.First().Open,
                High = bars.Max(b => b.High),
                Low = bars.Min(b => b.Low),
                Close = bars.Last().Close,
                Volume = bars.Sum(b => b.Volume)
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Gets previous day summaries for multiple symbols.
    /// </summary>
    public static Dictionary<string, DaySummary> GetPreviousDaySummaries(IEnumerable<string> symbols, DateTime? referenceDate = null)
    {
        var result = new Dictionary<string, DaySummary>(StringComparer.OrdinalIgnoreCase);
        
        foreach (var symbol in symbols)
        {
            var summary = GetPreviousDaySummary(symbol, referenceDate);
            if (summary != null)
            {
                result[symbol] = summary;
            }
        }

        return result;
    }
}
