using IdiotProof.Blazor.Data;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Blazor.Services;

public sealed class LiveBarRepository(IDbContextFactory<AppDbContext> dbFactory)
{
    public async Task UpsertBarAsync(LiveBar bar, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var existing = await db.LiveBars
            .FirstOrDefaultAsync(b => b.StrategyId == bar.StrategyId
                                   && b.DateEt == bar.DateEt
                                   && b.Min == bar.Min, ct);
        if (existing is null)
        {
            db.LiveBars.Add(bar);
        }
        else
        {
            existing.Et = bar.Et;
            existing.Open = bar.Open;
            existing.High = bar.High;
            existing.Low = bar.Low;
            existing.Close = bar.Close;
            existing.Volume = bar.Volume;
            existing.Vwap = bar.Vwap;
            existing.WindowHigh = bar.WindowHigh;
            existing.Volx = bar.Volx;
            existing.InSession = bar.InSession;
            existing.CondBitsJson = bar.CondBitsJson;
            existing.Fire = bar.Fire;
            existing.Exit = bar.Exit;
            existing.WrittenUtc = bar.WrittenUtc;
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<LiveBar>> GetTodayBarsAsync(Guid strategyId, string dateEt, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.LiveBars
            .Where(b => b.StrategyId == strategyId && b.DateEt == dateEt)
            .OrderBy(b => b.Min)
            .AsNoTracking()
            .ToListAsync(ct);
    }

    public async Task<List<LiveBar>> GetRecentlyUpdatedAsync(DateTime since, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.LiveBars
            .Where(b => b.WrittenUtc > since)
            .AsNoTracking()
            .ToListAsync(ct);
    }

    public async Task PurgeOldDaysAsync(Guid strategyId, int keepDays = 2, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var cutoff = DateTime.UtcNow.AddDays(-keepDays);
        var old = await db.LiveBars
            .Where(b => b.StrategyId == strategyId && b.WrittenUtc < cutoff)
            .ToListAsync(ct);
        if (old.Count > 0)
        {
            db.LiveBars.RemoveRange(old);
            await db.SaveChangesAsync(ct);
        }
    }
}
