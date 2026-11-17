using System.Data;
using System.Diagnostics;
using System.Text.Json;
using IdiotProof.Core.Data;
using IdiotProof.Core.History;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IdiotProof.Core.Catalysts;

/// <summary>
/// One store's reclassification: rows read, rows whose type or LOI flag was written, how many rows the version 1 rules
/// leave in Other (the "before"), the type of every row after the run, and the rows flagged non-binding.
/// </summary>
public sealed record CatalystReclassifyResult(string Store, long Rows, long Written, long LegacyOther, IReadOnlyDictionary<string, long> TypeCounts, long NonBindingLoi, double Seconds)
{
    public long Other => TypeCounts.GetValueOrDefault(nameof(CatalystPreClass.Other));
    public long NoCatalyst => TypeCounts.GetValueOrDefault(nameof(CatalystPreClass.NoCatalyst));
}

/// <summary>
/// Writes the catalyst type (IP-T487) onto rows stored before the type existed, or under older rules: the app database's
/// catalyst events and the permanent history's news and filings. Deterministic (<see cref="CatalystPreClassifier.TypeOf"/>),
/// idempotent (a row whose type and flag already match is not written), and it never touches the frozen version 1 fields.
/// No model and no network call.
/// </summary>
public sealed class CatalystReclassifier(IDbContextFactory<AppDbContext> dbFactory, HistoryDatabase history, ILogger<CatalystReclassifier> log)
{
    private const int Page = 2000;

    /// <summary>Every catalyst event of the app database (live and backfilled).</summary>
    public async Task<CatalystReclassifyResult> AppEventsAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        long rows = 0, written = 0, legacyOther = 0, loi = 0;
        var counts = new SortedDictionary<string, long>(StringComparer.Ordinal);
        Guid? after = null;
        while (true)
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var query = db.CatalystEvents.AsQueryable();
            if (after is { } a) query = query.Where(e => e.Id.CompareTo(a) > 0);
            var page = await query.OrderBy(e => e.Id).Take(Page).ToListAsync(ct);
            if (page.Count == 0) break;
            foreach (var row in page)
            {
                rows++;
                var type = CatalystPreClassifier.TypeOf(row.Headline, row.FormType, Items(row.ItemsJson), row.PreClass);
                var name = type.Class.ToString();
                if (row.CatalystType != name || row.NonBindingLoi != type.NonBindingLoi)
                {
                    row.CatalystType = name;
                    row.NonBindingLoi = type.NonBindingLoi;
                    written++;
                }
                counts[name] = counts.GetValueOrDefault(name) + 1;
                if (type.NonBindingLoi) loi++;
                if (row.PreClass == nameof(CatalystLegacyClass.Other)) legacyOther++;
            }
            await db.SaveChangesAsync(ct);
            after = page[^1].Id;
        }
        return new("app catalyst events", rows, written, legacyOther, counts, loi, Math.Round(sw.Elapsed.TotalSeconds, 1));
    }

    /// <summary>The permanent history's news: one type per item from its headline and stored version 1 class.</summary>
    public async Task<CatalystReclassifyResult> HistoryNewsAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        await history.EnsureSchemaAsync(ct).ConfigureAwait(false);
        long rows = 0, written = 0, legacyOther = 0, loi = 0;
        var counts = new SortedDictionary<string, long>(StringComparer.Ordinal);
        var changes = NewsChanges();
        await using var read = await history.OpenAsync(ct).ConfigureAwait(false);
        await using var write = await history.OpenAsync(ct).ConfigureAwait(false);
        await using (var q = new SqlCommand("SELECT Id, Headline, PreClass, CatalystType, NonBindingLoi FROM dbo.News", read) { CommandTimeout = 0 })
        await using (var r = await q.ExecuteReaderAsync(ct).ConfigureAwait(false))
            while (await r.ReadAsync(ct).ConfigureAwait(false))
            {
                rows++;
                var legacy = r.IsDBNull(2) ? null : r.GetString(2);
                var type = CatalystPreClassifier.TypeOf(r.GetString(1), null, null, legacy);
                var name = type.Class.ToString();
                if (r.IsDBNull(3) || r.GetString(3) != name || r.IsDBNull(4) || r.GetBoolean(4) != type.NonBindingLoi)
                {
                    changes.Rows.Add(r.GetGuid(0), name, type.NonBindingLoi);
                    written++;
                }
                counts[name] = counts.GetValueOrDefault(name) + 1;
                if (type.NonBindingLoi) loi++;
                if (legacy is null or nameof(CatalystLegacyClass.Other)) legacyOther++;
            }
        // Written after the read has finished, so the scan never waits on its own updates.
        log.LogInformation("Catalyst reclassify: history news {Rows} read, {Written} to write", rows, written);
        await FlushNewsAsync(write, changes, ct).ConfigureAwait(false);
        return new("history news", rows, written, legacyOther, counts, loi, Math.Round(sw.Elapsed.TotalSeconds, 1));
    }

    /// <summary>
    /// The permanent history's filings. A filing's type is a function of its form and 8-K items alone, so each distinct
    /// (form, items) pair is classified once and written with one set-based update. The "before" is what the version 1
    /// rules say about the same form and items.
    /// </summary>
    public async Task<CatalystReclassifyResult> HistoryFilingsAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        await history.EnsureSchemaAsync(ct).ConfigureAwait(false);
        long rows = 0, legacyOther = 0;
        var counts = new SortedDictionary<string, long>(StringComparer.Ordinal);
        var map = new DataTable();
        map.Columns.Add("FormType", typeof(string)); map.Columns.Add("ItemsJson", typeof(string)); map.Columns.Add("CatalystType", typeof(string));
        await using var c = await history.OpenAsync(ct).ConfigureAwait(false);
        await using (var q = new SqlCommand("SELECT FormType, ItemsJson, COUNT_BIG(*) FROM dbo.Filings GROUP BY FormType, ItemsJson", c) { CommandTimeout = 0 })
        await using (var r = await q.ExecuteReaderAsync(ct).ConfigureAwait(false))
            while (await r.ReadAsync(ct).ConfigureAwait(false))
            {
                var form = r.GetString(0);
                var itemsJson = r.IsDBNull(1) ? null : r.GetString(1);
                long n = r.GetInt64(2);
                var items = Items(itemsJson);
                var name = CatalystPreClassifier.TypeOf(null, form, items, null).Class.ToString();
                map.Rows.Add(form, (object?)itemsJson ?? DBNull.Value, name);
                rows += n;
                counts[name] = counts.GetValueOrDefault(name) + n;
                if (CatalystLegacyClassifier.Classify(null, null, form, items, "edgar").Class == CatalystLegacyClass.Other) legacyOther += n;
            }
        long written = 0;
        if (map.Rows.Count > 0)
        {
            await using var tx = (SqlTransaction)await c.BeginTransactionAsync(ct).ConfigureAwait(false);
            await using (var create = new SqlCommand("CREATE TABLE #types (FormType varchar(24) NOT NULL, ItemsJson nvarchar(400) NULL, CatalystType varchar(32) NOT NULL)", c, tx))
                await create.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            await SqlHistoryBarCache.BulkAsync(c, tx, "#types", map, ct).ConfigureAwait(false);
            await using var update = new SqlCommand("""
                UPDATE f SET f.CatalystType = t.CatalystType
                FROM dbo.Filings f JOIN #types t ON t.FormType = f.FormType AND (t.ItemsJson = f.ItemsJson OR (t.ItemsJson IS NULL AND f.ItemsJson IS NULL))
                WHERE f.CatalystType IS NULL OR f.CatalystType <> t.CatalystType;
                DECLARE @n bigint = @@ROWCOUNT;
                DROP TABLE #types;
                SELECT @n;
                """, c, tx) { CommandTimeout = 0 };
            written = Convert.ToInt64(await update.ExecuteScalarAsync(ct).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
            await tx.CommitAsync(ct).ConfigureAwait(false);
        }
        return new("history filings", rows, written, legacyOther, counts, 0, Math.Round(sw.Elapsed.TotalSeconds, 1));
    }

    private static DataTable NewsChanges()
    {
        var t = new DataTable();
        t.Columns.Add("Id", typeof(Guid)); t.Columns.Add("CatalystType", typeof(string)); t.Columns.Add("NonBindingLoi", typeof(bool));
        return t;
    }

    private static async Task FlushNewsAsync(SqlConnection c, DataTable changes, CancellationToken ct)
    {
        if (changes.Rows.Count == 0) return;
        await using var tx = (SqlTransaction)await c.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using (var create = new SqlCommand("CREATE TABLE #types (Id uniqueidentifier NOT NULL PRIMARY KEY, CatalystType varchar(32) NOT NULL, NonBindingLoi bit NOT NULL)", c, tx))
            await create.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        await SqlHistoryBarCache.BulkAsync(c, tx, "#types", changes, ct).ConfigureAwait(false);
        await using (var update = new SqlCommand("UPDATE n SET n.CatalystType = t.CatalystType, n.NonBindingLoi = t.NonBindingLoi FROM dbo.News n JOIN #types t ON t.Id = n.Id; DROP TABLE #types;", c, tx) { CommandTimeout = 0 })
            await update.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
        changes.Clear();
    }

    private static List<string> Items(string? itemsJson)
    {
        if (string.IsNullOrWhiteSpace(itemsJson)) return [];
        try { return JsonSerializer.Deserialize<List<string>>(itemsJson) ?? []; }
        catch (JsonException) { return []; }
    }
}
