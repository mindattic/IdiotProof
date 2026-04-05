// IdiotProof.Core.FutureState
// Unified Service Client
// Single entry point for all IdiotProof.Core functionality across platforms

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IdiotProof.Core.FutureState.Abstractions;
using IdiotProof.Core.FutureState.Contracts;
using IdiotProof.Core.FutureState.CrossPlatform;
using IdiotProof.Core.FutureState.Security;

namespace IdiotProof.Core.FutureState;

/// <summary>
/// Configuration for IdiotProof client.
/// </summary>
public class IdiotProofClientConfiguration
{
    /// <summary>
    /// Server URL (e.g., "https://api.idiotproof.io").
    /// </summary>
    public string ServerUrl { get; set; } = string.Empty;
    
    /// <summary>
    /// API key for authentication (alternative to user/password).
    /// </summary>
    public string? ApiKey { get; set; }
    
    /// <summary>
    /// Preferred transport protocol. Null = auto-detect.
    /// </summary>
    public TransportProtocol? PreferredTransport { get; set; }
    
    /// <summary>
    /// Security configuration.
    /// </summary>
    public SecurityConfiguration Security { get; set; } = new();
    
    /// <summary>
    /// Rate limit configuration.
    /// </summary>
    public RateLimitConfiguration RateLimit { get; set; } = RateLimitPolicies.Default;
    
    /// <summary>
    /// Enable automatic reconnection.
    /// </summary>
    public bool AutoReconnect { get; set; } = true;
    
    /// <summary>
    /// Enable offline mode (cache requests when offline).
    /// </summary>
    public bool EnableOfflineMode { get; set; } = false;
}

/// <summary>
/// Connection status of the client.
/// </summary>
public enum ClientConnectionStatus
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting,
    Offline,
    Error
}

/// <summary>
/// Unified client interface for IdiotProof.Core.
/// This is the main entry point for all frontend applications.
/// </summary>
public interface IIdiotProofClient : IAsyncDisposable
{
    #region Connection & Status
    
    /// <summary>
    /// Current connection status.
    /// </summary>
    ClientConnectionStatus Status { get; }
    
    /// <summary>
    /// Whether the client is connected and ready.
    /// </summary>
    bool IsConnected { get; }
    
    /// <summary>
    /// Event fired when connection status changes.
    /// </summary>
    event EventHandler<ClientConnectionStatus>? StatusChanged;
    
    /// <summary>
    /// Connects to the IdiotProof server.
    /// </summary>
    Task<bool> ConnectAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Disconnects from the server.
    /// </summary>
    Task DisconnectAsync();
    
    #endregion
    
    #region Authentication
    
    /// <summary>
    /// Security provider for authentication operations.
    /// </summary>
    ISecurityProvider Security { get; }
    
    /// <summary>
    /// Authenticates with username and password.
    /// </summary>
    Task<AuthenticationResult> LoginAsync(string username, string password, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Logs out the current user.
    /// </summary>
    Task LogoutAsync(CancellationToken cancellationToken = default);
    
    #endregion
    
    #region Trading
    
    /// <summary>
    /// Places a new order.
    /// </summary>
    Task<ApiResponse<OrderDto>> PlaceOrderAsync(PlaceOrderRequest request, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Cancels an existing order.
    /// </summary>
    Task<ApiResponse<bool>> CancelOrderAsync(string orderId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets orders with optional filtering.
    /// </summary>
    Task<ApiResponse<List<OrderDto>>> GetOrdersAsync(OrderStatus[]? statusFilter = null, string? ticker = null, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Closes an open position.
    /// </summary>
    Task<ApiResponse<OrderDto>> ClosePositionAsync(string positionId, decimal? limitPrice = null, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets all open positions.
    /// </summary>
    Task<ApiResponse<List<PositionDto>>> GetPositionsAsync(string? ticker = null, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets account information.
    /// </summary>
    Task<ApiResponse<AccountDto>> GetAccountAsync(CancellationToken cancellationToken = default);
    
    #endregion
    
    #region Market Data
    
    /// <summary>
    /// Gets a real-time quote.
    /// </summary>
    Task<ApiResponse<QuoteDto>> GetQuoteAsync(string ticker, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets historical bars.
    /// </summary>
    Task<ApiResponse<List<BarDto>>> GetBarsAsync(string ticker, string timeframe = "1m", DateTime? start = null, DateTime? end = null, int? limit = null, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets a full ticker snapshot.
    /// </summary>
    Task<ApiResponse<SnapshotDto>> GetSnapshotAsync(string ticker, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets current indicator values.
    /// </summary>
    Task<ApiResponse<IndicatorsDto>> GetIndicatorsAsync(string ticker, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets market score with optional breakdown.
    /// </summary>
    Task<ApiResponse<MarketScoreDto>> GetMarketScoreAsync(string ticker, bool includeBreakdown = false, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Subscribes to real-time quotes.
    /// </summary>
    Task<IAsyncDisposable> SubscribeQuotesAsync(string[] tickers, Action<QuoteDto> onQuote, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Subscribes to real-time market score updates.
    /// </summary>
    Task<IAsyncDisposable> SubscribeMarketScoreAsync(string[] tickers, Action<MarketScoreDto> onScore, CancellationToken cancellationToken = default);
    
    #endregion
    
    #region Strategies
    
    /// <summary>
    /// Parses an IdiotScript without creating a strategy.
    /// </summary>
    Task<ApiResponse<StrategyDto>> ParseScriptAsync(string script, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Creates a new strategy from IdiotScript.
    /// </summary>
    Task<ApiResponse<StrategyDto>> CreateStrategyAsync(string script, string? name = null, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets a strategy by ID.
    /// </summary>
    Task<ApiResponse<StrategyDto>> GetStrategyAsync(string strategyId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Lists all strategies.
    /// </summary>
    Task<ApiResponse<List<StrategyDto>>> GetStrategiesAsync(StrategyStatus[]? statusFilter = null, string? ticker = null, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Activates a strategy.
    /// </summary>
    Task<ApiResponse<bool>> ActivateStrategyAsync(string strategyId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Pauses a strategy.
    /// </summary>
    Task<ApiResponse<bool>> PauseStrategyAsync(string strategyId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Deletes a strategy.
    /// </summary>
    Task<ApiResponse<bool>> DeleteStrategyAsync(string strategyId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets strategy performance metrics.
    /// </summary>
    Task<ApiResponse<StrategyPerformanceDto>> GetStrategyPerformanceAsync(string strategyId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Subscribes to strategy updates.
    /// </summary>
    Task<IAsyncDisposable> SubscribeStrategyUpdatesAsync(string[] strategyIds, Action<StrategyDto> onUpdate, CancellationToken cancellationToken = default);
    
    #endregion
    
    #region Feature Flags
    
    /// <summary>
    /// Feature flag service.
    /// </summary>
    IFeatureFlagService FeatureFlags { get; }
    
    #endregion
}

/// <summary>
/// Builder for creating IdiotProof clients.
/// </summary>
public class IdiotProofClientBuilder
{
    private readonly IdiotProofClientConfiguration config = new();
    private IPlatformServices? platformServices;
    
    public IdiotProofClientBuilder WithServerUrl(string serverUrl)
    {
        config.ServerUrl = serverUrl;
        return this;
    }
    
    public IdiotProofClientBuilder WithApiKey(string apiKey)
    {
        config.ApiKey = apiKey;
        return this;
    }
    
    public IdiotProofClientBuilder WithTransport(TransportProtocol protocol)
    {
        config.PreferredTransport = protocol;
        return this;
    }
    
    public IdiotProofClientBuilder WithAutoReconnect(bool enable = true)
    {
        config.AutoReconnect = enable;
        return this;
    }
    
    public IdiotProofClientBuilder WithOfflineMode(bool enable = true)
    {
        config.EnableOfflineMode = enable;
        return this;
    }
    
    public IdiotProofClientBuilder WithPlatformServices(IPlatformServices platformServices)
    {
        this.platformServices = platformServices;
        return this;
    }
    
    public IdiotProofClientBuilder WithRateLimiting(RateLimitConfiguration config)
    {
        this.config.RateLimit = config;
        return this;
    }

    public IdiotProofClientBuilder WithSecurityConfiguration(SecurityConfiguration config)
    {
        this.config.Security = config;
        return this;
    }
    
    public IIdiotProofClient Build()
    {
        if (string.IsNullOrEmpty(config.ServerUrl))
            throw new InvalidOperationException("Server URL is required. Call WithServerUrl().");
        
        // Use platform-detected transport if not specified
        config.PreferredTransport ??= PlatformConfiguration.RecommendedTransport;
        
        // Register platform services if provided
        if (platformServices != null && !PlatformServiceRegistry.IsInitialized)
        {
            PlatformServiceRegistry.Initialize(platformServices);
        }
        
        return new IdiotProofClient(config);
    }
}

/// <summary>
/// Default implementation of IIdiotProofClient.
/// </summary>
internal class IdiotProofClient : IIdiotProofClient
{
    private readonly IdiotProofClientConfiguration config;
    private readonly IFeatureFlagService featureFlags;
    private readonly IRateLimiter rateLimiter;
    private ClientConnectionStatus status = ClientConnectionStatus.Disconnected;
    
    // These would be implemented with actual transport
    public ClientConnectionStatus Status => status;
    public bool IsConnected => status == ClientConnectionStatus.Connected;
    public event EventHandler<ClientConnectionStatus>? StatusChanged;
    
    public ISecurityProvider Security => throw new NotImplementedException("Implement with actual security provider");
    public IFeatureFlagService FeatureFlags => featureFlags;
    
    internal IdiotProofClient(IdiotProofClientConfiguration config)
    {
        this.config = config;
        featureFlags = new FeatureFlagService();
        rateLimiter = new RateLimiter(config.RateLimit);
    }
    
    public Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
    {
        SetStatus(ClientConnectionStatus.Connecting);
        // Implement actual connection logic based on transport
        SetStatus(ClientConnectionStatus.Connected);
        return Task.FromResult(true);
    }
    
    public Task DisconnectAsync()
    {
        SetStatus(ClientConnectionStatus.Disconnected);
        return Task.CompletedTask;
    }
    
    private void SetStatus(ClientConnectionStatus status)
    {
        if (status != status)
        {
            this.status = status;
            StatusChanged?.Invoke(this, status);
        }
    }
    
    public ValueTask DisposeAsync()
    {
        return new ValueTask(DisconnectAsync());
    }
    
    // All API methods would be implemented here using the transport layer
    // For brevity, showing stub implementations
    
    public Task<AuthenticationResult> LoginAsync(string username, string password, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
    
    public Task LogoutAsync(CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
    
    public Task<ApiResponse<OrderDto>> PlaceOrderAsync(PlaceOrderRequest request, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
    
    public Task<ApiResponse<bool>> CancelOrderAsync(string orderId, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
    
    public Task<ApiResponse<List<OrderDto>>> GetOrdersAsync(OrderStatus[]? statusFilter = null, string? ticker = null, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
    
    public Task<ApiResponse<OrderDto>> ClosePositionAsync(string positionId, decimal? limitPrice = null, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
    
    public Task<ApiResponse<List<PositionDto>>> GetPositionsAsync(string? ticker = null, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
    
    public Task<ApiResponse<AccountDto>> GetAccountAsync(CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
    
    public Task<ApiResponse<QuoteDto>> GetQuoteAsync(string ticker, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
    
    public Task<ApiResponse<List<BarDto>>> GetBarsAsync(string ticker, string timeframe = "1m", DateTime? start = null, DateTime? end = null, int? limit = null, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
    
    public Task<ApiResponse<SnapshotDto>> GetSnapshotAsync(string ticker, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
    
    public Task<ApiResponse<IndicatorsDto>> GetIndicatorsAsync(string ticker, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
    
    public Task<ApiResponse<MarketScoreDto>> GetMarketScoreAsync(string ticker, bool includeBreakdown = false, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
    
    public Task<IAsyncDisposable> SubscribeQuotesAsync(string[] tickers, Action<QuoteDto> onQuote, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
    
    public Task<IAsyncDisposable> SubscribeMarketScoreAsync(string[] tickers, Action<MarketScoreDto> onScore, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
    
    public Task<ApiResponse<StrategyDto>> ParseScriptAsync(string script, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
    
    public Task<ApiResponse<StrategyDto>> CreateStrategyAsync(string script, string? name = null, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
    
    public Task<ApiResponse<StrategyDto>> GetStrategyAsync(string strategyId, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
    
    public Task<ApiResponse<List<StrategyDto>>> GetStrategiesAsync(StrategyStatus[]? statusFilter = null, string? ticker = null, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
    
    public Task<ApiResponse<bool>> ActivateStrategyAsync(string strategyId, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
    
    public Task<ApiResponse<bool>> PauseStrategyAsync(string strategyId, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
    
    public Task<ApiResponse<bool>> DeleteStrategyAsync(string strategyId, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
    
    public Task<ApiResponse<StrategyPerformanceDto>> GetStrategyPerformanceAsync(string strategyId, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
    
    public Task<IAsyncDisposable> SubscribeStrategyUpdatesAsync(string[] strategyIds, Action<StrategyDto> onUpdate, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
}

/// <summary>
/// Static factory for quick client creation.
/// </summary>
public static class IdiotProof
{
    /// <summary>
    /// Creates a new client builder.
    /// </summary>
    public static IdiotProofClientBuilder CreateClient() => new();
    
    /// <summary>
    /// Creates a client with default configuration.
    /// </summary>
    public static IIdiotProofClient CreateClient(string serverUrl, string? apiKey = null)
    {
        var builder = new IdiotProofClientBuilder()
            .WithServerUrl(serverUrl);
            
        if (!string.IsNullOrEmpty(apiKey))
            builder.WithApiKey(apiKey);
            
        return builder.Build();
    }
}
