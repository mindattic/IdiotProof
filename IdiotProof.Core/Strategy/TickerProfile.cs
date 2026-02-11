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
using IdiotProof.Constants;
using IdiotProof.Logging;
using IdiotProof.Settings;
using System.Text.Json.Serialization;
using IdiotProof.Learning;

namespace IdiotProof.Strategy {
    /// <summary>
    /// Records a single trade for learning purposes.
    /// </summary>
    public class TradeRecord
    {
        public DateTime EntryTime { get; set; }
        public DateTime ExitTime { get; set; }
        public double EntryPrice { get; set; }
        public double ExitPrice { get; set; }
        public bool IsLong { get; set; }
        public int Quantity { get; set; }

        // Indicator values at entry
        public int EntryScore { get; set; }
        public int EntryVwapScore { get; set; }
        public int EntryEmaScore { get; set; }
        public int EntryRsiScore { get; set; }
        public int EntryMacdScore { get; set; }
        public int EntryAdxScore { get; set; }
        public int EntryVolumeScore { get; set; }
        public double RsiAtEntry { get; set; }
        public double AdxAtEntry { get; set; }

        // Additional indicator scores at entry
        public int EntryBollingerScore { get; set; }
        public int EntryStochasticScore { get; set; }
        public int EntryObvScore { get; set; }
        public int EntryCciScore { get; set; }
        public int EntryWilliamsRScore { get; set; }

        // Sentiment at entry (from news/earnings)
        public int EntrySentimentScore { get; set; }
        public int EntrySentimentConfidence { get; set; }

        // Additional indicator values at entry (for backtest learning)
        public double MacdHistogramAtEntry { get; set; }
        public double VolumeRatioAtEntry { get; set; }
        public bool AboveVwapAtEntry { get; set; }
        public bool AboveEma9AtEntry { get; set; }
        public bool AboveEma21AtEntry { get; set; }

        // Indicator values at exit
        public int ExitScore { get; set; }
        public double RsiAtExit { get; set; }
        public double AdxAtExit { get; set; }

        /// <summary>Exit reason: TP, SL, score, END</summary>
        public string ExitReason { get; set; } = "score";

        /// <summary>
        /// Gets the age of this trade in days from the backtest end date.
        /// Used for time-weighted learning.
        /// </summary>
        [JsonIgnore]
        public int AgeDays { get; set; }

        // Outcome (calculated)
        public double PnL => IsLong
            ? (ExitPrice - EntryPrice) * Quantity
            : (EntryPrice - ExitPrice) * Quantity;

        public double PnLPercent => IsLong
            ? (ExitPrice - EntryPrice) / EntryPrice * 100
            : (EntryPrice - ExitPrice) / EntryPrice * 100;

        public bool IsWin => PnL > 0;

        public TimeSpan Duration => ExitTime - EntryTime;

        public int EntryHour => EntryTime.Hour;
        public int EntryMinute => EntryTime.Minute;
        
        /// <summary>Day of week for pattern analysis.</summary>
        [JsonIgnore]
        public DayOfWeek DayOfWeek => EntryTime.DayOfWeek;

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
                TotalProfit += trade.PnL;
            }
            else
            {
                Losses++;
                TotalLoss += trade.PnL; // Will be negative
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
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Backtest date range
        public DateTime? BacktestStartDate { get; set; }
        public DateTime? BacktestEndDate { get; set; }
        public int BacktestDays { get; set; }

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
        public double TotalPnL => TotalProfit + TotalLoss;

        // Threshold-specific statistics
        public Dictionary<int, ThresholdStats> LongEntryThresholdStats { get; set; } = new();
        public Dictionary<int, ThresholdStats> ShortEntryThresholdStats { get; set; } = new();
        public Dictionary<int, ThresholdStats> ExitScoreStats { get; set; } = new();

        // Time-of-day patterns
        public Dictionary<string, TimeWindowStats> TimeWindowStats { get; set; } = new();

        // Indicator correlations
        public List<IndicatorCorrelation> IndicatorCorrelations { get; set; } = new();

        // Learned optimal values (adjusted over time)
        public int OptimalLongEntryThreshold { get; set; } = TradingDefaults.LongEntryThreshold;
        public int OptimalShortEntryThreshold { get; set; } = TradingDefaults.ShortEntryThreshold;
        public int OptimalLongExitThreshold { get; set; } = TradingDefaults.LongExitThreshold;
        public int OptimalShortExitThreshold { get; set; } = TradingDefaults.ShortExitThreshold;

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

        // Historical metadata reference (loaded separately)
        [JsonIgnore]
        public HistoricalMetadata? HistoricalMetadata { get; set; }

        /// <summary>Gets whether historical metadata is available.</summary>
        [JsonIgnore]
        public bool HasHistoricalMetadata => HistoricalMetadata != null;

        // Time patterns (for backtest compatibility)
        public List<int> BestHours { get; set; } = new();
        public List<int> AvoidHours { get; set; } = new();

        /// <summary>
        /// Gets the adjusted entry threshold based on learned patterns.
        /// Blends learned value with default based on confidence.
        /// </summary>
        public double GetAdjustedLongEntryThreshold(double defaultThreshold)
        {
            double confidence = Math.Min(TotalTrades / 50.0, 1.0);
            return OptimalLongEntryThreshold * confidence + defaultThreshold * (1 - confidence);
        }

        /// <summary>
        /// Checks if a given hour should be avoided based on historical data.
        /// </summary>
        public bool ShouldAvoidHour(int hour)
        {
            double confidence = Math.Min(TotalTrades / 50.0, 1.0);
            return confidence > 0.3 && AvoidHours.Contains(hour);
        }

        /// <summary>
        /// Gets a risk multiplier based on current streak.
        /// More conservative after losses, slightly aggressive after wins.
        /// </summary>
        public double GetStreakRiskMultiplier()
        {
            if (CurrentStreak <= -3)
                return 0.5;  // Reduce position size after 3+ losses
            if (CurrentStreak <= -2)
                return 0.75;
            if (CurrentStreak >= 5)
                return 0.9;  // Slightly reduce to protect gains
            return 1.0;
        }

        /// <summary>
        /// Records a completed trade and updates all statistics.
        /// </summary>
        public void RecordTrade(TradeRecord trade)
        {
            UpdatedAt = DateTime.UtcNow;

            // Add to recent trades (maintain max size)
            RecentTrades.Add(trade);
            if (RecentTrades.Count > MaxRecentTrades)
                RecentTrades.RemoveAt(0);

            // Update overall stats
            TotalTrades++;
            if (trade.IsWin)
            {
                TotalWins++;
                TotalProfit += trade.PnL;
                CurrentStreak = CurrentStreak >= 0 ? CurrentStreak + 1 : 1;
                LongestWinStreak = Math.Max(LongestWinStreak, CurrentStreak);
            }
            else
            {
                TotalLosses++;
                TotalLoss += trade.PnL; // Negative value
                CurrentStreak = CurrentStreak <= 0 ? CurrentStreak - 1 : -1;
                LongestLossStreak = Math.Max(LongestLossStreak, Math.Abs(CurrentStreak));
            }

            // Update long/short stats
            if (trade.IsLong)
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
            BestTradeProfit = Math.Max(BestTradeProfit, trade.PnL);
            WorstTradeLoss = Math.Min(WorstTradeLoss, trade.PnL);

            // Update averages
            AverageTradeDurationMinutes = RecentTrades.Average(t => t.Duration.TotalMinutes);
            AverageWinAmount = RecentTrades.Where(t => t.IsWin).Select(t => t.PnL).DefaultIfEmpty(0).Average();
            AverageLossAmount = RecentTrades.Where(t => !t.IsWin).Select(t => t.PnL).DefaultIfEmpty(0).Average();

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

            if (trade.IsLong)
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
            stats.TotalProfit += trade.PnL;
        }

        private void UpdateIndicatorCorrelations(TradeRecord trade)
        {
            // RSI Oversold entries
            if (trade.RsiAtEntry <= 30)
            {
                UpdateCorrelation("RSI", "Oversold (<=30)", trade);
            }
            else if (trade.RsiAtEntry >= 70)
            {
                UpdateCorrelation("RSI", "Overbought (>=70)", trade);
            }
            else if (trade.RsiAtEntry >= 50)
            {
                UpdateCorrelation("RSI", "Bullish (50-70)", trade);
            }
            else
            {
                UpdateCorrelation("RSI", "Bearish (30-50)", trade);
            }

            // ADX Trend Strength
            if (trade.AdxAtEntry >= 40)
            {
                UpdateCorrelation("ADX", "Very Strong (>=40)", trade);
            }
            else if (trade.AdxAtEntry >= 25)
            {
                UpdateCorrelation("ADX", "Strong Trend (>=25)", trade);
            }
            else
            {
                UpdateCorrelation("ADX", "Weak Trend (<25)", trade);
            }

            // MACD direction
            if (trade.EntryMacdScore > 50)
            {
                UpdateCorrelation("MACD", "Strong Bullish (>50)", trade);
            }
            else if (trade.EntryMacdScore > 0)
            {
                UpdateCorrelation("MACD", "Bullish", trade);
            }
            else if (trade.EntryMacdScore < -50)
            {
                UpdateCorrelation("MACD", "Strong Bearish (<-50)", trade);
            }
            else
            {
                UpdateCorrelation("MACD", "Bearish", trade);
            }

            // Volume analysis
            if (trade.EntryVolumeScore > 75)
            {
                UpdateCorrelation("Volume", "Very High (>2x avg)", trade);
            }
            else if (trade.EntryVolumeScore > 50)
            {
                UpdateCorrelation("Volume", "High (>1.5x avg)", trade);
            }
            else if (trade.EntryVolumeScore < -25)
            {
                UpdateCorrelation("Volume", "Low (<avg)", trade);
            }
            
            // Bollinger Bands
            if (trade.EntryBollingerScore > 50)
            {
                UpdateCorrelation("Bollinger", "Near Lower Band (oversold)", trade);
            }
            else if (trade.EntryBollingerScore < -50)
            {
                UpdateCorrelation("Bollinger", "Near Upper Band (overbought)", trade);
            }
            
            // Stochastic
            if (trade.EntryStochasticScore > 75)
            {
                UpdateCorrelation("Stochastic", "Oversold Bounce", trade);
            }
            else if (trade.EntryStochasticScore < -75)
            {
                UpdateCorrelation("Stochastic", "Overbought Reversal", trade);
            }
            
            // CCI
            if (trade.EntryCciScore > 50)
            {
                UpdateCorrelation("CCI", "Oversold Zone", trade);
            }
            else if (trade.EntryCciScore < -50)
            {
                UpdateCorrelation("CCI", "Overbought Zone", trade);
            }
            
            // Williams %R
            if (trade.EntryWilliamsRScore > 50)
            {
                UpdateCorrelation("Williams%R", "Oversold Signal", trade);
            }
            else if (trade.EntryWilliamsRScore < -50)
            {
                UpdateCorrelation("Williams%R", "Overbought Signal", trade);
            }
            
            // OBV (On-Balance Volume)
            if (trade.EntryObvScore > 25)
            {
                UpdateCorrelation("OBV", "Rising (bullish)", trade);
            }
            else if (trade.EntryObvScore < -25)
            {
                UpdateCorrelation("OBV", "Falling (bearish)", trade);
            }
            
            // Sentiment (if available)
            if (trade.EntrySentimentConfidence >= 50)
            {
                if (trade.EntrySentimentScore >= 50)
                {
                    UpdateCorrelation("Sentiment", "Strong Bullish (>=50)", trade);
                }
                else if (trade.EntrySentimentScore >= 25)
                {
                    UpdateCorrelation("Sentiment", "Bullish (25-50)", trade);
                }
                else if (trade.EntrySentimentScore <= -50)
                {
                    UpdateCorrelation("Sentiment", "Strong Bearish (<=-50)", trade);
                }
                else if (trade.EntrySentimentScore <= -25)
                {
                    UpdateCorrelation("Sentiment", "Bearish (-50 to -25)", trade);
                }
                else
                {
                    UpdateCorrelation("Sentiment", "Neutral (-25 to 25)", trade);
                }
            }
            
            // VWAP position
            if (trade.EntryVwapScore > 50)
            {
                UpdateCorrelation("VWAP", "Well Above (bullish)", trade);
            }
            else if (trade.EntryVwapScore > 0)
            {
                UpdateCorrelation("VWAP", "Above (bullish)", trade);
            }
            else if (trade.EntryVwapScore < -50)
            {
                UpdateCorrelation("VWAP", "Well Below (bearish)", trade);
            }
            else if (trade.EntryVwapScore < 0)
            {
                UpdateCorrelation("VWAP", "Below (bearish)", trade);
            }
            
            // EMA stack
            if (trade.EntryEmaScore > 75)
            {
                UpdateCorrelation("EMA", "Bullish Stack (above all)", trade);
            }
            else if (trade.EntryEmaScore > 25)
            {
                UpdateCorrelation("EMA", "Bullish Bias", trade);
            }
            else if (trade.EntryEmaScore < -75)
            {
                UpdateCorrelation("EMA", "Bearish Stack (below all)", trade);
            }
            else if (trade.EntryEmaScore < -25)
            {
                UpdateCorrelation("EMA", "Bearish Bias", trade);
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
            correlation.TotalProfit += trade.PnL;
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
        /// Gets the top performing indicator conditions for this ticker.
        /// </summary>
        /// <param name="minOccurrences">Minimum number of occurrences to consider.</param>
        /// <param name="count">Number of top conditions to return.</param>
        public List<IndicatorCorrelation> GetBestIndicatorConditions(int minOccurrences = 5, int count = 10)
        {
            return IndicatorCorrelations
                .Where(c => c.Occurrences >= minOccurrences)
                .OrderByDescending(c => c.WinRate * (c.AverageProfit > 0 ? 1.5 : 1))
                .ThenByDescending(c => c.WinRate)
                .Take(count)
                .ToList();
        }
        
        /// <summary>
        /// Gets the worst performing indicator conditions to avoid.
        /// </summary>
        public List<IndicatorCorrelation> GetWorstIndicatorConditions(int minOccurrences = 5, int count = 10)
        {
            return IndicatorCorrelations
                .Where(c => c.Occurrences >= minOccurrences && c.WinRate < 45)
                .OrderBy(c => c.WinRate)
                .ThenBy(c => c.AverageProfit)
                .Take(count)
                .ToList();
        }
        
        /// <summary>
        /// Gets indicator weight adjustments based on historical performance.
        /// Returns a multiplier (0.5 to 1.5) for each indicator based on its effectiveness.
        /// </summary>
        public Dictionary<string, double> GetIndicatorWeightAdjustments()
        {
            var weights = new Dictionary<string, double>();
            
            // Group correlations by indicator
            var byIndicator = IndicatorCorrelations
                .GroupBy(c => c.IndicatorName)
                .ToDictionary(g => g.Key, g => g.ToList());
                
            foreach (var kvp in byIndicator)
            {
                string indicator = kvp.Key;
                var conditions = kvp.Value;
                
                int totalOccurrences = conditions.Sum(c => c.Occurrences);
                if (totalOccurrences < 10)
                {
                    weights[indicator] = 1.0; // Default weight
                    continue;
                }
                
                // Calculate weighted win rate for this indicator
                double weightedWinRate = conditions.Sum(c => c.WinRate * c.Occurrences) / totalOccurrences;
                
                // Convert to multiplier: 50% win rate = 1.0, 60% = 1.2, 70% = 1.4, 40% = 0.8
                double multiplier = 1.0 + (weightedWinRate - 50) * 0.02;
                weights[indicator] = Math.Clamp(multiplier, 0.5, 1.5);
            }
            
            return weights;
        }
        
        /// <summary>
        /// Gets a sentiment effectiveness score based on historical trades with sentiment data.
        /// Returns how much sentiment should be weighted in decisions.
        /// </summary>
        public double GetSentimentEffectiveness()
        {
            var sentimentTrades = RecentTrades
                .Where(t => t.EntrySentimentConfidence >= 50)
                .ToList();
                
            if (sentimentTrades.Count < 10)
                return 0.5; // Not enough data, use moderate weight
                
            // Check if trading with sentiment direction improves results
            var withSentiment = sentimentTrades
                .Where(t => (t.IsLong && t.EntrySentimentScore > 0) || (!t.IsLong && t.EntrySentimentScore < 0))
                .ToList();
                
            var againstSentiment = sentimentTrades
                .Where(t => (t.IsLong && t.EntrySentimentScore < 0) || (!t.IsLong && t.EntrySentimentScore > 0))
                .ToList();
                
            if (withSentiment.Count < 5 || againstSentiment.Count < 5)
                return 0.5;
                
            double withWinRate = withSentiment.Count(t => t.IsWin) / (double)withSentiment.Count;
            double againstWinRate = againstSentiment.Count(t => t.IsWin) / (double)againstSentiment.Count;
            
            // If trading with sentiment is significantly better, increase weight
            if (withWinRate > againstWinRate + 0.1)
                return Math.Min(1.0, 0.5 + (withWinRate - againstWinRate));
                
            // If trading against sentiment is better, reduce sentiment weight
            if (againstWinRate > withWinRate + 0.1)
                return Math.Max(0.1, 0.5 - (againstWinRate - withWinRate));
                
            return 0.5;
        }
        
        /// <summary>
        /// Gets the best time windows for trading this ticker.
        /// </summary>
        public List<TimeWindowStats> GetBestTimeWindows(int minTrades = 5)
        {
            return TimeWindowStats.Values
                .Where(w => w.TotalTrades >= minTrades)
                .OrderByDescending(w => w.WinRate)
                .ThenByDescending(w => w.AverageProfit)
                .Take(5)
                .ToList();
        }
        
        /// <summary>
        /// Gets time windows to avoid based on poor historical performance.
        /// </summary>
        public List<string> GetBadTimeWindows(int minTrades = 5, double maxWinRate = 40)
        {
            return TimeWindowStats.Values
                .Where(w => w.TotalTrades >= minTrades && w.WinRate < maxWinRate)
                .Select(w => w.TimeBucket)
                .ToList();
        }

        /// <summary>
        /// Gets a summary string of the ticker profile for logging.
        /// </summary>
        public string GetSummary()
        {
            return $"{Symbol}: {TotalTrades} trades, {WinRate:F1}% win rate, " +
                   $"PF={ProfitFactor:F2}, Net=${TotalPnL:F2}, " +
                   $"Conf={Confidence}%, OptLong={OptimalLongEntryThreshold}, OptShort={OptimalShortEntryThreshold}";
        }

        // ====================================================================
        // Time-Weighted Learning
        // ====================================================================

        /// <summary>
        /// Half-life in days for exponential decay weighting.
        /// Trades 7 days old have half the weight of today's trades.
        /// </summary>
        public const double DecayHalfLifeDays = 7.0;

        /// <summary>
        /// Calculates the time decay weight for a trade.
        /// Recent trades get higher weight (closer to 1.0).
        /// Older trades decay exponentially.
        /// </summary>
        /// <param name="tradeDate">The date of the trade.</param>
        /// <param name="referenceDate">The reference date (usually backtest end or now).</param>
        /// <returns>Weight between 0 and 1.</returns>
        public static double CalculateTimeWeight(DateTime tradeDate, DateTime referenceDate)
        {
            double daysDiff = (referenceDate - tradeDate).TotalDays;
            if (daysDiff < 0) daysDiff = 0;

            // Exponential decay: weight = 0.5^(days/halfLife)
            return Math.Pow(0.5, daysDiff / DecayHalfLifeDays);
        }

        /// <summary>
        /// Recalculates all statistics using time-weighted learning.
        /// Recent trades are weighted more heavily than older trades.
        /// </summary>
        /// <param name="referenceDate">The reference date for decay calculation.</param>
        public void RecalculateWithTimeWeighting(DateTime? referenceDate = null)
        {
            if (RecentTrades.Count == 0) return;

            var refDate = referenceDate ?? RecentTrades.Max(t => t.ExitTime);

            // Calculate time-weighted win rate
            double weightedWins = 0;
            double weightedLosses = 0;
            double weightedProfit = 0;

            foreach (var trade in RecentTrades)
            {
                double weight = CalculateTimeWeight(trade.EntryTime, refDate);

                if (trade.IsWin)
                    weightedWins += weight;
                else
                    weightedLosses += weight;

                weightedProfit += trade.PnL * weight;
            }

            double totalWeight = weightedWins + weightedLosses;
            double timeWeightedWinRate = totalWeight > 0 ? (weightedWins / totalWeight) * 100 : 50;

            // Recalculate optimal thresholds with time weighting
            RecalculateOptimalThresholdsWithTimeWeighting(refDate);

            // Store the time-weighted metrics
            TimeWeightedWinRate = timeWeightedWinRate;
            TimeWeightedNetProfit = weightedProfit;
        }

        /// <summary>
        /// Time-weighted win rate (recent trades count more).
        /// </summary>
        [JsonIgnore]
        public double TimeWeightedWinRate { get; private set; }

        /// <summary>
        /// Time-weighted net profit (recent trades count more).
        /// </summary>
        [JsonIgnore]
        public double TimeWeightedNetProfit { get; private set; }

        /// <summary>
        /// Recalculates optimal thresholds using time-weighted statistics.
        /// </summary>
        private void RecalculateOptimalThresholdsWithTimeWeighting(DateTime referenceDate)
        {
            if (RecentTrades.Count < 10) return;

            // Group trades by score bucket with time weighting
            var longScoreBuckets = new Dictionary<int, (double weightedWins, double weightedTotal, double weightedProfit)>();
            var shortScoreBuckets = new Dictionary<int, (double weightedWins, double weightedTotal, double weightedProfit)>();

            foreach (var trade in RecentTrades)
            {
                double weight = CalculateTimeWeight(trade.EntryTime, referenceDate);
                int scoreBucket = (trade.EntryScore / 5) * 5;

                var buckets = trade.IsLong ? longScoreBuckets : shortScoreBuckets;

                if (!buckets.ContainsKey(scoreBucket))
                    buckets[scoreBucket] = (0, 0, 0);

                var current = buckets[scoreBucket];
                buckets[scoreBucket] = (
                    current.weightedWins + (trade.IsWin ? weight : 0),
                    current.weightedTotal + weight,
                    current.weightedProfit + trade.PnL * weight
                );
            }

            // Find optimal long threshold
            var bestLong = longScoreBuckets
                .Where(kvp => kvp.Value.weightedTotal >= 2.0) // Minimum weighted sample
                .Select(kvp => new
                {
                    Threshold = kvp.Key,
                    WinRate = kvp.Value.weightedTotal > 0 ? kvp.Value.weightedWins / kvp.Value.weightedTotal * 100 : 0,
                    Expectancy = kvp.Value.weightedTotal > 0 ? kvp.Value.weightedProfit / kvp.Value.weightedTotal : 0
                })
                .Where(x => x.WinRate > 55)
                .OrderByDescending(x => x.Expectancy)
                .ThenByDescending(x => x.WinRate)
                .FirstOrDefault();

            if (bestLong != null)
                OptimalLongEntryThreshold = bestLong.Threshold;

            // Find optimal short threshold
            var bestShort = shortScoreBuckets
                .Where(kvp => kvp.Value.weightedTotal >= 2.0)
                .Select(kvp => new
                {
                    Threshold = kvp.Key,
                    WinRate = kvp.Value.weightedTotal > 0 ? kvp.Value.weightedWins / kvp.Value.weightedTotal * 100 : 0,
                    Expectancy = kvp.Value.weightedTotal > 0 ? kvp.Value.weightedProfit / kvp.Value.weightedTotal : 0
                })
                .Where(x => x.WinRate > 55)
                .OrderByDescending(x => x.Expectancy)
                .ThenByDescending(x => x.WinRate)
                .FirstOrDefault();

            if (bestShort != null)
                OptimalShortEntryThreshold = bestShort.Threshold;
        }

        // ====================================================================
        // Sentiment Data
        // ====================================================================

        /// <summary>
        /// Latest sentiment score for this ticker (-100 to +100).
        /// </summary>
        public int LastSentimentScore { get; set; }

        /// <summary>
        /// Confidence in the sentiment score (0-100).
        /// </summary>
        public int LastSentimentConfidence { get; set; }

        /// <summary>
        /// When the sentiment was last updated.
        /// </summary>
        public DateTime LastSentimentUpdate { get; set; }

        /// <summary>
        /// Updates the sentiment data for this ticker.
        /// </summary>
        public void UpdateSentiment(int score, int confidence)
        {
            LastSentimentScore = score;
            LastSentimentConfidence = confidence;
            LastSentimentUpdate = DateTime.UtcNow;
        }

        /// <summary>
        /// Gets whether sentiment data is fresh (within 1 hour).
        /// </summary>
        [JsonIgnore]
        public bool HasFreshSentiment => (DateTime.UtcNow - LastSentimentUpdate).TotalHours < 1;

        /// <summary>
        /// Gets the sentiment adjustment for the market score.
        /// Weighted by confidence.
        /// </summary>
        public int GetSentimentAdjustment()
        {
            if (!HasFreshSentiment || LastSentimentConfidence < 25)
                return 0;

            // Weight by confidence and cap at +/- 15 points
            double adjustment = LastSentimentScore * (LastSentimentConfidence / 100.0) * 0.15;
            return (int)Math.Clamp(adjustment, -15, 15);
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
                lock (_lock)
                {
                    _profiles[profile.Symbol.ToUpperInvariant()] = profile;
                }

                string filePath = Path.Combine(_profileDirectory, $"{profile.Symbol}.json");
                string json = JsonSerializer.Serialize(profile, JsonOptions);
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                ConsoleLog.Warn("Profile", $"Failed to save ticker profile for {profile.Symbol}: {ex.Message}");
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
                        ConsoleLog.Warn("Profile", $"Failed to load ticker profile from {file}: {ex.Message}");
                    }
                }

                if (_profiles.Count > 0)
                {
                    ConsoleLog.Success("Profile", $"Loaded {_profiles.Count} ticker profiles from disk");
                }
            }
            catch (Exception ex)
            {
                ConsoleLog.Warn("Profile", $"Failed to load ticker profiles: {ex.Message}");
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
                double netProfit = _profiles.Values.Sum(p => p.TotalPnL);
                double winRate = totalTrades > 0 ? (double)totalWins / totalTrades * 100 : 0;

                return $"Overall: {_profiles.Count} tickers, {totalTrades} trades, " +
                       $"{winRate:F1}% win rate, Net P&L: ${netProfit:F2}";
            }
        }

    }
}
