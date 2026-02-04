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
using IdiotProof.Backend.Enums;
using IdiotProof.Backend.Helpers;
using IdiotProof.Backend.Logging;
using IdiotProof.Backend.Strategy;
using IdiotProof.Shared.Helpers;
using IdiotProof.Shared.Settings;
using IbContract = IBApi.Contract;
using MarketTimeZone = IdiotProof.Shared.Enums.MarketTimeZone;

namespace IdiotProof.Backend.Models
{
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
        private double _takeProfitTarget;

        // Stop loss tracking
        private int _stopLossOrderId = -1;
        private bool _stopLossFilled;

        // Exit tracking
        private bool _exitedWithProfit;
        private double _exitFillPrice;

        // Trailing stop loss tracking
        private int _trailingStopLossOrderId = -1;
        private bool _trailingStopLossTriggered;
        private double _trailingStopLossPrice;
        private double _highWaterMark;  // Highest price since entry (for trailing stop)

        // ATR calculator for volatility-based stops
        private readonly Helpers.AtrCalculator? _atrCalculator;

        // Candlestick aggregator for candle-based indicators
        private readonly Helpers.CandlestickAggregator _candlestickAggregator;

        // EMA calculators for indicator conditions
        private readonly Dictionary<int, Helpers.EmaCalculator> _emaCalculators = new();

        // ADX calculator for trend strength and DI conditions
        private Helpers.AdxCalculator? _adxCalculator;

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
                maxCandles: Settings.MaxCandlesticks);
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

            // Subscribe to fill events
            _wrapper.OnOrderFill += OnOrderFill;

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
            var candles = historicalBars.Select(bar => new Helpers.Candlestick
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
            }

            Log($"Historical warm-up complete. Ready for live data.", ConsoleColor.Cyan);
        }

        /// <summary>
        /// Called when a new candlestick completes.
        /// Updates all candle-based indicators.
        /// </summary>
        private void OnCandleComplete(Helpers.Candlestick candle)
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
        /// Initializes all indicator calculators for the strategy conditions
        /// and wires up the callback functions.
        /// </summary>
        private void InitializeIndicatorCalculators()
        {
            foreach (var condition in _strategy.Conditions)
            {
                try
                {
                    switch (condition)
                    {
                        case EmaAboveCondition emaAbove:
                            var emaAboveCalc = GetOrCreateEmaCalculator(emaAbove.Period);
                            emaAbove.GetEmaValue = () => emaAboveCalc.CurrentValue;
                            Log($"Initialized EMA({emaAbove.Period}) calculator for 'Above EMA' condition", ConsoleColor.DarkGray);
                            break;

                        case EmaBelowCondition emaBelow:
                            var emaBelowCalc = GetOrCreateEmaCalculator(emaBelow.Period);
                            emaBelow.GetEmaValue = () => emaBelowCalc.CurrentValue;
                            Log($"Initialized EMA({emaBelow.Period}) calculator for 'Below EMA' condition", ConsoleColor.DarkGray);
                            break;

                        case EmaBetweenCondition emaBetween:
                            var lowerCalc = GetOrCreateEmaCalculator(emaBetween.LowerPeriod);
                            var upperCalc = GetOrCreateEmaCalculator(emaBetween.UpperPeriod);
                            emaBetween.GetLowerEmaValue = () => lowerCalc.CurrentValue;
                            emaBetween.GetUpperEmaValue = () => upperCalc.CurrentValue;
                            Log($"Initialized EMA({emaBetween.LowerPeriod}) and EMA({emaBetween.UpperPeriod}) calculators for 'Between EMA' condition", ConsoleColor.DarkGray);
                            break;

                        case AdxCondition adxCondition:
                            // Create ADX calculator if not already initialized
                            _adxCalculator ??= new Helpers.AdxCalculator(period: 14, ticksPerBar: 50);
                            adxCondition.GetAdxValue = () => _adxCalculator.CurrentAdx;
                            Log($"Initialized ADX(14) calculator for 'ADX {adxCondition.Comparison} {adxCondition.Threshold}' condition", ConsoleColor.DarkGray);
                            break;

                        case RsiCondition rsiCondition:
                            // Create RSI calculator if not already initialized
                            _rsiCalculator ??= new Helpers.RsiCalculator(period: 14);
                            rsiCondition.GetRsiValue = () => _rsiCalculator.CurrentValue;
                            Log($"Initialized RSI(14) calculator for '{rsiCondition.Name}' condition", ConsoleColor.DarkGray);
                            break;

                        case MacdCondition macdCondition:
                            // Create MACD calculator if not already initialized
                            _macdCalculator ??= new Helpers.MacdCalculator(12, 26, 9);
                            macdCondition.GetMacdValues = () => (
                                _macdCalculator.MacdLine,
                                _macdCalculator.SignalLine,
                                _macdCalculator.Histogram,
                                _macdCalculator.PreviousHistogram
                            );
                            Log($"Initialized MACD(12,26,9) calculator for '{macdCondition.Name}' condition", ConsoleColor.DarkGray);
                            break;

                        case DiCondition diCondition:
                            // DI uses the ADX calculator (which calculates +DI/-DI)
                            _adxCalculator ??= new Helpers.AdxCalculator(period: 14, ticksPerBar: 50);
                            diCondition.GetDiValues = () => (_adxCalculator.PlusDI, _adxCalculator.MinusDI);
                            Log($"Initialized DI calculator for '{diCondition.Name}' condition", ConsoleColor.DarkGray);
                            break;

                        case MomentumAboveCondition momentumAbove:
                            _momentumCalculator ??= new Helpers.MomentumCalculator(period: 10);
                            momentumAbove.GetMomentumValue = () => _momentumCalculator.CurrentValue;
                            Log($"Initialized Momentum(10) calculator for '{momentumAbove.Name}' condition", ConsoleColor.DarkGray);
                            break;

                        case MomentumBelowCondition momentumBelow:
                            _momentumCalculator ??= new Helpers.MomentumCalculator(period: 10);
                            momentumBelow.GetMomentumValue = () => _momentumCalculator.CurrentValue;
                            Log($"Initialized Momentum(10) calculator for '{momentumBelow.Name}' condition", ConsoleColor.DarkGray);
                            break;

                        case HigherLowsCondition higherLows:
                            higherLows.GetRecentLows = () => _candlestickAggregator.GetRecentLows(higherLows.LookbackBars);
                            Log($"Initialized HigherLows({higherLows.LookbackBars}) pattern detector", ConsoleColor.DarkGray);
                            break;

                        case EmaTurningUpCondition emaTurningUp:
                            var turningUpCalc = GetOrCreateEmaCalculator(emaTurningUp.Period);
                            emaTurningUp.GetCurrentEmaValue = () => turningUpCalc.CurrentValue;
                            emaTurningUp.GetPreviousEmaValue = () => turningUpCalc.PreviousValue;
                            Log($"Initialized EMA({emaTurningUp.Period}) calculator for 'EMA Turning Up' condition", ConsoleColor.DarkGray);
                            break;

                        case VolumeAboveCondition volumeAbove:
                            _volumeCalculator ??= new Helpers.VolumeCalculator(period: 20);
                            volumeAbove.GetCurrentVolume = () => _volumeCalculator.CurrentVolume;
                            volumeAbove.GetAverageVolume = () => _volumeCalculator.AverageVolume;
                            Log($"Initialized Volume calculator for 'Volume >= {volumeAbove.Multiplier:F1}x' condition", ConsoleColor.DarkGray);
                            break;

                        case CloseAboveVwapCondition closeAboveVwap:
                            closeAboveVwap.GetLastClose = () => _candlestickAggregator.LastCompletedCandle?.Close ?? 0;
                            Log($"Initialized CloseAboveVwap condition (uses last candle close)", ConsoleColor.DarkGray);
                            break;

                        case VwapRejectionCondition vwapRejection:
                            vwapRejection.GetLastHigh = () => _candlestickAggregator.LastCompletedCandle?.High ?? 0;
                            vwapRejection.GetLastClose = () => _candlestickAggregator.LastCompletedCandle?.Close ?? 0;
                            Log($"Initialized VwapRejection condition (uses last candle high/close)", ConsoleColor.DarkGray);
                            break;

                        case RocAboveCondition rocAbove:
                            _rocCalculator ??= new Helpers.RocCalculator(period: 10);
                            rocAbove.GetRocValue = () => _rocCalculator.CurrentValue;
                            Log($"Initialized ROC(10) calculator for '{rocAbove.Name}' condition", ConsoleColor.DarkGray);
                            break;

                        case RocBelowCondition rocBelow:
                            _rocCalculator ??= new Helpers.RocCalculator(period: 10);
                            rocBelow.GetRocValue = () => _rocCalculator.CurrentValue;
                            Log($"Initialized ROC(10) calculator for '{rocBelow.Name}' condition", ConsoleColor.DarkGray);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    // Log but continue - don't let one failed calculator prevent others from initializing
                    Log($"WARNING: Failed to initialize calculator for '{condition.Name}': {ex.Message}", ConsoleColor.Yellow);
                }
            }

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
            }

            // Evaluate current condition
            EvaluateConditions(lastPrice, vwap);
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
                // For short positions, track low water mark and stop above
                // (inverse logic - not implemented yet, but structure is here)
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

            // Cancel any existing take profit order
            if (_takeProfitOrderId > 0 && !_takeProfitFilled && !_takeProfitCancelled)
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
            if (!string.IsNullOrEmpty(Settings.AccountNumber))
            {
                stopOrder.Account = Settings.AccountNumber;
            }

            var sessionStr = isRTH ? "RTH" : "Extended";
            var orderTypeStr = isRTH ? "MKT" : $"LMT @ ${stopOrder.LmtPrice:F2}";
            Log($">> SUBMITTING TRAILING STOP LOSS {action} {order.Quantity} @ {orderTypeStr} ({sessionStr})", ConsoleColor.Red);
            Log($"  OrderId={_trailingStopLossOrderId} | Triggered at ${_lastPrice:F2} | Stop Level: ${_trailingStopLossPrice:F2}", ConsoleColor.DarkGray);

            _client.placeOrder(_trailingStopLossOrderId, _contract, stopOrder);
        }

        /// <summary>
        /// Monitors and adjusts take profit and stop loss orders based on market conditions.
        /// Uses multiple indicators to calculate a market score and adjusts orders accordingly.
        /// </summary>
        private void MonitorAdaptiveOrder(double currentPrice, double vwap)
        {
            var order = _strategy.Order;

            // Only monitor if adaptive order is enabled and we have an open position
            if (!order.UseAdaptiveOrder || !_entryFilled || _isComplete)
                return;

            var config = order.AdaptiveOrder!;

            // Rate limiting - don't adjust too frequently
            var now = DateTime.UtcNow;
            var timeSinceLastAdjustment = (now - _lastAdaptiveAdjustmentTime).TotalSeconds;
            if (timeSinceLastAdjustment < config.MinSecondsBetweenAdjustments)
                return;

            // Calculate market score
            var score = CalculateMarketScore(currentPrice, vwap, order.Side == OrderSide.Buy);

            // Check for emergency exit
            if (score.ShouldEmergencyExit)
            {
                Log($"*** ADAPTIVE EMERGENCY EXIT! Score: {score.TotalScore} ({score.Condition})", ConsoleColor.Red);
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
                double profitRange = _originalTakeProfitPrice - _entryFillPrice;
                double adjustment = profitRange * (score.TakeProfitMultiplier - 1.0);
                double newTakeProfitPrice = Math.Round(_originalTakeProfitPrice + adjustment, 2);

                // Ensure TP doesn't go below entry (no negative profit target)
                newTakeProfitPrice = Math.Max(newTakeProfitPrice, _entryFillPrice + 0.01);

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
                double lossRange = _entryFillPrice - _originalStopLossPrice;
                double adjustment = lossRange * (score.StopLossMultiplier - 1.0);
                double newStopLossPrice = Math.Round(_originalStopLossPrice - adjustment, 2);

                // Ensure SL doesn't go above entry (no profit stop)
                newStopLossPrice = Math.Min(newStopLossPrice, _entryFillPrice - 0.01);

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
        /// Calculates a market score (-100 to +100) based on multiple indicators.
        /// Positive = bullish, Negative = bearish.
        /// </summary>
        private MarketScore CalculateMarketScore(double price, double vwap, bool isLong)
        {
            var order = _strategy.Order;
            var config = order.AdaptiveOrder!;

            int vwapScore = 0, emaScore = 0, rsiScore = 0, macdScore = 0, adxScore = 0, volumeScore = 0;

            // VWAP Position (15% weight) - Range: -100 to +100
            if (vwap > 0)
            {
                double vwapDiff = (price - vwap) / vwap * 100; // Percentage above/below VWAP
                vwapScore = (int)Math.Clamp(vwapDiff * 20, -100, 100); // Scale: 5% above VWAP = +100
            }

            // EMA Stack Alignment (20% weight)
            if (_emaCalculators.Count > 0)
            {
                int bullishCount = 0, bearishCount = 0;
                var sortedEmas = _emaCalculators.OrderBy(kvp => kvp.Key).ToList();

                foreach (var kvp in sortedEmas)
                {
                    if (kvp.Value.IsReady)
                    {
                        if (price > kvp.Value.CurrentValue) bullishCount++;
                        else bearishCount++;
                    }
                }

                // Check EMA alignment (price > EMA9 > EMA21 = very bullish)
                int total = bullishCount + bearishCount;
                if (total > 0)
                {
                    emaScore = (int)((bullishCount - bearishCount) / (double)total * 100);
                }
            }

            // RSI (15% weight) - Overbought/oversold affects score
            if (_rsiCalculator?.IsReady == true)
            {
                double rsi = _rsiCalculator.CurrentValue;
                // RSI 50 = neutral (0), RSI 70+ = overbought (-50 to -100), RSI 30- = oversold (+50 to +100)
                if (rsi > 70)
                    rsiScore = (int)(-(rsi - 70) * 3.33); // 70->0, 100->-100
                else if (rsi < 30)
                    rsiScore = (int)((30 - rsi) * 3.33); // 30->0, 0->+100
                else
                    rsiScore = (int)((rsi - 50) * 2.5); // 30->-50, 70->+50
            }

            // MACD (20% weight)
            if (_macdCalculator?.IsReady == true)
            {
                bool bullish = _macdCalculator.IsBullish;
                double histogram = _macdCalculator.Histogram;
                // Scale histogram to score (-100 to +100)
                macdScore = bullish ? 50 : -50;
                macdScore += (int)Math.Clamp(histogram * 500, -50, 50); // Histogram strength adds ±50
            }

            // ADX Trend Strength (20% weight)
            if (_adxCalculator?.IsReady == true)
            {
                double adx = _adxCalculator.CurrentAdx;
                bool diPositive = _adxCalculator.PlusDI > _adxCalculator.MinusDI;

                // ADX determines magnitude, DI determines direction
                int magnitude = (int)Math.Min(adx * 2, 100); // ADX 50+ = max magnitude
                adxScore = diPositive ? magnitude : -magnitude;
            }

            // Volume (10% weight)
            if (_volumeCalculator?.IsReady == true)
            {
                double volumeRatio = _volumeCalculator.VolumeRatio;
                // High volume confirms moves: >1.5x = +50, >2x = +100
                if (volumeRatio > 1.0)
                {
                    int volumeMagnitude = (int)Math.Min((volumeRatio - 1.0) * 100, 100);
                    // Volume confirms the current direction
                    volumeScore = price > vwap ? volumeMagnitude : -volumeMagnitude;
                }
            }

            // Calculate weighted total score
            double totalScore =
                vwapScore * config.WeightVwap +
                emaScore * config.WeightEma +
                rsiScore * config.WeightRsi +
                macdScore * config.WeightMacd +
                adxScore * config.WeightAdx +
                volumeScore * config.WeightVolume;

            int finalScore = (int)Math.Clamp(totalScore, -100, 100);

            // For short positions, invert the score
            if (!isLong)
                finalScore = -finalScore;

            // Calculate adjustment multipliers based on score and mode
            double tpMultiplier = CalculateTakeProfitMultiplier(finalScore, config);
            double slMultiplier = CalculateStopLossMultiplier(finalScore, config);

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
                TakeProfitMultiplier = tpMultiplier,
                StopLossMultiplier = slMultiplier,
                ShouldEmergencyExit = shouldExit
            };
        }

        /// <summary>
        /// Calculates the take profit multiplier based on market score.
        /// </summary>
        private static double CalculateTakeProfitMultiplier(int score, AdaptiveOrderConfig config)
        {
            // Score ranges: -100 to +100
            // Strong bullish (70+): Extend TP
            // Moderate bullish (30-70): Keep original
            // Neutral (-30 to 30): Slightly reduce
            // Moderate bearish (-70 to -30): Reduce significantly
            // Strong bearish (<-70): Maximum reduction

            if (score >= 70)
            {
                // Extend: 1.0 to 1.0 + MaxExtension
                double extensionFactor = (score - 70) / 30.0; // 0 at 70, 1 at 100
                return 1.0 + (config.MaxTakeProfitExtension * extensionFactor);
            }
            else if (score >= 30)
            {
                // Keep original
                return 1.0;
            }
            else if (score >= -30)
            {
                // Slight reduction: 1.0 to 0.85
                double reductionFactor = (30 - score) / 60.0; // 0 at 30, 1 at -30
                return 1.0 - (0.15 * reductionFactor);
            }
            else if (score >= -70)
            {
                // Moderate reduction: 0.85 to 1.0 - MaxReduction/2
                double reductionFactor = (-30 - score) / 40.0; // 0 at -30, 1 at -70
                return 0.85 - ((config.MaxTakeProfitReduction / 2 - 0.15) * reductionFactor);
            }
            else
            {
                // Maximum reduction
                return 1.0 - config.MaxTakeProfitReduction;
            }
        }

        /// <summary>
        /// Calculates the stop loss multiplier based on market score.
        /// </summary>
        private static double CalculateStopLossMultiplier(int score, AdaptiveOrderConfig config)
        {
            // Strong bullish: Tighten SL to protect gains
            // Neutral: Widen slightly to avoid noise
            // Bearish: Keep tight to limit losses

            if (score >= 70)
            {
                // Tighten: Multiplier > 1 means stop gets closer
                double tightenFactor = (score - 70) / 30.0;
                return 1.0 + (config.MaxStopLossTighten * tightenFactor);
            }
            else if (score >= 0)
            {
                // Slight tighten to neutral
                return 1.0;
            }
            else if (score >= -50)
            {
                // Widen slightly: Multiplier < 1 means stop gets further
                double widenFactor = -score / 50.0;
                return 1.0 - (config.MaxStopLossWiden * widenFactor * 0.5);
            }
            else
            {
                // Keep relatively tight in bearish conditions
                return 1.0 - (config.MaxStopLossWiden * 0.5);
            }
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

            if (!string.IsNullOrEmpty(Settings.AccountNumber))
                tpOrder.Account = Settings.AccountNumber;

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

            if (!string.IsNullOrEmpty(Settings.AccountNumber))
                slOrder.Account = Settings.AccountNumber;

            _wrapper.ModifyOrder(_stopLossOrderId, _contract, slOrder);
        }

        /// <summary>
        /// Executes an emergency exit when market conditions are severely against the position.
        /// </summary>
        private void ExecuteEmergencyExit()
        {
            if (_isComplete) return;

            // Cancel existing orders
            if (_takeProfitOrderId > 0 && !_takeProfitFilled && !_takeProfitCancelled)
            {
                Log($"Cancelling take profit order #{_takeProfitOrderId} for emergency exit...", ConsoleColor.Yellow);
                _client.cancelOrder(_takeProfitOrderId, new OrderCancel());
                _takeProfitCancelled = true;
            }

            if (_stopLossOrderId > 0 && !_stopLossFilled)
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

            if (!string.IsNullOrEmpty(Settings.AccountNumber))
                exitOrder.Account = Settings.AccountNumber;

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
                var info = TimezoneHelper.GetTimezoneDisplayInfo(Settings.Timezone);
                var startLocal = TimezoneHelper.ToLocal(_strategy.StartTime.Value, Settings.Timezone);
                Log($"Will start monitoring at {_strategy.StartTime.Value:h:mm tt} ET ({startLocal:h:mm tt} {info.Abbreviation})", ConsoleColor.DarkGray);
            }
        }

        private double GetVwap()
        {
            return _vSum > 0 ? _pvSum / _vSum : 0;
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
                    var info = TimezoneHelper.GetTimezoneDisplayInfo(Settings.Timezone);
                    var startLocal = TimezoneHelper.ToLocal(_strategy.StartTime.Value, Settings.Timezone);
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
                    var info = TimezoneHelper.GetTimezoneDisplayInfo(Settings.Timezone);
                    var startLocal = _strategy.StartTime.HasValue
                        ? TimezoneHelper.ToLocal(_strategy.StartTime.Value, Settings.Timezone)
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
        /// Gets additional context info for indicator conditions (the actual indicator values).
        /// </summary>
        private string GetEmaContextInfo(IStrategyCondition condition)
        {
            return condition switch
            {
                EmaAboveCondition ema when ema.GetEmaValue != null =>
                    $" (EMA({ema.Period})=${ema.GetEmaValue():F2})",
                EmaBelowCondition ema when ema.GetEmaValue != null =>
                    $" (EMA({ema.Period})=${ema.GetEmaValue():F2})",
                EmaBetweenCondition ema when ema.GetLowerEmaValue != null && ema.GetUpperEmaValue != null =>
                    $" (EMA({ema.LowerPeriod})=${ema.GetLowerEmaValue():F2}, EMA({ema.UpperPeriod})=${ema.GetUpperEmaValue():F2})",
                AdxCondition adx when adx.GetAdxValue != null && _adxCalculator != null =>
                    $" (ADX={adx.GetAdxValue():F1}, +DI={_adxCalculator.PlusDI:F1}, -DI={_adxCalculator.MinusDI:F1})",
                RsiCondition rsi when rsi.GetRsiValue != null =>
                    $" (RSI={rsi.GetRsiValue():F1})",
                MacdCondition macd when macd.GetMacdValues != null =>
                    $" (MACD={_macdCalculator?.MacdLine:F2}, Signal={_macdCalculator?.SignalLine:F2})",
                DiCondition di when di.GetDiValues != null && _adxCalculator != null =>
                    $" (+DI={_adxCalculator.PlusDI:F1}, -DI={_adxCalculator.MinusDI:F1})",
                MomentumAboveCondition mom when mom.GetMomentumValue != null =>
                    $" (Momentum={mom.GetMomentumValue():F2})",
                MomentumBelowCondition mom when mom.GetMomentumValue != null =>
                    $" (Momentum={mom.GetMomentumValue():F2})",
                HigherLowsCondition hl when hl.GetRecentLows != null =>
                    FormatHigherLowsInfo(hl),
                EmaTurningUpCondition emaUp when emaUp.GetCurrentEmaValue != null && emaUp.GetPreviousEmaValue != null =>
                    $" (EMA({emaUp.Period})=${emaUp.GetCurrentEmaValue():F2}, Prev=${emaUp.GetPreviousEmaValue():F2})",
                VolumeAboveCondition vol when vol.GetCurrentVolume != null && vol.GetAverageVolume != null =>
                    $" (Vol={vol.GetCurrentVolume():N0}, Avg={vol.GetAverageVolume():N0})",
                CloseAboveVwapCondition closeVwap when closeVwap.GetLastClose != null =>
                    $" (Close=${closeVwap.GetLastClose():F2})",
                VwapRejectionCondition vwapRej when vwapRej.GetLastHigh != null && vwapRej.GetLastClose != null =>
                    $" (High=${vwapRej.GetLastHigh():F2}, Close=${vwapRej.GetLastClose():F2})",
                RocAboveCondition roc when roc.GetRocValue != null =>
                    $" (ROC={roc.GetRocValue():F2}%)",
                RocBelowCondition roc when roc.GetRocValue != null =>
                    $" (ROC={roc.GetRocValue():F2}%)",
                _ => ""
            };
        }

        /// <summary>
        /// Formats the Higher Lows pattern info for logging.
        /// </summary>
        private string FormatHigherLowsInfo(HigherLowsCondition hl)
        {
            var lows = hl.GetRecentLows?.Invoke();
            if (lows == null || lows.Length < 2)
                return " (HigherLows: waiting for data)";

            var lowsStr = string.Join(" > ", lows.Select(l => $"${l:F2}"));
            return $" (Lows: {lowsStr})";
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

            var ibOrder = new Order
            {
                Action = order.GetIbAction(),
                OrderType = effectiveOrderType,
                TotalQuantity = order.Quantity,
                OutsideRth = GetEffectiveOutsideRth(order),
                Tif = order.GetIbTif(),
                AllOrNone = order.AllOrNone
            };

            // Set account if specified
            if (!string.IsNullOrEmpty(Settings.AccountNumber))
            {
                ibOrder.Account = Settings.AccountNumber;
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
                    // Use current price + offset for forced limit orders, or VWAP + offset for explicit limit
                    double basePrice = forceLimitOrder ? _lastPrice : vwap;
                    double offset = order.Side == OrderSide.Buy ? order.LimitOffset : -order.LimitOffset;
                    ibOrder.LmtPrice = Math.Round(basePrice + offset, 2);
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
                SessionLogger?.LogFill(_strategy.Symbol, "BUY", fillSize, fillPrice);
                SessionLogger?.UpdateStrategyStatus(_strategy.Symbol, "Position Open", $"Entry @ ${fillPrice:F2}");

                // Handle take profit
                if (_strategy.Order.Side == OrderSide.Buy && _strategy.Order.EnableTakeProfit)
                {
                    SubmitTakeProfit(fillPrice);
                }

                // Handle stop loss
                if (_strategy.Order.EnableStopLoss)
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
                        _originalStopLossPrice = fillPrice - _strategy.Order.StopLossOffset;
                        _currentAdaptiveStopLossPrice = _originalStopLossPrice;
                    }

                    Log($"ADAPTIVE ORDER ENABLED: TP=${_originalTakeProfitPrice:F2}, SL=${_originalStopLossPrice:F2} ({_strategy.Order.AdaptiveOrder!.Mode})", ConsoleColor.Cyan);
                }

                // Initialize trailing stop loss tracking
                if (_strategy.Order.EnableTrailingStopLoss)
                {
                    _highWaterMark = fillPrice;

                    // Calculate initial trailing stop - use ATR if configured, otherwise percentage
                    string stopDescription;
                    if (_strategy.Order.UseAtrStopLoss && _atrCalculator != null && _atrCalculator.IsReady)
                    {
                        var atrConfig = _strategy.Order.AtrStopLoss!;
                        _trailingStopLossPrice = _atrCalculator.CalculateStopPrice(
                            referencePrice: fillPrice,
                            multiplier: atrConfig.Multiplier,
                            isLong: _strategy.Order.Side == OrderSide.Buy,
                            minPercent: atrConfig.MinStopPercent,
                            maxPercent: atrConfig.MaxStopPercent
                        );
                        double atrValue = _atrCalculator.CurrentAtr;
                        double stopDistance = fillPrice - _trailingStopLossPrice;
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
                _isComplete = true;
                _exitFillPrice = fillPrice;

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

                // Cancel take profit if still active
                if (_takeProfitOrderId > 0 && !_takeProfitFilled && !_takeProfitCancelled)
                {
                    _client.cancelOrder(_takeProfitOrderId, new OrderCancel());
                    _takeProfitCancelled = true;
                }

                PrintFinalResult();
                return;
            }

            // Check for fixed stop loss order fill
            if (orderId == _stopLossOrderId && !_stopLossFilled)
            {
                _stopLossFilled = true;
                _isComplete = true;
                _exitFillPrice = fillPrice;

                double pnl = _strategy.Order.Quantity * (fillPrice - _entryFillPrice);
                _result = StrategyResult.StopLossFilled;

                Log($"*** STOP LOSS FILLED @ ${fillPrice:F2} | P&L: ${pnl:F2}", ConsoleColor.Red);
                SessionLogger?.LogFill(_strategy.Symbol, "SELL", _strategy.Order.Quantity, fillPrice, pnl);
                SessionLogger?.UpdateStrategyStatus(_strategy.Symbol, "Stop Loss", $"Exit @ ${fillPrice:F2} P&L=${pnl:F2}");

                // Cancel take profit if still active
                if (_takeProfitOrderId > 0 && !_takeProfitFilled && !_takeProfitCancelled)
                {
                    _client.cancelOrder(_takeProfitOrderId, new OrderCancel());
                    _takeProfitCancelled = true;
                }

                PrintFinalResult();
                return;
            }

            // Check for take profit order fill (or early exit fill)
            if (orderId == _takeProfitOrderId && !_takeProfitFilled)
            {
                _takeProfitFilled = true;
                _isComplete = true;
                _exitFillPrice = fillPrice;

                double pnl = _strategy.Order.Quantity * (fillPrice - _entryFillPrice);

                // Cancel stop loss if still active
                if (_stopLossOrderId > 0 && !_stopLossFilled)
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

                // Show final result
                PrintFinalResult();
            }
        }

        private void SubmitTakeProfit(double entryPrice)
        {
            var order = _strategy.Order;
            _takeProfitOrderId = _wrapper.ConsumeNextOrderId();

            double tpPrice = order.TakeProfitPrice.HasValue
                ? Math.Round(order.TakeProfitPrice.Value, 2)
                : Math.Round(entryPrice + order.TakeProfitOffset, 2);

            _takeProfitTarget = tpPrice;

            var tpOrder = new Order
            {
                Action = "SELL",
                OrderType = "LMT",
                TotalQuantity = order.Quantity,
                LmtPrice = tpPrice,
                OutsideRth = order.TakeProfitOutsideRth,
                Tif = order.GetIbTif()
            };

            // Set account if specified
            if (!string.IsNullOrEmpty(Settings.AccountNumber))
            {
                tpOrder.Account = Settings.AccountNumber;
            }

            Log($">> SUBMITTING TAKE PROFIT SELL {order.Quantity} @ ${tpPrice:F2}", ConsoleColor.Yellow);
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
            if (!string.IsNullOrEmpty(Settings.AccountNumber))
            {
                exitOrder.Account = Settings.AccountNumber;
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

            double slPrice = order.StopLossPrice.HasValue
                ? Math.Round(order.StopLossPrice.Value, 2)
                : Math.Round(entryPrice - order.StopLossOffset, 2);

            var slOrder = new Order
            {
                Action = order.Side == OrderSide.Buy ? "SELL" : "BUY",
                OrderType = "STP",
                TotalQuantity = order.Quantity,
                AuxPrice = slPrice,
                OutsideRth = order.OutsideRth,
                Tif = order.GetIbTif()
            };

            // Set account if specified
            if (!string.IsNullOrEmpty(Settings.AccountNumber))
            {
                slOrder.Account = Settings.AccountNumber;
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

            // Cancel any open take profit order
            if (_takeProfitOrderId > 0 && !_takeProfitFilled && !_takeProfitCancelled)
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
            if (!string.IsNullOrEmpty(Settings.AccountNumber))
            {
                closeOrder.Account = Settings.AccountNumber;
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
