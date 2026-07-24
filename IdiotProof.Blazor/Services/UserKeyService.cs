using IdiotProof.Blazor.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IdiotProof.Blazor.Services;

/// <summary>
/// Loads and saves per-user API keys, encrypting sensitive fields with IDataProtector.
/// </summary>
public sealed class UserKeyService(
    IDbContextFactory<AppDbContext> dbFactory,
    IDataProtectionProvider dpProvider,
    ILogger<UserKeyService> logger)
{
    private readonly IDataProtector protector = dpProvider.CreateProtector("IdiotProof.UserApiKeys.v1");

    public async Task<UserApiKeys> GetOrCreateAsync(Guid userId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.UserApiKeys.FirstOrDefaultAsync(k => k.UserId == userId, ct);
        if (row is null)
            return new UserApiKeys { UserId = userId };

        return Decrypt(row);
    }

    public async Task SaveAsync(Guid userId, UserApiKeys keys, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.UserApiKeys.FirstOrDefaultAsync(k => k.UserId == userId, ct);

        var encrypted = Encrypt(keys);
        encrypted.UserId = userId;

        if (row is null)
        {
            db.UserApiKeys.Add(encrypted);
        }
        else
        {
            encrypted.Id = row.Id;
            db.Entry(row).CurrentValues.SetValues(encrypted);
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Returns all users who have at least one real data feed key configured.
    /// Used by the strategy execution service to find active users.
    /// </summary>
    public async Task<List<(Guid UserId, UserApiKeys Keys)>> GetAllActiveUsersAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await db.UserApiKeys.ToListAsync(ct);
        return rows
            .Select(r => (r.UserId, Decrypt(r)))
            .ToList();
    }

    private UserApiKeys Encrypt(UserApiKeys k) => new()
    {
        UserId             = k.UserId,
        AlpacaApiKeyId     = Protect(k.AlpacaApiKeyId),
        AlpacaApiSecretKey = Protect(k.AlpacaApiSecretKey),
        AlpacaIsPaper      = k.AlpacaIsPaper,
        AlpacaLiveApiKeyId     = Protect(k.AlpacaLiveApiKeyId),
        AlpacaLiveApiSecretKey = Protect(k.AlpacaLiveApiSecretKey),
        ClaudeApiKey       = Protect(k.ClaudeApiKey),
        LlmVotingEnabled   = k.LlmVotingEnabled,
        ClaudeModel        = k.ClaudeModel,
        DefaultBroker      = k.DefaultBroker,
        DefaultDataFeed    = k.DefaultDataFeed,
        AlpacaOAuthAccessToken  = Protect(k.AlpacaOAuthAccessToken),
        AlpacaOAuthRefreshToken = Protect(k.AlpacaOAuthRefreshToken),
        AlpacaOAuthScope        = k.AlpacaOAuthScope
    };

    private UserApiKeys Decrypt(UserApiKeys k) => new()
    {
        Id                 = k.Id,
        UserId             = k.UserId,
        AlpacaApiKeyId     = Unprotect(k.AlpacaApiKeyId, k.UserId, nameof(k.AlpacaApiKeyId)),
        AlpacaApiSecretKey = Unprotect(k.AlpacaApiSecretKey, k.UserId, nameof(k.AlpacaApiSecretKey)),
        AlpacaIsPaper      = k.AlpacaIsPaper,
        AlpacaLiveApiKeyId     = Unprotect(k.AlpacaLiveApiKeyId, k.UserId, nameof(k.AlpacaLiveApiKeyId)),
        AlpacaLiveApiSecretKey = Unprotect(k.AlpacaLiveApiSecretKey, k.UserId, nameof(k.AlpacaLiveApiSecretKey)),
        ClaudeApiKey       = Unprotect(k.ClaudeApiKey, k.UserId, nameof(k.ClaudeApiKey)),
        LlmVotingEnabled   = k.LlmVotingEnabled,
        ClaudeModel        = k.ClaudeModel,
        DefaultBroker      = k.DefaultBroker,
        DefaultDataFeed    = k.DefaultDataFeed,
        AlpacaOAuthAccessToken  = Unprotect(k.AlpacaOAuthAccessToken, k.UserId, nameof(k.AlpacaOAuthAccessToken)),
        AlpacaOAuthRefreshToken = Unprotect(k.AlpacaOAuthRefreshToken, k.UserId, nameof(k.AlpacaOAuthRefreshToken)),
        AlpacaOAuthScope        = k.AlpacaOAuthScope
    };

    private string? Protect(string? value) =>
        string.IsNullOrEmpty(value) ? value : protector.Protect(value);

    private string? Unprotect(string? value, Guid userId, string field)
    {
        if (string.IsNullOrEmpty(value)) return value;
        try { return protector.Unprotect(value); }
        catch (Exception ex)
        {
            // A stored key that no longer decrypts is a routing hazard, not a
            // cosmetic blank: the UI renders an empty field (looks like
            // "never configured") while UserBrokerResolver silently falls
            // back to the global broker — a user who thinks live trading
            // routes to THEIR Alpaca account is trading a different one. The
            // usual cause is a DataProtection key-ring mismatch between the
            // Blazor host and the Monitor (DataProtection:KeyRingPath must
            // point both processes at the same directory).
            logger.LogError(ex,
                "Failed to decrypt {Field} for user {UserId} — DataProtection key-ring mismatch? " +
                "Broker routing will fall back to the global default until the key is re-entered.",
                field, userId);
            return null;
        }
    }
}
