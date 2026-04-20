using IdiotProof.Frontend.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Frontend.Services;

/// <summary>
/// Loads and saves per-user API keys, encrypting sensitive fields with IDataProtector.
/// </summary>
public sealed class UserKeyService(IDbContextFactory<AppDbContext> dbFactory, IDataProtectionProvider dpProvider)
{
    private readonly IDataProtector protector = dpProvider.CreateProtector("IdiotProof.UserApiKeys.v1");

    public async Task<UserApiKeys> GetOrCreateAsync(string userId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.UserApiKeys.FirstOrDefaultAsync(k => k.UserId == userId, ct);
        if (row is null)
            return new UserApiKeys { UserId = userId };

        return Decrypt(row);
    }

    public async Task SaveAsync(string userId, UserApiKeys keys, CancellationToken ct = default)
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
    public async Task<List<(string UserId, UserApiKeys Keys)>> GetAllActiveUsersAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await db.UserApiKeys.ToListAsync(ct);
        return rows
            .Select(r => (r.UserId, Decrypt(r)))
            .ToList();
    }

    private UserApiKeys Encrypt(UserApiKeys k) => new()
    {
        UserId          = k.UserId,
        AlpacaApiKeyId  = Protect(k.AlpacaApiKeyId),
        AlpacaApiSecretKey = Protect(k.AlpacaApiSecretKey),
        AlpacaIsPaper   = k.AlpacaIsPaper,
        PolygonApiKey   = Protect(k.PolygonApiKey),
        ClaudeApiKey    = Protect(k.ClaudeApiKey),
        LlmVotingEnabled = k.LlmVotingEnabled,
        ClaudeModel     = k.ClaudeModel,
        IbkrHost        = k.IbkrHost,
        IbkrLivePort    = k.IbkrLivePort,
        IbkrPaperPort   = k.IbkrPaperPort,
        IbkrClientId    = k.IbkrClientId,
        IbkrUsePaper    = k.IbkrUsePaper,
        DefaultBroker   = k.DefaultBroker,
        DefaultDataFeed = k.DefaultDataFeed
    };

    private UserApiKeys Decrypt(UserApiKeys k) => new()
    {
        Id              = k.Id,
        UserId          = k.UserId,
        AlpacaApiKeyId  = Unprotect(k.AlpacaApiKeyId),
        AlpacaApiSecretKey = Unprotect(k.AlpacaApiSecretKey),
        AlpacaIsPaper   = k.AlpacaIsPaper,
        PolygonApiKey   = Unprotect(k.PolygonApiKey),
        ClaudeApiKey    = Unprotect(k.ClaudeApiKey),
        LlmVotingEnabled = k.LlmVotingEnabled,
        ClaudeModel     = k.ClaudeModel,
        IbkrHost        = k.IbkrHost,
        IbkrLivePort    = k.IbkrLivePort,
        IbkrPaperPort   = k.IbkrPaperPort,
        IbkrClientId    = k.IbkrClientId,
        IbkrUsePaper    = k.IbkrUsePaper,
        DefaultBroker   = k.DefaultBroker,
        DefaultDataFeed = k.DefaultDataFeed
    };

    private string? Protect(string? value) =>
        string.IsNullOrEmpty(value) ? value : protector.Protect(value);

    private string? Unprotect(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        try { return protector.Unprotect(value); }
        catch { return null; }
    }
}
