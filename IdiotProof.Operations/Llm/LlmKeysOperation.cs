using IdiotProof.Blazor.Services;
using IdiotProof.Engine.Settings;
using IdiotProof.Operations.Rendering;

namespace IdiotProof.Operations.Llm;

/// <summary>How the Claude key resolves for the owner; booleans and sources only, never a key.</summary>
public sealed record LlmKeyStatus(
    bool OwnerKeyStored, bool HostKeyAvailable, string ResolvedFrom, string? Model, string VaultEntry,
    bool OwnerVotingEnabled, bool HostVotingEnabled, bool GateActive);

/// <summary>
/// Which Claude key the LLM features would use for you, in the order <see cref="UserClaudeKeyResolver"/>
/// tries them: your own (MindAttic.Vault Tokens entry), then the host's (the MindAttic LLM keyring),
/// then none, plus the model and both voting toggles. Never prints, hashes or hints at a key.
/// </summary>
[Operation("llm keys", Summary = "Which Claude key the LLM panel resolves for you (yours, the host's, or none) and the voting toggles; never shows a key.", Risk = OperationRisk.Read)]
[Example("idiotproof llm keys")]
public sealed class LlmKeysOperation : IOperation<NoArgs>
{
    public async Task<OperationResult> ExecuteAsync(NoArgs args, OperationContext ctx)
    {
        var owner = ctx.RequireOwner();
        var resolver = ctx.Get<UserClaudeKeyResolver>();
        var resolved = await resolver.ResolveAsync(owner, ctx.CancellationToken);
        var hostAvailable = !string.IsNullOrWhiteSpace(resolver.ResolveHost().ClaudeApiKey);
        var settings = ctx.Get<AppSettings>();
        var status = new LlmKeyStatus(
            resolved.KeySource == "owner", hostAvailable, resolved.KeySource,
            resolved.KeySource == "owner" ? resolved.ClaudeModel : settings.LlmVoterModel,
            "Tokens/" + AccountSecretVault.TokenEntryName(owner, AccountToken.Claude),
            await LlmReads.OwnerVotingAsync(ctx, owner), settings.LlmVotingEnabled,
            resolved.VotingEnabled && resolved.ClaudeApiKey is not null);

        var block = new KeyValueBlock()
            .Add("Your key", status.OwnerKeyStored ? $"stored ({status.VaultEntry})" : "not set (`idiotproof keys set --claude`)")
            .Add("Host key", status.HostKeyAvailable ? "available (MindAttic LLM keyring)" : "not available")
            .Add("Resolves to", LlmReads.Source(status.ResolvedFrom))
            .Add("Model", status.Model, "-")
            .Add("Voting", $"you {(status.OwnerVotingEnabled ? "on" : "off")}, host {(status.HostVotingEnabled ? "on" : "off")}")
            .Add("LLM gate", status.GateActive ? "ON for your automated entries" : "skipped (" + (resolved.ClaudeApiKey is null ? "no key" : "voting off") + ")");
        return OperationResult.Ok(status, block.Render() + "\nKeys are never displayed.");
    }
}
