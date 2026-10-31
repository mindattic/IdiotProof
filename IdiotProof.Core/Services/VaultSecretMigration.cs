using IdiotProof.Blazor.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IdiotProof.Blazor.Services;

/// <summary>What the migration does (or would do) with one legacy secret.</summary>
public enum VaultMigrationAction
{
    /// <summary>Vault lacks it: copy the decrypted SQL value into Vault, then clear the column.</summary>
    CopyToVault,

    /// <summary>Vault already holds the identical value: just clear the column.</summary>
    AlreadyInVault,

    /// <summary>Vault holds a DIFFERENT value: nothing is touched; a person decides which is right.</summary>
    Conflict,

    /// <summary>The SQL ciphertext no longer decrypts (key-ring mismatch): left as is.</summary>
    Undecryptable,
}

/// <summary>One legacy secret: whose, which (paper pair, live pair, Claude, OAuth), where in Vault, and the verdict. Never the value.</summary>
public sealed record VaultMigrationItem(Guid UserId, string Secret, string VaultEntry, VaultMigrationAction Action, bool Done);

/// <summary>The migration's report. <see cref="Applied"/> false = a dry run that changed nothing.</summary>
public sealed record VaultMigrationReport(bool Applied, IReadOnlyList<VaultMigrationItem> Items, int RowsCleared)
{
    public int Conflicts => Items.Count(i => i.Action is VaultMigrationAction.Conflict);

    public int Undecryptable => Items.Count(i => i.Action is VaultMigrationAction.Undecryptable);
}

/// <summary>
/// The one-time move of account secrets from the legacy <c>UserApiKeys</c> SQL columns into
/// MindAttic.Vault (IP-T330), run explicitly with <c>idiotproof keys migrate-to-vault</c>
/// (a dry run) and <c>... --apply</c>. Per user and per secret: when Vault lacks it the decrypted
/// value is copied in and read back to verify; when Vault already has the same value nothing is
/// copied; either way the SQL column is then nulled (a plain tracked-entity update, no EF schema
/// migration). A Vault value that differs from SQL, or ciphertext that no longer decrypts, is
/// reported and left untouched on both sides. Idempotent: a second run finds nothing to do.
/// Values are never logged or returned.
/// </summary>
public sealed class VaultSecretMigration(
    IDbContextFactory<AppDbContext> dbFactory,
    UserKeyService userKeys,
    ILogger<VaultSecretMigration> logger)
{
    public async Task<VaultMigrationReport> RunAsync(bool apply, CancellationToken ct = default)
    {
        var vault = userKeys.Vault;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await db.UserApiKeys.ToListAsync(ct);
        var items = new List<VaultMigrationItem>();
        var cleared = 0;

        foreach (var row in rows.Where(r => UserKeyService.LegacySecretColumns(r).Count > 0))
        {
            var changed = false;

            // Alpaca pairs move as a unit: the key id and secret columns are cleared together.
            foreach (var isPaper in new[] { true, false })
            {
                var (idCol, secretCol) = isPaper
                    ? (row.AlpacaApiKeyId, row.AlpacaApiSecretKey)
                    : (row.AlpacaLiveApiKeyId, row.AlpacaLiveApiSecretKey);
                if (string.IsNullOrEmpty(idCol) && string.IsNullOrEmpty(secretCol)) continue;

                var entry = vault.AlpacaEntry(row.UserId, isPaper);
                var name = isPaper ? "alpaca-paper" : "alpaca-live";
                if (!TryDecrypt(idCol, out var keyId) || !TryDecrypt(secretCol, out var secret))
                {
                    items.Add(new(row.UserId, name, entry, VaultMigrationAction.Undecryptable, false));
                    continue;
                }

                var (vaultId, vaultSecret) = vault.GetAlpaca(row.UserId, isPaper);
                VaultMigrationAction action;
                if (string.IsNullOrWhiteSpace(keyId) || string.IsNullOrWhiteSpace(secret))
                    action = VaultMigrationAction.AlreadyInVault; // a half pair was never usable; nothing to keep
                else if (vaultId is null)
                    action = VaultMigrationAction.CopyToVault;
                else if (vaultId == keyId.Trim() && vaultSecret == secret.Trim())
                    action = VaultMigrationAction.AlreadyInVault;
                else
                    action = VaultMigrationAction.Conflict;

                var done = false;
                if (apply && action is not VaultMigrationAction.Conflict)
                {
                    if (action is VaultMigrationAction.CopyToVault)
                    {
                        vault.SetAlpaca(row.UserId, isPaper, keyId!, secret!);
                        var (backId, backSecret) = vault.GetAlpaca(row.UserId, isPaper);
                        if (backId != keyId!.Trim() || backSecret != secret!.Trim())
                            throw new InvalidOperationException($"Vault read-back of {entry} did not match; nothing was cleared for user {row.UserId}.");
                    }
                    if (isPaper) { row.AlpacaApiKeyId = null; row.AlpacaApiSecretKey = null; }
                    else { row.AlpacaLiveApiKeyId = null; row.AlpacaLiveApiSecretKey = null; }
                    changed = done = true;
                }
                items.Add(new(row.UserId, name, entry, action, done));
            }

            foreach (var token in new[] { AccountToken.Claude, AccountToken.AlpacaOAuthAccess, AccountToken.AlpacaOAuthRefresh })
            {
                var column = token switch
                {
                    AccountToken.Claude => row.ClaudeApiKey,
                    AccountToken.AlpacaOAuthAccess => row.AlpacaOAuthAccessToken,
                    _ => row.AlpacaOAuthRefreshToken,
                };
                if (string.IsNullOrEmpty(column)) continue;

                var entry = AccountSecretVault.TokenEntryName(row.UserId, token);
                var name = token switch
                {
                    AccountToken.Claude => "claude",
                    AccountToken.AlpacaOAuthAccess => "alpaca-oauth-access",
                    _ => "alpaca-oauth-refresh",
                };
                if (!TryDecrypt(column, out var value))
                {
                    items.Add(new(row.UserId, name, entry, VaultMigrationAction.Undecryptable, false));
                    continue;
                }

                var current = vault.GetToken(row.UserId, token);
                var action = string.IsNullOrWhiteSpace(value) ? VaultMigrationAction.AlreadyInVault
                    : current is null ? VaultMigrationAction.CopyToVault
                    : current == value.Trim() ? VaultMigrationAction.AlreadyInVault
                    : VaultMigrationAction.Conflict;

                var done = false;
                if (apply && action is not VaultMigrationAction.Conflict)
                {
                    if (action is VaultMigrationAction.CopyToVault)
                    {
                        vault.SetToken(row.UserId, token, value);
                        if (vault.GetToken(row.UserId, token) != value!.Trim())
                            throw new InvalidOperationException($"Vault read-back of {entry} did not match; nothing was cleared for user {row.UserId}.");
                    }
                    switch (token)
                    {
                        case AccountToken.Claude: row.ClaudeApiKey = null; break;
                        case AccountToken.AlpacaOAuthAccess: row.AlpacaOAuthAccessToken = null; break;
                        default: row.AlpacaOAuthRefreshToken = null; break;
                    }
                    changed = done = true;
                }
                items.Add(new(row.UserId, name, entry, action, done));
            }

            if (changed) cleared++;
        }

        if (apply && cleared > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Vault migration cleared legacy secret columns on {Rows} UserApiKeys row(s).", cleared);
        }
        return new VaultMigrationReport(apply, items, apply ? cleared : 0);
    }

    private bool TryDecrypt(string? ciphertext, out string? value)
    {
        try
        {
            value = userKeys.UnprotectLegacy(ciphertext);
            return true;
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or FormatException)
        {
            value = null;
            return false;
        }
    }
}
