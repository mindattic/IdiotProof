// ============================================================================
// BacktestService - Autonomous Trading Learning via Historical Data
// ============================================================================
//
// PURPOSE:
// Fetches historical data from IBKR and runs autonomous trading simulation
// to build TickerProfiles for learning-based trading decisions.
//
// USAGE:
// Called via IPC from Console with RunBacktestRequest payload.
// Returns BacktestResponsePayload with simulation results.
//
// ============================================================================

using IdiotProof.Calculators;
using IdiotProof.Constants;
using IdiotProof.Enums;
using IdiotProof.Helpers;
using IdiotProof.Logging;
using IdiotProof.Models;
using IdiotProof.Strategy;

namespace IdiotProof.Services {
    /// <summary>
    /// Service for running autonomous trading backtests and building TickerProfiles.
    /// </summary>
    public sealed class BacktestService
    {
        private readonly HistoricalDataService historicalDataService;
        private readonly HistoricalDataCache cache = new();

        public BacktestService(HistoricalDataService historicalDataService)
        {
            this.historicalDataService = historicalDataService ?? throw new ArgumentNullException(nameof(historicalDataService));
        }

        /// <summary>
        /// Runs an autonomous learning backtest for a symbol.
        /// </summary>
        /// <param name="request">The backtest request configuration.</param>
        /// <returns>Backtest response with results.</returns>
        public async Task<BacktestResponsePayload> RunBacktestAsync(RunBacktestRequest request)
        {
            try
            {
                ConsoleLog.Write("Backtest", $"Starting backtest for {request.Symbol}, {request.Days} days, mode: {request.Mode}");

                // Calculate bars needed for the requested days
                // 1 trading day = ~390 minutes RTH or ~960 minutes extended hours
                // We'll use extended hours data
                int barsPerDay = 960; // Extended hours: 4am-8pm = 16 hours x 60 min
                int totalBarsNeeded = request.Days * barsPerDay;
                const int minimumViableBars = 255; // Minimum for EMA200 warmup

                var allBars = new List<HistoricalBar>();
                bool cacheWasUpdated = false;

                // STEP 1: Load cached data first (always use local data when available)
                var cachedBars = cache.LoadFromCache(request.Symbol);
                if (cachedBars != null && cachedBars.Count > 0)
                {
                    allBars = cachedBars;
                    var lastBarTime = cachedBars.Max(b => b.Time);
                    var firstBarTime = cachedBars.Min(b => b.Time);
                    ConsoleLog.Write("Backtest", $"Loaded {cachedBars.Count} bars from cache ({firstBarTime:MM/dd HH:mm} to {lastBarTime:MM/dd HH:mm})");

                    // STEP 2a: Check if we need to backfill RECENT data (forward fill)
                    var timeSinceLastBar = DateTime.Now - lastBarTime;
                    var hoursGap = timeSinceLastBar.TotalHours;
                    
                    // Backfill if gap is more than 2 hours (account for market closed periods)
                    if (hoursGap > 2)
                    {
                        int daysToBackfill = Math.Min((int)Math.Ceiling(hoursGap / 24.0) + 1, 5); // Max 5 days per request
                        ConsoleLog.Write("Backtest", $"Cache is {hoursGap:F1} hours old, backfilling {daysToBackfill} days from IBKR...");

                        try
                        {
                            int barsFetched = await historicalDataService.FetchHistoricalDataAsync(
                                request.Symbol,
                                barCount: daysToBackfill * barsPerDay,
                                barSize: BarSize.Minutes1,
                                dataType: HistoricalDataType.Trades,
                                useRTH: false,
                                endDate: null); // null = up to now

                            if (barsFetched > 0)
                            {
                                var fetchedBars = historicalDataService.Store.GetBars(request.Symbol);
                                if (fetchedBars != null)
                                {
                                    int newBarsAdded = 0;
                                    foreach (var bar in fetchedBars)
                                    {
                                        // Only add bars newer than what we have
                                        if (bar.Time > lastBarTime && !allBars.Any(b => b.Time == bar.Time))
                                        {
                                            allBars.Add(bar);
                                            newBarsAdded++;
                                        }
                                    }
                                    if (newBarsAdded > 0)
                                    {
                                        cacheWasUpdated = true;
                                        ConsoleLog.Write("Backtest", $"Added {newBarsAdded} new bars from IBKR (forward backfill complete)");
                                    }
                                    else
                                    {
                                        ConsoleLog.Write("Backtest", "No new bars to add (market may be closed)");
                                    }
                                }
                            }
                        }
                        catch (TimeoutException)
                        {
                            ConsoleLog.Warn("Backtest", "IBKR forward backfill timed out - using cached data only");
                        }
                        catch (Exception ex)
                        {
                            ConsoleLog.Warn("Backtest", $"IBKR forward backfill failed ({ex.Message}) - using cached data only");
                        }
                    }
                    else
                    {
                        ConsoleLog.Write("Backtest", $"Cache is fresh ({hoursGap:F1} hours old), no forward backfill needed");
                    }

                    // STEP 2b: Check if we need to backfill OLDER data (backward fill)
                    // Calculate how many days of data we currently have
                    int cachedDays = (int)Math.Ceiling((lastBarTime - firstBarTime).TotalDays);
                    int daysNeeded = request.Days;
                    
                    if (cachedDays < daysNeeded)
                    {
                        int daysToFetchBack = daysNeeded - cachedDays;
                        ConsoleLog.Write("Backtest", $"Cache has ~{cachedDays} days, need {daysNeeded} days. Fetching {daysToFetchBack} more days of history...");

                        // Fetch in 5-day chunks going backward
                        DateTime endDate = firstBarTime.AddMinutes(-1); // Start just before our oldest bar
                        int daysRemaining = daysToFetchBack;

                        while (daysRemaining > 0)
                        {
                            int fetchDays = Math.Min(daysRemaining, 5);
                            ConsoleLog.Write("Backtest", $"Fetching {fetchDays} days ending {endDate:MM/dd HH:mm}...");

                            try
                            {
                                int barsFetched = await historicalDataService.FetchHistoricalDataAsync(
                                    request.Symbol,
                                    barCount: fetchDays * barsPerDay,
                                    barSize: BarSize.Minutes1,
                                    dataType: HistoricalDataType.Trades,
                                    useRTH: false,
                                    endDate: endDate);

                                if (barsFetched == 0)
                                {
                                    ConsoleLog.Write("Backtest", "No more historical data available from IBKR");
                                    break;
                                }

                                var fetchedBars = historicalDataService.Store.GetBars(request.Symbol);
                                if (fetchedBars != null)
                                {
                                    int oldBarsAdded = 0;
                                    foreach (var bar in fetchedBars)
                                    {
                                        // Only add bars older than what we have
                                        if (bar.Time < firstBarTime && !allBars.Any(b => b.Time == bar.Time))
                                        {
                                            allBars.Add(bar);
                                            oldBarsAdded++;
                                        }
                                    }
                                    if (oldBarsAdded > 0)
                                    {
                                        cacheWasUpdated = true;
                                        firstBarTime = allBars.Min(b => b.Time); // Update for next iteration
                                        ConsoleLog.Write("Backtest", $"Added {oldBarsAdded} historical bars (now starting from {firstBarTime:MM/dd HH:mm})");
                                    }
                                    else
                                    {
                                        ConsoleLog.Write("Backtest", "No older bars found in this chunk");
                                        break;
                                    }
                                }

                                daysRemaining -= fetchDays;
                                endDate = endDate.AddDays(-fetchDays);

                                // Respect pacing limits
                                if (daysRemaining > 0)
                                {
                                    await Task.Delay(1000);
                                }
                            }
                            catch (TimeoutException)
                            {
                                ConsoleLog.Warn("Backtest", "IBKR backward backfill timed out - using available data");
                                break;
                            }
                            catch (Exception ex)
                            {
                                ConsoleLog.Warn("Backtest", $"IBKR backward backfill failed ({ex.Message}) - using available data");
                                break;
                            }
                        }
                    }
                }
                else
                {
                    // STEP 2c: No cache - fetch everything from IBKR API
                    ConsoleLog.Write("Backtest", $"No cache found, fetching {request.Days} days from IBKR API...");

                    // IBKR limits: 1 min bars max "5 D" per request
                    // For 30 days, we need multiple requests
                    int daysRemaining = request.Days;
                    DateTime endDate = DateTime.Now;

                    while (daysRemaining > 0 && allBars.Count < totalBarsNeeded)
                    {
                        int fetchDays = Math.Min(daysRemaining, 5);
                        
                        ConsoleLog.Write("Backtest", $"Fetching {fetchDays} days ending {endDate:yyyy-MM-dd HH:mm}");

                        // Fetch historical data with specific end date
                        int barsFetched = await historicalDataService.FetchHistoricalDataAsync(
                            request.Symbol,
                            barCount: fetchDays * barsPerDay,
                            barSize: BarSize.Minutes1,
                            dataType: HistoricalDataType.Trades,
                            useRTH: false,
                            endDate: endDate);

                        if (barsFetched == 0)
                        {
                            if (allBars.Count == 0)
                            {
                                return new BacktestResponsePayload
                                {
                                    Success = false,
                                    Symbol = request.Symbol,
                                    ErrorMessage = $"Failed to fetch historical data for {request.Symbol}"
                                };
                            }
                            break; // Use what we have
                        }

                        // Get bars from store
                        var fetchedBars = historicalDataService.Store.GetBars(request.Symbol);
                        if (fetchedBars != null && fetchedBars.Count > 0)
                        {
                            // Add only bars we don't already have
                            foreach (var bar in fetchedBars)
                            {
                                if (!allBars.Any(b => b.Time == bar.Time))
                                {
                                    allBars.Add(bar);
                                }
                            }
                        }

                        daysRemaining -= fetchDays;
                        endDate = endDate.AddDays(-fetchDays);

                        // Respect pacing limits
                        if (daysRemaining > 0)
                        {
                            await Task.Delay(1000); // Wait 1 second between requests
                        }
                    }

                    // Sort bars by time
                    allBars = allBars.OrderBy(b => b.Time).ToList();

                    // STEP 3: Save to cache for future use
                    if (allBars.Count > 0)
                    {
                        cache.SaveToCache(request.Symbol, allBars);
                        ConsoleLog.Write("Backtest", $"Saved {allBars.Count} bars to {request.Symbol}.history.json");
                    }
                }

                // Save updated cache if we backfilled new data
                if (cacheWasUpdated && allBars.Count > 0)
                {
                    allBars = allBars.OrderBy(b => b.Time).ToList();
                    cache.SaveToCache(request.Symbol, allBars);
                    ConsoleLog.Write("Backtest", $"Updated cache with {allBars.Count} total bars");
                }

                ConsoleLog.Write("Backtest", $"Using {allBars.Count} total bars for {request.Symbol}");

                if (allBars.Count < 50)
                {
                    return new BacktestResponsePayload
                    {
                        Success = false,
                        Symbol = request.Symbol,
                        ErrorMessage = $"Insufficient data: only {allBars.Count} bars (need at least 50)"
                    };
                }

                // Check for existing profile to use learned thresholds
                var existingProfile = StrategyRunner.ProfileManager.GetProfile(request.Symbol);

                // Run the simulation with adaptive config
                var (profile, detailedLog) = RunAutonomousLearning(
                    request.Symbol,
                    allBars,
                    request.Allocation,
                    existingProfile,
                    verboseLogging: true,
                    useAiConfirmation: request.UseAiConfirmation);

                // Calculate results
                int totalTrades = profile.TotalTrades;
                int winningTrades = profile.TotalWins;
                double winRate = profile.WinRate;
                double totalPnL = profile.TotalPnL;
                double avgPnL = totalTrades > 0 ? totalPnL / totalTrades : 0;

                // Save profile if requested
                bool profileSaved = false;
                if (request.SaveProfile && totalTrades > 0)
                {
                    StrategyRunner.ProfileManager.SaveProfile(profile);
                    profileSaved = true;
                    ConsoleLog.Write("Backtest", $"Profile saved for {request.Symbol}");
                }

                ConsoleLog.Success("Backtest", $"Complete: {totalTrades} trades, {winRate:F1}% win rate, ${totalPnL:F2} total P&L");

                return new BacktestResponsePayload
                {
                    Success = true,
                    Symbol = request.Symbol,
                    TotalTrades = totalTrades,
                    WinningTrades = winningTrades,
                    WinRate = winRate,
                    TotalPnL = totalPnL,
                    AvgPnL = avgPnL,
                    BarsProcessed = allBars.Count,
                    ProfileSaved = profileSaved,
                    Confidence = profile.Confidence,
                    DetailedTradeLog = detailedLog,
                    EndingCapital = (decimal)(request.Allocation + totalPnL),
                    ReturnPercent = request.Allocation > 0 ? totalPnL / request.Allocation * 100 : 0
                };
            }
            catch (Exception ex)
            {
                ConsoleLog.Error("Backtest", ex.Message);
                return new BacktestResponsePayload
                {
                    Success = false,
                    Symbol = request.Symbol,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// Runs autonomous trading simulation against historical data and populates a TickerProfile.
        /// </summary>
        private static (TickerProfile Profile, string DetailedLog) RunAutonomousLearning(
            string symbol,
            IReadOnlyList<HistoricalBar> bars,
            double allocation = 1000.0,
            TickerProfile? existingProfile = null,
            bool verboseLogging = false,
            bool useAiConfirmation = true)
        {
            if (bars.Count < 50)
            {
                ConsoleLog.Warn("Backtest", $"Need at least 50 bars for autonomous learning, got {bars.Count}");
                return (new TickerProfile { Symbol = symbol }, "");
            }

            // Create adaptive simulator that uses learned thresholds from profile
            var simulator = new AutonomousLearningSimulator(symbol, allocation, existingProfile);

            // Enable ChatGPT confirmation if requested
            if (useAiConfirmation)
            {
                simulator.EnableChatGptConfirmation();
                ConsoleLog.Write("Learn", $"ChatGPT confirmation ENABLED for {symbol}");
            }

            if (verboseLogging)
            {
                ConsoleLog.Write("Learn", $"Running autonomous learning for {symbol} with {bars.Count} bars...");
                ConsoleLog.Write("Learn", $"Date range: {bars[0].Time:yyyy-MM-dd} to {bars[^1].Time:yyyy-MM-dd}");
                Console.WriteLine();
            }

            // Track day number for logging
            DateTime? currentDay = null;
            int dayNumber = 0;
            
            // Collect all trades with their dates for detailed log
            var allTrades = new List<(int DayNum, DateTime Date, Strategy.TradeRecord Trade)>();

            // Process each bar (oldest to newest - bars should already be sorted)
            foreach (var bar in bars)
            {
                // Check if we've moved to a new trading day
                if (bar.Time.Date != currentDay)
                {
                    currentDay = bar.Time.Date;
                    dayNumber++;
                    if (verboseLogging)
                        ConsoleLog.Write("Learn", $"--- Day {dayNumber}: {bar.Time:ddd MM/dd/yyyy} ---");
                }

                var trades = simulator.ProcessBar(bar);
                
                // Collect trades for detailed log
                foreach (var trade in trades)
                {
                    allTrades.Add((dayNumber, bar.Time.Date, trade));
                }

                if (verboseLogging)
                {
                    foreach (var trade in trades)
                    {
                        var icon = trade.IsWin ? "[OK]" : "[--]";
                        var reasonTag = trade.ExitReason switch
                        {
                            "TP" => " TP",
                            "SL" => " SL",
                            _ => ""
                        };
                        
                        // Show clear direction: LONG = Buy->Sell, SHORT = Short->Cover
                        var direction = trade.IsLong ? "LONG " : "SHORT";
                        var entryAction = trade.IsLong ? "Buy  " : "Short";
                        var exitAction = trade.IsLong ? "Sell " : "Cover";
                        var pnlSign = trade.PnL >= 0 ? "+" : "";
                        
                        ConsoleLog.Write("Learn", $"{icon}{reasonTag} {direction} | {entryAction} {trade.EntryTime:h:mm tt} @ ${trade.EntryPrice:F2} | " +
                            $"{exitAction} {trade.ExitTime:h:mm tt} @ ${trade.ExitPrice:F2} | " +
                            $"{pnlSign}${trade.PnL:F2} ({pnlSign}{trade.PnLPercent:F2}%)");
                    }
                }
            }

            // Close any open position
            if (simulator.HasOpenPosition)
            {
                var finalTrade = simulator.ClosePosition(bars[^1].Close, bars[^1].Time);
                if (finalTrade != null)
                {
                    allTrades.Add((dayNumber, bars[^1].Time.Date, finalTrade));
                    if (verboseLogging)
                        ConsoleLog.Write("Learn", $"[END] Closed at end of data: ${finalTrade.PnL:F2}");
                }
            }

            var profile = simulator.GetProfile();

            // Save backtest date range to profile
            if (bars.Count > 0)
            {
                profile.BacktestStartDate = bars[0].Time;
                profile.BacktestEndDate = bars[^1].Time;
                profile.BacktestDays = dayNumber;

                // Apply time-weighted learning
                profile.RecalculateWithTimeWeighting(bars[^1].Time);
            }

            if (verboseLogging)
            {
                Console.WriteLine();
                ConsoleLog.Success("Learn", $"Complete: {profile.GetSummary()}");
                
                // Show AI confirmation stats if enabled
                var (aiApproved, aiBlocked) = simulator.GetAiStats();
                if (aiApproved + aiBlocked > 0)
                {
                    ConsoleLog.Write("Learn", $"AI Confirmation: {aiApproved} approved, {aiBlocked} blocked");
                }
            }
            
            // Build detailed trade log
            var detailedLog = BuildDetailedTradeLog(symbol, allTrades, profile.TotalPnL);

            return (profile, detailedLog);
        }
        
        /// <summary>
        /// Builds a detailed trade log in the format:
        /// Day 1 (MM/DD/YYYY)
        ///   Buy:   $124.54 @ 10:11 AM
        ///   Sell:  $125.23 @ 10:45 AM
        ///   P/L:   +$0.69
        /// </summary>
        private static string BuildDetailedTradeLog(
            string symbol,
            List<(int DayNum, DateTime Date, Strategy.TradeRecord Trade)> trades,
            double totalPnL)
        {
            var sb = new System.Text.StringBuilder();
            
            sb.AppendLine($"========================================");
            sb.AppendLine($"  {symbol} DETAILED TRADE LOG");
            sb.AppendLine($"========================================");
            sb.AppendLine();
            
            // Group trades by day
            var tradesByDay = trades.GroupBy(t => (t.DayNum, t.Date)).OrderBy(g => g.Key.DayNum);
            
            foreach (var dayGroup in tradesByDay)
            {
                double dayPnL = dayGroup.Sum(t => t.Trade.PnL);
                
                // Only show individual trade details if setting is enabled
                if (Settings.AppSettings.ShowBacktestDailyDetails)
                {
                    sb.AppendLine($"Day {dayGroup.Key.DayNum} ({dayGroup.Key.Date:MM/dd/yyyy})");
                    sb.AppendLine(new string('-', 40));
                    
                    foreach (var (_, _, trade) in dayGroup)
                    {
                        if (trade.IsLong)
                        {
                            sb.AppendLine($"  Buy:   ${trade.EntryPrice,8:F2} @ {trade.EntryTime:h:mm tt}");
                            sb.AppendLine($"  Sell:  ${trade.ExitPrice,8:F2} @ {trade.ExitTime:h:mm tt}");
                        }
                        else
                        {
                            sb.AppendLine($"  Short: ${trade.EntryPrice,8:F2} @ {trade.EntryTime:h:mm tt}");
                            sb.AppendLine($"  Cover: ${trade.ExitPrice,8:F2} @ {trade.ExitTime:h:mm tt}");
                        }
                        
                        var pnlSign = trade.PnL >= 0 ? "+" : "";
                        sb.AppendLine($"  P/L:   {pnlSign}${trade.PnL:F2}");
                        sb.AppendLine();
                    }
                    
                    var dayPnlSign = dayPnL >= 0 ? "+" : "";
                    sb.AppendLine($"  Daily P/L: {dayPnlSign}${dayPnL:F2} ({dayGroup.Count()} trades)");
                    sb.AppendLine();
                }
            }
            
            // Total summary
            sb.AppendLine("========================================");
            var totalSign = totalPnL >= 0 ? "+" : "";
            sb.AppendLine($"TOTAL: {totalSign}${totalPnL:F2} ({trades.Count} trades)");
            sb.AppendLine("========================================");
            
            return sb.ToString();
        }
    }

    /// <summary>
    /// Holds all indicator scores for comprehensive analysis.
    /// </summary>
    internal struct IndicatorScores
    {
        // Core indicators
        public int Total;
        public int Vwap;
        public int Ema;
        public int Rsi;
        public int Macd;
        public int Adx;
        public int Volume;

        // Extended indicators
        public int Bollinger;
        public int Stochastic;
        public int Obv;
        public int Cci;
        public int WilliamsR;
    }

    /// <summary>
    /// Simulates autonomous trading for learning purposes.
    /// Adapts thresholds dynamically based on market conditions and learned profiles.
    /// </summary>
    internal sealed class AutonomousLearningSimulator
    {
        private readonly string symbol;
        private readonly double allocation;
        private int currentTradeQuantity; // Quantity for current trade (calculated at entry)
        private readonly TickerProfile profile;
        private readonly TickerProfile? learnedProfile;

        // Baseline thresholds (adjusted dynamically by ADX and learned profile)
        private static readonly int BaselineLongEntry = TradingDefaults.LongEntryThreshold;
        private static readonly int BaselineShortEntry = TradingDefaults.ShortEntryThreshold;
        private static readonly int BaselineLongExit = TradingDefaults.LongExitThreshold;
        private static readonly int BaselineShortExit = TradingDefaults.ShortExitThreshold;
        private static readonly double BaselineTpAtrMultiplier = TradingDefaults.TpAtrMultiplier;
        private static readonly double BaselineSlAtrMultiplier = TradingDefaults.SlAtrMultiplier;
        private static readonly int MinSecondsBetweenTrades = TradingDefaults.MinSecondsBetweenAdjustments;

        // Position tracking
        private bool inPosition;
        private bool isLong;
        private double entryPrice;
        private DateTime entryTime;
        private DateTime lastTradeTime;
        private int entryScore;
        private int entryVwapScore, entryEmaScore, entryRsiScore, entryMacdScore, entryAdxScore, entryVolumeScore;


        // TP/SL price levels (ATR-based)
        private double takeProfitPrice;
        private double stopLossPrice;



        // VWAP tracking (resets each trading day)
        private double pvSum;
        private double vSum;
        private DateTime? currentVwapDay;

        // Indicator calculators - Core
        private readonly EmaCalculator ema9 = new(9);
        private readonly EmaCalculator ema21 = new(21);
        private readonly EmaCalculator ema34 = new(34);
        private readonly EmaCalculator ema50 = new(50);
        private readonly AdxCalculator adx = new(14, 50);
        private readonly RsiCalculator rsi = new(14);
        private readonly MacdCalculator macd = new(12, 26, 9);
        private readonly VolumeCalculator volume = new(20);
        private readonly AtrCalculator atr = new(14);
        private readonly SmaCalculator sma20 = new(20);
        private readonly SmaCalculator sma50 = new(50);

        // Indicator calculators - Extended (new indicators)
        private readonly BollingerBandsCalculator bollinger = new(20, 2.0);
        private readonly StochasticCalculator stochastic = new(14, 3);
        private readonly ObvCalculator obv = new(20);
        private readonly CciCalculator cci = new(20);
        private readonly WilliamsRCalculator williamsR = new(14);
        private readonly MomentumCalculator momentum = new(10);
        private readonly RocCalculator roc = new(10);
        private readonly Calculators.PreviousDayLevelsTracker prevDayLevels;

        // Trend direction filter - prevents trading against obvious trends
        private readonly Helpers.TrendDirectionFilter trendFilter = new();

        // AI Advisor for ChatGPT confirmation (optional)
        private Learning.AIAdvisor? aiAdvisor;
        private bool useRealAiConfirmation;
        private int aiBlockedCount;
        private int aiApprovedCount;

        // Short selling toggle (default: disabled)
        private readonly bool allowShort;

        // Indicator weights - learned or default
        private IdiotProof.Optimization.IndicatorWeights weights = IdiotProof.Optimization.IndicatorWeights.Default;

        // Additional entry scores for extended indicators
        private int entryBollingerScore, entryStochasticScore, entryObvScore, entryCciScore, entryWilliamsRScore;

        // Warm-up tracking
        private int barCount;
        private bool indicatorsReady;

        public bool HasOpenPosition => inPosition;

        public AutonomousLearningSimulator(string symbol, double allocation, TickerProfile? learnedProfile = null, bool allowShort = false)
        {
            this.symbol = symbol;
            this.allocation = allocation > 0 ? allocation : TradingDefaults.DefaultAllocationDollars;
            this.learnedProfile = learnedProfile;
            profile = new TickerProfile { Symbol = symbol };
            prevDayLevels = new Calculators.PreviousDayLevelsTracker(symbol);
            this.allowShort = allowShort;
        }
        
        /// <summary>
        /// Sets the indicator weights for score calculation.
        /// Call after construction to use ticker-specific learned weights.
        /// </summary>
        public void SetWeights(IdiotProof.Optimization.IndicatorWeights weights)
        {
            weights = weights;
        }

        /// <summary>
        /// Enables real ChatGPT confirmation for each trade entry.
        /// This will make the backtest slower but more accurate.
        /// </summary>
        public void EnableChatGptConfirmation()
        {
            aiAdvisor = new Learning.AIAdvisor();
            useRealAiConfirmation = aiAdvisor.IsConfigured;
            if (useRealAiConfirmation)
            {
                aiAdvisor.MinConfidenceRequired = 55; // Same as synthetic threshold
            }
        }

        /// <summary>
        /// Get AI statistics from backtest run.
        /// </summary>
        public (int approved, int blocked) GetAiStats() => (aiApprovedCount, aiBlockedCount);

        /// <summary>
        /// Get dynamic entry threshold based on ADX and learned profile.
        /// Stronger trends = can be more aggressive (lower threshold).
        /// </summary>
        private int GetLongEntryThreshold()
        {
            int baseline = learnedProfile?.OptimalLongEntryThreshold ?? BaselineLongEntry;
            
            // Adjust based on ADX - stronger trend = lower threshold needed
            if (adx.IsReady)
            {
                double adxVal = adx.CurrentAdx;
                if (adxVal >= 40) return Math.Max(baseline - 15, 50); // Very strong trend: lower bar
                if (adxVal >= 25) return Math.Max(baseline - 5, 55);  // Moderate trend: slightly lower
                if (adxVal < 15) return Math.Min(baseline + 10, 85);  // Weak trend: higher bar
            }
            return baseline;
        }

        private int GetShortEntryThreshold()
        {
            int baseline = learnedProfile?.OptimalShortEntryThreshold ?? BaselineShortEntry;

            if (adx.IsReady)
            {
                double adxVal = adx.CurrentAdx;
                if (adxVal >= 40) return Math.Min(baseline + 15, -50);
                if (adxVal >= 25) return Math.Min(baseline + 5, -55);
                if (adxVal < 15) return Math.Max(baseline - 10, -85);
            }
            return baseline;
        }

        private int GetLongExitThreshold()
        {
            int baseline = learnedProfile?.OptimalLongExitThreshold ?? BaselineLongExit;
            
            // If in profit, give more room (lower exit threshold)
            if (inPosition && isLong)
            {
                double unrealizedPnl = (profile.TotalTrades > 0 ? profile.TotalPnL / profile.TotalTrades : 0);
                if (unrealizedPnl > 0) return Math.Max(baseline - 10, 20);
            }
            return baseline;
        }

        private int GetShortExitThreshold()
        {
            int baseline = learnedProfile?.OptimalShortExitThreshold ?? BaselineShortExit;
            
            if (inPosition && !isLong)
            {
                double unrealizedPnl = (profile.TotalTrades > 0 ? profile.TotalPnL / profile.TotalTrades : 0);
                if (unrealizedPnl > 0) return Math.Min(baseline + 10, -20);
            }
            return baseline;
        }

        /// <summary>
        /// Get ATR multipliers - adjust based on volatility.
        /// Higher volatility = wider TP/SL to avoid noise.
        /// </summary>
        private (double tpMult, double slMult) GetAtrMultipliers()
        {
            double tpMult = BaselineTpAtrMultiplier;
            double slMult = BaselineSlAtrMultiplier;

            if (atr.IsReady && ema50.IsReady)
            {
                // Calculate volatility ratio: ATR as % of price
                double atrPercent = atr.CurrentAtr / ema50.CurrentValue;
                
                if (atrPercent > 0.02) // High volatility (>2%)
                {
                    tpMult = 2.5; // Wider TP
                    slMult = 1.5; // Tighter SL (positive R:R = TP > SL)
                }
                else if (atrPercent < 0.008) // Low volatility (<0.8%)
                {
                    tpMult = 1.5; // Tighter TP
                    slMult = 1.0; // Tighter SL
                }
            }
            return (tpMult, slMult);
        }

        public List<TradeRecord> ProcessBar(HistoricalBar bar)
        {
            var completedTrades = new List<TradeRecord>();
            barCount++;

            // Reset VWAP at the start of each new trading day
            if (currentVwapDay == null || bar.Time.Date != currentVwapDay)
            {
                pvSum = 0;
                vSum = 0;
                currentVwapDay = bar.Time.Date;
            }

            // Update VWAP
            if (bar.Close > 0 && bar.Volume > 0)
            {
                pvSum += bar.Close * bar.Volume;
                vSum += bar.Volume;
            }
            double vwap = vSum > 0 ? pvSum / vSum : bar.Close;

            // Update core indicators
            ema9.Update(bar.Close);
            ema21.Update(bar.Close);
            ema34.Update(bar.Close);
            ema50.Update(bar.Close);
            adx.UpdateFromCandle(bar.High, bar.Low, bar.Close);
            rsi.Update(bar.Close);
            macd.Update(bar.Close);
            volume.Update(bar.Volume);
            atr.UpdateFromCandle(bar.High, bar.Low, bar.Close);
            sma20.Update(bar.Close);
            sma50.Update(bar.Close);

            // Update extended indicators
            bollinger.Update(bar.Close);
            stochastic.Update(bar.High, bar.Low, bar.Close);
            obv.Update(bar.Close, bar.Volume);
            cci.Update(bar.High, bar.Low, bar.Close);
            williamsR.Update(bar.High, bar.Low, bar.Close);
            momentum.Update(bar.Close);
            roc.Update(bar.Close);
            prevDayLevels.UpdateFromBar(bar);

            // Update trend direction filter
            trendFilter.Update(
                bar.Close, vwap,
                ema9.IsReady ? ema9.CurrentValue : 0,
                ema21.IsReady ? ema21.CurrentValue : 0,
                ema50.IsReady ? ema50.CurrentValue : 0,
                adx.IsReady ? adx.CurrentAdx : 0,
                adx.IsReady ? adx.PlusDI : 0,
                adx.IsReady ? adx.MinusDI : 0,
                bar.High, bar.Low);

            // Check warm-up
            if (!indicatorsReady)
            {
                if (ema50.IsReady && adx.IsReady && rsi.IsReady && macd.IsReady && bollinger.IsReady && atr.IsReady && momentum.IsReady && roc.IsReady)
                {
                    indicatorsReady = true;
                }
                else
                {
                    return completedTrades;
                }
            }

            // Calculate market score with all indicators
            var scores = CalculateScore(bar.Close, vwap);

            // Check if within RTH (avoid premarket/afterhours noise)
            var hour = bar.Time.Hour;
            var minute = bar.Time.Minute;
            var timeOfDay = hour * 60 + minute;
            bool withinTradingHours = timeOfDay >= 570 && timeOfDay < 960; // 9:30 AM - 4:00 PM
            
            // Opening bell protection: skip first 2 minutes (9:30-9:32) due to extreme volatility
            // This is where GOOG lost $5,690 in 3 minutes - never trade the open!
            bool isOpeningBellWindow = timeOfDay >= 570 && timeOfDay < 572; // 9:30-9:32 AM
            if (isOpeningBellWindow)
            {
                withinTradingHours = false; // Block trading during opening volatility
            }

            // Check time between trades
            bool canTrade = (bar.Time - lastTradeTime).TotalSeconds >= MinSecondsBetweenTrades;

            if (!inPosition)
            {
                // Get dynamic thresholds
                int longThreshold = GetLongEntryThreshold();
                int shortThreshold = GetShortEntryThreshold();

                // Entry requires: score above threshold + trend filter + AI confidence (matches LIVE)
                if (withinTradingHours && canTrade)
                {
                    // Trend direction filter (matches LIVE: allows strong signals to override)
                    bool trendAllowsLong = !trendFilter.IsReady || !trendFilter.IsInClearDowntrend;
                    bool trendAllowsShort = !trendFilter.IsReady || !trendFilter.IsInClearUptrend;

                    // Build snapshot for confidence check (populate ALL fields for AI)
                    var snapshot = new IndicatorSnapshot
                    {
                        Price = bar.Close,
                        Vwap = vwap,
                        Ema9 = ema9.IsReady ? ema9.CurrentValue : 0,
                        Ema21 = ema21.IsReady ? ema21.CurrentValue : 0,
                        Ema34 = ema34.IsReady ? ema34.CurrentValue : 0,
                        Ema50 = ema50.IsReady ? ema50.CurrentValue : 0,
                        Rsi = rsi.IsReady ? rsi.CurrentValue : 50,
                        Macd = macd.IsReady ? macd.MacdLine : 0,
                        MacdSignal = macd.IsReady ? macd.SignalLine : 0,
                        MacdHistogram = macd.IsReady ? macd.Histogram : 0,
                        Adx = adx.IsReady ? adx.CurrentAdx : 0,
                        PlusDi = adx.IsReady ? adx.PlusDI : 0,
                        MinusDi = adx.IsReady ? adx.MinusDI : 0,
                        VolumeRatio = volume.IsReady ? volume.VolumeRatio : 1.0,
                        BollingerUpper = bollinger.IsReady ? bollinger.UpperBand : 0,
                        BollingerMiddle = bollinger.IsReady ? bollinger.MiddleBand : 0,
                        BollingerLower = bollinger.IsReady ? bollinger.LowerBand : 0,
                        BollingerPercentB = bollinger.IsReady ? bollinger.PercentB : 0,
                        BollingerBandwidth = bollinger.IsReady ? bollinger.Bandwidth : 0,
                        Atr = atr.IsReady ? atr.CurrentAtr : 0,
                        Sma20 = sma20.IsReady ? sma20.CurrentValue : 0,
                        Sma50 = sma50.IsReady ? sma50.CurrentValue : 0,
                        Momentum = momentum.IsReady ? momentum.CurrentValue : 0,
                        Roc = roc.IsReady ? roc.CurrentValue : 0,
                        StochasticK = stochastic.IsReady ? stochastic.PercentK : 0,
                        StochasticD = stochastic.IsReady ? stochastic.PercentD : 0,
                        ObvSlope = obv.IsReady ? (obv.IsRising ? 1.0 : (obv.IsFalling ? -1.0 : 0)) : 0,
                        Cci = cci.IsReady ? cci.CurrentCci : 0,
                        WilliamsR = williamsR.IsReady ? williamsR.CurrentValue : -50,
                        PrevDayHigh = prevDayLevels.HasData ? prevDayLevels.PrevDayHigh : 0,
                        PrevDayLow = prevDayLevels.HasData ? prevDayLevels.PrevDayLow : 0,
                        PrevDayClose = prevDayLevels.HasData ? prevDayLevels.PrevDayClose : 0,
                        TwoDayHigh = prevDayLevels.TwoDayHigh,
                        TwoDayLow = prevDayLevels.TwoDayLow,
                        SessionHigh = prevDayLevels.SessionHigh,
                        SessionLow = prevDayLevels.SessionLow
                    };

                    // AI confidence check - use real ChatGPT if enabled, otherwise synthetic
                    const int MinSyntheticConfidence = 55;

                    // LONG: Score above threshold AND trend allows (matches LIVE - no MACD/DI/Stochastic/OBV gates)
                    if (scores.Total >= longThreshold && trendAllowsLong)
                    {
                        bool aiApproved = false;
                        
                        if (useRealAiConfirmation && aiAdvisor != null)
                        {
                            // Real ChatGPT API call
                            var scoreResult = new Calculators.MarketScoreResult
                            {
                                TotalScore = scores.Total,
                                VwapScore = scores.Vwap,
                                EmaScore = scores.Ema,
                                RsiScore = scores.Rsi,
                                MacdScore = scores.Macd,
                                AdxScore = scores.Adx,
                                VolumeScore = scores.Volume
                            };
                            var (approved, confidence, reason) = aiAdvisor.CheckTradeApproval(
                                symbol, snapshot, isLong: true, scoreResult, useSyntheticForSpeed: false);
                            aiApproved = approved;
                            if (approved) aiApprovedCount++;
                            else
                            {
                                aiBlockedCount++;
                                // Log first few blocked trades to debug
                                if (aiBlockedCount <= 3)
                                {
                                    ConsoleLog.Warn("AI", $"[{symbol}] LONG blocked (#{aiBlockedCount}): {reason}");
                                }
                            }
                        }
                        else
                        {
                            // Fast synthetic check
                            int longConfidence = Learning.AIAdvisor.CalculateSyntheticConfidence(snapshot, isLong: true);
                            aiApproved = longConfidence >= MinSyntheticConfidence;
                        }

                        if (aiApproved)
                        {
                            EnterPosition(bar.Close, bar.Time, true, scores);
                        }
                    }
                    // SHORT: Score below threshold AND trend allows (matches LIVE - no MACD/DI/Stochastic/OBV gates)
                    else if (allowShort && scores.Total <= shortThreshold && trendAllowsShort)
                    {
                        bool aiApproved = false;
                        
                        if (useRealAiConfirmation && aiAdvisor != null)
                        {
                            // Real ChatGPT API call
                            var scoreResult = new Calculators.MarketScoreResult
                            {
                                TotalScore = scores.Total,
                                VwapScore = scores.Vwap,
                                EmaScore = scores.Ema,
                                RsiScore = scores.Rsi,
                                MacdScore = scores.Macd,
                                AdxScore = scores.Adx,
                                VolumeScore = scores.Volume
                            };
                            var (approved, confidence, reason) = aiAdvisor.CheckTradeApproval(
                                symbol, snapshot, isLong: false, scoreResult, useSyntheticForSpeed: false);
                            aiApproved = approved;
                            if (approved) aiApprovedCount++;
                            else
                            {
                                aiBlockedCount++;
                                // Log first few blocked trades to debug
                                if (aiBlockedCount <= 3)
                                {
                                    ConsoleLog.Warn("AI", $"[{symbol}] SHORT blocked (#{aiBlockedCount}): {reason}");
                                }
                            }
                        }
                        else
                        {
                            // Fast synthetic check
                            int shortConfidence = Learning.AIAdvisor.CalculateSyntheticConfidence(snapshot, isLong: false);
                            aiApproved = shortConfidence >= MinSyntheticConfidence;
                        }

                        if (aiApproved)
                        {
                            EnterPosition(bar.Close, bar.Time, false, scores);
                        }
                    }
                }
            }
            else
            {
                // Check for exit - TP/SL first (price-based), then score-based
                bool shouldExit = false;
                double exitPrice = bar.Close;
                string exitReason = "score";
                
                // Apply slippage for score-based exits (TP/SL use fixed price)
                double exitSlippage = TradingDefaults.GetSlippagePercent(bar.Close);

                // Check TP/SL hits using bar high/low
                if (isLong)
                {
                    if (takeProfitPrice > 0 && bar.High >= takeProfitPrice)
                    {
                        shouldExit = true;
                        exitPrice = takeProfitPrice;
                        exitReason = "TP";
                    }
                    else if (stopLossPrice > 0 && bar.Low <= stopLossPrice)
                    {
                        shouldExit = true;
                        exitPrice = stopLossPrice;
                        exitReason = "SL";
                    }
                    else
                    {
                        // Score-based exit: immediate on threshold breach (matches LIVE)
                        int exitThreshold = GetLongExitThreshold();

                        if (scores.Total < exitThreshold)
                        {
                            shouldExit = true;
                            exitPrice = bar.Close * (1 - exitSlippage); // Selling long at slightly lower
                            exitReason = "reversal";
                        }
                    }
                }
                else // SHORT position
                {
                    if (takeProfitPrice > 0 && bar.Low <= takeProfitPrice)
                    {
                        shouldExit = true;
                        exitPrice = takeProfitPrice;
                        exitReason = "TP";
                    }
                    else if (stopLossPrice > 0 && bar.High >= stopLossPrice)
                    {
                        shouldExit = true;
                        exitPrice = stopLossPrice;
                        exitReason = "SL";
                    }
                    else
                    {
                        // Score-based exit: immediate on threshold breach (matches LIVE)
                        int exitThreshold = GetShortExitThreshold();

                        if (scores.Total > exitThreshold)
                        {
                            shouldExit = true;
                            exitPrice = bar.Close * (1 + exitSlippage); // Covering short at slightly higher
                            exitReason = "reversal";
                        }
                    }
                }

                if (shouldExit)
                {
                    var trade = CreateTradeRecord(exitPrice, bar.Time, scores.Total, exitReason);
                    completedTrades.Add(trade);
                    profile.RecordTrade(trade);
                    
                    ResetPosition();
                }
            }

            return completedTrades;
        }

        public TradeRecord? ClosePosition(double price, DateTime time)
        {
            if (!inPosition) return null;

            var scoresForClose = CalculateScore(price, pvSum / Math.Max(vSum, 1));
            var trade = CreateTradeRecord(price, time, scoresForClose.Total, "END");
            profile.RecordTrade(trade);
            ResetPosition();
            return trade;
        }

        public TickerProfile GetProfile() => profile;

        /// <summary>
        /// Calculates comprehensive market score using MarketScoreCalculator (SINGLE SOURCE OF TRUTH).
        /// Extended indicators are calculated separately for additional filtering.
        /// </summary>
        private IndicatorScores CalculateScore(double price, double vwap)
        {
            var scores = new IndicatorScores();

            // ================================================================
            // BUILD SNAPSHOT FOR MarketScoreCalculator
            // ================================================================
            var snapshot = new IndicatorSnapshot
            {
                Price = price,
                Vwap = vwap,
                Ema9 = ema9.IsReady ? ema9.CurrentValue : 0,
                Ema21 = ema21.IsReady ? ema21.CurrentValue : 0,
                Ema34 = ema34.IsReady ? ema34.CurrentValue : 0,
                Ema50 = ema50.IsReady ? ema50.CurrentValue : 0,
                Rsi = rsi.IsReady ? rsi.CurrentValue : 50,
                Macd = macd.IsReady ? macd.MacdLine : 0,
                MacdSignal = macd.IsReady ? macd.SignalLine : 0,
                MacdHistogram = macd.IsReady ? macd.Histogram : 0,
                Adx = adx.IsReady ? adx.CurrentAdx : 0,
                PlusDi = adx.IsReady ? adx.PlusDI : 0,
                MinusDi = adx.IsReady ? adx.MinusDI : 0,
                VolumeRatio = volume.IsReady ? volume.VolumeRatio : 1.0,
                BollingerUpper = bollinger.IsReady ? bollinger.UpperBand : 0,
                BollingerMiddle = bollinger.IsReady ? bollinger.MiddleBand : 0,
                BollingerLower = bollinger.IsReady ? bollinger.LowerBand : 0,
                BollingerPercentB = bollinger.IsReady ? bollinger.PercentB : 0,
                BollingerBandwidth = bollinger.IsReady ? bollinger.Bandwidth : 0,
                Atr = atr.IsReady ? atr.CurrentAtr : 0,
                Sma20 = sma20.IsReady ? sma20.CurrentValue : 0,
                Sma50 = sma50.IsReady ? sma50.CurrentValue : 0,
                Momentum = momentum.IsReady ? momentum.CurrentValue : 0,
                Roc = roc.IsReady ? roc.CurrentValue : 0,
                // Extended indicators
                StochasticK = stochastic.IsReady ? stochastic.PercentK : 0,
                StochasticD = stochastic.IsReady ? stochastic.PercentD : 0,
                ObvSlope = obv.IsReady ? (obv.IsRising ? 1.0 : (obv.IsFalling ? -1.0 : 0)) : 0,
                Cci = cci.IsReady ? cci.CurrentCci : 0,
                WilliamsR = williamsR.IsReady ? williamsR.CurrentValue : -50,
                PrevDayHigh = prevDayLevels.HasData ? prevDayLevels.PrevDayHigh : 0,
                PrevDayLow = prevDayLevels.HasData ? prevDayLevels.PrevDayLow : 0,
                PrevDayClose = prevDayLevels.HasData ? prevDayLevels.PrevDayClose : 0,
                TwoDayHigh = prevDayLevels.TwoDayHigh,
                TwoDayLow = prevDayLevels.TwoDayLow,
                SessionHigh = prevDayLevels.SessionHigh,
                SessionLow = prevDayLevels.SessionLow
            };

            // USE SHARED CALCULATOR with learned or default weights (SINGLE SOURCE OF TRUTH)
            var result = MarketScoreCalculator.Calculate(snapshot, weights.ToCalculatorWeights());
            
            scores.Vwap = result.VwapScore;
            scores.Ema = result.EmaScore;
            scores.Rsi = result.RsiScore;
            scores.Macd = result.MacdScore;
            scores.Adx = result.AdxScore;
            scores.Volume = result.VolumeScore;
            scores.Bollinger = result.BollingerScore;
            scores.Stochastic = result.StochasticScore;
            scores.Obv = result.ObvScore;
            scores.Cci = result.CciScore;
            scores.WilliamsR = result.WilliamsRScore;
            scores.Total = result.TotalScore;

            // ================================================================
            // EXTENDED INDICATORS (Additional filters, not in core score)
            // ================================================================

            // Stochastic - for additional confirmation
            if (stochastic.IsReady)
            {
                scores.Stochastic = stochastic.GetScore();
            }

            // OBV - volume flow confirmation
            if (obv.IsReady)
            {
                scores.Obv = obv.GetScore();
            }

            // CCI - trend strength confirmation
            if (cci.IsReady)
            {
                scores.Cci = cci.GetScore();
            }

            // Williams %R - momentum confirmation
            if (williamsR.IsReady)
            {
                scores.WilliamsR = williamsR.GetScore();
            }

            return scores;
        }

        private void EnterPosition(double price, DateTime time, bool isLong, IndicatorScores scores)
        {
            // Apply realistic slippage (matches TradeSimulator and LIVE reality)
            double slippagePct = TradingDefaults.GetSlippagePercent(price);
            double slippedPrice = isLong 
                ? price * (1 + slippagePct)   // Buy at slightly higher
                : price * (1 - slippagePct);  // Sell at slightly lower
            
            inPosition = true;
            isLong = isLong;
            entryPrice = slippedPrice;
            entryTime = time;
            lastTradeTime = time;
            
            // Calculate quantity from allocation and entry price
            currentTradeQuantity = price > 0 ? (int)Math.Floor(allocation / price) : 1;
            if (currentTradeQuantity < 1) currentTradeQuantity = 1;
            
            entryScore = scores.Total;
            entryVwapScore = scores.Vwap;
            entryEmaScore = scores.Ema;
            entryRsiScore = scores.Rsi;
            entryMacdScore = scores.Macd;
            entryAdxScore = scores.Adx;
            entryVolumeScore = scores.Volume;
            entryBollingerScore = scores.Bollinger;
            entryStochasticScore = scores.Stochastic;
            entryObvScore = scores.Obv;
            entryCciScore = scores.Cci;
            entryWilliamsRScore = scores.WilliamsR;

            // Get dynamic ATR multipliers based on volatility
            var (tpMult, slMult) = GetAtrMultipliers();

            // Calculate ATR-based TP/SL prices
            double atrValue = atr.IsReady ? atr.CurrentAtr : 0;
            if (atrValue > 0)
            {
                if (isLong)
                {
                    takeProfitPrice = price + (atrValue * tpMult);
                    stopLossPrice = price - (atrValue * slMult);
                }
                else
                {
                    takeProfitPrice = price - (atrValue * tpMult);
                    stopLossPrice = price + (atrValue * slMult);
                }
            }
            else
            {
                // Fallback to percentage-based TP/SL
                double tpPct = 0.015;
                double slPct = 0.025;
                if (isLong)
                {
                    takeProfitPrice = price * (1 + tpPct);
                    stopLossPrice = price * (1 - slPct);
                }
                else
                {
                    takeProfitPrice = price * (1 - tpPct);
                    stopLossPrice = price * (1 + slPct);
                }
            }
        }

        private TradeRecord CreateTradeRecord(double exitPrice, DateTime exitTime, int exitScore, string exitReason = "score")
        {
            return new TradeRecord
            {
                EntryTime = entryTime,
                EntryPrice = entryPrice,
                ExitTime = exitTime,
                ExitPrice = exitPrice,
                IsLong = isLong,
                Quantity = currentTradeQuantity,
                EntryScore = entryScore,
                ExitScore = exitScore,
                EntryVwapScore = entryVwapScore,
                EntryEmaScore = entryEmaScore,
                EntryRsiScore = entryRsiScore,
                EntryMacdScore = entryMacdScore,
                EntryAdxScore = entryAdxScore,
                EntryVolumeScore = entryVolumeScore,
                RsiAtEntry = rsi.CurrentValue,
                AdxAtEntry = adx.CurrentAdx,
                RsiAtExit = rsi.CurrentValue,
                AdxAtExit = adx.CurrentAdx,
                ExitReason = exitReason
            };
        }

        private void ResetPosition()
        {
            inPosition = false;
            isLong = false;
            entryPrice = 0;
            entryScore = 0;
            takeProfitPrice = 0;
            stopLossPrice = 0;
        }
    }
}
