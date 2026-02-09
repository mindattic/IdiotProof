// ============================================================================
// Strategy Rules Manager - Custom Trading Rules for AI Advisor
// ============================================================================
//
// PURPOSE:
// Allows users to define custom trading rules in plain text that ChatGPT
// incorporates into its analysis. These rules work ALONGSIDE the market score
// system - they enhance decisions without overriding indicator logic.
//
// FILE LOCATION:
//   {SolutionRoot}\IdiotProof.Core\Data\strategy-rules.json
//
// FILE FORMAT:
// {
//   "rules": [
//     {
//       "symbol": "CCHH",
//       "enabled": true,
//       "rule": "Wait for breakout above $0.78, then wait for pullback..."
//     }
//   ]
// }
//
// HOW IT WORKS:
// 1. User defines rules as plain text (breakout levels, pullback requirements, etc.)
// 2. AIAdvisor includes these rules in its ChatGPT prompt
// 3. ChatGPT evaluates entries against BOTH indicators AND custom rules
// 4. Rules are advisory - they inform the AI, don't override market score
//
// ============================================================================

using IdiotProof.Settings;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IdiotProof.Services;

/// <summary>
/// A single custom strategy rule for a ticker.
/// </summary>
public sealed class StrategyRule
{
    /// <summary>Ticker symbol this rule applies to (e.g., "CCHH").</summary>
    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = "";

    /// <summary>Whether this rule is active.</summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Start date for this rule (ISO 8601 format in EST, e.g., "2026-02-07" or "2026-02-07T09:30:00"). 
    /// Rules only apply on or after this date. Before this date, the rule is ignored.
    /// Useful for ensuring tips aren't used in backtesting before they were known.
    /// </summary>
    [JsonPropertyName("validFrom")]
    public string? ValidFrom { get; set; }

    /// <summary>
    /// Expiration date for this rule (ISO 8601 format in EST, e.g., "2026-02-10" or "2026-02-10T16:00:00"). 
    /// Rules only apply on or before this date. After this date, the rule is ignored.
    /// These are typically daily tips that don't apply after the specified date.
    /// </summary>
    [JsonPropertyName("validUntil")]
    public string? ValidUntil { get; set; }

    /// <summary>Eastern Standard Time zone for date comparisons.</summary>
    private static readonly TimeZoneInfo EstZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
    
    /// <summary>Gets the current date/time in EST.</summary>
    private static DateTime NowEst => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, EstZone);

    /// <summary>User-friendly name for this strategy.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// The rule description in plain text. ChatGPT will interpret this.
    /// Example: "Wait for breakout above $0.78, then pullback. Only enter if pullback holds above $0.70."
    /// </summary>
    [JsonPropertyName("rule")]
    public string Rule { get; set; } = "";

    /// <summary>Optional: Key levels to watch (breakout resistance, support, etc.)</summary>
    [JsonPropertyName("levels")]
    public StrategyLevels? Levels { get; set; }

    /// <summary>Optional: Target prices.</summary>
    [JsonPropertyName("targets")]
    public List<double>? Targets { get; set; }

    /// <summary>Optional: Notes or additional context.</summary>
    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    /// <summary>Parses an ISO 8601 date string to DateTime in EST.</summary>
    private static DateTime? ParseEstDate(string? dateStr)
    {
        if (string.IsNullOrWhiteSpace(dateStr))
            return null;
        
        // Try parsing as full ISO 8601 datetime first
        if (DateTime.TryParse(dateStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
        {
            // If no timezone info, assume EST
            if (dt.Kind == DateTimeKind.Unspecified)
                return dt;
            // Convert to EST if UTC
            if (dt.Kind == DateTimeKind.Utc)
                return TimeZoneInfo.ConvertTimeFromUtc(dt, EstZone);
            return dt;
        }
        
        // Try parsing as date only (YYYY-MM-DD)
        if (DateOnly.TryParse(dateStr, out var dateOnly))
        {
            return dateOnly.ToDateTime(TimeOnly.MinValue);
        }
        
        return null;
    }

    /// <summary>Checks if the rule has not yet started (before validFrom date in EST).</summary>
    [JsonIgnore]
    public bool IsNotYetValid
    {
        get
        {
            var startDate = ParseEstDate(ValidFrom);
            if (startDate == null)
                return false; // No start date = always started
            
            return NowEst < startDate.Value;
        }
    }

    /// <summary>Checks if the rule has expired based on the validUntil date in EST.</summary>
    [JsonIgnore]
    public bool IsExpired
    {
        get
        {
            var expirationDate = ParseEstDate(ValidUntil);
            if (expirationDate == null)
                return false; // No expiration = never expires
            
            // If only date was specified (no time), expire at end of day
            if (expirationDate.Value.TimeOfDay == TimeSpan.Zero)
                expirationDate = expirationDate.Value.AddDays(1).AddSeconds(-1);
            
            return NowEst > expirationDate.Value;
        }
    }

    /// <summary>Checks if the current date is within the valid date range (in EST).</summary>
    [JsonIgnore]
    public bool IsWithinDateRange => !IsNotYetValid && !IsExpired;

    /// <summary>Checks if the rule is currently valid (enabled, within date range, and has content).</summary>
    [JsonIgnore]
    public bool IsValid => Enabled && IsWithinDateRange && !string.IsNullOrWhiteSpace(Rule);

    public override string ToString()
    {
        var status = IsNotYetValid ? " [NOT YET ACTIVE]" : (IsExpired ? " [EXPIRED]" : "");
        var dateRange = "";
        if (ValidFrom != null || ValidUntil != null)
        {
            var from = ValidFrom ?? "...";
            var until = ValidUntil ?? "...";
            dateRange = $" [{from} to {until}]";
        }
        return $"{Symbol}: {Name ?? Rule.Substring(0, Math.Min(50, Rule.Length))}...{status}{dateRange}";
    }
}

/// <summary>
/// Key price levels for a strategy rule.
/// </summary>
public sealed class StrategyLevels
{
    /// <summary>Price that must break for entry consideration (resistance).</summary>
    [JsonPropertyName("breakout")]
    public double? Breakout { get; set; }

    /// <summary>Price that pullback must hold above (support).</summary>
    [JsonPropertyName("support")]
    public double? Support { get; set; }

    /// <summary>Previous high of day or resistance level.</summary>
    [JsonPropertyName("previousHigh")]
    public double? PreviousHigh { get; set; }

    /// <summary>Previous low of day or support level.</summary>
    [JsonPropertyName("previousLow")]
    public double? PreviousLow { get; set; }
    
    /// <summary>VWAP level for reference.</summary>
    [JsonPropertyName("vwap")]
    public double? Vwap { get; set; }
    
    /// <summary>First trigger level (e.g., local resistance before main breakout).</summary>
    [JsonPropertyName("trigger1")]
    public double? Trigger1 { get; set; }
    
    /// <summary>Micro support level (tighter stop zone).</summary>
    [JsonPropertyName("microSupport")]
    public double? MicroSupport { get; set; }
    
    /// <summary>Range low for ranging/choppy markets.</summary>
    [JsonPropertyName("rangeLow")]
    public double? RangeLow { get; set; }
    
    /// <summary>Range high for ranging/choppy markets.</summary>
    [JsonPropertyName("rangeHigh")]
    public double? RangeHigh { get; set; }
    
    /// <summary>Captures any additional custom levels not explicitly defined.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
    
    /// <summary>Gets all levels as a dictionary for display purposes.</summary>
    public Dictionary<string, double> GetAllLevels()
    {
        var levels = new Dictionary<string, double>();
        
        if (Breakout.HasValue) levels["breakout"] = Breakout.Value;
        if (Support.HasValue) levels["support"] = Support.Value;
        if (PreviousHigh.HasValue) levels["previousHigh"] = PreviousHigh.Value;
        if (PreviousLow.HasValue) levels["previousLow"] = PreviousLow.Value;
        if (Vwap.HasValue) levels["vwap"] = Vwap.Value;
        if (Trigger1.HasValue) levels["trigger1"] = Trigger1.Value;
        if (MicroSupport.HasValue) levels["microSupport"] = MicroSupport.Value;
        if (RangeLow.HasValue) levels["rangeLow"] = RangeLow.Value;
        if (RangeHigh.HasValue) levels["rangeHigh"] = RangeHigh.Value;
        
        // Include any extension data
        if (ExtensionData != null)
        {
            foreach (var kvp in ExtensionData)
            {
                if (kvp.Value.TryGetDouble(out double val))
                {
                    levels[kvp.Key] = val;
                }
            }
        }
        
        return levels;
    }
}

/// <summary>
/// Container for all strategy rules loaded from strategy-rules.json.
/// </summary>
public sealed class StrategyRulesConfig
{
    /// <summary>List of custom strategy rules.</summary>
    [JsonPropertyName("rules")]
    public List<StrategyRule> Rules { get; set; } = [];

    /// <summary>Whether custom rules are enabled globally.</summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>Description of this rules file.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>Gets only enabled and non-expired rules.</summary>
    [JsonIgnore]
    public IEnumerable<StrategyRule> EnabledRules => Rules.Where(r => r.IsValid);

    /// <summary>Gets all expired rules (for cleanup/display purposes).</summary>
    [JsonIgnore]
    public IEnumerable<StrategyRule> ExpiredRules => Rules.Where(r => r.IsExpired);

    /// <summary>Gets rules for a specific symbol (only valid, non-expired rules).</summary>
    public IEnumerable<StrategyRule> GetRulesForSymbol(string symbol) =>
        EnabledRules.Where(r => r.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase));

    /// <summary>Checks if there are any valid rules for a symbol.</summary>
    public bool HasRulesFor(string symbol) =>
        EnabledRules.Any(r => r.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Manages the strategy-rules.json file for custom AI trading rules.
/// </summary>
public static class StrategyRulesManager
{
    private const string RulesFileName = "strategy-rules.json";
    private static StrategyRulesConfig? _cachedConfig;
    private static DateTime _lastLoadTime = DateTime.MinValue;
    private static readonly TimeSpan CacheExpiry = TimeSpan.FromMinutes(1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Gets the path to the strategy-rules.json file.
    /// </summary>
    public static string GetRulesPath()
    {
        return Path.Combine(SettingsManager.GetDataFolder(), RulesFileName);
    }

    /// <summary>
    /// Checks if a rules file exists.
    /// </summary>
    public static bool Exists()
    {
        return File.Exists(GetRulesPath());
    }

    /// <summary>
    /// Loads the strategy rules from the JSON file.
    /// Returns an empty config if file doesn't exist.
    /// Uses caching to avoid frequent disk reads.
    /// </summary>
    public static StrategyRulesConfig Load()
    {
        // Check cache
        if (_cachedConfig != null && DateTime.UtcNow - _lastLoadTime < CacheExpiry)
        {
            return _cachedConfig;
        }

        var path = GetRulesPath();

        if (!File.Exists(path))
        {
            _cachedConfig = new StrategyRulesConfig();
            _lastLoadTime = DateTime.UtcNow;
            return _cachedConfig;
        }

        try
        {
            var json = File.ReadAllText(path);
            var config = JsonSerializer.Deserialize<StrategyRulesConfig>(json, JsonOptions);

            if (config == null)
            {
                Console.WriteLine($"[StrategyRules] Failed to parse rules, using empty config");
                _cachedConfig = new StrategyRulesConfig();
            }
            else
            {
                Console.WriteLine($"[StrategyRules] Loaded {config.EnabledRules.Count()}/{config.Rules.Count} rules from {RulesFileName}");
                foreach (var rule in config.EnabledRules)
                {
                    Console.WriteLine($"  - {rule.Symbol}: {rule.Name ?? "(custom rule)"}");
                }
                _cachedConfig = config;
            }

            _lastLoadTime = DateTime.UtcNow;
            return _cachedConfig;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[StrategyRules] Error loading rules: {ex.Message}");
            _cachedConfig = new StrategyRulesConfig();
            _lastLoadTime = DateTime.UtcNow;
            return _cachedConfig;
        }
    }

    /// <summary>
    /// Forces a reload from disk on next access.
    /// </summary>
    public static void InvalidateCache()
    {
        _cachedConfig = null;
        _lastLoadTime = DateTime.MinValue;
    }

    /// <summary>
    /// Saves a rules config to the JSON file.
    /// </summary>
    public static void Save(StrategyRulesConfig config)
    {
        var path = GetRulesPath();

        try
        {
            // Ensure directory exists
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(config, JsonOptions);
            File.WriteAllText(path, json);

            Console.WriteLine($"[StrategyRules] Saved {config.Rules.Count} rules to {RulesFileName}");
            
            // Update cache
            _cachedConfig = config;
            _lastLoadTime = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[StrategyRules] Error saving rules: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets rules formatted for inclusion in an AI prompt.
    /// </summary>
    public static string GetRulesForPrompt(string symbol)
    {
        var config = Load();
        if (!config.Enabled || !config.HasRulesFor(symbol))
            return "";

        var rules = config.GetRulesForSymbol(symbol).ToList();
        if (rules.Count == 0)
            return "";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine();
        sb.AppendLine("=== USER-DEFINED STRATEGY RULES ===");
        sb.AppendLine("IMPORTANT: Evaluate the entry against these custom rules in addition to indicators.");
        sb.AppendLine("These rules should enhance your analysis, not override indicator signals.");
        sb.AppendLine();

        foreach (var rule in rules)
        {
            sb.AppendLine($"RULE ({rule.Name ?? "Custom"}):");
            sb.AppendLine(rule.Rule);

            if (rule.Levels != null)
            {
                sb.AppendLine("Key Levels:");
                var allLevels = rule.Levels.GetAllLevels();
                foreach (var kvp in allLevels.OrderBy(x => x.Value))
                {
                    // Format level name nicely (camelCase to Title Case)
                    string levelName = System.Text.RegularExpressions.Regex.Replace(
                        kvp.Key, "([a-z])([A-Z])", "$1 $2");
                    levelName = char.ToUpper(levelName[0]) + levelName[1..];
                    sb.AppendLine($"  - {levelName}: ${kvp.Value:F2}");
                }
            }

            if (rule.Targets != null && rule.Targets.Count > 0)
            {
                sb.AppendLine($"Targets: {string.Join(", ", rule.Targets.Select(t => $"${t:F2}"))}");
            }

            if (!string.IsNullOrWhiteSpace(rule.Notes))
            {
                sb.AppendLine($"Notes: {rule.Notes}");
            }

            sb.AppendLine();
        }

        sb.AppendLine("In your analysis, indicate whether the current setup matches these rules.");
        sb.AppendLine("If rules say 'wait for pullback' but there's no pullback yet, recommend WAIT.");
        sb.AppendLine();

        return sb.ToString();
    }

    /// <summary>
    /// Creates sample rules file with the user's provided strategies.
    /// </summary>
    public static void CreateSampleIfNotExists()
    {
        if (Exists())
            return;

        var sample = new StrategyRulesConfig
        {
            Description = "Custom Strategy Rules - ChatGPT evaluates entries against these rules alongside indicators",
            Enabled = true,
            Rules =
            [
                new StrategyRule
                {
                    Symbol = "CCHH",
                    Enabled = true,
                    Name = "Day 2 Breakout-Pullback",
                    Rule = @"CONFIRMATIONS (in order):
1. Price breaks above $0.78 (previous HOD / double top)
2. DO NOT buy the breakout - wait for a pullback
3. Pullback must hold higher lows above $0.70
4. Only take entries if pullback forms clean bottoms above $0.70

RULE: No chasing. Pullback entries only.",
                    Levels = new StrategyLevels
                    {
                        Breakout = 0.78,
                        Support = 0.70,
                        PreviousHigh = 0.78
                    },
                    Targets = [0.85, 1.00],
                    Notes = "Day 2 watch - be patient for proper setup"
                },
                new StrategyRule
                {
                    Symbol = "TONN",
                    Enabled = true,
                    Name = "Earnings Runner Day 2",
                    Rule = @"CONFIRMATIONS (in order):
1. Price breaks above $0.94 (previous high)
2. If it breaks, wait for a pullback/retest
3. Retest must hold above $0.90
4. If it holds, that is your entry confirmation

RULE: No break = no trade.",
                    Levels = new StrategyLevels
                    {
                        Breakout = 0.94,
                        Support = 0.90
                    },
                    Targets = [1.10, 1.30]
                },
                new StrategyRule
                {
                    Symbol = "SMX",
                    Enabled = true,
                    Name = "High Volatility Explosive Setup",
                    Rule = @"CONFIRMATIONS (in order):
1. Price breaks above $20.50 (topping tail high)
2. After the break, wait for a pullback
3. Pullback must hold above $18.70 on the retest
4. Only take entries if price is above VWAP AND above EMAs

RULE: No break = no trade.",
                    Levels = new StrategyLevels
                    {
                        Breakout = 20.50,
                        Support = 18.70,
                        PreviousHigh = 20.50
                    },
                    Targets = [30.00, 40.00],
                    Notes = "High volatility - use smaller position size"
                }
            ]
        };

        Save(sample);
        Console.WriteLine($"[StrategyRules] Created sample rules at: {GetRulesPath()}");
    }

    /// <summary>
    /// Adds or updates a rule for a symbol.
    /// </summary>
    public static void AddOrUpdate(StrategyRule rule)
    {
        var config = Load();
        var existing = config.Rules.FirstOrDefault(r =>
            r.Symbol.Equals(rule.Symbol, StringComparison.OrdinalIgnoreCase) &&
            (r.Name?.Equals(rule.Name, StringComparison.OrdinalIgnoreCase) ?? rule.Name == null));

        if (existing != null)
        {
            var index = config.Rules.IndexOf(existing);
            config.Rules[index] = rule;
        }
        else
        {
            config.Rules.Add(rule);
        }

        Save(config);
    }

    /// <summary>
    /// Enables or disables a rule by symbol.
    /// </summary>
    public static void SetEnabled(string symbol, bool enabled)
    {
        var config = Load();
        foreach (var rule in config.Rules.Where(r =>
            r.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase)))
        {
            rule.Enabled = enabled;
        }
        Save(config);
    }

    /// <summary>
    /// Prints a summary of all rules.
    /// </summary>
    public static void PrintSummary()
    {
        var config = Load();

        Console.WriteLine();
        Console.WriteLine("=== STRATEGY RULES ===");
        Console.WriteLine($"File: {GetRulesPath()}");
        Console.WriteLine($"Global Enabled: {config.Enabled}");
        Console.WriteLine($"Rules: {config.EnabledRules.Count()}/{config.Rules.Count} active ({config.ExpiredRules.Count()} expired)");
        Console.WriteLine();

        foreach (var rule in config.Rules)
        {
            var status = rule.IsExpired ? "x" : (rule.Enabled ? "*" : "o");
            var expiryInfo = rule.ValidUntil != null 
                ? (rule.IsExpired ? $" [EXPIRED {rule.ValidUntil}]" : $" [until {rule.ValidUntil}]")
                : " [no expiry]";
            Console.WriteLine($"[{status}] {rule.Symbol}: {rule.Name ?? "(unnamed)"}{expiryInfo}");
            if (rule.Levels != null)
            {
                if (rule.Levels.Breakout.HasValue)
                    Console.WriteLine($"    Breakout: ${rule.Levels.Breakout.Value:F2}");
                if (rule.Levels.Support.HasValue)
                    Console.WriteLine($"    Support:  ${rule.Levels.Support.Value:F2}");
            }
            if (rule.Targets != null && rule.Targets.Count > 0)
            {
                Console.WriteLine($"    Targets:  {string.Join(", ", rule.Targets.Select(t => $"${t:F2}"))}");
            }
        }
        Console.WriteLine();
        Console.WriteLine("Legend: [*] active, [o] disabled, [x] expired");
        Console.WriteLine();
    }
}
