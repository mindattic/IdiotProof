// ============================================================================
// TradeTrackingService - Tracks IdiotProof orders through their lifecycle
// ============================================================================

using IdiotProof.Logging;
using IdiotProof.Models;
using System.Collections.Concurrent;
using System.Text.Json;

namespace IdiotProof.Services {
    /// <summary>
    /// Service for tracking IdiotProof trades.
    /// Persists trade data to allow filtering orders to only show IdiotProof trades.
    /// </summary>
    public class TradeTrackingService : ITradeTrackingService, IDisposable
    {
        private readonly string tradesFilePath;
        private readonly ConcurrentDictionary<Guid, IdiotProofTrade> trades = new();
        private readonly ConcurrentDictionary<int, Guid> orderIdToTradeId = new();
        private readonly SemaphoreSlim saveLock = new(1, 1);
        private readonly JsonSerializerOptions jsonOptions;
        private bool disposed;

        /// <summary>
        /// Initializes the trade tracking service.
        /// </summary>
        /// <param name="dataFolder">Folder to store trade data. If null, uses AppData.</param>
        public TradeTrackingService(string? dataFolder = null)
        {
            var folder = dataFolder ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "IdiotProof");
            
            Directory.CreateDirectory(folder);
            tradesFilePath = Path.Combine(folder, "trades.json");

            jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
            };

            LoadTrades();
        }

        /// <summary>
        /// Registers a new trade when an entry order is placed.
        /// </summary>
        public async Task<IdiotProofTrade> RegisterTradeAsync(
            int entryOrderId,
            Guid strategyId,
            string strategyName,
            string symbol,
            int quantity)
        {
            var trade = new IdiotProofTrade
            {
                TradeId = Guid.NewGuid(),
                EntryOrderId = entryOrderId,
                StrategyId = strategyId,
                StrategyName = strategyName,
                Symbol = symbol,
                Quantity = quantity,
                Status = TradeStatus.EntrySubmitted,
                CreatedAt = DateTime.UtcNow,
                LastUpdated = DateTime.UtcNow
            };

            trades[trade.TradeId] = trade;
            orderIdToTradeId[entryOrderId] = trade.TradeId;

            await SaveTradesAsync();
            return trade;
        }

        /// <summary>
        /// Adds a child order to an existing trade.
        /// </summary>
        public async Task AddChildOrderAsync(Guid tradeId, int orderId, string orderType)
        {
            if (!trades.TryGetValue(tradeId, out var trade))
                return;

            switch (orderType.ToUpperInvariant())
            {
                case "STOPLOSS":
                    trade.StopLossOrderId = orderId;
                    break;
                case "TAKEPROFIT":
                    trade.TakeProfitOrderId = orderId;
                    break;
                default:
                    if (!trade.ChildOrderIds.Contains(orderId))
                        trade.ChildOrderIds.Add(orderId);
                    break;
            }

            orderIdToTradeId[orderId] = tradeId;
            trade.LastUpdated = DateTime.UtcNow;

            await SaveTradesAsync();
        }

        /// <summary>
        /// Updates the status of a trade based on order fills.
        /// </summary>
        public async Task UpdateTradeStatusAsync(int orderId, OrderStatus status, double? fillPrice = null)
        {
            if (!orderIdToTradeId.TryGetValue(orderId, out var tradeId))
                return;

            if (!trades.TryGetValue(tradeId, out var trade))
                return;

            trade.LastUpdated = DateTime.UtcNow;

            // Determine what happened based on which order filled
            if (orderId == trade.EntryOrderId)
            {
                switch (status)
                {
                    case OrderStatus.Filled:
                        trade.Status = TradeStatus.Open;
                        trade.EntryPrice = fillPrice;
                        break;
                    case OrderStatus.Cancelled:
                    case OrderStatus.ApiCancelled:
                        trade.Status = TradeStatus.Cancelled;
                        break;
                    case OrderStatus.Error:
                        trade.Status = TradeStatus.Error;
                        break;
                }
            }
            else if (orderId == trade.TakeProfitOrderId && status == OrderStatus.Filled)
            {
                trade.Status = TradeStatus.TakeProfitFilled;
                trade.ExitPrice = fillPrice;
                CalculateRealizedPnL(trade);
            }
            else if (orderId == trade.StopLossOrderId && status == OrderStatus.Filled)
            {
                trade.Status = TradeStatus.StopLossFilled;
                trade.ExitPrice = fillPrice;
                CalculateRealizedPnL(trade);
            }
            else if (trade.ChildOrderIds.Contains(orderId) && status == OrderStatus.Filled)
            {
                // If any child order fills, check if it's an exit
                if (trade.Status == TradeStatus.Open)
                {
                    trade.Status = TradeStatus.ManuallyClosed;
                    trade.ExitPrice = fillPrice;
                    CalculateRealizedPnL(trade);
                }
            }

            await SaveTradesAsync();
        }

        private static void CalculateRealizedPnL(IdiotProofTrade trade)
        {
            if (trade.EntryPrice.HasValue && trade.ExitPrice.HasValue)
            {
                trade.RealizedPnL = (trade.ExitPrice.Value - trade.EntryPrice.Value) * trade.Quantity;
            }
        }

        /// <summary>
        /// Gets all tracked trades.
        /// </summary>
        public Task<List<IdiotProofTrade>> GetAllTradesAsync()
        {
            return Task.FromResult(trades.Values.OrderByDescending(t => t.CreatedAt).ToList());
        }

        /// <summary>
        /// Gets active trades (not yet closed).
        /// </summary>
        public Task<List<IdiotProofTrade>> GetActiveTradesAsync()
        {
            var activeTrades = trades.Values
                .Where(t => t.Status is TradeStatus.Pending or TradeStatus.EntrySubmitted or TradeStatus.Open)
                .OrderByDescending(t => t.CreatedAt)
                .ToList();

            return Task.FromResult(activeTrades);
        }

        /// <summary>
        /// Gets a trade by its IdiotProof trade ID.
        /// </summary>
        public Task<IdiotProofTrade?> GetTradeByIdAsync(Guid tradeId)
        {
            trades.TryGetValue(tradeId, out var trade);
            return Task.FromResult(trade);
        }

        /// <summary>
        /// Gets a trade by any of its order IDs.
        /// </summary>
        public Task<IdiotProofTrade?> GetTradeByOrderIdAsync(int orderId)
        {
            if (orderIdToTradeId.TryGetValue(orderId, out var tradeId))
            {
                trades.TryGetValue(tradeId, out var trade);
                return Task.FromResult(trade);
            }

            return Task.FromResult<IdiotProofTrade?>(null);
        }

        /// <summary>
        /// Checks if an order ID belongs to an IdiotProof trade.
        /// </summary>
        public bool IsIdiotProofOrder(int orderId)
        {
            return orderIdToTradeId.ContainsKey(orderId);
        }

        /// <summary>
        /// Gets all order IDs that belong to IdiotProof trades.
        /// </summary>
        public HashSet<int> GetAllIdiotProofOrderIds()
        {
            return new HashSet<int>(orderIdToTradeId.Keys);
        }

        /// <summary>
        /// Removes completed/cancelled trades older than the specified days.
        /// </summary>
        public async Task CleanupOldTradesAsync(int daysToKeep = 30)
        {
            var cutoffDate = DateTime.UtcNow.AddDays(-daysToKeep);
            var tradesToRemove = trades.Values
                .Where(t => t.Status is not (TradeStatus.Pending or TradeStatus.EntrySubmitted or TradeStatus.Open)
                         && t.LastUpdated < cutoffDate)
                .ToList();

            foreach (var trade in tradesToRemove)
            {
                if (trades.TryRemove(trade.TradeId, out _))
                {
                    // Remove all order ID mappings
                    foreach (var orderId in trade.GetAllOrderIds())
                    {
                        orderIdToTradeId.TryRemove(orderId, out _);
                    }
                }
            }

            await SaveTradesAsync();
        }

        private void LoadTrades()
        {
            try
            {
                if (!File.Exists(tradesFilePath))
                    return;

                var json = File.ReadAllText(tradesFilePath);
                var collection = JsonSerializer.Deserialize<IdiotProofTradeCollection>(json, jsonOptions);
                
                if (collection?.Trades == null)
                    return;

                foreach (var trade in collection.Trades)
                {
                    trades[trade.TradeId] = trade;
                    
                    // Rebuild order ID index
                    orderIdToTradeId[trade.EntryOrderId] = trade.TradeId;
                    if (trade.StopLossOrderId.HasValue)
                        orderIdToTradeId[trade.StopLossOrderId.Value] = trade.TradeId;
                    if (trade.TakeProfitOrderId.HasValue)
                        orderIdToTradeId[trade.TakeProfitOrderId.Value] = trade.TradeId;
                    foreach (var childId in trade.ChildOrderIds)
                        orderIdToTradeId[childId] = trade.TradeId;
                }
            }
            catch (Exception ex)
            {
                ConsoleLog.Error("TradeTracking", $"Loading trades failed: {ex.Message}");
            }
        }

        private async Task SaveTradesAsync()
        {
            await saveLock.WaitAsync();
            try
            {
                var collection = new IdiotProofTradeCollection
                {
                    Trades = trades.Values.ToList(),
                    LastModified = DateTime.UtcNow
                };

                var json = JsonSerializer.Serialize(collection, jsonOptions);
                await File.WriteAllTextAsync(tradesFilePath, json);
            }
            catch (Exception ex)
            {
                ConsoleLog.Error("TradeTracking", $"Saving trades failed: {ex.Message}");
            }
            finally
            {
                saveLock.Release();
            }
        }

        /// <summary>
        /// Disposes the trade tracking service, releasing managed resources.
        /// </summary>
        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;

            saveLock.Dispose();
        }
    }
}


