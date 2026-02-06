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

                    // Fetch historical data
                    int barsFetched = await _historicalDataService.FetchHistoricalDataAsync(
                        request.Symbol,
                        barCount: fetchDays * barsPerDay,
                        barSize: BarSize.Minutes1,
                        dataType: HistoricalDataType.Trades,
                        useRTH: false);

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

                // Parse mode
                var mode = request.Mode?.ToLowerInvariant() switch
                {
                    "conservative" => AdaptiveMode.Conservative,
                    "aggressive" => AdaptiveMode.Aggressive,
                    _ => AdaptiveMode.Balanced
                };

                // Run the simulation
                var profile = RunAutonomousLearning(
                    request.Symbol,
                    allBars,
                    request.Quantity,
                    mode,
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
            AdaptiveMode mode = AdaptiveMode.Balanced,
            bool verboseLogging = false)
        {
            if (bars.Count < 50)
            {
                Console.WriteLine($"[WARN] Need at least 50 bars for autonomous learning, got {bars.Count}");
                return new TickerProfile { Symbol = symbol };
            }

            var config = mode switch
            {
                AdaptiveMode.Conservative => Autonomous.Conservative,
                AdaptiveMode.Aggressive => Autonomous.Aggressive,
                _ => Autonomous.Balanced
            };
            var simulator = new AutonomousLearningSimulator(symbol, config, quantity);

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
                        Console.WriteLine($"  {icon} {trade.EntryTime:MM/dd HH:mm} -> {trade.ExitTime:HH:mm} | " +
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
    /// </summary>
    internal sealed class AutonomousLearningSimulator
    {
        private readonly string _symbol;
        private readonly AutonomousTradingConfig _config;
        private readonly int _quantity;
        private readonly TickerProfile _profile;

        // Position tracking
        private bool _inPosition;
        private bool _isLong;
        private double _entryPrice;
        private DateTime _entryTime;
        private int _entryScore;
        private int _entryVwapScore, _entryEmaScore, _entryRsiScore, _entryMacdScore, _entryAdxScore, _entryVolumeScore;

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

        // Warm-up tracking
        private int _barCount;
        private bool _indicatorsReady;

        public bool HasOpenPosition => _inPosition;

        public AutonomousLearningSimulator(string symbol, AutonomousTradingConfig config, int quantity)
        {
            _symbol = symbol;
            _config = config;
            _quantity = quantity;
            _profile = new TickerProfile { Symbol = symbol };
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

            if (!_inPosition)
            {
                // Check for entry
                if (score >= _config.LongEntryThreshold)
                {
                    EnterPosition(bar.Close, bar.Time, true, score, vwapS, emaS, rsiS, macdS, adxS, volS);
                }
                else if (_config.AllowShort && score <= _config.ShortEntryThreshold)
                {
                    EnterPosition(bar.Close, bar.Time, false, score, vwapS, emaS, rsiS, macdS, adxS, volS);
                }
            }
            else
            {
                // Check for exit
                bool shouldExit = false;
                if (_isLong && score < _config.LongExitThreshold)
                {
                    shouldExit = true;
                }
                else if (!_isLong && score > _config.ShortExitThreshold)
                {
                    shouldExit = true;
                }

                if (shouldExit)
                {
                    var trade = CreateTradeRecord(bar.Close, bar.Time, score);
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
            var trade = CreateTradeRecord(price, time, score);
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
            _entryScore = score;
            _entryVwapScore = vwap;
            _entryEmaScore = ema;
            _entryRsiScore = rsi;
            _entryMacdScore = macd;
            _entryAdxScore = adx;
            _entryVolumeScore = vol;
        }

        private TradeRecord CreateTradeRecord(double exitPrice, DateTime exitTime, int exitScore)
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
                ExitAdx = _adx.CurrentAdx
            };
        }

        private void ResetPosition()
        {
            _inPosition = false;
            _isLong = false;
            _entryPrice = 0;
            _entryScore = 0;
        }
    }
}
