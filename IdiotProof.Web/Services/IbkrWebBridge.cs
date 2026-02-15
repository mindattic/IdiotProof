// ============================================================================
// IBKR to Web Bridge - Connects Core's IBKR Data to Web's Charts
// ============================================================================
// This service runs in the Web project and receives price updates from Core
// via an internal HTTP/SignalR connection, then broadcasts to browser clients.
//
// Data Flow:
// IBKR → Core (tick handler) → HTTP POST → Web (this bridge) → SignalR → Browser
// ============================================================================

using System.Collections.Concurrent;
using System.Text.Json;
using IdiotProof.Web.Hubs;
using IdiotProof.Shared;

namespace IdiotProof.Web.Services;

/// <summary>
/// Receives price data from Core and broadcasts to web clients.
/// </summary>
public sealed class IbkrWebBridge : IHostedService
{
    private readonly MarketDataBroadcaster _broadcaster;
    private readonly ILogger<IbkrWebBridge> _logger;
    private readonly ConcurrentDictionary<string, SymbolState> _symbolStates = new();
    private readonly ConcurrentDictionary<string, PositionInfo> _positions = new();

    // Track last broadcast time to throttle updates
    private readonly ConcurrentDictionary<string, DateTime> _lastBroadcast = new();
    private readonly TimeSpan _minBroadcastInterval = TimeSpan.FromMilliseconds(100);

    // Connection status
    private bool _isConnected;
    private DateTime _lastHeartbeat;

    public bool IsConnected => _isConnected && (DateTime.UtcNow - _lastHeartbeat).TotalSeconds < 30;

    public IbkrWebBridge(
        MarketDataBroadcaster broadcaster,
        ILogger<IbkrWebBridge> logger)
    {
        _broadcaster = broadcaster;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("IBKR Web Bridge started - ready to receive price data");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("IBKR Web Bridge stopped");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Called by Core to indicate it's connected and sending data.
    /// </summary>
    public void OnHeartbeat()
    {
        _isConnected = true;
        _lastHeartbeat = DateTime.UtcNow;
    }

    /// <summary>
    /// Updates position data from Core.
    /// </summary>
    public void UpdatePosition(string symbol, decimal quantity, double avgCost, double? marketPrice, double? unrealizedPnL)
    {
        var position = new PositionInfo
        {
            Symbol = symbol.ToUpperInvariant(),
            Quantity = quantity,
            AvgCost = avgCost,
            MarketPrice = marketPrice,
            UnrealizedPnL = unrealizedPnL
        };

        if (quantity == 0)
        {
            _positions.TryRemove(symbol.ToUpperInvariant(), out _);
        }
        else
        {
            _positions[symbol.ToUpperInvariant()] = position;
        }
    }

    /// <summary>
    /// Gets all current positions.
    /// </summary>
    public List<PositionInfo> GetPositions() => _positions.Values.ToList();
    
    /// <summary>
    /// Called when a price tick arrives from Core.
    /// </summary>
    public async Task OnPriceTickAsync(string symbol, double price, double bid, double ask, long volume)
    {
        symbol = symbol.ToUpperInvariant();
        
        // Get or create symbol state
        var state = _symbolStates.GetOrAdd(symbol, _ => new SymbolState { Symbol = symbol });
        
        // Update state
        state.UpdatePrice(price, volume);
        
        // Throttle broadcasts
        if (_lastBroadcast.TryGetValue(symbol, out var lastTime) && 
            DateTime.UtcNow - lastTime < _minBroadcastInterval)
        {
            return;
        }
        
        _lastBroadcast[symbol] = DateTime.UtcNow;
        
        // Create tick
        var tick = new MarketTick
        {
            Symbol = symbol,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Price = price,
            Bid = bid,
            Ask = ask,
            Volume = volume,
            DayHigh = state.DayHigh,
            DayLow = state.DayLow,
            DayOpen = state.DayOpen,
            PrevClose = state.PrevClose,
            Vwap = state.Vwap
        };
        
        // Broadcast to web clients
        await _broadcaster.BroadcastTickAsync(tick);
    }
    
    /// <summary>
    /// Called when a candle completes (end of minute/interval).
    /// </summary>
    public async Task OnCandleCompleteAsync(string symbol, long time, double open, double high, double low, double close, long volume)
    {
        var candle = new CandleUpdate
        {
            Symbol = symbol.ToUpperInvariant(),
            Time = time,
            Open = open,
            High = high,
            Low = low,
            Close = close,
            Volume = volume,
            IsComplete = true
        };
        
        await _broadcaster.BroadcastCandleAsync(candle);
    }
    
    /// <summary>
    /// Sets daily reference data for a symbol (from Core on startup).
    /// </summary>
    public void SetSymbolDailyData(string symbol, double prevClose, double dayOpen, double dayHigh, double dayLow, double avgVolume)
    {
        symbol = symbol.ToUpperInvariant();
        var state = _symbolStates.GetOrAdd(symbol, _ => new SymbolState { Symbol = symbol });
        
        state.PrevClose = prevClose;
        state.DayOpen = dayOpen;
        state.DayHigh = dayHigh;
        state.DayLow = dayLow;
        state.AverageVolume = avgVolume;
        
        _logger.LogInformation("Set daily data for {Symbol}: PrevClose={PrevClose}, Open={Open}", 
            symbol, prevClose, dayOpen);
    }
    
    /// <summary>
    /// Gets current state for a symbol.
    /// </summary>
    public SymbolState? GetSymbolState(string symbol)
    {
        return _symbolStates.TryGetValue(symbol.ToUpperInvariant(), out var state) ? state : null;
    }
    
    /// <summary>
    /// Gets all tracked symbols.
    /// </summary>
    public IEnumerable<string> GetTrackedSymbols() => _symbolStates.Keys;
}

/// <summary>
/// Tracks state for a single symbol.
/// </summary>
public sealed class SymbolState
{
    public string Symbol { get; init; } = "";
    public double LastPrice { get; private set; }
    public double PrevClose { get; set; }
    public double DayOpen { get; set; }
    public double DayHigh { get; set; }
    public double DayLow { get; set; } = double.MaxValue;
    public long DayVolume { get; private set; }
    public double AverageVolume { get; set; } = 1_000_000;
    public double Vwap { get; private set; }
    
    // For VWAP calculation
    private double _cumulativeTypicalPriceVolume;
    private long _cumulativeVolume;
    
    public void UpdatePrice(double price, long volume)
    {
        LastPrice = price;
        
        if (price > DayHigh) DayHigh = price;
        if (price < DayLow) DayLow = price;
        
        DayVolume += volume;
        
        // Update VWAP
        if (volume > 0)
        {
            var typicalPrice = price; // Simplified - would use (H+L+C)/3 with full candle
            _cumulativeTypicalPriceVolume += typicalPrice * volume;
            _cumulativeVolume += volume;
            Vwap = _cumulativeVolume > 0 ? _cumulativeTypicalPriceVolume / _cumulativeVolume : price;
        }
    }
    
    public double ChangePercent => PrevClose > 0 ? ((LastPrice - PrevClose) / PrevClose) * 100 : 0;
    public double Change => LastPrice - PrevClose;
}

/// <summary>
/// Position information for display.
/// </summary>
public sealed class PositionInfo
{
    public string Symbol { get; set; } = "";
    public decimal Quantity { get; set; }
    public double AvgCost { get; set; }
    public double? MarketPrice { get; set; }
    public double? MarketValue => MarketPrice.HasValue ? (double)Quantity * MarketPrice.Value : null;
    public double? UnrealizedPnL { get; set; }
    public bool IsLong => Quantity > 0;
}
