// ============================================================================
// Stock - Fluent Builder for Creating Strategies
// ============================================================================
//
// BEST PRACTICES:
// 1. Always add at least one condition before calling Buy(), Sell(), or Close().
// 2. Chain conditions in the order they should be evaluated (chronological).
// 3. Use Start() and End() to define the active time window.
// 4. Always set TakeProfit AND StopLoss for proper risk management.
// 5. Validate that Start time is before End time.
// 6. Use meaningful condition sequences (e.g., Breakout -> Pullback -> AboveVwap).
//
// FLUENT PATTERN:
//   Stock.Ticker("SYMBOL")     // Start builder
//       .Start(...)            // Optional: when to start monitoring
//       .Breakout(...)         // Add conditions
//       .Pullback(...)
//       .AboveVwap()
//       .Buy(...)              // Returns StrategyBuilder (opens position)
//       .Sell(...)             // Returns StrategyBuilder (opens short or exits)
//       .Close(...)            // Returns StrategyBuilder (closes existing position)
//       .TakeProfit(...)       // Exit configuration
//       .StopLoss(...)
//       .End(...)              // Terminal: returns TradingStrategy
//
// OPENING POSITIONS:
// var buyStrategy = Stock.Ticker("AAPL")
//     .Breakout(150)
//     .Buy(quantity: 100, Price.Current)
//     .TakeProfit(155)
//     .StopLoss(148)
//     .Build();
//
// CLOSING POSITIONS:
// var closeStrategy = Stock.Ticker("AAPL")
//     .PriceAbove(155)                    // Exit when price hits target
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
using IdiotProof.Enums;

namespace IdiotProof.Models
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
    ///   <item>Use <see cref="Start"/> and <see cref="StrategyBuilder.End"/> to bound the strategy window.</item>
    ///   <item>Always configure both take profit and stop loss for risk management.</item>
    /// </list>
    /// 
    /// <para><b>Builder Flow:</b></para>
    /// <code>
    /// Stock.Ticker("AAPL")    // Initialize
    ///     .Breakout(150)      // Conditions (Stock returns Stock)
    ///     .Pullback(148)
    ///     .Buy(100)           // Order (Stock returns StrategyBuilder)
    ///     .TakeProfit(155)    // Exit config (StrategyBuilder returns StrategyBuilder)
    ///     .End(...)           // Terminal (StrategyBuilder returns TradingStrategy)
    /// </code>
    /// </remarks>
    public sealed class Stock
    {
        private readonly string _symbol;
        private string _exchange = "SMART";
        private string _currency = "USD";
        private string _secType = "STK";
        private readonly List<IStrategyCondition> _conditions = new();
        private bool _enabled = true;
        private TimeOnly? _startTime;
        private TimeOnly? _endTime;

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
        /// <returns>A new <see cref="Stock"/> builder instance.</returns>
        /// <exception cref="ArgumentNullException">Thrown if symbol is null.</exception>
        /// <remarks>
        /// <b>Best Practice:</b> Use uppercase ticker symbols to match exchange conventions.
        /// </remarks>
        /// <example>
        /// <code>Stock.Ticker("AAPL")</code>
        /// </example>
        public static Stock Ticker(string symbol) => new(symbol);

        /// <summary>
        /// Sets the exchange for order routing.
        /// </summary>
        /// <param name="exchange">Exchange identifier (default: "SMART").</param>
        /// <returns>The builder for method chaining.</returns>
        /// <remarks>
        /// <b>Best Practice:</b> Use "SMART" for automatic routing. Specify exchange
        /// (e.g., "NASDAQ", "NYSE") only when you need specific routing.
        /// </remarks>
        public Stock Exchange(string exchange)
        {
            _exchange = exchange;
            return this;
        }

        /// <summary>
        /// Sets the currency for the order.
        /// </summary>
        /// <param name="currency">Currency code (default: "USD").</param>
        /// <returns>The builder for method chaining.</returns>
        public Stock Currency(string currency)
        {
            _currency = currency;
            return this;
        }

        /// <summary>
        /// Enables or disables this strategy.
        /// </summary>
        /// <param name="enabled">True to enable, false to disable.</param>
        /// <returns>The builder for method chaining.</returns>
        /// <remarks>
        /// <b>Use Case:</b> Temporarily disable a strategy without removing it from code.
        /// Disabled strategies are skipped during execution.
        /// </remarks>
        public Stock Enabled(bool enabled)
        {
            _enabled = enabled;
            return this;
        }

        /// <summary>
        /// Sets the time to start monitoring the strategy (CST).
        /// </summary>
        /// <param name="startTime">The time to begin monitoring.</param>
        /// <returns>The builder for method chaining.</returns>
        /// <remarks>
        /// <para><b>Best Practice:</b> Use <see cref="Time"/> helper for common times:</para>
        /// <code>.Start(Time.PreMarket.Start)</code>
        /// <para><b>Note:</b> Currently stored but not enforced by StrategyRunner.</para>
        /// </remarks>
        public Stock Start(TimeOnly startTime)
        {
            _startTime = startTime;
            return this;
        }

        // ====================================================================
        // CONDITION METHODS
        // ====================================================================

        /// <summary>
        /// Adds a breakout condition: Price >= level.
        /// </summary>
        public Stock Breakout(double level)
        {
            _conditions.Add(new BreakoutCondition(level));
            return this;
        }

        /// <summary>
        /// Adds a pullback condition: Price &lt;= level.
        /// </summary>
        public Stock Pullback(double level)
        {
            _conditions.Add(new PullbackCondition(level));
            return this;
        }

        /// <summary>
        /// Adds an above-VWAP condition: Price >= VWAP + buffer.
        /// </summary>
        public Stock AboveVwap(double buffer = 0)
        {
            _conditions.Add(new AboveVwapCondition(buffer));
            return this;
        }

        /// <summary>
        /// Adds a below-VWAP condition: Price &lt;= VWAP - buffer.
        /// </summary>
        public Stock BelowVwap(double buffer = 0)
        {
            _conditions.Add(new BelowVwapCondition(buffer));
            return this;
        }

        /// <summary>
        /// Adds a price-above condition: Price > level.
        /// </summary>
        public Stock PriceAbove(double level)
        {
            _conditions.Add(new PriceAboveCondition(level));
            return this;
        }

        /// <summary>
        /// Adds a price-below condition: Price &lt; level.
        /// </summary>
        public Stock PriceBelow(double level)
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
        // ORDER METHODS - Returns StrategyBuilder for chaining
        // ====================================================================

        /// <summary>
        /// Creates a BUY order and returns a builder for additional configuration.
        /// </summary>
        /// <param name="quantity">Number of shares to buy.</param>
        /// <param name="priceType">Price type for order execution (default: Current).</param>
        /// <param name="orderType">Order type (Market or Limit, default: Market).</param>
        public StrategyBuilder Buy(int quantity, Price priceType = Price.Current, OrderType orderType = OrderType.Market)
        {
            return new StrategyBuilder(this, OrderSide.Buy, quantity, priceType, orderType);
        }

        /// <summary>
        /// Creates a SELL order and returns a builder for additional configuration.
        /// </summary>
        /// <param name="quantity">Number of shares to sell.</param>
        /// <param name="priceType">Price type for order execution (default: Current).</param>
        /// <param name="orderType">Order type (Market or Limit, default: Market).</param>
        public StrategyBuilder Sell(int quantity, Price priceType = Price.Current, OrderType orderType = OrderType.Market)
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
        /// <param name="orderType">Order type (Market or Limit, default: Market).</param>
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
        ///     .PriceAbove(150)
        ///     .Close(quantity: 100, positionSide: OrderSide.Buy)  // Sells 100 shares
        ///     .TimeInForce(TIF.GTC)
        ///     .OutsideRTH(true)
        ///     .Build();
        /// </code>
        /// 
        /// <para><b>Example - Close short position:</b></para>
        /// <code>
        /// Stock.Ticker("TSLA")
        ///     .PriceBelow(200)
        ///     .Close(quantity: 50, positionSide: OrderSide.Sell)  // Buys 50 shares to cover
        ///     .TimeInForce(TIF.Day)
        ///     .Build();
        /// </code>
        /// </remarks>
        public StrategyBuilder Close(int quantity, OrderSide positionSide = OrderSide.Buy, Price priceType = Price.Current, OrderType orderType = OrderType.Market)
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
        public StrategyBuilder CloseLong(int quantity, Price priceType = Price.Current, OrderType orderType = OrderType.Market)
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
        public StrategyBuilder CloseShort(int quantity, Price priceType = Price.Current, OrderType orderType = OrderType.Market)
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
                Currency = _currency,
                SecType = _secType,
                Conditions = _conditions.AsReadOnly(),
                Order = order,
                Enabled = _enabled,
                StartTime = _startTime,
                EndTime = _endTime
            };
        }

        internal string Symbol => _symbol;
        internal string ExchangeValue => _exchange;
        internal string CurrencyValue => _currency;
        internal string SecTypeValue => _secType;
        internal IReadOnlyList<IStrategyCondition> Conditions => _conditions.AsReadOnly();
        internal bool EnabledValue => _enabled;
        internal TimeOnly? StartTimeValue => _startTime;
        internal TimeOnly? EndTimeValue { get => _endTime; set => _endTime = value; }
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
        private TimeOnly? _closePositionTime;
        private Enums.TimeInForce _timeInForce = Enums.TimeInForce.GoodTillCancel;
        private bool _outsideRth = true;
        private bool _takeProfitOutsideRth = true;
        private Enums.OrderType _orderType = Enums.OrderType.Market;
        private bool _allOrNone = false;

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
        /// Sets the take profit price.
        /// </summary>
        public StrategyBuilder TakeProfit(double price)
        {
            _takeProfit = price;
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
            return this;
        }

        /// <summary>
        /// Sets the time to close position if still open.
        /// </summary>
        public StrategyBuilder ClosePosition(TimeOnly time)
        {
            _closePositionTime = time;
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
        /// Sets the time to stop monitoring the strategy and builds it.
        /// </summary>
        public TradingStrategy End(TimeOnly endTime)
        {
            _stock.EndTimeValue = endTime;
            return Build();
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
                EnableTakeProfit = _takeProfit.HasValue,
                TakeProfitPrice = _takeProfit,
                TakeProfitOutsideRth = _takeProfitOutsideRth,
                EnableStopLoss = _stopLoss.HasValue,
                StopLossPrice = _stopLoss,
                EnableTrailingStopLoss = _enableTrailingStopLoss,
                TrailingStopLossPercent = _trailingStopLossPercent,
                ClosePositionTime = _closePositionTime
            };

            return _stock.BuildStrategy(order);
        }

        /// <summary>
        /// Implicit conversion to TradingStrategy for cleaner syntax.
        /// </summary>
        public static implicit operator TradingStrategy(StrategyBuilder builder) => builder.Build();
    }
}
