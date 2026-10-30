using System.Collections.Concurrent;
using IdiotProof.Shared.Risk;
using Microsoft.EntityFrameworkCore;
using IdiotProof.Blazor.Data;

namespace IdiotProof.Blazor.Services;

/// <summary>
/// Per-user RiskGuardian cache. The Monitor calls
/// <see cref="GetForUserAsync"/> once per signal evaluation; the service
/// either returns a cached <see cref="RiskGuardian"/> instance or builds a
/// fresh one from the user's <see cref="UserPreferences"/> risk fields.
///
/// Why instance-cached: <see cref="RiskGuardian"/> tracks <c>dailyLoss</c>
/// in memory across calls (so the daily circuit breaker actually trips).
/// Re-instantiating per signal would reset that tracker and let users blow
/// past their daily cap. The cache lifetime is the host process — daily
/// loss naturally rolls at the ET trading-day boundary inside the Guardian
/// itself, no external reset required.
///
/// <see cref="InvalidateAsync"/> is called by the Settings page after a
/// risk-config change so the next signal picks up the new limits.
/// </summary>
public sealed class RiskGuardianService(IDbContextFactory<AppDbContext> dbFactory)
{
    /// <summary>
    /// How long a cached Guardian's CONFIG is trusted before it is re-read
    /// from SQL. The Guardian instance itself is never replaced (its daily
    /// loss counter must survive) — only its limits are refreshed via
    /// <see cref="RiskGuardian.UpdateConfig"/>. Without this, the Monitor —
    /// a separate process from the Blazor UI — never saw risk-config edits
    /// until restart: the UI's Invalidate() only clears the UI process's own
    /// cache.
    /// </summary>
    private static readonly TimeSpan ConfigTtl = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Cached Guardians keyed by user id. ConcurrentDictionary because the
    /// Monitor evaluates strategies in parallel and may hit two strategies
    /// from the same user back-to-back.
    /// </summary>
    private readonly ConcurrentDictionary<Guid, CacheEntry> cache = new();

    private sealed class CacheEntry(RiskGuardian guardian, DateTime configLoadedUtc)
    {
        public RiskGuardian Guardian { get; } = guardian;
        public DateTime ConfigLoadedUtc { get; set; } = configLoadedUtc;
    }

    /// <summary>
    /// Resolves the Guardian for the given user. First call hits SQL to load
    /// the user's risk config; subsequent calls return the cached instance
    /// so the in-memory daily-loss counter is preserved. When the cached
    /// config is older than <see cref="ConfigTtl"/>, the limits are re-read
    /// and swapped in on the SAME instance.
    /// </summary>
    public async Task<RiskGuardian> GetForUserAsync(Guid userId, CancellationToken ct = default)
    {
        if (cache.TryGetValue(userId, out var hit))
        {
            if (DateTime.UtcNow - hit.ConfigLoadedUtc > ConfigTtl)
            {
                hit.Guardian.UpdateConfig(await LoadConfigAsync(userId, ct));
                hit.ConfigLoadedUtc = DateTime.UtcNow;
            }
            return hit.Guardian;
        }

        var config = await LoadConfigAsync(userId, ct);
        var entry = cache.GetOrAdd(userId, new CacheEntry(new RiskGuardian(config), DateTime.UtcNow));
        return entry.Guardian;
    }

    /// <summary>
    /// Marks the user's cached config stale — the next
    /// <see cref="GetForUserAsync"/> call re-reads it from SQL. The entry is
    /// expired, NOT removed: dropping it would discard the Guardian instance
    /// and silently reset its in-memory daily-loss counter, the exact hazard
    /// <see cref="RiskGuardian.UpdateConfig"/> exists to avoid.
    /// </summary>
    public void Invalidate(Guid userId)
    {
        if (cache.TryGetValue(userId, out var entry))
            entry.ConfigLoadedUtc = DateTime.MinValue;
    }

    /// <summary>
    /// Loads the user's <see cref="UserPreferences"/> and projects it onto a
    /// <see cref="RiskGuardianConfig"/>. Missing user → canonical defaults.
    /// </summary>
    private async Task<RiskGuardianConfig> LoadConfigAsync(Guid userId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var prefs = await db.UserPreferences.FirstOrDefaultAsync(p => p.UserId == userId, ct);
        if (prefs is null) return new RiskGuardianConfig();

        return new RiskGuardianConfig
        {
            MaxLossPerTrade        = prefs.RiskMaxLossPerTrade,
            MaxLossPerDay          = prefs.RiskMaxLossPerDay,
            MinStopLossPercent     = prefs.RiskMinStopLossPercent,
            MaxStopLossPercent     = prefs.RiskMaxStopLossPercent,
            AccountBalance         = prefs.RiskAccountBalance,
            MaxAccountRiskPercent  = prefs.RiskMaxAccountRiskPercent,
        };
    }
}
