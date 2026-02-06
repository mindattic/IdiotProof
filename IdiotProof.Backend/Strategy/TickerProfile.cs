// ============================================================================
// Ticker Profile - Learning System for Autonomous Trading
// ============================================================================
//
// Tracks historical patterns and outcomes for each ticker to improve
// autonomous trading decisions over time. Learns what works for each
// specific stock based on actual trade outcomes.
//
// ╔═══════════════════════════════════════════════════════════════════════════╗
// ║  LEARNING PIPELINE                                                        ║
// ║                                                                           ║
// ║  Trade Entry → Track Indicators → Trade Exit → Analyze Outcome           ║
// ║       ↓              ↓                ↓              ↓                   ║
// ║  Record entry    Snapshot         Record P&L    Update profile           ║
// ║  conditions      all values       and duration  statistics               ║
// ║                                                                           ║
// ║  Over time, the profile learns:                                          ║
// ║    • Optimal entry/exit score thresholds for THIS ticker                ║
// ║    • Best time-of-day windows                                            ║
// ║    • Which indicator combinations work best                              ║
// ║    • Average trade duration and profit                                   ║
// ║    • Win rate at different threshold levels                              ║
// ╚═══════════════════════════════════════════════════════════════════════════╝

using System.Text.Json;
using IdiotProof.Shared.Settings;
using System.Text.Json.Serialization;

namespace IdiotProof.Backend.Strategy
{
    /// <summary>
    /// Records a single trade for learning purposes.
    /// </summary>
    public class TradeRecord
    {
        public DateTime EntryTime { get; set; }
        public DateTime ExitTime { get; set; }
        public double EntryPrice { get; set; }
        public double ExitPrice { get; set; }
        public bool WasLong { get; set; }
        public int Quantity { get; set; }

        // Indicator values at entry
        public int EntryScore { get; set; }
        public int EntryVwapScore { get; set; }
        public int EntryEmaScore { get; set; }
        public int EntryRsiScore { get; set; }
        public int EntryMacdScore { get; set; }
        public int EntryAdxScore { get; set; }
        public int EntryVolumeScore { get; set; }
        public double EntryRsi { get; set; }
        public double EntryAdx { get; set; }

        // Indicator values at exit
        public int ExitScore { get; set; }
        public double ExitRsi { get; set; }
        public double ExitAdx { get; set; }

        // Outcome
        public double ProfitLoss => WasLong
            ? (ExitPrice - EntryPrice) * Quantity
            : (EntryPrice - ExitPrice) * Quantity;

        public double ProfitLossPercent => WasLong
            ? (ExitPrice - EntryPrice) / EntryPrice * 100
            : (EntryPrice - ExitPrice) / EntryPrice * 100;

        public bool IsWin => ProfitLoss > 0;

        public TimeSpan Duration => ExitTime - EntryTime;

        public int EntryHour => EntryTime.Hour;
        public int EntryMinute => EntryTime.Minute;

        /// <summary>
        /// Gets the time bucket (e.g., "09:30", "10:00") for pattern analysis.
        /// </summary>
        public string TimeBucket => $"{EntryHour:D2}:{(EntryMinute / 15) * 15:D2}";
    }

    /// <summary>
    /// Statistics for a specific threshold level.
    /// </summary>
    public class ThresholdStats
    {
        public int Threshold { get; set; }
        public int TotalTrades { get; set; }
        public int Wins { get; set; }
        public int Losses { get; set; }
        public double TotalProfit { get; set; }
        public double TotalLoss { get; set; }

        public double WinRate => TotalTrades > 0 ? (double)Wins / TotalTrades * 100 : 0;
        public double ProfitFactor => TotalLoss != 0 ? Math.Abs(TotalProfit / TotalLoss) : TotalProfit > 0 ? 999 : 0;
        public double AverageWin => Wins > 0 ? TotalProfit / Wins : 0;
        public double AverageLoss => Losses > 0 ? TotalLoss / Losses : 0;
        public double Expectancy => TotalTrades > 0 ? (TotalProfit + TotalLoss) / TotalTrades : 0;

        public void RecordTrade(TradeRecord trade)
        {
            TotalTrades++;
            if (trade.IsWin)
            {
                Wins++;
                TotalProfit += trade.ProfitLoss;
            }
            else
            {
                Losses++;
                TotalLoss += trade.ProfitLoss; // Will be negative
            }
        }
    }

    /// <summary>
    /// Time-of-day statistics for a ticker.
    /// </summary>
    public class TimeWindowStats
    {
        public string TimeBucket { get; set; } = "";
        public int TotalTrades { get; set; }
        public int Wins { get; set; }
        public double TotalProfit { get; set; }

        public double WinRate => TotalTrades > 0 ? (double)Wins / TotalTrades * 100 : 0;
        public double AverageProfit => TotalTrades > 0 ? TotalProfit / TotalTrades : 0;
    }

    /// <summary>
    /// Indicator correlation statistics.
    /// Tracks how often a specific indicator state leads to winning trades.
    /// </summary>
    public class IndicatorCorrelation
    {
        public string IndicatorName { get; set; } = "";
        public string Condition { get; set; } = ""; // e.g., "RSI < 30", "MACD Bullish"
        public int Occurrences { get; set; }
        public int Wins { get; set; }
        public double TotalProfit { get; set; }

        public double WinRate => Occurrences > 0 ? (double)Wins / Occurrences * 100 : 0;
        public double AverageProfit => Occurrences > 0 ? TotalProfit / Occurrences : 0;
    }

    /// <summary>
    /// Learned profile for a specific ticker.
    /// Accumulates statistics over time to improve trading decisions.
    /// </summary>
    public class TickerProfile
    {
        public string Symbol { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

        // Trade history (keep last N trades for detailed analysis)
        public List<TradeRecord> RecentTrades { get; set; } = new();
        private const int MaxRecentTrades = 100;

        // Overall statistics
        public int TotalTrades { get; set; }
        public int TotalWins { get; set; }
        public int TotalLosses { get; set; }
        public double TotalProfit { get; set; }
        public double TotalLoss { get; set; }
        public int LongTrades { get; set; }
        public int LongWins { get; set; }
        public int ShortTrades { get; set; }
        public int ShortWins { get; set; }

        // Calculated properties
        public double WinRate => TotalTrades > 0 ? (double)TotalWins / TotalTrades * 100 : 50;
        public double LongWinRate => LongTrades > 0 ? (double)LongWins / LongTrades * 100 : 50;
        public double ShortWinRate => ShortTrades > 0 ? (double)ShortWins / ShortTrades * 100 : 50;
        public double ProfitFactor => TotalLoss != 0 ? Math.Abs(TotalProfit / TotalLoss) : 1;
        public double NetProfit => TotalProfit + TotalLoss;

        // Threshold-specific statistics
        public Dictionary<int, ThresholdStats> LongEntryThresholdStats { get; set; } = new();
        public Dictionary<int, ThresholdStats> ShortEntryThresholdStats { get; set; } = new();
        public Dictionary<int, ThresholdStats> ExitScoreStats { get; set; } = new();

        // Time-of-day patterns
        public Dictionary<string, TimeWindowStats> TimeWindowStats { get; set; } = new();

        // Indicator correlations
        public List<IndicatorCorrelation> IndicatorCorrelations { get; set; } = new();

        // Learned optimal values (adjusted over time)
        public int OptimalLongEntryThreshold { get; set; } = 70;
        public int OptimalShortEntryThreshold { get; set; } = -70;
        public int OptimalLongExitThreshold { get; set; } = 40;
        public int OptimalShortExitThreshold { get; set; } = -40;

        // Confidence level (0-100, increases with more trades)
        public int Confidence => Math.Min(100, TotalTrades * 2);

        // Average trade metrics
        public double AverageTradeDurationMinutes { get; set; }
        public double AverageWinAmount { get; set; }
        public double AverageLossAmount { get; set; }
        public double BestTradeProfit { get; set; }
        public double WorstTradeLoss { get; set; }

        // Streak tracking
        public int CurrentStreak { get; set; } // Positive = win streak, negative = loss streak
        public int LongestWinStreak { get; set; }
        public int LongestLossStreak { get; set; }

        /// <summary>
        /// Records a completed trade and updates all statistics.
        /// </summary>
        public void RecordTrade(TradeRecord trade)
        {
            LastUpdated = DateTime.UtcNow;

            // Add to recent trades (maintain max size)
            RecentTrades.Add(trade);
            if (RecentTrades.Count > MaxRecentTrades)
                RecentTrades.RemoveAt(0);

            // Update overall stats
            TotalTrades++;
            if (trade.IsWin)
            {
                TotalWins++;
                TotalProfit += trade.ProfitLoss;
                CurrentStreak = CurrentStreak >= 0 ? CurrentStreak + 1 : 1;
                LongestWinStreak = Math.Max(LongestWinStreak, CurrentStreak);
            }
            else
            {
                TotalLosses++;
                TotalLoss += trade.ProfitLoss; // Negative value
                CurrentStreak = CurrentStreak <= 0 ? CurrentStreak - 1 : -1;
                LongestLossStreak = Math.Max(LongestLossStreak, Math.Abs(CurrentStreak));
            }

            // Update long/short stats
            if (trade.WasLong)
            {
                LongTrades++;
                if (trade.IsWin) LongWins++;
            }
            else
            {
                ShortTrades++;
                if (trade.IsWin) ShortWins++;
            }

            // Update best/worst
            BestTradeProfit = Math.Max(BestTradeProfit, trade.ProfitLoss);
            WorstTradeLoss = Math.Min(WorstTradeLoss, trade.ProfitLoss);

            // Update averages
            AverageTradeDurationMinutes = RecentTrades.Average(t => t.Duration.TotalMinutes);
            AverageWinAmount = RecentTrades.Where(t => t.IsWin).Select(t => t.ProfitLoss).DefaultIfEmpty(0).Average();
            AverageLossAmount = RecentTrades.Where(t => !t.IsWin).Select(t => t.ProfitLoss).DefaultIfEmpty(0).Average();

            // Update threshold stats
            UpdateThresholdStats(trade);

            // Update time window stats
            UpdateTimeWindowStats(trade);

            // Update indicator correlations
            UpdateIndicatorCorrelations(trade);

            // Recalculate optimal thresholds
            RecalculateOptimalThresholds();
        }

        private void UpdateThresholdStats(TradeRecord trade)
        {
            // Round entry score to nearest 5 for bucketing
            int scoreBucket = (trade.EntryScore / 5) * 5;

            if (trade.WasLong)
            {
                if (!LongEntryThresholdStats.ContainsKey(scoreBucket))
                    LongEntryThresholdStats[scoreBucket] = new ThresholdStats { Threshold = scoreBucket };
                LongEntryThresholdStats[scoreBucket].RecordTrade(trade);
            }
            else
            {
                if (!ShortEntryThresholdStats.ContainsKey(scoreBucket))
                    ShortEntryThresholdStats[scoreBucket] = new ThresholdStats { Threshold = scoreBucket };
                ShortEntryThresholdStats[scoreBucket].RecordTrade(trade);
            }

            // Exit score stats
            int exitBucket = (trade.ExitScore / 5) * 5;
            if (!ExitScoreStats.ContainsKey(exitBucket))
                ExitScoreStats[exitBucket] = new ThresholdStats { Threshold = exitBucket };
            ExitScoreStats[exitBucket].RecordTrade(trade);
        }

        private void UpdateTimeWindowStats(TradeRecord trade)
        {
            string bucket = trade.TimeBucket;
            if (!TimeWindowStats.ContainsKey(bucket))
                TimeWindowStats[bucket] = new TimeWindowStats { TimeBucket = bucket };

            var stats = TimeWindowStats[bucket];
            stats.TotalTrades++;
            if (trade.IsWin) stats.Wins++;
            stats.TotalProfit += trade.ProfitLoss;
        }

        private void UpdateIndicatorCorrelations(TradeRecord trade)
        {
            // RSI Oversold entries
            if (trade.EntryRsi <= 30)
            {
                UpdateCorrelation("RSI", "Oversold (<=30)", trade);
            }
            else if (trade.EntryRsi >= 70)
            {
                UpdateCorrelation("RSI", "Overbought (>=70)", trade);
            }

            // ADX Trend Strength
            if (trade.EntryAdx >= 25)
            {
                UpdateCorrelation("ADX", "Strong Trend (>=25)", trade);
            }
            else
            {
                UpdateCorrelation("ADX", "Weak Trend (<25)", trade);
            }

            // MACD direction
            if (trade.EntryMacdScore > 0)
            {
                UpdateCorrelation("MACD", "Bullish", trade);
            }
            else
            {
                UpdateCorrelation("MACD", "Bearish", trade);
            }

            // High volume entries
            if (trade.EntryVolumeScore > 50)
            {
                UpdateCorrelation("Volume", "High (>1.5x avg)", trade);
            }
        }

        private void UpdateCorrelation(string indicator, string condition, TradeRecord trade)
        {
            var correlation = IndicatorCorrelations.FirstOrDefault(c =>
                c.IndicatorName == indicator && c.Condition == condition);

            if (correlation == null)
            {
                correlation = new IndicatorCorrelation
                {
                    IndicatorName = indicator,
                    Condition = condition
                };
                IndicatorCorrelations.Add(correlation);
            }

            correlation.Occurrences++;
            if (trade.IsWin) correlation.Wins++;
            correlation.TotalProfit += trade.ProfitLoss;
        }

        /// <summary>
        /// Recalculates optimal thresholds based on historical performance.
        /// </summary>
        private void RecalculateOptimalThresholds()
        {
            // Need at least 10 trades to start adjusting
            if (TotalTrades < 10) return;

            // Find best long entry threshold
            if (LongEntryThresholdStats.Count > 0)
            {
                var bestLongThreshold = LongEntryThresholdStats
                    .Where(kvp => kvp.Value.TotalTrades >= 3) // Minimum sample size
                    .OrderByDescending(kvp => kvp.Value.Expectancy)
                    .ThenByDescending(kvp => kvp.Value.WinRate)
                    .FirstOrDefault();

                if (bestLongThreshold.Value != null && bestLongThreshold.Value.WinRate > 55)
                {
                    OptimalLongEntryThreshold = bestLongThreshold.Key;
                }
            }

            // Find best short entry threshold
            if (ShortEntryThresholdStats.Count > 0)
            {
                var bestShortThreshold = ShortEntryThresholdStats
                    .Where(kvp => kvp.Value.TotalTrades >= 3)
                    .OrderByDescending(kvp => kvp.Value.Expectancy)
                    .ThenByDescending(kvp => kvp.Value.WinRate)
                    .FirstOrDefault();

                if (bestShortThreshold.Value != null && bestShortThreshold.Value.WinRate > 55)
                {
                    OptimalShortEntryThreshold = bestShortThreshold.Key;
                }
            }
        }

        /// <summary>
        /// Gets adjusted entry threshold based on learned patterns.
        /// Blends default with learned optimal based on confidence.
        /// </summary>
        public int GetAdjustedLongEntryThreshold(int defaultThreshold)
        {
            if (Confidence < 20) return defaultThreshold;

            // Blend: as confidence increases, use more of the learned value
            double blendFactor = Confidence / 100.0;
            return (int)(defaultThreshold * (1 - blendFactor) + OptimalLongEntryThreshold * blendFactor);
        }

        /// <summary>
        /// Gets adjusted short entry threshold based on learned patterns.
        /// </summary>
        public int GetAdjustedShortEntryThreshold(int defaultThreshold)
        {
            if (Confidence < 20) return defaultThreshold;

            double blendFactor = Confidence / 100.0;
            return (int)(defaultThreshold * (1 - blendFactor) + OptimalShortEntryThreshold * blendFactor);
        }

        /// <summary>
        /// Checks if current time is a good trading window based on historical patterns.
        /// </summary>
        public bool IsGoodTimeWindow(DateTime currentTime)
        {
            if (TimeWindowStats.Count < 5) return true; // Not enough data

            string bucket = $"{currentTime.Hour:D2}:{(currentTime.Minute / 15) * 15:D2}";
            if (!TimeWindowStats.TryGetValue(bucket, out var stats))
                return true; // No data for this window

            // Avoid windows with poor win rates (< 40%) and sufficient sample size
            if (stats.TotalTrades >= 5 && stats.WinRate < 40)
                return false;

            return true;
        }

        /// <summary>
        /// Gets a confidence-weighted score adjustment.
        /// Positive = should be more aggressive, Negative = should be more conservative.
        /// </summary>
        public int GetScoreAdjustment(int currentScore, bool isLong)
        {
            if (Confidence < 30) return 0;

            // If we're on a losing streak, be more conservative
            if (CurrentStreak <= -3)
                return isLong ? 10 : -10; // Require higher absolute score

            // If we're on a winning streak and this ticker has good win rate, be slightly more aggressive
            if (CurrentStreak >= 3 && WinRate > 60)
                return isLong ? -5 : 5; // Allow slightly lower absolute score

            return 0;
        }

        /// <summary>
        /// Gets a summary string of the ticker profile for logging.
        /// </summary>
        public string GetSummary()
        {
            return $"{Symbol}: {TotalTrades} trades, {WinRate:F1}% win rate, " +
                   $"PF={ProfitFactor:F2}, Net=${NetProfit:F2}, " +
                   $"Conf={Confidence}%, OptLong={OptimalLongEntryThreshold}, OptShort={OptimalShortEntryThreshold}";
        }
    }

    /// <summary>
    /// Manages ticker profiles for all symbols.
    /// Handles persistence to disk.
    /// </summary>
    public class TickerProfileManager
    {
        private readonly Dictionary<string, TickerProfile> _profiles = new();
        private readonly string _profileDirectory;
        private readonly object _lock = new();

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public TickerProfileManager(string? profileDirectory = null)
        {
            _profileDirectory = profileDirectory ?? SettingsManager.GetProfilesFolder();

            Directory.CreateDirectory(_profileDirectory);
            LoadAllProfiles();
        }

        /// <summary>
        /// Gets or creates a profile for a symbol.
        /// </summary>
        public TickerProfile GetProfile(string symbol)
        {
            lock (_lock)
            {
                if (!_profiles.TryGetValue(symbol.ToUpperInvariant(), out var profile))
                {
                    profile = new TickerProfile { Symbol = symbol.ToUpperInvariant() };
                    _profiles[symbol.ToUpperInvariant()] = profile;
                }
                return profile;
            }
        }

        /// <summary>
        /// Records a completed trade and saves the updated profile.
        /// </summary>
        public void RecordTrade(string symbol, TradeRecord trade)
        {
            var profile = GetProfile(symbol);
            profile.RecordTrade(trade);
            SaveProfile(profile);
        }

        /// <summary>
        /// Saves a profile to disk.
        /// </summary>
        public void SaveProfile(TickerProfile profile)
        {
            try
            {
                string filePath = Path.Combine(_profileDirectory, $"{profile.Symbol}.json");
                string json = JsonSerializer.Serialize(profile, JsonOptions);
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARN] Failed to save ticker profile for {profile.Symbol}: {ex.Message}");
            }
        }

        /// <summary>
        /// Loads all profiles from disk.
        /// </summary>
        private void LoadAllProfiles()
        {
            try
            {
                foreach (var file in Directory.GetFiles(_profileDirectory, "*.json"))
                {
                    try
                    {
                        string json = File.ReadAllText(file);
                        var profile = JsonSerializer.Deserialize<TickerProfile>(json, JsonOptions);
                        if (profile != null)
                        {
                            _profiles[profile.Symbol.ToUpperInvariant()] = profile;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[WARN] Failed to load ticker profile from {file}: {ex.Message}");
                    }
                }

                if (_profiles.Count > 0)
                {
                    Console.WriteLine($"[OK] Loaded {_profiles.Count} ticker profiles from disk");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARN] Failed to load ticker profiles: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets all loaded profiles.
        /// </summary>
        public IReadOnlyCollection<TickerProfile> GetAllProfiles()
        {
            lock (_lock)
            {
                return _profiles.Values.ToList().AsReadOnly();
            }
        }

        /// <summary>
        /// Gets summary statistics across all profiles.
        /// </summary>
        public string GetOverallSummary()
        {
            lock (_lock)
            {
                if (_profiles.Count == 0)
                    return "No ticker profiles yet";

                int totalTrades = _profiles.Values.Sum(p => p.TotalTrades);
                int totalWins = _profiles.Values.Sum(p => p.TotalWins);
                double netProfit = _profiles.Values.Sum(p => p.NetProfit);
                double winRate = totalTrades > 0 ? (double)totalWins / totalTrades * 100 : 0;

                return $"Overall: {_profiles.Count} tickers, {totalTrades} trades, " +
                       $"{winRate:F1}% win rate, Net P&L: ${netProfit:F2}";
            }
        }
    }
}
