// ============================================================================
// Order Action - Defines buy/sell orders for strategies
// ============================================================================

namespace IdiotProof.Models
{
    /// <summary>
    /// Time in force options for orders.
    /// </summary>
    public enum TimeInForce
    {
        /// <summary>Day order - expires at end of trading day.</summary>
        Day,
        
        /// <summary>Good Till Cancelled - remains active until filled or cancelled.</summary>
        GoodTillCancel,
        
        /// <summary>Immediate or Cancel - fill immediately or cancel.</summary>
        ImmediateOrCancel,
        
        /// <summary>Fill or Kill - fill entire order immediately or cancel.</summary>
        FillOrKill
    }

    /// <summary>
    /// Order side (buy or sell).
    /// </summary>
    public enum OrderSide
    {
        Buy,
        Sell
    }

    /// <summary>
    /// Order type.
    /// </summary>
    public enum OrderType
    {
        Market,
        Limit
    }

    /// <summary>
    /// Represents an order action to be executed when strategy conditions are met.
    /// </summary>
    public sealed class OrderAction
    {
        /// <summary>Buy or Sell.</summary>
        public OrderSide Side { get; init; } = OrderSide.Buy;

        /// <summary>Number of shares.</summary>
        public int Quantity { get; init; } = 100;

        /// <summary>Market or Limit order.</summary>
        public OrderType Type { get; init; } = OrderType.Limit;

        /// <summary>Limit price (for limit orders). If null, uses VWAP + LimitOffset.</summary>
        public double? LimitPrice { get; init; }

        /// <summary>Offset from VWAP for limit orders (when LimitPrice is null).</summary>
        public double LimitOffset { get; init; } = 0.02;

        /// <summary>Time in force for the order.</summary>
        public TimeInForce TimeInForce { get; init; } = TimeInForce.GoodTillCancel;

        /// <summary>Allow order outside regular trading hours.</summary>
        public bool OutsideRth { get; init; } = true;

        /// <summary>Enable automatic take profit order.</summary>
        public bool EnableTakeProfit { get; init; } = true;

        /// <summary>Take profit price. If null, uses entry + TakeProfitOffset.</summary>
        public double? TakeProfitPrice { get; init; }

        /// <summary>Take profit offset from entry price (when TakeProfitPrice is null).</summary>
        public double TakeProfitOffset { get; init; } = 0.30;

        /// <summary>Allow take profit order outside regular trading hours.</summary>
        public bool TakeProfitOutsideRth { get; init; } = true;

        /// <summary>Enable automatic stop loss order.</summary>
        public bool EnableStopLoss { get; init; } = false;

        /// <summary>Stop loss price. If null, uses entry - StopLossOffset.</summary>
        public double? StopLossPrice { get; init; }

        /// <summary>Stop loss offset from entry price (when StopLossPrice is null).</summary>
        public double StopLossOffset { get; init; } = 0.20;

        /// <summary>Time to cancel unfilled take profit order (null = no auto-cancel).</summary>
        public TimeOnly? CancelTakeProfitAt { get; init; }

        /// <summary>
        /// Gets the IB time-in-force string.
        /// </summary>
        public string GetIbTif() => TimeInForce switch
        {
            TimeInForce.Day => "DAY",
            TimeInForce.GoodTillCancel => "GTC",
            TimeInForce.ImmediateOrCancel => "IOC",
            TimeInForce.FillOrKill => "FOK",
            _ => "GTC"
        };

        /// <summary>
        /// Gets the IB order type string.
        /// </summary>
        public string GetIbOrderType() => Type switch
        {
            OrderType.Market => "MKT",
            OrderType.Limit => "LMT",
            _ => "LMT"
        };

        /// <summary>
        /// Gets the IB action string.
        /// </summary>
        public string GetIbAction() => Side switch
        {
            OrderSide.Buy => "BUY",
            OrderSide.Sell => "SELL",
            _ => "BUY"
        };

        public override string ToString()
        {
            var tpStr = EnableTakeProfit 
                ? (TakeProfitPrice.HasValue ? $"TP={TakeProfitPrice:F2}" : $"TP=+{TakeProfitOffset:F2}") 
                : "TP=Off";
            return $"{Side} {Quantity} shares, {Type}, TIF={TimeInForce}, {tpStr}";
        }
    }
}
