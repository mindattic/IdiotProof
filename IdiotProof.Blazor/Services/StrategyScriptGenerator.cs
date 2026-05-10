using System.Reflection;
using System.Text;
using IdiotProof.Engine.Settings;
using IdiotProof.Scripting;
using MindAttic.Legion;

namespace IdiotProof.Blazor.Services;

/// <summary>
/// Translates trader prose into IdiotScript fluent text via the MindAttic.Legion
/// gateway. The system prompt is built from reflection on <see cref="StrategyBuilder"/>
/// + <see cref="Conditions"/> so the verb catalog never drifts from the codebase
/// — adding a new builder method automatically teaches Claude about it.
///
/// Tier policy comes from <c>legion.json</c> at the project root. IdiotProof ships
/// with the "high" tier: 4 voters (claude/openai/gemini/deepseek) with claude as
/// judge — see CLAUDE.md and the legion.json comment for rationale.
/// </summary>
public sealed class StrategyScriptGenerator(LegionClient legion, AppSettings appSettings, ILogger<StrategyScriptGenerator> logger)
{
    /// <summary>
    /// Single-shot generation. Returns the raw IdiotScript text Claude produced,
    /// stripped of any explanatory prose / fence markers. Caller is responsible
    /// for parse-time validation; Claude is told to emit ONLY a fluent chain.
    /// </summary>
    public async Task<GenerationResult> GenerateAsync(string prose, string ticker, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(prose))
            return new GenerationResult(false, "", "Provide a description of the strategy you want.");

        if (string.IsNullOrWhiteSpace(appSettings.ClaudeApiKey))
            return new GenerationResult(false, "", "No Claude API key configured. Set it in API Keys or in MindAttic\\LLM\\providers.json.");

        var systemPrompt = BuildSystemPrompt();
        var userMessage  = BuildUserMessage(prose, ticker);

        try
        {
            var content = await legion.CallAsync(
                providerId: "claude",
                apiKey: appSettings.ClaudeApiKey,
                model: appSettings.LlmVoterModel ?? "claude-sonnet-4-6",
                systemPrompt: systemPrompt,
                userMessage: userMessage,
                maxTokens: 1024,
                ct: ct).ConfigureAwait(false);

            var script = StripCodeFence(content).Trim();
            if (string.IsNullOrWhiteSpace(script))
                return new GenerationResult(false, "", "LLM returned an empty response.");

            return new GenerationResult(true, script, null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "StrategyScriptGenerator failed for ticker {Ticker}", ticker);
            return new GenerationResult(false, "", $"Generation error: {ex.Message}");
        }
    }

    /// <summary>
    /// Reflection-built verb catalog. Walks every public instance method on
    /// <see cref="StrategyBuilder"/> + every public static member on
    /// <see cref="Conditions"/> to keep the catalog in sync with the codebase.
    /// </summary>
    private static string BuildSystemPrompt()
    {
        var sb = new StringBuilder();
        sb.AppendLine("You translate plain-English trading strategy descriptions into IdiotScript.");
        sb.AppendLine();
        sb.AppendLine("IdiotScript is a fluent C# DSL. The strategy walks through six fixed phases in order:");
        sb.AppendLine("  Setup (Ticker/Session/Quantity) → Filters (regime gates) → Entry (triggers) → Order (Long/Short) → Risk (stops) → Exit (targets).");
        sb.AppendLine();
        sb.AppendLine("Branching uses expression syntax with the static `Conditions` catalog and `.And()/.Or()/.Not()` operators:");
        sb.AppendLine("  using static IdiotProof.Scripting.Conditions;");
        sb.AppendLine("  Stock.Ticker(\"TSLA\")");
        sb.AppendLine("      .RequireAdxAbove(20)");
        sb.AppendLine("      .RequireEmaStack(9, 31)");
        sb.AppendLine("      .IsAboveVwap()");
        sb.AppendLine("      .IsBetweenEma(9, 31)");
        sb.AppendLine("      .OnReclaim(9)");
        sb.AppendLine("      .WithVolumeConfirm(1.2)");
        sb.AppendLine("      .Long()");
        sb.AppendLine("      .StopLoss(9.50)");
        sb.AppendLine("      .TakeProfit(12.00)");
        sb.AppendLine("      .Build();");
        sb.AppendLine();
        sb.AppendLine("Output format requirements (STRICT):");
        sb.AppendLine("  • Emit ONLY the fluent chain. No prose, no markdown, no code fences, no comments.");
        sb.AppendLine("  • Start with `Stock.Ticker(\"<SYMBOL>\")`.");
        sb.AppendLine("  • Always end with `.Long()` or `.Short()` followed by the relevant Risk/Exit verbs and `.Build()`.");
        sb.AppendLine("  • Use ONLY the verbs in the catalog below. Inventing verbs will fail to parse.");
        sb.AppendLine("  • Multi-line is fine; indent continuation lines with four spaces and a leading dot.");
        sb.AppendLine();
        sb.AppendLine("StrategyBuilder verbs (instance methods, callable in chain order):");
        AppendVerbCatalog(sb, typeof(StrategyBuilder), staticOnly: false);
        sb.AppendLine();
        sb.AppendLine("Static `Conditions` catalog (for use inside .If / .ElseIf branching expressions):");
        AppendVerbCatalog(sb, typeof(Conditions), staticOnly: true);
        sb.AppendLine();
        sb.AppendLine("If the user's description is ambiguous, prefer conservative defaults: paper-mode-friendly, RTH session, ADX>20 regime filter, simple Long/Short, fixed-price stop and target. If a verb the user implies isn't in the catalog, pick the closest existing verb.");
        return sb.ToString();
    }

    private static void AppendVerbCatalog(StringBuilder sb, Type type, bool staticOnly)
    {
        var bindings = staticOnly
            ? BindingFlags.Public | BindingFlags.Static
            : BindingFlags.Public | BindingFlags.Instance;

        var members = new HashSet<string>(StringComparer.Ordinal);

        foreach (var m in type.GetMethods(bindings)
            .Where(m => !m.IsSpecialName && m.DeclaringType == type))
        {
            var paramList = string.Join(", ", m.GetParameters().Select(p =>
                $"{FriendlyType(p.ParameterType)} {p.Name}{(p.HasDefaultValue ? $"={FormatDefault(p.DefaultValue)}" : "")}"));
            members.Add($"{m.Name}({paramList})");
        }

        if (staticOnly)
        {
            foreach (var p in type.GetProperties(bindings).Where(p => p.GetMethod is { IsPublic: true }))
                members.Add(p.Name);
        }

        foreach (var line in members.OrderBy(s => s, StringComparer.Ordinal))
            sb.Append("  • ").AppendLine(line);
    }

    private static string FriendlyType(Type t)
    {
        if (t == typeof(string)) return "string";
        if (t == typeof(int))    return "int";
        if (t == typeof(double)) return "double";
        if (t == typeof(bool))   return "bool";
        var nullable = Nullable.GetUnderlyingType(t);
        if (nullable is not null) return FriendlyType(nullable) + "?";
        return t.Name;
    }

    private static string FormatDefault(object? value) => value switch
    {
        null     => "null",
        string s => $"\"{s}\"",
        bool b   => b ? "true" : "false",
        _        => value.ToString() ?? "null"
    };

    private static string BuildUserMessage(string prose, string ticker)
    {
        var ticketUpper = string.IsNullOrWhiteSpace(ticker) ? "SPY" : ticker.ToUpperInvariant();
        return $"Ticker: {ticketUpper}\n\nDescription:\n{prose}";
    }

    /// <summary>
    /// Strip Markdown code fences (```csharp ... ```) that some Claude responses
    /// wrap around the chain even when told not to. Idempotent for clean output.
    /// </summary>
    private static string StripCodeFence(string content)
    {
        var trimmed = content.Trim();
        if (!trimmed.StartsWith("```")) return trimmed;
        var firstNewline = trimmed.IndexOf('\n');
        if (firstNewline < 0) return trimmed;
        var body = trimmed[(firstNewline + 1)..];
        var lastFence = body.LastIndexOf("```", StringComparison.Ordinal);
        if (lastFence > 0) body = body[..lastFence];
        return body.Trim();
    }

    public sealed record GenerationResult(bool Success, string Script, string? Error);
}
