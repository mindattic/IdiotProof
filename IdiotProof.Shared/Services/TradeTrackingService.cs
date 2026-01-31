// ============================================================================
// TradeTrackingService - Tracks IdiotProof orders through their lifecycle
// ============================================================================

using IdiotProof.Shared.Models;
using System.Collections.Concurrent;
using System.Text.Json;

namespace IdiotProof.Shared.Services
{
    /// <summary>
    /// Service for tracking IdiotProof trades.
    /// Persists trade data to allow filtering orders to only show IdiotProof trades.
    /// </summary>
    public class TradeTrackingService : ITradeTrackingService
    {
        private readonly string _tradesFilePath;
        private readonly ConcurrentDictionary<Guid, IdiotProofTrade> _trades = new();
        private readonly ConcurrentDictionary<int, Guid> _orderIdToTradeId = new();
        private readonly SemaphoreSlim _saveLock = new(1, 1);
        private readonly JsonSerializerOptions _jsonOptions;

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
            _tradesFilePath = Path.Combine(folder, "trades.json");

            _jsonOptions = new JsonSerializerOptions
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

            _trades[trade.TradeId] = trade;
            _orderIdToTradeId[entryOrderId] = trade.TradeId;

            await SaveTradesAsync();
            return trade;
        }

        /// <summary>
        /// Adds a child order to an existing trade.
        /// </summary>
        public async Task AddChildOrderAsync(Guid tradeId, int orderId, string orderType)
        {
            if (!_trades.TryGetValue(tradeId, out var trade))
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

            _orderIdToTradeId[orderId] = tradeId;
            trade.LastUpdated = DateTime.UtcNow;

            await SaveTradesAsync();
        }

        /// <summary>
        /// Updates the status of a trade based on order fills.
        /// </summary>
        public async Task UpdateTradeStatusAsync(int orderId, OrderStatus status, double? fillPrice = null)
        {
            if (!_orderIdToTradeId.TryGetValue(orderId, out var tradeId))
                return;

            if (!_trades.TryGetValue(tradeId, out var trade))
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
            return Task.FromResult(_trades.Values.OrderByDescending(t => t.CreatedAt).ToList());
        }

        /// <summary>
        /// Gets active trades (not yet closed).
        /// </summary>
        public Task<List<IdiotProofTrade>> GetActiveTradesAsync()
        {
            var activeTrades = _trades.Values
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
            _trades.TryGetValue(tradeId, out var trade);
            return Task.FromResult(trade);
        }

        /// <summary>
        /// Gets a trade by any of its order IDs.
        /// </summary>
        public Task<IdiotProofTrade?> GetTradeByOrderIdAsync(int orderId)
        {
            if (_orderIdToTradeId.TryGetValue(orderId, out var tradeId))
            {
                _trades.TryGetValue(tradeId, out var trade);
                return Task.FromResult(trade);
            }

            return Task.FromResult<IdiotProofTrade?>(null);
        }

        /// <summary>
        /// Checks if an order ID belongs to an IdiotProof trade.
        /// </summary>
        public bool IsIdiotProofOrder(int orderId)
        {
            return _orderIdToTradeId.ContainsKey(orderId);
        }

        /// <summary>
        /// Gets all order IDs that belong to IdiotProof trades.
        /// </summary>
        public HashSet<int> GetAllIdiotProofOrderIds()
        {
            return new HashSet<int>(_orderIdToTradeId.Keys);
        }

        /// <summary>
        /// Removes completed/cancelled trades older than the specified days.
        /// </summary>
        public async Task CleanupOldTradesAsync(int daysToKeep = 30)
        {
            var cutoffDate = DateTime.UtcNow.AddDays(-daysToKeep);
            var tradesToRemove = _trades.Values
                .Where(t => t.Status is not (TradeStatus.Pending or TradeStatus.EntrySubmitted or TradeStatus.Open)
                         && t.LastUpdated < cutoffDate)
                .ToList();

            foreach (var trade in tradesToRemove)
            {
                if (_trades.TryRemove(trade.TradeId, out _))
                {
                    // Remove all order ID mappings
                    foreach (var orderId in trade.GetAllOrderIds())
                    {
                        _orderIdToTradeId.TryRemove(orderId, out _);
                    }
                }
            }

            await SaveTradesAsync();
        }

        private void LoadTrades()
        {
            try
            {
                if (!File.Exists(_tradesFilePath))
                    return;

                var json = File.ReadAllText(_tradesFilePath);
                var collection = JsonSerializer.Deserialize<IdiotProofTradeCollection>(json, _jsonOptions);
                
                if (collection?.Trades == null)
                    return;

                foreach (var trade in collection.Trades)
                {
                    _trades[trade.TradeId] = trade;
                    
                    // Rebuild order ID index
                    _orderIdToTradeId[trade.EntryOrderId] = trade.TradeId;
                    if (trade.StopLossOrderId.HasValue)
                        _orderIdToTradeId[trade.StopLossOrderId.Value] = trade.TradeId;
                    if (trade.TakeProfitOrderId.HasValue)
                        _orderIdToTradeId[trade.TakeProfitOrderId.Value] = trade.TradeId;
                    foreach (var childId in trade.ChildOrderIds)
                        _orderIdToTradeId[childId] = trade.TradeId;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TradeTracking] Error loading trades: {ex.Message}");
            }
        }

        private async Task SaveTradesAsync()
        {
            await _saveLock.WaitAsync();
            try
            {
                var collection = new IdiotProofTradeCollection
                {
                    Trades = _trades.Values.ToList(),
                    LastModified = DateTime.UtcNow
                };

                var json = JsonSerializer.Serialize(collection, _jsonOptions);
                await File.WriteAllTextAsync(_tradesFilePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TradeTracking] Error saving trades: {ex.Message}");
            }
            finally
            {
                _saveLock.Release();
            }
        }
    }
}
