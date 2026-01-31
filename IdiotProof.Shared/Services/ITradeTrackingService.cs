// ============================================================================
// ITradeTrackingService - Interface for tracking IdiotProof trades
// ============================================================================

using IdiotProof.Shared.Models;

namespace IdiotProof.Shared.Services
{
    /// <summary>
    /// Service for tracking IdiotProof trades through their lifecycle.
    /// Implemented by both frontend and backend to track orders.
    /// </summary>
    public interface ITradeTrackingService
    {
        /// <summary>
        /// Registers a new trade when an entry order is placed.
        /// </summary>
        /// <param name="entryOrderId">The IBKR order ID for the entry order.</param>
        /// <param name="strategyId">The strategy that created this trade.</param>
        /// <param name="strategyName">The strategy name for display.</param>
        /// <param name="symbol">The stock symbol.</param>
        /// <param name="quantity">Number of shares.</param>
        /// <returns>The created trade record.</returns>
        Task<IdiotProofTrade> RegisterTradeAsync(
            int entryOrderId,
            Guid strategyId,
            string strategyName,
            string symbol,
            int quantity);

        /// <summary>
        /// Adds a child order to an existing trade (stop loss, take profit, etc.).
        /// </summary>
        /// <param name="tradeId">The IdiotProof trade ID.</param>
        /// <param name="orderId">The IBKR order ID to add.</param>
        /// <param name="orderType">Type of child order: "StopLoss", "TakeProfit", or "Child".</param>
        Task AddChildOrderAsync(Guid tradeId, int orderId, string orderType);

        /// <summary>
        /// Updates the status of a trade based on order fills.
        /// </summary>
        /// <param name="orderId">The IBKR order ID that was updated.</param>
        /// <param name="status">The new order status.</param>
        /// <param name="fillPrice">The fill price (if applicable).</param>
        Task UpdateTradeStatusAsync(int orderId, OrderStatus status, double? fillPrice = null);

        /// <summary>
        /// Gets all tracked trades.
        /// </summary>
        Task<List<IdiotProofTrade>> GetAllTradesAsync();

        /// <summary>
        /// Gets active trades (not yet closed).
        /// </summary>
        Task<List<IdiotProofTrade>> GetActiveTradesAsync();

        /// <summary>
        /// Gets a trade by its IdiotProof trade ID.
        /// </summary>
        Task<IdiotProofTrade?> GetTradeByIdAsync(Guid tradeId);

        /// <summary>
        /// Gets a trade by any of its order IDs.
        /// </summary>
        Task<IdiotProofTrade?> GetTradeByOrderIdAsync(int orderId);

        /// <summary>
        /// Checks if an order ID belongs to an IdiotProof trade.
        /// </summary>
        bool IsIdiotProofOrder(int orderId);

        /// <summary>
        /// Gets all order IDs that belong to IdiotProof trades.
        /// </summary>
        HashSet<int> GetAllIdiotProofOrderIds();

        /// <summary>
        /// Removes completed/cancelled trades older than the specified days.
        /// </summary>
        Task CleanupOldTradesAsync(int daysToKeep = 30);
    }
}
