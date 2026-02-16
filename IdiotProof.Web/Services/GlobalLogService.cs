// ============================================================================
// Global Log Service - Collects logs from Core regardless of active page
// ============================================================================

using Microsoft.AspNetCore.SignalR.Client;

namespace IdiotProof.Web.Services;

/// <summary>
/// Log entry received from Core.
/// </summary>
public sealed class LogEntry
{
    public string Id { get; set; } = "";
    public long Timestamp { get; set; }
    public string Level { get; set; } = "Info";
    public string Category { get; set; } = "System";
    public string Message { get; set; } = "";
    public string? Symbol { get; set; }
    public string? HtmlContent { get; set; }
}

/// <summary>
/// Global singleton service that maintains SignalR connection and collects logs.
/// Logs are collected even when the Log page is not active.
/// </summary>
public sealed class GlobalLogService : IHostedService, IAsyncDisposable
{
    private readonly ILogger<GlobalLogService> _logger;
    private readonly IConfiguration _configuration;
    private HubConnection? _hubConnection;
    private readonly List<LogEntry> _logs = new();
    private readonly object _lock = new();
    private const int MaxLogs = 1000;

    public event Action? OnLogReceived;
    
    public IReadOnlyList<LogEntry> Logs
    {
        get
        {
            lock (_lock)
            {
                return _logs.ToList();
            }
        }
    }

    public bool IsConnected => _hubConnection?.State == HubConnectionState.Connected;

    public GlobalLogService(ILogger<GlobalLogService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var baseUrl = _configuration["WebAppUrl"] ?? "http://localhost:5114";
        
        _hubConnection = new HubConnectionBuilder()
            .WithUrl($"{baseUrl}/hubs/marketdata")
            .WithAutomaticReconnect(new[] { TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10) })
            .Build();

        _hubConnection.On<LogEntry>("ReceiveLogMessage", OnLogMessage);

        _hubConnection.Reconnecting += error =>
        {
            _logger.LogWarning("SignalR reconnecting: {Error}", error?.Message);
            return Task.CompletedTask;
        };

        _hubConnection.Reconnected += connectionId =>
        {
            _logger.LogInformation("SignalR reconnected: {ConnectionId}", connectionId);
            return Task.CompletedTask;
        };

        _hubConnection.Closed += error =>
        {
            _logger.LogWarning("SignalR connection closed: {Error}", error?.Message);
            return Task.CompletedTask;
        };

        try
        {
            await _hubConnection.StartAsync(cancellationToken);
            _logger.LogInformation("GlobalLogService connected to SignalR hub");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect GlobalLogService to SignalR hub");
        }
    }

    private void OnLogMessage(LogEntry log)
    {
        lock (_lock)
        {
            _logs.Add(log);

            // Trim old logs
            while (_logs.Count > MaxLogs)
            {
                _logs.RemoveAt(0);
            }
        }

        // Notify subscribers (Log page)
        OnLogReceived?.Invoke();
    }

    public void AddLog(LogEntry log)
    {
        OnLogMessage(log);
    }

    public void ClearLogs()
    {
        lock (_lock)
        {
            _logs.Clear();
        }
        OnLogReceived?.Invoke();
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("GlobalLogService stopping");
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_hubConnection is not null)
        {
            await _hubConnection.DisposeAsync();
        }
    }
}
