using IdiotProof.Blazor.Data;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Blazor.Services;

/// <summary>
/// The trade diary: one row per executed trade, opened on the buy and
/// closed on the sell. Written by the Monitor on the money path — so every
/// method is defensive (a diary failure must NEVER break a trade; callers wrap
/// these in log-and-continue). Read by the /diary page and the CLI export.
/// </summary>
public sealed class TradeDiaryRepository(IDbContextFactory<AppDbContext> dbFactory)
{
    /// <summary>
    /// Records a new open trade at entry. Returns the created row's Id so the
    /// caller can correlate, though the exit path re-finds by (strategy, Open)
    /// to stay robust across process restarts.
    /// </summary>
    public async Task<Guid> OpenAsync(TradeDiaryEntry entry, CancellationToken ct = default)
    {
        entry.Status     = TradeDiaryStatus.Open;
        entry.CreatedUtc = DateTime.UtcNow;
        entry.UpdatedUtc = entry.CreatedUtc;
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        // A strategy holds at most one position at a time, so there should be no
        // Open row when we open a new one. If there is (the Monitor stopped
        // between a buy and its sell), it's an orphan — mark it Orphaned so the
        // exit path's "most-recent Open" match can never close the wrong trade.
        var stale = await db.TradeDiary
            .Where(t => t.StrategyId == entry.StrategyId && t.Status == TradeDiaryStatus.Open)
            .ToListAsync(ct);
        foreach (var s in stale) { s.Status = TradeDiaryStatus.Orphaned; s.UpdatedUtc = DateTime.UtcNow; }

        db.TradeDiary.Add(entry);
        await db.SaveChangesAsync(ct);
        return entry.Id;
    }

    /// <summary>
    /// Closes the open diary row for a strategy on exit — stamps exit price/
    /// time/reason, the exit order id, and realized P&amp;L / return %. Matches the
    /// most-recent Open row for the strategy (a strategy holds at most one
    /// position at a time). No-op if none is open (e.g. a manual/foreign exit).
    /// </summary>
    public async Task CloseAsync(
        Guid strategyId, decimal exitPrice, string exitReason, string? exitOrderId,
        decimal realizedPnL, int soldQty, DateTime exitUtc, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.TradeDiary
            .Where(t => t.StrategyId == strategyId && t.Status == TradeDiaryStatus.Open)
            .OrderByDescending(t => t.EntryUtc)
            .FirstOrDefaultAsync(ct);
        if (row is null) return;

        // Record the quantity ACTUALLY traded (reconciliation may have found a
        // partial fill: soldQty < the optimistically-recorded entry quantity).
        // Return % must be on that same quantity as the realized P&L, or the two
        // disagree.
        if (soldQty > 0 && soldQty != row.Quantity) row.Quantity = soldQty;

        row.ExitUtc     = exitUtc;
        row.ExitPrice   = exitPrice;
        row.ExitReason  = exitReason;
        row.ExitOrderId = exitOrderId;
        row.RealizedPnL = realizedPnL;
        var cost = row.EntryPrice * row.Quantity;
        row.ReturnPercent = cost > 0m ? Math.Round(realizedPnL / cost * 100m, 4) : 0m;
        row.Status      = TradeDiaryStatus.Closed;
        row.UpdatedUtc  = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Voids the open diary row when reconciliation proves the entry never
    /// filled (phantom). Marks it NotFilled with zero P&amp;L rather than deleting,
    /// so the attempt stays visible in the diary.
    /// </summary>
    public async Task MarkNotFilledAsync(Guid strategyId, DateTime whenUtc, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.TradeDiary
            .Where(t => t.StrategyId == strategyId && t.Status == TradeDiaryStatus.Open)
            .OrderByDescending(t => t.EntryUtc)
            .FirstOrDefaultAsync(ct);
        if (row is null) return;

        row.Status      = TradeDiaryStatus.NotFilled;
        row.ExitUtc     = whenUtc;
        row.ExitReason  = "NotFilled";
        row.RealizedPnL = 0m;
        row.ReturnPercent = 0m;
        row.UpdatedUtc  = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    // ── Manual orders (the TransactionGateway, IP-T93) ──────────────────

    /// <summary>
    /// Records a person's entry (CLI, MCP, web) the broker accepted. Manual rows carry
    /// <see cref="Guid.Empty"/> as their strategy, so unlike <see cref="OpenAsync"/> nothing is
    /// orphaned: a person may hold several manual positions at once.
    /// </summary>
    public async Task<Guid> RecordManualEntryAsync(TradeDiaryEntry entry, CancellationToken ct = default)
    {
        entry.StrategyId = Guid.Empty;
        entry.Status     = TradeDiaryStatus.Open;
        entry.CreatedUtc = DateTime.UtcNow;
        entry.UpdatedUtc = entry.CreatedUtc;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.TradeDiary.Add(entry);
        await db.SaveChangesAsync(ct);
        return entry.Id;
    }

    /// <summary>
    /// Closes the newest open MANUAL row for <paramref name="symbol"/> in that account (paper or
    /// not) when a person's exit was accepted. A smaller exit than the row's size leaves it open
    /// (a partial). P&amp;L and return % are set only when both prices are known; otherwise they
    /// stay empty rather than guessed. Returns false when there was no manual row to close
    /// (e.g. the position was opened outside IdiotProof, or by a strategy).
    /// </summary>
    public async Task<bool> CloseManualAsync(Guid ownerUserId, string symbol, bool isPaper, decimal? exitPrice,
        string? exitOrderId, int quantity, DateTime exitUtc, string exitReason, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.TradeDiary
            .Where(t => t.StrategyId == Guid.Empty && t.OwnerUserId == ownerUserId && t.Symbol == symbol
                     && t.IsPaper == isPaper && t.Status == TradeDiaryStatus.Open)
            .OrderByDescending(t => t.EntryUtc)
            .FirstOrDefaultAsync(ct);
        if (row is null || quantity < row.Quantity) return false;

        row.ExitUtc     = exitUtc;
        row.ExitPrice   = exitPrice;
        row.ExitReason  = exitReason;
        row.ExitOrderId = exitOrderId;
        if (exitPrice is { } exit && row.EntryPrice > 0m)
        {
            var perShare = row.Direction == nameof(IdiotProof.Models.TradeDirection.Short) ? row.EntryPrice - exit : exit - row.EntryPrice;
            row.RealizedPnL = Math.Round(perShare * row.Quantity, 2);
            var cost = row.EntryPrice * row.Quantity;
            row.ReturnPercent = cost > 0m ? Math.Round(row.RealizedPnL.Value / cost * 100m, 4) : 0m;
        }
        row.Status     = TradeDiaryStatus.Closed;
        row.UpdatedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    // ── Reads (diary page + CLI export) ─────────────────────────────────

    public async Task<List<TradeDiaryEntry>> GetForUserAsync(Guid ownerUserId, int limit = 200, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.TradeDiary
            .Where(t => t.OwnerUserId == ownerUserId)
            .OrderByDescending(t => t.EntryUtc)
            .Take(Math.Clamp(limit, 1, 5000))
            .ToListAsync(ct);
    }

    public async Task<List<TradeDiaryEntry>> GetAllAsync(int limit = 5000, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.TradeDiary
            .OrderByDescending(t => t.EntryUtc)
            .Take(Math.Clamp(limit, 1, 50000))
            .ToListAsync(ct);
    }
}
