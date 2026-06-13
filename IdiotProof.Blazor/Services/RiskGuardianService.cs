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
    /// Cached Guardians keyed by user id. ConcurrentDictionary because the
    /// Monitor evaluates strategies in parallel and may hit two strategies
    /// from the same user back-to-back.
    /// </summary>
    private readonly ConcurrentDictionary<Guid, RiskGuardian> cache = new();

    /// <summary>
    /// Resolves the Guardian for the given user. First call hits SQL to load
    /// the user's risk config; subsequent calls return the cached instance
    /// so the in-memory daily-loss counter is preserved.
    /// </summary>
    public async Task<RiskGuardian> GetForUserAsync(Guid userId, CancellationToken ct = default)
    {
        if (cache.TryGetValue(userId, out var existing))
            return existing;

        var config = await LoadConfigAsync(userId, ct);
        var guardian = new RiskGuardian(config);
        return cache.GetOrAdd(userId, guardian);
    }

    /// <summary>
    /// Drops any cached Guardian for the user — the next
    /// <see cref="GetForUserAsync"/> call will re-read the config from SQL.
    /// </summary>
    public void Invalidate(Guid userId) => cache.TryRemove(userId, out _);

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
