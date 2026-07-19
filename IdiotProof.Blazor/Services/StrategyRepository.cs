using IdiotProof.Blazor.Data;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Blazor.Services;

/// <summary>
/// CRUD over the Strategies table on the IdiotProof SQL Server database.
/// All write operations stamp UpdatedUtc; Create assigns a UUIDv7 Id (time-ordered)
/// and CreatedUtc. The Monitor reads via <see cref="GetActiveAsync"/> to find every
/// strategy it should evaluate this tick.
/// </summary>
public sealed class StrategyRepository(IDbContextFactory<AppDbContext> dbFactory)
{
    public async Task<List<Strategy>> GetAllForUserAsync(Guid userId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Strategies
            .Where(s => s.OwnerUserId == userId)
            .OrderByDescending(s => s.UpdatedUtc)
            .ToListAsync(ct);
    }

    public async Task<Strategy?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Strategies.FirstOrDefaultAsync(s => s.Id == id, ct);
    }

    /// <summary>
    /// Returns every active strategy across all users — the input set for the Monitor.
    /// </summary>
    public async Task<List<Strategy>> GetActiveAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Strategies
            .Where(s => s.IsActive)
            .ToListAsync(ct);
    }

    public async Task<Strategy> CreateAsync(Guid ownerUserId, string title, string symbol,
        string scriptText, string? description = null, string? workspaceId = null,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var strategy = new Strategy
        {
            Id          = Guid.CreateVersion7(),
            OwnerUserId = ownerUserId,
            Title       = title,
            Description = description,
            Symbol      = symbol.ToUpperInvariant(),
            ScriptText  = scriptText,
            WorkspaceId = workspaceId,
            IsActive    = false,
            CreatedUtc  = now,
            UpdatedUtc  = now,
        };

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.Strategies.Add(strategy);
        await db.SaveChangesAsync(ct);
        return strategy;
    }

    public async Task UpdateAsync(Strategy strategy, CancellationToken ct = default)
    {
        strategy.UpdatedUtc = DateTime.UtcNow;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.Strategies.Update(strategy);
        await db.SaveChangesAsync(ct);
    }

    public async Task SetActiveAsync(Guid id, bool isActive, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var strategy = await db.Strategies.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (strategy is null) return;
        strategy.IsActive = isActive;
        strategy.UpdatedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var strategy = await db.Strategies.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (strategy is null) return;
        db.Strategies.Remove(strategy);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Bumps FireCount + LastFiredUtc when the Monitor reports a signal fired
    /// for this strategy. Stays minimal — full TradeSignal log is a separate table.
    /// </summary>
    public async Task RecordFiredAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var strategy = await db.Strategies.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (strategy is null) return;
        strategy.LastFiredUtc = DateTime.UtcNow;
        strategy.FireCount++;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Records an entry fill: the Monitor now manages an open position for
    /// this strategy and will evaluate exit rules instead of entry conditions.
    /// </summary>
    public async Task RecordEntryFillAsync(Guid id, int quantity, decimal fillPrice, DateTime filledUtc, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var strategy = await db.Strategies.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (strategy is null) return;
        strategy.PositionQty    = quantity;
        strategy.LastEntryPrice = fillPrice;
        strategy.EntryFilledUtc = filledUtc;
        strategy.LastExitedUtc  = null;
        strategy.LastExitPrice  = null;
        strategy.LastExitReason = null;
        strategy.UpdatedUtc     = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Records an exit fill: flattens the tracked position and stamps the
    /// exit bookkeeping the UI renders ("sold 09:22 — PeakGiveback @ 11.48").
    /// </summary>
    public async Task RecordExitFillAsync(Guid id, decimal exitPrice, string reason, DateTime exitedUtc, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var strategy = await db.Strategies.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (strategy is null) return;
        strategy.PositionQty    = 0;
        strategy.LastExitPrice  = exitPrice;
        strategy.LastExitReason = reason;
        strategy.LastExitedUtc  = exitedUtc;
        strategy.UpdatedUtc     = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
