// ============================================================================
// IndicatorConfigManager - Toggle Indicators On/Off with Dynamic Weight Redistribution
// ============================================================================
//
// PURPOSE:
// JSON config file to enable/disable individual indicators and adjust their
// base weights. When an indicator is disabled, its weight is redistributed
// proportionally to the remaining enabled indicators so the total always = 100%.
//
// FILE LOCATION:
//   {SolutionRoot}\IdiotProof.Core\Data\indicator-config.json
//
// HOW IT WORKS:
//   1. Each indicator has a base weight and an enabled flag
//   2. Disabled indicators get weight = 0
//   3. Enabled indicators are normalized so their weights sum to 1.0
//   4. The resulting IndicatorWeights flows into MarketScoreCalculator
//
// EXAMPLE:
//   Disable CCI (3%), WilliamsR (3%), SMA (6%) = 12% redistributed
//   Remaining 88% base → normalized to 100%
//   MACD was 16% (0.16/0.88 = 0.1818 = 18.2%)
//   EMA was 13% (0.13/0.88 = 0.1477 = 14.8%)
//   etc.
//
// USAGE:
//   var config = IndicatorConfigManager.Load();
//   var weights = config.ToCalculatorWeights();
//   var result = MarketScoreCalculator.Calculate(snapshot, weights);
//
// ============================================================================

using IdiotProof.Calculators;
using IdiotProof.Logging;
using IdiotProof.Settings;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IdiotProof.Services;

/// <summary>
/// Configuration for a single indicator: enabled state and base weight.
/// </summary>
public sealed class IndicatorEntry
{
    /// <summary>Whether this indicator contributes to the market score.</summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>Base weight before normalization (default weights sum to 1.0).</summary>
    [JsonPropertyName("baseWeight")]
    public double BaseWeight { get; set; }

    /// <summary>Friendly name for display.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
}

/// <summary>
/// Root configuration loaded from indicator-config.json.
/// </summary>
public sealed class IndicatorConfig
{
    /// <summary>Optional description.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>Global enable/disable for the config system. When false, use hard-coded defaults.</summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>All 13 indicators keyed by their identifier.</summary>
    [JsonPropertyName("indicators")]
    public Dictionary<string, IndicatorEntry> Indicators { get; set; } = new();

    // ====================================================================
    // Indicator accessors (safe lookup with fallback to default weight)
    // ====================================================================

    private IndicatorEntry Get(string key, double defaultWeight) =>
        Indicators.TryGetValue(key, out var entry) ? entry : new IndicatorEntry { Enabled = true, BaseWeight = defaultWeight, Name = key };

    public IndicatorEntry Vwap => Get("vwap", 0.09);
    public IndicatorEntry Ema => Get("ema", 0.13);
    public IndicatorEntry Rsi => Get("rsi", 0.10);
    public IndicatorEntry Macd => Get("macd", 0.16);
    public IndicatorEntry Adx => Get("adx", 0.13);
    public IndicatorEntry Volume => Get("volume", 0.07);
    public IndicatorEntry Bollinger => Get("bollinger", 0.05);
    public IndicatorEntry Stochastic => Get("stochastic", 0.05);
    public IndicatorEntry Obv => Get("obv", 0.05);
    public IndicatorEntry Cci => Get("cci", 0.03);
    public IndicatorEntry WilliamsR => Get("williamsR", 0.03);
    public IndicatorEntry Sma => Get("sma", 0.06);
    public IndicatorEntry Momentum => Get("momentum", 0.05);

    /// <summary>Number of enabled indicators.</summary>
    [JsonIgnore]
    public int EnabledCount => Indicators.Count(kv => kv.Value.Enabled);

    /// <summary>Number of disabled indicators.</summary>
    [JsonIgnore]
    public int DisabledCount => Indicators.Count(kv => !kv.Value.Enabled);

    /// <summary>Total number of indicators.</summary>
    [JsonIgnore]
    public int TotalCount => Indicators.Count;

    /// <summary>
    /// Converts the config to Calculator IndicatorWeights with dynamic redistribution.
    /// Disabled indicators get weight 0. Enabled indicators are normalized to sum to 1.0.
    /// </summary>
    public IndicatorWeights ToCalculatorWeights()
    {
        if (!Enabled)
            return IndicatorWeights.Default;

        double vwap = Vwap.Enabled ? Vwap.BaseWeight : 0;
        double ema = Ema.Enabled ? Ema.BaseWeight : 0;
        double rsi = Rsi.Enabled ? Rsi.BaseWeight : 0;
        double macd = Macd.Enabled ? Macd.BaseWeight : 0;
        double adx = Adx.Enabled ? Adx.BaseWeight : 0;
        double volume = Volume.Enabled ? Volume.BaseWeight : 0;
        double bollinger = Bollinger.Enabled ? Bollinger.BaseWeight : 0;
        double stochastic = Stochastic.Enabled ? Stochastic.BaseWeight : 0;
        double obv = Obv.Enabled ? Obv.BaseWeight : 0;
        double cci = Cci.Enabled ? Cci.BaseWeight : 0;
        double williamsR = WilliamsR.Enabled ? WilliamsR.BaseWeight : 0;
        double sma = Sma.Enabled ? Sma.BaseWeight : 0;
        double momentum = Momentum.Enabled ? Momentum.BaseWeight : 0;

        double sum = vwap + ema + rsi + macd + adx + volume + bollinger + stochastic + obv + cci + williamsR + sma + momentum;

        // If all disabled or sum is 0, return defaults
        if (sum <= 0)
            return IndicatorWeights.Default;

        // Normalize so enabled weights sum to 1.0
        return new IndicatorWeights
        {
            Vwap = vwap / sum,
            Ema = ema / sum,
            Rsi = rsi / sum,
            Macd = macd / sum,
            Adx = adx / sum,
            Volume = volume / sum,
            Bollinger = bollinger / sum,
            Stochastic = stochastic / sum,
            Obv = obv / sum,
            Cci = cci / sum,
            WilliamsR = williamsR / sum,
            Sma = sma / sum,
            Momentum = momentum / sum
        };
    }

    /// <summary>
    /// Creates a default config with all indicators enabled at standard weights.
    /// </summary>
    public static IndicatorConfig CreateDefault()
    {
        return new IndicatorConfig
        {
            Description = "Indicator Configuration - Enable/disable indicators and adjust base weights",
            Enabled = true,
            Indicators = new Dictionary<string, IndicatorEntry>
            {
                ["vwap"]       = new() { Enabled = true, BaseWeight = 0.09, Name = "VWAP Position" },
                ["ema"]        = new() { Enabled = true, BaseWeight = 0.13, Name = "EMA Stack" },
                ["rsi"]        = new() { Enabled = true, BaseWeight = 0.10, Name = "RSI Momentum" },
                ["macd"]       = new() { Enabled = true, BaseWeight = 0.16, Name = "MACD Signal" },
                ["adx"]        = new() { Enabled = true, BaseWeight = 0.13, Name = "ADX Trend Strength" },
                ["volume"]     = new() { Enabled = true, BaseWeight = 0.07, Name = "Volume Confirmation" },
                ["bollinger"]  = new() { Enabled = true, BaseWeight = 0.05, Name = "Bollinger Bands" },
                ["stochastic"] = new() { Enabled = true, BaseWeight = 0.05, Name = "Stochastic Oscillator" },
                ["obv"]        = new() { Enabled = true, BaseWeight = 0.05, Name = "On-Balance Volume" },
                ["cci"]        = new() { Enabled = true, BaseWeight = 0.03, Name = "CCI" },
                ["williamsR"]  = new() { Enabled = true, BaseWeight = 0.03, Name = "Williams %R" },
                ["sma"]        = new() { Enabled = true, BaseWeight = 0.06, Name = "SMA Trend" },
                ["momentum"]   = new() { Enabled = true, BaseWeight = 0.05, Name = "Momentum/ROC" }
            }
        };
    }
}

/// <summary>
/// Manages the indicator-config.json file for enabling/disabling indicators
/// and dynamically redistributing weights.
/// </summary>
public static class IndicatorConfigManager
{
    private const string ConfigFileName = "indicator-config.json";

    // Cached config (loaded once, reused)
    private static IndicatorConfig? _cached;
    private static IndicatorWeights? _cachedWeights;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>
    /// Gets the path to the indicator-config.json file.
    /// </summary>
    public static string GetConfigPath()
    {
        return Path.Combine(SettingsManager.GetDataFolder(), ConfigFileName);
    }

    /// <summary>
    /// Checks if a config file exists.
    /// </summary>
    public static bool Exists() => File.Exists(GetConfigPath());

    /// <summary>
    /// Loads the indicator configuration from JSON.
    /// Creates a default file if it doesn't exist.
    /// Caches the result for performance.
    /// </summary>
    public static IndicatorConfig Load()
    {
        if (_cached != null)
            return _cached;

        var path = GetConfigPath();

        if (!File.Exists(path))
        {
            ConsoleLog.Write("Indicators", $"No indicator config found, creating default at: {path}");
            var defaultConfig = IndicatorConfig.CreateDefault();
            Save(defaultConfig);
            _cached = defaultConfig;
            return defaultConfig;
        }

        try
        {
            var json = File.ReadAllText(path);
            var config = JsonSerializer.Deserialize<IndicatorConfig>(json, JsonOptions);

            if (config == null)
            {
                ConsoleLog.Warn("Indicators", "Failed to parse indicator config, using defaults");
                _cached = IndicatorConfig.CreateDefault();
                return _cached;
            }

            _cached = config;
            return config;
        }
        catch (Exception ex)
        {
            ConsoleLog.Warn("Indicators", $"Error reading indicator config: {ex.Message}");
            _cached = IndicatorConfig.CreateDefault();
            return _cached;
        }
    }

    /// <summary>
    /// Saves the indicator configuration to JSON.
    /// </summary>
    public static void Save(IndicatorConfig config)
    {
        var path = GetConfigPath();
        var dir = Path.GetDirectoryName(path);
        if (dir != null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(path, json);

        // Invalidate weight cache
        _cachedWeights = null;
    }

    /// <summary>
    /// Gets the IndicatorWeights with disabled indicators zeroed and remaining normalized.
    /// This is what MarketScoreCalculator should use.
    /// Cached for performance (recalculated only when config changes).
    /// </summary>
    public static IndicatorWeights GetWeights()
    {
        if (_cachedWeights.HasValue)
            return _cachedWeights.Value;

        var config = Load();
        var weights = config.ToCalculatorWeights();
        _cachedWeights = weights;
        return weights;
    }

    /// <summary>
    /// Enables or disables a specific indicator by key.
    /// Automatically saves and invalidates caches.
    /// </summary>
    public static void SetEnabled(string indicatorKey, bool enabled)
    {
        var config = Load();

        if (config.Indicators.TryGetValue(indicatorKey, out var entry))
        {
            entry.Enabled = enabled;
            Save(config);
            InvalidateCache();

            var weights = config.ToCalculatorWeights();
            ConsoleLog.Write("Indicators",
                $"{entry.Name} ({indicatorKey}) {(enabled ? "ENABLED" : "DISABLED")} - " +
                $"Active: {config.EnabledCount}/{config.TotalCount}");
        }
        else
        {
            ConsoleLog.Warn("Indicators", $"Unknown indicator key: {indicatorKey}");
        }
    }

    /// <summary>
    /// Updates the base weight for an indicator.
    /// Does NOT normalize - that happens at ToCalculatorWeights() time.
    /// </summary>
    public static void SetBaseWeight(string indicatorKey, double weight)
    {
        var config = Load();

        if (config.Indicators.TryGetValue(indicatorKey, out var entry))
        {
            entry.BaseWeight = Math.Max(0, weight);
            Save(config);
            InvalidateCache();
        }
        else
        {
            ConsoleLog.Warn("Indicators", $"Unknown indicator key: {indicatorKey}");
        }
    }

    /// <summary>
    /// Reloads the config from disk (useful after external edits).
    /// </summary>
    public static void Reload()
    {
        InvalidateCache();
        Load();
    }

    /// <summary>
    /// Clears cached config and weights, forcing reload on next access.
    /// </summary>
    public static void InvalidateCache()
    {
        _cached = null;
        _cachedWeights = null;
    }

    /// <summary>
    /// Prints a summary table of all indicators with their enabled state and effective weights.
    /// </summary>
    public static void PrintSummary()
    {
        var config = Load();
        var weights = config.ToCalculatorWeights();

        ConsoleLog.Write("Indicators", "");
        ConsoleLog.Write("Indicators", "+-------+---+------------+-----------------------+--------+-----------+");
        ConsoleLog.Write("Indicators", "| State |   | Key        | Name                  | Base   | Effective |");
        ConsoleLog.Write("Indicators", "+-------+---+------------+-----------------------+--------+-----------+");

        // Get effective weights as a dictionary for lookup
        var effectiveMap = new Dictionary<string, double>
        {
            ["vwap"] = weights.Vwap,
            ["ema"] = weights.Ema,
            ["rsi"] = weights.Rsi,
            ["macd"] = weights.Macd,
            ["adx"] = weights.Adx,
            ["volume"] = weights.Volume,
            ["bollinger"] = weights.Bollinger,
            ["stochastic"] = weights.Stochastic,
            ["obv"] = weights.Obv,
            ["cci"] = weights.Cci,
            ["williamsR"] = weights.WilliamsR,
            ["sma"] = weights.Sma,
            ["momentum"] = weights.Momentum
        };

        // Sort: enabled first (by effective weight desc), then disabled
        var sorted = config.Indicators
            .OrderByDescending(kv => kv.Value.Enabled)
            .ThenByDescending(kv => effectiveMap.GetValueOrDefault(kv.Key, 0));

        foreach (var (key, entry) in sorted)
        {
            string state = entry.Enabled ? "  *  " : "  o  ";
            double effective = effectiveMap.GetValueOrDefault(key, 0);
            string effectiveStr = entry.Enabled ? $"  {effective:P1}  " : "    --    ";

            ConsoleLog.Write("Indicators",
                $"|{state}  | | {key,-10} | {entry.Name,-21} | {entry.BaseWeight:P0}  | {effectiveStr} |");
        }

        ConsoleLog.Write("Indicators", "+-------+---+------------+-----------------------+--------+-----------+");
        ConsoleLog.Write("Indicators",
            $"| Active: {config.EnabledCount}/{config.TotalCount}  |  Disabled weight redistributed proportionally to enabled  |");
        ConsoleLog.Write("Indicators", "+------------------------------------------------------------------------+");
        ConsoleLog.Write("Indicators", "");
    }

    /// <summary>
    /// Returns all valid indicator keys.
    /// </summary>
    public static IReadOnlyList<string> AllKeys => [
        "vwap", "ema", "rsi", "macd", "adx", "volume",
        "bollinger", "stochastic", "obv", "cci", "williamsR", "sma", "momentum"
    ];
}
