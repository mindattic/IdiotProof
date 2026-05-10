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
    public async Task<UserPreferences> GetOrCreateAsync(string userId, CancellationToken ct = default)
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
        await db.SaveChangesAsync(ct);
        return row;
    }

    public async Task SaveAsync(UserPreferences prefs, CancellationToken ct = default)
    {
        prefs.UpdatedUtc = DateTime.UtcNow;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.UserPreferences.Update(prefs);
        await db.SaveChangesAsync(ct);
    }

    public async Task SetThemeAsync(string userId, string theme, CancellationToken ct = default)
    {
        var p = await GetOrCreateAsync(userId, ct);
        p.Theme = theme;
        await SaveAsync(p, ct);
    }

    public async Task SetActiveAccountAsync(string userId, string accountId, string accountType, CancellationToken ct = default)
    {
        var p = await GetOrCreateAsync(userId, ct);
        p.ActiveAccountId   = accountId;
        p.ActiveAccountType = accountType;
        await SaveAsync(p, ct);
    }

    /// <summary>
    /// Adds <paramref name="tabKey"/> to the OpenStrategyTabs list if it's not
    /// already there. Tab keys are either a strategy guid (for an existing
    /// strategy being edited) or the literal string "new" for a blank draft.
    /// CSV-encoded; preserves order so the tab bar renders left-to-right in
    /// open order.
    /// </summary>
    public async Task<List<string>> AddOpenTabAsync(string userId, string tabKey, CancellationToken ct = default)
    {
        var prefs = await GetOrCreateAsync(userId, ct);
        var tabs = ParseTabs(prefs.OpenStrategyTabs);
        if (!tabs.Contains(tabKey)) tabs.Add(tabKey);
        prefs.OpenStrategyTabs = string.Join(",", tabs);
        await SaveAsync(prefs, ct);
        return tabs;
    }

    /// <summary>Removes a tab from the list. No-op when the key isn't open.</summary>
    public async Task<List<string>> RemoveOpenTabAsync(string userId, string tabKey, CancellationToken ct = default)
    {
        var prefs = await GetOrCreateAsync(userId, ct);
        var tabs = ParseTabs(prefs.OpenStrategyTabs);
        tabs.RemoveAll(t => t == tabKey);
        prefs.OpenStrategyTabs = string.Join(",", tabs);
        await SaveAsync(prefs, ct);
        return tabs;
    }

    /// <summary>Returns the open tabs as a typed list. Empty when none.</summary>
    public async Task<List<string>> GetOpenTabsAsync(string userId, CancellationToken ct = default)
    {
        var prefs = await GetOrCreateAsync(userId, ct);
        return ParseTabs(prefs.OpenStrategyTabs);
    }

    private static List<string> ParseTabs(string csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? new List<string>()
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    /// <summary>
    /// Updates the risk-guardian fields. Validates the basic sanity invariants
    /// (positive amounts, max ≥ min stop %, daily ≥ per-trade) so callers can
    /// trust the persisted row without re-checking. Returns the saved row so
    /// the caller can read clamp adjustments back.
    /// </summary>
    public async Task<UserPreferences> SetRiskConfigAsync(
        string userId,
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
