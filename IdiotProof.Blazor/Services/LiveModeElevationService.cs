using System.Collections.Concurrent;
using IdiotProof.Blazor.Data;
using Microsoft.EntityFrameworkCore;
using MindAttic.Authentication.Crypto;
using MindAttic.Authentication.Secrets;

namespace IdiotProof.Blazor.Services;

/// <summary>
/// Tracks which users have an active "elevated session" — the 5-minute window
/// that opens after successfully re-entering their password, allowing Paper →
/// Live broker-mode promotions. The elevation is in-process only (not persisted)
/// so a server restart requires re-authentication before the next live promotion.
/// </summary>
public sealed class LiveModeElevationService(
    IDbContextFactory<AppDbContext> dbFactory,
    IPasswordHasher passwordHasher,
    IAuthSecrets authSecrets,
    ILogger<LiveModeElevationService> logger)
{
    private static readonly TimeSpan WindowDuration = TimeSpan.FromMinutes(5);
    private readonly ConcurrentDictionary<Guid, DateTime> elevatedUntil = new();

    public bool IsElevated(Guid userId) =>
        elevatedUntil.TryGetValue(userId, out var expiry) && DateTime.UtcNow < expiry;

    public void Elevate(Guid userId) =>
        elevatedUntil[userId] = DateTime.UtcNow.Add(WindowDuration);

    public void Revoke(Guid userId) =>
        elevatedUntil.TryRemove(userId, out _);

    public TimeSpan? RemainingWindow(Guid userId)
    {
        if (!elevatedUntil.TryGetValue(userId, out var expiry)) return null;
        var remaining = expiry - DateTime.UtcNow;
        return remaining > TimeSpan.Zero ? remaining : null;
    }

    /// <summary>
    /// Verifies the user's current password. Uses the stored Argon2id hash from
    /// the auth database and the pepper from MindAttic.Vault.
    /// </summary>
    public async Task<bool> VerifyPasswordAsync(Guid userId, string password, CancellationToken ct = default)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var user = await db.AuthUsers.FindAsync([userId], ct);
            if (user?.PasswordHash is null) return false;

            // Pepper key id is embedded in the PHC hash string; try v1 and v2.
            foreach (var keyId in new[] { "v1", "v2" })
            {
                var pepper = authSecrets.GetOptional($"pepper.{keyId}");
                if (pepper is null) continue;
                var result = passwordHasher.Verify(user.PasswordHash, pepper, keyId, password);
                if (result.Succeeded) return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Password verification failed for user {UserId}", userId);
            return false;
        }
    }
}
