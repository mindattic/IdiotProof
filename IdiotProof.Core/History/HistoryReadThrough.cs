using System.Data;
using IdiotProof.Core.Catalysts;
using IdiotProof.Core.Evidence;
using IdiotProof.Core.Services;
using IdiotProof.Evidence.Gappers;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace IdiotProof.Core.History;

/// <summary>
/// The read-through for news and SEC filings (IP-T454): what the history database already holds is served from it;
/// only the days it does not cover are fetched, stored and marked covered, so a range read twice is fetched once.
/// Coverage is marked for finished ET days only: the current day is always fetched live and stored, never marked.
/// When the history server does not answer, every call falls back to the live source unstored, the way
/// <see cref="FallbackBarCache"/> does for bars.
/// </summary>
public sealed class HistoryReadThrough(
    HistoryDatabase history,
    AlpacaNewsService news,
    EdgarService edgar,
    TimeProvider time,
    ILogger<HistoryReadThrough> log,
    EdgarTickerMap? edgarTickers = null,
    EdgarSubmissions? edgarSubmissions = null)
{
    private const int NewsChunk = 50;

    private DateOnly TodayEt => DateOnly.FromDateTime(MinuteBarMath.ToEastern(time.GetUtcNow().UtcDateTime));

    // ── news ──

    /// <summary>
    /// Every Alpaca news item tagged with any of <paramref name="symbols"/> and published in [fromUtc, toUtc), oldest
    /// first. Symbols whose finished days are all covered come from the history database; the rest are fetched for
    /// the whole finished-day span, stored and marked; the current day is fetched for every symbol.
    /// </summary>
    public async Task<List<AlpacaNewsItem>> GetNewsAsync(IReadOnlyCollection<string> symbols, DateTime fromUtc, DateTime toUtc,
        (string KeyId, string Secret)? keys = null, CancellationToken ct = default)
    {
        var syms = symbols.Select(s => s.Trim().ToUpperInvariant()).Where(s => s.Length is > 0 and <= 16).Distinct(StringComparer.Ordinal).ToList();
        if (syms.Count == 0 || toUtc <= fromUtc) return [];
        var fromEt = DateOnly.FromDateTime(MinuteBarMath.ToEastern(fromUtc));
        var toEt = DateOnly.FromDateTime(MinuteBarMath.ToEastern(toUtc.AddTicks(-1)));
        var today = TodayEt;
        var doneTo = toEt < today ? toEt : today.AddDays(-1);
        try
        {
            await using var c = await history.OpenAsync(ct).ConfigureAwait(false);
            if (fromEt <= doneTo)
            {
                var held = await RangesAsync(c, syms, HistoryDatabase.Kinds.News, fromEt, doneTo, ct).ConfigureAwait(false);
                var missing = syms.Where(s => !HistoryWrites.Covers(held.GetValueOrDefault(s) ?? [], fromEt, doneTo)).ToList();
                foreach (var chunk in missing.Chunk(NewsChunk))
                {
                    var (items, complete) = await news.GetNewsRangeCheckedAsync(chunk, FileBackedBarStore.EtToUtc(fromEt, TimeOnly.MinValue),
                        FileBackedBarStore.EtToUtc(doneTo.AddDays(1), TimeOnly.MinValue), keys: keys, ct: ct).ConfigureAwait(false);
                    // an outage answers with an empty page, and no symbol set goes a year without news: never mark that
                    var mark = complete && !(items.Count == 0 && doneTo.DayNumber - fromEt.DayNumber > 365);
                    await StoreNewsAsync(c, items, mark ? chunk : [], fromEt, doneTo, ct).ConfigureAwait(false);
                }
            }
            if (toEt >= today)
            {
                var liveFrom = FileBackedBarStore.EtToUtc(today, TimeOnly.MinValue);
                if (liveFrom < fromUtc) liveFrom = fromUtc;
                foreach (var chunk in syms.Chunk(NewsChunk))
                {
                    var (items, _) = await news.GetNewsRangeCheckedAsync(chunk, liveFrom, toUtc, keys: keys, ct: ct).ConfigureAwait(false);
                    await StoreNewsAsync(c, items, [], fromEt, doneTo, ct).ConfigureAwait(false);
                }
            }
            return await ReadNewsAsync(c, syms, fromUtc, toUtc, ct).ConfigureAwait(false);
        }
        catch (SqlException ex)
        {
            log.LogWarning("History read-through: news served live, history database unavailable ({Message}).", ex.Message);
            var all = new List<AlpacaNewsItem>();
            foreach (var chunk in syms.Chunk(NewsChunk))
                all.AddRange((await news.GetNewsRangeCheckedAsync(chunk, fromUtc, toUtc, keys: keys, ct: ct).ConfigureAwait(false)).Items);
            return all.DistinctBy(n => n.Id).OrderBy(n => n.CreatedAt).ToList();
        }
    }

    /// <summary>The newest <paramref name="limit"/> items of the last <paramref name="daysBack"/> days for one ticker (the old live call's shape).</summary>
    public async Task<List<AlpacaNewsArticle>> GetRecentNewsAsync(string ticker, int daysBack, int limit = 50, CancellationToken ct = default)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var items = await GetNewsAsync([ticker], now.AddDays(-daysBack), now, ct: ct).ConfigureAwait(false);
        return items.OrderByDescending(n => n.CreatedAt).Take(limit).Select(n => new AlpacaNewsArticle(n.Headline, n.Summary, n.Source, n.Url, n.CreatedAt)).ToList();
    }

    private async Task StoreNewsAsync(SqlConnection c, List<AlpacaNewsItem> items, IReadOnlyList<string> markSymbols, DateOnly fromEt, DateOnly toEt, CancellationToken ct)
    {
        if (items.Count == 0 && markSymbols.Count == 0) return;
        await using var tx = (SqlTransaction)await c.BeginTransactionAsync(ct).ConfigureAwait(false);
        await HistoryWrites.InsertNewsAsync(c, tx, items, ct).ConfigureAwait(false);
        var now = time.GetUtcNow().UtcDateTime;
        foreach (var s in markSymbols)
            await SqlHistoryBarCache.AddRangeAsync(c, tx, s, HistoryDatabase.Kinds.News, fromEt, toEt,
                items.Count(n => n.Symbols.Contains(s, StringComparer.OrdinalIgnoreCase)), now, CatalystIntake.AlpacaNews, ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    private static async Task<List<AlpacaNewsItem>> ReadNewsAsync(SqlConnection c, List<string> syms, DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        await using var q = new SqlCommand("""
            SELECT n.ExternalId, n.Headline, ISNULL(n.Snippet, N''), ISNULL(n.SourceName, N''), ISNULL(n.Url, N''), n.PublishedUtc,
                   (SELECT STRING_AGG(x.Symbol, ',') FROM dbo.NewsSymbols x WHERE x.NewsId = n.Id)
            FROM dbo.News n
            WHERE n.Source = @src AND n.PublishedUtc >= @a AND n.PublishedUtc < @b
              AND EXISTS (SELECT 1 FROM dbo.NewsSymbols s WHERE s.NewsId = n.Id AND s.Symbol IN (SELECT value FROM STRING_SPLIT(@syms, ',')))
            ORDER BY n.PublishedUtc
            """, c);
        q.Parameters.Add("@src", SqlDbType.VarChar, 32).Value = CatalystIntake.AlpacaNews;
        q.Parameters.Add("@a", SqlDbType.DateTime2).Value = fromUtc;
        q.Parameters.Add("@b", SqlDbType.DateTime2).Value = toUtc;
        q.Parameters.Add("@syms", SqlDbType.NVarChar, -1).Value = string.Join(",", syms);
        var list = new List<AlpacaNewsItem>();
        await using var r = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
            list.Add(new AlpacaNewsItem(r.GetString(0), r.GetString(1), r.GetString(2), "", r.GetString(3), r.GetString(4),
                DateTime.SpecifyKind(r.GetDateTime(5), DateTimeKind.Utc), null,
                r.IsDBNull(6) ? [] : r.GetString(6).Split(',', StringSplitOptions.RemoveEmptyEntries)));
        return list;
    }

    // ── filings ──

    /// <summary>
    /// The symbol's EDGAR filings with a filing date in [from, to] (every form, or only <paramref name="forms"/>),
    /// oldest first. Served from the history database when the finished days are covered (by the symbol's own
    /// backfill or the nightly market-wide pass); otherwise the issuer's submissions history is read once, stored and
    /// marked. Without the EDGAR clients or the history database it falls back to EDGAR full-text search, unstored.
    /// </summary>
    public async Task<List<EdgarFiling>> GetFilingsAsync(string symbol, DateOnly from, DateOnly to, IReadOnlyCollection<string>? forms = null, CancellationToken ct = default)
    {
        var sym = symbol.Trim().ToUpperInvariant();
        if (sym.Length is 0 or > 16 || to < from) return [];
        var formSet = forms is { Count: > 0 } ? forms.ToHashSet(StringComparer.OrdinalIgnoreCase) : null;
        if (edgarTickers is null || edgarSubmissions is null) return await LiveFilingsAsync(sym, from, to, formSet, ct).ConfigureAwait(false);
        var today = TodayEt;
        var doneTo = to < today ? to : today.AddDays(-1);
        try
        {
            await using var c = await history.OpenAsync(ct).ConfigureAwait(false);
            bool covered = true;
            if (from <= doneTo)
            {
                var held = await RangesAsync(c, [sym, HistoryBackfillService.AllSymbols], HistoryDatabase.Kinds.Filings, from, doneTo, ct).ConfigureAwait(false);
                covered = HistoryWrites.Covers((held.GetValueOrDefault(sym) ?? []).Concat(held.GetValueOrDefault(HistoryBackfillService.AllSymbols) ?? []), from, doneTo);
            }
            if (!covered || to >= today)
            {
                var cik = await edgarTickers.CikForTickerAsync(sym, ct).ConfigureAwait(false);
                List<SubmissionFiling>? got = cik is null ? (edgarTickers.Count > 0 ? [] : null) : await edgarSubmissions.GetAsync(cik, from, to, ct).ConfigureAwait(false);
                if (got is null)
                {
                    log.LogDebug("History read-through: SEC submissions for {Symbol} unavailable; serving what is held.", sym);
                }
                else
                {
                    await using var tx = (SqlTransaction)await c.BeginTransactionAsync(ct).ConfigureAwait(false);
                    await HistoryWrites.InsertFilingsAsync(c, tx, got.Select(f => (sym, cik ?? "", f)), ct).ConfigureAwait(false);
                    if (!covered && from <= doneTo)
                        await SqlHistoryBarCache.AddRangeAsync(c, tx, sym, HistoryDatabase.Kinds.Filings, from, doneTo, got.Count(f => f.FiledDate <= doneTo), time.GetUtcNow().UtcDateTime,
                            cik is null ? "sec-submissions (no CIK)" : "sec-submissions", ct).ConfigureAwait(false);
                    await tx.CommitAsync(ct).ConfigureAwait(false);
                }
            }
            return await ReadFilingsAsync(c, sym, from, to, formSet, ct).ConfigureAwait(false);
        }
        catch (SqlException ex)
        {
            log.LogWarning("History read-through: filings for {Symbol} served live, history database unavailable ({Message}).", sym, ex.Message);
            return await LiveFilingsAsync(sym, from, to, formSet, ct).ConfigureAwait(false);
        }
    }

    /// <summary>The last <paramref name="daysBack"/> ET days of one form list for a ticker (the old live call's shape).</summary>
    public Task<List<EdgarFiling>> GetRecentFilingsAsync(string ticker, string forms, int daysBack, CancellationToken ct = default)
    {
        var today = TodayEt;
        return GetFilingsAsync(ticker, today.AddDays(-daysBack), today, forms.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), ct);
    }

    private async Task<List<EdgarFiling>> LiveFilingsAsync(string sym, DateOnly from, DateOnly to, HashSet<string>? forms, CancellationToken ct) =>
        await edgar.GetFilingsAsync(sym, forms is null ? "" : string.Join(",", forms), from, to, ct).ConfigureAwait(false);

    private static async Task<List<EdgarFiling>> ReadFilingsAsync(SqlConnection c, string sym, DateOnly from, DateOnly to, HashSet<string>? forms, CancellationToken ct)
    {
        await using var q = new SqlCommand("""
            SELECT FormType, FiledDate, AccessionNumber, Cik, ItemsJson, PrimaryDocument
            FROM dbo.Filings WHERE Symbol = @s AND FiledDate >= @a AND FiledDate <= @b ORDER BY FiledDate, AccessionNumber
            """, c);
        q.Parameters.Add("@s", SqlDbType.VarChar, 16).Value = sym;
        q.Parameters.Add("@a", SqlDbType.Date).Value = from.ToDateTime(TimeOnly.MinValue);
        q.Parameters.Add("@b", SqlDbType.Date).Value = to.ToDateTime(TimeOnly.MinValue);
        var list = new List<EdgarFiling>();
        await using var r = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            var form = r.GetString(0);
            if (forms is not null && !forms.Contains(form)) continue;
            var accession = r.GetString(2);
            var cik = r.IsDBNull(3) ? null : EdgarTickerMap.Normalize(r.GetString(3));
            string[]? items = r.IsDBNull(4) ? null : System.Text.Json.JsonSerializer.Deserialize<string[]>(r.GetString(4));
            var browse = cik is not null
                ? $"https://www.sec.gov/Archives/edgar/data/{cik}/{accession.Replace("-", "")}/{accession}-index.htm"
                : $"https://www.sec.gov/cgi-bin/browse-edgar?action=getcompany&CIK={Uri.EscapeDataString(sym)}&type={Uri.EscapeDataString(form)}&dateb=&owner=include&count=10";
            list.Add(new EdgarFiling(form, DateOnly.FromDateTime(r.GetDateTime(1)).ToString("yyyy-MM-dd"), sym, accession, browse,
                SubmissionFiling.RawDocument(r.IsDBNull(5) ? null : r.GetString(5)), cik, items));
        }
        return list;
    }

    // ── coverage ──

    /// <summary>Covered ranges of <paramref name="kind"/> overlapping [from, to] for many symbols in one query.</summary>
    private static async Task<Dictionary<string, List<(DateOnly From, DateOnly To)>>> RangesAsync(SqlConnection c, IReadOnlyCollection<string> symbols, string kind, DateOnly from, DateOnly to, CancellationToken ct)
    {
        await using var q = new SqlCommand("""
            SELECT Symbol, FromEt, ToEt FROM dbo.CoverageRanges
            WHERE Kind = @k AND ToEt >= @a AND FromEt <= @b AND Symbol IN (SELECT value FROM STRING_SPLIT(@syms, ','))
            """, c);
        q.Parameters.Add("@k", SqlDbType.VarChar, 16).Value = kind;
        q.Parameters.Add("@a", SqlDbType.Date).Value = from.AddDays(-1).ToDateTime(TimeOnly.MinValue);
        q.Parameters.Add("@b", SqlDbType.Date).Value = to.ToDateTime(TimeOnly.MinValue);
        q.Parameters.Add("@syms", SqlDbType.NVarChar, -1).Value = string.Join(",", symbols);
        var map = new Dictionary<string, List<(DateOnly, DateOnly)>>(StringComparer.Ordinal);
        await using var r = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            var s = r.GetString(0);
            if (!map.TryGetValue(s, out var l)) map[s] = l = [];
            l.Add((DateOnly.FromDateTime(r.GetDateTime(1)), DateOnly.FromDateTime(r.GetDateTime(2))));
        }
        return map;
    }
}
