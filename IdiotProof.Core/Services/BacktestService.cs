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
        private readonly HistoricalDataService _historicalDataService;
        private readonly HistoricalDataCache _cache = new();

        public BacktestService(HistoricalDataService historicalDataService)
        {
            _historicalDataService = historicalDataService ?? throw new ArgumentNullException(nameof(historicalDataService));
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
                var cachedBars = _cache.LoadFromCache(request.Symbol);
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
                            int barsFetched = await _historicalDataService.FetchHistoricalDataAsync(
                                request.Symbol,
                                barCount: daysToBackfill * barsPerDay,
                                barSize: BarSize.Minutes1,
                                dataType: HistoricalDataType.Trades,
                                useRTH: false,
                                endDate: null); // null = up to now

                            if (barsFetched > 0)
                            {
                                var fetchedBars = _historicalDataService.Store.GetBars(request.Symbol);
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
                                int barsFetched = await _historicalDataService.FetchHistoricalDataAsync(
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

                                var fetchedBars = _historicalDataService.Store.GetBars(request.Symbol);
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
                        int barsFetched = await _historicalDataService.FetchHistoricalDataAsync(
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
                        var fetchedBars = _historicalDataService.Store.GetBars(request.Symbol);
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
                        _cache.SaveToCache(request.Symbol, allBars);
                        ConsoleLog.Write("Backtest", $"Saved {allBars.Count} bars to {request.Symbol}.history.json");
                    }
                }

                // Save updated cache if we backfilled new data
                if (cacheWasUpdated && allBars.Count > 0)
                {
                    allBars = allBars.OrderBy(b => b.Time).ToList();
                    _cache.SaveToCache(request.Symbol, allBars);
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
                    request.Quantity,
                    existingProfile,
                    verboseLogging: true);

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
                    DetailedTradeLog = detailedLog
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
            int quantity = 100,
            TickerProfile? existingProfile = null,
            bool verboseLogging = false)
        {
            if (bars.Count < 50)
            {
                ConsoleLog.Warn("Backtest", $"Need at least 50 bars for autonomous learning, got {bars.Count}");
                return (new TickerProfile { Symbol = symbol }, "");
            }

            // Create adaptive simulator that uses learned thresholds from profile
            var simulator = new AutonomousLearningSimulator(symbol, quantity, existingProfile);

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
                sb.AppendLine($"Day {dayGroup.Key.DayNum} ({dayGroup.Key.Date:MM/dd/yyyy})");
                sb.AppendLine(new string('-', 40));
                
                double dayPnL = 0;
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
                    dayPnL += trade.PnL;
                }
                
                var dayPnlSign = dayPnL >= 0 ? "+" : "";
                sb.AppendLine($"  Daily P/L: {dayPnlSign}${dayPnL:F2} ({dayGroup.Count()} trades)");
                sb.AppendLine();
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
        private readonly string _symbol;
        private readonly int _quantity;
        private readonly TickerProfile _profile;
        private readonly TickerProfile? _learnedProfile;

        // Baseline thresholds (adjusted dynamically by ADX and learned profile)
        private static readonly int BaselineLongEntry = TradingDefaults.LongEntryThreshold;
        private static readonly int BaselineShortEntry = TradingDefaults.ShortEntryThreshold;
        private static readonly int BaselineLongExit = TradingDefaults.LongExitThreshold;
        private static readonly int BaselineShortExit = TradingDefaults.ShortExitThreshold;
        private static readonly double BaselineTpAtrMultiplier = TradingDefaults.TpAtrMultiplier;
        private static readonly double BaselineSlAtrMultiplier = TradingDefaults.SlAtrMultiplier;
        private static readonly int MinSecondsBetweenTrades = TradingDefaults.MinSecondsBetweenAdjustments;

        // Position tracking
        private bool _inPosition;
        private bool _isLong;
        private double _entryPrice;
        private DateTime _entryTime;
        private DateTime _lastTradeTime;
        private int _entryScore;
        private int _entryVwapScore, _entryEmaScore, _entryRsiScore, _entryMacdScore, _entryAdxScore, _entryVolumeScore;

        // TP/SL price levels (ATR-based)
        private double _takeProfitPrice;
        private double _stopLossPrice;

        // Exit confirmation tracking (avoid exiting on one bad candle)
        private int _exitConfirmationBars;
        private const int RequiredExitConfirmationBars = 2;

        // VWAP tracking
        private double _pvSum;
        private double _vSum;

        // Indicator calculators - Core
        private readonly EmaCalculator _ema9 = new(9);
        private readonly EmaCalculator _ema21 = new(21);
        private readonly EmaCalculator _ema50 = new(50);
        private readonly AdxCalculator _adx = new(14, 50);
        private readonly RsiCalculator _rsi = new(14);
        private readonly MacdCalculator _macd = new(12, 26, 9);
        private readonly VolumeCalculator _volume = new(20);
        private readonly AtrCalculator _atr = new(14);

        // Indicator calculators - Extended (new indicators)
        private readonly BollingerBandsCalculator _bollinger = new(20, 2.0);
        private readonly StochasticCalculator _stochastic = new(14, 3);
        private readonly ObvCalculator _obv = new(20);
        private readonly CciCalculator _cci = new(20);
        private readonly WilliamsRCalculator _williamsR = new(14);

        // Indicator weights - learned or default
        private IdiotProof.Optimization.IndicatorWeights _weights = IdiotProof.Optimization.IndicatorWeights.Default;

        // Additional entry scores for extended indicators
        private int _entryBollingerScore, _entryStochasticScore, _entryObvScore, _entryCciScore, _entryWilliamsRScore;

        // Warm-up tracking
        private int _barCount;
        private bool _indicatorsReady;

        public bool HasOpenPosition => _inPosition;

        public AutonomousLearningSimulator(string symbol, int quantity, TickerProfile? learnedProfile = null)
        {
            _symbol = symbol;
            _quantity = quantity;
            _learnedProfile = learnedProfile;
            _profile = new TickerProfile { Symbol = symbol };
        }
        
        /// <summary>
        /// Sets the indicator weights for score calculation.
        /// Call after construction to use ticker-specific learned weights.
        /// </summary>
        public void SetWeights(IdiotProof.Optimization.IndicatorWeights weights)
        {
            _weights = weights;
        }

        /// <summary>
        /// Get dynamic entry threshold based on ADX and learned profile.
        /// Stronger trends = can be more aggressive (lower threshold).
        /// </summary>
        private int GetLongEntryThreshold()
        {
            int baseline = _learnedProfile?.OptimalLongEntryThreshold ?? BaselineLongEntry;
            
            // Adjust based on ADX - stronger trend = lower threshold needed
            if (_adx.IsReady)
            {
                double adx = _adx.CurrentAdx;
                if (adx >= 40) return Math.Max(baseline - 15, 50); // Very strong trend: lower bar
                if (adx >= 25) return Math.Max(baseline - 5, 55);  // Moderate trend: slightly lower
                if (adx < 15) return Math.Min(baseline + 10, 85);  // Weak trend: higher bar
            }
            return baseline;
        }

        private int GetShortEntryThreshold()
        {
            int baseline = _learnedProfile?.OptimalShortEntryThreshold ?? BaselineShortEntry;
            
            if (_adx.IsReady)
            {
                double adx = _adx.CurrentAdx;
                if (adx >= 40) return Math.Min(baseline + 15, -50);
                if (adx >= 25) return Math.Min(baseline + 5, -55);
                if (adx < 15) return Math.Max(baseline - 10, -85);
            }
            return baseline;
        }

        private int GetLongExitThreshold()
        {
            int baseline = _learnedProfile?.OptimalLongExitThreshold ?? BaselineLongExit;
            
            // If in profit, give more room (lower exit threshold)
            if (_inPosition && _isLong)
            {
                double unrealizedPnl = (_profile.TotalTrades > 0 ? _profile.TotalPnL / _profile.TotalTrades : 0);
                if (unrealizedPnl > 0) return Math.Max(baseline - 10, 20);
            }
            return baseline;
        }

        private int GetShortExitThreshold()
        {
            int baseline = _learnedProfile?.OptimalShortExitThreshold ?? BaselineShortExit;
            
            if (_inPosition && !_isLong)
            {
                double unrealizedPnl = (_profile.TotalTrades > 0 ? _profile.TotalPnL / _profile.TotalTrades : 0);
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

            if (_atr.IsReady && _ema50.IsReady)
            {
                // Calculate volatility ratio: ATR as % of price
                double atrPercent = _atr.CurrentAtr / _ema50.CurrentValue;
                
                if (atrPercent > 0.02) // High volatility (>2%)
                {
                    tpMult = 2.5; // Wider TP
                    slMult = 4.0; // Much wider SL
                }
                else if (atrPercent < 0.008) // Low volatility (<0.8%)
                {
                    tpMult = 1.5; // Tighter TP
                    slMult = 2.0; // Tighter SL
                }
            }
            return (tpMult, slMult);
        }

        public List<TradeRecord> ProcessBar(HistoricalBar bar)
        {
            var completedTrades = new List<TradeRecord>();
            _barCount++;

            // Update VWAP
            if (bar.Close > 0 && bar.Volume > 0)
            {
                _pvSum += bar.Close * bar.Volume;
                _vSum += bar.Volume;
            }
            double vwap = _vSum > 0 ? _pvSum / _vSum : bar.Close;

            // Update core indicators
            _ema9.Update(bar.Close);
            _ema21.Update(bar.Close);
            _ema50.Update(bar.Close);
            _adx.UpdateFromCandle(bar.High, bar.Low, bar.Close);
            _rsi.Update(bar.Close);
            _macd.Update(bar.Close);
            _volume.Update(bar.Volume);
            _atr.Update(bar.High);
            _atr.Update(bar.Low);
            _atr.Update(bar.Close);

            // Update extended indicators
            _bollinger.Update(bar.Close);
            _stochastic.Update(bar.High, bar.Low, bar.Close);
            _obv.Update(bar.Close, bar.Volume);
            _cci.Update(bar.High, bar.Low, bar.Close);
            _williamsR.Update(bar.High, bar.Low, bar.Close);

            // Check warm-up
            if (!_indicatorsReady)
            {
                if (_ema50.IsReady && _adx.IsReady && _rsi.IsReady && _macd.IsReady && _bollinger.IsReady)
                {
                    _indicatorsReady = true;
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

            // Check time between trades
            bool canTrade = (bar.Time - _lastTradeTime).TotalSeconds >= MinSecondsBetweenTrades;

            if (!_inPosition)
            {
                // Reset exit confirmation on no position
                _exitConfirmationBars = 0;

                // Get dynamic thresholds
                int longThreshold = GetLongEntryThreshold();
                int shortThreshold = GetShortEntryThreshold();

                // Entry requires: 1) score above threshold, 2) +DI/-DI aligned, 3) MACD confirms
                if (withinTradingHours && canTrade)
                {
                    bool macdBullish = _macd.IsReady && _macd.IsBullish;
                    bool macdBearish = _macd.IsReady && !_macd.IsBullish;
                    bool diPositive = _adx.IsReady && _adx.PlusDI > _adx.MinusDI;
                    bool diNegative = _adx.IsReady && _adx.MinusDI > _adx.PlusDI;

                    // Additional confirmation from extended indicators
                    bool stochasticBullish = _stochastic.IsReady && (_stochastic.IsBullishCrossover || !_stochastic.IsOverbought);
                    bool stochasticBearish = _stochastic.IsReady && (_stochastic.IsBearishCrossover || !_stochastic.IsOversold);
                    bool obvConfirmsBull = !_obv.IsReady || _obv.IsAboveEma;
                    bool obvConfirmsBear = !_obv.IsReady || _obv.IsBelowEma;

                    // LONG: Score above threshold AND MACD bullish AND +DI > -DI
                    if (scores.Total >= longThreshold && macdBullish && diPositive && stochasticBullish && obvConfirmsBull)
                    {
                        EnterPosition(bar.Close, bar.Time, true, scores);
                    }
                    // SHORT: Score below threshold AND MACD bearish AND -DI > +DI
                    else if (scores.Total <= shortThreshold && macdBearish && diNegative && stochasticBearish && obvConfirmsBear)
                    {
                        EnterPosition(bar.Close, bar.Time, false, scores);
                    }
                }
            }
            else
            {
                // Check for exit - TP/SL first (price-based), then score-based with confirmation
                bool shouldExit = false;
                double exitPrice = bar.Close;
                string exitReason = "score";

                // Check TP/SL hits using bar high/low
                if (_isLong)
                {
                    if (_takeProfitPrice > 0 && bar.High >= _takeProfitPrice)
                    {
                        shouldExit = true;
                        exitPrice = _takeProfitPrice;
                        exitReason = "TP";
                    }
                    else if (_stopLossPrice > 0 && bar.Low <= _stopLossPrice)
                    {
                        shouldExit = true;
                        exitPrice = _stopLossPrice;
                        exitReason = "SL";
                    }
                    else
                    {
                        // Score-based exit: require MACD flip OR sustained score drop
                        bool macdFlipped = _macd.IsReady && !_macd.IsBullish;
                        bool diFlipped = _adx.IsReady && _adx.MinusDI > _adx.PlusDI;
                        int exitThreshold = GetLongExitThreshold();

                        if (scores.Total < exitThreshold)
                        {
                            _exitConfirmationBars++;
                            // Exit if: MACD flipped bearish AND DI flipped, OR sustained low score
                            if ((macdFlipped && diFlipped) || _exitConfirmationBars >= RequiredExitConfirmationBars)
                            {
                                shouldExit = true;
                                exitReason = "reversal";
                            }
                        }
                        else
                        {
                            _exitConfirmationBars = 0; // Reset if score recovers
                        }
                    }
                }
                else // SHORT position
                {
                    if (_takeProfitPrice > 0 && bar.Low <= _takeProfitPrice)
                    {
                        shouldExit = true;
                        exitPrice = _takeProfitPrice;
                        exitReason = "TP";
                    }
                    else if (_stopLossPrice > 0 && bar.High >= _stopLossPrice)
                    {
                        shouldExit = true;
                        exitPrice = _stopLossPrice;
                        exitReason = "SL";
                    }
                    else
                    {
                        bool macdFlipped = _macd.IsReady && _macd.IsBullish;
                        bool diFlipped = _adx.IsReady && _adx.PlusDI > _adx.MinusDI;
                        int exitThreshold = GetShortExitThreshold();

                        if (scores.Total > exitThreshold)
                        {
                            _exitConfirmationBars++;
                            if ((macdFlipped && diFlipped) || _exitConfirmationBars >= RequiredExitConfirmationBars)
                            {
                                shouldExit = true;
                                exitReason = "reversal";
                            }
                        }
                        else
                        {
                            _exitConfirmationBars = 0;
                        }
                    }
                }

                if (shouldExit)
                {
                    var trade = CreateTradeRecord(exitPrice, bar.Time, scores.Total, exitReason);
                    completedTrades.Add(trade);
                    _profile.RecordTrade(trade);
                    ResetPosition();
                }
            }

            return completedTrades;
        }

        public TradeRecord? ClosePosition(double price, DateTime time)
        {
            if (!_inPosition) return null;

            var scoresForClose = CalculateScore(price, _pvSum / Math.Max(_vSum, 1));
            var trade = CreateTradeRecord(price, time, scoresForClose.Total, "END");
            _profile.RecordTrade(trade);
            ResetPosition();
            return trade;
        }

        public TickerProfile GetProfile() => _profile;

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
                Ema9 = _ema9.IsReady ? _ema9.CurrentValue : 0,
                Ema21 = _ema21.IsReady ? _ema21.CurrentValue : 0,
                Ema50 = _ema50.IsReady ? _ema50.CurrentValue : 0,
                Rsi = _rsi.IsReady ? _rsi.CurrentValue : 50,
                Macd = _macd.IsReady ? _macd.MacdLine : 0,
                MacdSignal = _macd.IsReady ? _macd.SignalLine : 0,
                MacdHistogram = _macd.IsReady ? _macd.Histogram : 0,
                Adx = _adx.IsReady ? _adx.CurrentAdx : 0,
                PlusDi = _adx.IsReady ? _adx.PlusDI : 0,
                MinusDi = _adx.IsReady ? _adx.MinusDI : 0,
                VolumeRatio = _volume.IsReady ? _volume.VolumeRatio : 1.0,
                BollingerUpper = _bollinger.IsReady ? _bollinger.UpperBand : 0,
                BollingerMiddle = _bollinger.IsReady ? _bollinger.MiddleBand : 0,
                BollingerLower = _bollinger.IsReady ? _bollinger.LowerBand : 0,
                Atr = _atr.IsReady ? _atr.CurrentAtr : 0,
                // Extended indicators
                StochasticK = _stochastic.IsReady ? _stochastic.PercentK : 0,
                StochasticD = _stochastic.IsReady ? _stochastic.PercentD : 0,
                ObvSlope = _obv.IsReady ? (_obv.IsRising ? 1.0 : (_obv.IsFalling ? -1.0 : 0)) : 0,
                Cci = _cci.IsReady ? _cci.CurrentCci : 0,
                WilliamsR = _williamsR.IsReady ? _williamsR.CurrentValue : -50
            };

            // USE SHARED CALCULATOR with learned or default weights (SINGLE SOURCE OF TRUTH)
            var result = MarketScoreCalculator.Calculate(snapshot, _weights.ToCalculatorWeights());
            
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
            if (_stochastic.IsReady)
            {
                scores.Stochastic = _stochastic.GetScore();
            }

            // OBV - volume flow confirmation
            if (_obv.IsReady)
            {
                scores.Obv = _obv.GetScore();
            }

            // CCI - trend strength confirmation
            if (_cci.IsReady)
            {
                scores.Cci = _cci.GetScore();
            }

            // Williams %R - momentum confirmation
            if (_williamsR.IsReady)
            {
                scores.WilliamsR = _williamsR.GetScore();
            }

            return scores;
        }

        private void EnterPosition(double price, DateTime time, bool isLong, IndicatorScores scores)
        {
            _inPosition = true;
            _isLong = isLong;
            _entryPrice = price;
            _entryTime = time;
            _lastTradeTime = time;
            _entryScore = scores.Total;
            _entryVwapScore = scores.Vwap;
            _entryEmaScore = scores.Ema;
            _entryRsiScore = scores.Rsi;
            _entryMacdScore = scores.Macd;
            _entryAdxScore = scores.Adx;
            _entryVolumeScore = scores.Volume;
            _entryBollingerScore = scores.Bollinger;
            _entryStochasticScore = scores.Stochastic;
            _entryObvScore = scores.Obv;
            _entryCciScore = scores.Cci;
            _entryWilliamsRScore = scores.WilliamsR;
            _exitConfirmationBars = 0;

            // Get dynamic ATR multipliers based on volatility
            var (tpMult, slMult) = GetAtrMultipliers();

            // Calculate ATR-based TP/SL prices
            double atrValue = _atr.IsReady ? _atr.CurrentAtr : 0;
            if (atrValue > 0)
            {
                if (isLong)
                {
                    _takeProfitPrice = price + (atrValue * tpMult);
                    _stopLossPrice = price - (atrValue * slMult);
                }
                else
                {
                    _takeProfitPrice = price - (atrValue * tpMult);
                    _stopLossPrice = price + (atrValue * slMult);
                }
            }
            else
            {
                // Fallback to percentage-based TP/SL
                double tpPct = 0.015;
                double slPct = 0.025;
                if (isLong)
                {
                    _takeProfitPrice = price * (1 + tpPct);
                    _stopLossPrice = price * (1 - slPct);
                }
                else
                {
                    _takeProfitPrice = price * (1 - tpPct);
                    _stopLossPrice = price * (1 + slPct);
                }
            }
        }

        private TradeRecord CreateTradeRecord(double exitPrice, DateTime exitTime, int exitScore, string exitReason = "score")
        {
            return new TradeRecord
            {
                EntryTime = _entryTime,
                EntryPrice = _entryPrice,
                ExitTime = exitTime,
                ExitPrice = exitPrice,
                IsLong = _isLong,
                Quantity = _quantity,
                EntryScore = _entryScore,
                ExitScore = exitScore,
                EntryVwapScore = _entryVwapScore,
                EntryEmaScore = _entryEmaScore,
                EntryRsiScore = _entryRsiScore,
                EntryMacdScore = _entryMacdScore,
                EntryAdxScore = _entryAdxScore,
                EntryVolumeScore = _entryVolumeScore,
                RsiAtEntry = _rsi.CurrentValue,
                AdxAtEntry = _adx.CurrentAdx,
                RsiAtExit = _rsi.CurrentValue,
                AdxAtExit = _adx.CurrentAdx,
                ExitReason = exitReason
            };
        }

        private void ResetPosition()
        {
            _inPosition = false;
            _isLong = false;
            _entryPrice = 0;
            _entryScore = 0;
            _takeProfitPrice = 0;
            _stopLossPrice = 0;
            _exitConfirmationBars = 0;
        }
    }
}
