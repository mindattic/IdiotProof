using System.Globalization;
using IdiotProof.Core.Data;
using IdiotProof.DataFeeds;
using IdiotProof.Models;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Core.Evidence;

/// <summary>
/// Where <see cref="HistoricalBarStore"/> keeps what it fetched: which completed (symbol, timeframe,
/// ET day) were fetched, and their bars. A day is saved once with its coverage mark, so a day with no
/// bars (halted, not yet listed) is never requested again. IP-T454 adds the history database behind
/// the same seam; the app database cache stays the default.
/// </summary>
public interface IBarCache
{
    Task<HashSet<DateOnly>> CoveredDaysAsync(string symbol, BarTimeframe timeframe, DateOnly from, DateOnly to, CancellationToken ct);

    /// <summary>Save completed days and mark each covered (with its bar count, zero included). Days already covered are skipped.</summary>
    Task SaveDaysAsync(string symbol, BarTimeframe timeframe, IReadOnlyList<(DateOnly Day, IReadOnlyList<Candle> Bars)> days, string feedName, DateTime nowUtc, CancellationToken ct);

    /// <summary>Stored bars of [from, to], oldest first.</summary>
    Task<List<Candle>> ReadAsync(string symbol, BarTimeframe timeframe, DateOnly from, DateOnly to, CancellationToken ct);
}

/// <summary>The original cache: <c>HistoricalBars</c> and <c>HistoricalBarCoverage</c> in the app database.</summary>
public sealed class AppDbBarCache(IDbContextFactory<AppDbContext> dbFactory) : IBarCache
{
    public static string Key(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public static string Code(BarTimeframe tf) => tf == BarTimeframe.Day1 ? "1Day" : "1Min";
    public static TimeSpan Span(BarTimeframe tf) => tf == BarTimeframe.Day1 ? TimeSpan.FromDays(1) : TimeSpan.FromMinutes(1);

    public async Task<HashSet<DateOnly>> CoveredDaysAsync(string symbol, BarTimeframe timeframe, DateOnly from, DateOnly to, CancellationToken ct)
    {
        string tf = Code(timeframe), fromKey = Key(from), toKey = Key(to);
        await using var db = await dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return (await db.HistoricalBarCoverage.AsNoTracking()
                .Where(c => c.Symbol == symbol && c.Timeframe == tf && string.Compare(c.DateEt, fromKey) >= 0 && string.Compare(c.DateEt, toKey) <= 0)
                .Select(c => c.DateEt).ToListAsync(ct).ConfigureAwait(false))
            .Select(k => DateOnly.ParseExact(k, "yyyy-MM-dd", CultureInfo.InvariantCulture)).ToHashSet();
    }

    public async Task SaveDaysAsync(string symbol, BarTimeframe timeframe, IReadOnlyList<(DateOnly Day, IReadOnlyList<Candle> Bars)> days, string feedName, DateTime nowUtc, CancellationToken ct)
    {
        if (days.Count == 0) return;
        string tf = Code(timeframe), runFrom = Key(days.Min(d => d.Day)), runTo = Key(days.Max(d => d.Day));
        await using var db = await dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var startKeys = (await db.HistoricalBars.AsNoTracking()
                .Where(b => b.Symbol == symbol && b.Timeframe == tf && string.Compare(b.DateEt, runFrom) >= 0 && string.Compare(b.DateEt, runTo) <= 0)
                .Select(b => b.StartUtc).ToListAsync(ct).ConfigureAwait(false))
            .ToHashSet();
        foreach (var (d, bars) in days)
        {
            foreach (var c in bars.DistinctBy(c => c.StartUtc))
            {
                if (!startKeys.Add(c.StartUtc)) continue;
                db.HistoricalBars.Add(new HistoricalBar
                {
                    Symbol = symbol, Timeframe = tf, DateEt = Key(d), StartUtc = c.StartUtc,
                    Open = c.Open, High = c.High, Low = c.Low, Close = c.Close, Volume = c.Volume,
                    Feed = BarProvenance.SourceOf(c, feedName),
                });
            }
            db.HistoricalBarCoverage.Add(new HistoricalBarCoverage { Symbol = symbol, Timeframe = tf, DateEt = Key(d), BarCount = bars.Count, FetchedUtc = nowUtc });
        }
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<List<Candle>> ReadAsync(string symbol, BarTimeframe timeframe, DateOnly from, DateOnly to, CancellationToken ct)
    {
        string tf = Code(timeframe), fromKey = Key(from), toKey = Key(to);
        await using var db = await dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var stored = await db.HistoricalBars.AsNoTracking()
            .Where(b => b.Symbol == symbol && b.Timeframe == tf && string.Compare(b.DateEt, fromKey) >= 0 && string.Compare(b.DateEt, toKey) <= 0)
            .ToListAsync(ct).ConfigureAwait(false);
        var step = Span(timeframe);
        return stored.Select(b => new Candle
        {
            Symbol = b.Symbol, StartUtc = DateTime.SpecifyKind(b.StartUtc, DateTimeKind.Utc), EndUtc = DateTime.SpecifyKind(b.StartUtc, DateTimeKind.Utc) + step,
            Open = b.Open, High = b.High, Low = b.Low, Close = b.Close, Volume = b.Volume, Note = b.Feed,
        }).ToList();
    }
}
