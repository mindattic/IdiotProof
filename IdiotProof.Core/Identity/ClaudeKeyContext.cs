using IdiotProof.Blazor.Services;

namespace IdiotProof.Core.Identity;

/// <summary>
/// The Claude key for whoever is making this request: their own key from the API Keys page, then
/// the host key, then none (<see cref="UserClaudeKeyResolver"/>). The interactive LLM features
/// (strategy generation, the gapper transcript reader, research analysis) take the result per
/// call. The web app resolves it from the signed-in user; the Hub, CLI and MCP from the
/// operation's owner. Resolved on every call, never cached, so one user's key never serves
/// another user's request.
/// </summary>
public interface IClaudeKeyContext
{
    Task<SignalVotingCredentials> ResolveAsync(CancellationToken ct = default);
}

/// <summary>Headless <see cref="IClaudeKeyContext"/>: the owner's key, or the host key when there is no owner.</summary>
public sealed class OwnerClaudeKeyContext(IOwnerContext owner, UserClaudeKeyResolver resolver) : IClaudeKeyContext
{
    public async Task<SignalVotingCredentials> ResolveAsync(CancellationToken ct = default) =>
        await owner.GetOwnerIdAsync(ct) is { } ownerId
            ? await resolver.ResolveAsync(ownerId, ct)
            : resolver.ResolveHost();
}
