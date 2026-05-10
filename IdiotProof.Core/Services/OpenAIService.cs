// ============================================================================
// OpenAIService — IdiotProof's OpenAI chat helper
// ============================================================================
//
// PURPOSE:
// Communicates with OpenAI to answer questions, generate mathematical models,
// and provide trading insights based on market data. The wire-level work
// (endpoints, auth headers, payload shape, response parsing, retries with
// exponential backoff, per-provider circuit breaker) is owned by
// MindAttic.Legion's LegionClient — this class is just IdiotProof's
// orchestration layer (system prompt, multi-turn history, helper methods).
//
// USAGE:
//   var openai = new OpenAIService();
//   var reply = await openai.AskAsync("What's the formula for calculating EMA?");
//
// CREDENTIAL RESOLUTION (first hit wins):
//   1. Explicit apiKey passed to the constructor
//   2. %APPDATA%/MindAttic/LLM/providers.json (provider id "openai") via
//      MindAttic.Vault's LlmCredentialStore (drop-in replacement for the
//      legacy Legion store; honours MINDATTIC_LLM_CREDENTIALS for tests)
//   3. OPENAI_IDIOTPROOF_API_KEY environment variable
// ============================================================================

using System.Text;
using System.Text.Json;
using MindAttic.Legion;
using MindAttic.Vault.Credentials;
using MindAttic.Vault.Paths;

namespace IdiotProof.Services;

/// <summary>Reply from a chat call: the response text plus a small diagnostic JSON blob.</summary>
public record ChatReply(string Text, string FullJson);

/// <summary>A single message in the conversation history.</summary>
public record ChatMessage(string Role, string Content);

/// <summary>
/// IdiotProof-flavoured OpenAI client. Routes the wire-level call through
/// MindAttic.Legion; keeps the trading-domain system prompt and the multi-turn
/// conversation history local.
/// </summary>
public sealed class OpenAIService : IDisposable
{
    private readonly LegionClient legion;
    private readonly string apiKey;
    private readonly string model;
    private readonly List<ChatMessage> conversationHistory = [];

    private const string DefaultModel = "gpt-4.1-mini";

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
    /// Creates a new OpenAI service instance backed by MindAttic.Legion.
    /// </summary>
    /// <param name="model">Model to use (default: gpt-4.1-mini)</param>
    /// <param name="apiKey">Optional API key override (defaults to the MindAttic Vault LLM keyring, then env var)</param>
    public OpenAIService(string? model = null, string? apiKey = null)
    {
        this.model = model ?? DefaultModel;

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            // Construct fresh so MINDATTIC_LLM_CREDENTIALS env-var overrides
            // (used by the test suite) are re-evaluated each call instead of
            // captured once via LlmCredentialStore.Default at type-load.
            var store = new LlmCredentialStore(
                Environment.GetEnvironmentVariable(LlmCredentialStore.DirectoryEnvVar)
                ?? VaultPaths.RoamingBucket(LlmCredentialStore.Bucket));
            apiKey = store.GetKey("openai");
        }
        if (string.IsNullOrWhiteSpace(apiKey))
            apiKey = Environment.GetEnvironmentVariable("OPENAI_IDIOTPROOF_API_KEY");

        this.apiKey = apiKey ?? "";

        // Legion owns retries + circuit breaker, so we skip per-app retry loops.
        this.legion = new LegionClient(new HttpClient { Timeout = TimeSpan.FromSeconds(60) });

        conversationHistory.Add(new ChatMessage("system", TradingSystemPrompt));
    }

    /// <summary>Checks if the API key is configured.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(apiKey);

    /// <summary>Ask a question; reply is added to conversation history.</summary>
    public async Task<ChatReply> AskAsync(string question, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(question))
            throw new ArgumentException("Question cannot be empty", nameof(question));

        conversationHistory.Add(new ChatMessage("user", question));
        var reply = await SendAsync(conversationHistory, systemPrompt: null, ct);
        conversationHistory.Add(new ChatMessage("assistant", reply.Text));
        return reply;
    }

    /// <summary>Ask with custom system instructions; does not modify history.</summary>
    public async Task<ChatReply> AskWithInstructionsAsync(
        string question, string systemInstructions, CancellationToken ct = default)
    {
        var transient = new List<ChatMessage>
        {
            new("user", question)
        };
        return await SendAsync(transient, systemInstructions, ct);
    }

    /// <summary>Ask specifically for a mathematical model or formula explanation.</summary>
    public Task<ChatReply> GetMathModelAsync(string topic, CancellationToken ct = default)
    {
        var prompt = $"""
            Provide the mathematical model/formula for: {topic}

            Include:
            1. The formula in mathematical notation
            2. Variable definitions
            3. A practical C# implementation
            4. Usage example with sample values
            """;
        return AskAsync(prompt, ct);
    }

    /// <summary>Analyze a trading strategy and provide insights.</summary>
    public Task<ChatReply> AnalyzeStrategyAsync(
        string strategyDescription,
        Dictionary<string, double>? performanceMetrics = null,
        CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Analyze this trading strategy: {strategyDescription}");
        if (performanceMetrics?.Count > 0)
        {
            sb.AppendLine("\nPerformance metrics:");
            foreach (var (k, v) in performanceMetrics)
                sb.AppendLine($"- {k}: {v:F2}");
        }
        sb.AppendLine("\nProvide:");
        sb.AppendLine("1. Strengths and weaknesses");
        sb.AppendLine("2. Suggested improvements");
        sb.AppendLine("3. Risk considerations");
        return AskAsync(sb.ToString(), ct);
    }

    /// <summary>Clear conversation history (re-seeds with the trading system prompt).</summary>
    public void ClearHistory()
    {
        conversationHistory.Clear();
        conversationHistory.Add(new ChatMessage("system", TradingSystemPrompt));
    }

    public IReadOnlyList<ChatMessage> GetHistory() => conversationHistory.AsReadOnly();

    public void Dispose() { /* Legion owns the HttpClient now; nothing app-side to release. */ }

    private async Task<ChatReply> SendAsync(
        IEnumerable<ChatMessage> messages, string? systemPrompt, CancellationToken ct)
    {
        if (!IsConfigured)
            throw new InvalidOperationException(
                "OpenAI API key not configured. Set it via the shared MindAttic credential store or OPENAI_IDIOTPROOF_API_KEY.");

        // Map IdiotProof's ChatMessage to Legion's ChatTurn. The system entry (if
        // any) is hoisted out and passed as the systemPrompt parameter.
        var systemHoist = systemPrompt;
        var turns = new List<ChatTurn>();
        foreach (var m in messages)
        {
            if (string.Equals(m.Role, "system", StringComparison.OrdinalIgnoreCase))
            {
                systemHoist ??= m.Content;
                continue;
            }
            turns.Add(new ChatTurn(m.Role, m.Content));
        }

        string text;
        try
        {
            text = await legion.CallChatAsync(
                providerId: "openai",
                apiKey: apiKey,
                model: model,
                messages: turns,
                systemPrompt: systemHoist,
                maxTokens: 2000,
                temperature: 0.7,
                ct: ct);
        }
        catch (CircuitBreakerOpenException ex)
        {
            Console.WriteLine($"[IdiotProof] Legion circuit breaker open for openai: {ex.Message}");
            throw;
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"[IdiotProof] Legion call failed for openai (model={model}, status={ex.StatusCode}): {ex.Message}");
            throw;
        }

        var diagnostic = JsonSerializer.Serialize(new
        {
            provider = "openai",
            model,
            text,
        }, new JsonSerializerOptions { WriteIndented = true });

        return new ChatReply(text, diagnostic);
    }
}
