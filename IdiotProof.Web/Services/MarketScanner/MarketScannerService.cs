// ============================================================================
// MarketScannerService - Background service that continuously scans for gappers
// ============================================================================

namespace IdiotProof.Web.Services.MarketScanner;

/// <summary>
/// Background service that continuously scans multiple sources for gappers.
/// </summary>
public sealed class MarketScannerService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<MarketScannerService> _logger;
    private readonly GapperAggregator _aggregator;
    
    private DateTime _lastFullScan = DateTime.MinValue;
    private readonly TimeSpan _fullScanInterval = TimeSpan.FromMinutes(1);
    
    public MarketScannerService(
        IServiceProvider services,
        ILogger<MarketScannerService> logger,
        GapperAggregator aggregator)
    {
        _services = services;
        _logger = logger;
        _aggregator = aggregator;
    }
    
    /// <summary>
    /// Event fired when new gapper data is available.
    /// </summary>
    public event Action<IReadOnlyList<GapperCandidate>>? OnGappersUpdated;
    
    /// <summary>
    /// Event fired when a high-confidence gapper is detected.
    /// </summary>
    public event Action<GapperCandidate>? OnHighConfidenceGapper;
    
    /// <summary>
    /// Gets current top candidates.
    /// </summary>
    public IReadOnlyList<GapperCandidate> GetTopCandidates(int count = 20) 
        => _aggregator.GetTopCandidates(count);
    
    /// <summary>
    /// Gets a specific candidate.
    /// </summary>
    public GapperCandidate? GetCandidate(string symbol) 
        => _aggregator.GetCandidate(symbol);
    
    /// <summary>
    /// Gets current statistics.
    /// </summary>
    public ScanStatistics GetStatistics() 
        => _aggregator.GetStatistics();
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("MarketScannerService starting...");
        
        // Wait a bit for the app to fully start
        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Check if it's a reasonable time to scan (premarket or market hours)
                var now = DateTime.Now;
                var estNow = TimeZoneInfo.ConvertTime(now, 
                    TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"));
                
                // Premarket: 4:00 AM - 9:30 AM ET
                // Market: 9:30 AM - 4:00 PM ET
                // After hours: 4:00 PM - 8:00 PM ET
                var hour = estNow.Hour;
                var minute = estNow.Minute;
                var timeOfDay = hour * 60 + minute;
                
                var isPremarket = timeOfDay >= 4 * 60 && timeOfDay < 9 * 60 + 30;
                var isMarketHours = timeOfDay >= 9 * 60 + 30 && timeOfDay < 16 * 60;
                var isAfterHours = timeOfDay >= 16 * 60 && timeOfDay < 20 * 60;
                
                // Only scan during trading-relevant hours (or always in debug)
#if DEBUG
                var shouldScan = true;
#else
                var shouldScan = isPremarket || isMarketHours || isAfterHours;
#endif
                
                if (shouldScan && DateTime.UtcNow - _lastFullScan >= _fullScanInterval)
                {
                    await RunFullScanAsync(stoppingToken);
                    _lastFullScan = DateTime.UtcNow;
                }
                
                // Wait before next check
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in market scanner loop");
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
        
        _logger.LogInformation("MarketScannerService stopped");
    }
    
    private async Task RunFullScanAsync(CancellationToken ct)
    {
        _logger.LogInformation("Starting full market scan...");
        
        using var scope = _services.CreateScope();
        var sources = scope.ServiceProvider.GetServices<IGapperSource>().ToList();
        
        if (sources.Count == 0)
        {
            _logger.LogWarning("No gapper sources registered");
            return;
        }
        
        // Track previously seen high-confidence gappers
        var prevHighConf = _aggregator.GetTopCandidates(50)
            .Where(c => c.ConfidenceScore >= 75)
            .Select(c => c.Symbol)
            .ToHashSet();
        
        // Fetch from all sources (with rate limiting)
        foreach (var source in sources.OrderBy(s => s.Priority))
        {
            if (ct.IsCancellationRequested) break;
            
            try
            {
                _logger.LogDebug("Fetching from {Source}...", source.SourceName);
                var rawData = await source.FetchGappersAsync(ct);
                _aggregator.ProcessRawData(rawData);
                
                // Small delay between sources to be polite
                await Task.Delay(TimeSpan.FromMilliseconds(500), ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch from {Source}", source.SourceName);
            }
        }
        
        // Get updated candidates
        var candidates = _aggregator.GetTopCandidates(50);
        var stats = _aggregator.GetStatistics();
        
        _logger.LogInformation(
            "Scan complete: {Total} candidates, {HighConf} high confidence, {GapUps} up, {GapDowns} down",
            stats.TotalCandidates, stats.HighConfidence, stats.GapUps, stats.GapDowns);
        
        // Notify subscribers
        OnGappersUpdated?.Invoke(candidates);
        
        // Check for new high-confidence gappers
        foreach (var candidate in candidates.Where(c => c.ConfidenceScore >= 75))
        {
            if (!prevHighConf.Contains(candidate.Symbol))
            {
                _logger.LogInformation("NEW high confidence gapper: {Symbol} {Gap:+0.0;-0.0}% (Conf: {Conf}%)",
                    candidate.Symbol, candidate.GapPercent, candidate.ConfidenceScore);
                OnHighConfidenceGapper?.Invoke(candidate);
            }
        }
    }
    
    /// <summary>
    /// Manually triggers a scan (for UI refresh button).
    /// </summary>
    public async Task TriggerScanAsync(CancellationToken ct = default)
    {
        await RunFullScanAsync(ct);
    }

    /// <summary>
    /// Looks up a specific symbol and calculates its confidence score.
    /// </summary>
    public async Task<GapperCandidate?> LookupSymbolAsync(string symbol, CancellationToken ct = default)
    {
        symbol = symbol.Trim().ToUpperInvariant();

        // First check if we already have it cached
        var existing = _aggregator.GetCandidate(symbol);
        if (existing != null)
            return existing;

        _logger.LogInformation("Looking up symbol: {Symbol}", symbol);

        try
        {
            using var scope = _services.CreateScope();
            var sources = scope.ServiceProvider.GetServices<IGapperSource>().ToList();

            // Try each source to find the symbol
            foreach (var source in sources.OrderBy(s => s.Priority))
            {
                if (ct.IsCancellationRequested) break;

                try
                {
                    var rawData = await source.FetchGappersAsync(ct);
                    var match = rawData.FirstOrDefault(r => 
                        r.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase));

                    if (match != null)
                    {
                        // Process just this one symbol
                        _aggregator.ProcessRawData([match]);
                        var candidate = _aggregator.GetCandidate(symbol);
                        if (candidate != null)
                        {
                            _logger.LogInformation("Found {Symbol}: Gap {Gap:+0.0;-0.0}%, Confidence {Conf}%",
                                symbol, candidate.GapPercent, candidate.ConfidenceScore);
                            return candidate;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to lookup {Symbol} from {Source}", symbol, source.SourceName);
                }
            }

            // If not found in any source, create a basic candidate with estimated confidence
            // This is a fallback - in production you'd want a proper quote API
            _logger.LogWarning("Symbol {Symbol} not found in any scanner source", symbol);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error looking up symbol {Symbol}", symbol);
            return null;
        }
    }
}
