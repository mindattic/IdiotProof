using IdiotProof.Blazor.Data;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Core.Identity;

/// <summary>
/// The user an operation acts for. Every strategy, key, preference and order is owned by one
/// <c>AuthUsers</c> row; services take that owner explicitly, and each front door supplies it its
/// own way: the web app from the signed-in user's NameIdentifier claim, the Hub/CLI/MCP from an
/// explicit <c>--user</c>, a persisted default, or the only user in the database.
/// </summary>
public interface IOwnerContext
{
    /// <summary>The owner's user id, or null when nobody is identified (callers fail closed).</summary>
    Task<Guid?> GetOwnerIdAsync(CancellationToken ct = default);
}

/// <summary>An owner already known to the caller (a CLI <c>--user</c>, a test, a Hub request).</summary>
public sealed class FixedOwnerContext(Guid? ownerId) : IOwnerContext
{
    public Task<Guid?> GetOwnerIdAsync(CancellationToken ct = default) => Task.FromResult(ownerId);
}

/// <summary>Outcome of <see cref="OwnerResolver.ResolveAsync"/>: an owner, or why there is none.</summary>
public sealed record OwnerResolution(Guid? OwnerId, string? Error)
{
    public bool Found => OwnerId.HasValue;
}

/// <summary>
/// The headless rule for "who am I acting for": an explicit id wins; otherwise the single user in
/// <c>AuthUsers</c>; with zero or several users there is no owner and the caller must name one.
/// Never guesses between users.
/// </summary>
public static class OwnerResolver
{
    public const string NoUsersMessage = "No users exist — create one first (create-account).";
    public const string AmbiguousMessage = "Multiple users exist — pass --user <guid> to pick one.";

    public static async Task<OwnerResolution> ResolveAsync(
        IDbContextFactory<AppDbContext> dbFactory, Guid? explicitOwnerId, CancellationToken ct = default)
    {
        if (explicitOwnerId is { } id) return new OwnerResolution(id, null);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var ids = await db.AuthUsers.OrderBy(x => x.Id).Select(x => x.Id).Take(2).ToListAsync(ct);
        return ids.Count switch
        {
            1 => new OwnerResolution(ids[0], null),
            0 => new OwnerResolution(null, NoUsersMessage),
            _ => new OwnerResolution(null, AmbiguousMessage),
        };
    }
}
