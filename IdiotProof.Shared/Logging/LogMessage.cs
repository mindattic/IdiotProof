// ============================================================================
// LogMessage - Structured Log Messages for Core → Web Communication
// ============================================================================

namespace IdiotProof.Shared.Logging;

/// <summary>
/// Structured log message that can be rendered with rich formatting in the Web UI.
/// </summary>
public sealed class LogMessage
{
    public required string Id { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public required LogLevel Level { get; init; }
    public required LogCategory Category { get; init; }
    public required string Message { get; init; }
    
    /// <summary>
    /// Optional symbol this log relates to.
    /// </summary>
    public string? Symbol { get; init; }
    
    /// <summary>
    /// Optional structured data (JSON-serializable).
    /// </summary>
    public object? Data { get; init; }
    
    /// <summary>
    /// Optional HTML content for rich formatting.
    /// If null, Message is used with default formatting.
    /// </summary>
    public string? HtmlContent { get; init; }
    
    /// <summary>
    /// Creates a simple info log.
    /// </summary>
    public static LogMessage Info(string message, LogCategory category = LogCategory.System)
        => new()
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            Timestamp = DateTimeOffset.Now,
            Level = LogLevel.Info,
            Category = category,
            Message = message
        };
    
    /// <summary>
    /// Creates a success log (green).
    /// </summary>
    public static LogMessage Success(string message, LogCategory category = LogCategory.System)
        => new()
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            Timestamp = DateTimeOffset.Now,
            Level = LogLevel.Success,
            Category = category,
            Message = message
        };
    
    /// <summary>
    /// Creates a warning log (yellow/orange).
    /// </summary>
    public static LogMessage Warning(string message, LogCategory category = LogCategory.System)
        => new()
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            Timestamp = DateTimeOffset.Now,
            Level = LogLevel.Warning,
            Category = category,
            Message = message
        };
    
    /// <summary>
    /// Creates an error log (red).
    /// </summary>
    public static LogMessage Error(string message, LogCategory category = LogCategory.System)
        => new()
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            Timestamp = DateTimeOffset.Now,
            Level = LogLevel.Error,
            Category = category,
            Message = message
        };
    
    /// <summary>
    /// Creates a trade-related log with symbol highlighting.
    /// </summary>
    public static LogMessage Trade(string symbol, string action, string details, bool isProfit = true)
        => new()
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            Timestamp = DateTimeOffset.Now,
            Level = isProfit ? LogLevel.Success : LogLevel.Warning,
            Category = LogCategory.Trade,
            Symbol = symbol,
            Message = $"{action}: {details}",
            HtmlContent = $"""
                <span class="log-symbol">{symbol}</span>
                <span class="log-action {(isProfit ? "profit" : "loss")}">{action}</span>
                <span class="log-details">{details}</span>
                """
        };
    
    /// <summary>
    /// Creates an order log.
    /// </summary>
    public static LogMessage Order(string symbol, string side, int quantity, string orderType, double? price = null)
    {
        var priceStr = price.HasValue ? $" @ ${price:F2}" : " @ MKT";
        var emoji = side.Equals("BUY", StringComparison.OrdinalIgnoreCase) ? "📈" : "📉";
        
        return new()
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            Timestamp = DateTimeOffset.Now,
            Level = LogLevel.Info,
            Category = LogCategory.Order,
            Symbol = symbol,
            Message = $"{side} {quantity} {symbol}{priceStr}",
            HtmlContent = $"""
                <span class="log-emoji">{emoji}</span>
                <span class="log-symbol">{symbol}</span>
                <span class="log-side log-{side.ToLowerInvariant()}">{side}</span>
                <span class="log-quantity">{quantity}</span>
                <span class="log-price">{priceStr}</span>
                """
        };
    }
    
    /// <summary>
    /// Creates an alert log with high visibility.
    /// </summary>
    public static LogMessage Alert(string symbol, string alertType, double changePercent, int confidence)
    {
        var direction = changePercent >= 0 ? "🚀" : "📉";
        var changeStr = changePercent >= 0 ? $"+{changePercent:F1}%" : $"{changePercent:F1}%";
        
        return new()
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            Timestamp = DateTimeOffset.Now,
            Level = LogLevel.Alert,
            Category = LogCategory.Alert,
            Symbol = symbol,
            Message = $"ALERT: {symbol} {alertType} ({changeStr})",
            HtmlContent = $"""
                <span class="log-alert-icon">{direction}</span>
                <span class="log-alert-type">{alertType}</span>
                <span class="log-symbol highlight">{symbol}</span>
                <span class="log-change {(changePercent >= 0 ? "positive" : "negative")}">{changeStr}</span>
                <span class="log-confidence">Conf: {confidence}%</span>
                """
        };
    }
    
    /// <summary>
    /// Creates a connection status log.
    /// </summary>
    public static LogMessage Connection(bool connected, string service)
    {
        var emoji = connected ? "✅" : "❌";
        var status = connected ? "Connected" : "Disconnected";
        
        return new()
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            Timestamp = DateTimeOffset.Now,
            Level = connected ? LogLevel.Success : LogLevel.Error,
            Category = LogCategory.Connection,
            Message = $"{service}: {status}",
            HtmlContent = $"""
                <span class="log-emoji">{emoji}</span>
                <span class="log-service">{service}</span>
                <span class="log-status {(connected ? "connected" : "disconnected")}">{status}</span>
                """
        };
    }
    
    /// <summary>
    /// Creates an AI/ChatGPT response log.
    /// </summary>
    public static LogMessage AI(string response, string? symbol = null)
        => new()
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            Timestamp = DateTimeOffset.Now,
            Level = LogLevel.Info,
            Category = LogCategory.AI,
            Symbol = symbol,
            Message = response,
            HtmlContent = $"""
                <span class="log-ai-icon">🤖</span>
                <span class="log-ai-response">{System.Net.WebUtility.HtmlEncode(response)}</span>
                """
        };
    
    /// <summary>
    /// Creates a price update log.
    /// </summary>
    public static LogMessage Price(string symbol, double price, double changePercent)
    {
        var arrow = changePercent >= 0 ? "▲" : "▼";
        var changeStr = changePercent >= 0 ? $"+{changePercent:F2}%" : $"{changePercent:F2}%";
        
        return new()
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            Timestamp = DateTimeOffset.Now,
            Level = LogLevel.Debug,
            Category = LogCategory.Price,
            Symbol = symbol,
            Message = $"{symbol}: ${price:F2} ({changeStr})",
            HtmlContent = $"""
                <span class="log-symbol">{symbol}</span>
                <span class="log-price-value">${price:F2}</span>
                <span class="log-arrow {(changePercent >= 0 ? "up" : "down")}">{arrow}</span>
                <span class="log-change {(changePercent >= 0 ? "positive" : "negative")}">{changeStr}</span>
                """
        };
    }
    
    /// <summary>
    /// Creates a heartbeat/status log.
    /// </summary>
    public static LogMessage Heartbeat(bool ibkrConnected, bool tradingActive, int strategyCount)
    {
        var ibkrStatus = ibkrConnected ? "✅ IBKR" : "❌ IBKR";
        var tradingStatus = tradingActive ? $"📊 Trading ({strategyCount})" : "⏸️ Idle";
        
        return new()
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            Timestamp = DateTimeOffset.Now,
            Level = LogLevel.Debug,
            Category = LogCategory.Heartbeat,
            Message = $"Heartbeat: {ibkrStatus} | {tradingStatus}",
            HtmlContent = $"""
                <span class="log-heartbeat-icon">💓</span>
                <span class="log-ibkr-status {(ibkrConnected ? "connected" : "disconnected")}">{ibkrStatus}</span>
                <span class="log-separator">|</span>
                <span class="log-trading-status {(tradingActive ? "active" : "idle")}">{tradingStatus}</span>
                """
        };
    }
}

/// <summary>
/// Log severity levels.
/// </summary>
public enum LogLevel
{
    Debug,      // Gray - verbose info
    Info,       // White - general info
    Success,    // Green - positive outcomes
    Warning,    // Yellow/Orange - caution
    Error,      // Red - errors
    Alert       // Cyan/Pulsing - important alerts
}

/// <summary>
/// Log categories for filtering.
/// </summary>
public enum LogCategory
{
    System,     // Core system messages
    Connection, // IBKR connection status
    Trade,      // Trade executions
    Order,      // Order placements/cancellations
    Alert,      // Price alerts
    Price,      // Price updates
    AI,         // AI/ChatGPT responses
    Heartbeat,  // Status heartbeats
    Strategy,   // Strategy-related
    Backtest    // Backtest results
}
