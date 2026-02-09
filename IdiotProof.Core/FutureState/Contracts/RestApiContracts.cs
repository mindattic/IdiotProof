// IdiotProof.Core.FutureState.Contracts
// REST API Contracts (Fallback when gRPC is not available)
// Used for: React, Angular, Vue, Blazor WASM, older browsers

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace IdiotProof.Core.FutureState.Contracts;

// =============================================================================
// API RESPONSE WRAPPER
// =============================================================================

/// <summary>
/// Standard API response wrapper.
/// All REST endpoints return this format.
/// </summary>
/// <typeparam name="T">Type of data payload</typeparam>
public class ApiResponse<T>
{
    [JsonPropertyName("success")]
    public bool Success { get; init; }
    
    [JsonPropertyName("data")]
    public T? Data { get; init; }
    
    [JsonPropertyName("error")]
    public ApiError? Error { get; init; }
    
    [JsonPropertyName("meta")]
    public ApiMetadata? Meta { get; init; }
    
    public static ApiResponse<T> Ok(T data, ApiMetadata? meta = null) =>
        new() { Success = true, Data = data, Meta = meta };
        
    public static ApiResponse<T> Fail(string code, string message, string? details = null) =>
        new() { Success = false, Error = new ApiError { Code = code, Message = message, Details = details } };
}

public class ApiError
{
    [JsonPropertyName("code")]
    public string Code { get; init; } = string.Empty;
    
    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;
    
    [JsonPropertyName("details")]
    public string? Details { get; init; }
    
    [JsonPropertyName("field")]
    public string? Field { get; init; }  // For validation errors
    
    [JsonPropertyName("innerErrors")]
    public List<ApiError>? InnerErrors { get; init; }
}

public class ApiMetadata
{
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    
    [JsonPropertyName("requestId")]
    public string? RequestId { get; init; }
    
    [JsonPropertyName("pagination")]
    public PaginationInfo? Pagination { get; init; }
    
    [JsonPropertyName("rateLimit")]
    public RateLimitInfo? RateLimit { get; init; }
}

public class PaginationInfo
{
    [JsonPropertyName("page")]
    public int Page { get; init; }
    
    [JsonPropertyName("pageSize")]
    public int PageSize { get; init; }
    
    [JsonPropertyName("totalItems")]
    public int TotalItems { get; init; }
    
    [JsonPropertyName("totalPages")]
    public int TotalPages { get; init; }
    
    [JsonPropertyName("hasNext")]
    public bool HasNext { get; init; }
    
    [JsonPropertyName("hasPrevious")]
    public bool HasPrevious { get; init; }
}

public class RateLimitInfo
{
    [JsonPropertyName("limit")]
    public int Limit { get; init; }
    
    [JsonPropertyName("remaining")]
    public int Remaining { get; init; }
    
    [JsonPropertyName("resetsAt")]
    public DateTime ResetsAt { get; init; }
}

// =============================================================================
// TRADING CONTRACTS
// =============================================================================

/// <summary>
/// POST /api/v1/orders
/// </summary>
public class PlaceOrderRequest
{
    [JsonPropertyName("ticker")]
    public string Ticker { get; init; } = string.Empty;
    
    [JsonPropertyName("direction")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public OrderDirection Direction { get; init; }
    
    [JsonPropertyName("orderType")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public OrderType OrderType { get; init; } = OrderType.Market;
    
    [JsonPropertyName("quantity")]
    public int Quantity { get; init; }
    
    [JsonPropertyName("limitPrice")]
    public decimal? LimitPrice { get; init; }
    
    [JsonPropertyName("stopPrice")]
    public decimal? StopPrice { get; init; }
    
    [JsonPropertyName("takeProfit")]
    public decimal? TakeProfit { get; init; }
    
    [JsonPropertyName("stopLoss")]
    public decimal? StopLoss { get; init; }
    
    [JsonPropertyName("trailingStopPercent")]
    public decimal? TrailingStopPercent { get; init; }
    
    [JsonPropertyName("session")]
    public string? Session { get; init; }
    
    [JsonPropertyName("outsideRth")]
    public bool OutsideRth { get; init; } = true;
    
    [JsonPropertyName("strategyId")]
    public string? StrategyId { get; init; }
    
    [JsonPropertyName("idempotencyKey")]
    public string? IdempotencyKey { get; init; }
}

public class OrderDto
{
    [JsonPropertyName("orderId")]
    public string OrderId { get; init; } = string.Empty;
    
    [JsonPropertyName("ticker")]
    public string Ticker { get; init; } = string.Empty;
    
    [JsonPropertyName("direction")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public OrderDirection Direction { get; init; }
    
    [JsonPropertyName("orderType")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public OrderType OrderType { get; init; }
    
    [JsonPropertyName("quantity")]
    public int Quantity { get; init; }
    
    [JsonPropertyName("limitPrice")]
    public decimal? LimitPrice { get; init; }
    
    [JsonPropertyName("stopPrice")]
    public decimal? StopPrice { get; init; }
    
    [JsonPropertyName("status")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public OrderStatus Status { get; init; }
    
    [JsonPropertyName("fillPrice")]
    public decimal? FillPrice { get; init; }
    
    [JsonPropertyName("filledQuantity")]
    public int FilledQuantity { get; init; }
    
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; init; }
    
    [JsonPropertyName("updatedAt")]
    public DateTime? UpdatedAt { get; init; }
    
    [JsonPropertyName("filledAt")]
    public DateTime? FilledAt { get; init; }
    
    [JsonPropertyName("rejectionReason")]
    public string? RejectionReason { get; init; }
}

public class PositionDto
{
    [JsonPropertyName("positionId")]
    public string PositionId { get; init; } = string.Empty;
    
    [JsonPropertyName("ticker")]
    public string Ticker { get; init; } = string.Empty;
    
    [JsonPropertyName("direction")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public OrderDirection Direction { get; init; }
    
    [JsonPropertyName("quantity")]
    public int Quantity { get; init; }
    
    [JsonPropertyName("entryPrice")]
    public decimal EntryPrice { get; init; }
    
    [JsonPropertyName("currentPrice")]
    public decimal CurrentPrice { get; init; }
    
    [JsonPropertyName("unrealizedPnl")]
    public decimal UnrealizedPnl { get; init; }
    
    [JsonPropertyName("unrealizedPnlPercent")]
    public decimal UnrealizedPnlPercent { get; init; }
    
    [JsonPropertyName("takeProfit")]
    public decimal? TakeProfit { get; init; }
    
    [JsonPropertyName("stopLoss")]
    public decimal? StopLoss { get; init; }
    
    [JsonPropertyName("trailingStop")]
    public decimal? TrailingStop { get; init; }
    
    [JsonPropertyName("openedAt")]
    public DateTime OpenedAt { get; init; }
}

public class AccountDto
{
    [JsonPropertyName("accountId")]
    public string AccountId { get; init; } = string.Empty;
    
    [JsonPropertyName("cashBalance")]
    public decimal CashBalance { get; init; }
    
    [JsonPropertyName("buyingPower")]
    public decimal BuyingPower { get; init; }
    
    [JsonPropertyName("portfolioValue")]
    public decimal PortfolioValue { get; init; }
    
    [JsonPropertyName("dayTradeCount")]
    public int DayTradeCount { get; init; }
    
    [JsonPropertyName("patternDayTrader")]
    public bool PatternDayTrader { get; init; }
    
    [JsonPropertyName("positions")]
    public List<PositionDto> Positions { get; init; } = new();
    
    [JsonPropertyName("openOrders")]
    public List<OrderDto> OpenOrders { get; init; } = new();
    
    [JsonPropertyName("asOf")]
    public DateTime AsOf { get; init; }
}

// =============================================================================
// MARKET DATA CONTRACTS
// =============================================================================

public class QuoteDto
{
    [JsonPropertyName("ticker")]
    public string Ticker { get; init; } = string.Empty;
    
    [JsonPropertyName("bid")]
    public decimal Bid { get; init; }
    
    [JsonPropertyName("ask")]
    public decimal Ask { get; init; }
    
    [JsonPropertyName("bidSize")]
    public long BidSize { get; init; }
    
    [JsonPropertyName("askSize")]
    public long AskSize { get; init; }
    
    [JsonPropertyName("last")]
    public decimal Last { get; init; }
    
    [JsonPropertyName("lastSize")]
    public long LastSize { get; init; }
    
    [JsonPropertyName("volume")]
    public long Volume { get; init; }
    
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; init; }
}

public class BarDto
{
    [JsonPropertyName("ticker")]
    public string Ticker { get; init; } = string.Empty;
    
    [JsonPropertyName("open")]
    public decimal Open { get; init; }
    
    [JsonPropertyName("high")]
    public decimal High { get; init; }
    
    [JsonPropertyName("low")]
    public decimal Low { get; init; }
    
    [JsonPropertyName("close")]
    public decimal Close { get; init; }
    
    [JsonPropertyName("volume")]
    public long Volume { get; init; }
    
    [JsonPropertyName("vwap")]
    public decimal Vwap { get; init; }
    
    [JsonPropertyName("tradeCount")]
    public int TradeCount { get; init; }
    
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; init; }
    
    [JsonPropertyName("timeframe")]
    public string Timeframe { get; init; } = "1m";
}

public class SnapshotDto
{
    [JsonPropertyName("ticker")]
    public string Ticker { get; init; } = string.Empty;
    
    [JsonPropertyName("name")]
    public string? Name { get; init; }
    
    [JsonPropertyName("lastPrice")]
    public decimal LastPrice { get; init; }
    
    [JsonPropertyName("change")]
    public decimal Change { get; init; }
    
    [JsonPropertyName("changePercent")]
    public decimal ChangePercent { get; init; }
    
    [JsonPropertyName("open")]
    public decimal Open { get; init; }
    
    [JsonPropertyName("high")]
    public decimal High { get; init; }
    
    [JsonPropertyName("low")]
    public decimal Low { get; init; }
    
    [JsonPropertyName("previousClose")]
    public decimal PreviousClose { get; init; }
    
    [JsonPropertyName("volume")]
    public long Volume { get; init; }
    
    [JsonPropertyName("vwap")]
    public decimal Vwap { get; init; }
    
    [JsonPropertyName("marketStatus")]
    public string MarketStatus { get; init; } = string.Empty;
    
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; init; }
}

public class IndicatorsDto
{
    [JsonPropertyName("ticker")]
    public string Ticker { get; init; } = string.Empty;
    
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; init; }
    
    [JsonPropertyName("ema9")]
    public decimal? Ema9 { get; init; }
    
    [JsonPropertyName("ema21")]
    public decimal? Ema21 { get; init; }
    
    [JsonPropertyName("ema50")]
    public decimal? Ema50 { get; init; }
    
    [JsonPropertyName("ema200")]
    public decimal? Ema200 { get; init; }
    
    [JsonPropertyName("vwap")]
    public decimal? Vwap { get; init; }
    
    [JsonPropertyName("rsi")]
    public decimal? Rsi { get; init; }
    
    [JsonPropertyName("macd")]
    public decimal? Macd { get; init; }
    
    [JsonPropertyName("macdSignal")]
    public decimal? MacdSignal { get; init; }
    
    [JsonPropertyName("macdHistogram")]
    public decimal? MacdHistogram { get; init; }
    
    [JsonPropertyName("adx")]
    public decimal? Adx { get; init; }
    
    [JsonPropertyName("plusDi")]
    public decimal? PlusDi { get; init; }
    
    [JsonPropertyName("minusDi")]
    public decimal? MinusDi { get; init; }
    
    [JsonPropertyName("atr")]
    public decimal? Atr { get; init; }
    
    [JsonPropertyName("volumeRatio")]
    public decimal? VolumeRatio { get; init; }
    
    [JsonPropertyName("marketScore")]
    public decimal? MarketScore { get; init; }
}

public class MarketScoreDto
{
    [JsonPropertyName("ticker")]
    public string Ticker { get; init; } = string.Empty;
    
    [JsonPropertyName("totalScore")]
    public decimal TotalScore { get; init; }
    
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; init; }
    
    [JsonPropertyName("breakdown")]
    public MarketScoreBreakdownDto? Breakdown { get; init; }
    
    [JsonPropertyName("signal")]
    public string Signal { get; init; } = string.Empty;  // "STRONG_BUY", "BUY", "NEUTRAL", "SELL", "STRONG_SELL"
    
    [JsonPropertyName("reasoning")]
    public string? Reasoning { get; init; }
}

public class MarketScoreBreakdownDto
{
    [JsonPropertyName("vwapScore")]
    public decimal VwapScore { get; init; }
    
    [JsonPropertyName("emaScore")]
    public decimal EmaScore { get; init; }
    
    [JsonPropertyName("rsiScore")]
    public decimal RsiScore { get; init; }
    
    [JsonPropertyName("macdScore")]
    public decimal MacdScore { get; init; }
    
    [JsonPropertyName("adxScore")]
    public decimal AdxScore { get; init; }
    
    [JsonPropertyName("volumeScore")]
    public decimal VolumeScore { get; init; }
}

// =============================================================================
// STRATEGY CONTRACTS
// =============================================================================

public class CreateStrategyRequest
{
    [JsonPropertyName("rawScript")]
    public string RawScript { get; init; } = string.Empty;
    
    [JsonPropertyName("name")]
    public string? Name { get; init; }
}

public class StrategyDto
{
    [JsonPropertyName("strategyId")]
    public string StrategyId { get; init; } = string.Empty;
    
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;
    
    [JsonPropertyName("ticker")]
    public string Ticker { get; init; } = string.Empty;
    
    [JsonPropertyName("rawScript")]
    public string RawScript { get; init; } = string.Empty;
    
    [JsonPropertyName("quantity")]
    public int Quantity { get; init; }
    
    [JsonPropertyName("session")]
    public string Session { get; init; } = "RTH";
    
    [JsonPropertyName("direction")]
    public string Direction { get; init; } = "LONG";
    
    [JsonPropertyName("status")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public StrategyStatus Status { get; init; }
    
    [JsonPropertyName("conditions")]
    public List<ConditionDto> Conditions { get; init; } = new();
    
    [JsonPropertyName("exits")]
    public List<ExitDto> Exits { get; init; } = new();
    
    [JsonPropertyName("adaptiveOrder")]
    public bool AdaptiveOrder { get; init; }
    
    [JsonPropertyName("autonomousTrading")]
    public bool AutonomousTrading { get; init; }
    
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; init; }
    
    [JsonPropertyName("lastTriggeredAt")]
    public DateTime? LastTriggeredAt { get; init; }
    
    [JsonPropertyName("triggerCount")]
    public int TriggerCount { get; init; }
}

public class ConditionDto
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;
    
    [JsonPropertyName("value")]
    public decimal? Value { get; init; }
    
    [JsonPropertyName("value2")]
    public decimal? Value2 { get; init; }
    
    [JsonPropertyName("isMet")]
    public bool IsMet { get; init; }
    
    [JsonPropertyName("displayName")]
    public string DisplayName { get; init; } = string.Empty;
}

public class ExitDto
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;  // "TakeProfit", "StopLoss", "TrailingStop", "TimeExit"
    
    [JsonPropertyName("value")]
    public decimal? Value { get; init; }
    
    [JsonPropertyName("trailingPercent")]
    public decimal? TrailingPercent { get; init; }
    
    [JsonPropertyName("timeValue")]
    public string? TimeValue { get; init; }
    
    [JsonPropertyName("ifProfitable")]
    public bool IfProfitable { get; init; }
}

public class StrategyPerformanceDto
{
    [JsonPropertyName("strategyId")]
    public string StrategyId { get; init; } = string.Empty;
    
    [JsonPropertyName("totalTrades")]
    public int TotalTrades { get; init; }
    
    [JsonPropertyName("winningTrades")]
    public int WinningTrades { get; init; }
    
    [JsonPropertyName("losingTrades")]
    public int LosingTrades { get; init; }
    
    [JsonPropertyName("winRate")]
    public decimal WinRate { get; init; }
    
    [JsonPropertyName("totalPnl")]
    public decimal TotalPnl { get; init; }
    
    [JsonPropertyName("averagePnl")]
    public decimal AveragePnl { get; init; }
    
    [JsonPropertyName("largestWin")]
    public decimal LargestWin { get; init; }
    
    [JsonPropertyName("largestLoss")]
    public decimal LargestLoss { get; init; }
    
    [JsonPropertyName("profitFactor")]
    public decimal ProfitFactor { get; init; }
    
    [JsonPropertyName("sharpeRatio")]
    public decimal SharpeRatio { get; init; }
    
    [JsonPropertyName("maxDrawdown")]
    public decimal MaxDrawdown { get; init; }
    
    [JsonPropertyName("calculatedAt")]
    public DateTime CalculatedAt { get; init; }
}

// =============================================================================
// ENUMS
// =============================================================================

public enum OrderDirection
{
    Long,
    Short
}

public enum OrderType
{
    Market,
    Limit,
    Stop,
    StopLimit
}

public enum OrderStatus
{
    Pending,
    Submitted,
    Filled,
    PartiallyFilled,
    Cancelled,
    Rejected,
    Expired
}

public enum StrategyStatus
{
    Inactive,
    Active,
    Paused,
    Completed,
    Error
}

// =============================================================================
// REST API ENDPOINT DOCUMENTATION
// =============================================================================

/// <summary>
/// REST API endpoint definitions.
/// Use this as reference for implementing frontend clients.
/// </summary>
public static class ApiEndpoints
{
    public const string BaseUrl = "/api/v1";
    
    // Authentication
    public const string Login = "/auth/login";              // POST
    public const string Logout = "/auth/logout";            // POST
    public const string Refresh = "/auth/refresh";          // POST
    public const string Register = "/auth/register";        // POST
    
    // Account
    public const string GetAccount = "/account";            // GET
    public const string GetPositions = "/account/positions"; // GET
    
    // Orders
    public const string PlaceOrder = "/orders";             // POST
    public const string GetOrders = "/orders";              // GET
    public const string GetOrder = "/orders/{orderId}";     // GET
    public const string CancelOrder = "/orders/{orderId}";  // DELETE
    
    // Positions
    public const string ClosePosition = "/positions/{positionId}/close"; // POST
    
    // Market Data
    public const string GetQuote = "/market/{ticker}/quote";       // GET
    public const string GetBars = "/market/{ticker}/bars";         // GET
    public const string GetSnapshot = "/market/{ticker}/snapshot"; // GET
    public const string GetIndicators = "/market/{ticker}/indicators"; // GET
    public const string GetMarketScore = "/market/{ticker}/score"; // GET
    
    // Strategies
    public const string CreateStrategy = "/strategies";            // POST
    public const string GetStrategies = "/strategies";             // GET
    public const string GetStrategy = "/strategies/{strategyId}";  // GET
    public const string DeleteStrategy = "/strategies/{strategyId}"; // DELETE
    public const string ActivateStrategy = "/strategies/{strategyId}/activate"; // POST
    public const string PauseStrategy = "/strategies/{strategyId}/pause"; // POST
    public const string GetPerformance = "/strategies/{strategyId}/performance"; // GET
    
    // Script Parsing
    public const string ParseScript = "/scripts/parse";            // POST
    public const string ValidateScript = "/scripts/validate";      // POST
    
    // WebSocket endpoints for real-time data
    public const string QuotesStream = "/ws/quotes";               // WS
    public const string TradesStream = "/ws/trades";               // WS
    public const string BarsStream = "/ws/bars";                   // WS
    public const string OrderUpdatesStream = "/ws/orders";         // WS
    public const string StrategyUpdatesStream = "/ws/strategies";  // WS
}
