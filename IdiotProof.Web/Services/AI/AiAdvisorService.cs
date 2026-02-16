// ============================================================================
// AI Advisor Service - ChatGPT Integration for Chart Analysis
// ============================================================================
// This service provides AI-powered analysis of trading setups.
// 
// KEY PRINCIPLE: AI is an ADVISOR, not a DECISION MAKER
// - Indicators make the quantitative decisions
// - AI interprets what's being seen
// - AI provides context and historical comparisons
// - AI warns about risks
// - Final decision is always the trader's
// ============================================================================

using System.Text.Json;
using IdiotProof.Shared;

namespace IdiotProof.Web.Services.AI;

/// <summary>
/// Configuration for AI advisor.
/// </summary>
public sealed class AiAdvisorConfig
{
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "gpt-4o";
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    public int MaxTokens { get; set; } = 1000;
    public double Temperature { get; set; } = 0.7;
}

/// <summary>
/// AI-powered trading advisor.
/// </summary>
public sealed class AiAdvisorService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<AiAdvisorService> _logger;
    private readonly AiAdvisorConfig _config;
    
    public AiAdvisorService(HttpClient httpClient, ILogger<AiAdvisorService> logger, AiAdvisorConfig config)
    {
        _httpClient = httpClient;
        _logger = logger;
        _config = config;
        
        if (!string.IsNullOrEmpty(_config.ApiKey))
        {
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_config.ApiKey}");
        }
    }
    
    /// <summary>
    /// Analyzes a chart/setup and returns AI insights.
    /// </summary>
    public async Task<AiAnalysis> AnalyzeSetupAsync(
        string symbol,
        IndicatorSnapshot indicators,
        TradeSetup? proposedSetup = null,
        CancellationToken ct = default)
    {
        var prompt = BuildAnalysisPrompt(symbol, indicators, proposedSetup);
        
        try
        {
            var response = await CallGptAsync(prompt, ct);
            return ParseAnalysisResponse(symbol, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI analysis failed for {Symbol}", symbol);
            
            // Return a basic fallback analysis
            return BuildFallbackAnalysis(symbol, indicators);
        }
    }
    
    /// <summary>
    /// Performs a comprehensive "full screen" analysis.
    /// </summary>
    public async Task<string> FullScreenAnalysisAsync(
        string symbol,
        IndicatorSnapshot indicators,
        List<TradeSetup>? recentTrades = null,
        CancellationToken ct = default)
    {
        var prompt = $@"
You are an expert day trader analyzing {symbol}. Provide a comprehensive analysis.

CURRENT MARKET DATA:
Price: ${indicators.Price:F2}
VWAP: ${indicators.Vwap:F2} ({(indicators.VwapDistance > 0 ? "+" : "")}{indicators.VwapDistance:F2}% from VWAP)

EMAs:
- EMA 9: ${indicators.Ema9:F2} {(indicators.Price > indicators.Ema9 ? "✓ Above" : "✗ Below")}
- EMA 21: ${indicators.Ema21:F2} {(indicators.Price > indicators.Ema21 ? "✓ Above" : "✗ Below")}
- EMA 50: ${indicators.Ema50:F2} {(indicators.Price > indicators.Ema50 ? "✓ Above" : "✗ Below")}

RSI: {indicators.Rsi:F1} {(indicators.Rsi < 30 ? "(OVERSOLD)" : indicators.Rsi > 70 ? "(OVERBOUGHT)" : "")}
{(indicators.HasBullishDivergence == true ? "⚡ BULLISH DIVERGENCE DETECTED" : "")}
{(indicators.HasBearishDivergence == true ? "⚡ BEARISH DIVERGENCE DETECTED" : "")}

MACD: {(indicators.IsMacdBullish ? "BULLISH (MACD > Signal)" : "BEARISH (MACD < Signal)")}
- MACD Line: {indicators.MacdLine:F4}
- Signal Line: {indicators.SignalLine:F4}
- Histogram: {indicators.Histogram:F4}

ADX: {indicators.Adx:F1} {(indicators.IsTrending ? "(TRENDING)" : "(RANGING)")}
- +DI: {indicators.PlusDI:F1}
- -DI: {indicators.MinusDI:F1}
- Direction: {(indicators.IsBullishTrend ? "BULLISH (+DI > -DI)" : "BEARISH (-DI > +DI)")}

Volume Ratio: {indicators.VolumeRatio:F1}x average {(indicators.VolumeRatio > 2 ? "(HIGH VOLUME)" : indicators.VolumeRatio < 0.5 ? "(LOW VOLUME)" : "")}

MARKET SCORE: {indicators.CalculateMarketScore()}/100

Please provide:
1. **SITUATION SUMMARY** (2-3 sentences)
2. **BULLISH FACTORS** (bullet points)
3. **BEARISH FACTORS** (bullet points)  
4. **RISK ASSESSMENT** (Low/Medium/High/Extreme)
5. **HISTORICAL CONTEXT** (Have you seen similar setups? What typically happens?)
6. **RECOMMENDED ACTION** (LONG, SHORT, or WAIT - with entry/stop/target if applicable)
7. **KEY LEVELS TO WATCH**

Be concise and actionable. The trader will make the final decision.
";
        
        return await CallGptAsync(prompt, ct);
    }
    
    /// <summary>
    /// Gets AI interpretation of why indicators are showing what they're showing.
    /// </summary>
    public async Task<string> InterpretIndicatorsAsync(
        IndicatorSnapshot indicators,
        CancellationToken ct = default)
    {
        var marketScore = indicators.CalculateMarketScore();
        
        var prompt = $@"
In 2-3 sentences, explain what the indicators are telling us about {indicators.Symbol}:

- Price ${indicators.Price:F2} is {(indicators.VwapDistance > 0 ? "above" : "below")} VWAP by {Math.Abs(indicators.VwapDistance ?? 0):F1}%
- RSI is {indicators.Rsi:F0} {(indicators.HasBullishDivergence == true ? "with BULLISH DIVERGENCE" : indicators.HasBearishDivergence == true ? "with BEARISH DIVERGENCE" : "")}
- MACD is {(indicators.IsMacdBullish ? "bullish" : "bearish")} with histogram {(indicators.Histogram > 0 ? "positive" : "negative")}
- ADX {indicators.Adx:F0} shows {(indicators.IsTrending ? "trending" : "ranging")} market, direction is {(indicators.IsBullishTrend ? "bullish" : "bearish")}
- Volume is {indicators.VolumeRatio:F1}x average

Overall market score: {marketScore}/100 ({(marketScore >= 50 ? "BULLISH" : "BEARISH")})

Speak directly to the trader. Be concise.
";

        return await CallGptAsync(prompt, ct);
    }

    /// <summary>
    /// Gets general trading advice based on a user question.
    /// Used by the global chatbox.
    /// </summary>
    public async Task<string> GetAdviceAsync(string question, string? symbol = null, CancellationToken ct = default)
    {
        var symbolContext = !string.IsNullOrEmpty(symbol) 
            ? $"The user is currently viewing {symbol}. " 
            : "";

        var prompt = $@"
You are an expert trading assistant for IdiotProof, an automated trading system.
{symbolContext}

USER QUESTION: {question}

Provide a helpful, concise response. Be direct and actionable.
- If they ask about a specific stock, give relevant trading insights
- If they ask about strategies, explain in simple terms
- If they ask about risk, prioritize safety
- Keep responses under 150 words unless more detail is needed

Remember: You are an ADVISOR. The trader makes the final decision.
";

        return await CallGptAsync(prompt, ct);
    }

    private string BuildAnalysisPrompt(string symbol, IndicatorSnapshot indicators, TradeSetup? setup)
    {
        var marketScore = indicators.CalculateMarketScore();
        
        var prompt = $@"
Analyze this trading setup for {symbol}. Be brief and actionable.

INDICATORS:
- Price: ${indicators.Price:F2}
- VWAP Distance: {indicators.VwapDistance:+0.0;-0.0}%
- RSI: {indicators.Rsi:F1}
- MACD: {(indicators.IsMacdBullish ? "Bullish" : "Bearish")}
- ADX: {indicators.Adx:F1} ({(indicators.IsTrending ? "Trending" : "Ranging")})
- Direction: {(indicators.IsBullishTrend ? "Bullish" : "Bearish")}
- Volume: {indicators.VolumeRatio:F1}x average
- Market Score: {marketScore}

{(setup != null ? $@"
PROPOSED TRADE:
- Direction: {setup.Direction}
- Entry: ${setup.EntryPrice:F2}
- Stop Loss: ${setup.StopLoss:F2} ({setup.StopLossPercent:F1}%)
- Take Profit: ${setup.TakeProfit:F2} ({setup.TakeProfitPercent:F1}%)
- Risk: ${setup.RiskDollars:F2}
- R:R: {setup.RiskRewardRatio:F1}
" : "")}

Respond in JSON format:
{{
  ""summary"": ""Brief 1-2 sentence analysis"",
  ""confidence"": 0-100,
  ""direction"": ""LONG"" or ""SHORT"" or ""WAIT"",
  ""bullish"": [""factor1"", ""factor2""],
  ""bearish"": [""factor1"", ""factor2""],
  ""warnings"": [""warning1""],
  ""riskLevel"": ""Low"" or ""Medium"" or ""High"" or ""Extreme"",
  ""stopPercent"": 2.0,
  ""targetPercent"": 5.0
}}
";
        return prompt;
    }
    
    private async Task<string> CallGptAsync(string prompt, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_config.ApiKey))
        {
            // Return mock response for testing
            return MockGptResponse(prompt);
        }
        
        var request = new
        {
            model = _config.Model,
            messages = new[]
            {
                new { role = "system", content = "You are an expert day trader and technical analyst. Provide clear, actionable insights. Always prioritize risk management." },
                new { role = "user", content = prompt }
            },
            max_tokens = _config.MaxTokens,
            temperature = _config.Temperature
        };
        
        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        
        var response = await _httpClient.PostAsync($"{_config.BaseUrl}/chat/completions", content, ct);
        response.EnsureSuccessStatusCode();
        
        var responseJson = await response.Content.ReadAsStringAsync(ct);
        var responseObj = JsonDocument.Parse(responseJson);
        
        return responseObj.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? "";
    }
    
    private string MockGptResponse(string prompt)
    {
        // Extract symbol from prompt
        var symbolMatch = System.Text.RegularExpressions.Regex.Match(prompt, @"for (\w+)");
        var symbol = symbolMatch.Success ? symbolMatch.Groups[1].Value : "STOCK";
        
        if (prompt.Contains("JSON format"))
        {
            return $@"{{
  ""summary"": ""{symbol} showing mixed signals. VWAP and EMA alignment suggest potential continuation, but RSI approaching overbought. Wait for pullback or breakout confirmation."",
  ""confidence"": 65,
  ""direction"": ""WAIT"",
  ""bullish"": [""Above VWAP"", ""EMA 9 > EMA 21"", ""Volume confirming""],
  ""bearish"": [""RSI near overbought"", ""Approaching resistance""],
  ""warnings"": [""Extended move - higher risk of pullback""],
  ""riskLevel"": ""Medium"",
  ""stopPercent"": 2.0,
  ""targetPercent"": 5.0
}}";
        }
        
        return $@"**{symbol} Analysis**

The stock is showing a mixed picture. Price is holding above VWAP which is bullish, and the EMA stack is properly aligned (9 > 21 > 50). However, RSI is approaching overbought territory which suggests we may see a pullback soon.

**Bullish Factors:**
- Price above VWAP
- EMAs properly stacked
- Volume confirming the move

**Bearish Factors:**
- RSI near overbought
- Extended from recent base

**Recommendation:** WAIT for either:
1. A pullback to EMA 21 for a better entry, or
2. A breakout above the current high on volume

Risk Level: **Medium**

If entering now, keep position size small and use a tight stop below VWAP.";
    }
    
    private AiAnalysis ParseAnalysisResponse(string symbol, string response)
    {
        var analysis = new AiAnalysis { Symbol = symbol };
        
        try
        {
            // Try to parse as JSON
            if (response.TrimStart().StartsWith("{"))
            {
                var json = JsonDocument.Parse(response);
                var root = json.RootElement;
                
                analysis.Summary = root.TryGetProperty("summary", out var s) ? s.GetString() ?? "" : "";
                analysis.ConfidenceScore = root.TryGetProperty("confidence", out var c) ? c.GetInt32() : 50;
                
                var direction = root.TryGetProperty("direction", out var d) ? d.GetString() : "WAIT";
                analysis.RecommendedDirection = direction?.ToUpperInvariant() switch
                {
                    "LONG" => TradeDirection.Long,
                    "SHORT" => TradeDirection.Short,
                    _ => null
                };
                
                if (root.TryGetProperty("bullish", out var bullish))
                {
                    foreach (var item in bullish.EnumerateArray())
                        analysis.BullishSignals.Add(item.GetString() ?? "");
                }
                
                if (root.TryGetProperty("bearish", out var bearish))
                {
                    foreach (var item in bearish.EnumerateArray())
                        analysis.BearishSignals.Add(item.GetString() ?? "");
                }
                
                if (root.TryGetProperty("warnings", out var warnings))
                {
                    foreach (var item in warnings.EnumerateArray())
                        analysis.Warnings.Add(item.GetString() ?? "");
                }
                
                analysis.RiskLevel = root.TryGetProperty("riskLevel", out var r) ? r.GetString() ?? "Medium" : "Medium";
                analysis.SuggestedStopPercent = root.TryGetProperty("stopPercent", out var sp) ? sp.GetDouble() : 2.0;
                analysis.SuggestedTargetPercent = root.TryGetProperty("targetPercent", out var tp) ? tp.GetDouble() : 5.0;
            }
            else
            {
                // Plain text response
                analysis.Summary = response.Length > 200 ? response[..200] + "..." : response;
                analysis.ConfidenceScore = 50;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse AI response as JSON");
            analysis.Summary = response;
            analysis.ConfidenceScore = 50;
        }
        
        return analysis;
    }
    
    private AiAnalysis BuildFallbackAnalysis(string symbol, IndicatorSnapshot indicators)
    {
        var score = indicators.CalculateMarketScore();
        
        return new AiAnalysis
        {
            Symbol = symbol,
            Summary = $"Market score {score}. {(score >= 50 ? "Bullish" : "Bearish")} bias based on indicators.",
            ConfidenceScore = Math.Abs(score),
            RecommendedDirection = score >= 50 ? TradeDirection.Long : score <= -50 ? TradeDirection.Short : null,
            BullishSignals = indicators.VwapDistance > 0 ? ["Above VWAP"] : [],
            BearishSignals = indicators.VwapDistance < 0 ? ["Below VWAP"] : [],
            RiskLevel = Math.Abs(score) < 30 ? "High" : Math.Abs(score) < 50 ? "Medium" : "Low"
        };
    }
}
