// ============================================================================
// ProactiveMarketScanner - Empirical Pattern Detection & Opportunity Scoring
// ============================================================================
//
// PURPOSE:
// Continuously monitors the market for FORMING patterns and builds empirical
// confidence through observation. Decisions are made with confidence based on
// evidence gathered during the monitoring process, not just the current candle.
//
// PHILOSOPHY:
// - PROACTIVE: Look for patterns that WILL become good entries, not react to them
// - EMPIRICAL: Build confidence through observation, not assumptions
// - MULTI-BAR: Minimum 10-20 candles of context for any decision
// - PATIENT: Wait for high-confidence setups rather than forcing trades
//
// KEY COMPONENTS:
// 1. PatternFormationScanner - Detects patterns as they're forming
// 2. MomentumExhaustionDetector - Spots fading momentum before reversal
// 3. VolumeProfileAnalyzer - Tracks institutional levels (POC, VAH, VAL)
// 4. AccumulationDistributionTracker - Detects smart money activity
// 5. EmpiricalConfidenceCalculator - Scores confidence based on evidence
//
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using IdiotProof.Core.Models;

namespace IdiotProof.Helpers;

#region Enums

/// <summary>
/// Types of chart patterns the scanner can detect.
/// </summary>
public enum PatternType
{
    None,
    BullFlag,           // Consolidation after strong up move
    BearFlag,           // Consolidation after strong down move
    AscendingTriangle,  // Higher lows, flat resistance
    DescendingTriangle, // Lower highs, flat support
    SymmetricalTriangle,// Converging trendlines
    Wedge,              // Converging trendlines with slope
    DoubleBottom,       // W pattern
    DoubleTop,          // M pattern
    HeadAndShoulders,   // Reversal pattern
    InverseHeadAndShoulders,
    CupAndHandle,       // Bullish continuation
    FlagBreakout,       // Pattern just broke out
    RangeBreakout       // Breaking out of consolidation
}

/// <summary>
/// Formation stage of a pattern.
/// </summary>
public enum PatternStage
{
    NotDetected,        // No pattern forming
    EarlyFormation,     // 25% complete - just starting to form
    MidFormation,       // 50% complete - pattern taking shape
    LateFormation,      // 75% complete - pattern nearly complete
    ReadyToBreak,       // Pattern complete, waiting for breakout
    BrokeOut,           // Breakout occurred
    Failed              // Pattern failed/invalidated
}

/// <summary>
/// Momentum state for exhaustion detection.
/// </summary>
public enum MomentumState
{
    StrongBullish,      // Strong upward momentum
    ModerateBullish,    // Moderate upward momentum
    WeakeningBullish,   // Momentum fading but still up
    Exhausted,          // Momentum near zero
    WeakeningBearish,   // Momentum fading but still down
    ModerateBearish,    // Moderate downward momentum
    StrongBearish       // Strong downward momentum
}

/// <summary>
/// Volume profile zones.
/// </summary>
public enum VolumeZone
{
    AboveVAH,           // Above Value Area High - premium
    InValueArea,        // Between VAH and VAL - fair value
    AtPOC,              // At Point of Control - highest volume
    BelowVAL,           // Below Value Area Low - discount
    Unknown
}

#endregion

#region Data Classes

/// <summary>
/// Represents a detected pattern with formation progress.
/// </summary>
public sealed record DetectedPattern
{
    public PatternType Type { get; init; }
    public PatternStage Stage { get; init; }
    public DateTime StartTime { get; init; }
    public DateTime? BreakoutTime { get; set; }
    public double EntryLevel { get; init; }        // Ideal entry price
    public double TargetLevel { get; init; }       // Projected target
    public double StopLevel { get; init; }         // Stop loss level
    public double Confidence { get; init; }        // 0-1 confidence score
    public int BarsInFormation { get; init; }      // How many bars pattern has been forming
    public string Description { get; init; } = "";
    
    public double RiskRewardRatio => 
        StopLevel != EntryLevel ? Math.Abs(TargetLevel - EntryLevel) / Math.Abs(EntryLevel - StopLevel) : 0;
    
    public override string ToString() =>
        $"{Type} ({Stage}) | Entry: ${EntryLevel:F2} | Target: ${TargetLevel:F2} | R:R={RiskRewardRatio:F1} | Conf={Confidence:P0}";
}

/// <summary>
/// Momentum exhaustion analysis result.
/// </summary>
public sealed class MomentumAnalysis
{
    public MomentumState State { get; init; }
    public double MomentumValue { get; init; }     // Raw momentum value
    public double RateOfChange { get; init; }      // How fast momentum is changing
    public bool IsExhausting { get; init; }        // True if momentum fading fast
    public bool IsDiverging { get; init; }         // True if price/momentum diverging
    public int BarsUntilExhaustion { get; init; }  // Estimated bars until momentum exhausts
    public double ExhaustionProbability { get; init; } // 0-1 probability of exhaustion
    public string Reason { get; init; } = "";
    
    public override string ToString() =>
        $"Momentum: {State} | Exhausting={IsExhausting} | P(exhaust)={ExhaustionProbability:P0} | {Reason}";
}

/// <summary>
/// Volume profile analysis result.
/// </summary>
public sealed class VolumeProfileAnalysis
{
    public double POC { get; init; }               // Point of Control - highest volume price
    public double VAH { get; init; }               // Value Area High (70% of volume)
    public double VAL { get; init; }               // Value Area Low (70% of volume)
    public VolumeZone CurrentZone { get; init; }
    public double DistanceToPOC { get; init; }     // % distance to POC
    public bool IsPriceAtPOC { get; init; }        // Within 0.5% of POC
    public double VolumeWeightedPrice { get; init; } // VWAP-like calculation
    public List<(double price, long volume)> HighVolumeLevels { get; init; } = new();
    
    public override string ToString() =>
        $"VP: POC=${POC:F2} | VAH=${VAH:F2} | VAL=${VAL:F2} | Zone={CurrentZone}";
}

/// <summary>
/// Accumulation/Distribution analysis.
/// </summary>
public sealed class AccumulationDistributionAnalysis
{
    public double ADLine { get; init; }            // Cumulative A/D line
    public double ADTrend { get; init; }           // Slope of A/D line
    public bool IsAccumulating { get; init; }      // Smart money buying
    public bool IsDistributing { get; init; }      // Smart money selling
    public bool IsDiverging { get; init; }         // A/D diverging from price
    public string DivergenceType { get; init; } = "";  // Bullish or Bearish divergence
    public double ConfidenceScore { get; init; }   // 0-1 confidence in signal
    
    public override string ToString() =>
        $"A/D: {(IsAccumulating ? "ACCUMULATION" : IsDistributing ? "DISTRIBUTION" : "Neutral")} | Diverging={IsDiverging} ({DivergenceType})";
}

/// <summary>
/// Comprehensive market opportunity assessment.
/// </summary>
public sealed class MarketOpportunity
{
    public DateTime Timestamp { get; init; }
    public double CurrentPrice { get; init; }
    
    // Pattern analysis
    public DetectedPattern? ActivePattern { get; init; }
    public List<DetectedPattern> FormingPatterns { get; init; } = new();
    
    // Momentum analysis
    public MomentumAnalysis Momentum { get; init; } = null!;
    
    // Volume analysis
    public VolumeProfileAnalysis VolumeProfile { get; init; } = null!;
    public AccumulationDistributionAnalysis AccumDist { get; init; } = null!;
    
    // Empirical confidence
    public double EmpiricalConfidence { get; init; }  // 0-100 overall confidence
    public int EvidenceCount { get; init; }           // Number of supporting signals
    public List<string> BullishEvidence { get; init; } = new();
    public List<string> BearishEvidence { get; init; } = new();
    
    // Actionable signals
    public bool IsHighConfidenceLongSetup { get; init; }
    public bool IsHighConfidenceShortSetup { get; init; }
    public bool ShouldWaitForBetterEntry { get; init; }
    public string RecommendedAction { get; init; } = "";
    
    // Entry/Exit levels
    public double IdealLongEntry { get; init; }
    public double IdealShortEntry { get; init; }
    public double SuggestedStopLoss { get; init; }
    public double SuggestedTakeProfit { get; init; }
    
    public override string ToString()
    {
        var bias = BullishEvidence.Count > BearishEvidence.Count ? "BULLISH" :
                   BearishEvidence.Count > BullishEvidence.Count ? "BEARISH" : "NEUTRAL";
        return $"[OPPORTUNITY] {bias} | Conf={EmpiricalConfidence:F0}% | Evidence: {EvidenceCount} signals | {RecommendedAction}";
    }
}

#endregion

/// <summary>
/// Proactive market scanner that continuously monitors for forming patterns
/// and builds empirical confidence through multi-bar observation.
/// </summary>
public sealed class ProactiveMarketScanner
{
    #region Configuration
    
    private const int MinimumBarsForAnalysis = 20;
    private const int MaxCandleHistory = 500;
    private const int VolumeProfileBins = 50;
    private const double PatternConfidenceThreshold = 0.6;
    private const double HighConfidenceThreshold = 75;
    
    #endregion
    
    #region State
    
    private readonly List<Candlestick> _candles = new();
    private readonly List<double> _momentumHistory = new();
    private readonly List<double> _adLineHistory = new();
    private readonly Dictionary<double, long> _volumeByPrice = new();
    
    // Tracked patterns
    private readonly List<DetectedPattern> _activePatterns = new();
    private readonly List<DetectedPattern> _completedPatterns = new();
    
    // Empirical tracking - count outcomes for confidence building
    private readonly Dictionary<PatternType, (int wins, int losses)> _patternPerformance = new();
    
    // Session tracking
    private double _sessionHigh = double.MinValue;
    private double _sessionLow = double.MaxValue;
    private DateTime _sessionDate = DateTime.MinValue;
    
    // Indicator values (passed in from outside)
    private double _ema9, _ema21, _ema50;
    private double _rsi, _macd, _macdSignal, _macdHistogram;
    private double _adx, _plusDi, _minusDi;
    private double _atr;
    
    #endregion
    
    #region Public Properties
    
    public int CandleCount => _candles.Count;
    public bool HasEnoughData => _candles.Count >= MinimumBarsForAnalysis;
    public IReadOnlyList<DetectedPattern> ActivePatterns => _activePatterns.AsReadOnly();
    public IReadOnlyList<DetectedPattern> CompletedPatterns => _completedPatterns.AsReadOnly();
    
    #endregion
    
    #region Public Methods
    
    /// <summary>
    /// Updates the scanner with a new completed candle.
    /// </summary>
    public void Update(Candlestick candle, 
                       double ema9 = 0, double ema21 = 0, double ema50 = 0,
                       double rsi = 50, double macd = 0, double macdSignal = 0, double macdHistogram = 0,
                       double adx = 20, double plusDi = 0, double minusDi = 0,
                       double atr = 0)
    {
        // Store indicator values
        _ema9 = ema9;
        _ema21 = ema21;
        _ema50 = ema50;
        _rsi = rsi;
        _macd = macd;
        _macdSignal = macdSignal;
        _macdHistogram = macdHistogram;
        _adx = adx;
        _plusDi = plusDi;
        _minusDi = minusDi;
        _atr = atr;
        
        // Track session
        var candleDate = candle.Timestamp.Date;
        if (candleDate != _sessionDate)
        {
            _sessionDate = candleDate;
            _sessionHigh = candle.High;
            _sessionLow = candle.Low;
        }
        else
        {
            _sessionHigh = Math.Max(_sessionHigh, candle.High);
            _sessionLow = Math.Min(_sessionLow, candle.Low);
        }
        
        // Add candle
        _candles.Add(candle);
        if (_candles.Count > MaxCandleHistory)
            _candles.RemoveAt(0);
        
        // Update volume profile
        UpdateVolumeProfile(candle);
        
        // Update A/D line
        UpdateAccumulationDistribution(candle);
        
        // Update momentum tracking
        UpdateMomentumTracking(candle);
        
        // Scan for patterns
        ScanForPatterns();
        
        // Update existing pattern status
        UpdatePatternStatus(candle.Close);
    }
    
    /// <summary>
    /// Gets comprehensive market opportunity analysis.
    /// </summary>
    public MarketOpportunity GetOpportunity(double currentPrice)
    {
        if (!HasEnoughData)
        {
            return new MarketOpportunity
            {
                Timestamp = DateTime.UtcNow,
                CurrentPrice = currentPrice,
                EmpiricalConfidence = 0,
                RecommendedAction = "Insufficient data - need 20+ candles",
                Momentum = new MomentumAnalysis { State = MomentumState.Exhausted },
                VolumeProfile = new VolumeProfileAnalysis(),
                AccumDist = new AccumulationDistributionAnalysis()
            };
        }
        
        // Analyze all components
        var momentum = AnalyzeMomentum(currentPrice);
        var volumeProfile = AnalyzeVolumeProfile(currentPrice);
        var accumDist = AnalyzeAccumulationDistribution(currentPrice);
        
        // Build evidence
        var bullishEvidence = new List<string>();
        var bearishEvidence = new List<string>();
        
        // Momentum evidence
        GatherMomentumEvidence(momentum, bullishEvidence, bearishEvidence);
        
        // Volume evidence
        GatherVolumeEvidence(volumeProfile, currentPrice, bullishEvidence, bearishEvidence);
        
        // A/D evidence
        GatherAccumDistEvidence(accumDist, bullishEvidence, bearishEvidence);
        
        // Pattern evidence
        GatherPatternEvidence(currentPrice, bullishEvidence, bearishEvidence);
        
        // Price structure evidence
        GatherPriceStructureEvidence(currentPrice, bullishEvidence, bearishEvidence);
        
        // Calculate empirical confidence
        int totalEvidence = bullishEvidence.Count + bearishEvidence.Count;
        double empiricalConfidence = CalculateEmpiricalConfidence(
            bullishEvidence.Count, bearishEvidence.Count, momentum, volumeProfile, accumDist);
        
        // Determine action
        bool isHighConfLong = bullishEvidence.Count >= 4 && bearishEvidence.Count <= 1 && 
                              empiricalConfidence >= HighConfidenceThreshold;
        bool isHighConfShort = bearishEvidence.Count >= 4 && bullishEvidence.Count <= 1 && 
                               empiricalConfidence >= HighConfidenceThreshold;
        bool shouldWait = empiricalConfidence < 50 || Math.Abs(bullishEvidence.Count - bearishEvidence.Count) < 2;
        
        string action = DetermineRecommendedAction(isHighConfLong, isHighConfShort, shouldWait,
                                                    bullishEvidence.Count, bearishEvidence.Count,
                                                    momentum, currentPrice);
        
        // Calculate entry/exit levels
        var (longEntry, shortEntry, sl, tp) = CalculateEntryExitLevels(currentPrice, volumeProfile);
        
        return new MarketOpportunity
        {
            Timestamp = DateTime.UtcNow,
            CurrentPrice = currentPrice,
            ActivePattern = _activePatterns.FirstOrDefault(p => p.Stage >= PatternStage.LateFormation),
            FormingPatterns = _activePatterns.Where(p => p.Stage < PatternStage.LateFormation).ToList(),
            Momentum = momentum,
            VolumeProfile = volumeProfile,
            AccumDist = accumDist,
            EmpiricalConfidence = empiricalConfidence,
            EvidenceCount = totalEvidence,
            BullishEvidence = bullishEvidence,
            BearishEvidence = bearishEvidence,
            IsHighConfidenceLongSetup = isHighConfLong,
            IsHighConfidenceShortSetup = isHighConfShort,
            ShouldWaitForBetterEntry = shouldWait,
            RecommendedAction = action,
            IdealLongEntry = longEntry,
            IdealShortEntry = shortEntry,
            SuggestedStopLoss = sl,
            SuggestedTakeProfit = tp
        };
    }
    
    /// <summary>
    /// Records pattern outcome for empirical learning.
    /// </summary>
    public void RecordPatternOutcome(PatternType type, bool wasSuccessful)
    {
        if (!_patternPerformance.ContainsKey(type))
            _patternPerformance[type] = (0, 0);
        
        var (wins, losses) = _patternPerformance[type];
        if (wasSuccessful)
            _patternPerformance[type] = (wins + 1, losses);
        else
            _patternPerformance[type] = (wins, losses + 1);
    }
    
    /// <summary>
    /// Gets historical win rate for a pattern type.
    /// </summary>
    public double GetPatternWinRate(PatternType type)
    {
        if (!_patternPerformance.TryGetValue(type, out var stats))
            return 0.5; // Default 50% if no history
        
        int total = stats.wins + stats.losses;
        return total > 0 ? (double)stats.wins / total : 0.5;
    }
    
    /// <summary>
    /// Resets for a new session.
    /// </summary>
    public void Reset()
    {
        _candles.Clear();
        _momentumHistory.Clear();
        _adLineHistory.Clear();
        _volumeByPrice.Clear();
        _activePatterns.Clear();
        _sessionHigh = double.MinValue;
        _sessionLow = double.MaxValue;
        _sessionDate = DateTime.MinValue;
    }
    
    #endregion
    
    #region Pattern Detection
    
    private void ScanForPatterns()
    {
        if (_candles.Count < 15)
            return;
        
        // Check for bull flag
        DetectBullFlag();
        
        // Check for bear flag
        DetectBearFlag();
        
        // Check for triangles
        DetectTriangles();
        
        // Check for double bottoms/tops
        DetectDoublePatterns();
    }
    
    private void DetectBullFlag()
    {
        if (_candles.Count < 20)
            return;
        
        var recent = _candles.TakeLast(20).ToList();
        
        // Bull flag: Strong up move (pole) followed by gentle down/sideways drift (flag)
        // Look for: 5+ green candles, then 5-10 candles of consolidation
        
        // Find the pole (strong up move)
        int poleStart = -1, poleEnd = -1;
        double poleHigh = 0, poleLow = double.MaxValue;
        
        for (int i = 0; i < 10; i++)
        {
            var c = recent[i];
            if (c.Close > c.Open) // Green candle
            {
                if (poleStart == -1) poleStart = i;
                poleEnd = i;
                poleHigh = Math.Max(poleHigh, c.High);
                poleLow = Math.Min(poleLow, c.Low);
            }
            else if (poleStart != -1)
            {
                break; // Pole ended
            }
        }
        
        if (poleEnd - poleStart < 3) return; // Need at least 4 candle pole
        
        double poleHeight = poleHigh - poleLow;
        if (poleHeight < poleLow * 0.02) return; // Pole should be at least 2% move
        
        // Check for flag (consolidation after pole)
        var flagCandles = recent.Skip(poleEnd + 1).ToList();
        if (flagCandles.Count < 5) return;
        
        double flagHigh = flagCandles.Max(c => c.High);
        double flagLow = flagCandles.Min(c => c.Low);
        double flagRange = flagHigh - flagLow;
        
        // Flag should be tighter than pole and slightly declining
        if (flagRange > poleHeight * 0.5) return;
        
        // Check for declining highs (gentle pullback)
        bool hasLowerHighs = true;
        for (int i = 1; i < Math.Min(5, flagCandles.Count); i++)
        {
            if (flagCandles[i].High > flagCandles[i - 1].High * 1.002)
            {
                hasLowerHighs = false;
                break;
            }
        }
        
        if (hasLowerHighs)
        {
            double entryLevel = flagHigh; // Enter on breakout above flag
            double targetLevel = entryLevel + poleHeight; // Measured move
            double stopLevel = flagLow - (_atr > 0 ? _atr * 0.5 : flagRange * 0.25);
            
            var confidence = CalculatePatternConfidence(PatternType.BullFlag, poleHeight, flagRange, flagCandles.Count);
            
            var pattern = new DetectedPattern
            {
                Type = PatternType.BullFlag,
                Stage = flagCandles.Count >= 8 ? PatternStage.ReadyToBreak : PatternStage.MidFormation,
                StartTime = recent[poleStart].Timestamp,
                EntryLevel = entryLevel,
                TargetLevel = targetLevel,
                StopLevel = stopLevel,
                Confidence = confidence,
                BarsInFormation = recent.Count - poleStart,
                Description = $"Bull flag: {poleHeight / poleLow * 100:F1}% pole, {flagCandles.Count} bar consolidation"
            };
            
            AddOrUpdatePattern(pattern);
        }
    }
    
    private void DetectBearFlag()
    {
        if (_candles.Count < 20)
            return;
        
        var recent = _candles.TakeLast(20).ToList();
        
        // Bear flag: Strong down move (pole) followed by gentle up/sideways drift (flag)
        int poleStart = -1, poleEnd = -1;
        double poleHigh = 0, poleLow = double.MaxValue;
        
        for (int i = 0; i < 10; i++)
        {
            var c = recent[i];
            if (c.Close < c.Open) // Red candle
            {
                if (poleStart == -1) poleStart = i;
                poleEnd = i;
                poleHigh = Math.Max(poleHigh, c.High);
                poleLow = Math.Min(poleLow, c.Low);
            }
            else if (poleStart != -1)
            {
                break;
            }
        }
        
        if (poleEnd - poleStart < 3) return;
        
        double poleHeight = poleHigh - poleLow;
        if (poleHeight < poleHigh * 0.02) return;
        
        var flagCandles = recent.Skip(poleEnd + 1).ToList();
        if (flagCandles.Count < 5) return;
        
        double flagHigh = flagCandles.Max(c => c.High);
        double flagLow = flagCandles.Min(c => c.Low);
        double flagRange = flagHigh - flagLow;
        
        if (flagRange > poleHeight * 0.5) return;
        
        // Check for rising lows (gentle bounce)
        bool hasHigherLows = true;
        for (int i = 1; i < Math.Min(5, flagCandles.Count); i++)
        {
            if (flagCandles[i].Low < flagCandles[i - 1].Low * 0.998)
            {
                hasHigherLows = false;
                break;
            }
        }
        
        if (hasHigherLows)
        {
            double entryLevel = flagLow;
            double targetLevel = entryLevel - poleHeight;
            double stopLevel = flagHigh + (_atr > 0 ? _atr * 0.5 : flagRange * 0.25);
            
            var confidence = CalculatePatternConfidence(PatternType.BearFlag, poleHeight, flagRange, flagCandles.Count);
            
            var pattern = new DetectedPattern
            {
                Type = PatternType.BearFlag,
                Stage = flagCandles.Count >= 8 ? PatternStage.ReadyToBreak : PatternStage.MidFormation,
                StartTime = recent[poleStart].Timestamp,
                EntryLevel = entryLevel,
                TargetLevel = targetLevel,
                StopLevel = stopLevel,
                Confidence = confidence,
                BarsInFormation = recent.Count - poleStart,
                Description = $"Bear flag: {poleHeight / poleHigh * 100:F1}% pole, {flagCandles.Count} bar consolidation"
            };
            
            AddOrUpdatePattern(pattern);
        }
    }
    
    private void DetectTriangles()
    {
        if (_candles.Count < 15)
            return;
        
        var recent = _candles.TakeLast(15).ToList();
        
        // Find swing highs and lows
        var swingHighs = new List<(int index, double price)>();
        var swingLows = new List<(int index, double price)>();
        
        for (int i = 2; i < recent.Count - 2; i++)
        {
            var c = recent[i];
            if (c.High > recent[i - 1].High && c.High > recent[i - 2].High &&
                c.High > recent[i + 1].High && c.High > recent[i + 2].High)
            {
                swingHighs.Add((i, c.High));
            }
            if (c.Low < recent[i - 1].Low && c.Low < recent[i - 2].Low &&
                c.Low < recent[i + 1].Low && c.Low < recent[i + 2].Low)
            {
                swingLows.Add((i, c.Low));
            }
        }
        
        if (swingHighs.Count < 2 || swingLows.Count < 2)
            return;
        
        // Check for ascending triangle (higher lows, flat highs)
        bool hasHigherLows = swingLows.Count >= 2 && 
                             swingLows[^1].price > swingLows[^2].price;
        bool hasFlatHighs = swingHighs.Count >= 2 && 
                            Math.Abs(swingHighs[^1].price - swingHighs[^2].price) / swingHighs[^1].price < 0.01;
        
        if (hasHigherLows && hasFlatHighs)
        {
            double resistance = swingHighs.Max(h => h.price);
            double support = swingLows.Last().price;
            double range = resistance - support;
            
            var pattern = new DetectedPattern
            {
                Type = PatternType.AscendingTriangle,
                Stage = PatternStage.MidFormation,
                StartTime = recent[Math.Min(swingHighs[0].index, swingLows[0].index)].Timestamp,
                EntryLevel = resistance * 1.002, // Slight buffer above resistance
                TargetLevel = resistance + range,
                StopLevel = support - (_atr > 0 ? _atr : range * 0.2),
                Confidence = 0.65,
                BarsInFormation = recent.Count,
                Description = "Ascending triangle: Higher lows + flat resistance"
            };
            
            AddOrUpdatePattern(pattern);
        }
        
        // Check for descending triangle (lower highs, flat lows)
        bool hasLowerHighs = swingHighs.Count >= 2 && 
                             swingHighs[^1].price < swingHighs[^2].price;
        bool hasFlatLows = swingLows.Count >= 2 && 
                           Math.Abs(swingLows[^1].price - swingLows[^2].price) / swingLows[^1].price < 0.01;
        
        if (hasLowerHighs && hasFlatLows)
        {
            double support = swingLows.Min(l => l.price);
            double resistance = swingHighs.Last().price;
            double range = resistance - support;
            
            var pattern = new DetectedPattern
            {
                Type = PatternType.DescendingTriangle,
                Stage = PatternStage.MidFormation,
                StartTime = recent[Math.Min(swingHighs[0].index, swingLows[0].index)].Timestamp,
                EntryLevel = support * 0.998,
                TargetLevel = support - range,
                StopLevel = resistance + (_atr > 0 ? _atr : range * 0.2),
                Confidence = 0.65,
                BarsInFormation = recent.Count,
                Description = "Descending triangle: Lower highs + flat support"
            };
            
            AddOrUpdatePattern(pattern);
        }
    }
    
    private void DetectDoublePatterns()
    {
        if (_candles.Count < 30)
            return;
        
        var recent = _candles.TakeLast(30).ToList();
        
        // Find the two lowest lows for double bottom
        var sortedByLow = recent.OrderBy(c => c.Low).Take(2).ToList();
        if (sortedByLow.Count == 2)
        {
            double low1 = sortedByLow[0].Low;
            double low2 = sortedByLow[1].Low;
            
            // Lows should be within 1% of each other
            if (Math.Abs(low1 - low2) / low1 < 0.01)
            {
                // Find the high between them (neckline)
                int idx1 = recent.IndexOf(sortedByLow[0]);
                int idx2 = recent.IndexOf(sortedByLow[1]);
                if (Math.Abs(idx1 - idx2) >= 5) // At least 5 bars apart
                {
                    int startIdx = Math.Min(idx1, idx2);
                    int endIdx = Math.Max(idx1, idx2);
                    double neckline = recent.Skip(startIdx).Take(endIdx - startIdx + 1).Max(c => c.High);
                    double depth = neckline - Math.Min(low1, low2);
                    
                    var pattern = new DetectedPattern
                    {
                        Type = PatternType.DoubleBottom,
                        Stage = PatternStage.LateFormation,
                        StartTime = recent[startIdx].Timestamp,
                        EntryLevel = neckline * 1.002,
                        TargetLevel = neckline + depth,
                        StopLevel = Math.Min(low1, low2) - (_atr > 0 ? _atr * 0.5 : depth * 0.1),
                        Confidence = 0.70,
                        BarsInFormation = endIdx - startIdx,
                        Description = $"Double bottom at ${low1:F2} - neckline ${neckline:F2}"
                    };
                    
                    AddOrUpdatePattern(pattern);
                }
            }
        }
        
        // Find the two highest highs for double top
        var sortedByHigh = recent.OrderByDescending(c => c.High).Take(2).ToList();
        if (sortedByHigh.Count == 2)
        {
            double high1 = sortedByHigh[0].High;
            double high2 = sortedByHigh[1].High;
            
            if (Math.Abs(high1 - high2) / high1 < 0.01)
            {
                int idx1 = recent.IndexOf(sortedByHigh[0]);
                int idx2 = recent.IndexOf(sortedByHigh[1]);
                if (Math.Abs(idx1 - idx2) >= 5)
                {
                    int startIdx = Math.Min(idx1, idx2);
                    int endIdx = Math.Max(idx1, idx2);
                    double neckline = recent.Skip(startIdx).Take(endIdx - startIdx + 1).Min(c => c.Low);
                    double depth = Math.Max(high1, high2) - neckline;
                    
                    var pattern = new DetectedPattern
                    {
                        Type = PatternType.DoubleTop,
                        Stage = PatternStage.LateFormation,
                        StartTime = recent[startIdx].Timestamp,
                        EntryLevel = neckline * 0.998,
                        TargetLevel = neckline - depth,
                        StopLevel = Math.Max(high1, high2) + (_atr > 0 ? _atr * 0.5 : depth * 0.1),
                        Confidence = 0.70,
                        BarsInFormation = endIdx - startIdx,
                        Description = $"Double top at ${high1:F2} - neckline ${neckline:F2}"
                    };
                    
                    AddOrUpdatePattern(pattern);
                }
            }
        }
    }
    
    private void AddOrUpdatePattern(DetectedPattern pattern)
    {
        // Remove existing pattern of same type
        _activePatterns.RemoveAll(p => p.Type == pattern.Type);
        _activePatterns.Add(pattern);
        
        // Keep only top 5 patterns
        if (_activePatterns.Count > 5)
        {
            _activePatterns.RemoveAt(0);
        }
    }
    
    private void UpdatePatternStatus(double currentPrice)
    {
        foreach (var pattern in _activePatterns.ToList())
        {
            bool brokeOut = false;
            bool failed = false;
            
            switch (pattern.Type)
            {
                case PatternType.BullFlag:
                case PatternType.AscendingTriangle:
                case PatternType.DoubleBottom:
                    brokeOut = currentPrice >= pattern.EntryLevel;
                    failed = currentPrice <= pattern.StopLevel;
                    break;
                    
                case PatternType.BearFlag:
                case PatternType.DescendingTriangle:
                case PatternType.DoubleTop:
                    brokeOut = currentPrice <= pattern.EntryLevel;
                    failed = currentPrice >= pattern.StopLevel;
                    break;
            }
            
            if (brokeOut || failed)
            {
                _activePatterns.Remove(pattern);
                _completedPatterns.Add(pattern with 
                { 
                    Stage = failed ? PatternStage.Failed : PatternStage.BrokeOut,
                    BreakoutTime = DateTime.UtcNow
                });
                
                // Keep only last 20 completed patterns
                if (_completedPatterns.Count > 20)
                    _completedPatterns.RemoveAt(0);
            }
        }
    }
    
    private double CalculatePatternConfidence(PatternType type, double poleHeight, double flagRange, int flagBars)
    {
        double baseConfidence = 0.5;
        
        // Historical performance bonus
        var winRate = GetPatternWinRate(type);
        baseConfidence += (winRate - 0.5) * 0.3; // Up to ±15%
        
        // Tight consolidation bonus
        if (flagRange < poleHeight * 0.3)
            baseConfidence += 0.15;
        else if (flagRange < poleHeight * 0.4)
            baseConfidence += 0.10;
        
        // Ideal flag duration bonus (5-10 bars)
        if (flagBars >= 5 && flagBars <= 10)
            baseConfidence += 0.10;
        
        // RSI confirmation
        if (type == PatternType.BullFlag && _rsi > 40 && _rsi < 70)
            baseConfidence += 0.05;
        if (type == PatternType.BearFlag && _rsi > 30 && _rsi < 60)
            baseConfidence += 0.05;
        
        return Math.Clamp(baseConfidence, 0.3, 0.95);
    }
    
    #endregion
    
    #region Momentum Analysis
    
    private void UpdateMomentumTracking(Candlestick candle)
    {
        if (_candles.Count < 10)
            return;
        
        // Calculate momentum as rate of change over 10 bars
        double momentum = (candle.Close - _candles[^10].Close) / _candles[^10].Close * 100;
        _momentumHistory.Add(momentum);
        
        if (_momentumHistory.Count > 50)
            _momentumHistory.RemoveAt(0);
    }
    
    private MomentumAnalysis AnalyzeMomentum(double currentPrice)
    {
        if (_momentumHistory.Count < 5)
        {
            return new MomentumAnalysis
            {
                State = MomentumState.Exhausted,
                Reason = "Insufficient momentum history"
            };
        }
        
        double currentMomentum = _momentumHistory.Last();
        double prevMomentum = _momentumHistory[^2];
        double momentumChange = currentMomentum - prevMomentum;
        
        // Calculate rate of momentum change
        double roc = _momentumHistory.Count >= 5 
            ? (_momentumHistory[^1] - _momentumHistory[^5]) / 5 
            : 0;
        
        // Determine state
        MomentumState state;
        if (currentMomentum > 2)
            state = roc > 0 ? MomentumState.StrongBullish : MomentumState.WeakeningBullish;
        else if (currentMomentum > 0.5)
            state = roc > 0 ? MomentumState.ModerateBullish : MomentumState.WeakeningBullish;
        else if (currentMomentum > -0.5)
            state = MomentumState.Exhausted;
        else if (currentMomentum > -2)
            state = roc < 0 ? MomentumState.ModerateBearish : MomentumState.WeakeningBearish;
        else
            state = roc < 0 ? MomentumState.StrongBearish : MomentumState.WeakeningBearish;
        
        // Check for exhaustion
        bool isExhausting = (currentMomentum > 0 && roc < -0.1) || (currentMomentum < 0 && roc > 0.1);
        
        // Check for divergence (price vs momentum)
        bool isDiverging = false;
        if (_candles.Count >= 10)
        {
            double priceChange = (currentPrice - _candles[^10].Close) / _candles[^10].Close * 100;
            isDiverging = (priceChange > 1 && currentMomentum < 0) || (priceChange < -1 && currentMomentum > 0);
        }
        
        // Estimate bars until exhaustion
        int barsUntilExhaustion = 0;
        if (roc != 0 && isExhausting)
        {
            barsUntilExhaustion = (int)Math.Abs(currentMomentum / roc);
        }
        
        // Exhaustion probability
        double exhaustionProb = 0;
        if (Math.Abs(currentMomentum) > 3 && isExhausting)
            exhaustionProb = 0.8;
        else if (Math.Abs(currentMomentum) > 2 && isExhausting)
            exhaustionProb = 0.6;
        else if (isExhausting)
            exhaustionProb = 0.4;
        
        if (isDiverging)
            exhaustionProb = Math.Min(exhaustionProb + 0.2, 1.0);
        
        string reason = "";
        if (isExhausting && isDiverging)
            reason = "Momentum fading with price divergence - reversal likely";
        else if (isExhausting)
            reason = "Momentum fading - watch for reversal";
        else if (isDiverging)
            reason = "Price diverging from momentum - caution";
        else if (state == MomentumState.StrongBullish)
            reason = "Strong bullish momentum - trend intact";
        else if (state == MomentumState.StrongBearish)
            reason = "Strong bearish momentum - trend intact";
        
        return new MomentumAnalysis
        {
            State = state,
            MomentumValue = currentMomentum,
            RateOfChange = roc,
            IsExhausting = isExhausting,
            IsDiverging = isDiverging,
            BarsUntilExhaustion = barsUntilExhaustion,
            ExhaustionProbability = exhaustionProb,
            Reason = reason
        };
    }
    
    #endregion
    
    #region Volume Profile Analysis
    
    private void UpdateVolumeProfile(Candlestick candle)
    {
        // Distribute volume across price range
        double priceStep = (candle.High - candle.Low) / 10;
        if (priceStep <= 0) priceStep = 0.01;
        
        long volumePerLevel = candle.Volume / 10;
        for (double price = candle.Low; price <= candle.High; price += priceStep)
        {
            double roundedPrice = Math.Round(price * 100) / 100; // Round to cents
            if (!_volumeByPrice.ContainsKey(roundedPrice))
                _volumeByPrice[roundedPrice] = 0;
            _volumeByPrice[roundedPrice] += volumePerLevel;
        }
        
        // Cleanup old prices (keep within session range + buffer)
        var keysToRemove = _volumeByPrice.Keys
            .Where(p => p < _sessionLow * 0.95 || p > _sessionHigh * 1.05)
            .ToList();
        foreach (var key in keysToRemove)
            _volumeByPrice.Remove(key);
    }
    
    private VolumeProfileAnalysis AnalyzeVolumeProfile(double currentPrice)
    {
        if (_volumeByPrice.Count < 10)
        {
            return new VolumeProfileAnalysis
            {
                POC = currentPrice,
                VAH = currentPrice * 1.01,
                VAL = currentPrice * 0.99,
                CurrentZone = VolumeZone.Unknown
            };
        }
        
        // Find POC (price with highest volume)
        var poc = _volumeByPrice.OrderByDescending(kv => kv.Value).First().Key;
        
        // Calculate total volume
        long totalVolume = _volumeByPrice.Values.Sum();
        long targetVolume = (long)(totalVolume * 0.7); // 70% value area
        
        // Build value area from POC outward
        var sortedPrices = _volumeByPrice.Keys.OrderBy(p => p).ToList();
        int pocIndex = sortedPrices.IndexOf(poc);
        
        long volumeInVa = _volumeByPrice[poc];
        int lowerIdx = pocIndex - 1;
        int upperIdx = pocIndex + 1;
        
        while (volumeInVa < targetVolume && (lowerIdx >= 0 || upperIdx < sortedPrices.Count))
        {
            long lowerVol = lowerIdx >= 0 ? _volumeByPrice[sortedPrices[lowerIdx]] : 0;
            long upperVol = upperIdx < sortedPrices.Count ? _volumeByPrice[sortedPrices[upperIdx]] : 0;
            
            if (lowerVol >= upperVol && lowerIdx >= 0)
            {
                volumeInVa += lowerVol;
                lowerIdx--;
            }
            else if (upperIdx < sortedPrices.Count)
            {
                volumeInVa += upperVol;
                upperIdx++;
            }
            else
                break;
        }
        
        double val = lowerIdx >= 0 ? sortedPrices[lowerIdx + 1] : sortedPrices[0];
        double vah = upperIdx < sortedPrices.Count ? sortedPrices[upperIdx - 1] : sortedPrices[^1];
        
        // Determine current zone
        VolumeZone zone;
        double distToPoc = Math.Abs(currentPrice - poc) / poc * 100;
        
        if (distToPoc < 0.3)
            zone = VolumeZone.AtPOC;
        else if (currentPrice > vah)
            zone = VolumeZone.AboveVAH;
        else if (currentPrice < val)
            zone = VolumeZone.BelowVAL;
        else
            zone = VolumeZone.InValueArea;
        
        // Find high volume levels (nodes)
        var highVolumeLevels = _volumeByPrice
            .OrderByDescending(kv => kv.Value)
            .Take(5)
            .Select(kv => (kv.Key, kv.Value))
            .ToList();
        
        return new VolumeProfileAnalysis
        {
            POC = poc,
            VAH = vah,
            VAL = val,
            CurrentZone = zone,
            DistanceToPOC = distToPoc,
            IsPriceAtPOC = distToPoc < 0.5,
            VolumeWeightedPrice = _volumeByPrice.Sum(kv => kv.Key * kv.Value) / totalVolume,
            HighVolumeLevels = highVolumeLevels
        };
    }
    
    #endregion
    
    #region Accumulation/Distribution Analysis
    
    private void UpdateAccumulationDistribution(Candlestick candle)
    {
        // A/D = ((Close - Low) - (High - Close)) / (High - Low) * Volume
        double range = candle.High - candle.Low;
        if (range <= 0) return;
        
        double clv = ((candle.Close - candle.Low) - (candle.High - candle.Close)) / range;
        double ad = clv * candle.Volume;
        
        double prevAd = _adLineHistory.Count > 0 ? _adLineHistory.Last() : 0;
        _adLineHistory.Add(prevAd + ad);
        
        if (_adLineHistory.Count > 100)
            _adLineHistory.RemoveAt(0);
    }
    
    private AccumulationDistributionAnalysis AnalyzeAccumulationDistribution(double currentPrice)
    {
        if (_adLineHistory.Count < 10)
        {
            return new AccumulationDistributionAnalysis
            {
                DivergenceType = "None"
            };
        }
        
        double currentAd = _adLineHistory.Last();
        double adTrend = (currentAd - _adLineHistory[^10]) / 10;
        
        // Determine accumulation/distribution
        bool isAccumulating = adTrend > 0;
        bool isDistributing = adTrend < 0;
        
        // Check for divergence
        string divergenceType = "None";
        if (_candles.Count >= 10)
        {
            double priceChange = _candles[^1].Close - _candles[^10].Close;
            bool priceUp = priceChange > 0;
            bool adUp = adTrend > 0;
            
            if (priceUp && !adUp)
            {
                divergenceType = "Bearish";
            }
            else if (!priceUp && adUp)
            {
                divergenceType = "Bullish";
            }
        }
        
        return new AccumulationDistributionAnalysis
        {
            ADLine = currentAd,
            ADTrend = adTrend,
            IsAccumulating = isAccumulating,
            IsDistributing = isDistributing,
            IsDiverging = divergenceType != "None",
            DivergenceType = divergenceType,
            ConfidenceScore = Math.Abs(adTrend) > 1000 ? 0.8 : 0.5
        };
    }
    
    #endregion
    
    #region Evidence Gathering
    
    private void GatherMomentumEvidence(MomentumAnalysis momentum, 
                                         List<string> bullish, List<string> bearish)
    {
        switch (momentum.State)
        {
            case MomentumState.StrongBullish:
                bullish.Add("Strong bullish momentum");
                break;
            case MomentumState.ModerateBullish:
                bullish.Add("Moderate bullish momentum");
                break;
            case MomentumState.StrongBearish:
                bearish.Add("Strong bearish momentum");
                break;
            case MomentumState.ModerateBearish:
                bearish.Add("Moderate bearish momentum");
                break;
            case MomentumState.WeakeningBullish:
                bearish.Add("Bullish momentum exhausting");
                break;
            case MomentumState.WeakeningBearish:
                bullish.Add("Bearish momentum exhausting");
                break;
        }
        
        if (momentum.IsDiverging)
        {
            if (momentum.MomentumValue < 0)
                bullish.Add("Bullish momentum divergence");
            else
                bearish.Add("Bearish momentum divergence");
        }
    }
    
    private void GatherVolumeEvidence(VolumeProfileAnalysis vp, double currentPrice,
                                       List<string> bullish, List<string> bearish)
    {
        switch (vp.CurrentZone)
        {
            case VolumeZone.BelowVAL:
                bullish.Add($"Price in discount zone (below VAL ${vp.VAL:F2})");
                break;
            case VolumeZone.AboveVAH:
                bearish.Add($"Price in premium zone (above VAH ${vp.VAH:F2})");
                break;
            case VolumeZone.AtPOC:
                bullish.Add($"Price at POC ${vp.POC:F2} - high volume support");
                bearish.Add($"Price at POC ${vp.POC:F2} - potential resistance");
                break;
        }
        
        // Check proximity to high volume nodes
        foreach (var (price, volume) in vp.HighVolumeLevels.Take(3))
        {
            double dist = Math.Abs(currentPrice - price) / currentPrice * 100;
            if (dist < 0.5 && price < currentPrice)
                bullish.Add($"Near high volume support ${price:F2}");
            else if (dist < 0.5 && price > currentPrice)
                bearish.Add($"Near high volume resistance ${price:F2}");
        }
    }
    
    private void GatherAccumDistEvidence(AccumulationDistributionAnalysis ad,
                                          List<string> bullish, List<string> bearish)
    {
        if (ad.IsAccumulating)
            bullish.Add("Smart money accumulating");
        if (ad.IsDistributing)
            bearish.Add("Smart money distributing");
        
        if (ad.IsDiverging)
        {
            if (ad.DivergenceType == "Bullish")
                bullish.Add("Bullish A/D divergence - hidden buying");
            else if (ad.DivergenceType == "Bearish")
                bearish.Add("Bearish A/D divergence - hidden selling");
        }
    }
    
    private void GatherPatternEvidence(double currentPrice,
                                        List<string> bullish, List<string> bearish)
    {
        foreach (var pattern in _activePatterns)
        {
            if (pattern.Stage < PatternStage.MidFormation) continue;
            
            switch (pattern.Type)
            {
                case PatternType.BullFlag:
                case PatternType.AscendingTriangle:
                case PatternType.DoubleBottom:
                case PatternType.InverseHeadAndShoulders:
                case PatternType.CupAndHandle:
                    bullish.Add($"{pattern.Type} forming (Conf={pattern.Confidence:P0})");
                    break;
                    
                case PatternType.BearFlag:
                case PatternType.DescendingTriangle:
                case PatternType.DoubleTop:
                case PatternType.HeadAndShoulders:
                    bearish.Add($"{pattern.Type} forming (Conf={pattern.Confidence:P0})");
                    break;
            }
            
            // Check if price is near pattern entry
            double distToEntry = Math.Abs(currentPrice - pattern.EntryLevel) / currentPrice * 100;
            if (distToEntry < 1 && pattern.Stage >= PatternStage.LateFormation)
            {
                bool isBullish = pattern.Type is PatternType.BullFlag or PatternType.AscendingTriangle
                                 or PatternType.DoubleBottom;
                if (isBullish)
                    bullish.Add($"Near {pattern.Type} breakout entry ${pattern.EntryLevel:F2}");
                else
                    bearish.Add($"Near {pattern.Type} breakdown entry ${pattern.EntryLevel:F2}");
            }
        }
    }
    
    private void GatherPriceStructureEvidence(double currentPrice,
                                               List<string> bullish, List<string> bearish)
    {
        if (_candles.Count < 20) return;
        
        // EMA alignment
        if (_ema9 > 0 && _ema21 > 0 && _ema50 > 0)
        {
            if (currentPrice > _ema9 && _ema9 > _ema21 && _ema21 > _ema50)
                bullish.Add("Bullish EMA stack (Price > 9 > 21 > 50)");
            else if (currentPrice < _ema9 && _ema9 < _ema21 && _ema21 < _ema50)
                bearish.Add("Bearish EMA stack (Price < 9 < 21 < 50)");
        }
        
        // MACD
        if (_macdHistogram > 0 && _macdHistogram > _momentumHistory.LastOrDefault())
            bullish.Add("MACD histogram rising");
        else if (_macdHistogram < 0 && _macdHistogram < _momentumHistory.LastOrDefault())
            bearish.Add("MACD histogram falling");
        
        // RSI
        if (_rsi < 30)
            bullish.Add($"RSI oversold ({_rsi:F0})");
        else if (_rsi > 70)
            bearish.Add($"RSI overbought ({_rsi:F0})");
        
        // ADX trend strength
        if (_adx > 25)
        {
            if (_plusDi > _minusDi)
                bullish.Add($"Strong uptrend (ADX={_adx:F0}, +DI>{-_minusDi:F0})");
            else
                bearish.Add($"Strong downtrend (ADX={_adx:F0}, -DI>{_minusDi:F0})");
        }
        
        // Higher highs/lows vs lower highs/lows (last 10 bars)
        var recent10 = _candles.TakeLast(10).ToList();
        int higherHighs = 0, lowerLows = 0;
        for (int i = 1; i < recent10.Count; i++)
        {
            if (recent10[i].High > recent10[i - 1].High) higherHighs++;
            if (recent10[i].Low < recent10[i - 1].Low) lowerLows++;
        }
        
        if (higherHighs >= 6)
            bullish.Add($"Making higher highs ({higherHighs}/9)");
        if (lowerLows >= 6)
            bearish.Add($"Making lower lows ({lowerLows}/9)");
    }
    
    #endregion
    
    #region Confidence & Action Calculation
    
    private double CalculateEmpiricalConfidence(int bullishCount, int bearishCount,
                                                 MomentumAnalysis momentum,
                                                 VolumeProfileAnalysis volume,
                                                 AccumulationDistributionAnalysis ad)
    {
        double confidence = 30; // Base
        
        // Evidence count difference
        int diff = Math.Abs(bullishCount - bearishCount);
        confidence += diff * 10; // +10 per evidence advantage
        
        // Momentum alignment
        bool momentumBullish = momentum.State is MomentumState.StrongBullish or MomentumState.ModerateBullish;
        bool momentumBearish = momentum.State is MomentumState.StrongBearish or MomentumState.ModerateBearish;
        
        if ((momentumBullish && bullishCount > bearishCount) || (momentumBearish && bearishCount > bullishCount))
            confidence += 15;
        
        // Exhaustion penalty
        if (momentum.IsExhausting)
            confidence -= 10;
        
        // Pattern bonus
        if (_activePatterns.Any(p => p.Stage >= PatternStage.LateFormation && p.Confidence >= 0.65))
            confidence += 15;
        
        // A/D agreement
        if ((ad.IsAccumulating && bullishCount > bearishCount) || (ad.IsDistributing && bearishCount > bullishCount))
            confidence += 10;
        
        // Volume zone
        if (volume.CurrentZone == VolumeZone.BelowVAL && bullishCount > bearishCount)
            confidence += 10; // Buying at discount with bullish signals
        if (volume.CurrentZone == VolumeZone.AboveVAH && bearishCount > bullishCount)
            confidence += 10; // Selling at premium with bearish signals
        
        return Math.Clamp(confidence, 0, 100);
    }
    
    private string DetermineRecommendedAction(bool isHighConfLong, bool isHighConfShort, bool shouldWait,
                                               int bullCount, int bearCount,
                                               MomentumAnalysis momentum, double currentPrice)
    {
        if (isHighConfLong && !momentum.IsExhausting)
            return "HIGH CONFIDENCE LONG - Strong bullish evidence, enter on pullback";
        
        if (isHighConfShort && !momentum.IsExhausting)
            return "HIGH CONFIDENCE SHORT - Strong bearish evidence, enter on bounce";
        
        if (momentum.IsExhausting && momentum.ExhaustionProbability > 0.7)
        {
            if (momentum.MomentumValue > 0)
                return "MOMENTUM EXHAUSTING - Wait for pullback before long";
            else
                return "MOMENTUM EXHAUSTING - Wait for bounce before short";
        }
        
        if (shouldWait)
        {
            if (bullCount > bearCount)
                return $"WAIT - Bullish bias ({bullCount} vs {bearCount}) but need more confirmation";
            else if (bearCount > bullCount)
                return $"WAIT - Bearish bias ({bearCount} vs {bullCount}) but need more confirmation";
            else
                return "WAIT - Mixed signals, monitor for direction";
        }
        
        // Check for pattern setups
        var readyPattern = _activePatterns.FirstOrDefault(p => p.Stage >= PatternStage.ReadyToBreak);
        if (readyPattern != null)
        {
            return $"PATTERN WATCH - {readyPattern.Type} ready to break. Entry: ${readyPattern.EntryLevel:F2}";
        }
        
        return "MONITOR - Building evidence, no clear setup yet";
    }
    
    private (double longEntry, double shortEntry, double sl, double tp) CalculateEntryExitLevels(
        double currentPrice, VolumeProfileAnalysis volume)
    {
        // Ideal long entry: Near VAL or POC (value)
        double longEntry = Math.Min(volume.VAL, volume.POC);
        
        // Ideal short entry: Near VAH or POC
        double shortEntry = Math.Max(volume.VAH, volume.POC);
        
        // Stop loss: Beyond the value area
        double sl = longEntry - (_atr > 0 ? _atr * 1.5 : (volume.VAH - volume.VAL) * 0.3);
        
        // Take profit: Opposite end of value area + extension
        double tp = volume.VAH + (volume.VAH - volume.VAL) * 0.5;
        
        return (longEntry, shortEntry, sl, tp);
    }
    
    #endregion
}
