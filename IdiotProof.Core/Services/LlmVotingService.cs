using System.Text;
using IdiotProof.Models;
using MindAttic.Legion;
using MindAttic.Legion.Providers;
using LegionPanel = MindAttic.Legion.LlmVotingService;

namespace IdiotProof.Blazor.Services;

public enum VoteDecision { Approve, Reject, Abstain }

public sealed class LlmVotingResult
{
    public string SignalId { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public List<LlmVote> Votes { get; set; } = [];

    /// <summary>
    /// Initialized to Abstain, not the enum default: Approve is enum zero, so
    /// an uninitialized result (panel unavailable, zero votes) must never read
    /// as an approval on the money path (same fail-closed rule as LlmVote).
    /// </summary>
    public VoteDecision Consensus { get; set; } = VoteDecision.Abstain;
    public decimal ConsensusConfidence { get; set; }
    public string ConsensusReasoning { get; set; } = "";
    public DateTime VotedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class LlmVote
{
    /// <summary>Voter display name: the provider plus the trading lens it voted through, e.g. "claude (Risk Manager)".</summary>
    public string PersonaName { get; set; } = "";

    /// <summary>The Legion provider id that cast the vote ("claude", "openai", ...).</summary>
    public string ModelId { get; set; } = "";

    /// <summary>
    /// Defaults to Abstain, NOT the enum's zero value (Approve): a vote whose
    /// decision was never parsed must never count as an approval on a
    /// money-movement path (IP-LAW-1).
    /// </summary>
    public VoteDecision Decision { get; set; } = VoteDecision.Abstain;

    /// <summary>0-100 (Legion reports 1-10; scaled ×10).</summary>
    public decimal Confidence { get; set; }
    public string Reasoning { get; set; } = "";
}

/// <summary>
/// The LLM gate of IP-LAW-1, run on MindAttic.Legion's own voter panel. The panel is the providers
/// <c>legion.json</c> declares (claude, openai, gemini, deepseek) that have a key; Claude always sits
/// on it with the strategy owner's resolved key (<see cref="UserClaudeKeyResolver"/>), the others use
/// the shared MindAttic LLM keyring. Each voter takes one trading lens (Risk Manager, Momentum Trader,
/// Technical Analyst, rotating) and casts a choice vote — Approve, Reject or Abstain — through
/// <see cref="LegionPanel.VoteWithProfilesAsync"/>, which runs the calls in parallel, snaps each answer
/// to the ballot and refills failed seats from the providers that answered.
/// <para>
/// The consensus is IdiotProof's and fails closed: only successful votes count, each equally;
/// Approve needs at least <c>LlmConsensusThreshold</c> of them; zero votes, unparseable or off-ballot
/// answers (Legion marks those as errors) and a below-threshold split all leave the result at Abstain,
/// which the Monitor treats as a block.
/// </para>
/// </summary>
public sealed class LlmVotingService
{
    public const string ApproveOption = "Approve";
    public const string RejectOption  = "Reject";
    public const string AbstainOption = "Abstain";

    private readonly LegionPanel panel;
    private readonly VotingConfiguration config;
    private readonly ILogger<LlmVotingService> logger;

    public LlmVotingService(LegionPanel panel, VotingConfiguration config, ILogger<LlmVotingService> logger)
    {
        this.panel  = panel;
        this.config = config;
        this.logger = logger;
    }

    /// <summary>
    /// The voting configuration for the shipped <c>legion.json</c>: its voters become the provider
    /// whitelist, its judge and model overrides apply. Keys resolve per voter (Claude's is passed in
    /// per vote) and otherwise from the shared MindAttic LLM keyring.
    /// </summary>
    public static VotingConfiguration BuildConfiguration(LegionConfig? legion)
    {
        var cfg = new VotingConfiguration();
        legion?.ApplyTo(cfg);
        return cfg;
    }

    /// <summary>
    /// Wires the panel against a transport. Production passes an <see cref="IHttpClientFactory"/>
    /// client; tests pass an <see cref="HttpClient"/> over a fake handler so nothing leaves the process.
    /// </summary>
    public static LlmVotingService Create(HttpClient http, VotingConfiguration config, ILoggerFactory loggers) =>
        new(new LegionPanel(new LlmVotingProvider(http, config), config, loggers.CreateLogger<LegionPanel>()),
            config, loggers.CreateLogger<LlmVotingService>());

    /// <summary>Trading lenses, assigned to voters in rotation.</summary>
    internal static readonly (string Name, string Markdown)[] Lenses =
    [
        ("Risk Manager",
            """
            You are a strict risk manager at a proprietary trading firm. Your primary concern is capital preservation.
            Approve only when the risk:reward is at least 1.5:1, the stop is clearly defined and not too wide,
            the signal does not fight the dominant trend, and the setup justifies the position size.
            You are skeptical by nature. When in doubt, Reject.
            """),
        ("Momentum Trader",
            """
            You are a momentum trader who capitalizes on strong directional moves. Approve when price action shows
            clear momentum (strong closes, volume surge), the signal aligns with the intraday trend, entry is near
            a key level (VWAP, premarket high/low, prior close), and the reward is at least twice the risk.
            If the setup is weak or choppy, Reject.
            """),
        ("Technical Analyst",
            """
            You are an objective technical analyst. Judge indicator alignment (RSI, MACD, ADX, VWAP, EMA trend),
            chart structure (higher highs/lows or lower highs/lows), volume confirmation, time of day (premarket
            vs regular hours) and divergences. Vote purely on technical merit.
            """),
    ];

    /// <summary>
    /// The voters for one signal: every legion.json provider with a key (Claude always, on
    /// <paramref name="claudeApiKey"/>), Claude first, each with a rotating trading lens.
    /// </summary>
    internal IReadOnlyList<VoterProfile> BuildPanel(string claudeApiKey, string? claudeModel)
    {
        var claudeAllowed = config.AllowedProviderIds.Count == 0 || config.AllowedProviderIds.Contains("claude");
        var others = config.ActiveProviderIds
            .Where(id => !id.Equals("claude", StringComparison.OrdinalIgnoreCase))
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase);
        var ids = new List<string>();
        if (claudeAllowed) ids.Add("claude");
        ids.AddRange(others);

        return ids.Select((id, i) =>
        {
            var lens = Lenses[i % Lenses.Length];
            var isClaude = id.Equals("claude", StringComparison.OrdinalIgnoreCase);
            return new VoterProfile
            {
                ProviderId          = id,
                Name                = $"{id} ({lens.Name})",
                PersonalityMarkdown = lens.Markdown.Trim(),
                ApiKeyOverride      = isClaude ? claudeApiKey : null,
                ModelOverride       = isClaude && !string.IsNullOrWhiteSpace(claudeModel) ? claudeModel : null,
            };
        }).ToList();
    }

    /// <summary>
    /// Puts <paramref name="signal"/> to the panel. Returns an empty Abstain result (no votes) when
    /// voting is off or no Claude key resolved; the Monitor skips the gate only in those two cases.
    /// </summary>
    public async Task<LlmVotingResult> VoteOnSignalAsync(
        TradeSignal signal,
        IReadOnlyList<Candle> recentCandles,
        SignalVotingCredentials credentials,
        decimal consensusThreshold,
        CancellationToken ct = default)
    {
        var result = new LlmVotingResult();
        if (!credentials.VotingEnabled || string.IsNullOrWhiteSpace(credentials.ClaudeApiKey))
            return result;

        var request = new VoteRequest
        {
            Question            = $"Should this {signal.Direction} trade on {signal.Symbol} be executed now?",
            Context             = BuildSignalContext(signal, recentCandles),
            Options             = [ApproveOption, RejectOption, AbstainOption],
            MaxTokens           = 512,
            Temperature         = 0.3,
            SynthesizeNarrative = false,
        };

        VotingResult vote;
        try
        {
            vote = await panel.VoteWithProfilesAsync(
                request, Quorum.Plurality, BuildPanel(credentials.ClaudeApiKey, credentials.ClaudeModel), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "LLM voter panel failed for signal {Symbol} — failing closed.", signal.Symbol);
            return result;
        }

        foreach (var failed in vote.IndividualVotes.Where(v => v.IsError))
            logger.LogWarning("LLM voter {Voter} failed: {Error}", failed.VoterName, failed.ErrorMessage);

        result.Votes = vote.IndividualVotes
            .Where(v => !v.IsError)
            .Select(v => new LlmVote
            {
                PersonaName = v.VoterName,
                ModelId     = v.ProviderId,
                Decision    = MapDecision(v.Decision),
                Confidence  = Math.Clamp(v.Confidence, 0, 10) * 10m,
                Reasoning   = v.Reasoning,
            })
            .ToList();

        if (result.Votes.Count == 0)
            return result;

        CalculateConsensus(result, consensusThreshold);
        result.VotedAtUtc = DateTime.UtcNow;
        return result;
    }

    /// <summary>Ballot text → decision. Anything but an exact Approve/Reject is Abstain (fail closed).</summary>
    internal static VoteDecision MapDecision(string? decision) => decision?.Trim().ToLowerInvariant() switch
    {
        "approve" => VoteDecision.Approve,
        "reject"  => VoteDecision.Reject,
        _         => VoteDecision.Abstain,
    };

    /// <summary>
    /// Equal-weight consensus over the counted votes: Approve when the approve share reaches
    /// <paramref name="consensusThreshold"/>, Reject when the reject share does, else Abstain.
    /// </summary>
    internal static void CalculateConsensus(LlmVotingResult result, decimal consensusThreshold)
    {
        var votes = result.Votes;
        if (votes.Count == 0)
        {
            result.Consensus = VoteDecision.Abstain;
            result.ConsensusConfidence = 0;
            result.ConsensusReasoning = "";
            return;
        }

        decimal total = votes.Count;
        var approveRatio = votes.Count(v => v.Decision == VoteDecision.Approve) / total;
        var rejectRatio  = votes.Count(v => v.Decision == VoteDecision.Reject) / total;

        result.Consensus = approveRatio >= consensusThreshold ? VoteDecision.Approve
            : rejectRatio >= consensusThreshold ? VoteDecision.Reject
            : VoteDecision.Abstain;
        result.ConsensusConfidence = votes.Average(v => v.Confidence);
        result.ConsensusReasoning = string.Join(" | ", votes
            .Where(v => !string.IsNullOrEmpty(v.Reasoning))
            .Select(v => $"{v.PersonaName}: {v.Reasoning}"));
    }

    internal static string BuildSignalContext(TradeSignal signal, IReadOnlyList<Candle> candles)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"TRADE SIGNAL — {signal.Symbol}");
        sb.AppendLine($"Direction: {signal.Direction}");
        sb.AppendLine($"Strategy: {signal.StrategyName}");
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

        if (candles.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Recent Price Action (last 15 candles, newest last):");
            foreach (var c in candles.TakeLast(15))
            {
                var trend = c.Close > c.Open ? "▲" : c.Close < c.Open ? "▼" : "─";
                sb.AppendLine($"  {c.StartUtc:HH:mm} {trend} O:{c.Open:F2} H:{c.High:F2} L:{c.Low:F2} C:{c.Close:F2} V:{c.Volume:N0}");
            }
        }

        return sb.ToString().TrimEnd();
    }
}

/// <summary>Registration shared by the Blazor host and the Monitor.</summary>
public static class SignalVotingRegistration
{
    /// <summary>
    /// Registers <see cref="LlmVotingService"/> on Legion's voter panel, configured from the
    /// <c>legion.json</c> shipped next to the binaries.
    /// </summary>
    public static IServiceCollection AddSignalVotingPanel(this IServiceCollection services)
    {
        var config = LlmVotingService.BuildConfiguration(LegionConfig.LoadFromDirectory(AppContext.BaseDirectory));
        services.AddHttpClient(nameof(LlmVotingProvider));
        services.AddSingleton(sp => LlmVotingService.Create(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(LlmVotingProvider)),
            config,
            sp.GetRequiredService<ILoggerFactory>()));
        return services;
    }
}
