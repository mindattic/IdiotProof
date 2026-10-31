using MindAttic.Authentication.Services;

using IdiotProof.Core.Services;
namespace IdiotProof.Operations.Users;

public sealed record UsersCreateArgs
{
    [Argument(0, Name = "email", Description = "The new account's email (also its user name).")]
    public string Email { get; init; } = "";

    [Option("password", Description = "Initial password: 8 to 128 characters with at least one digit.", Required = true)]
    public string Password { get; init; } = "";

    [Option("role", Description = "User (default) or Admin.")]
    public string Role { get; init; } = "User";

    [Option("must-change", Description = "Make them choose a new password at first sign-in.")]
    public bool MustChange { get; init; }
}

/// <summary>
/// Creates an account through the real MindAttic.Authentication path (Argon2id, the shared pepper),
/// the port of the Monitor's <c>create-account</c> (IP-T254): the disposable-domain blocklist
/// (seeded first, so a fresh database still blocks) and the web app's password rule apply. The
/// password is never echoed and the ledger masks it.
/// </summary>
[Operation("users create", Summary = "Create an IdiotProof account (blocklist and password rules of web sign-up; Argon2id via MindAttic.Authentication).", Risk = OperationRisk.Write)]
[Example("idiotproof users create trader@example.com --password <password>")]
[Example("idiotproof users create admin@example.com --password <password> --role Admin --must-change")]
public sealed class UsersCreateOperation : IOperation<UsersCreateArgs>
{
    public async Task<OperationResult> ExecuteAsync(UsersCreateArgs args, OperationContext ctx)
    {
        var email = args.Email.Trim();
        if (EmailDomainBlocklistService.DomainOf(email) is null)
            return OperationResult.Usage($"'{email}' is not an email address.");
        var role = args.Role.Trim();
        if (!string.Equals(role, "User", StringComparison.OrdinalIgnoreCase) && !string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase))
            return OperationResult.Usage("--role takes User or Admin.");
        role = char.ToUpperInvariant(role[0]) + role[1..].ToLowerInvariant();
        var password = args.Password;
        if (password.Length < 8 || password.Length > 128 || !password.Any(char.IsDigit))
            return OperationResult.Usage("The password must be 8 to 128 characters and contain at least one digit.");

        var blocklist = ctx.Get<EmailDomainBlocklistService>();
        await blocklist.SeedAsync(ctx.CancellationToken);
        if (await blocklist.IsBlockedAsync(email, ctx.CancellationToken))
            return OperationResult.Blocked($"{EmailDomainBlocklistService.DomainOf(email)} is a disposable/blocked email domain.");

        var result = await ctx.Get<IUserAdminService>().CreateAsync(
            userName: email, email: email, role: role, password: password,
            mustChangePassword: args.MustChange, ct: ctx.CancellationToken);
        if (!result.Ok)
            return OperationResult.Fail($"Account not created: {result.Error}");

        return OperationResult.Ok(new { id = result.UserId, email, role, must_change_password = args.MustChange },
            $"Created {email} ({role}), id {result.UserId}. Alpaca routing starts on the Sandbox; " +
            $"act as them with `idiotproof users use {result.UserId}`.");
    }
}
