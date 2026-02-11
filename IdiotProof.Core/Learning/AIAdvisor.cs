// ============================================================================
// AIAdvisor - ChatGPT-Powered Trading Decision Support
// ============================================================================
//
// PURPOSE:
// Integrates OpenAI with the learning system (LSH, Genetic, Neural, Gradient)
// to provide intelligent trading decision support during:
// - Learning phase: Analyze weight optimization results
// - Backtesting: Validate strategy decisions
// - Live trading: Provide "third opinion" alongside indicators and LSH
//
// USAGE:
// var advisor = new AIAdvisor();
// var analysis = await advisor.AnalyzeEntryAsync(snapshot, lshForecast, score);
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
/// Uses ChatGPT to provide decision support alongside indicators and LSH.
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
        PatternForecast? lshForecast,
        MarketScoreResult score,
        LearnedWeights? learnedWeights = null,
        int timeoutMs = 10000)
    {
        try
        {
            var task = AnalyzeEntryAsync(symbol, snapshot, lshForecast, score, learnedWeights, CancellationToken.None);
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
        var analysis = AnalyzeEntrySync(symbol, snapshot, null, score, null, 10000);
        
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

        return (true, analysis.Confidence, $"AI approved {direction} with {analysis.Confidence}% confidence");
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
    /// Combines indicator data, LSH forecast, and learning results.
    /// </summary>
    public async Task<AIAnalysis> AnalyzeEntryAsync(
        string symbol,
        IndicatorSnapshot snapshot,
        PatternForecast? lshForecast,
        MarketScoreResult score,
        LearnedWeights? learnedWeights = null,
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

        var prompt = BuildEntryAnalysisPrompt(symbol, snapshot, lshForecast, score, learnedWeights);
        
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
        PatternForecast? lshForecast,
        MarketScoreResult score,
        LearnedWeights? weights)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine($"TRADE ANALYSIS REQUEST: {symbol}");
        sb.AppendLine();
        sb.AppendLine("=== CURRENT INDICATORS ===");
        sb.AppendLine($"Price: ${snapshot.Price:F2}");
        sb.AppendLine($"VWAP: ${snapshot.Vwap:F2} ({((snapshot.Price - snapshot.Vwap) / snapshot.Vwap * 100):+0.00;-0.00}%)");
        sb.AppendLine($"EMA 9/21/34/50: ${snapshot.Ema9:F2} / ${snapshot.Ema21:F2} / ${snapshot.Ema34:F2} / ${snapshot.Ema50:F2}");
        sb.AppendLine($"Price vs EMA 34: {(snapshot.Price > snapshot.Ema34 ? "ABOVE" : "BELOW")} ({((snapshot.Price - snapshot.Ema34) / snapshot.Ema34 * 100):+0.00;-0.00}%)");
        sb.AppendLine($"RSI(14): {snapshot.Rsi:F1}");
        sb.AppendLine($"MACD: {snapshot.Macd:F4} | Signal: {snapshot.MacdSignal:F4} | Hist: {snapshot.MacdHistogram:F4}");
        sb.AppendLine($"ADX: {snapshot.Adx:F1} | +DI: {snapshot.PlusDi:F1} | -DI: {snapshot.MinusDi:F1}");
        sb.AppendLine($"Volume Ratio: {snapshot.VolumeRatio:F2}x average");
        sb.AppendLine($"ATR: ${snapshot.Atr:F2}");
        
        // CHOP DETECTION
        bool isChop = IsChopDetected(snapshot);
        sb.AppendLine();
        sb.AppendLine("=== SETUP ANALYSIS ===");
        sb.AppendLine($"CHOP DETECTED: {(isChop ? "YES - NO TRADING (confidence capped at 49)" : "NO - Market trending")}");
        
        // LONG/SHORT requirements check
        bool priceAboveEma34 = snapshot.Ema34 > 0 && snapshot.Price > snapshot.Ema34;
        bool rsiBullish = snapshot.Rsi > 50;
        bool rsiBearish = snapshot.Rsi < 45;
        bool macdBullish = snapshot.Macd > snapshot.MacdSignal && snapshot.MacdHistogram > 0;
        bool macdBearish = snapshot.Macd < snapshot.MacdSignal && snapshot.MacdHistogram < 0;
        bool priceAboveVwap = snapshot.Vwap > 0 && snapshot.Price > snapshot.Vwap;
        bool adxStrong = snapshot.Adx >= 20;
        bool diPositive = snapshot.PlusDi > snapshot.MinusDi;
        
        sb.AppendLine();
        sb.AppendLine("LONG REQUIREMENTS:");
        sb.AppendLine($"  - Price > EMA 34: {(priceAboveEma34 ? "[MET]" : "[FAILED]")}");
        sb.AppendLine($"  - Price > VWAP: {(priceAboveVwap ? "[MET]" : "[FAILED]")}");
        sb.AppendLine($"  - RSI > 50: {(rsiBullish ? "[MET]" : "[FAILED]")} (RSI={snapshot.Rsi:F1})");
        sb.AppendLine($"  - MACD Bullish: {(macdBullish ? "[MET]" : "[FAILED]")}");
        sb.AppendLine($"  - ADX >= 20: {(adxStrong ? "[MET]" : "[FAILED]")} (ADX={snapshot.Adx:F1})");
        sb.AppendLine($"  - +DI > -DI: {(diPositive ? "[MET]" : "[FAILED]")}");
        
        sb.AppendLine();
        sb.AppendLine("SHORT REQUIREMENTS:");
        sb.AppendLine($"  - Price < EMA 34: {(!priceAboveEma34 ? "[MET]" : "[FAILED]")}");
        sb.AppendLine($"  - Price < VWAP: {(!priceAboveVwap ? "[MET]" : "[FAILED]")}");
        sb.AppendLine($"  - RSI < 45: {(rsiBearish ? "[MET]" : "[FAILED]")} (RSI={snapshot.Rsi:F1})");
        sb.AppendLine($"  - MACD Bearish: {(macdBearish ? "[MET]" : "[FAILED]")}");
        sb.AppendLine($"  - ADX >= 20: {(adxStrong ? "[MET]" : "[FAILED]")} (ADX={snapshot.Adx:F1})");
        sb.AppendLine($"  - -DI > +DI: {(!diPositive ? "[MET]" : "[FAILED]")}");
        
        sb.AppendLine();
        sb.AppendLine("=== MARKET SCORE ===");
        sb.AppendLine($"Total: {score.TotalScore} (VWAP={score.VwapScore}, EMA={score.EmaScore}, RSI={score.RsiScore}, MACD={score.MacdScore}, ADX={score.AdxScore}, Vol={score.VolumeScore})");
        sb.AppendLine($"DI Positive: {score.IsDiPositive} | MACD Bullish: {score.IsMacdBullish}");
        
        if (lshForecast != null && lshForecast.IsUsable)
        {
            sb.AppendLine();
            sb.AppendLine("=== LSH PATTERN MATCHING ===");
            sb.AppendLine($"Analogs Found: {lshForecast.AnalogCount}");
            sb.AppendLine($"P(Higher): {lshForecast.ProbabilityHigher:P0}");
            sb.AppendLine($"Avg Return: {lshForecast.AverageReturn:+0.00;-0.00}%");
            sb.AppendLine($"Suggested: {(lshForecast.SuggestedDirection == 1 ? "LONG" : lshForecast.SuggestedDirection == -1 ? "SHORT" : "NEUTRAL")}");
            sb.AppendLine($"Confidence: {lshForecast.Confidence:P0}");
        }
        
        if (weights != null)
        {
            sb.AppendLine();
            sb.AppendLine("=== LEARNED WEIGHTS ===");
            sb.AppendLine($"Method: {weights.LearningMethod}");
            sb.AppendLine($"Training Fitness: {weights.TrainingFitness:F2}");
            sb.AppendLine($"Validation Fitness: {weights.ValidationFitness:F2}");
            var longBias = weights.EntryBiases[0];
            var shortBias = weights.EntryBiases[1];
            sb.AppendLine($"Long Entry Bias: {longBias:F3}");
            sb.AppendLine($"Short Entry Bias: {shortBias:F3}");
        }
        
        // Include custom strategy rules from the user's strategy-rules.json
        var customRules = StrategyRulesManager.GetRulesForPrompt(symbol);
        if (!string.IsNullOrWhiteSpace(customRules))
        {
            sb.Append(customRules);
        }
        
        sb.AppendLine();
        sb.AppendLine("=== RESPONSE RULES ===");
        sb.AppendLine("- LONG or SHORT requires confidence >= 55 (RELAXED from 85 - too strict)");
        sb.AppendLine("- If CHOP detected (RSI 45-55, ADX<20, tight MACD), output WAIT with confidence <= 49");
        sb.AppendLine("- DO NOT require ALL conditions - 4 out of 6 met is acceptable for a trade");
        sb.AppendLine("- COVER = exit existing position (when exit conditions are met)");
        sb.AppendLine();
        sb.AppendLine("Respond in this EXACT format:");
        sb.AppendLine("ACTION: LONG|SHORT|COVER|WAIT");
        sb.AppendLine("CONFIDENCE: 0-100 (55+ for LONG/SHORT - 4/6 requirements is acceptable)");
        sb.AppendLine("REASONING: one sentence (include which requirements passed/failed)");
        sb.AppendLine("RULE_STATUS: MET|NOT_MET|NO_RULES (whether user-defined rules are satisfied)");
        sb.AppendLine("RISKS: comma-separated list");
        sb.AppendLine("TPSL: optional advice");
        
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
            You are a quantitative trading analyst providing real-time trade analysis.
            
            Your role:
            - Analyze indicator data and provide clear BUY/SELL/WAIT recommendations
            - Consider risk/reward and market conditions
            - Be concise and actionable
            - Never hallucinate or make up data
            - If uncertain, recommend WAIT
            
            Key trading principles:
            - Score > 70 with confirming LSH = Strong LONG signal
            - Score < -70 with confirming LSH = Strong SHORT signal
            - Conflicting signals = WAIT for clarity
            - RSI extremes (>80 or <20) = Caution for reversals
            - Low ADX (<20) = Weak trend, avoid trend trades
            - High volume confirms moves, low volume = skepticism
            
            CUSTOM USER RULES:
            When user-defined strategy rules are provided, incorporate them into your analysis:
            - If rules specify "wait for breakout then pullback", only recommend entry AFTER both occur
            - If rules specify support levels, verify price is above those levels
            - Rules are ADDITIONAL filters - they work WITH indicators, not against them
            - A good setup needs BOTH good indicators AND rule compliance
            - If rules say "no chasing" or "pullback only", recommend WAIT if no pullback has occurred
            
            In your reasoning, explicitly state whether the custom rules are satisfied.
            
            Always respond in the exact format requested.
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
