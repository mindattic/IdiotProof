// ============================================================================
// GapperAggregator - Combines data from multiple sources into scored candidates
// ============================================================================

using System.Collections.Concurrent;

namespace IdiotProof.Web.Services.MarketScanner;

/// <summary>
/// Aggregates gapper data from multiple sources and calculates confidence scores.
/// </summary>
public sealed class GapperAggregator
{
    private readonly ILogger<GapperAggregator> _logger;
    private readonly ConcurrentDictionary<string, GapperCandidate> _candidates = new();
    
    // Configuration
    public double MinGapPercent { get; set; } = 3.0;
    public double MinPrice { get; set; } = 0.50;
    public double MaxPrice { get; set; } = 500.0;
    public long MinVolume { get; set; } = 50_000;
    
    public GapperAggregator(ILogger<GapperAggregator> logger)
    {
        _logger = logger;
    }
    
    /// <summary>
    /// Processes raw data from a source and updates aggregated candidates.
    /// </summary>
    public void ProcessRawData(IEnumerable<RawGapperData> rawData)
    {
        foreach (var raw in rawData)
        {
            ProcessSingleEntry(raw);
        }
    }
    
    private void ProcessSingleEntry(RawGapperData raw)
    {
        if (string.IsNullOrWhiteSpace(raw.Symbol)) return;
        
        var key = raw.Symbol.ToUpperInvariant();
        
        _candidates.AddOrUpdate(
            key,
            // Add new
            _ => CreateCandidate(raw),
            // Update existing
            (_, existing) => MergeCandidate(existing, raw)
        );
    }
    
    private GapperCandidate CreateCandidate(RawGapperData raw)
    {
        var candidate = new GapperCandidate
        {
            Symbol = raw.Symbol.ToUpperInvariant(),
            CompanyName = raw.CompanyName,
            PremarketPrice = raw.Price ?? 0,
            GapPercent = raw.GapPercent ?? 0,
            PremarketVolume = raw.Volume ?? 0,
            MarketCap = raw.MarketCap,
            Sector = raw.Sector,
            Catalyst = raw.Catalyst,
            Sources = [raw.Source],
            FirstSeenUtc = DateTime.UtcNow,
            LastUpdatedUtc = DateTime.UtcNow
        };
        
        // Calculate initial confidence
        candidate.ConfidenceScore = CalculateConfidence(candidate);
        
        // Generate trade scenarios
        GenerateScenarios(candidate);
        
        return candidate;
    }
    
    private GapperCandidate MergeCandidate(GapperCandidate existing, RawGapperData raw)
    {
        // Add source if not already present
        if (!existing.Sources.Contains(raw.Source))
        {
            existing.Sources.Add(raw.Source);
        }
        
        // Update with better data (prefer non-null, non-zero values)
        if (raw.Price.HasValue && raw.Price > 0)
            existing.PremarketPrice = raw.Price.Value;
        
        if (raw.GapPercent.HasValue && Math.Abs(raw.GapPercent.Value) > Math.Abs(existing.GapPercent))
            existing.GapPercent = raw.GapPercent.Value;
        
        if (raw.Volume.HasValue && raw.Volume > existing.PremarketVolume)
            existing.PremarketVolume = raw.Volume.Value;
        
        if (raw.MarketCap.HasValue && (!existing.MarketCap.HasValue || raw.MarketCap > 0))
            existing.MarketCap = raw.MarketCap;
        
        if (!string.IsNullOrEmpty(raw.CompanyName) && string.IsNullOrEmpty(existing.CompanyName))
            existing.CompanyName = raw.CompanyName;
        
        if (!string.IsNullOrEmpty(raw.Sector) && string.IsNullOrEmpty(existing.Sector))
            existing.Sector = raw.Sector;
        
        if (!string.IsNullOrEmpty(raw.Catalyst) && string.IsNullOrEmpty(existing.Catalyst))
            existing.Catalyst = raw.Catalyst;
        
        existing.LastUpdatedUtc = DateTime.UtcNow;
        
        // Recalculate confidence
        existing.ConfidenceScore = CalculateConfidence(existing);
        
        // Regenerate scenarios with updated data
        GenerateScenarios(existing);
        
        return existing;
    }
    
    /// <summary>
    /// Calculates a confidence score (0-100) for a gapper candidate.
    /// </summary>
    private int CalculateConfidence(GapperCandidate candidate)
    {
        int score = 0;
        
        // Source confirmation (up to 30 points)
        // More sources = more confidence
        score += Math.Min(30, candidate.SourceCount * 10);
        
        // Gap magnitude (up to 25 points)
        // Bigger gap = higher conviction (but not too extreme)
        var absGap = Math.Abs(candidate.GapPercent);
        if (absGap >= 3 && absGap < 5) score += 10;
        else if (absGap >= 5 && absGap < 10) score += 20;
        else if (absGap >= 10 && absGap < 20) score += 25;
        else if (absGap >= 20 && absGap < 50) score += 15;  // Too extreme = risky
        else if (absGap >= 50) score += 5;  // Probably a bad data point
        
        // Volume (up to 20 points)
        if (candidate.PremarketVolume >= 1_000_000) score += 20;
        else if (candidate.PremarketVolume >= 500_000) score += 15;
        else if (candidate.PremarketVolume >= 100_000) score += 10;
        else if (candidate.PremarketVolume >= 50_000) score += 5;
        
        // Price sanity (up to 10 points)
        // Prefer stocks in a tradeable range
        if (candidate.PremarketPrice >= 5 && candidate.PremarketPrice <= 100) score += 10;
        else if (candidate.PremarketPrice >= 1 && candidate.PremarketPrice <= 200) score += 5;
        
        // Market cap (up to 10 points)
        // Mid-large caps are more predictable
        if (candidate.MarketCap.HasValue)
        {
            if (candidate.MarketCap >= 2_000_000_000) score += 10;  // Large+
            else if (candidate.MarketCap >= 300_000_000) score += 7;  // Small
            else if (candidate.MarketCap >= 50_000_000) score += 3;  // Micro
            // Nano caps get no bonus
        }
        
        // Catalyst bonus (5 points)
        if (!string.IsNullOrEmpty(candidate.Catalyst)) score += 5;
        
        return Math.Min(100, score);
    }
    
    /// <summary>
    /// Generates LONG and SHORT trade scenarios for a candidate.
    /// </summary>
    private void GenerateScenarios(GapperCandidate candidate)
    {
        if (candidate.PremarketPrice <= 0) return;
        
        var price = candidate.PremarketPrice;
        var absGap = Math.Abs(candidate.GapPercent);
        
        // Risk management parameters based on gap size
        double stopPercent = absGap switch
        {
            < 5 => 2.0,
            < 10 => 2.5,
            < 20 => 3.0,
            _ => 4.0
        };
        
        double targetMultiplier = 2.5;  // Risk:Reward 1:2.5
        double trailingPercent = stopPercent * 0.6;
        
        // Calculate quantity for ~$50 risk
        double riskDollars = 50.0;
        var riskPerShare = price * (stopPercent / 100);
        var quantity = (int)Math.Floor(riskDollars / riskPerShare);
        quantity = Math.Max(1, quantity);
        
        // LONG scenario (for gap ups or bounce plays on gap downs)
        candidate.LongScenario = new TradeScenario
        {
            Symbol = candidate.Symbol,
            IsLong = true,
            EntryPrice = Math.Round(price, 2),
            StopLoss = Math.Round(price * (1 - stopPercent / 100), 2),
            TakeProfit = Math.Round(price * (1 + stopPercent * targetMultiplier / 100), 2),
            TrailingStopPercent = trailingPercent,
            Quantity = quantity,
            RiskDollars = Math.Round(quantity * riskPerShare, 2),
            RewardDollars = Math.Round(quantity * riskPerShare * targetMultiplier, 2),
            ScenarioConfidence = candidate.IsGapUp ? candidate.ConfidenceScore : candidate.ConfidenceScore - 20,
            Rationale = candidate.IsGapUp 
                ? $"Gap up continuation: {candidate.GapPercent:+0.0}% with {candidate.SourceCount} source(s) confirming"
                : $"Gap down bounce: Looking for mean reversion from {candidate.GapPercent:0.0}% drop"
        };
        
        // SHORT scenario (for fades or continuation on gap downs)
        candidate.ShortScenario = new TradeScenario
        {
            Symbol = candidate.Symbol,
            IsLong = false,
            EntryPrice = Math.Round(price, 2),
            StopLoss = Math.Round(price * (1 + stopPercent / 100), 2),
            TakeProfit = Math.Round(price * (1 - stopPercent * targetMultiplier / 100), 2),
            TrailingStopPercent = trailingPercent,
            Quantity = quantity,
            RiskDollars = Math.Round(quantity * riskPerShare, 2),
            RewardDollars = Math.Round(quantity * riskPerShare * targetMultiplier, 2),
            ScenarioConfidence = !candidate.IsGapUp ? candidate.ConfidenceScore : candidate.ConfidenceScore - 20,
            Rationale = !candidate.IsGapUp 
                ? $"Gap down continuation: {candidate.GapPercent:0.0}% with momentum"
                : $"Gap up fade: Extended {candidate.GapPercent:+0.0}% may pull back"
        };
    }
    
    /// <summary>
    /// Gets all candidates sorted by confidence.
    /// </summary>
    public IReadOnlyList<GapperCandidate> GetTopCandidates(int count = 20)
    {
        return _candidates.Values
            .Where(c => Math.Abs(c.GapPercent) >= MinGapPercent)
            .Where(c => c.PremarketPrice >= MinPrice && c.PremarketPrice <= MaxPrice)
            .Where(c => c.PremarketVolume >= MinVolume)
            .OrderByDescending(c => c.ConfidenceScore)
            .ThenByDescending(c => c.SourceCount)
            .Take(count)
            .ToList();
    }
    
    /// <summary>
    /// Gets a specific candidate by symbol.
    /// </summary>
    public GapperCandidate? GetCandidate(string symbol)
    {
        return _candidates.TryGetValue(symbol.ToUpperInvariant(), out var candidate) 
            ? candidate 
            : null;
    }
    
    /// <summary>
    /// Gets statistics about the current scan.
    /// </summary>
    public ScanStatistics GetStatistics()
    {
        var all = _candidates.Values.ToList();
        
        return new ScanStatistics
        {
            TotalCandidates = all.Count,
            GapUps = all.Count(c => c.IsGapUp),
            GapDowns = all.Count(c => !c.IsGapUp),
            HighConfidence = all.Count(c => c.ConfidenceScore >= 70),
            MultiSourceConfirmed = all.Count(c => c.SourceCount >= 2),
            AverageGapPercent = all.Count > 0 ? all.Average(c => Math.Abs(c.GapPercent)) : 0,
            LastUpdatedUtc = all.Count > 0 ? all.Max(c => c.LastUpdatedUtc) : DateTime.UtcNow
        };
    }
    
    /// <summary>
    /// Clears all candidates (for refresh).
    /// </summary>
    public void Clear()
    {
        _candidates.Clear();
    }
}

public sealed class ScanStatistics
{
    public int TotalCandidates { get; set; }
    public int GapUps { get; set; }
    public int GapDowns { get; set; }
    public int HighConfidence { get; set; }
    public int MultiSourceConfirmed { get; set; }
    public double AverageGapPercent { get; set; }
    public DateTime LastUpdatedUtc { get; set; }
}
