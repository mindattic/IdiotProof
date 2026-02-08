// ============================================================================
// Historical Metadata - Stock behavior patterns learned from historical data
// ============================================================================
//
// Stores metadata about how a stock typically behaves:
// - HOD/LOD patterns (when they typically occur, at what % from open)
// - Support/resistance levels
// - Optimal buy/sell/short/cover points
// - Gap behavior (how it reacts to gaps)
// - VWAP behavior (rejections, reclaims, etc.)
//
// This data helps autonomous trading "know how the stock works" before trading.
//
// ============================================================================

using System.Text.Json.Serialization;

namespace IdiotProof.Learning;

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
/// Represents an optimal trade point identified from historical data.
/// </summary>
public sealed class OptimalTradePoint
{
    /// <summary>The time of day when this trade point occurred.</summary>
    public TimeOnly TimeOfDay { get; set; }

    /// <summary>Price relative to open (e.g., 1.02 = 2% above open).</summary>
    public double PriceRelativeToOpen { get; set; }

    /// <summary>Price relative to VWAP (e.g., 0.98 = 2% below VWAP).</summary>
    public double PriceRelativeToVwap { get; set; }

    /// <summary>Type of trade: Long, Short, CloseLong, CloseShort.</summary>
    public TradePointType Type { get; set; }

    /// <summary>Score at this point (if available).</summary>
    public double Score { get; set; }

    /// <summary>Potential profit in % if this trade was taken.</summary>
    public double PotentialProfitPercent { get; set; }

    /// <summary>How many times this pattern occurred.</summary>
    public int Occurrences { get; set; }

    /// <summary>How many times this trade would have been profitable.</summary>
    public int ProfitableCount { get; set; }

    /// <summary>Win rate for this trade point.</summary>
    [JsonIgnore]
    public double WinRate => Occurrences > 0 ? (double)ProfitableCount / Occurrences * 100 : 0;
}

/// <summary>
/// Types of trade points.
/// </summary>
public enum TradePointType
{
    Long,
    Short,
    CloseLong,
    CloseShort
}

/// <summary>
/// HOD/LOD statistics for a stock.
/// </summary>
public sealed class DailyExtremesStats
{
    /// <summary>Average time HOD occurs (minutes from market open).</summary>
    public double AvgHodMinutesFromOpen { get; set; }

    /// <summary>Std deviation of HOD time.</summary>
    public double StdHodMinutesFromOpen { get; set; }

    /// <summary>Average time LOD occurs (minutes from market open).</summary>
    public double AvgLodMinutesFromOpen { get; set; }

    /// <summary>Std deviation of LOD time.</summary>
    public double StdLodMinutesFromOpen { get; set; }

    /// <summary>Percentage of days where HOD occurred in first 30 minutes.</summary>
    public double HodInFirst30MinPercent { get; set; }

    /// <summary>Percentage of days where LOD occurred in first 30 minutes.</summary>
    public double LodInFirst30MinPercent { get; set; }

    /// <summary>Percentage of days where HOD occurred in last 30 minutes.</summary>
    public double HodInLast30MinPercent { get; set; }

    /// <summary>Percentage of days where LOD occurred in last 30 minutes.</summary>
    public double LodInLast30MinPercent { get; set; }

    /// <summary>Average HOD % from open.</summary>
    public double AvgHodPercentFromOpen { get; set; }

    /// <summary>Average LOD % from open.</summary>
    public double AvgLodPercentFromOpen { get; set; }

    /// <summary>Average daily range in %.</summary>
    public double AvgDailyRangePercent { get; set; }
}

/// <summary>
/// Gap behavior statistics.
/// </summary>
public sealed class GapBehaviorStats
{
    /// <summary>Average gap up %.</summary>
    public double AvgGapUpPercent { get; set; }

    /// <summary>Average gap down %.</summary>
    public double AvgGapDownPercent { get; set; }

    /// <summary>% of gap ups that fill same day.</summary>
    public double GapUpFillRate { get; set; }

    /// <summary>% of gap downs that fill same day.</summary>
    public double GapDownFillRate { get; set; }

    /// <summary>% of gap ups that continue higher.</summary>
    public double GapUpContinuationRate { get; set; }

    /// <summary>% of gap downs that continue lower.</summary>
    public double GapDownContinuationRate { get; set; }

    /// <summary>Average time to gap fill (minutes).</summary>
    public double AvgGapFillTimeMinutes { get; set; }
}

/// <summary>
/// VWAP behavior statistics.
/// </summary>
public sealed class VwapBehaviorStats
{
    /// <summary>% of day typically spent above VWAP.</summary>
    public double AvgPercentAboveVwap { get; set; }

    /// <summary>Average number of VWAP crosses per day.</summary>
    public double AvgVwapCrossesPerDay { get; set; }

    /// <summary>% of VWAP rejections that lead to continuation.</summary>
    public double VwapRejectionContinuationRate { get; set; }

    /// <summary>% of VWAP reclaims that hold.</summary>
    public double VwapReclaimHoldRate { get; set; }

    /// <summary>Average distance from VWAP at close (%).</summary>
    public double AvgCloseDistanceFromVwap { get; set; }
}

/// <summary>
/// A single day's analysis results.
/// </summary>
public sealed class DayAnalysis
{
    public required DateOnly Date { get; init; }
    public required double Open { get; init; }
    public required double High { get; init; }
    public required double Low { get; init; }
    public required double Close { get; init; }
    public required double HodPercentFromOpen { get; init; }
    public required double LodPercentFromOpen { get; init; }
    public required TimeOnly HodTime { get; init; }
    public required TimeOnly LodTime { get; init; }
    public required int HodMinutesFromOpen { get; init; }
    public required int LodMinutesFromOpen { get; init; }
    public double GapPercent { get; init; }
    public bool GapFilled { get; init; }
    public int GapFillTimeMinutes { get; init; }
    public int VwapCrosses { get; init; }
    public double PercentTimeAboveVwap { get; init; }
    public double DailyRangePercent { get; init; }

    /// <summary>Optimal long entry point for this day.</summary>
    public OptimalTradePoint? BestLongEntry { get; set; }

    /// <summary>Optimal short entry point for this day.</summary>
    public OptimalTradePoint? BestShortEntry { get; set; }

    /// <summary>Optimal long exit point for this day.</summary>
    public OptimalTradePoint? BestLongExit { get; set; }

    /// <summary>Optimal short exit point for this day.</summary>
    public OptimalTradePoint? BestShortExit { get; set; }
}

/// <summary>
/// Complete historical metadata for a ticker.
/// </summary>
public sealed class HistoricalMetadata
{
    // ========================================================================
    // Metadata
    // ========================================================================

    /// <summary>The ticker symbol.</summary>
    public string Symbol { get; set; } = "";

    /// <summary>When this metadata was first created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>When this metadata was last updated.</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Number of days analyzed.</summary>
    public int DaysAnalyzed { get; set; }

    // ========================================================================
    // Statistical Patterns
    // ========================================================================

    /// <summary>HOD/LOD timing and magnitude patterns.</summary>
    public DailyExtremesStats DailyExtremes { get; set; } = new();

    /// <summary>Gap behavior patterns.</summary>
    public GapBehaviorStats GapBehavior { get; set; } = new();

    /// <summary>VWAP interaction patterns.</summary>
    public VwapBehaviorStats VwapBehavior { get; set; } = new();

    // ========================================================================
    // Price Levels
    // ========================================================================

    /// <summary>Identified support levels.</summary>
    public List<PriceLevel> SupportLevels { get; set; } = [];

    /// <summary>Identified resistance levels.</summary>
    public List<PriceLevel> ResistanceLevels { get; set; } = [];

    // ========================================================================
    // Optimal Trade Points (aggregated from all days)
    // ========================================================================

    /// <summary>Best times/conditions to enter long.</summary>
    public List<OptimalTradePoint> OptimalLongEntries { get; set; } = [];

    /// <summary>Best times/conditions to enter short.</summary>
    public List<OptimalTradePoint> OptimalShortEntries { get; set; } = [];

    /// <summary>Best times/conditions to exit long.</summary>
    public List<OptimalTradePoint> OptimalLongExits { get; set; } = [];

    /// <summary>Best times/conditions to exit short.</summary>
    public List<OptimalTradePoint> OptimalShortExits { get; set; } = [];

    // ========================================================================
    // Raw Day Analysis (for re-analysis)
    // ========================================================================

    /// <summary>Recent day analyses (limited to last 50 days).</summary>
    public List<DayAnalysis> RecentDays { get; set; } = [];

    private const int MaxRecentDays = 50;

    // ========================================================================
    // Methods
    // ========================================================================

    /// <summary>
    /// Adds a day analysis and updates aggregate statistics.
    /// </summary>
    public void AddDayAnalysis(DayAnalysis day)
    {
        RecentDays.Add(day);

        // Trim to max size
        if (RecentDays.Count > MaxRecentDays)
        {
            RecentDays.RemoveAt(0);
        }

        DaysAnalyzed++;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Recalculates all aggregate statistics from day analyses.
    /// </summary>
    public void RecalculateStatistics()
    {
        if (RecentDays.Count == 0) return;

        RecalculateDailyExtremes();
        RecalculateGapBehavior();
        RecalculateVwapBehavior();
        AggregateOptimalTradePoints();
    }

    private void RecalculateDailyExtremes()
    {
        var hodTimes = RecentDays.Select(d => (double)d.HodMinutesFromOpen).ToList();
        var lodTimes = RecentDays.Select(d => (double)d.LodMinutesFromOpen).ToList();

        DailyExtremes.AvgHodMinutesFromOpen = hodTimes.Average();
        DailyExtremes.StdHodMinutesFromOpen = CalculateStdDev(hodTimes);
        DailyExtremes.AvgLodMinutesFromOpen = lodTimes.Average();
        DailyExtremes.StdLodMinutesFromOpen = CalculateStdDev(lodTimes);

        // First/last 30 min analysis (assuming RTH starts at 9:30)
        DailyExtremes.HodInFirst30MinPercent = RecentDays.Count(d => d.HodMinutesFromOpen <= 30) * 100.0 / RecentDays.Count;
        DailyExtremes.LodInFirst30MinPercent = RecentDays.Count(d => d.LodMinutesFromOpen <= 30) * 100.0 / RecentDays.Count;
        DailyExtremes.HodInLast30MinPercent = RecentDays.Count(d => d.HodMinutesFromOpen >= 360) * 100.0 / RecentDays.Count;  // 6 hours = 360 min
        DailyExtremes.LodInLast30MinPercent = RecentDays.Count(d => d.LodMinutesFromOpen >= 360) * 100.0 / RecentDays.Count;

        DailyExtremes.AvgHodPercentFromOpen = RecentDays.Average(d => d.HodPercentFromOpen);
        DailyExtremes.AvgLodPercentFromOpen = RecentDays.Average(d => d.LodPercentFromOpen);
        DailyExtremes.AvgDailyRangePercent = RecentDays.Average(d => d.DailyRangePercent);
    }

    private void RecalculateGapBehavior()
    {
        var gapUpDays = RecentDays.Where(d => d.GapPercent > 0.5).ToList();
        var gapDownDays = RecentDays.Where(d => d.GapPercent < -0.5).ToList();

        if (gapUpDays.Count > 0)
        {
            GapBehavior.AvgGapUpPercent = gapUpDays.Average(d => d.GapPercent);
            GapBehavior.GapUpFillRate = gapUpDays.Count(d => d.GapFilled) * 100.0 / gapUpDays.Count;
            GapBehavior.GapUpContinuationRate = gapUpDays.Count(d => d.Close > d.Open) * 100.0 / gapUpDays.Count;
        }

        if (gapDownDays.Count > 0)
        {
            GapBehavior.AvgGapDownPercent = gapDownDays.Average(d => d.GapPercent);
            GapBehavior.GapDownFillRate = gapDownDays.Count(d => d.GapFilled) * 100.0 / gapDownDays.Count;
            GapBehavior.GapDownContinuationRate = gapDownDays.Count(d => d.Close < d.Open) * 100.0 / gapDownDays.Count;
        }

        var filledGaps = RecentDays.Where(d => d.GapFilled && d.GapFillTimeMinutes > 0).ToList();
        if (filledGaps.Count > 0)
        {
            GapBehavior.AvgGapFillTimeMinutes = filledGaps.Average(d => d.GapFillTimeMinutes);
        }
    }

    private void RecalculateVwapBehavior()
    {
        VwapBehavior.AvgPercentAboveVwap = RecentDays.Average(d => d.PercentTimeAboveVwap);
        VwapBehavior.AvgVwapCrossesPerDay = RecentDays.Average(d => d.VwapCrosses);
    }

    private void AggregateOptimalTradePoints()
    {
        // Group optimal trade points by time windows (30 min buckets)
        var longEntries = RecentDays
            .Where(d => d.BestLongEntry != null)
            .Select(d => d.BestLongEntry!)
            .GroupBy(p => p.TimeOfDay.Hour)
            .Select(g => new OptimalTradePoint
            {
                TimeOfDay = new TimeOnly(g.Key, 30),
                PriceRelativeToOpen = g.Average(p => p.PriceRelativeToOpen),
                PriceRelativeToVwap = g.Average(p => p.PriceRelativeToVwap),
                Type = TradePointType.Long,
                Score = g.Average(p => p.Score),
                PotentialProfitPercent = g.Average(p => p.PotentialProfitPercent),
                Occurrences = g.Count(),
                ProfitableCount = g.Count(p => p.PotentialProfitPercent > 0)
            })
            .Where(p => p.Occurrences >= 2)
            .OrderByDescending(p => p.WinRate)
            .Take(5)
            .ToList();

        OptimalLongEntries = longEntries;

        // Similarly for shorts
        var shortEntries = RecentDays
            .Where(d => d.BestShortEntry != null)
            .Select(d => d.BestShortEntry!)
            .GroupBy(p => p.TimeOfDay.Hour)
            .Select(g => new OptimalTradePoint
            {
                TimeOfDay = new TimeOnly(g.Key, 30),
                PriceRelativeToOpen = g.Average(p => p.PriceRelativeToOpen),
                PriceRelativeToVwap = g.Average(p => p.PriceRelativeToVwap),
                Type = TradePointType.Short,
                Score = g.Average(p => p.Score),
                PotentialProfitPercent = g.Average(p => p.PotentialProfitPercent),
                Occurrences = g.Count(),
                ProfitableCount = g.Count(p => p.PotentialProfitPercent > 0)
            })
            .Where(p => p.Occurrences >= 2)
            .OrderByDescending(p => p.WinRate)
            .Take(5)
            .ToList();

        OptimalShortEntries = shortEntries;
    }

    private static double CalculateStdDev(List<double> values)
    {
        if (values.Count <= 1) return 0;
        double avg = values.Average();
        double sumSquares = values.Sum(v => (v - avg) * (v - avg));
        return Math.Sqrt(sumSquares / (values.Count - 1));
    }

    // ========================================================================
    // Display
    // ========================================================================

    public override string ToString()
    {
        return $"""
            +==================================================================+
            | HISTORICAL METADATA: {Symbol,-10}                                |
            +==================================================================+
            | Created:       {CreatedAt:yyyy-MM-dd HH:mm}
            | Updated:       {UpdatedAt:yyyy-MM-dd HH:mm}
            | Days Analyzed: {DaysAnalyzed}
            +------------------------------------------------------------------+
            | DAILY EXTREMES                                                   |
            +------------------------------------------------------------------+
            | Avg HOD: {DailyExtremes.AvgHodPercentFromOpen,+5:F2}% from open at ~{DailyExtremes.AvgHodMinutesFromOpen,3:F0} min
            | Avg LOD: {DailyExtremes.AvgLodPercentFromOpen,+5:F2}% from open at ~{DailyExtremes.AvgLodMinutesFromOpen,3:F0} min
            | Avg Range: {DailyExtremes.AvgDailyRangePercent,5:F2}%
            | HOD in first 30 min: {DailyExtremes.HodInFirst30MinPercent,5:F1}%
            | LOD in first 30 min: {DailyExtremes.LodInFirst30MinPercent,5:F1}%
            +------------------------------------------------------------------+
            | GAP BEHAVIOR                                                     |
            +------------------------------------------------------------------+
            | Avg Gap Up:   {GapBehavior.AvgGapUpPercent,+5:F2}%   Fill Rate: {GapBehavior.GapUpFillRate,5:F1}%
            | Avg Gap Down: {GapBehavior.AvgGapDownPercent,+5:F2}%   Fill Rate: {GapBehavior.GapDownFillRate,5:F1}%
            | Avg Fill Time: {GapBehavior.AvgGapFillTimeMinutes,5:F0} min
            +------------------------------------------------------------------+
            | VWAP BEHAVIOR                                                    |
            +------------------------------------------------------------------+
            | Time Above VWAP: {VwapBehavior.AvgPercentAboveVwap,5:F1}%
            | Avg VWAP Crosses/Day: {VwapBehavior.AvgVwapCrossesPerDay,5:F1}
            +------------------------------------------------------------------+
            | SUPPORT/RESISTANCE ({SupportLevels.Count} / {ResistanceLevels.Count} levels)
            +==================================================================+
            """;
    }
}
