using System.Collections.Concurrent;
using IdiotProof.Blazor.Data;
using Microsoft.EntityFrameworkCore;
using MindAttic.Authentication.Crypto;

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
    ILogger<LiveModeElevationService> logger)
{
    private static readonly TimeSpan WindowDuration = TimeSpan.FromMinutes(5);
    private readonly ConcurrentDictionary<Guid, DateTime> elevatedUntil = new();

    public bool IsElevated(Guid userId) =>
        elevatedUntil.TryGetValue(userId, out var expiry) && DateTime.UtcNow < expiry;

    public void Elevate(Guid userId)
    {
        var now = DateTime.UtcNow;
        foreach (var key in elevatedUntil.Keys)
            if (elevatedUntil.TryGetValue(key, out var exp) && now >= exp)
                elevatedUntil.TryRemove(key, out _);
        elevatedUntil[userId] = now.Add(WindowDuration);
    }

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

            // PasswordPepperKeyId is a separate column, not embedded in the PHC string —
            // Hash() returns { Phc, PepperKeyId } as two distinct values, so both must be
            // stored and both must round-trip back into Verify (passing null here made
            // every verification fail, since it resolves to the wrong/no pepper).
            var result = passwordHasher.Verify(password, user.PasswordHash, user.PasswordPepperKeyId, user.LegacyHashScheme);
            return result.Succeeded;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Password verification failed for user {UserId}", userId);
            return false;
        }
    }
}
