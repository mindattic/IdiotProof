using IdiotProof.Blazor.Data;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Blazor.Services;

/// <summary>
/// Read/write per-user UI state. Canonical source is SQL; the Blazor UI is
/// expected to also mirror these values to <c>localStorage</c> via JS interop
/// so SSR can pre-paint with the user's chosen theme + active account before
/// the server renders.
/// </summary>
public sealed class UserPreferencesService(IDbContextFactory<AppDbContext> dbFactory)
{
    /// <summary>
    /// Returns the row for <paramref name="userId"/>, creating defaults if absent.
    /// Defaults: theme=alpaca, paper account, no open tabs.
    /// </summary>
    public async Task<UserPreferences> GetOrCreateAsync(Guid userId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.UserPreferences.FirstOrDefaultAsync(p => p.UserId == userId, ct);
        if (row is not null) return row;

        row = new UserPreferences
        {
            UserId            = userId,
            Theme             = "alpaca",
            ActiveAccountId   = "",
            ActiveAccountType = "paper",
            OpenStrategyTabs  = "",
            UiStateJson       = "{}",
            UpdatedUtc        = DateTime.UtcNow,
        };
        db.UserPreferences.Add(row);
        try
        {
            await db.SaveChangesAsync(ct);
            return row;
        }
        catch (DbUpdateException)
        {
            // Two concurrent first requests for a brand-new user can both see
            // no row and both insert; UserId is the PK so the loser lands
            // here. Return the winner's row instead of surfacing the crash.
            db.Entry(row).State = EntityState.Detached;
            return await db.UserPreferences.FirstAsync(p => p.UserId == userId, ct);
        }
    }

    public async Task SaveAsync(UserPreferences prefs, CancellationToken ct = default)
    {
        prefs.UpdatedUtc = DateTime.UtcNow;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        // Reload the tracked row and copy changed values onto it. Using Update()
        // on a detached entity marks ALL columns Modified, so a concurrent
        // SetThemeAsync and SetRiskConfigAsync would each overwrite the other's
        // columns. SetValues only generates UPDATE for columns that differ from
        // what EF loaded, preventing the lost-update.
        var existing = await db.UserPreferences.FirstOrDefaultAsync(p => p.UserId == prefs.UserId, ct);
        if (existing is null)
            db.UserPreferences.Add(prefs);
        else
            db.Entry(existing).CurrentValues.SetValues(prefs);
        await db.SaveChangesAsync(ct);
    }

    public async Task SetThemeAsync(Guid userId, string theme, CancellationToken ct = default)
    {
        var p = await GetOrCreateAsync(userId, ct);
        p.Theme = theme;
        await SaveAsync(p, ct);
    }

    public async Task SetActiveAccountAsync(Guid userId, string accountId, string accountType, CancellationToken ct = default)
    {
        var p = await GetOrCreateAsync(userId, ct);
        p.ActiveAccountId   = accountId;
        p.ActiveAccountType = accountType;
        await SaveAsync(p, ct);
    }

    // NOTE (IP-A10): the AddOpenTabAsync/RemoveOpenTabAsync/GetOpenTabsAsync
    // trio was removed — it fed OpenStrategyTabs for a "BuilderTabBar" that
    // was never built, so the CSV column grew forever with nothing ever
    // reading it back (2026-07-18 audit, H6). The column itself is scheduled
    // for removal with the next schema migration (BIBLE §7).

    /// <summary>
    /// Updates the risk-guardian fields. Validates the basic sanity invariants
    /// (positive amounts, max ≥ min stop %, daily ≥ per-trade) so callers can
    /// trust the persisted row without re-checking. Returns the saved row so
    /// the caller can read clamp adjustments back.
    /// </summary>
    public async Task<UserPreferences> SetRiskConfigAsync(
        Guid userId,
        decimal maxLossPerTrade,
        decimal maxLossPerDay,
        decimal minStopPct,
        decimal maxStopPct,
        decimal accountBalance,
        decimal maxAccountRiskPercent,
        CancellationToken ct = default)
    {
        var p = await GetOrCreateAsync(userId, ct);

        p.RiskMaxLossPerTrade        = Math.Max(0m, maxLossPerTrade);
        p.RiskMaxLossPerDay          = Math.Max(p.RiskMaxLossPerTrade, maxLossPerDay);
        p.RiskMinStopLossPercent     = Math.Clamp(minStopPct, 0.01m, 50m);
        p.RiskMaxStopLossPercent     = Math.Max(p.RiskMinStopLossPercent, maxStopPct);
        p.RiskAccountBalance         = Math.Max(0m, accountBalance);
        p.RiskMaxAccountRiskPercent  = Math.Clamp(maxAccountRiskPercent, 0m, 100m);

        await SaveAsync(p, ct);
        return p;
    }
}
