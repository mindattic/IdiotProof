// ============================================================================
// Ticker Profile - Learned patterns for autonomous trading
// ============================================================================
//
// Stores learned patterns from backtesting that improve autonomous trading:
// - Optimal entry/exit thresholds for this specific ticker
// - Time-of-day patterns (best hours to trade)
// - Indicator correlations (which signals work best)
// - Historical win rates at different score levels
//
// ============================================================================

using System.Text.Json;
using System.Text.Json.Serialization;

namespace IdiotProof.BackTesting.Learning;

/// <summary>
/// A record of a single trade for learning purposes.
/// </summary>
public sealed record TradeRecord
{
    public required DateTime EntryTime { get; init; }
    public required DateTime ExitTime { get; init; }
    public required double EntryPrice { get; init; }
    public required double ExitPrice { get; init; }
    public required double EntryScore { get; init; }
    public required double ExitScore { get; init; }
    public required bool IsLong { get; init; }
    public required bool IsWin { get; init; }
    public required double PnL { get; init; }
    public required double PnLPercent { get; init; }

    // Indicator values at entry
    public double RsiAtEntry { get; init; }
    public double AdxAtEntry { get; init; }
    public double MacdHistogramAtEntry { get; init; }
    public double VolumeRatioAtEntry { get; init; }
    public bool AboveVwapAtEntry { get; init; }
    public bool AboveEma9AtEntry { get; init; }
    public bool AboveEma21AtEntry { get; init; }

    // Time analysis
    public int EntryHour => EntryTime.Hour;
    public int EntryMinute => EntryTime.Minute;
    public DayOfWeek DayOfWeek => EntryTime.DayOfWeek;
    public TimeSpan Duration => ExitTime - EntryTime;
}

/// <summary>
/// Statistics for a specific time window.
/// </summary>
public sealed class TimeWindowStats
{
    public int Hour { get; set; }
    public int TradeCount { get; set; }
    public int WinCount { get; set; }
    public double TotalPnL { get; set; }
    public double WinRate => TradeCount > 0 ? (double)WinCount / TradeCount * 100 : 0;
    public double AvgPnL => TradeCount > 0 ? TotalPnL / TradeCount : 0;
}

/// <summary>
/// Statistics for a specific score range.
/// </summary>
public sealed class ScoreRangeStats
{
    public int MinScore { get; set; }
    public int MaxScore { get; set; }
    public int TradeCount { get; set; }
    public int WinCount { get; set; }
    public double TotalPnL { get; set; }
    public double WinRate => TradeCount > 0 ? (double)WinCount / TradeCount * 100 : 0;
    public double AvgPnL => TradeCount > 0 ? TotalPnL / TradeCount : 0;

    public string RangeLabel => $"{MinScore} to {MaxScore}";
}

/// <summary>
/// Indicator correlation data.
/// </summary>
public sealed class IndicatorCorrelation
{
    public string IndicatorName { get; set; } = "";
    public int TradesWithCondition { get; set; }
    public int WinsWithCondition { get; set; }
    public int TradesWithoutCondition { get; set; }
    public int WinsWithoutCondition { get; set; }

    public double WinRateWith => TradesWithCondition > 0
        ? (double)WinsWithCondition / TradesWithCondition * 100 : 0;

    public double WinRateWithout => TradesWithoutCondition > 0
        ? (double)WinsWithoutCondition / TradesWithoutCondition * 100 : 0;

    /// <summary>
    /// Positive = indicator helps, Negative = indicator hurts.
    /// </summary>
    public double Correlation => WinRateWith - WinRateWithout;
}

/// <summary>
/// Complete learned profile for a ticker.
/// </summary>
public sealed class TickerProfile
{
    // ========================================================================
    // Metadata
    // ========================================================================

    public string Symbol { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public int TotalTrades { get; set; }
    public int TotalWins { get; set; }
    public int TotalLosses { get; set; }
    public double TotalPnL { get; set; }

    /// <summary>
    /// Confidence level based on trade count (0-1).
    /// </summary>
    [JsonIgnore]
    public double Confidence => Math.Min(TotalTrades / 50.0, 1.0);

    // ========================================================================
    // Learned Thresholds
    // ========================================================================

    /// <summary>
    /// Optimal score threshold for long entries (learned).
    /// </summary>
    public double OptimalLongEntryThreshold { get; set; } = 70;

    /// <summary>
    /// Optimal score threshold for short entries (learned).
    /// </summary>
    public double OptimalShortEntryThreshold { get; set; } = -70;

    /// <summary>
    /// Optimal score threshold for long exits (learned).
    /// </summary>
    public double OptimalLongExitThreshold { get; set; } = 40;

    /// <summary>
    /// Optimal score threshold for short exits (learned).
    /// </summary>
    public double OptimalShortExitThreshold { get; set; } = -40;

    // ========================================================================
    // Time Patterns
    // ========================================================================

    /// <summary>
    /// Win rate by hour of day.
    /// </summary>
    public List<TimeWindowStats> HourlyStats { get; set; } = [];

    /// <summary>
    /// Hours to avoid trading (win rate &lt; 40%).
    /// </summary>
    public List<int> AvoidHours { get; set; } = [];

    /// <summary>
    /// Best hours to trade (win rate &gt; 60%).
    /// </summary>
    public List<int> BestHours { get; set; } = [];

    // ========================================================================
    // Historical Metadata Reference
    // ========================================================================

    /// <summary>
    /// Link to historical metadata for this ticker (HOD/LOD patterns, support/resistance, etc.).
    /// Loaded separately via HistoricalMetadataManager.
    /// </summary>
    [JsonIgnore]
    public HistoricalMetadata? HistoricalMetadata { get; set; }

    /// <summary>
    /// Gets whether historical metadata is available for this ticker.
    /// </summary>
    [JsonIgnore]
    public bool HasHistoricalMetadata => HistoricalMetadata != null;

    // ========================================================================
    // Score Analysis
    // ========================================================================

    /// <summary>
    /// Win rate by entry score range.
    /// </summary>
    public List<ScoreRangeStats> ScoreRangeStats { get; set; } = [];

    // ========================================================================
    // Indicator Correlations
    // ========================================================================

    /// <summary>
    /// How well each indicator predicts wins.
    /// </summary>
    public List<IndicatorCorrelation> IndicatorCorrelations { get; set; } = [];

    // ========================================================================
    // Streak Tracking
    // ========================================================================

    public int CurrentWinStreak { get; set; }
    public int CurrentLossStreak { get; set; }
    public int MaxWinStreak { get; set; }
    public int MaxLossStreak { get; set; }

    // ========================================================================
    // Raw Trade History (for re-analysis)
    // ========================================================================

    /// <summary>
    /// Recent trade records (kept for re-analysis).
    /// Limited to last 200 trades to prevent unbounded growth.
    /// </summary>
    public List<TradeRecord> RecentTrades { get; set; } = [];

    private const int MaxRecentTrades = 200;

    // ========================================================================
    // Methods
    // ========================================================================

    /// <summary>
    /// Adds a trade record and updates statistics.
    /// </summary>
    public void AddTrade(TradeRecord trade)
    {
        RecentTrades.Add(trade);

        // Trim to max size
        if (RecentTrades.Count > MaxRecentTrades)
        {
            RecentTrades.RemoveAt(0);
        }

        // Update totals
        TotalTrades++;
        if (trade.IsWin)
        {
            TotalWins++;
            CurrentWinStreak++;
            CurrentLossStreak = 0;
            MaxWinStreak = Math.Max(MaxWinStreak, CurrentWinStreak);
        }
        else
        {
            TotalLosses++;
            CurrentLossStreak++;
            CurrentWinStreak = 0;
            MaxLossStreak = Math.Max(MaxLossStreak, CurrentLossStreak);
        }

        TotalPnL += trade.PnL;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Recalculates all derived statistics from trade history.
    /// </summary>
    public void RecalculateStatistics()
    {
        if (RecentTrades.Count == 0) return;

        // Hourly stats
        HourlyStats = RecentTrades
            .GroupBy(t => t.EntryHour)
            .Select(g => new TimeWindowStats
            {
                Hour = g.Key,
                TradeCount = g.Count(),
                WinCount = g.Count(t => t.IsWin),
                TotalPnL = g.Sum(t => t.PnL)
            })
            .OrderBy(s => s.Hour)
            .ToList();

        // Best/worst hours
        BestHours = HourlyStats.Where(s => s.WinRate >= 60 && s.TradeCount >= 3)
            .Select(s => s.Hour).ToList();
        AvoidHours = HourlyStats.Where(s => s.WinRate < 40 && s.TradeCount >= 3)
            .Select(s => s.Hour).ToList();

        // Score range stats
        var scoreRanges = new[] { (60, 70), (70, 80), (80, 90), (90, 100) };
        ScoreRangeStats = scoreRanges.Select(range =>
        {
            var trades = RecentTrades.Where(t =>
                t.IsLong && t.EntryScore >= range.Item1 && t.EntryScore < range.Item2).ToList();
            return new ScoreRangeStats
            {
                MinScore = range.Item1,
                MaxScore = range.Item2,
                TradeCount = trades.Count,
                WinCount = trades.Count(t => t.IsWin),
                TotalPnL = trades.Sum(t => t.PnL)
            };
        }).Where(s => s.TradeCount > 0).ToList();

        // Find optimal thresholds
        var bestLongRange = ScoreRangeStats
            .Where(s => s.TradeCount >= 3)
            .OrderByDescending(s => s.WinRate)
            .ThenByDescending(s => s.AvgPnL)
            .FirstOrDefault();

        if (bestLongRange != null)
        {
            OptimalLongEntryThreshold = bestLongRange.MinScore;
        }

        // Indicator correlations
        CalculateIndicatorCorrelations();
    }

    private void CalculateIndicatorCorrelations()
    {
        IndicatorCorrelations =
        [
            CalculateCorrelation("AboveVWAP", t => t.AboveVwapAtEntry),
            CalculateCorrelation("AboveEMA9", t => t.AboveEma9AtEntry),
            CalculateCorrelation("AboveEMA21", t => t.AboveEma21AtEntry),
            CalculateCorrelation("RSI<30 (Oversold)", t => t.RsiAtEntry < 30),
            CalculateCorrelation("RSI>70 (Overbought)", t => t.RsiAtEntry > 70),
            CalculateCorrelation("ADX>25 (Trending)", t => t.AdxAtEntry > 25),
            CalculateCorrelation("HighVolume (>1.5x)", t => t.VolumeRatioAtEntry > 1.5),
            CalculateCorrelation("MACD Bullish", t => t.MacdHistogramAtEntry > 0)
        ];
    }

    private IndicatorCorrelation CalculateCorrelation(string name, Func<TradeRecord, bool> condition)
    {
        var withCondition = RecentTrades.Where(condition).ToList();
        var withoutCondition = RecentTrades.Where(t => !condition(t)).ToList();

        return new IndicatorCorrelation
        {
            IndicatorName = name,
            TradesWithCondition = withCondition.Count,
            WinsWithCondition = withCondition.Count(t => t.IsWin),
            TradesWithoutCondition = withoutCondition.Count,
            WinsWithoutCondition = withoutCondition.Count(t => t.IsWin)
        };
    }

    /// <summary>
    /// Gets the adjusted entry threshold based on learned patterns.
    /// Blends learned value with default based on confidence.
    /// </summary>
    public double GetAdjustedLongEntryThreshold(double defaultThreshold)
    {
        return OptimalLongEntryThreshold * Confidence + defaultThreshold * (1 - Confidence);
    }

    /// <summary>
    /// Checks if a given hour should be avoided based on historical data.
    /// </summary>
    public bool ShouldAvoidHour(int hour)
    {
        return Confidence > 0.3 && AvoidHours.Contains(hour);
    }

    /// <summary>
    /// Gets a risk multiplier based on current streak.
    /// More conservative after losses, slightly aggressive after wins.
    /// </summary>
    public double GetStreakRiskMultiplier()
    {
        if (CurrentLossStreak >= 3)
            return 0.5;  // Reduce position size
        if (CurrentLossStreak >= 2)
            return 0.75;
        if (CurrentWinStreak >= 5)
            return 0.9;  // Slightly reduce to protect gains
        return 1.0;
    }

    // ========================================================================
    // Display
    // ========================================================================

    public override string ToString()
    {
        double winRate = TotalTrades > 0 ? (double)TotalWins / TotalTrades * 100 : 0;

        return $"""
            +==================================================================+
            | TICKER PROFILE: {Symbol,-10}                                     |
            +==================================================================+
            | Created:    {CreatedAt:yyyy-MM-dd HH:mm}
            | Updated:    {UpdatedAt:yyyy-MM-dd HH:mm}
            | Confidence: {Confidence * 100:F0}% ({TotalTrades} trades)
            +------------------------------------------------------------------+
            | PERFORMANCE                                                      |
            +------------------------------------------------------------------+
            | Total Trades:  {TotalTrades,6}
            | Win Rate:      {winRate,5:F1}%
            | Total PnL:    ${TotalPnL,8:F2}
            | Win Streak:    {CurrentWinStreak,6} (max: {MaxWinStreak})
            | Loss Streak:   {CurrentLossStreak,6} (max: {MaxLossStreak})
            +------------------------------------------------------------------+
            | LEARNED THRESHOLDS                                               |
            +------------------------------------------------------------------+
            | Long Entry:  >= {OptimalLongEntryThreshold:F0}
            | Short Entry: <= {OptimalShortEntryThreshold:F0}
            | Long Exit:   <  {OptimalLongExitThreshold:F0}
            | Short Exit:  >  {OptimalShortExitThreshold:F0}
            +------------------------------------------------------------------+
            | TIME PATTERNS                                                    |
            +------------------------------------------------------------------+
            | Best Hours:  {(BestHours.Count > 0 ? string.Join(", ", BestHours.Select(h => $"{h}:00")) : "Not enough data")}
            | Avoid Hours: {(AvoidHours.Count > 0 ? string.Join(", ", AvoidHours.Select(h => $"{h}:00")) : "None identified")}
            +==================================================================+
            """;
    }
}

/// <summary>
/// Manages ticker profiles - loading, saving, and updating.
/// </summary>
public sealed class TickerProfileManager
{
    private readonly string _profileDirectory;
    private readonly Dictionary<string, TickerProfile> _cache = [];

    /// <summary>
    /// Default profile directory relative to solution: IdiotProof.Scripts\Profiles
    /// </summary>
    private static string GetDefaultProfileDirectory()
    {
        // Try to find the IdiotProof.Scripts directory relative to current location
        string currentDir = AppDomain.CurrentDomain.BaseDirectory;

        // Walk up to find solution root
        DirectoryInfo? dir = new DirectoryInfo(currentDir);
        while (dir != null)
        {
            string scriptsPath = Path.Combine(dir.FullName, "IdiotProof.Scripts", "Profiles");
            if (Directory.Exists(Path.Combine(dir.FullName, "IdiotProof.Scripts")))
            {
                return scriptsPath;
            }

            // Also check if IdiotProof.Scripts is a sibling
            string siblingPath = Path.Combine(dir.FullName, "..", "IdiotProof.Scripts", "Profiles");
            if (Directory.Exists(Path.Combine(dir.FullName, "..", "IdiotProof.Scripts")))
            {
                return Path.GetFullPath(siblingPath);
            }

            dir = dir.Parent;
        }

        // Fallback to current directory if we can't find the solution
        return Path.Combine(currentDir, "Profiles");
    }

    public TickerProfileManager(string? profileDirectory = null)
    {
        _profileDirectory = profileDirectory ?? GetDefaultProfileDirectory();

        if (!Directory.Exists(_profileDirectory))
        {
            Directory.CreateDirectory(_profileDirectory);
        }
    }

    /// <summary>
    /// Gets the profile directory path.
    /// </summary>
    public string ProfileDirectory => _profileDirectory;

    /// <summary>
    /// Gets or creates a profile for a ticker.
    /// </summary>
    public TickerProfile GetOrCreate(string symbol)
    {
        symbol = symbol.ToUpperInvariant();

        if (_cache.TryGetValue(symbol, out var cached))
            return cached;

        var profile = Load(symbol) ?? new TickerProfile { Symbol = symbol };
        _cache[symbol] = profile;
        return profile;
    }

    /// <summary>
    /// Loads a profile from disk.
    /// </summary>
    public TickerProfile? Load(string symbol)
    {
        symbol = symbol.ToUpperInvariant();
        var path = GetProfilePath(symbol);

        if (!File.Exists(path))
            return null;

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<TickerProfile>(json, GetJsonOptions());
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARN] Failed to load profile for {symbol}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Saves a profile to disk.
    /// </summary>
    public void Save(TickerProfile profile)
    {
        var path = GetProfilePath(profile.Symbol);

        var json = JsonSerializer.Serialize(profile, GetJsonOptions());
        File.WriteAllText(path, json);

        _cache[profile.Symbol.ToUpperInvariant()] = profile;
    }

    /// <summary>
    /// Lists all available profiles.
    /// </summary>
    public List<string> ListProfiles()
    {
        return Directory.GetFiles(_profileDirectory, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => n != null)
            .Cast<string>()
            .ToList();
    }

    /// <summary>
    /// Deletes a profile.
    /// </summary>
    public void Delete(string symbol)
    {
        symbol = symbol.ToUpperInvariant();
        var path = GetProfilePath(symbol);

        if (File.Exists(path))
            File.Delete(path);

        _cache.Remove(symbol);
    }

    /// <summary>
    /// Loads a profile with its associated historical metadata.
    /// </summary>
    public TickerProfile? LoadWithMetadata(string symbol, HistoricalMetadataManager? metadataManager = null)
    {
        var profile = Load(symbol);
        if (profile == null) return null;

        metadataManager ??= new HistoricalMetadataManager();
        profile.HistoricalMetadata = metadataManager.Load(symbol);

        return profile;
    }

    /// <summary>
    /// Gets or creates a profile with its associated historical metadata.
    /// </summary>
    public TickerProfile GetOrCreateWithMetadata(string symbol, HistoricalMetadataManager? metadataManager = null)
    {
        var profile = GetOrCreate(symbol);

        metadataManager ??= new HistoricalMetadataManager();
        profile.HistoricalMetadata = metadataManager.Load(symbol);

        return profile;
    }

    private string GetProfilePath(string symbol) =>
        Path.Combine(_profileDirectory, $"{symbol.ToUpperInvariant()}.json");

    private static JsonSerializerOptions GetJsonOptions() => new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}
