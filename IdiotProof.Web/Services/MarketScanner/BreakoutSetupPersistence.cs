// ============================================================================
// BreakoutSetupPersistence - Saves and loads breakout setups to/from disk
// ============================================================================
//
// Persists setups so they survive application restarts and provides
// historical tracking of setup performance over time.
// ============================================================================

using System.Text.Json;
using IdiotProof.Scripting;

namespace IdiotProof.Web.Services.MarketScanner;

/// <summary>
/// Handles persistence of breakout setups to disk.
/// </summary>
public sealed class BreakoutSetupPersistence
{
    private readonly ILogger<BreakoutSetupPersistence> _logger;
    private readonly string _dataDirectory;
    private readonly string _activeSetupsFile;
    private readonly string _historyFile;
    private readonly JsonSerializerOptions _jsonOptions;
    
    public BreakoutSetupPersistence(ILogger<BreakoutSetupPersistence> logger, IConfiguration config)
    {
        _logger = logger;
        
        // Use configured data directory or default
        _dataDirectory = config["DataDirectory"] ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "IdiotProof", "Setups");
        
        Directory.CreateDirectory(_dataDirectory);
        
        _activeSetupsFile = Path.Combine(_dataDirectory, "active-setups.json");
        _historyFile = Path.Combine(_dataDirectory, "setup-history.json");
        
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }
    
    /// <summary>
    /// Saves active setups to disk.
    /// </summary>
    public async Task SaveActiveSetupsAsync(IEnumerable<BreakoutSetup> setups)
    {
        try
        {
            var data = setups.Select(s => new PersistedSetup
            {
                Symbol = s.Symbol,
                CompanyName = s.CompanyName,
                Bias = s.Bias,
                Pattern = s.Pattern,
                ConfidenceScore = s.ConfidenceScore,
                TriggerPrice = s.TriggerPrice,
                SupportPrice = s.SupportPrice,
                InvalidationPrice = s.InvalidationPrice,
                Targets = s.Targets.Select(t => new PersistedTarget
                {
                    Label = t.Label,
                    Price = t.Price,
                    PercentToSell = t.PercentToSell,
                    IsHit = t.IsHit
                }).ToList(),
                State = s.State.ToString(),
                CurrentPrice = s.CurrentPrice,
                VwapPrice = s.VwapPrice,
                GapPercent = s.GapPercent,
                VolumeRatio = s.VolumeRatio,
                DiscoveredUtc = s.DiscoveredUtc,
                TriggeredUtc = s.TriggeredUtc,
                ConfirmedUtc = s.ConfirmedUtc,
                EnteredUtc = s.EnteredUtc,
                CompletedUtc = s.CompletedUtc,
                ActualEntryPrice = s.ActualEntryPrice,
                ActualExitPrice = s.ActualExitPrice,
                CompletionReason = s.CompletionReason
            }).ToList();
            
            var json = JsonSerializer.Serialize(data, _jsonOptions);
            await File.WriteAllTextAsync(_activeSetupsFile, json);
            
            _logger.LogDebug("Saved {Count} active setups to disk", data.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save active setups");
        }
    }
    
    /// <summary>
    /// Loads active setups from disk.
    /// </summary>
    public async Task<List<BreakoutSetup>> LoadActiveSetupsAsync()
    {
        try
        {
            if (!File.Exists(_activeSetupsFile))
                return [];
            
            var json = await File.ReadAllTextAsync(_activeSetupsFile);
            var data = JsonSerializer.Deserialize<List<PersistedSetup>>(json, _jsonOptions);
            
            if (data == null)
                return [];
            
            var setups = data.Select(p => new BreakoutSetup
            {
                Symbol = p.Symbol,
                CompanyName = p.CompanyName,
                Bias = p.Bias,
                Pattern = p.Pattern,
                ConfidenceScore = p.ConfidenceScore,
                TriggerPrice = p.TriggerPrice,
                SupportPrice = p.SupportPrice,
                InvalidationPrice = p.InvalidationPrice,
                Targets = p.Targets.Select(t => new TargetLevel
                {
                    Label = t.Label,
                    Price = t.Price,
                    PercentToSell = t.PercentToSell,
                    IsHit = t.IsHit
                }).ToList(),
                State = Enum.Parse<SetupState>(p.State),
                CurrentPrice = p.CurrentPrice,
                VwapPrice = p.VwapPrice,
                GapPercent = p.GapPercent,
                VolumeRatio = p.VolumeRatio,
                DiscoveredUtc = p.DiscoveredUtc,
                TriggeredUtc = p.TriggeredUtc,
                ConfirmedUtc = p.ConfirmedUtc,
                EnteredUtc = p.EnteredUtc,
                CompletedUtc = p.CompletedUtc,
                ActualEntryPrice = p.ActualEntryPrice,
                ActualExitPrice = p.ActualExitPrice,
                CompletionReason = p.CompletionReason
            }).ToList();
            
            _logger.LogInformation("Loaded {Count} active setups from disk", setups.Count);
            return setups;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load active setups");
            return [];
        }
    }
    
    /// <summary>
    /// Archives a completed/invalidated setup to history.
    /// </summary>
    public async Task ArchiveSetupAsync(BreakoutSetup setup)
    {
        try
        {
            var history = await LoadHistoryAsync();
            
            history.Add(new SetupHistoryEntry
            {
                Symbol = setup.Symbol,
                Bias = setup.Bias,
                Pattern = setup.Pattern,
                ConfidenceScore = setup.ConfidenceScore,
                TriggerPrice = setup.TriggerPrice,
                FinalState = setup.State.ToString(),
                EntryPrice = setup.ActualEntryPrice,
                ExitPrice = setup.ActualExitPrice,
                PnLPercent = setup.ActualEntryPrice > 0 
                    ? (setup.ActualExitPrice - setup.ActualEntryPrice) / setup.ActualEntryPrice * 100 
                    : 0,
                TargetsHit = setup.Targets.Count(t => t.IsHit),
                TotalTargets = setup.Targets.Count,
                DiscoveredUtc = setup.DiscoveredUtc,
                CompletedUtc = setup.CompletedUtc ?? DateTime.UtcNow,
                CompletionReason = setup.CompletionReason ?? setup.State.ToString()
            });
            
            var json = JsonSerializer.Serialize(history, _jsonOptions);
            await File.WriteAllTextAsync(_historyFile, json);
            
            _logger.LogInformation("Archived setup {Symbol} to history ({State})", 
                setup.Symbol, setup.State);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to archive setup {Symbol}", setup.Symbol);
        }
    }
    
    /// <summary>
    /// Loads setup history from disk.
    /// </summary>
    public async Task<List<SetupHistoryEntry>> LoadHistoryAsync()
    {
        try
        {
            if (!File.Exists(_historyFile))
                return [];
            
            var json = await File.ReadAllTextAsync(_historyFile);
            return JsonSerializer.Deserialize<List<SetupHistoryEntry>>(json, _jsonOptions) ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load setup history");
            return [];
        }
    }
    
    /// <summary>
    /// Gets performance statistics from history.
    /// </summary>
    public async Task<SetupPerformanceStats> GetPerformanceStatsAsync(int days = 30)
    {
        var history = await LoadHistoryAsync();
        var cutoff = DateTime.UtcNow.AddDays(-days);
        var recent = history.Where(h => h.CompletedUtc >= cutoff).ToList();
        
        var completed = recent.Where(h => h.FinalState == "Completed").ToList();
        var invalidated = recent.Where(h => h.FinalState == "Invalidated").ToList();
        
        return new SetupPerformanceStats
        {
            TotalSetups = recent.Count,
            CompletedCount = completed.Count,
            InvalidatedCount = invalidated.Count,
            WinRate = completed.Count > 0 
                ? completed.Count(c => c.PnLPercent > 0) * 100.0 / completed.Count 
                : 0,
            AveragePnLPercent = completed.Count > 0 
                ? completed.Average(c => c.PnLPercent) 
                : 0,
            BestTrade = completed.MaxBy(c => c.PnLPercent),
            WorstTrade = completed.MinBy(c => c.PnLPercent),
            AverageConfidence = recent.Count > 0 
                ? recent.Average(r => r.ConfidenceScore) 
                : 0,
            DaysAnalyzed = days
        };
    }
}

/// <summary>
/// Persisted setup data structure.
/// </summary>
public sealed class PersistedSetup
{
    public string Symbol { get; set; } = "";
    public string? CompanyName { get; set; }
    public string Bias { get; set; } = "";
    public string Pattern { get; set; } = "";
    public int ConfidenceScore { get; set; }
    public double TriggerPrice { get; set; }
    public double SupportPrice { get; set; }
    public double InvalidationPrice { get; set; }
    public List<PersistedTarget> Targets { get; set; } = [];
    public string State { get; set; } = "Watching";
    public double CurrentPrice { get; set; }
    public double VwapPrice { get; set; }
    public double GapPercent { get; set; }
    public double VolumeRatio { get; set; }
    public DateTime DiscoveredUtc { get; set; }
    public DateTime? TriggeredUtc { get; set; }
    public DateTime? ConfirmedUtc { get; set; }
    public DateTime? EnteredUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }
    public double ActualEntryPrice { get; set; }
    public double ActualExitPrice { get; set; }
    public string? CompletionReason { get; set; }
}

/// <summary>
/// Persisted target data structure.
/// </summary>
public sealed class PersistedTarget
{
    public string Label { get; set; } = "T1";
    public double Price { get; set; }
    public int PercentToSell { get; set; }
    public bool IsHit { get; set; }
}

/// <summary>
/// Historical record of a completed setup.
/// </summary>
public sealed class SetupHistoryEntry
{
    public string Symbol { get; set; } = "";
    public string Bias { get; set; } = "";
    public string Pattern { get; set; } = "";
    public int ConfidenceScore { get; set; }
    public double TriggerPrice { get; set; }
    public string FinalState { get; set; } = "";
    public double EntryPrice { get; set; }
    public double ExitPrice { get; set; }
    public double PnLPercent { get; set; }
    public int TargetsHit { get; set; }
    public int TotalTargets { get; set; }
    public DateTime DiscoveredUtc { get; set; }
    public DateTime CompletedUtc { get; set; }
    public string CompletionReason { get; set; } = "";
}

/// <summary>
/// Performance statistics for setups.
/// </summary>
public sealed class SetupPerformanceStats
{
    public int TotalSetups { get; set; }
    public int CompletedCount { get; set; }
    public int InvalidatedCount { get; set; }
    public double WinRate { get; set; }
    public double AveragePnLPercent { get; set; }
    public SetupHistoryEntry? BestTrade { get; set; }
    public SetupHistoryEntry? WorstTrade { get; set; }
    public double AverageConfidence { get; set; }
    public int DaysAnalyzed { get; set; }
}
