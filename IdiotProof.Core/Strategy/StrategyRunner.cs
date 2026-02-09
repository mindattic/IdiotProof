// ============================================================================
// Strategy Runner - Executes multi-step strategies
// ============================================================================
//
// ╔═══════════════════════════════════════════════════════════════════════════╗
// ║  IBKR API DEPENDENCY                                                      ║
// ║                                                                           ║
// ║  This class directly interfaces with the Interactive Brokers TWS API.     ║
// ║  When modifying order submission code, ensure compatibility with IB API:  ║
// ║                                                                           ║
// ║  Order Properties Used:                                                   ║
// ║    • order.Action        = "BUY" | "SELL"                                ║
// ║    • order.OrderType     = "MKT" | "LMT" | "STP"                         ║
// ║    • order.TotalQuantity                                                  ║
// ║    • order.LmtPrice      (for limit orders)                              ║
// ║    • order.AuxPrice      (for stop orders)                               ║
// ║    • order.OutsideRth    = true | false                                  ║
// ║    • order.Tif           = "GTC" | "DAY" | "IOC" | "FOK" | "OPG" | "DTC"║
// ║    • order.AllOrNone     = true | false                                  ║
// ║    • order.Account       (for multi-account setups)                       ║
// ║                                                                           ║
// ║  Reference: https://interactivebrokers.github.io/tws-api/               ║
// ╚═══════════════════════════════════════════════════════════════════════════╝

using IBApi;
using IdiotProof.Constants;
using IdiotProof.Enums;
using IdiotProof.Helpers;
using IdiotProof.Logging;
using IdiotProof.Models;
using IdiotProof.Core.Models;
using IdiotProof.Settings;
using IbContract = IBApi.Contract;
using MarketTimeZone = IdiotProof.Enums.MarketTimeZone;

namespace IdiotProof.Strategy {
    /// <summary>
    /// Executes a multi-step strategy for a single symbol.
    /// Monitors price and VWAP, evaluates conditions in sequence,
    /// and places orders when all conditions are met.
    /// </summary>
    public sealed class StrategyRunner : IDisposable
    {
        /// <summary>
        /// Shared session logger instance (set from Program.cs).
        /// </summary>
        public static SessionLogger? SessionLogger { get; set; }

        private readonly TradingStrategy _strategy;
        private readonly IbContract _contract;
        private readonly IbWrapper _wrapper;
        private readonly EClientSocket _client;

        private int _currentConditionIndex;
        private bool _isComplete;
        private volatile bool _disposed;

        // Lock for thread-safe timer callback handling
        private readonly object _disposeLock = new object();

        // VWAP accumulators
        private double _pvSum;
        private double _vSum;
        private double _lastPrice;

        // Bid/Ask tracking for PriceType support
        private double _lastBid;
        private double _lastAsk;

        // Session tracking
        private double _sessionHigh;
        private double _sessionLow = double.MaxValue;

        // Order tracking
        private int _entryOrderId = -1;
        private bool _entryFilled;
        private double _entryFillPrice;

        // Take profit tracking
        private int _takeProfitOrderId = -1;
        private bool _takeProfitFilled;
        private bool _takeProfitCancelled;
        private bool _takeProfitOrderRejected;  // Order rejected by IBKR (never placed)
        private double _takeProfitTarget;

        // Stop loss tracking
        private int _stopLossOrderId = -1;
        private bool _stopLossFilled;
        private bool _stopLossOrderRejected;  // Order rejected by IBKR (never placed)

        // Exit tracking
        private bool _exitedWithProfit;
        private double _exitFillPrice;

        // Trailing stop loss tracking
        private int _trailingStopLossOrderId = -1;
        private bool _trailingStopLossTriggered;
        private double _trailingStopLossPrice;
        private double _highWaterMark;  // Highest price since entry (for trailing stop)

        // ATR calculator for volatility-based stops
        private Helpers.AtrCalculator? _atrCalculator;

        // Candlestick aggregator for candle-based indicators
        private readonly Helpers.CandlestickAggregator _candlestickAggregator;

        // EMA calculators for indicator conditions
        private readonly Dictionary<int, Helpers.EmaCalculator> _emaCalculators = new();

        // ADX calculator for trend strength and DI conditions
        private Helpers.AdxCalculator? _adxCalculator;

        // ADX rollover detection for dynamic TakeProfit
        private double _adxPeakValue;
        private bool _adxRolledOver;

        // RSI calculator for overbought/oversold conditions
        private Helpers.RsiCalculator? _rsiCalculator;

        // MACD calculator for momentum conditions
        private Helpers.MacdCalculator? _macdCalculator;

        // Momentum calculator for price momentum conditions
        private Helpers.MomentumCalculator? _momentumCalculator;

        // ROC calculator for rate of change conditions
        private Helpers.RocCalculator? _rocCalculator;

        // Volume calculator for volume spike conditions
        private Helpers.VolumeCalculator? _volumeCalculator;

        // Bollinger Bands calculator for mean reversion signals
        private Helpers.BollingerBandsCalculator? _bollingerBands;
        
        // Extended indicator calculators for comprehensive market scoring
        private Helpers.StochasticCalculator? _stochasticCalculator;
        private Helpers.ObvCalculator? _obvCalculator;
        private Helpers.CciCalculator? _cciCalculator;
        private Helpers.WilliamsRCalculator? _williamsRCalculator;

        // Warm-up logging
        private bool _warmupLoggedEma;
        private bool _warmupLoggedAdx;
        private bool _warmupLoggedRsi;
        private bool _warmupLoggedMacd;
        private bool _warmupLoggedMomentum;
        private bool _warmupLoggedRoc;
        private bool _warmupLoggedVolume;

        // Cancel timer
        private Timer? _cancelTimer;

        // Overnight cancellation timer (for Overnight TIF orders)
        private Timer? _overnightCancelTimer;

        // Close position timer (time-based exit)
        private Timer? _closePositionTimer;
        private bool _closePositionTriggered;
        private int _closePositionOrderId = -1;

        // Adaptive order tracking
        private DateTime _lastAdaptiveAdjustmentTime = DateTime.MinValue;
        private int _lastAdaptiveScore;
        private double _originalTakeProfitPrice;
        private double _originalStopLossPrice;
        private double _currentAdaptiveTakeProfitPrice;
        private double _currentAdaptiveStopLossPrice;

        // Dynamic trading tracking
        private DateTime _lastTradeTime = DateTime.MinValue;
        private int _lastScore;
        private bool _indicatorsReady;
        private int _exitOrderId = -1;
        private bool _isLong = true;  // Tracks position direction
        private double _dynamicTakeProfit;   // Dynamic TP target
        private double _dynamicStopLoss;     // Dynamic SL target
        private bool _shortSaleBlocked;         // True if short sale was rejected for this ticker
        private double _previousClose;          // Previous session close for gap detection

        // Learning system - tracks patterns and outcomes per ticker
        private static readonly TickerProfileManager _profileManager = new();
        private TickerProfile? _tickerProfile;
        private TradeRecord? _pendingTradeRecord;
        private MarketScore? _entryScore;

        // AI-learned weights for market score calculation (if available)
        private IdiotProof.Learning.LearnedWeights? _learnedWeights;

        // LSH Pattern Matcher - provides "second opinion" based on historical analogs
        private IdiotProof.Learning.PatternMatcher? _patternMatcher;
        private IdiotProof.Learning.PatternForecast? _lastLshForecast;
        private IdiotProof.Helpers.IndicatorSnapshot? _lastIndicatorSnapshot;

        // AI Advisor - provides "third opinion" using ChatGPT analysis
        private IdiotProof.Learning.AIAdvisor? _aiAdvisor;
        private IdiotProof.Learning.AIAnalysis? _lastAiAnalysis;
        private DateTime _lastAiAnalysisTime = DateTime.MinValue;
        private readonly TimeSpan _aiAnalysisInterval = TimeSpan.FromMinutes(5);  // Rate limit AI calls

        // LSTM Predictor - provides deep learning-based price direction prediction
        private IdiotProof.Learning.LstmPredictor? _lstmPredictor;
        private IdiotProof.Learning.LstmPrediction? _lastLstmPrediction;
        private bool _lstmWarmupLogged;
        private DateTime _lastLstmTrainingTime = DateTime.MinValue;
        private readonly TimeSpan _lstmTrainingInterval = TimeSpan.FromMinutes(15);  // Retrain every 15 minutes

        // Historical metadata - provides insights about stock behavior
        private IdiotProof.Models.TickerMetadata? _tickerMetadata;

        /// <summary>
        /// Gets the shared ticker profile manager for learning across sessions.
        /// </summary>
        public static TickerProfileManager ProfileManager => _profileManager;

        /// <summary>
        /// Gets or sets the ticker metadata for informed trading decisions.
        /// </summary>
        public IdiotProof.Models.TickerMetadata? TickerMetadata
        {
            get => _tickerMetadata;
            set => _tickerMetadata = value;
        }

        // Result tracking
        private StrategyResult _result = StrategyResult.Running;


        // Daily reset tracking
        private DateOnly _lastCheckedDate;
        private bool _waitingForWindowLogged;
        private bool _windowEndedLogged;

        /// <summary>Gets the strategy being executed.</summary>
        public TradingStrategy Strategy => _strategy;

        /// <summary>Gets the symbol being traded.</summary>
        public string Symbol => _strategy.Symbol;

        /// <summary>Gets whether all conditions have been met and order executed.</summary>
        public bool IsComplete => _isComplete;

        /// <summary>Gets the current step index (0-based).</summary>
        public int CurrentStep => _currentConditionIndex;

        /// <summary>Gets the total number of conditions.</summary>
        public int TotalSteps => _strategy.Conditions.Count;

        /// <summary>Gets whether the entry order has been filled.</summary>
        public bool EntryFilled => _entryFilled;

        /// <summary>Gets the entry fill price.</summary>
        public double EntryFillPrice => _entryFillPrice;

        /// <summary>Gets whether the take profit order has been filled.</summary>
        public bool TakeProfitFilled => _takeProfitFilled;

        /// <summary>Gets the take profit target price.</summary>
        public double TakeProfitTarget => _takeProfitTarget;

        /// <summary>Gets the current VWAP value.</summary>
        public double CurrentVwap => GetVwap();

        /// <summary>Gets the last traded price.</summary>
        public double LastPrice => _lastPrice;

        /// <summary>Gets the final result of the strategy.</summary>
        public StrategyResult Result => _result;

        /// <summary>Gets the current condition name.</summary>
        public string CurrentConditionName =>
            _currentConditionIndex < _strategy.Conditions.Count
                ? _strategy.Conditions[_currentConditionIndex].Name
                : "Complete";

        /// <summary>
        /// Logs a timestamped message to both console and session log file.
        /// </summary>
        private void Log(string message, ConsoleColor? color = null, string category = "STRATEGY")
        {
            if (color.HasValue)
            {
                Console.ForegroundColor = color.Value;
            }
            Console.WriteLine($"{TimeStamp.NowBracketed} [{_strategy.Symbol}] {message}");
            if (color.HasValue)
            {
                Console.ResetColor();
            }
            SessionLogger?.LogEvent(category, $"[{_strategy.Symbol}] {message}");
        }

        public StrategyRunner(TradingStrategy strategy, IbContract contract, IbWrapper wrapper, EClientSocket client)
        {
            _strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
            _contract = contract ?? throw new ArgumentNullException(nameof(contract));
            _wrapper = wrapper ?? throw new ArgumentNullException(nameof(wrapper));
            _client = client ?? throw new ArgumentNullException(nameof(client));

            _currentConditionIndex = 0;
            _lastCheckedDate = DateOnly.FromDateTime(DateTime.Today);

            // Initialize candlestick aggregator (1-minute candles, trimmed to MaxCandlesticks setting)
            _candlestickAggregator = new Helpers.CandlestickAggregator(
                candleSizeMinutes: 1,
                maxCandles: AppSettings.MaxCandlesticks);
            _candlestickAggregator.OnCandleComplete += OnCandleComplete;

            // Initialize ATR calculator if ATR-based stop loss is configured
            if (strategy.Order.UseAtrStopLoss && strategy.Order.AtrStopLoss != null)
            {
                _atrCalculator = new Helpers.AtrCalculator(
                    period: strategy.Order.AtrStopLoss.Period,
                    ticksPerBar: 50 // Aggregate 50 ticks into one "bar" for ATR calculation
                );
            }

            // Initialize EMA and ADX calculators for any indicator conditions in the strategy
            InitializeIndicatorCalculators();

            // Load AI-learned weights if available for this ticker
            _learnedWeights = IdiotProof.Learning.LearnedWeights.Load(contract.Symbol);
            if (_learnedWeights != null)
            {
                Log($"[AI] Loaded learned weights for {contract.Symbol} (gen {_learnedWeights.Generation})", ConsoleColor.Magenta);
                
                // Initialize ALL calculators needed for AI learned weights
                // These are required to build ExtendedSnapshot with all values
                InitializeAllCalculatorsForAI();
            }

            // Initialize LSH Pattern Matcher for analog-based predictions
            var dataFolder = IdiotProof.Settings.SettingsManager.GetDataFolder();
            _patternMatcher = new IdiotProof.Learning.PatternMatcher(contract.Symbol, dataFolder);
            if (_patternMatcher.PatternCount > 0)
            {
                Log($"[LSH] Loaded {_patternMatcher.PatternCount} patterns for {contract.Symbol}", ConsoleColor.DarkCyan);
            }

            // Initialize AI Advisor for ChatGPT-powered decision support
            _aiAdvisor = new IdiotProof.Learning.AIAdvisor();
            if (_aiAdvisor.IsConfigured)
            {
                Log($"[AI] OpenAI advisor configured for {contract.Symbol}", ConsoleColor.Magenta);
            }

            // Subscribe to fill events
            _wrapper.OnOrderFill += OnOrderFill;

            // Subscribe to order rejection events to track failed orders
            _wrapper.OnOrderRejected += OnOrderRejected;

            Log("Strategy initialized - waiting for market data...", ConsoleColor.DarkGray);
        }

        /// <summary>
        /// Warms up all indicators using historical data.
        /// Call this after construction but before live trading starts.
        /// </summary>
        /// <param name="historicalBars">Historical bars in chronological order (oldest first).</param>
        /// <remarks>
        /// <para><b>Data Separation:</b></para>
        /// <para>Historical data is kept separate from live data:</para>
        /// <list type="bullet">
        ///   <item>Historical bars are used ONLY for indicator warm-up</item>
        ///   <item>They are NOT stored in the candlestick aggregator's active queue</item>
        ///   <item>Live candlesticks are aggregated separately from tick data</item>
        /// </list>
        /// <para>This separation enables future backtesting between any startDate and endDate.</para>
        /// </remarks>
        public void WarmUpFromHistoricalData(IReadOnlyList<HistoricalBar> historicalBars)
        {
            if (historicalBars.Count == 0)
            {
                Log("No historical data available for warm-up", ConsoleColor.DarkYellow);
                return;
            }

            Log($"Warming up indicators with {historicalBars.Count} historical bars...", ConsoleColor.Cyan);

            // Convert HistoricalBars to Candlesticks
            var candles = historicalBars.Select(bar => new Candlestick
            {
                Timestamp = bar.Time,
                Open = bar.Open,
                High = bar.High,
                Low = bar.Low,
                Close = bar.Close,
                Volume = bar.Volume,
                TickCount = bar.TradeCount ?? 1,
                IsComplete = true
            }).ToList();

            // Seed the candlestick aggregator (with events to update indicators)
            _candlestickAggregator.SeedWithHistoricalData(candles, fireEvents: true);

            // Log warm-up results
            int candleCount = _candlestickAggregator.CompletedCandleCount;
            Log($"  Loaded {candleCount} candles into aggregator", ConsoleColor.DarkGray);

            // Check indicator readiness
            int maxEmaPeriod = _emaCalculators.Count > 0 ? _emaCalculators.Keys.Max() : 0;
            if (maxEmaPeriod > 0)
            {
                bool emaReady = candleCount >= maxEmaPeriod;
                if (emaReady)
                {
                    _warmupLoggedEma = true;
                    var emaValues = string.Join(", ", _emaCalculators
                        .OrderBy(kvp => kvp.Key)
                        .Select(kvp => $"EMA({kvp.Key})=${kvp.Value.CurrentValue:F2}"));
                    Log($"  [OK] EMA warm-up complete: {emaValues}", ConsoleColor.Green);
                }
                else
                {
                    Log($"  [--] EMA needs {maxEmaPeriod} bars, only have {candleCount}", ConsoleColor.Yellow);
                }
            }

            if (_adxCalculator != null)
            {
                if (_adxCalculator.IsReady)
                {
                    _warmupLoggedAdx = true;
                    Log($"  [OK] ADX warm-up complete: ADX={_adxCalculator.CurrentAdx:F1}, +DI={_adxCalculator.PlusDI:F1}, -DI={_adxCalculator.MinusDI:F1}", ConsoleColor.Green);
                }
                else
                {
                    Log($"  [--] ADX needs 28 bars, only have {candleCount}", ConsoleColor.Yellow);
                }
            }

            if (_rsiCalculator != null)
            {
                if (_rsiCalculator.IsReady)
                {
                    _warmupLoggedRsi = true;
                    Log($"  [OK] RSI warm-up complete: RSI={_rsiCalculator.CurrentValue:F1}", ConsoleColor.Green);
                }
                else
                {
                    Log($"  [--] RSI needs 15 bars, only have {candleCount}", ConsoleColor.Yellow);
                }
            }

            if (_macdCalculator != null)
            {
                if (_macdCalculator.IsReady)
                {
                    _warmupLoggedMacd = true;
                    Log($"  [OK] MACD warm-up complete: MACD={_macdCalculator.MacdLine:F2}, Signal={_macdCalculator.SignalLine:F2}", ConsoleColor.Green);
                }
                else
                {
                    Log($"  [--] MACD needs 35 bars, only have {candleCount}", ConsoleColor.Yellow);
                }
            }

            if (_momentumCalculator != null)
            {
                if (_momentumCalculator.IsReady)
                {
                    _warmupLoggedMomentum = true;
                    Log($"  [OK] Momentum warm-up complete: Momentum={_momentumCalculator.CurrentValue:F2}", ConsoleColor.Green);
                }
                else
                {
                    Log($"  [--] Momentum needs 11 bars, only have {candleCount}", ConsoleColor.Yellow);
                }
            }

            if (_rocCalculator != null)
            {
                if (_rocCalculator.IsReady)
                {
                    _warmupLoggedRoc = true;
                    Log($"  [OK] ROC warm-up complete: ROC={_rocCalculator.CurrentValue:F2}%", ConsoleColor.Green);
                }
                else
                {
                    Log($"  [--] ROC needs 11 bars, only have {candleCount}", ConsoleColor.Yellow);
                }
            }

            if (_volumeCalculator != null)
            {
                if (_volumeCalculator.IsReady)
                {
                    _warmupLoggedVolume = true;
                    Log($"  [OK] Volume warm-up complete: Avg={_volumeCalculator.AverageVolume:N0}", ConsoleColor.Green);
                }
                else
                {
                    Log($"  [--] Volume needs 20 bars, only have {candleCount}", ConsoleColor.Yellow);
                }
            }

            // Update last price from most recent bar
            if (candles.Count > 0)
            {
                _lastPrice = candles[^1].Close;

                // Set previous close for gap conditions
                // Use the close of the day before the last bar (or just use the first bar's open if we only have intraday data)
                SetPreviousCloseForGapConditions(candles);
            }

            // Log historical metadata insights if available
            if (_tickerMetadata != null && _tickerMetadata.DaysAnalyzed > 0)
            {
                Log($"  [OK] Historical metadata loaded: {_tickerMetadata.DaysAnalyzed} days analyzed", ConsoleColor.Green);

                var de = _tickerMetadata.DailyExtremes;
                if (de.HodInFirst30MinPercent > 40)
                    Log($"       HOD typically early ({de.HodInFirst30MinPercent:F0}% in first 30 min)", ConsoleColor.DarkGray);
                if (de.LodInFirst30MinPercent > 40)
                    Log($"       LOD typically early ({de.LodInFirst30MinPercent:F0}% in first 30 min)", ConsoleColor.DarkGray);
                if (_tickerMetadata.SupportLevels.Count > 0)
                    Log($"       Key support: ${_tickerMetadata.SupportLevels[0].Price:F2}", ConsoleColor.DarkGray);
                if (_tickerMetadata.ResistanceLevels.Count > 0)
                    Log($"       Key resistance: ${_tickerMetadata.ResistanceLevels[0].Price:F2}", ConsoleColor.DarkGray);
                if (_tickerMetadata.BullishBias)
                    Log($"       Bullish bias ({_tickerMetadata.VwapBehavior.AvgPercentAboveVwap:F0}% above VWAP avg)", ConsoleColor.DarkGray);
                else if (_tickerMetadata.BearishBias)
                    Log($"       Bearish bias ({100 - _tickerMetadata.VwapBehavior.AvgPercentAboveVwap:F0}% below VWAP avg)", ConsoleColor.DarkGray);
            }

            Log($"Historical warm-up complete. Ready for live data.", ConsoleColor.Cyan);
        }

        /// <summary>
        /// Sets the previous session close price for all GapUp/GapDown conditions.
        /// </summary>
        /// <param name="candles">Historical candles to extract previous close from.</param>
        private void SetPreviousCloseForGapConditions(List<Candlestick> candles)
        {
            if (candles.Count == 0)
                return;

            // Use the close of the first candle as "previous close" (start of historical window)
            // In a real scenario, we'd want the close from the previous trading day
            // For now, use the first candle's open as a proxy for previous session close
            double previousClose = candles[0].Open;

            // If we have enough bars, use the close from the start of the historical window
            if (candles.Count > 1)
            {
                previousClose = candles[0].Close;
            }

            // Store previous close for potential use by autonomous trading
            _previousClose = previousClose;
            Log($"Previous close set to ${previousClose:F2} from historical data", ConsoleColor.DarkGray);
        }

        /// <summary>
        /// Called when a new candlestick completes.
        /// Updates all candle-based indicators.
        /// </summary>
        private void OnCandleComplete(Candlestick candle)
        {
            // Thread-safe check - don't process if disposed
            if (_disposed)
                return;

            try
            {
                // Log candle completion periodically for warm-up visibility
                int candleCount = _candlestickAggregator.CompletedCandleCount;

                // Update EMA calculators with candle close price
                foreach (var emaCalc in _emaCalculators.Values)
                {
                    try
                    {
                        emaCalc.Update(candle.Close);
                    }
                    catch (Exception ex)
                    {
                        Log($"WARNING: EMA calculator update failed: {ex.Message}", ConsoleColor.Yellow);
                    }
                }

                // Update ADX calculator with candle OHLC data (ADX needs High/Low/Close for True Range)
                try
                {
                    _adxCalculator?.UpdateFromCandle(candle.High, candle.Low, candle.Close);
                }
                catch (Exception ex)
                {
                    Log($"WARNING: ADX calculator update failed: {ex.Message}", ConsoleColor.Yellow);
                }

                // Update RSI calculator with candle close price
                try
                {
                    _rsiCalculator?.Update(candle.Close);
                }
                catch (Exception ex)
                {
                    Log($"WARNING: RSI calculator update failed: {ex.Message}", ConsoleColor.Yellow);
                }

                // Update MACD calculator with candle close price
                try
                {
                    _macdCalculator?.Update(candle.Close);
                }
                catch (Exception ex)
                {
                    Log($"WARNING: MACD calculator update failed: {ex.Message}", ConsoleColor.Yellow);
                }

                // Update Momentum calculator with candle close price
                try
                {
                    _momentumCalculator?.Update(candle.Close);
                }
                catch (Exception ex)
                {
                    Log($"WARNING: Momentum calculator update failed: {ex.Message}", ConsoleColor.Yellow);
                }

                // Update ROC calculator with candle close price
                try
                {
                    _rocCalculator?.Update(candle.Close);
                }
                catch (Exception ex)
                {
                    Log($"WARNING: ROC calculator update failed: {ex.Message}", ConsoleColor.Yellow);
                }

                // Update Volume calculator with candle volume
                try
                {
                    _volumeCalculator?.Update(candle.Volume);
                }
                catch (Exception ex)
                {
                    Log($"WARNING: Volume calculator update failed: {ex.Message}", ConsoleColor.Yellow);
                }

                // Update Bollinger Bands with candle close price
                try
                {
                    _bollingerBands?.Update(candle.Close);
                }
                catch (Exception ex)
                {
                    Log($"WARNING: Bollinger Bands calculator update failed: {ex.Message}", ConsoleColor.Yellow);
                }

                // Update extended indicator calculators
                try
                {
                    _stochasticCalculator?.Update(candle.High, candle.Low, candle.Close);
                    _obvCalculator?.Update(candle.Close, candle.Volume);
                    _cciCalculator?.Update(candle.High, candle.Low, candle.Close);
                    _williamsRCalculator?.Update(candle.High, candle.Low, candle.Close);
                }
                catch (Exception ex)
                {
                    Log($"WARNING: Extended indicator update failed: {ex.Message}", ConsoleColor.Yellow);
                }

                // Update LSTM predictor with indicator snapshot
                try
                {
                    if (_lstmPredictor != null && AreIndicatorsReadyForAutonomous())
                    {
                        var vwap = _vSum > 0 ? _pvSum / _vSum : candle.Close;
                        var snapshot = BuildIndicatorSnapshot(candle.Close, vwap);
                        _lstmPredictor.AddDataPoint(snapshot);
                        _lastLstmPrediction = _lstmPredictor.Predict();
                        
                        // Log LSTM warm-up complete
                        if (!_lstmWarmupLogged && _lastLstmPrediction?.IsUsable == true)
                        {
                            _lstmWarmupLogged = true;
                            var stats = _lstmPredictor.GetStats();
                            Log($"[OK] LSTM warm-up complete ({_lastLstmPrediction.Value.SequenceLength} data points)", ConsoleColor.Green);
                        }
                        
                        // Periodically retrain LSTM
                        var now = DateTime.UtcNow;
                        if ((now - _lastLstmTrainingTime) >= _lstmTrainingInterval)
                        {
                            _lstmPredictor.Train(epochs: 5, learningRate: 0.001);
                            _lastLstmTrainingTime = now;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log($"WARNING: LSTM predictor update failed: {ex.Message}", ConsoleColor.Yellow);
                }

                // Log warm-up progress
                if (!_warmupLoggedEma && _emaCalculators.Count > 0)
                {
                    int maxPeriod = _emaCalculators.Keys.Max();
                    if (candleCount >= maxPeriod)
                    {
                        _warmupLoggedEma = true;
                        Log($"[OK] EMA warm-up complete ({candleCount} candles collected)", ConsoleColor.Green);
                    }
                    else if (candleCount % 5 == 0) // Log every 5 candles during warm-up
                    {
                        Log($"Warming up EMA: {candleCount}/{maxPeriod} candles...", ConsoleColor.DarkGray);
                    }
                }

                if (!_warmupLoggedAdx && _adxCalculator != null)
                {
                    if (_adxCalculator.IsReady)
                    {
                        _warmupLoggedAdx = true;
                        Log($"[OK] ADX warm-up complete (ADX={_adxCalculator.CurrentAdx:F1})", ConsoleColor.Green);
                    }
                    else if (candleCount % 5 == 0 && candleCount <= 28)
                    {
                        Log($"Warming up ADX: {candleCount}/28 candles...", ConsoleColor.DarkGray);
                    }
                }

                // RSI warm-up logging
                if (!_warmupLoggedRsi && _rsiCalculator != null)
                {
                    if (_rsiCalculator.IsReady)
                    {
                        _warmupLoggedRsi = true;
                        Log($"[OK] RSI warm-up complete (RSI={_rsiCalculator.CurrentValue:F1})", ConsoleColor.Green);
                    }
                    else if (candleCount % 5 == 0 && candleCount <= 15)
                    {
                        Log($"Warming up RSI: {candleCount}/15 candles...", ConsoleColor.DarkGray);
                    }
                }

                // MACD warm-up logging
                if (!_warmupLoggedMacd && _macdCalculator != null)
                {
                    if (_macdCalculator.IsReady)
                    {
                        _warmupLoggedMacd = true;
                        Log($"[OK] MACD warm-up complete (MACD={_macdCalculator.MacdLine:F2}, Signal={_macdCalculator.SignalLine:F2})", ConsoleColor.Green);
                    }
                    else if (candleCount % 5 == 0 && candleCount <= 35)
                    {
                        Log($"Warming up MACD: {candleCount}/35 candles...", ConsoleColor.DarkGray);
                    }
                }

                // Momentum warm-up logging
                if (!_warmupLoggedMomentum && _momentumCalculator != null)
                {
                    if (_momentumCalculator.IsReady)
                    {
                        _warmupLoggedMomentum = true;
                        Log($"[OK] Momentum warm-up complete (Momentum={_momentumCalculator.CurrentValue:F2})", ConsoleColor.Green);
                    }
                    else if (candleCount % 5 == 0 && candleCount <= 11)
                    {
                        Log($"Warming up Momentum: {candleCount}/11 candles...", ConsoleColor.DarkGray);
                    }
                }

                // ROC warm-up logging
                if (!_warmupLoggedRoc && _rocCalculator != null)
                {
                    if (_rocCalculator.IsReady)
                    {
                        _warmupLoggedRoc = true;
                        Log($"[OK] ROC warm-up complete (ROC={_rocCalculator.CurrentValue:F2}%)", ConsoleColor.Green);
                    }
                    else if (candleCount % 5 == 0 && candleCount <= 11)
                    {
                        Log($"Warming up ROC: {candleCount}/11 candles...", ConsoleColor.DarkGray);
                    }
                }

                // Volume warm-up logging
                if (!_warmupLoggedVolume && _volumeCalculator != null)
                {
                    if (_volumeCalculator.IsReady)
                    {
                        _warmupLoggedVolume = true;
                        Log($"[OK] Volume warm-up complete (Avg={_volumeCalculator.AverageVolume:N0})", ConsoleColor.Green);
                    }
                    else if (candleCount % 5 == 0 && candleCount <= 20)
                    {
                        Log($"Warming up Volume: {candleCount}/20 candles...", ConsoleColor.DarkGray);
                    }
                }
            }
            catch (Exception ex)
            {
                // Log error but don't crash - graceful degradation
                Log($"ERROR in OnCandleComplete: {ex.Message}", ConsoleColor.Red);
            }
        }

        /// <summary>
        /// Initializes ALL indicator calculators needed for AI learned weights.
        /// This ensures live trading has the SAME data as backtesting.
        /// </summary>
        private void InitializeAllCalculatorsForAI()
        {
            Log("[AI] Initializing all calculators for learned weights...", ConsoleColor.DarkMagenta);
            
            // EMAs - required for VWAP/EMA scoring
            GetOrCreateEmaCalculator(9);
            GetOrCreateEmaCalculator(21);
            GetOrCreateEmaCalculator(50);
            
            // RSI - for overbought/oversold detection
            _rsiCalculator ??= new Helpers.RsiCalculator(period: 14);
            
            // MACD - for momentum
            _macdCalculator ??= new Helpers.MacdCalculator(12, 26, 9);
            
            // ADX/DI - for trend strength and direction
            _adxCalculator ??= new Helpers.AdxCalculator(period: 14, ticksPerBar: 50);
            
            // Volume - for confirmation signals
            _volumeCalculator ??= new Helpers.VolumeCalculator(period: 20);
            
            // Bollinger Bands - for mean reversion
            _bollingerBands ??= new Helpers.BollingerBandsCalculator(period: 20, multiplier: 2.0);
            
            // ATR - for volatility and TP/SL sizing
            if (_atrCalculator == null && _strategy.Order.UseAtrStopLoss == false)
            {
                _atrCalculator = new Helpers.AtrCalculator(period: 14, ticksPerBar: 50);
            }
            
            // Momentum and ROC - CRITICAL: these were missing before!
            _momentumCalculator ??= new Helpers.MomentumCalculator(period: 10);
            _rocCalculator ??= new Helpers.RocCalculator(period: 10);
            
            // Extended indicators for comprehensive market scoring
            _stochasticCalculator ??= new Helpers.StochasticCalculator(kPeriod: 14, dPeriod: 3);
            _obvCalculator ??= new Helpers.ObvCalculator(emaPeriod: 20);
            _cciCalculator ??= new Helpers.CciCalculator(period: 20);
            _williamsRCalculator ??= new Helpers.WilliamsRCalculator(period: 14);
            
            Log("[AI] All required calculators initialized for AI scoring", ConsoleColor.DarkMagenta);
        }

        /// <summary>
        /// Initializes all indicator calculators for the strategy.
        /// For AutonomousTrading, initializes all indicators for market score calculation.
        /// </summary>
        private void InitializeIndicatorCalculators()
        {
            // Skip condition-based initialization - AutonomousTrading uses market score

            if (_emaCalculators.Count > 0)
            {
                var periods = string.Join(", ", _emaCalculators.Keys.OrderBy(k => k));
                Log($"EMA tracking enabled for periods: {periods}", ConsoleColor.DarkCyan);
            }

            if (_adxCalculator != null)
            {
                Log($"ADX/DI tracking enabled (14-period)", ConsoleColor.DarkCyan);
            }

            if (_rsiCalculator != null)
            {
                Log($"RSI tracking enabled (14-period)", ConsoleColor.DarkCyan);
            }

            if (_macdCalculator != null)
            {
                Log($"MACD tracking enabled (12,26,9)", ConsoleColor.DarkCyan);
            }

            if (_momentumCalculator != null)
            {
                Log($"Momentum tracking enabled (10-period)", ConsoleColor.DarkCyan);
            }

            if (_rocCalculator != null)
            {
                Log($"ROC tracking enabled (10-period)", ConsoleColor.DarkCyan);
            }

            if (_volumeCalculator != null)
            {
                Log($"Volume tracking enabled (20-period average)", ConsoleColor.DarkCyan);
            }

            // Initialize all indicators for autonomous trading or adaptive order
            // These modes need to calculate market score, which requires all indicators
            if (_strategy.Order.UseAutonomousTrading || _strategy.Order.UseAdaptiveOrder)
            {
                InitializeAllIndicatorsForMarketScore();
            }

            // Load ticker profile for learning system
            if (_strategy.Order.UseAutonomousTrading)
            {
                _tickerProfile = _profileManager.GetProfile(_strategy.Symbol);
                if (_tickerProfile.TotalTrades > 0)
                {
                    Log($"Loaded ticker profile: {_tickerProfile.GetSummary()}", ConsoleColor.DarkCyan);
                }
            }
        }

        /// <summary>
        /// Initializes all indicators required for market score calculation.
        /// Called when autonomous trading or adaptive order is enabled.
        /// </summary>
        private void InitializeAllIndicatorsForMarketScore()
        {
            // Initialize EMA calculators for market score (9, 21, 50)
            GetOrCreateEmaCalculator(9);
            GetOrCreateEmaCalculator(21);
            GetOrCreateEmaCalculator(50);
            Log($"Initialized EMA(9,21,50) for market score calculation", ConsoleColor.DarkGray);

            // Initialize ADX/DI calculator
            _adxCalculator ??= new Helpers.AdxCalculator(period: 14, ticksPerBar: 50);
            Log($"Initialized ADX(14) for market score calculation", ConsoleColor.DarkGray);

            // Initialize RSI calculator
            _rsiCalculator ??= new Helpers.RsiCalculator(period: 14);
            Log($"Initialized RSI(14) for market score calculation", ConsoleColor.DarkGray);

            // Initialize MACD calculator
            _macdCalculator ??= new Helpers.MacdCalculator(12, 26, 9);
            Log($"Initialized MACD(12,26,9) for market score calculation", ConsoleColor.DarkGray);

            // Initialize Volume calculator
            _volumeCalculator ??= new Helpers.VolumeCalculator(period: 20);
            Log($"Initialized Volume(20) for market score calculation", ConsoleColor.DarkGray);

            // Initialize Bollinger Bands calculator for mean reversion signals
            _bollingerBands ??= new Helpers.BollingerBandsCalculator(period: 20, multiplier: 2.0);
            Log($"Initialized Bollinger Bands(20, 2.0) for mean reversion analysis", ConsoleColor.DarkGray);

            // Initialize ATR calculator for autonomous TP/SL calculation
            if (_strategy.Order.UseAutonomousTrading && _atrCalculator == null)
            {
                _atrCalculator = new Helpers.AtrCalculator(period: 14, ticksPerBar: 50);
                Log($"Initialized ATR(14) for autonomous TP/SL calculation", ConsoleColor.DarkGray);
            }

            // Initialize LSTM predictor for deep learning-based price prediction
            if (_strategy.Order.UseAutonomousTrading)
            {
                _lstmPredictor = new IdiotProof.Learning.LstmPredictor(_strategy.Symbol);
                Log($"Initialized LSTM predictor for {_strategy.Symbol}", ConsoleColor.DarkGray);
            }
        }

        /// <summary>
        /// Gets an existing EMA calculator for the period, or creates a new one.
        /// </summary>
        private Helpers.EmaCalculator GetOrCreateEmaCalculator(int period)
        {
            if (!_emaCalculators.TryGetValue(period, out var calculator))
            {
                calculator = new Helpers.EmaCalculator(period);
                _emaCalculators[period] = calculator;
            }
            return calculator;
        }

        /// <summary>
        /// Call this method when bid/ask prices are updated.
        /// </summary>
        public void OnBidAskUpdate(double bid, double ask)
        {
            if (_disposed)
                return;

            if (bid > 0)
                _lastBid = bid;
            if (ask > 0)
                _lastAsk = ask;
        }

        /// <summary>
        /// Call this method when a new trade tick is received.
        /// </summary>
        public void OnLastTrade(double lastPrice, int lastSize)
        {
            if (_disposed)
                return;

            _lastPrice = lastPrice;

            // Ignore invalid data
            if (lastPrice <= 0 || lastSize <= 0)
                return;

            // Update session high/low
            if (lastPrice > _sessionHigh)
                _sessionHigh = lastPrice;
            if (lastPrice < _sessionLow)
                _sessionLow = lastPrice;

            // Update VWAP: sum(price * size) / sum(size)
            _pvSum += lastPrice * lastSize;
            _vSum += lastSize;

            // Update ATR calculator if configured
            _atrCalculator?.Update(lastPrice);

            // Update candlestick aggregator - this will trigger OnCandleComplete when a candle closes
            // OnCandleComplete updates EMA and ADX calculators with the candle close price
            _candlestickAggregator.Update(lastPrice, lastSize);

            double vwap = GetVwap();

            // Monitor trailing stop loss if position is open
            if (_entryFilled && !_isComplete)
            {
                MonitorTrailingStopLoss(lastPrice);
                MonitorAdaptiveOrder(lastPrice, vwap);
                MonitorAdxRollover(lastPrice);
            }

            // For autonomous trading, monitor indicators for entry/exit decisions
            if (_strategy.Order.UseAutonomousTrading)
            {
                MonitorAutonomousTrading(lastPrice, vwap);
            }
            else
            {
                // Evaluate current condition (standard mode)
                EvaluateConditions(lastPrice, vwap);
            }
        }

        /// <summary>
        /// Monitors and updates the trailing stop loss based on current price.
        /// Supports both percentage-based and ATR-based trailing stops.
        /// </summary>
        private void MonitorTrailingStopLoss(double currentPrice)
        {
            var order = _strategy.Order;

            if (!order.EnableTrailingStopLoss || _trailingStopLossTriggered)
                return;

            bool isLong = order.Side == OrderSide.Buy;

            // Update high water mark for long positions
            if (isLong)
            {
                if (currentPrice > _highWaterMark)
                {
                    _highWaterMark = currentPrice;

                    // Calculate new trailing stop price based on ATR or percentage
                    double newStopPrice;
                    string stopDescription;

                    if (order.UseAtrStopLoss && _atrCalculator != null && _atrCalculator.IsReady)
                    {
                        // ATR-based trailing stop
                        var atrConfig = order.AtrStopLoss!;
                        newStopPrice = _atrCalculator.CalculateStopPrice(
                            referencePrice: _highWaterMark,
                            multiplier: atrConfig.Multiplier,
                            isLong: true,
                            minPercent: atrConfig.MinStopPercent,
                            maxPercent: atrConfig.MaxStopPercent
                        );
                        double atrValue = _atrCalculator.CurrentAtr;
                        double stopDistance = _highWaterMark - newStopPrice;
                        stopDescription = $"{atrConfig.Multiplier:F1}× ATR (ATR=${atrValue:F2}, Distance=${stopDistance:F2})";
                    }
                    else
                    {
                        // Percentage-based trailing stop (fallback or explicit)
                        newStopPrice = Math.Round(_highWaterMark * (1 - order.TrailingStopLossPercent), 2);
                        stopDescription = $"{order.TrailingStopLossPercent * 100:F1}% below ${_highWaterMark:F2}";
                    }

                    // Only update if the new stop is higher (tighter)
                    if (newStopPrice > _trailingStopLossPrice)
                    {
                        double oldStop = _trailingStopLossPrice;
                        _trailingStopLossPrice = newStopPrice;

                        if (oldStop > 0)
                        {
                            Log($"TRAILING STOP UPDATED: ${oldStop:F2} -> ${_trailingStopLossPrice:F2} (High: ${_highWaterMark:F2})", ConsoleColor.Magenta);
                        }
                        else
                        {
                            Log($"TRAILING STOP SET: ${_trailingStopLossPrice:F2} ({stopDescription})", ConsoleColor.Magenta);
                        }
                    }
                }

                // Check if current price dropped below trailing stop
                if (_trailingStopLossPrice > 0 && currentPrice <= _trailingStopLossPrice)
                {
                    Log($"*** TRAILING STOP TRIGGERED! Price ${currentPrice:F2} <= Stop ${_trailingStopLossPrice:F2}", ConsoleColor.Red);
                    ExecuteTrailingStopLoss();
                }
            }
            else
            {
                // For short positions, track low water mark (lowest price = best for short)
                // Initialize low water mark if not set
                if (_highWaterMark == 0 || currentPrice < _highWaterMark)
                {
                    _highWaterMark = currentPrice; // Reusing field - for shorts this is "low water mark"

                    // Calculate new trailing stop price based on ATR or percentage
                    double newStopPrice;
                    string stopDescription;

                    if (order.UseAtrStopLoss && _atrCalculator != null && _atrCalculator.IsReady)
                    {
                        // ATR-based trailing stop for shorts
                        var atrConfig = order.AtrStopLoss!;
                        newStopPrice = _atrCalculator.CalculateStopPrice(
                            referencePrice: _highWaterMark,
                            multiplier: atrConfig.Multiplier,
                            isLong: false,
                            minPercent: atrConfig.MinStopPercent,
                            maxPercent: atrConfig.MaxStopPercent
                        );
                        double atrValue = _atrCalculator.CurrentAtr;
                        double stopDistance = newStopPrice - _highWaterMark;
                        stopDescription = $"{atrConfig.Multiplier:F1}× ATR (ATR=${atrValue:F2}, Distance=${stopDistance:F2})";
                    }
                    else
                    {
                        // Percentage-based trailing stop for shorts (stop ABOVE low water mark)
                        newStopPrice = Math.Round(_highWaterMark * (1 + order.TrailingStopLossPercent), 2);
                        stopDescription = $"{order.TrailingStopLossPercent * 100:F1}% above ${_highWaterMark:F2}";
                    }

                    // Only update if the new stop is lower (tighter for shorts)
                    if (_trailingStopLossPrice == 0 || newStopPrice < _trailingStopLossPrice)
                    {
                        double oldStop = _trailingStopLossPrice;
                        _trailingStopLossPrice = newStopPrice;

                        if (oldStop > 0)
                        {
                            Log($"TRAILING STOP UPDATED: ${oldStop:F2} -> ${_trailingStopLossPrice:F2} (Low: ${_highWaterMark:F2})", ConsoleColor.Magenta);
                        }
                        else
                        {
                            Log($"TRAILING STOP SET: ${_trailingStopLossPrice:F2} ({stopDescription})", ConsoleColor.Magenta);
                        }
                    }
                }

                // Check if current price rose above trailing stop (bad for shorts)
                if (_trailingStopLossPrice > 0 && currentPrice >= _trailingStopLossPrice)
                {
                    Log($"*** TRAILING STOP TRIGGERED! Price ${currentPrice:F2} >= Stop ${_trailingStopLossPrice:F2}", ConsoleColor.Red);
                    ExecuteTrailingStopLoss();
                }
            }
        }

        /// <summary>
        /// Executes a sell order when trailing stop loss is triggered.
        /// Uses market order during RTH for faster execution, limit order outside RTH for safer fills.
        /// </summary>
        private void ExecuteTrailingStopLoss()
        {
            if (_trailingStopLossTriggered)
                return;

            _trailingStopLossTriggered = true;
            var order = _strategy.Order;

            // Cancel any existing take profit order (only if it wasn't rejected)
            if (_takeProfitOrderId > 0 && !_takeProfitFilled && !_takeProfitCancelled && !_takeProfitOrderRejected)
            {
                Log($"Cancelling take profit order #{_takeProfitOrderId}...", ConsoleColor.Yellow);
                _client.cancelOrder(_takeProfitOrderId, new OrderCancel());
                _takeProfitCancelled = true;
            }

            // Submit stop loss order
            _trailingStopLossOrderId = _wrapper.ConsumeNextOrderId();

            string action = order.Side == OrderSide.Buy ? "SELL" : "BUY";

            // Determine if we're in Regular Trading Hours
            var currentTimeET = TimezoneHelper.GetCurrentTime(MarketTimeZone.EST);
            bool isRTH = MarketTime.RTH.Contains(currentTimeET);

            Order stopOrder;

            if (isRTH)
            {
                // Use market order during RTH for immediate execution (good liquidity)
                stopOrder = new Order
                {
                    Action = action,
                    OrderType = "MKT",
                    TotalQuantity = order.Quantity,
                    OutsideRth = false,
                    Tif = "DAY"
                };
            }
            else
            {
                // Use limit order outside RTH for safer execution (low liquidity)
                // Set limit price slightly below current price to ensure fill
                // (price may have already dropped below stop level by the time order is submitted)
                bool isLong = order.Side == OrderSide.Buy;
                double offset = 0.02; // 2 cents buffer for slippage
                double limitPrice = isLong
                    ? Math.Round(Math.Min(_lastPrice, _trailingStopLossPrice) - offset, 2)  // Selling: slightly below current/stop
                    : Math.Round(Math.Max(_lastPrice, _trailingStopLossPrice) + offset, 2); // Covering short: slightly above

                stopOrder = new Order
                {
                    Action = action,
                    OrderType = "LMT",
                    LmtPrice = limitPrice,
                    TotalQuantity = order.Quantity,
                    OutsideRth = true,
                    Tif = "GTC"
                };
            }

            // Set account if specified
            if (!string.IsNullOrEmpty(AppSettings.AccountNumber))
            {
                stopOrder.Account = AppSettings.AccountNumber;
            }

            var sessionStr = isRTH ? "RTH" : "Extended";
            var orderTypeStr = isRTH ? "MKT" : $"LMT @ ${stopOrder.LmtPrice:F2}";
            Log($">> SUBMITTING TRAILING STOP LOSS {action} {order.Quantity} @ {orderTypeStr} ({sessionStr})", ConsoleColor.Red);
            Log($"  OrderId={_trailingStopLossOrderId} | Triggered at ${_lastPrice:F2} | Stop Level: ${_trailingStopLossPrice:F2}", ConsoleColor.DarkGray);

            _client.placeOrder(_trailingStopLossOrderId, _contract, stopOrder);
        }

        // ========================================================================
        // AUTONOMOUS TRADING - AI-driven entry and exit decisions
        // ========================================================================

        /// <summary>
        /// Monitors market conditions and autonomously decides when to enter/exit positions.
        /// Uses indicator-based market score to determine optimal trade timing.
        /// </summary>
        private void MonitorAutonomousTrading(double currentPrice, double vwap)
        {
            var order = _strategy.Order;
            var config = order.AutonomousTrading;

            if (config == null) return;

            // Check if we're within the time window
            var currentTimeET = TimezoneHelper.GetCurrentTime(MarketTimeZone.EST);
            
            // If StartTime is set and we haven't reached it yet, don't trade
            if (_strategy.StartTime.HasValue && currentTimeET < _strategy.StartTime.Value)
                return;

            // If EndTime is set and we've passed it, don't trade
            if (_strategy.EndTime.HasValue && currentTimeET > _strategy.EndTime.Value)
                return;

            // Check if indicators are ready for market score calculation
            if (!AreIndicatorsReadyForAutonomous())
            {
                if (!_indicatorsReady)
                {
                    // Log once that we're waiting for warm-up
                    return;
                }
            }
            else if (!_indicatorsReady)
            {
                _indicatorsReady = true;
                Log($"[OK] Autonomous trading indicators ready - monitoring for signals", ConsoleColor.Green);
            }

            // Rate limiting - don't trade too frequently
            var now = DateTime.UtcNow;
            var timeSinceLastTrade = (now - _lastTradeTime).TotalSeconds;
            if (timeSinceLastTrade < config.MinSecondsBetweenTrades)
                return;

            // Calculate market score
            // Use a temporary AdaptiveOrderConfig for score calculation
            var tempAdaptive = new AdaptiveOrderConfig { Mode = AdaptiveMode.Balanced };
            var originalAdaptive = order.AdaptiveOrder;
            
            // Create a modified order temporarily to access CalculateMarketScore
            // Pass optimized weights if available from the config
            var score = CalculateMarketScoreForAutonomous(currentPrice, vwap, config.OptimizedWeights);

            // Check if score changed significantly
            int scoreDelta = Math.Abs(score.TotalScore - _lastScore);
            if (scoreDelta < config.MinScoreChangeForTrade && _lastScore != 0)
                return;

            _lastScore = score.TotalScore;

            // Entry/Exit logic based on position state
            if (!_entryFilled)
            {
                // No position - look for entry signals
                HandleAutonomousEntry(currentPrice, vwap, score, config);
            }
            else if (!_isComplete)
            {
                // Position open - look for exit signals
                HandleAutonomousExit(currentPrice, vwap, score, config);
            }
        }

        /// <summary>
        /// Checks if all indicators needed for autonomous trading are warmed up.
        /// </summary>
        private bool AreIndicatorsReadyForAutonomous()
        {
            // Need at least EMA, ADX, RSI, and MACD to be ready
            bool emaReady = _emaCalculators.Count > 0 && _emaCalculators.Values.All(e => e.IsReady);
            bool adxReady = _adxCalculator?.IsReady ?? false;
            bool rsiReady = _rsiCalculator?.IsReady ?? false;
            bool macdReady = _macdCalculator?.IsReady ?? false;

            return emaReady && adxReady && rsiReady && macdReady;
        }

        /// <summary>
        /// Gets a time-of-day weight multiplier for score adjustment.
        /// Returns a value between 0.5 and 1.2 based on trading quality of the time period.
        /// </summary>
        private static double GetTimeOfDayWeight()
        {
            var now = TimezoneHelper.GetCurrentTime(MarketTimeZone.EST);
            int hour = now.Hour;
            int minute = now.Minute;
            double time = hour + (minute / 60.0);

            // Pre-market (4:00 AM - 6:00 AM): Low liquidity, be cautious
            if (time >= 4.0 && time < 6.0)
                return 0.7;

            // Early pre-market momentum (6:00 AM - 7:30 AM): Medium
            if (time >= 6.0 && time < 7.5)
                return 0.85;

            // Late pre-market (7:30 AM - 9:30 AM): Good momentum setups
            if (time >= 7.5 && time < 9.5)
                return 1.0;

            // Market Open (9:30 AM - 10:30 AM): BEST - high volume, clear direction
            if (time >= 9.5 && time < 10.5)
                return 1.2;

            // Mid-morning (10:30 AM - 11:30 AM): Good continuation
            if (time >= 10.5 && time < 11.5)
                return 1.1;

            // Lunch (11:30 AM - 1:00 PM): WORST - low volume, choppy
            if (time >= 11.5 && time < 13.0)
                return 0.5;

            // Early afternoon (1:00 PM - 2:00 PM): Recovering
            if (time >= 13.0 && time < 14.0)
                return 0.8;

            // Mid-afternoon (2:00 PM - 3:00 PM): Good
            if (time >= 14.0 && time < 15.0)
                return 1.0;

            // Power Hour (3:00 PM - 4:00 PM): EXCELLENT
            if (time >= 15.0 && time < 16.0)
                return 1.15;

            // After Hours (4:00 PM - 8:00 PM): Low liquidity
            if (time >= 16.0 && time < 20.0)
                return 0.6;

            // Night (8:00 PM - 4:00 AM): Very low activity
            return 0.4;
        }

        /// <summary>
        /// Calculates market score for autonomous trading decision making.
        /// Uses learned weights if available, otherwise falls back to SHARED MarketScoreCalculator.
        /// </summary>
        /// <param name="price">Current price.</param>
        /// <param name="vwap">Current VWAP.</param>
        /// <param name="optimizedWeights">Optional optimized weights (currently ignored - using shared calculator).</param>
        private MarketScore CalculateMarketScoreForAutonomous(double price, double vwap, 
            IdiotProof.Optimization.IndicatorWeights? optimizedWeights = null)
        {
            // Build indicator snapshot from live calculator values
            var snapshot = BuildIndicatorSnapshot(price, vwap);
            
            // Store for LSH pattern matching
            _lastIndicatorSnapshot = snapshot;
            
            double timeWeight = GetTimeOfDayWeight();
            int adjustedScore;
            int vwapScore, emaScore, rsiScore, macdScore, adxScore, volumeScore, bollingerScore;
            
            // Learned entry/exit signals (from AI training)
            bool learnedShouldLong = false;
            bool learnedShouldShort = false;
            bool learnedShouldExit = false;
            bool hasLearnedWeights = false;
            
            // Get indicator weights - learned or default
            var weights = _learnedWeights != null 
                ? _learnedWeights.ToIndicatorWeights() 
                : IdiotProof.Helpers.IndicatorWeights.Default;
            
            // SINGLE SOURCE OF TRUTH: Use same calculation for live and backtest
            var result = MarketScoreCalculator.Calculate(snapshot, weights);
            vwapScore = result.VwapScore;
            emaScore = result.EmaScore;
            rsiScore = result.RsiScore;
            macdScore = result.MacdScore;
            adxScore = result.AdxScore;
            volumeScore = result.VolumeScore;
            bollingerScore = result.BollingerScore;
            
            // Apply time weight to the score
            adjustedScore = (int)Math.Clamp(result.TotalScore * timeWeight, -100, 100);
            
            // LSTM prediction adjustment
            double lstmDirection = 0;
            double lstmConfidence = 0;
            double lstmPredictedChange = 0;
            double lstmVolatility = 0;
            int lstmScoreAdjustment = 0;
            bool hasLstmPrediction = false;
            
            if (_lastLstmPrediction?.IsUsable == true)
            {
                var lstm = _lastLstmPrediction.Value;
                lstmDirection = lstm.Direction;
                lstmConfidence = lstm.Confidence;
                lstmPredictedChange = lstm.PredictedChangePercent;
                lstmVolatility = lstm.PredictedVolatility;
                lstmScoreAdjustment = lstm.ScoreAdjustment;
                hasLstmPrediction = true;
                
                // Apply LSTM adjustment to score (max ±25 points)
                adjustedScore = (int)Math.Clamp(adjustedScore + lstmScoreAdjustment, -100, 100);
            }
            
            // If we have AI-learned weights, also calculate entry/exit signals
            if (_learnedWeights != null)
            {
                hasLearnedWeights = true;
                
                // Build FULL ExtendedSnapshot with ALL values for signal detection
                var extSnap = BuildExtendedSnapshot(price, vwap, snapshot);
                
                // Get entry/exit signals from the full weighted system
                var (_, shouldLong, shouldShort, shouldExit) = IdiotProof.Learning.WeightedScoreCalculator.Calculate(extSnap, _learnedWeights);
                
                // IMPORTANT: Capture the learned entry/exit signals!
                learnedShouldLong = shouldLong;
                learnedShouldShort = shouldShort;
                learnedShouldExit = shouldExit;
            }

            return new MarketScore
            {
                TotalScore = adjustedScore,
                VwapScore = vwapScore,
                EmaScore = emaScore,
                RsiScore = rsiScore,
                MacdScore = macdScore,
                AdxScore = adxScore,
                VolumeScore = volumeScore,
                BollingerScore = bollingerScore,
                TimeWeight = timeWeight,
                TakeProfitMultiplier = 1.0,
                StopLossMultiplier = 1.0,
                ShouldEmergencyExit = false,
                // LEARNED SIGNALS - from AI training
                HasLearnedWeights = hasLearnedWeights,
                LearnedShouldEnterLong = learnedShouldLong,
                LearnedShouldEnterShort = learnedShouldShort,
                LearnedShouldExit = learnedShouldExit,
                // LSTM PREDICTION SIGNALS
                LstmDirection = lstmDirection,
                LstmConfidence = lstmConfidence,
                LstmPredictedChangePercent = lstmPredictedChange,
                LstmPredictedVolatility = lstmVolatility,
                LstmScoreAdjustment = lstmScoreAdjustment,
                HasLstmPrediction = hasLstmPrediction
            };
        }
        
        /// <summary>
        /// Builds an indicator snapshot from live calculator values.
        /// </summary>
        private IndicatorSnapshot BuildIndicatorSnapshot(double price, double vwap)
        {
            // Get EMA values
            double ema9 = 0, ema21 = 0, ema50 = 0;
            if (_emaCalculators.TryGetValue(9, out var ema9Calc) && ema9Calc.IsReady)
                ema9 = ema9Calc.CurrentValue;
            if (_emaCalculators.TryGetValue(21, out var ema21Calc) && ema21Calc.IsReady)
                ema21 = ema21Calc.CurrentValue;
            if (_emaCalculators.TryGetValue(50, out var ema50Calc) && ema50Calc.IsReady)
                ema50 = ema50Calc.CurrentValue;
            
            // Get RSI
            double rsi = _rsiCalculator?.IsReady == true ? _rsiCalculator.CurrentValue : 50;
            
            // Get MACD
            double macd = 0, macdSignal = 0, macdHistogram = 0;
            if (_macdCalculator?.IsReady == true)
            {
                macd = _macdCalculator.MacdLine;
                macdSignal = _macdCalculator.SignalLine;
                macdHistogram = _macdCalculator.Histogram;
            }
            
            // Get ADX
            double adx = 0, plusDi = 0, minusDi = 0;
            if (_adxCalculator?.IsReady == true)
            {
                adx = _adxCalculator.CurrentAdx;
                plusDi = _adxCalculator.PlusDI;
                minusDi = _adxCalculator.MinusDI;
            }
            
            // Get Volume ratio
            double volumeRatio = _volumeCalculator?.IsReady == true ? _volumeCalculator.VolumeRatio : 1.0;
            
            // Get Bollinger Bands
            double bbUpper = 0, bbLower = 0, bbMiddle = 0;
            if (_bollingerBands?.IsReady == true)
            {
                bbUpper = _bollingerBands.UpperBand;
                bbLower = _bollingerBands.LowerBand;
                bbMiddle = _bollingerBands.MiddleBand;
            }
            
            // Get ATR
            double atr = _atrCalculator?.IsReady == true ? _atrCalculator.CurrentAtr : 0;
            
            // Get extended indicators
            double stochasticK = 0, stochasticD = 0;
            if (_stochasticCalculator?.IsReady == true)
            {
                stochasticK = _stochasticCalculator.PercentK;
                stochasticD = _stochasticCalculator.PercentD;
            }
            
            double obvSlope = 0;
            if (_obvCalculator?.IsReady == true)
            {
                // Convert boolean direction to normalized slope: +1 rising, -1 falling, 0 neutral
                obvSlope = _obvCalculator.IsRising ? 1.0 : (_obvCalculator.IsFalling ? -1.0 : 0);
            }
            double cci = _cciCalculator?.IsReady == true ? _cciCalculator.CurrentCci : 0;
            double williamsR = _williamsRCalculator?.IsReady == true ? _williamsRCalculator.CurrentValue : -50;
            
            return new IndicatorSnapshot
            {
                Price = price,
                Vwap = vwap,
                Ema9 = ema9,
                Ema21 = ema21,
                Ema50 = ema50,
                Rsi = rsi,
                Macd = macd,
                MacdSignal = macdSignal,
                MacdHistogram = macdHistogram,
                Adx = adx,
                PlusDi = plusDi,
                MinusDi = minusDi,
                VolumeRatio = volumeRatio,
                BollingerUpper = bbUpper,
                BollingerLower = bbLower,
                BollingerMiddle = bbMiddle,
                Atr = atr,
                StochasticK = stochasticK,
                StochasticD = stochasticD,
                ObvSlope = obvSlope,
                Cci = cci,
                WilliamsR = williamsR
            };
        }

        /// <summary>
        /// Builds a COMPLETE ExtendedSnapshot with ALL values needed for AI learning.
        /// This ensures live trading uses the SAME data as backtesting.
        /// </summary>
        private IdiotProof.Learning.ExtendedSnapshot BuildExtendedSnapshot(double price, double vwap, IndicatorSnapshot basic)
        {
            // Get Momentum and ROC values
            double momentum = _momentumCalculator?.IsReady == true ? _momentumCalculator.CurrentValue : 0;
            double roc = _rocCalculator?.IsReady == true ? _rocCalculator.CurrentValue : 0;
            
            // Pattern detection using recent candles
            bool isHigherLow = false;
            bool isLowerHigh = false;
            bool isNearLod = false;
            bool isNearHod = false;
            bool isVwapReclaim = false;
            bool isVwapRejection = false;
            
            var candles = _candlestickAggregator.GetCompletedCandles();
            if (candles.Count >= 4)
            {
                // Higher lows pattern (bullish)
                var c0 = candles[candles.Count - 1];
                var c1 = candles[candles.Count - 2];
                var c2 = candles[candles.Count - 3];
                var c3 = candles[candles.Count - 4];
                isHigherLow = c0.Low > c2.Low && c1.Low > c3.Low;
                isLowerHigh = c0.High < c2.High && c1.High < c3.High;
            }
            
            // Near HOD/LOD detection
            if (_sessionHigh > 0 && _sessionLow < double.MaxValue)
            {
                double range = _sessionHigh - _sessionLow;
                if (range > 0)
                {
                    isNearHod = price >= _sessionHigh - (range * 0.05);  // Within 5% of HOD
                    isNearLod = price <= _sessionLow + (range * 0.05);   // Within 5% of LOD
                }
            }
            
            // VWAP reclaim/rejection using last candle
            if (candles.Count >= 2 && vwap > 0)
            {
                var current = candles[candles.Count - 1];
                var prev = candles[candles.Count - 2];
                isVwapReclaim = prev.Close < vwap && current.Close > vwap;  // Crossed above
                isVwapRejection = current.High > vwap && current.Close < vwap;  // Wick above, close below
            }
            
            return new IdiotProof.Learning.ExtendedSnapshot
            {
                Price = basic.Price,
                Vwap = basic.Vwap,
                Ema9 = basic.Ema9,
                Ema21 = basic.Ema21,
                Ema50 = basic.Ema50,
                Rsi = basic.Rsi,
                Macd = basic.Macd,
                MacdSignal = basic.MacdSignal,
                MacdHistogram = basic.MacdHistogram,
                Adx = basic.Adx,
                PlusDi = basic.PlusDi,
                MinusDi = basic.MinusDi,
                VolumeRatio = basic.VolumeRatio,
                BollingerUpper = basic.BollingerUpper,
                BollingerLower = basic.BollingerLower,
                BollingerMiddle = basic.BollingerMiddle,
                Atr = basic.Atr,
                // CRITICAL: These were MISSING before - now matching backtesting!
                Momentum = momentum,
                Roc = roc,
                TimeOfDay = TimeOnly.FromDateTime(DateTime.Now),
                IsHigherLow = isHigherLow,
                IsLowerHigh = isLowerHigh,
                IsNearLod = isNearLod,
                IsNearHod = isNearHod,
                IsVwapReclaim = isVwapReclaim,
                IsVwapRejection = isVwapRejection
            };
        }

        /// <summary>
        /// Calculates dynamic thresholds for OPTIMIZED mode based on current market conditions.
        /// Automatically adjusts between aggressive/balanced/conservative based on:
        /// - ADX (trend strength): Strong trend = more aggressive
        /// - ATR (volatility): High volatility = more conservative  
        /// - Indicator agreement: High agreement = more aggressive
        /// - RSI extremes: Overbought/oversold = more conservative (reversal risk)
        /// </summary>
        private (int longEntry, int shortEntry, int longExit, int shortExit, double tpMultiplier, double slMultiplier, string reasoning) 
            CalculateDynamicThresholds(double currentPrice, MarketScore score)
        {
            // Start with balanced defaults from TradingDefaults
            int longEntry = TradingDefaults.LongEntryThreshold;
            int shortEntry = TradingDefaults.ShortEntryThreshold;
            int longExit = TradingDefaults.LongExitThreshold;
            int shortExit = TradingDefaults.ShortExitThreshold;
            double tpMultiplier = TradingDefaults.TpAtrMultiplier;
            double slMultiplier = TradingDefaults.SlAtrMultiplier;
            var reasons = new List<string>();
            
            // 1. ADX Trend Strength Adjustment
            // Strong trend (ADX > 30) = more aggressive, weak trend (ADX < 20) = more conservative
            double adx = _adxCalculator?.CurrentAdx ?? 20;
            if (adx >= TradingDefaults.AdxStrongTrend)
            {
                // Very strong trend - be aggressive
                longEntry -= 15;  // 50
                shortEntry += 15; // -50
                longExit -= 10;   // 25
                shortExit += 10;  // -25
                tpMultiplier = TradingDefaults.StrongTrend.TpMultiplier;
                slMultiplier = TradingDefaults.StrongTrend.SlMultiplier;
                reasons.Add($"ADX {adx:F0} (strong trend->aggressive)");
            }
            else if (adx >= TradingDefaults.AdxModerateTrend)
            {
                // Moderate trend - slightly aggressive
                longEntry -= 5;   // 60
                shortEntry += 5;  // -60
                tpMultiplier = TradingDefaults.ModerateTrend.TpMultiplier;
                slMultiplier = TradingDefaults.ModerateTrend.SlMultiplier;
                reasons.Add($"ADX {adx:F0} (moderate trend)");
            }
            else if (adx < TradingDefaults.AdxRangingMarket)
            {
                // Weak/ranging - be conservative
                longEntry += 10;  // 75
                shortEntry -= 10; // -75
                longExit += 10;   // 45
                shortExit -= 10;  // -45
                tpMultiplier = TradingDefaults.RangingMarket.TpMultiplier;
                slMultiplier = TradingDefaults.RangingMarket.SlMultiplier;
                reasons.Add($"ADX {adx:F0} (ranging->conservative)");
            }
            
            // 2. ATR Volatility Adjustment
            // High volatility = wider stops, higher thresholds
            if (_atrCalculator?.IsReady == true)
            {
                double atrPercent = (_atrCalculator.CurrentAtr / currentPrice) * 100;
                
                if (atrPercent > 5.0)
                {
                    // Very high volatility - be conservative
                    longEntry += 10;
                    shortEntry -= 10;
                    slMultiplier += 0.5;
                    reasons.Add($"ATR {atrPercent:F1}% (high vol->conservative)");
                }
                else if (atrPercent > 3.0)
                {
                    // Moderate-high volatility
                    longEntry += 5;
                    shortEntry -= 5;
                    reasons.Add($"ATR {atrPercent:F1}% (moderate vol)");
                }
                else if (atrPercent < 1.0)
                {
                    // Low volatility - can be more aggressive
                    longEntry -= 5;
                    shortEntry += 5;
                    tpMultiplier -= 0.3;
                    reasons.Add($"ATR {atrPercent:F1}% (low vol->aggressive)");
                }
            }
            
            // 3. Indicator Agreement Adjustment
            // When all indicators strongly agree, lower thresholds
            int indicatorAgreement = CalculateIndicatorAgreement(score);
            if (indicatorAgreement >= 80)
            {
                // Strong agreement - very aggressive
                longEntry -= 10;
                shortEntry += 10;
                reasons.Add($"Indicators {indicatorAgreement}% agree (strong->aggressive)");
            }
            else if (indicatorAgreement >= 60)
            {
                // Good agreement
                longEntry -= 5;
                shortEntry += 5;
                reasons.Add($"Indicators {indicatorAgreement}% agree");
            }
            else if (indicatorAgreement < 40)
            {
                // Mixed signals - be conservative
                longEntry += 10;
                shortEntry -= 10;
                reasons.Add($"Indicators {indicatorAgreement}% mixed (->conservative)");
            }
            
            // 4. RSI Extreme Adjustment
            // Be careful at extremes - higher risk of reversal
            double rsi = _rsiCalculator?.CurrentValue ?? 50;
            if (rsi > 75)
            {
                // Overbought - don't go long aggressively
                longEntry += 15;
                reasons.Add($"RSI {rsi:F0} (overbought->careful long)");
            }
            else if (rsi < 25)
            {
                // Oversold - don't go short aggressively
                shortEntry -= 15;
                reasons.Add($"RSI {rsi:F0} (oversold->careful short)");
            }
            
            // 5. Time of Day Adjustment
            // Be more conservative in first 15 minutes and last 30 minutes
            var currentTimeET = TimezoneHelper.GetCurrentTime(MarketTimeZone.EST);
            var minsFromOpen = (int)(currentTimeET - new TimeOnly(9, 30)).TotalMinutes;
            
            if (minsFromOpen >= 0 && minsFromOpen <= 15)
            {
                // First 15 minutes - volatile, be conservative
                longEntry += 10;
                shortEntry -= 10;
                reasons.Add("First 15min->conservative");
            }
            else if (minsFromOpen >= 360) // After 3:30 PM
            {
                // Last 30 minutes - be conservative
                longEntry += 5;
                shortEntry -= 5;
                reasons.Add("Last 30min->conservative");
            }
            
            // Clamp thresholds to valid ranges
            longEntry = Math.Clamp(longEntry, 45, 85);
            shortEntry = Math.Clamp(shortEntry, -85, -45);
            longExit = Math.Clamp(longExit, 15, 55);
            shortExit = Math.Clamp(shortExit, -55, -15);
            tpMultiplier = Math.Clamp(tpMultiplier, TradingDefaults.MinTpAtrMultiplier, TradingDefaults.MaxTpAtrMultiplier);
            slMultiplier = Math.Clamp(slMultiplier, TradingDefaults.MinSlAtrMultiplier, TradingDefaults.MaxSlAtrMultiplier);
            
            string reasoning = reasons.Count > 0 ? string.Join(", ", reasons) : "Using defaults";
            
            return (longEntry, shortEntry, longExit, shortExit, tpMultiplier, slMultiplier, reasoning);
        }
        
        /// <summary>
        /// Calculates how strongly the indicators agree (0-100%).
        /// 100% = all indicators pointing same direction with strong values.
        /// </summary>
        private int CalculateIndicatorAgreement(MarketScore score)
        {
            // Count how many indicators agree on direction
            int bullishCount = 0;
            int bearishCount = 0;
            int totalIndicators = 0;
            
            // VWAP (weight: 1)
            if (score.VwapScore > 10) bullishCount++;
            else if (score.VwapScore < -10) bearishCount++;
            totalIndicators++;
            
            // EMA (weight: 1)
            if (score.EmaScore > 20) bullishCount++;
            else if (score.EmaScore < -20) bearishCount++;
            totalIndicators++;
            
            // RSI (weight: 1)
            if (score.RsiScore > 10) bullishCount++;
            else if (score.RsiScore < -10) bearishCount++;
            totalIndicators++;
            
            // MACD (weight: 1)
            if (score.MacdScore > 20) bullishCount++;
            else if (score.MacdScore < -20) bearishCount++;
            totalIndicators++;
            
            // ADX (weight: 1)
            if (score.AdxScore > 20) bullishCount++;
            else if (score.AdxScore < -20) bearishCount++;
            totalIndicators++;
            
            // Volume (weight: 1)
            if (score.VolumeScore > 10) bullishCount++;
            else if (score.VolumeScore < -10) bearishCount++;
            totalIndicators++;
            
            // Agreement = percentage of indicators pointing in the dominant direction
            int dominant = Math.Max(bullishCount, bearishCount);
            int agreement = (dominant * 100) / totalIndicators;
            
            // Boost if total score strength backs it up
            int scoreStrength = Math.Abs(score.TotalScore);
            if (scoreStrength >= 70) agreement = Math.Min(100, agreement + 15);
            else if (scoreStrength >= 50) agreement = Math.Min(100, agreement + 5);
            
            return agreement;
        }

        /// <summary>
        /// Handles autonomous entry decision when no position is open.
        /// Uses learned patterns from ticker profile to adjust thresholds.
        /// </summary>
        private void HandleAutonomousEntry(double currentPrice, double vwap, MarketScore score, AutonomousTradingConfig config)
        {
            // Apply ATR volatility filter - skip entries when volatility is too low or too high
            // Small caps and volatile growth stocks can have 10-20% ATR, so use 15% as upper limit
            if (_atrCalculator?.IsReady == true)
            {
                double atr = _atrCalculator.CurrentAtr;
                double atrPercent = (atr / currentPrice) * 100;
                
                // Too low volatility (< 0.3%) - not enough movement potential
                if (atrPercent < 0.3)
                {
                    Log($"[FILTER] Skipping entry - ATR too low ({atrPercent:F2}% < 0.3%) - insufficient volatility", ConsoleColor.DarkYellow);
                    return;
                }
                
                // Too high volatility (> 15%) - extreme volatility, likely earnings/major news
                if (atrPercent > 15.0)
                {
                    Log($"[FILTER] Skipping entry - ATR too high ({atrPercent:F2}% > 15.0%) - excessive volatility", ConsoleColor.DarkYellow);
                    return;
                }
            }

            // Apply Support/Resistance awareness from ticker metadata
            int srScoreAdjustment = 0;
            if (_tickerMetadata != null)
            {
                // Check if near support (bullish bias)
                var nearestSupport = _tickerMetadata.SupportLevels
                    .Where(s => s.IsValid)
                    .OrderBy(s => Math.Abs(currentPrice - s.Price))
                    .FirstOrDefault();
                
                if (nearestSupport != null)
                {
                    double distancePercent = Math.Abs(currentPrice - nearestSupport.Price) / currentPrice * 100;
                    if (distancePercent <= 1.0) // Within 1% of support
                    {
                        srScoreAdjustment = (int)(nearestSupport.Strength * 15); // +15 max for strong support
                        Log($"[S/R] Near support ${nearestSupport.Price:F2} (strength {nearestSupport.Strength:F2}) - Score adjustment +{srScoreAdjustment}", ConsoleColor.DarkCyan);
                    }
                }
                
                // Check if near resistance (bearish bias)
                var nearestResistance = _tickerMetadata.ResistanceLevels
                    .Where(r => r.IsValid)
                    .OrderBy(r => Math.Abs(currentPrice - r.Price))
                    .FirstOrDefault();
                
                if (nearestResistance != null)
                {
                    double distancePercent = Math.Abs(currentPrice - nearestResistance.Price) / currentPrice * 100;
                    if (distancePercent <= 1.0) // Within 1% of resistance
                    {
                        int negAdjust = (int)(nearestResistance.Strength * 15); // -15 max for strong resistance
                        srScoreAdjustment -= negAdjust;
                        Log($"[S/R] Near resistance ${nearestResistance.Price:F2} (strength {nearestResistance.Strength:F2}) - Score adjustment -{negAdjust}", ConsoleColor.DarkCyan);
                    }
                }
            }

            // Calculate dynamic thresholds that self-adjust based on market conditions
            var (dynLong, dynShort, dynLongExit, dynShortExit, dynTp, dynSl, reasoning) = 
                CalculateDynamicThresholds(currentPrice, score);
            
            int adjustedLongThreshold = dynLong;
            int adjustedShortThreshold = dynShort;
            double dynamicTpMultiplier = dynTp;
            double dynamicSlMultiplier = dynSl;
            
            // Log the dynamic adjustments
            Log($"[AUTO] Thresholds: Long>={dynLong}, Short<={dynShort} | {reasoning}", ConsoleColor.DarkCyan);

            // Apply S/R adjustment to thresholds (more lenient when near support, stricter near resistance)
            adjustedLongThreshold -= srScoreAdjustment;
            adjustedShortThreshold += srScoreAdjustment;

            if (_tickerProfile != null && _tickerProfile.Confidence >= 20)
            {
                adjustedLongThreshold = _tickerProfile.GetAdjustedLongEntryThreshold(config.LongEntryThreshold) - srScoreAdjustment;
                adjustedShortThreshold = _tickerProfile.GetAdjustedShortEntryThreshold(config.ShortEntryThreshold) + srScoreAdjustment;

                // Apply score adjustment based on streaks
                int adjustment = _tickerProfile.GetScoreAdjustment(score.TotalScore, true);
                adjustedLongThreshold += adjustment;
                adjustedShortThreshold -= adjustment;

                // Check if current time is a good window based on history
                var currentTimeET = TimezoneHelper.GetCurrentTime(MarketTimeZone.EST);
                var currentDateTime = DateTime.Today.AddHours(currentTimeET.Hour).AddMinutes(currentTimeET.Minute);
                if (!_tickerProfile.IsGoodTimeWindow(currentDateTime))
                {
                    // Skip entry during historically poor time windows
                    return;
                }
            }

            // Apply historical metadata adjustments (HOD/LOD patterns, support/resistance)
            if (_tickerMetadata != null)
            {
                var currentTimeET = TimezoneHelper.GetCurrentTime(MarketTimeZone.EST);
                int minutesFromOpen = (int)(new TimeSpan(currentTimeET.Hour, currentTimeET.Minute, 0) - new TimeSpan(9, 30, 0)).TotalMinutes;
                if (minutesFromOpen < 0) minutesFromOpen = 0;

                // Get adjustment based on metadata patterns
                int metadataAdjustment = _tickerMetadata.GetEntryAdjustment(currentPrice, minutesFromOpen, true);
                if (metadataAdjustment != 0)
                {
                    adjustedLongThreshold -= metadataAdjustment;  // Lower threshold if conditions favor entry
                    adjustedShortThreshold += metadataAdjustment;

                    // Log metadata influence
                    if (metadataAdjustment > 5 || metadataAdjustment < -5)
                    {
                        string reason = "";
                        if (_tickerMetadata.IsNearSupport(currentPrice)) reason += "near support, ";
                        if (_tickerMetadata.IsNearResistance(currentPrice)) reason += "near resistance, ";
                        if (_tickerMetadata.HodTypicallyEarly && minutesFromOpen > 30) reason += "HOD usually early, ";
                        if (Math.Abs(minutesFromOpen - (int)_tickerMetadata.DailyExtremes.AvgLodMinutesFromOpen) < 30) reason += "near typical LOD time, ";

                        if (!string.IsNullOrEmpty(reason))
                        {
                            Log($"[METADATA] Threshold adjusted by {metadataAdjustment}: {reason.TrimEnd(',', ' ')}", ConsoleColor.DarkGray);
                        }
                    }
                }
            }

            // =====================================================================
            // ENTRY DECISION: Use learned signals when available, else use thresholds
            // =====================================================================
            
            bool shouldEnterLong;
            bool shouldEnterShort;
            
            if (score.HasLearnedWeights)
            {
                // AI-learned weights determine entry - this is the trained decision!
                shouldEnterLong = score.LearnedShouldEnterLong;
                shouldEnterShort = score.LearnedShouldEnterShort;
                
                if (shouldEnterLong || shouldEnterShort)
                {
                    Log($"[AI] Learned weights triggered entry: Long={shouldEnterLong}, Short={shouldEnterShort}, Score={score.TotalScore}", ConsoleColor.Magenta);
                }
            }
            else
            {
                // Fallback: use hardcoded thresholds
                shouldEnterLong = score.TotalScore >= adjustedLongThreshold;
                shouldEnterShort = score.TotalScore <= adjustedShortThreshold;
            }

            // =====================================================================
            // LSH PATTERN MATCHING: Get "second opinion" from historical analogs
            // =====================================================================
            
            if (_patternMatcher != null && _patternMatcher.PatternCount >= 100 && _lastIndicatorSnapshot.HasValue)
            {
                var lshForecast = _patternMatcher.GetForecast(_lastIndicatorSnapshot.Value, maxAnalogs: 15, maxDistance: 85);
                _lastLshForecast = lshForecast;
                
                if (lshForecast.IsUsable)
                {
                    // LSH provides a "confirming" or "vetoing" signal
                    // If main system wants to enter but LSH strongly disagrees, skip
                    // If main system is neutral but LSH is very confident, consider entry
                    
                    bool lshConfirmsLong = lshForecast.SuggestedDirection == 1 && lshForecast.Confidence >= 0.6;
                    bool lshConfirmsShort = lshForecast.SuggestedDirection == -1 && lshForecast.Confidence >= 0.6;
                    bool lshVetoesLong = lshForecast.SuggestedDirection == -1 && lshForecast.Confidence >= 0.7;
                    bool lshVetoesShort = lshForecast.SuggestedDirection == 1 && lshForecast.Confidence >= 0.7;
                    
                    // Log LSH opinion
                    if (shouldEnterLong || shouldEnterShort || lshForecast.Confidence >= 0.65)
                    {
                        string direction = lshForecast.SuggestedDirection switch
                        {
                            1 => "LONG",
                            -1 => "SHORT",
                            _ => "NEUTRAL"
                        };
                        Log($"[LSH] {lshForecast.AnalogCount} analogs: {direction} | P(up)={lshForecast.ProbabilityHigher:P0} | Conf={lshForecast.Confidence:P0} | AvgRet={lshForecast.AverageReturn:+0.00%;-0.00%}", ConsoleColor.DarkCyan);
                    }
                    
                    // Apply LSH influence
                    if (shouldEnterLong && lshVetoesLong)
                    {
                        Log($"[LSH] VETO: Skipping LONG - historical analogs strongly bearish", ConsoleColor.DarkYellow);
                        shouldEnterLong = false;
                    }
                    else if (shouldEnterShort && lshVetoesShort)
                    {
                        Log($"[LSH] VETO: Skipping SHORT - historical analogs strongly bullish", ConsoleColor.DarkYellow);
                        shouldEnterShort = false;
                    }
                    else if (!shouldEnterLong && !shouldEnterShort && lshConfirmsLong && score.TotalScore >= adjustedLongThreshold - 10)
                    {
                        // LSH strongly agrees and score is close - boost into entry
                        Log($"[LSH] BOOST: Entering LONG - historical analogs strongly bullish (score was {score.TotalScore}, needed {adjustedLongThreshold})", ConsoleColor.Cyan);
                        shouldEnterLong = true;
                    }
                    else if (!shouldEnterLong && !shouldEnterShort && lshConfirmsShort && score.TotalScore <= adjustedShortThreshold + 10)
                    {
                        Log($"[LSH] BOOST: Entering SHORT - historical analogs strongly bearish (score was {score.TotalScore}, needed {adjustedShortThreshold})", ConsoleColor.Magenta);
                        shouldEnterShort = true;
                    }
                }
            }

            // =====================================================================
            // AI ADVISOR: Get ChatGPT analysis as "third opinion" (rate-limited)
            // =====================================================================
            
            if (_aiAdvisor != null && _aiAdvisor.IsConfigured && 
                (shouldEnterLong || shouldEnterShort) && 
                DateTime.UtcNow - _lastAiAnalysisTime >= _aiAnalysisInterval &&
                _lastIndicatorSnapshot.HasValue)
            {
                // Fire-and-forget AI analysis to avoid blocking trading decisions
                // The analysis runs async and updates _lastAiAnalysis when complete
                var snapshot = _lastIndicatorSnapshot.Value;
                var lshForecast = _lastLshForecast;
                var learnedWeights = _learnedWeights;
                var scoreResult = new IdiotProof.Helpers.MarketScoreResult
                {
                    TotalScore = score.TotalScore,
                    VwapScore = score.VwapScore,
                    EmaScore = score.EmaScore,
                    RsiScore = score.RsiScore,
                    MacdScore = score.MacdScore,
                    AdxScore = score.AdxScore,
                    VolumeScore = score.VolumeScore,
                    BollingerScore = score.BollingerScore
                };
                
                // Fire-and-forget pattern - the task runs in background
                Task.Run(async () =>
                {
                    try
                    {
                        var aiAnalysis = await _aiAdvisor.AnalyzeEntryAsync(
                            _strategy.Symbol,
                            snapshot,
                            lshForecast,
                            scoreResult,
                            learnedWeights);
                        
                        _lastAiAnalysis = aiAnalysis;
                        _lastAiAnalysisTime = DateTime.UtcNow;
                        
                        if (aiAnalysis.IsUsable)
                        {
                            Log($"[AI] {aiAnalysis.Action} (Conf={aiAnalysis.Confidence}%): {aiAnalysis.Reasoning}", ConsoleColor.Magenta);
                            if (aiAnalysis.RiskFactors.Count > 0)
                            {
                                Log($"[AI] Risks: {string.Join(", ", aiAnalysis.RiskFactors)}", ConsoleColor.DarkGray);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"[AI] Error: {ex.Message}", ConsoleColor.DarkRed);
                    }
                });
            }

            // Check for LONG entry
            if (shouldEnterLong)
            {
                string thresholdInfo = score.HasLearnedWeights 
                    ? " (AI-learned weights)"
                    : (adjustedLongThreshold != config.LongEntryThreshold
                        ? $" (threshold: {adjustedLongThreshold}, default: {config.LongEntryThreshold})"
                        : "");
                Log($"*** AUTONOMOUS LONG SIGNAL: Score {score.TotalScore} >= {adjustedLongThreshold}{thresholdInfo}", ConsoleColor.Cyan);
                Log($"  Indicators: VWAP={score.VwapScore}, EMA={score.EmaScore}, RSI={score.RsiScore}, MACD={score.MacdScore}, ADX={score.AdxScore}, Vol={score.VolumeScore}", ConsoleColor.DarkGray);
                if (_tickerProfile?.Confidence >= 20)
                {
                    Log($"  Profile: {_tickerProfile.TotalTrades} trades, {_tickerProfile.WinRate:F1}% win rate, Conf={_tickerProfile.Confidence}%", ConsoleColor.DarkGray);
                }
                
                // Calculate TP/SL based on ATR with self-adjusting multipliers
                var (takeProfit, stopLoss) = CalculateAutonomousTpSl(currentPrice, true, config, 
                    dynamicTpMultiplier, dynamicSlMultiplier);
                
                Log($"  Auto TP: ${takeProfit:F2} | Auto SL: ${stopLoss:F2}", ConsoleColor.DarkGray);

                // Store entry score for learning
                _entryScore = score;

                // Create pending trade record for learning
                _pendingTradeRecord = new TradeRecord
                {
                    EntryTime = DateTime.UtcNow,
                    EntryPrice = currentPrice,
                    IsLong = true,
                    Quantity = _strategy.Order.Quantity,
                    EntryScore = score.TotalScore,
                    EntryVwapScore = score.VwapScore,
                    EntryEmaScore = score.EmaScore,
                    EntryRsiScore = score.RsiScore,
                    EntryMacdScore = score.MacdScore,
                    EntryAdxScore = score.AdxScore,
                    EntryVolumeScore = score.VolumeScore,
                    RsiAtEntry = _rsiCalculator?.CurrentValue ?? 50,
                    AdxAtEntry = _adxCalculator?.CurrentAdx ?? 20
                };
                
                // Execute long entry
                ExecuteAutonomousEntry(currentPrice, vwap, true, takeProfit, stopLoss, config);
                return;
            }

            // Check for SHORT entry (if allowed and not blocked)
            if (config.AllowShort && !_shortSaleBlocked && shouldEnterShort)
            {
                string thresholdInfo = score.HasLearnedWeights 
                    ? " (AI-learned weights)"
                    : (adjustedShortThreshold != config.ShortEntryThreshold
                        ? $" (threshold: {adjustedShortThreshold}, default: {config.ShortEntryThreshold})"
                        : "");
                Log($"*** AUTONOMOUS SHORT SIGNAL: Score {score.TotalScore} <= {adjustedShortThreshold}{thresholdInfo}", ConsoleColor.Magenta);
                Log($"  Indicators: VWAP={score.VwapScore}, EMA={score.EmaScore}, RSI={score.RsiScore}, MACD={score.MacdScore}, ADX={score.AdxScore}, Vol={score.VolumeScore}", ConsoleColor.DarkGray);
                if (_tickerProfile?.Confidence >= 20)
                {
                    Log($"  Profile: {_tickerProfile.TotalTrades} trades, {_tickerProfile.WinRate:F1}% win rate, Conf={_tickerProfile.Confidence}%", ConsoleColor.DarkGray);
                }
                
                // Calculate TP/SL based on ATR with self-adjusting multipliers
                var (takeProfit, stopLoss) = CalculateAutonomousTpSl(currentPrice, false, config,
                    dynamicTpMultiplier, dynamicSlMultiplier);
                
                Log($"  Auto TP: ${takeProfit:F2} | Auto SL: ${stopLoss:F2}", ConsoleColor.DarkGray);

                // Store entry score for learning
                _entryScore = score;

                // Create pending trade record for learning
                _pendingTradeRecord = new TradeRecord
                {
                    EntryTime = DateTime.UtcNow,
                    EntryPrice = currentPrice,
                    IsLong = false,
                    Quantity = _strategy.Order.Quantity,
                    EntryScore = score.TotalScore,
                    EntryVwapScore = score.VwapScore,
                    EntryEmaScore = score.EmaScore,
                    EntryRsiScore = score.RsiScore,
                    EntryMacdScore = score.MacdScore,
                    EntryAdxScore = score.AdxScore,
                    EntryVolumeScore = score.VolumeScore,
                    RsiAtEntry = _rsiCalculator?.CurrentValue ?? 50,
                    AdxAtEntry = _adxCalculator?.CurrentAdx ?? 20
                };
                
                // Execute short entry
                ExecuteAutonomousEntry(currentPrice, vwap, false, takeProfit, stopLoss, config);
            }
        }

        /// <summary>
        /// Handles autonomous exit decision when position is open.
        /// </summary>
        private void HandleAutonomousExit(double currentPrice, double vwap, MarketScore score, AutonomousTradingConfig config)
        {
            bool isLong = _isLong;  // Use tracked position direction
            bool isFlipMode = config.UseFlipMode;

            // FLIP MODE: Always flip direction on reversal - stay in market
            if (isFlipMode)
            {
                // In FlipMode, we flip when score crosses the opposite entry threshold
                // This keeps us always in market, just on the right side of the trend
                if (isLong && score.TotalScore <= config.ShortEntryThreshold)
                {
                    // Bearish signal while long - FLIP TO SHORT
                    if (config.AllowShort && !_shortSaleBlocked)
                    {
                        Log($"*** FLIP: LONG -> SHORT | Score {score.TotalScore} <= {config.ShortEntryThreshold}", ConsoleColor.Magenta);
                        Log($"  Indicators: {score.Condition}", ConsoleColor.DarkGray);
                        
                        // Exit long and enter short in one motion
                        ExecuteFlipPosition(currentPrice, vwap, false, config);
                    }
                }
                else if (!isLong && score.TotalScore >= config.LongEntryThreshold)
                {
                    // Bullish signal while short - FLIP TO LONG
                    Log($"*** FLIP: SHORT -> LONG | Score {score.TotalScore} >= {config.LongEntryThreshold}", ConsoleColor.Cyan);
                    Log($"  Indicators: {score.Condition}", ConsoleColor.DarkGray);
                    
                    // Exit short and enter long in one motion
                    ExecuteFlipPosition(currentPrice, vwap, true, config);
                }
                return; // FlipMode handles its own exit logic
            }

            // STANDARD MODE: Use exit thresholds, optionally flip
            if (isLong && score.TotalScore < config.LongExitThreshold)
            {
                Log($"*** AUTONOMOUS LONG EXIT: Score {score.TotalScore} < {config.LongExitThreshold} ({score.Condition})", ConsoleColor.Yellow);
                Log($"  Position was LONG, momentum lost. Consider direction flip: {config.AllowDirectionFlip}", ConsoleColor.DarkGray);
                
                // Exit the position
                ExecuteAutonomousExit(currentPrice, vwap, isLong);
                
                // Consider direction flip (only if short sale is not blocked)
                if (config.AllowDirectionFlip && config.AllowShort && !_shortSaleBlocked && score.TotalScore <= config.ShortEntryThreshold)
                {
                    Log($"  -> Flipping to SHORT (score {score.TotalScore} <= {config.ShortEntryThreshold})", ConsoleColor.Magenta);
                    var (tp, sl) = CalculateAutonomousTpSl(currentPrice, false, config);
                    ExecuteAutonomousEntry(currentPrice, vwap, false, tp, sl, config);
                }
            }
            else if (!isLong && score.TotalScore > config.ShortExitThreshold)
            {
                Log($"*** AUTONOMOUS SHORT EXIT: Score {score.TotalScore} > {config.ShortExitThreshold} ({score.Condition})", ConsoleColor.Yellow);
                Log($"  Position was SHORT, momentum lost. Consider direction flip: {config.AllowDirectionFlip}", ConsoleColor.DarkGray);
                
                // Exit the position
                ExecuteAutonomousExit(currentPrice, vwap, isLong);
                
                // Consider direction flip
                if (config.AllowDirectionFlip && score.TotalScore >= config.LongEntryThreshold)
                {
                    Log($"  -> Flipping to LONG (score {score.TotalScore} >= {config.LongEntryThreshold})", ConsoleColor.Cyan);
                    var (tp, sl) = CalculateAutonomousTpSl(currentPrice, true, config);
                    ExecuteAutonomousEntry(currentPrice, vwap, true, tp, sl, config);
                }
            }
        }

        /// <summary>
        /// Executes a position flip in FlipMode - closes current position and opens opposite.
        /// Uses a single close order that doubles as an entry (e.g., sell 200 to close 100 long and open 100 short).
        /// </summary>
        private void ExecuteFlipPosition(double currentPrice, double vwap, bool goLong, AutonomousTradingConfig config)
        {
            _lastTradeTime = DateTime.UtcNow;
            
            // Record the exit for learning
            CompletePendingTradeRecord(currentPrice);
            
            // Calculate P&L for logging
            double pnl = _isLong 
                ? _strategy.Order.Quantity * (currentPrice - _entryFillPrice)
                : _strategy.Order.Quantity * (_entryFillPrice - currentPrice);
            
            var pnlColor = pnl >= 0 ? ConsoleColor.Green : ConsoleColor.Red;
            Log($"  Exit P&L: ${pnl:F2}", pnlColor);
            SessionLogger?.LogFill(_strategy.Symbol, _isLong ? "SELL" : "BUY", _strategy.Order.Quantity, currentPrice, pnl);
            
            // Reset for new position
            ResetForNextAutonomousTrade();
            
            // Immediately enter opposite direction
            _isLong = goLong;
            _dynamicTakeProfit = 0; // Not used in FlipMode
            _dynamicStopLoss = 0;   // Not used in FlipMode
            
            // Create new entry for learning
            var score = CalculateMarketScoreForAutonomous(currentPrice, vwap, config.OptimizedWeights);
            _entryScore = score;
            _pendingTradeRecord = new TradeRecord
            {
                EntryTime = DateTime.UtcNow,
                EntryPrice = currentPrice,
                IsLong = goLong,
                Quantity = _strategy.Order.Quantity,
                EntryScore = score.TotalScore,
                EntryVwapScore = score.VwapScore,
                EntryEmaScore = score.EmaScore,
                EntryRsiScore = score.RsiScore,
                EntryMacdScore = score.MacdScore,
                EntryAdxScore = score.AdxScore,
                EntryVolumeScore = score.VolumeScore,
                RsiAtEntry = _rsiCalculator?.CurrentValue ?? 50,
                AdxAtEntry = _adxCalculator?.CurrentAdx ?? 20
            };
            
            Log($"  -> New {(goLong ? "LONG" : "SHORT")} entry @ ${currentPrice:F2}", goLong ? ConsoleColor.Cyan : ConsoleColor.Magenta);
            
            ExecuteOrder(vwap);
        }

        /// <summary>
        /// Calculates take profit and stop loss prices for autonomous trading.
        /// </summary>
        /// <param name="entryPrice">The entry price.</param>
        /// <param name="isLong">Whether this is a long position.</param>
        /// <param name="config">The autonomous trading config.</param>
        /// <param name="tpMultiplierOverride">Optional override for TP ATR multiplier (for OPTIMIZED mode).</param>
        /// <param name="slMultiplierOverride">Optional override for SL ATR multiplier (for OPTIMIZED mode).</param>
        private (double takeProfit, double stopLoss) CalculateAutonomousTpSl(
            double entryPrice, bool isLong, AutonomousTradingConfig config,
            double? tpMultiplierOverride = null, double? slMultiplierOverride = null)
        {
            double tpDistance, slDistance;

            // Use override multipliers for OPTIMIZED mode, otherwise use config defaults
            double tpMultiplier = tpMultiplierOverride ?? config.TakeProfitAtrMultiplier;
            double slMultiplier = slMultiplierOverride ?? config.StopLossAtrMultiplier;

            // Try to use ATR if available
            if (_atrCalculator?.IsReady == true)
            {
                double atr = _atrCalculator.CurrentAtr;
                tpDistance = atr * tpMultiplier;
                slDistance = atr * slMultiplier;
            }
            else
            {
                // Fallback to percentage-based
                tpDistance = entryPrice * config.TakeProfitPercent;
                slDistance = entryPrice * config.StopLossPercent;
            }

            if (isLong)
            {
                return (entryPrice + tpDistance, entryPrice - slDistance);
            }
            else
            {
                return (entryPrice - tpDistance, entryPrice + slDistance);
            }
        }

        /// <summary>
        /// Executes an autonomous entry order.
        /// </summary>
        private void ExecuteAutonomousEntry(double currentPrice, double vwap, bool isLong, double takeProfit, double stopLoss, AutonomousTradingConfig config)
        {
            // Check margin availability before placing order
            double estimatedOrderValue = currentPrice * _strategy.Order.Quantity;
            // For margin accounts, typically need ~25-50% of the position value depending on stock
            double estimatedMarginRequired = estimatedOrderValue * 0.50; // Conservative 50% margin estimate
            
            if (!_wrapper.HasSufficientMargin(estimatedMarginRequired))
            {
                double available = _wrapper.AvailableFunds;
                Log($"[FILTER] Skipping entry - Insufficient margin: ${available:N2} available, ~${estimatedMarginRequired:N2} required", ConsoleColor.Red);
                return;
            }

            _lastTradeTime = DateTime.UtcNow;
            
            // Track autonomous position direction (used by SubmitTakeProfit/StopLoss)
            _isLong = isLong;
            _dynamicTakeProfit = takeProfit;
            _dynamicStopLoss = stopLoss;
            
            // Set the dynamically calculated TP/SL tracking vars
            _takeProfitTarget = takeProfit;
            _originalStopLossPrice = stopLoss;
            
            // Log and execute the entry order using the existing ExecuteOrder mechanism
            // Since we're in autonomous mode, we skip condition evaluation
            _currentConditionIndex = _strategy.Conditions.Count; // Mark all conditions as met
            
            Log($"*** AUTONOMOUS ENTRY: {(isLong ? "LONG" : "SHORT")} @ ${currentPrice:F2}", ConsoleColor.Cyan);
            Log($"  TP: ${takeProfit:F2} | SL: ${stopLoss:F2}", ConsoleColor.DarkGray);
            
            ExecuteOrder(vwap);
        }

        /// <summary>
        /// Executes an autonomous exit order and records the trade for learning.
        /// After the fill, the system will reset and continue looking for the next trade.
        /// </summary>
        private void ExecuteAutonomousExit(double currentPrice, double vwap, bool wasLong)
        {
            _lastTradeTime = DateTime.UtcNow;

            // Complete the pending trade record for learning
            CompletePendingTradeRecord(currentPrice);
            
            // Cancel any pending TP/SL orders (only if not rejected)
            if (_takeProfitOrderId > 0 && !_takeProfitFilled && !_takeProfitCancelled && !_takeProfitOrderRejected)
            {
                Log($"Cancelling Take Profit order {_takeProfitOrderId}", ConsoleColor.DarkGray);
                _client.cancelOrder(_takeProfitOrderId, new OrderCancel());
                _takeProfitCancelled = true;
            }
            
            if (_stopLossOrderId > 0 && !_stopLossFilled && !_stopLossOrderRejected)
            {
                Log($"Cancelling Stop Loss order {_stopLossOrderId}", ConsoleColor.DarkGray);
                _client.cancelOrder(_stopLossOrderId, new OrderCancel());
            }

            // Submit autonomous exit order (tracked separately so we can cycle after fill)
            _exitOrderId = _wrapper.ConsumeNextOrderId();
            string action = wasLong ? "SELL" : "BUY"; // Close position

            var exitOrder = new Order
            {
                Action = action,
                OrderType = "MKT",
                TotalQuantity = _strategy.Order.Quantity,
                OutsideRth = true,
                Tif = "GTC"
            };

            if (!string.IsNullOrEmpty(AppSettings.AccountNumber))
                exitOrder.Account = AppSettings.AccountNumber;

            Log($">> AUTONOMOUS EXIT: {action} {_strategy.Order.Quantity} @ MKT (cycling to next trade)", ConsoleColor.Yellow);
            _client.placeOrder(_exitOrderId, _contract, exitOrder);
        }

        /// <summary>
        /// Completes the pending trade record and saves it to the ticker profile.
        /// </summary>
        private void CompletePendingTradeRecord(double exitPrice)
        {
            if (_pendingTradeRecord == null || _tickerProfile == null)
                return;

            try
            {
                // Calculate current score for exit
                var exitScore = CalculateMarketScoreForAutonomous(exitPrice, GetVwap());

                _pendingTradeRecord.ExitTime = DateTime.UtcNow;
                _pendingTradeRecord.ExitPrice = exitPrice;
                _pendingTradeRecord.ExitScore = exitScore.TotalScore;
                _pendingTradeRecord.RsiAtExit = _rsiCalculator?.CurrentValue ?? 50;
                _pendingTradeRecord.AdxAtExit = _adxCalculator?.CurrentAdx ?? 20;

                // Record the trade
                _profileManager.RecordTrade(_strategy.Symbol, _pendingTradeRecord);

                // Record LSH pattern for future analog matching
                if (_patternMatcher != null && _lastIndicatorSnapshot.HasValue)
                {
                    try
                    {
                        double entryPrice = _pendingTradeRecord.EntryPrice;
                        double nextReturn = (exitPrice - entryPrice) / entryPrice;
                        // For live trading, we don't have exact max gain/drawdown, estimate from returns
                        double maxGain = Math.Max(0, nextReturn);
                        double maxDrawdown = Math.Max(0, -nextReturn);
                        
                        _patternMatcher.RecordPattern(
                            _lastIndicatorSnapshot.Value,
                            _pendingTradeRecord.EntryTime,
                            entryPrice,
                            _pendingTradeRecord.EntryScore,
                            nextReturn,
                            maxGain,
                            maxDrawdown);
                        
                        // Periodically save patterns (every 10 trades)
                        if (_patternMatcher.PatternCount % 10 == 0)
                        {
                            _patternMatcher.Save();
                        }
                    }
                    catch (Exception lshEx)
                    {
                        Log($"[LSH] Pattern recording failed: {lshEx.Message}", ConsoleColor.DarkGray);
                    }
                }

                // Log the outcome
                string outcome = _pendingTradeRecord.IsWin ? "WIN" : "LOSS";
                var color = _pendingTradeRecord.IsWin ? ConsoleColor.Green : ConsoleColor.Red;
                Log($"[LEARN] Trade recorded: {outcome} ${_pendingTradeRecord.PnL:F2} ({_pendingTradeRecord.PnLPercent:F2}%)", color);
                Log($"  Duration: {_pendingTradeRecord.Duration.TotalMinutes:F0} min | Entry score: {_pendingTradeRecord.EntryScore} | Exit score: {_pendingTradeRecord.ExitScore}", ConsoleColor.DarkGray);
                Log($"  Profile updated: {_tickerProfile.GetSummary()}", ConsoleColor.DarkGray);

                // Record AI recommendation accuracy for learning
                if (_aiAdvisor != null && _lastAiAnalysis != null && _lastAiAnalysis.IsUsable)
                {
                    bool aiWasCorrect = (_lastAiAnalysis.Action == "LONG" && _pendingTradeRecord.IsLong && _pendingTradeRecord.IsWin) ||
                                        (_lastAiAnalysis.Action == "SHORT" && !_pendingTradeRecord.IsLong && _pendingTradeRecord.IsWin) ||
                                        (_lastAiAnalysis.Action == "WAIT" && !_pendingTradeRecord.IsWin);
                    _aiAdvisor.RecordOutcome(_lastAiAnalysis, aiWasCorrect);
                    
                    var (total, correct, accuracy) = _aiAdvisor.GetAccuracyStats();
                    if (total >= 5)
                    {
                        Log($"[AI] Accuracy: {accuracy:F0}% ({correct}/{total} correct)", ConsoleColor.DarkMagenta);
                    }
                }

                _pendingTradeRecord = null;
            }
            catch (Exception ex)
            {
                Log($"[WARN] Failed to record trade: {ex.Message}", ConsoleColor.Yellow);
            }
        }

        /// <summary>
        /// Monitors and adjusts take profit and stop loss orders based on market conditions.
        /// Uses multiple indicators to calculate a market score and adjusts orders accordingly.
        /// </summary>
        private void MonitorAdaptiveOrder(double currentPrice, double vwap)
        {
            var order = _strategy.Order;

            // Skip if no open position
            if (!_entryFilled || _isComplete)
                return;

            // Get adaptive config - either from explicit AdaptiveOrder or derived from AutonomousTrading
            AdaptiveOrderConfig? config = null;
            
            if (order.UseAdaptiveOrder)
            {
                config = order.AdaptiveOrder!;
            }
            else if (order.UseAutonomousTrading && order.AutonomousTrading?.UseFlipMode != true)
            {
                // Derive AdaptiveOrderConfig from AutonomousTradingConfig for non-FlipMode
                config = DeriveAdaptiveConfigFromAutonomous(order.AutonomousTrading!);
            }
            
            if (config == null)
                return;

            // Rate limiting - don't adjust too frequently
            var now = DateTime.UtcNow;
            var timeSinceLastAdjustment = (now - _lastAdaptiveAdjustmentTime).TotalSeconds;
            if (timeSinceLastAdjustment < config.MinSecondsBetweenAdjustments)
                return;

            // Calculate market score - use tracked position direction for autonomous trading
            bool isLong = order.UseAutonomousTrading ? _isLong : (order.Side == OrderSide.Buy);
            var score = CalculateMarketScore(currentPrice, vwap, isLong);

            // Check for emergency exit (AdaptiveOrder threshold-based)
            if (score.ShouldEmergencyExit)
            {
                Log($"*** ADAPTIVE EMERGENCY EXIT! Score: {score.TotalScore} ({score.Condition})", ConsoleColor.Red);
                ExecuteEmergencyExit();
                return;
            }

            // Check for learned exit signal (AI weights-based)
            if (score.HasLearnedWeights && score.LearnedShouldExit)
            {
                Log($"*** LEARNED WEIGHTS EXIT! Score: {score.TotalScore}, AI signals exit", ConsoleColor.Yellow);
                ExecuteEmergencyExit();
                return;
            }

            // Only adjust if score changed significantly
            int scoreDelta = Math.Abs(score.TotalScore - _lastAdaptiveScore);
            if (scoreDelta < config.MinScoreChangeForAdjustment)
                return;

            // Calculate new prices based on score
            bool adjustmentMade = false;

            // Adjust take profit
            if (_takeProfitOrderId > 0 && !_takeProfitFilled && !_takeProfitCancelled && _originalTakeProfitPrice > 0)
            {
                double newTakeProfitPrice;
                
                if (isLong)
                {
                    // Long: TP is above entry, extend means higher
                    double profitRange = _originalTakeProfitPrice - _entryFillPrice;
                    double adjustment = profitRange * (score.TakeProfitMultiplier - 1.0);
                    newTakeProfitPrice = Math.Round(_originalTakeProfitPrice + adjustment, 2);
                    newTakeProfitPrice = Math.Max(newTakeProfitPrice, _entryFillPrice + 0.01);
                }
                else
                {
                    // Short: TP is below entry, extend means lower
                    double profitRange = _entryFillPrice - _originalTakeProfitPrice;
                    double adjustment = profitRange * (score.TakeProfitMultiplier - 1.0);
                    newTakeProfitPrice = Math.Round(_originalTakeProfitPrice - adjustment, 2);
                    newTakeProfitPrice = Math.Min(newTakeProfitPrice, _entryFillPrice - 0.01);
                }

                if (Math.Abs(newTakeProfitPrice - _currentAdaptiveTakeProfitPrice) > 0.01)
                {
                    Log($"ADAPTIVE: Adjusting TP ${_currentAdaptiveTakeProfitPrice:F2} -> ${newTakeProfitPrice:F2} (Score: {score.TotalScore}, ×{score.TakeProfitMultiplier:F2})", ConsoleColor.Cyan);
                    ModifyTakeProfitOrder(newTakeProfitPrice);
                    _currentAdaptiveTakeProfitPrice = newTakeProfitPrice;
                    adjustmentMade = true;
                }
            }

            // Adjust stop loss (if using fixed SL, not trailing)
            if (_stopLossOrderId > 0 && !_stopLossFilled && _originalStopLossPrice > 0 && !order.EnableTrailingStopLoss)
            {
                double newStopLossPrice;
                
                if (isLong)
                {
                    // Long: SL is below entry, tighten means higher (closer to entry)
                    double lossRange = _entryFillPrice - _originalStopLossPrice;
                    double adjustment = lossRange * (score.StopLossMultiplier - 1.0);
                    newStopLossPrice = Math.Round(_originalStopLossPrice - adjustment, 2);
                    newStopLossPrice = Math.Min(newStopLossPrice, _entryFillPrice - 0.01);
                }
                else
                {
                    // Short: SL is above entry, tighten means lower (closer to entry)
                    double lossRange = _originalStopLossPrice - _entryFillPrice;
                    double adjustment = lossRange * (score.StopLossMultiplier - 1.0);
                    newStopLossPrice = Math.Round(_originalStopLossPrice + adjustment, 2);
                    newStopLossPrice = Math.Max(newStopLossPrice, _entryFillPrice + 0.01);
                }

                if (Math.Abs(newStopLossPrice - _currentAdaptiveStopLossPrice) > 0.01)
                {
                    Log($"ADAPTIVE: Adjusting SL ${_currentAdaptiveStopLossPrice:F2} -> ${newStopLossPrice:F2} (Score: {score.TotalScore}, ×{score.StopLossMultiplier:F2})", ConsoleColor.Yellow);
                    ModifyStopLossOrder(newStopLossPrice);
                    _currentAdaptiveStopLossPrice = newStopLossPrice;
                    adjustmentMade = true;
                }
            }

            if (adjustmentMade)
            {
                _lastAdaptiveAdjustmentTime = now;
                _lastAdaptiveScore = score.TotalScore;
                Log($"  Market Analysis: {score}", ConsoleColor.DarkGray);
            }
        }

        /// <summary>
        /// Monitors ADX for rollover (peak detection) to trigger early exit on fading momentum.
        /// Used with ADX-based TakeProfit when ExitOnAdxRollover is enabled.
        /// </summary>
        private void MonitorAdxRollover(double currentPrice)
        {
            var order = _strategy.Order;

            // Only monitor if ADX-based TP is configured with rollover exit enabled
            if (order.AdxTakeProfit == null || !order.AdxTakeProfit.ExitOnAdxRollover)
                return;

            // Need ADX calculator and position must be open
            if (_adxCalculator == null || !_adxCalculator.IsReady || !_entryFilled || _isComplete)
                return;

            // Skip if already triggered rollover exit
            if (_adxRolledOver)
                return;

            double currentAdx = _adxCalculator.CurrentAdx;
            double rolloverThreshold = order.AdxTakeProfit.AdxRolloverThreshold;

            // Track ADX peak
            if (currentAdx > _adxPeakValue)
            {
                _adxPeakValue = currentAdx;
            }

            // Check for rollover: ADX dropped from peak by threshold amount
            double dropFromPeak = _adxPeakValue - currentAdx;
            if (dropFromPeak >= rolloverThreshold && _adxPeakValue >= order.AdxTakeProfit.DevelopingTrendThreshold)
            {
                _adxRolledOver = true;

                // Only exit if profitable
                double currentPnL = currentPrice - _entryFillPrice;
                if (currentPnL > 0)
                {
                    Log($"*** ADX ROLLOVER EXIT! ADX dropped {dropFromPeak:F1} from peak {_adxPeakValue:F1} to {currentAdx:F1} ***", ConsoleColor.Magenta);
                    Log($"  Momentum fading - exiting with profit ${currentPnL:F2}/share", ConsoleColor.Magenta);

                    // Update take profit target to current price for immediate exit
                    if (_takeProfitOrderId > 0 && !_takeProfitFilled && !_takeProfitCancelled && !_takeProfitOrderRejected)
                    {
                        // Adjust TP to slightly below current price for quick fill
                        double exitPrice = Math.Round(currentPrice - 0.01, 2);
                        Log($"  Adjusting TP ${_takeProfitTarget:F2} -> ${exitPrice:F2} for quick exit", ConsoleColor.Yellow);
                        ModifyTakeProfitOrder(exitPrice);
                        _takeProfitTarget = exitPrice;
                    }
                }
                else
                {
                    Log($"ADX ROLLOVER detected (ADX {_adxPeakValue:F1} -> {currentAdx:F1}), but position not profitable. Holding.", ConsoleColor.DarkYellow);
                }
            }
        }

        /// <summary>
        /// Calculates a market score (-100 to +100) based on multiple indicators.
        /// Uses MarketScoreCalculator (SINGLE SOURCE OF TRUTH) for consistent scoring.
        /// Integrates LSTM predictions when available for enhanced accuracy.
        /// Positive = bullish, Negative = bearish.
        /// </summary>
        private MarketScore CalculateMarketScore(double price, double vwap, bool isLong)
        {
            var order = _strategy.Order;
            var config = order.AdaptiveOrder!;

            // Get indicator weights - learned or default
            var weights = _learnedWeights != null 
                ? _learnedWeights.ToIndicatorWeights() 
                : IdiotProof.Helpers.IndicatorWeights.Default;
            
            // Use MarketScoreCalculator for component scores (SINGLE SOURCE OF TRUTH)
            var snapshot = BuildIndicatorSnapshot(price, vwap);
            var result = MarketScoreCalculator.Calculate(snapshot, weights);
            
            int vwapScore = result.VwapScore;
            int emaScore = result.EmaScore;
            int rsiScore = result.RsiScore;
            int macdScore = result.MacdScore;
            int adxScore = result.AdxScore;
            int volumeScore = result.VolumeScore;
            int bollingerScore = result.BollingerScore;

            int finalScore = result.TotalScore;
            
            // LSTM prediction integration
            double lstmDirection = 0;
            double lstmConfidence = 0;
            double lstmPredictedChange = 0;
            double lstmVolatility = 0;
            int lstmScoreAdjustment = 0;
            bool hasLstmPrediction = false;
            
            if (_lastLstmPrediction?.IsUsable == true)
            {
                var lstm = _lastLstmPrediction.Value;
                lstmDirection = lstm.Direction;
                lstmConfidence = lstm.Confidence;
                lstmPredictedChange = lstm.PredictedChangePercent;
                lstmVolatility = lstm.PredictedVolatility;
                lstmScoreAdjustment = lstm.ScoreAdjustment;
                hasLstmPrediction = true;
                
                // Apply LSTM adjustment to final score (max ±25 points)
                finalScore = (int)Math.Clamp(finalScore + lstmScoreAdjustment, -100, 100);
            }

            // For short positions, invert the score
            if (!isLong)
                finalScore = -finalScore;

            // Calculate adjustment multipliers based on score, mode, and LSTM volatility
            double tpMultiplier = CalculateTakeProfitMultiplier(finalScore, config, lstmVolatility, lstmConfidence);
            double slMultiplier = CalculateStopLossMultiplier(finalScore, config, lstmVolatility, lstmConfidence);

            bool shouldExit = isLong
                ? finalScore <= config.EmergencyExitThreshold
                : finalScore >= -config.EmergencyExitThreshold;

            return new MarketScore
            {
                TotalScore = finalScore,
                VwapScore = vwapScore,
                EmaScore = emaScore,
                RsiScore = rsiScore,
                MacdScore = macdScore,
                AdxScore = adxScore,
                VolumeScore = volumeScore,
                BollingerScore = bollingerScore,
                TakeProfitMultiplier = tpMultiplier,
                StopLossMultiplier = slMultiplier,
                ShouldEmergencyExit = shouldExit,
                // LSTM prediction signals
                LstmDirection = lstmDirection,
                LstmConfidence = lstmConfidence,
                LstmPredictedChangePercent = lstmPredictedChange,
                LstmPredictedVolatility = lstmVolatility,
                LstmScoreAdjustment = lstmScoreAdjustment,
                HasLstmPrediction = hasLstmPrediction
            };
        }

        /// <summary>
        /// Derives an AdaptiveOrderConfig from AutonomousTradingConfig for automatic TP/SL adjustment.
        /// This enables dynamic TP/SL optimization without requiring explicit AdaptiveOrder() in the script.
        /// </summary>
        private static AdaptiveOrderConfig DeriveAdaptiveConfigFromAutonomous(AutonomousTradingConfig autonomousConfig)
        {
            // Map autonomous mode to adaptive settings
            return new AdaptiveOrderConfig
            {
                Mode = AdaptiveMode.Balanced, // Base mode - actual settings override
                MinSecondsBetweenAdjustments = Math.Max(10, autonomousConfig.MinSecondsBetweenTrades),
                MinScoreChangeForAdjustment = 10, // More responsive than default 15
                EmergencyExitThreshold = -70,
                // Aggressive TP extension to maximize profits
                MaxTakeProfitExtension = 0.75,
                MaxTakeProfitReduction = 0.30,
                // Conservative SL adjustments
                MaxStopLossTighten = 0.50,
                MaxStopLossWiden = 0.15
            };
        }

        /// <summary>
        /// Calculates the take profit multiplier based on market score and LSTM predictions.
        /// When LSTM volatility is high but direction agrees with score, extend TP more.
        /// When LSTM confidence is high, weight its prediction more heavily.
        /// </summary>
        private static double CalculateTakeProfitMultiplier(int score, AdaptiveOrderConfig config, 
            double lstmVolatility = 0, double lstmConfidence = 0)
        {
            // Score ranges: -100 to +100
            // Strong bullish (70+): Extend TP
            // Moderate bullish (30-70): Keep original
            // Neutral (-30 to 30): Slightly reduce
            // Moderate bearish (-70 to -30): Reduce significantly
            // Strong bearish (<-70): Maximum reduction
            
            double baseMultiplier;

            if (score >= 70)
            {
                // Extend: 1.0 to 1.0 + MaxExtension
                double extensionFactor = (score - 70) / 30.0; // 0 at 70, 1 at 100
                baseMultiplier = 1.0 + (config.MaxTakeProfitExtension * extensionFactor);
            }
            else if (score >= 30)
            {
                // Keep original
                baseMultiplier = 1.0;
            }
            else if (score >= -30)
            {
                // Slight reduction: 1.0 to 0.85
                double reductionFactor = (30 - score) / 60.0; // 0 at 30, 1 at -30
                baseMultiplier = 1.0 - (0.15 * reductionFactor);
            }
            else if (score >= -70)
            {
                // Moderate reduction: 0.85 to 1.0 - MaxReduction/2
                double reductionFactor = (-30 - score) / 40.0; // 0 at -30, 1 at -70
                baseMultiplier = 0.85 - ((config.MaxTakeProfitReduction / 2 - 0.15) * reductionFactor);
            }
            else
            {
                // Maximum reduction
                baseMultiplier = 1.0 - config.MaxTakeProfitReduction;
            }
            
            // LSTM enhancement: Adjust multiplier based on predicted volatility and confidence
            if (lstmConfidence > 0.5 && lstmVolatility > 0)
            {
                // High volatility + high confidence = can extend/reduce more aggressively
                // Normalize volatility effect (typical range 0.5% - 3%)
                double volatilityFactor = Math.Clamp(lstmVolatility / 2.0, 0.5, 2.0);
                double confidenceWeight = (lstmConfidence - 0.5) * 2; // 0 at 0.5, 1 at 1.0
                
                if (baseMultiplier > 1.0)
                {
                    // Extending: High volatility = extend even more (expecting big move)
                    double extension = (baseMultiplier - 1.0) * (1 + (volatilityFactor - 1) * confidenceWeight * 0.3);
                    baseMultiplier = 1.0 + extension;
                }
                else if (baseMultiplier < 1.0)
                {
                    // Reducing: High volatility in bearish = reduce less (might spike back)
                    double reduction = (1.0 - baseMultiplier) * (1 - (volatilityFactor - 1) * confidenceWeight * 0.2);
                    baseMultiplier = 1.0 - reduction;
                }
            }
            
            return Math.Clamp(baseMultiplier, 0.5, 2.0);
        }

        /// <summary>
        /// Calculates the stop loss multiplier based on market score and LSTM predictions.
        /// LSTM volatility helps size stops appropriately for expected price swings.
        /// </summary>
        private static double CalculateStopLossMultiplier(int score, AdaptiveOrderConfig config,
            double lstmVolatility = 0, double lstmConfidence = 0)
        {
            // Strong bullish: Tighten SL to protect gains
            // Neutral: Widen slightly to avoid noise
            // Bearish: Keep tight to limit losses
            
            double baseMultiplier;

            if (score >= 70)
            {
                // Tighten: Multiplier > 1 means stop gets closer
                double tightenFactor = (score - 70) / 30.0;
                baseMultiplier = 1.0 + (config.MaxStopLossTighten * tightenFactor);
            }
            else if (score >= 0)
            {
                // Slight tighten to neutral
                baseMultiplier = 1.0;
            }
            else if (score >= -50)
            {
                // Widen slightly: Multiplier < 1 means stop gets further
                double widenFactor = -score / 50.0;
                baseMultiplier = 1.0 - (config.MaxStopLossWiden * widenFactor * 0.5);
            }
            else
            {
                // Keep relatively tight in bearish conditions
                baseMultiplier = 1.0 - (config.MaxStopLossWiden * 0.5);
            }
            
            // LSTM enhancement: Adjust stop based on predicted volatility
            if (lstmConfidence > 0.5 && lstmVolatility > 0)
            {
                // High volatility = widen stops to avoid noise stops
                // Low volatility = can tighten stops
                double volatilityFactor = Math.Clamp(lstmVolatility / 2.0, 0.5, 2.0);
                double confidenceWeight = (lstmConfidence - 0.5) * 2;
                
                // If volatility is high, widen the stop slightly
                if (volatilityFactor > 1.0)
                {
                    double widenAdjustment = (volatilityFactor - 1.0) * confidenceWeight * config.MaxStopLossWiden * 0.5;
                    baseMultiplier -= widenAdjustment; // Lower multiplier = wider stop
                }
                // If volatility is low, can tighten more
                else if (volatilityFactor < 0.8)
                {
                    double tightenAdjustment = (1.0 - volatilityFactor) * confidenceWeight * config.MaxStopLossTighten * 0.3;
                    baseMultiplier += tightenAdjustment; // Higher multiplier = tighter stop
                }
            }
            
            return Math.Clamp(baseMultiplier, 0.5, 2.0);
        }

        /// <summary>
        /// Modifies an existing take profit order with a new price.
        /// </summary>
        private void ModifyTakeProfitOrder(double newPrice)
        {
            if (_takeProfitOrderId <= 0) return;

            var tpOrder = new Order
            {
                Action = _strategy.Order.Side == OrderSide.Buy ? "SELL" : "BUY",
                OrderType = "LMT",
                LmtPrice = newPrice,
                TotalQuantity = _strategy.Order.Quantity,
                OutsideRth = _strategy.Order.TakeProfitOutsideRth,
                Tif = "GTC"
            };

            if (!string.IsNullOrEmpty(AppSettings.AccountNumber))
                tpOrder.Account = AppSettings.AccountNumber;

            _wrapper.ModifyOrder(_takeProfitOrderId, _contract, tpOrder);
        }

        /// <summary>
        /// Modifies an existing stop loss order with a new price.
        /// </summary>
        private void ModifyStopLossOrder(double newPrice)
        {
            if (_stopLossOrderId <= 0) return;

            var slOrder = new Order
            {
                Action = _strategy.Order.Side == OrderSide.Buy ? "SELL" : "BUY",
                OrderType = "STP",
                AuxPrice = newPrice,
                TotalQuantity = _strategy.Order.Quantity,
                OutsideRth = true,
                Tif = "GTC"
            };

            if (!string.IsNullOrEmpty(AppSettings.AccountNumber))
                slOrder.Account = AppSettings.AccountNumber;

            _wrapper.ModifyOrder(_stopLossOrderId, _contract, slOrder);
        }

        /// <summary>
        /// Executes an emergency exit when market conditions are severely against the position.
        /// </summary>
        private void ExecuteEmergencyExit()
        {
            if (_isComplete) return;

            // Cancel existing orders (only if not rejected)
            if (_takeProfitOrderId > 0 && !_takeProfitFilled && !_takeProfitCancelled && !_takeProfitOrderRejected)
            {
                Log($"Cancelling take profit order #{_takeProfitOrderId} for emergency exit...", ConsoleColor.Yellow);
                _client.cancelOrder(_takeProfitOrderId, new OrderCancel());
                _takeProfitCancelled = true;
            }

            if (_stopLossOrderId > 0 && !_stopLossFilled && !_stopLossOrderRejected)
            {
                Log($"Cancelling stop loss order #{_stopLossOrderId} for emergency exit...", ConsoleColor.Yellow);
                _client.cancelOrder(_stopLossOrderId, new OrderCancel());
            }

            // Submit market exit order
            int exitOrderId = _wrapper.ConsumeNextOrderId();
            string action = _strategy.Order.Side == OrderSide.Buy ? "SELL" : "BUY";

            var exitOrder = new Order
            {
                Action = action,
                OrderType = "MKT",
                TotalQuantity = _strategy.Order.Quantity,
                OutsideRth = true,
                Tif = "GTC"
            };

            if (!string.IsNullOrEmpty(AppSettings.AccountNumber))
                exitOrder.Account = AppSettings.AccountNumber;

            Log($">> EMERGENCY EXIT: {action} {_strategy.Order.Quantity} @ MKT", ConsoleColor.Red);
            _client.placeOrder(exitOrderId, _contract, exitOrder);

            _isComplete = true;
            _result = StrategyResult.EmergencyExit;
        }

        /// <summary>
        /// Resets the strategy state for a new trading day.
        /// Called automatically at midnight to allow strategies to run again.
        /// </summary>
        private void ResetForNewDay(DateOnly newDate)
        {
            // Only reset if we haven't filled an entry order (don't reset mid-trade)
            if (_entryFilled)
            {
                Log($"New day detected but position is open - not resetting", ConsoleColor.DarkYellow);
                _lastCheckedDate = newDate;
                return;
            }

            Log($"*** MIDNIGHT RESET - New trading day detected, resetting strategy ***", ConsoleColor.Cyan);

            // Reset condition tracking
            _currentConditionIndex = 0;
            _isComplete = false;

            // Reset VWAP accumulators for new session
            _pvSum = 0;
            _vSum = 0;

            // Reset session tracking
            _sessionHigh = 0;
            _sessionLow = double.MaxValue;

            // Reset logging flags
            _waitingForWindowLogged = false;
            _windowEndedLogged = false;

            // Reset result
            _result = StrategyResult.Running;

            // Update the date
            _lastCheckedDate = newDate;

            // Log next window time
            if (_strategy.StartTime.HasValue)
            {
                var info = TimezoneHelper.GetTimezoneDisplayInfo(AppSettings.Timezone);
                var startLocal = TimezoneHelper.ToLocal(_strategy.StartTime.Value, AppSettings.Timezone);
                Log($"Will start monitoring at {_strategy.StartTime.Value:h:mm tt} ET ({startLocal:h:mm tt} {info.Abbreviation})", ConsoleColor.DarkGray);
            }
        }

        private double GetVwap()
        {
            return _vSum > 0 ? _pvSum / _vSum : 0;
        }

        /// <summary>
        /// Gets the appropriate price based on the PriceType setting.
        /// </summary>
        /// <param name="priceType">The price type to use.</param>
        /// <param name="vwap">The current VWAP value.</param>
        /// <returns>The price to use for order execution.</returns>
        private double GetPriceForType(Price priceType, double vwap)
        {
            return priceType switch
            {
                Price.Current => _lastPrice,
                Price.VWAP => vwap > 0 ? vwap : _lastPrice,
                Price.Bid => _lastBid > 0 ? _lastBid : _lastPrice,
                Price.Ask => _lastAsk > 0 ? _lastAsk : _lastPrice,
                _ => _lastPrice
            };
        }

        /// <summary>
        /// Resets the strategy state for repeating after a trade completes.
        /// Called when RepeatEnabled is true after take profit, stop loss, or trailing stop fills.
        /// </summary>
        private void ResetForRepeat()
        {
            Log($"*** REPEAT ENABLED - Resetting strategy to wait for conditions again ***", ConsoleColor.Magenta);

            // IMPORTANT: Dispose pending timers to prevent old callbacks from firing during new trade
            // Bug fix: Old timers could fire and prematurely close Trade 2's position
            _cancelTimer?.Dispose();
            _cancelTimer = null;

            _overnightCancelTimer?.Dispose();
            _overnightCancelTimer = null;

            _closePositionTimer?.Dispose();
            _closePositionTimer = null;

            // Reset condition tracking
            _currentConditionIndex = 0;
            _isComplete = false;

            // Reset order tracking
            _entryOrderId = -1;
            _entryFilled = false;
            _entryFillPrice = 0;

            // Reset take profit tracking
            _takeProfitOrderId = -1;
            _takeProfitFilled = false;
            _takeProfitCancelled = false;
            _takeProfitTarget = 0;

            // Reset stop loss tracking
            _stopLossOrderId = -1;
            _stopLossFilled = false;

            // Reset exit tracking
            _exitedWithProfit = false;
            _exitFillPrice = 0;

            // Reset trailing stop loss tracking
            _trailingStopLossOrderId = -1;
            _trailingStopLossTriggered = false;
            _trailingStopLossPrice = 0;
            _highWaterMark = 0;

            // Reset adaptive order tracking
            _lastAdaptiveAdjustmentTime = DateTime.MinValue;
            _lastAdaptiveScore = 0;
            _originalTakeProfitPrice = 0;
            _originalStopLossPrice = 0;
            _currentAdaptiveTakeProfitPrice = 0;
            _currentAdaptiveStopLossPrice = 0;

            // Reset close position tracking
            _closePositionTriggered = false;
            _closePositionOrderId = -1;

            // Reset result
            _result = StrategyResult.Running;

            // DON'T reset VWAP - we want continuous tracking within the session
            // DON'T reset session high/low - these continue throughout the day
            // DON'T reset logging flags - we're still in the same time window

            Log($"Waiting for conditions to be met again...", ConsoleColor.DarkGray);
        }

        /// <summary>
        /// Validates that the current price is not already above the take profit target.
        /// This prevents buying when we've "missed the boat" - the price has already run up
        /// past where we would have taken profits.
        /// </summary>
        /// <param name="currentPrice">The current market price.</param>
        /// <returns>True if safe to proceed with buy, false if price is too high.</returns>
        private bool ValidatePriceNotAboveTakeProfit(double currentPrice)
        {
            var order = _strategy.Order;

            // Only validate for BUY orders with take profit enabled
            if (order.Side != OrderSide.Buy || !order.EnableTakeProfit)
                return true;

            double? takeProfitThreshold = null;

            // Check ADX-based take profit (use conservative/weak target as threshold)
            if (order.AdxTakeProfit != null)
            {
                takeProfitThreshold = order.AdxTakeProfit.ConservativeTarget;
            }
            // Check fixed take profit price
            else if (order.TakeProfitPrice.HasValue)
            {
                takeProfitThreshold = order.TakeProfitPrice.Value;
            }

            // If we have a threshold and current price is at or above it, reject the trade
            if (takeProfitThreshold.HasValue && currentPrice >= takeProfitThreshold.Value)
            {
                Log($"*** MISSED THE BOAT! Price ${currentPrice:F2} >= Take Profit ${takeProfitThreshold.Value:F2}", ConsoleColor.Red);
                Log($"  Skipping entry - no profit potential at current price", ConsoleColor.Red);
                Log($"  Strategy will reset tomorrow and try again", ConsoleColor.Yellow);

                // Mark strategy as complete for today (will reset tomorrow)
                _isComplete = true;
                _result = StrategyResult.MissedTheBoat;

                PrintFinalResult();
                return false;
            }

            // Additional check: warn if price is close to take profit (within 5%)
            if (takeProfitThreshold.HasValue)
            {
                double potentialProfit = takeProfitThreshold.Value - currentPrice;
                double percentToTarget = (potentialProfit / currentPrice) * 100;

                if (percentToTarget < 5.0 && percentToTarget > 0)
                {
                    Log($"WARNING: Only {percentToTarget:F1}% potential profit to take profit target", ConsoleColor.Yellow);
                    Log($"  Current: ${currentPrice:F2} | Target: ${takeProfitThreshold.Value:F2} | Upside: ${potentialProfit:F2}", ConsoleColor.Yellow);
                }
            }

            return true;
        }

        private void EvaluateConditions(double price, double vwap)
        {
            if (_currentConditionIndex >= _strategy.Conditions.Count)
                return;

            // Check if we're within the time window (times are in Eastern)
            var currentTimeET = TimezoneHelper.GetCurrentTime(MarketTimeZone.EST);
            var todayDate = DateOnly.FromDateTime(DateTime.Today);

            // Check for midnight reset - reset strategy state for a new trading day
            if (todayDate > _lastCheckedDate)
            {
                ResetForNewDay(todayDate);
            }

            // If StartTime is set and we haven't reached it yet, don't evaluate
            if (_strategy.StartTime.HasValue && currentTimeET < _strategy.StartTime.Value)
            {
                if (!_waitingForWindowLogged)
                {
                    _waitingForWindowLogged = true;
                    var info = TimezoneHelper.GetTimezoneDisplayInfo(AppSettings.Timezone);
                    var startLocal = TimezoneHelper.ToLocal(_strategy.StartTime.Value, AppSettings.Timezone);
                    Log($"Not monitoring yet - will start at {_strategy.StartTime.Value:h:mm tt} ET ({startLocal:h:mm tt} {info.Abbreviation})", ConsoleColor.DarkYellow);
                }
                return;
            }

            // If EndTime is set and we've passed it, wait for tomorrow (don't mark as complete)
            if (_strategy.EndTime.HasValue && currentTimeET > _strategy.EndTime.Value)
            {
                if (!_windowEndedLogged)
                {
                    _windowEndedLogged = true;
                    var info = TimezoneHelper.GetTimezoneDisplayInfo(AppSettings.Timezone);
                    var startLocal = _strategy.StartTime.HasValue
                        ? TimezoneHelper.ToLocal(_strategy.StartTime.Value, AppSettings.Timezone)
                        : new TimeOnly(4, 0);
                    var startET = _strategy.StartTime ?? new TimeOnly(4, 0);
                    Log($"Strategy window ended at {_strategy.EndTime.Value:h:mm tt} ET - will resume tomorrow at {startET:h:mm tt} ET ({startLocal:h:mm tt} {info.Abbreviation})", ConsoleColor.Yellow);
                }
                return;
            }

            var condition = _strategy.Conditions[_currentConditionIndex];

            if (condition.Evaluate(price, vwap))
            {
                _currentConditionIndex++;

                // Build additional context info for EMA conditions
                string emaInfo = GetEmaContextInfo(condition);

                // Check if all conditions are met
                if (_currentConditionIndex >= _strategy.Conditions.Count)
                {
                    Log($"[OK] STEP {_currentConditionIndex}/{_strategy.Conditions.Count}: {condition.Name} - TRIGGERED @ ${price:F2}{emaInfo}", ConsoleColor.Green);

                    // Safety check: Don't buy if price is already above take profit target ("missed the boat")
                    if (!ValidatePriceNotAboveTakeProfit(price))
                    {
                        return;
                    }

                    Log($"*** ALL CONDITIONS MET - Executing order... (VWAP=${vwap:F2})", ConsoleColor.Cyan);
                    ExecuteOrder(vwap);
                }
                else
                {
                    var nextCondition = _strategy.Conditions[_currentConditionIndex];
                    Log($"[OK] STEP {_currentConditionIndex}/{_strategy.Conditions.Count}: {condition.Name} - TRIGGERED @ ${price:F2}{emaInfo}", ConsoleColor.Green);
                    Log($"  -> Next: {nextCondition.Name} (VWAP=${vwap:F2})", ConsoleColor.DarkGray);
                }
            }
        }

        /// <summary>
        /// Gets additional context info for conditions (not used with AutonomousTrading).
        /// </summary>
        private string GetEmaContextInfo(IStrategyCondition condition)
        {
            // Condition context info not used with AutonomousTrading
            return "";
        }

        private void ExecuteOrder(double vwap)
        {
            var order = _strategy.Order;
            _entryOrderId = _wrapper.ConsumeNextOrderId();

            // Determine if we're in regular trading hours
            var currentTimeET = TimezoneHelper.GetCurrentTime(MarketTimeZone.EST);
            bool isRTH = currentTimeET >= new TimeOnly(9, 30) && currentTimeET < new TimeOnly(16, 0);

            // IBKR requires LIMIT orders outside RTH (premarket/after-hours)
            // Force LIMIT if user specified MARKET but we're outside RTH
            bool forceLimitOrder = !isRTH && order.Type == OrderType.Market;
            string effectiveOrderType = forceLimitOrder ? "LMT" : order.GetIbOrderType();

            // For autonomous trading, use tracked direction; otherwise use order's side
            string entryAction = _strategy.Order.UseAutonomousTrading ? GetOpenAction() : order.GetIbAction();

            var ibOrder = new Order
            {
                Action = entryAction,
                OrderType = effectiveOrderType,
                TotalQuantity = order.Quantity,
                OutsideRth = GetEffectiveOutsideRth(order),
                Tif = order.GetIbTif(),
                AllOrNone = order.AllOrNone
            };

            // Set account if specified
            if (!string.IsNullOrEmpty(AppSettings.AccountNumber))
            {
                ibOrder.Account = AppSettings.AccountNumber;
            }

            // Set limit price if LIMIT order (either explicit or forced due to extended hours)
            if (order.Type == OrderType.Limit || forceLimitOrder)
            {
                if (order.LimitPrice.HasValue)
                {
                    ibOrder.LmtPrice = Math.Round(order.LimitPrice.Value, 2);
                }
                else
                {
                    // Use PriceType to determine base price for limit orders
                    double basePrice = GetPriceForType(order.PriceType, vwap);
                    bool isLong = IsPositionLong();
                    double offset = isLong ? order.LimitOffset : -order.LimitOffset;
                    ibOrder.LmtPrice = Math.Round(basePrice + offset, 2);
                    Log($"Using PriceType.{order.PriceType}: base=${basePrice:F2}, offset=${offset:F2}", ConsoleColor.DarkGray);
                }
            }

            // Build logging strings
            string priceStr = (order.Type == OrderType.Limit || forceLimitOrder) ? $"@ ${ibOrder.LmtPrice:F2}" : "@ MKT";
            string tifDesc = GetTifDescription(order.TimeInForce);
            string aonStr = order.AllOrNone ? " | AON=true" : "";
            string sessionStr = isRTH ? "RTH" : "Extended Hours";

            if (forceLimitOrder)
            {
                Log($"NOTE: Forcing LIMIT order (MARKET orders not allowed in {sessionStr})", ConsoleColor.DarkYellow);
            }

            Log($">> SUBMITTING {order.Side} {order.Quantity} shares {priceStr} ({sessionStr})", ConsoleColor.Yellow);
            Log($"  OrderId={_entryOrderId} | TIF={tifDesc} | OutsideRTH={ibOrder.OutsideRth}{aonStr}", ConsoleColor.DarkGray);

            // Log to session logger
            SessionLogger?.LogOrder(_strategy.Symbol, order.Side.ToString(), order.Quantity, ibOrder.LmtPrice, _entryOrderId.ToString());

            // Special handling for AtTheOpening orders
            if (order.TimeInForce == TimeInForce.AtTheOpening)
            {
                Log($"  NOTE: Order will execute at market open auction only", ConsoleColor.DarkGray);
            }

            // Special handling for Overnight orders - schedule cancellation at market open
            if (order.TimeInForce == TimeInForce.Overnight)
            {
                ScheduleOvernightCancellation();
            }

            // Log AllOrNone warning if enabled
            if (order.AllOrNone)
            {
                Log($"  NOTE: AllOrNone enabled - order must fill completely or not at all", ConsoleColor.DarkGray);
            }

            _client.placeOrder(_entryOrderId, _contract, ibOrder);
        }

        /// <summary>
        /// Gets the effective OutsideRth setting based on TIF type.
        /// </summary>
        /// <remarks>
        /// <para><b>Behavior by TIF:</b></para>
        /// <list type="bullet">
        ///   <item><see cref="TimeInForce.Overnight"/>: Forces OutsideRth = true.</item>
        ///   <item><see cref="TimeInForce.OvernightPlusDay"/>: Forces OutsideRth = true.</item>
        ///   <item><see cref="TimeInForce.AtTheOpening"/>: Uses order setting (typically false).</item>
        ///   <item>All others: Uses the order's OutsideRth setting.</item>
        /// </list>
        /// </remarks>
        private bool GetEffectiveOutsideRth(OrderAction order)
        {
            return order.TimeInForce switch
            {
                TimeInForce.Overnight => true,        // Must be true for extended hours
                TimeInForce.OvernightPlusDay => true, // Must be true for overnight portion
                _ => order.OutsideRth
            };
        }

        /// <summary>
        /// Gets whether the current position is long.
        /// For autonomous trading, this uses the tracked direction rather than the static order side.
        /// </summary>
        private bool IsPositionLong() =>
            _strategy.Order.UseAutonomousTrading ? _isLong : _strategy.Order.Side == OrderSide.Buy;

        /// <summary>
        /// Gets the IB action string for closing the current position.
        /// Returns "SELL" for longs, "BUY" for shorts.
        /// </summary>
        private string GetCloseAction() => IsPositionLong() ? "SELL" : "BUY";

        /// <summary>
        /// Gets the IB action string for opening a position.
        /// Returns "BUY" for longs, "SELL" for shorts.
        /// </summary>
        private string GetOpenAction() => IsPositionLong() ? "BUY" : "SELL";

        /// <summary>
        /// Gets a human-readable description of the TimeInForce setting.
        /// </summary>
        private string GetTifDescription(TimeInForce tif)
        {
            return tif switch
            {
                TimeInForce.Day => "Day (expires 4:00 PM EST)",
                TimeInForce.GoodTillCancel => "GTC (until filled/cancelled)",
                TimeInForce.ImmediateOrCancel => "IOC (immediate fill or cancel)",
                TimeInForce.FillOrKill => "FOK (all or nothing)",
                TimeInForce.Overnight => "Overnight (extended hours only)",
                TimeInForce.OvernightPlusDay => "Overnight+Day (extended + next day)",
                TimeInForce.AtTheOpening => "OPG (opening auction only)",
                _ => tif.ToString()
            };
        }

        /// <summary>
        /// Schedules order cancellation at market open for Overnight TIF orders.
        /// </summary>
        /// <remarks>
        /// Overnight orders should only execute during extended hours.
        /// This method schedules automatic cancellation at 9:30 AM EST if not filled.
        /// </remarks>
        private void ScheduleOvernightCancellation()
        {
            var now = DateTime.Now;
            var marketOpen = now.Date.Add(new TimeSpan(9, 30, 0)); // 9:30 AM local time

            // If market already opened today, schedule for tomorrow
            if (now >= marketOpen)
            {
                marketOpen = marketOpen.AddDays(1);
                // Skip weekends
                if (marketOpen.DayOfWeek == DayOfWeek.Saturday)
                    marketOpen = marketOpen.AddDays(2);
                else if (marketOpen.DayOfWeek == DayOfWeek.Sunday)
                    marketOpen = marketOpen.AddDays(1);
            }

            var delay = marketOpen - now;
            Log($"  OVERNIGHT TIF: Order will cancel at {marketOpen:HH:mm} if not filled ({delay.TotalHours:F1} hours)", ConsoleColor.DarkGray);

            // Schedule the timer to cancel the entry order at market open
            _overnightCancelTimer = new Timer(OvernightCancelCallback, null, delay, Timeout.InfiniteTimeSpan);
        }

        /// <summary>
        /// Callback triggered at market open to cancel unfilled overnight orders.
        /// </summary>
        private void OvernightCancelCallback(object? state)
        {
            // Thread-safe check
            lock (_disposeLock)
            {
                if (_disposed || _entryFilled || _entryOrderId < 0)
                    return;
            }

            Log($"OVERNIGHT ORDER EXPIRED: Cancelling unfilled entry order #{_entryOrderId} at market open", ConsoleColor.Yellow);
            _client.cancelOrder(_entryOrderId, new OrderCancel());

            _isComplete = true;
            _result = StrategyResult.EntryCancelled;
            PrintFinalResult();
        }

        private void OnOrderFill(int orderId, double fillPrice, int fillSize)
        {
            // Check for entry order fill
            if (orderId == _entryOrderId && !_entryFilled)
            {
                _entryFilled = true;
                _entryFillPrice = fillPrice;

                Log($"[OK] ENTRY FILLED @ ${fillPrice:F2} ({fillSize} shares)", ConsoleColor.Green);
                string entryAction = IsPositionLong() ? "BUY" : "SELL";
                SessionLogger?.LogFill(_strategy.Symbol, entryAction, fillSize, fillPrice);
                SessionLogger?.UpdateStrategyStatus(_strategy.Symbol, "Position Open", $"Entry @ ${fillPrice:F2}");

                // Check if FlipMode is enabled - no TP/SL orders, just monitor and flip
                bool isFlipMode = _strategy.Order.UseAutonomousTrading && 
                                  _strategy.Order.AutonomousTrading?.UseFlipMode == true;

                if (isFlipMode)
                {
                    Log($"[FLIP MODE] No TP/SL orders - monitoring for reversal signals", ConsoleColor.Magenta);
                    // In FlipMode, we don't submit TP/SL - the HandleAutonomousExit will flip direction
                    return;
                }

                // Handle take profit (for both long and short positions)
                // Autonomous trading always enables TP/SL with pre-calculated values
                bool enableTp = _strategy.Order.EnableTakeProfit || 
                               (_strategy.Order.UseAutonomousTrading && _dynamicTakeProfit > 0);
                if (enableTp)
                {
                    SubmitTakeProfit(fillPrice);
                }

                // Handle stop loss
                bool enableSl = _strategy.Order.EnableStopLoss ||
                               (_strategy.Order.UseAutonomousTrading && _dynamicStopLoss > 0);
                if (enableSl)
                {
                    SubmitStopLoss(fillPrice);
                }

                // Initialize adaptive order tracking if enabled
                if (_strategy.Order.UseAdaptiveOrder)
                {
                    // Store original prices for adaptive adjustments
                    _originalTakeProfitPrice = _takeProfitTarget;
                    _currentAdaptiveTakeProfitPrice = _takeProfitTarget;

                    if (_strategy.Order.StopLossPrice.HasValue)
                    {
                        _originalStopLossPrice = _strategy.Order.StopLossPrice.Value;
                        _currentAdaptiveStopLossPrice = _strategy.Order.StopLossPrice.Value;
                    }
                    else if (_strategy.Order.StopLossOffset > 0)
                    {
                        bool isLong = IsPositionLong();
                        _originalStopLossPrice = isLong 
                            ? fillPrice - _strategy.Order.StopLossOffset
                            : fillPrice + _strategy.Order.StopLossOffset;
                        _currentAdaptiveStopLossPrice = _originalStopLossPrice;
                    }

                    Log($"ADAPTIVE ORDER ENABLED: TP=${_originalTakeProfitPrice:F2}, SL=${_originalStopLossPrice:F2} ({_strategy.Order.AdaptiveOrder!.Mode})", ConsoleColor.Cyan);
                }

                // Initialize trailing stop loss tracking
                if (_strategy.Order.EnableTrailingStopLoss)
                {
                    _highWaterMark = fillPrice;
                    bool isLong = IsPositionLong();

                    // Calculate initial trailing stop - use ATR if configured, otherwise percentage
                    string stopDescription;
                    if (_strategy.Order.UseAtrStopLoss && _atrCalculator != null && _atrCalculator.IsReady)
                    {
                        var atrConfig = _strategy.Order.AtrStopLoss!;
                        _trailingStopLossPrice = _atrCalculator.CalculateStopPrice(
                            referencePrice: fillPrice,
                            multiplier: atrConfig.Multiplier,
                            isLong: isLong,
                            minPercent: atrConfig.MinStopPercent,
                            maxPercent: atrConfig.MaxStopPercent
                        );
                        double atrValue = _atrCalculator.CurrentAtr;
                        double stopDistance = isLong ? fillPrice - _trailingStopLossPrice : _trailingStopLossPrice - fillPrice;
                        stopDescription = $"{atrConfig.Multiplier:F1}x ATR (ATR=${atrValue:F2}, Distance=${stopDistance:F2})";
                    }
                    else
                    {
                        _trailingStopLossPrice = Math.Round(fillPrice * (1 - _strategy.Order.TrailingStopLossPercent), 2);
                        stopDescription = $"{_strategy.Order.TrailingStopLossPercent * 100:F1}% below entry";
                    }

                    Log($"TRAILING STOP INITIALIZED: ${_trailingStopLossPrice:F2} ({stopDescription})", ConsoleColor.Magenta);
                }

                // Schedule close position if configured
                if (_strategy.Order.ClosePositionTime.HasValue)
                {
                    ScheduleClosePosition(_strategy.Order.ClosePositionTime.Value, _strategy.Order.ClosePositionOnlyIfProfitable);
                }

                Log($"  Monitoring position... Entry=${fillPrice:F2}", ConsoleColor.DarkGray);
                return;
            }

            // Check for trailing stop loss order fill
            if (orderId == _trailingStopLossOrderId && _trailingStopLossTriggered)
            {
                _exitFillPrice = fillPrice;

                // Record trade for learning
                CompletePendingTradeRecord(fillPrice);

                double pnl = _strategy.Order.Quantity * (fillPrice - _entryFillPrice);
                _result = StrategyResult.TrailingStopLossFilled;

                if (pnl >= 0)
                {
                    Log($"*** TRAILING STOP LOSS FILLED @ ${fillPrice:F2} | P&L: ${pnl:F2} (protected profit)", ConsoleColor.Yellow);
                }
                else
                {
                    Log($"*** TRAILING STOP LOSS FILLED @ ${fillPrice:F2} | P&L: ${pnl:F2} (limited loss)", ConsoleColor.Red);
                }
                SessionLogger?.LogFill(_strategy.Symbol, "SELL", _strategy.Order.Quantity, fillPrice, pnl);
                SessionLogger?.UpdateStrategyStatus(_strategy.Symbol, "Trailing Stop", $"Exit @ ${fillPrice:F2} P&L=${pnl:F2}");

                // Cancel take profit if still active (and wasn't rejected)
                if (_takeProfitOrderId > 0 && !_takeProfitFilled && !_takeProfitCancelled && !_takeProfitOrderRejected)
                {
                    _client.cancelOrder(_takeProfitOrderId, new OrderCancel());
                    _takeProfitCancelled = true;
                }

                // For autonomous trading, cycle to next trade instead of stopping
                if (_strategy.Order.UseAutonomousTrading)
                {
                    var config = _strategy.Order.AutonomousTrading;
                    bool positionWasLong = _isLong;
                    
                    // Direction flip on trailing stop - trend reversing, flip direction
                    // Only flip if we had a loss (trend truly reversed), not if we protected profits
                    if (config != null && config.AllowDirectionFlip && pnl < 0)
                    {
                        bool canFlipShort = config.AllowShort && !_shortSaleBlocked;
                        
                        if (positionWasLong && canFlipShort)
                        {
                            Log($"[AUTONOMOUS] Trailing stop hit with loss - FLIPPING TO SHORT", ConsoleColor.Magenta);
                            ResetForNextAutonomousTrade();
                            
                            var (tp, sl) = CalculateAutonomousTpSl(_lastPrice, false, config);
                            ExecuteAutonomousEntry(_lastPrice, _pvSum / Math.Max(_vSum, 1), false, tp, sl, config);
                            return;
                        }
                        else if (!positionWasLong)
                        {
                            Log($"[AUTONOMOUS] Trailing stop hit with loss - FLIPPING TO LONG", ConsoleColor.Magenta);
                            ResetForNextAutonomousTrade();
                            
                            var (tp, sl) = CalculateAutonomousTpSl(_lastPrice, true, config);
                            ExecuteAutonomousEntry(_lastPrice, _pvSum / Math.Max(_vSum, 1), true, tp, sl, config);
                            return;
                        }
                    }
                    
                    Log($"[AUTONOMOUS] Trailing stop hit, cycling to next trade...", ConsoleColor.Cyan);
                    ResetForNextAutonomousTrade();
                    return;
                }

                _isComplete = true;
                PrintFinalResult();
                return;
            }

            // Check for fixed stop loss order fill
            if (orderId == _stopLossOrderId && !_stopLossFilled)
            {
                _stopLossFilled = true;
                _exitFillPrice = fillPrice;

                // Record trade for learning
                CompletePendingTradeRecord(fillPrice);

                double pnl = _strategy.Order.Quantity * (fillPrice - _entryFillPrice);
                _result = StrategyResult.StopLossFilled;

                Log($"*** STOP LOSS FILLED @ ${fillPrice:F2} | P&L: ${pnl:F2}", ConsoleColor.Red);
                SessionLogger?.LogFill(_strategy.Symbol, "SELL", _strategy.Order.Quantity, fillPrice, pnl);
                SessionLogger?.UpdateStrategyStatus(_strategy.Symbol, "Stop Loss", $"Exit @ ${fillPrice:F2} P&L=${pnl:F2}");

                // Cancel take profit if still active (and wasn't rejected)
                if (_takeProfitOrderId > 0 && !_takeProfitFilled && !_takeProfitCancelled && !_takeProfitOrderRejected)
                {
                    _client.cancelOrder(_takeProfitOrderId, new OrderCancel());
                    _takeProfitCancelled = true;
                }

                // For autonomous trading, cycle to next trade instead of stopping
                if (_strategy.Order.UseAutonomousTrading)
                {
                    var config = _strategy.Order.AutonomousTrading;
                    bool positionWasLong = _isLong;
                    
                    // Direction flip on stop loss - market proved us wrong, flip direction
                    // This minimizes losses by immediately pivoting to the winning side
                    if (config != null && config.AllowDirectionFlip)
                    {
                        bool canFlipShort = config.AllowShort && !_shortSaleBlocked;
                        
                        if (positionWasLong && canFlipShort)
                        {
                            Log($"[AUTONOMOUS] Stop loss hit - FLIPPING TO SHORT (market bearish)", ConsoleColor.Magenta);
                            ResetForNextAutonomousTrade();
                            
                            // Execute short entry at current price
                            var (tp, sl) = CalculateAutonomousTpSl(_lastPrice, false, config);
                            ExecuteAutonomousEntry(_lastPrice, _pvSum / Math.Max(_vSum, 1), false, tp, sl, config);
                            return;
                        }
                        else if (!positionWasLong)
                        {
                            Log($"[AUTONOMOUS] Stop loss hit - FLIPPING TO LONG (market bullish)", ConsoleColor.Magenta);
                            ResetForNextAutonomousTrade();
                            
                            // Execute long entry at current price
                            var (tp, sl) = CalculateAutonomousTpSl(_lastPrice, true, config);
                            ExecuteAutonomousEntry(_lastPrice, _pvSum / Math.Max(_vSum, 1), true, tp, sl, config);
                            return;
                        }
                    }
                    
                    Log($"[AUTONOMOUS] Stop loss hit, cycling to next trade...", ConsoleColor.Cyan);
                    ResetForNextAutonomousTrade();
                    return;
                }

                _isComplete = true;
                PrintFinalResult();
                return;
            }

            // Check for take profit order fill (or early exit fill)
            if (orderId == _takeProfitOrderId && !_takeProfitFilled)
            {
                _takeProfitFilled = true;
                _exitFillPrice = fillPrice;

                // Record trade for learning
                CompletePendingTradeRecord(fillPrice);

                double pnl = _strategy.Order.Quantity * (fillPrice - _entryFillPrice);

                // Cancel stop loss if still active (and wasn't rejected)
                if (_stopLossOrderId > 0 && !_stopLossFilled && !_stopLossOrderRejected)
                {
                    _client.cancelOrder(_stopLossOrderId, new OrderCancel());
                }

                // Determine if this was the original TP or an early exit
                if (_exitedWithProfit)
                {
                    _result = StrategyResult.ExitedWithProfit;
                    Log($"*** EXITED WITH PROFIT (time limit) @ ${fillPrice:F2} | P&L: ${pnl:F2}", ConsoleColor.Cyan);
                    SessionLogger?.LogFill(_strategy.Symbol, "SELL", _strategy.Order.Quantity, fillPrice, pnl);
                    SessionLogger?.UpdateStrategyStatus(_strategy.Symbol, "Timed Exit", $"Exit @ ${fillPrice:F2} P&L=${pnl:F2}");
                }
                else
                {
                    _result = StrategyResult.TakeProfitFilled;
                    Log($"*** TAKE PROFIT FILLED @ ${fillPrice:F2} | P&L: ${pnl:F2}", ConsoleColor.Green);
                    SessionLogger?.LogFill(_strategy.Symbol, "SELL", _strategy.Order.Quantity, fillPrice, pnl);
                    SessionLogger?.UpdateStrategyStatus(_strategy.Symbol, "Take Profit", $"Exit @ ${fillPrice:F2} P&L=${pnl:F2}");
                }

                // For autonomous trading, cycle to next trade instead of stopping
                if (_strategy.Order.UseAutonomousTrading)
                {
                    Log($"[AUTONOMOUS] Trade complete, cycling to next trade...", ConsoleColor.Cyan);
                    ResetForNextAutonomousTrade();
                    return;
                }

                // Show final result
                _isComplete = true;
                PrintFinalResult();
                return;
            }

            // Check for autonomous exit order fill (score-based exit)
            if (orderId == _exitOrderId)
            {
                _exitFillPrice = fillPrice;

                double pnl = _strategy.Order.Quantity * (fillPrice - _entryFillPrice);
                var color = pnl >= 0 ? ConsoleColor.Green : ConsoleColor.Red;
                Log($"*** AUTONOMOUS EXIT FILLED @ ${fillPrice:F2} | P&L: ${pnl:F2}", color);
                SessionLogger?.LogFill(_strategy.Symbol, "SELL", _strategy.Order.Quantity, fillPrice, pnl);
                SessionLogger?.UpdateStrategyStatus(_strategy.Symbol, "Autonomous Exit", $"P&L=${pnl:F2}");

                // Reset for next trade (cycling)
                Log($"[AUTONOMOUS] Score-based exit complete, cycling to next trade...", ConsoleColor.Cyan);
                ResetForNextAutonomousTrade();
                return;
            }
        }

        /// <summary>
        /// Handles order rejection from IBKR. Marks orders as rejected so we don't
        /// try to cancel orders that were never successfully placed.
        /// </summary>
        private void OnOrderRejected(int orderId, int errorCode, string errorMessage)
        {
            // Check if TP order was rejected
            if (orderId == _takeProfitOrderId && !_takeProfitFilled)
            {
                _takeProfitOrderRejected = true;
                Log($"[ERR] Take profit order #{orderId} rejected: {errorMessage}", ConsoleColor.Red);
                return;
            }

            // Check if SL order was rejected
            if (orderId == _stopLossOrderId && !_stopLossFilled)
            {
                _stopLossOrderRejected = true;
                Log($"[ERR] Stop loss order #{orderId} rejected: {errorMessage}", ConsoleColor.Red);
                return;
            }

            // Check if entry order was rejected
            if (orderId == _entryOrderId && !_entryFilled)
            {
                Log($"[ERR] Entry order #{orderId} rejected: {errorMessage}", ConsoleColor.Red);
                
                // Check for short sale rejection (error codes 4108, 4110)
                // 4108 = Contract not available for short sale
                // 4110 = No trading permission / Small cap restriction
                if (errorCode == 4108 || errorCode == 4110 || 
                    errorMessage.Contains("short sale", StringComparison.OrdinalIgnoreCase) ||
                    errorMessage.Contains("Small Cap", StringComparison.OrdinalIgnoreCase))
                {
                    _shortSaleBlocked = true;
                    Log($"[WARN] SHORT SALE BLOCKED for {_strategy.Symbol} - future short entries will be skipped", ConsoleColor.Red);
                }
                
                // For autonomous trading, log the rejection and reset for next attempt
                if (_strategy.Order.UseAutonomousTrading)
                {
                    Log($"[AUTONOMOUS] Entry rejected, will retry when conditions are met...", ConsoleColor.Yellow);
                    _entryOrderId = -1;  // Reset to allow new entry attempt
                }
                return;
            }
        }

        /// <summary>
        /// Resets state after an autonomous trade completes to allow the next trade.
        /// Called after TP, SL, TSL, or score-based exits in autonomous mode.
        /// </summary>
        private void ResetForNextAutonomousTrade()
        {
            // IMPORTANT: Dispose pending timers to prevent old callbacks
            _cancelTimer?.Dispose();
            _cancelTimer = null;

            _overnightCancelTimer?.Dispose();
            _overnightCancelTimer = null;

            _closePositionTimer?.Dispose();
            _closePositionTimer = null;

            // Reset condition tracking (autonomous trading calculates its own entry)
            _currentConditionIndex = _strategy.Conditions.Count; // Stay at "all conditions met" for autonomous
            _isComplete = false;

            // Reset order tracking
            _entryOrderId = -1;
            _entryFilled = false;
            _entryFillPrice = 0;

            // Reset take profit tracking
            _takeProfitOrderId = -1;
            _takeProfitFilled = false;
            _takeProfitCancelled = false;
            _takeProfitOrderRejected = false;
            _takeProfitTarget = 0;

            // Reset stop loss tracking
            _stopLossOrderId = -1;
            _stopLossFilled = false;
            _stopLossOrderRejected = false;

            // Reset exit tracking
            _exitedWithProfit = false;
            _exitFillPrice = 0;

            // Reset trailing stop loss tracking
            _trailingStopLossOrderId = -1;
            _trailingStopLossTriggered = false;
            _trailingStopLossPrice = 0;
            _highWaterMark = 0;

            // Reset adaptive order tracking
            _lastAdaptiveAdjustmentTime = DateTime.MinValue;
            _lastAdaptiveScore = 0;
            _originalTakeProfitPrice = 0;
            _originalStopLossPrice = 0;
            _currentAdaptiveTakeProfitPrice = 0;
            _currentAdaptiveStopLossPrice = 0;

            // Reset autonomous exit tracking
            _exitOrderId = -1;
            _dynamicTakeProfit = 0;
            _dynamicStopLoss = 0;
            // Note: _isLong is NOT reset - it will be set on next entry

            // Reset close position tracking
            _closePositionTriggered = false;
            _closePositionOrderId = -1;

            // Reset pending trade record
            _pendingTradeRecord = null;
            _entryScore = null;

            // Reset result
            _result = StrategyResult.Running;

            // DON'T reset VWAP, session high/low, or indicator calculators
            // These should continue accumulating throughout the session

            Log($"*** AUTONOMOUS CYCLE: Ready for next trade signal ***", ConsoleColor.Magenta);
        }

        private void SubmitTakeProfit(double entryPrice)
        {
            var order = _strategy.Order;
            _takeProfitOrderId = _wrapper.ConsumeNextOrderId();

            double tpPrice;
            bool isLong = IsPositionLong();

            // For autonomous trading, use pre-calculated TP target
            if (_strategy.Order.UseAutonomousTrading && _dynamicTakeProfit > 0)
            {
                tpPrice = _dynamicTakeProfit;
            }
            // Check for ADX-based dynamic take profit
            else if (order.AdxTakeProfit != null && _adxCalculator != null && _adxCalculator.IsReady)
            {
                double currentAdx = _adxCalculator.CurrentAdx;
                tpPrice = order.AdxTakeProfit.GetTargetForAdx(currentAdx);
                string trendStr = order.AdxTakeProfit.GetTrendStrength(currentAdx);
                Log($"ADX-BASED TP: ADX={currentAdx:F1} ({trendStr})", ConsoleColor.Cyan);
                Log($"  Conservative=${order.AdxTakeProfit.ConservativeTarget:F2}, Aggressive=${order.AdxTakeProfit.AggressiveTarget:F2}", ConsoleColor.DarkGray);
                Log($"  Selected target: ${tpPrice:F2}", ConsoleColor.Cyan);

                // Track ADX peak for rollover detection
                _adxPeakValue = currentAdx;
                _adxRolledOver = false;
            }
            else if (order.TakeProfitPrice.HasValue)
            {
                tpPrice = order.TakeProfitPrice.Value;
            }
            else
            {
                // For longs: TP = entry + offset (price goes UP for profit)
                // For shorts: TP = entry - offset (price goes DOWN for profit)
                tpPrice = isLong
                    ? entryPrice + order.TakeProfitOffset
                    : entryPrice - order.TakeProfitOffset;
            }

            // Apply tick size rounding to ensure IBKR accepts the order
            tpPrice = PriceHelper.RoundTakeProfitPrice(tpPrice, isLong);
            _takeProfitTarget = tpPrice;

            // For longs: SELL to close (take profit)
            // For shorts: BUY to cover (take profit)
            string tpAction = isLong ? "SELL" : "BUY";

            var tpOrder = new Order
            {
                Action = tpAction,
                OrderType = "LMT",
                TotalQuantity = order.Quantity,
                LmtPrice = tpPrice,
                OutsideRth = order.TakeProfitOutsideRth,
                Tif = order.GetIbTif()
            };

            // Set account if specified
            if (!string.IsNullOrEmpty(AppSettings.AccountNumber))
            {
                tpOrder.Account = AppSettings.AccountNumber;
            }

            Log($">> SUBMITTING TAKE PROFIT {tpAction} {order.Quantity} @ ${tpPrice:F2}", ConsoleColor.Yellow);
            Log($"  OrderId={_takeProfitOrderId} | OutsideRTH={order.TakeProfitOutsideRth}", ConsoleColor.DarkGray);

            _client.placeOrder(_takeProfitOrderId, _contract, tpOrder);

            // Schedule cancellation if EndTime is set
            if (order.EndTime.HasValue)
            {
                ScheduleTakeProfitCancellation(order.EndTime.Value);
            }
        }

        private void ScheduleTakeProfitCancellation(TimeOnly cancelTime)
        {
            var now = DateTime.Now;
            var cancelDateTime = now.Date.Add(cancelTime.ToTimeSpan());

            // If cancel time already passed today, don't schedule
            if (cancelDateTime <= now)
            {
                Log($"WARNING: End time {cancelTime} already passed, no auto-exit scheduled", ConsoleColor.Red);
                return;
            }

            var delay = cancelDateTime - now;
            Log($"TIMER SET: Auto-exit at {cancelTime} ({delay.TotalMinutes:F0} min from now)", ConsoleColor.Magenta);

            _cancelTimer = new Timer(CancelTakeProfitCallback, null, delay, Timeout.InfiniteTimeSpan);
        }

        private void CancelTakeProfitCallback(object? state)
        {
            // Thread-safe check - lock to prevent race with Dispose
            lock (_disposeLock)
            {
                if (_disposed || _takeProfitFilled || _takeProfitCancelled || _takeProfitOrderId < 0)
                    return;

                _takeProfitCancelled = true;
            }

            // Check if position is profitable
            bool isLong = _strategy.Order.Side == OrderSide.Buy;
            bool isProfitable = isLong ? _lastPrice > _entryFillPrice : _lastPrice < _entryFillPrice;

            if (_entryFilled && isProfitable)
            {
                double unrealizedPnl = _strategy.Order.Quantity * (_lastPrice - _entryFillPrice);

                Log($"TIME LIMIT REACHED - Position is profitable!", ConsoleColor.Cyan);
                Log($"  Entry=${_entryFillPrice:F2} | Current=${_lastPrice:F2} | Unrealized P&L=${unrealizedPnl:F2}", ConsoleColor.Cyan);
                Log($"  Cancelling TP order #{_takeProfitOrderId} and selling at limit...", ConsoleColor.Yellow);

                _client.cancelOrder(_takeProfitOrderId, new OrderCancel());

                // Mark that we're exiting early with profit
                _exitedWithProfit = true;

                // Submit limit sell order slightly below current price for quick fill in premarket
                SubmitLimitExit();
            }
            else
            {
                Log($"TIME LIMIT REACHED - Position NOT profitable", ConsoleColor.Yellow);
                Log($"  Entry=${_entryFillPrice:F2} | Current=${_lastPrice:F2} | Cancelling TP order", ConsoleColor.Yellow);
                Log($"  WARNING: STILL HOLDING {_strategy.Order.Quantity} SHARES - manage manually!", ConsoleColor.Red);

                _client.cancelOrder(_takeProfitOrderId, new OrderCancel());

                // Set result - still holding position
                _result = StrategyResult.TakeProfitCancelled;
                _isComplete = true;
                PrintFinalResult();
            }
        }

        private void SubmitLimitExit()
        {
            var order = _strategy.Order;
            int exitOrderId = _wrapper.ConsumeNextOrderId();

            string action = order.Side == OrderSide.Buy ? "SELL" : "BUY";

            // Set limit price slightly below current price for sells (or above for buys) to ensure quick fill in premarket
            double offset = 0.02;
            double limitPrice = order.Side == OrderSide.Buy
                ? Math.Round(_lastPrice - offset, 2)  // Selling long position: slightly below current
                : Math.Round(_lastPrice + offset, 2); // Covering short position: slightly above current

            var exitOrder = new Order
            {
                Action = action,
                OrderType = "LMT",
                LmtPrice = limitPrice,
                TotalQuantity = order.Quantity,
                OutsideRth = order.TakeProfitOutsideRth,
                Tif = "GTC"
            };

            // Set account if specified
            if (!string.IsNullOrEmpty(AppSettings.AccountNumber))
            {
                exitOrder.Account = AppSettings.AccountNumber;
            }

            Log($">> SUBMITTING LIMIT EXIT {action} {order.Quantity} @ ${limitPrice:F2}", ConsoleColor.Yellow);
            Log($"  OrderId={exitOrderId} | OutsideRTH={order.TakeProfitOutsideRth}", ConsoleColor.DarkGray);

            // Update take profit order ID to track the exit fill
            _takeProfitOrderId = exitOrderId;
            _takeProfitCancelled = false; // Reset so we can track the fill

            _client.placeOrder(exitOrderId, _contract, exitOrder);
        }

        private void SubmitStopLoss(double entryPrice)
        {
            var order = _strategy.Order;
            _stopLossOrderId = _wrapper.ConsumeNextOrderId();

            double slPrice;
            bool isLong = IsPositionLong();

            // For autonomous trading, use pre-calculated SL target
            if (_strategy.Order.UseAutonomousTrading && _dynamicStopLoss > 0)
            {
                slPrice = _dynamicStopLoss;
            }
            else if (order.StopLossPrice.HasValue)
            {
                slPrice = order.StopLossPrice.Value;
            }
            else
            {
                // For longs: SL = entry - offset (price goes DOWN = loss)
                // For shorts: SL = entry + offset (price goes UP = loss)
                slPrice = isLong
                    ? entryPrice - order.StopLossOffset
                    : entryPrice + order.StopLossOffset;
            }

            // Apply tick size rounding to ensure IBKR accepts the order
            slPrice = PriceHelper.RoundStopLossPrice(slPrice, isLong);

            // For longs: SELL to close (stop loss)
            // For shorts: BUY to cover (stop loss)
            string slAction = isLong ? "SELL" : "BUY";

            var slOrder = new Order
            {
                Action = slAction,
                OrderType = "STP",
                TotalQuantity = order.Quantity,
                AuxPrice = slPrice,
                OutsideRth = order.OutsideRth,
                Tif = order.GetIbTif()
            };

            // Set account if specified
            if (!string.IsNullOrEmpty(AppSettings.AccountNumber))
            {
                slOrder.Account = AppSettings.AccountNumber;
            }

            Log($">> SUBMITTING STOP LOSS @ ${slPrice:F2}", ConsoleColor.Yellow);
            Log($"  OrderId={_stopLossOrderId}", ConsoleColor.DarkGray);

            _client.placeOrder(_stopLossOrderId, _contract, slOrder);
        }

        /// <summary>
        /// Schedules automatic position close at specified time.
        /// </summary>
        private void ScheduleClosePosition(TimeOnly closeTime, bool onlyIfProfitable)
        {
            var currentTimeET = TimezoneHelper.GetCurrentTime(MarketTimeZone.EST);
            var now = DateTime.Now;

            // Calculate time until close (assumes same day)
            var closeDateTime = now.Date.Add(closeTime.ToTimeSpan());

            // If close time already passed today, don't schedule
            if (closeDateTime <= now)
            {
                Log($"WARNING: ClosePosition time {closeTime:h:mm tt} ET already passed, no auto-close scheduled", ConsoleColor.Red);
                return;
            }

            var delay = closeDateTime - now;
            var profitClause = onlyIfProfitable ? " (only if profitable)" : " (regardless of P&L)";
            Log($"CLOSE POSITION SCHEDULED: {closeTime:h:mm tt} ET{profitClause} ({delay.TotalMinutes:F0} min from now)", ConsoleColor.Magenta);

            _closePositionTimer = new Timer(ClosePositionCallback, onlyIfProfitable, delay, Timeout.InfiniteTimeSpan);
        }

        /// <summary>
        /// Callback triggered at ClosePositionTime to close the position.
        /// </summary>
        private void ClosePositionCallback(object? state)
        {
            bool onlyIfProfitable = state is bool b && b;

            // Thread-safe check
            lock (_disposeLock)
            {
                if (_disposed || _isComplete || !_entryFilled || _closePositionTriggered)
                    return;

                _closePositionTriggered = true;
            }

            // Check if position is profitable
            bool isLong = _strategy.Order.Side == OrderSide.Buy;
            bool isProfitable = isLong ? _lastPrice > _entryFillPrice : _lastPrice < _entryFillPrice;
            double unrealizedPnl = _strategy.Order.Quantity * (_lastPrice - _entryFillPrice);
            if (!isLong) unrealizedPnl = -unrealizedPnl;

            Log($"*** CLOSE POSITION TIME REACHED ***", ConsoleColor.Cyan);
            Log($"  Entry=${_entryFillPrice:F2} | Current=${_lastPrice:F2} | Unrealized P&L=${unrealizedPnl:F2}", ConsoleColor.Cyan);

            if (onlyIfProfitable && !isProfitable)
            {
                Log($"  Position is NOT profitable - keeping position open", ConsoleColor.Yellow);
                Log($"  WARNING: STILL HOLDING {_strategy.Order.Quantity} SHARES - will rely on stop loss or manual exit", ConsoleColor.Red);
                return;
            }

            // Cancel any open take profit order (if not rejected)
            if (_takeProfitOrderId > 0 && !_takeProfitFilled && !_takeProfitCancelled && !_takeProfitOrderRejected)
            {
                Log($"  Cancelling take profit order #{_takeProfitOrderId}...", ConsoleColor.Yellow);
                _client.cancelOrder(_takeProfitOrderId, new OrderCancel());
                _takeProfitCancelled = true;
            }

            // Submit close position order
            SubmitClosePositionOrder(isProfitable);
        }

        /// <summary>
        /// Submits an order to close the position at current price.
        /// </summary>
        private void SubmitClosePositionOrder(bool isProfitable)
        {
            var order = _strategy.Order;
            _closePositionOrderId = _wrapper.ConsumeNextOrderId();

            string action = order.Side == OrderSide.Buy ? "SELL" : "BUY";

            // Check if we're in Regular Trading Hours
            var currentTimeET = TimezoneHelper.GetCurrentTime(MarketTimeZone.EST);
            bool isRTH = MarketTime.RTH.Contains(currentTimeET);

            Order closeOrder;

            if (isRTH)
            {
                // Use market order during RTH for immediate execution
                closeOrder = new Order
                {
                    Action = action,
                    OrderType = "MKT",
                    TotalQuantity = order.Quantity,
                    OutsideRth = false,
                    Tif = "DAY"
                };
                Log($">> SUBMITTING CLOSE POSITION {action} {order.Quantity} @ MKT (RTH)", ConsoleColor.Yellow);
            }
            else
            {
                // Use limit order outside RTH for safer execution
                double offset = 0.02;
                double limitPrice = order.Side == OrderSide.Buy
                    ? Math.Round(_lastPrice - offset, 2)  // Selling long: slightly below current
                    : Math.Round(_lastPrice + offset, 2); // Covering short: slightly above current

                closeOrder = new Order
                {
                    Action = action,
                    OrderType = "LMT",
                    LmtPrice = limitPrice,
                    TotalQuantity = order.Quantity,
                    OutsideRth = true,
                    Tif = "GTC"
                };
                Log($">> SUBMITTING CLOSE POSITION {action} {order.Quantity} @ ${limitPrice:F2} LMT (Outside RTH)", ConsoleColor.Yellow);
            }

            // Set account if specified
            if (!string.IsNullOrEmpty(AppSettings.AccountNumber))
            {
                closeOrder.Account = AppSettings.AccountNumber;
            }

            Log($"  OrderId={_closePositionOrderId}", ConsoleColor.DarkGray);

            // Track fill using the take profit order slot (it's already cancelled)
            _takeProfitOrderId = _closePositionOrderId;
            _takeProfitCancelled = false;
            _exitedWithProfit = isProfitable;

            _client.placeOrder(_closePositionOrderId, _contract, closeOrder);
        }

        public void Dispose()
        {
            lock (_disposeLock)
            {
                if (_disposed) return;
                _disposed = true;
            }

            // Dispose AI advisor
            _aiAdvisor?.Dispose();
            _aiAdvisor = null;

            // Dispose timers first to prevent callbacks after disposal starts
            _cancelTimer?.Dispose();
            _cancelTimer = null;

            _overnightCancelTimer?.Dispose();
            _overnightCancelTimer = null;

            _closePositionTimer?.Dispose();
            _closePositionTimer = null;

            // Unsubscribe from events to prevent memory leaks
            _wrapper.OnOrderFill -= OnOrderFill;
            _candlestickAggregator.OnCandleComplete -= OnCandleComplete;

            // Note: Do NOT null out calculator fields here!
            // The lambda callbacks (e.g., volumeAbove.GetCurrentVolume = () => _volumeCalculator.CurrentVolume)
            // capture 'this' and access fields at evaluation time. If we null these fields,
            // any in-flight condition evaluation during a race window would throw NullReferenceException.
            // The GC will handle cleanup when the StrategyRunner instance is no longer referenced.
            _emaCalculators.Clear();

            // If strategy never completed, determine final result
            if (_result == StrategyResult.Running)
            {
                if (!_entryFilled)
                {
                    _result = StrategyResult.NeverBought;
                }
                PrintFinalResult();
            }

            Log("Strategy disposed", ConsoleColor.DarkGray);
        }

        private void PrintFinalResult()
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            Console.WriteLine();
            Console.WriteLine($"[{timestamp}] +===============================================================+");

            double pnl = _strategy.Order.Quantity * (_exitFillPrice - _entryFillPrice);
            string resultMsg;
            string detailsMsg;

            switch (_result)
            {
                case StrategyResult.TakeProfitFilled:
                    Console.ForegroundColor = ConsoleColor.Green;
                    resultMsg = $"[{_strategy.Symbol}] RESULT: *** TAKE PROFIT FILLED ***";
                    detailsMsg = $"Entry: ${_entryFillPrice:F2} -> Exit: ${_exitFillPrice:F2} | P&L: ${pnl:F2}";
                    Console.WriteLine($"[{timestamp}] |  {resultMsg}");
                    Console.WriteLine($"[{timestamp}] |  Entry: ${_entryFillPrice:F2} -> Exit: ${_exitFillPrice:F2}");
                    Console.WriteLine($"[{timestamp}] |  P&L: ${pnl:F2}");
                    SessionLogger?.LogEvent("RESULT", resultMsg);
                    SessionLogger?.LogEvent("RESULT", detailsMsg);
                    break;

                case StrategyResult.StopLossFilled:
                    Console.ForegroundColor = ConsoleColor.Red;
                    resultMsg = $"[{_strategy.Symbol}] RESULT: *** STOP LOSS FILLED ***";
                    detailsMsg = $"Entry: ${_entryFillPrice:F2} -> Exit: ${_exitFillPrice:F2} | P&L: ${pnl:F2}";
                    Console.WriteLine($"[{timestamp}] |  {resultMsg}");
                    Console.WriteLine($"[{timestamp}] |  Entry: ${_entryFillPrice:F2} -> Exit: ${_exitFillPrice:F2}");
                    Console.WriteLine($"[{timestamp}] |  P&L: ${pnl:F2}");
                    SessionLogger?.LogEvent("RESULT", resultMsg);
                    SessionLogger?.LogEvent("RESULT", detailsMsg);
                    break;

                case StrategyResult.TrailingStopLossFilled:
                    if (pnl >= 0)
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        resultMsg = $"[{_strategy.Symbol}] RESULT: *** TRAILING STOP LOSS (profit protected) ***";
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        resultMsg = $"[{_strategy.Symbol}] RESULT: *** TRAILING STOP LOSS (loss limited) ***";
                    }
                    detailsMsg = $"Entry: ${_entryFillPrice:F2} -> Exit: ${_exitFillPrice:F2} | High: ${_highWaterMark:F2} | P&L: ${pnl:F2}";
                    Console.WriteLine($"[{timestamp}] |  {resultMsg}");
                    Console.WriteLine($"[{timestamp}] |  Entry: ${_entryFillPrice:F2} -> Exit: ${_exitFillPrice:F2}");
                    Console.WriteLine($"[{timestamp}] |  High: ${_highWaterMark:F2} | Trail Stop: ${_trailingStopLossPrice:F2}");
                    Console.WriteLine($"[{timestamp}] |  P&L: ${pnl:F2}");
                    SessionLogger?.LogEvent("RESULT", resultMsg);
                    SessionLogger?.LogEvent("RESULT", detailsMsg);
                    break;

                case StrategyResult.ExitedWithProfit:
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    resultMsg = $"[{_strategy.Symbol}] RESULT: *** EXITED WITH PROFIT (time limit) ***";
                    detailsMsg = $"Entry: ${_entryFillPrice:F2} -> Exit: ${_exitFillPrice:F2} | P&L: ${pnl:F2}";
                    Console.WriteLine($"[{timestamp}] |  {resultMsg}");
                    Console.WriteLine($"[{timestamp}] |  Entry: ${_entryFillPrice:F2} -> Exit: ${_exitFillPrice:F2}");
                    Console.WriteLine($"[{timestamp}] |  P&L: ${pnl:F2}");
                    SessionLogger?.LogEvent("RESULT", resultMsg);
                    SessionLogger?.LogEvent("RESULT", detailsMsg);
                    break;

                case StrategyResult.TakeProfitCancelled:
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    resultMsg = $"[{_strategy.Symbol}] RESULT: TAKE PROFIT CANCELLED (not profitable)";
                    detailsMsg = $"Entry: ${_entryFillPrice:F2} | Current: ${_lastPrice:F2} | WARNING: STILL HOLDING {_strategy.Order.Quantity} SHARES";
                    Console.WriteLine($"[{timestamp}] |  {resultMsg}");
                    Console.WriteLine($"[{timestamp}] |  Entry: ${_entryFillPrice:F2} | Current: ${_lastPrice:F2}");
                    Console.WriteLine($"[{timestamp}] |  WARNING: STILL HOLDING {_strategy.Order.Quantity} SHARES - MANAGE MANUALLY!");
                    SessionLogger?.LogEvent("RESULT", resultMsg);
                    SessionLogger?.LogEvent("RESULT", detailsMsg);
                    break;

                case StrategyResult.NeverBought:
                    Console.ForegroundColor = ConsoleColor.Gray;
                    resultMsg = $"[{_strategy.Symbol}] RESULT: NEVER BOUGHT";
                    detailsMsg = "Conditions not met - no position taken";
                    Console.WriteLine($"[{timestamp}] |  {resultMsg}");
                    Console.WriteLine($"[{timestamp}] |  {detailsMsg}");
                    SessionLogger?.LogEvent("RESULT", resultMsg);
                    SessionLogger?.LogEvent("RESULT", detailsMsg);
                    break;

                case StrategyResult.MissedTheBoat:
                    Console.ForegroundColor = ConsoleColor.DarkYellow;
                    resultMsg = $"[{_strategy.Symbol}] RESULT: MISSED THE BOAT";
                    detailsMsg = "Price already at/above take profit target when conditions met. No position taken";
                    Console.WriteLine($"[{timestamp}] |  {resultMsg}");
                    Console.WriteLine($"[{timestamp}] |  {detailsMsg}");
                    SessionLogger?.LogEvent("RESULT", resultMsg);
                    SessionLogger?.LogEvent("RESULT", detailsMsg);
                    break;

                case StrategyResult.EntryCancelled:
                    Console.ForegroundColor = ConsoleColor.Gray;
                    resultMsg = $"[{_strategy.Symbol}] RESULT: ENTRY CANCELLED";
                    detailsMsg = "Entry order was cancelled before fill";
                    Console.WriteLine($"[{timestamp}] |  {resultMsg}");
                    Console.WriteLine($"[{timestamp}] |  {detailsMsg}");
                    SessionLogger?.LogEvent("RESULT", resultMsg);
                    SessionLogger?.LogEvent("RESULT", detailsMsg);
                    break;

                default:
                    Console.ForegroundColor = ConsoleColor.White;
                    resultMsg = $"[{_strategy.Symbol}] RESULT: {_result}";
                    Console.WriteLine($"[{timestamp}] |  {resultMsg}");
                    SessionLogger?.LogEvent("RESULT", resultMsg);
                    break;
            }

            Console.ResetColor();
            Console.WriteLine($"[{timestamp}] +===============================================================+");

            // Check if strategy should repeat after completion
            if (_strategy.RepeatEnabled && ShouldRepeat())
            {
                ResetForRepeat();
            }
        }

        /// <summary>
        /// Determines if the strategy should reset and repeat after the current trade completes.
        /// </summary>
        private bool ShouldRepeat()
        {
            // Only repeat after successful exits (TP, SL, TSL, or ExitedWithProfit)
            // Don't repeat if we never bought, missed the boat, or entry was cancelled
            return _result switch
            {
                StrategyResult.TakeProfitFilled => true,
                StrategyResult.StopLossFilled => true,
                StrategyResult.TrailingStopLossFilled => true,
                StrategyResult.ExitedWithProfit => true,
                _ => false
            };
        }
    }
}


