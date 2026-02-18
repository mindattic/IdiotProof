// ============================================================================
// Historical Data Provider - Bridges Core historical data to Web charting
// ============================================================================
// TradingView Lightweight Charts is FREE and open source:
// https://github.com/nicholasxuu/lightweight-charts
//
// NO API KEY OR SUBSCRIPTION NEEDED - we're using OUR OWN data!
// ============================================================================

using System.Text.Json;
using IdiotProof.Shared;

namespace IdiotProof.Web.Services.TradingView;

/// <summary>
/// Historical bar data matching IdiotProof.Core format.
/// </summary>
public sealed class HistoricalBar
{
    public DateTime Time { get; set; }
    public double Open { get; set; }
    public double High { get; set; }
    public double Low { get; set; }
    public double Close { get; set; }
    public long Volume { get; set; }
    public double? Vwap { get; set; }
    public int TradeCount { get; set; }
}

/// <summary>
/// Historical data file format from IdiotProof.Core.
/// </summary>
public sealed class HistoricalDataFile
{
    public string Symbol { get; set; } = "";
    public DateTime FetchedAtUtc { get; set; }
    public int BarCount { get; set; }
    public DateTime FirstBarTime { get; set; }
    public DateTime LastBarTime { get; set; }
    public List<HistoricalBar> Bars { get; set; } = [];
}

/// <summary>
/// Provides historical candle data from stored files.
/// </summary>
public sealed class HistoricalDataProvider
{
    private readonly ILogger<HistoricalDataProvider> _logger;
    private readonly string _dataBasePath;
    private readonly Dictionary<string, HistoricalDataFile> _cache = new();
    
    public HistoricalDataProvider(ILogger<HistoricalDataProvider> logger, IConfiguration config)
    {
        _logger = logger;
        
        // Default to IdiotProof.Core/Data folder
        _dataBasePath = config["HistoricalDataPath"] 
            ?? Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "IdiotProof.Core", "Data");
    }
    
    /// <summary>
    /// Gets available symbols with historical data.
    /// </summary>
    public List<string> GetAvailableSymbols()
    {
        var symbols = new List<string>();
        
        try
        {
            if (!Directory.Exists(_dataBasePath))
            {
                _logger.LogWarning("Historical data path not found: {Path}", _dataBasePath);
                return symbols;
            }
            
            foreach (var dir in Directory.GetDirectories(_dataBasePath))
            {
                var symbol = Path.GetFileName(dir);
                var historyFile = Path.Combine(dir, $"{symbol}.history.json");
                
                if (File.Exists(historyFile))
                {
                    symbols.Add(symbol);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scanning for historical data");
        }
        
        return symbols.OrderBy(s => s).ToList();
    }
    
    /// <summary>
    /// Loads historical data for a symbol.
    /// </summary>
    public async Task<HistoricalDataFile?> LoadHistoricalDataAsync(string symbol, CancellationToken ct = default)
    {
        symbol = symbol.ToUpperInvariant();
        
        // Check cache first
        if (_cache.TryGetValue(symbol, out var cached))
        {
            return cached;
        }
        
        var filePath = Path.Combine(_dataBasePath, symbol, $"{symbol}.history.json");
        
        if (!File.Exists(filePath))
        {
            _logger.LogWarning("No historical data found for {Symbol}", symbol);
            return null;
        }
        
        try
        {
            var json = await File.ReadAllTextAsync(filePath, ct);
            var data = JsonSerializer.Deserialize<HistoricalDataFile>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            
            if (data != null)
            {
                _cache[symbol] = data;
                _logger.LogInformation("Loaded {Count} bars for {Symbol} ({From} to {To})",
                    data.BarCount, symbol, data.FirstBarTime, data.LastBarTime);
            }
            
            return data;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading historical data for {Symbol}", symbol);
            return null;
        }
    }
    
    /// <summary>
    /// Gets candles for a specific date range and interval.
    /// </summary>
    public async Task<List<ChartDataPoint>> GetCandlesAsync(
        string symbol,
        DateTime? from = null,
        DateTime? to = null,
        int intervalMinutes = 1,
        CancellationToken ct = default)
    {
        var data = await LoadHistoricalDataAsync(symbol, ct);
        if (data == null) return [];
        
        var bars = data.Bars.AsEnumerable();
        
        // Filter by date range
        if (from.HasValue)
            bars = bars.Where(b => b.Time >= from.Value);
        if (to.HasValue)
            bars = bars.Where(b => b.Time <= to.Value);
        
        var filteredBars = bars.ToList();
        
        // Aggregate if needed (e.g., 5-min, 15-min candles)
        if (intervalMinutes > 1)
        {
            filteredBars = AggregateCandles(filteredBars, intervalMinutes);
        }
        
        // Convert to chart format
        return filteredBars.Select(b => new ChartDataPoint
        {
            Time = new DateTimeOffset(b.Time).ToUnixTimeSeconds(),
            Open = Math.Round(b.Open, 2),
            High = Math.Round(b.High, 2),
            Low = Math.Round(b.Low, 2),
            Close = Math.Round(b.Close, 2),
            Volume = b.Volume
        }).ToList();
    }
    
    /// <summary>
    /// Gets a single trading day's data.
    /// </summary>
    public async Task<List<ChartDataPoint>> GetDayDataAsync(
        string symbol, 
        DateTime date, 
        int intervalMinutes = 1,
        CancellationToken ct = default)
    {
        var startOfDay = date.Date;
        var endOfDay = date.Date.AddDays(1).AddSeconds(-1);
        
        return await GetCandlesAsync(symbol, startOfDay, endOfDay, intervalMinutes, ct);
    }
    
    /// <summary>
    /// Gets available trading days for a symbol.
    /// </summary>
    public async Task<List<DateTime>> GetTradingDaysAsync(string symbol, CancellationToken ct = default)
    {
        var data = await LoadHistoricalDataAsync(symbol, ct);
        if (data == null) return [];
        
        return data.Bars
            .Select(b => b.Time.Date)
            .Distinct()
            .OrderBy(d => d)
            .ToList();
    }
    
    /// <summary>
    /// Aggregates 1-minute bars into larger intervals.
    /// </summary>
    private List<HistoricalBar> AggregateCandles(List<HistoricalBar> bars, int intervalMinutes)
    {
        var result = new List<HistoricalBar>();
        
        var groups = bars
            .GroupBy(b => new DateTime(
                b.Time.Year, b.Time.Month, b.Time.Day,
                b.Time.Hour, (b.Time.Minute / intervalMinutes) * intervalMinutes, 0))
            .OrderBy(g => g.Key);
        
        foreach (var group in groups)
        {
            var groupBars = group.OrderBy(b => b.Time).ToList();
            
            result.Add(new HistoricalBar
            {
                Time = group.Key,
                Open = groupBars.First().Open,
                High = groupBars.Max(b => b.High),
                Low = groupBars.Min(b => b.Low),
                Close = groupBars.Last().Close,
                Volume = groupBars.Sum(b => b.Volume),
                TradeCount = groupBars.Sum(b => b.TradeCount)
            });
        }
        
        return result;
    }
    
    /// <summary>
    /// Clears the cache for a symbol (or all if null).
    /// </summary>
    public void ClearCache(string? symbol = null)
    {
        if (symbol != null)
        {
            _cache.Remove(symbol.ToUpperInvariant());
        }
        else
        {
            _cache.Clear();
        }
    }

    /// <summary>
    /// Gets historical bars for backtesting (returns raw HistoricalBar objects).
    /// </summary>
    public async Task<List<HistoricalBar>> GetHistoricalBarsAsync(
        string symbol,
        DateTime startDate,
        DateTime endDate,
        string interval = "1min",
        CancellationToken ct = default)
    {
        var data = await LoadHistoricalDataAsync(symbol, ct);
        if (data == null) return [];

        var bars = data.Bars
            .Where(b => b.Time >= startDate && b.Time <= endDate)
            .OrderBy(b => b.Time)
            .ToList();

        // Parse interval (e.g., "1min", "5min", "15min")
        int intervalMinutes = 1;
        if (interval.EndsWith("min"))
        {
            int.TryParse(interval.Replace("min", ""), out intervalMinutes);
            intervalMinutes = Math.Max(1, intervalMinutes);
        }

        // Aggregate if needed
        if (intervalMinutes > 1)
        {
            bars = AggregateCandles(bars, intervalMinutes);
        }

        return bars;
    }
}
