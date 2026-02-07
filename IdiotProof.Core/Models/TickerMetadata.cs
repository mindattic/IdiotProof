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

namespace IdiotProof.Core.Models;

/// <summary>
/// Volatility classification for a stock.
/// </summary>
public enum VolatilityLevel
{
    Unknown,
    Low,       // Beta < 0.8 - Less volatile than market
    Normal,    // Beta 0.8-1.5 - Market-like volatility
    High,      // Beta 1.5-2.0 - More volatile than market
    Extreme    // Beta > 2.0 or low float - Very high volatility
}

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
    // Volatility & Risk Profile
    // ========================================================================

    /// <summary>Beta relative to S&P 500 (1.0 = market, >1 = more volatile).</summary>
    public double? Beta { get; set; }

    /// <summary>14-day Average True Range (typical daily move in $).</summary>
    public double? Atr14Day { get; set; }

    /// <summary>Average daily trading volume.</summary>
    public long? AvgVolume { get; set; }

    /// <summary>Number of shares available to trade (low float = volatile).</summary>
    public long? FloatShares { get; set; }

    /// <summary>Short interest as % of float.</summary>
    public double? ShortInterestPercent { get; set; }

    /// <summary>Days to cover (short interest / avg volume).</summary>
    public double? DaysToCover { get; set; }

    // ========================================================================
    // Fundamentals
    // ========================================================================

    /// <summary>Sector classification (e.g., "Technology", "Healthcare").</summary>
    public string? Sector { get; set; }

    /// <summary>Industry within sector (e.g., "Semiconductors", "Software").</summary>
    public string? Industry { get; set; }

    /// <summary>Market capitalization in USD.</summary>
    public long? MarketCap { get; set; }

    /// <summary>Next earnings date (avoid trading around this).</summary>
    public DateOnly? EarningsDate { get; set; }

    /// <summary>Days until next earnings (negative if past).</summary>
    [JsonIgnore]
    public int? DaysToEarnings => EarningsDate.HasValue 
        ? (EarningsDate.Value.ToDateTime(TimeOnly.MinValue) - DateTime.Today).Days 
        : null;

    /// <summary>Next ex-dividend date.</summary>
    public DateOnly? ExDividendDate { get; set; }

    /// <summary>52-week high price.</summary>
    public double? High52Week { get; set; }

    /// <summary>52-week low price.</summary>
    public double? Low52Week { get; set; }

    // ========================================================================
    // Correlation & Sector Data
    // ========================================================================

    /// <summary>Correlation with SPY (-1 to +1).</summary>
    public double? CorrelationSpy { get; set; }

    /// <summary>Sector ETF symbol to watch (e.g., XLK for tech).</summary>
    public string? SectorEtf { get; set; }

    /// <summary>Correlation with sector ETF.</summary>
    public double? CorrelationSector { get; set; }

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

    /// <summary>Whether this is a high beta stock (more volatile than market).</summary>
    [JsonIgnore]
    public bool IsHighBeta => Beta.HasValue && Beta.Value > 1.5;

    /// <summary>Whether this is a low float stock (prone to volatility spikes).</summary>
    [JsonIgnore]
    public bool IsLowFloat => FloatShares.HasValue && FloatShares.Value < 20_000_000;

    /// <summary>Whether this has high short interest (squeeze potential).</summary>
    [JsonIgnore]
    public bool HasHighShortInterest => ShortInterestPercent.HasValue && ShortInterestPercent.Value > 15;

    /// <summary>Whether earnings are within 2 days (high risk period).</summary>
    [JsonIgnore]
    public bool IsNearEarnings => DaysToEarnings.HasValue && Math.Abs(DaysToEarnings.Value) <= 2;

    /// <summary>Whether stock is near 52-week high (within 5%).</summary>
    [JsonIgnore]
    public bool IsNear52WeekHigh => High52Week.HasValue && High52Week.Value > 0;

    /// <summary>Whether stock is near 52-week low (within 5%).</summary>
    [JsonIgnore]
    public bool IsNear52WeekLow => Low52Week.HasValue && Low52Week.Value > 0;

    /// <summary>Gets volatility classification based on Beta and ATR.</summary>
    [JsonIgnore]
    public VolatilityLevel Volatility
    {
        get
        {
            if (!Beta.HasValue)
                return VolatilityLevel.Unknown;
            
            if (Beta.Value > 2.0 || IsLowFloat)
                return VolatilityLevel.Extreme;
            if (Beta.Value > 1.5)
                return VolatilityLevel.High;
            if (Beta.Value > 0.8)
                return VolatilityLevel.Normal;
            return VolatilityLevel.Low;
        }
    }

    /// <summary>Suggested position size reduction based on volatility (0.0-1.0 multiplier).</summary>
    [JsonIgnore]
    public double VolatilityPositionMultiplier => Volatility switch
    {
        VolatilityLevel.Extreme => 0.25,  // Only 25% of normal size
        VolatilityLevel.High => 0.50,     // 50% of normal size
        VolatilityLevel.Normal => 1.0,    // Full size
        VolatilityLevel.Low => 1.25,      // Can size up slightly
        _ => 1.0
    };

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

        // Apply universal adjustments
        
        // Be cautious around earnings
        if (IsNearEarnings)
            adjustment -= 20; // High risk, reduce score significantly

        // High volatility stocks need stronger signals
        if (Volatility == VolatilityLevel.Extreme)
            adjustment -= 15; // Require stronger conviction on extreme volatility
        else if (Volatility == VolatilityLevel.High)
            adjustment -= 5;

        // High short interest can accelerate moves in either direction
        if (HasHighShortInterest && isLong)
            adjustment += 5; // Squeeze potential helps longs

        return adjustment;
    }

    /// <summary>
    /// Gets the recommended take profit distance in dollars based on ATR.
    /// </summary>
    public double GetRecommendedTakeProfitDistance(double multiplier = 2.0)
    {
        if (!Atr14Day.HasValue || Atr14Day.Value <= 0)
            return 0;
        
        return Atr14Day.Value * multiplier;
    }

    /// <summary>
    /// Gets the recommended stop loss distance in dollars based on ATR.
    /// </summary>
    public double GetRecommendedStopLossDistance(double multiplier = 1.5)
    {
        if (!Atr14Day.HasValue || Atr14Day.Value <= 0)
            return 0;
        
        return Atr14Day.Value * multiplier;
    }

    /// <summary>
    /// Checks if the current price is within X% of 52-week high.
    /// </summary>
    public bool IsWithinPercentOf52WeekHigh(double currentPrice, double percent = 5.0)
    {
        if (!High52Week.HasValue || High52Week.Value <= 0)
            return false;
        
        double distance = (High52Week.Value - currentPrice) / High52Week.Value * 100;
        return distance >= 0 && distance <= percent;
    }

    /// <summary>
    /// Checks if the current price is within X% of 52-week low.
    /// </summary>
    public bool IsWithinPercentOf52WeekLow(double currentPrice, double percent = 5.0)
    {
        if (!Low52Week.HasValue || Low52Week.Value <= 0)
            return false;
        
        double distance = (currentPrice - Low52Week.Value) / Low52Week.Value * 100;
        return distance >= 0 && distance <= percent;
    }

    /// <summary>
    /// Gets a trading risk assessment based on all metadata factors.
    /// </summary>
    public TradingRiskLevel GetTradingRiskLevel()
    {
        int riskScore = 0;

        // High volatility adds risk
        if (Volatility == VolatilityLevel.Extreme) riskScore += 3;
        else if (Volatility == VolatilityLevel.High) riskScore += 2;
        
        // Low float adds risk
        if (IsLowFloat) riskScore += 2;
        
        // High short interest adds risk (can move sharply both ways)
        if (HasHighShortInterest) riskScore += 1;
        
        // Near earnings is risky
        if (IsNearEarnings) riskScore += 3;

        return riskScore switch
        {
            >= 5 => TradingRiskLevel.Extreme,
            >= 3 => TradingRiskLevel.High,
            >= 1 => TradingRiskLevel.Moderate,
            _ => TradingRiskLevel.Low
        };
    }
}

/// <summary>
/// Overall trading risk assessment for a ticker.
/// </summary>
public enum TradingRiskLevel
{
    Low,      // Safe to trade with normal position sizing
    Moderate, // Some caution needed, consider reducing position
    High,     // Significant risk, reduce position substantially
    Extreme   // Very risky, consider not trading or minimal position
}
