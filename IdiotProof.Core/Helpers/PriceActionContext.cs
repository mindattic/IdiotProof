// ============================================================================
// PriceActionContext - Proactive Multi-Bar Pattern Analysis
// ============================================================================
//
// PURPOSE:
// Transforms the trading system from REACTIVE (score triggers entry) to 
// PROACTIVE (understand market structure, wait for high-probability setups).
//
// The system now tracks:
// - Trend structure (HH/HL for uptrend, LH/LL for downtrend)
// - Fair Value Gaps (FVGs) - institutional footprints
// - Consolidation and breakout quality
// - Extension detection (don't chase)
// - Pullback quality assessment
// - Premium/Discount zones
// - Liquidity concepts (sweeps, grabs)
//
// USAGE:
//   var context = new PriceActionContext();
//   context.Update(candle);  // Call on each new candle
//   
//   var analysis = context.GetAnalysis(currentPrice);
//   if (analysis.ShouldWaitForPullback) return;  // Don't chase
//   if (analysis.IsIdealLongEntry) // High probability setup
//
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using IdiotProof.Core.Models;

namespace IdiotProof.Helpers;

/// <summary>
/// Represents a Fair Value Gap - an imbalance zone where price moved too fast.
/// These often act as support/resistance and get "filled" (revisited).
/// </summary>
public sealed class FairValueGap
{
    public DateTime Timestamp { get; init; }
    public double High { get; init; }      // Top of gap
    public double Low { get; init; }       // Bottom of gap
    public bool IsBullish { get; init; }   // True = bullish FVG (support), False = bearish (resistance)
    public bool IsFilled { get; set; }     // True if price has returned to fill the gap
    public double FillPercent { get; set; } // How much of the gap has been filled (0-1)
    
    public double MidPoint => (High + Low) / 2;
    public double Size => High - Low;
    
    /// <summary>
    /// Checks if a price level is within this FVG.
    /// </summary>
    public bool ContainsPrice(double price) => price >= Low && price <= High;
    
    public override string ToString() => 
        $"FVG {(IsBullish ? "Bull" : "Bear")} ${Low:F2}-${High:F2} " +
        $"({(IsFilled ? "FILLED" : $"{FillPercent:P0} filled")})";
}

/// <summary>
/// Represents a swing point (local high or low).
/// </summary>
public sealed class SwingPoint
{
    public DateTime Timestamp { get; init; }
    public double Price { get; init; }
    public bool IsHigh { get; init; }  // True = swing high, False = swing low
    public int BarIndex { get; init; } // Index in candle history
    
    public override string ToString() => 
        $"Swing {(IsHigh ? "High" : "Low")} ${Price:F2} @ {Timestamp:HH:mm}";
}

/// <summary>
/// Represents a consolidation/range zone.
/// </summary>
public sealed class ConsolidationZone
{
    public DateTime StartTime { get; init; }
    public DateTime EndTime { get; set; }
    public double High { get; set; }
    public double Low { get; set; }
    public int BarCount { get; set; }
    public bool IsBroken { get; set; }
    public int BreakoutDirection { get; set; } // 1 = up, -1 = down, 0 = not broken
    
    public double Range => High - Low;
    public double MidPoint => (High + Low) / 2;
    public double RangePercent(double price) => price > 0 ? Range / price * 100 : 0;
    
    /// <summary>
    /// True if this is a tight consolidation (squeeze).
    /// </summary>
    public bool IsSqueeze(double avgRange) => Range < avgRange * 0.5;
    
    public override string ToString() => 
        $"Consolidation ${Low:F2}-${High:F2} ({BarCount} bars)" +
        (IsBroken ? $" BROKEN {(BreakoutDirection > 0 ? "UP" : "DOWN")}" : "");
}

/// <summary>
/// Market structure trend analysis.
/// </summary>
public enum TrendState
{
    StrongUptrend,      // Clear HH/HL structure
    WeakUptrend,        // Higher lows but no new highs
    Ranging,            // No clear direction
    WeakDowntrend,      // Lower highs but no new lows
    StrongDowntrend,    // Clear LH/LL structure
    TrendReversal       // Structure break detected
}

/// <summary>
/// Entry quality assessment.
/// </summary>
public enum EntryQuality
{
    Ideal,              // Perfect setup - high probability
    Good,               // Solid setup with minor concerns
    Acceptable,         // Can trade but not optimal
    Poor,               // Low probability - consider skipping
    Avoid               // Do not enter - bad setup
}

/// <summary>
/// Comprehensive analysis result.
/// </summary>
public sealed class PriceActionAnalysis
{
    // Overall assessment
    public EntryQuality LongEntryQuality { get; init; }
    public EntryQuality ShortEntryQuality { get; init; }
    public string Reasoning { get; init; } = "";
    public List<string> BullishFactors { get; init; } = new();
    public List<string> BearishFactors { get; init; } = new();
    
    // Trend structure
    public TrendState TrendState { get; init; }
    public int ConsecutiveHigherHighs { get; init; }
    public int ConsecutiveHigherLows { get; init; }
    public int ConsecutiveLowerHighs { get; init; }
    public int ConsecutiveLowerLows { get; init; }
    public bool IsStructureBreak { get; init; }
    
    // Extension/Chase detection
    public bool IsOverextended { get; init; }
    public int ConsecutiveGreenBars { get; init; }
    public int ConsecutiveRedBars { get; init; }
    public double DistanceFromEma9Percent { get; init; }
    public bool ShouldWaitForPullback { get; init; }
    
    // Pullback quality
    public bool IsInPullback { get; init; }
    public double PullbackDepthPercent { get; init; }  // How deep is pullback (0-100)
    public bool IsPullbackHealthy { get; init; }       // Decreasing volume, structure intact
    public double PullbackFibLevel { get; init; }      // 0.382, 0.5, 0.618, 0.786
    
    // Fair Value Gaps
    public FairValueGap? NearestBullishFvg { get; init; }
    public FairValueGap? NearestBearishFvg { get; init; }
    public bool IsInFvg { get; init; }
    public bool IsFvgBeingFilled { get; init; }
    
    // Consolidation/Breakout
    public ConsolidationZone? ActiveConsolidation { get; init; }
    public bool IsBreakingOut { get; init; }
    public bool IsFirstPullbackAfterBreakout { get; init; }
    public int BarsSinceBreakout { get; init; }
    
    // Premium/Discount
    public bool IsInPremiumZone { get; init; }    // Top 1/3 of range
    public bool IsInDiscountZone { get; init; }   // Bottom 1/3 of range
    public bool IsInFairValue { get; init; }      // Middle 1/3
    
    // Liquidity concepts
    public bool JustSweptLows { get; init; }      // Took out lows then reversed (bullish)
    public bool JustSweptHighs { get; init; }     // Took out highs then reversed (bearish)
    public double NearestLiquidityPoolAbove { get; init; }
    public double NearestLiquidityPoolBelow { get; init; }
    
    // Divergences
    public bool HasBullishRsiDivergence { get; init; }
    public bool HasBearishRsiDivergence { get; init; }
    public bool HasBullishMacdDivergence { get; init; }
    public bool HasBearishMacdDivergence { get; init; }
    
    // Candle patterns
    public bool HasBullishEngulfing { get; init; }
    public bool HasBearishEngulfing { get; init; }
    public bool HasHammer { get; init; }
    public bool HasShootingStar { get; init; }
    public bool HasDoji { get; init; }
    
    // Score adjustments (to apply to market score)
    public int LongScoreAdjustment { get; init; }
    public int ShortScoreAdjustment { get; init; }
    
    /// <summary>
    /// True if this is a high-probability long setup.
    /// </summary>
    public bool IsIdealLongEntry => LongEntryQuality == EntryQuality.Ideal;
    
    /// <summary>
    /// True if this is a high-probability short setup.
    /// </summary>
    public bool IsIdealShortEntry => ShortEntryQuality == EntryQuality.Ideal;
    
    /// <summary>
    /// True if long entries should be blocked.
    /// </summary>
    public bool BlockLongEntry => LongEntryQuality == EntryQuality.Avoid;
    
    /// <summary>
    /// True if short entries should be blocked.
    /// </summary>
    public bool BlockShortEntry => ShortEntryQuality == EntryQuality.Avoid;
    
    public override string ToString()
    {
        var factors = new List<string>();
        if (BullishFactors.Any()) factors.Add($"Bull: {string.Join(", ", BullishFactors)}");
        if (BearishFactors.Any()) factors.Add($"Bear: {string.Join(", ", BearishFactors)}");
        
        return $"[PA] {TrendState} | Long={LongEntryQuality}, Short={ShortEntryQuality} | " +
               $"Adj: L{LongScoreAdjustment:+#;-#;0}/S{ShortScoreAdjustment:+#;-#;0} | {Reasoning}";
    }
}

/// <summary>
/// Proactive price action analysis engine.
/// Tracks multi-bar patterns and provides context-aware trading signals.
/// </summary>
public sealed class PriceActionContext
{
    // Configuration
    private const int MaxCandles = 200;            // History depth
    private const int MaxFvgs = 20;                // Max FVGs to track
    private const int MaxSwingPoints = 30;         // Max swing points
    private const int SwingLookback = 3;           // Bars on each side for swing detection
    private const double ExtensionThreshold = 2.5; // % from EMA9 = overextended
    private const int ChaseThreshold = 4;          // Consecutive same-color bars = chasing
    
    // State
    private readonly List<Candlestick> _candles = new();
    private readonly List<FairValueGap> _fvgs = new();
    private readonly List<SwingPoint> _swings = new();
    private readonly List<ConsolidationZone> _consolidations = new();
    
    // Cached values for divergence detection
    private readonly List<(DateTime time, double price, double rsi)> _rsiHistory = new();
    private readonly List<(DateTime time, double price, double macd)> _macdHistory = new();
    
    // Session tracking
    private double _sessionHigh = double.MinValue;
    private double _sessionLow = double.MaxValue;
    private double _sessionOpen = 0;
    private DateTime _sessionDate = DateTime.MinValue;
    
    // EMA cache
    private double _ema9 = 0;
    private double _ema21 = 0;
    private double _atr = 0;
    
    /// <summary>
    /// Number of candles in history.
    /// </summary>
    public int CandleCount => _candles.Count;
    
    /// <summary>
    /// Active unfilled Fair Value Gaps.
    /// </summary>
    public IReadOnlyList<FairValueGap> UnfilledFvgs => 
        _fvgs.Where(f => !f.IsFilled).ToList().AsReadOnly();
    
    /// <summary>
    /// Recent swing points for structure analysis.
    /// </summary>
    public IReadOnlyList<SwingPoint> SwingPoints => _swings.AsReadOnly();
    
    /// <summary>
    /// Updates context with a new completed candle.
    /// Call this when a bar closes.
    /// </summary>
    public void Update(Candlestick candle, double ema9 = 0, double ema21 = 0, 
                       double atr = 0, double rsi = 0, double macd = 0)
    {
        // Check for new session
        var candleDate = candle.Timestamp.Date;
        if (candleDate != _sessionDate)
        {
            _sessionDate = candleDate;
            _sessionHigh = candle.High;
            _sessionLow = candle.Low;
            _sessionOpen = candle.Open;
        }
        else
        {
            _sessionHigh = Math.Max(_sessionHigh, candle.High);
            _sessionLow = Math.Min(_sessionLow, candle.Low);
        }
        
        // Cache indicator values
        _ema9 = ema9;
        _ema21 = ema21;
        _atr = atr;
        
        // Add candle
        _candles.Add(candle);
        if (_candles.Count > MaxCandles)
            _candles.RemoveAt(0);
        
        // Track RSI/MACD for divergences
        if (rsi > 0)
        {
            _rsiHistory.Add((candle.Timestamp, candle.Close, rsi));
            if (_rsiHistory.Count > 50) _rsiHistory.RemoveAt(0);
        }
        if (macd != 0)
        {
            _macdHistory.Add((candle.Timestamp, candle.Close, macd));
            if (_macdHistory.Count > 50) _macdHistory.RemoveAt(0);
        }
        
        // Detect swing points
        DetectSwingPoints();
        
        // Detect Fair Value Gaps
        DetectFvg();
        
        // Update FVG fill status
        UpdateFvgFills(candle);
        
        // Detect consolidation
        DetectConsolidation();
    }
    
    /// <summary>
    /// Gets comprehensive price action analysis for the current moment.
    /// </summary>
    public PriceActionAnalysis GetAnalysis(double currentPrice, double currentRsi = 50, 
                                            double currentMacd = 0)
    {
        if (_candles.Count < 10)
        {
            return new PriceActionAnalysis
            {
                LongEntryQuality = EntryQuality.Acceptable,
                ShortEntryQuality = EntryQuality.Acceptable,
                Reasoning = "Insufficient candle history",
                TrendState = TrendState.Ranging
            };
        }
        
        var bullishFactors = new List<string>();
        var bearishFactors = new List<string>();
        int longAdj = 0, shortAdj = 0;
        
        // ====================================================================
        // 1. TREND STRUCTURE ANALYSIS
        // ====================================================================
        var (trendState, hhCount, hlCount, lhCount, llCount, structureBreak) = AnalyzeTrendStructure();
        
        if (trendState == TrendState.StrongUptrend)
        {
            bullishFactors.Add($"Strong uptrend (HH:{hhCount}, HL:{hlCount})");
            longAdj += 15;
            shortAdj -= 20;
        }
        else if (trendState == TrendState.StrongDowntrend)
        {
            bearishFactors.Add($"Strong downtrend (LH:{lhCount}, LL:{llCount})");
            shortAdj += 15;
            longAdj -= 20;
        }
        
        if (structureBreak)
        {
            var msg = "Structure break detected - potential reversal";
            bullishFactors.Add(msg);
            bearishFactors.Add(msg);
        }
        
        // ====================================================================
        // 2. EXTENSION / CHASE DETECTION
        // ====================================================================
        var (isExtended, greenBars, redBars, ema9Distance, shouldWait) = AnalyzeExtension(currentPrice);
        
        if (isExtended && greenBars >= ChaseThreshold)
        {
            bearishFactors.Add($"Overextended UP ({greenBars} green bars, {ema9Distance:F1}% from EMA9)");
            longAdj -= 25; // Don't chase
        }
        else if (isExtended && redBars >= ChaseThreshold)
        {
            bullishFactors.Add($"Overextended DOWN ({redBars} red bars, {ema9Distance:F1}% from EMA9)");
            shortAdj -= 25; // Don't chase
        }
        
        // ====================================================================
        // 3. PULLBACK QUALITY ASSESSMENT
        // ====================================================================
        var (inPullback, pullbackDepth, pullbackHealthy, fibLevel) = AnalyzePullback(currentPrice);
        
        if (inPullback && pullbackHealthy)
        {
            if (trendState == TrendState.StrongUptrend || trendState == TrendState.WeakUptrend)
            {
                bullishFactors.Add($"Healthy pullback in uptrend ({fibLevel:F3} fib, {pullbackDepth:F0}% depth)");
                longAdj += 20;
            }
            else if (trendState == TrendState.StrongDowntrend || trendState == TrendState.WeakDowntrend)
            {
                bearishFactors.Add($"Healthy rally in downtrend ({fibLevel:F3} fib)");
                shortAdj += 20;
            }
        }
        
        // ====================================================================
        // 4. FAIR VALUE GAP ANALYSIS
        // ====================================================================
        var (nearestBullFvg, nearestBearFvg, inFvg, fvgFilling) = AnalyzeFvgs(currentPrice);
        
        if (nearestBullFvg != null && !nearestBullFvg.IsFilled)
        {
            double distToFvg = (currentPrice - nearestBullFvg.High) / currentPrice * 100;
            if (distToFvg < 1.0 && distToFvg > -1.0)
            {
                bullishFactors.Add($"Near bullish FVG ${nearestBullFvg.Low:F2}-${nearestBullFvg.High:F2}");
                longAdj += 10;
            }
        }
        if (nearestBearFvg != null && !nearestBearFvg.IsFilled)
        {
            double distToFvg = (nearestBearFvg.Low - currentPrice) / currentPrice * 100;
            if (distToFvg < 1.0 && distToFvg > -1.0)
            {
                bearishFactors.Add($"Near bearish FVG ${nearestBearFvg.Low:F2}-${nearestBearFvg.High:F2}");
                shortAdj += 10;
            }
        }
        
        // ====================================================================
        // 5. CONSOLIDATION & BREAKOUT
        // ====================================================================
        var (activeConsol, isBreaking, isFirstPbAfterBo, barsSinceBo) = AnalyzeConsolidation(currentPrice);
        
        if (isBreaking && activeConsol != null)
        {
            if (currentPrice > activeConsol.High)
            {
                bullishFactors.Add($"Breaking out of consolidation (range: ${activeConsol.Low:F2}-${activeConsol.High:F2})");
                longAdj += 15;
            }
            else if (currentPrice < activeConsol.Low)
            {
                bearishFactors.Add($"Breaking down from consolidation");
                shortAdj += 15;
            }
        }
        
        if (isFirstPbAfterBo && barsSinceBo <= 10)
        {
            bullishFactors.Add($"First pullback after breakout ({barsSinceBo} bars ago)");
            longAdj += 25; // This is the ideal entry!
        }
        
        // ====================================================================
        // 6. PREMIUM / DISCOUNT ZONES
        // ====================================================================
        var (isPremium, isDiscount, isFairValue) = AnalyzePremiumDiscount(currentPrice);
        
        if (isDiscount && (trendState == TrendState.StrongUptrend || trendState == TrendState.WeakUptrend))
        {
            bullishFactors.Add("Price in discount zone of uptrend range");
            longAdj += 10;
        }
        else if (isPremium && (trendState == TrendState.StrongDowntrend || trendState == TrendState.WeakDowntrend))
        {
            bearishFactors.Add("Price in premium zone of downtrend range");
            shortAdj += 10;
        }
        else if (isPremium && trendState != TrendState.StrongUptrend)
        {
            bearishFactors.Add("Price in premium zone - risky for longs");
            longAdj -= 10;
        }
        
        // ====================================================================
        // 7. LIQUIDITY SWEEPS
        // ====================================================================
        var (sweptLows, sweptHighs, liqAbove, liqBelow) = AnalyzeLiquidity(currentPrice);
        
        if (sweptLows)
        {
            bullishFactors.Add("Just swept lows and reversed - bullish liquidity grab");
            longAdj += 20;
            shortAdj -= 15;
        }
        if (sweptHighs)
        {
            bearishFactors.Add("Just swept highs and reversed - bearish liquidity grab");
            shortAdj += 20;
            longAdj -= 15;
        }
        
        // ====================================================================
        // 8. DIVERGENCES
        // ====================================================================
        var (bullRsiDiv, bearRsiDiv, bullMacdDiv, bearMacdDiv) = AnalyzeDivergences(currentPrice, currentRsi, currentMacd);
        
        if (bullRsiDiv)
        {
            bullishFactors.Add("Bullish RSI divergence (price lower, RSI higher)");
            longAdj += 15;
        }
        if (bearRsiDiv)
        {
            bearishFactors.Add("Bearish RSI divergence (price higher, RSI lower)");
            shortAdj += 15;
        }
        
        // ====================================================================
        // 9. CANDLE PATTERNS
        // ====================================================================
        var (bullEngulf, bearEngulf, hammer, shootingStar, doji) = AnalyzeCandlePatterns();
        
        if (bullEngulf)
        {
            bullishFactors.Add("Bullish engulfing pattern");
            longAdj += 10;
        }
        if (bearEngulf)
        {
            bearishFactors.Add("Bearish engulfing pattern");
            shortAdj += 10;
        }
        if (hammer && isDiscount)
        {
            bullishFactors.Add("Hammer at support");
            longAdj += 15;
        }
        if (shootingStar && isPremium)
        {
            bearishFactors.Add("Shooting star at resistance");
            shortAdj += 15;
        }
        
        // ====================================================================
        // CALCULATE FINAL ENTRY QUALITY
        // ====================================================================
        var longQuality = CalculateEntryQuality(longAdj, bullishFactors.Count, bearishFactors.Count, 
                                                 trendState, true, isExtended && greenBars >= ChaseThreshold);
        var shortQuality = CalculateEntryQuality(shortAdj, bearishFactors.Count, bullishFactors.Count,
                                                  trendState, false, isExtended && redBars >= ChaseThreshold);
        
        // Build reasoning
        var reasoning = BuildReasoning(trendState, longQuality, shortQuality, 
                                       bullishFactors, bearishFactors);
        
        return new PriceActionAnalysis
        {
            LongEntryQuality = longQuality,
            ShortEntryQuality = shortQuality,
            Reasoning = reasoning,
            BullishFactors = bullishFactors,
            BearishFactors = bearishFactors,
            TrendState = trendState,
            ConsecutiveHigherHighs = hhCount,
            ConsecutiveHigherLows = hlCount,
            ConsecutiveLowerHighs = lhCount,
            ConsecutiveLowerLows = llCount,
            IsStructureBreak = structureBreak,
            IsOverextended = isExtended,
            ConsecutiveGreenBars = greenBars,
            ConsecutiveRedBars = redBars,
            DistanceFromEma9Percent = ema9Distance,
            ShouldWaitForPullback = shouldWait,
            IsInPullback = inPullback,
            PullbackDepthPercent = pullbackDepth,
            IsPullbackHealthy = pullbackHealthy,
            PullbackFibLevel = fibLevel,
            NearestBullishFvg = nearestBullFvg,
            NearestBearishFvg = nearestBearFvg,
            IsInFvg = inFvg,
            IsFvgBeingFilled = fvgFilling,
            ActiveConsolidation = activeConsol,
            IsBreakingOut = isBreaking,
            IsFirstPullbackAfterBreakout = isFirstPbAfterBo,
            BarsSinceBreakout = barsSinceBo,
            IsInPremiumZone = isPremium,
            IsInDiscountZone = isDiscount,
            IsInFairValue = isFairValue,
            JustSweptLows = sweptLows,
            JustSweptHighs = sweptHighs,
            NearestLiquidityPoolAbove = liqAbove,
            NearestLiquidityPoolBelow = liqBelow,
            HasBullishRsiDivergence = bullRsiDiv,
            HasBearishRsiDivergence = bearRsiDiv,
            HasBullishMacdDivergence = bullMacdDiv,
            HasBearishMacdDivergence = bearMacdDiv,
            HasBullishEngulfing = bullEngulf,
            HasBearishEngulfing = bearEngulf,
            HasHammer = hammer,
            HasShootingStar = shootingStar,
            HasDoji = doji,
            LongScoreAdjustment = longAdj,
            ShortScoreAdjustment = shortAdj
        };
    }
    
    // ========================================================================
    // ANALYSIS METHODS
    // ========================================================================
    
    private (TrendState state, int hh, int hl, int lh, int ll, bool structureBreak) AnalyzeTrendStructure()
    {
        if (_swings.Count < 4)
            return (TrendState.Ranging, 0, 0, 0, 0, false);
        
        var highs = _swings.Where(s => s.IsHigh).OrderByDescending(s => s.Timestamp).Take(5).ToList();
        var lows = _swings.Where(s => !s.IsHigh).OrderByDescending(s => s.Timestamp).Take(5).ToList();
        
        if (highs.Count < 2 || lows.Count < 2)
            return (TrendState.Ranging, 0, 0, 0, 0, false);
        
        // Count consecutive higher highs and higher lows
        int hh = 0, hl = 0, lh = 0, ll = 0;
        
        for (int i = 0; i < highs.Count - 1; i++)
        {
            if (highs[i].Price > highs[i + 1].Price) hh++;
            else if (highs[i].Price < highs[i + 1].Price) lh++;
            else break;
        }
        
        for (int i = 0; i < lows.Count - 1; i++)
        {
            if (lows[i].Price > lows[i + 1].Price) hl++;
            else if (lows[i].Price < lows[i + 1].Price) ll++;
            else break;
        }
        
        // Detect structure break (most recent swing violated)
        bool structureBreak = false;
        if (_candles.Count > 0)
        {
            var lastCandle = _candles[^1];
            var lastSwingLow = lows.FirstOrDefault();
            var lastSwingHigh = highs.FirstOrDefault();
            
            if (lastSwingLow != null && hh > 0 && hl > 0 && lastCandle.Close < lastSwingLow.Price)
                structureBreak = true; // Uptrend structure broken
            if (lastSwingHigh != null && lh > 0 && ll > 0 && lastCandle.Close > lastSwingHigh.Price)
                structureBreak = true; // Downtrend structure broken
        }
        
        // Determine trend state
        TrendState state;
        if (hh >= 2 && hl >= 2)
            state = TrendState.StrongUptrend;
        else if (hl >= 2)
            state = TrendState.WeakUptrend;
        else if (lh >= 2 && ll >= 2)
            state = TrendState.StrongDowntrend;
        else if (lh >= 2)
            state = TrendState.WeakDowntrend;
        else
            state = TrendState.Ranging;
        
        if (structureBreak)
            state = TrendState.TrendReversal;
        
        return (state, hh, hl, lh, ll, structureBreak);
    }
    
    private (bool isExtended, int greenBars, int redBars, double ema9Dist, bool shouldWait) 
        AnalyzeExtension(double currentPrice)
    {
        if (_candles.Count < 5)
            return (false, 0, 0, 0, false);
        
        // Count consecutive same-color bars
        int greenBars = 0, redBars = 0;
        for (int i = _candles.Count - 1; i >= 0; i--)
        {
            var c = _candles[i];
            if (c.Close > c.Open)
            {
                if (redBars > 0) break;
                greenBars++;
            }
            else
            {
                if (greenBars > 0) break;
                redBars++;
            }
        }
        
        // Calculate distance from EMA9
        double ema9Dist = 0;
        if (_ema9 > 0)
        {
            ema9Dist = Math.Abs((currentPrice - _ema9) / _ema9 * 100);
        }
        
        bool isExtended = ema9Dist >= ExtensionThreshold || 
                          greenBars >= ChaseThreshold || 
                          redBars >= ChaseThreshold;
        
        bool shouldWait = isExtended && (greenBars >= ChaseThreshold || redBars >= ChaseThreshold);
        
        return (isExtended, greenBars, redBars, ema9Dist, shouldWait);
    }
    
    private (bool inPullback, double depth, bool healthy, double fibLevel) AnalyzePullback(double currentPrice)
    {
        var highs = _swings.Where(s => s.IsHigh).OrderByDescending(s => s.Timestamp).Take(3).ToList();
        var lows = _swings.Where(s => !s.IsHigh).OrderByDescending(s => s.Timestamp).Take(3).ToList();
        
        if (highs.Count < 2 || lows.Count < 1)
            return (false, 0, false, 0);
        
        var lastHigh = highs[0];
        var lastLow = lows[0];
        var prevHigh = highs.Count > 1 ? highs[1] : null;
        
        // Check if we're in a pullback from a high
        bool inPullback = lastHigh.Timestamp > lastLow.Timestamp && currentPrice < lastHigh.Price;
        
        if (!inPullback)
            return (false, 0, false, 0);
        
        // Calculate pullback depth
        double moveUp = lastHigh.Price - (prevHigh != null ? Math.Min(prevHigh.Price, lastLow.Price) : lastLow.Price);
        double pullbackAmount = lastHigh.Price - currentPrice;
        double depth = moveUp > 0 ? (pullbackAmount / moveUp) * 100 : 0;
        
        // Fibonacci level
        double fibLevel = moveUp > 0 ? pullbackAmount / moveUp : 0;
        
        // Check if pullback is healthy (volume decreasing, not too deep)
        bool healthy = fibLevel >= 0.382 && fibLevel <= 0.618;
        
        // Check volume decrease during pullback
        if (_candles.Count >= 3)
        {
            var recentCandles = _candles.TakeLast(5).ToList();
            var upCandles = recentCandles.Where(c => c.Close > c.Open).ToList();
            var downCandles = recentCandles.Where(c => c.Close <= c.Open).ToList();
            
            if (downCandles.Any() && upCandles.Any())
            {
                // Pullback candles should have less volume than impulse
                double avgPullbackVol = downCandles.Average(c => c.Volume);
                double avgImpulseVol = upCandles.Average(c => c.Volume);
                healthy = healthy && avgPullbackVol <= avgImpulseVol * 1.2;
            }
        }
        
        return (inPullback, depth, healthy, fibLevel);
    }
    
    private (FairValueGap? bullish, FairValueGap? bearish, bool inFvg, bool filling) 
        AnalyzeFvgs(double currentPrice)
    {
        var unfilled = _fvgs.Where(f => !f.IsFilled).ToList();
        
        var nearestBull = unfilled
            .Where(f => f.IsBullish && f.Low <= currentPrice)
            .OrderByDescending(f => f.High)
            .FirstOrDefault();
        
        var nearestBear = unfilled
            .Where(f => !f.IsBullish && f.High >= currentPrice)
            .OrderBy(f => f.Low)
            .FirstOrDefault();
        
        bool inFvg = unfilled.Any(f => f.ContainsPrice(currentPrice));
        bool filling = inFvg;
        
        return (nearestBull, nearestBear, inFvg, filling);
    }
    
    private (ConsolidationZone? active, bool breaking, bool firstPb, int barsSince) 
        AnalyzeConsolidation(double currentPrice)
    {
        if (!_consolidations.Any())
            return (null, false, false, 0);
        
        var active = _consolidations.LastOrDefault(c => !c.IsBroken);
        var broken = _consolidations.Where(c => c.IsBroken).OrderByDescending(c => c.EndTime).FirstOrDefault();
        
        bool breaking = false;
        if (active != null)
        {
            breaking = currentPrice > active.High * 1.002 || currentPrice < active.Low * 0.998;
        }
        
        // First pullback after breakout
        bool firstPb = false;
        int barsSince = 0;
        if (broken != null)
        {
            barsSince = _candles.Count(c => c.Timestamp > broken.EndTime);
            
            // If we broke out up and now pulling back to the breakout level
            if (broken.BreakoutDirection > 0 && currentPrice <= broken.High * 1.02 && currentPrice >= broken.High * 0.98)
            {
                firstPb = true;
            }
            // If we broke out down and now rallying to the breakdown level
            else if (broken.BreakoutDirection < 0 && currentPrice >= broken.Low * 0.98 && currentPrice <= broken.Low * 1.02)
            {
                firstPb = true;
            }
        }
        
        return (active, breaking, firstPb, barsSince);
    }
    
    private (bool premium, bool discount, bool fairValue) AnalyzePremiumDiscount(double currentPrice)
    {
        if (_sessionHigh <= _sessionLow || _sessionHigh == double.MinValue)
            return (false, false, true);
        
        double range = _sessionHigh - _sessionLow;
        double third = range / 3;
        
        bool premium = currentPrice >= _sessionHigh - third;
        bool discount = currentPrice <= _sessionLow + third;
        bool fairValue = !premium && !discount;
        
        return (premium, discount, fairValue);
    }
    
    private (bool sweptLows, bool sweptHighs, double liqAbove, double liqBelow) 
        AnalyzeLiquidity(double currentPrice)
    {
        bool sweptLows = false, sweptHighs = false;
        double liqAbove = 0, liqBelow = 0;
        
        if (_candles.Count < 5 || _swings.Count < 2)
            return (false, false, liqAbove, liqBelow);
        
        var recentLows = _swings.Where(s => !s.IsHigh).OrderByDescending(s => s.Timestamp).Take(3).ToList();
        var recentHighs = _swings.Where(s => s.IsHigh).OrderByDescending(s => s.Timestamp).Take(3).ToList();
        
        // Nearest liquidity pools
        liqAbove = recentHighs.Where(h => h.Price > currentPrice).OrderBy(h => h.Price).FirstOrDefault()?.Price ?? 0;
        liqBelow = recentLows.Where(l => l.Price < currentPrice).OrderByDescending(l => l.Price).FirstOrDefault()?.Price ?? 0;
        
        // Check last 3 candles for liquidity sweeps
        if (_candles.Count >= 3)
        {
            var last = _candles[^1];
            var prev = _candles[^2];
            var prev2 = _candles[^3];
            
            // Swept lows: went below prior swing low then closed back above
            var priorLow = recentLows.Skip(1).FirstOrDefault();
            if (priorLow != null)
            {
                bool wentBelow = last.Low < priorLow.Price || prev.Low < priorLow.Price;
                bool closedAbove = last.Close > priorLow.Price && last.Close > last.Open;
                sweptLows = wentBelow && closedAbove;
            }
            
            // Swept highs: went above prior swing high then closed back below
            var priorHigh = recentHighs.Skip(1).FirstOrDefault();
            if (priorHigh != null)
            {
                bool wentAbove = last.High > priorHigh.Price || prev.High > priorHigh.Price;
                bool closedBelow = last.Close < priorHigh.Price && last.Close < last.Open;
                sweptHighs = wentAbove && closedBelow;
            }
        }
        
        return (sweptLows, sweptHighs, liqAbove, liqBelow);
    }
    
    private (bool bullRsi, bool bearRsi, bool bullMacd, bool bearMacd) 
        AnalyzeDivergences(double currentPrice, double currentRsi, double currentMacd)
    {
        bool bullRsi = false, bearRsi = false, bullMacd = false, bearMacd = false;
        
        if (_rsiHistory.Count < 10)
            return (bullRsi, bearRsi, bullMacd, bearMacd);
        
        // Find recent swing lows in price
        var priceLows = FindLocalExtremes(_candles.TakeLast(20).ToList(), false);
        var rsiAtLows = new List<(double price, double rsi)>();
        
        foreach (var low in priceLows)
        {
            var rsiMatch = _rsiHistory.Where(r => Math.Abs((r.time - low.Timestamp).TotalMinutes) < 5).FirstOrDefault();
            if (rsiMatch != default)
            {
                rsiAtLows.Add((low.Low, rsiMatch.rsi));
            }
        }
        
        // Bullish divergence: lower price lows but higher RSI lows
        if (rsiAtLows.Count >= 2)
        {
            var recent = rsiAtLows[^1];
            var prior = rsiAtLows[^2];
            bullRsi = recent.price < prior.price && recent.rsi > prior.rsi;
        }
        
        // Find recent swing highs for bearish divergence
        var priceHighs = FindLocalExtremes(_candles.TakeLast(20).ToList(), true);
        var rsiAtHighs = new List<(double price, double rsi)>();
        
        foreach (var high in priceHighs)
        {
            var rsiMatch = _rsiHistory.Where(r => Math.Abs((r.time - high.Timestamp).TotalMinutes) < 5).FirstOrDefault();
            if (rsiMatch != default)
            {
                rsiAtHighs.Add((high.High, rsiMatch.rsi));
            }
        }
        
        // Bearish divergence: higher price highs but lower RSI highs
        if (rsiAtHighs.Count >= 2)
        {
            var recent = rsiAtHighs[^1];
            var prior = rsiAtHighs[^2];
            bearRsi = recent.price > prior.price && recent.rsi < prior.rsi;
        }
        
        // Similar for MACD divergences
        if (_macdHistory.Count >= 10)
        {
            var macdAtLows = new List<(double price, double macd)>();
            foreach (var low in priceLows)
            {
                var macdMatch = _macdHistory.Where(m => Math.Abs((m.time - low.Timestamp).TotalMinutes) < 5).FirstOrDefault();
                if (macdMatch != default)
                    macdAtLows.Add((low.Low, macdMatch.macd));
            }
            if (macdAtLows.Count >= 2)
            {
                var recent = macdAtLows[^1];
                var prior = macdAtLows[^2];
                bullMacd = recent.price < prior.price && recent.macd > prior.macd;
            }
        }
        
        return (bullRsi, bearRsi, bullMacd, bearMacd);
    }
    
    private (bool bullEngulf, bool bearEngulf, bool hammer, bool shootingStar, bool doji) 
        AnalyzeCandlePatterns()
    {
        if (_candles.Count < 2)
            return (false, false, false, false, false);
        
        var curr = _candles[^1];
        var prev = _candles[^2];
        
        double currBody = Math.Abs(curr.Close - curr.Open);
        double prevBody = Math.Abs(prev.Close - prev.Open);
        double currRange = curr.High - curr.Low;
        
        // Bullish engulfing: red prev, green curr that fully engulfs
        bool bullEngulf = prev.Close < prev.Open && curr.Close > curr.Open &&
                          curr.Open <= prev.Close && curr.Close >= prev.Open;
        
        // Bearish engulfing
        bool bearEngulf = prev.Close > prev.Open && curr.Close < curr.Open &&
                          curr.Open >= prev.Close && curr.Close <= prev.Open;
        
        // Hammer: small body at top, long lower wick
        double upperWick = curr.High - Math.Max(curr.Open, curr.Close);
        double lowerWick = Math.Min(curr.Open, curr.Close) - curr.Low;
        bool hammer = currBody < currRange * 0.3 && 
                      lowerWick > currBody * 2 && 
                      upperWick < currBody;
        
        // Shooting star: small body at bottom, long upper wick
        bool shootingStar = currBody < currRange * 0.3 && 
                            upperWick > currBody * 2 && 
                            lowerWick < currBody;
        
        // Doji: very small body
        bool doji = currBody <= currRange * 0.1;
        
        return (bullEngulf, bearEngulf, hammer, shootingStar, doji);
    }
    
    // ========================================================================
    // DETECTION METHODS (called on each candle update)
    // ========================================================================
    
    private void DetectSwingPoints()
    {
        if (_candles.Count < SwingLookback * 2 + 1)
            return;
        
        int idx = _candles.Count - 1 - SwingLookback;
        if (idx < SwingLookback)
            return;
        
        var candle = _candles[idx];
        bool isSwingHigh = true, isSwingLow = true;
        
        for (int i = idx - SwingLookback; i <= idx + SwingLookback; i++)
        {
            if (i == idx) continue;
            if (_candles[i].High >= candle.High) isSwingHigh = false;
            if (_candles[i].Low <= candle.Low) isSwingLow = false;
        }
        
        if (isSwingHigh)
        {
            // Avoid duplicates
            if (!_swings.Any(s => s.Timestamp == candle.Timestamp && s.IsHigh))
            {
                _swings.Add(new SwingPoint
                {
                    Timestamp = candle.Timestamp,
                    Price = candle.High,
                    IsHigh = true,
                    BarIndex = idx
                });
                
                if (_swings.Count > MaxSwingPoints)
                    _swings.RemoveAt(0);
            }
        }
        
        if (isSwingLow)
        {
            if (!_swings.Any(s => s.Timestamp == candle.Timestamp && !s.IsHigh))
            {
                _swings.Add(new SwingPoint
                {
                    Timestamp = candle.Timestamp,
                    Price = candle.Low,
                    IsHigh = false,
                    BarIndex = idx
                });
                
                if (_swings.Count > MaxSwingPoints)
                    _swings.RemoveAt(0);
            }
        }
    }
    
    private void DetectFvg()
    {
        if (_candles.Count < 3)
            return;
        
        var c0 = _candles[^3]; // Oldest of 3
        var c1 = _candles[^2]; // Middle
        var c2 = _candles[^1]; // Newest
        
        // Bullish FVG: Gap between c0.High and c2.Low (c1 is the impulse candle)
        if (c2.Low > c0.High && c1.Close > c1.Open)
        {
            var fvg = new FairValueGap
            {
                Timestamp = c1.Timestamp,
                Low = c0.High,
                High = c2.Low,
                IsBullish = true
            };
            
            if (fvg.Size > _atr * 0.1 || fvg.Size > c1.Close * 0.001) // Minimum size check
            {
                _fvgs.Add(fvg);
                if (_fvgs.Count > MaxFvgs)
                    _fvgs.RemoveAt(0);
            }
        }
        
        // Bearish FVG: Gap between c0.Low and c2.High
        if (c2.High < c0.Low && c1.Close < c1.Open)
        {
            var fvg = new FairValueGap
            {
                Timestamp = c1.Timestamp,
                Low = c2.High,
                High = c0.Low,
                IsBullish = false
            };
            
            if (fvg.Size > _atr * 0.1 || fvg.Size > c1.Close * 0.001)
            {
                _fvgs.Add(fvg);
                if (_fvgs.Count > MaxFvgs)
                    _fvgs.RemoveAt(0);
            }
        }
    }
    
    private void UpdateFvgFills(Candlestick candle)
    {
        foreach (var fvg in _fvgs.Where(f => !f.IsFilled))
        {
            if (fvg.IsBullish)
            {
                // Bullish FVG filled when price trades down into it
                if (candle.Low <= fvg.High)
                {
                    double fillPoint = Math.Max(candle.Low, fvg.Low);
                    fvg.FillPercent = (fvg.High - fillPoint) / fvg.Size;
                    if (candle.Low <= fvg.Low)
                        fvg.IsFilled = true;
                }
            }
            else
            {
                // Bearish FVG filled when price trades up into it
                if (candle.High >= fvg.Low)
                {
                    double fillPoint = Math.Min(candle.High, fvg.High);
                    fvg.FillPercent = (fillPoint - fvg.Low) / fvg.Size;
                    if (candle.High >= fvg.High)
                        fvg.IsFilled = true;
                }
            }
        }
    }
    
    private void DetectConsolidation()
    {
        if (_candles.Count < 10)
            return;
        
        // Look at last 10 candles for consolidation
        var recent = _candles.TakeLast(10).ToList();
        double high = recent.Max(c => c.High);
        double low = recent.Min(c => c.Low);
        double range = high - low;
        
        // Calculate average range of individual candles
        double avgCandleRange = recent.Average(c => c.High - c.Low);
        
        // Consolidation: overall range is less than 4x average candle range
        bool isConsolidating = range < avgCandleRange * 4;
        
        var activeConsol = _consolidations.LastOrDefault(c => !c.IsBroken);
        
        if (isConsolidating)
        {
            if (activeConsol == null)
            {
                // Start new consolidation
                _consolidations.Add(new ConsolidationZone
                {
                    StartTime = recent[0].Timestamp,
                    EndTime = recent[^1].Timestamp,
                    High = high,
                    Low = low,
                    BarCount = recent.Count
                });
            }
            else
            {
                // Extend existing consolidation
                activeConsol.EndTime = recent[^1].Timestamp;
                activeConsol.High = Math.Max(activeConsol.High, high);
                activeConsol.Low = Math.Min(activeConsol.Low, low);
                activeConsol.BarCount++;
            }
        }
        else if (activeConsol != null)
        {
            // Check for breakout
            var lastCandle = _candles[^1];
            if (lastCandle.Close > activeConsol.High)
            {
                activeConsol.IsBroken = true;
                activeConsol.BreakoutDirection = 1;
                activeConsol.EndTime = lastCandle.Timestamp;
            }
            else if (lastCandle.Close < activeConsol.Low)
            {
                activeConsol.IsBroken = true;
                activeConsol.BreakoutDirection = -1;
                activeConsol.EndTime = lastCandle.Timestamp;
            }
        }
        
        // Cleanup old consolidations
        if (_consolidations.Count > 10)
            _consolidations.RemoveRange(0, _consolidations.Count - 10);
    }
    
    // ========================================================================
    // HELPER METHODS
    // ========================================================================
    
    private List<Candlestick> FindLocalExtremes(List<Candlestick> candles, bool findHighs)
    {
        var extremes = new List<Candlestick>();
        
        for (int i = 1; i < candles.Count - 1; i++)
        {
            var c = candles[i];
            var prev = candles[i - 1];
            var next = candles[i + 1];
            
            if (findHighs)
            {
                if (c.High > prev.High && c.High > next.High)
                    extremes.Add(c);
            }
            else
            {
                if (c.Low < prev.Low && c.Low < next.Low)
                    extremes.Add(c);
            }
        }
        
        return extremes;
    }
    
    private EntryQuality CalculateEntryQuality(int adjustment, int supportingFactors, 
                                                int opposingFactors, TrendState trend,
                                                bool isLong, bool isChasing)
    {
        // Chasing is always bad
        if (isChasing)
            return EntryQuality.Avoid;
        
        // Check trend alignment
        bool alignedWithTrend = (isLong && (trend == TrendState.StrongUptrend || trend == TrendState.WeakUptrend)) ||
                                (!isLong && (trend == TrendState.StrongDowntrend || trend == TrendState.WeakDowntrend));
        
        bool againstTrend = (isLong && (trend == TrendState.StrongDowntrend || trend == TrendState.WeakDowntrend)) ||
                            (!isLong && (trend == TrendState.StrongUptrend || trend == TrendState.WeakUptrend));
        
        // Score-based quality
        if (adjustment >= 35 && supportingFactors >= 3 && alignedWithTrend)
            return EntryQuality.Ideal;
        
        if (adjustment >= 20 && supportingFactors >= 2)
            return EntryQuality.Good;
        
        if (adjustment >= 0 && supportingFactors > opposingFactors)
            return EntryQuality.Acceptable;
        
        if (againstTrend && adjustment < 10)
            return EntryQuality.Avoid;
        
        if (adjustment < -10 || opposingFactors > supportingFactors + 1)
            return EntryQuality.Poor;
        
        return EntryQuality.Acceptable;
    }
    
    private string BuildReasoning(TrendState trend, EntryQuality longQ, EntryQuality shortQ,
                                  List<string> bullish, List<string> bearish)
    {
        var parts = new List<string>();
        
        parts.Add($"Trend: {trend}");
        
        if (longQ == EntryQuality.Ideal)
            parts.Add("IDEAL LONG setup");
        else if (longQ == EntryQuality.Avoid)
            parts.Add("Avoid longs");
        
        if (shortQ == EntryQuality.Ideal)
            parts.Add("IDEAL SHORT setup");
        else if (shortQ == EntryQuality.Avoid)
            parts.Add("Avoid shorts");
        
        if (bullish.Count > bearish.Count + 1)
            parts.Add($"Bullish bias ({bullish.Count} vs {bearish.Count})");
        else if (bearish.Count > bullish.Count + 1)
            parts.Add($"Bearish bias ({bearish.Count} vs {bullish.Count})");
        
        return string.Join(" | ", parts);
    }
    
    /// <summary>
    /// Clears all state for a new session.
    /// </summary>
    public void Reset()
    {
        _candles.Clear();
        _fvgs.Clear();
        _swings.Clear();
        _consolidations.Clear();
        _rsiHistory.Clear();
        _macdHistory.Clear();
        _sessionHigh = double.MinValue;
        _sessionLow = double.MaxValue;
        _sessionOpen = 0;
        _sessionDate = DateTime.MinValue;
    }
}
