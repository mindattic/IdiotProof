using IdiotProof.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Core.Catalysts;

/// <summary>
/// Point-in-time reads of <see cref="CatalystEvent"/> for the Monitor and the formation recorder
/// (IP-LAW-10: SQL rows read each tick). Only events published at or before the asked instant are
/// returned, so a snapshot built for 08:00 never sees the 08:05 headline.
/// </summary>
public sealed class CatalystLookup(IDbContextFactory<AppDbContext> dbFactory)
{
    public async Task<CatalystEvent?> LatestForSymbolAsync(string symbol, DateTime asOfUtc, CancellationToken ct = default)
    {
        var sym = symbol.Trim().ToUpperInvariant();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await Events(db, [sym])
            .Where(e => e.PublishedUtc <= asOfUtc)
            .OrderByDescending(e => e.PublishedUtc)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<List<CatalystEvent>> RecentAsync(IEnumerable<string> symbols, DateTime sinceUtc, CancellationToken ct = default)
    {
        var syms = symbols.Select(s => s.Trim().ToUpperInvariant()).Where(s => s.Length > 0).Distinct().ToList();
        if (syms.Count == 0) return [];
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await Events(db, syms)
            .Where(e => e.PublishedUtc >= sinceUtc)
            .OrderByDescending(e => e.PublishedUtc)
            .ToListAsync(ct);
    }

    private static IQueryable<CatalystEvent> Events(AppDbContext db, List<string> syms) =>
        db.CatalystEvents.AsNoTracking()
            .Where(e => db.CatalystEventSymbols.Any(s => s.EventId == e.Id && syms.Contains(s.Symbol)));
}
