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
public sealed class GapperPullStage(
    GapperStudyServices s,
    CatalystBackfillService backfill,
    EdgarService edgar,
    EdgarFilingHeaders headers,
    EdgarRateGate edgarGate,
    IHttpClientFactory httpFactory)
{
    public const string BarsTier = "bars";
    public const string NewsTier = "news";
    public const string CikTier = "cik";
    public const string EdgarIndexTier = "edgar-index";
    public const string EdgarHeaderTier = "edgar-header";

    public async Task<List<TierSummary>> RunAsync(PullStageRequest req, CancellationToken ct)
    {
        var tiers = req.Tier.ToLowerInvariant() switch
        {
            "all" => new[] { "splits", BarsTier, NewsTier, CikTier, "edgar" },
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
                "splits" => await PullSplitsAsync(req, calls, ct),
                _ => throw new ArgumentException($"Unknown tier '{tier}'. Use bars, news, edgar, cik, splits or all."),
            });
        }
        return results;
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

    // ── T2 bars ──────────────────────────────────────────────────────────

    private async Task<TierSummary> PullBarsAsync(PullStageRequest req, CallBudget calls, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var keys = await s.ResolveKeysAsync(req.Owner, ct);
        await using var feed = s.Feed(keys);
        var store = await s.StoreAsync(ct);
        var candidates = await CandidateDaysAsync(req, ct);
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
        return new TierSummary(NewsTier, inserted, windows, Math.Round(sw.Elapsed.TotalMinutes, 2), ranges.Count == 0 ? null : Math.Round(100.0 * (skipped + windows) / ranges.Count, 2),
            $"{seen} items seen, {inserted} new rows, {skipped} windows already covered");
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

            var unmapped = rows.Where(r => r.Cik is null && candidateSymbols.Contains(r.Symbol)).OrderBy(r => r.Symbol, StringComparer.Ordinal).ToList();
            s.Logger.LogInformation("[{Now}] cik: {File} from company_tickers.json, {Name} by issuer name; {Left} candidate symbols go to Form 4 search", GapperStudyServices.Et(s.UtcNow), fromFile, fromName, unmapped.Count);
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

        var covered = await s.CoveredAsync(EdgarIndexTier, ct);
        int indexRows = 0, quarterCalls = 0;
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
            await s.MarkAsync(EdgarIndexTier, [(key, rows.Count)], ct);
            s.Logger.LogInformation("[{Now}] edgar: {Quarter} → {Rows} rows for mapped CIKs ({Total} inserted so far)", GapperStudyServices.Et(s.UtcNow), key, rows.Count, indexRows);
        }

        int headerCalls = 0, headersFilled = 0;
        var windows = candidates.GroupBy(c => c.Symbol).ToDictionary(g => g.Key, g => g.Select(c => (From: c.DateEt.AddDays(-acceptance.Before), To: c.DateEt.AddDays(acceptance.After))).ToList(), StringComparer.Ordinal);
        await using (var db = await s.Db.CreateDbContextAsync(ct))
        {
            var candidateSymbols = windows.Keys.ToList();
            var pending = await db.EdgarFilingIndex.Where(r => r.HeaderFetchedUtc == null && r.Symbol != null && candidateSymbols.Contains(r.Symbol) && r.FiledDate >= from && r.FiledDate <= to).ToListAsync(ct);
            var inWindow = pending.Where(r => windows[r.Symbol!].Any(w => r.FiledDate >= w.From && r.FiledDate <= w.To)).OrderBy(r => r.FiledDate).ToList();
            s.Logger.LogInformation("[{Now}] edgar: {Count} filings inside [D-{B}, D+{A}] of candidate days need acceptance times", GapperStudyServices.Et(s.UtcNow), inWindow.Count, acceptance.Before, acceptance.After);
            foreach (var row in inWindow)
            {
                if (!calls.Allow()) { s.Logger.LogWarning("edgar headers: --max-calls reached"); break; }
                headerCalls++;
                var header = await headers.FetchAsync(row.Cik, row.AccessionNumber, ct);
                row.HeaderFetchedUtc = s.UtcNow;
                if (header is not null)
                {
                    row.AcceptedUtc = header.AcceptanceUtc;
                    row.ItemsJson = header.Items.Count > 0 ? JsonSerializer.Serialize(header.Items) : null;
                    if (header.AcceptanceUtc is not null) headersFilled++;
                }
                if (headerCalls % 100 == 0)
                {
                    await db.SaveChangesAsync(ct);
                    s.Logger.LogInformation("[{Now}] edgar headers: {Calls} fetched, {Filled} with acceptance time", GapperStudyServices.Et(s.UtcNow), headerCalls, headersFilled);
                }
            }
            await db.SaveChangesAsync(ct);
        }

        double? coveragePct;
        await using (var db = await s.Db.CreateDbContextAsync(ct))
        {
            var candidateSymbols = windows.Keys.ToList();
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
