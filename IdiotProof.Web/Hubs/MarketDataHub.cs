// ============================================================================
// Live Market Data Hub - Real-Time IBKR Data to Web Frontend
// ============================================================================
// This bridges the IBKR connection in Core to the Web frontend via SignalR.
// Delivers tick-by-tick price updates to TradingView charts in real-time.
// ============================================================================

using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;

namespace IdiotProof.Web.Hubs;

/// <summary>
/// Real-time market data hub for live chart updates.
/// </summary>
public class MarketDataHub : Hub
{
    private static readonly ConcurrentDictionary<string, HashSet<string>> _symbolSubscriptions = new();
    
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
