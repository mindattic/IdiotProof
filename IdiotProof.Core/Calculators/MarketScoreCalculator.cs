// ============================================================================
// MarketScoreCalculator - THE SINGLE SOURCE OF TRUTH FOR MARKET SCORING
// ============================================================================
//
// CRITICAL: This is the ONLY place market score calculation logic lives.
// Both live trading (StrategyRunner) and backtesting (Backtester)
// MUST use this same code. Any change here affects BOTH live and backtest.
//
// This ensures backtesting results accurately predict live trading performance.
//
// Weights: VWAP 13%, EMA 18%, RSI 14%, MACD 20%, ADX 20%, Volume 10%, Bollinger 5%
//
// ============================================================================

namespace IdiotProof.Calculators;

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
    public double Ema34 { get; init; }
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
    
    // Extended Indicators
    public double StochasticK { get; init; }
    public double StochasticD { get; init; }
    public double ObvSlope { get; init; }  // OBV trend direction (-1 to +1)
    public double Cci { get; init; }
    public double WilliamsR { get; init; }
    
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
    
    // Extended indicator scores
    public int StochasticScore { get; init; }
    public int ObvScore { get; init; }
    public int CciScore { get; init; }
    public int WilliamsRScore { get; init; }
    
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
/// Configurable indicator weights for market score calculation.
/// Weights should sum to 1.0 (100%).
/// Training learns optimal weights per ticker.
/// </summary>
public readonly struct IndicatorWeights
{
    public double Vwap { get; init; }
    public double Ema { get; init; }
    public double Rsi { get; init; }
    public double Macd { get; init; }
    public double Adx { get; init; }
    public double Volume { get; init; }
    public double Bollinger { get; init; }
    public double Stochastic { get; init; }
    public double Obv { get; init; }
    public double Cci { get; init; }
    public double WilliamsR { get; init; }
    
    /// <summary>
    /// Default weights used when no learned weights are available.
    /// </summary>
    public static readonly IndicatorWeights Default = new()
    {
        Vwap = 0.10,
        Ema = 0.15,
        Rsi = 0.12,
        Macd = 0.18,
        Adx = 0.15,
        Volume = 0.08,
        Bollinger = 0.05,
        Stochastic = 0.06,
        Obv = 0.05,
        Cci = 0.03,
        WilliamsR = 0.03
    };
    
    /// <summary>
    /// Validates that weights sum to approximately 1.0.
    /// </summary>
    public bool IsValid()
    {
        double sum = Vwap + Ema + Rsi + Macd + Adx + Volume + Bollinger + Stochastic + Obv + Cci + WilliamsR;
        return Math.Abs(sum - 1.0) < 0.01;
    }
    
    /// <summary>
    /// Returns a normalized version of weights that sum to 1.0.
    /// </summary>
    public IndicatorWeights Normalize()
    {
        double sum = Vwap + Ema + Rsi + Macd + Adx + Volume + Bollinger + Stochastic + Obv + Cci + WilliamsR;
        if (sum <= 0) return Default;
        
        return new IndicatorWeights
        {
            Vwap = Vwap / sum,
            Ema = Ema / sum,
            Rsi = Rsi / sum,
            Macd = Macd / sum,
            Adx = Adx / sum,
            Volume = Volume / sum,
            Bollinger = Bollinger / sum,
            Stochastic = Stochastic / sum,
            Obv = Obv / sum,
            Cci = Cci / sum,
            WilliamsR = WilliamsR / sum
        };
    }
}

/// <summary>
/// THE SINGLE SOURCE OF TRUTH for market score calculation.
/// Used by BOTH live trading and backtesting.
/// </summary>
public static class MarketScoreCalculator
{
    // ========================================================================
    // DEFAULT WEIGHTS - Same for live and backtest (sum to 100%)
    // Used when no learned weights are available
    // ========================================================================
    // Core indicators (78%)
    public const double WeightVwap = 0.10;
    public const double WeightEma = 0.15;
    public const double WeightRsi = 0.12;
    public const double WeightMacd = 0.18;
    public const double WeightAdx = 0.15;
    public const double WeightVolume = 0.08;
    
    // Extended indicators (22%)
    public const double WeightBollinger = 0.05;
    public const double WeightStochastic = 0.06;
    public const double WeightObv = 0.05;
    public const double WeightCci = 0.03;
    public const double WeightWilliamsR = 0.03;
    
    /// <summary>
    /// Calculates market score using DEFAULT weights.
    /// Use this when no learned weights are available.
    /// </summary>
    public static MarketScoreResult Calculate(IndicatorSnapshot snapshot)
    {
        return Calculate(snapshot, IndicatorWeights.Default);
    }
    
    /// <summary>
    /// Calculates market score using LEARNED weights.
    /// This is THE ONLY score calculation - used by live, backtest, and training.
    /// </summary>
    public static MarketScoreResult Calculate(IndicatorSnapshot snapshot, IndicatorWeights weights)
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
        // Stochastic (6% weight) - Momentum oscillator
        // ====================================================================
        int stochasticScore = CalculateStochasticScore(snapshot.StochasticK, snapshot.StochasticD);

        // ====================================================================
        // OBV (5% weight) - Volume flow confirmation
        // ====================================================================
        int obvScore = CalculateObvScore(snapshot.ObvSlope);

        // ====================================================================
        // CCI (3% weight) - Trend strength and mean reversion
        // ====================================================================
        int cciScore = CalculateCciScore(snapshot.Cci);

        // ====================================================================
        // Williams %R (3% weight) - Momentum extremes
        // ====================================================================
        int williamsRScore = CalculateWilliamsRScore(snapshot.WilliamsR);

        // ====================================================================
        // Weighted Total - Uses learned or default weights
        // ====================================================================
        double totalScore =
            vwapScore * weights.Vwap +
            emaScore * weights.Ema +
            rsiScore * weights.Rsi +
            macdScore * weights.Macd +
            adxScore * weights.Adx +
            volumeScore * weights.Volume +
            bollingerScore * weights.Bollinger +
            stochasticScore * weights.Stochastic +
            obvScore * weights.Obv +
            cciScore * weights.Cci +
            williamsRScore * weights.WilliamsR;

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
            StochasticScore = stochasticScore,
            ObvScore = obvScore,
            CciScore = cciScore,
            WilliamsRScore = williamsRScore,
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
    /// Calculates Stochastic oscillator score.
    /// %K crossing above %D = bullish, crossing below = bearish.
    /// Overbought (>80) = bearish bias, Oversold (<20) = bullish bias.
    /// </summary>
    private static int CalculateStochasticScore(double k, double d)
    {
        if (k <= 0 && d <= 0)
            return 0;
            
        int score = 0;
        
        // K > D = bullish momentum, K < D = bearish momentum
        if (k > d)
            score = 40;
        else if (k < d)
            score = -40;
            
        // Overbought/oversold adjustments
        if (k > 80)
            score -= 30; // Overbought - bearish pressure
        else if (k < 20)
            score += 30; // Oversold - bullish bounce expected
            
        return (int)Math.Clamp(score, -100, 100);
    }
    
    /// <summary>
    /// Calculates OBV (On-Balance Volume) score.
    /// ObvSlope is the normalized slope of OBV (-1 to +1).
    /// Rising OBV = bullish volume flow, Falling = bearish.
    /// </summary>
    private static int CalculateObvScore(double obvSlope)
    {
        // ObvSlope ranges from -1 (strong selling) to +1 (strong buying)
        // Convert to score: -100 to +100
        return (int)Math.Clamp(obvSlope * 100, -100, 100);
    }
    
    /// <summary>
    /// Calculates CCI (Commodity Channel Index) score.
    /// CCI > 100 = overbought, CCI < -100 = oversold.
    /// </summary>
    private static int CalculateCciScore(double cci)
    {
        // CCI typically ranges from -200 to +200
        // Map to score with overbought/oversold logic
        int score;
        
        if (cci > 100)
            score = (int)(-(cci - 100) * 0.5); // Overbought = bearish
        else if (cci < -100)
            score = (int)((-100 - cci) * 0.5); // Oversold = bullish
        else
            score = (int)(cci * 0.5); // Trend following in normal range
            
        return (int)Math.Clamp(score, -100, 100);
    }
    
    /// <summary>
    /// Calculates Williams %R score.
    /// Williams %R ranges from -100 to 0.
    /// -100 to -80 = oversold (bullish), -20 to 0 = overbought (bearish).
    /// </summary>
    private static int CalculateWilliamsRScore(double williamsR)
    {
        // Williams %R is -100 (oversold) to 0 (overbought)
        int score;
        
        if (williamsR >= -20)
            score = (int)((williamsR + 20) * -5); // Overbought = bearish
        else if (williamsR <= -80)
            score = (int)((-80 - williamsR) * 5); // Oversold = bullish
        else
            score = 0; // Normal range
            
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
