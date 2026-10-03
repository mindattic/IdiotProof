using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace IdiotProof.Blazor.Services;

/// <summary>
/// The Claude key for the person making this request: the signed-in user's own key from the API
/// Keys page, then the host key, then none (<see cref="UserClaudeKeyResolver"/>, the same rule the
/// Monitor uses for a strategy owner). Scoped per circuit and resolved on every call, never cached,
/// so one user's key can never serve another user's request. The Describe tab, the Gapper transcript
/// reader and the Research page resolve through here and pass the result to the LLM service.
/// </summary>
public sealed class SignedInClaudeKey
{
    private readonly Func<Task<ClaimsPrincipal>> currentUser;
    private readonly UserClaudeKeyResolver resolver;

    public SignedInClaudeKey(AuthenticationStateProvider auth, UserClaudeKeyResolver resolver)
        : this(async () => (await auth.GetAuthenticationStateAsync()).User, resolver) { }

    /// <summary>Test seam: any source of the current principal.</summary>
    internal SignedInClaudeKey(Func<Task<ClaimsPrincipal>> currentUser, UserClaudeKeyResolver resolver)
    {
        this.currentUser = currentUser;
        this.resolver    = resolver;
    }

    public async Task<SignalVotingCredentials> ResolveAsync(CancellationToken ct = default)
    {
        var user = await currentUser();
        var raw = user.Identity?.IsAuthenticated == true ? user.FindFirst(ClaimTypes.NameIdentifier)?.Value : null;
        return Guid.TryParse(raw, out var userId)
            ? await resolver.ResolveAsync(userId, ct)
            : resolver.ResolveHost();
    }
}
