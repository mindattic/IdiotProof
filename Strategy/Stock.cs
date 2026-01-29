// ============================================================================
// Stock - Fluent Builder for Creating Strategies
// ============================================================================
//
// Usage:
// var strategy = Stock.Create("NAMM")
//     .Breakout(7.10)
//     .Pullback(6.80)
//     .AboveVwap()
//     .Buy(1000, takeProfit: 9.00);
//
// ============================================================================

using System;
using System.Collections.Generic;

namespace IdiotProof.Models
{
    /// <summary>
    /// Fluent builder for creating trading strategies.
    /// </summary>
    public sealed class Stock
    {
        private readonly string _symbol;
        private string _exchange = "SMART";
        private string _currency = "USD";
        private string _secType = "STK";
        private readonly List<IStrategyCondition> _conditions = new();
        private bool _enabled = true;

        private Stock(string symbol)
        {
            _symbol = symbol ?? throw new ArgumentNullException(nameof(symbol));
        }

        /// <summary>
        /// Creates a new strategy builder for the specified symbol.
        /// </summary>
        public static Stock Create(string symbol) => new(symbol);

        /// <summary>
        /// Sets the exchange (default: SMART).
        /// </summary>
        public Stock Exchange(string exchange)
        {
            _exchange = exchange;
            return this;
        }

        /// <summary>
        /// Sets the currency (default: USD).
        /// </summary>
        public Stock Currency(string currency)
        {
            _currency = currency;
            return this;
        }

        /// <summary>
        /// Enables or disables this strategy.
        /// </summary>
        public Stock Enabled(bool enabled)
        {
            _enabled = enabled;
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
        // ORDER METHODS (Terminal - builds the strategy)
        // ====================================================================

        /// <summary>
        /// Creates a BUY order and builds the strategy.
        /// </summary>
        /// <param name="quantity">Number of shares to buy.</param>
        /// <param name="takeProfit">Take profit price (absolute). If null, uses takeProfitOffset.</param>
        /// <param name="takeProfitOffset">Take profit offset from entry (default: 0.30).</param>
        /// <param name="limitPrice">Limit price. If null, uses VWAP + limitOffset.</param>
        /// <param name="limitOffset">Limit offset from VWAP (default: 0.02).</param>
        /// <param name="timeInForce">Time in force (default: GTC).</param>
        /// <param name="outsideRth">Allow outside regular trading hours (default: true).</param>
        /// <param name="takeProfitOutsideRth">Allow TP outside RTH (default: true).</param>
        /// <param name="orderType">Order type (default: Limit).</param>
        /// <param name="cancelAt">Time to cancel unfilled take profit (e.g., new TimeOnly(8, 20) for 8:20 AM).</param>
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
            TimeOnly? cancelAt = null)
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
                CancelTakeProfitAt = cancelAt
            });
        }

        /// <summary>
        /// Creates a SELL order and builds the strategy.
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

        private TradingStrategy BuildStrategy(OrderAction order)
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
                Enabled = _enabled
            };
        }
    }
}
