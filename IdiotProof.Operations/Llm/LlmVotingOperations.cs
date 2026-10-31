using IdiotProof.Engine.Settings;

using IdiotProof.Core.Services;
namespace IdiotProof.Operations.Llm;

/// <summary>Shared by <c>llm voting enable</c> and <c>llm voting disable</c>: the owner's own toggle.</summary>
internal static class LlmVotingToggle
{
    public static async Task<OperationResult> SetAsync(OperationContext ctx, bool enable)
    {
        var owner = ctx.RequireOwner();
        var keys = ctx.Get<UserKeyService>();
        var row = await keys.GetOrCreateAsync(owner, ctx.CancellationToken);
        var was = row.LlmVotingEnabled;
        row.LlmVotingEnabled = enable;
        await keys.SavePreferencesAsync(owner, row, ctx.CancellationToken); // never touches a secret

        var host = ctx.Get<AppSettings>().LlmVotingEnabled;
        var resolved = await ctx.Get<UserClaudeKeyResolver>().ResolveAsync(owner, ctx.CancellationToken);
        var gate = resolved.VotingEnabled && resolved.ClaudeApiKey is not null;
        var said = (was == enable ? $"Your LLM voting was already {(enable ? "on" : "off")}." : $"Your LLM voting is now {(enable ? "on" : "off")}.")
                   + (!enable && host ? " The host has voting on, so the gate still runs for your automated entries (a user toggle can add the gate, never remove a host-wide one)." : "")
                   + (enable && resolved.ClaudeApiKey is null ? " No Claude key resolves yet, so the gate stays skipped until one does (`idiotproof llm keys`)." : "")
                   + $"\nLLM gate for your automated entries: {(gate ? "ON" : "skipped")}.";
        return OperationResult.Ok(new { ownerVotingEnabled = enable, wasEnabled = was, hostVotingEnabled = host, gateActive = gate }, said);
    }
}

/// <summary>
/// Turns on your own LLM voting: the Monitor's automated entries on your strategies then need the
/// panel's Approve consensus (IP-LAW-1), voted on your Claude key (else the host's).
/// </summary>
[Operation("llm voting enable", Summary = "Turn on the LLM voter gate for your automated entries (needs a Claude key to take effect).", Risk = OperationRisk.Write)]
[Example("idiotproof llm voting enable")]
public sealed class LlmVotingEnableOperation : IOperation<NoArgs>
{
    public Task<OperationResult> ExecuteAsync(NoArgs args, OperationContext ctx) => LlmVotingToggle.SetAsync(ctx, true);
}

/// <summary>
/// Turns off your own LLM voting. A host-wide setting still applies: a user can add the gate, never
/// remove a host's.
/// </summary>
[Operation("llm voting disable", Summary = "Turn off your own LLM voter gate (a host-wide setting still applies).", Risk = OperationRisk.Write)]
[Example("idiotproof llm voting disable")]
public sealed class LlmVotingDisableOperation : IOperation<NoArgs>
{
    public Task<OperationResult> ExecuteAsync(NoArgs args, OperationContext ctx) => LlmVotingToggle.SetAsync(ctx, false);
}
