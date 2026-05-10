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
}
