// ============================================================================
// StrategyPicker - Analyzes market data to find breakout-pullback candidates
// ============================================================================
//
// Replicates the pro trader's methodology for identifying setups:
// 1. Scan for stocks near key resistance levels
// 2. Look for consolidation patterns (wedges, flags, bases)
// 3. Identify catalyst or momentum
// 4. Calculate optimal entry, targets, and invalidation
//
// ============================================================================

using IdiotProof.Shared;

namespace IdiotProof.Scripting;

/// <summary>
/// Criteria for picking breakout-pullback candidates.
/// </summary>
public sealed class SetupCriteria
{
    // Price range filters
    public double MinPrice { get; set; } = 0.30;
    public double MaxPrice { get; set; } = 20.00;
    
    // Volume requirements
    public double MinAverageVolume { get; set; } = 500_000;
    public double MinVolumeRatio { get; set; } = 1.5;  // Current vs average
    
    // Gap/momentum requirements  
    public double MinGapPercent { get; set; } = 3.0;
    public bool RequireGap { get; set; } = false;
    
    // Technical requirements
    public bool RequireNearResistance { get; set; } = true;
    public double MaxDistanceToResistancePercent { get; set; } = 5.0;
    public bool RequireConsolidation { get; set; } = false;
    
    // Risk parameters
    public double TargetRiskReward { get; set; } = 2.5;
    public double MaxRiskPercent { get; set; } = 5.0;
}

/// <summary>
/// Analyzes stocks to find breakout-pullback setups like a pro trader.
/// </summary>
public sealed class StrategyPicker
{
    private readonly SetupCriteria criteria;
    
    public StrategyPicker(SetupCriteria? criteria = null)
    {
        criteria = criteria ?? new SetupCriteria();
    }
    
    /// <summary>
    /// Analyzes a stock and generates a trading setup if it qualifies.
    /// </summary>
    public TradingSetup? Analyze(StockSnapshot snapshot)
    {
        // Check basic filters
        if (snapshot.Price < criteria.MinPrice || snapshot.Price > criteria.MaxPrice)
            return null;
            
        if (snapshot.AverageVolume < criteria.MinAverageVolume)
            return null;
            
        if (criteria.RequireGap && Math.Abs(snapshot.GapPercent) < criteria.MinGapPercent)
            return null;
        
        // Find resistance level (key breakout trigger)
        var resistance = FindResistanceLevel(snapshot);
        if (resistance == null)
            return null;
            
        // Check if near resistance
        var distanceToResistance = (resistance.Value - snapshot.Price) / snapshot.Price * 100;
        if (distanceToResistance > criteria.MaxDistanceToResistancePercent)
            return null;
            
        // Determine support level
        var support = FindSupportLevel(snapshot, resistance.Value);
        
        // Calculate invalidation (stop loss)
        var invalidation = CalculateInvalidation(snapshot, support);
        
        // Calculate targets based on risk:reward
        var targets = CalculateTargets(resistance.Value, invalidation, criteria.TargetRiskReward);
        
        // Determine bias and pattern
        var (bias, pattern) = DetermineBiasAndPattern(snapshot);
        
        return new TradingSetup
        {
            Symbol = snapshot.Symbol,
            Bias = bias,
            Pattern = pattern,
            TriggerPrice = resistance.Value,
            SupportPrice = support,
            InvalidationPrice = invalidation,
            TargetPrices = targets,
            Status = SetupStatus.Watching
        };
    }
    
    private double? FindResistanceLevel(StockSnapshot snapshot)
    {
        // Priority order for resistance:
        // 1. Recent high (premarket or yesterday)
        // 2. Round number levels
        // 3. VWAP if above current price
        
        if (snapshot.PremarketHigh > 0 && snapshot.PremarketHigh > snapshot.Price)
            return snapshot.PremarketHigh;
            
        if (snapshot.YesterdayHigh > 0 && snapshot.YesterdayHigh > snapshot.Price)
            return snapshot.YesterdayHigh;
            
        // Find nearest round number resistance
        var price = snapshot.Price;
        double roundLevel;
        
        if (price < 1)
            roundLevel = Math.Ceiling(price * 20) / 20;  // $0.05 increments
        else if (price < 5)
            roundLevel = Math.Ceiling(price * 10) / 10;  // $0.10 increments
        else if (price < 20)
            roundLevel = Math.Ceiling(price * 4) / 4;    // $0.25 increments
        else
            roundLevel = Math.Ceiling(price);            // $1.00 increments
            
        return roundLevel > price ? roundLevel : null;
    }
    
    private double FindSupportLevel(StockSnapshot snapshot, double resistance)
    {
        // Support levels in priority order:
        // 1. VWAP (strongest institutional reference)
        // 2. Prior resistance turned support
        // 3. Premarket low or yesterday close
        
        if (snapshot.Vwap > 0 && snapshot.Vwap < resistance)
            return snapshot.Vwap;
            
        if (snapshot.YesterdayClose > 0 && snapshot.YesterdayClose < resistance)
            return snapshot.YesterdayClose;
            
        // Default: 3-5% below resistance (resistance becomes support concept)
        return resistance * 0.96;
    }
    
    private double CalculateInvalidation(StockSnapshot snapshot, double support)
    {
        // Invalidation is typically:
        // - 2-3% below support for tight setups
        // - Below premarket low for premarket plays
        // - Below yesterday's close for swing setups
        
        var belowSupport = support * 0.97;  // 3% below support
        
        if (snapshot.PremarketLow > 0)
            return Math.Min(belowSupport, snapshot.PremarketLow * 0.99);
            
        return belowSupport;
    }
    
    private List<double> CalculateTargets(double trigger, double invalidation, double targetRR)
    {
        var risk = trigger - invalidation;
        var targets = new List<double>();
        
        // T1: Quick scalp (1.5:1 R:R)
        targets.Add(Math.Round(trigger + risk * 1.5, 2));
        
        // T2: Main target (2.5:1 R:R)
        targets.Add(Math.Round(trigger + risk * 2.5, 2));
        
        // T3: Full runner (4:1 R:R) - only for strong setups
        if (targetRR >= 3.0)
            targets.Add(Math.Round(trigger + risk * 4.0, 2));
        
        return targets;
    }
    
    private (string bias, string pattern) DetermineBiasAndPattern(StockSnapshot snapshot)
    {
        // Determine bias based on gap direction and momentum
        string bias;
        if (snapshot.GapPercent > 5)
            bias = "Strong bullish gap";
        else if (snapshot.GapPercent > 0)
            bias = "Bullish continuation";
        else if (snapshot.GapPercent < -5)
            bias = "Bearish gap (fade candidate)";
        else
            bias = "Neutral - watch for direction";
        
        // Determine pattern based on price action
        string pattern;
        if (snapshot.IsNearHOD && snapshot.VolumeRatio > 2)
            pattern = "Momentum breakout";
        else if (snapshot.IsNearVwap)
            pattern = "VWAP bounce setup";
        else if (snapshot.ConsolidationBars >= 5)
            pattern = "Coiled consolidation breakout";
        else if (snapshot.IsWedgePattern)
            pattern = "Wedge breakout";
        else
            pattern = "Breakout pullback";
        
        return (bias, pattern);
    }
}

/// <summary>
/// Snapshot of current stock data for analysis.
/// </summary>
public sealed class StockSnapshot
{
    public string Symbol { get; set; } = "";
    public double Price { get; set; }
    public double Vwap { get; set; }
    public long Volume { get; set; }
    public double AverageVolume { get; set; }
    public double VolumeRatio => AverageVolume > 0 ? Volume / AverageVolume : 0;
    
    // Gap info
    public double YesterdayClose { get; set; }
    public double GapPercent => YesterdayClose > 0 ? (Price - YesterdayClose) / YesterdayClose * 100 : 0;
    
    // Levels
    public double PremarketHigh { get; set; }
    public double PremarketLow { get; set; }
    public double YesterdayHigh { get; set; }
    public double YesterdayLow { get; set; }
    
    // Patterns
    public bool IsNearHOD => PremarketHigh > 0 && Math.Abs(Price - PremarketHigh) / PremarketHigh < 0.02;
    public bool IsNearVwap => Vwap > 0 && Math.Abs(Price - Vwap) / Vwap < 0.01;
    public int ConsolidationBars { get; set; }
    public bool IsWedgePattern { get; set; }
    
    // Indicators
    public double? Ema9 { get; set; }
    public double? Adx { get; set; }
    public double? Rsi { get; set; }
}
