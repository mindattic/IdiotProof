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
//   Stock.Ticker("SYMBOL")     // Start builder
//       .TimeFrame(...)          // Optional: when to monitor (comment out for immediate testing)
//       .Breakout(...)         // Add conditions
//       .Pullback(...)
//       .IsAboveVwap()
//       .Buy(...)              // Returns StrategyBuilder (opens position)
//       .Sell(...)             // Returns StrategyBuilder (opens short or exits)
//       .Close(...)            // Returns StrategyBuilder (closes existing position)
//       .TakeProfit(...)       // Exit configuration
//       .StopLoss(...)
//       .Build()               // Terminal: returns TradingStrategy
//
// OPENING POSITIONS:
// var buyStrategy = Stock.Ticker("AAPL")
//     .TimeFrame(new TimeOnly(4, 0), new TimeOnly(9, 30))  // Comment out to test immediately
//     .Breakout(150)
//     .Buy(quantity: 100, Price.Current)
//     .TakeProfit(155)
//     .StopLoss(148)
//     .Build();
//
// CLOSING POSITIONS:
// var closeStrategy = Stock.Ticker("AAPL")
//     .IsPriceAbove(155)                   // Exit when price hits target
//     .Close(quantity: 100)               // Sell to close long position
//     .TimeInForce(TIF.GTC)               // TIF is NOT automatic
//     .OutsideRTH(true)                   // OutsideRth is NOT automatic
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
    ///   <item>Always add at least one condition before calling <see cref="Buy"/> or <see cref="Sell"/>.</item>
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
    ///     .Buy(100)           // Order (Stock returns StrategyBuilder)
    ///     .TakeProfit(155)    // Exit config (StrategyBuilder returns StrategyBuilder)
    ///     .Build()            // Terminal (StrategyBuilder returns TradingStrategy)
    /// </code>
    /// </remarks>
    public sealed class Stock
    {
        private readonly string _symbol;
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
        ///     .Buy(100, Price.Current)
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
        ///     .Buy(100, Price.Current)
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
        ///     .Buy(100, Price.Current)
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
        ///     .Buy(100, Price.Current)
        ///     .Build();
        /// 
        /// // Sell when RSI indicates overbought with custom threshold
        /// Stock.Ticker("AAPL")
        ///     .IsRsi(RsiState.Overbought, 80)   // RSI >= 80
        ///     .Sell(100, Price.Current)
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
        ///     .Buy(100, Price.Current)
        ///     .Build();
        /// 
        /// // Range-bound strategy
        /// Stock.Ticker("AAPL")
        ///     .IsAdx(Comparison.Lte, 20)       // ADX &lt;= 20 (no trend)
        ///     .Breakout(150)
        ///     .Buy(100, Price.Current)
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
        ///     .Buy(100, Price.Current)
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
        ///     .Buy(100, Price.Current)
        ///     .Build();
        /// 
        /// // Require significant DI difference for stronger signal
        /// Stock.Ticker("AAPL")
        ///     .IsDI(DiDirection.Positive, 5)     // +DI must exceed -DI by at least 5
        ///     .Buy(100, Price.Current)
        ///     .Build();
        /// </code>
        /// </remarks>
        public Stock IsDI(Enums.DiDirection direction, double minDifference = 0)
        {
            _conditions.Add(new DiCondition(direction, minDifference));
            return this;
        }

        // ====================================================================
        // ORDER METHODS - Returns StrategyBuilder for chaining
        // ====================================================================

        /// <summary>
        /// Creates a BUY order and returns a builder for additional configuration.
        /// </summary>
        /// <param name="quantity">Number of shares to buy.</param>
        /// <param name="priceType">Price type for order execution (default: Current).</param>
        /// <param name="orderType">Order type (Market or Limit, default: Limit).</param>
        public StrategyBuilder Buy(int quantity, Price priceType = Price.Current, OrderType orderType = OrderType.Limit)
        {
            return new StrategyBuilder(this, OrderSide.Buy, quantity, priceType, orderType);
        }

        /// <summary>
        /// Creates a SELL order and returns a builder for additional configuration.
        /// </summary>
        /// <param name="quantity">Number of shares to sell.</param>
        /// <param name="priceType">Price type for order execution (default: Current).</param>
        /// <param name="orderType">Order type (Market or Limit, default: Limit).</param>
        public StrategyBuilder Sell(int quantity, Price priceType = Price.Current, OrderType orderType = OrderType.Limit)
        {
            return new StrategyBuilder(this, OrderSide.Sell, quantity, priceType, orderType);
        }

        /// <summary>
        /// Creates a CLOSE order to exit an existing position.
        /// </summary>
        /// <param name="quantity">Number of shares to close (your full position size).</param>
        /// <param name="positionSide">The side of your current position: Buy = long position (will sell to close), 
        /// Sell = short position (will buy to close). Default: Buy (closes a long position).</param>
        /// <param name="priceType">Price type for order execution (default: Current).</param>
        /// <param name="orderType">Order type (Market or Limit, default: Limit).</param>
        /// <returns>A <see cref="StrategyBuilder"/> for additional configuration.</returns>
        /// <remarks>
        /// <para><b>Behavior:</b></para>
        /// <list type="bullet">
        ///   <item>For long positions (<paramref name="positionSide"/> = Buy): Creates a SELL order.</item>
        ///   <item>For short positions (<paramref name="positionSide"/> = Sell): Creates a BUY order.</item>
        ///   <item>Quantity defaults to your full position size - you must specify it.</item>
        ///   <item>Does NOT automatically set OutsideRth or TimeInForce - configure separately.</item>
        /// </list>
        /// 
        /// <para><b>IBKR API Translation:</b></para>
        /// <code>
        /// order.action = "SELL"        // For closing long positions
        /// order.totalQuantity = 25     // Your position size
        /// order.orderType = "LMT"      // Or "MKT" for market orders
        /// order.lmtPrice = 39.35       // For limit orders
        /// order.tif = "GTC"            // Set via .TimeInForce(TIF.GTC)
        /// order.outsideRth = True      // Set via .OutsideRTH(true)
        /// </code>
        /// 
        /// <para><b>Example - Close long position:</b></para>
        /// <code>
        /// Stock.Ticker("AAPL")
        ///     .IsPriceAbove(150)
        ///     .Close(quantity: 100, positionSide: OrderSide.Buy)  // Sells 100 shares
        ///     .TimeInForce(TIF.GTC)
        ///     .OutsideRTH(true)
        ///     .Build();
        /// </code>
        /// 
        /// <para><b>Example - Close short position:</b></para>
        /// <code>
        /// Stock.Ticker("TSLA")
        ///     .IsPriceBelow(200)
        ///     .Close(quantity: 50, positionSide: OrderSide.Sell)  // Buys 50 shares to cover
        ///     .TimeInForce(TIF.Day)
        ///     .Build();
        /// </code>
        /// </remarks>
        public StrategyBuilder Close(int quantity, OrderSide positionSide = OrderSide.Buy, Price priceType = Price.Current, OrderType orderType = OrderType.Limit)
        {
            // Close is the opposite action of your position
            // Long position (Buy) -> Sell to close
            // Short position (Sell) -> Buy to close
            var closeAction = positionSide == OrderSide.Buy ? OrderSide.Sell : OrderSide.Buy;
            return new StrategyBuilder(this, closeAction, quantity, priceType, orderType, isClosingPosition: true);
        }

        /// <summary>
        /// Creates a CLOSE order to exit a LONG position (sells shares).
        /// </summary>
        /// <param name="quantity">Number of shares to sell (your full position size).</param>
        /// <param name="priceType">Price type for order execution (default: Current).</param>
        /// <param name="orderType">Order type (Market or Limit, default: Market).</param>
        /// <returns>A <see cref="StrategyBuilder"/> for additional configuration.</returns>
        /// <remarks>
        /// <para>Shorthand for <c>.Close(quantity, OrderSide.Buy)</c></para>
        /// <para><b>Action:</b> SELL (to close long position)</para>
        /// </remarks>
        public StrategyBuilder CloseLong(int quantity, Price priceType = Price.Current, OrderType orderType = OrderType.Limit)
        {
            return Close(quantity, OrderSide.Buy, priceType, orderType);
        }

        /// <summary>
        /// Creates a CLOSE order to exit a SHORT position (buys shares to cover).
        /// </summary>
        /// <param name="quantity">Number of shares to buy back (your full position size).</param>
        /// <param name="priceType">Price type for order execution (default: Current).</param>
        /// <param name="orderType">Order type (Market or Limit, default: Market).</param>
        /// <returns>A <see cref="StrategyBuilder"/> for additional configuration.</returns>
        /// <remarks>
        /// <para>Shorthand for <c>.Close(quantity, OrderSide.Sell)</c></para>
        /// <para><b>Action:</b> BUY (to cover short position)</para>
        /// </remarks>
        public StrategyBuilder CloseShort(int quantity, Price priceType = Price.Current, OrderType orderType = OrderType.Limit)
        {
            return Close(quantity, OrderSide.Sell, priceType, orderType);
        }

        // ====================================================================
        // LEGACY ORDER METHODS (Terminal - builds the strategy directly)
        // ====================================================================

        /// <summary>
        /// Creates a BUY order and builds the strategy (legacy method).
        /// </summary>
        public TradingStrategy Buy(
            int quantity = 1000,
            double? takeProfit = null,
            double takeProfitOffset = 0.30,
            double? limitPrice = null,
            double limitOffset = 0.02,
            TimeInForce timeInForce = TimeInForce.GoodTillCancel,
            bool outsideRth = true,
            bool takeProfitOutsideRth = true,
            OrderType orderType = OrderType.Limit,
            TimeOnly? endTime = null)
        {
            return BuildStrategy(new OrderAction
            {
                Side = OrderSide.Buy,
                Quantity = quantity,
                Type = orderType,
                LimitPrice = limitPrice,
                LimitOffset = limitOffset,
                TimeInForce = timeInForce,
                OutsideRth = outsideRth,
                EnableTakeProfit = takeProfit.HasValue || takeProfitOffset > 0,
                TakeProfitPrice = takeProfit,
                TakeProfitOffset = takeProfitOffset,
                TakeProfitOutsideRth = takeProfitOutsideRth,
                EndTime = endTime
            });
        }

        /// <summary>
        /// Creates a SELL order and builds the strategy (legacy method).
        /// </summary>
        public TradingStrategy Sell(
            int quantity = 1000,
            double? stopLoss = null,
            double stopLossOffset = 0.20,
            double? limitPrice = null,
            double limitOffset = 0.02,
            TimeInForce timeInForce = TimeInForce.GoodTillCancel,
            bool outsideRth = true,
            OrderType orderType = OrderType.Limit)
        {
            return BuildStrategy(new OrderAction
            {
                Side = OrderSide.Sell,
                Quantity = quantity,
                Type = orderType,
                LimitPrice = limitPrice,
                LimitOffset = limitOffset,
                TimeInForce = timeInForce,
                OutsideRth = outsideRth,
                EnableTakeProfit = false,
                EnableStopLoss = stopLoss.HasValue || stopLossOffset > 0,
                StopLossPrice = stopLoss,
                StopLossOffset = stopLossOffset
            });
        }

        /// <summary>
        /// Creates a market BUY order and builds the strategy.
        /// </summary>
        public TradingStrategy MarketBuy(
            int quantity = 1000,
            double? takeProfit = null,
            double takeProfitOffset = 0.30,
            TimeInForce timeInForce = TimeInForce.GoodTillCancel,
            bool outsideRth = true)
        {
            return Buy(quantity, takeProfit, takeProfitOffset, 
                orderType: OrderType.Market, 
                timeInForce: timeInForce, 
                outsideRth: outsideRth);
        }

        internal TradingStrategy BuildStrategy(OrderAction order)
        {
            if (_conditions.Count == 0)
                throw new InvalidOperationException("Strategy must have at least one condition.");

            return new TradingStrategy
            {
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
                Notes = _notes
            };
        }

        internal string Symbol => _symbol;
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
    }

    /// <summary>
    /// Builder for configuring strategy order details after Buy/Sell/Close.
    /// </summary>
    /// <remarks>
    /// <para><b>Order Intent:</b></para>
    /// <list type="bullet">
    ///   <item><see cref="IsOpeningPosition"/>: True for Buy/Sell orders that open new positions.</item>
    ///   <item><see cref="IsClosingPosition"/>: True for Close orders that exit existing positions.</item>
    /// </list>
    /// <para>This distinction is informational - the IBKR API receives the same order structure
    /// regardless of intent. The difference is in how you configure TIF, OutsideRth, etc.</para>
    /// </remarks>
    public sealed class StrategyBuilder
    {
        private readonly Stock _stock;
        private readonly OrderSide _side;
        private readonly int _quantity;
        private readonly Price _priceType;
        private readonly bool _isClosingPosition;
        private double? _takeProfit;
        private double? _stopLoss;
        private bool _enableTrailingStopLoss;
        private double _trailingStopLossPercent;
        private AtrStopLossConfig? _atrStopLoss;
        private TimeOnly? _closePositionTime;
        private bool _closePositionOnlyIfProfitable = true;
        private Enums.TimeInForce _timeInForce = Enums.TimeInForce.GoodTillCancel;
        private bool _outsideRth = true;
        private bool _takeProfitOutsideRth = true;
        private Enums.OrderType _orderType = Enums.OrderType.Limit;
        private bool _allOrNone = false;
        private AdxTakeProfitConfig? _adxTakeProfit;

        /// <summary>
        /// Gets whether this order is closing an existing position.
        /// </summary>
        /// <remarks>
        /// When true, the order was created via <see cref="Stock.Close"/>, <see cref="Stock.CloseLong"/>,
        /// or <see cref="Stock.CloseShort"/>. This is informational only - the IBKR API receives
        /// the same BUY/SELL action regardless.
        /// </remarks>
        public bool IsClosingPosition => _isClosingPosition;

        /// <summary>
        /// Gets whether this order is opening a new position.
        /// </summary>
        /// <remarks>
        /// When true, the order was created via <see cref="Stock.Buy"/> or <see cref="Stock.Sell"/>.
        /// This is informational only.
        /// </remarks>
        public bool IsOpeningPosition => !_isClosingPosition;

        internal StrategyBuilder(Stock stock, OrderSide side, int quantity, Price priceType, Enums.OrderType orderType = Enums.OrderType.Market, bool isClosingPosition = false)
        {
            _stock = stock;
            _side = side;
            _quantity = quantity;
            _priceType = priceType;
            _orderType = orderType;
            _isClosingPosition = isClosingPosition;
        }

        /// <summary>
        /// Sets a fixed take profit price.
        /// </summary>
        /// <param name="price">The absolute take profit price.</param>
        public StrategyBuilder TakeProfit(double price)
        {
            _takeProfit = price;
            _adxTakeProfit = null; // Clear any ADX config when using fixed price
            return this;
        }

        /// <summary>
        /// Sets a dynamic take profit range that adjusts based on ADX trend strength.
        /// </summary>
        /// <param name="lowTarget">Conservative target price (used when ADX is weak, typically the midpoint).</param>
        /// <param name="highTarget">Aggressive target price (used when ADX is strong, typically range high).</param>
        /// <returns>The builder for method chaining.</returns>
        /// <remarks>
        /// <para><b>ADX-Based Take Profit Rules:</b></para>
        /// <list type="bullet">
        ///   <item>ADX &lt; 15 (No Trend): Take profit at <paramref name="lowTarget"/></item>
        ///   <item>ADX 15-25 (Developing): Interpolate between targets</item>
        ///   <item>ADX 25-35 (Strong): Take profit at <paramref name="highTarget"/></item>
        ///   <item>ADX &gt; 35 (Very Strong): Target <paramref name="highTarget"/> or beyond</item>
        ///   <item>ADX Rolling Over: Exit early as momentum fades</item>
        /// </list>
        /// 
        /// <para><b>Example:</b></para>
        /// <code>
        /// // Range 1.30-1.70, use midpoint (1.50) when weak, high (1.70) when strong
        /// .TakeProfit(1.50, 1.70)
        /// 
        /// // With custom ADX thresholds
        /// .TakeProfit(1.50, 1.70, weakThreshold: 20, strongThreshold: 40)
        /// </code>
        /// </remarks>
        public StrategyBuilder TakeProfit(double lowTarget, double highTarget)
        {
            _adxTakeProfit = AdxTakeProfitConfig.FromRange(lowTarget, highTarget);
            _takeProfit = lowTarget; // Use conservative as fallback if ADX unavailable
            return this;
        }

        /// <summary>
        /// Sets a dynamic take profit range with custom ADX thresholds.
        /// </summary>
        /// <param name="lowTarget">Conservative target price (used when ADX &lt; <paramref name="weakThreshold"/>).</param>
        /// <param name="highTarget">Aggressive target price (used when ADX &gt; <paramref name="strongThreshold"/>).</param>
        /// <param name="weakThreshold">ADX value below which trend is considered weak (default: 15).</param>
        /// <param name="developingThreshold">ADX value for developing trend (default: 25).</param>
        /// <param name="strongThreshold">ADX value above which trend is strong (default: 35).</param>
        /// <param name="exitOnRollover">Exit when ADX peaks and begins falling (default: true).</param>
        /// <returns>The builder for method chaining.</returns>
        public StrategyBuilder TakeProfit(
            double lowTarget,
            double highTarget,
            double weakThreshold = 15.0,
            double developingThreshold = 25.0,
            double strongThreshold = 35.0,
            bool exitOnRollover = true)
        {
            _adxTakeProfit = new AdxTakeProfitConfig
            {
                ConservativeTarget = lowTarget,
                AggressiveTarget = highTarget,
                WeakTrendThreshold = weakThreshold,
                DevelopingTrendThreshold = developingThreshold,
                StrongTrendThreshold = strongThreshold,
                ExitOnAdxRollover = exitOnRollover
            };
            _takeProfit = lowTarget; // Use conservative as fallback
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
        /// Sets the time to close position if still open.
        /// </summary>
        /// <param name="time">The time to close the position.</param>
        /// <param name="onlyIfProfitable">
        /// When true (default), only closes if position is profitable (price >= entry for longs).
        /// When false, closes regardless of profit/loss.
        /// </param>
        /// <returns>The builder for method chaining.</returns>
        /// <remarks>
        /// <para><b>Example:</b></para>
        /// <code>
        /// // Close at 9:20 AM ET only if profitable
        /// .ClosePosition(Time.PreMarket.Ending)
        /// 
        /// // Close at 9:20 AM ET regardless of profit/loss
        /// .ClosePosition(Time.PreMarket.Ending, onlyIfProfitable: false)
        /// </code>
        /// </remarks>
        public StrategyBuilder ClosePosition(TimeOnly time, bool onlyIfProfitable = true)
        {
            _closePositionTime = time;
            _closePositionOnlyIfProfitable = onlyIfProfitable;
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
        /// Sets whether to allow trading outside regular trading hours.
        /// </summary>
        public StrategyBuilder OutsideRTH(bool outsideRth = true, bool takeProfit = true)
        {
            _outsideRth = outsideRth;
            _takeProfitOutsideRth = takeProfit;
            return this;
        }

        /// <summary>
        /// Sets the order type.
        /// </summary>
        public StrategyBuilder OrderType(Enums.OrderType type)
        {
            _orderType = type;
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
        ///     .Buy(100, Price.Current)
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
        ///     .Buy(100, Price.Current)
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
