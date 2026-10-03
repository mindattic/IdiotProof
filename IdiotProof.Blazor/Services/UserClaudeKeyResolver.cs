using IdiotProof.Blazor.Data;
using IdiotProof.Engine.Settings;

namespace IdiotProof.Blazor.Services;

/// <summary>What the LLM gate runs with for one strategy owner.</summary>
/// <param name="VotingEnabled">The owner turned voting on (API Keys page) or the host did (<c>AppSettings.LlmVotingEnabled</c>).</param>
/// <param name="ClaudeApiKey">The Claude key the panel's Claude voter uses; null when none resolved.</param>
/// <param name="ClaudeModel">The owner's chosen Claude model when the owner's own key is used; null otherwise.</param>
/// <param name="KeySource">"owner", "host" or "none" — for the audit trail, never the key itself.</param>
public sealed record SignalVotingCredentials(bool VotingEnabled, string? ClaudeApiKey, string? ClaudeModel, string KeySource);

/// <summary>
/// Resolves the Claude key for a strategy's owner, so the Monitor's LLM gate votes on the owner's
/// own key (BIBLE §4.4). Order: the owner's key from the API Keys page (the encrypted
/// <c>UserApiKeys</c> row, read through <see cref="UserKeyService"/>), then the host key
/// (<see cref="AppSettings.ClaudeApiKey"/>: the MindAttic Vault LLM keyring, own-scoped
/// <c>idiotproof-claude</c> before the shared <c>claude</c>, then env/config), then none.
/// Voting is on when either the owner or the host enabled it, so a user toggle can add the gate
/// but never remove a host-wide one. An owner row that fails to load or decrypt falls through to
/// the host key; it never borrows another user's key.
/// </summary>
public sealed class UserClaudeKeyResolver
{
    private readonly Func<Guid, CancellationToken, Task<UserApiKeys>> loadOwnerKeys;
    private readonly Func<string?> hostClaudeKey;
    private readonly Func<bool> hostVotingEnabled;
    private readonly ILogger<UserClaudeKeyResolver> logger;

    public UserClaudeKeyResolver(UserKeyService userKeys, AppSettings appSettings, ILogger<UserClaudeKeyResolver> logger)
        : this(userKeys.GetOrCreateAsync, () => appSettings.ClaudeApiKey, () => appSettings.LlmVotingEnabled, logger) { }

    /// <summary>Test seam: fakes for the owner-key store and the host settings.</summary>
    internal UserClaudeKeyResolver(
        Func<Guid, CancellationToken, Task<UserApiKeys>> loadOwnerKeys,
        Func<string?> hostClaudeKey,
        Func<bool> hostVotingEnabled,
        ILogger<UserClaudeKeyResolver> logger)
    {
        this.loadOwnerKeys     = loadOwnerKeys;
        this.hostClaudeKey     = hostClaudeKey;
        this.hostVotingEnabled = hostVotingEnabled;
        this.logger            = logger;
    }

    /// <summary>The pure rule: owner key, then host key, then none.</summary>
    public static SignalVotingCredentials Choose(UserApiKeys? owner, string? hostKey, bool hostVotingEnabled)
    {
        var enabled = hostVotingEnabled || owner?.LlmVotingEnabled == true;
        if (!string.IsNullOrWhiteSpace(owner?.ClaudeApiKey))
            return new(enabled, owner.ClaudeApiKey.Trim(),
                string.IsNullOrWhiteSpace(owner.ClaudeModel) ? null : owner.ClaudeModel, "owner");
        if (!string.IsNullOrWhiteSpace(hostKey))
            return new(enabled, hostKey.Trim(), null, "host");
        return new(enabled, null, null, "none");
    }

    public async Task<SignalVotingCredentials> ResolveAsync(Guid ownerUserId, CancellationToken ct = default)
    {
        UserApiKeys? owner = null;
        try
        {
            owner = await loadOwnerKeys(ownerUserId, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not read API keys for user {UserId}; the LLM gate falls back to the host Claude key.", ownerUserId);
        }
        return Choose(owner, hostClaudeKey(), hostVotingEnabled());
    }
}
