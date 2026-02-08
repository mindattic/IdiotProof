// ============================================================================
// WeightedScoreCalculator - Uses LearnedWeights to Calculate Trading Scores
// ============================================================================
//
// This replaces the hardcoded MarketScoreCalculator weights with learned,
// dynamically evolving weights that interact with each other.
//
// The key insight is that indicators don't work in isolation - RSI being
// overbought means different things depending on ADX trend strength,
// time of day, and recent price action.
//
// ============================================================================

using IdiotProof.Helpers;

namespace IdiotProof.Learning;

/// <summary>
/// Input snapshot for weighted score calculation.
/// Extends IndicatorSnapshot with additional context.
/// </summary>
public readonly struct ExtendedSnapshot
{
    // Core indicators (from IndicatorSnapshot)
    public double Price { get; init; }
    public double Vwap { get; init; }
    public double Ema9 { get; init; }
    public double Ema21 { get; init; }
    public double Ema50 { get; init; }
    public double Rsi { get; init; }
    public double Macd { get; init; }
    public double MacdSignal { get; init; }
    public double MacdHistogram { get; init; }
    public double Adx { get; init; }
    public double PlusDi { get; init; }
    public double MinusDi { get; init; }
    public double VolumeRatio { get; init; }
    public double BollingerUpper { get; init; }
    public double BollingerLower { get; init; }
    public double BollingerMiddle { get; init; }
    public double Atr { get; init; }
    
    // Extended context
    public double Momentum { get; init; }
    public double Roc { get; init; }
    public TimeOnly TimeOfDay { get; init; }
    
    // Pattern detection (simple versions)
    public bool IsHigherLow { get; init; }
    public bool IsLowerHigh { get; init; }
    public bool IsNearLod { get; init; }
    public bool IsNearHod { get; init; }
    public bool IsVwapRejection { get; init; }
    public bool IsVwapReclaim { get; init; }
    
    /// <summary>
    /// Converts from basic IndicatorSnapshot.
    /// </summary>
    public static ExtendedSnapshot FromBasic(IndicatorSnapshot basic, TimeOnly time)
    {
        return new ExtendedSnapshot
        {
            Price = basic.Price,
            Vwap = basic.Vwap,
            Ema9 = basic.Ema9,
            Ema21 = basic.Ema21,
            Ema50 = basic.Ema50,
            Rsi = basic.Rsi,
            Macd = basic.Macd,
            MacdSignal = basic.MacdSignal,
            MacdHistogram = basic.MacdHistogram,
            Adx = basic.Adx,
            PlusDi = basic.PlusDi,
            MinusDi = basic.MinusDi,
            VolumeRatio = basic.VolumeRatio,
            BollingerUpper = basic.BollingerUpper,
            BollingerLower = basic.BollingerLower,
            BollingerMiddle = basic.BollingerMiddle,
            Atr = basic.Atr,
            TimeOfDay = time
        };
    }
}

/// <summary>
/// Calculates trading scores using learned weights.
/// </summary>
public static class WeightedScoreCalculator
{
    // Indicator indices for weight lookup
    private const int IDX_VWAP = 0;
    private const int IDX_EMA9 = 1;
    private const int IDX_EMA21 = 2;
    private const int IDX_EMA50 = 3;
    private const int IDX_RSI = 4;
    private const int IDX_MACD = 5;
    private const int IDX_ADX = 6;
    private const int IDX_VOLUME = 7;
    private const int IDX_BOLLINGER = 8;
    private const int IDX_MOMENTUM = 9;
    private const int IDX_ROC = 10;
    private const int IDX_ATR = 11;
    private const int IDX_PATTERN1 = 12;
    private const int IDX_PATTERN2 = 13;
    private const int IDX_PATTERN3 = 14;
    private const int IDX_PATTERN4 = 15;
    
    /// <summary>
    /// Calculates market score using learned weights.
    /// This is the "AI" version that uses interaction matrices.
    /// </summary>
    public static (double score, bool shouldEnterLong, bool shouldEnterShort, bool shouldExit) 
        Calculate(ExtendedSnapshot snap, LearnedWeights weights)
    {
        // Step 1: Calculate raw indicator scores (normalized to -1 to +1)
        var rawScores = new double[16];
        rawScores[IDX_VWAP] = CalculateVwapScore(snap);
        rawScores[IDX_EMA9] = CalculateEmaScore(snap.Price, snap.Ema9);
        rawScores[IDX_EMA21] = CalculateEmaScore(snap.Price, snap.Ema21);
        rawScores[IDX_EMA50] = CalculateEmaScore(snap.Price, snap.Ema50);
        rawScores[IDX_RSI] = CalculateRsiScore(snap.Rsi);
        rawScores[IDX_MACD] = CalculateMacdScore(snap);
        rawScores[IDX_ADX] = CalculateAdxScore(snap);
        rawScores[IDX_VOLUME] = CalculateVolumeScore(snap);
        rawScores[IDX_BOLLINGER] = CalculateBollingerScore(snap);
        rawScores[IDX_MOMENTUM] = CalculateMomentumScore(snap.Momentum);
        rawScores[IDX_ROC] = CalculateRocScore(snap.Roc);
        rawScores[IDX_ATR] = 0; // ATR is meta-indicator, used for sizing not direction
        rawScores[IDX_PATTERN1] = snap.IsHigherLow ? 0.5 : (snap.IsLowerHigh ? -0.5 : 0);
        rawScores[IDX_PATTERN2] = snap.IsNearLod ? 0.3 : (snap.IsNearHod ? -0.3 : 0);
        rawScores[IDX_PATTERN3] = snap.IsVwapReclaim ? 0.4 : (snap.IsVwapRejection ? -0.4 : 0);
        rawScores[IDX_PATTERN4] = 0;
        
        // Step 2: Determine market regime and get multipliers
        bool isTrending = snap.Adx > 25;
        bool isRanging = snap.Adx < 20;
        var regimeMultipliers = isTrending ? weights.TrendingMultipliers : 
                                 isRanging ? weights.RangingMultipliers : 
                                 weights.IndicatorWeights; // Use base weights for neutral
        
        // Step 3: Apply interaction matrix - this is where the magic happens
        // Each indicator's effect is modified by all other indicators
        var modifiedScores = new double[16];
        for (int i = 0; i < 16; i++)
        {
            modifiedScores[i] = rawScores[i] * weights.IndicatorWeights[i] * regimeMultipliers[i];
            
            // Add interaction effects
            for (int j = 0; j < 16; j++)
            {
                if (i != j)
                {
                    // Interaction: how much does indicator j modify indicator i's importance?
                    double interaction = weights.InteractionMatrix[j * 16 + i];
                    modifiedScores[i] += rawScores[j] * interaction;
                }
            }
        }
        
        // Step 4: Apply time-of-day weight
        int timeWindow = GetTimeWindow(snap.TimeOfDay);
        double timeWeight = weights.TimeWeights[Math.Clamp(timeWindow, 0, 15)];
        
        // Step 5: Apply pattern weights
        double patternBonus = 0;
        if (snap.IsHigherLow) patternBonus += weights.PatternWeights[0];
        if (snap.IsLowerHigh) patternBonus += weights.PatternWeights[1];
        if (snap.IsNearLod) patternBonus += weights.PatternWeights[2];
        if (snap.IsNearHod) patternBonus += weights.PatternWeights[3];
        if (snap.IsVwapReclaim) patternBonus += weights.PatternWeights[4];
        if (snap.IsVwapRejection) patternBonus += weights.PatternWeights[5];
        
        // Step 6: Sum and normalize
        double totalScore = 0;
        for (int i = 0; i < 16; i++)
        {
            totalScore += modifiedScores[i];
        }
        
        // Apply time weight and pattern bonus
        totalScore = (totalScore * timeWeight) + patternBonus;
        
        // Normalize to -100 to +100 range
        double normalizedScore = Math.Clamp(totalScore * 100, -100, 100);
        
        // Step 7: Determine entry/exit using learned thresholds
        bool isTrendingNow = snap.Adx > 25;
        bool isVolatile = snap.Atr > 0 && (snap.Atr / snap.Price) > 0.02;
        
        double longThreshold = weights.EntryBiases[0]; // base_long
        double shortThreshold = weights.EntryBiases[1]; // base_short
        
        if (isTrendingNow)
        {
            longThreshold += weights.EntryBiases[2];
            shortThreshold += weights.EntryBiases[3];
        }
        else if (isRanging)
        {
            longThreshold += weights.EntryBiases[4];
            shortThreshold += weights.EntryBiases[5];
        }
        
        if (isVolatile)
        {
            longThreshold += weights.EntryBiases[6];
            shortThreshold += weights.EntryBiases[7];
        }
        
        bool shouldEnterLong = normalizedScore >= longThreshold;
        bool shouldEnterShort = normalizedScore <= shortThreshold;
        
        // Exit sensitivity
        double exitThreshold = longThreshold * weights.ExitSensitivity[0];
        bool shouldExit = Math.Abs(normalizedScore) < exitThreshold;
        
        return (normalizedScore, shouldEnterLong, shouldEnterShort, shouldExit);
    }
    
    /// <summary>
    /// Calculates fitness score for a set of trades.
    /// Higher is better.
    /// </summary>
    public static double CalculateFitness(
        int totalTrades, 
        int wins, 
        double totalPnL, 
        double maxDrawdown,
        double sharpeRatio)
    {
        if (totalTrades == 0) return -1000;
        
        double winRate = (double)wins / totalTrades;
        double avgPnL = totalPnL / totalTrades;
        
        // Composite fitness:
        // - Win rate matters (0-100 points)
        // - Average PnL matters (scaled)
        // - Sharpe ratio matters a lot (risk-adjusted returns)
        // - Max drawdown penalty
        // - Trade count bonus (we want some activity)
        
        double fitness = 
            winRate * 40 +                              // Win rate contribution
            Math.Clamp(avgPnL * 20, -50, 50) +          // PnL contribution (bounded)
            sharpeRatio * 30 +                          // Sharpe contribution
            Math.Min(totalTrades / 10.0, 10) +          // Activity bonus (max 10)
            (maxDrawdown > 20 ? -(maxDrawdown - 20) : 0); // Drawdown penalty
        
        return fitness;
    }
    
    private static int GetTimeWindow(TimeOnly time)
    {
        // 16 windows of 30 minutes each, starting at 4:00 AM
        int minutesSince4am = (time.Hour - 4) * 60 + time.Minute;
        if (minutesSince4am < 0) minutesSince4am += 24 * 60;
        return minutesSince4am / 30;
    }
    
    private static double CalculateVwapScore(ExtendedSnapshot snap)
    {
        if (snap.Vwap <= 0) return 0;
        double pctDiff = (snap.Price - snap.Vwap) / snap.Vwap;
        return Math.Clamp(pctDiff * 20, -1, 1); // 5% diff = ±1
    }
    
    private static double CalculateEmaScore(double price, double ema)
    {
        if (ema <= 0) return 0;
        return price > ema ? 0.5 : -0.5;
    }
    
    private static double CalculateRsiScore(double rsi)
    {
        if (rsi >= 70) return -(rsi - 70) / 30.0; // 70=0, 100=-1
        if (rsi <= 30) return (30 - rsi) / 30.0;   // 30=0, 0=+1
        return (rsi - 50) / 40.0; // 50=0, 30=-0.5, 70=+0.5
    }
    
    private static double CalculateMacdScore(ExtendedSnapshot snap)
    {
        bool bullish = snap.Macd > snap.MacdSignal;
        double histogramEffect = Math.Clamp(snap.MacdHistogram * 10, -0.5, 0.5);
        return (bullish ? 0.3 : -0.3) + histogramEffect;
    }
    
    private static double CalculateAdxScore(ExtendedSnapshot snap)
    {
        bool bullishDi = snap.PlusDi > snap.MinusDi;
        double strength = Math.Min(snap.Adx / 50.0, 1.0);
        return bullishDi ? strength : -strength;
    }
    
    private static double CalculateVolumeScore(ExtendedSnapshot snap)
    {
        if (snap.VolumeRatio <= 1.0) return 0;
        double excess = Math.Min((snap.VolumeRatio - 1.0) * 2, 1.0);
        return snap.Price > snap.Vwap ? excess : -excess;
    }
    
    private static double CalculateBollingerScore(ExtendedSnapshot snap)
    {
        if (snap.BollingerUpper <= snap.BollingerLower) return 0;
        double bandwidth = snap.BollingerUpper - snap.BollingerLower;
        double position = (snap.Price - snap.BollingerMiddle) / (bandwidth / 2);
        
        // Near bands = mean reversion signal
        if (position > 0.9) return -0.3; // Overbought
        if (position < -0.9) return 0.3; // Oversold
        return 0;
    }
    
    private static double CalculateMomentumScore(double momentum)
    {
        return Math.Clamp(momentum / 10.0, -1, 1);
    }
    
    private static double CalculateRocScore(double roc)
    {
        return Math.Clamp(roc / 5.0, -1, 1);
    }
}
