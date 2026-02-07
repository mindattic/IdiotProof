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

using IdiotProof.Backend.Enums;
using IdiotProof.Backend.Helpers;
using IdiotProof.Backend.Models;
using IdiotProof.Backend.Strategy;
using IdiotProof.Shared.Models;

namespace IdiotProof.Backend.Services
{
    /// <summary>
    /// Service for running autonomous trading backtests and building TickerProfiles.
    /// </summary>
    public sealed class BacktestService
    {
        private readonly HistoricalDataService _historicalDataService;

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
                Console.WriteLine($"[BACKTEST] Starting backtest for {request.Symbol}, {request.Days} days, mode: {request.Mode}");

                // Calculate bars needed for the requested days
                // 1 trading day = ~390 minutes RTH or ~960 minutes extended hours
                // We'll use extended hours data
                int barsPerDay = 960; // Extended hours: 4am-8pm = 16 hours x 60 min
                int totalBarsNeeded = request.Days * barsPerDay;

                // IBKR limits: 1 min bars max "5 D" per request
                // For 30 days, we need multiple requests
                var allBars = new List<HistoricalBar>();

                // Fetch data in chunks of 5 days
                int daysRemaining = request.Days;
                DateTime endDate = DateTime.Now;

                while (daysRemaining > 0 && allBars.Count < totalBarsNeeded)
                {
                    int fetchDays = Math.Min(daysRemaining, 5);
                    
                    Console.WriteLine($"[BACKTEST] Fetching {fetchDays} days ending {endDate:yyyy-MM-dd HH:mm}");

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

                Console.WriteLine($"[BACKTEST] Fetched {allBars.Count} total bars for {request.Symbol}");

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
                var profile = RunAutonomousLearning(
                    request.Symbol,
                    allBars,
                    request.Quantity,
                    existingProfile,
                    verboseLogging: true);

                // Calculate results
                int totalTrades = profile.TotalTrades;
                int winningTrades = profile.TotalWins;
                double winRate = profile.WinRate;
                double totalPnL = profile.NetProfit;
                double avgPnL = totalTrades > 0 ? totalPnL / totalTrades : 0;

                // Save profile if requested
                bool profileSaved = false;
                if (request.SaveProfile && totalTrades > 0)
                {
                    StrategyRunner.ProfileManager.SaveProfile(profile);
                    profileSaved = true;
                    Console.WriteLine($"[BACKTEST] Profile saved for {request.Symbol}");
                }

                Console.WriteLine($"[BACKTEST] Complete: {totalTrades} trades, {winRate:F1}% win rate, ${totalPnL:F2} total P&L");

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
                    Confidence = profile.Confidence
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BACKTEST] Error: {ex.Message}");
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
        private static TickerProfile RunAutonomousLearning(
            string symbol,
            IReadOnlyList<HistoricalBar> bars,
            int quantity = 100,
            TickerProfile? existingProfile = null,
            bool verboseLogging = false)
        {
            if (bars.Count < 50)
            {
                Console.WriteLine($"[WARN] Need at least 50 bars for autonomous learning, got {bars.Count}");
                return new TickerProfile { Symbol = symbol };
            }

            // Create adaptive simulator that uses learned thresholds from profile
            var simulator = new AutonomousLearningSimulator(symbol, quantity, existingProfile);

            if (verboseLogging)
            {
                Console.WriteLine($"[LEARN] Running autonomous learning for {symbol} with {bars.Count} bars...");
            }

            // Process each bar
            foreach (var bar in bars)
            {
                var trades = simulator.ProcessBar(bar);

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
                        Console.WriteLine($"  {icon}{reasonTag} {trade.EntryTime:MM/dd HH:mm} -> {trade.ExitTime:HH:mm} | " +
                            $"${trade.EntryPrice:F2} -> ${trade.ExitPrice:F2} | " +
                            $"P&L: ${trade.ProfitLoss:F2} ({trade.ProfitLossPercent:F2}%)");
                    }
                }
            }

            // Close any open position
            if (simulator.HasOpenPosition)
            {
                var finalTrade = simulator.ClosePosition(bars[^1].Close, bars[^1].Time);
                if (verboseLogging && finalTrade != null)
                {
                    Console.WriteLine($"  [END] Closed at end of data: ${finalTrade.ProfitLoss:F2}");
                }
            }

            var profile = simulator.GetProfile();

            if (verboseLogging)
            {
                Console.WriteLine($"[LEARN] Complete: {profile.GetSummary()}");
            }

            return profile;
        }
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
        private const int BaselineLongEntry = 70;
        private const int BaselineShortEntry = -70;
        private const int BaselineLongExit = 35;
        private const int BaselineShortExit = -35;
        private const double BaselineTpAtrMultiplier = 2.0;
        private const double BaselineSlAtrMultiplier = 3.0;
        private const int MinSecondsBetweenTrades = 180; // 3 minutes

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

        // Indicator calculators
        private readonly EmaCalculator _ema9 = new(9);
        private readonly EmaCalculator _ema21 = new(21);
        private readonly EmaCalculator _ema50 = new(50);
        private readonly AdxCalculator _adx = new(14, 50);
        private readonly RsiCalculator _rsi = new(14);
        private readonly MacdCalculator _macd = new(12, 26, 9);
        private readonly VolumeCalculator _volume = new(20);
        private readonly AtrCalculator _atr = new(14);

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
                double unrealizedPnl = (_profile.TotalTrades > 0 ? _profile.NetProfit / _profile.TotalTrades : 0);
                if (unrealizedPnl > 0) return Math.Max(baseline - 10, 20);
            }
            return baseline;
        }

        private int GetShortExitThreshold()
        {
            int baseline = _learnedProfile?.OptimalShortExitThreshold ?? BaselineShortExit;
            
            if (_inPosition && !_isLong)
            {
                double unrealizedPnl = (_profile.TotalTrades > 0 ? _profile.NetProfit / _profile.TotalTrades : 0);
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

            // Update indicators
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

            // Check warm-up
            if (!_indicatorsReady)
            {
                if (_ema50.IsReady && _adx.IsReady && _rsi.IsReady && _macd.IsReady)
                {
                    _indicatorsReady = true;
                }
                else
                {
                    return completedTrades;
                }
            }

            // Calculate market score
            var (score, vwapS, emaS, rsiS, macdS, adxS, volS) = CalculateScore(bar.Close, vwap);

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

                    // LONG: Score above threshold AND MACD bullish AND +DI > -DI
                    if (score >= longThreshold && macdBullish && diPositive)
                    {
                        EnterPosition(bar.Close, bar.Time, true, score, vwapS, emaS, rsiS, macdS, adxS, volS);
                    }
                    // SHORT: Score below threshold AND MACD bearish AND -DI > +DI
                    else if (score <= shortThreshold && macdBearish && diNegative)
                    {
                        EnterPosition(bar.Close, bar.Time, false, score, vwapS, emaS, rsiS, macdS, adxS, volS);
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

                        if (score < exitThreshold)
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

                        if (score > exitThreshold)
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
                    var trade = CreateTradeRecord(exitPrice, bar.Time, score, exitReason);
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

            var (score, _, _, _, _, _, _) = CalculateScore(price, _pvSum / Math.Max(_vSum, 1));
            var trade = CreateTradeRecord(price, time, score, "END");
            _profile.RecordTrade(trade);
            ResetPosition();
            return trade;
        }

        public TickerProfile GetProfile() => _profile;

        private (int total, int vwap, int ema, int rsi, int macd, int adx, int vol) CalculateScore(double price, double vwap)
        {
            int vwapScore = 0, emaScore = 0, rsiScore = 0, macdScore = 0, adxScore = 0, volumeScore = 0;

            // VWAP Position (15% weight)
            if (vwap > 0)
            {
                double vwapDiff = (price - vwap) / vwap * 100;
                vwapScore = (int)Math.Clamp(vwapDiff * 20, -100, 100);
            }

            // EMA Stack Alignment (20% weight)
            int bullish = 0, bearish = 0;
            if (_ema9.IsReady && price > _ema9.CurrentValue) bullish++; else bearish++;
            if (_ema21.IsReady && price > _ema21.CurrentValue) bullish++; else bearish++;
            if (_ema50.IsReady && price > _ema50.CurrentValue) bullish++; else bearish++;
            int total = bullish + bearish;
            if (total > 0)
            {
                emaScore = (int)((bullish - bearish) / (double)total * 100);
            }

            // RSI (15% weight)
            if (_rsi.IsReady)
            {
                double rsi = _rsi.CurrentValue;
                if (rsi >= 70)
                    rsiScore = (int)((70 - rsi) * 3.33);
                else if (rsi <= 30)
                    rsiScore = (int)((30 - rsi) * 3.33);
                else
                    rsiScore = (int)((rsi - 50) * 2.5);
            }

            // MACD (20% weight)
            if (_macd.IsReady)
            {
                macdScore = _macd.IsBullish ? 50 : -50;
                macdScore += (int)Math.Clamp(_macd.Histogram * 500, -50, 50);
            }

            // ADX Trend Strength (20% weight)
            if (_adx.IsReady)
            {
                double adx = _adx.CurrentAdx;
                bool diPositive = _adx.PlusDI > _adx.MinusDI;
                int magnitude = (int)Math.Min(adx * 2, 100);
                adxScore = diPositive ? magnitude : -magnitude;
            }

            // Volume (10% weight)
            if (_volume.IsReady)
            {
                double volumeRatio = _volume.VolumeRatio;
                if (volumeRatio > 1.0)
                {
                    int volumeMagnitude = (int)Math.Min((volumeRatio - 1.0) * 100, 100);
                    volumeScore = price > vwap ? volumeMagnitude : -volumeMagnitude;
                }
            }

            // Calculate weighted total
            double totalScore =
                vwapScore * 0.15 +
                emaScore * 0.20 +
                rsiScore * 0.15 +
                macdScore * 0.20 +
                adxScore * 0.20 +
                volumeScore * 0.10;

            return ((int)Math.Clamp(totalScore, -100, 100), vwapScore, emaScore, rsiScore, macdScore, adxScore, volumeScore);
        }

        private void EnterPosition(double price, DateTime time, bool isLong, int score, int vwap, int ema, int rsi, int macd, int adx, int vol)
        {
            _inPosition = true;
            _isLong = isLong;
            _entryPrice = price;
            _entryTime = time;
            _lastTradeTime = time;
            _entryScore = score;
            _entryVwapScore = vwap;
            _entryEmaScore = ema;
            _entryRsiScore = rsi;
            _entryMacdScore = macd;
            _entryAdxScore = adx;
            _entryVolumeScore = vol;
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
                WasLong = _isLong,
                Quantity = _quantity,
                EntryScore = _entryScore,
                ExitScore = exitScore,
                EntryVwapScore = _entryVwapScore,
                EntryEmaScore = _entryEmaScore,
                EntryRsiScore = _entryRsiScore,
                EntryMacdScore = _entryMacdScore,
                EntryAdxScore = _entryAdxScore,
                EntryVolumeScore = _entryVolumeScore,
                EntryRsi = _rsi.CurrentValue,
                EntryAdx = _adx.CurrentAdx,
                ExitRsi = _rsi.CurrentValue,
                ExitAdx = _adx.CurrentAdx,
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
