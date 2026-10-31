using IdiotProof.Blazor.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IdiotProof.Blazor.Services;

/// <summary>
/// Loads and saves a user's broker/LLM settings. Since IP-T330 the secrets (Alpaca paper/live
/// pairs, Alpaca OAuth tokens, the Claude key) live only in MindAttic.Vault through
/// <see cref="AccountSecretVault"/>; the <c>UserApiKeys</c> row keeps only the non-secret choices
/// (routing opt-in, data feed, Claude model, voting toggle, OAuth scope); nothing here ever writes a
/// secret column. Reads never fall back to SQL: a secret still sitting in a legacy column is
/// invisible until <see cref="VaultSecretMigration"/> (<c>idiotproof keys migrate-to-vault</c>)
/// moves it and clears the column.
/// </summary>
public sealed class UserKeyService(
    IDbContextFactory<AppDbContext> dbFactory,
    IDataProtectionProvider dpProvider,
    AccountSecretVault vault,
    ILogger<UserKeyService> logger)
{
    /// <summary>The DataProtection purpose the legacy SQL secret columns were encrypted under.</summary>
    public const string ProtectorPurpose = "IdiotProof.UserApiKeys.v1";

    private readonly IDataProtector protector = dpProvider.CreateProtector(ProtectorPurpose);

    /// <summary>The Vault this service reads and writes.</summary>
    public AccountSecretVault Vault => vault;

    /// <summary>The user's settings with secrets filled from Vault (a defaults object when the user has no row).</summary>
    public async Task<UserApiKeys> GetOrCreateAsync(Guid userId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.UserApiKeys.AsNoTracking().FirstOrDefaultAsync(k => k.UserId == userId, ct);
        var keys = row is null ? new UserApiKeys { UserId = userId } : NonSecret(row);
        FillFromVault(keys);
        return keys;
    }

    /// <summary>Whether Vault holds a complete paper / live Alpaca pair for this user.</summary>
    public Task<(bool PaperKeyStored, bool LiveKeyStored)> GetKeyPresenceAsync(Guid userId, CancellationToken ct = default)
    {
        var (pk, ps) = vault.GetAlpaca(userId, isPaper: true);
        var (lk, ls) = vault.GetAlpaca(userId, isPaper: false);
        return Task.FromResult((pk is not null && ps is not null, lk is not null && ls is not null));
    }

    /// <summary>
    /// Saves the whole settings object: secrets to Vault (a blank secret removes it; a half
    /// Alpaca pair is refused), everything else to the SQL row, whose secret columns are nulled.
    /// </summary>
    public async Task SaveAsync(Guid userId, UserApiKeys keys, CancellationToken ct = default)
    {
        WritePair(userId, isPaper: true, keys.AlpacaApiKeyId, keys.AlpacaApiSecretKey);
        WritePair(userId, isPaper: false, keys.AlpacaLiveApiKeyId, keys.AlpacaLiveApiSecretKey);
        vault.SetToken(userId, AccountToken.Claude, keys.ClaudeApiKey);
        vault.SetToken(userId, AccountToken.AlpacaOAuthAccess, keys.AlpacaOAuthAccessToken);
        vault.SetToken(userId, AccountToken.AlpacaOAuthRefresh, keys.AlpacaOAuthRefreshToken);
        await SavePreferencesAsync(userId, keys, ct);
    }

    /// <summary>
    /// Saves only the non-secret columns (routing opt-in, feed, model, voting, scope). Legacy secret
    /// columns are left exactly as they are: only <see cref="VaultSecretMigration"/> clears them,
    /// and only after it has verified Vault holds the same value.
    /// </summary>
    public async Task SavePreferencesAsync(Guid userId, UserApiKeys keys, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.UserApiKeys.FirstOrDefaultAsync(k => k.UserId == userId, ct);
        if (row is null)
        {
            row = new UserApiKeys { UserId = userId };
            db.UserApiKeys.Add(row);
        }
        row.LlmVotingEnabled = keys.LlmVotingEnabled;
        row.ClaudeModel      = keys.ClaudeModel;
        row.DefaultBroker    = keys.DefaultBroker;
        row.DefaultDataFeed  = keys.DefaultDataFeed;
        row.AlpacaOAuthScope = keys.AlpacaOAuthScope;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Sets or clears just the <c>DefaultBroker</c> routing opt-in ("alpaca" or "Sandbox").</summary>
    public async Task SetDefaultBrokerAsync(Guid userId, string defaultBroker, CancellationToken ct = default)
    {
        var keys = await GetOrCreateAsync(userId, ct);
        keys.DefaultBroker = defaultBroker;
        await SavePreferencesAsync(userId, keys, ct);
    }

    /// <summary>Every user with a settings row, secrets filled from Vault.</summary>
    public async Task<List<(Guid UserId, UserApiKeys Keys)>> GetAllActiveUsersAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await db.UserApiKeys.AsNoTracking().ToListAsync(ct);
        return rows.Select(r =>
        {
            var k = NonSecret(r);
            FillFromVault(k);
            return (r.UserId, k);
        }).ToList();
    }

    /// <summary>Which legacy SQL secret columns still hold ciphertext for this user (names only, never values).</summary>
    public async Task<IReadOnlyList<string>> GetLegacyDbSecretColumnsAsync(Guid userId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.UserApiKeys.AsNoTracking().FirstOrDefaultAsync(k => k.UserId == userId, ct);
        return row is null ? [] : LegacySecretColumns(row);
    }

    /// <summary>The names of the secret columns that are non-empty on <paramref name="row"/>.</summary>
    public static IReadOnlyList<string> LegacySecretColumns(UserApiKeys row)
    {
        var names = new List<string>();
        if (!string.IsNullOrEmpty(row.AlpacaApiKeyId)) names.Add(nameof(row.AlpacaApiKeyId));
        if (!string.IsNullOrEmpty(row.AlpacaApiSecretKey)) names.Add(nameof(row.AlpacaApiSecretKey));
        if (!string.IsNullOrEmpty(row.AlpacaLiveApiKeyId)) names.Add(nameof(row.AlpacaLiveApiKeyId));
        if (!string.IsNullOrEmpty(row.AlpacaLiveApiSecretKey)) names.Add(nameof(row.AlpacaLiveApiSecretKey));
        if (!string.IsNullOrEmpty(row.ClaudeApiKey)) names.Add(nameof(row.ClaudeApiKey));
        if (!string.IsNullOrEmpty(row.AlpacaOAuthAccessToken)) names.Add(nameof(row.AlpacaOAuthAccessToken));
        if (!string.IsNullOrEmpty(row.AlpacaOAuthRefreshToken)) names.Add(nameof(row.AlpacaOAuthRefreshToken));
        return names;
    }

    /// <summary>
    /// Decrypts one legacy SQL secret column (for <see cref="VaultSecretMigration"/> only).
    /// Null when empty; throws when the ciphertext no longer decrypts.
    /// </summary>
    internal string? UnprotectLegacy(string? value) =>
        string.IsNullOrEmpty(value) ? null : protector.Unprotect(value);

    /// <summary>Encrypts a value the way the legacy columns were written (tests seed legacy rows with it).</summary>
    public string ProtectLegacy(string value) => protector.Protect(value);

    private void WritePair(Guid userId, bool isPaper, string? keyId, string? secret)
    {
        var hasId = !string.IsNullOrWhiteSpace(keyId);
        var hasSecret = !string.IsNullOrWhiteSpace(secret);
        if (hasId && hasSecret)
        {
            var (curId, curSecret) = vault.GetAlpaca(userId, isPaper);
            if (curId != keyId!.Trim() || curSecret != secret!.Trim()) vault.SetAlpaca(userId, isPaper, keyId, secret!);
        }
        else if (!hasId && !hasSecret)
        {
            if (vault.RemoveAlpaca(userId, isPaper))
                logger.LogInformation("Removed the {Mode} Alpaca pair from Vault for user {UserId}.", isPaper ? "paper" : "live", userId);
        }
        else
        {
            throw new ArgumentException($"The {(isPaper ? "paper" : "live")} Alpaca pair needs both the key id and the secret.");
        }
    }

    private void FillFromVault(UserApiKeys keys)
    {
        (keys.AlpacaApiKeyId, keys.AlpacaApiSecretKey) = vault.GetAlpaca(keys.UserId, isPaper: true);
        (keys.AlpacaLiveApiKeyId, keys.AlpacaLiveApiSecretKey) = vault.GetAlpaca(keys.UserId, isPaper: false);
        keys.ClaudeApiKey = vault.GetToken(keys.UserId, AccountToken.Claude);
        keys.AlpacaOAuthAccessToken = vault.GetToken(keys.UserId, AccountToken.AlpacaOAuthAccess);
        keys.AlpacaOAuthRefreshToken = vault.GetToken(keys.UserId, AccountToken.AlpacaOAuthRefresh);
    }

    /// <summary>A copy carrying only the non-secret columns (every secret column null).</summary>
    private static UserApiKeys NonSecret(UserApiKeys k) => new()
    {
        Id               = k.Id,
        UserId           = k.UserId,
        LlmVotingEnabled = k.LlmVotingEnabled,
        ClaudeModel      = k.ClaudeModel,
        DefaultBroker    = k.DefaultBroker,
        DefaultDataFeed  = k.DefaultDataFeed,
        AlpacaOAuthScope = k.AlpacaOAuthScope,
    };
}
