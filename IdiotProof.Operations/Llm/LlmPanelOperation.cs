using IdiotProof.Core.Data;
using IdiotProof.Engine.Settings;
using IdiotProof.Operations.Rendering;
using Microsoft.EntityFrameworkCore;

using IdiotProof.Core.Services;
namespace IdiotProof.Operations.Llm;

/// <summary>One seat on the voter panel; never carries a key.</summary>
public sealed record LlmVoterSeat(string Voter, string Provider, string Lens, bool HasKey, string KeySource, string? Model);

/// <summary>The panel as <c>llm panel</c> reports it.</summary>
public sealed record LlmPanelView(
    bool OwnerVotingEnabled, bool HostVotingEnabled, bool VotingEnabled, string ClaudeKeySource, decimal Threshold,
    bool GateActive, string GateReason, IReadOnlyList<LlmVoterSeat> Voters, IReadOnlyList<string> ConfiguredWithoutKey);

internal static class LlmReads
{
    /// <summary>The consensus threshold the Monitor's next vote uses: the saved one, else AppSettings'.</summary>
    public static Task<decimal> ThresholdAsync(OperationContext ctx) =>
        LlmPanelSettings.ThresholdAsync(ctx.Get<IDbContextFactory<AppDbContext>>(), ctx.Get<AppSettings>().LlmConsensusThreshold, ctx.CancellationToken);

    /// <summary>The owner's own voting toggle from the API Keys settings row (false when there is none).</summary>
    public static async Task<bool> OwnerVotingAsync(OperationContext ctx, Guid owner)
    {
        await using var db = await ctx.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync(ctx.CancellationToken);
        return await db.UserApiKeys.AsNoTracking().Where(k => k.UserId == owner).Select(k => k.LlmVotingEnabled).FirstOrDefaultAsync(ctx.CancellationToken);
    }

    /// <summary>"owner", "host" or "none" in words.</summary>
    public static string Source(string keySource) => keySource switch
    {
        "owner" => "your own key (API Keys page / `keys set --claude`)",
        "host" => "the host's key (MindAttic LLM keyring)",
        _ => "none",
    };

    public static async Task<LlmPanelView> PanelAsync(OperationContext ctx, Guid owner)
    {
        var credentials = await ctx.Get<UserClaudeKeyResolver>().ResolveAsync(owner, ctx.CancellationToken);
        var voting = ctx.Get<LlmVotingService>();
        var settings = ctx.Get<AppSettings>();
        var ownerVoting = await OwnerVotingAsync(ctx, owner);
        var hasClaude = !string.IsNullOrWhiteSpace(credentials.ClaudeApiKey);

        // The panel is built exactly as a vote builds it; the Claude seat gets a placeholder here
        // because this only reads seats and lenses: no key leaves this method.
        var seats = voting.BuildPanel("(not shown)", credentials.ClaudeModel)
            .Select(v =>
            {
                var isClaude = v.ProviderId.Equals("claude", StringComparison.OrdinalIgnoreCase);
                var open = v.Name.IndexOf('(');
                var lens = open < 0 ? "" : v.Name[(open + 1)..].TrimEnd(')');
                return new LlmVoterSeat(v.Name, v.ProviderId, lens, isClaude ? hasClaude : true,
                    isClaude ? credentials.KeySource : "keyring", isClaude ? credentials.ClaudeModel ?? settings.LlmVoterModel : null);
            })
            .ToList();
        var configured = voting.Configuration.AllowedProviderIds
            .Where(id => seats.All(s => !s.Provider.Equals(id, StringComparison.OrdinalIgnoreCase)))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var gateActive = credentials.VotingEnabled && hasClaude;
        var reason = gateActive
            ? "ON: automated entries need an Approve consensus (IP-LAW-1)"
            : !credentials.VotingEnabled
                ? "SKIPPED: voting is off for you and the host"
                : "SKIPPED: no Claude key resolves (yours, then the host's)";
        return new LlmPanelView(ownerVoting, settings.LlmVotingEnabled, credentials.VotingEnabled, credentials.KeySource,
            await ThresholdAsync(ctx), gateActive, reason, seats, configured);
    }
}

/// <summary>
/// The LLM voter panel that gates the Monitor's automated entries (IP-LAW-1): every seat with its
/// provider and trading lens and whether it has a key, providers legion.json names but that have
/// no key, the consensus threshold, and whether the gate is on for you (voting on for you or the
/// host, and a Claude key resolving). Never shows a key.
/// </summary>
[Operation("llm panel", Summary = "The LLM voter panel: voters, trading lenses, which have keys, the consensus threshold and whether the gate is on.", Risk = OperationRisk.Read)]
[Example("idiotproof llm panel")]
public sealed class LlmPanelOperation : IOperation<NoArgs>
{
    public async Task<OperationResult> ExecuteAsync(NoArgs args, OperationContext ctx)
    {
        var view = await LlmReads.PanelAsync(ctx, ctx.RequireOwner());
        var table = new TextTable { MaxWidth = ctx.Width }.Column("Voter").Column("Provider").Column("Lens").Column("Key").Column("Model");
        foreach (var s in view.Voters)
            table.Row(s.Voter, s.Provider, s.Lens, s.HasKey ? s.KeySource : "MISSING", s.Model ?? "-");
        var block = new KeyValueBlock()
            .Add("Gate", view.GateReason)
            .Add("Voting", $"you {(view.OwnerVotingEnabled ? "on" : "off")}, host {(view.HostVotingEnabled ? "on" : "off")} (on when either is)")
            .Add("Claude key", LlmReads.Source(view.ClaudeKeySource))
            .Add("Threshold", $"{Fmt.Percent(view.Threshold, 0)} of counted votes must Approve (Abstain/failed votes never count as yes)")
            .Add("No key", view.ConfiguredWithoutKey.Count == 0 ? null : string.Join(", ", view.ConfiguredWithoutKey) + " (named in legion.json, not seated)", "-");
        return OperationResult.Ok(view, block.Render() + "\n\n" + table.Render());
    }
}
