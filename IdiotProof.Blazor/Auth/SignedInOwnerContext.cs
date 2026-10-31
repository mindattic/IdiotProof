using System.Security.Claims;
using IdiotProof.Core.Identity;
using Microsoft.AspNetCore.Components.Authorization;

namespace IdiotProof.Blazor.Auth;

/// <summary>
/// The web front door's <see cref="IOwnerContext"/>: the signed-in user's id from the
/// <see cref="ClaimTypes.NameIdentifier"/> claim. Scoped per circuit and read on every call, never
/// cached, so a sign-out or a different user on the circuit is seen at once. Anonymous, or a claim
/// that is not a Guid, means no owner (callers fail closed). The Hub, CLI and MCP supply the owner
/// their own way (<c>OperationOwnerContext</c>); everything downstream takes the same interface.
/// </summary>
public sealed class SignedInOwnerContext : IOwnerContext
{
    private readonly Func<Task<ClaimsPrincipal>> currentUser;

    public SignedInOwnerContext(AuthenticationStateProvider auth)
        : this(async () => (await auth.GetAuthenticationStateAsync()).User) { }

    /// <summary>Test seam: any source of the current principal.</summary>
    internal SignedInOwnerContext(Func<Task<ClaimsPrincipal>> currentUser) => this.currentUser = currentUser;

    public async Task<Guid?> GetOwnerIdAsync(CancellationToken ct = default) => OwnerOf(await currentUser());

    /// <summary>The owner id carried by <paramref name="user"/>, or null when unauthenticated or malformed.</summary>
    public static Guid? OwnerOf(ClaimsPrincipal? user)
    {
        if (user?.Identity?.IsAuthenticated != true) return null;
        return Guid.TryParse(user.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) && id != Guid.Empty
            ? id
            : null;
    }
}
