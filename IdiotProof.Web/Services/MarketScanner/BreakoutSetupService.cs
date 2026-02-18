// ============================================================================
// BreakoutSetupService - Bridges scanner to strategy generation
// ============================================================================
//
// This service:
// 1. Takes gapper candidates from MarketScannerService
// 2. Converts them to ScannerInput for the PremarketSetupScanner
// 3. Generates BreakoutSetup objects with IdiotScript strategies
// 4. Monitors setups through their lifecycle (Watching → Triggered → Confirmed)
//
// The flow:
//   MarketScannerService → GapperCandidate → ScannerInput → BreakoutSetup → IdiotScript
// ============================================================================

using IdiotProof.Scripting;

namespace IdiotProof.Web.Services.MarketScanner;

/// <summary>
/// Service that generates and monitors breakout-pullback setups from gapper data.
/// </summary>
public sealed class BreakoutSetupService : IDisposable
{
    private readonly ILogger<BreakoutSetupService> _logger;
    private readonly MarketScannerService _scannerService;
    private readonly BreakoutSetupPersistence? _persistence;
    private readonly BreakoutSetupAlerts? _alerts;
    private readonly PremarketSetupScanner _setupScanner;
    private readonly Dictionary<string, BreakoutSetup> _activeSetups = new();
    private readonly object _lock = new();
    private readonly Timer _persistTimer;
    private bool _disposed;

    public BreakoutSetupService(
        ILogger<BreakoutSetupService> logger,
        MarketScannerService scannerService,
        BreakoutSetupPersistence? persistence = null,
        BreakoutSetupAlerts? alerts = null)
    {
        _logger = logger;
        _scannerService = scannerService;
        _persistence = persistence;
        _alerts = alerts;
        _setupScanner = new PremarketSetupScanner(new SetupScannerConfig
        {
            MinPrice = 0.30,
            MaxPrice = 25.00,
            MinGapPercent = 3.0,
            MinVolumeRatio = 1.5,
            MinConfidenceScore = 60
        });

        // Subscribe to gapper updates
        _scannerService.OnGappersUpdated += HandleGappersUpdated;

        // Persist setups every 30 seconds
        _persistTimer = new Timer(PersistSetups, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));

        // Load persisted setups on startup
        _ = LoadPersistedSetupsAsync();
    }

    private async Task LoadPersistedSetupsAsync()
    {
        if (_persistence == null) return;

        try
        {
            var setups = await _persistence.LoadActiveSetupsAsync();
            lock (_lock)
            {
                foreach (var setup in setups)
                {
                    _activeSetups[setup.Symbol.ToUpperInvariant()] = setup;
                }
            }
            _logger.LogInformation("Loaded {Count} persisted setups", setups.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load persisted setups");
        }
    }

    private void PersistSetups(object? state)
    {
        if (_persistence == null) return;

        try
        {
            List<BreakoutSetup> setups;
            lock (_lock)
            {
                setups = _activeSetups.Values.ToList();
            }

            _ = _persistence.SaveActiveSetupsAsync(setups);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist setups");
        }
    }
    
    /// <summary>
    /// Event fired when new setups are generated.
    /// </summary>
    public event Action<IReadOnlyList<BreakoutSetup>>? OnSetupsUpdated;
    
    /// <summary>
    /// Event fired when a setup state changes (e.g., Triggered, Confirmed).
    /// </summary>
    public event Action<BreakoutSetup, SetupState>? OnSetupStateChanged;
    
    /// <summary>
    /// Gets current active setups.
    /// </summary>
    public IReadOnlyList<BreakoutSetup> GetActiveSetups()
    {
        lock (_lock)
        {
            return _activeSetups.Values
                .Where(s => s.State != SetupState.Invalidated && s.State != SetupState.Completed)
                .OrderByDescending(s => s.ConfidenceScore)
                .ToList();
        }
    }
    
    /// <summary>
    /// Gets a specific setup by symbol.
    /// </summary>
    public BreakoutSetup? GetSetup(string symbol)
    {
        lock (_lock)
        {
            return _activeSetups.GetValueOrDefault(symbol.ToUpperInvariant());
        }
    }
    
    /// <summary>
    /// Manually triggers a rescan of current gappers.
    /// </summary>
    public void RescanGappers()
    {
        var gappers = _scannerService.GetTopCandidates(50);
        HandleGappersUpdated(gappers);
    }

    /// <summary>
    /// Marks a setup as entered (trade executed).
    /// </summary>
    public bool MarkAsEntered(string symbol, double entryPrice)
    {
        lock (_lock)
        {
            if (!_activeSetups.TryGetValue(symbol.ToUpperInvariant(), out var setup))
                return false;

            if (setup.State != SetupState.Confirmed)
            {
                _logger.LogWarning("[{Symbol}] Cannot mark as entered - not confirmed (state: {State})", 
                    symbol, setup.State);
                return false;
            }

            setup.State = SetupState.Entered;
            setup.EnteredUtc = DateTime.UtcNow;
            setup.ActualEntryPrice = entryPrice;

            _logger.LogInformation("[{Symbol}] ENTERED at ${Price}", symbol, entryPrice);
            OnSetupStateChanged?.Invoke(setup, setup.State);

            return true;
        }
    }

    /// <summary>
    /// Marks a setup as completed (all targets hit or manually closed).
    /// </summary>
    public bool MarkAsCompleted(string symbol, double exitPrice, string reason)
    {
        lock (_lock)
        {
            if (!_activeSetups.TryGetValue(symbol.ToUpperInvariant(), out var setup))
                return false;

            setup.State = SetupState.Completed;
            setup.CompletedUtc = DateTime.UtcNow;
            setup.ActualExitPrice = exitPrice;
            setup.CompletionReason = reason;

            _logger.LogInformation("[{Symbol}] COMPLETED at ${Price} - {Reason}", 
                symbol, exitPrice, reason);
            OnSetupStateChanged?.Invoke(setup, setup.State);

            return true;
        }
    }

    /// <summary>
    /// Gets setup performance statistics.
    /// </summary>
    public SetupStatistics GetStatistics()
    {
        lock (_lock)
        {
            var all = _activeSetups.Values.ToList();
            return new SetupStatistics
            {
                TotalSetups = all.Count,
                Watching = all.Count(s => s.State == SetupState.Watching),
                Triggered = all.Count(s => s.State == SetupState.Triggered),
                PullingBack = all.Count(s => s.State == SetupState.PullingBack),
                Confirmed = all.Count(s => s.State == SetupState.Confirmed),
                Entered = all.Count(s => s.State == SetupState.Entered),
                Completed = all.Count(s => s.State == SetupState.Completed),
                Invalidated = all.Count(s => s.State == SetupState.Invalidated),
                AverageConfidence = all.Count > 0 ? all.Average(s => s.ConfidenceScore) : 0
            };
        }
    }
    
    /// <summary>
    /// Updates setups with current price data.
    /// </summary>
    public void UpdatePrice(string symbol, double price, double vwap)
    {
        BreakoutSetup? setupToAlert = null;
        SetupState previousState = SetupState.Watching;

        lock (_lock)
        {
            if (!_activeSetups.TryGetValue(symbol.ToUpperInvariant(), out var setup))
                return;

            previousState = setup.State;
            setup.CurrentPrice = price;
            setup.VwapPrice = vwap;

            // Update state based on price action
            UpdateSetupState(setup, price, vwap);

            if (setup.State != previousState)
            {
                _logger.LogInformation("[{Symbol}] State changed: {OldState} → {NewState}", 
                    symbol, previousState, setup.State);
                setupToAlert = setup;
                OnSetupStateChanged?.Invoke(setup, setup.State);
            }
        }

        // Send alerts outside of lock
        if (setupToAlert != null)
        {
            _ = HandleStateChangeAsync(setupToAlert, previousState);
        }
    }

    private async Task HandleStateChangeAsync(BreakoutSetup setup, SetupState previousState)
    {
        try
        {
            // Send alert
            if (_alerts != null)
            {
                await _alerts.SendStateChangeAlertAsync(setup, previousState);
            }

            // Archive completed/invalidated setups
            if (_persistence != null && 
                (setup.State == SetupState.Completed || setup.State == SetupState.Invalidated))
            {
                await _persistence.ArchiveSetupAsync(setup);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to handle state change for {Symbol}", setup.Symbol);
        }
    }
    
    private void HandleGappersUpdated(IReadOnlyList<GapperCandidate> gappers)
    {
        // Convert GapperCandidate to ScannerInput
        var inputs = gappers.Select(g => new ScannerInput
        {
            Symbol = g.Symbol,
            CompanyName = g.CompanyName,
            PremarketPrice = g.PremarketPrice,
            PreviousClose = g.PreviousClose,
            PremarketVolume = g.PremarketVolume,
            AverageVolume = g.AverageVolume,
            Catalyst = g.Catalyst,
            SourceCount = g.SourceCount
        }).ToList();
        
        // Scan for setups
        var result = _setupScanner.ScanGappers(inputs);
        
        _logger.LogInformation("Scanned {Total} gappers, found {Qualified} qualified setups",
            result.TotalScanned, result.QualifiedCount);
        
        // Update active setups
        lock (_lock)
        {
            foreach (var setup in result.Setups)
            {
                var key = setup.Symbol.ToUpperInvariant();
                
                if (_activeSetups.TryGetValue(key, out var existing))
                {
                    // Update existing setup (preserve state)
                    existing.CurrentPrice = setup.CurrentPrice;
                    existing.GapPercent = setup.GapPercent;
                    existing.VolumeRatio = setup.VolumeRatio;
                    existing.ConfidenceScore = setup.ConfidenceScore;
                }
                else
                {
                    // Add new setup
                    _activeSetups[key] = setup;
                    _logger.LogInformation("New setup: {Symbol} - {Bias} (Trigger: ${Trigger}, Conf: {Conf}%)",
                        setup.Symbol, setup.Bias, setup.TriggerPrice, setup.ConfidenceScore);
                }
            }
        }
        
        OnSetupsUpdated?.Invoke(GetActiveSetups());
    }
    
    private void UpdateSetupState(BreakoutSetup setup, double price, double vwap)
    {
        switch (setup.State)
        {
            case SetupState.Watching:
                // Check if price breaks above trigger
                if (price > setup.TriggerPrice * 1.005) // 0.5% above trigger
                {
                    setup.State = SetupState.Triggered;
                    setup.TriggeredUtc = DateTime.UtcNow;
                    _logger.LogInformation("[{Symbol}] TRIGGERED at ${Price}", setup.Symbol, price);
                }
                break;
                
            case SetupState.Triggered:
                // Check if pulling back toward trigger/support
                if (price < setup.TriggerPrice * 1.02) // Within 2% of trigger
                {
                    setup.State = SetupState.PullingBack;
                }
                // Check for immediate failure (price dumps below support)
                else if (price < setup.InvalidationPrice)
                {
                    setup.State = SetupState.Invalidated;
                }
                break;
                
            case SetupState.PullingBack:
                // Check confirmation: holding support + above VWAP
                bool holdsSupport = price >= setup.SupportPrice * 0.995;
                bool holdsVwap = vwap > 0 && price >= vwap;
                bool bouncing = price > setup.TriggerPrice; // Back above trigger
                
                if (holdsSupport && (holdsVwap || vwap == 0) && bouncing)
                {
                    setup.State = SetupState.Confirmed;
                    setup.ConfirmedUtc = DateTime.UtcNow;
                    _logger.LogInformation("[{Symbol}] CONFIRMED - Ready to enter!", setup.Symbol);
                }
                // Failed support
                else if (price < setup.InvalidationPrice)
                {
                    setup.State = SetupState.Invalidated;
                    _logger.LogInformation("[{Symbol}] INVALIDATED - Failed support at ${Price}", 
                        setup.Symbol, price);
                }
                break;
                
            case SetupState.Confirmed:
                // Stay confirmed until entry or invalidation
                if (price < setup.InvalidationPrice)
                {
                    setup.State = SetupState.Invalidated;
                }
                break;
                
            case SetupState.Entered:
                // Check targets
                foreach (var target in setup.Targets.Where(t => !t.IsHit))
                {
                    if (price >= target.Price)
                    {
                        target.IsHit = true;
                        _logger.LogInformation("[{Symbol}] HIT {Label} at ${Price}!", 
                            setup.Symbol, target.Label, price);
                    }
                }
                
                if (setup.Targets.All(t => t.IsHit))
                {
                    setup.State = SetupState.Completed;
                }
                else if (price < setup.InvalidationPrice)
                {
                    setup.State = SetupState.Invalidated;
                }
                break;
        }
    }
    
    /// <summary>
    /// Generates IdiotScript strategies for all confirmed setups.
    /// </summary>
    public List<string> GenerateIdiotScripts()
    {
        var scripts = new List<string>();
        
        lock (_lock)
        {
            foreach (var setup in _activeSetups.Values
                .Where(s => s.State == SetupState.Confirmed || s.State == SetupState.Watching))
            {
                scripts.Add(setup.ToIdiotScript());
            }
        }
        
        return scripts;
    }
    
    /// <summary>
    /// Generates strategy cards for all active setups.
    /// </summary>
    public string GenerateStrategyCards()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("╔═══════════════════════════════════════════════════════════════════════════════╗");
        sb.AppendLine("║  BREAKOUT-PULLBACK SETUPS - Generated " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "                ║");
        sb.AppendLine("╚═══════════════════════════════════════════════════════════════════════════════╝");

        lock (_lock)
        {
            foreach (var setup in _activeSetups.Values
                .Where(s => s.State != SetupState.Invalidated && s.State != SetupState.Completed)
                .OrderByDescending(s => s.ConfidenceScore))
            {
                sb.AppendLine(setup.ToStrategyCard());
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Gets historical performance statistics.
    /// </summary>
    public async Task<SetupPerformanceStats?> GetPerformanceStatsAsync(int days = 30)
    {
        if (_persistence == null)
            return null;

        return await _persistence.GetPerformanceStatsAsync(days);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _persistTimer.Dispose();
        _scannerService.OnGappersUpdated -= HandleGappersUpdated;

        // Final persist
        if (_persistence != null)
        {
            List<BreakoutSetup> setups;
            lock (_lock)
            {
                setups = _activeSetups.Values.ToList();
            }
            _persistence.SaveActiveSetupsAsync(setups).GetAwaiter().GetResult();
        }
    }
}
