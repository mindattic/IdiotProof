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
    
    // SMA (Simple Moving Average)
    public double Sma20 { get; init; }
    public double Sma50 { get; init; }
    
    // Momentum / Rate of Change
    public double Momentum { get; init; }
    public double Roc { get; init; }
    
    // ATR (for TP/SL calculation)
    public double Atr { get; init; }

    // Bollinger Bands derived
    public double BollingerPercentB { get; init; }
    public double BollingerBandwidth { get; init; }
    
    // Previous Day Levels (Support/Resistance)
    public double PrevDayHigh { get; init; }
    public double PrevDayLow { get; init; }
    public double PrevDayClose { get; init; }
    
    // Multi-day key levels
    public double TwoDayHigh { get; init; }    // Highest high of last 2 days
    public double TwoDayLow { get; init; }     // Lowest low of last 2 days
    public double SessionHigh { get; init; }   // Current session high
    public double SessionLow { get; init; }    // Current session low
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
    public int SmaScore { get; init; }
    public int MomentumScore { get; init; }
    
    /// <summary>
    /// True if +DI > -DI (bullish directional movement).
    /// </summary>
    public bool IsDiPositive { get; init; }
    
    /// <summary>
    /// True if MACD > Signal (bullish MACD).
    /// </summary>
    public bool IsMacdBullish { get; init; }
    
    /// <summary>
    /// Support/Resistance proximity score (-100 to +100).
    /// Positive = near support (bullish), Negative = near resistance (bearish).
    /// </summary>
    public int SupportResistanceScore { get; init; }
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
    public double Sma { get; init; }
    public double Momentum { get; init; }
    
    /// <summary>
    /// Default weights used when no learned weights are available.
    /// </summary>
    public static readonly IndicatorWeights Default = new()
    {
        Vwap = 0.09,
        Ema = 0.13,
        Rsi = 0.10,
        Macd = 0.16,
        Adx = 0.13,
        Volume = 0.07,
        Bollinger = 0.05,
        Stochastic = 0.05,
        Obv = 0.05,
        Cci = 0.03,
        WilliamsR = 0.03,
        Sma = 0.06,
        Momentum = 0.05
    };
    
    /// <summary>
    /// Validates that weights sum to approximately 1.0.
    /// </summary>
    public bool IsValid()
    {
        double sum = Vwap + Ema + Rsi + Macd + Adx + Volume + Bollinger + Stochastic + Obv + Cci + WilliamsR + Sma + Momentum;
        return Math.Abs(sum - 1.0) < 0.01;
    }
    
    /// <summary>
    /// Returns a normalized version of weights that sum to 1.0.
    /// </summary>
    public IndicatorWeights Normalize()
    {
        double sum = Vwap + Ema + Rsi + Macd + Adx + Volume + Bollinger + Stochastic + Obv + Cci + WilliamsR + Sma + Momentum;
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
            WilliamsR = WilliamsR / sum,
            Sma = Sma / sum,
            Momentum = Momentum / sum
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
    // Core indicators (68%)
    public const double WeightVwap = 0.09;
    public const double WeightEma = 0.13;
    public const double WeightRsi = 0.10;
    public const double WeightMacd = 0.16;
    public const double WeightAdx = 0.13;
    public const double WeightVolume = 0.07;
    
    // Extended indicators (32%)
    public const double WeightBollinger = 0.05;
    public const double WeightStochastic = 0.05;
    public const double WeightObv = 0.05;
    public const double WeightCci = 0.03;
    public const double WeightWilliamsR = 0.03;
    public const double WeightSma = 0.06;
    public const double WeightMomentum = 0.05;
    
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
        // Smooth scoring with NO discontinuities.
        // Key change: RSI 30-50 is now neutral-to-bullish (bounce recovery zone)
        // instead of heavily bearish. This allows bounce detection.
        // ====================================================================
        int rsiScore = 0;
        double rsi = snapshot.Rsi;
        
        if (rsi <= 0 || rsi >= 100)
        {
            rsiScore = 0;
        }
        else if (rsi <= 20)
        {
            // Deeply oversold → strong bullish mean-reversion expected
            rsiScore = 100;
        }
        else if (rsi <= 35)
        {
            // Oversold recovery zone → bullish (bounce forming)
            // 20→100, 35→25 (smooth decline)
            rsiScore = (int)(100 - (rsi - 20) * 5.0);
        }
        else if (rsi <= 50)
        {
            // Recovery zone → mildly bullish to neutral (room to run)
            // 35→25, 50→0
            rsiScore = (int)(25.0 - (rsi - 35) * (25.0 / 15.0));
        }
        else if (rsi <= 65)
        {
            // Healthy bullish momentum → neutral to mildly bullish
            // 50→0, 65→+25
            rsiScore = (int)((rsi - 50) * (25.0 / 15.0));
        }
        else if (rsi <= 75)
        {
            // Getting extended → bullish fading to neutral
            // 65→+25, 75→0
            rsiScore = (int)(25.0 - (rsi - 65) * 2.5);
        }
        else
        {
            // Overbought → bearish (mean reversion risk)
            // 75→0, 100→-100
            rsiScore = (int)(-(rsi - 75) * 4.0);
        }
        
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
        // SMA (6% weight) - Trend confirmation via simple moving averages
        // ====================================================================
        int smaScore = CalculateSmaScore(snapshot.Price, snapshot.Sma20, snapshot.Sma50);

        // ====================================================================
        // Momentum/ROC (5% weight) - Price momentum and rate of change
        // ====================================================================
        int momentumScore = CalculateMomentumScore(snapshot.Momentum, snapshot.Roc);

        // ====================================================================
        // Support/Resistance from Previous Day Levels (bonus, not weighted)
        // PDH/PDL/PDC act as natural S/R - proximity adjusts score
        // ====================================================================
        int srScore = CalculateSupportResistanceScore(snapshot);

        // ====================================================================
        // DIRECTIONAL CONFLUENCE BONUS
        // When multiple momentum/directional indicators agree (e.g., MACD bullish,
        // +DI > -DI, positive momentum, stochastic bullish, OBV rising) while
        // RSI is in a healthy range, this is a high-probability setup.
        // This bonus helps overcome the lagging indicator drag (EMA/SMA/VWAP)
        // during early bounces and reversals.
        // ====================================================================
        int confluenceBonus = CalculateDirectionalConfluence(
            macdScore, adxScore, momentumScore, stochasticScore, obvScore,
            snapshot.Rsi, snapshot.Adx, snapshot.PlusDi > snapshot.MinusDi);

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
            williamsRScore * weights.WilliamsR +
            smaScore * weights.Sma +
            momentumScore * weights.Momentum;

        // S/R bonus increased from 0.15 to 0.25 so that support bounces
        // have more impact on the overall score
        int finalScore = (int)Math.Clamp(totalScore + srScore * 0.25 + confluenceBonus, -100, 100);

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
            SmaScore = smaScore,
            MomentumScore = momentumScore,
            IsDiPositive = isDiPositive,
            IsMacdBullish = isMacdBullish,
            SupportResistanceScore = srScore
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
    /// Calculates SMA score based on price position relative to SMA 20 and SMA 50.
    /// Price above both = strong bullish, below both = strong bearish.
    /// SMA 20 > SMA 50 = bullish structure ("Golden Cross" territory).
    /// </summary>
    private static int CalculateSmaScore(double price, double sma20, double sma50)
    {
        if (sma20 <= 0 && sma50 <= 0)
            return 0;
        
        int score = 0;
        
        // Price vs SMA 20 (short-term trend)
        if (sma20 > 0)
        {
            if (price > sma20)
                score += 30; // Bullish: price above short-term SMA
            else
                score -= 30; // Bearish: price below short-term SMA
        }
        
        // Price vs SMA 50 (medium-term trend)
        if (sma50 > 0)
        {
            if (price > sma50)
                score += 25; // Bullish: price above medium-term SMA
            else
                score -= 25; // Bearish: price below medium-term SMA
        }
        
        // SMA alignment (Golden Cross / Death Cross territory)
        if (sma20 > 0 && sma50 > 0)
        {
            if (sma20 > sma50)
                score += 20; // Bullish alignment: short-term above long-term
            else
                score -= 20; // Bearish alignment: short-term below long-term
        }
        
        return (int)Math.Clamp(score, -100, 100);
    }
    
    /// <summary>
    /// Calculates Momentum/ROC combined score.
    /// Momentum > 0 = bullish, ROC > 0% = bullish.
    /// </summary>
    private static int CalculateMomentumScore(double momentum, double roc)
    {
        int score = 0;
        
        // Momentum component (price - price_N_bars_ago)
        if (momentum > 0)
            score += 35; // Bullish momentum
        else if (momentum < 0)
            score -= 35; // Bearish momentum
        
        // ROC component (percentage rate of change)
        if (roc > 2.0)
            score += 40; // Strong bullish ROC (>2%)
        else if (roc > 0)
            score += 15; // Mild bullish ROC
        else if (roc < -2.0)
            score -= 40; // Strong bearish ROC (<-2%)
        else if (roc < 0)
            score -= 15; // Mild bearish ROC
        
        return (int)Math.Clamp(score, -100, 100);
    }
    
    /// <summary>
    /// Calculates Support/Resistance score from Previous Day Levels.
    /// PDH acts as resistance, PDL acts as support, PDC acts as pivot.
    /// Price bouncing off support = bullish, rejected at resistance = bearish.
    /// </summary>
    private static int CalculateSupportResistanceScore(IndicatorSnapshot snapshot)
    {
        double price = snapshot.Price;
        double pdh = snapshot.PrevDayHigh;
        double pdl = snapshot.PrevDayLow;
        double pdc = snapshot.PrevDayClose;
        
        // Need valid previous day data
        if (pdh <= 0 || pdl <= 0 || pdc <= 0 || price <= 0)
            return 0;
        
        double prevRange = pdh - pdl;
        if (prevRange <= 0)
            return 0;
        
        int score = 0;
        
        // Proximity threshold: within 0.5% of a level = "at" the level
        double proximityPct = 0.005;
        
        // --- Previous Day High (Resistance) ---
        double distToPdh = (price - pdh) / price;
        if (Math.Abs(distToPdh) < proximityPct)
        {
            // Price AT PDH - acts as resistance
            score -= 30; // Bearish: testing resistance
        }
        else if (distToPdh > 0 && distToPdh < 0.02)
        {
            // Price just BROKE ABOVE PDH - breakout, old resistance becomes support
            score += 40; // Bullish: breakout above yesterday's high
        }
        else if (distToPdh > 0.02)
        {
            // Price well above PDH - extended, PDH is now strong support below
            score += 15;
        }
        
        // --- Previous Day Low (Support) ---
        double distToPdl = (price - pdl) / price;
        if (Math.Abs(distToPdl) < proximityPct)
        {
            // Price AT PDL - acts as support
            score += 30; // Bullish: testing support
        }
        else if (distToPdl < 0 && distToPdl > -0.02)
        {
            // Price just BROKE BELOW PDL - breakdown, old support becomes resistance
            score -= 40; // Bearish: breakdown below yesterday's low
        }
        else if (distToPdl < -0.02)
        {
            // Price well below PDL - extended down, PDL is now resistance above
            score -= 15;
        }
        
        // --- Previous Day Close (Pivot) ---
        double distToPdc = (price - pdc) / price;
        if (price > pdc)
        {
            // Above previous close = bullish bias
            score += (int)Math.Min(distToPdc * 1000, 20); // Up to +20 based on distance
        }
        else
        {
            // Below previous close = bearish bias
            score += (int)Math.Max(distToPdc * 1000, -20); // Down to -20 based on distance
        }
        
        // --- Position within previous day range (Premium/Discount) ---
        double positionInRange = (price - pdl) / prevRange;
        if (positionInRange >= 0 && positionInRange <= 1)
        {
            // Price is within yesterday's range
            if (positionInRange < 0.33)
                score += 15; // Discount zone - bullish
            else if (positionInRange > 0.67)
                score -= 15; // Premium zone - bearish (unless breakout)
        }
        
        // --- Two-day high/low (wider S/R) ---
        if (snapshot.TwoDayHigh > 0)
        {
            double distTo2dh = (price - snapshot.TwoDayHigh) / price;
            if (distTo2dh > 0 && distTo2dh < 0.01)
                score += 25; // Breaking multi-day high = strong bullish
        }
        if (snapshot.TwoDayLow > 0)
        {
            double distTo2dl = (price - snapshot.TwoDayLow) / price;
            if (distTo2dl < 0 && distTo2dl > -0.01)
                score -= 25; // Breaking multi-day low = strong bearish
        }
        
        return (int)Math.Clamp(score, -100, 100);
    }
    
    /// <summary>
    /// Calculates a directional confluence bonus when multiple momentum/directional
    /// indicators agree. This helps detect bounce and reversal setups where price
    /// is still below lagging indicators (EMA/SMA/VWAP) but leading indicators
    /// (MACD, ADX/DI, Stochastic, Momentum, OBV) have already turned bullish/bearish.
    /// 
    /// Pattern: Support bounce + higher lows + ADX 20+ + RSI not overbought + MACD bullish crossover
    /// → This is exactly the pattern the confluence bonus rewards.
    /// </summary>
    /// <returns>Bonus from -20 to +20 added directly to the total score.</returns>
    private static int CalculateDirectionalConfluence(
        int macdScore, int adxScore, int momentumScore, int stochasticScore, int obvScore,
        double rsi, double adx, bool isDiPositive)
    {
        // Count bullish/bearish directional indicators
        int bullishCount = 0;
        int bearishCount = 0;
        
        // MACD direction (strong signal)
        if (macdScore > 20) bullishCount++;
        else if (macdScore < -20) bearishCount++;
        
        // ADX/DI direction (strong signal)
        if (adxScore > 20) bullishCount++;
        else if (adxScore < -20) bearishCount++;
        
        // Momentum direction
        if (momentumScore > 15) bullishCount++;
        else if (momentumScore < -15) bearishCount++;
        
        // Stochastic direction
        if (stochasticScore > 15) bullishCount++;
        else if (stochasticScore < -15) bearishCount++;
        
        // OBV direction (volume confirmation)
        if (obvScore > 20) bullishCount++;
        else if (obvScore < -20) bearishCount++;
        
        int bonus = 0;
        
        // BULLISH CONFLUENCE: 4+ bullish directional indicators agree
        // AND RSI is in healthy range (not overbought) AND ADX shows trending
        if (bullishCount >= 4 && rsi < 70 && adx >= 20)
        {
            // Strong directional agreement with healthy RSI = high probability bounce/continuation
            bonus = bullishCount >= 5 ? 20 : 15;
            
            // Extra boost when RSI is in recovery zone (30-50) = early bounce detection
            if (rsi >= 30 && rsi <= 50)
                bonus += 5;
        }
        else if (bullishCount >= 3 && rsi < 65 && adx >= 25 && isDiPositive)
        {
            // Moderate bullish confluence with DI confirmation
            bonus = 10;
        }
        
        // BEARISH CONFLUENCE: 4+ bearish directional indicators agree
        // AND RSI is not oversold AND ADX shows trending
        if (bearishCount >= 4 && rsi > 30 && adx >= 20)
        {
            int bearBonus = bearishCount >= 5 ? -20 : -15;
            
            // Extra penalty when RSI is in 50-70 range = early breakdown detection
            if (rsi >= 50 && rsi <= 70)
                bearBonus -= 5;
            
            bonus = bearBonus;
        }
        else if (bearishCount >= 3 && rsi > 35 && adx >= 25 && !isDiPositive)
        {
            bonus = -10;
        }
        
        return (int)Math.Clamp(bonus, -25, 25);
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
