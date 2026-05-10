using IdiotProof.Blazor.Data;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Blazor.Services;

/// <summary>
/// Upsert + read for the ConditionProgress table. Written by the Monitor on
/// every evaluation tick; read by the Strategies page to render live
/// progress badges. Designed for many-writes, occasional-reads:
///
///   • <see cref="UpsertAsync"/> — single-row upsert per strategy. Cheap,
///     no read-modify-write race for distinct StrategyIds.
///   • <see cref="GetForStrategyIdsAsync"/> — bulk read for the Strategies
///     page; takes the list of visible row ids and returns a dictionary so
///     the page can render badges in one pass.
/// </summary>
public sealed class ConditionProgressRepository(IDbContextFactory<AppDbContext> dbFactory)
{
    public async Task UpsertAsync(Guid strategyId, int passedCount, int totalCount, string? firstFailingVerb, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.ConditionProgress.FirstOrDefaultAsync(p => p.StrategyId == strategyId, ct);
        var now = DateTime.UtcNow;

        if (row is null)
        {
            db.ConditionProgress.Add(new ConditionProgress
            {
                StrategyId       = strategyId,
                PassedCount      = passedCount,
                TotalCount       = totalCount,
                FirstFailingVerb = firstFailingVerb,
                EvaluatedUtc     = now,
            });
        }
        else
        {
            row.PassedCount      = passedCount;
            row.TotalCount       = totalCount;
            row.FirstFailingVerb = firstFailingVerb;
            row.EvaluatedUtc     = now;
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Returns a dictionary keyed by StrategyId for the supplied ids. Missing
    /// strategies (never evaluated) simply don't appear in the result. The
    /// Strategies page treats absence as "not yet evaluated" and renders no
    /// badge.
    /// </summary>
    public async Task<Dictionary<Guid, ConditionProgress>> GetForStrategyIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default)
    {
        var idList = ids.ToList();
        if (idList.Count == 0) return new Dictionary<Guid, ConditionProgress>();

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await db.ConditionProgress
            .Where(p => idList.Contains(p.StrategyId))
            .ToListAsync(ct);
        return rows.ToDictionary(r => r.StrategyId);
    }

    public async Task<ConditionProgress?> GetAsync(Guid strategyId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.ConditionProgress.FirstOrDefaultAsync(p => p.StrategyId == strategyId, ct);
    }
}
