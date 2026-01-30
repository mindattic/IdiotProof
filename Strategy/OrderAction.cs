// ============================================================================
// Order Action - Defines buy/sell orders for strategies
// ============================================================================
//
// ╔═══════════════════════════════════════════════════════════════════════════╗
// ║  IBKR API MAPPING REFERENCE                                               ║
// ║                                                                           ║
// ║  The enums and IB API codes in this file are based on the Interactive     ║
// ║  Brokers TWS API documentation. When adding new values, ensure they       ║
// ║  match the official IB API string codes exactly.                          ║
// ║                                                                           ║
// ║  TimeInForce → order.Tif: "DAY", "GTC", "IOC", "FOK", "OPG", "DTC"       ║
// ║  OrderSide   → order.Action: "BUY", "SELL"                               ║
// ║  OrderType   → order.OrderType: "MKT", "LMT", "STP", "STP LMT"           ║
// ║                                                                           ║
// ║  Reference: https://interactivebrokers.github.io/tws-api/               ║
// ╚═══════════════════════════════════════════════════════════════════════════╝
//
// BEST PRACTICES:
// 1. Always specify quantity explicitly - avoid relying on defaults for real trading.
// 2. Use TakeProfit AND StopLoss together for proper risk management.
// 3. TrailingStopLoss is mutually exclusive with fixed StopLoss - choose one.
// 4. For pre-market trading, ensure OutsideRth = true and TimeInForce = GTC.
// 5. Validate that TakeProfit > EntryPrice for Buy orders.
// 6. Validate that StopLoss < EntryPrice for Buy orders.
//
// PROPERTY RELATIONSHIPS:
// - EnableTakeProfit: true if TakeProfitPrice is set
// - EnableStopLoss: true if StopLossPrice is set
// - EnableTrailingStopLoss: mutually exclusive with EnableStopLoss
//
// ============================================================================

using System;
using IdiotProof.Enums;

namespace IdiotProof.Models
{
    /// <remarks>
    /// <para><b>Best Practices:</b></para>
    /// <list type="bullet">
    ///   <item>Always set both take profit and stop loss for risk management.</item>
    ///   <item>Use trailing stop loss for trending strategies.</item>
    ///   <item>Set appropriate time limits to avoid holding positions overnight.</item>
    ///   <item>Validate TakeProfit &gt; entry and StopLoss &lt; entry for Buy orders.</item>
    /// </list>
    /// 
    /// <para><b>Order Flow:</b></para>
    /// <list type="number">
    ///   <item>Entry order submitted when all conditions met.</item>
    ///   <item>On fill: TakeProfit and/or StopLoss orders submitted.</item>
    ///   <item>TrailingStopLoss monitors price and adjusts stop level.</item>
    ///   <item>First exit order to fill cancels the others (OCO behavior).</item>
    /// </list>
    /// </remarks>
    public sealed class OrderAction
    {
        /// <summary>Buy or Sell direction for the entry order.</summary>
        public OrderSide Side { get; init; } = OrderSide.Buy;

        /// <summary>
        /// Number of shares to trade.
        /// </summary>
        /// <remarks>
        /// <b>Best Practice:</b> Always specify explicitly. Calculate position size
        /// based on risk tolerance (e.g., 1-2% of account per trade).
        /// </remarks>
        public int Quantity { get; init; } = 100;

        /// <summary>Market or Limit order type for entry.</summary>
        /// <remarks>
        /// <b>Best Practice:</b> Use Limit orders in pre-market/after-hours
        /// due to wider spreads and lower liquidity.
        /// </remarks>
        public OrderType Type { get; init; } = OrderType.Limit;

        /// <summary>
        /// Explicit limit price for entry. If null, uses VWAP + LimitOffset.
        /// </summary>
        public double? LimitPrice { get; init; }

        /// <summary>
        /// Offset from VWAP for limit orders when LimitPrice is null.
        /// </summary>
        /// <remarks>
        /// <b>Default:</b> $0.02 above VWAP for buy orders.
        /// </remarks>
        public double LimitOffset { get; init; } = 0.02;

        /// <summary>Time in force for the entry order.</summary>
        public TimeInForce TimeInForce { get; init; } = TimeInForce.GoodTillCancel;

        /// <summary>
        /// Allow entry order execution outside regular trading hours.
        /// </summary>
        /// <remarks>
        /// <b>Best Practice:</b> Set to true for pre-market strategies.
        /// </remarks>
        public bool OutsideRth { get; init; } = true;

        /// <summary>
        /// Require entire order to be filled at once or not at all.
        /// </summary>
        /// <remarks>
        /// <para><b>Behavior:</b></para>
        /// <list type="bullet">
        ///   <item>When true: Order must be filled completely in a single transaction.</item>
        ///   <item>When false (default): Partial fills are allowed.</item>
        /// </list>
        /// <para><b>Use Cases:</b></para>
        /// <list type="bullet">
        ///   <item>Use true when you need the exact quantity for a specific strategy.</item>
        ///   <item>Use false (default) for normal trading where partial fills are acceptable.</item>
        /// </list>
        /// <para><b>Warning:</b> AllOrNone orders may take longer to fill or may not fill
        /// at all if the full quantity is not available at your price.</para>
        /// <para><b>IB API:</b> Maps to <c>order.AllOrNone</c></para>
        /// </remarks>
        public bool AllOrNone { get; init; } = false;

        /// <summary>Enable automatic take profit order after entry fills.</summary>
        public bool EnableTakeProfit { get; init; } = true;

        /// <summary>
        /// Absolute take profit price. If null, uses entry + TakeProfitOffset.
        /// </summary>
        /// <remarks>
        /// <b>Validation:</b> For Buy orders, must be greater than entry price.
        /// </remarks>
        public double? TakeProfitPrice { get; init; }

        /// <summary>
        /// Take profit offset from entry price when TakeProfitPrice is null.
        /// </summary>
        public double TakeProfitOffset { get; init; } = 0.30;

        /// <summary>Allow take profit order execution outside regular trading hours.</summary>
        public bool TakeProfitOutsideRth { get; init; } = true;

        /// <summary>Enable automatic fixed stop loss order after entry fills.</summary>
        /// <remarks>
        /// <b>Note:</b> Mutually exclusive with <see cref="EnableTrailingStopLoss"/>.
        /// Fixed stop stays at set price; trailing stop moves with price.
        /// </remarks>
        public bool EnableStopLoss { get; init; } = false;

        /// <summary>
        /// Absolute stop loss price. If null, uses entry - StopLossOffset.
        /// </summary>
        /// <remarks>
        /// <b>Validation:</b> For Buy orders, must be less than entry price.
        /// </remarks>
        public double? StopLossPrice { get; init; }

        /// <summary>
        /// Stop loss offset from entry price when StopLossPrice is null.
        /// </summary>
        public double StopLossOffset { get; init; } = 0.20;

        /// <summary>
        /// Enable trailing stop loss that follows price upward.
        /// </summary>
        /// <remarks>
        /// <para><b>Behavior:</b></para>
        /// <list type="number">
        ///   <item>Initializes at entry × (1 - TrailingStopLossPercent).</item>
        ///   <item>Tracks highest price since entry (high-water mark).</item>
        ///   <item>Recalculates stop as highWaterMark × (1 - percent).</item>
        ///   <item>Stop only moves UP, never down.</item>
        ///   <item>Triggers market sell when price ≤ trailing stop.</item>
        /// </list>
        /// <para><b>Note:</b> Takes precedence over fixed StopLoss if both enabled.</para>
        /// </remarks>
        public bool EnableTrailingStopLoss { get; init; } = false;

        /// <summary>
        /// Trailing stop loss percentage as decimal (e.g., 0.10 for 10%).
        /// </summary>
        /// <remarks>
        /// <b>Best Practice:</b> Use 5-10% for moderate volatility stocks,
        /// 10-20% for high volatility. See <see cref="Percent"/> for constants.
        /// </remarks>
        public double TrailingStopLossPercent { get; init; } = 0.10;

        /// <summary>
        /// Time to cancel unfilled orders and optionally exit position (CST).
        /// </summary>
        /// <remarks>
        /// <b>Best Practice:</b> Set this for pre-market strategies to exit
        /// before regular session begins if take profit hasn't filled.
        /// </remarks>
        public TimeOnly? EndTime { get; init; }

        /// <summary>Price type for order execution (Current, VWAP, Bid, Ask).</summary>
        /// <remarks>
        /// <b>Note:</b> Currently stored but not fully implemented in StrategyRunner.
        /// </remarks>
        public Price PriceType { get; init; } = Price.Current;

        /// <summary>
        /// Time to start monitoring the strategy (null = immediately).
        /// </summary>
        /// <remarks>
        /// <b>Note:</b> Currently stored but not fully implemented in StrategyRunner.
        /// </remarks>
        public TimeOnly? StartTime { get; init; }

        /// <summary>
        /// Time to force-close position if still open (null = no auto-close).
        /// </summary>
        /// <remarks>
        /// <b>Note:</b> Currently stored but not fully implemented in StrategyRunner.
        /// </remarks>
        public TimeOnly? ClosePositionTime { get; init; }

        /// <summary>
        /// Gets the Interactive Brokers time-in-force string.
        /// </summary>
        /// <returns>
        /// IB API TIF code: "DAY", "GTC", "IOC", "FOK", "OPG", or "DTC".
        /// </returns>
        /// <remarks>
        /// <para><b>IB API TIF Codes:</b></para>
        /// <list type="bullet">
        ///   <item>"DAY" - Day order, expires at market close.</item>
        ///   <item>"GTC" - Good Till Cancelled, persists until filled or cancelled.</item>
        ///   <item>"IOC" - Immediate or Cancel, fill immediately or cancel unfilled.</item>
        ///   <item>"FOK" - Fill or Kill, fill entire order immediately or cancel all.</item>
        ///   <item>"OPG" - At the Opening, executes at market open auction.</item>
        ///   <item>"DTC" - Day Till Cancelled, overnight + day coverage.</item>
        /// </list>
        /// <para><b>Note:</b> For <see cref="TimeInForce.Overnight"/>, use "GTC" with
        /// <see cref="OutsideRth"/> = true to achieve overnight-only behavior through
        /// time-based cancellation logic.</para>
        /// </remarks>
        public string GetIbTif() => TimeInForce switch
        {
            TimeInForce.Day => "DAY",
            TimeInForce.GoodTillCancel => "GTC",
            TimeInForce.ImmediateOrCancel => "IOC",
            TimeInForce.FillOrKill => "FOK",
            TimeInForce.Overnight => "GTC",           // Use GTC with OutsideRth + time limits
            TimeInForce.OvernightPlusDay => "DTC",    // Day Till Cancelled variant
            TimeInForce.AtTheOpening => "OPG",        // Opening auction only
            _ => "GTC"
        };

        /// <summary>
        /// Gets the Interactive Brokers order type string.
        /// </summary>
        /// <returns>IB API order type: "MKT" or "LMT".</returns>
        public string GetIbOrderType() => Type switch
        {
            OrderType.Market => "MKT",
            OrderType.Limit => "LMT",
            _ => "LMT"
        };

        /// <summary>
        /// Gets the Interactive Brokers action string.
        /// </summary>
        /// <returns>IB API action: "BUY" or "SELL".</returns>
        public string GetIbAction() => Side switch
        {
            OrderSide.Buy => "BUY",
            OrderSide.Sell => "SELL",
            _ => "BUY"
        };

        /// <summary>
        /// Returns a human-readable summary of the order action.
        /// </summary>
        public override string ToString()
        {
            var tpStr = EnableTakeProfit 
                ? (TakeProfitPrice.HasValue ? $"TP={TakeProfitPrice:F2}" : $"TP=+{TakeProfitOffset:F2}") 
                : "TP=Off";
            var slStr = EnableStopLoss ? $", SL={StopLossPrice:F2}" : "";
            var tslStr = EnableTrailingStopLoss ? $", TSL={TrailingStopLossPercent * 100:F0}%" : "";
            return $"{Side} {Quantity} shares, {Type}, TIF={TimeInForce}, {tpStr}{slStr}{tslStr}";
        }
    }
}
