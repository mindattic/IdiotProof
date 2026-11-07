using System.Collections.Concurrent;
using System.Data;
using System.Globalization;
using IdiotProof.Core.Evidence;
using IdiotProof.DataFeeds;
using IdiotProof.Models;
using Microsoft.Data.SqlClient;

namespace IdiotProof.Core.History;

public sealed record HistoryImportResult(string Step, long Rows, long Skipped, double Seconds, string? Note = null);

/// <summary>
/// Moves data already downloaded into the history database (IP-T454) so it is never fetched again:
/// the gapper study's research database (daily bars, splits, news and their symbols, EDGAR filings,
/// with each fetched span as a coverage range) and its 1-minute file store (every covered day,
/// zero-bar days included). Every step is idempotent: rows whose key is already held are skipped.
/// </summary>
public sealed class HistoryImporter(HistoryDatabase history, Action<string>? log = null)
{
    private const string ResearchSource = "gapper-study";

    private void Log(string message) => log?.Invoke(message);

    // ── research database ─────────────────────────────────────────────────

    public async Task<IReadOnlyList<HistoryImportResult>> ImportResearchAsync(string researchConnectionString, CancellationToken ct)
    {
        var results = new List<HistoryImportResult>
        {
            await StageAndInsertAsync(researchConnectionString, "daily bars",
                "SELECT UPPER(Symbol) AS Symbol, DateEt, [Open], High, Low, [Close], Volume, ISNULL(Feed, '') AS Feed FROM dbo.GapperStudyDailyBars",
                "Symbol varchar(16), DateEt date, [Open] decimal(19,6), High decimal(19,6), Low decimal(19,6), [Close] decimal(19,6), Volume decimal(19,4), Feed varchar(24)",
                """
                INSERT dbo.DailyBars (Symbol, DateEt, [Open], High, Low, [Close], Volume, Feed)
                SELECT s.Symbol, s.DateEt, MIN(s.[Open]), MAX(s.High), MIN(s.Low), MIN(s.[Close]), MAX(s.Volume), MIN(s.Feed) FROM #stage s
                WHERE NOT EXISTS (SELECT 1 FROM dbo.DailyBars d WHERE d.Symbol = s.Symbol AND d.DateEt = s.DateEt)
                GROUP BY s.Symbol, s.DateEt
                """, ct),
            await StageAndInsertAsync(researchConnectionString, "splits",
                "SELECT UPPER(Symbol) AS Symbol, ExDate, OldRate, NewRate FROM dbo.GapperStudySplits",
                "Symbol varchar(16), ExDate date, OldRate decimal(19,6), NewRate decimal(19,6)",
                $"""
                INSERT dbo.Splits (Symbol, ExDate, OldRate, NewRate, Source)
                SELECT s.Symbol, s.ExDate, MIN(s.OldRate), MIN(s.NewRate), '{ResearchSource}' FROM #stage s
                WHERE NOT EXISTS (SELECT 1 FROM dbo.Splits d WHERE d.Symbol = s.Symbol AND d.ExDate = s.ExDate)
                GROUP BY s.Symbol, s.ExDate
                """, ct),
            await StageAndInsertAsync(researchConnectionString, "news",
                "SELECT Id, LEFT(Source, 32) AS Source, LEFT(ExternalId, 128) AS ExternalId, PublishedUtc, ReceivedUtc, LEFT(StampKind, 16) AS StampKind, LEFT(Headline, 1000) AS Headline, LEFT(Url, 1000) AS Url, SourceTier, LEFT(PreClass, 32) AS PreClass, Direction, LEFT(SourceName, 200) AS SourceName, RawSnippet AS Snippet FROM dbo.CatalystEvents",
                "Id uniqueidentifier, Source varchar(32), ExternalId varchar(128), PublishedUtc datetime2(0), ReceivedUtc datetime2(0), StampKind varchar(16), Headline nvarchar(1000), Url nvarchar(1000), SourceTier int, PreClass varchar(32), Direction int, SourceName nvarchar(200), Snippet nvarchar(max)",
                """
                INSERT dbo.News (Id, Source, ExternalId, PublishedUtc, ReceivedUtc, StampKind, Headline, Url, SourceTier, PreClass, Direction, SourceName, Snippet)
                SELECT s.Id, s.Source, s.ExternalId, s.PublishedUtc, s.ReceivedUtc, s.StampKind, ISNULL(s.Headline, N''), s.Url, s.SourceTier, s.PreClass, s.Direction, s.SourceName, s.Snippet
                FROM (SELECT *, ROW_NUMBER() OVER (PARTITION BY Source, ExternalId ORDER BY PublishedUtc) AS rn FROM #stage) s
                WHERE s.rn = 1 AND NOT EXISTS (SELECT 1 FROM dbo.News d WHERE d.Source = s.Source AND d.ExternalId = s.ExternalId) AND NOT EXISTS (SELECT 1 FROM dbo.News d WHERE d.Id = s.Id)
                """, ct),
            await StageAndInsertAsync(researchConnectionString, "news symbols",
                "SELECT EventId AS NewsId, UPPER(LEFT(Symbol, 16)) AS Symbol FROM dbo.CatalystEventSymbols",
                "NewsId uniqueidentifier, Symbol varchar(16)",
                """
                INSERT dbo.NewsSymbols (NewsId, Symbol)
                SELECT DISTINCT s.NewsId, s.Symbol FROM #stage s
                WHERE EXISTS (SELECT 1 FROM dbo.News n WHERE n.Id = s.NewsId)
                  AND NOT EXISTS (SELECT 1 FROM dbo.NewsSymbols d WHERE d.Symbol = s.Symbol AND d.NewsId = s.NewsId)
                """, ct),
            await StageAndInsertAsync(researchConnectionString, "filings",
                "SELECT LEFT(AccessionNumber, 32) AS AccessionNumber, UPPER(LEFT(Symbol, 16)) AS Symbol, LEFT(Cik, 16) AS Cik, LEFT(FormType, 24) AS FormType, FiledDate, AcceptedUtc, LEFT(ItemsJson, 400) AS ItemsJson FROM dbo.EdgarFilingIndex WHERE Symbol IS NOT NULL",
                "AccessionNumber varchar(32), Symbol varchar(16), Cik varchar(16), FormType varchar(24), FiledDate date, AcceptedUtc datetime2(0), ItemsJson nvarchar(400)",
                """
                INSERT dbo.Filings (AccessionNumber, Symbol, Cik, FormType, FiledDate, AcceptedUtc, ItemsJson)
                SELECT s.AccessionNumber, s.Symbol, MIN(s.Cik), MIN(s.FormType), s.FiledDate, MAX(s.AcceptedUtc), MAX(s.ItemsJson) FROM #stage s
                WHERE NOT EXISTS (SELECT 1 FROM dbo.Filings d WHERE d.Symbol = s.Symbol AND d.FiledDate = s.FiledDate AND d.AccessionNumber = s.AccessionNumber)
                GROUP BY s.AccessionNumber, s.Symbol, s.FiledDate
                """, ct),
            await StageAndInsertAsync(researchConnectionString, "coverage ranges",
                $"""
                SELECT LEFT(PARSENAME(REPLACE([Key], '|', '.'), 3), 16) AS Symbol,
                       CASE Tier WHEN 'daily' THEN '{HistoryDatabase.Kinds.Daily}' WHEN 'splits' THEN '{HistoryDatabase.Kinds.Splits}' ELSE '{HistoryDatabase.Kinds.News}' END AS Kind,
                       TRY_CONVERT(date, PARSENAME(REPLACE([Key], '|', '.'), 2)) AS FromEt, TRY_CONVERT(date, PARSENAME(REPLACE([Key], '|', '.'), 1)) AS ToEt,
                       [Count] AS Items, CompletedUtc AS FetchedUtc
                FROM dbo.GapperStudyCoverage WHERE Tier IN ('daily', 'splits', 'news') AND [Key] NOT LIKE '%.%'
                """,
                "Symbol varchar(16), Kind varchar(16), FromEt date, ToEt date, Items int, FetchedUtc datetime2(0)",
                $"""
                INSERT dbo.CoverageRanges (Symbol, Kind, FromEt, ToEt, Items, FetchedUtc, Source)
                SELECT UPPER(s.Symbol), s.Kind, s.FromEt, s.ToEt, MAX(s.Items), MAX(s.FetchedUtc), '{ResearchSource}' FROM #stage s
                WHERE s.Symbol IS NOT NULL AND s.FromEt IS NOT NULL AND s.ToEt IS NOT NULL
                  AND NOT EXISTS (SELECT 1 FROM dbo.CoverageRanges d WHERE d.Symbol = UPPER(s.Symbol) AND d.Kind = s.Kind AND d.FromEt = s.FromEt AND d.ToEt = s.ToEt)
                GROUP BY UPPER(s.Symbol), s.Kind, s.FromEt, s.ToEt
                """, ct),
        };
        return results;
    }

    /// <summary>Stream a source query into a temp table on the history server, then insert the rows not already held.</summary>
    private async Task<HistoryImportResult> StageAndInsertAsync(string sourceConnection, string step, string sourceSql, string stageColumns, string insertSql, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await using var target = await history.OpenAsync(ct).ConfigureAwait(false);
        await using (var create = new SqlCommand($"CREATE TABLE #stage ({stageColumns})", target)) await create.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        long staged;
        await using (var source = new SqlConnection(sourceConnection))
        {
            await source.OpenAsync(ct).ConfigureAwait(false);
            await using var read = new SqlCommand(sourceSql, source) { CommandTimeout = 0 };
            await using var reader = await read.ExecuteReaderAsync(ct).ConfigureAwait(false);
            using var bulk = new SqlBulkCopy(target) { DestinationTableName = "#stage", BulkCopyTimeout = 0, BatchSize = 200_000, EnableStreaming = true, NotifyAfter = 1_000_000 };
            for (int i = 0; i < reader.FieldCount; i++) bulk.ColumnMappings.Add(reader.GetName(i), reader.GetName(i));
            bulk.SqlRowsCopied += (_, e) => Log($"{step}: {e.RowsCopied:N0} staged");
            await bulk.WriteToServerAsync(reader, ct).ConfigureAwait(false);
        }
        await using (var count = new SqlCommand("SELECT COUNT_BIG(*) FROM #stage", target)) staged = (long)(await count.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
        int inserted;
        await using (var insert = new SqlCommand(insertSql, target) { CommandTimeout = 0 }) inserted = await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        Log($"{step}: {inserted:N0} inserted of {staged:N0} read");
        return new HistoryImportResult(step, inserted, staged - inserted, Math.Round(sw.Elapsed.TotalSeconds, 1));
    }

    // ── minute-bar file store ─────────────────────────────────────────────

    /// <summary>
    /// Every covered day of every symbol in the file store that the history database does not cover
    /// yet, one transaction per symbol (bars plus their coverage marks, zero-bar days included).
    /// </summary>
    public async Task<HistoryImportResult> ImportMinuteFilesAsync(string fileStoreRoot, int parallelism, CancellationToken ct, IReadOnlyCollection<string>? onlySymbols = null)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var store = new FileBackedBarStore(fileStoreRoot);
        var symbols = onlySymbols?.Select(x => x.ToUpperInvariant()).ToList() ?? store.Symbols().ToList();
        long bars = 0, days = 0, skipped = 0;
        int done = 0;
        var errors = new ConcurrentBag<string>();
        var from = new DateOnly(2000, 1, 1);
        var to = new DateOnly(2100, 1, 1);
        await Parallel.ForEachAsync(symbols, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, parallelism), CancellationToken = ct }, async (symbol, token) =>
        {
            try
            {
                var fileDays = store.CoveredDays(symbol, from, to);
                if (fileDays.Count == 0) return;
                await using var c = await history.OpenAsync(token).ConfigureAwait(false);
                await using var tx = (SqlTransaction)await c.BeginTransactionAsync(token).ConfigureAwait(false);
                HashSet<DateOnly> held;
                await using (var q = new SqlCommand("SELECT DateEt FROM dbo.Coverage WITH (UPDLOCK, HOLDLOCK) WHERE Symbol = @s AND Kind = @k", c, tx))
                {
                    q.Parameters.Add("@s", SqlDbType.VarChar, 16).Value = symbol;
                    q.Parameters.Add("@k", SqlDbType.VarChar, 16).Value = HistoryDatabase.Kinds.Minute;
                    held = [];
                    await using var r = await q.ExecuteReaderAsync(token).ConfigureAwait(false);
                    while (await r.ReadAsync(token).ConfigureAwait(false)) held.Add(DateOnly.FromDateTime(r.GetDateTime(0)));
                }
                var missing = fileDays.Where(d => !held.Contains(d)).OrderBy(d => d).ToList();
                Interlocked.Add(ref skipped, fileDays.Count - missing.Count);
                if (missing.Count == 0) { await tx.CommitAsync(token).ConfigureAwait(false); return; }
                var read = store.ReadDays(symbol, missing[0], missing[^1]);
                var byDay = read.GroupBy(b => FileBackedBarStore.SessionDate(b.StartUtc)).ToDictionary(g => g.Key, g => (IReadOnlyList<Candle>)g.ToList());
                var dayList = missing.Select(d => (d, byDay.TryGetValue(d, out var list) ? list : (IReadOnlyList<Candle>)[])).ToList();
                await SqlHistoryBarCache.BulkAsync(c, tx, "dbo.MinuteBars", SqlHistoryBarCache.MinuteTable(symbol, dayList, AlpacaDataFeed.SourceLabel("sip")), token).ConfigureAwait(false);
                await SqlHistoryBarCache.BulkAsync(c, tx, "dbo.Coverage", SqlHistoryBarCache.CoverageTable(symbol, HistoryDatabase.Kinds.Minute, dayList.Select(x => (x.d, x.Item2.Count)), DateTime.UtcNow), token).ConfigureAwait(false);
                await tx.CommitAsync(token).ConfigureAwait(false);
                Interlocked.Add(ref bars, dayList.Sum(x => x.Item2.Count));
                Interlocked.Add(ref days, dayList.Count);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                errors.Add($"{symbol}: {ex.Message}");
            }
            finally
            {
                int n = Interlocked.Increment(ref done);
                if (n % 250 == 0) Log($"minute bars: {n:N0}/{symbols.Count:N0} symbols, {Interlocked.Read(ref days):N0} days, {Interlocked.Read(ref bars):N0} bars, {sw.Elapsed.TotalMinutes:0.0} min");
            }
        }).ConfigureAwait(false);
        var note = errors.IsEmpty ? null : $"{errors.Count} symbols failed: " + string.Join("; ", errors.Take(5));
        Log($"minute bars: done, {days:N0} days, {bars:N0} bars{(note is null ? "" : "; " + note)}");
        return new HistoryImportResult("minute bars", bars, skipped, Math.Round(sw.Elapsed.TotalSeconds, 1), note);
    }
}
