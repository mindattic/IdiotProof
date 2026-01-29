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

namespace IdiotProof.Models
{
    /// <summary>
    /// Time in force options for orders, controlling how long an order remains active.
    /// </summary>
    /// <remarks>
    /// <para><b>⚠️ IBKR API MAPPING - Values must match IB API string codes ⚠️</b></para>
    /// <para>Reference: https://interactivebrokers.github.io/tws-api/classIBApi_1_1Order.html</para>
    /// 
    /// <para><b>IB API Tif Codes:</b></para>
    /// <list type="table">
    ///   <listheader><term>Enum</term><description>IB Code</description></listheader>
    ///   <item><term><see cref="Day"/></term><description>"DAY"</description></item>
    ///   <item><term><see cref="GoodTillCancel"/></term><description>"GTC"</description></item>
    ///   <item><term><see cref="ImmediateOrCancel"/></term><description>"IOC"</description></item>
    ///   <item><term><see cref="FillOrKill"/></term><description>"FOK"</description></item>
    ///   <item><term><see cref="Overnight"/></term><description>"GTC" (with time limits)</description></item>
    ///   <item><term><see cref="OvernightPlusDay"/></term><description>"DTC"</description></item>
    ///   <item><term><see cref="AtTheOpening"/></term><description>"OPG"</description></item>
    /// </list>
    /// 
    /// <para><b>Session Times (Eastern):</b></para>
    /// <list type="bullet">
    ///   <item>Pre-market: 4:00 AM - 9:30 AM</item>
    ///   <item>Regular: 9:30 AM - 4:00 PM</item>
    ///   <item>After-hours: 4:00 PM - 8:00 PM</item>
    /// </list>
    /// </remarks>
    public enum TimeInForce
    {
        /// <summary>
        /// Day order - expires at end of trading day (4:00 PM EST).
        /// </summary>
        /// <remarks>
        /// <para><b>Behavior:</b></para>
        /// <list type="bullet">
        ///   <item>Order is only valid for today's trading session.</item>
        ///   <item>If not filled by market close, automatically cancelled.</item>
        ///   <item>Does not carry over to the next trading day.</item>
        /// </list>
        /// <para><b>Best for:</b> Normal trades you only care about today.</para>
        /// <para><b>IB API Code:</b> "DAY"</para>
        /// </remarks>
        Day,

        /// <summary>
        /// Good Till Cancelled - remains active until filled or cancelled.
        /// </summary>
        /// <remarks>
        /// <para><b>Behavior:</b></para>
        /// <list type="bullet">
        ///   <item>Order stays open until it fills or you manually cancel it.</item>
        ///   <item>Can last weeks or months depending on broker settings.</item>
        ///   <item>Persists across multiple trading sessions.</item>
        /// </list>
        /// <para><b>Best for:</b> Long-term limit orders you want sitting in the market.</para>
        /// <para><b>IB API Code:</b> "GTC"</para>
        /// </remarks>
        GoodTillCancel,

        /// <summary>
        /// Immediate or Cancel - fill what's available immediately, cancel rest.
        /// </summary>
        /// <remarks>
        /// <para><b>Behavior:</b></para>
        /// <list type="bullet">
        ///   <item>Attempts to fill as much as possible immediately.</item>
        ///   <item>Any unfilled portion is cancelled.</item>
        ///   <item>May result in partial fills.</item>
        /// </list>
        /// <para><b>Best for:</b> When you want immediate execution; partial fills acceptable.</para>
        /// <para><b>IB API Code:</b> "IOC"</para>
        /// </remarks>
        ImmediateOrCancel,

        /// <summary>
        /// Fill or Kill - fill entire order immediately or cancel completely.
        /// </summary>
        /// <remarks>
        /// <para><b>Behavior:</b></para>
        /// <list type="bullet">
        ///   <item>Order must be filled completely and immediately.</item>
        ///   <item>If full quantity not available, entire order is cancelled.</item>
        ///   <item>No partial fills allowed.</item>
        /// </list>
        /// <para><b>Best for:</b> When all-or-nothing execution is required.</para>
        /// <para><b>IB API Code:</b> "FOK"</para>
        /// </remarks>
        FillOrKill,

        /// <summary>
        /// Overnight - active only during after-hours/overnight trading sessions.
        /// </summary>
        /// <remarks>
        /// <para><b>Behavior:</b></para>
        /// <list type="bullet">
        ///   <item>Order is active in after-hours and pre-market sessions.</item>
        ///   <item>Used when the regular market is closed but broker supports extended hours.</item>
        ///   <item>Typically covers 4:00 PM - 9:30 AM EST (after-hours + pre-market).</item>
        ///   <item>Cancelled if not filled before regular session opens.</item>
        /// </list>
        /// <para><b>Best for:</b> Trading news events outside regular hours; earnings plays.</para>
        /// <para><b>IB API Code:</b> "OPG" (Note: IB uses OPG for extended hours context)</para>
        /// <para><b>Note:</b> Requires <see cref="OrderAction.OutsideRth"/> = true.</para>
        /// </remarks>
        Overnight,

        /// <summary>
        /// Overnight + Day - spans overnight session and next regular day session.
        /// </summary>
        /// <remarks>
        /// <para><b>Behavior:</b></para>
        /// <list type="bullet">
        ///   <item>Order stays active through overnight session AND the next regular day.</item>
        ///   <item>Provides continuous coverage from extended hours into regular trading.</item>
        ///   <item>If still not filled by end of next regular session, order cancels.</item>
        /// </list>
        /// <para><b>Best for:</b> One continuous order spanning extended hours into tomorrow.</para>
        /// <para><b>IB API Code:</b> "DTC" (Day Till Cancelled variant)</para>
        /// <para><b>Note:</b> Requires <see cref="OrderAction.OutsideRth"/> = true for overnight portion.</para>
        /// </remarks>
        OvernightPlusDay,

        /// <summary>
        /// At the Opening - order executes only at market open auction.
        /// </summary>
        /// <remarks>
        /// <para><b>Behavior:</b></para>
        /// <list type="bullet">
        ///   <item>Order is only active at market open (9:30 AM EST).</item>
        ///   <item>Participates in the opening auction/cross.</item>
        ///   <item>If not filled in the opening auction, order is cancelled.</item>
        ///   <item>Cannot be cancelled or modified during pre-open period.</item>
        /// </list>
        /// <para><b>Best for:</b> Trying to catch the opening price move; gap plays.</para>
        /// <para><b>IB API Code:</b> "OPG"</para>
        /// <para><b>Warning:</b> Price may differ significantly from pre-market prices.</para>
        /// </remarks>
        AtTheOpening
    }

    /// <summary>
    /// Order side indicating whether to buy or sell.
    /// </summary>
    /// <remarks>
    /// <para><b>⚠️ IBKR API MAPPING ⚠️</b></para>
    /// <para>Maps to <c>order.Action</c> in the IB API:</para>
    /// <list type="bullet">
    ///   <item><see cref="Buy"/> → "BUY"</item>
    ///   <item><see cref="Sell"/> → "SELL"</item>
    /// </list>
    /// <para>Reference: https://interactivebrokers.github.io/tws-api/classIBApi_1_1Order.html</para>
    /// </remarks>
    public enum OrderSide
    {
        /// <summary>Buy order - go long on the security. IB API: "BUY"</summary>
        Buy,

        /// <summary>Sell order - close long position or go short. IB API: "SELL"</summary>
        Sell
    }

    /// <summary>
    /// Order type determining execution method.
    /// </summary>
    /// <remarks>
    /// <para><b>⚠️ IBKR API MAPPING ⚠️</b></para>
    /// <para>Maps to <c>order.OrderType</c> in the IB API:</para>
    /// <list type="bullet">
    ///   <item><see cref="Market"/> → "MKT"</item>
    ///   <item><see cref="Limit"/> → "LMT"</item>
    /// </list>
    /// <para>Additional IB order types (not yet implemented): "STP", "STP LMT", "TRAIL", etc.</para>
    /// <para>Reference: https://interactivebrokers.github.io/tws-api/classIBApi_1_1Order.html</para>
    /// 
    /// <para><b>Best Practices:</b></para>
    /// <list type="bullet">
    ///   <item><see cref="Market"/>: Fast execution, potential slippage.</item>
    ///   <item><see cref="Limit"/>: Price control, may not fill.</item>
    ///   <item>Use Limit orders in pre-market due to lower liquidity.</item>
    /// </list>
    /// </remarks>
    public enum OrderType
    {
        /// <summary>Market order - executes immediately at best available price. IB API: "MKT"</summary>
        Market,

        /// <summary>Limit order - executes only at specified price or better. IB API: "LMT"</summary>
        Limit
    }

    /// <summary>
    /// Represents an order action to be executed when strategy conditions are met.
    /// Contains all configuration for entry, take profit, stop loss, and timing.
    /// </summary>
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
