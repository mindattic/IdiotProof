using IdiotProof.Core.Data;
using IdiotProof.Operations.Rendering;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Operations.Users;

/// <summary>Which accounts are test fixtures (Cypress/NUnit runs leave many behind).</summary>
internal static class UserFixtures
{
    /// <summary>The domain every automated test registers under.</summary>
    public const string TestDomain = "idiotproof.local";

    /// <summary>A fixture: an <c>@idiotproof.local</c> address, or a bare user name with no email at all.</summary>
    public static bool IsFixture(string userName, string? email)
    {
        var address = string.IsNullOrWhiteSpace(email) ? userName : email;
        return !address.Contains('@') || address.EndsWith("@" + TestDomain, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record UsersListArgs
{
    [Option("all", Description = "Include test-fixture accounts (@idiotproof.local and bare user names), hidden by default.")]
    public bool All { get; init; }
}

/// <summary>
/// The accounts in the database (IP-T253): id, email, role, active, created and last sign-in (ET),
/// and their Alpaca routing. Test-fixture accounts are hidden unless <c>--all</c>; the owner this
/// call acts for is starred. Never shows a password hash or a key.
/// </summary>
[Operation("users list", Summary = "List the IdiotProof accounts (test fixtures hidden unless --all); * marks who you act as.", Risk = OperationRisk.Read)]
[Example("idiotproof users list")]
[Example("idiotproof users list --all")]
public sealed class UsersListOperation : IOperation<UsersListArgs>
{
    public async Task<OperationResult> ExecuteAsync(UsersListArgs args, OperationContext ctx)
    {
        await using var db = await ctx.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync(ctx.CancellationToken);
        var users = await db.AuthUsers.AsNoTracking()
            .OrderBy(u => u.CreatedUtc)
            .Select(u => new { u.Id, u.UserName, u.Email, u.Role, u.IsActive, u.CreatedUtc, u.LastLoginUtc })
            .ToListAsync(ctx.CancellationToken);
        var routing = await db.UserApiKeys.AsNoTracking()
            .Select(k => new { k.UserId, k.DefaultBroker })
            .ToDictionaryAsync(k => k.UserId, k => k.DefaultBroker, ctx.CancellationToken);

        var shown = users.Where(u => args.All || !UserFixtures.IsFixture(u.UserName, u.Email)).ToList();
        var hidden = users.Count - shown.Count;
        var rows = shown.Select(u => new
        {
            id = u.Id,
            email = u.Email ?? u.UserName,
            role = u.Role,
            active = u.IsActive,
            fixture = UserFixtures.IsFixture(u.UserName, u.Email),
            routing = routing.TryGetValue(u.Id, out var b) && string.Equals(b, "alpaca", StringComparison.OrdinalIgnoreCase) ? "alpaca" : "sandbox",
            you = u.Id == ctx.OwnerId,
            created_utc = u.CreatedUtc,
            last_login_utc = u.LastLoginUtc,
        }).ToList();

        var table = new TextTable { MaxWidth = ctx.Width }
            .Column("").Column("id").Column("email", maxWidth: 40).Column("role").Column("active").Column("routing").Column("created").Column("last sign-in");
        foreach (var r in rows)
            table.Row(r.you ? "*" : "", r.id.ToString(), r.email, r.role, r.active ? "yes" : "no", r.routing,
                ctx.Et(r.created_utc), r.last_login_utc is { } l ? ctx.Et(l) : "never");

        var text = rows.Count == 0 ? "No accounts." : table.Render();
        if (hidden > 0) text += $"\n\n{hidden} test-fixture account(s) hidden; --all shows them.";
        text += "\nAct as one with `idiotproof users use <id>`; create one with `idiotproof users create <email> --password ...`.";
        return OperationResult.Ok(new { users = rows, hidden_fixtures = hidden }, text);
    }
}
