// ============================================================================
// TradingHub - SignalR hub for real-time trading updates
// ============================================================================

using Microsoft.AspNetCore.SignalR;
using IdiotProof.Web.Services.MarketScanner;

namespace IdiotProof.Web.Hubs;

/// <summary>
/// SignalR hub for real-time trading updates.
/// </summary>
public sealed class TradingHub : Hub
{
    private readonly ILogger<TradingHub> _logger;
    private readonly MarketScannerService _scanner;
    
    public TradingHub(ILogger<TradingHub> logger, MarketScannerService scanner)
    {
        _logger = logger;
        _scanner = scanner;
    }
    
    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("Client connected: {ConnectionId}", Context.ConnectionId);
        
        // Send current gapper list to new client
        var candidates = _scanner.GetTopCandidates(20);
        await Clients.Caller.SendAsync("GappersUpdated", candidates);
        
        await base.OnConnectedAsync();
    }
    
    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Client disconnected: {ConnectionId}", Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
    
    /// <summary>
    /// Client requests a refresh of gapper data.
    /// </summary>
    public async Task RequestRefresh()
    {
        _logger.LogInformation("Client {ConnectionId} requested refresh", Context.ConnectionId);
        await _scanner.TriggerScanAsync();
    }
    
    /// <summary>
    /// Client subscribes to updates for a specific symbol.
    /// </summary>
    public async Task SubscribeToSymbol(string symbol)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"symbol:{symbol.ToUpperInvariant()}");
        _logger.LogDebug("Client {ConnectionId} subscribed to {Symbol}", Context.ConnectionId, symbol);
    }
    
    /// <summary>
    /// Client unsubscribes from a specific symbol.
    /// </summary>
    public async Task UnsubscribeFromSymbol(string symbol)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"symbol:{symbol.ToUpperInvariant()}");
    }
    
    /// <summary>
    /// Gets details for a specific gapper candidate.
    /// </summary>
    public GapperCandidate? GetGapperDetails(string symbol)
    {
        return _scanner.GetCandidate(symbol);
    }
    
    /// <summary>
    /// Gets current scanner statistics.
    /// </summary>
    public ScanStatistics GetStatistics()
    {
        return _scanner.GetStatistics();
    }
}

/// <summary>
/// Service that broadcasts updates to SignalR clients.
/// </summary>
public sealed class TradingHubNotifier
{
    private readonly IHubContext<TradingHub> _hubContext;
    private readonly MarketScannerService _scanner;
    private readonly ILogger<TradingHubNotifier> _logger;
    
    public TradingHubNotifier(
        IHubContext<TradingHub> hubContext,
        MarketScannerService scanner,
        ILogger<TradingHubNotifier> logger)
    {
        _hubContext = hubContext;
        _scanner = scanner;
        _logger = logger;
        
        // Subscribe to scanner events
        _scanner.OnGappersUpdated += OnGappersUpdated;
        _scanner.OnHighConfidenceGapper += OnHighConfidenceGapper;
    }
    
    private async void OnGappersUpdated(IReadOnlyList<GapperCandidate> candidates)
    {
        try
        {
            await _hubContext.Clients.All.SendAsync("GappersUpdated", candidates);
            _logger.LogDebug("Broadcast {Count} gappers to all clients", candidates.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to broadcast gapper update");
        }
    }
    
    private async void OnHighConfidenceGapper(GapperCandidate candidate)
    {
        try
        {
            // Broadcast alert to all clients
            await _hubContext.Clients.All.SendAsync("GapperAlert", candidate);
            
            // Also notify subscribers to this specific symbol
            await _hubContext.Clients.Group($"symbol:{candidate.Symbol}")
                .SendAsync("SymbolUpdate", candidate);
            
            _logger.LogInformation("Alert broadcast: {Symbol} {Gap:+0.0;-0.0}%", 
                candidate.Symbol, candidate.GapPercent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to broadcast gapper alert");
        }
    }
    
    /// <summary>
    /// Broadcasts a price update for a specific symbol.
    /// </summary>
    public async Task BroadcastPriceAsync(string symbol, double price, double change)
    {
        await _hubContext.Clients.Group($"symbol:{symbol.ToUpperInvariant()}")
            .SendAsync("PriceUpdate", new { Symbol = symbol, Price = price, Change = change });
    }
}
