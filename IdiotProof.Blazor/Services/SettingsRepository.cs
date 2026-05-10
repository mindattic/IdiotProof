using System.Globalization;
using IdiotProof.Blazor.Data;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Blazor.Services;

/// <summary>
/// Thin wrapper over the SettingsKv table providing typed get/set helpers for
/// runtime-editable application settings. The Settings page binds to this
/// repository; the rest of the engine continues reading the typed
/// <c>AppSettings</c> snapshot loaded at startup. To make a setting hot-reloadable,
/// add a TypedSetting consumer that subscribes to changes here (future work).
///
/// Type coercion is invariant-culture for numerics (no comma-vs-period surprises
/// across locales) and case-insensitive for booleans.
/// </summary>
public sealed class SettingsRepository(IDbContextFactory<AppDbContext> dbFactory)
{
    public async Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.SettingsKv.FirstOrDefaultAsync(s => s.Key == key, ct);
        return row?.Value;
    }

    public async Task SetAsync(string key, string value, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.SettingsKv.FirstOrDefaultAsync(s => s.Key == key, ct);
        if (row is null)
        {
            db.SettingsKv.Add(new SettingsKv { Key = key, Value = value, UpdatedUtc = DateTime.UtcNow });
        }
        else
        {
            row.Value = value;
            row.UpdatedUtc = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task<int?> GetIntAsync(string key, CancellationToken ct = default)
    {
        var v = await GetAsync(key, ct);
        return int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;
    }

    public async Task<decimal?> GetDecimalAsync(string key, CancellationToken ct = default)
    {
        var v = await GetAsync(key, ct);
        return decimal.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    public async Task<bool?> GetBoolAsync(string key, CancellationToken ct = default)
    {
        var v = await GetAsync(key, ct);
        if (v is null) return null;
        return v.Equals("true", StringComparison.OrdinalIgnoreCase) || v == "1";
    }

    public async Task<Dictionary<string, string>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await db.SettingsKv.ToListAsync(ct);
        return rows.ToDictionary(r => r.Key, r => r.Value, StringComparer.OrdinalIgnoreCase);
    }
}
