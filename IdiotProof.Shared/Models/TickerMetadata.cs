// ============================================================================
// Ticker Metadata - Historical behavior patterns for informed trading
// ============================================================================
//
// This module provides historical metadata about how a stock typically behaves:
// - HOD/LOD timing patterns
// - Support/resistance levels
// - Gap behavior patterns
// - VWAP interaction patterns
//
// Used by AutonomousTrading and AdaptiveOrder to make smarter decisions.
//
// ============================================================================

using System.Text.Json.Serialization;

namespace IdiotProof.Shared.Models;

/// <summary>
/// Represents a price level that acted as support or resistance.
/// </summary>
public sealed class PriceLevel
{
    /// <summary>The price level.</summary>
    public double Price { get; set; }

    /// <summary>How many times price touched this level.</summary>
    public int Touches { get; set; }

    /// <summary>How many times price bounced off this level.</summary>
    public int Bounces { get; set; }

    /// <summary>How many times price broke through this level.</summary>
    public int Breaks { get; set; }

    /// <summary>Whether this is a support (true) or resistance (false) level.</summary>
    public bool IsSupport { get; set; }

    /// <summary>Strength of the level (bounces / touches).</summary>
    [JsonIgnore]
    public double Strength => Touches > 0 ? (double)Bounces / Touches : 0;

    /// <summary>Whether this level is still valid (not broken too many times).</summary>
    [JsonIgnore]
    public bool IsValid => Strength >= 0.5 && Touches >= 2;
}

/// <summary>
/// HOD/LOD timing statistics for a stock.
/// </summary>
public sealed class DailyExtremesPattern
{
    /// <summary>Average time HOD occurs (minutes from 9:30 open).</summary>
    public double AvgHodMinutesFromOpen { get; set; }

    /// <summary>Average time LOD occurs (minutes from 9:30 open).</summary>
    public double AvgLodMinutesFromOpen { get; set; }

    /// <summary>Percentage of days where HOD occurred in first 30 minutes.</summary>
    public double HodInFirst30MinPercent { get; set; }

    /// <summary>Percentage of days where LOD occurred in first 30 minutes.</summary>
    public double LodInFirst30MinPercent { get; set; }

    /// <summary>Percentage of days where HOD occurred in last 30 minutes.</summary>
    public double HodInLast30MinPercent { get; set; }

    /// <summary>Percentage of days where LOD occurred in last 30 minutes.</summary>
    public double LodInLast30MinPercent { get; set; }

    /// <summary>Average HOD % from open price.</summary>
    public double AvgHodPercentFromOpen { get; set; }

    /// <summary>Average LOD % from open price.</summary>
    public double AvgLodPercentFromOpen { get; set; }

    /// <summary>Average daily range in %.</summary>
    public double AvgDailyRangePercent { get; set; }
}

/// <summary>
/// Gap behavior statistics.
/// </summary>
public sealed class GapBehaviorPattern
{
    /// <summary>Average gap up % when gapping up.</summary>
    public double AvgGapUpPercent { get; set; }

    /// <summary>Average gap down % when gapping down.</summary>
    public double AvgGapDownPercent { get; set; }

    /// <summary>% of gap ups that fill same day.</summary>
    public double GapUpFillRate { get; set; }

    /// <summary>% of gap downs that fill same day.</summary>
    public double GapDownFillRate { get; set; }

    /// <summary>% of gap ups that continue higher.</summary>
    public double GapUpContinuationRate { get; set; }

    /// <summary>% of gap downs that continue lower.</summary>
    public double GapDownContinuationRate { get; set; }

    /// <summary>Average time to gap fill in minutes.</summary>
    public double AvgGapFillTimeMinutes { get; set; }
}

/// <summary>
/// VWAP behavior statistics.
/// </summary>
public sealed class VwapBehaviorPattern
{
    /// <summary>% of day typically spent above VWAP.</summary>
    public double AvgPercentAboveVwap { get; set; }

    /// <summary>Average number of VWAP crosses per day.</summary>
    public double AvgVwapCrossesPerDay { get; set; }

    /// <summary>% of VWAP rejections that lead to continuation.</summary>
    public double VwapRejectionContinuationRate { get; set; }

    /// <summary>% of VWAP reclaims that hold.</summary>
    public double VwapReclaimHoldRate { get; set; }
}

/// <summary>
/// Time window performance statistics.
/// </summary>
public sealed class TimeWindowPerformance
{
    /// <summary>The time window identifier (e.g., "09", "10", "11").</summary>
    public int Hour { get; set; }

    /// <summary>Number of trades in this window.</summary>
    public int TradeCount { get; set; }

    /// <summary>Win count in this window.</summary>
    public int WinCount { get; set; }

    /// <summary>Win rate for this window.</summary>
    [JsonIgnore]
    public double WinRate => TradeCount > 0 ? (double)WinCount / TradeCount * 100 : 0;
}

/// <summary>
/// Complete historical metadata for a ticker.
/// Built from analyzing historical price data during backend warmup.
/// </summary>
public sealed class TickerMetadata
{
    // ========================================================================
    // Identification
    // ========================================================================

    /// <summary>The ticker symbol.</summary>
    public string Symbol { get; set; } = "";

    /// <summary>When this metadata was created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>When this metadata was last updated.</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Number of days of data analyzed.</summary>
    public int DaysAnalyzed { get; set; }

    /// <summary>Number of bars analyzed.</summary>
    public int BarsAnalyzed { get; set; }

    // ========================================================================
    // Behavioral Patterns
    // ========================================================================

    /// <summary>HOD/LOD timing patterns.</summary>
    public DailyExtremesPattern DailyExtremes { get; set; } = new();

    /// <summary>Gap behavior patterns.</summary>
    public GapBehaviorPattern GapBehavior { get; set; } = new();

    /// <summary>VWAP interaction patterns.</summary>
    public VwapBehaviorPattern VwapBehavior { get; set; } = new();

    // ========================================================================
    // Price Levels
    // ========================================================================

    /// <summary>Identified support levels.</summary>
    public List<PriceLevel> SupportLevels { get; set; } = [];

    /// <summary>Identified resistance levels.</summary>
    public List<PriceLevel> ResistanceLevels { get; set; } = [];

    // ========================================================================
    // Time-Based Patterns
    // ========================================================================

    /// <summary>Performance by hour of day.</summary>
    public List<TimeWindowPerformance> HourlyPerformance { get; set; } = [];

    /// <summary>Hours with historically poor performance to avoid.</summary>
    public List<int> AvoidHours { get; set; } = [];

    /// <summary>Hours with historically good performance.</summary>
    public List<int> BestHours { get; set; } = [];

    // ========================================================================
    // Derived Insights
    // ========================================================================

    /// <summary>Whether HOD typically occurs early (first 30 min).</summary>
    [JsonIgnore]
    public bool HodTypicallyEarly => DailyExtremes.HodInFirst30MinPercent > 50;

    /// <summary>Whether LOD typically occurs early (first 30 min).</summary>
    [JsonIgnore]
    public bool LodTypicallyEarly => DailyExtremes.LodInFirst30MinPercent > 50;

    /// <summary>Whether gaps tend to fill.</summary>
    [JsonIgnore]
    public bool GapsTypicallyFill => GapBehavior.GapUpFillRate > 60 || GapBehavior.GapDownFillRate > 60;

    /// <summary>Whether stock is bullish biased (spends more time above VWAP).</summary>
    [JsonIgnore]
    public bool BullishBias => VwapBehavior.AvgPercentAboveVwap > 55;

    /// <summary>Whether stock is bearish biased (spends more time below VWAP).</summary>
    [JsonIgnore]
    public bool BearishBias => VwapBehavior.AvgPercentAboveVwap < 45;

    // ========================================================================
    // Methods
    // ========================================================================

    /// <summary>
    /// Gets the nearest support level below the given price.
    /// </summary>
    public PriceLevel? GetNearestSupport(double currentPrice)
    {
        return SupportLevels
            .Where(s => s.Price < currentPrice && s.IsValid)
            .OrderByDescending(s => s.Price)
            .FirstOrDefault();
    }

    /// <summary>
    /// Gets the nearest resistance level above the given price.
    /// </summary>
    public PriceLevel? GetNearestResistance(double currentPrice)
    {
        return ResistanceLevels
            .Where(r => r.Price > currentPrice && r.IsValid)
            .OrderBy(r => r.Price)
            .FirstOrDefault();
    }

    /// <summary>
    /// Checks if the given hour should be avoided based on historical performance.
    /// </summary>
    public bool ShouldAvoidHour(int hour) => AvoidHours.Contains(hour);

    /// <summary>
    /// Checks if the given hour has historically good performance.
    /// </summary>
    public bool IsBestHour(int hour) => BestHours.Contains(hour);

    /// <summary>
    /// Checks if price is near a support level (within tolerance %).
    /// </summary>
    public bool IsNearSupport(double price, double tolerancePercent = 0.5)
    {
        return SupportLevels.Any(s => 
            s.IsValid && 
            Math.Abs(price - s.Price) / s.Price * 100 <= tolerancePercent);
    }

    /// <summary>
    /// Checks if price is near a resistance level (within tolerance %).
    /// </summary>
    public bool IsNearResistance(double price, double tolerancePercent = 0.5)
    {
        return ResistanceLevels.Any(r => 
            r.IsValid && 
            Math.Abs(price - r.Price) / r.Price * 100 <= tolerancePercent);
    }

    /// <summary>
    /// Gets an entry score adjustment based on current conditions and metadata.
    /// Positive = more favorable, Negative = less favorable.
    /// </summary>
    public int GetEntryAdjustment(double price, int minutesFromOpen, bool isLong)
    {
        int adjustment = 0;

        if (isLong)
        {
            // Long entries
            if (HodTypicallyEarly && minutesFromOpen > 30)
                adjustment -= 10; // HOD usually early, we're past it

            if (Math.Abs(minutesFromOpen - DailyExtremes.AvgLodMinutesFromOpen) < 30)
                adjustment += 15; // Near typical LOD time = good for longs

            if (IsNearSupport(price))
                adjustment += 10; // Near support = good for longs

            if (IsNearResistance(price))
                adjustment -= 10; // Near resistance = bad for longs
        }
        else
        {
            // Short entries
            if (LodTypicallyEarly && minutesFromOpen > 30)
                adjustment -= 10; // LOD usually early, we're past it

            if (Math.Abs(minutesFromOpen - DailyExtremes.AvgHodMinutesFromOpen) < 30)
                adjustment += 15; // Near typical HOD time = good for shorts

            if (IsNearResistance(price))
                adjustment += 10; // Near resistance = good for shorts

            if (IsNearSupport(price))
                adjustment -= 10; // Near support = bad for shorts
        }

        return adjustment;
    }
}
