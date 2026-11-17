using System.Data;
using System.Globalization;
using IdiotProof.Core.Catalysts;
using IdiotProof.Core.Services;
using IdiotProof.Models;
using Microsoft.Data.SqlClient;

namespace IdiotProof.Core.History;

/// <summary>
/// The history database's write paths for reference data (IP-T454), shared by the backfill and the read-through:
/// each stages rows in a temp table and inserts only what is not held, so every write is idempotent.
/// </summary>
public static class HistoryWrites
{
    /// <summary>Alpaca news items kept once by (source, id) with every tagged symbol, pre-classified on the way in. Returns the rows inserted.</summary>
    public static async Task<int> InsertNewsAsync(SqlConnection c, SqlTransaction tx, IEnumerable<AlpacaNewsItem> items, CancellationToken ct)
    {
        var rows = new DataTable();
        foreach (var (name, type) in new (string, Type)[] { ("Id", typeof(Guid)), ("Source", typeof(string)), ("ExternalId", typeof(string)), ("PublishedUtc", typeof(DateTime)), ("Headline", typeof(string)), ("Url", typeof(string)), ("PreClass", typeof(string)), ("Direction", typeof(int)), ("SourceName", typeof(string)), ("Snippet", typeof(string)), ("Symbols", typeof(string)) })
            rows.Columns.Add(name, type);
        foreach (var n in items.DistinctBy(n => n.Id))
        {
            var pre = CatalystPreClassifier.Classify(n.Headline, n.Summary, source: n.Source);
            rows.Rows.Add(Guid.NewGuid(), CatalystIntake.AlpacaNews, Clip(n.Id, 128), n.CreatedAt, Clip(n.Headline, 1000), Clip(n.Url, 1000), pre.Class.ToString(), pre.Direction,
                Clip(n.Source, 200), string.IsNullOrWhiteSpace(n.Summary) ? DBNull.Value : n.Summary,
                string.Join(",", n.Symbols.Select(x => x.ToUpperInvariant()).Where(x => x.Length <= 16)));
        }
        if (rows.Rows.Count == 0) return 0;
        await using (var create = new SqlCommand("CREATE TABLE #news (Id uniqueidentifier, Source varchar(32), ExternalId varchar(128), PublishedUtc datetime2(0), Headline nvarchar(1000), Url nvarchar(1000), PreClass varchar(32), Direction int, SourceName nvarchar(200), Snippet nvarchar(max), Symbols nvarchar(max))", c, tx))
            await create.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        await SqlHistoryBarCache.BulkAsync(c, tx, "#news", rows, ct).ConfigureAwait(false);
        await using var insert = new SqlCommand("""
            DECLARE @new TABLE (Id uniqueidentifier);
            INSERT dbo.News (Id, Source, ExternalId, PublishedUtc, ReceivedUtc, StampKind, Headline, Url, SourceTier, PreClass, Direction, SourceName, Snippet)
            OUTPUT inserted.Id INTO @new
            SELECT s.Id, s.Source, s.ExternalId, s.PublishedUtc, NULL, 'realtime', s.Headline, s.Url, NULL, s.PreClass, s.Direction, s.SourceName, s.Snippet FROM #news s
            WHERE NOT EXISTS (SELECT 1 FROM dbo.News d WHERE d.Source = s.Source AND d.ExternalId = s.ExternalId);
            INSERT dbo.NewsSymbols (NewsId, Symbol)
            SELECT DISTINCT n.Id, LEFT(LTRIM(RTRIM(v.value)), 16) FROM #news s
            JOIN dbo.News n ON n.Source = s.Source AND n.ExternalId = s.ExternalId
            CROSS APPLY STRING_SPLIT(s.Symbols, ',') v
            WHERE LEN(v.value) > 0 AND NOT EXISTS (SELECT 1 FROM dbo.NewsSymbols x WHERE x.Symbol = LEFT(LTRIM(RTRIM(v.value)), 16) AND x.NewsId = n.Id);
            DROP TABLE #news;
            SELECT COUNT(*) FROM @new;
            """, c, tx);
        return Convert.ToInt32(await insert.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture);
    }

    /// <summary>Splits kept once by (symbol, ex-date). Returns the rows inserted.</summary>
    public static async Task<int> InsertSplitsAsync(SqlConnection c, SqlTransaction tx, IEnumerable<SplitEvent> list, CancellationToken ct)
    {
        var rows = new DataTable();
        rows.Columns.Add("Symbol", typeof(string)); rows.Columns.Add("ExDate", typeof(DateTime)); rows.Columns.Add("OldRate", typeof(decimal)); rows.Columns.Add("NewRate", typeof(decimal));
        foreach (var e in list.Where(e => e.Symbol.Length is > 0 and <= 16).DistinctBy(e => (e.Symbol.ToUpperInvariant(), e.ExDate)))
            rows.Rows.Add(e.Symbol.ToUpperInvariant(), e.ExDate.ToDateTime(TimeOnly.MinValue), e.OldRate, e.NewRate);
        if (rows.Rows.Count == 0) return 0;
        await using (var create = new SqlCommand("CREATE TABLE #splits (Symbol varchar(16), ExDate date, OldRate decimal(19,6), NewRate decimal(19,6))", c, tx))
            await create.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        await SqlHistoryBarCache.BulkAsync(c, tx, "#splits", rows, ct).ConfigureAwait(false);
        await using var insert = new SqlCommand("""
            INSERT dbo.Splits (Symbol, ExDate, OldRate, NewRate, Source)
            SELECT s.Symbol, s.ExDate, s.OldRate, s.NewRate, 'alpaca' FROM #splits s
            WHERE NOT EXISTS (SELECT 1 FROM dbo.Splits d WHERE d.Symbol = s.Symbol AND d.ExDate = s.ExDate);
            DECLARE @n int = @@ROWCOUNT;
            DROP TABLE #splits;
            SELECT @n;
            """, c, tx);
        return Convert.ToInt32(await insert.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Filings kept once by (symbol, filed date, accession). A held row missing its primary document, acceptance time
    /// or items (the study import had none of the document names) is filled in. Returns the rows inserted.
    /// </summary>
    public static async Task<int> InsertFilingsAsync(SqlConnection c, SqlTransaction tx, IEnumerable<(string Symbol, string Cik, SubmissionFiling Filing)> list, CancellationToken ct)
    {
        var rows = new DataTable();
        foreach (var (name, type) in new (string, Type)[] { ("AccessionNumber", typeof(string)), ("Symbol", typeof(string)), ("Cik", typeof(string)), ("FormType", typeof(string)), ("FiledDate", typeof(DateTime)), ("AcceptedUtc", typeof(DateTime)), ("ItemsJson", typeof(string)), ("PrimaryDocument", typeof(string)) })
            rows.Columns.Add(name, type);
        foreach (var (sym, cik, f) in list.Where(x => x.Symbol.Length is > 0 and <= 16 && x.Filing.AccessionNumber.Length <= 32).DistinctBy(x => (x.Symbol, x.Filing.FiledDate, x.Filing.AccessionNumber)))
        {
            var items = f.Items.Count == 0 ? null : System.Text.Json.JsonSerializer.Serialize(f.Items);
            rows.Rows.Add(f.AccessionNumber, sym, cik.Length == 0 ? DBNull.Value : Clip(cik, 16), Clip(f.FormType, 24), f.FiledDate.ToDateTime(TimeOnly.MinValue),
                f.AcceptedUtc is { } a ? a : DBNull.Value, items is null ? DBNull.Value : Clip(items, 400), f.PrimaryDocument is { Length: > 0 } d ? Clip(d, 260) : DBNull.Value);
        }
        if (rows.Rows.Count == 0) return 0;
        await using (var create = new SqlCommand("CREATE TABLE #filings (AccessionNumber varchar(32), Symbol varchar(16), Cik varchar(16), FormType varchar(24), FiledDate date, AcceptedUtc datetime2(0), ItemsJson nvarchar(400), PrimaryDocument nvarchar(260))", c, tx))
            await create.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        await SqlHistoryBarCache.BulkAsync(c, tx, "#filings", rows, ct).ConfigureAwait(false);
        await using var insert = new SqlCommand("""
            UPDATE d SET d.PrimaryDocument = ISNULL(d.PrimaryDocument, s.PrimaryDocument), d.AcceptedUtc = ISNULL(d.AcceptedUtc, s.AcceptedUtc), d.ItemsJson = ISNULL(d.ItemsJson, s.ItemsJson)
            FROM dbo.Filings d JOIN #filings s ON d.Symbol = s.Symbol AND d.FiledDate = s.FiledDate AND d.AccessionNumber = s.AccessionNumber
            WHERE (d.PrimaryDocument IS NULL AND s.PrimaryDocument IS NOT NULL) OR (d.AcceptedUtc IS NULL AND s.AcceptedUtc IS NOT NULL) OR (d.ItemsJson IS NULL AND s.ItemsJson IS NOT NULL);
            INSERT dbo.Filings (AccessionNumber, Symbol, Cik, FormType, FiledDate, AcceptedUtc, ItemsJson, PrimaryDocument)
            SELECT s.AccessionNumber, s.Symbol, s.Cik, s.FormType, s.FiledDate, s.AcceptedUtc, s.ItemsJson, s.PrimaryDocument FROM #filings s
            WHERE NOT EXISTS (SELECT 1 FROM dbo.Filings d WHERE d.Symbol = s.Symbol AND d.FiledDate = s.FiledDate AND d.AccessionNumber = s.AccessionNumber);
            DECLARE @n int = @@ROWCOUNT;
            DROP TABLE #filings;
            SELECT @n;
            """, c, tx);
        return Convert.ToInt32(await insert.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Whether the union of <paramref name="ranges"/> covers every day of [<paramref name="from"/>, <paramref name="to"/>]
    /// (adjacent ranges join: a day-by-day market-wide mark covers a span). An empty span is covered. Pure.
    /// </summary>
    public static bool Covers(IEnumerable<(DateOnly From, DateOnly To)> ranges, DateOnly from, DateOnly to)
    {
        if (to < from) return true;
        var next = from;
        foreach (var r in ranges.OrderBy(r => r.From))
        {
            if (r.From > next) return false;
            if (r.To >= next) next = r.To.AddDays(1);
            if (next > to) return true;
        }
        return false;
    }

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..max];
}
