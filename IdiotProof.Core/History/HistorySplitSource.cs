using System.Data;
using IdiotProof.Core.Evidence;
using IdiotProof.DataFeeds;
using IdiotProof.Evidence.Gappers;
using IdiotProof.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace IdiotProof.Core.History;

/// <summary>
/// The read-through for splits (IP-T454), the way <see cref="HistoryReadThrough"/> serves news and filings: a symbol
/// whose finished ET days in the range are covered (by its own five-year backfill, an earlier read, or the nightly
/// market-wide pass under <see cref="HistoryBackfillService.AllSymbols"/>) is served from <c>dbo.Splits</c>; the rest
/// are fetched from <paramref name="inner"/> once for the finished-day span, stored and marked covered. The current day
/// onward (calendared ex-dates) is always fetched live and stored, never marked. When the history server does not
/// answer, the call falls back to <paramref name="inner"/> unstored. Without a real split source (Mock feed, no keys)
/// it is a pass-through.
/// </summary>
public sealed class HistorySplitSource(HistoryDatabase history, ISplitSource inner, TimeProvider time, ILogger<HistorySplitSource>? log = null) : ISplitSource
{
    private Task? schema;

    private DateOnly TodayEt => DateOnly.FromDateTime(MinuteBarMath.ToEastern(time.GetUtcNow().UtcDateTime));

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<SplitEvent>>> GetSplitsAsync(
        IEnumerable<string> symbols, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var syms = symbols.Select(s => s.Trim().ToUpperInvariant()).Where(s => s.Length is > 0 and <= 16).Distinct(StringComparer.Ordinal).ToList();
        if (inner is NullSplitSource || syms.Count == 0 || to < from) return await inner.GetSplitsAsync(syms, from, to, ct).ConfigureAwait(false);
        var today = TodayEt;
        var doneTo = to < today ? to : today.AddDays(-1);
        SqlConnection c;
        try
        {
            var ensure = schema ??= history.EnsureSchemaAsync(CancellationToken.None);
            try { await ensure.ConfigureAwait(false); }
            catch { schema = null; throw; }
            c = await history.OpenAsync(ct).ConfigureAwait(false);
        }
        catch (SqlException ex)
        {
            log?.LogWarning("History read-through: splits served live, history database unavailable ({Message}).", ex.Message);
            return await inner.GetSplitsAsync(syms, from, to, ct).ConfigureAwait(false);
        }

        await using (c)
        {
            if (from <= doneTo)
            {
                var held = await HistoryReadThrough.RangesAsync(c, [.. syms, HistoryBackfillService.AllSymbols], HistoryDatabase.Kinds.Splits, from, doneTo, ct).ConfigureAwait(false);
                var market = held.GetValueOrDefault(HistoryBackfillService.AllSymbols) ?? [];
                var missing = syms.Where(s => !HistoryWrites.Covers((held.GetValueOrDefault(s) ?? []).Concat(market), from, doneTo)).ToList();
                if (missing.Count > 0)
                {
                    var got = await inner.GetSplitsAsync(missing, from, doneTo, ct).ConfigureAwait(false);
                    await using var tx = (SqlTransaction)await c.BeginTransactionAsync(ct).ConfigureAwait(false);
                    await HistoryWrites.InsertSplitsAsync(c, tx, got.Values.SelectMany(x => x), ct).ConfigureAwait(false);
                    var now = time.GetUtcNow().UtcDateTime;
                    foreach (var s in missing)
                        await SqlHistoryBarCache.AddRangeAsync(c, tx, s, HistoryDatabase.Kinds.Splits, from, doneTo,
                            got.TryGetValue(s, out var l) ? l.Count : 0, now, "alpaca", ct).ConfigureAwait(false);
                    await tx.CommitAsync(ct).ConfigureAwait(false);
                }
            }
            if (to >= today)
            {
                var liveFrom = from > today ? from : today;
                var got = await inner.GetSplitsAsync(syms, liveFrom, to, ct).ConfigureAwait(false);
                var list = got.Values.SelectMany(x => x).ToList();
                if (list.Count > 0)
                {
                    await using var tx = (SqlTransaction)await c.BeginTransactionAsync(ct).ConfigureAwait(false);
                    await HistoryWrites.InsertSplitsAsync(c, tx, list, ct).ConfigureAwait(false);
                    await tx.CommitAsync(ct).ConfigureAwait(false);
                }
            }
            return await ReadAsync(c, syms, from, to, ct).ConfigureAwait(false);
        }
    }

    private static async Task<IReadOnlyDictionary<string, IReadOnlyList<SplitEvent>>> ReadAsync(SqlConnection c, List<string> syms, DateOnly from, DateOnly to, CancellationToken ct)
    {
        await using var q = new SqlCommand("""
            SELECT Symbol, ExDate, OldRate, NewRate FROM dbo.Splits
            WHERE ExDate >= @a AND ExDate <= @b AND Symbol IN (SELECT value FROM STRING_SPLIT(@syms, ','))
            ORDER BY Symbol, ExDate
            """, c);
        q.Parameters.Add("@a", SqlDbType.Date).Value = from.ToDateTime(TimeOnly.MinValue);
        q.Parameters.Add("@b", SqlDbType.Date).Value = to.ToDateTime(TimeOnly.MinValue);
        q.Parameters.Add("@syms", SqlDbType.NVarChar, -1).Value = string.Join(",", syms);
        var map = syms.ToDictionary(s => s, _ => new List<SplitEvent>(), StringComparer.OrdinalIgnoreCase);
        await using var r = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            var s = r.GetString(0);
            if (map.TryGetValue(s, out var l))
                l.Add(new SplitEvent(s, DateOnly.FromDateTime(r.GetDateTime(1)), r.GetDecimal(2), r.GetDecimal(3)));
        }
        return map.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<SplitEvent>)kv.Value, StringComparer.OrdinalIgnoreCase);
    }
}
