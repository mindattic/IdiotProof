// ============================================================================
// Market Data API Controller - Receives Live Data from Core
// ============================================================================
// Core posts price ticks here, and we broadcast them to web clients via SignalR.
// This is the entry point for all live IBKR data into the web frontend.
// ============================================================================

using Microsoft.AspNetCore.Mvc;
using IdiotProof.Web.Services;
using IdiotProof.Web.Hubs;

namespace IdiotProof.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MarketDataController : ControllerBase
{
    private readonly IbkrWebBridge _bridge;
    private readonly MarketDataBroadcaster _broadcaster;
    private readonly ILogger<MarketDataController> _logger;
    
    public MarketDataController(
        IbkrWebBridge bridge,
        MarketDataBroadcaster broadcaster,
        ILogger<MarketDataController> logger)
    {
        _bridge = bridge;
        _broadcaster = broadcaster;
        _logger = logger;
    }
    
    /// <summary>
    /// Receives a price tick from Core.
    /// POST /api/marketdata/tick
    /// </summary>
    [HttpPost("tick")]
    public async Task<IActionResult> PostTick([FromBody] TickData tick)
    {
        if (string.IsNullOrEmpty(tick.Symbol))
            return BadRequest("Symbol is required");
        
        await _bridge.OnPriceTickAsync(
            tick.Symbol,
            tick.Price,
            tick.Bid,
            tick.Ask,
            tick.Volume);
        
        return Ok();
    }
    
    /// <summary>
    /// Receives multiple ticks in a batch (more efficient).
    /// POST /api/marketdata/ticks
    /// </summary>
    [HttpPost("ticks")]
    public async Task<IActionResult> PostTicks([FromBody] TickData[] ticks)
    {
        foreach (var tick in ticks)
        {
            if (!string.IsNullOrEmpty(tick.Symbol))
            {
                await _bridge.OnPriceTickAsync(
                    tick.Symbol,
                    tick.Price,
                    tick.Bid,
                    tick.Ask,
                    tick.Volume);
            }
        }
        
        return Ok(new { processed = ticks.Length });
    }
    
    /// <summary>
    /// Receives a completed candle from Core.
    /// POST /api/marketdata/candle
    /// </summary>
    [HttpPost("candle")]
    public async Task<IActionResult> PostCandle([FromBody] CandleData candle)
    {
        if (string.IsNullOrEmpty(candle.Symbol))
            return BadRequest("Symbol is required");
        
        await _bridge.OnCandleCompleteAsync(
            candle.Symbol,
            candle.Time,
            candle.Open,
            candle.High,
            candle.Low,
            candle.Close,
            candle.Volume);
        
        return Ok();
    }
    
    /// <summary>
    /// Sets daily reference data for a symbol (called once per day by Core).
    /// POST /api/marketdata/daily
    /// </summary>
    [HttpPost("daily")]
    public IActionResult SetDailyData([FromBody] DailyData data)
    {
        if (string.IsNullOrEmpty(data.Symbol))
            return BadRequest("Symbol is required");
        
        _bridge.SetSymbolDailyData(
            data.Symbol,
            data.PrevClose,
            data.DayOpen,
            data.DayHigh,
            data.DayLow,
            data.AvgVolume);
        
        return Ok();
    }
    
    /// <summary>
    /// Broadcasts an alert to all connected clients.
    /// POST /api/marketdata/alert
    /// </summary>
    [HttpPost("alert")]
    public async Task<IActionResult> PostAlert([FromBody] AlertData alert)
    {
        await _broadcaster.BroadcastAlertAsync(alert);
        _logger.LogInformation("Alert broadcast: {Symbol} {Type}", alert.Symbol, alert.Type);
        return Ok();
    }
    
    /// <summary>
    /// Gets current state for a symbol.
    /// GET /api/marketdata/{symbol}/state
    /// </summary>
    [HttpGet("{symbol}/state")]
    public IActionResult GetSymbolState(string symbol)
    {
        var state = _bridge.GetSymbolState(symbol);
        if (state == null)
            return NotFound($"No data for {symbol}");
        
        return Ok(new
        {
            symbol = state.Symbol,
            price = state.LastPrice,
            prevClose = state.PrevClose,
            change = state.Change,
            changePercent = state.ChangePercent,
            dayHigh = state.DayHigh,
            dayLow = state.DayLow,
            dayVolume = state.DayVolume,
            vwap = state.Vwap
        });
    }
    
    /// <summary>
    /// Gets list of tracked symbols.
    /// GET /api/marketdata/symbols
    /// </summary>
    [HttpGet("symbols")]
    public IActionResult GetTrackedSymbols()
    {
        return Ok(_bridge.GetTrackedSymbols());
    }

    /// <summary>
    /// Heartbeat from Core to indicate it's connected and sending data.
    /// POST /api/marketdata/heartbeat
    /// </summary>
    [HttpPost("heartbeat")]
    public IActionResult Heartbeat()
    {
        _bridge.OnHeartbeat();
        return Ok(new { status = "ok", timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });
    }

    /// <summary>
    /// Receives position updates from Core.
    /// POST /api/marketdata/positions
    /// </summary>
    [HttpPost("positions")]
    public async Task<IActionResult> UpdatePositions([FromBody] PositionData[] positions)
    {
        foreach (var pos in positions)
        {
            _bridge.UpdatePosition(pos.Symbol, pos.Quantity, pos.AvgCost, pos.MarketPrice, pos.UnrealizedPnL);
        }

        // Broadcast to connected clients
        var positionInfos = _bridge.GetPositions();
        await _broadcaster.BroadcastPositionsAsync(positionInfos);

        return Ok(new { updated = positions.Length });
    }

    /// <summary>
    /// Gets current positions.
    /// GET /api/marketdata/positions
    /// </summary>
    [HttpGet("positions")]
    public IActionResult GetPositions()
    {
        return Ok(_bridge.GetPositions());
    }

    /// <summary>
    /// Receives order updates from Core.
    /// POST /api/marketdata/orders
    /// </summary>
    [HttpPost("orders")]
    public async Task<IActionResult> UpdateOrders([FromBody] OrderData[] orders)
    {
        foreach (var order in orders)
        {
            _bridge.UpdateOrder(order.OrderId, order.Symbol, order.Direction, order.Quantity, 
                order.OrderType, order.LimitPrice, order.StopPrice, order.Status);
        }

        // Broadcast to connected clients
        var orderInfos = _bridge.GetOrders();
        await _broadcaster.BroadcastOrdersAsync(orderInfos);

        return Ok(new { updated = orders.Length });
    }

    /// <summary>
    /// Gets current open orders.
    /// GET /api/marketdata/orders
    /// </summary>
    [HttpGet("orders")]
    public IActionResult GetOrders()
    {
        return Ok(_bridge.GetOrders());
    }

    /// <summary>
    /// Gets pending commands queued by Web clients (for Core to poll and execute).
    /// GET /api/marketdata/commands
    /// </summary>
    [HttpGet("commands")]
    public IActionResult GetPendingCommands()
    {
        var commands = MarketDataHub.GetPendingCommands();
        return Ok(commands);
    }

    /// <summary>
    /// Receives log messages from Core for display in the Log tab.
    /// POST /api/marketdata/log
    /// </summary>
    [HttpPost("log")]
    public async Task<IActionResult> PostLogMessage([FromBody] LogMessageData log)
    {
        if (string.IsNullOrEmpty(log.Message))
            return BadRequest("Message is required");

        await _broadcaster.BroadcastLogMessageAsync(log.Timestamp, log.Message);
        return Ok();
    }
}

// Request DTOs
public sealed class TickData
{
    public string Symbol { get; set; } = "";
    public double Price { get; set; }
    public double Bid { get; set; }
    public double Ask { get; set; }
    public long Volume { get; set; }
}

public sealed class CandleData
{
    public string Symbol { get; set; } = "";
    public long Time { get; set; }
    public double Open { get; set; }
    public double High { get; set; }
    public double Low { get; set; }
    public double Close { get; set; }
    public long Volume { get; set; }
}

public sealed class DailyData
{
    public string Symbol { get; set; } = "";
    public double PrevClose { get; set; }
    public double DayOpen { get; set; }
    public double DayHigh { get; set; }
    public double DayLow { get; set; }
    public double AvgVolume { get; set; }
}

public sealed class AlertData
{
    public string Symbol { get; set; } = "";
    public string Type { get; set; } = "";  // "SuddenSpike", "SuddenDrop", etc.
    public string Severity { get; set; } = "";
    public double Price { get; set; }
    public double ChangePercent { get; set; }
    public int Confidence { get; set; }
    public string Reason { get; set; } = "";
    public object? LongSetup { get; set; }
    public object? ShortSetup { get; set; }
}

public sealed class PositionData
{
    public string Symbol { get; set; } = "";
    public decimal Quantity { get; set; }
    public double AvgCost { get; set; }
    public double? MarketPrice { get; set; }
    public double? UnrealizedPnL { get; set; }
}

public sealed class OrderData
{
    public int OrderId { get; set; }
    public string Symbol { get; set; } = "";
    public string Direction { get; set; } = "";
    public int Quantity { get; set; }
    public string OrderType { get; set; } = "";
    public double? LimitPrice { get; set; }
    public double? StopPrice { get; set; }
    public string Status { get; set; } = "";
}

public sealed class LogMessageData
{
    public DateTimeOffset Timestamp { get; set; }
    public string Message { get; set; } = "";
}
