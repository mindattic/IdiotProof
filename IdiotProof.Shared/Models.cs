// ============================================================================
// IdiotProof.Shared - Common Models
// ============================================================================
// Shared between Core, Web, and Scripting projects
// ============================================================================

namespace IdiotProof.Shared;

/// <summary>
/// Trade direction enumeration.
/// </summary>
public enum TradeDirection
{
    Long,
    Short
}

/// <summary>
/// Trading session types.
/// </summary>
public enum TradingSession
{
    Premarket,    // 4:00 AM - 9:30 AM ET
    RTH,          // 9:30 AM - 4:00 PM ET (Regular Trading Hours)
    AfterHours,   // 4:00 PM - 8:00 PM ET
    Extended      // All sessions
}

/// <summary>
/// Order types.
/// </summary>
public enum OrderType
{
    Market,
    Limit,
    Stop,
    StopLimit,
    TrailingStop
}

/// <summary>
/// Price type for entry calculations.
/// </summary>
public enum PriceType
{
    Current,
    Bid,
    Ask,
    VWAP,
    Open,
    High,
    Low,
    Close
}

/// <summary>
/// Confidence grade (A+ through F).
/// </summary>
public enum ConfidenceGrade
{
    APlus,  // 85-100
    A,      // 75-84
    B,      // 65-74
    C,      // 55-64
    D,      // 45-54
    F       // 0-44
}

/// <summary>
/// Pre-calculated trade setup ready for execution.
/// </summary>
public sealed class TradeSetup
{
    public string SetupId { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public DateTime GeneratedUtc { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresUtc { get; set; } = DateTime.UtcNow.AddMinutes(10);
    
    // Symbol
    public string Symbol { get; set; } = "";
    public string? CompanyName { get; set; }
    
    // Direction
    public TradeDirection Direction { get; set; }
    public bool IsLong => Direction == TradeDirection.Long;
    
    // Entry
    public double EntryPrice { get; set; }
    public OrderType EntryType { get; set; } = OrderType.Limit;
    
    // Risk Management
    public double StopLoss { get; set; }
    public double TakeProfit { get; set; }
    public double TrailingStopPercent { get; set; }
    
    // Position Sizing
    public int Quantity { get; set; }
    public double RiskDollars { get; set; }
    public double RewardDollars { get; set; }
    public double RiskRewardRatio => RiskDollars > 0 ? RewardDollars / RiskDollars : 0;
    
    // Confidence
    public int ConfidenceScore { get; set; }
    public ConfidenceGrade Grade => ConfidenceScore switch
    {
        >= 85 => ConfidenceGrade.APlus,
        >= 75 => ConfidenceGrade.A,
        >= 65 => ConfidenceGrade.B,
        >= 55 => ConfidenceGrade.C,
        >= 45 => ConfidenceGrade.D,
        _ => ConfidenceGrade.F
    };
    
    // Rationale
    public string Rationale { get; set; } = "";
    public List<string> BullishFactors { get; set; } = [];
    public List<string> BearishFactors { get; set; } = [];
    
    // Calculated properties
    public double StopLossPercent => EntryPrice > 0 
        ? Math.Abs((StopLoss - EntryPrice) / EntryPrice * 100) : 0;
    public double TakeProfitPercent => EntryPrice > 0 
        ? Math.Abs((TakeProfit - EntryPrice) / EntryPrice * 100) : 0;
    public bool IsExpired => DateTime.UtcNow > ExpiresUtc;
}

/// <summary>
/// Risk limits to prevent catastrophic losses.
/// </summary>
public sealed class RiskLimits
{
    /// <summary>Maximum loss per trade in dollars.</summary>
    public double MaxLossPerTrade { get; set; } = 100.0;
    
    /// <summary>Maximum loss per day in dollars.</summary>
    public double MaxLossPerDay { get; set; } = 500.0;
    
    /// <summary>Maximum position size in dollars.</summary>
    public double MaxPositionSize { get; set; } = 5000.0;
    
    /// <summary>Maximum percent of account per trade.</summary>
    public double MaxAccountPercentPerTrade { get; set; } = 2.0;
    
    /// <summary>Minimum R:R ratio to allow trade.</summary>
    public double MinRiskRewardRatio { get; set; } = 1.5;
    
    /// <summary>Minimum confidence score to allow trade.</summary>
    public int MinConfidenceScore { get; set; } = 50;
    
    /// <summary>Maximum number of open positions.</summary>
    public int MaxOpenPositions { get; set; } = 5;
    
    /// <summary>
    /// Validates a trade setup against risk limits.
    /// </summary>
    public RiskCheckResult ValidateTrade(TradeSetup setup, double accountBalance, int currentOpenPositions)
    {
        var result = new RiskCheckResult();
        
        // Check max loss per trade
        if (setup.RiskDollars > MaxLossPerTrade)
        {
            result.AddError($"Risk ${setup.RiskDollars:F2} exceeds max ${MaxLossPerTrade:F2} per trade");
        }
        
        // Check position size
        var positionValue = setup.EntryPrice * setup.Quantity;
        if (positionValue > MaxPositionSize)
        {
            result.AddError($"Position ${positionValue:F2} exceeds max ${MaxPositionSize:F2}");
        }
        
        // Check account percent
        var accountPercent = (positionValue / accountBalance) * 100;
        if (accountPercent > MaxAccountPercentPerTrade)
        {
            result.AddError($"Position is {accountPercent:F1}% of account (max {MaxAccountPercentPerTrade}%)");
        }
        
        // Check R:R ratio
        if (setup.RiskRewardRatio < MinRiskRewardRatio)
        {
            result.AddWarning($"R:R ratio {setup.RiskRewardRatio:F1} below minimum {MinRiskRewardRatio}");
        }
        
        // Check confidence
        if (setup.ConfidenceScore < MinConfidenceScore)
        {
            result.AddWarning($"Confidence {setup.ConfidenceScore}% below minimum {MinConfidenceScore}%");
        }
        
        // Check open positions
        if (currentOpenPositions >= MaxOpenPositions)
        {
            result.AddError($"Already have {currentOpenPositions} positions open (max {MaxOpenPositions})");
        }
        
        return result;
    }
}

/// <summary>
/// Result of risk validation check.
/// </summary>
public sealed class RiskCheckResult
{
    public bool IsValid => Errors.Count == 0;
    public List<string> Errors { get; } = [];
    public List<string> Warnings { get; } = [];
    
    public void AddError(string message) => Errors.Add(message);
    public void AddWarning(string message) => Warnings.Add(message);
}

/// <summary>
/// Market indicator values at a point in time.
/// </summary>
public sealed class IndicatorSnapshot
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Symbol { get; set; } = "";
    public double Price { get; set; }
    
    // VWAP
    public double? Vwap { get; set; }
    public double? VwapDistance => Vwap.HasValue && Vwap.Value > 0 
        ? ((Price - Vwap.Value) / Vwap.Value) * 100 : null;
    
    // EMAs
    public double? Ema9 { get; set; }
    public double? Ema21 { get; set; }
    public double? Ema50 { get; set; }
    public double? Ema200 { get; set; }
    
    // RSI
    public double? Rsi { get; set; }
    public bool? HasBullishDivergence { get; set; }
    public bool? HasBearishDivergence { get; set; }
    
    // MACD
    public double? MacdLine { get; set; }
    public double? SignalLine { get; set; }
    public double? Histogram { get; set; }
    public bool IsMacdBullish => MacdLine > SignalLine;
    
    // ADX
    public double? Adx { get; set; }
    public double? PlusDI { get; set; }
    public double? MinusDI { get; set; }
    public bool IsTrending => Adx >= 25;
    public bool IsBullishTrend => PlusDI > MinusDI;
    
    // Volume
    public long Volume { get; set; }
    public double AverageVolume { get; set; }
    public double VolumeRatio => AverageVolume > 0 ? Volume / AverageVolume : 0;
    
    /// <summary>
    /// Calculates an overall market score from -100 to +100.
    /// </summary>
    public int CalculateMarketScore()
    {
        double score = 0;
        int factors = 0;
        
        // VWAP (15%)
        if (VwapDistance.HasValue)
        {
            var vwapScore = Math.Max(-100, Math.Min(100, VwapDistance.Value * 20));
            score += vwapScore * 0.15;
            factors++;
        }
        
        // EMA stack (20%)
        if (Ema9.HasValue && Ema21.HasValue && Ema50.HasValue)
        {
            int bullishCount = 0;
            if (Price > Ema9) bullishCount++;
            if (Price > Ema21) bullishCount++;
            if (Price > Ema50) bullishCount++;
            if (Ema9 > Ema21) bullishCount++;
            if (Ema21 > Ema50) bullishCount++;
            
            var emaScore = ((bullishCount / 5.0) - 0.5) * 200;
            score += emaScore * 0.20;
            factors++;
        }
        
        // RSI (15%)
        if (Rsi.HasValue)
        {
            double rsiScore;
            if (Rsi <= 30) rsiScore = 100;  // Oversold = bullish
            else if (Rsi >= 70) rsiScore = -100;  // Overbought = bearish
            else rsiScore = ((50 - Rsi.Value) / 20) * 100;  // Linear between
            
            // Divergence bonus
            if (HasBullishDivergence == true) rsiScore += 30;
            if (HasBearishDivergence == true) rsiScore -= 30;
            
            score += Math.Max(-100, Math.Min(100, rsiScore)) * 0.15;
            factors++;
        }
        
        // MACD (20%)
        if (MacdLine.HasValue && SignalLine.HasValue)
        {
            double macdScore = IsMacdBullish ? 50 : -50;
            if (Histogram.HasValue)
            {
                macdScore += Math.Max(-50, Math.Min(50, Histogram.Value * 10));
            }
            score += macdScore * 0.20;
            factors++;
        }
        
        // ADX (20%)
        if (Adx.HasValue && PlusDI.HasValue && MinusDI.HasValue)
        {
            var direction = IsBullishTrend ? 1 : -1;
            var strength = Math.Min(100, Adx.Value * 2);
            var adxScore = direction * strength;
            score += adxScore * 0.20;
            factors++;
        }
        
        // Volume (10%)
        if (VolumeRatio > 0)
        {
            // Volume confirms current direction
            var volumeBonus = Math.Min(50, (VolumeRatio - 1) * 25);
            var currentDirection = score > 0 ? 1 : -1;
            score += (volumeBonus * currentDirection) * 0.10;
        }
        
        return (int)Math.Max(-100, Math.Min(100, score));
    }
}

/// <summary>
/// AI analysis of a chart/setup.
/// </summary>
public sealed class AiAnalysis
{
    public string Symbol { get; set; } = "";
    public DateTime AnalyzedUtc { get; set; } = DateTime.UtcNow;
    
    // Overall assessment
    public string Summary { get; set; } = "";
    public int ConfidenceScore { get; set; }
    public TradeDirection? RecommendedDirection { get; set; }
    
    // Detailed analysis
    public List<string> BullishSignals { get; set; } = [];
    public List<string> BearishSignals { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    
    // Historical context
    public string? SimilarSetupReference { get; set; }
    public double? HistoricalWinRate { get; set; }
    
    // Risk assessment
    public string RiskLevel { get; set; } = "Medium";  // Low, Medium, High, Extreme
    public double SuggestedStopPercent { get; set; }
    public double SuggestedTargetPercent { get; set; }
}
