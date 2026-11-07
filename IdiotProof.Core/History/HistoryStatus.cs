using Microsoft.Data.SqlClient;

namespace IdiotProof.Core.History;

/// <summary>What the history database holds (IP-T454): size, rows per table, symbols per coverage kind, the backfill queue.</summary>
public sealed record HistoryStatus(string Database, double SizeGb, int SchemaVersion, IReadOnlyList<(string Table, long Rows)> Rows,
    IReadOnlyList<(string Kind, long Symbols)> SymbolsByKind, IReadOnlyDictionary<string, int> Jobs)
{
    public static async Task<HistoryStatus> ReadAsync(HistoryDatabase h, CancellationToken ct)
    {
        await using var c = await h.OpenAsync(ct).ConfigureAwait(false);
        async Task<List<object[]>> Query(string sql)
        {
            await using var q = new SqlCommand(sql, c) { CommandTimeout = 300 };
            await using var r = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
            var rows = new List<object[]>();
            while (await r.ReadAsync(ct).ConfigureAwait(false))
            {
                var v = new object[r.FieldCount];
                r.GetValues(v);
                rows.Add(v);
            }
            return rows;
        }
        double size = Convert.ToDouble((await Query("SELECT SUM(CAST(size AS bigint)) * 8.0 / 1024 / 1024 FROM sys.database_files"))[0][0]);
        int version = Convert.ToInt32((await Query("SELECT ISNULL(MAX(Version), 0) FROM dbo.SchemaVersion"))[0][0]);
        var rows = (await Query("""
            SELECT t.name, SUM(p.rows) FROM sys.tables t JOIN sys.partitions p ON p.object_id = t.object_id AND p.index_id IN (0, 1)
            WHERE t.name <> 'SchemaVersion' GROUP BY t.name ORDER BY t.name
            """)).Select(r => ((string)r[0], Convert.ToInt64(r[1]))).ToList();
        var kinds = (await Query("""
            SELECT Kind, COUNT(DISTINCT Symbol) FROM (SELECT Symbol, Kind FROM dbo.Coverage UNION ALL SELECT Symbol, Kind FROM dbo.CoverageRanges) x GROUP BY Kind ORDER BY Kind
            """)).Select(r => ((string)r[0], Convert.ToInt64(r[1]))).ToList();
        var jobs = (await Query("SELECT Status, COUNT(*) FROM dbo.BackfillJobs GROUP BY Status")).ToDictionary(r => (string)r[0], r => Convert.ToInt32(r[1]));
        return new HistoryStatus(h.DatabaseName, Math.Round(size, 2), version, rows, kinds, jobs);
    }
}
