// ============================================================================
// Stock - Fluent Builder for Creating Strategies
// ============================================================================
//
// BEST PRACTICES:
// 1. Always add at least one condition before calling Buy(), Sell(), or Close().
// 2. Chain conditions in the order they should be evaluated (chronological).
// 3. Use TimeFrame() to define the active time window (easy to comment out for testing).
// 4. Always set TakeProfit AND StopLoss for proper risk management.
// 5. Validate that Start time is before End time.
// 6. Use meaningful condition sequences (e.g., Breakout -> Pullback -> AboveVwap).
//
// ============================================================================
// DEFAULT VALUES REFERENCE
// ============================================================================
//
// STOCK BUILDER DEFAULTS (if method not called):
// ┌─────────────────────┬─────────────────────────────────────────────────────┐
// │ Property            │ Default Value                                       │
// ├─────────────────────┼─────────────────────────────────────────────────────┤
// │ Exchange            │ "SMART"                                             │
// │ PrimaryExchange     │ null                                                │
// │ Currency            │ "USD"                                               │
// │ SecType             │ "STK"                                               │
// │ Enabled             │ true                                                │
// │ StartTime           │ null (no start restriction)                         │
// │ EndTime             │ null (no end restriction)                           │
// │ Session             │ null (no session restriction)                       │
// │ Notes               │ null                                                │
// └─────────────────────┴─────────────────────────────────────────────────────┘
//
// CONDITION METHOD DEFAULTS:
// ┌─────────────────────┬─────────────────────────────────────────────────────┐
// │ Method              │ Default Parameters                                  │
// ├─────────────────────┼─────────────────────────────────────────────────────┤
// │ AboveVwap(buffer)   │ buffer = 0                                          │
// │ BelowVwap(buffer)   │ buffer = 0                                          │
// │ IsRsi(state, thresh)│ threshold = null (70 overbought, 30 oversold)       │
// │ IsAdx(comp, thresh) │ threshold = 25                                      │
// │ IsDI(dir, minDiff)  │ minDifference = 0                                   │
// └─────────────────────┴─────────────────────────────────────────────────────┘
//
// ORDER METHOD DEFAULTS (Buy/Sell/Close):
// ┌─────────────────────┬─────────────────────────────────────────────────────┐
// │ Parameter           │ Default Value                                       │
// ├─────────────────────┼─────────────────────────────────────────────────────┤
// │ priceType           │ Price.Current                                       │
// │ orderType           │ OrderType.Limit                                     │
// │ positionSide (Close)│ OrderSide.Buy (closes long position)                │
// └─────────────────────┴─────────────────────────────────────────────────────┘
//
// STRATEGY BUILDER DEFAULTS (after Buy/Sell/Close):
// ┌─────────────────────┬─────────────────────────────────────────────────────┐
// │ Property            │ Default Value                                       │
// ├─────────────────────┼─────────────────────────────────────────────────────┤
// │ TakeProfit          │ null (disabled)                                     │
// │ StopLoss            │ null (disabled)                                     │
// │ TrailingStopLoss    │ disabled                                            │
// │ TrailingStopPercent │ 0                                                   │
// │ AtrStopLoss         │ null                                                │
// │ ClosePositionTime   │ null (no auto-close)                                │
// │ CloseOnlyIfProfit   │ true                                                │
// │ TimeInForce         │ TimeInForce.GoodTillCancel                          │
// │ OutsideRth          │ true                                                │
// │ TakeProfitOutsideRth│ true                                                │
// │ AllOrNone           │ false                                               │
// │ AdxTakeProfit       │ null                                                │
// └─────────────────────┴─────────────────────────────────────────────────────┘
//
// ADX TAKE PROFIT DEFAULTS (when using TakeProfit(low, high)):
// ┌─────────────────────┬─────────────────────────────────────────────────────┐
// │ Property            │ Default Value                                       │
// ├─────────────────────┼─────────────────────────────────────────────────────┤
// │ WeakTrendThreshold  │ 15.0                                                │
// │ DevelopingThreshold │ 25.0                                                │
// │ StrongTrendThreshold│ 35.0                                                │
// │ ExitOnAdxRollover   │ true                                                │
// └─────────────────────┴─────────────────────────────────────────────────────┘
//
// ============================================================================
//
// FLUENT PATTERN:
//   Stock.Ticker("SYMBOL")        // Start builder
//       .TimeFrame(...)           // Optional: when to monitor (comment out for immediate testing)
//       .Breakout(...)            // Add conditions
//       .Pullback(...)
//       .IsAboveVwap()
//       .Long(...)                // Returns StrategyBuilder (opens long position)
//       .Short(...)               // Returns StrategyBuilder (opens short position)
//       .Close(...)               // Returns StrategyBuilder (closes existing position)
//       .TakeProfit(...)          // Exit configuration
//       .StopLoss(...)
//       .Build()                  // Terminal: returns TradingStrategy
//
// OPENING POSITIONS:
// var longStrategy = Stock.Ticker("AAPL")
//     .TimeFrame(new TimeOnly(4, 0), new TimeOnly(9, 30))  // Comment out to test immediately
//     .Breakout(150)
//     .Long(quantity: 100, Price.Current)
//     .TakeProfit(155)
//     .StopLoss(148)
//     .Build();
//
// CLOSING POSITIONS:
// var closeStrategy = Stock.Ticker("AAPL")
//     .IsPriceAbove(155)                   // Exit when price hits target
//     .Close(quantity: 100)                // Sell to close long position
//     .TimeInForce(TIF.GTC)                // TIF is NOT automatic
//     .OutsideRTH(true)                    // OutsideRth is NOT automatic
//     .Build();
//
// IBKR API NOTE:
// Close() generates the same IBKR order structure as Sell():
//   order.action = "SELL"
//   order.totalQuantity = 100
//   order.orderType = "MKT"
//   order.tif = "GTC"          // Set via .TimeInForce()
//   order.outsideRth = True    // Set via .OutsideRTH()
//
// ============================================================================

using System;
using System.Collections.Generic;
using IdiotProof.Backend.Enums;
using IdiotProof.Backend.Models;

namespace IdiotProof.Backend.Strategy
{
    /// <summary>
    /// Fluent builder for creating multi-step trading strategies.
    /// Provides a clean, readable API for defining entry conditions and order parameters.
    /// </summary>
    /// <remarks>
    /// <para><b>Best Practices:</b></para>
    /// <list type="bullet">
    ///   <item>Always add at least one condition before calling <see cref="Long"/> or <see cref="Short"/>.</item>
    ///   <item>Chain conditions in chronological order of expected occurrence.</item>
    ///   <item>Use <see cref="TimeFrame(TradingSession)"/> to define the active time window.</item>
    ///   <item>Always configure both take profit and stop loss for risk management.</item>
    /// </list>
    /// 
    /// <para><b>Builder Flow:</b></para>
    /// <code>
    /// Stock.Ticker("AAPL")    // Initialize
    ///     .TimeFrame(TradingSession.PreMarket)  // Time window
    ///     .Breakout(150)      // Conditions (Stock returns Stock)
    ///     .Pullback(148)
    ///     .Long(100)          // Order (Stock returns StrategyBuilder)
    ///     .TakeProfit(155)    // Exit config (StrategyBuilder returns StrategyBuilder)
    ///     .Build()            // Terminal (StrategyBuilder returns TradingStrategy)
    /// </code>
    /// </remarks>
    public sealed class Stock
    {
        private readonly string _symbol;
        private Guid _id = Guid.NewGuid();
        private string _name = string.Empty;
        private string _exchange = "SMART";
        private string? _primaryExchange;
        private string _currency = "USD";
        private string _secType = "STK";
        private readonly List<IStrategyCondition> _conditions = new();
        private bool _enabled = true;
        private TimeOnly? _startTime;
        private TimeOnly? _endTime;
        private TradingSession? _session;
        private string? _notes;
        private bool _repeatEnabled = false;

        /// <summary>
        /// Private constructor - use <see cref="Ticker"/> to create instances.
        /// </summary>
        /// <param name="symbol">The stock ticker symbol.</param>
        /// <exception cref="ArgumentNullException">Thrown if symbol is null.</exception>
        private Stock(string symbol)
        {
            _symbol = symbol ?? throw new ArgumentNullException(nameof(symbol));
        }

        /// <summary>
        /// Creates a new strategy builder for the specified stock symbol.
        /// </summary>
        /// <param name="symbol">The stock ticker symbol (e.g., "AAPL", "NAMM").</param>
        /// <param name="notes">Optional notes for documentation.</param>
        /// <returns>A new <see cref="Stock"/> builder instance.</returns>
        /// <exception cref="ArgumentNullException">Thrown if symbol is null.</exception>
        /// <remarks>
        /// <b>Best Practice:</b> Use uppercase ticker symbols to match exchange conventions.
        /// </remarks>
        /// <example>
        /// <code>Stock.Ticker("AAPL", notes: "Apple breakout strategy")</code>
        /// </example>
        public static Stock Ticker(string symbol, string? notes = null) => new(symbol) { _notes = notes };

        /// <summary>
        /// Sets the unique identifier for this strategy (for tracking/updates).
        /// </summary>
        /// <param name="id">The strategy ID (should match StrategyDefinition.Id).</param>
        /// <returns>The builder for method chaining.</returns>
        public Stock WithId(Guid id)
        {
            _id = id;
            return this;
        }

        /// <summary>
        /// Sets the user-friendly name for this strategy.
        /// </summary>
        /// <param name="name">The strategy name.</param>
        /// <returns>The builder for method chaining.</returns>
        public Stock WithName(string name)
        {
            _name = name;
            return this;
        }

        /// <summary>
        /// Adds notes to the strategy for documentation purposes.
        /// </summary>
        /// <param name="notes">Notes describing the strategy or providing reminders.</param>
        /// <returns>The builder for method chaining.</returns>
        public Stock WithNotes(string? notes)
        {
            _notes = notes;
            return this;
        }

        /// <summary>
        /// Sets the exchange for order routing.
        /// </summary>
        /// <param name="exchange">Exchange identifier (default: "SMART").</param>
        /// <param name="notes">Optional notes for documentation.</param>
        /// <returns>The builder for method chaining.</returns>
        /// <remarks>
        /// <b>Best Practice:</b> Use "SMART" for automatic routing. Specify exchange
        /// (e.g., "NASDAQ", "NYSE") only when you need specific routing.
        /// </remarks>
        public Stock Exchange(string exchange, string? notes = null)
        {
            _exchange = exchange;
            return this;
        }

        /// <summary>
        /// Sets the exchange for order routing using a predefined exchange type.
        /// </summary>
        /// <param name="exchange">Exchange type (default: SMART).</param>
        /// <param name="notes">Optional notes for documentation.</param>
        /// <returns>The builder for method chaining.</returns>
        /// <remarks>
        /// <para><b>Best Practice:</b> Use <see cref="ContractExchange.Smart"/> for most stocks.</para>
        /// <para>Use <see cref="ContractExchange.Pink"/> for OTC/microcap stocks under $1.</para>
        /// <para><b>Note:</b> Pink uses SMART routing with PrimaryExchange="PINK" for IBKR compatibility.</para>
        /// </remarks>
        public Stock Exchange(ContractExchange exchange, string? notes = null)
        {
            switch (exchange)
            {
                case ContractExchange.Pink:
                    _exchange = "SMART";
                    _primaryExchange = "PINK";
                    break;
                case ContractExchange.Smart:
                default:
                    _exchange = "SMART";
                    _primaryExchange = null;
                    break;
            }
            return this;
        }

        /// <summary>
        /// Sets the primary exchange for order routing (used with SMART routing).
        /// </summary>
        /// <param name="primaryExchange">Primary exchange identifier (e.g., "NASDAQ", "NYSE", "PINK").</param>
        /// <param name="notes">Optional notes for documentation.</param>
        /// <returns>The builder for method chaining.</returns>
        /// <remarks>
        /// <b>Use Case:</b> Required for OTC stocks when using SMART routing.
        /// </remarks>
        public Stock PrimaryExchange(string primaryExchange, string? notes = null)
        {
            _primaryExchange = primaryExchange;
            return this;
        }

        /// <summary>
        /// Sets the currency for the order.
        /// </summary>
        /// <param name="currency">Currency code (default: "USD").</param>
        /// <param name="notes">Optional notes for documentation.</param>
        /// <returns>The builder for method chaining.</returns>
        public Stock Currency(string currency, string? notes = null)
        {
            _currency = currency;
            return this;
        }

        /// <summary>
        /// Enables or disables this strategy.
        /// </summary>
        /// <param name="enabled">True to enable, false to disable.</param>
        /// <param name="notes">Optional notes for documentation.</param>
        /// <returns>The builder for method chaining.</returns>
        /// <remarks>
        /// <b>Use Case:</b> Temporarily disable a strategy without removing it from code.
        /// Disabled strategies are skipped during execution.
        /// </remarks>
        public Stock Enabled(bool enabled, string? notes = null)
        {
            _enabled = enabled;
            return this;
        }

        /// <summary>
        /// Enables repeating for this strategy.
        /// When enabled, the strategy resets after completion and can fire again when conditions are met.
        /// </summary>
        /// <param name="repeat">True to repeat (default), false to fire once.</param>
        /// <returns>The builder for method chaining.</returns>
        /// <remarks>
        /// <para><b>Behavior:</b></para>
        /// <list type="bullet">
        ///   <item>When true: After take profit or stop loss fills, the strategy resets and waits for conditions again.</item>
        ///   <item>When false (default): The strategy fires once and stops.</item>
        /// </list>
        /// 
        /// <para><b>Example - Repeating scalp strategy:</b></para>
        /// <code>
        /// Stock.Ticker("ABC")
        ///     .TimeFrame(TradingSession.RTH)
        ///     .IsPriceAbove(5.00)
        ///     .IsAboveVwap()
        ///     .Long(100, Price.Current)
        ///     .TakeProfit(6.00)
        ///     .StopLoss(4.50)
        ///     .Repeat()  // Will buy again at $5, sell at $6, repeat
        ///     .Build();
        /// </code>
        /// </remarks>
        public Stock Repeat(bool repeat = true)
        {
            _repeatEnabled = repeat;
            return this;
        }

        /// <summary>
        /// Sets the time window for monitoring the strategy (Eastern Time).
        /// </summary>
        /// <param name="startTime">The time to begin monitoring.</param>
        /// <param name="endTime">The time to stop monitoring.</param>
        /// <returns>The builder for method chaining.</returns>
        /// <remarks>
        /// <para><b>Best Practice:</b> Use <see cref="MarketTime"/> helper for common times:</para>
        /// <code>.TimeFrame(MarketTime.PreMarket.Start, MarketTime.PreMarket.End)</code>
        /// <para><b>Tip:</b> Comment out this single line to test without time restrictions.</para>
        /// </remarks>
        /// <example>
        /// <code>
        /// Stock.Ticker("AAPL")
        ///     .TimeFrame(new TimeOnly(4, 0), new TimeOnly(9, 30))  // Comment out to test immediately
        ///     .Breakout(150)
        ///     .Long(100, Price.Current)
        ///     .Build();
        /// </code>
        /// </example>
        public Stock TimeFrame(TimeOnly startTime, TimeOnly endTime)
        {
            _startTime = startTime;
            _endTime = endTime;
            return this;
        }

        /// <summary>
        /// Sets the start time for monitoring the strategy (Eastern Time).
        /// </summary>
        /// <param name="startTime">The time to begin monitoring.</param>
        /// <returns>The builder for method chaining.</returns>
        /// <remarks>
        /// <para><b>Best Practice:</b> Use with <see cref="StrategyBuilder.End"/> for a complete time window,
        /// or use <see cref="TimeFrame"/> to set both in one call.</para>
        /// <para><b>Tip:</b> Use <see cref="MarketTime"/> helper for common times:</para>
        /// <code>.Start(MarketTime.PreMarket.Start)</code>
        /// </remarks>
        /// <example>
        /// <code>
        /// Stock.Ticker("AAPL")
        ///     .Start(MarketTime.PreMarket.Start)  // 4:00 AM ET
        ///     .Breakout(150)
        ///     .Long(100, Price.Current)
        ///     .End(MarketTime.PreMarket.End);     // 9:30 AM ET
        /// </code>
        /// </example>
        public Stock Start(TimeOnly startTime)
        {
            _startTime = startTime;
            return this;
        }

        /// <summary>
        /// Sets the time window for monitoring the strategy using a predefined trading session.
        /// </summary>
        /// <param name="session">The trading session to use.</param>
        /// <returns>The builder for method chaining.</returns>
        /// <remarks>
        /// <para><b>Session Times (Eastern Time):</b></para>
        /// <list type="bullet">
        ///   <item><see cref="TradingSession.PreMarket"/>: 4:00 AM - 9:30 AM ET</item>
        ///   <item><see cref="TradingSession.RTH"/>: 9:30 AM - 4:00 PM ET</item>
        ///   <item><see cref="TradingSession.AfterHours"/>: 4:00 PM - 8:00 PM ET</item>
        ///   <item><see cref="TradingSession.Extended"/>: 4:00 AM - 8:00 PM ET</item>
        /// </list>
        /// <para><b>Tip:</b> Comment out this single line to test without time restrictions.</para>
        /// </remarks>
        /// <example>
        /// <code>
        /// Stock.Ticker("AAPL")
        ///     .TimeFrame(TradingSession.PreMarket)  // Comment out to test immediately
        ///     .Breakout(150)
        ///     .Long(100, Price.Current)
        ///     .Build();
        /// </code>
        /// </example>
        public Stock TimeFrame(TradingSession session)
        {
            _session = session;

            // Handle Active separately since it clears time restrictions
            if (session == TradingSession.Active)
            {
                _startTime = null;
                _endTime = null;
                return this;
            }

            (_startTime, _endTime) = session switch
            {
                // Standard sessions (full duration)
                TradingSession.PreMarket => (MarketTime.PreMarket.Start, MarketTime.PreMarket.End),
                TradingSession.RTH => (MarketTime.RTH.Start, MarketTime.RTH.End),
                TradingSession.AfterHours => (MarketTime.AfterHours.Start, MarketTime.AfterHours.End),
                TradingSession.Extended => (MarketTime.Extended.Start, MarketTime.Extended.End),

                // Buffered sessions (10-minute buffer)
                TradingSession.PreMarketEndEarly => (MarketTime.PreMarket.Start, MarketTime.PreMarket.Ending),
                TradingSession.PreMarketStartLate => (MarketTime.PreMarket.Starting, MarketTime.PreMarket.End),
                TradingSession.RTHEndEarly => (MarketTime.RTH.Start, MarketTime.RTH.Ending),
                TradingSession.RTHStartLate => (MarketTime.RTH.Starting, MarketTime.RTH.End),
                TradingSession.AfterHoursEndEarly => (MarketTime.AfterHours.Start, MarketTime.AfterHours.Ending),

                _ => throw new ArgumentOutOfRangeException(nameof(session), session, "Unknown trading session")
            };

            return this;
        }

        // ====================================================================
        // CONDITION METHODS
        // ====================================================================

        /// <summary>
        /// Adds a breakout condition: Price >= level.
        /// </summary>
        /// <param name="level">The breakout price level.</param>
        /// <param name="notes">Optional notes for documentation.</param>
        public Stock Breakout(double level, string? notes = null)
        {
            _conditions.Add(new BreakoutCondition(level));
            return this;
        }

        /// <summary>
        /// Adds a pullback condition: Price &lt;= level.
        /// </summary>
        /// <param name="level">The pullback price level.</param>
        /// <param name="notes">Optional notes for documentation.</param>
        public Stock Pullback(double level, string? notes = null)
        {
            _conditions.Add(new PullbackCondition(level));
            return this;
        }

        /// <summary>
        /// Adds an above-VWAP condition: Price >= VWAP + buffer.
        /// </summary>
        /// <param name="buffer">Buffer above VWAP.</param>
        /// <param name="notes">Optional notes for documentation.</param>
        public Stock IsAboveVwap(double buffer = 0, string? notes = null)
        {
            _conditions.Add(new AboveVwapCondition(buffer));
            return this;
        }

        /// <summary>
        /// Adds a below-VWAP condition: Price &lt;= VWAP - buffer.
        /// </summary>
        /// <param name="buffer">Buffer below VWAP.</param>
        /// <param name="notes">Optional notes for documentation.</param>
        public Stock IsBelowVwap(double buffer = 0, string? notes = null)
        {
            _conditions.Add(new BelowVwapCondition(buffer));
            return this;
        }

        /// <summary>
        /// Adds a price-at-or-above condition: Price >= level.
        /// </summary>
        /// <param name="level">The price level.</param>
        /// <param name="notes">Optional notes for documentation.</param>
        public Stock IsPriceAbove(double level, string? notes = null)
        {
            _conditions.Add(new PriceAtOrAboveCondition(level));
            return this;
        }

        /// <summary>
        /// Adds a price-below condition: Price &lt; level.
        /// </summary>
        /// <param name="level">The price level.</param>
        /// <param name="notes">Optional notes for documentation.</param>
        public Stock IsPriceBelow(double level, string? notes = null)
        {
            _conditions.Add(new PriceBelowCondition(level));
            return this;
        }

        /// <summary>
        /// Adds a custom condition.
        /// </summary>
        public Stock When(string name, Func<double, double, bool> condition)
        {
            _conditions.Add(new CustomCondition(name, condition));
            return this;
        }

        /// <summary>
        /// Adds any condition implementing IStrategyCondition.
        /// </summary>
        public Stock Condition(IStrategyCondition condition)
        {
            _conditions.Add(condition ?? throw new ArgumentNullException(nameof(condition)));
            return this;
        }

        /// <summary>
        /// Adds any condition implementing IStrategyCondition.
        /// Alias for <see cref="Condition"/> for fluent naming.
        /// </summary>
        public Stock WithCondition(IStrategyCondition condition)
        {
            return Condition(condition);
        }

        // ====================================================================
        // INDICATOR CONDITION METHODS
        // ====================================================================

        /// <summary>
        /// Adds an RSI condition: Checks if RSI is in overbought or oversold state.
        /// </summary>
        /// <param name="state">The RSI state to check for (Overbought >= 70, Oversold &lt;= 30).</param>
        /// <param name="threshold">Custom threshold value (default: 70 for overbought, 30 for oversold).</param>
        /// <returns>The builder for method chaining.</returns>
        /// <remarks>
        /// <para><b>RSI (Relative Strength Index):</b></para>
        /// <list type="bullet">
        ///   <item><b>Overbought (RSI >= 70):</b> May indicate selling pressure building</item>
        ///   <item><b>Oversold (RSI &lt;= 30):</b> May indicate buying opportunity</item>
        /// </list>
        /// 
        /// <para><b>Example:</b></para>
        /// <code>
        /// // Buy when RSI indicates oversold
        /// Stock.Ticker("AAPL")
        ///     .IsRsi(RsiState.Oversold)         // RSI &lt;= 30
        ///     .Long(100, Price.Current)
        ///     .Build();
        /// 
        /// // Sell when RSI indicates overbought with custom threshold
        /// Stock.Ticker("AAPL")
        ///     .IsRsi(RsiState.Overbought, 80)   // RSI >= 80
        ///     .Short(100, Price.Current)
        ///     .Build();
        /// </code>
        /// </remarks>
        public Stock IsRsi(Enums.RsiState state, double? threshold = null)
        {
            _conditions.Add(new RsiCondition(state, threshold));
            return this;
        }

        /// <summary>
        /// Adds an ADX condition: Checks if ADX meets the comparison threshold.
        /// </summary>
        /// <param name="comparison">The comparison operator (Gte, Lte, Gt, Lt, Eq).</param>
        /// <param name="threshold">The ADX threshold value (0-100).</param>
        /// <returns>The builder for method chaining.</returns>
        /// <remarks>
        /// <para><b>ADX (Average Directional Index):</b></para>
        /// <list type="bullet">
        ///   <item><b>ADX &lt; 20:</b> Weak or no trend</item>
        ///   <item><b>ADX 20-25:</b> Trend developing</item>
        ///   <item><b>ADX 25-50:</b> Strong trend</item>
        ///   <item><b>ADX > 50:</b> Very strong trend</item>
        /// </list>
        /// 
        /// <para><b>Example:</b></para>
        /// <code>
        /// // Only buy when trend is strong
        /// Stock.Ticker("AAPL")
        ///     .IsAdx(Comparison.Gte, 25)       // ADX >= 25
        ///     .IsDI(DiDirection.Positive)      // Bullish direction
        ///     .Long(100, Price.Current)
        ///     .Build();
        /// 
        /// // Range-bound strategy
        /// Stock.Ticker("AAPL")
        ///     .IsAdx(Comparison.Lte, 20)       // ADX &lt;= 20 (no trend)
        ///     .Breakout(150)
        ///     .Long(100, Price.Current)
        ///     .Build();
        /// </code>
        /// </remarks>
        public Stock IsAdx(Enums.Comparison comparison, double threshold)
        {
            _conditions.Add(new AdxCondition(comparison, threshold));
            return this;
        }

        /// <summary>
        /// Adds a MACD condition: Checks if MACD is in the specified state.
        /// </summary>
        /// <param name="state">The MACD state to check for.</param>
        /// <returns>The builder for method chaining.</returns>
        /// <remarks>
        /// <para><b>MACD States:</b></para>
        /// <list type="bullet">
        ///   <item><b>Bullish:</b> MACD line > Signal line (momentum up)</item>
        ///   <item><b>Bearish:</b> MACD line &lt; Signal line (momentum down)</item>
        ///   <item><b>AboveZero:</b> MACD line > 0 (uptrend)</item>
        ///   <item><b>BelowZero:</b> MACD line &lt; 0 (downtrend)</item>
        ///   <item><b>HistogramRising:</b> Histogram increasing</item>
        ///   <item><b>HistogramFalling:</b> Histogram decreasing</item>
        /// </list>
        /// 
        /// <para><b>Example:</b></para>
        /// <code>
        /// // Buy on bullish MACD crossover in uptrend
        /// Stock.Ticker("AAPL")
        ///     .IsMacd(MacdState.Bullish)        // MACD > Signal
        ///     .IsMacd(MacdState.AboveZero)      // Confirming uptrend
        ///     .Long(100, Price.Current)
        ///     .Build();
        /// </code>
        /// </remarks>
        public Stock IsMacd(Enums.MacdState state)
        {
            _conditions.Add(new MacdCondition(state));
            return this;
        }

        /// <summary>
        /// Adds a DI condition: Checks the directional indicator relationship.
        /// </summary>
        /// <param name="direction">The DI direction to check for (Positive or Negative).</param>
        /// <param name="minDifference">
        /// Minimum difference between +DI and -DI (default: 0).
        /// The dominant DI must be strictly greater than the other,
        /// and the difference must be at least this value.
        /// </param>
        /// <returns>The builder for method chaining.</returns>
        /// <remarks>
        /// <para><b>Directional Indicators:</b></para>
        /// <list type="bullet">
        ///   <item><b>Positive (+DI > -DI):</b> Bullish pressure dominates</item>
        ///   <item><b>Negative (-DI > +DI):</b> Bearish pressure dominates</item>
        ///   <item><b>Equal values:</b> No direction dominates, condition returns false</item>
        /// </list>
        /// 
        /// <para><b>MinDifference Behavior:</b></para>
        /// <para>When MinDifference is specified, the dominant DI must exceed the other by at least that amount:</para>
        /// <list type="bullet">
        ///   <item>+DI=30, -DI=25 with MinDifference=5: Returns true (5 >= 5)</item>
        ///   <item>+DI=29, -DI=25 with MinDifference=5: Returns false (4 &lt; 5)</item>
        ///   <item>+DI=25, -DI=25 with any MinDifference: Returns false (equal values)</item>
        /// </list>
        /// 
        /// <para><b>Best Practice:</b> Combine with ADX for trend strength confirmation.</para>
        /// 
        /// <para><b>Example:</b></para>
        /// <code>
        /// // Buy in strong bullish trend
        /// Stock.Ticker("AAPL")
        ///     .IsAdx(Comparison.Gte, 25)         // Strong trend
        ///     .IsDI(DiDirection.Positive)        // Bullish direction (+DI > -DI)
        ///     .Long(100, Price.Current)
        ///     .Build();
        /// 
        /// // Require significant DI difference for stronger signal
        /// Stock.Ticker("AAPL")
        ///     .IsDI(DiDirection.Positive, 5)     // +DI must exceed -DI by at least 5
        ///     .Long(100, Price.Current)
        ///     .Build();
        /// </code>
        /// </remarks>
        public Stock IsDI(Enums.DiDirection direction, double minDifference = 0)
        {
            _conditions.Add(new DiCondition(direction, minDifference));
            return this;
        }

        // ====================================================================
        // EMA CONDITION METHODS
        // ====================================================================

        /// <summary>
        /// Adds a price-above-EMA condition: Price >= EMA(period).
        /// </summary>
        /// <param name="period">The EMA period (e.g., 9, 21, 50, 200).</param>
        /// <returns>The builder for method chaining.</returns>
        /// <remarks>
        /// <para><b>EMA (Exponential Moving Average):</b></para>
        /// <para>Gives more weight to recent prices, making it more responsive than SMA.</para>
        /// 
        /// <para><b>Common Periods:</b></para>
        /// <list type="bullet">
        ///   <item><b>9 EMA:</b> Short-term trend, very responsive</item>
        ///   <item><b>21 EMA:</b> Medium-term trend</item>
        ///   <item><b>50 EMA:</b> Intermediate trend</item>
        ///   <item><b>200 EMA:</b> Long-term trend, major support/resistance</item>
        /// </list>
        /// 
        /// <para><b>ASCII Visualization:</b></para>
        /// <code>
        ///     Price above EMA = Bullish
        ///     +--------------------------------------------+
        ///     |     /\          ___/                       |
        ///     |    /  \   ___/     \____    Price above    |
        ///     |   /    \_/     EMA       \___  = bullish   |
        ///     |  /  ___________________________            |
        ///     | /_/                                        |
        ///     +--------------------------------------------+
        /// </code>
        /// 
        /// <para><b>Example:</b></para>
        /// <code>
        /// Stock.Ticker("AAPL")
        ///     .IsEmaAbove(9)              // Price above short-term trend
        ///     .IsEmaAbove(21)             // Price above medium-term trend
        ///     .Long(100, Price.Current)
        ///     .Build();
        /// </code>
        /// </remarks>
        public Stock IsEmaAbove(int period)
        {
            _conditions.Add(new EmaAboveCondition(period));
            return this;
        }

        /// <summary>
        /// Adds a price-below-EMA condition: Price &lt;= EMA(period).
        /// </summary>
        /// <param name="period">The EMA period (e.g., 9, 21, 50, 200).</param>
        /// <returns>The builder for method chaining.</returns>
        /// <remarks>
        /// <para><b>Use Cases:</b></para>
        /// <list type="bullet">
        ///   <item>Identify bearish conditions (price below moving average)</item>
        ///   <item>Short selling setups</item>
        ///   <item>Exit signals for long positions</item>
        /// </list>
        /// 
        /// <para><b>ASCII Visualization:</b></para>
        /// <code>
        ///     Price below EMA = Bearish
        ///     +--------------------------------------------+
        ///     |  ___________________________  EMA          |
        ///     | /                           \____          |
        ///     |/   ____                          \         |
        ///     |   /    \    Price below EMA      \___      |
        ///     |  /      \_____/    = bearish          \    |
        ///     +--------------------------------------------+
        /// </code>
        /// 
        /// <para><b>Example:</b></para>
        /// <code>
        /// Stock.Ticker("AAPL")
        ///     .IsEmaBelow(200)             // Price below long-term trend
        ///     .Short(100, Price.Current)
        ///     .Build();
        /// </code>
        /// </remarks>
        public Stock IsEmaBelow(int period)
        {
            _conditions.Add(new EmaBelowCondition(period));
            return this;
        }

        /// <summary>
        /// Adds a price-between-EMAs condition: Price is between EMA(lowerPeriod) and EMA(upperPeriod).
        /// </summary>
        /// <param name="lowerPeriod">The lower EMA period (e.g., 9).</param>
        /// <param name="upperPeriod">The upper EMA period (e.g., 21).</param>
        /// <returns>The builder for method chaining.</returns>
        /// <remarks>
        /// <para><b>Pullback Zone Strategy:</b></para>
        /// <para>When price pulls back into the zone between two EMAs, it often provides
        /// an opportunity to enter in the direction of the trend.</para>
        /// 
        /// <para><b>ASCII Visualization:</b></para>
        /// <code>
        ///     EmaBetween(9, 21) = Pullback Zone
        ///     +--------------------------------------------+
        ///     |  EMA(21) _______________________________   |
        ///     |         /           ↑                  \   |
        ///     |  ______/    * * PULLBACK ZONE * *       \__|
        ///     | /          ↓                               |
        ///     |/  EMA(9) _______________________________   |
        ///     +--------------------------------------------+
        ///           Price in zone = potential entry
        /// </code>
        /// 
        /// <para><b>Example - Pullback entry:</b></para>
        /// <code>
        /// Stock.Ticker("AAPL")
        ///     .IsEmaBetween(9, 21)         // In pullback zone
        ///     .IsDiPositive()              // Still bullish
        ///     .Long(100, Price.Current)
        ///     .Build();
        /// </code>
        /// </remarks>
        public Stock IsEmaBetween(int lowerPeriod, int upperPeriod)
        {
            _conditions.Add(new EmaBetweenCondition(lowerPeriod, upperPeriod));
            return this;
        }

        /// <summary>
        /// Adds a momentum above condition: Momentum >= threshold.
        /// </summary>
        /// <param name="threshold">The momentum threshold value (typically 0 for positive momentum).</param>
        /// <returns>The builder for method chaining.</returns>
        /// <remarks>
        /// <para><b>Momentum Indicator:</b></para>
        /// <para>Measures the rate of price change by comparing current price to a previous price.</para>
        /// <para>Formula: Momentum = Current Price - Price N periods ago</para>
        /// 
        /// <para><b>Interpretation:</b></para>
        /// <list type="bullet">
        ///   <item><b>Momentum > 0:</b> Price is higher than N periods ago (bullish)</item>
        ///   <item><b>Momentum &lt; 0:</b> Price is lower than N periods ago (bearish)</item>
        ///   <item><b>Rising Momentum:</b> Trend is accelerating</item>
        /// </list>
        /// 
        /// <para><b>Example:</b></para>
        /// <code>
        /// // Buy when momentum is positive (price rising)
        /// Stock.Ticker("AAPL")
        ///     .IsMomentumAbove(0)               // Positive momentum
        ///     .IsAboveVwap()                    // Above VWAP
        ///     .Long(100, Price.Current)
        ///     .Build();
        /// </code>
        /// </remarks>
        public Stock IsMomentumAbove(double threshold)
        {
            _conditions.Add(new MomentumAboveCondition(threshold));
            return this;
        }

        /// <summary>
        /// Adds a momentum below condition: Momentum &lt;= threshold.
        /// </summary>
        /// <param name="threshold">The momentum threshold value (typically 0 for negative momentum).</param>
        /// <returns>The builder for method chaining.</returns>
        /// <remarks>
        /// <para><b>Momentum Indicator (Bearish):</b></para>
        /// <para>Used to detect downward price momentum or weakening bullish momentum.</para>
        /// 
        /// <para><b>Example:</b></para>
        /// <code>
        /// // Short when momentum is negative (price falling)
        /// Stock.Ticker("AAPL")
        ///     .IsMomentumBelow(0)               // Negative momentum
        ///     .IsBelowVwap()                    // Below VWAP
        ///     .Short(100, Price.Current)
        ///     .Build();
        /// </code>
        /// </remarks>
        public Stock IsMomentumBelow(double threshold)
        {
            _conditions.Add(new MomentumBelowCondition(threshold));
            return this;
        }

        /// <summary>
        /// Adds a higher lows condition: Recent candle lows are forming an ascending pattern.
        /// </summary>
        /// <param name="lookbackBars">Number of bars to analyze for the pattern (default: 3).</param>
        /// <returns>The builder for method chaining.</returns>
        /// <remarks>
        /// <para><b>Higher Lows Pattern:</b></para>
        /// <para>Detects when recent candlestick lows are each higher than the previous,</para>
        /// <para>indicating building support and potential bullish momentum.</para>
        /// 
        /// <para><b>ASCII Visualization:</b></para>
        /// <code>
        ///                          /\
        ///              /\         /  \
        ///    /\       /  \       /    \
        ///   /  \     /    \     /      \
        ///  /    \___/  ↑   \___/        \
        ///        Higher    Higher
        ///          Low       Low
        /// </code>
        /// 
        /// <para><b>Example:</b></para>
        /// <code>
        /// // Buy when higher lows forming with VWAP support
        /// Stock.Ticker("AAPL")
        ///     .IsHigherLows()             // Ascending support pattern
        ///     .IsAboveVwap()              // Above VWAP
        ///     .Long(100, Price.Current)
        ///     .Build();
        /// </code>
        /// </remarks>
        public Stock IsHigherLows(int lookbackBars = 3)
        {
            _conditions.Add(new HigherLowsCondition(lookbackBars));
            return this;
        }

        /// <summary>
        /// Adds a lower highs condition: Each recent high is lower than the previous.
        /// </summary>
        /// <param name="lookbackBars">Number of bars to analyze (default 3).</param>
        /// <returns>The builder for method chaining.</returns>
        /// <remarks>
        /// <para><b>Lower Highs Pattern (Bearish):</b></para>
        /// <para>Detects when recent candlestick highs are each lower than the previous,</para>
        /// <para>indicating descending resistance and potential bearish momentum.</para>
        /// 
        /// <para><b>ASCII Visualization:</b></para>
        /// <code>
        ///  \        Lower    Lower
        ///   \        High     High
        ///    \___/\   ↓   /\___/
        ///          \     /      \
        ///           \   /        \
        ///            \/           \
        ///                          \/
        /// </code>
        /// 
        /// <para><b>Example:</b></para>
        /// <code>
        /// // Short when lower highs forming with VWAP rejection
        /// Stock.Ticker("AAPL")
        ///     .IsLowerHighs()             // Descending resistance pattern
        ///     .IsBelowVwap()              // Below VWAP
        ///     .Short(100, Price.Current)
        ///     .Build();
        /// </code>
        /// </remarks>
        public Stock IsLowerHighs(int lookbackBars = 3)
        {
            _conditions.Add(new LowerHighsCondition(lookbackBars));
            return this;
        }

        /// <summary>
        /// Adds an EMA turning up condition: EMA slope is flat or positive.
        /// </summary>
        /// <param name="period">The EMA period (e.g., 9, 21).</param>
        /// <returns>The builder for method chaining.</returns>
        /// <remarks>
        /// <para><b>EMA Turning Up:</b></para>
        /// <para>Detects when the EMA slope is transitioning from negative to flat/positive.</para>
        /// <para>This often signals early trend reversal or continuation momentum.</para>
        /// 
        /// <para><b>Warm-up:</b> N+1 candles for EMA(N)</para>
        /// 
        /// <para><b>Example:</b></para>
        /// <code>
        /// Stock.Ticker("AAPL")
        ///     .IsEmaTurningUp(9)          // Short-term EMA flattening/rising
        ///     .IsAboveVwap()              // Above VWAP
        ///     .Long(100, Price.Current)
        ///     .Build();
        /// </code>
        /// </remarks>
        public Stock IsEmaTurningUp(int period)
        {
            _conditions.Add(new EmaTurningUpCondition(period));
            return this;
        }

        /// <summary>
        /// Adds a volume above condition: Current volume >= multiplier × average volume.
        /// </summary>
        /// <param name="multiplier">The volume multiplier (e.g., 1.5 = 150% of average).</param>
        /// <returns>The builder for method chaining.</returns>
        /// <remarks>
        /// <para><b>Volume Spike Detection:</b></para>
        /// <para>Detects unusual volume activity which often confirms breakouts.</para>
        /// 
        /// <para><b>Common Multipliers:</b></para>
        /// <list type="bullet">
        ///   <item><b>1.5:</b> Moderate spike (50% above average)</item>
        ///   <item><b>2.0:</b> Strong spike (100% above average)</item>
        ///   <item><b>3.0:</b> Exceptional spike (200% above average)</item>
        /// </list>
        /// 
        /// <para><b>Warm-up:</b> 20 candles for volume averaging</para>
        /// 
        /// <para><b>Example:</b></para>
        /// <code>
        /// Stock.Ticker("AAPL")
        ///     .IsVolumeAbove(1.5)         // Volume spike confirmation
        ///     .Breakout(150.00)           // Price breakout
        ///     .Long(100, Price.Current)
        ///     .Build();
        /// </code>
        /// </remarks>
        public Stock IsVolumeAbove(double multiplier)
        {
            _conditions.Add(new VolumeAboveCondition(multiplier));
            return this;
        }

        /// <summary>
        /// Adds a close above VWAP condition: Last candle closed above VWAP.
        /// </summary>
        /// <returns>The builder for method chaining.</returns>
        /// <remarks>
        /// <para><b>Close Above VWAP:</b></para>
        /// <para>Stronger signal than just price above VWAP - the candle CLOSED above.</para>
        /// <para>Indicates sustained strength, not just a wick above VWAP.</para>
        /// 
        /// <para><b>ASCII Visualization:</b></para>
        /// <code>
        ///         +---+
        ///         |   | ← Close ABOVE VWAP = Strong
        ///  VWAP ══|═══|══════════════════════════════
        ///         |   |
        ///         +---+
        /// </code>
        /// 
        /// <para><b>No warm-up required</b> (uses last completed candle)</para>
        /// 
        /// <para><b>Example:</b></para>
        /// <code>
        /// Stock.Ticker("AAPL")
        ///     .IsCloseAboveVwap()         // Strong VWAP reclaim
        ///     .IsHigherLows()             // Building support
        ///     .Long(100, Price.Current)
        ///     .Build();
        /// </code>
        /// </remarks>
        public Stock IsCloseAboveVwap()
        {
            _conditions.Add(new CloseAboveVwapCondition());
            return this;
        }

        /// <summary>
        /// Adds a VWAP rejection condition: Wick above VWAP but close below.
        /// </summary>
        /// <returns>The builder for method chaining.</returns>
        /// <remarks>
        /// <para><b>VWAP Rejection (Bearish Signal):</b></para>
        /// <para>Price attempted to break above VWAP but failed to hold.</para>
        /// <para>Indicates selling pressure at VWAP resistance.</para>
        /// 
        /// <para><b>ASCII Visualization:</b></para>
        /// <code>
        ///       │ ← Wick above VWAP
        /// VWAP ═╪═══════════════════════════
        ///     ┌─┴─┐
        ///     │   │ ← Close below = REJECTED
        ///     └───┘
        /// </code>
        /// 
        /// <para><b>No warm-up required</b> (uses last completed candle)</para>
        /// 
        /// <para><b>Example:</b></para>
        /// <code>
        /// Stock.Ticker("AAPL")
        ///     .IsVwapRejection()          // Failed VWAP reclaim
        ///     .IsMomentumBelow(0)         // Negative momentum
        ///     .Short(100, Price.Current)   // Short entry
        ///     .Build();
        /// </code>
        /// </remarks>
        public Stock IsVwapRejection()
        {
            _conditions.Add(new VwapRejectionCondition());
            return this;
        }

        /// <summary>
        /// Adds a Rate of Change (ROC) above condition: ROC >= threshold.
        /// </summary>
        /// <param name="threshold">The ROC threshold (percentage).</param>
        /// <returns>The builder for method chaining.</returns>
        /// <remarks>
        /// <para><b>Rate of Change (ROC):</b></para>
        /// <para>Measures percentage price change over N periods.</para>
        /// <para>Formula: ROC = ((Current - Previous) / Previous) × 100</para>
        /// 
        /// <para><b>Interpretation:</b></para>
        /// <list type="bullet">
        ///   <item><b>ROC > 2%:</b> Strong bullish momentum</item>
        ///   <item><b>ROC > 0%:</b> Price increasing (bullish)</item>
        ///   <item><b>ROC &lt; 0%:</b> Price decreasing (bearish)</item>
        /// </list>
        /// 
        /// <para><b>Warm-up:</b> 11 candles for ROC(10)</para>
        /// 
        /// <para><b>Example:</b></para>
        /// <code>
        /// Stock.Ticker("AAPL")
        ///     .IsRocAbove(2)              // Strong upward momentum (2%+)
        ///     .IsAboveVwap()
        ///     .Long(100, Price.Current)
        ///     .Build();
        /// </code>
        /// </remarks>
        public Stock IsRocAbove(double threshold)
        {
            _conditions.Add(new RocAboveCondition(threshold));
            return this;
        }

        /// <summary>
        /// Adds a Rate of Change (ROC) below condition: ROC &lt;= threshold.
        /// </summary>
        /// <param name="threshold">The ROC threshold (percentage).</param>
        /// <returns>The builder for method chaining.</returns>
        /// <remarks>
        /// <para><b>ROC Below (Bearish):</b></para>
        /// <para>Detects negative or weakening momentum.</para>
        /// 
        /// <para><b>Warm-up:</b> 11 candles for ROC(10)</para>
        /// 
        /// <para><b>Example:</b></para>
        /// <code>
        /// Stock.Ticker("AAPL")
        ///     .IsRocBelow(-2)             // Strong downward momentum (-2%+)
        ///     .IsBelowVwap()
        ///     .Short(100, Price.Current)   // Short entry
        ///     .Build();
        /// </code>
        /// </remarks>
        public Stock IsRocBelow(double threshold)
        {
            _conditions.Add(new RocBelowCondition(threshold));
            return this;
        }

        // ====================================================================
        // GAP CONDITION METHODS
        // ====================================================================

        /// <summary>
        /// Adds a gap up condition: Price has gapped up by the specified percentage from previous close.
        /// </summary>
        /// <param name="percentage">The minimum gap percentage (e.g., 5 for 5%).</param>
        /// <returns>The builder for method chaining.</returns>
        /// <remarks>
        /// <para><b>Gap Up Detection:</b></para>
        /// <para>A gap up occurs when the current price is significantly higher than the previous
        /// session's close. This often indicates strong buying pressure or positive news.</para>
        /// 
        /// <para><b>Requirements:</b></para>
        /// <list type="bullet">
        ///   <item>The StrategyRunner fetches previous close from IBKR historical data</item>
        ///   <item>Best used at market open or during premarket</item>
        /// </list>
        /// 
        /// <para><b>ASCII Visualization:</b></para>
        /// <code>
        ///                   ┌────┐
        ///                   │    │  Current Price (gapped up)
        ///           Gap Up  │    │
        ///           (5%+)   └────┘
        ///                     ↑
        ///     ─────────────────────────── Gap
        ///                     ↓
        ///     ┌────┐
        ///     │    │  Previous Close
        ///     └────┘
        /// </code>
        /// 
        /// <para><b>Example - Gap and Go strategy:</b></para>
        /// <code>
        /// Stock.Ticker("NVDA")
        ///     .TimeFrame(TradingSession.PreMarket)
        ///     .GapUp(5)                    // Gapped up 5%+ from previous close
        ///     .IsAboveVwap()               // Holding above VWAP
        ///     .IsDiPositive()              // Bullish momentum
        ///     .Long(100, Price.Current)
        ///     .AdaptiveOrder(AdaptiveOrderMode.Aggressive)
        ///     .Build();
        /// </code>
        /// </remarks>
        public Stock GapUp(double percentage)
        {
            _conditions.Add(new GapUpCondition(percentage));
            return this;
        }

        /// <summary>
        /// Adds a gap down condition: Price has gapped down by the specified percentage from previous close.
        /// </summary>
        /// <param name="percentage">The minimum gap percentage (e.g., 5 for 5%).</param>
        /// <returns>The builder for method chaining.</returns>
        /// <remarks>
        /// <para><b>Gap Down Detection:</b></para>
        /// <para>A gap down occurs when the current price is significantly lower than the previous
        /// session's close. This often indicates selling pressure or negative news.</para>
        /// 
        /// <para><b>ASCII Visualization:</b></para>
        /// <code>
        ///     ┌────┐
        ///     │    │  Previous Close
        ///     └────┘
        ///                     ↑
        ///     ─────────────────────────── Gap
        ///                     ↓
        ///           Gap Down  ┌────┐
        ///           (5%+)     │    │  Current Price (gapped down)
        ///                     └────┘
        /// </code>
        /// 
        /// <para><b>Example - Gap down reversal strategy:</b></para>
        /// <code>
        /// Stock.Ticker("AAPL")
        ///     .TimeFrame(TradingSession.RTH)
        ///     .GapDown(3)                  // Gapped down 3%+ from previous close
        ///     .IsRsi(RsiState.Oversold)    // RSI oversold (potential reversal)
        ///     .Long(100, Price.Current)     // Buy the dip
        ///     .TakeProfit(155)
        ///     .StopLoss(145)
        ///     .Build();
        /// </code>
        /// </remarks>
        public Stock GapDown(double percentage)
        {
            _conditions.Add(new GapDownCondition(percentage));
            return this;
        }

        /// <summary>
        /// Adds a gap up condition: Price has gapped up by the specified percentage from previous close.
        /// Alias for <see cref="GapUp"/>.
        /// </summary>
        /// <param name="percentage">The minimum gap percentage (e.g., 5 for 5%).</param>
        /// <returns>The builder for method chaining.</returns>
        public Stock IsGapUp(double percentage) => GapUp(percentage);

        /// <summary>
        /// Adds a gap down condition: Price has gapped down by the specified percentage from previous close.
        /// Alias for <see cref="GapDown"/>.
        /// </summary>
        /// <param name="percentage">The minimum gap percentage (e.g., 5 for 5%).</param>
        /// <returns>The builder for method chaining.</returns>
        public Stock IsGapDown(double percentage) => GapDown(percentage);

        // ====================================================================
        // SHORTHAND INDICATOR METHODS
        // ====================================================================

        /// <summary>
        /// Adds a MACD bullish condition: MACD line > Signal line (momentum up).
        /// Shorthand for <c>.IsMacd(MacdState.Bullish)</c>.
        /// </summary>
        /// <returns>The builder for method chaining.</returns>
        public Stock IsMacdBullish() => IsMacd(Enums.MacdState.Bullish);

        /// <summary>
        /// Adds a MACD bearish condition: MACD line &lt; Signal line (momentum down).
        /// Shorthand for <c>.IsMacd(MacdState.Bearish)</c>.
        /// </summary>
        /// <returns>The builder for method chaining.</returns>
        public Stock IsMacdBearish() => IsMacd(Enums.MacdState.Bearish);

        /// <summary>
        /// Adds a +DI positive condition: +DI > -DI (bullish pressure dominates).
        /// Shorthand for <c>.IsDI(DiDirection.Positive)</c>.
        /// </summary>
        /// <param name="minDifference">Minimum difference between +DI and -DI (default: 0).</param>
        /// <returns>The builder for method chaining.</returns>
        public Stock IsDiPositive(double minDifference = 0) => IsDI(Enums.DiDirection.Positive, minDifference);

        /// <summary>
        /// Adds a -DI negative condition: -DI > +DI (bearish pressure dominates).
        /// Shorthand for <c>.IsDI(DiDirection.Negative)</c>.
        /// </summary>
        /// <param name="minDifference">Minimum difference between -DI and +DI (default: 0).</param>
        /// <returns>The builder for method chaining.</returns>
        public Stock IsDiNegative(double minDifference = 0) => IsDI(Enums.DiDirection.Negative, minDifference);

        // ====================================================================
        // ORDER METHODS - Returns StrategyBuilder for chaining
        // ====================================================================

        /// <summary>
        /// Creates an order with the specified direction.
        /// </summary>
        /// <param name="direction">Order direction: Long (buy to open) or Short (sell to open).</param>
        /// <returns>A <see cref="StrategyBuilder"/> for additional configuration.</returns>
        /// <example>
        /// <code>
        /// Stock.Ticker("AAPL")
        ///     .Entry(150)
        ///     .Order(OrderDirection.Long)
        ///     .Quantity(100)
        ///     .TakeProfit(155)
        ///     .Build();
        /// </code>
        /// </example>
        public StrategyBuilder Order(OrderDirection direction)
        {
            var side = direction == OrderDirection.Long ? OrderSide.Buy : OrderSide.Sell;
            return new StrategyBuilder(this, side);
        }

        /// <summary>
        /// Creates a LONG order (buy to open).
        /// </summary>
        public StrategyBuilder Long() => Order(OrderDirection.Long);

        /// <summary>
        /// Creates a SHORT order (sell to open).
        /// </summary>
        public StrategyBuilder Short() => Order(OrderDirection.Short);

        /// <summary>
        /// Creates a CLOSE order to exit a long position.
        /// </summary>
        public StrategyBuilder CloseLong()
        {
            return new StrategyBuilder(this, OrderSide.Sell, isClosingPosition: true);
        }

        /// <summary>
        /// Creates a CLOSE order to exit a short position.
        /// </summary>
        public StrategyBuilder CloseShort()
        {
            return new StrategyBuilder(this, OrderSide.Buy, isClosingPosition: true);
        }

        internal TradingStrategy BuildStrategy(OrderAction order)
        {
            // AutonomousTrading handles its own entry conditions - no manual conditions needed
            if (_conditions.Count == 0 && !order.UseAutonomousTrading)
                throw new InvalidOperationException("Strategy must have at least one condition (or use AutonomousTrading).");

            return new TradingStrategy
            {
                Id = _id,
                Name = _name,
                Symbol = _symbol,
                Exchange = _exchange,
                PrimaryExchange = _primaryExchange,
                Currency = _currency,
                SecType = _secType,
                Conditions = _conditions.AsReadOnly(),
                Order = order,
                Enabled = _enabled,
                StartTime = _startTime,
                EndTime = _endTime,
                Session = _session,
                Notes = _notes,
                RepeatEnabled = _repeatEnabled
            };
        }

        internal string Symbol => _symbol;
        internal Guid IdValue => _id;
        internal string NameValue => _name;
        internal string ExchangeValue => _exchange;
        internal string? PrimaryExchangeValue => _primaryExchange;
        internal string CurrencyValue => _currency;
        internal string SecTypeValue => _secType;
        internal IReadOnlyList<IStrategyCondition> Conditions => _conditions.AsReadOnly();
        internal bool EnabledValue => _enabled;
        internal TimeOnly? StartTimeValue => _startTime;
        internal TimeOnly? EndTimeValue { get => _endTime; set => _endTime = value; }
        internal TradingSession? SessionValue => _session;
        internal string? NotesValue => _notes;
        internal bool RepeatEnabledValue { get => _repeatEnabled; set => _repeatEnabled = value; }
    }

    /// <summary>
    /// Builder for configuring strategy order details.
    /// Each method does one thing for clean fluent chaining.
    /// </summary>
    /// <example>
    /// <code>
    /// Stock.Ticker("AAPL")
    ///     .Entry(150)
    ///     .Order(OrderDirection.Long)
    ///     .Quantity(100)
    ///     .TakeProfit(155)
    ///     .StopLoss(145)
    ///     .Build();
    /// </code>
    /// </example>
    public sealed class StrategyBuilder
    {
        private readonly Stock _stock;
        private OrderSide _side;
        private int _quantity = 1;
        private Price _priceType = Price.Current;
        private readonly bool _isClosingPosition;
        private double? _takeProfit;
        private double? _stopLoss;
        private bool _enableTrailingStopLoss;
        private double _trailingStopLossPercent;
        private AtrStopLossConfig? _atrStopLoss;
        private AutonomousTradingConfig? _autonomousTrading;
        private TimeOnly? _closePositionTime;
        private bool _closePositionOnlyIfProfitable = false; // Default false - use .IsProfitable() to enable
        private Enums.TimeInForce _timeInForce = Enums.TimeInForce.GoodTillCancel;
        private bool _outsideRth = true;
        private bool _takeProfitOutsideRth = true;
        private Enums.OrderType _orderType = Enums.OrderType.Limit;
        private bool _allOrNone = false;
        private AdxTakeProfitConfig? _adxTakeProfit;

        /// <summary>
        /// Gets whether this order is closing an existing position.
        /// </summary>
        public bool IsClosingPosition => _isClosingPosition;

        /// <summary>
        /// Gets whether this order is opening a new position.
        /// </summary>
        public bool IsOpeningPosition => !_isClosingPosition;

        internal StrategyBuilder(Stock stock, OrderSide side, bool isClosingPosition = false)
        {
            _stock = stock;
            _side = side;
            _isClosingPosition = isClosingPosition;
        }

        /// <summary>
        /// Sets the order quantity.
        /// </summary>
        public StrategyBuilder Quantity(int quantity)
        {
            _quantity = quantity;
            return this;
        }

        /// <summary>
        /// Sets the order quantity (alias for Quantity).
        /// </summary>
        public StrategyBuilder Qty(int quantity) => Quantity(quantity);

        /// <summary>
        /// Sets the price type for order execution.
        /// </summary>
        public StrategyBuilder PriceType(Price priceType)
        {
            _priceType = priceType;
            return this;
        }

        /// <summary>
        /// Sets the order type (Market or Limit).
        /// </summary>
        public StrategyBuilder OrderType(Enums.OrderType type)
        {
            _orderType = type;
            return this;
        }

        /// <summary>
        /// Sets a fixed take profit price.
        /// </summary>
        public StrategyBuilder TakeProfit(double price)
        {
            _takeProfit = price;
            _adxTakeProfit = null;
            return this;
        }

        /// <summary>
        /// Sets the stop loss price.
        /// </summary>
        public StrategyBuilder StopLoss(double price)
        {
            _stopLoss = price;
            return this;
        }

        /// <summary>
        /// Sets the ADX-based take profit configuration.
        /// </summary>
        public StrategyBuilder AdxTakeProfit(AdxTakeProfitConfig config)
        {
            _adxTakeProfit = config ?? throw new ArgumentNullException(nameof(config));
            _takeProfit = config.ConservativeTarget;
            return this;
        }

        /// <summary>
        /// Enables a trailing stop loss that moves up as price increases.
        /// Sells immediately if price drops below the trailing stop level.
        /// </summary>
        /// <param name="percent">Percentage below current price (e.g., Percent.Ten for 10%).</param>
        public StrategyBuilder TrailingStopLoss(double percent)
        {
            _enableTrailingStopLoss = true;
            _trailingStopLossPercent = percent;
            _atrStopLoss = null; // Clear ATR when using percentage
            return this;
        }

        /// <summary>
        /// Enables an ATR-based trailing stop loss that adapts to market volatility.
        /// Stop distance = ATR × Multiplier, providing tighter stops in calm markets
        /// and wider stops in volatile conditions.
        /// </summary>
        /// <param name="config">ATR stop loss configuration (use Atr.Tight, Atr.Balanced, Atr.Loose, or Atr.Multiplier()).</param>
        /// <returns>The builder for method chaining.</returns>
        /// <remarks>
        /// <para><b>ATR Multiplier Guidelines:</b></para>
        /// <list type="bullet">
        ///   <item>1.5× ATR (Tight) - More stops, smaller losses. Good for scalping.</item>
        ///   <item>2.0× ATR (Balanced) - Standard swing trading. Good risk/reward.</item>
        ///   <item>3.0× ATR (Loose) - Trend following. Fewer stops, larger swings.</item>
        /// </list>
        /// 
        /// <para><b>Example:</b></para>
        /// <code>
        /// // Preset configurations
        /// .TrailingStopLoss(Atr.Balanced)     // 2.0× ATR (recommended)
        /// .TrailingStopLoss(Atr.Tight)        // 1.5× ATR (aggressive)
        /// .TrailingStopLoss(Atr.Loose)        // 3.0× ATR (trend-following)
        /// 
        /// // Custom multiplier
        /// .TrailingStopLoss(Atr.Multiplier(2.5))  // 2.5× ATR
        /// </code>
        /// 
        /// <para><b>How it works:</b></para>
        /// <list type="number">
        ///   <item>ATR is calculated from price volatility over 14 periods.</item>
        ///   <item>Stop distance = ATR × Multiplier (e.g., $1.20 ATR × 2.0 = $2.40).</item>
        ///   <item>Stop trails upward as price makes new highs.</item>
        ///   <item>Triggers sell when price drops ATR distance below high water mark.</item>
        /// </list>
        /// </remarks>
        public StrategyBuilder TrailingStopLoss(AtrStopLossConfig config)
        {
            _enableTrailingStopLoss = true;
            _atrStopLoss = config ?? throw new ArgumentNullException(nameof(config));
            // Set a fallback percentage in case ATR isn't ready
            _trailingStopLossPercent = Math.Min(config.MaxStopPercent, 0.15);
            return this;
        }

        /// <summary>
        /// Enables fully autonomous AI-driven trading that monitors all indicators
        /// and independently decides when to enter and exit positions.
        /// </summary>
        /// <param name="mode">The trading aggressiveness mode (Conservative, Balanced, Aggressive).</param>
        /// <returns>The builder for method chaining.</returns>
        /// <remarks>
        /// <para><b>Modes:</b></para>
        /// <list type="bullet">
        /// <item><description>Automatically adjusts thresholds based on market conditions</description></item>
        /// <item><description>More aggressive in strong trends (high ADX)</description></item>
        /// <item><description>More conservative in ranging/volatile markets</description></item>
        /// <item><description>Accounts for indicator agreement, RSI extremes, and time of day</description></item>
        /// </list>
        /// <para><b>Example:</b></para>
        /// <code>
        /// Stock.Ticker("AAPL")
        ///     .AutonomousTrading()   // Self-adjusting - no mode needed
        ///     .Build();
        /// </code>
        /// </remarks>
        public StrategyBuilder AutonomousTrading(string mode = "")
        {
            // Always use self-adjusting Optimized mode - mode parameter ignored for backwards compatibility
            _autonomousTrading = Autonomous.Optimized;
            return this;
        }

        /// <summary>
        /// Enables autonomous trading with a specific configuration.
        /// </summary>
        /// <param name="config">The autonomous trading configuration.</param>
        /// <returns>The builder for method chaining.</returns>
        public StrategyBuilder AutonomousTrading(AutonomousTradingConfig config)
        {
            _autonomousTrading = config ?? throw new ArgumentNullException(nameof(config));
            return this;
        }

        /// <summary>
        /// Alias for <see cref="AutonomousTrading"/>.
        /// </summary>
        public StrategyBuilder IsAutonomousTrading(string mode = "") => AutonomousTrading(mode);

        /// <summary>
        /// Sets the time to exit the position if still open.
        /// Chain with .IsProfitable() to only exit if the position is profitable.
        /// </summary>
        public StrategyBuilder ExitStrategy(TimeOnly time)
        {
            _closePositionTime = time;
            return this;
        }

        /// <summary>
        /// Only execute the previous exit strategy if the position is profitable.
        /// </summary>
        public StrategyBuilder IsProfitable()
        {
            _closePositionOnlyIfProfitable = true;
            return this;
        }

        /// <summary>
        /// Sets the time in force for the order.
        /// </summary>
        public StrategyBuilder TimeInForce(Enums.TimeInForce tif)
        {
            _timeInForce = tif;
            return this;
        }

        /// <summary>
        /// Allows trading outside regular trading hours.
        /// </summary>
        public StrategyBuilder OutsideRTH(bool outsideRth = true)
        {
            _outsideRth = outsideRth;
            return this;
        }

        /// <summary>
        /// Allows take profit outside regular trading hours.
        /// </summary>
        public StrategyBuilder TakeProfitOutsideRTH(bool outsideRth = true)
        {
            _takeProfitOutsideRth = outsideRth;
            return this;
        }

        /// <summary>
        /// Requires the entire order to be filled at once or not at all.
        /// </summary>
        /// <param name="allOrNone">True to require complete fill; false (default) allows partial fills.</param>
        /// <returns>The builder for method chaining.</returns>
        /// <remarks>
        /// <para><b>Behavior:</b></para>
        /// <list type="bullet">
        ///   <item>When true: Order must be filled completely in a single transaction.</item>
        ///   <item>When false (default): Partial fills are allowed.</item>
        /// </list>
        /// 
        /// <para><b>Use Cases:</b></para>
        /// <list type="bullet">
        ///   <item>Use true when you need the exact quantity for hedging or specific strategies.</item>
        ///   <item>Use false for normal trading where partial fills are acceptable.</item>
        /// </list>
        /// 
        /// <para><b>Warning:</b> AllOrNone orders may take longer to fill or may not fill
        /// at all if the full quantity is not available at your price.</para>
        /// 
        /// <para><b>IBKR API:</b> Maps to <c>order.AllOrNone = true</c></para>
        /// 
        /// <para><b>Example:</b></para>
        /// <code>
        /// Stock.Ticker("AAPL")
        ///     .Breakout(150)
        ///     .Long(100, Price.Current)
        ///     .AllOrNone()           // Must fill all 100 shares or none
        ///     .Build();
        /// </code>
        /// </remarks>
        public StrategyBuilder AllOrNone(bool allOrNone = true)
        {
            _allOrNone = allOrNone;
            return this;
        }

        /// <summary>
        /// Enables repeating for this strategy.
        /// When enabled, the strategy resets after completion and can fire again when conditions are met.
        /// </summary>
        /// <param name="repeat">True to repeat (default), false to fire once.</param>
        /// <returns>The builder for method chaining.</returns>
        /// <remarks>
        /// <para><b>Behavior:</b></para>
        /// <list type="bullet">
        ///   <item>When true: After take profit or stop loss fills, the strategy resets and waits for conditions again.</item>
        ///   <item>When false (default): The strategy fires once and stops.</item>
        /// </list>
        /// 
        /// <para><b>Example - Repeating scalp strategy:</b></para>
        /// <code>
        /// Stock.Ticker("ABC")
        ///     .TimeFrame(TradingSession.RTH)
        ///     .IsPriceAbove(5.00)
        ///     .IsAboveVwap()
        ///     .Long(100, Price.Current)
        ///     .TakeProfit(6.00)
        ///     .StopLoss(4.50)
        ///     .Repeat()  // Will buy again at $5, sell at $6, repeat
        ///     .Build();
        /// </code>
        /// </remarks>
        public StrategyBuilder Repeat(bool repeat = true)
        {
            _stock.RepeatEnabledValue = repeat;
            return this;
        }

        /// <summary>
        /// Builds the strategy with current configuration.
        /// </summary>
        public TradingStrategy Build()
        {
            var order = new OrderAction
            {
                Side = _side,
                Quantity = _quantity,
                Type = _orderType,
                PriceType = _priceType,
                TimeInForce = _timeInForce,
                OutsideRth = _outsideRth,
                AllOrNone = _allOrNone,
                EnableTakeProfit = _takeProfit.HasValue || _adxTakeProfit != null,
                TakeProfitPrice = _takeProfit,
                TakeProfitOutsideRth = _takeProfitOutsideRth,
                AdxTakeProfit = _adxTakeProfit,
                EnableStopLoss = _stopLoss.HasValue,
                StopLossPrice = _stopLoss,
                EnableTrailingStopLoss = _enableTrailingStopLoss,
                TrailingStopLossPercent = _trailingStopLossPercent,
                AtrStopLoss = _atrStopLoss,
                AdaptiveOrder = null, // Deprecated - adaptive TP/SL now integrated in AutonomousTrading
                AutonomousTrading = _autonomousTrading,
                ClosePositionTime = _closePositionTime,
                ClosePositionOnlyIfProfitable = _closePositionOnlyIfProfitable
            };

            return _stock.BuildStrategy(order);
        }

        /// <summary>
        /// Sets the end time for monitoring the strategy and builds it (Eastern Time).
        /// </summary>
        /// <param name="endTime">The time to stop monitoring.</param>
        /// <returns>The completed <see cref="TradingStrategy"/>.</returns>
        /// <remarks>
        /// <para><b>Best Practice:</b> Use with <see cref="Stock.Start"/> for a complete time window:</para>
        /// <code>
        /// Stock.Ticker("AAPL")
        ///     .Start(MarketTime.PreMarket.Start)  // 4:00 AM ET
        ///     .Breakout(150)
        ///     .Long(100, Price.Current)
        ///     .End(MarketTime.PreMarket.End);     // 9:30 AM ET
        /// </code>
        /// <para><b>Tip:</b> Use <see cref="MarketTime"/> helper for common times.</para>
        /// </remarks>
        public TradingStrategy End(TimeOnly endTime)
        {
            _stock.EndTimeValue = endTime;
            return Build();
        }

        /// <summary>
        /// Implicit conversion to TradingStrategy for cleaner syntax.
        /// </summary>
        public static implicit operator TradingStrategy(StrategyBuilder builder) => builder.Build();
    }
}




