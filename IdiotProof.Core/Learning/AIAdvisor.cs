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
        sb.AppendLine($"EMA 9/21/50: ${snapshot.Ema9:F2} / ${snapshot.Ema21:F2} / ${snapshot.Ema50:F2}");
        sb.AppendLine($"RSI(14): {snapshot.Rsi:F1}");
        sb.AppendLine($"MACD: {snapshot.Macd:F4} | Signal: {snapshot.MacdSignal:F4} | Hist: {snapshot.MacdHistogram:F4}");
        sb.AppendLine($"ADX: {snapshot.Adx:F1} | +DI: {snapshot.PlusDi:F1} | -DI: {snapshot.MinusDi:F1}");
        sb.AppendLine($"Volume Ratio: {snapshot.VolumeRatio:F2}x average");
        sb.AppendLine($"ATR: ${snapshot.Atr:F2}");
        
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
        sb.AppendLine("Respond in this EXACT format:");
        sb.AppendLine("ACTION: LONG|SHORT|WAIT");
        sb.AppendLine("CONFIDENCE: 0-100");
        sb.AppendLine("REASONING: one sentence (include whether custom rules are met if applicable)");
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
                if (value is "LONG" or "SHORT" or "WAIT")
                    analysis.Action = value;
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
            else if (trimmed.StartsWith("RISKS:", StringComparison.OrdinalIgnoreCase))
            {
                var risks = trimmed[6..].Split(',', StringSplitOptions.RemoveEmptyEntries);
                analysis.RiskFactors = risks.Select(r => r.Trim()).ToList();
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

    private static string GetTradingSystemPrompt() => """
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
