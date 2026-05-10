using IdiotProof.Blazor.Data;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Blazor.Services;

/// <summary>
/// Append-only audit-trail writer + reader. Used by the Blazor app, the
/// Monitor, and any future automation to record signal fires, order
/// placements, broker switches, risk-guardian vetoes, and other
/// trade-relevant events.
///
/// Reads are recent-first (TimestampUtc DESC) and capped by the caller — the
/// repository never returns the full table. Pruning / archival is a separate
/// maintenance job; this repo only writes new rows.
/// </summary>
public sealed class AuditLogRepository(IDbContextFactory<AppDbContext> dbFactory)
{
    /// <summary>
    /// Append a new audit entry. Lightweight: a single insert with the
    /// indexed columns populated; no read required.
    /// </summary>
    public async Task LogAsync(string category, string message, string? userId = null, string? dataJson = null, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.AuditLogs.Add(new AuditLog
        {
            TimestampUtc = DateTime.UtcNow,
            UserId       = userId,
            Category     = category,
            Message      = message,
            DataJson     = dataJson,
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<AuditLog>> GetRecentAsync(int limit = 100, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.AuditLogs
            .OrderByDescending(a => a.TimestampUtc)
            .Take(Math.Clamp(limit, 1, 1000))
            .ToListAsync(ct);
    }

    public async Task<List<AuditLog>> GetForUserAsync(string userId, int limit = 100, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.AuditLogs
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.TimestampUtc)
            .Take(Math.Clamp(limit, 1, 1000))
            .ToListAsync(ct);
    }

    public async Task<List<AuditLog>> GetByCategoryAsync(string category, int limit = 100, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.AuditLogs
            .Where(a => a.Category == category)
            .OrderByDescending(a => a.TimestampUtc)
            .Take(Math.Clamp(limit, 1, 1000))
            .ToListAsync(ct);
    }
}
