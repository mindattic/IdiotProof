// ============================================================================
// AIAdvisor - ChatGPT-Powered Trading Decision Support
// ============================================================================
//
// PURPOSE:
// Integrates OpenAI to provide intelligent trading decision support during:
// - Live trading: Provide AI analysis alongside indicator-based scoring
//
// USAGE:
// var advisor = new AIAdvisor();
// var analysis = await advisor.AnalyzeEntryAsync(symbol, snapshot, score);
//
// ============================================================================

using System.Text;
using System.Text.Json;
using IdiotProof.Calculators;
using IdiotProof.Helpers;
using IdiotProof.Services;

namespace IdiotProof.Learning;

/// <summary>
/// AI analysis result with trading recommendation.
/// </summary>
public sealed class AIAnalysis
{
    /// <summary>Suggested action: "LONG", "SHORT", or "WAIT".</summary>
    public string Action { get; set; } = "WAIT";
    
    /// <summary>Confidence level 0-100.</summary>
    public int Confidence { get; set; }
    
    /// <summary>Short reasoning for the decision.</summary>
    public string Reasoning { get; set; } = "";
    
    /// <summary>Key risk factors identified.</summary>
    public List<string> RiskFactors { get; set; } = [];
    
    /// <summary>Suggested adjustments to TP/SL if any.</summary>
    public string TpSlAdvice { get; set; } = "";
    
    /// <summary>Status of user-defined custom rules: "MET", "NOT_MET", or "NO_RULES".</summary>
    public string RuleStatus { get; set; } = "NO_RULES";
    
    /// <summary>Whether this analysis is usable (API succeeded).</summary>
    public bool IsUsable { get; set; }
    
    /// <summary>Error message if API failed.</summary>
    public string? Error { get; set; }
    
    /// <summary>Raw response from the model.</summary>
    public string RawResponse { get; set; } = "";
    
    /// <summary>Whether the user's custom rules are satisfied.</summary>
    public bool AreRulesMet => RuleStatus.Equals("MET", StringComparison.OrdinalIgnoreCase);
    
    public override string ToString()
    {
        if (!IsUsable) return $"[AI] Error: {Error}";
        var ruleInfo = RuleStatus != "NO_RULES" ? $" [Rules: {RuleStatus}]" : "";
        return $"[AI] {Action} (Conf={Confidence}%){ruleInfo}: {Reasoning}";
    }
}

/// <summary>
/// Learning method comparison result for AI analysis.
/// </summary>
public sealed class LearningMethodSummary
{
    public string MethodName { get; set; } = "";
    public double ValidationFitness { get; set; }
    public double ValidationWinRate { get; set; }
    public double ValidationPnL { get; set; }
    public bool IsBest { get; set; }
}

/// <summary>
/// AI-powered trading advisor that integrates with the learning system.
/// Uses ChatGPT to provide decision support alongside indicators.
/// </summary>
public sealed class AIAdvisor : IDisposable
{
    private readonly OpenAIService _openai;
    private readonly Dictionary<string, (AIAnalysis analysis, DateTime expiry)> _cache = new();
    private readonly TimeSpan _cacheExpiry = TimeSpan.FromMinutes(5);
    private readonly object _lock = new();
    
    private bool _disposed;
    private int _callCount;
    private DateTime _lastCall = DateTime.MinValue;
    private readonly TimeSpan _minCallInterval = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Minimum AI confidence required to enter a trade (0-100).
    /// Default is 55. Set to 0 to disable AI gate.
    /// NOTE: 85 was too strict - blocked 95%+ of trades including profitable ones.
    /// </summary>
    public int MinConfidenceRequired { get; set; } = 55;  // Lowered from 85 - was blocking too many trades

    /// <summary>
    /// Detects if the market is in a "chop" zone where trading should be avoided.
    /// </summary>
    /// <param name="snapshot">Current indicator snapshot</param>
    /// <returns>True if chop is detected</returns>
    public static bool IsChopDetected(IndicatorSnapshot snapshot)
    {
        int chopSignals = 0;
        
        // RSI in neutral zone (45-55)
        if (snapshot.Rsi >= 45 && snapshot.Rsi <= 55)
            chopSignals++;
        
        // ADX low (below 20 = weak trend)
        if (snapshot.Adx < 20)
            chopSignals++;
        
        // MACD compressed (signal and MACD close together)
        if (Math.Abs(snapshot.Macd - snapshot.MacdSignal) < 0.01 && 
            Math.Abs(snapshot.MacdHistogram) < 0.02)
            chopSignals++;
        
        // Price near VWAP (within 0.3%)
        if (snapshot.Vwap > 0)
        {
            double vwapDistance = Math.Abs(snapshot.Price - snapshot.Vwap) / snapshot.Vwap;
            if (vwapDistance < 0.003)
                chopSignals++;
        }
        
        // DI lines close together (no directional conviction)
        if (Math.Abs(snapshot.PlusDi - snapshot.MinusDi) < 5)
            chopSignals++;
        
        // Chop = 3+ signals present
        return chopSignals >= 3;
    }

    /// <summary>
    /// Calculates a synthetic confidence score (0-100) based on indicator alignment.
    /// This is a fast, local calculation that doesn't require API calls.
    /// Used for backtest mode where real AI calls would be too slow.
    /// Implements the LONG/SHORT requirements with EMA 34 focus.
    /// </summary>
    /// <param name="snapshot">Current indicator snapshot</param>
    /// <param name="isLong">True for long, false for short</param>
    /// <returns>Confidence 0-100 (55+ required for valid trade - relaxed from 85)</returns>
    public static int CalculateSyntheticConfidence(IndicatorSnapshot snapshot, bool isLong)
    {
        // Start with base score
        int confidence = 60;
        int requirementsMet = 0;
        int totalRequirements = 9;
        
        // =================================================================
        // CHOP DETECTION - Caps confidence at 49
        // =================================================================
        if (IsChopDetected(snapshot))
        {
            return 42; // Chop detected, no trade possible
        }
        
        // =================================================================
        // EMA 34 - PRIMARY DECISION LEVEL (CRITICAL)
        // =================================================================
        bool hasEma34 = snapshot.Ema34 > 0;
        if (hasEma34)
        {
            // Check price vs EMA 34
            bool priceAboveEma34 = snapshot.Price > snapshot.Ema34;
            
            if (isLong)
            {
                if (priceAboveEma34)
                {
                    confidence += 15; // Price above EMA 34 - requirement met
                    requirementsMet++;
                }
                else
                {
                    confidence -= 20; // CRITICAL: Price below EMA 34 for long = major penalty
                }
            }
            else // Short
            {
                if (!priceAboveEma34)
                {
                    confidence += 15; // Price below EMA 34 - requirement met
                    requirementsMet++;
                }
                else
                {
                    confidence -= 20; // CRITICAL: Price above EMA 34 for short = major penalty
                }
            }
        }
        else
        {
            // Fallback to EMA 21 if EMA 34 not available
            if (snapshot.Ema21 > 0)
            {
                bool priceAboveEma21 = snapshot.Price > snapshot.Ema21;
                if ((isLong && priceAboveEma21) || (!isLong && !priceAboveEma21))
                {
                    confidence += 10;
                    requirementsMet++;
                }
                else
                {
                    confidence -= 15;
                }
            }
        }

        // =================================================================
        // VWAP ALIGNMENT
        // =================================================================
        if (snapshot.Vwap > 0)
        {
            bool vwapBullish = snapshot.Price > snapshot.Vwap;
            if ((isLong && vwapBullish) || (!isLong && !vwapBullish))
            {
                confidence += 10;
                requirementsMet++;
            }
            else
            {
                confidence -= 12; // Going against VWAP
            }
        }

        // =================================================================
        // RSI REQUIREMENTS (Long: >50 rising, Short: <45 falling)
        // =================================================================
        if (snapshot.Rsi > 0)
        {
            if (isLong)
            {
                if (snapshot.Rsi > 50 && snapshot.Rsi < 70)
                {
                    confidence += 10; // RSI above 50, room to run
                    requirementsMet++;
                }
                else if (snapshot.Rsi <= 50)
                {
                    confidence -= 10; // RSI below 50 = not bullish
                }
                else if (snapshot.Rsi >= 70)
                {
                    confidence -= 15; // Overbought
                }
            }
            else // Short
            {
                if (snapshot.Rsi < 45)
                {
                    confidence += 10; // RSI below 45, bearish
                    requirementsMet++;
                }
                else if (snapshot.Rsi >= 50)
                {
                    confidence -= 10; // RSI above 50 = not bearish
                }
                else if (snapshot.Rsi <= 30)
                {
                    confidence -= 15; // Oversold
                }
            }
        }

        // =================================================================
        // MACD ALIGNMENT (Bullish/Bearish crossover or hook)
        // =================================================================
        if (snapshot.Macd != 0 || snapshot.MacdSignal != 0)
        {
            bool macdBullish = snapshot.Macd > snapshot.MacdSignal && snapshot.MacdHistogram > 0;
            bool macdBearish = snapshot.Macd < snapshot.MacdSignal && snapshot.MacdHistogram < 0;
            
            if ((isLong && macdBullish) || (!isLong && macdBearish))
            {
                confidence += 12;
                requirementsMet++;
            }
            else if ((isLong && macdBearish) || (!isLong && macdBullish))
            {
                confidence -= 15; // Going against MACD
            }
        }

        // =================================================================
        // ADX TREND STRENGTH (Must be rising, above 20)
        // =================================================================
        if (snapshot.Adx > 0)
        {
            bool diPositive = snapshot.PlusDi > snapshot.MinusDi;
            bool strongTrend = snapshot.Adx >= 20;
            
            if (strongTrend)
            {
                requirementsMet++; // ADX rising/strong = trend exists
                
                if ((isLong && diPositive) || (!isLong && !diPositive))
                {
                    confidence += 12; // Direction aligns
                    requirementsMet++; // DI alignment counts as requirement
                }
                else
                {
                    confidence -= 15; // Going against directional movement
                }
            }
            else
            {
                confidence -= 10; // Weak trend = harder to trade
            }
        }

        // =================================================================
        // VOLUME CONFIRMATION
        // =================================================================
        if (snapshot.VolumeRatio > 0)
        {
            if (snapshot.VolumeRatio >= 1.2)
            {
                confidence += 5;
                requirementsMet++;
            }
            else if (snapshot.VolumeRatio < 0.7)
            {
                confidence -= 5;
            }
        }

        // =================================================================
        // REQUIREMENTS MET BONUS/PENALTY
        // =================================================================
        double requirementRatio = (double)requirementsMet / totalRequirements;
        if (requirementRatio >= 0.8)
        {
            confidence += 15; // Most requirements met - boost to 85+ potential
        }
        else if (requirementRatio >= 0.6)
        {
            confidence += 5;
        }
        else if (requirementRatio < 0.5)
        {
            confidence -= 15; // Too many requirements failed
        }

        return Math.Clamp(confidence, 0, 100);
    }

    /// <summary>
    /// Synchronously get AI confidence for a trade entry.
    /// Blocks until the API returns. Use sparingly in live trading.
    /// </summary>
    public AIAnalysis AnalyzeEntrySync(
        string symbol,
        IndicatorSnapshot snapshot,
        MarketScoreResult score,
        int timeoutMs = 10000)
    {
        try
        {
            var task = AnalyzeEntryAsync(symbol, snapshot, score, CancellationToken.None);
            if (task.Wait(timeoutMs))
            {
                return task.Result;
            }
            return new AIAnalysis
            {
                IsUsable = false,
                Error = "AI analysis timed out",
                Confidence = 0
            };
        }
        catch (Exception ex)
        {
            return new AIAnalysis
            {
                IsUsable = false,
                Error = $"AI analysis failed: {ex.Message}",
                Confidence = 0
            };
        }
    }

    /// <summary>
    /// Check if AI approves a trade. Returns true if:
    /// 1. AI is not configured (disabled, allow trade), OR
    /// 2. AI confidence >= MinConfidenceRequired
    /// </summary>
    public (bool approved, int confidence, string reason) CheckTradeApproval(
        string symbol,
        IndicatorSnapshot snapshot,
        bool isLong,
        MarketScoreResult score,
        bool useSyntheticForSpeed = false)
    {
        if (!IsConfigured)
        {
            // AI not configured - use synthetic confidence as fallback
            int syntheticConf = CalculateSyntheticConfidence(snapshot, isLong);
            bool syntheticApproved = syntheticConf >= MinConfidenceRequired;
            return (syntheticApproved, syntheticConf, syntheticApproved 
                ? $"Synthetic confidence {syntheticConf}% (AI not configured)"
                : $"BLOCKED: Synthetic confidence {syntheticConf}% < {MinConfidenceRequired}% required");
        }

        if (useSyntheticForSpeed)
        {
            // Fast mode - no API call
            int syntheticConf = CalculateSyntheticConfidence(snapshot, isLong);
            bool approved = syntheticConf >= MinConfidenceRequired;
            return (approved, syntheticConf, approved 
                ? $"Synthetic confidence {syntheticConf}% (fast mode)"
                : $"BLOCKED: Synthetic confidence {syntheticConf}% < {MinConfidenceRequired}% required");
        }

        // Full AI mode - synchronous call
        var analysis = AnalyzeEntrySync(symbol, snapshot, score, 10000);
        
        if (!analysis.IsUsable)
        {
            // API failed - fall back to synthetic
            int fallbackConf = CalculateSyntheticConfidence(snapshot, isLong);
            bool fallbackApproved = fallbackConf >= MinConfidenceRequired;
            return (fallbackApproved, fallbackConf, $"AI failed ({analysis.Error}), using synthetic {fallbackConf}%");
        }

        bool aiApproved = analysis.Confidence >= MinConfidenceRequired;
        string direction = isLong ? "LONG" : "SHORT";
        string aiAction = analysis.Action;
        
        // Check if AI agrees with our direction
        bool directionMatches = (isLong && aiAction == "LONG") || (!isLong && aiAction == "SHORT");
        
        // COVER means AI suggests exiting, not entering - block new entries
        if (aiAction == "COVER")
        {
            return (false, analysis.Confidence,
                $"BLOCKED: AI recommends COVER (exit position), not new entry. Reason: {analysis.Reasoning}");
        }

        if (aiAction == "WAIT")
        {
            return (false, analysis.Confidence,
                $"BLOCKED: AI recommends WAIT (Conf={analysis.Confidence}%). Reason: {analysis.Reasoning}");
        }
        
        if (!directionMatches)
        {
            // AI suggests opposite direction - definitely block
            return (false, analysis.Confidence, 
                $"BLOCKED: AI suggests {aiAction} but we want {direction}. Reason: {analysis.Reasoning}");
        }

        if (!aiApproved)
        {
            return (false, analysis.Confidence,
                $"BLOCKED: AI confidence {analysis.Confidence}% < {MinConfidenceRequired}% required");
        }

        // Strategy rules are advisory only - log status but don't block
        var ruleInfo = analysis.RuleStatus != "NO_RULES" ? $" [Rules: {analysis.RuleStatus}]" : "";
        return (true, analysis.Confidence, $"AI approved {direction} with {analysis.Confidence}% confidence{ruleInfo}");
    }
    
    // Track decision accuracy for self-improvement
    private readonly List<(AIAnalysis analysis, bool wasCorrect)> _history = [];

    /// <summary>
    /// Creates an AI advisor using the default OpenAI configuration.
    /// </summary>
    public AIAdvisor()
    {
        _openai = new OpenAIService();
    }
    
    /// <summary>
    /// Creates an AI advisor with a custom OpenAI service.
    /// </summary>
    public AIAdvisor(OpenAIService openai)
    {
        _openai = openai;
    }

    /// <summary>
    /// Whether the AI advisor is configured and ready to use.
    /// </summary>
    public bool IsConfigured => _openai.IsConfigured;
    
    /// <summary>
    /// Number of API calls made during this session.
    /// </summary>
    public int CallCount => _callCount;

    /// <summary>
    /// Analyze a potential entry decision during live trading.
    /// Combines indicator data with market score analysis.
    /// </summary>
    public async Task<AIAnalysis> AnalyzeEntryAsync(
        string symbol,
        IndicatorSnapshot snapshot,
        MarketScoreResult score,
        CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            return new AIAnalysis
            {
                IsUsable = false,
                Error = "OpenAI API key not configured"
            };
        }

        // Check cache
        var cacheKey = $"{symbol}_{DateTime.UtcNow:yyyyMMdd_HHmm}_{score.TotalScore}";
        lock (_lock)
        {
            if (_cache.TryGetValue(cacheKey, out var cached) && cached.expiry > DateTime.UtcNow)
            {
                return cached.analysis;
            }
        }

        // Rate limiting
        var timeSinceLastCall = DateTime.UtcNow - _lastCall;
        if (timeSinceLastCall < _minCallInterval)
        {
            await Task.Delay(_minCallInterval - timeSinceLastCall, ct);
        }

        var prompt = BuildEntryAnalysisPrompt(symbol, snapshot, score);
        
        try
        {
            _lastCall = DateTime.UtcNow;
            _callCount++;
            
            var reply = await _openai.AskWithInstructionsAsync(
                prompt,
                GetTradingSystemPrompt(),
                ct);
            
            var analysis = ParseAnalysisResponse(reply.Text);
            analysis.RawResponse = reply.Text;
            
            // Cache the result
            lock (_lock)
            {
                _cache[cacheKey] = (analysis, DateTime.UtcNow.Add(_cacheExpiry));
            }
            
            return analysis;
        }
        catch (Exception ex)
        {
            return new AIAnalysis
            {
                IsUsable = false,
                Error = ex.Message
            };
        }
    }

    /// <summary>
    /// Analyze learning method results and recommend the best approach.
    /// </summary>
    public async Task<AIAnalysis> AnalyzeLearningResultsAsync(
        string symbol,
        IEnumerable<LearningMethodSummary> methods,
        int totalTrainingBars,
        int totalValidationBars,
        CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            return new AIAnalysis
            {
                IsUsable = false,
                Error = "OpenAI API key not configured"
            };
        }

        var prompt = BuildLearningAnalysisPrompt(symbol, methods, totalTrainingBars, totalValidationBars);
        
        try
        {
            _lastCall = DateTime.UtcNow;
            _callCount++;
            
            var reply = await _openai.AskWithInstructionsAsync(
                prompt,
                GetLearningSystemPrompt(),
                ct);
            
            var analysis = ParseLearningResponse(reply.Text);
            analysis.RawResponse = reply.Text;
            
            return analysis;
        }
        catch (Exception ex)
        {
            return new AIAnalysis
            {
                IsUsable = false,
                Error = ex.Message
            };
        }
    }

    /// <summary>
    /// Analyze a trade outcome for learning purposes.
    /// </summary>
    public async Task<string> AnalyzeTradeOutcomeAsync(
        string symbol,
        bool isLong,
        double entryPrice,
        double exitPrice,
        IndicatorSnapshot entrySnapshot,
        string exitReason,
        CancellationToken ct = default)
    {
        if (!IsConfigured)
            return "AI analysis unavailable - API key not configured";

        var pnlPercent = isLong 
            ? (exitPrice - entryPrice) / entryPrice * 100
            : (entryPrice - exitPrice) / entryPrice * 100;
        
        var prompt = $"""
            Analyze this {(isLong ? "LONG" : "SHORT")} trade on {symbol}:
            
            Entry: ${entryPrice:F2}
            Exit: ${exitPrice:F2} ({exitReason})
            P&L: {pnlPercent:+0.00;-0.00}%
            
            Entry Conditions:
            - RSI: {entrySnapshot.Rsi:F1}
            - MACD Histogram: {entrySnapshot.MacdHistogram:F4}
            - ADX: {entrySnapshot.Adx:F1}
            - Volume Ratio: {entrySnapshot.VolumeRatio:F2}x
            - Price vs VWAP: {((entrySnapshot.Price - entrySnapshot.Vwap) / entrySnapshot.Vwap * 100):+0.00;-0.00}%
            
            In 2-3 sentences: What could be improved?
            """;

        try
        {
            var reply = await _openai.AskAsync(prompt, ct);
            return reply.Text;
        }
        catch (Exception ex)
        {
            return $"Analysis failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Get AI explanation for a specific indicator calculation.
    /// </summary>
    public async Task<string> ExplainIndicatorAsync(string indicatorName, CancellationToken ct = default)
    {
        if (!IsConfigured)
            return "AI unavailable - API key not configured";

        try
        {
            var reply = await _openai.GetMathModelAsync(indicatorName, ct);
            return reply.Text;
        }
        catch (Exception ex)
        {
            return $"Explanation failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Record whether an AI recommendation was correct for future learning.
    /// </summary>
    public void RecordOutcome(AIAnalysis analysis, bool wasCorrect)
    {
        lock (_lock)
        {
            _history.Add((analysis, wasCorrect));
            
            // Keep only last 100 records
            if (_history.Count > 100)
            {
                _history.RemoveAt(0);
            }
        }
    }

    /// <summary>
    /// Get accuracy statistics for AI recommendations.
    /// </summary>
    public (int total, int correct, double accuracy) GetAccuracyStats()
    {
        lock (_lock)
        {
            if (_history.Count == 0)
                return (0, 0, 0);
            
            var correct = _history.Count(h => h.wasCorrect);
            return (_history.Count, correct, (double)correct / _history.Count * 100);
        }
    }

    private string BuildEntryAnalysisPrompt(
        string symbol,
        IndicatorSnapshot snapshot,
        MarketScoreResult score)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine($"TRADE ANALYSIS REQUEST: {symbol}");
        sb.AppendLine($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss} ET");
        sb.AppendLine();
        
        // ============================================================
        // SECTION 1: PRICE & ANCHOR LEVELS
        // ============================================================
        sb.AppendLine("=== PRICE & ANCHOR LEVELS ===");
        sb.AppendLine($"Current Price: ${snapshot.Price:F2}");
        sb.AppendLine($"VWAP: ${snapshot.Vwap:F2} ({((snapshot.Price - snapshot.Vwap) / Math.Max(snapshot.Vwap, 0.01) * 100):+0.00;-0.00}% from VWAP)");
        sb.AppendLine();
        
        // ============================================================
        // SECTION 1.5: PREVIOUS DAY LEVELS (S/R)
        // ============================================================
        if (snapshot.PrevDayHigh > 0 && snapshot.PrevDayLow > 0)
        {
            sb.AppendLine("=== PREVIOUS DAY LEVELS (KEY S/R) ===");
            sb.AppendLine($"PDH (Resistance): ${snapshot.PrevDayHigh:F2} ({((snapshot.Price - snapshot.PrevDayHigh) / Math.Max(snapshot.PrevDayHigh, 0.01) * 100):+0.00;-0.00}% from PDH)");
            sb.AppendLine($"PDL (Support):    ${snapshot.PrevDayLow:F2} ({((snapshot.Price - snapshot.PrevDayLow) / Math.Max(snapshot.PrevDayLow, 0.01) * 100):+0.00;-0.00}% from PDL)");
            sb.AppendLine($"PDC (Pivot):      ${snapshot.PrevDayClose:F2} ({((snapshot.Price - snapshot.PrevDayClose) / Math.Max(snapshot.PrevDayClose, 0.01) * 100):+0.00;-0.00}% from PDC)");
            
            double prevRange = snapshot.PrevDayHigh - snapshot.PrevDayLow;
            sb.AppendLine($"Prev Day Range:   ${prevRange:F2}");
            
            // Position within previous range
            if (prevRange > 0)
            {
                double posInRange = (snapshot.Price - snapshot.PrevDayLow) / prevRange;
                string zone = posInRange > 1.0 ? "ABOVE yesterday (breakout)" :
                              posInRange < 0.0 ? "BELOW yesterday (breakdown)" :
                              posInRange > 0.67 ? "PREMIUM ZONE (upper third)" :
                              posInRange < 0.33 ? "DISCOUNT ZONE (lower third)" :
                              "MID-RANGE (equilibrium)";
                sb.AppendLine($"Position in Range: {posInRange:P0} = {zone}");
            }
            
            if (snapshot.TwoDayHigh > 0 && snapshot.TwoDayLow > 0)
            {
                sb.AppendLine($"2-Day High:       ${snapshot.TwoDayHigh:F2} ({(snapshot.Price > snapshot.TwoDayHigh ? "ABOVE - multi-day breakout!" : $"{((snapshot.TwoDayHigh - snapshot.Price) / snapshot.Price * 100):F2}% below")})");
                sb.AppendLine($"2-Day Low:        ${snapshot.TwoDayLow:F2} ({(snapshot.Price < snapshot.TwoDayLow ? "BELOW - multi-day breakdown!" : $"{((snapshot.Price - snapshot.TwoDayLow) / snapshot.Price * 100):F2}% above")})");
            }
            
            if (snapshot.SessionHigh > 0 && snapshot.SessionLow > 0)
            {
                sb.AppendLine($"Today HOD:        ${snapshot.SessionHigh:F2}");
                sb.AppendLine($"Today LOD:        ${snapshot.SessionLow:F2}");
            }
            
            sb.AppendLine();
            sb.AppendLine("S/R RULES: Price AT PDH = expect resistance. Price BREAKING above PDH = bullish breakout (buy).");
            sb.AppendLine("Price AT PDL = expect support. Price BREAKING below PDL = bearish breakdown (short).");
            sb.AppendLine("Price above PDC = bullish bias. Price below PDC = bearish bias.");
            sb.AppendLine("Yesterday's range defines today's battlefield. Breakout above/below = directional move.");
            sb.AppendLine();
        }
        
        // ============================================================
        // SECTION 2: MOVING AVERAGES (EMA + SMA)
        // ============================================================
        sb.AppendLine("=== MOVING AVERAGES ===");
        sb.AppendLine($"EMA(9):  ${snapshot.Ema9:F2}  | Price {(snapshot.Price > snapshot.Ema9 ? "ABOVE" : "BELOW")}");
        sb.AppendLine($"EMA(21): ${snapshot.Ema21:F2}  | Price {(snapshot.Price > snapshot.Ema21 ? "ABOVE" : "BELOW")}");
        sb.AppendLine($"EMA(34): ${snapshot.Ema34:F2}  | Price {(snapshot.Price > snapshot.Ema34 ? "ABOVE" : "BELOW")} (KEY DECISION LEVEL)");
        sb.AppendLine($"EMA(50): ${snapshot.Ema50:F2}  | Price {(snapshot.Price > snapshot.Ema50 ? "ABOVE" : "BELOW")}");
        sb.AppendLine($"SMA(20): ${snapshot.Sma20:F2}  | Price {(snapshot.Price > snapshot.Sma20 ? "ABOVE" : "BELOW")}");
        sb.AppendLine($"SMA(50): ${snapshot.Sma50:F2}  | Price {(snapshot.Price > snapshot.Sma50 ? "ABOVE" : "BELOW")}");
        
        // EMA stack alignment
        int bullishEmas = 0;
        if (snapshot.Price > snapshot.Ema9 && snapshot.Ema9 > 0) bullishEmas++;
        if (snapshot.Price > snapshot.Ema21 && snapshot.Ema21 > 0) bullishEmas++;
        if (snapshot.Price > snapshot.Ema34 && snapshot.Ema34 > 0) bullishEmas++;
        if (snapshot.Price > snapshot.Ema50 && snapshot.Ema50 > 0) bullishEmas++;
        sb.AppendLine($"EMA Stack: Price above {bullishEmas}/4 EMAs ({(bullishEmas >= 3 ? "BULLISH" : bullishEmas <= 1 ? "BEARISH" : "MIXED")})");
        
        // SMA crossover
        if (snapshot.Sma20 > 0 && snapshot.Sma50 > 0)
        {
            sb.AppendLine($"SMA Cross: SMA(20) {(snapshot.Sma20 > snapshot.Sma50 ? ">" : "<")} SMA(50) = {(snapshot.Sma20 > snapshot.Sma50 ? "GOLDEN CROSS (bullish)" : "DEATH CROSS (bearish)")}");
        }
        sb.AppendLine();
        
        // ============================================================
        // SECTION 3: MOMENTUM OSCILLATORS
        // ============================================================
        sb.AppendLine("=== MOMENTUM OSCILLATORS ===");
        sb.AppendLine($"RSI(14): {snapshot.Rsi:F1} ({(snapshot.Rsi >= 70 ? "OVERBOUGHT" : snapshot.Rsi <= 30 ? "OVERSOLD" : snapshot.Rsi > 50 ? "Bullish zone" : "Bearish zone")})");
        sb.AppendLine($"MACD Line: {snapshot.Macd:F4}");
        sb.AppendLine($"MACD Signal: {snapshot.MacdSignal:F4}");
        sb.AppendLine($"MACD Histogram: {snapshot.MacdHistogram:F4} ({(snapshot.MacdHistogram > 0 ? "BULLISH" : "BEARISH")}{(Math.Abs(snapshot.MacdHistogram) > Math.Abs(snapshot.MacdSignal * 0.1) ? " STRONG" : "")})");
        sb.AppendLine($"MACD Cross: {(snapshot.Macd > snapshot.MacdSignal ? "MACD > Signal = BULLISH" : "MACD < Signal = BEARISH")}");
        sb.AppendLine($"Stochastic %K: {snapshot.StochasticK:F1} | %D: {snapshot.StochasticD:F1} ({(snapshot.StochasticK > 80 ? "OVERBOUGHT" : snapshot.StochasticK < 20 ? "OVERSOLD" : "Normal")})");
        sb.AppendLine($"Stochastic Cross: %K {(snapshot.StochasticK > snapshot.StochasticD ? ">" : "<")} %D = {(snapshot.StochasticK > snapshot.StochasticD ? "BULLISH momentum" : "BEARISH momentum")}");
        sb.AppendLine($"Williams %R(14): {snapshot.WilliamsR:F1} ({(snapshot.WilliamsR >= -20 ? "OVERBOUGHT" : snapshot.WilliamsR <= -80 ? "OVERSOLD" : "Normal")})");
        sb.AppendLine($"CCI(20): {snapshot.Cci:F1} ({(snapshot.Cci > 100 ? "OVERBOUGHT/strong uptrend" : snapshot.Cci < -100 ? "OVERSOLD/strong downtrend" : "Normal range")})");
        sb.AppendLine($"Momentum(10): {snapshot.Momentum:F2} ({(snapshot.Momentum > 0 ? "POSITIVE (price rising vs 10 bars ago)" : snapshot.Momentum < 0 ? "NEGATIVE (price falling vs 10 bars ago)" : "NEUTRAL (flat)")})");
        sb.AppendLine($"ROC(10): {snapshot.Roc:F2}% ({(snapshot.Roc > 2 ? "STRONG bullish" : snapshot.Roc > 0 ? "Mild bullish" : snapshot.Roc < -2 ? "STRONG bearish" : snapshot.Roc < 0 ? "Mild bearish" : "NEUTRAL")})");
        sb.AppendLine();
        
        // ============================================================
        // SECTION 4: TREND STRENGTH & DIRECTION
        // ============================================================
        sb.AppendLine("=== TREND STRENGTH & DIRECTION ===");
        sb.AppendLine($"ADX(14): {snapshot.Adx:F1} ({(snapshot.Adx >= 40 ? "VERY STRONG trend" : snapshot.Adx >= 25 ? "Strong trend" : snapshot.Adx >= 20 ? "Developing trend" : "WEAK/NO trend (ranging)")})");
        sb.AppendLine($"+DI: {snapshot.PlusDi:F1} | -DI: {snapshot.MinusDi:F1}");
        sb.AppendLine($"DI Direction: {(snapshot.PlusDi > snapshot.MinusDi ? "+DI > -DI = BULLISH directional movement" : "-DI > +DI = BEARISH directional movement")}");
        sb.AppendLine($"DI Spread: {Math.Abs(snapshot.PlusDi - snapshot.MinusDi):F1} ({(Math.Abs(snapshot.PlusDi - snapshot.MinusDi) > 10 ? "Strong conviction" : "Weak/mixed direction")})");
        sb.AppendLine();
        
        // ============================================================
        // SECTION 5: VOLUME ANALYSIS
        // ============================================================
        sb.AppendLine("=== VOLUME ANALYSIS ===");
        sb.AppendLine($"Volume Ratio: {snapshot.VolumeRatio:F2}x average ({(snapshot.VolumeRatio >= 2.0 ? "VERY HIGH - strong confirmation" : snapshot.VolumeRatio >= 1.5 ? "HIGH - good confirmation" : snapshot.VolumeRatio >= 1.0 ? "Normal" : snapshot.VolumeRatio >= 0.7 ? "Below average - weak" : "LOW - no conviction")})");
        sb.AppendLine($"OBV Trend: {(snapshot.ObvSlope > 0 ? "RISING (smart money buying)" : snapshot.ObvSlope < 0 ? "FALLING (smart money selling)" : "FLAT (neutral)")}");
        sb.AppendLine();
        
        // ============================================================
        // SECTION 6: VOLATILITY (Bollinger Bands + ATR)
        // ============================================================
        sb.AppendLine("=== VOLATILITY ===");
        sb.AppendLine($"Bollinger Upper: ${snapshot.BollingerUpper:F2}");
        sb.AppendLine($"Bollinger Middle (SMA20): ${snapshot.BollingerMiddle:F2}");
        sb.AppendLine($"Bollinger Lower: ${snapshot.BollingerLower:F2}");
        if (snapshot.BollingerUpper > 0 && snapshot.BollingerLower > 0)
        {
            double bbPosition = snapshot.BollingerUpper - snapshot.BollingerLower > 0
                ? (snapshot.Price - snapshot.BollingerLower) / (snapshot.BollingerUpper - snapshot.BollingerLower)
                : 0.5;
            sb.AppendLine($"BB Position: {bbPosition:P0} ({(bbPosition > 0.8 ? "Near upper band - OVERBOUGHT (mean reversion risk)" : bbPosition < 0.2 ? "Near lower band - OVERSOLD (bounce potential)" : "Mid-range")})");
            double bandwidth = (snapshot.BollingerUpper - snapshot.BollingerLower) / Math.Max(snapshot.BollingerMiddle, 0.01) * 100;
            sb.AppendLine($"BB Bandwidth: {bandwidth:F2}% ({(bandwidth < 3 ? "SQUEEZE - expect breakout" : bandwidth > 8 ? "WIDE - high volatility" : "Normal")})");
        }
        sb.AppendLine($"ATR(14): ${snapshot.Atr:F2} ({(snapshot.Price > 0 ? $"{(snapshot.Atr / snapshot.Price * 100):F2}% of price" : "N/A")})");
        sb.AppendLine();
        
        // ============================================================
        // SECTION 7: CHOP DETECTION
        // ============================================================
        bool isChop = IsChopDetected(snapshot);
        sb.AppendLine("=== MARKET STATE ===");
        sb.AppendLine($"CHOP DETECTED: {(isChop ? "YES - Market is RANGING/CHOPPY (avoid trades)" : "NO - Market is TRENDING (trades viable)")}");
        if (isChop)
        {
            sb.AppendLine("  Chop signals: RSI neutral (45-55), ADX low (<20), MACD compressed, price near VWAP, DI lines close");
        }
        sb.AppendLine();
        
        // ============================================================
        // SECTION 8: REQUIREMENT SCORECARD
        // ============================================================
        bool priceAboveEma34 = snapshot.Ema34 > 0 && snapshot.Price > snapshot.Ema34;
        bool rsiBullish = snapshot.Rsi > 50;
        bool rsiBearish = snapshot.Rsi < 45;
        bool macdBullish = snapshot.Macd > snapshot.MacdSignal && snapshot.MacdHistogram > 0;
        bool macdBearish = snapshot.Macd < snapshot.MacdSignal && snapshot.MacdHistogram < 0;
        bool priceAboveVwap = snapshot.Vwap > 0 && snapshot.Price > snapshot.Vwap;
        bool adxStrong = snapshot.Adx >= 20;
        bool diPositive = snapshot.PlusDi > snapshot.MinusDi;
        bool volumeConfirms = snapshot.VolumeRatio >= 1.2;
        bool smaAligned = snapshot.Sma20 > snapshot.Sma50 && snapshot.Sma20 > 0 && snapshot.Sma50 > 0;
        bool momentumPositive = snapshot.Momentum > 0;
        bool momentumNegative = snapshot.Momentum < 0;
        
        int longMet = 0, longTotal = 9;
        sb.AppendLine("LONG REQUIREMENTS:");
        sb.AppendLine($"  {(priceAboveEma34 ? "[MET]" : "[---]")} Price > EMA 34");               if (priceAboveEma34) longMet++;
        sb.AppendLine($"  {(priceAboveVwap ? "[MET]" : "[---]")} Price > VWAP");                   if (priceAboveVwap) longMet++;
        sb.AppendLine($"  {(rsiBullish ? "[MET]" : "[---]")} RSI > 50 (RSI={snapshot.Rsi:F1})");   if (rsiBullish) longMet++;
        sb.AppendLine($"  {(macdBullish ? "[MET]" : "[---]")} MACD Bullish");                      if (macdBullish) longMet++;
        sb.AppendLine($"  {(adxStrong ? "[MET]" : "[---]")} ADX >= 20 (ADX={snapshot.Adx:F1})");   if (adxStrong) longMet++;
        sb.AppendLine($"  {(diPositive ? "[MET]" : "[---]")} +DI > -DI");                          if (diPositive) longMet++;
        sb.AppendLine($"  {(volumeConfirms ? "[MET]" : "[---]")} Volume >= 1.2x average");         if (volumeConfirms) longMet++;
        sb.AppendLine($"  {(smaAligned ? "[MET]" : "[---]")} SMA(20) > SMA(50) (Golden Cross)");   if (smaAligned) longMet++;
        sb.AppendLine($"  {(momentumPositive ? "[MET]" : "[---]")} Momentum > 0");                 if (momentumPositive) longMet++;
        sb.AppendLine($"  Score: {longMet}/{longTotal} requirements met");
        
        sb.AppendLine();
        
        int shortMet = 0;
        sb.AppendLine("SHORT REQUIREMENTS:");
        sb.AppendLine($"  {(!priceAboveEma34 ? "[MET]" : "[---]")} Price < EMA 34");                  if (!priceAboveEma34) shortMet++;
        sb.AppendLine($"  {(!priceAboveVwap ? "[MET]" : "[---]")} Price < VWAP");                      if (!priceAboveVwap) shortMet++;
        sb.AppendLine($"  {(rsiBearish ? "[MET]" : "[---]")} RSI < 45 (RSI={snapshot.Rsi:F1})");       if (rsiBearish) shortMet++;
        sb.AppendLine($"  {(macdBearish ? "[MET]" : "[---]")} MACD Bearish");                          if (macdBearish) shortMet++;
        sb.AppendLine($"  {(adxStrong ? "[MET]" : "[---]")} ADX >= 20 (ADX={snapshot.Adx:F1})");       if (adxStrong) shortMet++;
        sb.AppendLine($"  {(!diPositive ? "[MET]" : "[---]")} -DI > +DI");                             if (!diPositive) shortMet++;
        sb.AppendLine($"  {(volumeConfirms ? "[MET]" : "[---]")} Volume >= 1.2x average");             if (volumeConfirms) shortMet++;
        sb.AppendLine($"  {(!smaAligned ? "[MET]" : "[---]")} SMA(20) < SMA(50) (Death Cross)");       if (!smaAligned) shortMet++;
        sb.AppendLine($"  {(!momentumPositive ? "[MET]" : "[---]")} Momentum < 0");                    if (momentumNegative) shortMet++;
        sb.AppendLine($"  Score: {shortMet}/{longTotal} requirements met");
        sb.AppendLine();
        
        // ============================================================
        // SECTION 9: COMPOSITE MARKET SCORE
        // ============================================================
        sb.AppendLine("=== COMPOSITE MARKET SCORE ===");
        sb.AppendLine($"Total Score: {score.TotalScore} / 100 ({(score.TotalScore >= 70 ? "STRONG BULLISH" : score.TotalScore >= 30 ? "Mild bullish" : score.TotalScore <= -70 ? "STRONG BEARISH" : score.TotalScore <= -30 ? "Mild bearish" : "NEUTRAL")})");
        sb.AppendLine($"  VWAP:       {score.VwapScore,4}  |  EMA:        {score.EmaScore,4}");
        sb.AppendLine($"  RSI:        {score.RsiScore,4}  |  MACD:       {score.MacdScore,4}");
        sb.AppendLine($"  ADX:        {score.AdxScore,4}  |  Volume:     {score.VolumeScore,4}");
        sb.AppendLine($"  Bollinger:  {score.BollingerScore,4}  |  Stochastic: {score.StochasticScore,4}");
        sb.AppendLine($"  OBV:        {score.ObvScore,4}  |  CCI:        {score.CciScore,4}");
        sb.AppendLine($"  Williams%R: {score.WilliamsRScore,4}  |  SMA:        {score.SmaScore,4}");
        sb.AppendLine($"  Momentum:   {score.MomentumScore,4}  |  S/R:        {score.SupportResistanceScore,4}");
        
        // Include custom strategy rules from the user's strategy-rules.json
        var customRules = StrategyRulesManager.GetRulesForPrompt(symbol);
        if (!string.IsNullOrWhiteSpace(customRules))
        {
            sb.Append(customRules);
        }
        
        sb.AppendLine();
        sb.AppendLine("=== YOUR TASK ===");
        sb.AppendLine("Analyze ALL indicators above holistically. Give a decisive recommendation.");
        sb.AppendLine("You are being paid to make profitable trades. Be bold when the setup is clear.");
        sb.AppendLine();
        sb.AppendLine("Respond in this EXACT format:");
        sb.AppendLine("ACTION: BUY|SELL|SHORT|COVER|WAIT");
        sb.AppendLine("CONFIDENCE: 0-100");
        sb.AppendLine("REASONING: one concise sentence explaining which indicators drove your decision");
        sb.AppendLine("RULE_STATUS: MET|NOT_MET|NO_RULES");
        sb.AppendLine("RISKS: comma-separated list of key risks");
        sb.AppendLine("TPSL: suggest take-profit and stop-loss levels based on ATR, PDH/PDL, and session high/low");
        
        return sb.ToString();
    }

    private string BuildLearningAnalysisPrompt(
        string symbol,
        IEnumerable<LearningMethodSummary> methods,
        int trainBars,
        int valBars)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine($"LEARNING ANALYSIS: {symbol}");
        sb.AppendLine($"Training Data: {trainBars} bars | Validation: {valBars} bars");
        sb.AppendLine();
        sb.AppendLine("=== METHOD RESULTS ===");
        
        foreach (var m in methods.OrderByDescending(x => x.ValidationFitness))
        {
            var marker = m.IsBest ? " [BEST]" : "";
            sb.AppendLine($"{m.MethodName}: ValFit={m.ValidationFitness:F2}, WinRate={m.ValidationWinRate:F1}%, PnL=${m.ValidationPnL:F2}{marker}");
        }
        
        sb.AppendLine();
        sb.AppendLine("Questions:");
        sb.AppendLine("1. Is the best method truly better or is it overfitting?");
        sb.AppendLine("2. What does the validation vs training gap tell us?");
        sb.AppendLine("3. Any concerns about the learning process?");
        sb.AppendLine();
        sb.AppendLine("Respond in this format:");
        sb.AppendLine("RECOMMENDATION: method name or ENSEMBLE");
        sb.AppendLine("CONFIDENCE: 0-100");
        sb.AppendLine("REASONING: 2-3 sentences");
        sb.AppendLine("CONCERNS: any overfitting or data quality issues");
        
        return sb.ToString();
    }

    private static AIAnalysis ParseAnalysisResponse(string response)
    {
        var analysis = new AIAnalysis { IsUsable = true };
        
        var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            
            if (trimmed.StartsWith("ACTION:", StringComparison.OrdinalIgnoreCase))
            {
                var value = trimmed[7..].Trim().ToUpperInvariant();
                // Normalize action names: LONG, SHORT, COVER, WAIT
                // Accept BUY/SELL as aliases for LONG/SHORT
                if (value is "BUY" or "LONG")
                    analysis.Action = "LONG";
                else if (value is "SELL" or "SHORT")
                    analysis.Action = "SHORT";
                else if (value is "COVER" or "CLOSE" or "EXIT")
                    analysis.Action = "COVER";
                else if (value is "WAIT" or "NONE" or "HOLD" or "SKIP")
                    analysis.Action = "WAIT";
            }
            else if (trimmed.StartsWith("CONFIDENCE:", StringComparison.OrdinalIgnoreCase) ||
                     trimmed.StartsWith("CONFIDENCE SCORE:", StringComparison.OrdinalIgnoreCase))
            {
                // Extract number from patterns like "CONFIDENCE: 72" or "Confidence Score: 72 / 100"
                var colonIdx = trimmed.IndexOf(':');
                if (colonIdx > 0)
                {
                    var valuePart = trimmed[(colonIdx + 1)..].Trim();
                    // Remove "/100" or "%" if present
                    valuePart = valuePart.Replace("/100", "").Replace("/ 100", "").TrimEnd('%').Trim();
                    // Take first number-like part
                    var numPart = new string(valuePart.TakeWhile(c => char.IsDigit(c)).ToArray());
                    if (int.TryParse(numPart, out var conf))
                        analysis.Confidence = Math.Clamp(conf, 0, 100);
                }
            }
            else if (trimmed.StartsWith("REASONING:", StringComparison.OrdinalIgnoreCase))
            {
                analysis.Reasoning = trimmed[10..].Trim();
            }
            else if (trimmed.StartsWith("RISKS:", StringComparison.OrdinalIgnoreCase) ||
                     trimmed.StartsWith("PRIMARY RISKS:", StringComparison.OrdinalIgnoreCase))
            {
                var colonIdx = trimmed.IndexOf(':');
                if (colonIdx > 0)
                {
                    var risks = trimmed[(colonIdx + 1)..].Split(',', StringSplitOptions.RemoveEmptyEntries);
                    analysis.RiskFactors = risks.Select(r => r.Trim()).ToList();
                }
            }
            else if (trimmed.StartsWith("TPSL:", StringComparison.OrdinalIgnoreCase))
            {
                analysis.TpSlAdvice = trimmed[5..].Trim();
            }
            else if (trimmed.StartsWith("RULE_STATUS:", StringComparison.OrdinalIgnoreCase))
            {
                var value = trimmed[12..].Trim().ToUpperInvariant();
                if (value is "MET" or "NOT_MET" or "NO_RULES")
                    analysis.RuleStatus = value;
            }
        }
        
        return analysis;
    }

    private static AIAnalysis ParseLearningResponse(string response)
    {
        var analysis = new AIAnalysis { IsUsable = true };
        
        var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            
            if (trimmed.StartsWith("RECOMMENDATION:", StringComparison.OrdinalIgnoreCase))
            {
                analysis.Action = trimmed[15..].Trim().ToUpperInvariant();
            }
            else if (trimmed.StartsWith("CONFIDENCE:", StringComparison.OrdinalIgnoreCase))
            {
                var value = trimmed[11..].Trim().TrimEnd('%');
                if (int.TryParse(value, out var conf))
                    analysis.Confidence = Math.Clamp(conf, 0, 100);
            }
            else if (trimmed.StartsWith("REASONING:", StringComparison.OrdinalIgnoreCase))
            {
                analysis.Reasoning = trimmed[10..].Trim();
            }
            else if (trimmed.StartsWith("CONCERNS:", StringComparison.OrdinalIgnoreCase))
            {
                var concerns = trimmed[9..].Trim();
                if (!string.IsNullOrWhiteSpace(concerns))
                    analysis.RiskFactors = [concerns];
            }
        }
        
        return analysis;
    }

    // Cached system prompt (loaded from file once)
    private static string? _cachedTradingSystemPrompt;
    private static readonly object _promptCacheLock = new();

    private static string GetTradingSystemPrompt()
    {
        // Return cached prompt if available
        lock (_promptCacheLock)
        {
            if (_cachedTradingSystemPrompt != null)
                return _cachedTradingSystemPrompt;
        }

        // Try to load from file
        try
        {
            var promptPath = Path.Combine(
                Path.GetDirectoryName(typeof(AIAdvisor).Assembly.Location) ?? "",
                "..", "..", "..", "Data", "chatgpt-system-prompt.txt");
            
            // Also check relative to working directory
            if (!File.Exists(promptPath))
            {
                promptPath = Path.Combine("IdiotProof.Core", "Data", "chatgpt-system-prompt.txt");
            }
            
            // Check Settings folder path
            if (!File.Exists(promptPath))
            {
                promptPath = Path.Combine(Settings.SettingsManager.GetDataFolder(), "chatgpt-system-prompt.txt");
            }

            if (File.Exists(promptPath))
            {
                var prompt = File.ReadAllText(promptPath);
                lock (_promptCacheLock)
                {
                    _cachedTradingSystemPrompt = prompt;
                }
                return prompt;
            }
        }
        catch
        {
            // Fall through to default
        }

        // Fallback to hardcoded default
        const string defaultPrompt = """
            You are a professional quantitative trading analyst embedded in an automated stock trading system called IdiotProof.
            Your ONLY job is to analyze real-time indicator data and make PROFITABLE trading decisions.
            You are being paid based on performance. Every dollar counts.

            ═══════════════════════════════════════════════════════════════
            YOUR AVAILABLE INDICATORS (all pre-calculated and provided):
            ═══════════════════════════════════════════════════════════════

            TREND INDICATORS:
            • EMA (9, 21, 34, 50) - Exponential Moving Averages at 4 timeframes
              - EMA 34 is the PRIMARY decision level. Price above = bullish bias, below = bearish.
              - EMA stack alignment (price above all = strong trend)
            • SMA (20, 50) - Simple Moving Averages for crossover signals
              - SMA 20 > SMA 50 = "Golden Cross" (bullish structure)
              - SMA 20 < SMA 50 = "Death Cross" (bearish structure)
            • VWAP - Volume Weighted Average Price (institutional anchor)
              - Price above VWAP = institutions buying, below = selling
            • ADX (14) - Average Directional Index (trend STRENGTH, not direction)
              - ADX < 20 = NO trend (ranging/choppy) → AVOID trading
              - ADX 20-25 = trend developing
              - ADX 25-40 = strong trend → trade WITH the trend
              - ADX > 40 = very strong trend → aggressive entries acceptable
            • +DI / -DI - Directional Indicators (trend DIRECTION)
              - +DI > -DI = bullish directional pressure
              - -DI > +DI = bearish directional pressure

            MOMENTUM OSCILLATORS:
            • RSI (14) - Relative Strength Index
              - RSI > 70 = OVERBOUGHT (potential reversal DOWN, risky for new longs)
              - RSI < 30 = OVERSOLD (potential reversal UP, risky for new shorts)
              - RSI 50-70 = bullish zone, RSI 30-50 = bearish zone
              - RSI divergence from price = powerful reversal signal
            • MACD (12, 26, 9) - Moving Average Convergence Divergence
              - MACD > Signal = bullish momentum
              - MACD < Signal = bearish momentum
              - Histogram rising = momentum increasing, falling = fading
              - Zero-line cross = significant trend change
            • Stochastic (14, 3) - %K and %D oscillator
              - %K > 80 = overbought, %K < 20 = oversold
              - %K crossing above %D = bullish, below = bearish
            • Williams %R (14) - Overbought/oversold momentum
              - Near 0 = overbought, near -100 = oversold
            • CCI (20) - Commodity Channel Index
              - CCI > 100 = overbought/strong uptrend
              - CCI < -100 = oversold/strong downtrend
            • Momentum (10) - Raw price momentum (current price - price 10 bars ago)
              - Positive = price gaining, Negative = price losing
            • ROC (10) - Rate of Change as percentage
              - ROC > 2% = strong bullish momentum
              - ROC < -2% = strong bearish momentum

            VOLUME INDICATORS:
            • Volume Ratio - Current volume vs 20-bar average
              - > 1.5x = strong volume confirmation
              - < 0.7x = low conviction, be skeptical of moves
            • OBV Slope - On-Balance Volume direction
              - Rising = smart money accumulating (bullish)
              - Falling = smart money distributing (bearish)
              - OBV divergence from price = early warning of reversal

            VOLATILITY INDICATORS:
            • Bollinger Bands (20, 2.0) - Volatility envelope
              - Price near upper band = overbought (mean reversion risk)
              - Price near lower band = oversold (bounce potential)
              - Band SQUEEZE (narrow bands) = expect explosive breakout
              - Band EXPANSION = high volatility, trend in progress
            • ATR (14) - Average True Range (volatility measure)
              - Use ATR for TP/SL sizing (TP = 2x ATR, SL = 1.5x ATR typical)
              - High ATR = wider stops needed, Low ATR = tighter stops

            ═══════════════════════════════════════════════════════════════
            DECISION FRAMEWORK:
            ═══════════════════════════════════════════════════════════════

            BUY (enter long) when:
            - Price above EMA 34 AND VWAP
            - MACD bullish (MACD > Signal, histogram positive/rising)
            - RSI in bullish zone (50-65 ideal, avoid >70)
            - ADX >= 20 with +DI > -DI
            - Volume confirming (ratio >= 1.0, OBV rising)
            - SMA 20 > SMA 50 (Golden Cross structure)
            - Momentum positive, ROC positive
            - NOT in Bollinger upper extreme

            SELL (close long) when:
            - RSI overbought (>70) AND momentum fading
            - MACD histogram turning negative
            - Price breaks below EMA 34
            - Volume declining on push higher (exhaustion)
            - Stochastic/Williams %R overbought crossover
            - Take Profit target reached

            SHORT (enter short) when:
            - Price below EMA 34 AND VWAP
            - MACD bearish (MACD < Signal, histogram negative/falling)
            - RSI in bearish zone (35-50 ideal, avoid <30)
            - ADX >= 20 with -DI > +DI
            - Volume confirming (ratio >= 1.0, OBV falling)
            - SMA 20 < SMA 50 (Death Cross structure)
            - Momentum negative, ROC negative

            COVER (close short) when:
            - RSI oversold (<30) AND momentum bouncing
            - MACD histogram turning positive
            - Price breaks above EMA 34
            - Stochastic/Williams %R oversold crossover

            WAIT when:
            - ADX < 20 (no trend - CHOPPY market)
            - RSI in neutral zone (45-55) with compressed MACD
            - Conflicting signals (half bullish, half bearish)
            - Low volume with no clear direction
            - Price stuck between Bollinger bands with no momentum
            - Fewer than 5 out of 9 requirements met for either direction

            ═══════════════════════════════════════════════════════════════
            CONFLUENCE SCORING:
            ═══════════════════════════════════════════════════════════════
            Count how many indicators agree:
            - 7-9 out of 9 requirements → Confidence 80-95 (STRONG entry)
            - 5-6 out of 9 requirements → Confidence 60-79 (acceptable entry)
            - 4 out of 9 requirements   → Confidence 50-59 (marginal, proceed with caution)
            - 3 or fewer requirements   → Confidence < 50 (WAIT, do not enter)

            ═══════════════════════════════════════════════════════════════
            RISK MANAGEMENT RULES:
            ═══════════════════════════════════════════════════════════════
            - NEVER chase extended moves (4+ same-color candles without pullback)
            - Suggest TP/SL levels based on ATR: TP = 2-2.5x ATR, SL = 1-1.5x ATR
            - If volume is below 0.7x average, downgrade confidence by 10-15 points
            - If RSI is overbought for a LONG or oversold for a SHORT, add to risks
            - Bollinger Squeeze = be ready for breakout but wait for direction confirmation
            - Early morning (first 2 minutes of RTH) = add 10 uncertainty points

            ═══════════════════════════════════════════════════════════════
            CUSTOM USER RULES:
            ═══════════════════════════════════════════════════════════════
            When user-defined strategy rules are provided in the analysis:
            - If rules specify "wait for breakout then pullback", only recommend entry AFTER both occur
            - If rules specify support levels, verify price is above those levels
            - Rules are ADDITIONAL filters - they work WITH indicators, not against them
            - A good setup needs BOTH good indicators AND rule compliance
            - If rules say "no chasing" or "pullback only", recommend WAIT if no pullback has occurred
            - In your reasoning, explicitly state whether the custom rules are satisfied
            - Set RULE_STATUS to MET, NOT_MET, or NO_RULES accordingly

            ═══════════════════════════════════════════════════════════════
            RESPONSE FORMAT (strict - parse depends on this):
            ═══════════════════════════════════════════════════════════════
            ACTION: BUY|SELL|SHORT|COVER|WAIT
            CONFIDENCE: 0-100
            REASONING: one concise sentence
            RULE_STATUS: MET|NOT_MET|NO_RULES
            RISKS: comma-separated list
            TPSL: TP=$X.XX SL=$X.XX (based on ATR and levels)
            """;

        lock (_promptCacheLock)
        {
            _cachedTradingSystemPrompt = defaultPrompt;
        }
        return defaultPrompt;
    }

    private static string GetLearningSystemPrompt() => """
        You are a machine learning expert analyzing trading model results.
        
        Your role:
        - Evaluate learning method performance (Genetic, Neural, Gradient, LSH)
        - Identify overfitting vs genuine learning
        - Recommend the best approach or ensemble strategy
        - Flag data quality or process concerns
        
        Key principles:
        - Validation performance > Training performance matters most
        - Large train/val gaps suggest overfitting
        - Consistent performance across methods = reliable data
        - LSH is non-parametric, can't overfit same way as others
        - Low win rate (<50%) = problematic
        - High PnL with low win rate = might rely on outliers
        
        Always respond in the exact format requested.
        """;

    public void Dispose()
    {
        if (!_disposed)
        {
            _openai.Dispose();
            _disposed = true;
        }
    }
}
