// ============================================================================
// IdiotProofTrade - Tracks orders created by IdiotProof through their lifecycle
// ============================================================================

namespace IdiotProof.Models {
    /// <summary>
    /// Represents an order created by IdiotProof that should be tracked.
    /// This allows filtering orders on the Orders page to show only IdiotProof trades.
    /// </summary>
    public class IdiotProofTrade
    {
        /// <summary>
        /// Unique identifier for this IdiotProof trade.
        /// </summary>
        public Guid TradeId { get; set; } = Guid.NewGuid();

        /// <summary>
        /// The IBKR order ID for the entry order.
        /// </summary>
        public int EntryOrderId { get; set; }

        /// <summary>
        /// The IBKR order ID for the stop loss order (if any).
        /// </summary>
        public int? StopLossOrderId { get; set; }

        /// <summary>
        /// The IBKR order ID for the take profit order (if any).
        /// </summary>
        public int? TakeProfitOrderId { get; set; }

        /// <summary>
        /// Additional child order IDs (bracket orders, etc.).
        /// </summary>
        public List<int> ChildOrderIds { get; set; } = [];

        /// <summary>
        /// The strategy ID that created this trade.
        /// </summary>
        public Guid StrategyId { get; set; }

        /// <summary>
        /// The strategy name for display purposes.
        /// </summary>
        public string StrategyName { get; set; } = string.Empty;

        /// <summary>
        /// The stock symbol.
        /// </summary>
        public string Symbol { get; set; } = string.Empty;

        /// <summary>
        /// When the trade was created.
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// When the trade was last updated.
        /// </summary>
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Current status of the trade.
        /// </summary>
        public TradeStatus Status { get; set; } = TradeStatus.Pending;

        /// <summary>
        /// Entry fill price (if filled).
        /// </summary>
        public double? EntryPrice { get; set; }

        /// <summary>
        /// Exit fill price (if closed).
        /// </summary>
        public double? ExitPrice { get; set; }

        /// <summary>
        /// Quantity of shares.
        /// </summary>
        public int Quantity { get; set; }

        /// <summary>
        /// Realized P&L for this trade (if closed).
        /// </summary>
        public double? RealizedPnL { get; set; }

        /// <summary>
        /// Notes about this trade.
        /// </summary>
        public string? Notes { get; set; }

        /// <summary>
        /// Checks if an order ID belongs to this trade.
        /// </summary>
        public bool ContainsOrderId(int orderId)
        {
            return EntryOrderId == orderId ||
                   StopLossOrderId == orderId ||
                   TakeProfitOrderId == orderId ||
                   ChildOrderIds.Contains(orderId);
        }

        /// <summary>
        /// Gets all order IDs associated with this trade.
        /// </summary>
        public IEnumerable<int> GetAllOrderIds()
        {
            yield return EntryOrderId;
            if (StopLossOrderId.HasValue)
                yield return StopLossOrderId.Value;
            if (TakeProfitOrderId.HasValue)
                yield return TakeProfitOrderId.Value;
            foreach (var id in ChildOrderIds)
                yield return id;
        }
    }

    /// <summary>
    /// Status of an IdiotProof trade.
    /// </summary>
    public enum TradeStatus
    {
        /// <summary>Order pending submission.</summary>
        Pending,

        /// <summary>Entry order submitted but not filled.</summary>
        EntrySubmitted,

        /// <summary>Entry order filled, position open.</summary>
        Open,

        /// <summary>Take profit hit.</summary>
        TakeProfitFilled,

        /// <summary>Stop loss hit.</summary>
        StopLossFilled,

        /// <summary>Manually closed.</summary>
        ManuallyClosed,

        /// <summary>Cancelled before fill.</summary>
        Cancelled,

        /// <summary>Error occurred.</summary>
        Error
    }

    /// <summary>
    /// Collection of IdiotProof trades for persistence.
    /// </summary>
    public class IdiotProofTradeCollection
    {
        /// <summary>
        /// List of all tracked trades.
        /// </summary>
        public List<IdiotProofTrade> Trades { get; set; } = [];

        /// <summary>
        /// Version for file format compatibility.
        /// </summary>
        public int Version { get; set; } = 1;

        /// <summary>
        /// Last modified timestamp.
        /// </summary>
        public DateTime LastModified { get; set; } = DateTime.UtcNow;
    }
}


