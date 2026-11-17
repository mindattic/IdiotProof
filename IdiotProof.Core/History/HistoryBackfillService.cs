using System.Data;
using IdiotProof.Core.Catalysts;
using IdiotProof.Core.Data;
using IdiotProof.Core.Evidence;
using IdiotProof.DataFeeds;
using IdiotProof.Core.Services;
using IdiotProof.Models;
using IdiotProof.Scripting;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IdiotProof.Core.History;

public sealed record BackfillJob(long Id, string Symbol, string Reason, DateOnly FromEt, DateOnly ToEt, string Status);

public sealed record BackfillRunResult(BackfillJob Job, int DailyBars, int MinuteBars, int MonthsFetched, string? Error, int NewsItems = 0, int Splits = 0, int Filings = 0);

/// <summary>What one nightly market-wide reference pass stored: splits and EDGAR filings, and the days it covered.</summary>
public sealed record ReferenceTopUpResult(int Splits, int Filings, DateOnly? SplitsThrough, DateOnly? FilingsThrough, IReadOnlyList<string> Notes);

/// <summary>
/// Keeps the permanent history complete (IP-T454). Every ticker the app has seen (a strategy, a
/// tracked ticker, a gapper candidate, or one named by hand) is queued once for
/// <see cref="Years"/> years of daily and 1-minute extended-hours bars; a nightly pass queues the
/// last session for every symbol already held. Jobs run one at a time through the same read-through
/// <see cref="HistoricalBarStore"/>, so a day already held is never fetched again, paced month by month
/// so a backfill never crowds out the live Monitor's share of the data API.
/// </summary>
public sealed class HistoryBackfillService(
    HistoryDatabase history,
    IMarketDataFeed feed,
    IDbContextFactory<AppDbContext> appDb,
    TimeProvider time,
    ILogger<HistoryBackfillService> log,
    ISplitSource? splits = null,
    IdiotProof.Core.Services.AlpacaNewsService? news = null,
    IdiotProof.Core.Catalysts.EdgarTickerMap? edgarTickers = null,
    IdiotProof.Core.Catalysts.EdgarSubmissions? edgarSubmissions = null,
    IHttpClientFactory? httpFactory = null,
    IdiotProof.Core.Catalysts.EdgarRateGate? edgarGate = null)
{
    public const int Years = 5;
    public const string Pending = "pending";
    public const string Running = "running";
    public const string Done = "done";
    public const string Failed = "failed";

    /// <summary>The CoverageRanges symbol for a market-wide pass (every listed symbol's splits or filings on those days).</summary>
    public const string AllSymbols = "*";

    /// <summary>A job this long or longer (a new ticker's backfill) also fetches its splits and filings; the nightly one-day jobs leave them to <see cref="TopUpReferenceAsync"/>.</summary>
    public const int ReferenceMinDays = 31;

    /// <summary>Pause between monthly minute-bar requests.</summary>
    public TimeSpan MonthPause { get; init; } = TimeSpan.FromSeconds(2);

    private DateOnly TodayEt => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(time.GetUtcNow().UtcDateTime, MarketTime.Eastern));

    /// <summary>Queue symbols for [today - years, yesterday] unless a pending or running job exists or the daily range is already held. Returns the symbols queued.</summary>
    public async Task<IReadOnlyList<string>> EnqueueAsync(IEnumerable<string> symbols, string reason, int years = Years, CancellationToken ct = default)
    {
        var to = TodayEt.AddDays(-1);
        var from = to.AddYears(-years);
        var queued = new List<string>();
        await using var c = await history.OpenAsync(ct).ConfigureAwait(false);
        foreach (var raw in symbols.Select(s => s.Trim().ToUpperInvariant()).Where(s => s.Length is > 0 and <= 16).Distinct())
        {
            await using var q = new SqlCommand("""
                IF NOT EXISTS (SELECT 1 FROM dbo.BackfillJobs WHERE Symbol = @s AND Status IN ('pending', 'running'))
                   AND NOT EXISTS (SELECT 1 FROM dbo.CoverageRanges WHERE Symbol = @s AND Kind = '1Day' AND FromEt <= @a AND ToEt >= DATEADD(day, -7, @b))
                BEGIN
                    INSERT dbo.BackfillJobs (Symbol, Reason, FromEt, ToEt, Status, RequestedUtc) VALUES (@s, @r, @a, @b, 'pending', SYSUTCDATETIME());
                    SELECT 1;
                END
                """, c);
            q.Parameters.Add("@s", SqlDbType.VarChar, 16).Value = raw;
            q.Parameters.Add("@r", SqlDbType.NVarChar, 200).Value = reason.Length > 200 ? reason[..200] : reason;
            q.Parameters.Add("@a", SqlDbType.Date).Value = from.ToDateTime(TimeOnly.MinValue);
            q.Parameters.Add("@b", SqlDbType.Date).Value = to.ToDateTime(TimeOnly.MinValue);
            if (await q.ExecuteScalarAsync(ct).ConfigureAwait(false) is not null) queued.Add(raw);
        }
        if (queued.Count > 0) log.LogInformation("History backfill: queued {Count} symbols ({Reason}).", queued.Count, reason);
        return queued;
    }

    /// <summary>Every symbol the app has met: strategies, tracked tickers and gapper candidates.</summary>
    public async Task<IReadOnlyList<string>> KnownSymbolsAsync(CancellationToken ct)
    {
        await using var db = await appDb.CreateDbContextAsync(ct).ConfigureAwait(false);
        var strategies = await db.Strategies.AsNoTracking().Select(s => s.Symbol).Distinct().ToListAsync(ct).ConfigureAwait(false);
        var tracked = await db.TrackedTickers.AsNoTracking().Select(t => t.Symbol).Distinct().ToListAsync(ct).ConfigureAwait(false);
        var candidates = await db.AutoGapperCandidates.AsNoTracking().Select(a => a.Symbol).Distinct().ToListAsync(ct).ConfigureAwait(false);
        return strategies.Concat(tracked).Concat(candidates)
            .Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim().ToUpperInvariant())
            .Where(s => s.All(ch => char.IsLetterOrDigit(ch) || ch is '.' or '-'))
            .Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal).ToList();
    }

    /// <summary>Queue every known symbol that is not yet held for the full window.</summary>
    public async Task<IReadOnlyList<string>> DiscoverAsync(CancellationToken ct) =>
        await EnqueueAsync(await KnownSymbolsAsync(ct).ConfigureAwait(false), "new ticker", Years, ct).ConfigureAwait(false);

    /// <summary>Queue the last completed session for every symbol the history holds (the read-through store skips what is already there).</summary>
    public async Task<int> QueueNightlyTopUpAsync(CancellationToken ct)
    {
        var to = TodayEt.AddDays(-1);
        while (!MarketCalendarIsTradingDay(to)) to = to.AddDays(-1);
        await using var c = await history.OpenAsync(ct).ConfigureAwait(false);
        await using var q = new SqlCommand("""
            INSERT dbo.BackfillJobs (Symbol, Reason, FromEt, ToEt, Status, RequestedUtc)
            SELECT s.Symbol, N'nightly top-up', @d, @d, 'pending', SYSUTCDATETIME()
            FROM (SELECT DISTINCT Symbol FROM dbo.CoverageRanges WHERE Kind = '1Day') s
            WHERE NOT EXISTS (SELECT 1 FROM dbo.BackfillJobs j WHERE j.Symbol = s.Symbol AND j.Status IN ('pending', 'running'))
              AND NOT EXISTS (SELECT 1 FROM dbo.CoverageRanges r WHERE r.Symbol = s.Symbol AND r.Kind = '1Day' AND r.ToEt >= @d)
            """, c);
        q.Parameters.Add("@d", SqlDbType.Date).Value = to.ToDateTime(TimeOnly.MinValue);
        return await q.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static bool MarketCalendarIsTradingDay(DateOnly d) => MarketCalendar.IsTradingDay(d);

    /// <summary>Claim the oldest pending job (null when the queue is empty).</summary>
    public async Task<BackfillJob?> ClaimAsync(CancellationToken ct)
    {
        await using var c = await history.OpenAsync(ct).ConfigureAwait(false);
        await using var q = new SqlCommand("""
            WITH next AS (SELECT TOP (1) * FROM dbo.BackfillJobs WITH (UPDLOCK, READPAST, ROWLOCK) WHERE Status = 'pending' ORDER BY RequestedUtc, Id)
            UPDATE next SET Status = 'running', StartedUtc = SYSUTCDATETIME()
            OUTPUT inserted.Id, inserted.Symbol, inserted.Reason, inserted.FromEt, inserted.ToEt, inserted.Status;
            """, c);
        await using var r = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await r.ReadAsync(ct).ConfigureAwait(false)) return null;
        return new BackfillJob(r.GetInt64(0), r.GetString(1), r.GetString(2), DateOnly.FromDateTime(r.GetDateTime(3)), DateOnly.FromDateTime(r.GetDateTime(4)), r.GetString(5));
    }

    /// <summary>Run one job: daily bars for the whole window, then minute bars month by month. Bars already held are not fetched.</summary>
    public async Task<BackfillRunResult> RunAsync(BackfillJob job, CancellationToken ct)
    {
        var store = new HistoricalBarStore(new SqlHistoryBarCache(history), feed, time, splits: splits);
        int daily = 0, minute = 0, months = 0, newsItems = 0, splitRows = 0, filingRows = 0;
        string? error = null;
        try
        {
            daily = (await store.GetBarsAsync(job.Symbol, job.FromEt, job.ToEt, BarTimeframe.Day1, ct).ConfigureAwait(false)).Count;
            for (var start = new DateOnly(job.FromEt.Year, job.FromEt.Month, 1); start <= job.ToEt; start = start.AddMonths(1))
            {
                var a = start < job.FromEt ? job.FromEt : start;
                var b = start.AddMonths(1).AddDays(-1);
                if (b > job.ToEt) b = job.ToEt;
                minute += (await store.GetBarsAsync(job.Symbol, a, b, BarTimeframe.Minute1, ct).ConfigureAwait(false)).Count;
                months++;
                if (MonthPause > TimeSpan.Zero && job.FromEt != job.ToEt) await Task.Delay(MonthPause, time, ct).ConfigureAwait(false);
            }
            newsItems = await BackfillNewsAsync(job, ct).ConfigureAwait(false);
            if (job.ToEt.DayNumber - job.FromEt.DayNumber >= ReferenceMinDays)
            {
                splitRows = await BackfillSplitsAsync(job, ct).ConfigureAwait(false);
                filingRows = await BackfillFilingsAsync(job, ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            error = ex.Message;
            log.LogWarning(ex, "History backfill {Symbol} failed.", job.Symbol);
        }
        await FinishAsync(job.Id, error, ct).ConfigureAwait(false);
        return new BackfillRunResult(job, daily, minute, months, error, newsItems, splitRows, filingRows);
    }

    /// <summary>
    /// Alpaca's splits for the symbol over the job's window, kept once by (symbol, ex-date), and the window marked
    /// covered (no splits is an answer, so an empty window is marked too). A failed fetch throws before anything is
    /// marked. Skipped without a real split source (Mock feed, no keys).
    /// </summary>
    private async Task<int> BackfillSplitsAsync(BackfillJob job, CancellationToken ct)
    {
        if (splits is null or NullSplitSource) return 0;
        await using var c = await history.OpenAsync(ct).ConfigureAwait(false);
        var held = await SqlHistoryBarCache.RangesAsync(c, null, job.Symbol, HistoryDatabase.Kinds.Splits, job.FromEt, job.ToEt, ct).ConfigureAwait(false);
        if (held.Any(r => r.From <= job.FromEt && r.To >= job.ToEt)) return 0;
        var got = await splits.GetSplitsAsync([job.Symbol], job.FromEt, job.ToEt, ct).ConfigureAwait(false);
        var list = got.TryGetValue(job.Symbol, out var l) ? l : [];
        await using var tx = (SqlTransaction)await c.BeginTransactionAsync(ct).ConfigureAwait(false);
        var inserted = await InsertSplitsAsync(c, tx, list, ct).ConfigureAwait(false);
        await SqlHistoryBarCache.AddRangeAsync(c, tx, job.Symbol, HistoryDatabase.Kinds.Splits, job.FromEt, job.ToEt, list.Count, time.GetUtcNow().UtcDateTime, "alpaca", ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
        return inserted;
    }

    /// <summary>
    /// Every EDGAR filing (all forms: 8-K with its items, Form 4, S-1/424B, 10-Q, ...) of the symbol's issuer over the
    /// job's window from the SEC submissions history, kept once by (symbol, filed date, accession), and the window
    /// marked covered. A symbol the SEC's ticker list does not carry (most ETFs, some foreign listings) is marked
    /// covered with nothing; an SEC outage marks nothing, so the next pass tries again.
    /// </summary>
    private async Task<int> BackfillFilingsAsync(BackfillJob job, CancellationToken ct)
    {
        if (edgarTickers is null || edgarSubmissions is null) return 0;
        await using var c = await history.OpenAsync(ct).ConfigureAwait(false);
        var held = await SqlHistoryBarCache.RangesAsync(c, null, job.Symbol, HistoryDatabase.Kinds.Filings, job.FromEt, job.ToEt, ct).ConfigureAwait(false);
        if (held.Any(r => r.From <= job.FromEt && r.To >= job.ToEt)) return 0;
        var cik = await edgarTickers.CikForTickerAsync(job.Symbol, ct).ConfigureAwait(false);
        if (cik is null && edgarTickers.Count == 0) return 0; // the SEC ticker list itself is unavailable
        List<SubmissionFiling> filings = [];
        if (cik is not null)
        {
            if (await edgarSubmissions.GetAsync(cik, job.FromEt, job.ToEt, ct).ConfigureAwait(false) is not { } got) return 0;
            filings = got;
        }
        await using var tx = (SqlTransaction)await c.BeginTransactionAsync(ct).ConfigureAwait(false);
        var inserted = await InsertFilingsAsync(c, tx, filings.Select(f => (job.Symbol, cik ?? "", f)), ct).ConfigureAwait(false);
        await SqlHistoryBarCache.AddRangeAsync(c, tx, job.Symbol, HistoryDatabase.Kinds.Filings, job.FromEt, job.ToEt, filings.Count, time.GetUtcNow().UtcDateTime,
            cik is null ? "sec-submissions (no CIK)" : "sec-submissions", ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
        return inserted;
    }

    /// <summary>
    /// The nightly market-wide reference pass: every split with an ex-date since the last pass (one Alpaca request for
    /// the whole market, calendared splits a week ahead included), and every filing on each EDGAR business day since
    /// the last pass, found through EDGAR's daily form index and read in full (items, acceptance time) from each listed
    /// filer's submissions history. Covered days are recorded under symbol <see cref="AllSymbols"/>; at most
    /// <paramref name="maxDays"/> calendar days are caught up.
    /// </summary>
    public async Task<ReferenceTopUpResult> TopUpReferenceAsync(int maxDays = 10, CancellationToken ct = default)
    {
        var notes = new List<string>();
        var through = TodayEt.AddDays(-1);
        int splitRows = 0, filingRows = 0;
        DateOnly? splitsThrough = null, filingsThrough = null;
        await using var c = await history.OpenAsync(ct).ConfigureAwait(false);
        var now = time.GetUtcNow().UtcDateTime;

        if (splits is AlpacaSplitSource alpaca)
        {
            var from = await NextUncoveredAsync(c, HistoryDatabase.Kinds.Splits, through, maxDays, ct).ConfigureAwait(false);
            if (from <= through)
            {
                try
                {
                    var list = await alpaca.GetMarketSplitsAsync(from, through.AddDays(7), ct).ConfigureAwait(false);
                    await using var tx = (SqlTransaction)await c.BeginTransactionAsync(ct).ConfigureAwait(false);
                    splitRows = await InsertSplitsAsync(c, tx, list, ct).ConfigureAwait(false);
                    await SqlHistoryBarCache.AddRangeAsync(c, tx, AllSymbols, HistoryDatabase.Kinds.Splits, from, through, list.Count, now, "alpaca market", ct).ConfigureAwait(false);
                    await tx.CommitAsync(ct).ConfigureAwait(false);
                    splitsThrough = through;
                }
                catch (Exception ex) when (!ct.IsCancellationRequested && ex is HttpRequestException or TaskCanceledException) { notes.Add("splits: " + ex.Message); }
            }
        }
        else notes.Add("splits: no Alpaca split source");

        // filings day by day, so a failed day stops the pass and the next night resumes there
        if (edgarTickers is not null && edgarSubmissions is not null && httpFactory is not null && edgarGate is not null)
        {
            await edgarTickers.EnsureFreshAsync(ct).ConfigureAwait(false);
            if (edgarTickers.Count == 0) notes.Add("filings: SEC ticker list unavailable");
            else
                for (var day = await NextUncoveredAsync(c, HistoryDatabase.Kinds.Filings, through, maxDays, ct).ConfigureAwait(false); day <= through; day = day.AddDays(1))
                {
                    if (await TopUpFilingsDayAsync(c, day, now, notes, ct).ConfigureAwait(false) is not { } n) break;
                    filingRows += n;
                    filingsThrough = day;
                }
        }
        else notes.Add("filings: EDGAR clients not registered");
        log.LogInformation("History reference top-up: {Splits} splits, {Filings} filings{Notes}.", splitRows, filingRows, notes.Count == 0 ? "" : "; " + string.Join("; ", notes));
        return new(splitRows, filingRows, splitsThrough, filingsThrough, notes);
    }

    /// <summary>One EDGAR day: the daily form index, then each listed filer's filings of that day. Null when the SEC failed (nothing marked).</summary>
    private async Task<int?> TopUpFilingsDayAsync(SqlConnection c, DateOnly day, DateTime now, List<string> notes, CancellationToken ct)
    {
        string? index = null;
        if (day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
        {
            try
            {
                var (status, body) = await edgarGate!.RunAsync(async x =>
                {
                    var http = httpFactory!.CreateClient("edgar");
                    using var resp = await http.GetAsync(IdiotProof.Core.Research.Gappers.EdgarFormIndex.DailyUrl(day), x).ConfigureAwait(false);
                    return (resp.StatusCode, resp.IsSuccessStatusCode ? await resp.Content.ReadAsStringAsync(x).ConfigureAwait(false) : null);
                }, ct).ConfigureAwait(false);
                // EDGAR answers a day it was closed (a federal holiday) with 403/404; any other failure is an outage
                if (body is null && status is not (System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Forbidden))
                {
                    notes.Add($"filings {day:yyyy-MM-dd}: EDGAR {(int)status}");
                    return null;
                }
                index = body;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested && ex is HttpRequestException or TaskCanceledException)
            {
                notes.Add($"filings {day:yyyy-MM-dd}: {ex.Message}");
                return null;
            }
        }

        var rows = new List<(string Symbol, string Cik, SubmissionFiling Filing)>();
        if (index is not null)
        {
            var ciks = IdiotProof.Core.Research.Gappers.EdgarFormIndex.Parse(new StringReader(index))
                .Select(e => IdiotProof.Core.Catalysts.EdgarTickerMap.Normalize(e.Cik)).Distinct().ToList();
            foreach (var cik in ciks)
            {
                var tickers = await edgarTickers!.TickersForCikAsync(cik, ct).ConfigureAwait(false);
                if (tickers.Count == 0) continue; // a fund, an insider, a private filer: no listed symbol
                if (await edgarSubmissions!.GetAsync(cik, day, day, ct).ConfigureAwait(false) is not { } got)
                {
                    notes.Add($"filings {day:yyyy-MM-dd}: submissions for CIK {cik} unavailable");
                    return null;
                }
                foreach (var t in tickers) foreach (var f in got) rows.Add((t, cik, f));
            }
        }
        await using var tx = (SqlTransaction)await c.BeginTransactionAsync(ct).ConfigureAwait(false);
        var inserted = await InsertFilingsAsync(c, tx, rows, ct).ConfigureAwait(false);
        await SqlHistoryBarCache.AddRangeAsync(c, tx, AllSymbols, HistoryDatabase.Kinds.Filings, day, day, rows.Count, now,
            index is null ? "sec daily index (closed)" : "sec daily index", ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
        return inserted;
    }

    /// <summary>The first day after the last market-wide coverage of <paramref name="kind"/>, no earlier than <paramref name="maxDays"/> days before <paramref name="through"/>.</summary>
    private static async Task<DateOnly> NextUncoveredAsync(SqlConnection c, string kind, DateOnly through, int maxDays, CancellationToken ct)
    {
        await using var q = new SqlCommand("SELECT MAX(ToEt) FROM dbo.CoverageRanges WHERE Symbol = @s AND Kind = @k", c);
        q.Parameters.Add("@s", SqlDbType.VarChar, 16).Value = AllSymbols;
        q.Parameters.Add("@k", SqlDbType.VarChar, 16).Value = kind;
        var floor = through.AddDays(1 - maxDays);
        return await q.ExecuteScalarAsync(ct).ConfigureAwait(false) is DateTime last && DateOnly.FromDateTime(last).AddDays(1) > floor
            ? DateOnly.FromDateTime(last).AddDays(1) : floor;
    }

    private static async Task<int> InsertSplitsAsync(SqlConnection c, SqlTransaction tx, IEnumerable<SplitEvent> list, CancellationToken ct)
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
        return Convert.ToInt32(await insert.ExecuteScalarAsync(ct).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<int> InsertFilingsAsync(SqlConnection c, SqlTransaction tx, IEnumerable<(string Symbol, string Cik, SubmissionFiling Filing)> list, CancellationToken ct)
    {
        var rows = new DataTable();
        foreach (var (name, type) in new (string, Type)[] { ("AccessionNumber", typeof(string)), ("Symbol", typeof(string)), ("Cik", typeof(string)), ("FormType", typeof(string)), ("FiledDate", typeof(DateTime)), ("AcceptedUtc", typeof(DateTime)), ("ItemsJson", typeof(string)) })
            rows.Columns.Add(name, type);
        foreach (var (sym, cik, f) in list.Where(x => x.Symbol.Length is > 0 and <= 16 && x.Filing.AccessionNumber.Length <= 32).DistinctBy(x => (x.Symbol, x.Filing.FiledDate, x.Filing.AccessionNumber)))
        {
            var items = f.Items.Count == 0 ? null : System.Text.Json.JsonSerializer.Serialize(f.Items);
            rows.Rows.Add(f.AccessionNumber, sym, cik.Length == 0 ? DBNull.Value : cik.Length <= 16 ? cik : cik[..16], f.FormType.Length <= 24 ? f.FormType : f.FormType[..24],
                f.FiledDate.ToDateTime(TimeOnly.MinValue), f.AcceptedUtc is { } a ? a : DBNull.Value, items is null ? DBNull.Value : items.Length <= 400 ? items : items[..400]);
        }
        if (rows.Rows.Count == 0) return 0;
        await using (var create = new SqlCommand("CREATE TABLE #filings (AccessionNumber varchar(32), Symbol varchar(16), Cik varchar(16), FormType varchar(24), FiledDate date, AcceptedUtc datetime2(0), ItemsJson nvarchar(400))", c, tx))
            await create.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        await SqlHistoryBarCache.BulkAsync(c, tx, "#filings", rows, ct).ConfigureAwait(false);
        await using var insert = new SqlCommand("""
            INSERT dbo.Filings (AccessionNumber, Symbol, Cik, FormType, FiledDate, AcceptedUtc, ItemsJson)
            SELECT s.AccessionNumber, s.Symbol, s.Cik, s.FormType, s.FiledDate, s.AcceptedUtc, s.ItemsJson FROM #filings s
            WHERE NOT EXISTS (SELECT 1 FROM dbo.Filings d WHERE d.Symbol = s.Symbol AND d.FiledDate = s.FiledDate AND d.AccessionNumber = s.AccessionNumber);
            DECLARE @n int = @@ROWCOUNT;
            DROP TABLE #filings;
            SELECT @n;
            """, c, tx);
        return Convert.ToInt32(await insert.ExecuteScalarAsync(ct).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Alpaca news tagged with the symbol over the job's window, pre-classified, kept once by
    /// (source, id), and the window marked covered. A multi-year window that returns nothing at all
    /// is not marked: the news API answers an outage with an empty page, and no traded symbol goes
    /// years without a headline, so the next pass tries again.
    /// </summary>
    private async Task<int> BackfillNewsAsync(BackfillJob job, CancellationToken ct)
    {
        if (news is null) return 0;
        await using var c = await history.OpenAsync(ct).ConfigureAwait(false);
        var held = await SqlHistoryBarCache.RangesAsync(c, null, job.Symbol, HistoryDatabase.Kinds.News, job.FromEt, job.ToEt, ct).ConfigureAwait(false);
        if (held.Any(r => r.From <= job.FromEt && r.To >= job.ToEt)) return 0;
        var from = FileBackedBarStore.EtToUtc(job.FromEt, TimeOnly.MinValue);
        var to = FileBackedBarStore.EtToUtc(job.ToEt.AddDays(1), TimeOnly.MinValue);
        var (items, complete) = await news.GetNewsRangeCheckedAsync([job.Symbol], from, to, ct: ct).ConfigureAwait(false);
        bool multiYear = job.ToEt.DayNumber - job.FromEt.DayNumber > 365;
        if (items.Count == 0 && (multiYear || !complete)) return 0;
        // A read cut short (a failed page, the page cap) covers only through the day before its last
        // item (pages run oldest first; that day may be partial). Marking the whole range lost the
        // rest for good, since a covered range is never fetched again.
        var coveredTo = job.ToEt;
        if (!complete)
        {
            coveredTo = DateOnly.FromDateTime(IdiotProof.Evidence.Gappers.MinuteBarMath.ToEastern(items.Max(i => i.CreatedAt))).AddDays(-1);
            if (coveredTo < job.FromEt) coveredTo = job.FromEt.AddDays(-1);
        }

        var rows = new System.Data.DataTable();
        foreach (var (name, type) in new (string, Type)[] { ("Id", typeof(Guid)), ("Source", typeof(string)), ("ExternalId", typeof(string)), ("PublishedUtc", typeof(DateTime)), ("Headline", typeof(string)), ("Url", typeof(string)), ("PreClass", typeof(string)), ("Direction", typeof(int)), ("SourceName", typeof(string)), ("Symbols", typeof(string)) })
            rows.Columns.Add(name, type);
        foreach (var n in items.DistinctBy(n => n.Id))
        {
            var pre = IdiotProof.Core.Catalysts.CatalystPreClassifier.Classify(n.Headline, n.Summary, source: n.Source);
            rows.Rows.Add(Guid.NewGuid(), IdiotProof.Core.Catalysts.CatalystIntake.AlpacaNews, n.Id.Length > 128 ? n.Id[..128] : n.Id, n.CreatedAt,
                n.Headline.Length > 1000 ? n.Headline[..1000] : n.Headline, n.Url.Length > 1000 ? n.Url[..1000] : n.Url, pre.Class.ToString(), pre.Direction,
                n.Source.Length > 200 ? n.Source[..200] : n.Source, string.Join(",", n.Symbols.Select(x => x.ToUpperInvariant()).Where(x => x.Length <= 16)));
        }
        await using var tx = (SqlTransaction)await c.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using (var create = new SqlCommand("CREATE TABLE #news (Id uniqueidentifier, Source varchar(32), ExternalId varchar(128), PublishedUtc datetime2(0), Headline nvarchar(1000), Url nvarchar(1000), PreClass varchar(32), Direction int, SourceName nvarchar(200), Symbols nvarchar(max))", c, tx))
            await create.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        await SqlHistoryBarCache.BulkAsync(c, tx, "#news", rows, ct).ConfigureAwait(false);
        int inserted;
        await using (var insert = new SqlCommand("""
            DECLARE @new TABLE (Id uniqueidentifier, Symbols nvarchar(max));
            INSERT dbo.News (Id, Source, ExternalId, PublishedUtc, ReceivedUtc, StampKind, Headline, Url, SourceTier, PreClass, Direction, SourceName, Snippet)
            OUTPUT inserted.Id, NULL INTO @new
            SELECT s.Id, s.Source, s.ExternalId, s.PublishedUtc, NULL, 'realtime', s.Headline, s.Url, NULL, s.PreClass, s.Direction, s.SourceName, NULL FROM #news s
            WHERE NOT EXISTS (SELECT 1 FROM dbo.News d WHERE d.Source = s.Source AND d.ExternalId = s.ExternalId);
            INSERT dbo.NewsSymbols (NewsId, Symbol)
            SELECT DISTINCT n.Id, LEFT(LTRIM(RTRIM(v.value)), 16) FROM #news s
            JOIN dbo.News n ON n.Source = s.Source AND n.ExternalId = s.ExternalId
            CROSS APPLY STRING_SPLIT(s.Symbols, ',') v
            WHERE LEN(v.value) > 0 AND NOT EXISTS (SELECT 1 FROM dbo.NewsSymbols x WHERE x.Symbol = LEFT(LTRIM(RTRIM(v.value)), 16) AND x.NewsId = n.Id);
            SELECT COUNT(*) FROM @new;
            """, c, tx))
            inserted = (int)(await insert.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
        if (coveredTo >= job.FromEt)
            await SqlHistoryBarCache.AddRangeAsync(c, tx, job.Symbol, HistoryDatabase.Kinds.News, job.FromEt, coveredTo, items.Count, time.GetUtcNow().UtcDateTime, IdiotProof.Core.Catalysts.CatalystIntake.AlpacaNews, ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
        return inserted;
    }

    private async Task FinishAsync(long id, string? error, CancellationToken ct)
    {
        await using var c = await history.OpenAsync(ct).ConfigureAwait(false);
        await using var q = new SqlCommand("UPDATE dbo.BackfillJobs SET Status = @st, FinishedUtc = SYSUTCDATETIME(), Error = @e WHERE Id = @id", c);
        q.Parameters.Add("@st", SqlDbType.VarChar, 16).Value = error is null ? Done : Failed;
        q.Parameters.Add("@e", SqlDbType.NVarChar, 2000).Value = error is null ? DBNull.Value : error.Length > 2000 ? error[..2000] : error;
        q.Parameters.Add("@id", SqlDbType.BigInt).Value = id;
        await q.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <summary>A job left running by a crash or a restart goes back to pending (its finished days are held, so it resumes where it stopped).</summary>
    public async Task<int> RequeueStaleAsync(TimeSpan olderThan, CancellationToken ct)
    {
        await using var c = await history.OpenAsync(ct).ConfigureAwait(false);
        await using var q = new SqlCommand("UPDATE dbo.BackfillJobs SET Status = 'pending', StartedUtc = NULL WHERE Status = 'running' AND StartedUtc < DATEADD(second, -@s, SYSUTCDATETIME())", c);
        q.Parameters.Add("@s", SqlDbType.Int).Value = (int)olderThan.TotalSeconds;
        return await q.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
