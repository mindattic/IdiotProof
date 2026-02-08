// ============================================================================
// MarketScoreCalculator - THE SINGLE SOURCE OF TRUTH FOR MARKET SCORING
// ============================================================================
//
// CRITICAL: This is the ONLY place market score calculation logic lives.
// Both live trading (StrategyRunner) and backtesting (AutonomousBacktester)
// MUST use this same code. Any change here affects BOTH live and backtest.
//
// This ensures backtesting results accurately predict live trading performance.
//
// Weights: VWAP 13%, EMA 18%, RSI 14%, MACD 20%, ADX 20%, Volume 10%, Bollinger 5%
//
// ============================================================================

namespace IdiotProof.Core.Helpers;

/// <summary>
/// Input data for market score calculation.
/// All indicator values needed to calculate the score.
/// </summary>
public readonly struct IndicatorSnapshot
{
    public double Price { get; init; }
    public double Vwap { get; init; }
    
    // EMA values
    public double Ema9 { get; init; }
    public double Ema21 { get; init; }
    public double Ema50 { get; init; }
    
    // RSI
    public double Rsi { get; init; }
    
    // MACD
    public double Macd { get; init; }
    public double MacdSignal { get; init; }
    public double MacdHistogram { get; init; }
    
    // ADX
    public double Adx { get; init; }
    public double PlusDi { get; init; }
    public double MinusDi { get; init; }
    
    // Volume
    public double VolumeRatio { get; init; }
    
    // Bollinger Bands
    public double BollingerUpper { get; init; }
    public double BollingerLower { get; init; }
    public double BollingerMiddle { get; init; }
    
    // ATR (for TP/SL calculation)
    public double Atr { get; init; }
}

/// <summary>
/// Result of market score calculation with component breakdown.
/// </summary>
public readonly struct MarketScoreResult
{
    public int TotalScore { get; init; }
    public int VwapScore { get; init; }
    public int EmaScore { get; init; }
    public int RsiScore { get; init; }
    public int MacdScore { get; init; }
    public int AdxScore { get; init; }
    public int VolumeScore { get; init; }
    public int BollingerScore { get; init; }
    
    /// <summary>
    /// True if +DI > -DI (bullish directional movement).
    /// </summary>
    public bool IsDiPositive { get; init; }
    
    /// <summary>
    /// True if MACD > Signal (bullish MACD).
    /// </summary>
    public bool IsMacdBullish { get; init; }
}

/// <summary>
/// THE SINGLE SOURCE OF TRUTH for market score calculation.
/// Used by BOTH live trading and backtesting.
/// </summary>
public static class MarketScoreCalculator
{
    // ========================================================================
    // WEIGHTS - Same for live and backtest
    // ========================================================================
    public const double WeightVwap = 0.13;
    public const double WeightEma = 0.18;
    public const double WeightRsi = 0.14;
    public const double WeightMacd = 0.20;
    public const double WeightAdx = 0.20;
    public const double WeightVolume = 0.10;
    public const double WeightBollinger = 0.05;
    
    /// <summary>
    /// Calculates market score using the unified formula.
    /// This is THE ONLY score calculation - used by both live and backtest.
    /// </summary>
    public static MarketScoreResult Calculate(IndicatorSnapshot snapshot)
    {
        // ====================================================================
        // VWAP Position (13% weight)
        // ====================================================================
        int vwapScore = 0;
        if (snapshot.Vwap > 0)
        {
            double vwapDiff = (snapshot.Price - snapshot.Vwap) / snapshot.Vwap * 100;
            vwapScore = (int)Math.Clamp(vwapDiff * 20, -100, 100);
        }

        // ====================================================================
        // EMA Stack Alignment (18% weight)
        // ====================================================================
        int emaScore = 0;
        int bullishCount = 0, bearishCount = 0;
        
        if (snapshot.Ema9 > 0)
        {
            if (snapshot.Price > snapshot.Ema9) bullishCount++; else bearishCount++;
        }
        if (snapshot.Ema21 > 0)
        {
            if (snapshot.Price > snapshot.Ema21) bullishCount++; else bearishCount++;
        }
        if (snapshot.Ema50 > 0)
        {
            if (snapshot.Price > snapshot.Ema50) bullishCount++; else bearishCount++;
        }
        
        int total = bullishCount + bearishCount;
        if (total > 0)
        {
            emaScore = (int)((bullishCount - bearishCount) / (double)total * 100);
        }

        // ====================================================================
        // RSI (14% weight)
        // ====================================================================
        int rsiScore = 0;
        double rsi = snapshot.Rsi;
        
        if (rsi >= 70)
            rsiScore = (int)((70 - rsi) * 3.33); // 70->0, 100->-100
        else if (rsi <= 30)
            rsiScore = (int)((30 - rsi) * 3.33); // 30->0, 0->+100
        else
            rsiScore = (int)((rsi - 50) * 2.5); // 30->-50, 70->+50
        
        rsiScore = (int)Math.Clamp(rsiScore, -100, 100);

        // ====================================================================
        // MACD (20% weight)
        // ====================================================================
        int macdScore = 0;
        bool isMacdBullish = snapshot.Macd > snapshot.MacdSignal;
        double histogram = snapshot.MacdHistogram;
        
        macdScore = isMacdBullish ? 50 : -50;
        macdScore += (int)Math.Clamp(histogram * 500, -50, 50);
        macdScore = (int)Math.Clamp(macdScore, -100, 100);

        // ====================================================================
        // ADX Trend Strength (20% weight)
        // ====================================================================
        int adxScore = 0;
        bool isDiPositive = snapshot.PlusDi > snapshot.MinusDi;
        int magnitude = (int)Math.Min(snapshot.Adx * 2, 100);
        adxScore = isDiPositive ? magnitude : -magnitude;

        // ====================================================================
        // Volume (10% weight)
        // ====================================================================
        int volumeScore = 0;
        if (snapshot.VolumeRatio > 1.0)
        {
            int volumeMagnitude = (int)Math.Min((snapshot.VolumeRatio - 1.0) * 100, 100);
            volumeScore = snapshot.Price > snapshot.Vwap ? volumeMagnitude : -volumeMagnitude;
        }

        // ====================================================================
        // Bollinger Bands (5% weight)
        // ====================================================================
        int bollingerScore = CalculateBollingerScore(
            snapshot.Price, 
            snapshot.BollingerUpper, 
            snapshot.BollingerLower, 
            snapshot.BollingerMiddle);

        // ====================================================================
        // Weighted Total
        // ====================================================================
        double totalScore =
            vwapScore * WeightVwap +
            emaScore * WeightEma +
            rsiScore * WeightRsi +
            macdScore * WeightMacd +
            adxScore * WeightAdx +
            volumeScore * WeightVolume +
            bollingerScore * WeightBollinger;

        int finalScore = (int)Math.Clamp(totalScore, -100, 100);

        return new MarketScoreResult
        {
            TotalScore = finalScore,
            VwapScore = vwapScore,
            EmaScore = emaScore,
            RsiScore = rsiScore,
            MacdScore = macdScore,
            AdxScore = adxScore,
            VolumeScore = volumeScore,
            BollingerScore = bollingerScore,
            IsDiPositive = isDiPositive,
            IsMacdBullish = isMacdBullish
        };
    }
    
    /// <summary>
    /// Calculates Bollinger Bands score.
    /// </summary>
    private static int CalculateBollingerScore(double price, double upper, double lower, double middle)
    {
        if (upper <= 0 || lower <= 0 || middle <= 0)
            return 0;
            
        double bandwidth = upper - lower;
        if (bandwidth <= 0)
            return 0;
            
        // Position within bands: -1 (at lower) to +1 (at upper)
        double position = (price - middle) / (bandwidth / 2);
        
        // Near upper band = overbought (bearish), near lower = oversold (bullish)
        // This is mean-reversion logic
        int score;
        if (position > 0.8)
            score = (int)(-(position - 0.8) * 250); // Strong overbought = -50
        else if (position < -0.8)
            score = (int)((-0.8 - position) * 250); // Strong oversold = +50
        else
            score = 0; // Within normal range
            
        return (int)Math.Clamp(score, -100, 100);
    }
    
    /// <summary>
    /// Gets default entry thresholds.
    /// </summary>
    public static (int longEntry, int shortEntry) GetDefaultThresholds()
    {
        return (65, -65);
    }
    
    /// <summary>
    /// Gets default exit thresholds.
    /// </summary>
    public static (int longExit, int shortExit) GetDefaultExitThresholds()
    {
        return (40, -40);
    }
}
