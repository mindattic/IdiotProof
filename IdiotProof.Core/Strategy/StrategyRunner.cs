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
using IdiotProof.Calculators;
using IdiotProof.Helpers;
using IdiotProof.Logging;
using IdiotProof.Models;
using IdiotProof.Core.Models;
using IdiotProof.Services;
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

        private readonly TradingStrategy strategy;
        private readonly IbContract contract;
        private readonly IbWrapper wrapper;
        private readonly EClientSocket client;

        private int currentConditionIndex;
        private bool isComplete;
        private volatile bool disposed;

        // Lock for thread-safe timer callback handling
        private readonly object disposeLock = new object();

        // VWAP accumulators
        private double pvSum;
        private double vSum;
        private double lastPrice;

        // Bid/Ask tracking for PriceType support
        private double lastBid;
        private double lastAsk;

        // Session tracking
        private double sessionHigh;
        private double sessionLow = double.MaxValue;

        // Order tracking
        private int entryOrderId = -1;
        private bool entryFilled;
        private double entryFillPrice;

        // Take profit tracking
        private int takeProfitOrderId = -1;
        private bool takeProfitFilled;
        private bool takeProfitCancelled;
        private bool takeProfitOrderRejected;  // Order rejected by IBKR (never placed)
        private double takeProfitTarget;

        // Stop loss tracking
        private int stopLossOrderId = -1;
        private bool stopLossFilled;
        private bool stopLossOrderRejected;  // Order rejected by IBKR (never placed)

        // Exit tracking
        private bool exitedWithProfit;
        private double exitFillPrice;

        // Trailing stop loss tracking
        private int trailingStopLossOrderId = -1;
        private bool trailingStopLossTriggered;
        private double trailingStopLossPrice;
        private double highWaterMark;  // Highest price since entry (for trailing stop)

        // ATR calculator for volatility-based stops
        private Helpers.AtrCalculator? atrCalculator;

        // Candlestick aggregator for candle-based indicators
        private readonly Helpers.CandlestickAggregatorHelper candlestickAggregator;

        // EMA calculators for indicator conditions
        private readonly Dictionary<int, Helpers.EmaCalculator> emaCalculators = new();

        // ADX calculator for trend strength and DI conditions
        private Helpers.AdxCalculator? adxCalculator;

        // ADX rollover detection for dynamic TakeProfit
        private double adxPeakValue;
        private bool adxRolledOver;

        // RSI calculator for overbought/oversold conditions
        private Helpers.RsiCalculator? rsiCalculator;

        // MACD calculator for momentum conditions
        private Helpers.MacdCalculator? macdCalculator;

        // Momentum calculator for price momentum conditions
        private Helpers.MomentumCalculator? momentumCalculator;

        // ROC calculator for rate of change conditions
        private Helpers.RocCalculator? rocCalculator;

        // Volume calculator for volume spike conditions
        private Helpers.VolumeCalculator? volumeCalculator;

        // Bollinger Bands calculator for mean reversion signals
        private Helpers.BollingerBandsCalculator? bollingerBands;
        
        // Extended indicator calculators for comprehensive market scoring
        private Helpers.StochasticCalculator? stochasticCalculator;
        private Helpers.ObvCalculator? obvCalculator;
        private Helpers.CciCalculator? cciCalculator;
        private Helpers.WilliamsRCalculator? williamsRCalculator;
        
        // SMA calculators for trend confirmation and Golden Cross/Death Cross detection
        private Helpers.SmaCalculator? sma20Calculator;
        private Helpers.SmaCalculator? sma50Calculator;

        // Previous day levels tracker for S/R (PDH/PDL/PDC, pivot points, multi-day range)
        private Calculators.PreviousDayLevelsTracker? prevDayLevels;

        // Price action context for proactive multi-bar pattern analysis (FVG, pullback, extension detection)
        private readonly Helpers.PriceActionContext priceActionContext = new();

        // Proactive market scanner for forming patterns, momentum exhaustion, volume profile
        private readonly Helpers.ProactiveMarketScanner proactiveScanner = new();

        // Warm-up logging
        private bool warmupLoggedEma;
        private bool warmupLoggedAdx;
        private bool warmupLoggedRsi;
        private bool warmupLoggedMacd;
        private bool warmupLoggedMomentum;
        private bool warmupLoggedRoc;
        private bool warmupLoggedVolume;

        // Cancel timer
        private Timer? cancelTimer;

        // Overnight cancellation timer (for Overnight TIF orders)
        private Timer? overnightCancelTimer;

        // Close position timer (time-based exit)
        private Timer? closePositionTimer;
        private bool closePositionTriggered;
        private int closePositionOrderId = -1;

        // Adaptive order tracking
        private DateTime lastAdaptiveAdjustmentTime = DateTime.MinValue;
        private int lastAdaptiveScore;
        private double originalTakeProfitPrice;
        private double originalStopLossPrice;
        private double currentAdaptiveTakeProfitPrice;
        private double currentAdaptiveStopLossPrice;

        // Dynamic trading tracking
        private DateTime lastTradeTime = DateTime.MinValue;
        private int lastScore;
        private bool indicatorsReady;
        private int exitOrderId = -1;
        private bool isLong = true;  // Tracks position direction
        private double dynamicTakeProfit;   // Dynamic TP target
        private double dynamicStopLoss;     // Dynamic SL target
        private bool shortSaleBlocked;         // True if short sale was rejected for this ticker
        private double previousClose;          // Previous session close for gap detection
        
        // HOD Fade Strategy: Tracks when we shorted at HOD to cover at VWAP
        private bool isHodFadeShort;           // True if current short is from HOD fade strategy
        private double hodFadeVwapTarget;      // VWAP target for covering the HOD fade short

        // Learning system - tracks patterns and outcomes per ticker
        private static readonly TickerProfileManager profileManager = new();
        private TickerProfile? tickerProfile;
        private TradeRecord? pendingTradeRecord;
        private MarketScore? entryScore;

        private IdiotProof.Calculators.IndicatorSnapshot? lastIndicatorSnapshot;

        // AI Advisor - provides "third opinion" using ChatGPT analysis
        private IdiotProof.Learning.AIAdvisor? aiAdvisor;
        private IdiotProof.Learning.AIAnalysis? lastAiAnalysis;
        private DateTime lastAiAnalysisTime = DateTime.MinValue;
        private readonly TimeSpan aiAnalysisInterval = TimeSpan.FromMinutes(5);  // Rate limit AI calls


        // Historical metadata - provides insights about stock behavior
        private IdiotProof.Models.TickerMetadata? tickerMetadata;

        // Breakout-Pullback tracker - detects resistance-becomes-support patterns
        private Helpers.BreakoutPullbackTracker? breakoutTracker;
        
        // Trend Direction Filter - prevents buying clear downtrends / shorting clear uptrends
        private readonly Helpers.TrendDirectionFilter trendFilter = new();

        /// <summary>
        /// Gets the shared ticker profile manager for learning across sessions.
        /// </summary>
        public static TickerProfileManager ProfileManager => profileManager;

        /// <summary>
        /// Gets or sets the ticker metadata for informed trading decisions.
        /// </summary>
        public IdiotProof.Models.TickerMetadata? TickerMetadata
        {
            get => tickerMetadata;
            set => tickerMetadata = value;
        }

        // Result tracking
        private StrategyResult result = StrategyResult.Running;


        // Daily reset tracking
        private DateOnly lastCheckedDate;
        private bool waitingForWindowLogged;
        private bool windowEndedLogged;

        /// <summary>Gets the strategy being executed.</summary>
        public TradingStrategy Strategy => strategy;

        /// <summary>Gets the symbol being traded.</summary>
        public string Symbol => strategy.Symbol;

        /// <summary>Gets whether all conditions have been met and order executed.</summary>
        public bool IsComplete => isComplete;

        /// <summary>Gets the current step index (0-based).</summary>
        public int CurrentStep => currentConditionIndex;

        /// <summary>Gets the total number of conditions.</summary>
        public int TotalSteps => strategy.Conditions.Count;

        /// <summary>Gets whether the entry order has been filled.</summary>
        public bool EntryFilled => entryFilled;

        /// <summary>Gets the entry fill price.</summary>
        public double EntryFillPrice => entryFillPrice;

        /// <summary>Gets whether the take profit order has been filled.</summary>
        public bool TakeProfitFilled => takeProfitFilled;

        /// <summary>Gets the take profit target price.</summary>
        public double TakeProfitTarget => takeProfitTarget;

        /// <summary>Gets the current VWAP value.</summary>
        public double CurrentVwap => GetVwap();

        /// <summary>Gets the last traded price.</summary>
        public double LastPrice => lastPrice;

        /// <summary>Gets the final result of the strategy.</summary>
        public StrategyResult Result => result;

        // Effective quantity (auto-calculated based on price if UseAutoQuantity is true)
        private int effectiveQuantity;

        /// <summary>
        /// Gets the effective quantity to use for orders.
        /// If UseAutoQuantity is true, calculates based on price tier.
        /// Otherwise returns the explicitly specified quantity.
        /// </summary>
        private int GetEffectiveQuantity(double price)
        {
            // If we've already calculated it, use that
            if (effectiveQuantity > 0)
                return effectiveQuantity;

            // Check if we should auto-calculate
            if (strategy.Order.UseAutoQuantity)
            {
                effectiveQuantity = TradingDefaults.GetDefaultQuantityForPrice(price);
                double estimatedPosition = effectiveQuantity * price;
                Log($"[AUTO-QTY] ${price:F2} × 3 → {effectiveQuantity} shares (~${estimatedPosition:F2})", ConsoleColor.DarkGray);
            }
            else
            {
                effectiveQuantity = strategy.Order.Quantity;
            }

            return effectiveQuantity;
        }

        /// <summary>Gets the current condition name.</summary>
        public string CurrentConditionName =>
            currentConditionIndex < strategy.Conditions.Count
                ? strategy.Conditions[currentConditionIndex].Name
                : "Complete";

        /// <summary>
        /// Logs a timestamped message to both console and session log file.
        /// </summary>
        private void Log(string message, ConsoleColor? color = null, string category = "STRATEGY")
        {
            if (color.HasValue)
            {
                ConsoleLog.Write(strategy.Symbol, $"{message}", color.Value);
            }
            else
            {
                ConsoleLog.Strategy(strategy.Symbol, message);
            }
            SessionLogger?.LogEvent(category, $"[{strategy.Symbol}] {message}");
        }

        public StrategyRunner(TradingStrategy strategy, IbContract contract, IbWrapper wrapper, EClientSocket client)
        {
            strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
            contract = contract ?? throw new ArgumentNullException(nameof(contract));
            wrapper = wrapper ?? throw new ArgumentNullException(nameof(wrapper));
            client = client ?? throw new ArgumentNullException(nameof(client));

            currentConditionIndex = 0;
            lastCheckedDate = DateOnly.FromDateTime(DateTime.Today);

            // Initialize candlestick aggregator (1-minute candles, trimmed to MaxCandlesticks setting)
            candlestickAggregator = new Helpers.CandlestickAggregatorHelper(
                candleSizeMinutes: 1,
                maxCandles: AppSettings.MaxCandlesticks);
            candlestickAggregator.OnCandleComplete += OnCandleComplete;

            // Initialize ATR calculator if ATR-based stop loss is configured
            if (strategy.Order.UseAtrStopLoss && strategy.Order.AtrStopLoss != null)
            {
                atrCalculator = new Helpers.AtrCalculator(
                    period: strategy.Order.AtrStopLoss.Period,
                    ticksPerBar: 50 // Aggregate 50 ticks into one "bar" for ATR calculation
                );
            }

            // Initialize EMA and ADX calculators for any indicator conditions in the strategy
            InitializeIndicatorCalculators();

            // Initialize AI Advisor for ChatGPT-powered decision support
            aiAdvisor = new IdiotProof.Learning.AIAdvisor();
            if (aiAdvisor.IsConfigured)
            {
                Log($"[AI] OpenAI advisor configured for {contract.Symbol}", ConsoleColor.Magenta);
            }

            // Initialize Breakout-Pullback tracker from strategy rules
            breakoutTracker = new Helpers.BreakoutPullbackTracker(contract.Symbol);
            var strategyRules = StrategyRulesManager.Load();
            if (strategyRules.HasRulesFor(contract.Symbol))
            {
                breakoutTracker.LoadFromStrategyRule(strategyRules);
                if (breakoutTracker.HasLevels)
                {
                    Log($"[BREAKOUT] Tracking breakout ${breakoutTracker.BreakoutLevel:F2}, support ${breakoutTracker.SupportLevel:F2}", ConsoleColor.DarkCyan);
                }
            }

            // Initialize Previous Day Levels tracker for S/R analysis
            prevDayLevels = new Calculators.PreviousDayLevelsTracker(contract.Symbol);
            prevDayLevels.InitializeFromCache();
            if (prevDayLevels.HasData)
            {
                Log($"[S/R] {prevDayLevels.GetLevelsSummary()}", ConsoleColor.DarkCyan);
            }

            // Subscribe to fill events
            wrapper.OnOrderFill += OnOrderFill;

            // Subscribe to order rejection events to track failed orders
            wrapper.OnOrderRejected += OnOrderRejected;

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
            candlestickAggregator.SeedWithHistoricalData(candles, fireEvents: true);

            // Log warm-up results
            int candleCount = candlestickAggregator.CompletedCandleCount;
            Log($"  Loaded {candleCount} candles into aggregator", ConsoleColor.DarkGray);

            // Check indicator readiness
            int maxEmaPeriod = emaCalculators.Count > 0 ? emaCalculators.Keys.Max() : 0;
            if (maxEmaPeriod > 0)
            {
                bool emaReady = candleCount >= maxEmaPeriod;
                if (emaReady)
                {
                    warmupLoggedEma = true;
                    var emaValues = string.Join(", ", emaCalculators
                        .OrderBy(kvp => kvp.Key)
                        .Select(kvp => $"EMA({kvp.Key})=${kvp.Value.CurrentValue:F2}"));
                    Log($"  [OK] EMA warm-up complete: {emaValues}", ConsoleColor.Green);
                }
                else
                {
                    Log($"  [--] EMA needs {maxEmaPeriod} bars, only have {candleCount}", ConsoleColor.Yellow);
                }
            }

            if (adxCalculator != null)
            {
                if (adxCalculator.IsReady)
                {
                    warmupLoggedAdx = true;
                    Log($"  [OK] ADX warm-up complete: ADX={adxCalculator.CurrentAdx:F1}, +DI={adxCalculator.PlusDI:F1}, -DI={adxCalculator.MinusDI:F1}", ConsoleColor.Green);
                }
                else
                {
                    Log($"  [--] ADX needs 28 bars, only have {candleCount}", ConsoleColor.Yellow);
                }
            }

            if (rsiCalculator != null)
            {
                if (rsiCalculator.IsReady)
                {
                    warmupLoggedRsi = true;
                    Log($"  [OK] RSI warm-up complete: RSI={rsiCalculator.CurrentValue:F1}", ConsoleColor.Green);
                }
                else
                {
                    Log($"  [--] RSI needs 15 bars, only have {candleCount}", ConsoleColor.Yellow);
                }
            }

            if (macdCalculator != null)
            {
                if (macdCalculator.IsReady)
                {
                    warmupLoggedMacd = true;
                    Log($"  [OK] MACD warm-up complete: MACD={macdCalculator.MacdLine:F2}, Signal={macdCalculator.SignalLine:F2}", ConsoleColor.Green);
                }
                else
                {
                    Log($"  [--] MACD needs 35 bars, only have {candleCount}", ConsoleColor.Yellow);
                }
            }

            if (momentumCalculator != null)
            {
                if (momentumCalculator.IsReady)
                {
                    warmupLoggedMomentum = true;
                    Log($"  [OK] Momentum warm-up complete: Momentum={momentumCalculator.CurrentValue:F2}", ConsoleColor.Green);
                }
                else
                {
                    Log($"  [--] Momentum needs 11 bars, only have {candleCount}", ConsoleColor.Yellow);
                }
            }

            if (rocCalculator != null)
            {
                if (rocCalculator.IsReady)
                {
                    warmupLoggedRoc = true;
                    Log($"  [OK] ROC warm-up complete: ROC={rocCalculator.CurrentValue:F2}%", ConsoleColor.Green);
                }
                else
                {
                    Log($"  [--] ROC needs 11 bars, only have {candleCount}", ConsoleColor.Yellow);
                }
            }

            if (volumeCalculator != null)
            {
                if (volumeCalculator.IsReady)
                {
                    warmupLoggedVolume = true;
                    Log($"  [OK] Volume warm-up complete: Avg={volumeCalculator.AverageVolume:N0}", ConsoleColor.Green);
                }
                else
                {
                    Log($"  [--] Volume needs 20 bars, only have {candleCount}", ConsoleColor.Yellow);
                }
            }

            // Update last price from most recent bar
            if (candles.Count > 0)
            {
                lastPrice = candles[^1].Close;

                // Set previous close for gap conditions
                SetPreviousCloseForGapConditions(candles);
                
                // Seed previous day levels tracker from historical candles
                prevDayLevels?.SeedFromHistoricalCandles(candles);
                if (prevDayLevels?.HasData == true)
                {
                    Log($"  [OK] Previous day levels: {prevDayLevels.GetLevelsSummary()}", ConsoleColor.Green);
                }
            }

            // Log historical metadata insights if available
            if (tickerMetadata != null && tickerMetadata.DaysAnalyzed > 0)
            {
                Log($"  [OK] Historical metadata loaded: {tickerMetadata.DaysAnalyzed} days analyzed", ConsoleColor.Green);

                var de = tickerMetadata.DailyExtremes;
                if (de.HodInFirst30MinPercent > 40)
                    Log($"       HOD typically early ({de.HodInFirst30MinPercent:F0}% in first 30 min)", ConsoleColor.DarkGray);
                if (de.LodInFirst30MinPercent > 40)
                    Log($"       LOD typically early ({de.LodInFirst30MinPercent:F0}% in first 30 min)", ConsoleColor.DarkGray);
                if (tickerMetadata.SupportLevels.Count > 0)
                    Log($"       Key support: ${tickerMetadata.SupportLevels[0].Price:F2}", ConsoleColor.DarkGray);
                if (tickerMetadata.ResistanceLevels.Count > 0)
                    Log($"       Key resistance: ${tickerMetadata.ResistanceLevels[0].Price:F2}", ConsoleColor.DarkGray);
                if (tickerMetadata.BullishBias)
                    Log($"       Bullish bias ({tickerMetadata.VwapBehavior.AvgPercentAboveVwap:F0}% above VWAP avg)", ConsoleColor.DarkGray);
                else if (tickerMetadata.BearishBias)
                    Log($"       Bearish bias ({100 - tickerMetadata.VwapBehavior.AvgPercentAboveVwap:F0}% below VWAP avg)", ConsoleColor.DarkGray);
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
            this.previousClose = previousClose;
            Log($"Previous close set to ${previousClose:F2} from historical data", ConsoleColor.DarkGray);
        }

        /// <summary>
        /// Called when a new candlestick completes.
        /// Updates all candle-based indicators.
        /// </summary>
        private void OnCandleComplete(Candlestick candle)
        {
            // Thread-safe check - don't process if disposed
            if (disposed)
                return;

            try
            {
                // Log candle completion periodically for warm-up visibility
                int candleCount = candlestickAggregator.CompletedCandleCount;

                // Update EMA calculators with candle close price
                foreach (var emaCalc in emaCalculators.Values)
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
                    adxCalculator?.UpdateFromCandle(candle.High, candle.Low, candle.Close);
                }
                catch (Exception ex)
                {
                    Log($"WARNING: ADX calculator update failed: {ex.Message}", ConsoleColor.Yellow);
                }

                // Update RSI calculator with candle close price
                try
                {
                    rsiCalculator?.Update(candle.Close);
                }
                catch (Exception ex)
                {
                    Log($"WARNING: RSI calculator update failed: {ex.Message}", ConsoleColor.Yellow);
                }

                // Update MACD calculator with candle close price
                try
                {
                    macdCalculator?.Update(candle.Close);
                }
                catch (Exception ex)
                {
                    Log($"WARNING: MACD calculator update failed: {ex.Message}", ConsoleColor.Yellow);
                }

                // Update Momentum calculator with candle close price
                try
                {
                    momentumCalculator?.Update(candle.Close);
                }
                catch (Exception ex)
                {
                    Log($"WARNING: Momentum calculator update failed: {ex.Message}", ConsoleColor.Yellow);
                }

                // Update ROC calculator with candle close price
                try
                {
                    rocCalculator?.Update(candle.Close);
                }
                catch (Exception ex)
                {
                    Log($"WARNING: ROC calculator update failed: {ex.Message}", ConsoleColor.Yellow);
                }

                // Update Volume calculator with candle volume
                try
                {
                    volumeCalculator?.Update(candle.Volume);
                }
                catch (Exception ex)
                {
                    Log($"WARNING: Volume calculator update failed: {ex.Message}", ConsoleColor.Yellow);
                }

                // Update Bollinger Bands with candle close price
                try
                {
                    bollingerBands?.Update(candle.Close);
                }
                catch (Exception ex)
                {
                    Log($"WARNING: Bollinger Bands calculator update failed: {ex.Message}", ConsoleColor.Yellow);
                }

                // Update extended indicator calculators
                try
                {
                    stochasticCalculator?.Update(candle.High, candle.Low, candle.Close);
                    obvCalculator?.Update(candle.Close, candle.Volume);
                    cciCalculator?.Update(candle.High, candle.Low, candle.Close);
                    williamsRCalculator?.Update(candle.High, candle.Low, candle.Close);
                    sma20Calculator?.Update(candle.Close);
                    sma50Calculator?.Update(candle.Close);
                    prevDayLevels?.Update(candle);
                }
                catch (Exception ex)
                {
                    Log($"WARNING: Extended indicator update failed: {ex.Message}", ConsoleColor.Yellow);
                }

                // Update breakout-pullback tracker with candle close (new bar)
                try
                {
                    if (breakoutTracker != null && breakoutTracker.HasLevels)
                    {
                        var result = breakoutTracker.Update(candle.Close, isNewBar: true);
                        
                        // Log significant state changes
                        if (result.State == Helpers.BreakoutState.BrokeOut && result.ScoreAdjustment < 0)
                        {
                            Log($"[BREAKOUT] New bar: Broke ${breakoutTracker.BreakoutLevel:F2} - waiting for pullback", ConsoleColor.Yellow);
                        }
                        else if (result.IsIdealEntry)
                        {
                            Log($"[BREAKOUT] *** PULLBACK CONFIRMED *** Bouncing from ${breakoutTracker.SupportLevel:F2} support", ConsoleColor.Green);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log($"WARNING: Breakout tracker update failed: {ex.Message}", ConsoleColor.Yellow);
                }
                
                // Update trend direction filter with candle data
                try
                {
                    var vwap = vSum > 0 ? pvSum / vSum : candle.Close;
                    double ema9 = emaCalculators.TryGetValue(9, out var e9) && e9.IsReady ? e9.CurrentValue : 0;
                    double ema21 = emaCalculators.TryGetValue(21, out var e21) && e21.IsReady ? e21.CurrentValue : 0;
                    double ema50 = emaCalculators.TryGetValue(50, out var e50) && e50.IsReady ? e50.CurrentValue : 0;
                    double adx = adxCalculator?.CurrentAdx ?? 0;
                    double plusDi = adxCalculator?.PlusDI ?? 0;
                    double minusDi = adxCalculator?.MinusDI ?? 0;
                    
                    trendFilter.Update(candle.Close, vwap, ema9, ema21, ema50, adx, plusDi, minusDi, candle.High, candle.Low);
                }
                catch (Exception ex)
                {
                    Log($"WARNING: Trend filter update failed: {ex.Message}", ConsoleColor.Yellow);
                }

                // Update Price Action Context with new candle for proactive multi-bar pattern analysis
                // This tracks FVGs, swing points, consolidations, extension detection, and pullback quality
                try
                {
                    double ema9 = emaCalculators.TryGetValue(9, out var e9) && e9.IsReady ? e9.CurrentValue : 0;
                    double ema21 = emaCalculators.TryGetValue(21, out var e21) && e21.IsReady ? e21.CurrentValue : 0;
                    double atr = atrCalculator?.IsReady == true ? atrCalculator.CurrentAtr : 0;
                    double rsi = rsiCalculator?.IsReady == true ? rsiCalculator.CurrentValue : 50;
                    double macd = macdCalculator?.IsReady == true ? macdCalculator.MacdLine : 0;
                    
                    priceActionContext.Update(candle, ema9, ema21, atr, rsi, macd);
                }
                catch (Exception ex)
                {
                    Log($"WARNING: PriceAction context update failed: {ex.Message}", ConsoleColor.Yellow);
                }

                // Update Proactive Market Scanner with forming patterns, momentum exhaustion, volume profile
                try
                {
                    double ema9 = emaCalculators.TryGetValue(9, out var e9) && e9.IsReady ? e9.CurrentValue : 0;
                    double ema21 = emaCalculators.TryGetValue(21, out var e21) && e21.IsReady ? e21.CurrentValue : 0;
                    double ema50 = emaCalculators.TryGetValue(50, out var e50) && e50.IsReady ? e50.CurrentValue : 0;
                    double rsi = rsiCalculator?.IsReady == true ? rsiCalculator.CurrentValue : 50;
                    double macd = macdCalculator?.IsReady == true ? macdCalculator.MacdLine : 0;
                    double macdSignal = macdCalculator?.IsReady == true ? macdCalculator.SignalLine : 0;
                    double macdHist = macdCalculator?.IsReady == true ? macdCalculator.Histogram : 0;
                    double adx = adxCalculator?.IsReady == true ? adxCalculator.CurrentAdx : 20;
                    double plusDi = adxCalculator?.IsReady == true ? adxCalculator.PlusDI : 0;
                    double minusDi = adxCalculator?.IsReady == true ? adxCalculator.MinusDI : 0;
                    double atr = atrCalculator?.IsReady == true ? atrCalculator.CurrentAtr : 0;
                    
                    proactiveScanner.Update(candle, ema9, ema21, ema50, rsi, macd, macdSignal, macdHist, adx, plusDi, minusDi, atr);
                }
                catch (Exception ex)
                {
                    Log($"WARNING: ProactiveScanner update failed: {ex.Message}", ConsoleColor.Yellow);
                }

                // Log warm-up progress
                if (!warmupLoggedEma && emaCalculators.Count > 0)
                {
                    int maxPeriod = emaCalculators.Keys.Max();
                    if (candleCount >= maxPeriod)
                    {
                        warmupLoggedEma = true;
                        Log($"[OK] EMA warm-up complete ({candleCount} candles collected)", ConsoleColor.Green);
                    }
                    else if (candleCount % 5 == 0) // Log every 5 candles during warm-up
                    {
                        Log($"Warming up EMA: {candleCount}/{maxPeriod} candles...", ConsoleColor.DarkGray);
                    }
                }

                if (!warmupLoggedAdx && adxCalculator != null)
                {
                    if (adxCalculator.IsReady)
                    {
                        warmupLoggedAdx = true;
                        Log($"[OK] ADX warm-up complete (ADX={adxCalculator.CurrentAdx:F1})", ConsoleColor.Green);
                    }
                    else if (candleCount % 5 == 0 && candleCount <= 28)
                    {
                        Log($"Warming up ADX: {candleCount}/28 candles...", ConsoleColor.DarkGray);
                    }
                }

                // RSI warm-up logging
                if (!warmupLoggedRsi && rsiCalculator != null)
                {
                    if (rsiCalculator.IsReady)
                    {
                        warmupLoggedRsi = true;
                        Log($"[OK] RSI warm-up complete (RSI={rsiCalculator.CurrentValue:F1})", ConsoleColor.Green);
                    }
                    else if (candleCount % 5 == 0 && candleCount <= 15)
                    {
                        Log($"Warming up RSI: {candleCount}/15 candles...", ConsoleColor.DarkGray);
                    }
                }

                // MACD warm-up logging
                if (!warmupLoggedMacd && macdCalculator != null)
                {
                    if (macdCalculator.IsReady)
                    {
                        warmupLoggedMacd = true;
                        Log($"[OK] MACD warm-up complete (MACD={macdCalculator.MacdLine:F2}, Signal={macdCalculator.SignalLine:F2})", ConsoleColor.Green);
                    }
                    else if (candleCount % 5 == 0 && candleCount <= 35)
                    {
                        Log($"Warming up MACD: {candleCount}/35 candles...", ConsoleColor.DarkGray);
                    }
                }

                // Momentum warm-up logging
                if (!warmupLoggedMomentum && momentumCalculator != null)
                {
                    if (momentumCalculator.IsReady)
                    {
                        warmupLoggedMomentum = true;
                        Log($"[OK] Momentum warm-up complete (Momentum={momentumCalculator.CurrentValue:F2})", ConsoleColor.Green);
                    }
                    else if (candleCount % 5 == 0 && candleCount <= 11)
                    {
                        Log($"Warming up Momentum: {candleCount}/11 candles...", ConsoleColor.DarkGray);
                    }
                }

                // ROC warm-up logging
                if (!warmupLoggedRoc && rocCalculator != null)
                {
                    if (rocCalculator.IsReady)
                    {
                        warmupLoggedRoc = true;
                        Log($"[OK] ROC warm-up complete (ROC={rocCalculator.CurrentValue:F2}%)", ConsoleColor.Green);
                    }
                    else if (candleCount % 5 == 0 && candleCount <= 11)
                    {
                        Log($"Warming up ROC: {candleCount}/11 candles...", ConsoleColor.DarkGray);
                    }
                }

                // Volume warm-up logging
                if (!warmupLoggedVolume && volumeCalculator != null)
                {
                    if (volumeCalculator.IsReady)
                    {
                        warmupLoggedVolume = true;
                        Log($"[OK] Volume warm-up complete (Avg={volumeCalculator.AverageVolume:N0})", ConsoleColor.Green);
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
            
            // EMAs - required for VWAP/EMA scoring (including EMA 34 for new trading rules)
            GetOrCreateEmaCalculator(9);
            GetOrCreateEmaCalculator(21);
            GetOrCreateEmaCalculator(34);  // PRIMARY: EMA 34 is the key decision level
            GetOrCreateEmaCalculator(50);
            
            // RSI - for overbought/oversold detection
            rsiCalculator ??= new Helpers.RsiCalculator(period: 14);
            
            // MACD - for momentum
            macdCalculator ??= new Helpers.MacdCalculator(12, 26, 9);
            
            // ADX/DI - for trend strength and direction
            adxCalculator ??= new Helpers.AdxCalculator(period: 14, ticksPerBar: 50);
            
            // Volume - for confirmation signals
            volumeCalculator ??= new Helpers.VolumeCalculator(period: 20);
            
            // Bollinger Bands - for mean reversion
            bollingerBands ??= new Helpers.BollingerBandsCalculator(period: 20, multiplier: 2.0);
            
            // ATR - for volatility and TP/SL sizing
            if (atrCalculator == null && strategy.Order.UseAtrStopLoss == false)
            {
                atrCalculator = new Helpers.AtrCalculator(period: 14, ticksPerBar: 50);
            }
            
            // Momentum and ROC - CRITICAL: these were missing before!
            momentumCalculator ??= new Helpers.MomentumCalculator(period: 10);
            rocCalculator ??= new Helpers.RocCalculator(period: 10);
            
            // Extended indicators for comprehensive market scoring
            stochasticCalculator ??= new Helpers.StochasticCalculator(kPeriod: 14, dPeriod: 3);
            obvCalculator ??= new Helpers.ObvCalculator(emaPeriod: 20);
            cciCalculator ??= new Helpers.CciCalculator(period: 20);
            williamsRCalculator ??= new Helpers.WilliamsRCalculator(period: 14);
            
            // SMA - for trend confirmation and crossover signals
            sma20Calculator ??= new Helpers.SmaCalculator(period: 20);
            sma50Calculator ??= new Helpers.SmaCalculator(period: 50);
            
            Log("[AI] All required calculators initialized for AI scoring", ConsoleColor.DarkMagenta);
        }

        /// <summary>
        /// Initializes all indicator calculators for the strategy.
        /// For AutonomousTrading, initializes all indicators for market score calculation.
        /// </summary>
        private void InitializeIndicatorCalculators()
        {
            // Skip condition-based initialization - AutonomousTrading uses market score

            if (emaCalculators.Count > 0)
            {
                var periods = string.Join(", ", emaCalculators.Keys.OrderBy(k => k));
                Log($"EMA tracking enabled for periods: {periods}", ConsoleColor.DarkCyan);
            }

            if (adxCalculator != null)
            {
                Log($"ADX/DI tracking enabled (14-period)", ConsoleColor.DarkCyan);
            }

            if (rsiCalculator != null)
            {
                Log($"RSI tracking enabled (14-period)", ConsoleColor.DarkCyan);
            }

            if (macdCalculator != null)
            {
                Log($"MACD tracking enabled (12,26,9)", ConsoleColor.DarkCyan);
            }

            if (momentumCalculator != null)
            {
                Log($"Momentum tracking enabled (10-period)", ConsoleColor.DarkCyan);
            }

            if (rocCalculator != null)
            {
                Log($"ROC tracking enabled (10-period)", ConsoleColor.DarkCyan);
            }

            if (volumeCalculator != null)
            {
                Log($"Volume tracking enabled (20-period average)", ConsoleColor.DarkCyan);
            }

            // Initialize all indicators for autonomous trading or adaptive order
            // These modes need to calculate market score, which requires all indicators
            if (strategy.Order.UseAutonomousTrading || strategy.Order.UseAdaptiveOrder)
            {
                InitializeAllIndicatorsForMarketScore();
            }

            // Load ticker profile for learning system
            if (strategy.Order.UseAutonomousTrading)
            {
                tickerProfile = profileManager.GetProfile(strategy.Symbol);
                if (tickerProfile.TotalTrades > 0)
                {
                    Log($"Loaded ticker profile: {tickerProfile.GetSummary()}", ConsoleColor.DarkCyan);
                }
            }
        }

        /// <summary>
        /// Initializes all indicators required for market score calculation.
        /// Called when autonomous trading or adaptive order is enabled.
        /// </summary>
        private void InitializeAllIndicatorsForMarketScore()
        {
            // Initialize EMA calculators for market score (9, 21, 34, 50)
            // EMA 34 is the PRIMARY decision level for new trading rules
            GetOrCreateEmaCalculator(9);
            GetOrCreateEmaCalculator(21);
            GetOrCreateEmaCalculator(34);  // PRIMARY: Key decision level
            GetOrCreateEmaCalculator(50);
            Log($"Initialized EMA(9,21,34,50) for market score calculation", ConsoleColor.DarkGray);

            // Initialize ADX/DI calculator
            adxCalculator ??= new Helpers.AdxCalculator(period: 14, ticksPerBar: 50);
            Log($"Initialized ADX(14) for market score calculation", ConsoleColor.DarkGray);

            // Initialize RSI calculator
            rsiCalculator ??= new Helpers.RsiCalculator(period: 14);
            Log($"Initialized RSI(14) for market score calculation", ConsoleColor.DarkGray);

            // Initialize MACD calculator
            macdCalculator ??= new Helpers.MacdCalculator(12, 26, 9);
            Log($"Initialized MACD(12,26,9) for market score calculation", ConsoleColor.DarkGray);

            // Initialize Volume calculator
            volumeCalculator ??= new Helpers.VolumeCalculator(period: 20);
            Log($"Initialized Volume(20) for market score calculation", ConsoleColor.DarkGray);

            // Initialize Bollinger Bands calculator for mean reversion signals
            bollingerBands ??= new Helpers.BollingerBandsCalculator(period: 20, multiplier: 2.0);
            Log($"Initialized Bollinger Bands(20, 2.0) for mean reversion analysis", ConsoleColor.DarkGray);

            // Initialize ATR calculator for autonomous TP/SL calculation
            if (strategy.Order.UseAutonomousTrading && atrCalculator == null)
            {
                atrCalculator = new Helpers.AtrCalculator(period: 14, ticksPerBar: 50);
                Log($"Initialized ATR(14) for autonomous TP/SL calculation", ConsoleColor.DarkGray);
            }

            // Initialize extended indicators for comprehensive market scoring
            momentumCalculator ??= new Helpers.MomentumCalculator(period: 10);
            rocCalculator ??= new Helpers.RocCalculator(period: 10);
            stochasticCalculator ??= new Helpers.StochasticCalculator(kPeriod: 14, dPeriod: 3);
            obvCalculator ??= new Helpers.ObvCalculator(emaPeriod: 20);
            cciCalculator ??= new Helpers.CciCalculator(period: 20);
            williamsRCalculator ??= new Helpers.WilliamsRCalculator(period: 14);
            sma20Calculator ??= new Helpers.SmaCalculator(period: 20);
            sma50Calculator ??= new Helpers.SmaCalculator(period: 50);
            Log($"Initialized extended indicators (Stochastic, OBV, CCI, W%R, SMA, Momentum, ROC)", ConsoleColor.DarkGray);
        }

        /// <summary>
        /// Gets an existing EMA calculator for the period, or creates a new one.
        /// </summary>
        private Helpers.EmaCalculator GetOrCreateEmaCalculator(int period)
        {
            if (!emaCalculators.TryGetValue(period, out var calculator))
            {
                calculator = new Helpers.EmaCalculator(period);
                emaCalculators[period] = calculator;
            }
            return calculator;
        }

        /// <summary>
        /// Call this method when bid/ask prices are updated.
        /// </summary>
        public void OnBidAskUpdate(double bid, double ask)
        {
            if (disposed)
                return;

            if (bid > 0)
                lastBid = bid;
            if (ask > 0)
                lastAsk = ask;
        }

        /// <summary>
        /// Call this method when a new trade tick is received.
        /// </summary>
        public void OnLastTrade(double lastPrice, int lastSize)
        {
            if (disposed)
                return;

            this.lastPrice = lastPrice;

            // Ignore invalid data
            if (lastPrice <= 0 || lastSize <= 0)
                return;

            // Update session high/low
            if (lastPrice > sessionHigh)
                sessionHigh = lastPrice;
            if (lastPrice < sessionLow)
                sessionLow = lastPrice;

            // Update VWAP: sum(price * size) / sum(size)
            pvSum += lastPrice * lastSize;
            vSum += lastSize;

            // Update ATR calculator if configured
            atrCalculator?.Update(lastPrice);

            // Update candlestick aggregator - this will trigger OnCandleComplete when a candle closes
            // OnCandleComplete updates EMA and ADX calculators with the candle close price
            candlestickAggregator.Update(lastPrice, lastSize);

            double vwap = GetVwap();

            // Monitor trailing stop loss if position is open
            if (entryFilled && !isComplete)
            {
                MonitorTrailingStopLoss(lastPrice);
                MonitorAdaptiveOrder(lastPrice, vwap);
                MonitorAdxRollover(lastPrice);
            }

            // For autonomous trading, monitor indicators for entry/exit decisions
            if (strategy.Order.UseAutonomousTrading)
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
            var order = strategy.Order;

            // Check global setting first, then order-level setting
            if (!AppSettings.UseTrailingStopLoss || !order.EnableTrailingStopLoss || trailingStopLossTriggered)
                return;

            bool isLong = order.Side == OrderSide.Buy;

            // Update high water mark for long positions
            if (isLong)
            {
                if (currentPrice > highWaterMark)
                {
                    highWaterMark = currentPrice;

                    // Calculate new trailing stop price based on ATR or percentage
                    double newStopPrice;
                    string stopDescription;

                    if (order.UseAtrStopLoss && atrCalculator != null && atrCalculator.IsReady)
                    {
                        // ATR-based trailing stop
                        var atrConfig = order.AtrStopLoss!;
                        newStopPrice = atrCalculator.CalculateStopPrice(
                            referencePrice: highWaterMark,
                            multiplier: atrConfig.Multiplier,
                            isLong: true,
                            minPercent: atrConfig.MinStopPercent,
                            maxPercent: atrConfig.MaxStopPercent
                        );
                        double atrValue = atrCalculator.CurrentAtr;
                        double stopDistance = highWaterMark - newStopPrice;
                        stopDescription = $"{atrConfig.Multiplier:F1}× ATR (ATR=${atrValue:F2}, Distance=${stopDistance:F2})";
                    }
                    else
                    {
                        // Percentage-based trailing stop (fallback or explicit)
                        newStopPrice = Math.Round(highWaterMark * (1 - order.TrailingStopLossPercent), 2);
                        stopDescription = $"{order.TrailingStopLossPercent * 100:F1}% below ${highWaterMark:F2}";
                    }

                    // Only update if the new stop is higher (tighter)
                    if (newStopPrice > trailingStopLossPrice)
                    {
                        double oldStop = trailingStopLossPrice;
                        trailingStopLossPrice = newStopPrice;

                        if (oldStop > 0)
                        {
                            Log($"TRAILING STOP UPDATED: ${oldStop:F2} -> ${trailingStopLossPrice:F2} (High: ${highWaterMark:F2})", ConsoleColor.Magenta);
                        }
                        else
                        {
                            Log($"TRAILING STOP SET: ${trailingStopLossPrice:F2} ({stopDescription})", ConsoleColor.Magenta);
                        }
                    }
                }

                // Check if current price dropped below trailing stop
                if (trailingStopLossPrice > 0 && currentPrice <= trailingStopLossPrice)
                {
                    Log($"*** TRAILING STOP TRIGGERED! Price ${currentPrice:F2} <= Stop ${trailingStopLossPrice:F2}", ConsoleColor.Red);
                    ExecuteTrailingStopLoss();
                }
            }
            else
            {
                // For short positions, track low water mark (lowest price = best for short)
                // Initialize low water mark if not set
                if (highWaterMark == 0 || currentPrice < highWaterMark)
                {
                    highWaterMark = currentPrice; // Reusing field - for shorts this is "low water mark"

                    // Calculate new trailing stop price based on ATR or percentage
                    double newStopPrice;
                    string stopDescription;

                    if (order.UseAtrStopLoss && atrCalculator != null && atrCalculator.IsReady)
                    {
                        // ATR-based trailing stop for shorts
                        var atrConfig = order.AtrStopLoss!;
                        newStopPrice = atrCalculator.CalculateStopPrice(
                            referencePrice: highWaterMark,
                            multiplier: atrConfig.Multiplier,
                            isLong: false,
                            minPercent: atrConfig.MinStopPercent,
                            maxPercent: atrConfig.MaxStopPercent
                        );
                        double atrValue = atrCalculator.CurrentAtr;
                        double stopDistance = newStopPrice - highWaterMark;
                        stopDescription = $"{atrConfig.Multiplier:F1}× ATR (ATR=${atrValue:F2}, Distance=${stopDistance:F2})";
                    }
                    else
                    {
                        // Percentage-based trailing stop for shorts (stop ABOVE low water mark)
                        newStopPrice = Math.Round(highWaterMark * (1 + order.TrailingStopLossPercent), 2);
                        stopDescription = $"{order.TrailingStopLossPercent * 100:F1}% above ${highWaterMark:F2}";
                    }

                    // Only update if the new stop is lower (tighter for shorts)
                    if (trailingStopLossPrice == 0 || newStopPrice < trailingStopLossPrice)
                    {
                        double oldStop = trailingStopLossPrice;
                        trailingStopLossPrice = newStopPrice;

                        if (oldStop > 0)
                        {
                            Log($"TRAILING STOP UPDATED: ${oldStop:F2} -> ${trailingStopLossPrice:F2} (Low: ${highWaterMark:F2})", ConsoleColor.Magenta);
                        }
                        else
                        {
                            Log($"TRAILING STOP SET: ${trailingStopLossPrice:F2} ({stopDescription})", ConsoleColor.Magenta);
                        }
                    }
                }

                // Check if current price rose above trailing stop (bad for shorts)
                if (trailingStopLossPrice > 0 && currentPrice >= trailingStopLossPrice)
                {
                    Log($"*** TRAILING STOP TRIGGERED! Price ${currentPrice:F2} >= Stop ${trailingStopLossPrice:F2}", ConsoleColor.Red);
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
            if (trailingStopLossTriggered)
                return;

            trailingStopLossTriggered = true;
            var order = strategy.Order;

            // Cancel any existing take profit order (only if it wasn't rejected)
            if (takeProfitOrderId > 0 && !takeProfitFilled && !takeProfitCancelled && !takeProfitOrderRejected)
            {
                Log($"Cancelling take profit order #{takeProfitOrderId}...", ConsoleColor.Yellow);
                client.cancelOrder(takeProfitOrderId, new OrderCancel());
                takeProfitCancelled = true;
            }

            // Submit stop loss order
            trailingStopLossOrderId = wrapper.ConsumeNextOrderId();

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
                    ? Math.Round(Math.Min(lastPrice, trailingStopLossPrice) - offset, 2)  // Selling: slightly below current/stop
                    : Math.Round(Math.Max(lastPrice, trailingStopLossPrice) + offset, 2); // Covering short: slightly above

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
            Log($"  OrderId={trailingStopLossOrderId} | Triggered at ${lastPrice:F2} | Stop Level: ${trailingStopLossPrice:F2}", ConsoleColor.DarkGray);

            client.placeOrder(trailingStopLossOrderId, contract, stopOrder);
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
            var order = strategy.Order;
            var config = order.AutonomousTrading;

            if (config == null) return;

            // Check if we're within the time window
            var currentTimeET = TimezoneHelper.GetCurrentTime(MarketTimeZone.EST);
            
            // If StartTime is set and we haven't reached it yet, don't trade
            if (strategy.StartTime.HasValue && currentTimeET < strategy.StartTime.Value)
                return;

            // If EndTime is set and we've passed it, don't trade
            if (strategy.EndTime.HasValue && currentTimeET > strategy.EndTime.Value)
                return;

            // Check if indicators are ready for market score calculation
            if (!AreIndicatorsReadyForAutonomous())
            {
                if (!indicatorsReady)
                {
                    // Log once that we're waiting for warm-up
                    return;
                }
            }
            else if (!indicatorsReady)
            {
                indicatorsReady = true;
                Log($"[OK] Autonomous trading indicators ready - monitoring for signals", ConsoleColor.Green);
            }

            // Rate limiting - don't trade too frequently
            var now = DateTime.UtcNow;
            var timeSinceLastTrade = (now - lastTradeTime).TotalSeconds;
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
            int scoreDelta = Math.Abs(score.TotalScore - lastScore);
            if (scoreDelta < config.MinScoreChangeForTrade && lastScore != 0)
                return;

            lastScore = score.TotalScore;

            // Entry/Exit logic based on position state
            if (!entryFilled)
            {
                // No position - look for entry signals
                HandleAutonomousEntry(currentPrice, vwap, score, config);
            }
            else if (!isComplete)
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
            bool emaReady = emaCalculators.Count > 0 && emaCalculators.Values.All(e => e.IsReady);
            bool adxReady = adxCalculator?.IsReady ?? false;
            bool rsiReady = rsiCalculator?.IsReady ?? false;
            bool macdReady = macdCalculator?.IsReady ?? false;

            return emaReady && adxReady && rsiReady && macdReady;
        }

        /// <summary>
        /// Checks if we should exit a LONG position early because the ticker typically 
        /// makes its high of day (HOD) in the first 30 minutes of RTH.
        /// This captures gains before the typical fade pattern.
        /// </summary>
        /// <param name="currentPrice">Current price</param>
        /// <returns>True if we should exit early at HOD</returns>
        private bool ShouldExitAtEarlyHod(double currentPrice)
        {
            // Only check if we have metadata that says HOD typically occurs early
            if (tickerMetadata == null || !tickerMetadata.HodTypicallyEarly)
                return false;
            
            // Check if we're within the first 30 minutes of RTH (9:30-10:00 AM ET)
            var now = TimezoneHelper.GetCurrentTime(MarketTimeZone.EST);
            var rthOpen = new TimeOnly(9, 30);
            var earlyPeriodEnd = new TimeOnly(10, 0);
            
            if (now < rthOpen || now > earlyPeriodEnd)
                return false;
            
            // Check if price is near the session high
            if (sessionHigh <= 0 || sessionLow <= 0 || sessionLow >= double.MaxValue)
                return false;
            
            double range = sessionHigh - sessionLow;
            if (range <= 0)
                return false;
            
            // Price must be within 3% of session high AND above VWAP for strong confirmation
            bool nearHod = currentPrice >= sessionHigh - (range * 0.03);
            bool aboveVwap = currentPrice > GetVwap();
            
            // Must be in profit to trigger early exit
            bool inProfit = currentPrice > entryFillPrice;
            
            // Signal strength: RSI should indicate overbought territory
            bool rsiOverbought = rsiCalculator?.CurrentValue >= 65;
            
            if (nearHod && aboveVwap && inProfit)
            {
                // Additional confirmation: RSI overbought OR significant profit
                double profitPercent = (entryFillPrice > 0) ? (currentPrice - entryFillPrice) / entryFillPrice * 100 : 0;
                bool significantProfit = profitPercent >= 0.5; // 0.5% or more
                
                if (rsiOverbought || significantProfit)
                {
                    Log($"[HOD] Early exit conditions met: Near HOD={nearHod}, Above VWAP={aboveVwap}, RSI={rsiCalculator?.CurrentValue:F1}, Profit={profitPercent:+0.00;-0.00}%", ConsoleColor.Yellow);
                    return true;
                }
            }
            
            return false;
        }

        /// <summary>
        /// Determines if we should cover a HOD fade short position at VWAP.
        /// Called when we're short from a HOD fade pattern and watching for VWAP reversion.
        /// </summary>
        private bool ShouldCoverHodFadeShortAtVwap(double currentPrice, double vwap)
        {
            // Must be in a HOD fade short
            if (!isHodFadeShort)
                return false;
            
            // Price should be at or below VWAP
            if (currentPrice > vwap * 1.002) // Allow 0.2% above VWAP for slippage
                return false;
            
            // Check RSI for oversold bounce potential
            bool rsiOversold = rsiCalculator?.CurrentValue <= 40;
            
            // Price is at/below VWAP - good cover point
            bool atVwapTarget = currentPrice <= vwap;
            
            // Calculate profit on the short
            double shortProfit = entryFillPrice - currentPrice;
            double profitPercent = (entryFillPrice > 0) ? shortProfit / entryFillPrice * 100 : 0;
            
            // Cover conditions:
            // 1. Price at/below VWAP (primary target)
            // 2. OR RSI oversold (reversal likely)
            // 3. OR profit >= 0.5% (don't be greedy)
            if (atVwapTarget || rsiOversold || profitPercent >= 0.5)
            {
                Log($"[HOD FADE] Cover conditions met: AtVWAP={atVwapTarget}, RSI={rsiCalculator?.CurrentValue:F1}, ShortProfit={profitPercent:+0.00;-0.00}%", ConsoleColor.Cyan);
                return true;
            }
            
            return false;
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
            lastIndicatorSnapshot = snapshot;
            
            double timeWeight = GetTimeOfDayWeight();
            int adjustedScore;
            int vwapScore, emaScore, rsiScore, macdScore, adxScore, volumeScore, bollingerScore;
            
            var weights = IndicatorConfigManager.GetWeights();
            
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
                ShouldEmergencyExit = false
            };
        }
        
        /// <summary>
        /// Builds an indicator snapshot from live calculator values.
        /// </summary>
        private IdiotProof.Calculators.IndicatorSnapshot BuildIndicatorSnapshot(double price, double vwap)
        {
            // Get EMA values (including EMA 34 for new trading rules)
            double ema9 = 0, ema21 = 0, ema34 = 0, ema50 = 0;
            if (emaCalculators.TryGetValue(9, out var ema9Calc) && ema9Calc.IsReady)
                ema9 = ema9Calc.CurrentValue;
            if (emaCalculators.TryGetValue(21, out var ema21Calc) && ema21Calc.IsReady)
                ema21 = ema21Calc.CurrentValue;
            if (emaCalculators.TryGetValue(34, out var ema34Calc) && ema34Calc.IsReady)
                ema34 = ema34Calc.CurrentValue;
            if (emaCalculators.TryGetValue(50, out var ema50Calc) && ema50Calc.IsReady)
                ema50 = ema50Calc.CurrentValue;
            
            // Get RSI
            double rsi = rsiCalculator?.IsReady == true ? rsiCalculator.CurrentValue : 50;
            
            // Get MACD
            double macd = 0, macdSignal = 0, macdHistogram = 0;
            if (macdCalculator?.IsReady == true)
            {
                macd = macdCalculator.MacdLine;
                macdSignal = macdCalculator.SignalLine;
                macdHistogram = macdCalculator.Histogram;
            }
            
            // Get ADX
            double adx = 0, plusDi = 0, minusDi = 0;
            if (adxCalculator?.IsReady == true)
            {
                adx = adxCalculator.CurrentAdx;
                plusDi = adxCalculator.PlusDI;
                minusDi = adxCalculator.MinusDI;
            }
            
            // Get Volume ratio
            double volumeRatio = volumeCalculator?.IsReady == true ? volumeCalculator.VolumeRatio : 1.0;
            
            // Get Bollinger Bands
            double bbUpper = 0, bbLower = 0, bbMiddle = 0;
            if (bollingerBands?.IsReady == true)
            {
                bbUpper = bollingerBands.UpperBand;
                bbLower = bollingerBands.LowerBand;
                bbMiddle = bollingerBands.MiddleBand;
            }
            
            // Get ATR
            double atr = atrCalculator?.IsReady == true ? atrCalculator.CurrentAtr : 0;
            
            // Get extended indicators
            double stochasticK = 0, stochasticD = 0;
            if (stochasticCalculator?.IsReady == true)
            {
                stochasticK = stochasticCalculator.PercentK;
                stochasticD = stochasticCalculator.PercentD;
            }
            
            double obvSlope = 0;
            if (obvCalculator?.IsReady == true)
            {
                // Convert boolean direction to normalized slope: +1 rising, -1 falling, 0 neutral
                obvSlope = obvCalculator.IsRising ? 1.0 : (obvCalculator.IsFalling ? -1.0 : 0);
            }
            double cci = cciCalculator?.IsReady == true ? cciCalculator.CurrentCci : 0;
            double williamsR = williamsRCalculator?.IsReady == true ? williamsRCalculator.CurrentValue : -50;
            
            // Get SMA values
            double sma20 = sma20Calculator?.IsReady == true ? sma20Calculator.CurrentValue : 0;
            double sma50 = sma50Calculator?.IsReady == true ? sma50Calculator.CurrentValue : 0;
            
            // Get Momentum and ROC
            double momentum = momentumCalculator?.IsReady == true ? momentumCalculator.CurrentValue : 0;
            double roc = rocCalculator?.IsReady == true ? rocCalculator.CurrentValue : 0;
            
            // Get Bollinger Bands derived values
            double bbPercentB = bollingerBands?.IsReady == true ? bollingerBands.PercentB : 0.5;
            double bbBandwidth = bollingerBands?.IsReady == true ? bollingerBands.Bandwidth : 0;
            
            return new IdiotProof.Calculators.IndicatorSnapshot
            {
                Price = price,
                Vwap = vwap,
                Ema9 = ema9,
                Ema21 = ema21,
                Ema34 = ema34,
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
                WilliamsR = williamsR,
                Sma20 = sma20,
                Sma50 = sma50,
                Momentum = momentum,
                Roc = roc,
                BollingerPercentB = bbPercentB,
                BollingerBandwidth = bbBandwidth,
                PrevDayHigh = prevDayLevels?.PrevDayHigh ?? 0,
                PrevDayLow = prevDayLevels?.PrevDayLow ?? 0,
                PrevDayClose = prevDayLevels?.PrevDayClose ?? 0,
                TwoDayHigh = prevDayLevels?.TwoDayHigh ?? 0,
                TwoDayLow = prevDayLevels?.TwoDayLow ?? 0,
                SessionHigh = prevDayLevels?.SessionHigh ?? 0,
                SessionLow = prevDayLevels?.SessionLow ?? 0
            };
        }

        /// <summary>
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
            double adx = adxCalculator?.CurrentAdx ?? 20;
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
            if (atrCalculator?.IsReady == true)
            {
                double atrPercent = (atrCalculator.CurrentAtr / currentPrice) * 100;
                
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
            double rsi = rsiCalculator?.CurrentValue ?? 50;
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
            // =====================================================================
            // OPENING BELL PATTERN FILTER
            // The first RTH candle (9:30-9:31) is extremely volatile - avoid trading.
            // If there's a "green rush" at end of premarket, expect crash after open.
            // =====================================================================
            var openingAnalysis = candlestickAggregator.GetOpeningBellAnalysis(currentPrice, vwap);
            
            if (openingAnalysis.IsFirstRthCandle)
            {
                // First candle trap - too volatile, skip entry
                Log($"[FILTER] First RTH candle (9:30-9:31) - too volatile, skipping entry", ConsoleColor.DarkYellow);
                return;
            }
            
            if (openingAnalysis.IsRthVolatilityWindow)
            {
                // Volatility window (9:30-9:32) - require higher score threshold
                Log($"[FILTER] RTH volatility window - reduced confidence ({openingAnalysis})", ConsoleColor.DarkYellow);
            }
            
            // Track opening bell score adjustment (applied to thresholds later)
            int openingBellPenalty = 0;
            if (openingAnalysis.IsRthVolatilityWindow)
                openingBellPenalty = 15; // Stricter entry during volatility window

            // Apply ATR volatility filter - skip entries when volatility is too low or too high
            // Small caps and volatile growth stocks can have 10-20% ATR, so use 15% as upper limit
            if (atrCalculator?.IsReady == true)
            {
                double atr = atrCalculator.CurrentAtr;
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
            if (tickerMetadata != null)
            {
                // Check if near support (bullish bias)
                var nearestSupport = tickerMetadata.SupportLevels
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
                var nearestResistance = tickerMetadata.ResistanceLevels
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

            // =====================================================================
            // BREAKOUT-PULLBACK PATTERN DETECTION
            // Classic pattern: Resistance becomes support after breakout
            // =====================================================================
            int breakoutScoreAdjustment = 0;
            bool breakoutWaiting = false;  // Flag to veto entry if pattern says wait
            
            if (breakoutTracker != null && breakoutTracker.HasLevels)
            {
                var breakoutResult = breakoutTracker.Update(currentPrice, isNewBar: false);
                breakoutScoreAdjustment = breakoutResult.ScoreAdjustment;
                breakoutWaiting = breakoutResult.ShouldWait;
                
                // Log state transitions and important signals
                if (breakoutResult.IsIdealEntry)
                {
                    Log($"[BREAKOUT] *** IDEAL ENTRY *** {breakoutResult.Reason}", ConsoleColor.Green);
                }
                else if (breakoutResult.State == Helpers.BreakoutState.PullingBack)
                {
                    Log($"[BREAKOUT] {breakoutResult.Reason}", ConsoleColor.Yellow);
                }
                else if (breakoutResult.State == Helpers.BreakoutState.BrokeOut)
                {
                    Log($"[BREAKOUT] {breakoutResult.Reason}", ConsoleColor.DarkYellow);
                }
                else if (breakoutResult.State == Helpers.BreakoutState.Failed)
                {
                    Log($"[BREAKOUT] {breakoutResult.Reason}", ConsoleColor.Red);
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

            // Apply opening bell penalty (stricter during RTH volatility window)
            if (openingBellPenalty > 0)
            {
                adjustedLongThreshold += openingBellPenalty;
                adjustedShortThreshold -= openingBellPenalty;
                Log($"[OPENING] RTH volatility window: threshold penalty +{openingBellPenalty}", ConsoleColor.DarkYellow);
            }

            // Apply S/R adjustment to thresholds (more lenient when near support, stricter near resistance)
            adjustedLongThreshold -= srScoreAdjustment;
            adjustedShortThreshold += srScoreAdjustment;

            // Apply breakout-pullback adjustment (bonus for confirmed pullback, penalty for chasing)
            if (breakoutScoreAdjustment != 0)
            {
                adjustedLongThreshold -= breakoutScoreAdjustment;  // Lower threshold for ideal pullback entries
                adjustedShortThreshold += breakoutScoreAdjustment;
                Log($"[BREAKOUT] Threshold adjusted by {breakoutScoreAdjustment:+#;-#;0} (Long>={adjustedLongThreshold}, Short<={adjustedShortThreshold})", ConsoleColor.DarkCyan);
            }

            if (tickerProfile != null && tickerProfile.Confidence >= 20)
            {
                adjustedLongThreshold = tickerProfile.GetAdjustedLongEntryThreshold(config.LongEntryThreshold) - srScoreAdjustment;
                adjustedShortThreshold = tickerProfile.GetAdjustedShortEntryThreshold(config.ShortEntryThreshold) + srScoreAdjustment;

                // Apply score adjustment based on streaks
                int adjustment = tickerProfile.GetScoreAdjustment(score.TotalScore, true);
                adjustedLongThreshold += adjustment;
                adjustedShortThreshold -= adjustment;

                // Check if current time is a good window based on history
                var currentTimeET = TimezoneHelper.GetCurrentTime(MarketTimeZone.EST);
                var currentDateTime = DateTime.Today.AddHours(currentTimeET.Hour).AddMinutes(currentTimeET.Minute);
                if (!tickerProfile.IsGoodTimeWindow(currentDateTime))
                {
                    // Skip entry during historically poor time windows
                    return;
                }
            }

            // Apply historical metadata adjustments (HOD/LOD patterns, support/resistance)
            if (tickerMetadata != null)
            {
                var currentTimeET = TimezoneHelper.GetCurrentTime(MarketTimeZone.EST);
                int minutesFromOpen = (int)(new TimeSpan(currentTimeET.Hour, currentTimeET.Minute, 0) - new TimeSpan(9, 30, 0)).TotalMinutes;
                if (minutesFromOpen < 0) minutesFromOpen = 0;

                // Get adjustment based on metadata patterns
                int metadataAdjustment = tickerMetadata.GetEntryAdjustment(currentPrice, minutesFromOpen, true);
                if (metadataAdjustment != 0)
                {
                    adjustedLongThreshold -= metadataAdjustment;  // Lower threshold if conditions favor entry
                    adjustedShortThreshold += metadataAdjustment;

                    // Log metadata influence
                    if (metadataAdjustment > 5 || metadataAdjustment < -5)
                    {
                        string reason = "";
                        if (tickerMetadata.IsNearSupport(currentPrice)) reason += "near support, ";
                        if (tickerMetadata.IsNearResistance(currentPrice)) reason += "near resistance, ";
                        if (tickerMetadata.HodTypicallyEarly && minutesFromOpen > 30) reason += "HOD usually early, ";
                        if (Math.Abs(minutesFromOpen - (int)tickerMetadata.DailyExtremes.AvgLodMinutesFromOpen) < 30) reason += "near typical LOD time, ";

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
            
            // Breakout-pullback veto: If pattern says wait (e.g., chasing breakout), skip entry
            if (breakoutWaiting && breakoutTracker != null && breakoutTracker.HasLevels)
            {
                Log($"[BREAKOUT] Entry vetoed - waiting for pullback confirmation", ConsoleColor.DarkYellow);
                return;  // Skip this tick, wait for proper pullback
            }
            
            bool shouldEnterLong;
            bool shouldEnterShort;
            
            // Use hardcoded thresholds
            shouldEnterLong = score.TotalScore >= adjustedLongThreshold;
            shouldEnterShort = score.TotalScore <= adjustedShortThreshold;
            
            // =====================================================================
            // APPLY TREND DIRECTION FILTER: Block entries against clear trends
            // BUT allow strong reversal signals through (bounce off support)
            // A score significantly above threshold = genuine reversal, not a trap
            // =====================================================================
            if (trendFilter.IsReady)
            {
                if (trendFilter.IsInClearDowntrend && shouldEnterLong)
                {
                    // Allow strong bounce signals through: if score >= threshold + 10,
                    // the bounce has enough momentum to override the downtrend filter
                    bool isStrongBounce = score.TotalScore >= adjustedLongThreshold + 10;
                    if (!isStrongBounce)
                    {
                        Log($"[TREND FILTER] BLOCKED LONG entry - clear downtrend ({trendFilter.Reason})", ConsoleColor.Red);
                        shouldEnterLong = false;
                    }
                    else
                    {
                        Log($"[TREND FILTER] Allowing LONG despite downtrend - strong bounce signal (score {score.TotalScore} >> threshold {adjustedLongThreshold})", ConsoleColor.Yellow);
                    }
                }
                
                if (trendFilter.IsInClearUptrend && shouldEnterShort)
                {
                    bool isStrongBreakdown = score.TotalScore <= adjustedShortThreshold - 10;
                    if (!isStrongBreakdown)
                    {
                        Log($"[TREND FILTER] BLOCKED SHORT entry - clear uptrend ({trendFilter.Reason})", ConsoleColor.Red);
                        shouldEnterShort = false;
                    }
                    else
                    {
                        Log($"[TREND FILTER] Allowing SHORT despite uptrend - strong breakdown signal (score {score.TotalScore} << threshold {adjustedShortThreshold})", ConsoleColor.Yellow);
                    }
                }
            }

            // =====================================================================
            // CLEAN ROCKET PATTERN: Clean premarket + above VWAP + rocket up = ride to HOD
            // If premarket was clean (no green rush) and stock rockets up after open,
            // this is a high-probability trade - reduce threshold and mark for HOD exit
            // =====================================================================
            if (openingAnalysis.Recommendation == OpeningBellAction.CleanRocketBuy)
            {
                var now = TimezoneHelper.GetCurrentTime(MarketTimeZone.EST);
                var rocketWindow = new TimeOnly(9, 32);  // After volatility window settles
                var rocketEnd = new TimeOnly(9, 45);     // Don't chase after first 15 min
                
                if (now >= rocketWindow && now <= rocketEnd)
                {
                    // Clean rocket pattern detected - boost LONG confidence
                    if (!shouldEnterLong && score.TotalScore >= adjustedLongThreshold - 15)
                    {
                        Log($"[CLEAN ROCKET] *** Clean premarket + above VWAP + early strength ***", ConsoleColor.Cyan);
                        Log($"  Boosting LONG entry (score {score.TotalScore} close to threshold {adjustedLongThreshold})", ConsoleColor.Cyan);
                        shouldEnterLong = true;
                    }
                }
            }

            // =====================================================================
            // AI ADVISOR: Get ChatGPT analysis as "third opinion" (rate-limited)
            // =====================================================================
            
            if (aiAdvisor != null && aiAdvisor.IsConfigured && 
                (shouldEnterLong || shouldEnterShort) && 
                DateTime.UtcNow - lastAiAnalysisTime >= aiAnalysisInterval &&
                lastIndicatorSnapshot.HasValue)
            {
                // Fire-and-forget AI analysis to avoid blocking trading decisions
                // The analysis runs async and updates lastAiAnalysis when complete
                var snapshot = lastIndicatorSnapshot.Value;
                var scoreResult = new IdiotProof.Calculators.MarketScoreResult
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
                        var aiAnalysis = await aiAdvisor.AnalyzeEntryAsync(
                            strategy.Symbol,
                            snapshot,
                            scoreResult);
                        
                        lastAiAnalysis = aiAnalysis;
                        lastAiAnalysisTime = DateTime.UtcNow;
                        
                        if (aiAnalysis.IsUsable)
                        {
                            Log($"[AI] {aiAnalysis.Action} (Conf={aiAnalysis.Confidence}%): {aiAnalysis.Reasoning}", ConsoleColor.Magenta);
                            if (aiAnalysis.RiskFactors.Count > 0)
                            {
                                Log($"[AI] Risks: {string.Join(", ", aiAnalysis.RiskFactors)}", ConsoleColor.DarkGray);
                            }
                            if (aiAnalysis.RuleStatus != "NO_RULES")
                            {
                                var ruleColor = aiAnalysis.AreRulesMet ? ConsoleColor.Green : ConsoleColor.Yellow;
                                Log($"[AI] Strategy Rules: {aiAnalysis.RuleStatus}", ruleColor);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"[AI] Error: {ex.Message}", ConsoleColor.DarkRed);
                    }
                });
            }

            // =====================================================================
            // PRICE ACTION CONTEXT: Proactive multi-bar pattern analysis
            // Checks: FVGs, pullback quality, extension/chase, consolidation, structure
            // This transforms the system from reactive to proactive!
            // =====================================================================
            
            if (priceActionContext.CandleCount >= 10)
            {
                try
                {
                    double rsi = rsiCalculator?.IsReady == true ? rsiCalculator.CurrentValue : 50;
                    double macd = macdCalculator?.IsReady == true ? macdCalculator.MacdLine : 0;
                    
                    var paAnalysis = priceActionContext.GetAnalysis(currentPrice, rsi, macd);
                    
                    // Log significant price action context
                    if (paAnalysis.ShouldWaitForPullback)
                    {
                        Log($"[PA] Wait for pullback - overextended ({paAnalysis.ConsecutiveGreenBars} green bars, {paAnalysis.DistanceFromEma9Percent:F1}% from EMA9)", ConsoleColor.DarkYellow);
                    }
                    
                    if (paAnalysis.IsFirstPullbackAfterBreakout)
                    {
                        Log($"[PA] *** FIRST PULLBACK AFTER BREAKOUT *** Ideal entry point ({paAnalysis.BarsSinceBreakout} bars since breakout)", ConsoleColor.Green);
                    }
                    
                    if (paAnalysis.NearestBullishFvg != null && !paAnalysis.NearestBullishFvg.IsFilled)
                    {
                        Log($"[PA] Near bullish FVG: ${paAnalysis.NearestBullishFvg.Low:F2}-${paAnalysis.NearestBullishFvg.High:F2}", ConsoleColor.Cyan);
                    }
                    
                    if (paAnalysis.JustSweptLows)
                    {
                        Log($"[PA] *** LIQUIDITY GRAB *** Just swept lows and reversed - bullish signal", ConsoleColor.Green);
                    }
                    else if (paAnalysis.JustSweptHighs)
                    {
                        Log($"[PA] *** LIQUIDITY GRAB *** Just swept highs and reversed - bearish signal", ConsoleColor.Red);
                    }
                    
                    if (paAnalysis.HasBullishRsiDivergence)
                    {
                        Log($"[PA] Bullish RSI divergence detected (price lower, RSI higher)", ConsoleColor.Cyan);
                    }
                    else if (paAnalysis.HasBearishRsiDivergence)
                    {
                        Log($"[PA] Bearish RSI divergence detected (price higher, RSI lower)", ConsoleColor.Magenta);
                    }
                    
                    // Apply score adjustments from price action
                    int paLongAdj = paAnalysis.LongScoreAdjustment;
                    int paShortAdj = paAnalysis.ShortScoreAdjustment;
                    
                    if (paLongAdj != 0 || paShortAdj != 0)
                    {
                        adjustedLongThreshold -= paLongAdj;    // Lower threshold = easier to enter
                        adjustedShortThreshold += paShortAdj;  // Higher (less negative) = easier to short
                        Log($"[PA] Threshold adjusted: Long adj {paLongAdj:+#;-#;0} (now {adjustedLongThreshold}), Short adj {paShortAdj:+#;-#;0} (now {adjustedShortThreshold})", ConsoleColor.DarkCyan);
                    }
                    
                    // VETO entries based on price action context
                    if (shouldEnterLong && paAnalysis.BlockLongEntry)
                    {
                        Log($"[PA] BLOCKED LONG: {paAnalysis.Reasoning}", ConsoleColor.Red);
                        Log($"     Bearish factors: {string.Join(", ", paAnalysis.BearishFactors)}", ConsoleColor.DarkRed);
                        shouldEnterLong = false;
                    }
                    
                    if (shouldEnterShort && paAnalysis.BlockShortEntry)
                    {
                        Log($"[PA] BLOCKED SHORT: {paAnalysis.Reasoning}", ConsoleColor.Red);
                        Log($"     Bullish factors: {string.Join(", ", paAnalysis.BullishFactors)}", ConsoleColor.DarkRed);
                        shouldEnterShort = false;
                    }
                    
                    // Don't chase - overextended stocks should wait for pullback
                    // ShouldWaitForPullback alone is sufficient (PriceActionContext already uses
                    // price-tier-aware thresholds internally)
                    if (shouldEnterLong && paAnalysis.ShouldWaitForPullback)
                    {
                        Log($"[PA] VETO: Don't chase - {paAnalysis.ConsecutiveGreenBars} consecutive green bars, {paAnalysis.DistanceFromEma9Percent:F1}% from EMA9", ConsoleColor.DarkYellow);
                        shouldEnterLong = false;
                    }
                    
                    if (shouldEnterShort && paAnalysis.ShouldWaitForPullback)
                    {
                        Log($"[PA] VETO: Don't chase - {paAnalysis.ConsecutiveRedBars} consecutive red bars, {paAnalysis.DistanceFromEma9Percent:F1}% from EMA9", ConsoleColor.DarkYellow);
                        shouldEnterShort = false;
                    }
                    
                    // BOOST entries for ideal setups
                    if (!shouldEnterLong && paAnalysis.IsIdealLongEntry && score.TotalScore >= adjustedLongThreshold - 20)
                    {
                        Log($"[PA] BOOST: Ideal LONG setup detected - {paAnalysis.Reasoning}", ConsoleColor.Green);
                        Log($"     Bullish factors: {string.Join(", ", paAnalysis.BullishFactors)}", ConsoleColor.DarkGreen);
                        shouldEnterLong = true;
                    }
                    
                    if (!shouldEnterShort && paAnalysis.IsIdealShortEntry && score.TotalScore <= adjustedShortThreshold + 20)
                    {
                        Log($"[PA] BOOST: Ideal SHORT setup detected - {paAnalysis.Reasoning}", ConsoleColor.Magenta);
                        Log($"     Bearish factors: {string.Join(", ", paAnalysis.BearishFactors)}", ConsoleColor.DarkMagenta);
                        shouldEnterShort = true;
                    }
                }
                catch (Exception ex)
                {
                    Log($"WARNING: PriceAction analysis failed: {ex.Message}", ConsoleColor.Yellow);
                }
            }

            // =====================================================================
            // PROACTIVE MARKET SCANNER: Empirical pattern detection & opportunity scoring
            // Scans for FORMING patterns, momentum exhaustion, volume profile analysis
            // Decisions made with confidence based on empirical evidence, not just current candle
            // =====================================================================
            
            if (proactiveScanner.HasEnoughData)
            {
                try
                {
                    var opportunity = proactiveScanner.GetOpportunity(currentPrice);
                    
                    // Log significant patterns forming
                    if (opportunity.ActivePattern != null)
                    {
                        Log($"[PATTERN] {opportunity.ActivePattern}", ConsoleColor.Cyan);
                    }
                    
                    foreach (var pattern in opportunity.FormingPatterns.Where(p => p.Confidence >= 0.6))
                    {
                        Log($"[FORMING] {pattern.Type} ({pattern.Stage}) - {pattern.Description}", ConsoleColor.DarkCyan);
                    }
                    
                    // Log momentum exhaustion warning
                    if (opportunity.Momentum.IsExhausting && opportunity.Momentum.ExhaustionProbability >= 0.6)
                    {
                        Log($"[MOMENTUM] *** EXHAUSTION WARNING *** {opportunity.Momentum.Reason}", ConsoleColor.Yellow);
                    }
                    
                    // Log A/D divergence
                    if (opportunity.AccumDist.IsDiverging)
                    {
                        Log($"[A/D] {opportunity.AccumDist.DivergenceType} divergence - {(opportunity.AccumDist.IsAccumulating ? "accumulating" : "distributing")}", ConsoleColor.DarkYellow);
                    }
                    
                    // Log high confidence setups
                    if (opportunity.EmpiricalConfidence >= 75)
                    {
                        Log($"[EMPIRICAL] Confidence: {opportunity.EmpiricalConfidence:F0}% | Evidence: +{opportunity.BullishEvidence.Count}/-{opportunity.BearishEvidence.Count}", ConsoleColor.White);
                        Log($"  {opportunity.RecommendedAction}", ConsoleColor.White);
                    }
                    
                    // Apply proactive scanner influence
                    if (opportunity.IsHighConfidenceLongSetup && !shouldEnterLong && score.TotalScore >= adjustedLongThreshold - 25)
                    {
                        Log($"[PROACTIVE] *** HIGH CONFIDENCE LONG *** {opportunity.RecommendedAction}", ConsoleColor.Green);
                        Log($"     Bullish evidence: {string.Join(", ", opportunity.BullishEvidence)}", ConsoleColor.DarkGreen);
                        shouldEnterLong = true;
                    }
                    
                    if (opportunity.IsHighConfidenceShortSetup && !shouldEnterShort && score.TotalScore <= adjustedShortThreshold + 25)
                    {
                        Log($"[PROACTIVE] *** HIGH CONFIDENCE SHORT *** {opportunity.RecommendedAction}", ConsoleColor.Magenta);
                        Log($"     Bearish evidence: {string.Join(", ", opportunity.BearishEvidence)}", ConsoleColor.DarkMagenta);
                        shouldEnterShort = true;
                    }
                    
                    // Block entries on momentum exhaustion
                    if (shouldEnterLong && opportunity.Momentum.State == Helpers.MomentumState.WeakeningBullish && 
                        opportunity.Momentum.ExhaustionProbability >= 0.7)
                    {
                        Log($"[PROACTIVE] BLOCKED LONG - Bullish momentum exhausting ({opportunity.Momentum.ExhaustionProbability:P0} probability)", ConsoleColor.Red);
                        shouldEnterLong = false;
                    }
                    
                    if (shouldEnterShort && opportunity.Momentum.State == Helpers.MomentumState.WeakeningBearish && 
                        opportunity.Momentum.ExhaustionProbability >= 0.7)
                    {
                        Log($"[PROACTIVE] BLOCKED SHORT - Bearish momentum exhausting ({opportunity.Momentum.ExhaustionProbability:P0} probability)", ConsoleColor.Red);
                        shouldEnterShort = false;
                    }
                    
                    // Block entries if scanner says wait
                    if (opportunity.ShouldWaitForBetterEntry && opportunity.EmpiricalConfidence < 50)
                    {
                        if (shouldEnterLong)
                        {
                            Log($"[PROACTIVE] WAIT - Need more bullish evidence ({opportunity.BullishEvidence.Count} vs {opportunity.BearishEvidence.Count})", ConsoleColor.DarkYellow);
                            shouldEnterLong = false;
                        }
                        if (shouldEnterShort)
                        {
                            Log($"[PROACTIVE] WAIT - Need more bearish evidence ({opportunity.BearishEvidence.Count} vs {opportunity.BullishEvidence.Count})", ConsoleColor.DarkYellow);
                            shouldEnterShort = false;
                        }
                    }
                    
                    // Log ideal entry levels for pattern setups
                    if (opportunity.ActivePattern != null && opportunity.ActivePattern.Stage >= Helpers.PatternStage.LateFormation)
                    {
                        double distToEntry = Math.Abs(currentPrice - opportunity.ActivePattern.EntryLevel) / currentPrice * 100;
                        if (distToEntry < 2)
                        {
                            Log($"[PATTERN ENTRY] Near {opportunity.ActivePattern.Type} entry: ${opportunity.ActivePattern.EntryLevel:F2} | Target: ${opportunity.ActivePattern.TargetLevel:F2} | Stop: ${opportunity.ActivePattern.StopLevel:F2} | R:R={opportunity.ActivePattern.RiskRewardRatio:F1}", ConsoleColor.White);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log($"WARNING: ProactiveScanner analysis failed: {ex.Message}", ConsoleColor.Yellow);
                }
            }

            // Check for LONG entry
            if (shouldEnterLong)
            {
                // AI CONFIDENCE CHECK - Block entry if confidence too low
                int aiConfidence = 0;
                bool aiApproved = true;
                string aiReason = "";
                
                if (lastIndicatorSnapshot.HasValue && aiAdvisor != null)
                {
                    var snapshot = lastIndicatorSnapshot.Value;
                    var scoreResult = new IdiotProof.Calculators.MarketScoreResult
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
                    
                    // Use synthetic for speed, real AI only if configured and on borderline cases
                    bool useSynthetic = !aiAdvisor.IsConfigured;
                    (aiApproved, aiConfidence, aiReason) = aiAdvisor.CheckTradeApproval(
                        strategy.Symbol, snapshot, isLong: true, scoreResult, useSyntheticForSpeed: useSynthetic);
                    
                    if (!aiApproved)
                    {
                        Log($"[AI GATE] LONG BLOCKED: {aiReason}", ConsoleColor.Red);
                        shouldEnterLong = false;
                    }
                    else
                    {
                        Log($"[AI GATE] LONG approved (Conf={aiConfidence}%)", ConsoleColor.Green);
                    }
                }

                if (!shouldEnterLong) return; // Blocked by AI

                string thresholdInfo = adjustedLongThreshold != config.LongEntryThreshold
                    ? $" (threshold: {adjustedLongThreshold}, default: {config.LongEntryThreshold})"
                    : "";
                Log($"*** AUTONOMOUS LONG SIGNAL: Score {score.TotalScore} >= {adjustedLongThreshold}{thresholdInfo}", ConsoleColor.Cyan);
                Log($"  Indicators: VWAP={score.VwapScore}, EMA={score.EmaScore}, RSI={score.RsiScore}, MACD={score.MacdScore}, ADX={score.AdxScore}, Vol={score.VolumeScore}", ConsoleColor.DarkGray);
                if (prevDayLevels?.HasData == true)
                {
                    Log($"  S/R Levels: PDH=${prevDayLevels.PrevDayHigh:F2}, PDL=${prevDayLevels.PrevDayLow:F2}, PDC=${prevDayLevels.PrevDayClose:F2}", ConsoleColor.DarkGray);
                }
                
                // Calculate TP/SL based on ATR with self-adjusting multipliers
                var (takeProfit, stopLoss) = CalculateAutonomousTpSl(currentPrice, true, config, 
                    dynamicTpMultiplier, dynamicSlMultiplier);
                
                Log($"  Auto TP: ${takeProfit:F2} | Auto SL: ${stopLoss:F2}", ConsoleColor.DarkGray);

                // Store entry score for learning
                entryScore = score;

                // Create pending trade record for learning
                pendingTradeRecord = new TradeRecord
                {
                    EntryTime = DateTime.UtcNow,
                    EntryPrice = currentPrice,
                    IsLong = true,
                    Quantity = GetEffectiveQuantity(currentPrice),
                    EntryScore = score.TotalScore,
                    EntryVwapScore = score.VwapScore,
                    EntryEmaScore = score.EmaScore,
                    EntryRsiScore = score.RsiScore,
                    EntryMacdScore = score.MacdScore,
                    EntryAdxScore = score.AdxScore,
                    EntryVolumeScore = score.VolumeScore,
                    RsiAtEntry = rsiCalculator?.CurrentValue ?? 50,
                    AdxAtEntry = adxCalculator?.CurrentAdx ?? 20
                };
                
                // Execute long entry
                ExecuteAutonomousEntry(currentPrice, vwap, true, takeProfit, stopLoss, config);
                return;
            }

            // Check for SHORT entry (if allowed and not blocked)
            if (config.AllowShort && !shortSaleBlocked && shouldEnterShort)
            {
                // AI CONFIDENCE CHECK - Block entry if confidence too low
                int aiConfidence = 0;
                bool aiApproved = true;
                string aiReason = "";
                
                if (lastIndicatorSnapshot.HasValue && aiAdvisor != null)
                {
                    var snapshot = lastIndicatorSnapshot.Value;
                    var scoreResult = new IdiotProof.Calculators.MarketScoreResult
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
                    
                    // Use synthetic for speed, real AI only if configured
                    bool useSynthetic = !aiAdvisor.IsConfigured;
                    (aiApproved, aiConfidence, aiReason) = aiAdvisor.CheckTradeApproval(
                        strategy.Symbol, snapshot, isLong: false, scoreResult, useSyntheticForSpeed: useSynthetic);
                    
                    if (!aiApproved)
                    {
                        Log($"[AI GATE] SHORT BLOCKED: {aiReason}", ConsoleColor.Red);
                        shouldEnterShort = false;
                    }
                    else
                    {
                        Log($"[AI GATE] SHORT approved (Conf={aiConfidence}%)", ConsoleColor.Green);
                    }
                }

                if (!shouldEnterShort) return; // Blocked by AI

                string thresholdInfo = adjustedShortThreshold != config.ShortEntryThreshold
                    ? $" (threshold: {adjustedShortThreshold}, default: {config.ShortEntryThreshold})"
                    : "";
                Log($"*** AUTONOMOUS SHORT SIGNAL: Score {score.TotalScore} <= {adjustedShortThreshold}{thresholdInfo}", ConsoleColor.Magenta);
                Log($"  Indicators: VWAP={score.VwapScore}, EMA={score.EmaScore}, RSI={score.RsiScore}, MACD={score.MacdScore}, ADX={score.AdxScore}, Vol={score.VolumeScore}", ConsoleColor.DarkGray);
                if (prevDayLevels?.HasData == true)
                {
                    Log($"  S/R Levels: PDH=${prevDayLevels.PrevDayHigh:F2}, PDL=${prevDayLevels.PrevDayLow:F2}, PDC=${prevDayLevels.PrevDayClose:F2}", ConsoleColor.DarkGray);
                }
                
                // Calculate TP/SL based on ATR with self-adjusting multipliers
                var (takeProfit, stopLoss) = CalculateAutonomousTpSl(currentPrice, false, config,
                    dynamicTpMultiplier, dynamicSlMultiplier);
                
                Log($"  Auto TP: ${takeProfit:F2} | Auto SL: ${stopLoss:F2}", ConsoleColor.DarkGray);

                // Store entry score for learning
                entryScore = score;

                // Create pending trade record for learning
                pendingTradeRecord = new TradeRecord
                {
                    EntryTime = DateTime.UtcNow,
                    EntryPrice = currentPrice,
                    IsLong = false,
                    Quantity = GetEffectiveQuantity(currentPrice),
                    EntryScore = score.TotalScore,
                    EntryVwapScore = score.VwapScore,
                    EntryEmaScore = score.EmaScore,
                    EntryRsiScore = score.RsiScore,
                    EntryMacdScore = score.MacdScore,
                    EntryAdxScore = score.AdxScore,
                    EntryVolumeScore = score.VolumeScore,
                    RsiAtEntry = rsiCalculator?.CurrentValue ?? 50,
                    AdxAtEntry = adxCalculator?.CurrentAdx ?? 20
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
            bool isLong = this.isLong;  // Use tracked position direction
            bool isFlipMode = config.UseFlipMode;

            // =====================================================================
            // PREMARKET GREEN RUSH WARNING EXIT
            // If stock shows lots of green candles right before RTH bell (9:25-9:30),
            // it's likely to crash after open. EXIT any long position before the bell.
            // =====================================================================
            var openingAnalysis = candlestickAggregator.GetOpeningBellAnalysis(currentPrice, vwap);
            
            if (isLong && openingAnalysis.HasGreenRushWarning)
            {
                var now = TimezoneHelper.GetCurrentTime(MarketTimeZone.EST);
                var rthOpen = new TimeOnly(9, 30);
                
                // Only exit if we're in the last few minutes before RTH open
                if (now >= rthOpen.AddMinutes(-3) && now < rthOpen)
                {
                    int qty = effectiveQuantity > 0 ? effectiveQuantity : GetEffectiveQuantity(currentPrice);
                    double pnl = (currentPrice - entryFillPrice) * qty;
                    double pnlPercent = (entryFillPrice > 0) ? ((currentPrice - entryFillPrice) / entryFillPrice) * 100 : 0;
                    
                    Log($"*** GREEN RUSH WARNING EXIT! Multiple green candles before RTH bell =  crash likely!", ConsoleColor.Red);
                    Log($"  Premarket gain: {openingAnalysis.PremarketGainPercent:+0.0;-0.0}% | Green candles: {openingAnalysis.PremarketGreenCandles}", ConsoleColor.Yellow);
                    Log($"  Exiting LONG at ${currentPrice:F2} ({pnlPercent:+0.00;-0.00}%, ${pnl:+0.00;-0.00})", ConsoleColor.Yellow);
                    
                    ExecuteAutonomousExit(currentPrice, vwap, isLong);
                    
                    // After RTH opens and first candle settles, consider shorting the fade
                    // This will be handled by normal entry logic after 9:32
                    return;
                }
            }

            // =====================================================================
            // EARLY HOD EXIT: If ticker typically makes HOD early and we're near it
            // Sell LONG → Immediately SHORT → Plan to cover at VWAP
            // =====================================================================
            if (isLong && ShouldExitAtEarlyHod(currentPrice))
            {
                double pnl = (currentPrice - entryFillPrice) * strategy.Order.Quantity;
                double pnlPercent = (entryFillPrice > 0) ? ((currentPrice - entryFillPrice) / entryFillPrice) * 100 : 0;
                
                Log($"*** EARLY HOD EXIT: Price ${currentPrice:F2} near session high in first 30 min!", ConsoleColor.Green);
                Log($"  Ticker typically makes HOD early - locking in {pnlPercent:+0.00;-0.00}% profit (${pnl:+0.00;-0.00})", ConsoleColor.Green);
                
                ExecuteAutonomousExit(currentPrice, vwap, isLong);
                
                // HOD reached → Price likely to fade → FLIP TO SHORT
                if (config.AllowShort && !shortSaleBlocked)
                {
                    Log($"  -> HOD fade strategy: Flipping to SHORT, target cover at VWAP (${vwap:F2})", ConsoleColor.Magenta);
                    
                    // Use VWAP as take profit for the short (cover target)
                    double shortTp = vwap;
                    // Stop loss above HOD with buffer
                    double shortSl = currentPrice * 1.02; // 2% above entry
                    
                    // Mark this as a HOD-fade short for special handling
                    isHodFadeShort = true;
                    hodFadeVwapTarget = vwap;
                    
                    ExecuteAutonomousEntry(currentPrice, vwap, false, shortTp, shortSl, config);
                }
                return;
            }
            
            // =====================================================================
            // HOD FADE SHORT COVER: Cover short at VWAP, then go LONG again
            // =====================================================================
            if (!isLong && isHodFadeShort && ShouldCoverHodFadeShortAtVwap(currentPrice, vwap))
            {
                double pnl = (entryFillPrice - currentPrice) * strategy.Order.Quantity;
                double pnlPercent = (entryFillPrice > 0) ? ((entryFillPrice - currentPrice) / entryFillPrice) * 100 : 0;
                
                Log($"*** VWAP COVER: Price ${currentPrice:F2} reached VWAP target (${hodFadeVwapTarget:F2})", ConsoleColor.Cyan);
                Log($"  HOD fade complete - locking in {pnlPercent:+0.00;-0.00}% short profit (${pnl:+0.00;-0.00})", ConsoleColor.Cyan);
                
                ExecuteAutonomousExit(currentPrice, vwap, isLong);
                isHodFadeShort = false;
                hodFadeVwapTarget = 0;
                
                // VWAP bounce → Price likely to go up → FLIP TO LONG
                Log($"  -> VWAP bounce strategy: Flipping to LONG for continuation", ConsoleColor.Cyan);
                
                // Calculate TP/SL for the new long position
                var (longTp, longSl) = CalculateAutonomousTpSl(currentPrice, true, config);
                ExecuteAutonomousEntry(currentPrice, vwap, true, longTp, longSl, config);
                return;
            }

            // FLIP MODE: Always flip direction on reversal - stay in market
            if (isFlipMode)
            {
                // In FlipMode, we flip when score crosses the opposite entry threshold
                // This keeps us always in market, just on the right side of the trend
                if (isLong && score.TotalScore <= config.ShortEntryThreshold)
                {
                    // Bearish signal while long - FLIP TO SHORT
                    if (config.AllowShort && !shortSaleBlocked)
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
            // Enforce minimum hold time before score-based exits (SL/TP still fire immediately via orders)
            bool holdTimeMet = lastTradeTime != DateTime.MinValue &&
                (DateTime.UtcNow - lastTradeTime).TotalMinutes >= TradingDefaults.MinHoldMinutesBeforeScoreExit;
            
            if (isLong && holdTimeMet && score.TotalScore < config.LongExitThreshold)
            {
                Log($"*** AUTONOMOUS LONG EXIT: Score {score.TotalScore} < {config.LongExitThreshold} ({score.Condition})", ConsoleColor.Yellow);
                Log($"  Position was LONG, momentum lost. Consider direction flip: {config.AllowDirectionFlip}", ConsoleColor.DarkGray);
                
                // Exit the position
                ExecuteAutonomousExit(currentPrice, vwap, isLong);
                
                // Consider direction flip (only if short sale is not blocked)
                if (config.AllowDirectionFlip && config.AllowShort && !shortSaleBlocked && score.TotalScore <= config.ShortEntryThreshold)
                {
                    Log($"  -> Flipping to SHORT (score {score.TotalScore} <= {config.ShortEntryThreshold})", ConsoleColor.Magenta);
                    var (tp, sl) = CalculateAutonomousTpSl(currentPrice, false, config);
                    ExecuteAutonomousEntry(currentPrice, vwap, false, tp, sl, config);
                }
            }
            else if (!isLong && holdTimeMet && score.TotalScore > config.ShortExitThreshold)
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
            lastTradeTime = DateTime.UtcNow;
            
            // Record the exit for learning
            CompletePendingTradeRecord(currentPrice);
            
            // Calculate P&L for logging
            double pnl = isLong 
                ? strategy.Order.Quantity * (currentPrice - entryFillPrice)
                : strategy.Order.Quantity * (entryFillPrice - currentPrice);
            
            var pnlColor = pnl >= 0 ? ConsoleColor.Green : ConsoleColor.Red;
            Log($"  Exit P&L: ${pnl:F2}", pnlColor);
            SessionLogger?.LogFill(strategy.Symbol, isLong ? "SELL" : "BUY", strategy.Order.Quantity, currentPrice, pnl);
            
            // Reset for new position
            ResetForNextAutonomousTrade();
            
            // Immediately enter opposite direction
            isLong = goLong;
            dynamicTakeProfit = 0; // Not used in FlipMode
            dynamicStopLoss = 0;   // Not used in FlipMode
            
            // Create new entry for learning
            var score = CalculateMarketScoreForAutonomous(currentPrice, vwap, config.OptimizedWeights);
            entryScore = score;
            pendingTradeRecord = new TradeRecord
            {
                EntryTime = DateTime.UtcNow,
                EntryPrice = currentPrice,
                IsLong = goLong,
                Quantity = GetEffectiveQuantity(currentPrice),
                EntryScore = score.TotalScore,
                EntryVwapScore = score.VwapScore,
                EntryEmaScore = score.EmaScore,
                EntryRsiScore = score.RsiScore,
                EntryMacdScore = score.MacdScore,
                EntryAdxScore = score.AdxScore,
                EntryVolumeScore = score.VolumeScore,
                RsiAtEntry = rsiCalculator?.CurrentValue ?? 50,
                AdxAtEntry = adxCalculator?.CurrentAdx ?? 20
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
            if (atrCalculator?.IsReady == true)
            {
                double atr = atrCalculator.CurrentAtr;
                // Scale ATR multiplier based on price tier (penny stocks need wider stops)
                double scale = TradingDefaults.GetAtrMultiplierScale(entryPrice);
                tpDistance = atr * tpMultiplier * scale;
                slDistance = atr * slMultiplier * scale;
            }
            else
            {
                // Fallback to percentage-based
                tpDistance = entryPrice * config.TakeProfitPercent;
                slDistance = entryPrice * config.StopLossPercent;
            }

            // Enforce minimum distances based on price tier
            (tpDistance, slDistance) = TradingDefaults.EnforceMinimumDistances(entryPrice, tpDistance, slDistance);

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
            double estimatedOrderValue = currentPrice * strategy.Order.Quantity;
            // For margin accounts, typically need ~25-50% of the position value depending on stock
            double estimatedMarginRequired = estimatedOrderValue * 0.50; // Conservative 50% margin estimate
            
            if (!wrapper.HasSufficientMargin(estimatedMarginRequired))
            {
                double available = wrapper.AvailableFunds;
                Log($"[FILTER] Skipping entry - Insufficient margin: ${available:N2} available, ~${estimatedMarginRequired:N2} required", ConsoleColor.Red);
                return;
            }

            lastTradeTime = DateTime.UtcNow;
            
            // Track autonomous position direction (used by SubmitTakeProfit/StopLoss)
            this.isLong = isLong;
            dynamicTakeProfit = takeProfit;
            dynamicStopLoss = stopLoss;
            
            // Set the dynamically calculated TP/SL tracking vars
            takeProfitTarget = takeProfit;
            originalStopLossPrice = stopLoss;
            
            // Log and execute the entry order using the existing ExecuteOrder mechanism
            // Since we're in autonomous mode, we skip condition evaluation
            currentConditionIndex = strategy.Conditions.Count; // Mark all conditions as met
            
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
            lastTradeTime = DateTime.UtcNow;

            // Complete the pending trade record for learning
            CompletePendingTradeRecord(currentPrice);
            
            // Cancel any pending TP/SL orders (only if not rejected)
            if (takeProfitOrderId > 0 && !takeProfitFilled && !takeProfitCancelled && !takeProfitOrderRejected)
            {
                Log($"Cancelling Take Profit order {takeProfitOrderId}", ConsoleColor.DarkGray);
                client.cancelOrder(takeProfitOrderId, new OrderCancel());
                takeProfitCancelled = true;
            }
            
            if (stopLossOrderId > 0 && !stopLossFilled && !stopLossOrderRejected)
            {
                Log($"Cancelling Stop Loss order {stopLossOrderId}", ConsoleColor.DarkGray);
                client.cancelOrder(stopLossOrderId, new OrderCancel());
            }

            // Submit autonomous exit order (tracked separately so we can cycle after fill)
            exitOrderId = wrapper.ConsumeNextOrderId();
            string action = wasLong ? "SELL" : "BUY"; // Close position

            // Use effective quantity (already calculated at entry)
            int qty = effectiveQuantity > 0 ? effectiveQuantity : GetEffectiveQuantity(currentPrice);

            var exitOrder = new Order
            {
                Action = action,
                OrderType = "MKT",
                TotalQuantity = qty,
                OutsideRth = true,
                Tif = "GTC"
            };

            if (!string.IsNullOrEmpty(AppSettings.AccountNumber))
                exitOrder.Account = AppSettings.AccountNumber;

            Log($">> AUTONOMOUS EXIT: {action} {qty} @ MKT (cycling to next trade)", ConsoleColor.Yellow);
            client.placeOrder(exitOrderId, contract, exitOrder);
        }

        /// <summary>
        /// Completes the pending trade record and saves it to the ticker profile.
        /// </summary>
        private void CompletePendingTradeRecord(double exitPrice)
        {
            if (pendingTradeRecord == null || tickerProfile == null)
                return;

            try
            {
                // Calculate current score for exit
                var exitScore = CalculateMarketScoreForAutonomous(exitPrice, GetVwap());

                pendingTradeRecord.ExitTime = DateTime.UtcNow;
                pendingTradeRecord.ExitPrice = exitPrice;
                pendingTradeRecord.ExitScore = exitScore.TotalScore;
                pendingTradeRecord.RsiAtExit = rsiCalculator?.CurrentValue ?? 50;
                pendingTradeRecord.AdxAtExit = adxCalculator?.CurrentAdx ?? 20;

                // Record the trade
                profileManager.RecordTrade(strategy.Symbol, pendingTradeRecord);

                // Log the outcome
                string outcome = pendingTradeRecord.IsWin ? "WIN" : "LOSS";
                var color = pendingTradeRecord.IsWin ? ConsoleColor.Green : ConsoleColor.Red;
                Log($"[LEARN] Trade recorded: {outcome} ${pendingTradeRecord.PnL:F2} ({pendingTradeRecord.PnLPercent:F2}%)", color);
                Log($"  Duration: {pendingTradeRecord.Duration.TotalMinutes:F0} min | Entry score: {pendingTradeRecord.EntryScore} | Exit score: {pendingTradeRecord.ExitScore}", ConsoleColor.DarkGray);
                Log($"  Profile updated: {tickerProfile.GetSummary()}", ConsoleColor.DarkGray);

                // Record AI recommendation accuracy for learning
                if (aiAdvisor != null && lastAiAnalysis != null && lastAiAnalysis.IsUsable)
                {
                    bool aiWasCorrect = (lastAiAnalysis.Action == "LONG" && pendingTradeRecord.IsLong && pendingTradeRecord.IsWin) ||
                                        (lastAiAnalysis.Action == "SHORT" && !pendingTradeRecord.IsLong && pendingTradeRecord.IsWin) ||
                                        (lastAiAnalysis.Action == "WAIT" && !pendingTradeRecord.IsWin);
                    aiAdvisor.RecordOutcome(lastAiAnalysis, aiWasCorrect);
                    
                    var (total, correct, accuracy) = aiAdvisor.GetAccuracyStats();
                    if (total >= 5)
                    {
                        Log($"[AI] Accuracy: {accuracy:F0}% ({correct}/{total} correct)", ConsoleColor.DarkMagenta);
                    }
                }

                pendingTradeRecord = null;
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
            var order = strategy.Order;

            // Skip if no open position
            if (!entryFilled || isComplete)
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
            var timeSinceLastAdjustment = (now - lastAdaptiveAdjustmentTime).TotalSeconds;
            if (timeSinceLastAdjustment < config.MinSecondsBetweenAdjustments)
                return;

            // Calculate market score - use tracked position direction for autonomous trading
            bool isLong = order.UseAutonomousTrading ? this.isLong : (order.Side == OrderSide.Buy);
            var score = CalculateMarketScore(currentPrice, vwap, isLong);

            // Check for emergency exit (AdaptiveOrder threshold-based)
            if (score.ShouldEmergencyExit)
            {
                Log($"*** ADAPTIVE EMERGENCY EXIT! Score: {score.TotalScore} ({score.Condition})", ConsoleColor.Red);
                ExecuteEmergencyExit();
                return;
            }

            // Only adjust if score changed significantly
            int scoreDelta = Math.Abs(score.TotalScore - lastAdaptiveScore);
            if (scoreDelta < config.MinScoreChangeForAdjustment)
                return;

            // Calculate new prices based on score
            bool adjustmentMade = false;

            // Adjust take profit
            if (takeProfitOrderId > 0 && !takeProfitFilled && !takeProfitCancelled && originalTakeProfitPrice > 0)
            {
                double newTakeProfitPrice;
                
                if (isLong)
                {
                    // Long: TP is above entry, extend means higher
                    double profitRange = originalTakeProfitPrice - entryFillPrice;
                    double adjustment = profitRange * (score.TakeProfitMultiplier - 1.0);
                    newTakeProfitPrice = Math.Round(originalTakeProfitPrice + adjustment, 2);
                    newTakeProfitPrice = Math.Max(newTakeProfitPrice, entryFillPrice + 0.01);
                }
                else
                {
                    // Short: TP is below entry, extend means lower
                    double profitRange = entryFillPrice - originalTakeProfitPrice;
                    double adjustment = profitRange * (score.TakeProfitMultiplier - 1.0);
                    newTakeProfitPrice = Math.Round(originalTakeProfitPrice - adjustment, 2);
                    newTakeProfitPrice = Math.Min(newTakeProfitPrice, entryFillPrice - 0.01);
                }

                if (Math.Abs(newTakeProfitPrice - currentAdaptiveTakeProfitPrice) > 0.01)
                {
                    Log($"ADAPTIVE: Adjusting TP ${currentAdaptiveTakeProfitPrice:F2} -> ${newTakeProfitPrice:F2} (Score: {score.TotalScore}, ×{score.TakeProfitMultiplier:F2})", ConsoleColor.Cyan);
                    ModifyTakeProfitOrder(newTakeProfitPrice);
                    currentAdaptiveTakeProfitPrice = newTakeProfitPrice;
                    adjustmentMade = true;
                }
            }

            // Adjust stop loss (if using fixed SL, not trailing)
            if (stopLossOrderId > 0 && !stopLossFilled && originalStopLossPrice > 0 && !order.EnableTrailingStopLoss)
            {
                double newStopLossPrice;
                
                if (isLong)
                {
                    // Long: SL is below entry, tighten means higher (closer to entry)
                    double lossRange = entryFillPrice - originalStopLossPrice;
                    double adjustment = lossRange * (score.StopLossMultiplier - 1.0);
                    newStopLossPrice = Math.Round(originalStopLossPrice - adjustment, 2);
                    newStopLossPrice = Math.Min(newStopLossPrice, entryFillPrice - 0.01);
                }
                else
                {
                    // Short: SL is above entry, tighten means lower (closer to entry)
                    double lossRange = originalStopLossPrice - entryFillPrice;
                    double adjustment = lossRange * (score.StopLossMultiplier - 1.0);
                    newStopLossPrice = Math.Round(originalStopLossPrice + adjustment, 2);
                    newStopLossPrice = Math.Max(newStopLossPrice, entryFillPrice + 0.01);
                }

                if (Math.Abs(newStopLossPrice - currentAdaptiveStopLossPrice) > 0.01)
                {
                    Log($"ADAPTIVE: Adjusting SL ${currentAdaptiveStopLossPrice:F2} -> ${newStopLossPrice:F2} (Score: {score.TotalScore}, ×{score.StopLossMultiplier:F2})", ConsoleColor.Yellow);
                    ModifyStopLossOrder(newStopLossPrice);
                    currentAdaptiveStopLossPrice = newStopLossPrice;
                    adjustmentMade = true;
                }
            }

            if (adjustmentMade)
            {
                lastAdaptiveAdjustmentTime = now;
                lastAdaptiveScore = score.TotalScore;
                Log($"  Market Analysis: {score}", ConsoleColor.DarkGray);
            }
        }

        /// <summary>
        /// Monitors ADX for rollover (peak detection) to trigger early exit on fading momentum.
        /// Used with ADX-based TakeProfit when ExitOnAdxRollover is enabled.
        /// </summary>
        private void MonitorAdxRollover(double currentPrice)
        {
            var order = strategy.Order;

            // Only monitor if ADX-based TP is configured with rollover exit enabled
            if (order.AdxTakeProfit == null || !order.AdxTakeProfit.ExitOnAdxRollover)
                return;

            // Need ADX calculator and position must be open
            if (adxCalculator == null || !adxCalculator.IsReady || !entryFilled || isComplete)
                return;

            // Skip if already triggered rollover exit
            if (adxRolledOver)
                return;

            double currentAdx = adxCalculator.CurrentAdx;
            double rolloverThreshold = order.AdxTakeProfit.AdxRolloverThreshold;

            // Track ADX peak
            if (currentAdx > adxPeakValue)
            {
                adxPeakValue = currentAdx;
            }

            // Check for rollover: ADX dropped from peak by threshold amount
            double dropFromPeak = adxPeakValue - currentAdx;
            if (dropFromPeak >= rolloverThreshold && adxPeakValue >= order.AdxTakeProfit.DevelopingTrendThreshold)
            {
                adxRolledOver = true;

                // Only exit if profitable
                double currentPnL = currentPrice - entryFillPrice;
                if (currentPnL > 0)
                {
                    Log($"*** ADX ROLLOVER EXIT! ADX dropped {dropFromPeak:F1} from peak {adxPeakValue:F1} to {currentAdx:F1} ***", ConsoleColor.Magenta);
                    Log($"  Momentum fading - exiting with profit ${currentPnL:F2}/share", ConsoleColor.Magenta);

                    // Update take profit target to current price for immediate exit
                    if (takeProfitOrderId > 0 && !takeProfitFilled && !takeProfitCancelled && !takeProfitOrderRejected)
                    {
                        // Adjust TP to slightly below current price for quick fill
                        double exitPrice = Math.Round(currentPrice - 0.01, 2);
                        Log($"  Adjusting TP ${takeProfitTarget:F2} -> ${exitPrice:F2} for quick exit", ConsoleColor.Yellow);
                        ModifyTakeProfitOrder(exitPrice);
                        takeProfitTarget = exitPrice;
                    }
                }
                else
                {
                    Log($"ADX ROLLOVER detected (ADX {adxPeakValue:F1} -> {currentAdx:F1}), but position not profitable. Holding.", ConsoleColor.DarkYellow);
                }
            }
        }

        /// <summary>
        /// Calculates a market score (-100 to +100) based on multiple indicators.
        /// Uses MarketScoreCalculator (SINGLE SOURCE OF TRUTH) for consistent scoring.
        /// Calculates market score for autonomous trading decisions.
        /// Positive = bullish, Negative = bearish.
        /// </summary>
        private MarketScore CalculateMarketScore(double price, double vwap, bool isLong)
        {
            var order = strategy.Order;
            var config = order.AdaptiveOrder!;

            // Get indicator weights (from indicator-config.json with dynamic redistribution)
            var weights = IndicatorConfigManager.GetWeights();
            
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

            // For short positions, invert the score
            if (!isLong)
                finalScore = -finalScore;

            // Calculate adjustment multipliers based on score
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
                BollingerScore = bollingerScore,
                TakeProfitMultiplier = tpMultiplier,
                StopLossMultiplier = slMultiplier,
                ShouldEmergencyExit = shouldExit
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
            
            return Math.Clamp(baseMultiplier, 0.5, 2.0);
        }

        /// <summary>
        /// Calculates the stop loss multiplier based on market score.
        /// </summary>
        private static double CalculateStopLossMultiplier(int score, AdaptiveOrderConfig config)
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
            
            return Math.Clamp(baseMultiplier, 0.5, 2.0);
        }

        /// <summary>
        /// Modifies an existing take profit order with a new price.
        /// </summary>
        private void ModifyTakeProfitOrder(double newPrice)
        {
            if (takeProfitOrderId <= 0) return;

            // Use effective quantity (already calculated at entry time)
            int qty = effectiveQuantity > 0 ? effectiveQuantity : GetEffectiveQuantity(lastPrice);

            var tpOrder = new Order
            {
                Action = strategy.Order.Side == OrderSide.Buy ? "SELL" : "BUY",
                OrderType = "LMT",
                LmtPrice = newPrice,
                TotalQuantity = qty,
                OutsideRth = strategy.Order.TakeProfitOutsideRth,
                Tif = "GTC"
            };

            if (!string.IsNullOrEmpty(AppSettings.AccountNumber))
                tpOrder.Account = AppSettings.AccountNumber;

            wrapper.ModifyOrder(takeProfitOrderId, contract, tpOrder);
        }

        /// <summary>
        /// Modifies an existing stop loss order with a new price.
        /// </summary>
        private void ModifyStopLossOrder(double newPrice)
        {
            if (stopLossOrderId <= 0) return;

            // Use effective quantity (already calculated at entry time)
            int slQty = effectiveQuantity > 0 ? effectiveQuantity : GetEffectiveQuantity(lastPrice);

            var slOrder = new Order
            {
                Action = strategy.Order.Side == OrderSide.Buy ? "SELL" : "BUY",
                OrderType = "STP",
                AuxPrice = newPrice,
                TotalQuantity = slQty,
                OutsideRth = true,
                Tif = "GTC"
            };

            if (!string.IsNullOrEmpty(AppSettings.AccountNumber))
                slOrder.Account = AppSettings.AccountNumber;

            wrapper.ModifyOrder(stopLossOrderId, contract, slOrder);
        }

        /// <summary>
        /// Executes an emergency exit when market conditions are severely against the position.
        /// </summary>
        private void ExecuteEmergencyExit()
        {
            if (isComplete) return;

            // Cancel existing orders (only if not rejected)
            if (takeProfitOrderId > 0 && !takeProfitFilled && !takeProfitCancelled && !takeProfitOrderRejected)
            {
                Log($"Cancelling take profit order #{takeProfitOrderId} for emergency exit...", ConsoleColor.Yellow);
                client.cancelOrder(takeProfitOrderId, new OrderCancel());
                takeProfitCancelled = true;
            }

            if (stopLossOrderId > 0 && !stopLossFilled && !stopLossOrderRejected)
            {
                Log($"Cancelling stop loss order #{stopLossOrderId} for emergency exit...", ConsoleColor.Yellow);
                client.cancelOrder(stopLossOrderId, new OrderCancel());
            }

            // Submit market exit order
            int exitOrderId = wrapper.ConsumeNextOrderId();
            string action = strategy.Order.Side == OrderSide.Buy ? "SELL" : "BUY";

            // Use effective quantity (already calculated at entry time)
            int emergencyQty = effectiveQuantity > 0 ? effectiveQuantity : GetEffectiveQuantity(lastPrice);

            var exitOrder = new Order
            {
                Action = action,
                OrderType = "MKT",
                TotalQuantity = emergencyQty,
                OutsideRth = true,
                Tif = "GTC"
            };

            if (!string.IsNullOrEmpty(AppSettings.AccountNumber))
                exitOrder.Account = AppSettings.AccountNumber;

            Log($">> EMERGENCY EXIT: {action} {emergencyQty} @ MKT", ConsoleColor.Red);
            client.placeOrder(exitOrderId, contract, exitOrder);

            isComplete = true;
            result = StrategyResult.EmergencyExit;
        }

        /// <summary>
        /// Resets the strategy state for a new trading day.
        /// Called automatically at midnight to allow strategies to run again.
        /// </summary>
        private void ResetForNewDay(DateOnly newDate)
        {
            // Only reset if we haven't filled an entry order (don't reset mid-trade)
            if (entryFilled)
            {
                Log($"New day detected but position is open - not resetting", ConsoleColor.DarkYellow);
                lastCheckedDate = newDate;
                return;
            }

            Log($"*** MIDNIGHT RESET - New trading day detected, resetting strategy ***", ConsoleColor.Cyan);

            // Reset condition tracking
            currentConditionIndex = 0;
            isComplete = false;

            // Reset VWAP accumulators for new session
            pvSum = 0;
            vSum = 0;

            // Reset session tracking
            sessionHigh = 0;
            sessionLow = double.MaxValue;
            
            // Reset trend direction filter for new session
            trendFilter.Reset();

            // Reset logging flags
            waitingForWindowLogged = false;
            windowEndedLogged = false;

            // Reset result
            result = StrategyResult.Running;

            // Update the date
            lastCheckedDate = newDate;

            // Log next window time
            if (strategy.StartTime.HasValue)
            {
                var info = TimezoneHelper.GetTimezoneDisplayInfo(AppSettings.Timezone);
                var startLocal = TimezoneHelper.ToLocal(strategy.StartTime.Value, AppSettings.Timezone);
                Log($"Will start monitoring at {strategy.StartTime.Value:h:mm tt} ET ({startLocal:h:mm tt} {info.Abbreviation})", ConsoleColor.DarkGray);
            }
        }

        private double GetVwap()
        {
            return vSum > 0 ? pvSum / vSum : 0;
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
                Price.Current => lastPrice,
                Price.VWAP => vwap > 0 ? vwap : lastPrice,
                Price.Bid => lastBid > 0 ? lastBid : lastPrice,
                Price.Ask => lastAsk > 0 ? lastAsk : lastPrice,
                _ => lastPrice
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
            cancelTimer?.Dispose();
            cancelTimer = null;

            overnightCancelTimer?.Dispose();
            overnightCancelTimer = null;

            closePositionTimer?.Dispose();
            closePositionTimer = null;

            // Reset condition tracking
            currentConditionIndex = 0;
            isComplete = false;

            // Reset order tracking
            entryOrderId = -1;
            entryFilled = false;
            entryFillPrice = 0;

            // Reset take profit tracking
            takeProfitOrderId = -1;
            takeProfitFilled = false;
            takeProfitCancelled = false;
            takeProfitTarget = 0;

            // Reset stop loss tracking
            stopLossOrderId = -1;
            stopLossFilled = false;

            // Reset exit tracking
            exitedWithProfit = false;
            exitFillPrice = 0;

            // Reset trailing stop loss tracking
            trailingStopLossOrderId = -1;
            trailingStopLossTriggered = false;
            trailingStopLossPrice = 0;
            highWaterMark = 0;

            // Reset adaptive order tracking
            lastAdaptiveAdjustmentTime = DateTime.MinValue;
            lastAdaptiveScore = 0;
            originalTakeProfitPrice = 0;
            originalStopLossPrice = 0;
            currentAdaptiveTakeProfitPrice = 0;
            currentAdaptiveStopLossPrice = 0;

            // Reset close position tracking
            closePositionTriggered = false;
            closePositionOrderId = -1;

            // Reset result
            result = StrategyResult.Running;

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
            var order = strategy.Order;

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
                isComplete = true;
                result = StrategyResult.MissedTheBoat;

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
            if (currentConditionIndex >= strategy.Conditions.Count)
                return;

            // Check if we're within the time window (times are in Eastern)
            var currentTimeET = TimezoneHelper.GetCurrentTime(MarketTimeZone.EST);
            var todayDate = DateOnly.FromDateTime(DateTime.Today);

            // Check for midnight reset - reset strategy state for a new trading day
            if (todayDate > lastCheckedDate)
            {
                ResetForNewDay(todayDate);
            }

            // If StartTime is set and we haven't reached it yet, don't evaluate
            if (strategy.StartTime.HasValue && currentTimeET < strategy.StartTime.Value)
            {
                if (!waitingForWindowLogged)
                {
                    waitingForWindowLogged = true;
                    var info = TimezoneHelper.GetTimezoneDisplayInfo(AppSettings.Timezone);
                    var startLocal = TimezoneHelper.ToLocal(strategy.StartTime.Value, AppSettings.Timezone);
                    Log($"Not monitoring yet - will start at {strategy.StartTime.Value:h:mm tt} ET ({startLocal:h:mm tt} {info.Abbreviation})", ConsoleColor.DarkYellow);
                }
                return;
            }

            // If EndTime is set and we've passed it, wait for tomorrow (don't mark as complete)
            if (strategy.EndTime.HasValue && currentTimeET > strategy.EndTime.Value)
            {
                if (!windowEndedLogged)
                {
                    windowEndedLogged = true;
                    var info = TimezoneHelper.GetTimezoneDisplayInfo(AppSettings.Timezone);
                    var startLocal = strategy.StartTime.HasValue
                        ? TimezoneHelper.ToLocal(strategy.StartTime.Value, AppSettings.Timezone)
                        : new TimeOnly(4, 0);
                    var startET = strategy.StartTime ?? new TimeOnly(4, 0);
                    Log($"Strategy window ended at {strategy.EndTime.Value:h:mm tt} ET - will resume tomorrow at {startET:h:mm tt} ET ({startLocal:h:mm tt} {info.Abbreviation})", ConsoleColor.Yellow);
                }
                return;
            }

            var condition = strategy.Conditions[currentConditionIndex];

            if (condition.Evaluate(price, vwap))
            {
                currentConditionIndex++;

                // Build additional context info for EMA conditions
                string emaInfo = GetEmaContextInfo(condition);

                // Check if all conditions are met
                if (currentConditionIndex >= strategy.Conditions.Count)
                {
                    Log($"[OK] STEP {currentConditionIndex}/{strategy.Conditions.Count}: {condition.Name} - TRIGGERED @ ${price:F2}{emaInfo}", ConsoleColor.Green);

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
                    var nextCondition = strategy.Conditions[currentConditionIndex];
                    Log($"[OK] STEP {currentConditionIndex}/{strategy.Conditions.Count}: {condition.Name} - TRIGGERED @ ${price:F2}{emaInfo}", ConsoleColor.Green);
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
            var order = strategy.Order;
            entryOrderId = wrapper.ConsumeNextOrderId();

            // Determine if we're in regular trading hours
            var currentTimeET = TimezoneHelper.GetCurrentTime(MarketTimeZone.EST);
            bool isRTH = currentTimeET >= new TimeOnly(9, 30) && currentTimeET < new TimeOnly(16, 0);

            // IBKR requires LIMIT orders outside RTH (premarket/after-hours)
            // Force LIMIT if user specified MARKET but we're outside RTH
            bool forceLimitOrder = !isRTH && order.Type == OrderType.Market;
            string effectiveOrderType = forceLimitOrder ? "LMT" : order.GetIbOrderType();

            // For autonomous trading, use tracked direction; otherwise use order's side
            string entryAction = strategy.Order.UseAutonomousTrading ? GetOpenAction() : order.GetIbAction();

            // Get effective quantity (auto-calculated if UseAutoQuantity is true)
            int qty = GetEffectiveQuantity(lastPrice);

            var ibOrder = new Order
            {
                Action = entryAction,
                OrderType = effectiveOrderType,
                TotalQuantity = qty,
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
            Log($"  OrderId={entryOrderId} | TIF={tifDesc} | OutsideRTH={ibOrder.OutsideRth}{aonStr}", ConsoleColor.DarkGray);

            // Log to session logger
            SessionLogger?.LogOrder(strategy.Symbol, order.Side.ToString(), order.Quantity, ibOrder.LmtPrice, entryOrderId.ToString());

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

            client.placeOrder(entryOrderId, contract, ibOrder);
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
            strategy.Order.UseAutonomousTrading ? isLong : strategy.Order.Side == OrderSide.Buy;

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
            overnightCancelTimer = new Timer(OvernightCancelCallback, null, delay, Timeout.InfiniteTimeSpan);
        }

        /// <summary>
        /// Callback triggered at market open to cancel unfilled overnight orders.
        /// </summary>
        private void OvernightCancelCallback(object? state)
        {
            // Thread-safe check
            lock (disposeLock)
            {
                if (disposed || entryFilled || entryOrderId < 0)
                    return;
            }

            Log($"OVERNIGHT ORDER EXPIRED: Cancelling unfilled entry order #{entryOrderId} at market open", ConsoleColor.Yellow);
            client.cancelOrder(entryOrderId, new OrderCancel());

            isComplete = true;
            result = StrategyResult.EntryCancelled;
            PrintFinalResult();
        }

        private void OnOrderFill(int orderId, double fillPrice, int fillSize)
        {
            // Check for entry order fill
            if (orderId == entryOrderId && !entryFilled)
            {
                entryFilled = true;
                entryFillPrice = fillPrice;

                Log($"[OK] ENTRY FILLED @ ${fillPrice:F2} ({fillSize} shares)", ConsoleColor.Green);
                string entryAction = IsPositionLong() ? "BUY" : "SELL";
                SessionLogger?.LogFill(strategy.Symbol, entryAction, fillSize, fillPrice);
                SessionLogger?.UpdateStrategyStatus(strategy.Symbol, "Position Open", $"Entry @ ${fillPrice:F2}");

                // Check if FlipMode is enabled - no TP/SL orders, just monitor and flip
                bool isFlipMode = strategy.Order.UseAutonomousTrading && 
                                  strategy.Order.AutonomousTrading?.UseFlipMode == true;

                if (isFlipMode)
                {
                    Log($"[FLIP MODE] No TP/SL orders - monitoring for reversal signals", ConsoleColor.Magenta);
                    // In FlipMode, we don't submit TP/SL - the HandleAutonomousExit will flip direction
                    return;
                }

                // Handle take profit (for both long and short positions)
                // Autonomous trading always enables TP/SL with pre-calculated values
                bool enableTp = strategy.Order.EnableTakeProfit || 
                               (strategy.Order.UseAutonomousTrading && dynamicTakeProfit > 0);
                if (enableTp)
                {
                    SubmitTakeProfit(fillPrice);
                }

                // Handle stop loss (check global setting first)
                bool enableSl = AppSettings.UseStopLoss && 
                               (strategy.Order.EnableStopLoss ||
                               (strategy.Order.UseAutonomousTrading && dynamicStopLoss > 0));
                if (enableSl)
                {
                    SubmitStopLoss(fillPrice);
                }

                // Initialize adaptive order tracking if enabled
                if (strategy.Order.UseAdaptiveOrder)
                {
                    // Store original prices for adaptive adjustments
                    originalTakeProfitPrice = takeProfitTarget;
                    currentAdaptiveTakeProfitPrice = takeProfitTarget;

                    if (strategy.Order.StopLossPrice.HasValue)
                    {
                        originalStopLossPrice = strategy.Order.StopLossPrice.Value;
                        currentAdaptiveStopLossPrice = strategy.Order.StopLossPrice.Value;
                    }
                    else if (strategy.Order.StopLossOffset > 0)
                    {
                        bool isLong = IsPositionLong();
                        originalStopLossPrice = isLong 
                            ? fillPrice - strategy.Order.StopLossOffset
                            : fillPrice + strategy.Order.StopLossOffset;
                        currentAdaptiveStopLossPrice = originalStopLossPrice;
                    }

                    Log($"ADAPTIVE ORDER ENABLED: TP=${originalTakeProfitPrice:F2}, SL=${originalStopLossPrice:F2} ({strategy.Order.AdaptiveOrder!.Mode})", ConsoleColor.Cyan);
                }

                // Initialize trailing stop loss tracking (check global setting first)
                if (AppSettings.UseTrailingStopLoss && strategy.Order.EnableTrailingStopLoss)
                {
                    highWaterMark = fillPrice;
                    bool isLong = IsPositionLong();

                    // Calculate initial trailing stop - use ATR if configured, otherwise percentage
                    string stopDescription;
                    if (strategy.Order.UseAtrStopLoss && atrCalculator != null && atrCalculator.IsReady)
                    {
                        var atrConfig = strategy.Order.AtrStopLoss!;
                        trailingStopLossPrice = atrCalculator.CalculateStopPrice(
                            referencePrice: fillPrice,
                            multiplier: atrConfig.Multiplier,
                            isLong: isLong,
                            minPercent: atrConfig.MinStopPercent,
                            maxPercent: atrConfig.MaxStopPercent
                        );
                        double atrValue = atrCalculator.CurrentAtr;
                        double stopDistance = isLong ? fillPrice - trailingStopLossPrice : trailingStopLossPrice - fillPrice;
                        stopDescription = $"{atrConfig.Multiplier:F1}x ATR (ATR=${atrValue:F2}, Distance=${stopDistance:F2})";
                    }
                    else
                    {
                        trailingStopLossPrice = Math.Round(fillPrice * (1 - strategy.Order.TrailingStopLossPercent), 2);
                        stopDescription = $"{strategy.Order.TrailingStopLossPercent * 100:F1}% below entry";
                    }

                    Log($"TRAILING STOP INITIALIZED: ${trailingStopLossPrice:F2} ({stopDescription})", ConsoleColor.Magenta);
                }

                // Schedule close position if configured
                if (strategy.Order.ClosePositionTime.HasValue)
                {
                    ScheduleClosePosition(strategy.Order.ClosePositionTime.Value, strategy.Order.ClosePositionOnlyIfProfitable);
                }

                Log($"  Monitoring position... Entry=${fillPrice:F2}", ConsoleColor.DarkGray);
                return;
            }

            // Check for trailing stop loss order fill
            if (orderId == trailingStopLossOrderId && trailingStopLossTriggered)
            {
                exitFillPrice = fillPrice;

                // Record trade for learning
                CompletePendingTradeRecord(fillPrice);

                double pnl = strategy.Order.Quantity * (fillPrice - entryFillPrice);
                result = StrategyResult.TrailingStopLossFilled;

                if (pnl >= 0)
                {
                    Log($"*** TRAILING STOP LOSS FILLED @ ${fillPrice:F2} | P&L: ${pnl:F2} (protected profit)", ConsoleColor.Yellow);
                }
                else
                {
                    Log($"*** TRAILING STOP LOSS FILLED @ ${fillPrice:F2} | P&L: ${pnl:F2} (limited loss)", ConsoleColor.Red);
                }
                SessionLogger?.LogFill(strategy.Symbol, "SELL", strategy.Order.Quantity, fillPrice, pnl);
                SessionLogger?.UpdateStrategyStatus(strategy.Symbol, "Trailing Stop", $"Exit @ ${fillPrice:F2} P&L=${pnl:F2}");

                // Cancel take profit if still active (and wasn't rejected)
                if (takeProfitOrderId > 0 && !takeProfitFilled && !takeProfitCancelled && !takeProfitOrderRejected)
                {
                    client.cancelOrder(takeProfitOrderId, new OrderCancel());
                    takeProfitCancelled = true;
                }

                // For autonomous trading, cycle to next trade instead of stopping
                if (strategy.Order.UseAutonomousTrading)
                {
                    var config = strategy.Order.AutonomousTrading;
                    bool positionWasLong = isLong;
                    
                    // Direction flip on trailing stop - TSL triggered means trend reversed
                    // Flip direction: If LONG TSL hit → go SHORT (price dropping)
                    //                 If SHORT TSL hit → go LONG (price rising)
                    if (config != null && config.AllowDirectionFlip)
                    {
                        bool canFlipShort = config.AllowShort && !shortSaleBlocked;
                        double vwap = pvSum / Math.Max(vSum, 1);
                        
                        if (positionWasLong && canFlipShort)
                        {
                            string profitType = pnl >= 0 ? "profit protected" : "loss limited";
                            Log($"[AUTONOMOUS] TSL triggered ({profitType}) - FLIPPING TO SHORT (price falling)", ConsoleColor.Magenta);
                            ResetForNextAutonomousTrade();
                            
                            var (tp, sl) = CalculateAutonomousTpSl(lastPrice, false, config);
                            ExecuteAutonomousEntry(lastPrice, vwap, false, tp, sl, config);
                            return;
                        }
                        else if (!positionWasLong)
                        {
                            string profitType = pnl >= 0 ? "profit protected" : "loss limited";
                            Log($"[AUTONOMOUS] TSL triggered ({profitType}) - FLIPPING TO LONG (price rising)", ConsoleColor.Magenta);
                            ResetForNextAutonomousTrade();
                            
                            var (tp, sl) = CalculateAutonomousTpSl(lastPrice, true, config);
                            ExecuteAutonomousEntry(lastPrice, vwap, true, tp, sl, config);
                            return;
                        }
                    }
                    
                    Log($"[AUTONOMOUS] Trailing stop hit, cycling to next trade...", ConsoleColor.Cyan);
                    ResetForNextAutonomousTrade();
                    return;
                }

                isComplete = true;
                PrintFinalResult();
                return;
            }

            // Check for fixed stop loss order fill
            if (orderId == stopLossOrderId && !stopLossFilled)
            {
                stopLossFilled = true;
                exitFillPrice = fillPrice;

                // Record trade for learning
                CompletePendingTradeRecord(fillPrice);

                double pnl = strategy.Order.Quantity * (fillPrice - entryFillPrice);
                result = StrategyResult.StopLossFilled;

                Log($"*** STOP LOSS FILLED @ ${fillPrice:F2} | P&L: ${pnl:F2}", ConsoleColor.Red);
                SessionLogger?.LogFill(strategy.Symbol, "SELL", strategy.Order.Quantity, fillPrice, pnl);
                SessionLogger?.UpdateStrategyStatus(strategy.Symbol, "Stop Loss", $"Exit @ ${fillPrice:F2} P&L=${pnl:F2}");

                // Cancel take profit if still active (and wasn't rejected)
                if (takeProfitOrderId > 0 && !takeProfitFilled && !takeProfitCancelled && !takeProfitOrderRejected)
                {
                    client.cancelOrder(takeProfitOrderId, new OrderCancel());
                    takeProfitCancelled = true;
                }

                // For autonomous trading, cycle to next trade instead of stopping
                if (strategy.Order.UseAutonomousTrading)
                {
                    var config = strategy.Order.AutonomousTrading;
                    bool positionWasLong = isLong;
                    
                    // Direction flip on stop loss - market proved us wrong, flip direction
                    if (config != null && config.AllowDirectionFlip)
                    {
                        bool canFlipShort = config.AllowShort && !shortSaleBlocked;
                        
                        if (positionWasLong && canFlipShort)
                        {
                            Log($"[AUTONOMOUS] Stop loss hit - FLIPPING TO SHORT (market bearish)", ConsoleColor.Magenta);
                            ResetForNextAutonomousTrade();
                            
                            // Execute short entry at current price
                            var (tp, sl) = CalculateAutonomousTpSl(lastPrice, false, config);
                            ExecuteAutonomousEntry(lastPrice, pvSum / Math.Max(vSum, 1), false, tp, sl, config);
                            return;
                        }
                        else if (!positionWasLong)
                        {
                            Log($"[AUTONOMOUS] Stop loss hit - FLIPPING TO LONG (market bullish)", ConsoleColor.Magenta);
                            ResetForNextAutonomousTrade();
                            
                            // Execute long entry at current price
                            var (tp, sl) = CalculateAutonomousTpSl(lastPrice, true, config);
                            ExecuteAutonomousEntry(lastPrice, pvSum / Math.Max(vSum, 1), true, tp, sl, config);
                            return;
                        }
                    }
                    
                    Log($"[AUTONOMOUS] Stop loss hit, cycling to next trade...", ConsoleColor.Cyan);
                    ResetForNextAutonomousTrade();
                    return;
                }

                isComplete = true;
                PrintFinalResult();
                return;
            }

            // Check for take profit order fill (or early exit fill)
            if (orderId == takeProfitOrderId && !takeProfitFilled)
            {
                takeProfitFilled = true;
                exitFillPrice = fillPrice;

                // Record trade for learning
                CompletePendingTradeRecord(fillPrice);

                double pnl = strategy.Order.Quantity * (fillPrice - entryFillPrice);

                // Cancel stop loss if still active (and wasn't rejected)
                if (stopLossOrderId > 0 && !stopLossFilled && !stopLossOrderRejected)
                {
                    client.cancelOrder(stopLossOrderId, new OrderCancel());
                }

                // Determine if this was the original TP or an early exit
                if (exitedWithProfit)
                {
                    result = StrategyResult.ExitedWithProfit;
                    Log($"*** EXITED WITH PROFIT (time limit) @ ${fillPrice:F2} | P&L: ${pnl:F2}", ConsoleColor.Cyan);
                    SessionLogger?.LogFill(strategy.Symbol, "SELL", strategy.Order.Quantity, fillPrice, pnl);
                    SessionLogger?.UpdateStrategyStatus(strategy.Symbol, "Timed Exit", $"Exit @ ${fillPrice:F2} P&L=${pnl:F2}");
                }
                else
                {
                    result = StrategyResult.TakeProfitFilled;
                    Log($"*** TAKE PROFIT FILLED @ ${fillPrice:F2} | P&L: ${pnl:F2}", ConsoleColor.Green);
                    SessionLogger?.LogFill(strategy.Symbol, "SELL", strategy.Order.Quantity, fillPrice, pnl);
                    SessionLogger?.UpdateStrategyStatus(strategy.Symbol, "Take Profit", $"Exit @ ${fillPrice:F2} P&L=${pnl:F2}");
                }

                // For autonomous trading, cycle to next trade instead of stopping
                if (strategy.Order.UseAutonomousTrading)
                {
                    Log($"[AUTONOMOUS] Trade complete, cycling to next trade...", ConsoleColor.Cyan);
                    ResetForNextAutonomousTrade();
                    return;
                }

                // Show final result
                isComplete = true;
                PrintFinalResult();
                return;
            }

            // Check for autonomous exit order fill (score-based exit)
            if (orderId == exitOrderId)
            {
                exitFillPrice = fillPrice;

                double pnl = strategy.Order.Quantity * (fillPrice - entryFillPrice);
                var color = pnl >= 0 ? ConsoleColor.Green : ConsoleColor.Red;
                Log($"*** AUTONOMOUS EXIT FILLED @ ${fillPrice:F2} | P&L: ${pnl:F2}", color);
                SessionLogger?.LogFill(strategy.Symbol, "SELL", strategy.Order.Quantity, fillPrice, pnl);
                SessionLogger?.UpdateStrategyStatus(strategy.Symbol, "Autonomous Exit", $"P&L=${pnl:F2}");

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
            if (orderId == takeProfitOrderId && !takeProfitFilled)
            {
                takeProfitOrderRejected = true;
                Log($"[ERR] Take profit order #{orderId} rejected: {errorMessage}", ConsoleColor.Red);
                return;
            }

            // Check if SL order was rejected
            if (orderId == stopLossOrderId && !stopLossFilled)
            {
                stopLossOrderRejected = true;
                Log($"[ERR] Stop loss order #{orderId} rejected: {errorMessage}", ConsoleColor.Red);
                return;
            }

            // Check if entry order was rejected
            if (orderId == entryOrderId && !entryFilled)
            {
                Log($"[ERR] Entry order #{orderId} rejected: {errorMessage}", ConsoleColor.Red);
                
                // Check for short sale rejection (error codes 4108, 4110)
                // 4108 = Contract not available for short sale
                // 4110 = No trading permission / Small cap restriction
                if (errorCode == 4108 || errorCode == 4110 || 
                    errorMessage.Contains("short sale", StringComparison.OrdinalIgnoreCase) ||
                    errorMessage.Contains("Small Cap", StringComparison.OrdinalIgnoreCase))
                {
                    shortSaleBlocked = true;
                    Log($"[WARN] SHORT SALE BLOCKED for {strategy.Symbol} - future short entries will be skipped", ConsoleColor.Red);
                }
                
                // For autonomous trading, log the rejection and reset for next attempt
                if (strategy.Order.UseAutonomousTrading)
                {
                    Log($"[AUTONOMOUS] Entry rejected, will retry when conditions are met...", ConsoleColor.Yellow);
                    entryOrderId = -1;  // Reset to allow new entry attempt
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
            cancelTimer?.Dispose();
            cancelTimer = null;

            overnightCancelTimer?.Dispose();
            overnightCancelTimer = null;

            closePositionTimer?.Dispose();
            closePositionTimer = null;

            // Reset condition tracking (autonomous trading calculates its own entry)
            currentConditionIndex = strategy.Conditions.Count; // Stay at "all conditions met" for autonomous
            isComplete = false;

            // Reset order tracking
            entryOrderId = -1;
            entryFilled = false;
            entryFillPrice = 0;

            // Reset take profit tracking
            takeProfitOrderId = -1;
            takeProfitFilled = false;
            takeProfitCancelled = false;
            takeProfitOrderRejected = false;
            takeProfitTarget = 0;

            // Reset stop loss tracking
            stopLossOrderId = -1;
            stopLossFilled = false;
            stopLossOrderRejected = false;

            // Reset exit tracking
            exitedWithProfit = false;
            exitFillPrice = 0;

            // Reset trailing stop loss tracking
            trailingStopLossOrderId = -1;
            trailingStopLossTriggered = false;
            trailingStopLossPrice = 0;
            highWaterMark = 0;

            // Reset adaptive order tracking
            lastAdaptiveAdjustmentTime = DateTime.MinValue;
            lastAdaptiveScore = 0;
            originalTakeProfitPrice = 0;
            originalStopLossPrice = 0;
            currentAdaptiveTakeProfitPrice = 0;
            currentAdaptiveStopLossPrice = 0;

            // Reset autonomous exit tracking
            exitOrderId = -1;
            dynamicTakeProfit = 0;
            dynamicStopLoss = 0;
            // Note: isLong is NOT reset - it will be set on next entry

            // Reset close position tracking
            closePositionTriggered = false;
            closePositionOrderId = -1;

            // Reset pending trade record
            pendingTradeRecord = null;
            entryScore = null;

            // Reset result
            result = StrategyResult.Running;

            // DON'T reset VWAP, session high/low, or indicator calculators
            // These should continue accumulating throughout the session

            Log($"*** AUTONOMOUS CYCLE: Ready for next trade signal ***", ConsoleColor.Magenta);
        }

        private void SubmitTakeProfit(double entryPrice)
        {
            var order = strategy.Order;
            takeProfitOrderId = wrapper.ConsumeNextOrderId();

            double tpPrice;
            bool isLong = IsPositionLong();

            // For autonomous trading, use pre-calculated TP target
            if (strategy.Order.UseAutonomousTrading && dynamicTakeProfit > 0)
            {
                tpPrice = dynamicTakeProfit;
            }
            // Check for ADX-based dynamic take profit
            else if (order.AdxTakeProfit != null && adxCalculator != null && adxCalculator.IsReady)
            {
                double currentAdx = adxCalculator.CurrentAdx;
                tpPrice = order.AdxTakeProfit.GetTargetForAdx(currentAdx);
                string trendStr = order.AdxTakeProfit.GetTrendStrength(currentAdx);
                Log($"ADX-BASED TP: ADX={currentAdx:F1} ({trendStr})", ConsoleColor.Cyan);
                Log($"  Conservative=${order.AdxTakeProfit.ConservativeTarget:F2}, Aggressive=${order.AdxTakeProfit.AggressiveTarget:F2}", ConsoleColor.DarkGray);
                Log($"  Selected target: ${tpPrice:F2}", ConsoleColor.Cyan);

                // Track ADX peak for rollover detection
                adxPeakValue = currentAdx;
                adxRolledOver = false;
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
            takeProfitTarget = tpPrice;

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
            Log($"  OrderId={takeProfitOrderId} | OutsideRTH={order.TakeProfitOutsideRth}", ConsoleColor.DarkGray);

            client.placeOrder(takeProfitOrderId, contract, tpOrder);

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

            cancelTimer = new Timer(CancelTakeProfitCallback, null, delay, Timeout.InfiniteTimeSpan);
        }

        private void CancelTakeProfitCallback(object? state)
        {
            // Thread-safe check - lock to prevent race with Dispose
            lock (disposeLock)
            {
                if (disposed || takeProfitFilled || takeProfitCancelled || takeProfitOrderId < 0)
                    return;

                takeProfitCancelled = true;
            }

            // Check if position is profitable
            bool isLong = strategy.Order.Side == OrderSide.Buy;
            bool isProfitable = isLong ? lastPrice > entryFillPrice : lastPrice < entryFillPrice;

            if (entryFilled && isProfitable)
            {
                double unrealizedPnl = strategy.Order.Quantity * (lastPrice - entryFillPrice);

                Log($"TIME LIMIT REACHED - Position is profitable!", ConsoleColor.Cyan);
                Log($"  Entry=${entryFillPrice:F2} | Current=${lastPrice:F2} | Unrealized P&L=${unrealizedPnl:F2}", ConsoleColor.Cyan);
                Log($"  Cancelling TP order #{takeProfitOrderId} and selling at limit...", ConsoleColor.Yellow);

                client.cancelOrder(takeProfitOrderId, new OrderCancel());

                // Mark that we're exiting early with profit
                exitedWithProfit = true;

                // Submit limit sell order slightly below current price for quick fill in premarket
                SubmitLimitExit();
            }
            else
            {
                Log($"TIME LIMIT REACHED - Position NOT profitable", ConsoleColor.Yellow);
                Log($"  Entry=${entryFillPrice:F2} | Current=${lastPrice:F2} | Cancelling TP order", ConsoleColor.Yellow);
                Log($"  WARNING: STILL HOLDING {strategy.Order.Quantity} SHARES - manage manually!", ConsoleColor.Red);

                client.cancelOrder(takeProfitOrderId, new OrderCancel());

                // Set result - still holding position
                result = StrategyResult.TakeProfitCancelled;
                isComplete = true;
                PrintFinalResult();
            }
        }

        private void SubmitLimitExit()
        {
            var order = strategy.Order;
            int exitOrderId = wrapper.ConsumeNextOrderId();

            string action = order.Side == OrderSide.Buy ? "SELL" : "BUY";

            // Set limit price slightly below current price for sells (or above for buys) to ensure quick fill in premarket
            double offset = 0.02;
            double limitPrice = order.Side == OrderSide.Buy
                ? Math.Round(lastPrice - offset, 2)  // Selling long position: slightly below current
                : Math.Round(lastPrice + offset, 2); // Covering short position: slightly above current

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
            takeProfitOrderId = exitOrderId;
            takeProfitCancelled = false; // Reset so we can track the fill

            client.placeOrder(exitOrderId, contract, exitOrder);
        }

        private void SubmitStopLoss(double entryPrice)
        {
            var order = strategy.Order;
            stopLossOrderId = wrapper.ConsumeNextOrderId();

            double slPrice;
            bool isLong = IsPositionLong();

            // For autonomous trading, use pre-calculated SL target
            if (strategy.Order.UseAutonomousTrading && dynamicStopLoss > 0)
            {
                slPrice = dynamicStopLoss;
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
            Log($"  OrderId={stopLossOrderId}", ConsoleColor.DarkGray);

            client.placeOrder(stopLossOrderId, contract, slOrder);
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

            closePositionTimer = new Timer(ClosePositionCallback, onlyIfProfitable, delay, Timeout.InfiniteTimeSpan);
        }

        /// <summary>
        /// Callback triggered at ClosePositionTime to close the position.
        /// </summary>
        private void ClosePositionCallback(object? state)
        {
            bool onlyIfProfitable = state is bool b && b;

            // Thread-safe check
            lock (disposeLock)
            {
                if (disposed || isComplete || !entryFilled || closePositionTriggered)
                    return;

                closePositionTriggered = true;
            }

            // Check if position is profitable
            bool isLong = strategy.Order.Side == OrderSide.Buy;
            bool isProfitable = isLong ? lastPrice > entryFillPrice : lastPrice < entryFillPrice;
            double unrealizedPnl = strategy.Order.Quantity * (lastPrice - entryFillPrice);
            if (!isLong) unrealizedPnl = -unrealizedPnl;

            Log($"*** CLOSE POSITION TIME REACHED ***", ConsoleColor.Cyan);
            Log($"  Entry=${entryFillPrice:F2} | Current=${lastPrice:F2} | Unrealized P&L=${unrealizedPnl:F2}", ConsoleColor.Cyan);

            if (onlyIfProfitable && !isProfitable)
            {
                Log($"  Position is NOT profitable - keeping position open", ConsoleColor.Yellow);
                Log($"  WARNING: STILL HOLDING {strategy.Order.Quantity} SHARES - will rely on stop loss or manual exit", ConsoleColor.Red);
                return;
            }

            // Cancel any open take profit order (if not rejected)
            if (takeProfitOrderId > 0 && !takeProfitFilled && !takeProfitCancelled && !takeProfitOrderRejected)
            {
                Log($"  Cancelling take profit order #{takeProfitOrderId}...", ConsoleColor.Yellow);
                client.cancelOrder(takeProfitOrderId, new OrderCancel());
                takeProfitCancelled = true;
            }

            // Submit close position order
            SubmitClosePositionOrder(isProfitable);
        }

        /// <summary>
        /// Submits an order to close the position at current price.
        /// </summary>
        private void SubmitClosePositionOrder(bool isProfitable)
        {
            var order = strategy.Order;
            closePositionOrderId = wrapper.ConsumeNextOrderId();

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
                    ? Math.Round(lastPrice - offset, 2)  // Selling long: slightly below current
                    : Math.Round(lastPrice + offset, 2); // Covering short: slightly above current

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

            Log($"  OrderId={closePositionOrderId}", ConsoleColor.DarkGray);

            // Track fill using the take profit order slot (it's already cancelled)
            takeProfitOrderId = closePositionOrderId;
            takeProfitCancelled = false;
            exitedWithProfit = isProfitable;

            client.placeOrder(closePositionOrderId, contract, closeOrder);
        }

        public void Dispose()
        {
            lock (disposeLock)
            {
                if (disposed) return;
                disposed = true;
            }

            // Dispose AI advisor
            aiAdvisor?.Dispose();
            aiAdvisor = null;

            // Dispose timers first to prevent callbacks after disposal starts
            cancelTimer?.Dispose();
            cancelTimer = null;

            overnightCancelTimer?.Dispose();
            overnightCancelTimer = null;

            closePositionTimer?.Dispose();
            closePositionTimer = null;

            // Unsubscribe from events to prevent memory leaks
            wrapper.OnOrderFill -= OnOrderFill;
            candlestickAggregator.OnCandleComplete -= OnCandleComplete;

            // Note: Do NOT null out calculator fields here!
            // The lambda callbacks (e.g., volumeAbove.GetCurrentVolume = () => volumeCalculator.CurrentVolume)
            // capture 'this' and access fields at evaluation time. If we null these fields,
            // any in-flight condition evaluation during a race window would throw NullReferenceException.
            // The GC will handle cleanup when the StrategyRunner instance is no longer referenced.
            emaCalculators.Clear();

            // If strategy never completed, determine final result
            if (result == StrategyResult.Running)
            {
                if (!entryFilled)
                {
                    result = StrategyResult.NeverBought;
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

            double pnl = strategy.Order.Quantity * (exitFillPrice - entryFillPrice);
            string resultMsg;
            string detailsMsg;

            switch (result)
            {
                case StrategyResult.TakeProfitFilled:
                    Console.ForegroundColor = ConsoleColor.Green;
                    resultMsg = $"[{strategy.Symbol}] RESULT: *** TAKE PROFIT FILLED ***";
                    detailsMsg = $"Entry: ${entryFillPrice:F2} -> Exit: ${exitFillPrice:F2} | P&L: ${pnl:F2}";
                    Console.WriteLine($"[{timestamp}] |  {resultMsg}");
                    Console.WriteLine($"[{timestamp}] |  Entry: ${entryFillPrice:F2} -> Exit: ${exitFillPrice:F2}");
                    Console.WriteLine($"[{timestamp}] |  P&L: ${pnl:F2}");
                    SessionLogger?.LogEvent("RESULT", resultMsg);
                    SessionLogger?.LogEvent("RESULT", detailsMsg);
                    break;

                case StrategyResult.StopLossFilled:
                    Console.ForegroundColor = ConsoleColor.Red;
                    resultMsg = $"[{strategy.Symbol}] RESULT: *** STOP LOSS FILLED ***";
                    detailsMsg = $"Entry: ${entryFillPrice:F2} -> Exit: ${exitFillPrice:F2} | P&L: ${pnl:F2}";
                    Console.WriteLine($"[{timestamp}] |  {resultMsg}");
                    Console.WriteLine($"[{timestamp}] |  Entry: ${entryFillPrice:F2} -> Exit: ${exitFillPrice:F2}");
                    Console.WriteLine($"[{timestamp}] |  P&L: ${pnl:F2}");
                    SessionLogger?.LogEvent("RESULT", resultMsg);
                    SessionLogger?.LogEvent("RESULT", detailsMsg);
                    break;

                case StrategyResult.TrailingStopLossFilled:
                    if (pnl >= 0)
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        resultMsg = $"[{strategy.Symbol}] RESULT: *** TRAILING STOP LOSS (profit protected) ***";
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        resultMsg = $"[{strategy.Symbol}] RESULT: *** TRAILING STOP LOSS (loss limited) ***";
                    }
                    detailsMsg = $"Entry: ${entryFillPrice:F2} -> Exit: ${exitFillPrice:F2} | High: ${highWaterMark:F2} | P&L: ${pnl:F2}";
                    Console.WriteLine($"[{timestamp}] |  {resultMsg}");
                    Console.WriteLine($"[{timestamp}] |  Entry: ${entryFillPrice:F2} -> Exit: ${exitFillPrice:F2}");
                    Console.WriteLine($"[{timestamp}] |  High: ${highWaterMark:F2} | Trail Stop: ${trailingStopLossPrice:F2}");
                    Console.WriteLine($"[{timestamp}] |  P&L: ${pnl:F2}");
                    SessionLogger?.LogEvent("RESULT", resultMsg);
                    SessionLogger?.LogEvent("RESULT", detailsMsg);
                    break;

                case StrategyResult.ExitedWithProfit:
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    resultMsg = $"[{strategy.Symbol}] RESULT: *** EXITED WITH PROFIT (time limit) ***";
                    detailsMsg = $"Entry: ${entryFillPrice:F2} -> Exit: ${exitFillPrice:F2} | P&L: ${pnl:F2}";
                    Console.WriteLine($"[{timestamp}] |  {resultMsg}");
                    Console.WriteLine($"[{timestamp}] |  Entry: ${entryFillPrice:F2} -> Exit: ${exitFillPrice:F2}");
                    Console.WriteLine($"[{timestamp}] |  P&L: ${pnl:F2}");
                    SessionLogger?.LogEvent("RESULT", resultMsg);
                    SessionLogger?.LogEvent("RESULT", detailsMsg);
                    break;

                case StrategyResult.TakeProfitCancelled:
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    resultMsg = $"[{strategy.Symbol}] RESULT: TAKE PROFIT CANCELLED (not profitable)";
                    detailsMsg = $"Entry: ${entryFillPrice:F2} | Current: ${lastPrice:F2} | WARNING: STILL HOLDING {strategy.Order.Quantity} SHARES";
                    Console.WriteLine($"[{timestamp}] |  {resultMsg}");
                    Console.WriteLine($"[{timestamp}] |  Entry: ${entryFillPrice:F2} | Current: ${lastPrice:F2}");
                    Console.WriteLine($"[{timestamp}] |  WARNING: STILL HOLDING {strategy.Order.Quantity} SHARES - MANAGE MANUALLY!");
                    SessionLogger?.LogEvent("RESULT", resultMsg);
                    SessionLogger?.LogEvent("RESULT", detailsMsg);
                    break;

                case StrategyResult.NeverBought:
                    Console.ForegroundColor = ConsoleColor.Gray;
                    resultMsg = $"[{strategy.Symbol}] RESULT: NEVER BOUGHT";
                    detailsMsg = "Conditions not met - no position taken";
                    Console.WriteLine($"[{timestamp}] |  {resultMsg}");
                    Console.WriteLine($"[{timestamp}] |  {detailsMsg}");
                    SessionLogger?.LogEvent("RESULT", resultMsg);
                    SessionLogger?.LogEvent("RESULT", detailsMsg);
                    break;

                case StrategyResult.MissedTheBoat:
                    Console.ForegroundColor = ConsoleColor.DarkYellow;
                    resultMsg = $"[{strategy.Symbol}] RESULT: MISSED THE BOAT";
                    detailsMsg = "Price already at/above take profit target when conditions met. No position taken";
                    Console.WriteLine($"[{timestamp}] |  {resultMsg}");
                    Console.WriteLine($"[{timestamp}] |  {detailsMsg}");
                    SessionLogger?.LogEvent("RESULT", resultMsg);
                    SessionLogger?.LogEvent("RESULT", detailsMsg);
                    break;

                case StrategyResult.EntryCancelled:
                    Console.ForegroundColor = ConsoleColor.Gray;
                    resultMsg = $"[{strategy.Symbol}] RESULT: ENTRY CANCELLED";
                    detailsMsg = "Entry order was cancelled before fill";
                    Console.WriteLine($"[{timestamp}] |  {resultMsg}");
                    Console.WriteLine($"[{timestamp}] |  {detailsMsg}");
                    SessionLogger?.LogEvent("RESULT", resultMsg);
                    SessionLogger?.LogEvent("RESULT", detailsMsg);
                    break;

                default:
                    Console.ForegroundColor = ConsoleColor.White;
                    resultMsg = $"[{strategy.Symbol}] RESULT: {result}";
                    Console.WriteLine($"[{timestamp}] |  {resultMsg}");
                    SessionLogger?.LogEvent("RESULT", resultMsg);
                    break;
            }

            Console.ResetColor();
            Console.WriteLine($"[{timestamp}] +===============================================================+");

            // Check if strategy should repeat after completion
            if (strategy.RepeatEnabled && ShouldRepeat())
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
            return result switch
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


