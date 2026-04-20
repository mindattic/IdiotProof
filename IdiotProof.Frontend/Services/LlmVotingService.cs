using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using IdiotProof.Engine.Settings;
using IdiotProof.Models;

namespace IdiotProof.Frontend.Services;

public enum VoteDecision { Approve, Reject, Abstain }

public sealed class LlmVotingResult
{
    public string SignalId { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public List<LlmVote> Votes { get; set; } = [];
    public VoteDecision Consensus { get; set; }
    public decimal ConsensusConfidence { get; set; }
    public string ConsensusReasoning { get; set; } = "";
    public DateTime VotedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class LlmVote
{
    public string PersonaName { get; set; } = "";
    public string ModelId { get; set; } = "";
    public VoteDecision Decision { get; set; }
    public decimal Confidence { get; set; }
    public string Reasoning { get; set; } = "";
    public TradeDirection? SuggestedDirection { get; set; }
}

/// <summary>
/// Voting persona — different trading mindsets that evaluate the same signal.
/// Inspired by LLMVoting's VoterProfile pattern.
/// </summary>
file static class TraderPersonas
{
    public static readonly (string Name, string ModelId, int Weight, string SystemPrompt)[] All =
    [
        (
            "Risk Manager",
            "claude-haiku-4-5-20251001",
            2,
            """
            You are a strict risk manager at a proprietary trading firm. Your primary concern is capital preservation.
            You approve trades only when:
            - Risk:Reward ratio is at least 1.5:1
            - Stop loss is clearly defined and not too wide (< 3% from entry)
            - The signal does not go against the dominant trend
            - Confidence is high enough to justify the position size
            You are skeptical by nature. When in doubt, Reject.
            Respond ONLY with JSON: {"decision":"Approve"|"Reject"|"Abstain","confidence":0-100,"reasoning":"1-2 sentences","direction":"Long"|"Short"|null}
            """
        ),
        (
            "Momentum Trader",
            "claude-haiku-4-5-20251001",
            2,
            """
            You are an aggressive momentum trader who capitalizes on strong directional moves.
            You approve trades when:
            - Price action shows clear momentum (strong candle closes, volume surge)
            - The signal aligns with the current intraday trend
            - Entry is near a key level (VWAP, premarket high/low, prior day close)
            - Potential reward is at least 2x the risk
            You look for conviction in the signal. If the setup is weak or choppy, Reject.
            Respond ONLY with JSON: {"decision":"Approve"|"Reject"|"Abstain","confidence":0-100,"reasoning":"1-2 sentences","direction":"Long"|"Short"|null}
            """
        ),
        (
            "Technical Analyst",
            "claude-sonnet-4-6",
            3,
            """
            You are an objective technical analyst with 20 years of experience reading price action and indicators.
            You evaluate signals based on:
            - Indicator alignment (RSI, MACD, ADX, VWAP, EMA trend)
            - Chart structure (higher highs/lows for uptrend, lower highs/lows for downtrend)
            - Volume confirmation (breakouts need volume)
            - Time of day context (premarket moves vs RTH)
            - Divergences (RSI/price divergence as reversal signal)
            You are analytical, not emotional. Base your vote purely on technical merit.
            Respond ONLY with JSON: {"decision":"Approve"|"Reject"|"Abstain","confidence":0-100,"reasoning":"1-2 sentences","direction":"Long"|"Short"|null}
            """
        ),
    ];
}

public sealed class LlmVotingService
{
    private readonly IHttpClientFactory httpClientFactory;
    private readonly ILogger<LlmVotingService> logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public LlmVotingService(IHttpClientFactory httpClientFactory, ILogger<LlmVotingService> logger)
    {
        this.httpClientFactory = httpClientFactory;
        this.logger = logger;
    }

    public async Task<LlmVotingResult> VoteOnSignalAsync(
        TradeSignal signal,
        IReadOnlyList<Candle> recentCandles,
        AppSettings settings,
        CancellationToken ct = default)
    {
        var result = new LlmVotingResult();

        if (!settings.LlmVotingEnabled || string.IsNullOrWhiteSpace(settings.ClaudeApiKey))
            return result;

        var signalContext = BuildSignalContext(signal, recentCandles);

        var tasks = TraderPersonas.All.Select(persona =>
            CallPersonaAsync(persona.Name, persona.ModelId, persona.Weight, persona.SystemPrompt,
                signalContext, settings.ClaudeApiKey, ct)).ToArray();

        LlmVote[] votes;
        try
        {
            votes = await Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "LLM voting encountered an error for signal {Symbol}", signal.Symbol);
            return result;
        }

        result.Votes = [.. votes.Where(v => v != null)];
        if (result.Votes.Count == 0)
            return result;

        CalculateWeightedConsensus(result, settings.LlmConsensusThreshold);
        result.VotedAtUtc = DateTime.UtcNow;

        return result;
    }

    private void CalculateWeightedConsensus(LlmVotingResult result, decimal consensusThreshold)
    {
        var personaWeights = TraderPersonas.All.ToDictionary(p => p.Name, p => p.Weight);

        decimal totalWeight = 0, approveWeight = 0, rejectWeight = 0, totalConfidence = 0;
        var reasonings = new List<string>();

        foreach (var vote in result.Votes)
        {
            var weight = personaWeights.GetValueOrDefault(vote.PersonaName, 1);
            totalWeight += weight;
            totalConfidence += vote.Confidence * weight;

            if (vote.Decision == VoteDecision.Approve) approveWeight += weight;
            else if (vote.Decision == VoteDecision.Reject) rejectWeight += weight;

            if (!string.IsNullOrEmpty(vote.Reasoning))
                reasonings.Add($"{vote.PersonaName}: {vote.Reasoning}");
        }

        if (totalWeight > 0)
        {
            var approveRatio = approveWeight / totalWeight;
            var rejectRatio = rejectWeight / totalWeight;

            result.Consensus = approveRatio >= consensusThreshold ? VoteDecision.Approve
                : rejectRatio >= consensusThreshold ? VoteDecision.Reject
                : VoteDecision.Abstain;

            result.ConsensusConfidence = totalConfidence / totalWeight;
        }

        result.ConsensusReasoning = string.Join(" | ", reasonings);
    }

    private async Task<LlmVote> CallPersonaAsync(
        string personaName,
        string modelId,
        int weight,
        string systemPrompt,
        string signalContext,
        string apiKey,
        CancellationToken ct)
    {
        var vote = new LlmVote { PersonaName = personaName, ModelId = modelId, Decision = VoteDecision.Abstain };

        try
        {
            using var client = httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("x-api-key", apiKey);
            client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

            var body = new
            {
                model = modelId,
                max_tokens = 256,
                system = systemPrompt,
                messages = new[]
                {
                    new { role = "user", content = signalContext }
                }
            };

            var response = await client.PostAsJsonAsync(
                "https://api.anthropic.com/v1/messages", body, JsonOpts, ct);

            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync(ct);
                logger.LogWarning("LLM persona {Persona} returned {Status}: {Error}", personaName, response.StatusCode, err);
                return vote;
            }

            var responseJson = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            var content = responseJson
                .GetProperty("content")[0]
                .GetProperty("text")
                .GetString() ?? "";

            var parsed = ParseVoteJson(content);
            if (parsed != null)
            {
                vote.Decision = parsed.Decision;
                vote.Confidence = parsed.Confidence;
                vote.Reasoning = parsed.Reasoning;
                vote.SuggestedDirection = parsed.SuggestedDirection;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error calling LLM persona {Persona}", personaName);
        }

        return vote;
    }

    private static string BuildSignalContext(TradeSignal signal, IReadOnlyList<Candle> candles)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"TRADE SIGNAL — {signal.Symbol}");
        sb.AppendLine($"Direction: {signal.Direction}");
        sb.AppendLine($"Strategy: {signal.StrategyName}");
        sb.AppendLine($"Confidence: {signal.ConfidencePercent:F1}%");
        sb.AppendLine($"Entry: ${signal.SuggestedEntry:F2}");
        sb.AppendLine($"Stop: ${signal.SuggestedStop:F2}");

        if (signal.Targets.Count > 0)
        {
            sb.AppendLine($"Targets: {string.Join(", ", signal.Targets.Select(t => $"${t:F2}"))}");
            var riskPts = Math.Abs(signal.SuggestedEntry - signal.SuggestedStop);
            var rewardPts = Math.Abs(signal.Targets[0] - signal.SuggestedEntry);
            if (riskPts > 0)
                sb.AppendLine($"R:R Ratio: {rewardPts / riskPts:F2}:1");
        }

        sb.AppendLine($"Reason: {signal.Reason}");
        sb.AppendLine($"Generated: {signal.GeneratedUtc:yyyy-MM-dd HH:mm} UTC");
        sb.AppendLine();

        if (candles.Count > 0)
        {
            sb.AppendLine("Recent Price Action (last 15 candles, newest last):");
            foreach (var c in candles.TakeLast(15))
            {
                var trend = c.Close > c.Open ? "▲" : c.Close < c.Open ? "▼" : "─";
                sb.AppendLine($"  {c.StartUtc:HH:mm} {trend} O:{c.Open:F2} H:{c.High:F2} L:{c.Low:F2} C:{c.Close:F2} V:{c.Volume:N0}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("Should this trade be executed? Vote now.");
        return sb.ToString();
    }

    private static LlmVote? ParseVoteJson(string content)
    {
        try
        {
            var start = content.IndexOf('{');
            var end = content.LastIndexOf('}');
            if (start < 0 || end < 0) return null;

            using var doc = JsonDocument.Parse(content[start..(end + 1)]);
            var root = doc.RootElement;
            var vote = new LlmVote();

            if (root.TryGetProperty("decision", out var d))
                vote.Decision = d.GetString()?.ToLowerInvariant() switch
                {
                    "approve" => VoteDecision.Approve,
                    "reject" => VoteDecision.Reject,
                    _ => VoteDecision.Abstain
                };

            if (root.TryGetProperty("confidence", out var c))
                vote.Confidence = c.TryGetDecimal(out var conf) ? conf : 0;

            if (root.TryGetProperty("reasoning", out var r))
                vote.Reasoning = r.GetString() ?? "";

            if (root.TryGetProperty("direction", out var dir))
                vote.SuggestedDirection = dir.GetString()?.ToLowerInvariant() switch
                {
                    "long" => TradeDirection.Long,
                    "short" => TradeDirection.Short,
                    _ => null
                };

            return vote;
        }
        catch
        {
            return null;
        }
    }
}
