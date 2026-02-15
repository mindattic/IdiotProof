// ============================================================================
// GapperCandidate - Represents a potential gapper from market-wide scanning
// ============================================================================

namespace IdiotProof.Web.Services.MarketScanner;

/// <summary>
/// A stock that is gapping up or down in premarket, with aggregated confidence.
/// </summary>
public sealed class GapperCandidate
{
    public string Symbol { get; set; } = "";
    public string? CompanyName { get; set; }
    
    // Price data
    public double PremarketPrice { get; set; }
    public double PreviousClose { get; set; }
    public double GapPercent { get; set; }
    public bool IsGapUp => GapPercent > 0;
    
    // Volume data
    public long PremarketVolume { get; set; }
    public double AverageVolume { get; set; }
    public double VolumeRatio => AverageVolume > 0 ? PremarketVolume / AverageVolume : 0;
    
    // Market cap (for filtering penny stocks vs blue chips)
    public double? MarketCap { get; set; }
    public string MarketCapTier => MarketCap switch
    {
        null => "Unknown",
        < 50_000_000 => "Nano",
        < 300_000_000 => "Micro",
        < 2_000_000_000 => "Small",
        < 10_000_000_000 => "Mid",
        < 200_000_000_000 => "Large",
        _ => "Mega"
    };
    
    // Sources that reported this gapper
    public List<string> Sources { get; set; } = [];
    public int SourceCount => Sources.Count;
    
    // Aggregated confidence score (0-100)
    public int ConfidenceScore { get; set; }
    public string ConfidenceGrade => ConfidenceScore switch
    {
        >= 85 => "A+",
        >= 75 => "A",
        >= 65 => "B",
        >= 55 => "C",
        >= 45 => "D",
        _ => "F"
    };
    
    // Additional context
    public string? Catalyst { get; set; }  // News, earnings, etc.
    public string? Sector { get; set; }
    public double? FloatShares { get; set; }  // Low float = more volatile
    public double? ShortInterest { get; set; }  // High SI = squeeze potential
    
    // Sentiment from social sources
    public int? SocialMentions { get; set; }
    public string? SocialSentiment { get; set; }  // "Bullish", "Bearish", "Mixed"
    
    // Timing
    public DateTime FirstSeenUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;
    
    // Pre-calculated trade setups
    public TradeScenario? LongScenario { get; set; }
    public TradeScenario? ShortScenario { get; set; }
    
    /// <summary>
    /// Unique key for deduplication.
    /// </summary>
    public string Key => Symbol.ToUpperInvariant();
    
    public override string ToString() => 
        $"{Symbol}: {GapPercent:+0.0;-0.0}% @ ${PremarketPrice:F2} (Conf: {ConfidenceScore}%, {SourceCount} sources)";
}

/// <summary>
/// Pre-calculated trade scenario ready for one-click execution.
/// </summary>
public sealed class TradeScenario
{
    public string Symbol { get; set; } = "";
    public bool IsLong { get; set; }
    public string Direction => IsLong ? "LONG" : "SHORT";
    
    // Entry
    public double EntryPrice { get; set; }
    public string EntryType { get; set; } = "Market";  // "Market", "Limit", "Breakout"
    
    // Risk Management
    public double StopLoss { get; set; }
    public double StopLossPercent => EntryPrice > 0 ? Math.Abs((StopLoss - EntryPrice) / EntryPrice * 100) : 0;
    
    public double TakeProfit { get; set; }
    public double TakeProfitPercent => EntryPrice > 0 ? Math.Abs((TakeProfit - EntryPrice) / EntryPrice * 100) : 0;
    
    public double TrailingStopPercent { get; set; } = 1.5;
    
    // Position sizing
    public int Quantity { get; set; }
    public double RiskDollars { get; set; }
    public double RewardDollars { get; set; }
    public double RiskRewardRatio => RiskDollars > 0 ? RewardDollars / RiskDollars : 0;
    
    // Confidence in this specific scenario
    public int ScenarioConfidence { get; set; }
    public string Rationale { get; set; } = "";
    
    // Timing
    public DateTime GeneratedUtc { get; set; } = DateTime.UtcNow;
    public TimeSpan ValidFor { get; set; } = TimeSpan.FromMinutes(10);
    public bool IsExpired => DateTime.UtcNow > GeneratedUtc + ValidFor;
    
    public string ScenarioId { get; set; } = Guid.NewGuid().ToString("N")[..8];
}

/// <summary>
/// Raw gapper data from a single source (before aggregation).
/// </summary>
public sealed class RawGapperData
{
    public string Source { get; set; } = "";
    public string Symbol { get; set; } = "";
    public string? CompanyName { get; set; }
    public double? Price { get; set; }
    public double? GapPercent { get; set; }
    public long? Volume { get; set; }
    public double? MarketCap { get; set; }
    public string? Sector { get; set; }
    public string? Catalyst { get; set; }
    public DateTime FetchedUtc { get; set; } = DateTime.UtcNow;
    
    // Source-specific fields
    public Dictionary<string, object> Extra { get; set; } = [];
}
