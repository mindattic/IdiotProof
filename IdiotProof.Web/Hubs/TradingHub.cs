// ============================================================================
// TradingHub - SignalR hub for real-time trading updates
// ============================================================================

using Microsoft.AspNetCore.SignalR;
using IdiotProof.Web.Services.MarketScanner;
using IdiotProof.Scripting;

namespace IdiotProof.Web.Hubs;

/// <summary>
/// SignalR hub for real-time trading updates.
/// </summary>
public sealed class TradingHub : Hub
{
    private readonly ILogger<TradingHub> _logger;
    private readonly MarketScannerService _scanner;
    private readonly BreakoutSetupService _setupService;

    public TradingHub(
        ILogger<TradingHub> logger, 
        MarketScannerService scanner,
        BreakoutSetupService setupService)
    {
        _logger = logger;
        _scanner = scanner;
        _setupService = setupService;
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("Client connected: {ConnectionId}", Context.ConnectionId);

        // Send current gapper list to new client
        var candidates = _scanner.GetTopCandidates(20);
        await Clients.Caller.SendAsync("GappersUpdated", candidates);

        // Send current breakout setups
        var setups = _setupService.GetActiveSetups();
        await Clients.Caller.SendAsync("BreakoutSetupsUpdated", setups.Select(s => new
        {
            s.Symbol,
            s.Bias,
            s.ConfidenceScore,
            s.TriggerPrice,
            s.SupportPrice,
            s.CurrentPrice,
            State = s.State.ToString(),
            Targets = s.Targets.Select(t => new { t.Label, t.Price, t.IsHit })
        }));

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

    /// <summary>
    /// Cancels a specific order by ID.
    /// </summary>
    public async Task CancelOrder(int orderId)
    {
        _logger.LogInformation("Client {ConnectionId} requested cancel for order {OrderId}", Context.ConnectionId, orderId);
        // Queue command to be sent to Core
        await Clients.All.SendAsync("OrderCancelRequested", orderId);
    }

    /// <summary>
    /// Cancels all open orders.
    /// </summary>
    public async Task CancelAllOrders()
    {
        _logger.LogInformation("Client {ConnectionId} requested cancel all orders", Context.ConnectionId);
        // Queue command to be sent to Core
        await Clients.All.SendAsync("CancelAllOrdersRequested");
    }

    /// <summary>
    /// Gets all active breakout setups.
    /// </summary>
    public IEnumerable<object> GetBreakoutSetups()
    {
        return _setupService.GetActiveSetups().Select(s => new
        {
            s.Symbol,
            s.CompanyName,
            s.Bias,
            s.Pattern,
            s.ConfidenceScore,
            s.TriggerPrice,
            s.SupportPrice,
            s.InvalidationPrice,
            s.CurrentPrice,
            s.VwapPrice,
            s.GapPercent,
            s.VolumeRatio,
            State = s.State.ToString(),
            Targets = s.Targets.Select(t => new { t.Label, t.Price, t.PercentToSell, t.IsHit }),
            IdiotScript = s.ToIdiotScript()
        });
    }

    /// <summary>
    /// Gets a specific breakout setup by symbol.
    /// </summary>
    public object? GetBreakoutSetup(string symbol)
    {
        var setup = _setupService.GetSetup(symbol);
        if (setup == null) return null;

        return new
        {
            setup.Symbol,
            setup.CompanyName,
            setup.Bias,
            setup.Pattern,
            setup.ConfidenceScore,
            setup.TriggerPrice,
            setup.SupportPrice,
            setup.InvalidationPrice,
            setup.CurrentPrice,
            setup.VwapPrice,
            setup.GapPercent,
            setup.VolumeRatio,
            State = setup.State.ToString(),
            Targets = setup.Targets.Select(t => new { t.Label, t.Price, t.PercentToSell, t.IsHit }),
            IdiotScript = setup.ToIdiotScript(),
            StrategyCard = setup.ToStrategyCard()
        };
    }

    /// <summary>
    /// Triggers a rescan of gappers for breakout setups.
    /// </summary>
    public async Task RescanBreakoutSetups()
    {
        _logger.LogInformation("Client {ConnectionId} requested breakout setup rescan", Context.ConnectionId);
        _setupService.RescanGappers();

        // Send updated setups to the requesting client
        var setups = _setupService.GetActiveSetups();
        await Clients.Caller.SendAsync("BreakoutSetupsUpdated", setups.Select(s => new
        {
            s.Symbol,
            s.Bias,
            s.ConfidenceScore,
            s.TriggerPrice,
            s.CurrentPrice,
            State = s.State.ToString()
        }));
    }

    /// <summary>
    /// Subscribes to breakout setup updates.
    /// </summary>
    public async Task SubscribeToBreakoutSetups()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "breakout-setups");
        _logger.LogDebug("Client {ConnectionId} subscribed to breakout setups", Context.ConnectionId);
    }

    /// <summary>
    /// Requests execution of a confirmed breakout setup.
    /// </summary>
    public async Task ExecuteBreakoutSetup(string symbol)
    {
        var setup = _setupService.GetSetup(symbol);
        if (setup == null)
        {
            await Clients.Caller.SendAsync("ExecutionError", $"No setup found for {symbol}");
            return;
        }

        if (setup.State != SetupState.Confirmed)
        {
            await Clients.Caller.SendAsync("ExecutionError", 
                $"Setup {symbol} is not confirmed (current state: {setup.State})");
            return;
        }

        _logger.LogInformation("Client {ConnectionId} executing breakout setup: {Symbol}", 
            Context.ConnectionId, symbol);

        // Generate the IdiotScript and queue for Core to execute
        var script = setup.ToIdiotScript();

        // Broadcast to Core via command queue
        await Clients.All.SendAsync("ExecuteStrategy", new
        {
            Symbol = symbol,
            Script = script,
            EntryPrice = setup.TriggerPrice,
            StopLoss = setup.InvalidationPrice,
            Targets = setup.Targets.Select(t => t.Price).ToArray(),
            RequestedBy = Context.ConnectionId
        });

        await Clients.Caller.SendAsync("ExecutionQueued", symbol);
    }
}

/// <summary>
/// Service that broadcasts updates to SignalR clients.
/// </summary>
public sealed class TradingHubNotifier
{
    private readonly IHubContext<TradingHub> _hubContext;
    private readonly MarketScannerService _scanner;
    private readonly BreakoutSetupService _setupService;
    private readonly ILogger<TradingHubNotifier> _logger;

    public TradingHubNotifier(
        IHubContext<TradingHub> hubContext,
        MarketScannerService scanner,
        BreakoutSetupService setupService,
        ILogger<TradingHubNotifier> logger)
    {
        _hubContext = hubContext;
        _scanner = scanner;
        _setupService = setupService;
        _logger = logger;

        // Subscribe to scanner events
        _scanner.OnGappersUpdated += OnGappersUpdated;
        _scanner.OnHighConfidenceGapper += OnHighConfidenceGapper;

        // Subscribe to breakout setup events
        _setupService.OnSetupsUpdated += OnSetupsUpdated;
        _setupService.OnSetupStateChanged += OnSetupStateChanged;
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

    private async void OnSetupsUpdated(IReadOnlyList<BreakoutSetup> setups)
    {
        try
        {
            var setupData = setups.Select(s => new
            {
                s.Symbol,
                s.Bias,
                s.ConfidenceScore,
                s.TriggerPrice,
                s.SupportPrice,
                s.CurrentPrice,
                State = s.State.ToString(),
                Targets = s.Targets.Select(t => new { t.Label, t.Price, t.IsHit })
            });

            await _hubContext.Clients.All.SendAsync("BreakoutSetupsUpdated", setupData);
            _logger.LogDebug("Broadcast {Count} breakout setups to all clients", setups.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to broadcast setup update");
        }
    }

    private async void OnSetupStateChanged(BreakoutSetup setup, SetupState newState)
    {
        try
        {
            var stateData = new
            {
                setup.Symbol,
                setup.Bias,
                PreviousState = setup.State.ToString(),
                NewState = newState.ToString(),
                setup.TriggerPrice,
                setup.CurrentPrice,
                setup.ConfidenceScore,
                Timestamp = DateTime.UtcNow
            };

            // Broadcast to all clients
            await _hubContext.Clients.All.SendAsync("SetupStateChanged", stateData);

            // Also notify symbol-specific subscribers
            await _hubContext.Clients.Group($"symbol:{setup.Symbol}")
                .SendAsync("SetupStateChanged", stateData);

            // Log significant state changes
            if (newState == SetupState.Triggered)
            {
                _logger.LogInformation("🚀 TRIGGERED: {Symbol} broke ${Trigger}", 
                    setup.Symbol, setup.TriggerPrice);
            }
            else if (newState == SetupState.Confirmed)
            {
                _logger.LogInformation("✅ CONFIRMED: {Symbol} ready for entry!", setup.Symbol);
            }
            else if (newState == SetupState.Invalidated)
            {
                _logger.LogInformation("❌ INVALIDATED: {Symbol} failed support", setup.Symbol);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to broadcast setup state change");
        }
    }
}
