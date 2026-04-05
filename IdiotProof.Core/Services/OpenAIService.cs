// ============================================================================
// OpenAIService - AI-Powered Chat and Mathematical Model Generation
// ============================================================================
//
// PURPOSE:
// Communicates with OpenAI's API to answer questions, generate mathematical
// models, and provide trading insights based on market data.
//
// USAGE:
// var openai = new OpenAIService();
// var reply = await openai.AskAsync("What's the formula for calculating EMA?");
// Console.WriteLine(reply.Text);
//
// ENVIRONMENT:
// Set OPENAI_IDIOTPROOF_API_KEY environment variable with your API key.
// Run: setx OPENAI_IDIOTPROOF_API_KEY "sk-..."
//
// ============================================================================

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace IdiotProof.Services;

/// <summary>
/// Response from OpenAI containing the text reply and raw JSON.
/// </summary>
public record ChatReply(string Text, string FullJson);

/// <summary>
/// A simple chat message with role (system/user/assistant) and content.
/// </summary>
public record ChatMessage(string Role, string Content);

/// <summary>
/// Service for interacting with OpenAI's Chat Completions API.
/// Supports general questions and mathematical model generation for trading.
/// </summary>
public sealed class OpenAIService : IDisposable
{
    private readonly HttpClient httpClient;
    private readonly string apiKey;
    private readonly string model;
    private readonly List<ChatMessage> conversationHistory = [];
    private bool disposed;

    // Valid OpenAI model options (as of Feb 2026):
    // ── GPT-5 Series ──
    //   "gpt-5.2"             - Latest flagship, best quality + speed
    //   "gpt-5.1"             - Previous flagship
    //   "gpt-5"               - Original GPT-5
    //   "gpt-5-mini"          - Smaller GPT-5, cheaper, fast
    // ── GPT-4 Series ──
    //   "gpt-4.1"             - Latest GPT-4 generation
    //   "gpt-4.1-mini"        - Smaller GPT-4.1, good balance
    //   "gpt-4.1-nano"        - Smallest GPT-4.1, fastest/cheapest
    //   "gpt-4o"              - GPT-4 Omni, multimodal
    //   "gpt-4o-mini"         - Smaller GPT-4o, cheap and fast
    //   "gpt-4-turbo"         - GPT-4 Turbo (legacy)
    // ── Reasoning Models ──
    //   "o3"                  - Advanced reasoning, slow, expensive
    //   "o3-mini"             - Smaller reasoning model
    //   "o4-mini"             - Latest small reasoning model
    //   "o1"                  - Original reasoning model
    //   "o1-mini"             - Smaller original reasoning
    private const string DefaultModel = "gpt-5.2";
    private const string ApiEndpoint = "https://api.openai.com/v1/chat/completions";
    private const int MaxRetries = 5;
    private const int BaseDelayMs = 2000;

    // System prompt for trading-focused mathematical assistance
    private const string TradingSystemPrompt = """
        You are an expert quantitative analyst and trading systems developer.
        You specialize in:
        - Technical indicator calculations (EMA, RSI, MACD, ADX, etc.)
        - Mathematical models for trading strategies
        - Statistical analysis of market data
        - Risk management formulas (position sizing, Kelly criterion, etc.)
        - Backtesting methodology and performance metrics
        
        When asked about formulas or calculations:
        - Provide the mathematical formula first
        - Then explain each variable
        - Give a practical C# code example when relevant
        - Use LaTeX notation for complex equations when helpful
        
        Be concise and precise. Focus on actionable information.
        """;

    /// <summary>
    /// Creates a new OpenAI service instance.
    /// Reads API key from OPENAI_API_KEY environment variable.
    /// </summary>
    /// <param name="model">Model to use (default: gpt-4o-mini)</param>
    /// <param name="apiKey">Optional API key override (defaults to env var)</param>
    public OpenAIService(string? model = null, string? apiKey = null)
    {
        this.model = model ?? DefaultModel;
        this.apiKey = apiKey 
            ?? Environment.GetEnvironmentVariable("OPENAI_IDIOTPROOF_API_KEY")
            ?? "";
        
        httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromSeconds(60);
        
        // Add trading system prompt to conversation
        conversationHistory.Add(new ChatMessage("system", TradingSystemPrompt));
    }

    /// <summary>
    /// Checks if the API key is configured.
    /// </summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(apiKey);

    /// <summary>
    /// Ask a simple question and get a reply.
    /// Maintains conversation history for context.
    /// </summary>
    public async Task<ChatReply> AskAsync(string question, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(question))
            throw new ArgumentException("Question cannot be empty", nameof(question));

        conversationHistory.Add(new ChatMessage("user", question));
        
        var reply = await GetReplyWithRetryAsync(ct);
        
        conversationHistory.Add(new ChatMessage("assistant", reply.Text));
        
        return reply;
    }

    /// <summary>
    /// Ask a question with custom system instructions (does not use trading prompt).
    /// </summary>
    public async Task<ChatReply> AskWithInstructionsAsync(
        string question, 
        string systemInstructions, 
        CancellationToken ct = default)
    {
        var messages = new List<ChatMessage>
        {
            new("system", systemInstructions),
            new("user", question)
        };
        
        return await SendMessagesAsync(messages, ct);
    }

    /// <summary>
    /// Ask specifically for a mathematical model or formula explanation.
    /// </summary>
    public async Task<ChatReply> GetMathModelAsync(string topic, CancellationToken ct = default)
    {
        var prompt = $"""
            Provide the mathematical model/formula for: {topic}
            
            Include:
            1. The formula in mathematical notation
            2. Variable definitions
            3. A practical C# implementation
            4. Usage example with sample values
            """;
        
        return await AskAsync(prompt, ct);
    }

    /// <summary>
    /// Analyze a trading strategy and provide insights.
    /// </summary>
    public async Task<ChatReply> AnalyzeStrategyAsync(
        string strategyDescription,
        Dictionary<string, double>? performanceMetrics = null,
        CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Analyze this trading strategy: {strategyDescription}");
        
        if (performanceMetrics?.Count > 0)
        {
            sb.AppendLine("\nPerformance metrics:");
            foreach (var (key, value) in performanceMetrics)
            {
                sb.AppendLine($"- {key}: {value:F2}");
            }
        }
        
        sb.AppendLine("\nProvide:");
        sb.AppendLine("1. Strengths and weaknesses");
        sb.AppendLine("2. Suggested improvements");
        sb.AppendLine("3. Risk considerations");
        
        return await AskAsync(sb.ToString(), ct);
    }

    /// <summary>
    /// Clear conversation history (keeps system prompt).
    /// </summary>
    public void ClearHistory()
    {
        conversationHistory.Clear();
        conversationHistory.Add(new ChatMessage("system", TradingSystemPrompt));
    }

    /// <summary>
    /// Get the current conversation history.
    /// </summary>
    public IReadOnlyList<ChatMessage> GetHistory() => conversationHistory.AsReadOnly();

    private async Task<ChatReply> GetReplyWithRetryAsync(CancellationToken ct)
    {
        return await SendMessagesAsync(conversationHistory, ct);
    }

    private async Task<ChatReply> SendMessagesAsync(List<ChatMessage> messages, CancellationToken ct)
    {
        Exception? lastException = null;

        for (int attempt = 0; attempt <= MaxRetries; attempt++)
        {
            try
            {
                return await SendMessagesCoreAsync(messages, ct);
            }
            catch (InvalidOperationException ex) when (IsRetryableError(ex.Message))
            {
                lastException = ex;

                if (attempt == MaxRetries)
                {
                    Console.WriteLine($"[OpenAI] API call failed after {MaxRetries + 1} attempts");
                    throw;
                }

                var delayMs = CalculateDelay(ex.Message, attempt);
                Console.WriteLine($"[OpenAI] Rate limited, waiting {delayMs}ms (attempt {attempt + 1}/{MaxRetries})");
                await Task.Delay(delayMs, ct);
            }
        }

        throw lastException ?? new InvalidOperationException("Unexpected retry loop exit");
    }

    private async Task<ChatReply> SendMessagesCoreAsync(List<ChatMessage> messages, CancellationToken ct)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException(
                "OpenAI API key not configured. Set OPENAI_IDIOTPROOF_API_KEY environment variable.");
        }

        var payload = new
        {
            model = this.model,
            messages = messages.Select(m => new { role = m.Role, content = m.Content }).ToArray(),
            temperature = 0.7,
            max_completion_tokens = 2000
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, ApiEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        try
        {
            using var response = await httpClient.SendAsync(request, ct);
            var json = await response.Content.ReadAsStringAsync(ct);
            var statusCode = (int)response.StatusCode;

            if (statusCode != 200)
            {
                var errorMessage = ExtractErrorMessage(json, statusCode);
                throw new InvalidOperationException($"OpenAI HTTP {statusCode}: {errorMessage}");
            }

            var text = ExtractResponseText(json);
            var formattedJson = FormatJson(json);

            return new ChatReply(text, formattedJson);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"Network error: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new InvalidOperationException("OpenAI API request timed out", ex);
        }
    }

    private static bool IsRetryableError(string message)
    {
        return message.Contains("429") ||
               message.Contains("Rate") ||
               message.Contains("overloaded") ||
               message.Contains("502") ||
               message.Contains("503");
    }

    private static int CalculateDelay(string errorMessage, int attempt)
    {
        // Exponential backoff: 2s, 4s, 8s, 16s, 32s
        var delayMs = BaseDelayMs * (int)Math.Pow(2, attempt);

        // Try to extract wait time from error message
        var waitMatch = Regex.Match(errorMessage, @"try again in (\d+\.?\d*)s");
        if (waitMatch.Success && double.TryParse(
            waitMatch.Groups[1].Value,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out var waitSeconds))
        {
            delayMs = (int)(waitSeconds * 1000) + 500; // Add 500ms buffer
        }

        return delayMs;
    }

    private static string ExtractErrorMessage(string json, int statusCode)
    {
        var apiError = ExtractOpenAIError(json);
        
        return statusCode switch
        {
            400 => $"Bad Request - {apiError ?? "Check your message format"}",
            401 => "Unauthorized - Invalid or expired API key",
            403 => "Forbidden - API key lacks permissions",
            404 => "Not Found - Invalid endpoint or model name",
            429 => $"Rate Limited - {apiError ?? "Too many requests"}",
            500 => "OpenAI Server Error - Try again later",
            502 => "Bad Gateway - OpenAI temporarily unavailable",
            503 => "Service Unavailable - OpenAI overloaded",
            _ => apiError ?? json
        };
    }

    private static string? ExtractOpenAIError(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out var error) &&
                error.TryGetProperty("message", out var message))
            {
                return message.GetString();
            }
        }
        catch { }
        return null;
    }

    private static string ExtractResponseText(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);

            // Standard Chat Completions format: choices[0].message.content
            if (doc.RootElement.TryGetProperty("choices", out var choices) &&
                choices.ValueKind == JsonValueKind.Array &&
                choices.GetArrayLength() > 0)
            {
                var firstChoice = choices[0];
                if (firstChoice.TryGetProperty("message", out var message) &&
                    message.TryGetProperty("content", out var content))
                {
                    return content.GetString() ?? "";
                }
            }
        }
        catch { }

        return json;
    }

    private static string FormatJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions 
            { 
                WriteIndented = true 
            });
        }
        catch
        {
            return json;
        }
    }

    public void Dispose()
    {
        if (!disposed)
        {
            httpClient.Dispose();
            disposed = true;
        }
    }
}
