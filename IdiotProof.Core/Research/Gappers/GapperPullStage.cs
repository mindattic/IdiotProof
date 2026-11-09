using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text.Json;
using IdiotProof.Core.Catalysts;
using IdiotProof.Core.Data;
using IdiotProof.Core.Evidence;
using IdiotProof.Core.Services;
using IdiotProof.DataFeeds;
using IdiotProof.Models;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Core.Research.Gappers;

public sealed record PullStageRequest(string Tier, bool Smoke, Guid? Owner, int MaxCalls, DateOnly? From, DateOnly? To);

/// <summary>What one tier did: rows written, API calls, minutes, and a coverage figure where one is defined.</summary>
public sealed record TierSummary(string Tier, int Rows, int Calls, double Minutes, double? CoveragePct, string Note);

/// <summary>
/// <c>research gappers pull</c>: T2 minute bars (candidate days, D-1..D-5, then the matched control
/// days once gappers are classified), T3 news through the catalyst backfill, T5 ticker↔CIK, T4 the
/// EDGAR form index plus acceptance times, T6 splits. Every tier is resumable by coverage mark and
/// stops at <c>--max-calls</c>; nothing here changes a frozen definition.
/// </summary>
public sealed partial class GapperPullStage(
    GapperStudyServices s,
    CatalystBackfillService backfill,
    EdgarService edgar,
    EdgarFilingHeaders headers,
    EdgarRateGate edgarGate,
    IHttpClientFactory httpFactory,
    Form4Parser form4Parser)
{
    public const string BarsTier = "bars";
    public const string NewsTier = "news";
    public const string CikTier = "cik";
    public const string EdgarIndexTier = "edgar-index";
    public const string EdgarHeaderTier = "edgar-header";
    public const string EdgarIndexAmendmentTier = "edgar-index-a01";
    public const string Form4Tier = "form4";

    public async Task<List<TierSummary>> RunAsync(PullStageRequest req, CancellationToken ct)
    {
        var tiers = req.Tier.ToLowerInvariant() switch
        {
            "all" => new[] { "splits", BarsTier, NewsTier, CikTier, "edgar", Form4Tier },
            var t => new[] { t },
        };
        var calls = new CallBudget(req.MaxCalls);
        var results = new List<TierSummary>();
        foreach (var tier in tiers)
        {
            results.Add(tier switch
            {
                BarsTier => await PullBarsAsync(req, calls, ct),
                NewsTier => await PullNewsAsync(req, calls, ct),
                CikTier => await MapCiksAsync(req, calls, ct),
                "edgar" => await PullEdgarAsync(req, calls, ct),
                Form4Tier => await PullForm4Async(req, calls, ct),
                "splits" => await PullSplitsAsync(req, calls, ct),
                EdgarDilutionTier => await PullDilutionIndexAsync(req, calls, ct),
                EdgarDilutionTextTier => await PullDilutionTextAsync(req, calls, ct),
                _ => throw new ArgumentException($"Unknown tier '{tier}'. Use bars, news, edgar, form4, cik, splits, all, or the exploration-only {EdgarDilutionTier} / {EdgarDilutionTextTier}."),
            });
        }
        return results;
    }

    // ── Form 4 purchases (feature form4BuysD30) ──────────────────────────

    /// <summary>
    /// For every Form 4 in the index filed inside [D - lookback, D] of a candidate day, fetches the
    /// submission text, parses the ownership XML and writes one Insider claim per filing with its
    /// transaction lines, so <c>form4BuysD30</c> and <c>research insiders</c> read the same rows.
    /// Resumable per accession; a filing whose claim already exists is only marked.
    /// </summary>
    private static readonly TimeSpan[] EdgarRetryDelays = [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)];

    /// <summary>
    /// One EDGAR document through the gate. A client timeout or network error is retried after 2, 10 and 30 s,
    /// then returns null like a non-success status (the filing stays unmarked, so the next pull retries it):
    /// one slow response once aborted `pull all` 160,400 filings in (IP-T466). Caller cancellation still throws.
    /// </summary>
    private async Task<string?> FetchEdgarTextAsync(string url, CancellationToken ct)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                return await edgarGate.RunAsync(async c =>
                {
                    var http = httpFactory.CreateClient(s.Params.DataTiers.EdgarHttpClientName);
                    using var resp = await http.GetAsync(url, c);
                    return resp.IsSuccessStatusCode ? await resp.Content.ReadAsStringAsync(c) : null;
                }, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested && ex is TaskCanceledException or HttpRequestException or IOException)
            {
                if (attempt >= EdgarRetryDelays.Length)
                {
                    s.Logger.LogWarning("EDGAR {Url} failed after {Attempts} attempts: {Error}", url, attempt + 1, ex.Message);
                    return null;
                }
                s.Logger.LogWarning("EDGAR {Url} attempt {Attempt} failed ({Error}); retrying in {Delay}", url, attempt + 1, ex.Message, EdgarRetryDelays[attempt]);
                await Task.Delay(EdgarRetryDelays[attempt], ct);
            }
        }
    }

    private async Task<TierSummary> PullForm4Async(PullStageRequest req, CallBudget calls, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var p = s.Params;
        var lookback = p.Features.Form4.LookbackDays;
        var candidates = await TargetDaysAsync(req, ct);
        if (candidates.Count == 0) return new TierSummary(Form4Tier, 0, 0, 0, null, "no candidate days in scope");
        var windows = candidates.GroupBy(c => c.Symbol).ToDictionary(g => g.Key, g => g.Select(c => (From: c.DateEt.AddDays(-lookback), To: c.DateEt)).ToList(), StringComparer.Ordinal);
        var from = candidates.Min(c => c.DateEt).AddDays(-lookback);
        var to = candidates.Max(c => c.DateEt);
        var covered = await s.CoveredAsync(Form4Tier, ct);

        List<EdgarFilingIndexRow> filings;
        await using (var db = await s.Db.CreateDbContextAsync(ct))
        {
            var symbols = windows.Keys.ToList();
            filings = await db.EdgarFilingIndex.AsNoTracking()
                .Where(f => f.FormType == Form4Submission.FormType && f.Symbol != null && symbols.Contains(f.Symbol) && f.FiledDate >= from && f.FiledDate <= to)
                .OrderBy(f => f.FiledDate).ThenBy(f => f.Symbol).ToListAsync(ct);
        }
        var pending = filings.Where(f => !covered.Contains(Key(f)) && windows[f.Symbol!].Any(w => f.FiledDate >= w.From && f.FiledDate <= w.To)).ToList();
        s.Logger.LogInformation("[{Now}] form4: {Pending} of {Total} indexed Form 4 filings inside [D-{Lookback}, D] still to fetch", GapperStudyServices.Et(s.UtcNow), pending.Count, filings.Count, lookback);

        int fetched = 0, claims = 0, lines = 0, purchases = 0, skipped = 0;
        foreach (var f in pending)
        {
            if (!calls.Allow()) { s.Logger.LogWarning("form4: --max-calls reached"); break; }
            var url = Form4Submission.Url(f.Cik, f.AccessionNumber);
            await using var db = await s.Db.CreateDbContextAsync(ct);
            if (await db.ResearchClaims.AnyAsync(c => c.SourceUrl == url, ct))
            {
                skipped++;
                await s.MarkAsync(Form4Tier, [(Key(f), 0)], ct);
                continue;
            }
            fetched++;
            var text = await FetchEdgarTextAsync(url, ct);
            if (text is null) { s.Logger.LogWarning("form4: {Url} not fetched", url); continue; }
            var xml = Form4Submission.ExtractOwnershipXml(text);
            var transactions = xml is null ? [] : form4Parser.Parse(xml, url);
            var buys = transactions.Where(t => t.TransactionCode == p.Features.Form4.TransactionCode).ToList();
            var summary = transactions.Count == 0
                ? $"SEC Form 4 filed {f.FiledDate:yyyy-MM-dd} by {f.CompanyName}: no non-derivative lines parsed."
                : string.Join(" ", transactions.Take(4).Select(form4Parser.Summarize));
            var claim = new ResearchClaim
            {
                Ticker = f.Symbol!, SourceName = Form4Submission.SourceName, SourceUrl = url, SourceTier = 1, ArticleDate = f.FiledDate,
                ClaimSummary = summary.Length > 500 ? summary[..500] : summary, ClaimType = Form4Submission.ClaimType,
                Sentiment = buys.Count > 0 ? "Bullish" : "Neutral", Magnitude = "Low", HasHappenedAlready = true, Status = "Realized", CreatedUtc = s.UtcNow,
            };
            db.ResearchClaims.Add(claim);
            foreach (var t in transactions)
            {
                t.ClaimId = claim.Id;
                t.FilingUrl = url;
                t.CreatedUtc = s.UtcNow;
                db.InsiderTransactions.Add(t);
            }
            await db.SaveChangesAsync(ct);
            claims++;
            lines += transactions.Count;
            purchases += buys.Count;
            await s.MarkAsync(Form4Tier, [(Key(f), transactions.Count)], ct);
            if (fetched % 100 == 0) s.Logger.LogInformation("[{Now}] form4: {Fetched} fetched, {Claims} claims, {Lines} lines ({Buys} purchases)", GapperStudyServices.Et(s.UtcNow), fetched, claims, lines, purchases);
        }
        var done = filings.Count(f => covered.Contains(Key(f))) + fetched + skipped;
        var inScope = filings.Count(f => windows[f.Symbol!].Any(w => f.FiledDate >= w.From && f.FiledDate <= w.To));
        return new TierSummary(Form4Tier, lines, fetched, Math.Round(sw.Elapsed.TotalMinutes, 2), inScope == 0 ? null : Math.Round(100.0 * Math.Min(done, inScope) / inScope, 2),
            $"{claims} filings parsed, {lines} lines, {purchases} code-{p.Features.Form4.TransactionCode} purchases, {skipped} already on file");

        static string Key(EdgarFilingIndexRow f) => $"{f.Cik}|{f.AccessionNumber}";
    }

    // ── Scope ────────────────────────────────────────────────────────────

    private IQueryable<GapperStudyUniverseDay> Scoped(AppDbContext db, PullStageRequest req)
    {
        var q = db.GapperStudyUniverseDays.AsNoTracking();
        q = req.Smoke ? q.Where(r => r.Split == GapperStudyServices.SmokeSplit) : q.Where(r => r.Split != GapperStudyServices.SmokeSplit);
        if (req.From is { } f) q = q.Where(r => r.DateEt >= f);
        if (req.To is { } t) q = q.Where(r => r.DateEt <= t);
        return q;
    }

    private async Task<List<(string Symbol, DateOnly DateEt)>> CandidateDaysAsync(PullStageRequest req, CancellationToken ct)
    {
        await using var db = await s.Db.CreateDbContextAsync(ct);
        return (await Scoped(db, req).Where(r => r.IsCandidate).Select(r => new { r.Symbol, r.DateEt }).ToListAsync(ct))
            .Select(x => (x.Symbol, x.DateEt)).OrderBy(x => x.DateEt).ThenBy(x => x.Symbol, StringComparer.Ordinal).ToList();
    }

    private async Task<HashSet<(string Symbol, DateOnly DateEt)>> PopulationSampleAsync(PullStageRequest req, CancellationToken ct)
    {
        await using var db = await s.Db.CreateDbContextAsync(ct);
        return await s.PopulationSampleAsync(Scoped(db, req), ct);
    }

    /// <summary>The days every fetching tier serves: candidate days, plus amendment 01's population sample so sample rows get exactly the candidates' data tiers.</summary>
    private async Task<List<(string Symbol, DateOnly DateEt)>> TargetDaysAsync(PullStageRequest req, CancellationToken ct)
    {
        var days = await CandidateDaysAsync(req, ct);
        var sample = await PopulationSampleAsync(req, ct);
        if (sample.Count == 0) return days;
        s.Logger.LogInformation("[{Now}] amendment {Id}: {Sample} population-sample days join {Candidates} candidate days", GapperStudyServices.Et(s.UtcNow), s.Amendment!.Id, sample.Count, days.Count);
        return days.Concat(sample).Distinct().OrderBy(x => x.DateEt).ThenBy(x => x.Symbol, StringComparer.Ordinal).ToList();
    }

    // ── T2 bars ──────────────────────────────────────────────────────────

    private async Task<TierSummary> PullBarsAsync(PullStageRequest req, CallBudget calls, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var keys = await s.ResolveKeysAsync(req.Owner, ct);
        await using var feed = s.Feed(keys);
        var store = await s.StoreAsync(ct);
        var candidates = await TargetDaysAsync(req, ct);
        int priorDays = s.Params.DataTiers.MinuteBars.PriorDays;

        var wanted = new HashSet<(string, DateOnly)>();
        foreach (var (sym, d) in candidates)
        {
            wanted.Add((sym, d));
            foreach (var prior in MarketCalendar.PriorTradingDays(d, priorDays)) wanted.Add((sym, prior));
        }
        s.Logger.LogInformation("[{Now}] bars: {Cands} candidate days → {Days} symbol-days wanted (incl. D-1..D-{Prior})", GapperStudyServices.Et(s.UtcNow), candidates.Count, wanted.Count, priorDays);
        var (calls1, days1) = await PullSymbolDaysAsync(feed, store, wanted, calls, ct);

        var (events, controls) = await ClassifyAndAssignControlsAsync(req, store, ct);

        var controlDays = new HashSet<(string, DateOnly)>();
        if (s.Params.DataTiers.MinuteBars.IncludeControlDay)
        {
            await using var db = await s.Db.CreateDbContextAsync(ct);
            foreach (var r in await Scoped(db, req).Where(r => r.ControlForSymbol != null).Select(r => new { r.Symbol, r.DateEt }).ToListAsync(ct))
                controlDays.Add((r.Symbol, r.DateEt));
        }
        var (calls2, days2) = await PullSymbolDaysAsync(feed, store, controlDays, calls, ct);

        var coverage = await BarCoverageAsync(req, store, ct);
        await GapperStudySample.WriteAsync(s, req.Smoke, new Dictionary<string, object?>
        {
            ["pull"] = new { barsCalls = calls1 + calls2, symbolDaysWritten = days1 + days2, events, controls, barCoveragePct = coverage.Pct, sipShare = coverage.SipShare, minutes = Math.Round(sw.Elapsed.TotalMinutes, 2) },
        }, ct);
        return new TierSummary(BarsTier, days1 + days2, calls1 + calls2, Math.Round(sw.Elapsed.TotalMinutes, 2), coverage.Pct,
            $"{events} gapper events, {controls} controls assigned, SIP share {coverage.SipShare:P1}");
    }

    /// <summary>Groups wanted symbol-days by day, 100 symbols per request over that day's 04:00-20:00 ET window; a day block is written per symbol even when empty.</summary>
    private async Task<(int Calls, int Days)> PullSymbolDaysAsync(AlpacaDataFeed feed, FileBackedBarStore store, HashSet<(string Symbol, DateOnly DateEt)> wanted, CallBudget calls, CancellationToken ct)
    {
        var todayEt = DateOnly.FromDateTime(EasternTime.FromUtc(s.UtcNow));
        var byDay = wanted.Where(w => w.DateEt < todayEt && MarketCalendar.IsTradingDay(w.DateEt))
            .GroupBy(w => w.DateEt).OrderBy(g => g.Key).ToList();
        int callCount = 0, written = 0, dayIndex = 0;
        foreach (var day in byDay)
        {
            dayIndex++;
            var symbols = day.Select(w => w.Symbol).Where(sym => !store.HasDay(sym, day.Key)).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();
            if (symbols.Count == 0) continue;
            var (startUtc, endUtc) = FileBackedBarStore.SessionWindow(day.Key, day.Key);
            foreach (var chunk in symbols.Chunk(AlpacaDataFeed.MaxSymbolsPerRequest))
            {
                if (!calls.Allow()) { s.Logger.LogWarning("bars: --max-calls reached after {Calls} calls", callCount); return (callCount, written); }
                MultiSymbolBars result;
                try
                {
                    result = await feed.GetHistoricalCandlesForSymbolsAsync(chunk, TimeSpan.FromMinutes(1), startUtc, endUtc, 10000, s.RateGate.WaitAsync, ct);
                    callCount += result.Pages;
                }
                catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity or HttpStatusCode.NotFound)
                {
                    s.Logger.LogWarning("bars: {Day} chunk rejected ({Status}); skipping {Count} symbols this pass", day.Key, ex.StatusCode, chunk.Length);
                    callCount++;
                    continue;
                }
                foreach (var sym in chunk)
                {
                    var bars = result.Bars.TryGetValue(sym, out var list) ? list : Array.Empty<Candle>();
                    if (store.AppendDay(sym, day.Key, bars)) written++;
                }
            }
            if (dayIndex % 10 == 0 || dayIndex == byDay.Count)
                s.Logger.LogInformation("[{Now}] bars: day {Index}/{Total} ({Day}), {Calls} calls, {Written} symbol-days written", GapperStudyServices.Et(s.UtcNow), dayIndex, byDay.Count, day.Key, callCount, written);
        }
        return (callCount, written);
    }

    /// <summary>Gapper tiers and sub-type for every candidate day with bars, then one control per gapper day; rewrites the events for the scope.</summary>
    public async Task<(int Events, int Controls)> ClassifyAndAssignControlsAsync(PullStageRequest req, FileBackedBarStore store, CancellationToken ct)
    {
        var p = s.Params;
        var lowestTier = p.Gapper.OrderedTiers()[0].Key;
        int events = 0, controls = 0;
        List<DateOnly> days;
        await using (var db = await s.Db.CreateDbContextAsync(ct))
            days = await Scoped(db, req).Where(r => r.IsCandidate).Select(r => r.DateEt).Distinct().OrderBy(d => d).ToListAsync(ct);

        foreach (var day in days)
        {
            await using var db = await s.Db.CreateDbContextAsync(ct);
            var rows = db.GapperStudyUniverseDays.Where(r => r.DateEt == day && r.ExcludedReason == null);
            rows = req.Smoke ? rows.Where(r => r.Split == GapperStudyServices.SmokeSplit) : rows.Where(r => r.Split != GapperStudyServices.SmokeSplit);
            var members = await rows.ToListAsync(ct);
            var candidateSymbols = members.Where(m => m.IsCandidate).Select(m => m.Symbol).ToList();
            await GapperStudyServices.DeleteAsync(db, db.GapperStudyEvents.Where(e => e.DateEt == day && candidateSymbols.Contains(e.Symbol)), ct);
            foreach (var m in members)
            {
                m.ControlForSymbol = null;
                if (!m.IsCandidate) continue;
                m.IsGapper = false; m.GapTier = null; m.GapperSubType = null; m.GapMax = null; m.PremarketDollarVolumeAtTier = null;
                var bars = store.ReadDay(m.Symbol, day);
                if (bars.Count == 0) continue;
                var c = GapperEventClassifier.Classify(p, bars, m.AdjPrevClose, day);
                var t20 = c.Tier("Tier20"); var t50 = c.Tier("Tier50"); var t100 = c.Tier("Tier100");
                if (c.Tier(lowestTier) is { Touched: true })
                {
                    db.GapperStudyEvents.Add(new GapperStudyEvent
                    {
                        Symbol = m.Symbol, DateEt = day, Split = m.Split, AdjPrevClose = m.AdjPrevClose, GapMax = c.GapMax,
                        Tier20Touched = t20?.Touched ?? false, Tier50Touched = t50?.Touched ?? false, Tier100Touched = t100?.Touched ?? false,
                        Tier20 = t20?.Qualified ?? false, Tier50 = t50?.Qualified ?? false, Tier100 = t100?.Qualified ?? false,
                        Tier20TouchTimeEt = t20?.TouchTimeEt, Tier50TouchTimeEt = t50?.TouchTimeEt, Tier100TouchTimeEt = t100?.TouchTimeEt,
                        PmDollarVolumeAtTier20 = t20?.PmDollarVolumeAtTouch, PmDollarVolumeAtTier50 = t50?.PmDollarVolumeAtTouch, PmDollarVolumeAtTier100 = t100?.PmDollarVolumeAtTouch,
                        SubType = c.SubType, PmHigh = c.PmHigh, PmHighTimeEt = c.PmHighTimeEt, RthHigh = c.RthHigh, HodTimeEt = c.HodTimeEt,
                        PmDollarVolume = c.PmDollarVolume, MinuteBars = c.MinuteBars, SipShare = c.SipShare,
                    });
                    events++;
                }
                m.GapMax = c.GapMax;
                m.IsGapper = c.IsGapper;
                m.GapTier = c.HighestQualifiedTier;
                m.GapperSubType = c.IsGapper ? c.SubType : null;
                m.PremarketDollarVolumeAtTier = c.Tier(lowestTier)?.PmDollarVolumeAtTouch;
            }

            var gappers = members.Where(m => m.IsGapper).Select(m => new GapperForControl(m.Symbol, m.AdvDecile, m.PriceTier)).ToList();
            if (gappers.Count > 0)
            {
                var pool = members.Where(m => !m.IsCandidate).Select(m => new ControlCandidate(m.Symbol, m.AdvDecile, m.PriceTier)).ToList();
                var bySymbol = members.ToDictionary(m => m.Symbol, StringComparer.Ordinal);
                foreach (var (gapper, control) in GapperControlPicker.Assign(p, day, gappers, pool))
                {
                    bySymbol[control].ControlForSymbol = gapper;
                    controls++;
                }
            }
            await db.SaveChangesAsync(ct);
        }
        s.Logger.LogInformation("[{Now}] classify: {Events} gapper events over {Days} days, {Controls} controls", GapperStudyServices.Et(s.UtcNow), events, days.Count, controls);
        return (events, controls);
    }

    public async Task<(double Pct, double SipShare)> BarCoverageAsync(PullStageRequest req, FileBackedBarStore store, CancellationToken ct)
    {
        List<(string Symbol, DateOnly DateEt)> needed;
        await using (var db = await s.Db.CreateDbContextAsync(ct))
            needed = (await Scoped(db, req).Where(r => r.IsCandidate || r.ControlForSymbol != null).Select(r => new { r.Symbol, r.DateEt }).ToListAsync(ct))
                .Select(x => (x.Symbol, x.DateEt)).ToList();
        if (needed.Count == 0) return (0, 0);
        int covered = 0;
        long bars = 0, sip = 0;
        foreach (var group in needed.GroupBy(n => n.Symbol))
        {
            var days = group.Select(g => g.DateEt).ToList();
            var have = store.CoveredDays(group.Key, days.Min(), days.Max());
            covered += days.Count(have.Contains);
            var cov = store.Coverage(group.Key, days.Min(), days.Max());
            bars += cov.Bars;
            sip += cov.SipBars;
        }
        return (Math.Round(100.0 * covered / needed.Count, 2), bars == 0 ? 0 : (double)sip / bars);
    }

    // ── T3 news ──────────────────────────────────────────────────────────

    private async Task<TierSummary> PullNewsAsync(PullStageRequest req, CallBudget calls, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var window = s.Params.DataTiers.NewsWindowDays;
        var candidates = await CandidateDaysAsync(req, ct);
        var covered = await s.CoveredAsync(NewsTier, ct);
        int inserted = 0, seen = 0, windows = 0, skipped = 0;
        var ranges = new List<(string Symbol, DateOnly From, DateOnly To)>();
        foreach (var g in candidates.GroupBy(c => c.Symbol))
            foreach (var (from, to) in Merge(g.Select(c => (c.DateEt.AddDays(-window.Before), c.DateEt.AddDays(window.After)))))
                ranges.Add((g.Key, from, to));
        s.Logger.LogInformation("[{Now}] news: {Ranges} symbol windows for {Symbols} candidate symbols", GapperStudyServices.Et(s.UtcNow), ranges.Count, candidates.Select(c => c.Symbol).Distinct().Count());
        foreach (var (symbol, from, to) in ranges)
        {
            var key = $"{symbol}|{from:yyyy-MM-dd}|{to:yyyy-MM-dd}";
            if (covered.Contains(key)) { skipped++; continue; }
            if (!calls.Allow()) { s.Logger.LogWarning("news: --max-calls reached"); break; }
            await s.RateGate.WaitAsync(ct);
            var fromUtc = EasternTime.ToUtc(from.ToDateTime(TimeOnly.MinValue));
            var toUtc = EasternTime.ToUtc(to.AddDays(1).ToDateTime(TimeOnly.MinValue));
            if (toUtc > s.UtcNow) toUtc = s.UtcNow;
            var r = await backfill.BackfillAsync([symbol], fromUtc, toUtc, 0, req.Owner, includeEdgar: false, ct);
            inserted += r.Inserted;
            seen += r.NewsSeen;
            windows++;
            await s.MarkAsync(NewsTier, [(key, r.NewsSeen)], ct);
            if (windows % 50 == 0) s.Logger.LogInformation("[{Now}] news: {Windows} windows, {Seen} items seen, {Inserted} new", GapperStudyServices.Et(s.UtcNow), windows, seen, inserted);
        }
        var population = await PullPopulationNewsAsync(req, calls, ct);
        return new TierSummary(NewsTier, inserted + population.Inserted, windows + population.Calls, Math.Round(sw.Elapsed.TotalMinutes, 2), ranges.Count == 0 ? null : Math.Round(100.0 * (skipped + windows) / ranges.Count, 2),
            $"{seen} items seen, {inserted} new rows, {skipped} windows already covered" + (population.Calls + population.Skipped > 0 ? $"; population sample: {population.Calls} batches, {population.Seen} items seen, {population.Inserted} new, {population.Skipped} batches already covered" : ""));
    }

    public const string NewsPopulationTier = "news-population";
    private const int NewsSymbolsPerRequest = 100;

    /// <summary>
    /// Amendment 01's sample days get the candidates' news window [D-Before, D+After], fetched one
    /// trading day at a time with up to 100 symbols per request (the sample is spread thin across
    /// symbols, so per-symbol windows would cost one call each). Items land through the same ingestor,
    /// deduplicated by source id, and are read back per symbol exactly as candidates' are.
    /// </summary>
    private async Task<(int Calls, int Skipped, int Seen, int Inserted)> PullPopulationNewsAsync(PullStageRequest req, CallBudget calls, CancellationToken ct)
    {
        var sample = await PopulationSampleAsync(req, ct);
        if (sample.Count == 0) return (0, 0, 0, 0);
        var window = s.Params.DataTiers.NewsWindowDays;
        var covered = await s.CoveredAsync(NewsPopulationTier, ct);
        var batches = sample.GroupBy(x => x.DateEt).OrderBy(g => g.Key)
            .SelectMany(g => g.Select(x => x.Symbol).OrderBy(x => x, StringComparer.Ordinal).Chunk(NewsSymbolsPerRequest).Select((chunk, i) => (Day: g.Key, Index: i, Symbols: chunk)))
            .ToList();
        s.Logger.LogInformation("[{Now}] news: {Batches} population-sample batches for {Days} sample days", GapperStudyServices.Et(s.UtcNow), batches.Count, sample.Count);
        int done = 0, skipped = 0, seen = 0, inserted = 0;
        foreach (var (day, index, symbols) in batches)
        {
            var key = $"{day:yyyy-MM-dd}|{index}|{symbols.Length}";
            if (covered.Contains(key)) { skipped++; continue; }
            if (!calls.Allow()) { s.Logger.LogWarning("news (population): --max-calls reached"); break; }
            await s.RateGate.WaitAsync(ct);
            var fromUtc = EasternTime.ToUtc(day.AddDays(-window.Before).ToDateTime(TimeOnly.MinValue));
            var toUtc = EasternTime.ToUtc(day.AddDays(window.After + 1).ToDateTime(TimeOnly.MinValue));
            if (toUtc > s.UtcNow) toUtc = s.UtcNow;
            var r = await backfill.BackfillAsync(symbols, fromUtc, toUtc, 0, req.Owner, includeEdgar: false, ct);
            inserted += r.Inserted;
            seen += r.NewsSeen;
            done++;
            await s.MarkAsync(NewsPopulationTier, [(key, r.NewsSeen)], ct);
            if (done % 50 == 0) s.Logger.LogInformation("[{Now}] news (population): {Done}/{Total} batches, {Seen} items seen, {Inserted} new", GapperStudyServices.Et(s.UtcNow), done, batches.Count - skipped, seen, inserted);
        }
        return (done, skipped, seen, inserted);
    }

    private static IEnumerable<(DateOnly From, DateOnly To)> Merge(IEnumerable<(DateOnly From, DateOnly To)> ranges)
    {
        (DateOnly From, DateOnly To)? current = null;
        foreach (var r in ranges.OrderBy(r => r.From))
        {
            if (current is { } c && r.From <= c.To.AddDays(1)) current = (c.From, r.To > c.To ? r.To : c.To);
            else
            {
                if (current is { } done) yield return done;
                current = r;
            }
        }
        if (current is { } last) yield return last;
    }

    // ── T5 ticker ↔ CIK ──────────────────────────────────────────────────

    private async Task<TierSummary> MapCiksAsync(PullStageRequest req, CallBudget calls, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var candidateSymbols = (await CandidateDaysAsync(req, ct)).Select(c => c.Symbol).Distinct().ToList();
        var targetSymbols = (await TargetDaysAsync(req, ct)).Select(c => c.Symbol).ToHashSet(StringComparer.Ordinal);
        var universeSymbols = new List<string>();
        await using (var db = await s.Db.CreateDbContextAsync(ct))
            universeSymbols = await Scoped(db, req).Select(r => r.Symbol).Distinct().ToListAsync(ct);

        var json = await edgarGate.RunAsync(async c =>
        {
            var http = httpFactory.CreateClient(s.Params.DataTiers.EdgarHttpClientName);
            return await http.GetStringAsync(EdgarTickerMap.Url, c);
        }, ct);
        var byTicker = new Dictionary<string, (string Cik, string Title)>(StringComparer.OrdinalIgnoreCase);
        var byName = new Dictionary<string, string>(StringComparer.Ordinal);
        using (var doc = JsonDocument.Parse(json))
            foreach (var entry in doc.RootElement.EnumerateObject())
            {
                var e = entry.Value;
                if (!e.TryGetProperty("cik_str", out var cikEl) || !e.TryGetProperty("ticker", out var tEl)) continue;
                var cik = cikEl.ValueKind == JsonValueKind.Number ? cikEl.GetRawText() : cikEl.GetString() ?? "";
                var ticker = (tEl.GetString() ?? "").Trim().ToUpperInvariant().Replace('-', '.');
                var title = e.TryGetProperty("title", out var ti) ? ti.GetString() ?? "" : "";
                if (ticker.Length == 0 || cik.Length == 0) continue;
                byTicker.TryAdd(ticker, (EdgarTickerMap.Normalize(cik), title));
                var norm = NormalizeName(title);
                if (norm.Length > 0) byName.TryAdd(norm, EdgarTickerMap.Normalize(cik));
            }

        int fromFile = 0, fromForm4 = 0, fromName = 0, form4Calls = 0;
        await using (var db = await s.Db.CreateDbContextAsync(ct))
        {
            var rows = await db.GapperStudyCikMap.Where(r => universeSymbols.Contains(r.Symbol)).ToListAsync(ct);
            var now = s.UtcNow;
            foreach (var row in rows.Where(r => r.Cik is null))
            {
                if (byTicker.TryGetValue(row.Symbol, out var hit))
                {
                    row.Cik = hit.Cik; row.CikSource = "company_tickers.json"; row.MappedUtc = now; fromFile++;
                    continue;
                }
                var norm = NormalizeName(row.Name);
                if (norm.Length > 0 && byName.TryGetValue(norm, out var cikByName))
                {
                    row.Cik = cikByName; row.CikSource = "exactIssuerName"; row.MappedUtc = now; fromName++;
                }
            }
            await db.SaveChangesAsync(ct);

            var unmapped = rows.Where(r => r.Cik is null && targetSymbols.Contains(r.Symbol)).OrderBy(r => r.Symbol, StringComparer.Ordinal).ToList();
            s.Logger.LogInformation("[{Now}] cik: {File} from company_tickers.json, {Name} by issuer name; {Left} candidate or population-sample symbols go to Form 4 search", GapperStudyServices.Et(s.UtcNow), fromFile, fromName, unmapped.Count);
            var span = s.Params.DataTiers.DailyBars;
            foreach (var row in unmapped)
            {
                if (!calls.Allow()) { s.Logger.LogWarning("cik: --max-calls reached"); break; }
                form4Calls++;
                var filings = await edgarGate.RunAsync(c => edgar.GetFilingsAsync(row.Symbol, "4", span.From, span.To, c), ct);
                var issuer = filings.Select(f => f.IssuerCik).FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));
                if (issuer is null) continue;
                row.Cik = EdgarTickerMap.Normalize(issuer); row.CikSource = "form4IssuerTradingSymbol"; row.MappedUtc = now; fromForm4++;
                await db.SaveChangesAsync(ct);
            }
        }

        double coverage;
        await using (var db = await s.Db.CreateDbContextAsync(ct))
        {
            var mapped = await db.GapperStudyCikMap.CountAsync(r => candidateSymbols.Contains(r.Symbol) && r.Cik != null, ct);
            coverage = candidateSymbols.Count == 0 ? 0 : Math.Round(100.0 * mapped / candidateSymbols.Count, 2);
            await GapperStudySample.WriteAsync(s, req.Smoke, new Dictionary<string, object?>
            {
                ["cikCoverage"] = new { candidateSymbols = candidateSymbols.Count, mapped, coveragePct = coverage, fromCompanyTickers = fromFile, fromForm4, fromIssuerName = fromName, h8Dropped = coverage / 100.0 < s.Params.KillCriteria.H8DropIfCikCoverageBelow },
            }, ct);
        }
        return new TierSummary(CikTier, fromFile + fromForm4 + fromName, 1 + form4Calls, Math.Round(sw.Elapsed.TotalMinutes, 2), coverage,
            $"{fromFile} company_tickers, {fromForm4} Form 4, {fromName} name match; candidate coverage {coverage}%");
    }

    public static string NormalizeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        var chars = name.ToUpperInvariant().Where(ch => char.IsLetterOrDigit(ch) || ch == ' ').ToArray();
        return string.Join(' ', new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    // ── T4 EDGAR form index + acceptance headers ─────────────────────────

    /// <summary>Header requests in flight at once; EdgarRateGate still spaces their starts under SEC's 10 req/s.</summary>
    internal const int HeaderConcurrency = 8;

    /// <summary>Headers fetched, then written in one fresh context, per chunk.</summary>
    internal const int HeaderChunk = 400;

    private async Task<TierSummary> PullEdgarAsync(PullStageRequest req, CallBudget calls, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var p = s.Params;
        var forms = p.DataTiers.EdgarForms.ToHashSet(StringComparer.Ordinal);
        var candidates = await CandidateDaysAsync(req, ct);
        var span = p.DataTiers.DailyBars;
        var acceptance = p.DataTiers.EdgarAcceptanceWindowDays;
        DateOnly from = req.Smoke && candidates.Count > 0 ? candidates.Min(c => c.DateEt).AddDays(-acceptance.Before) : span.From;
        DateOnly to = req.Smoke && candidates.Count > 0 ? candidates.Max(c => c.DateEt).AddDays(acceptance.After) : span.To;

        Dictionary<string, string> symbolByCik;
        await using (var db = await s.Db.CreateDbContextAsync(ct))
            symbolByCik = (await db.GapperStudyCikMap.AsNoTracking().Where(r => r.Cik != null).Select(r => new { r.Symbol, r.Cik }).ToListAsync(ct))
                .GroupBy(r => r.Cik!).ToDictionary(g => g.Key, g => g.OrderBy(x => x.Symbol, StringComparer.Ordinal).First().Symbol, StringComparer.Ordinal);
        if (symbolByCik.Count == 0)
            return new TierSummary("edgar", 0, 0, 0, null, "no CIK mappings yet: run `pull --tier cik` first");

        int indexRows = 0, quarterCalls = 0;
        await ReadQuartersAsync(EdgarIndexTier);
        if (s.Amendment is not null)
        {
            // CIKs mapped after a quarter was first read (the Form 4 search for population-sample symbols) have no index rows yet.
            symbolByCik = await MappedCiksAsync();
            await ReadQuartersAsync(EdgarIndexAmendmentTier);
        }

        async Task<Dictionary<string, string>> MappedCiksAsync()
        {
            await using var db = await s.Db.CreateDbContextAsync(ct);
            return (await db.GapperStudyCikMap.AsNoTracking().Where(r => r.Cik != null).Select(r => new { r.Symbol, r.Cik }).ToListAsync(ct))
                .GroupBy(r => r.Cik!).ToDictionary(g => g.Key, g => g.OrderBy(x => x.Symbol, StringComparer.Ordinal).First().Symbol, StringComparer.Ordinal);
        }

        async Task ReadQuartersAsync(string tier)
        {
        var covered = await s.CoveredAsync(tier, ct);
        foreach (var (year, quarter) in EdgarFormIndex.Quarters(from, to))
        {
            var key = $"{year}/QTR{quarter}";
            if (covered.Contains(key)) continue;
            if (!calls.Allow()) { s.Logger.LogWarning("edgar: --max-calls reached"); break; }
            quarterCalls++;
            var rows = await edgarGate.RunAsync(async c =>
            {
                var http = httpFactory.CreateClient(p.DataTiers.EdgarHttpClientName);
                http.Timeout = TimeSpan.FromMinutes(10);
                using var resp = await http.GetAsync(EdgarFormIndex.Url(year, quarter), HttpCompletionOption.ResponseHeadersRead, c);
                resp.EnsureSuccessStatusCode();
                using var stream = await resp.Content.ReadAsStreamAsync(c);
                using var reader = new StreamReader(stream);
                return EdgarFormIndex.Parse(reader, forms).Where(e => symbolByCik.ContainsKey(EdgarTickerMap.Normalize(e.Cik))).ToList();
            }, ct);
            await using (var db = await s.Db.CreateDbContextAsync(ct))
            {
                var ciks = rows.Select(r => EdgarTickerMap.Normalize(r.Cik)).Distinct().ToList();
                var present = (await db.EdgarFilingIndex.AsNoTracking().Where(r => r.SourceQuarter == key).Select(r => new { r.Cik, r.AccessionNumber }).ToListAsync(ct))
                    .Select(x => (x.Cik, x.AccessionNumber)).ToHashSet();
                foreach (var e in rows)
                {
                    var cik = EdgarTickerMap.Normalize(e.Cik);
                    if (!present.Add((cik, e.AccessionNumber))) continue;
                    db.EdgarFilingIndex.Add(new EdgarFilingIndexRow
                    {
                        Cik = cik, Symbol = symbolByCik[cik], FormType = e.FormType, AccessionNumber = e.AccessionNumber, FiledDate = e.FiledDate,
                        CompanyName = e.CompanyName.Length > 200 ? e.CompanyName[..200] : e.CompanyName, SourceQuarter = key,
                    });
                    indexRows++;
                }
                await db.SaveChangesAsync(ct);
            }
            await s.MarkAsync(tier, [(key, rows.Count)], ct);
            s.Logger.LogInformation("[{Now}] edgar: {Quarter} → {Rows} rows for mapped CIKs ({Total} inserted so far, {Tier})", GapperStudyServices.Et(s.UtcNow), key, rows.Count, indexRows, tier);
        }
        }

        int headerCalls = 0, headersFilled = 0;
        var targets = await TargetDaysAsync(req, ct);
        var windows = targets.GroupBy(c => c.Symbol).ToDictionary(g => g.Key, g => g.Select(c => (From: c.DateEt.AddDays(-acceptance.Before), To: c.DateEt.AddDays(acceptance.After))).ToList(), StringComparer.Ordinal);
        {
            // The pending list is read untracked (only what's needed to fetch), then fetched HeaderConcurrency at a time
            // through the shared EdgarRateGate, and written chunk by chunk through a fresh context: tracking hundreds of
            // thousands of rows in one context grew the process by gigabytes, and one request at a time ran ~3/s.
            var targetSymbols = windows.Keys.ToList();
            List<(long Id, string Cik, string Accession, string Symbol, DateOnly Filed)> pending;
            await using (var db = await s.Db.CreateDbContextAsync(ct))
                pending = [.. (await db.EdgarFilingIndex.AsNoTracking()
                    .Where(r => r.HeaderFetchedUtc == null && r.Symbol != null && targetSymbols.Contains(r.Symbol) && r.FiledDate >= from && r.FiledDate <= to)
                    .Select(r => new { r.Id, r.Cik, r.AccessionNumber, r.Symbol, r.FiledDate }).ToListAsync(ct))
                    .Select(r => (r.Id, r.Cik, r.AccessionNumber, r.Symbol!, r.FiledDate))];
            var inWindow = pending.Where(r => windows[r.Symbol].Any(w => r.Filed >= w.From && r.Filed <= w.To)).OrderBy(r => r.Filed).ToList();
            s.Logger.LogInformation("[{Now}] edgar: {Count} filings inside [D-{B}, D+{A}] of candidate or population-sample days need acceptance times", GapperStudyServices.Et(s.UtcNow), inWindow.Count, acceptance.Before, acceptance.After);
            var budgetHit = false;
            foreach (var chunk in inWindow.Chunk(HeaderChunk))
            {
                var allowed = new List<(long Id, string Cik, string Accession, string Symbol, DateOnly Filed)>(chunk.Length);
                foreach (var r in chunk)
                {
                    if (!calls.Allow()) { budgetHit = true; break; }
                    allowed.Add(r);
                }
                var results = new System.Collections.Concurrent.ConcurrentDictionary<long, EdgarFilingHeader?>();
                await Parallel.ForEachAsync(allowed, new ParallelOptions { MaxDegreeOfParallelism = HeaderConcurrency, CancellationToken = ct },
                    async (r, token) => results[r.Id] = await headers.FetchAsync(r.Cik, r.Accession, token));
                var fetchedAt = s.UtcNow;
                await using (var db = await s.Db.CreateDbContextAsync(ct))
                {
                    var ids = results.Keys.ToList();
                    // Only an answered fetch is marked: a refused or failed one (SEC throttling answers 403/429) stays pending
                    // for the next run instead of being recorded as fetched with no acceptance time.
                    foreach (var row in await db.EdgarFilingIndex.Where(r => ids.Contains(r.Id)).ToListAsync(ct))
                    {
                        if (results[row.Id] is not { } header) continue;
                        row.HeaderFetchedUtc = fetchedAt;
                        row.AcceptedUtc = header.AcceptanceUtc;
                        row.ItemsJson = header.Items.Count > 0 ? JsonSerializer.Serialize(header.Items) : null;
                        if (header.AcceptanceUtc is not null) headersFilled++;
                    }
                    await db.SaveChangesAsync(ct);
                }
                headerCalls += results.Count;
                var failed = results.Count(kv => kv.Value is null);
                s.Logger.LogInformation("[{Now}] edgar headers: {Calls} of {Total} fetched, {Filled} with acceptance time, {Failed} failed this chunk", GapperStudyServices.Et(s.UtcNow), headerCalls, inWindow.Count, headersFilled, failed);
                if (budgetHit) { s.Logger.LogWarning("edgar headers: --max-calls reached"); break; }
                if (results.Count > 0 && failed * 2 > results.Count)
                {
                    s.Logger.LogWarning("[{Now}] edgar headers: {Failed} of {Count} failed, backing off 2 minutes", GapperStudyServices.Et(s.UtcNow), failed, results.Count);
                    await Task.Delay(TimeSpan.FromMinutes(2), ct);
                }
            }
        }

        double? coveragePct;
        await using (var db = await s.Db.CreateDbContextAsync(ct))
        {
            var candidateSymbols = candidates.Select(c => c.Symbol).Distinct().ToList();
            var total = await db.EdgarFilingIndex.CountAsync(r => r.Symbol != null && candidateSymbols.Contains(r.Symbol) && r.FiledDate >= from && r.FiledDate <= to, ct);
            var withTime = await db.EdgarFilingIndex.CountAsync(r => r.Symbol != null && candidateSymbols.Contains(r.Symbol) && r.FiledDate >= from && r.FiledDate <= to && r.AcceptedUtc != null, ct);
            coveragePct = total == 0 ? null : Math.Round(100.0 * withTime / total, 2);
        }
        return new TierSummary("edgar", indexRows, quarterCalls + headerCalls, Math.Round(sw.Elapsed.TotalMinutes, 2), coveragePct,
            $"{quarterCalls} quarter files, {headerCalls} header fetches, {headersFilled} acceptance times");
    }

    // ── T6 splits (re-run of the universe stage's pull, for `pull --tier splits`) ──

    private async Task<TierSummary> PullSplitsAsync(PullStageRequest req, CallBudget calls, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var keys = await s.ResolveKeysAsync(req.Owner, ct);
        using var source = s.Splits(keys);
        List<string> symbols;
        await using (var db = await s.Db.CreateDbContextAsync(ct))
            symbols = await Scoped(db, req).Select(r => r.Symbol).Distinct().OrderBy(x => x).ToListAsync(ct);
        var span = s.Params.DataTiers.DailyBars;
        var from = (req.Smoke && req.From is { } f ? f.AddDays(-60) : span.From).AddDays(-30);
        var to = (req.Smoke && req.To is { } t ? t : span.To).AddDays(2);
        var covered = await s.CoveredAsync(GapperUniverseStage.SplitsTier, ct);
        string Key(string sym) => $"{sym}|{from:yyyy-MM-dd}|{to:yyyy-MM-dd}";
        var missing = symbols.Where(x => !covered.Contains(Key(x))).ToList();
        int callCount = 0, inserted = 0;
        foreach (var chunk in missing.Chunk(50))
        {
            if (!calls.Allow()) break;
            await s.RateGate.WaitAsync(ct);
            callCount++;
            var found = await source.GetSplitsAsync(chunk, from, to, ct);
            await using var db = await s.Db.CreateDbContextAsync(ct);
            var chunkList = chunk.ToList();
            var present = (await db.GapperStudySplits.AsNoTracking().Where(x => chunkList.Contains(x.Symbol)).Select(x => new { x.Symbol, x.ExDate }).ToListAsync(ct)).Select(x => (x.Symbol, x.ExDate)).ToHashSet();
            foreach (var (sym, list) in found)
                foreach (var e in list)
                    if (present.Add((sym, e.ExDate)))
                    {
                        db.GapperStudySplits.Add(new GapperStudySplit { Symbol = sym, ExDate = e.ExDate, OldRate = e.OldRate, NewRate = e.NewRate });
                        inserted++;
                    }
            await db.SaveChangesAsync(ct);
            await s.MarkAsync(GapperUniverseStage.SplitsTier, chunk.Select(sym => (Key(sym), 0)), ct);
        }
        return new TierSummary("splits", inserted, callCount, Math.Round(sw.Elapsed.TotalMinutes, 2), symbols.Count == 0 ? null : 100.0, $"{missing.Count} symbols pulled, {symbols.Count - missing.Count} already covered");
    }
}
