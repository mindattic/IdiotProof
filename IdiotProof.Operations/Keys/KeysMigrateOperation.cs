using IdiotProof.Blazor.Services;
using IdiotProof.Operations.Rendering;

namespace IdiotProof.Operations.Keys;

public sealed record KeysMigrateArgs
{
    [Option("apply", Description = "Actually copy into Vault and clear the SQL columns (without it: a dry run that changes nothing).")]
    public bool Apply { get; init; }
}

/// <summary>
/// The one-time move of account secrets from the legacy <c>UserApiKeys</c> SQL columns into
/// MindAttic.Vault (IP-T330), for every user. Without <c>--apply</c> it only reports what it would
/// do. With it: a secret Vault lacks is copied in and read back; one Vault already holds identically
/// is left; either way the SQL column is then cleared. A Vault value that differs from SQL, or
/// ciphertext that no longer decrypts, is reported and left alone on both sides. Never shows a value.
/// </summary>
[Operation("keys migrate-to-vault",
    Summary = "Move Alpaca keys, OAuth tokens and Claude keys out of SQL into MindAttic.Vault (dry run unless --apply).",
    Description = "Per user and secret: copy into Vault when Vault lacks it, verify equality when both have it, then clear the SQL column. Conflicts and undecryptable values are reported and left untouched. Idempotent.",
    Risk = OperationRisk.Destructive)]
[Example("idiotproof keys migrate-to-vault", Description = "Dry run: what would move, nothing changes.")]
[Example("idiotproof keys migrate-to-vault --apply")]
public sealed class KeysMigrateOperation : IOperation<KeysMigrateArgs>
{
    public async Task<OperationResult> ExecuteAsync(KeysMigrateArgs args, OperationContext ctx)
    {
        var report = await ctx.Get<VaultSecretMigration>().RunAsync(args.Apply, ctx.CancellationToken);
        var items = report.Items.Select(i => new
        {
            user = i.UserId,
            secret = i.Secret,
            vault_entry = i.VaultEntry,
            action = Action(i.Action),
            done = i.Done,
        }).ToList();
        var data = new { applied = report.Applied, rows_cleared = report.RowsCleared, conflicts = report.Conflicts, undecryptable = report.Undecryptable, items };

        if (items.Count == 0)
            return OperationResult.Ok(data, "Nothing to migrate: no SQL row holds a secret. Vault is the only home for account secrets.");

        var table = new TextTable { MaxWidth = ctx.Width }
            .Column("user").Column("secret").Column("vault entry", maxWidth: 52).Column("action").Column("done");
        foreach (var i in report.Items)
            table.Row(i.UserId.ToString("N")[..8], i.Secret, i.VaultEntry, Action(i.Action), i.Done ? "yes" : "-");

        var lines = new List<string> { table.Render(), "" };
        lines.Add(report.Applied
            ? $"Applied: cleared legacy secret columns on {report.RowsCleared} row(s)."
            : "Dry run: nothing changed. Run `idiotproof keys migrate-to-vault --apply` to move them.");
        if (report.Conflicts > 0)
            lines.Add($"{report.Conflicts} conflict(s): Vault and SQL disagree. Nothing was touched for those; set the right value with `idiotproof keys set`, then re-run.");
        if (report.Undecryptable > 0)
            lines.Add($"{report.Undecryptable} value(s) no longer decrypt (DataProtection key-ring mismatch). Re-enter them with `idiotproof keys set`.");
        return OperationResult.Ok(data, string.Join("\n", lines));
    }

    private static string Action(VaultMigrationAction a) => a switch
    {
        VaultMigrationAction.CopyToVault => "copy-to-vault",
        VaultMigrationAction.AlreadyInVault => "already-in-vault",
        VaultMigrationAction.Conflict => "conflict",
        _ => "undecryptable",
    };
}
