using System.Data;
using IdiotProof.Core.Evidence;
using IdiotProof.DataFeeds;
using IdiotProof.Models;
using Microsoft.Data.SqlClient;

namespace IdiotProof.Core.History;

/// <summary>
/// <see cref="IBarCache"/> on the history database (IP-T454): minute bars bulk-copied into the
/// columnstore, daily bars into <c>DailyBars</c>, one <c>Coverage</c> row per day saved (zero-bar
/// days included). A day already covered is never written again, so concurrent backfills and imports
/// cannot duplicate bars.
/// </summary>
public sealed class SqlHistoryBarCache(HistoryDatabase history) : IBarCache
{
    private Task? schema;

    /// <summary>Opens a connection, creating the database and applying the schema on first use.</summary>
    private async Task<SqlConnection> OpenAsync(CancellationToken ct)
    {
        var ensure = schema ??= history.EnsureSchemaAsync(CancellationToken.None);
        try { await ensure.ConfigureAwait(false); }
        catch { schema = null; throw; }
        return await history.OpenAsync(ct).ConfigureAwait(false);
    }

    private static string Kind(BarTimeframe tf) => tf == BarTimeframe.Day1 ? HistoryDatabase.Kinds.Daily : HistoryDatabase.Kinds.Minute;

    public async Task<HashSet<DateOnly>> CoveredDaysAsync(string symbol, BarTimeframe timeframe, DateOnly from, DateOnly to, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        return await CoveredAsync(c, null, symbol, Kind(timeframe), from, to, ct).ConfigureAwait(false);
    }

    /// <summary>Covered days: per-day marks for minute bars; for daily bars every day inside a covered range (ranges hold the days with no bar too).</summary>
    private static async Task<HashSet<DateOnly>> CoveredAsync(SqlConnection c, SqlTransaction? tx, string symbol, string kind, DateOnly from, DateOnly to, CancellationToken ct)
    {
        if (kind == HistoryDatabase.Kinds.Daily)
        {
            var ranges = await RangesAsync(c, tx, symbol, kind, from, to, ct).ConfigureAwait(false);
            var days = new HashSet<DateOnly>();
            foreach (var (a, b) in ranges)
                for (var d = a < from ? from : a; d <= (b > to ? to : b); d = d.AddDays(1)) days.Add(d);
            return days;
        }
        await using var q = new SqlCommand("SELECT DateEt FROM dbo.Coverage WITH (UPDLOCK, HOLDLOCK) WHERE Symbol = @s AND Kind = @k AND DateEt BETWEEN @a AND @b", c, tx);
        q.Parameters.Add("@s", SqlDbType.VarChar, 16).Value = symbol;
        q.Parameters.Add("@k", SqlDbType.VarChar, 16).Value = kind;
        q.Parameters.Add("@a", SqlDbType.Date).Value = from.ToDateTime(TimeOnly.MinValue);
        q.Parameters.Add("@b", SqlDbType.Date).Value = to.ToDateTime(TimeOnly.MinValue);
        var result = new HashSet<DateOnly>();
        await using var r = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false)) result.Add(DateOnly.FromDateTime(r.GetDateTime(0)));
        return result;
    }

    public static async Task<List<(DateOnly From, DateOnly To)>> RangesAsync(SqlConnection c, SqlTransaction? tx, string symbol, string kind, DateOnly from, DateOnly to, CancellationToken ct)
    {
        await using var q = new SqlCommand("SELECT FromEt, ToEt FROM dbo.CoverageRanges WITH (UPDLOCK, HOLDLOCK) WHERE Symbol = @s AND Kind = @k AND FromEt <= @b AND ToEt >= @a", c, tx);
        q.Parameters.Add("@s", SqlDbType.VarChar, 16).Value = symbol;
        q.Parameters.Add("@k", SqlDbType.VarChar, 16).Value = kind;
        q.Parameters.Add("@a", SqlDbType.Date).Value = from.ToDateTime(TimeOnly.MinValue);
        q.Parameters.Add("@b", SqlDbType.Date).Value = to.ToDateTime(TimeOnly.MinValue);
        var result = new List<(DateOnly, DateOnly)>();
        await using var r = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false)) result.Add((DateOnly.FromDateTime(r.GetDateTime(0)), DateOnly.FromDateTime(r.GetDateTime(1))));
        return result;
    }

    public static async Task AddRangeAsync(SqlConnection c, SqlTransaction? tx, string symbol, string kind, DateOnly from, DateOnly to, int items, DateTime nowUtc, string? source, CancellationToken ct)
    {
        await using var q = new SqlCommand("IF NOT EXISTS (SELECT 1 FROM dbo.CoverageRanges WHERE Symbol = @s AND Kind = @k AND FromEt = @a AND ToEt = @b) INSERT dbo.CoverageRanges (Symbol, Kind, FromEt, ToEt, Items, FetchedUtc, Source) VALUES (@s, @k, @a, @b, @n, @t, @src)", c, tx);
        q.Parameters.Add("@s", SqlDbType.VarChar, 16).Value = symbol;
        q.Parameters.Add("@k", SqlDbType.VarChar, 16).Value = kind;
        q.Parameters.Add("@a", SqlDbType.Date).Value = from.ToDateTime(TimeOnly.MinValue);
        q.Parameters.Add("@b", SqlDbType.Date).Value = to.ToDateTime(TimeOnly.MinValue);
        q.Parameters.Add("@n", SqlDbType.Int).Value = items;
        q.Parameters.Add("@t", SqlDbType.DateTime2).Value = nowUtc;
        q.Parameters.Add("@src", SqlDbType.NVarChar, 100).Value = (object?)source ?? DBNull.Value;
        await q.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task SaveDaysAsync(string symbol, BarTimeframe timeframe, IReadOnlyList<(DateOnly Day, IReadOnlyList<Candle> Bars)> days, string feedName, DateTime nowUtc, CancellationToken ct)
    {
        if (days.Count == 0) return;
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        await using var tx = (SqlTransaction)await c.BeginTransactionAsync(ct).ConfigureAwait(false);
        var kind = Kind(timeframe);
        var covered = await CoveredAsync(c, tx, symbol, kind, days.Min(d => d.Day), days.Max(d => d.Day), ct).ConfigureAwait(false);
        var fresh = days.Where(d => !covered.Contains(d.Day)).GroupBy(d => d.Day).Select(g => g.First()).ToList();
        if (fresh.Count == 0) { await tx.CommitAsync(ct).ConfigureAwait(false); return; }

        if (timeframe == BarTimeframe.Day1)
        {
            await BulkAsync(c, tx, "dbo.DailyBars", DailyTable(symbol, fresh, feedName), ct).ConfigureAwait(false);
            await AddRangeAsync(c, tx, symbol, kind, fresh.Min(d => d.Day), fresh.Max(d => d.Day), fresh.Sum(d => d.Bars.Count > 0 ? 1 : 0), nowUtc, feedName, ct).ConfigureAwait(false);
        }
        else
        {
            await BulkAsync(c, tx, "dbo.MinuteBars", MinuteTable(symbol, fresh, feedName), ct).ConfigureAwait(false);
            await BulkAsync(c, tx, "dbo.Coverage", CoverageTable(symbol, kind, fresh.Select(d => (d.Day, d.Bars.Count)), nowUtc), ct).ConfigureAwait(false);
        }
        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    public async Task<List<Candle>> ReadAsync(string symbol, BarTimeframe timeframe, DateOnly from, DateOnly to, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        var result = new List<Candle>();
        if (timeframe == BarTimeframe.Day1)
        {
            await using var q = new SqlCommand("SELECT DateEt, [Open], High, Low, [Close], Volume, Feed FROM dbo.DailyBars WHERE Symbol = @s AND DateEt BETWEEN @a AND @b ORDER BY DateEt", c);
            q.Parameters.Add("@s", SqlDbType.VarChar, 16).Value = symbol;
            q.Parameters.Add("@a", SqlDbType.Date).Value = from.ToDateTime(TimeOnly.MinValue);
            q.Parameters.Add("@b", SqlDbType.Date).Value = to.ToDateTime(TimeOnly.MinValue);
            await using var r = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await r.ReadAsync(ct).ConfigureAwait(false))
            {
                var start = DateTime.SpecifyKind(r.GetDateTime(0), DateTimeKind.Utc);
                result.Add(new Candle { Symbol = symbol, StartUtc = start, EndUtc = start.AddDays(1), Open = r.GetDecimal(1), High = r.GetDecimal(2), Low = r.GetDecimal(3), Close = r.GetDecimal(4), Volume = r.GetDecimal(5), Note = r.GetString(6) });
            }
            return result;
        }
        var (startUtc, endUtc) = (FileBackedBarStore.EtToUtc(from, TimeOnly.MinValue), FileBackedBarStore.EtToUtc(to.AddDays(1), TimeOnly.MinValue));
        await using (var q = new SqlCommand("SELECT StartUtc, [Open], High, Low, [Close], Volume, TradeCount, Vwap, Feed FROM dbo.MinuteBars WHERE Symbol = @s AND StartUtc >= @a AND StartUtc < @b ORDER BY StartUtc", c))
        {
            q.Parameters.Add("@s", SqlDbType.VarChar, 16).Value = symbol;
            q.Parameters.Add("@a", SqlDbType.DateTime2).Value = startUtc;
            q.Parameters.Add("@b", SqlDbType.DateTime2).Value = endUtc;
            await using var r = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await r.ReadAsync(ct).ConfigureAwait(false))
            {
                var start = DateTime.SpecifyKind(r.GetDateTime(0), DateTimeKind.Utc);
                result.Add(new DetailedCandle
                {
                    Symbol = symbol, StartUtc = start, EndUtc = start.AddMinutes(1), Open = r.GetDecimal(1), High = r.GetDecimal(2), Low = r.GetDecimal(3), Close = r.GetDecimal(4),
                    Volume = r.GetDecimal(5), TradeCount = r.IsDBNull(6) ? 0 : r.GetInt32(6), Vwap = r.IsDBNull(7) ? 0 : r.GetDecimal(7), Note = r.GetString(8),
                });
            }
        }
        return result;
    }

    // ── bulk copy ─────────────────────────────────────────────────────────

    public static async Task BulkAsync(SqlConnection c, SqlTransaction? tx, string table, DataTable rows, CancellationToken ct)
    {
        if (rows.Rows.Count == 0) return;
        using var bulk = new SqlBulkCopy(c, SqlBulkCopyOptions.Default, tx) { DestinationTableName = table, BulkCopyTimeout = 0, BatchSize = 100_000 };
        foreach (DataColumn col in rows.Columns) bulk.ColumnMappings.Add(col.ColumnName, col.ColumnName);
        await bulk.WriteToServerAsync(rows, ct).ConfigureAwait(false);
    }

    public static DataTable MinuteTable(string symbol, IEnumerable<(DateOnly Day, IReadOnlyList<Candle> Bars)> days, string feedName)
    {
        var t = new DataTable();
        t.Columns.Add("Symbol", typeof(string)); t.Columns.Add("StartUtc", typeof(DateTime));
        t.Columns.Add("Open", typeof(decimal)); t.Columns.Add("High", typeof(decimal)); t.Columns.Add("Low", typeof(decimal)); t.Columns.Add("Close", typeof(decimal));
        t.Columns.Add("Volume", typeof(decimal)); t.Columns.Add("TradeCount", typeof(int)); t.Columns.Add("Vwap", typeof(decimal)); t.Columns.Add("Feed", typeof(string));
        foreach (var (_, bars) in days)
            foreach (var b in bars.DistinctBy(b => b.StartUtc).OrderBy(b => b.StartUtc))
            {
                var d = b as DetailedCandle;
                t.Rows.Add(symbol, b.StartUtc, Clamp(b.Open), Clamp(b.High), Clamp(b.Low), Clamp(b.Close), Math.Min(b.Volume, 999_999_999_999_999m),
                    d is null ? DBNull.Value : d.TradeCount, d is null || d.Vwap == 0 ? DBNull.Value : Clamp(d.Vwap), Feed(b, feedName));
            }
        return t;
    }

    public static DataTable DailyTable(string symbol, IEnumerable<(DateOnly Day, IReadOnlyList<Candle> Bars)> days, string feedName)
    {
        var t = new DataTable();
        t.Columns.Add("Symbol", typeof(string)); t.Columns.Add("DateEt", typeof(DateTime));
        t.Columns.Add("Open", typeof(decimal)); t.Columns.Add("High", typeof(decimal)); t.Columns.Add("Low", typeof(decimal)); t.Columns.Add("Close", typeof(decimal));
        t.Columns.Add("Volume", typeof(decimal)); t.Columns.Add("Feed", typeof(string));
        foreach (var (day, bars) in days)
            foreach (var b in bars.Take(1))
                t.Rows.Add(symbol, day.ToDateTime(TimeOnly.MinValue), Clamp(b.Open), Clamp(b.High), Clamp(b.Low), Clamp(b.Close), Math.Min(b.Volume, 999_999_999_999_999m), Feed(b, feedName));
        return t;
    }

    public static DataTable CoverageTable(string symbol, string kind, IEnumerable<(DateOnly Day, int Items)> days, DateTime nowUtc)
    {
        var t = new DataTable();
        t.Columns.Add("Symbol", typeof(string)); t.Columns.Add("Kind", typeof(string)); t.Columns.Add("DateEt", typeof(DateTime));
        t.Columns.Add("Items", typeof(int)); t.Columns.Add("FetchedUtc", typeof(DateTime));
        foreach (var (day, items) in days) t.Rows.Add(symbol, kind, day.ToDateTime(TimeOnly.MinValue), items, nowUtc);
        return t;
    }

    private static decimal Clamp(decimal v) => Math.Clamp(Math.Round(v, 6), -9_999_999_999_999m, 9_999_999_999_999m);

    private static string Feed(Candle b, string feedName)
    {
        var f = BarProvenance.SourceOf(b, feedName);
        return f.Length > 24 ? f[..24] : f;
    }
}
