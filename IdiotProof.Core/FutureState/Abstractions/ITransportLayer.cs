// IdiotProof.Core.FutureState.Abstractions
// Transport Layer Abstraction
// Allows switching between gRPC, REST, WebSocket, or other protocols

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace IdiotProof.Core.FutureState.Abstractions;

/// <summary>
/// Transport protocol type.
/// </summary>
public enum TransportProtocol
{
    /// <summary>gRPC with Protobuf (recommended)</summary>
    Grpc,
    
    /// <summary>REST API with JSON</summary>
    RestJson,
    
    /// <summary>WebSocket for real-time (fallback for browsers)</summary>
    WebSocket,
    
    /// <summary>SignalR (ASP.NET Core real-time)</summary>
    SignalR,
    
    /// <summary>Direct in-process (for testing/same-process scenarios)</summary>
    InProcess
}

/// <summary>
/// Connection state for the transport layer.
/// </summary>
public enum ConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting,
    Failed
}

/// <summary>
/// Transport layer configuration.
/// </summary>
public class TransportConfiguration
{
    /// <summary>
    /// Primary protocol to use.
    /// </summary>
    public TransportProtocol PrimaryProtocol { get; set; } = TransportProtocol.Grpc;
    
    /// <summary>
    /// Fallback protocol if primary fails.
    /// </summary>
    public TransportProtocol? FallbackProtocol { get; set; } = TransportProtocol.RestJson;
    
    /// <summary>
    /// Server base URL (e.g., "https://api.idiotproof.io").
    /// </summary>
    public string ServerUrl { get; set; } = string.Empty;
    
    /// <summary>
    /// gRPC-specific port (often different from REST).
    /// </summary>
    public int GrpcPort { get; set; } = 5001;
    
    /// <summary>
    /// REST/WebSocket port.
    /// </summary>
    public int HttpPort { get; set; } = 5000;
    
    /// <summary>
    /// Connection timeout.
    /// </summary>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(10);
    
    /// <summary>
    /// Request timeout.
    /// </summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);
    
    /// <summary>
    /// Enable automatic reconnection.
    /// </summary>
    public bool AutoReconnect { get; set; } = true;
    
    /// <summary>
    /// Maximum reconnection attempts.
    /// </summary>
    public int MaxReconnectAttempts { get; set; } = 5;
    
    /// <summary>
    /// Delay between reconnection attempts.
    /// </summary>
    public TimeSpan ReconnectDelay { get; set; } = TimeSpan.FromSeconds(2);
    
    /// <summary>
    /// Enable request compression.
    /// </summary>
    public bool EnableCompression { get; set; } = true;
    
    /// <summary>
    /// Keep-alive interval for streaming connections.
    /// </summary>
    public TimeSpan KeepAliveInterval { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// Request metadata (headers, auth, etc.).
/// </summary>
public class RequestMetadata
{
    public string? AuthToken { get; set; }
    public string? ApiKey { get; set; }
    public string? ClientId { get; set; }
    public string? CorrelationId { get; set; }
    public Dictionary<string, string> Headers { get; set; } = new();
    public TimeSpan? Timeout { get; set; }
}

/// <summary>
/// Response from transport layer.
/// </summary>
public class TransportResponse<T>
{
    public bool IsSuccess { get; init; }
    public T? Data { get; init; }
    public int StatusCode { get; init; }
    public string? ErrorMessage { get; init; }
    public string? ErrorCode { get; init; }
    public Dictionary<string, string> Headers { get; init; } = new();
    public TimeSpan Latency { get; init; }
    
    public static TransportResponse<T> Success(T data, int statusCode = 200) =>
        new() { IsSuccess = true, Data = data, StatusCode = statusCode };
        
    public static TransportResponse<T> Failure(string errorMessage, int statusCode = 500, string? errorCode = null) =>
        new() { IsSuccess = false, ErrorMessage = errorMessage, StatusCode = statusCode, ErrorCode = errorCode };
}

/// <summary>
/// Stream subscription for real-time data.
/// </summary>
public interface IStreamSubscription<T> : IAsyncDisposable
{
    /// <summary>
    /// Stream of messages.
    /// </summary>
    IAsyncEnumerable<T> Messages { get; }
    
    /// <summary>
    /// Whether the subscription is active.
    /// </summary>
    bool IsActive { get; }
    
    /// <summary>
    /// Cancels the subscription.
    /// </summary>
    Task CancelAsync();
}

/// <summary>
/// Abstract transport layer interface.
/// Implementations: GrpcTransport, RestTransport, WebSocketTransport
/// </summary>
public interface ITransportLayer : IAsyncDisposable
{
    /// <summary>
    /// Current connection state.
    /// </summary>
    ConnectionState State { get; }
    
    /// <summary>
    /// Current protocol being used.
    /// </summary>
    TransportProtocol CurrentProtocol { get; }
    
    /// <summary>
    /// Event fired when connection state changes.
    /// </summary>
    event EventHandler<ConnectionState>? StateChanged;
    
    /// <summary>
    /// Event fired when an error occurs.
    /// </summary>
    event EventHandler<Exception>? ErrorOccurred;
    
    /// <summary>
    /// Connects to the server.
    /// </summary>
    Task<bool> ConnectAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Disconnects from the server.
    /// </summary>
    Task DisconnectAsync();
    
    /// <summary>
    /// Sends a unary request (request/response).
    /// </summary>
    Task<TransportResponse<TResponse>> SendAsync<TRequest, TResponse>(
        string service,
        string method,
        TRequest request,
        RequestMetadata? metadata = null,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Subscribes to a server stream.
    /// </summary>
    Task<IStreamSubscription<TResponse>> SubscribeAsync<TRequest, TResponse>(
        string service,
        string method,
        TRequest request,
        RequestMetadata? metadata = null,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Sends a client stream and gets a response.
    /// </summary>
    Task<TransportResponse<TResponse>> StreamAsync<TRequest, TResponse>(
        string service,
        string method,
        IAsyncEnumerable<TRequest> requests,
        RequestMetadata? metadata = null,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Bi-directional streaming.
    /// </summary>
    Task<IStreamSubscription<TResponse>> DuplexStreamAsync<TRequest, TResponse>(
        string service,
        string method,
        IAsyncEnumerable<TRequest> requests,
        RequestMetadata? metadata = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Factory for creating transport layers.
/// </summary>
public interface ITransportFactory
{
    /// <summary>
    /// Creates a transport layer with the given configuration.
    /// </summary>
    ITransportLayer Create(TransportConfiguration configuration);
    
    /// <summary>
    /// Creates a transport layer with automatic protocol detection.
    /// </summary>
    Task<ITransportLayer> CreateWithAutoDetectionAsync(
        string serverUrl,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Base implementation with retry and fallback logic.
/// </summary>
public abstract class TransportLayerBase : ITransportLayer
{
    protected readonly TransportConfiguration _config;
    private ConnectionState state = ConnectionState.Disconnected;
    
    public ConnectionState State
    {
        get => state;
        protected set
        {
            if (state != value)
            {
                state = value;
                StateChanged?.Invoke(this, value);
            }
        }
    }
    
    public abstract TransportProtocol CurrentProtocol { get; }
    
    public event EventHandler<ConnectionState>? StateChanged;
    public event EventHandler<Exception>? ErrorOccurred;
    
    protected TransportLayerBase(TransportConfiguration config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }
    
    public abstract Task<bool> ConnectAsync(CancellationToken cancellationToken = default);
    public abstract Task DisconnectAsync();
    
    public abstract Task<TransportResponse<TResponse>> SendAsync<TRequest, TResponse>(
        string service,
        string method,
        TRequest request,
        RequestMetadata? metadata = null,
        CancellationToken cancellationToken = default);
    
    public abstract Task<IStreamSubscription<TResponse>> SubscribeAsync<TRequest, TResponse>(
        string service,
        string method,
        TRequest request,
        RequestMetadata? metadata = null,
        CancellationToken cancellationToken = default);
    
    public abstract Task<TransportResponse<TResponse>> StreamAsync<TRequest, TResponse>(
        string service,
        string method,
        IAsyncEnumerable<TRequest> requests,
        RequestMetadata? metadata = null,
        CancellationToken cancellationToken = default);
    
    public abstract Task<IStreamSubscription<TResponse>> DuplexStreamAsync<TRequest, TResponse>(
        string service,
        string method,
        IAsyncEnumerable<TRequest> requests,
        RequestMetadata? metadata = null,
        CancellationToken cancellationToken = default);
    
    public abstract ValueTask DisposeAsync();
    
    protected void OnError(Exception ex)
    {
        ErrorOccurred?.Invoke(this, ex);
    }
    
    /// <summary>
    /// Executes with retry logic.
    /// </summary>
    protected async Task<T> WithRetryAsync<T>(
        Func<Task<T>> action,
        int maxRetries = 3,
        CancellationToken cancellationToken = default)
    {
        var attempts = 0;
        Exception? lastException = null;
        
        while (attempts < maxRetries)
        {
            try
            {
                return await action().ConfigureAwait(false);
            }
            catch (Exception ex) when (IsRetryable(ex))
            {
                lastException = ex;
                attempts++;
                
                if (attempts < maxRetries)
                {
                    var delay = TimeSpan.FromMilliseconds(100 * Math.Pow(2, attempts));
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        
        throw lastException ?? new InvalidOperationException("Retry failed without exception");
    }
    
    protected virtual bool IsRetryable(Exception ex)
    {
        // Override in implementations for protocol-specific retry logic
        return ex is TimeoutException or IOException;
    }
}

/// <summary>
/// Serialization format for transport.
/// </summary>
public interface ISerializer
{
    byte[] Serialize<T>(T value);
    T Deserialize<T>(byte[] data);
    T Deserialize<T>(Stream stream);
    string ContentType { get; }
}

/// <summary>
/// Protobuf serializer (for gRPC).
/// </summary>
public class ProtobufSerializer : ISerializer
{
    public string ContentType => "application/grpc+proto";
    
    public byte[] Serialize<T>(T value)
    {
        // Use Google.Protobuf serialization
        throw new NotImplementedException("Implement with Google.Protobuf package");
    }
    
    public T Deserialize<T>(byte[] data)
    {
        throw new NotImplementedException("Implement with Google.Protobuf package");
    }
    
    public T Deserialize<T>(Stream stream)
    {
        throw new NotImplementedException("Implement with Google.Protobuf package");
    }
}

/// <summary>
/// JSON serializer (for REST).
/// </summary>
public class JsonSerializer : ISerializer
{
    public string ContentType => "application/json";
    
    public byte[] Serialize<T>(T value)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(value);
        return System.Text.Encoding.UTF8.GetBytes(json);
    }
    
    public T Deserialize<T>(byte[] data)
    {
        var json = System.Text.Encoding.UTF8.GetString(data);
        return System.Text.Json.JsonSerializer.Deserialize<T>(json)!;
    }
    
    public T Deserialize<T>(Stream stream)
    {
        return System.Text.Json.JsonSerializer.Deserialize<T>(stream)!;
    }
}
