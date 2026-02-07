// ============================================================================
// OrderInfo - Shared model for order information between backend and frontend
// ============================================================================

namespace IdiotProof.Core.Models
{
    /// <summary>
    /// Order status from IBKR.
    /// </summary>
    public enum OrderStatus
    {
        PendingSubmit,
        PreSubmitted,
        Submitted,
        Filled,
        PartiallyFilled,
        Cancelled,
        ApiCancelled,
        Error,
        Unknown
    }

    /// <summary>
    /// Represents an order that can be displayed in the frontend.
    /// </summary>
    public class OrderInfo
    {
        /// <summary>
        /// IBKR Order ID.
        /// </summary>
        public int OrderId { get; set; }

        /// <summary>
        /// Stock symbol.
        /// </summary>
        public string Symbol { get; set; } = string.Empty;

        /// <summary>
        /// BUY or SELL.
        /// </summary>
        public string Action { get; set; } = string.Empty;

        /// <summary>
        /// Total quantity ordered.
        /// </summary>
        public decimal Quantity { get; set; }

        /// <summary>
        /// Quantity filled so far.
        /// </summary>
        public decimal Filled { get; set; }

        /// <summary>
        /// Remaining quantity.
        /// </summary>
        public decimal Remaining { get; set; }

        /// <summary>
        /// Order type (MKT, LMT, STP, etc.).
        /// </summary>
        public string OrderType { get; set; } = string.Empty;

        /// <summary>
        /// Limit price (if applicable).
        /// </summary>
        public double? LimitPrice { get; set; }

        /// <summary>
        /// Stop price (if applicable).
        /// </summary>
        public double? StopPrice { get; set; }

        /// <summary>
        /// Average fill price.
        /// </summary>
        public double? AvgFillPrice { get; set; }

        /// <summary>
        /// Current order status.
        /// </summary>
        public OrderStatus Status { get; set; } = OrderStatus.Unknown;

        /// <summary>
        /// Status as string from IBKR.
        /// </summary>
        public string StatusText { get; set; } = string.Empty;

        /// <summary>
        /// Time the order was placed.
        /// </summary>
        public DateTime? PlacedAt { get; set; }

        /// <summary>
        /// Time the order was last updated.
        /// </summary>
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Associated strategy name (if any).
        /// </summary>
        public string? StrategyName { get; set; }

        /// <summary>
        /// Whether this is a parent order or a child (bracket) order.
        /// </summary>
        public bool IsParentOrder { get; set; } = true;

        /// <summary>
        /// Parent order ID for bracket orders.
        /// </summary>
        public int? ParentOrderId { get; set; }

        /// <summary>
        /// IdiotProof trade ID if this order was created by IdiotProof.
        /// Used to filter orders to show only IdiotProof trades.
        /// </summary>
        public Guid? IdiotProofTradeId { get; set; }

        /// <summary>
        /// Whether this order was created by IdiotProof.
        /// </summary>
        public bool IsIdiotProofOrder => IdiotProofTradeId.HasValue;
    }

    /// <summary>
    /// Represents a position held in the account.
    /// </summary>
    public class PositionInfo
    {
        /// <summary>
        /// Stock symbol.
        /// </summary>
        public string Symbol { get; set; } = string.Empty;

        /// <summary>
        /// Number of shares held (positive = long, negative = short).
        /// </summary>
        public decimal Quantity { get; set; }

        /// <summary>
        /// Average cost per share.
        /// </summary>
        public double AvgCost { get; set; }

        /// <summary>
        /// Current market price.
        /// </summary>
        public double? MarketPrice { get; set; }

        /// <summary>
        /// Current market value.
        /// </summary>
        public double? MarketValue { get; set; }

        /// <summary>
        /// Unrealized P&L.
        /// </summary>
        public double? UnrealizedPnL { get; set; }

        /// <summary>
        /// Realized P&L.
        /// </summary>
        public double? RealizedPnL { get; set; }
    }
}


