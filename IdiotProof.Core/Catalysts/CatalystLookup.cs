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

    /// <summary>Every (symbol, event) pair published at or after <paramref name="sinceUtc"/>, newest first — the formation universe's catalyst leg.</summary>
    public async Task<List<Monitoring.Formation.CatalystSymbolHit>> RecentSymbolsAsync(DateTime sinceUtc, int max = 5000, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await RecentSymbolsQuery(db, sinceUtc, max).ToListAsync(ct);
        return rows.Select(x => new Monitoring.Formation.CatalystSymbolHit(x.Symbol, x.CatalystScore, x.Direction, x.PublishedUtc, x.Headline, x.PreClass, x.SourceTier)).ToList();
    }

    internal sealed record RecentSymbolRow(string Symbol, double CatalystScore, int Direction, DateTime PublishedUtc, string Headline, string PreClass, int SourceTier);

    // Order and limit on the entity columns before projecting: EF cannot translate an OrderBy on a constructed record (IP-T627).
    internal static IQueryable<RecentSymbolRow> RecentSymbolsQuery(AppDbContext db, DateTime sinceUtc, int max) =>
        db.CatalystEventSymbols.AsNoTracking()
            .Join(db.CatalystEvents.AsNoTracking().Where(e => e.PublishedUtc >= sinceUtc), s => s.EventId, e => e.Id,
                (s, e) => new { s.Symbol, e.CatalystScore, e.Direction, e.PublishedUtc, e.Headline, e.PreClass, e.SourceTier })
            .OrderByDescending(x => x.PublishedUtc)
            .Take(Math.Clamp(max, 1, 50_000))
            .Select(x => new RecentSymbolRow(x.Symbol, x.CatalystScore, x.Direction, x.PublishedUtc, x.Headline, x.PreClass, x.SourceTier));

    private static IQueryable<CatalystEvent> Events(AppDbContext db, List<string> syms) =>
        db.CatalystEvents.AsNoTracking()
            .Where(e => db.CatalystEventSymbols.Any(s => s.EventId == e.Id && syms.Contains(s.Symbol)));
}
