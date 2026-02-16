// ============================================================================
// Live Market Data Hub - Real-Time IBKR Data to Web Frontend
// ============================================================================
// This bridges the IBKR connection in Core to the Web frontend via SignalR.
// Delivers tick-by-tick price updates to TradingView charts in real-time.
// ============================================================================

using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;
using IdiotProof.Web.Controllers;

namespace IdiotProof.Web.Hubs;

/// <summary>
/// Real-time market data hub for live chart updates.
/// </summary>
public class MarketDataHub : Hub
{
    private static readonly ConcurrentDictionary<string, HashSet<string>> _symbolSubscriptions = new();

    // Pending commands queue for Core to poll
    private static readonly ConcurrentQueue<TradingCommand> _pendingCommands = new();

    /// <summary>
    /// Subscribe to real-time updates for a symbol.
    /// </summary>
    public async Task SubscribeToSymbol(string symbol)
    {
        symbol = symbol.ToUpperInvariant();

        // Add connection to symbol's subscriber list
        _symbolSubscriptions.AddOrUpdate(
            symbol,
            _ => [Context.ConnectionId],
            (_, set) => { set.Add(Context.ConnectionId); return set; }
        );

        // Add to SignalR group for efficient broadcasting
        await Groups.AddToGroupAsync(Context.ConnectionId, $"symbol:{symbol}");

        await Clients.Caller.SendAsync("SubscriptionConfirmed", symbol);
    }

    /// <summary>
    /// Unsubscribe from a symbol.
    /// </summary>
    public async Task UnsubscribeFromSymbol(string symbol)
    {
        symbol = symbol.ToUpperInvariant();

        if (_symbolSubscriptions.TryGetValue(symbol, out var subscribers))
        {
            subscribers.Remove(Context.ConnectionId);
        }

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"symbol:{symbol}");
    }

    /// <summary>
    /// Get list of currently subscribed symbols for this connection.
    /// </summary>
    public Task<List<string>> GetSubscribedSymbols()
    {
        var symbols = _symbolSubscriptions
            .Where(kvp => kvp.Value.Contains(Context.ConnectionId))
            .Select(kvp => kvp.Key)
            .ToList();

        return Task.FromResult(symbols);
    }

    /// <summary>
    /// Cancel a specific order (queued for Core to process).
    /// </summary>
    public async Task CancelOrder(int orderId)
    {
        _pendingCommands.Enqueue(new TradingCommand
        {
            Type = CommandType.CancelOrder,
            OrderId = orderId,
            Timestamp = DateTimeOffset.UtcNow
        });

        await Clients.Caller.SendAsync("CommandQueued", new { type = "CancelOrder", orderId });
    }

    /// <summary>
    /// Cancel all open orders (queued for Core to process).
    /// </summary>
    public async Task CancelAllOrders()
    {
        _pendingCommands.Enqueue(new TradingCommand
        {
            Type = CommandType.CancelAllOrders,
            Timestamp = DateTimeOffset.UtcNow
        });

        await Clients.Caller.SendAsync("CommandQueued", new { type = "CancelAllOrders" });
    }

    /// <summary>
    /// Close a specific position (queued for Core to process).
    /// </summary>
    public async Task ClosePosition(string symbol)
    {
        _pendingCommands.Enqueue(new TradingCommand
        {
            Type = CommandType.ClosePosition,
            Symbol = symbol.ToUpperInvariant(),
            Timestamp = DateTimeOffset.UtcNow
        });

        await Clients.Caller.SendAsync("CommandQueued", new { type = "ClosePosition", symbol });
    }

    /// <summary>
    /// Close all positions (queued for Core to process).
    /// </summary>
    public async Task CloseAllPositions()
    {
        _pendingCommands.Enqueue(new TradingCommand
        {
            Type = CommandType.CloseAllPositions,
            Timestamp = DateTimeOffset.UtcNow
        });

        await Clients.Caller.SendAsync("CommandQueued", new { type = "CloseAllPositions" });
    }

    /// <summary>
    /// Activate trading (queued for Core to process).
    /// </summary>
    public async Task ActivateTrading()
    {
        _pendingCommands.Enqueue(new TradingCommand
        {
            Type = CommandType.ActivateTrading,
            Timestamp = DateTimeOffset.UtcNow
        });

        await Clients.Caller.SendAsync("CommandQueued", new { type = "ActivateTrading" });
    }

    /// <summary>
    /// Deactivate trading (queued for Core to process).
    /// </summary>
    public async Task DeactivateTrading()
    {
        _pendingCommands.Enqueue(new TradingCommand
        {
            Type = CommandType.DeactivateTrading,
            Timestamp = DateTimeOffset.UtcNow
        });

        await Clients.Caller.SendAsync("CommandQueued", new { type = "DeactivateTrading" });
    }

    /// <summary>
    /// Reload watchlist (queued for Core to process).
    /// </summary>
    public async Task ReloadWatchlist()
    {
        _pendingCommands.Enqueue(new TradingCommand
        {
            Type = CommandType.ReloadWatchlist,
            Timestamp = DateTimeOffset.UtcNow
        });

        await Clients.Caller.SendAsync("CommandQueued", new { type = "ReloadWatchlist" });
    }

    /// <summary>
    /// Gets and clears pending commands (called by Core via HTTP).
    /// </summary>
    public static List<TradingCommand> GetPendingCommands()
    {
        var commands = new List<TradingCommand>();
        while (_pendingCommands.TryDequeue(out var cmd))
        {
            commands.Add(cmd);
        }
        return commands;
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        // Clean up subscriptions when client disconnects
        foreach (var kvp in _symbolSubscriptions)
        {
            kvp.Value.Remove(Context.ConnectionId);
        }

        await base.OnDisconnectedAsync(exception);
    }
}

/// <summary>
/// Trading command queued for Core to process.
/// </summary>
public sealed class TradingCommand
{
    public CommandType Type { get; set; }
    public int OrderId { get; set; }
    public string? Symbol { get; set; }
    public DateTimeOffset Timestamp { get; set; }
}

public enum CommandType
{
    CancelOrder,
    CancelAllOrders,
    ClosePosition,
    CloseAllPositions,
    ActivateTrading,
    DeactivateTrading,
    ReloadWatchlist
}

/// <summary>
/// Market data tick for real-time updates.
/// </summary>
public sealed class MarketTick
{
    public string Symbol { get; set; } = "";
    public long Timestamp { get; set; }  // Unix timestamp
    public double Price { get; set; }
    public double Bid { get; set; }
    public double Ask { get; set; }
    public long Volume { get; set; }
    public double DayHigh { get; set; }
    public double DayLow { get; set; }
    public double DayOpen { get; set; }
    public double PrevClose { get; set; }
    public double Vwap { get; set; }
    
    // Calculated fields
    public double Change => PrevClose > 0 ? Price - PrevClose : 0;
    public double ChangePercent => PrevClose > 0 ? ((Price - PrevClose) / PrevClose) * 100 : 0;
}

/// <summary>
/// Candle update for chart rendering.
/// </summary>
public sealed class CandleUpdate
{
    public string Symbol { get; set; } = "";
    public long Time { get; set; }
    public double Open { get; set; }
    public double High { get; set; }
    public double Low { get; set; }
    public double Close { get; set; }
    public long Volume { get; set; }
    public bool IsComplete { get; set; }  // True when candle is closed
}

/// <summary>
/// Service to broadcast market data to connected clients.
/// </summary>
public sealed class MarketDataBroadcaster
{
    private readonly IHubContext<MarketDataHub> _hubContext;
    private readonly ILogger<MarketDataBroadcaster> _logger;
    
    // Track current candles being built
    private readonly ConcurrentDictionary<string, CandleUpdate> _currentCandles = new();
    
    public MarketDataBroadcaster(
        IHubContext<MarketDataHub> hubContext,
        ILogger<MarketDataBroadcaster> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }
    
    /// <summary>
    /// Broadcast a price tick to all subscribers.
    /// </summary>
    public async Task BroadcastTickAsync(MarketTick tick)
    {
        try
        {
            // Send to symbol group
            await _hubContext.Clients
                .Group($"symbol:{tick.Symbol}")
                .SendAsync("ReceiveTick", tick);
            
            // Update current candle
            UpdateCurrentCandle(tick);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error broadcasting tick for {Symbol}", tick.Symbol);
        }
    }
    
    /// <summary>
    /// Broadcast a completed candle.
    /// </summary>
    public async Task BroadcastCandleAsync(CandleUpdate candle)
    {
        try
        {
            await _hubContext.Clients
                .Group($"symbol:{candle.Symbol}")
                .SendAsync("ReceiveCandle", candle);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error broadcasting candle for {Symbol}", candle.Symbol);
        }
    }
    
    /// <summary>
    /// Broadcast an alert to all connected clients.
    /// </summary>
    public async Task BroadcastAlertAsync(object alert)
    {
        try
        {
            await _hubContext.Clients.All.SendAsync("ReceiveAlert", alert);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error broadcasting alert");
        }
    }

    /// <summary>
    /// Broadcast position updates to all connected clients.
    /// </summary>
    public async Task BroadcastPositionsAsync(IEnumerable<object> positions)
    {
        try
        {
            await _hubContext.Clients.All.SendAsync("PositionsUpdated", positions.ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error broadcasting positions");
        }
    }

    /// <summary>
    /// Broadcast order updates to all connected clients.
    /// </summary>
    public async Task BroadcastOrdersAsync(IEnumerable<object> orders)
    {
        try
        {
            await _hubContext.Clients.All.SendAsync("OrdersUpdated", orders.ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error broadcasting orders");
        }
    }

    /// <summary>
    /// Broadcast a log message from Core to all connected clients.
    /// </summary>
    public async Task BroadcastLogMessageAsync(LogMessageData log)
    {
        try
        {
            await _hubContext.Clients.All.SendAsync("ReceiveLogMessage", new
            {
                id = log.Id,
                timestamp = log.Timestamp,
                level = log.Level,
                category = log.Category,
                message = log.Message,
                symbol = log.Symbol,
                htmlContent = log.HtmlContent,
                data = log.Data
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error broadcasting log message");
        }
    }

    private void UpdateCurrentCandle(MarketTick tick)
    {
        // Round to current minute
        var candleTime = (tick.Timestamp / 60) * 60;
        var key = $"{tick.Symbol}_{candleTime}";
        
        _currentCandles.AddOrUpdate(
            key,
            _ => new CandleUpdate
            {
                Symbol = tick.Symbol,
                Time = candleTime,
                Open = tick.Price,
                High = tick.Price,
                Low = tick.Price,
                Close = tick.Price,
                Volume = tick.Volume,
                IsComplete = false
            },
            (_, existing) =>
            {
                existing.High = Math.Max(existing.High, tick.Price);
                existing.Low = Math.Min(existing.Low, tick.Price);
                existing.Close = tick.Price;
                existing.Volume += tick.Volume;
                return existing;
            }
        );
        
        // Send candle update
        var candle = _currentCandles[key];
        _ = _hubContext.Clients
            .Group($"symbol:{tick.Symbol}")
            .SendAsync("ReceiveCandleUpdate", candle);
    }
}
