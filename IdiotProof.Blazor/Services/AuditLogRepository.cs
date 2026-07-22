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
    /// <summary>Column limits (see <see cref="AuditLog"/> data annotations).</summary>
    private const int MessageMaxLength = 500;
    private const int CategoryMaxLength = 32;

    public async Task LogAsync(string category, string message, Guid? userId = null, string? dataJson = null, CancellationToken ct = default)
    {
        // Truncate to the column widths BEFORE insert. The Monitor builds audit
        // messages from untrusted-length inputs — a raw Alpaca error body
        // (order-rejected) or a stack of RiskGuardian block reasons can exceed
        // 500 chars, and an over-length insert throws "String or binary data
        // would be truncated", losing the audit entry (and, on the order-placed
        // path, throwing right after a real order). Overflow is preserved in
        // DataJson (nvarchar(max), unbounded) so nothing is actually lost.
        string storedMessage = message ?? "";
        string storedCategory = category ?? "";
        string? storedData = dataJson;
        if (storedMessage.Length > MessageMaxLength)
        {
            storedData = $"[full message] {storedMessage}" + (storedData is null ? "" : $"\n---\n{storedData}");
            storedMessage = storedMessage[..(MessageMaxLength - 1)] + "…";
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.AuditLogs.Add(new AuditLog
        {
            TimestampUtc = DateTime.UtcNow,
            UserId       = userId,
            Category     = storedCategory.Length > CategoryMaxLength ? storedCategory[..CategoryMaxLength] : storedCategory,
            Message      = storedMessage,
            DataJson     = storedData,
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

    public async Task<List<AuditLog>> GetForUserAsync(Guid userId, int limit = 100, CancellationToken ct = default)
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
