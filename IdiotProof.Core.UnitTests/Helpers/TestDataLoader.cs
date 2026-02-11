// ============================================================================
// Test Data Loader - Loads real historical bar data for unit tests
// ============================================================================

using System.Text.Json;
using System.Text.Json.Serialization;

namespace IdiotProof.Core.UnitTests.Helpers;

/// <summary>
/// A single bar from the JSON history file.
/// </summary>
public sealed record TestBar
{
    [JsonPropertyName("time")]
    public DateTime Time { get; init; }

    [JsonPropertyName("open")]
    public double Open { get; init; }

    [JsonPropertyName("high")]
    public double High { get; init; }

    [JsonPropertyName("low")]
    public double Low { get; init; }

    [JsonPropertyName("close")]
    public double Close { get; init; }

    [JsonPropertyName("volume")]
    public long Volume { get; init; }

    [JsonPropertyName("vwap")]
    public double? Vwap { get; init; }

    [JsonPropertyName("tradeCount")]
    public int? TradeCount { get; init; }
}

/// <summary>
/// Root JSON object for history files.
/// </summary>
public sealed record TestHistoryFile
{
    [JsonPropertyName("symbol")]
    public string Symbol { get; init; } = "";

    [JsonPropertyName("barCount")]
    public int BarCount { get; init; }

    [JsonPropertyName("bars")]
    public List<TestBar> Bars { get; init; } = new();
}

/// <summary>
/// Loads real OHLCV bar data from JSON files in the Data folder.
/// </summary>
public static class TestDataLoader
{
    private static readonly Dictionary<string, TestHistoryFile> _cache = new();
    private static readonly object _lock = new();

    /// <summary>
    /// Gets the path to the Data folder containing history JSON files.
    /// </summary>
    private static string GetDataFolder()
    {
        // Walk up from the test output directory to find the solution root
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            var dataPath = Path.Combine(dir, "IdiotProof.Core", "Data");
            if (Directory.Exists(dataPath))
                return dataPath;
            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new DirectoryNotFoundException(
            "Could not find IdiotProof.Core/Data folder. " +
            "Ensure the test is running from within the solution directory.");
    }

    /// <summary>
    /// Loads history bars for a given symbol (e.g., "NVDA", "CCHH").
    /// Results are cached for performance.
    /// </summary>
    public static List<TestBar> LoadBars(string symbol)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(symbol, out var cached))
                return cached.Bars;

            var filePath = Path.Combine(GetDataFolder(), symbol, $"{symbol}.history.json");
            // Fallback to flat structure for backwards compatibility
            if (!File.Exists(filePath))
                filePath = Path.Combine(GetDataFolder(), $"{symbol}.history.json");
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"History file not found: {filePath}");

            var json = File.ReadAllText(filePath);
            var history = JsonSerializer.Deserialize<TestHistoryFile>(json)
                ?? throw new InvalidOperationException($"Failed to deserialize {filePath}");

            _cache[symbol] = history;
            return history.Bars;
        }
    }

    /// <summary>
    /// Loads a subset of bars - first N bars from a symbol.
    /// Useful for warm-up tests where you need a specific count.
    /// </summary>
    public static List<TestBar> LoadBars(string symbol, int count)
    {
        var all = LoadBars(symbol);
        return all.Take(count).ToList();
    }

    /// <summary>
    /// Loads bars for a specific date from a symbol's history.
    /// </summary>
    public static List<TestBar> LoadBarsForDate(string symbol, DateTime date)
    {
        var all = LoadBars(symbol);
        return all.Where(b => b.Time.Date == date.Date).ToList();
    }

    /// <summary>
    /// Loads bars within a date range.
    /// </summary>
    public static List<TestBar> LoadBarsInRange(string symbol, DateTime from, DateTime to)
    {
        var all = LoadBars(symbol);
        return all.Where(b => b.Time >= from && b.Time <= to).ToList();
    }

    /// <summary>
    /// Gets all available symbols that have history data.
    /// </summary>
    public static List<string> GetAvailableSymbols()
    {
        var dataFolder = GetDataFolder();
        // Search in per-ticker subfolders
        var symbols = new List<string>();
        foreach (var dir in Directory.GetDirectories(dataFolder))
        {
            var histFile = Path.Combine(dir, Path.GetFileName(dir) + ".history.json");
            if (File.Exists(histFile))
                symbols.Add(Path.GetFileName(dir));
        }
        // Also check flat structure for backwards compatibility
        symbols.AddRange(
            Directory.GetFiles(dataFolder, "*.history.json")
                .Select(f => Path.GetFileNameWithoutExtension(f).Replace(".history", ""))
        );
        return symbols.Distinct().ToList();
    }
}
