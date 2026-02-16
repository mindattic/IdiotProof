// ============================================================================
// Web Frontend Client - Pushes Live Data from Core to Web
// ============================================================================
// This runs in IdiotProof.Core and sends price ticks, candles, and alerts
// to the IdiotProof.Web frontend via HTTP.
//
// Usage:
// 1. Initialize with Web frontend URL
// 2. Call OnPriceTick() when IBKR sends price updates
// 3. Call OnCandleComplete() when a candle closes
// 4. Call SendAlert() when SuddenMoveDetector fires
// ============================================================================

using System.Net.Http.Json;
using System.Text.Json;

namespace IdiotProof.Services;

/// <summary>
/// Configuration for web frontend connection.
/// </summary>
public sealed class WebFrontendConfig
{
    /// <summary>
    /// Base URL of the IdiotProof.Web frontend.
    /// </summary>
    public string BaseUrl { get; set; } = "http://localhost:5000";
    
    /// <summary>
    /// Whether to enable pushing data to web frontend.
    /// </summary>
    public bool Enabled { get; set; } = true;
    
    /// <summary>
    /// Timeout for HTTP requests.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);
    
    /// <summary>
    /// Whether to batch ticks (more efficient).
    /// </summary>
    public bool BatchTicks { get; set; } = true;
    
    /// <summary>
    /// Batch size before sending.
    /// </summary>
    public int BatchSize { get; set; } = 10;
    
    /// <summary>
    /// Maximum time to hold a batch before sending.
    /// </summary>
    public TimeSpan BatchTimeout { get; set; } = TimeSpan.FromMilliseconds(100);
}

/// <summary>
/// Pushes market data to the web frontend.
/// </summary>
public sealed class WebFrontendClient : IDisposable
{
    private readonly WebFrontendConfig _config;
    private readonly HttpClient _httpClient;
    private readonly List<TickPayload> _tickBatch = new();
    private readonly object _batchLock = new();
    private readonly Timer? _batchTimer;
    private bool _disposed;
    
    public WebFrontendClient(WebFrontendConfig? config = null)
    {
        _config = config ?? new WebFrontendConfig();
        
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(_config.BaseUrl),
            Timeout = _config.Timeout
        };
        
        if (_config.BatchTicks)
        {
            _batchTimer = new Timer(FlushBatch, null, _config.BatchTimeout, _config.BatchTimeout);
        }
    }
    
    /// <summary>
    /// Sends a price tick to the web frontend.
    /// </summary>
    public async Task OnPriceTickAsync(string symbol, double price, double bid = 0, double ask = 0, long volume = 0)
    {
        if (!_config.Enabled) return;
        
        var tick = new TickPayload
        {
            Symbol = symbol,
            Price = price,
            Bid = bid,
            Ask = ask,
            Volume = volume
        };
        
        if (_config.BatchTicks)
        {
            lock (_batchLock)
            {
                _tickBatch.Add(tick);
                
                if (_tickBatch.Count >= _config.BatchSize)
                {
                    _ = FlushBatchAsync();
                }
            }
        }
        else
        {
            await SendTickAsync(tick);
        }
    }
    
    /// <summary>
    /// Sends a completed candle to the web frontend.
    /// </summary>
    public async Task OnCandleCompleteAsync(string symbol, DateTime time, double open, double high, double low, double close, long volume)
    {
        if (!_config.Enabled) return;
        
        try
        {
            var candle = new
            {
                symbol,
                time = new DateTimeOffset(time).ToUnixTimeSeconds(),
                open,
                high,
                low,
                close,
                volume
            };
            
            await _httpClient.PostAsJsonAsync("/api/marketdata/candle", candle);
        }
        catch (Exception ex)
        {
            // Log but don't throw - web frontend being down shouldn't stop trading
            Console.WriteLine($"[WebClient] Failed to send candle: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Sets daily reference data for a symbol.
    /// </summary>
    public async Task SetDailyDataAsync(string symbol, double prevClose, double dayOpen, double dayHigh, double dayLow, double avgVolume)
    {
        if (!_config.Enabled) return;
        
        try
        {
            var data = new
            {
                symbol,
                prevClose,
                dayOpen,
                dayHigh,
                dayLow,
                avgVolume
            };
            
            await _httpClient.PostAsJsonAsync("/api/marketdata/daily", data);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WebClient] Failed to set daily data: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Sends an alert to the web frontend.
    /// </summary>
    public async Task SendAlertAsync(
        string symbol,
        string type,
        string severity,
        double price,
        double changePercent,
        int confidence,
        string reason,
        object? longSetup = null,
        object? shortSetup = null)
    {
        if (!_config.Enabled) return;
        
        try
        {
            var alert = new
            {
                symbol,
                type,
                severity,
                price,
                changePercent,
                confidence,
                reason,
                longSetup,
                shortSetup
            };
            
            await _httpClient.PostAsJsonAsync("/api/marketdata/alert", alert);
            Console.WriteLine($"[WebClient] Alert sent: {symbol} {type}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WebClient] Failed to send alert: {ex.Message}");
        }
    }
    
    private async Task SendTickAsync(TickPayload tick)
    {
        try
        {
            await _httpClient.PostAsJsonAsync("/api/marketdata/tick", tick);
        }
        catch
        {
            // Silent fail - don't spam console with tick errors
        }
    }
    
    private void FlushBatch(object? state)
    {
        _ = FlushBatchAsync();
    }
    
    private async Task FlushBatchAsync()
    {
        TickPayload[] ticksToSend;
        
        lock (_batchLock)
        {
            if (_tickBatch.Count == 0) return;
            
            ticksToSend = _tickBatch.ToArray();
            _tickBatch.Clear();
        }
        
        try
        {
            await _httpClient.PostAsJsonAsync("/api/marketdata/ticks", ticksToSend);
        }
        catch
        {
            // Silent fail
        }
    }
    
    /// <summary>
    /// Tests connection to web frontend.
    /// </summary>
    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/marketdata/symbols");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Sends a heartbeat to indicate Core is connected and sending data.
    /// </summary>
    public async Task SendHeartbeatAsync()
    {
        if (!_config.Enabled) return;

        try
        {
            await _httpClient.PostAsync("/api/marketdata/heartbeat", null);
        }
        catch
        {
            // Silent fail
        }
    }

    /// <summary>
    /// Sends position updates to the web frontend.
    /// </summary>
    public async Task SendPositionsAsync(IEnumerable<PositionPayload> positions)
    {
        if (!_config.Enabled) return;

        try
        {
            await _httpClient.PostAsJsonAsync("/api/marketdata/positions", positions.ToArray());
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WebClient] Failed to send positions: {ex.Message}");
        }
    }

    /// <summary>
    /// Sends order updates to the web frontend.
    /// </summary>
    public async Task SendOrdersAsync(IEnumerable<OrderPayload> orders)
    {
        if (!_config.Enabled) return;

        try
        {
            await _httpClient.PostAsJsonAsync("/api/marketdata/orders", orders.ToArray());
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WebClient] Failed to send orders: {ex.Message}");
        }
    }

    /// <summary>
    /// Sends a simple log message to the web frontend for display in the Log tab.
    /// </summary>
    public async Task SendLogMessageAsync(string message)
    {
        if (!_config.Enabled) return;

        try
        {
            var payload = new
            {
                id = Guid.NewGuid().ToString("N")[..8],
                timestamp = DateTimeOffset.Now.ToUnixTimeMilliseconds(),
                level = "Info",
                category = "System",
                message,
                htmlContent = (string?)null
            };
            await _httpClient.PostAsJsonAsync("/api/marketdata/log", payload);
        }
        catch
        {
            // Silent fail - don't recurse on log failures
        }
    }

    /// <summary>
    /// Sends a structured log message with rich formatting.
    /// </summary>
    public async Task SendStructuredLogAsync(
        string level,
        string category,
        string message,
        string? symbol = null,
        string? htmlContent = null,
        object? data = null)
    {
        if (!_config.Enabled) return;

        try
        {
            var payload = new
            {
                id = Guid.NewGuid().ToString("N")[..8],
                timestamp = DateTimeOffset.Now.ToUnixTimeMilliseconds(),
                level,
                category,
                message,
                symbol,
                htmlContent,
                data
            };
            await _httpClient.PostAsJsonAsync("/api/marketdata/log", payload);
        }
        catch
        {
            // Silent fail
        }
    }

    /// <summary>
    /// Sends a trade execution log.
    /// </summary>
    public async Task SendTradeLogAsync(string symbol, string action, string details, bool isProfit = true)
    {
        var emoji = isProfit ? "💰" : "📉";
        var cssClass = isProfit ? "profit" : "loss";
        var html = $"""
            <span class="log-emoji">{emoji}</span>
            <span class="log-symbol">{symbol}</span>
            <span class="log-action {cssClass}">{action}</span>
            <span class="log-details">{details}</span>
            """;

        await SendStructuredLogAsync(
            isProfit ? "Success" : "Warning",
            "Trade",
            $"{symbol} {action}: {details}",
            symbol,
            html);
    }

    /// <summary>
    /// Sends an order log.
    /// </summary>
    public async Task SendOrderLogAsync(string symbol, string side, int quantity, string orderType, double? price = null, int? orderId = null)
    {
        var isBuy = side.Equals("BUY", StringComparison.OrdinalIgnoreCase);
        var emoji = isBuy ? "📈" : "📉";
        var priceStr = price.HasValue ? $"${price:F2}" : "MKT";
        var orderIdStr = orderId.HasValue ? $" (ID: {orderId})" : "";

        var html = $"""
            <span class="log-emoji">{emoji}</span>
            <span class="log-symbol">{symbol}</span>
            <span class="log-side {side.ToLowerInvariant()}">{side}</span>
            <span class="log-quantity">{quantity}</span>
            <span class="log-order-type">{orderType}</span>
            <span class="log-price">@ {priceStr}</span>
            <span class="log-order-id">{orderIdStr}</span>
            """;

        await SendStructuredLogAsync(
            "Info",
            "Order",
            $"{side} {quantity} {symbol} @ {priceStr}{orderIdStr}",
            symbol,
            html);
    }

    /// <summary>
    /// Sends an alert log with high visibility.
    /// </summary>
    public async Task SendAlertLogAsync(string symbol, string alertType, double changePercent, int confidence)
    {
        var direction = changePercent >= 0 ? "🚀" : "📉";
        var changeStr = changePercent >= 0 ? $"+{changePercent:F1}%" : $"{changePercent:F1}%";
        var changeClass = changePercent >= 0 ? "positive" : "negative";

        var html = $"""
            <span class="log-alert-icon">{direction}</span>
            <span class="log-alert-badge">{alertType}</span>
            <span class="log-symbol highlight">{symbol}</span>
            <span class="log-change {changeClass}">{changeStr}</span>
            <span class="log-confidence">Confidence: {confidence}%</span>
            """;

        await SendStructuredLogAsync(
            "Alert",
            "Alert",
            $"🚨 {symbol} {alertType} ({changeStr}) - Confidence: {confidence}%",
            symbol,
            html);
    }

    /// <summary>
    /// Sends a connection status log.
    /// </summary>
    public async Task SendConnectionLogAsync(string service, bool connected)
    {
        var emoji = connected ? "✅" : "❌";
        var status = connected ? "Connected" : "Disconnected";
        var statusClass = connected ? "connected" : "disconnected";

        var html = $"""
            <span class="log-emoji">{emoji}</span>
            <span class="log-service">{service}</span>
            <span class="log-status {statusClass}">{status}</span>
            """;

        await SendStructuredLogAsync(
            connected ? "Success" : "Error",
            "Connection",
            $"{service}: {status}",
            null,
            html);
    }

    /// <summary>
    /// Sends a heartbeat status log.
    /// </summary>
    public async Task SendHeartbeatLogAsync(bool ibkrConnected, bool tradingActive, int strategyCount)
    {
        var ibkrEmoji = ibkrConnected ? "✅" : "❌";
        var tradingEmoji = tradingActive ? "📊" : "⏸️";
        var tradingStatus = tradingActive ? $"Trading ({strategyCount})" : "Idle";

        var html = $"""
            <span class="log-heartbeat-icon">💓</span>
            <span class="log-ibkr {(ibkrConnected ? "connected" : "disconnected")}">{ibkrEmoji} IBKR</span>
            <span class="log-separator">|</span>
            <span class="log-trading {(tradingActive ? "active" : "idle")}">{tradingEmoji} {tradingStatus}</span>
            """;

        await SendStructuredLogAsync(
            "Debug",
            "Heartbeat",
            $"Heartbeat: IBKR {(ibkrConnected ? "Connected" : "Disconnected")} | {tradingStatus}",
            null,
            html);
    }

    /// <summary>
    /// Polls for pending commands from the Web frontend.
    /// </summary>
    public async Task<List<TradingCommandPayload>> GetPendingCommandsAsync()
    {
        if (!_config.Enabled) return [];

        try
        {
            var response = await _httpClient.GetAsync("/api/marketdata/commands");
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<List<TradingCommandPayload>>() ?? [];
            }
        }
        catch
        {
            // Silent fail
        }

        return [];
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _batchTimer?.Dispose();
        _httpClient.Dispose();
    }

    private sealed class TickPayload
    {
        public string Symbol { get; set; } = "";
        public double Price { get; set; }
        public double Bid { get; set; }
        public double Ask { get; set; }
        public long Volume { get; set; }
    }
}

/// <summary>
/// Position data payload for web frontend.
/// </summary>
public sealed class PositionPayload
{
    public string Symbol { get; set; } = "";
    public decimal Quantity { get; set; }
    public double AvgCost { get; set; }
    public double? MarketPrice { get; set; }
    public double? UnrealizedPnL { get; set; }
}

/// <summary>
/// Order data payload for web frontend.
/// </summary>
public sealed class OrderPayload
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

/// <summary>
/// Trading command received from web frontend.
/// </summary>
public sealed class TradingCommandPayload
{
    public string Type { get; set; } = "";
    public int OrderId { get; set; }
    public string? Symbol { get; set; }
    public DateTimeOffset Timestamp { get; set; }
}
