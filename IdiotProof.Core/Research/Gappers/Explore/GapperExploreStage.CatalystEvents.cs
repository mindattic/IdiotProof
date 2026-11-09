using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using IdiotProof.Core.Evidence;
using IdiotProof.Core.Services;
using IdiotProof.Evidence;
using IdiotProof.Evidence.Economics;
using IdiotProof.Evidence.Gappers;
using IdiotProof.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IdiotProof.Core.Research.Gappers.Explore;

/// <summary>
/// <c>explore catalyst-events</c> (IP-T488), TRAIN only: the event table of the machine-learning catalyst discovery. One row per
/// (symbol, stored news item or EDGAR filing) whose event session E is a train universe day with minute bars. E is the publish day
/// when the item came out 00:00-15:59 ET on a trading day, else the next trading day; outcomes run from the first full minute after
/// publication (04:00 of E for an item from the night before). Written to the explore cache as events.csv for
/// tools/gapper-catalyst-ml/catalyst_ml.py (topics and the text model) and `explore catalyst-ml` (report, boosted trees, trades).
/// Nothing after 2024-10-31 is read: news to the last train day's 16:00, daily bars to its close, and later exits are blank.
/// </summary>
public sealed partial class GapperExploreStage
{
    private string CatalystMlDir => Path.Combine(CacheDir(), "catalyst-ml");

    /// <summary>Insider ownership forms are not catalysts the classifier reads; everything else in the EDGAR index is kept.</summary>
    private static readonly HashSet<string> CatalystMlSkipForms = new(["3", "3/A", "4", "4/A", "5", "5/A"], StringComparer.Ordinal);

    /// <summary>Minutes after the first full minute past publication within which the first-bar entry must print.</summary>
    public const int CatalystMlEntryWindow = 30;

    /// <summary>Last grid minute (15:40) a first-bar entry may take.</summary>
    public const int CatalystMlLastEntry = 700;

    private sealed record MlItem(string Id, string Kind, DateTime Utc, string Symbol, string Form, string Items, string PreClass, int NSymbols, string Headline, string Snippet);

    private sealed record MlUniverse(double Pc, double AdvDollars, int Decile, bool IsAdr, bool IsCandidate, bool Excluded);

    /// <summary>The event session of an item and the grid minute its outcomes start from (the first full minute after publication).</summary>
    internal static (DateOnly Day, int Start) CatalystEventSession(DateTime utc)
    {
        var et = MinuteBarMath.ToEastern(utc);
        var date = DateOnly.FromDateTime(et);
        var tod = et.TimeOfDay;
        if (MarketCalendar.IsTradingDay(date) && tod < new TimeSpan(16, 0, 0))
            return (date, tod <= RunnerGrid.StartEt ? 0 : (int)Math.Ceiling((tod - RunnerGrid.StartEt).TotalMinutes - 1e-9));
        return (MarketCalendar.NextTradingDay(date), 0);
    }

    /// <summary>Close of the last bar that ended at or before <paramref name="utc"/> (bars sorted by start), with its index; null when none did.</summary>
    internal static (double Price, int Index)? CloseBy(IReadOnlyList<Candle> bars, DateTime utc)
    {
        int lo = 0, hi = bars.Count - 1, found = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (bars[mid].StartUtc.AddMinutes(1) <= utc) { found = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        return found < 0 ? null : ((double)bars[found].Close, found);
    }

    private async Task<(List<string>, int, string?)> CatalystEventsAsync(ExploreRequest req, CancellationToken ct)
    {
        var p = s.Params;
        var train = p.Split("train");
        var fromUtc = MinuteBarMath.ToUtc(MarketCalendar.PreviousTradingDay(train.From), new TimeSpan(16, 0, 0));
        var toUtc = MinuteBarMath.ToUtc(train.To, new TimeSpan(16, 0, 0));
        Directory.CreateDirectory(CatalystMlDir);

        // ── items ──
        var items = new List<MlItem>();
        int newsItems = 0, filingsRead = 0;
        await using (var db = await s.Db.CreateDbContextAsync(ct))
        {
            var news = new Dictionary<Guid, (DateTime Utc, string Headline, string Snippet, string PreClass)>();
            await foreach (var e in db.CatalystEvents.AsNoTracking().Where(e => e.PublishedUtc >= fromUtc && e.PublishedUtc < toUtc)
                               .Select(e => new { e.Id, e.PublishedUtc, e.Headline, e.RawSnippet, e.PreClass }).AsAsyncEnumerable().WithCancellation(ct))
            {
                var snippet = e.RawSnippet ?? "";
                if (snippet.Length > 400) snippet = snippet[..400];
                news[e.Id] = (e.PublishedUtc, e.Headline ?? "", snippet, e.PreClass ?? "");
            }
            newsItems = news.Count;
            var symbolsOf = new Dictionary<Guid, List<string>>();
            await foreach (var x in db.CatalystEventSymbols.AsNoTracking().Select(x => new { x.EventId, x.Symbol }).AsAsyncEnumerable().WithCancellation(ct))
                if (news.ContainsKey(x.EventId))
                {
                    if (!symbolsOf.TryGetValue(x.EventId, out var l)) symbolsOf[x.EventId] = l = [];
                    if (!l.Contains(x.Symbol)) l.Add(x.Symbol);
                }
            foreach (var (id, l) in symbolsOf)
            {
                var n = news[id];
                foreach (var sym in l) items.Add(new MlItem(id.ToString("N"), "news", n.Utc, sym, "", "", n.PreClass, l.Count, n.Headline, n.Snippet));
            }
            var fFrom = DateOnly.FromDateTime(fromUtc).AddDays(-1);
            var fTo = train.To;
            var filings = await db.EdgarFilingIndex.AsNoTracking().Where(f => f.Symbol != null && f.FiledDate >= fFrom && f.FiledDate <= fTo)
                .Select(f => new { f.Symbol, f.FiledDate, f.AcceptedUtc, f.FormType, f.ItemsJson, f.AccessionNumber }).ToListAsync(ct);
            foreach (var f in filings)
            {
                if (CatalystMlSkipForms.Contains(f.FormType)) continue;
                filingsRead++;
                string[] its = [];
                if (!string.IsNullOrEmpty(f.ItemsJson)) try { its = JsonSerializer.Deserialize<string[]>(f.ItemsJson) ?? []; } catch (JsonException) { }
                var known = f.AcceptedUtc ?? MinuteBarMath.ToUtc(f.FiledDate, new TimeSpan(17, 30, 0));
                if (known < fromUtc || known >= toUtc) continue;
                items.Add(new MlItem(f.AccessionNumber, "filing", known, f.Symbol!, f.FormType, string.Join(";", its.Select(i => i.Trim())), "", 1, "", ""));
            }
        }
        s.Logger.LogInformation("[{Now}] explore catalyst-events: {News} news items, {Filings} filings, {Rows} symbol rows before the universe filter", GapperStudyServices.Et(s.UtcNow), newsItems, filingsRead, items.Count);

        // ── universe rows of the event sessions ──
        var keyed = items.Select(it => (It: it, Session: CatalystEventSession(it.Utc))).Where(x => x.Session.Day >= train.From && x.Session.Day <= train.To).ToList();
        var symbols = keyed.Select(x => x.It.Symbol).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var needed = keyed.Select(x => (x.It.Symbol, x.Session.Day)).ToHashSet();
        var universe = new Dictionary<(string, DateOnly), MlUniverse>();
        for (int i = 0; i < symbols.Count; i += 400)
        {
            var batch = symbols.Skip(i).Take(400).ToList();
            await using var db = await s.Db.CreateDbContextAsync(ct);
            // Train only: the universe rows of the train split.
            foreach (var u in await db.GapperStudyUniverseDays.AsNoTracking().Where(u => u.Split == "train" && batch.Contains(u.Symbol))
                         .Select(u => new { u.Symbol, u.DateEt, u.AdjPrevClose, u.Adv20Dollars, u.AdvDecile, u.IsAdr, u.IsCandidate, u.ExcludedReason }).ToListAsync(ct))
                if (needed.Contains((u.Symbol, u.DateEt)))
                    universe[(u.Symbol, u.DateEt)] = new MlUniverse((double)u.AdjPrevClose, (double)u.Adv20Dollars, u.AdvDecile, u.IsAdr, u.IsCandidate, u.ExcludedReason != null);
        }
        keyed = keyed.Where(x => universe.ContainsKey((x.It.Symbol, x.Session.Day))).ToList();
        var evSymbols = keyed.Select(x => x.It.Symbol).Distinct(StringComparer.Ordinal).ToList();

        // ── daily bars, splits, float, prior top movers ──
        var daily = await OrbDailyAsync(evSymbols, train.To, ct);
        var splits = new Dictionary<string, List<(DateOnly Ex, double Ratio)>>(StringComparer.Ordinal);
        await using (var db = await s.Db.CreateDbContextAsync(ct))
            foreach (var sp in await db.GapperStudySplits.AsNoTracking().Select(x => new { x.Symbol, x.ExDate, x.OldRate, x.NewRate }).ToListAsync(ct))
            {
                if (sp.NewRate <= 0 || sp.OldRate <= 0) continue;
                if (!splits.TryGetValue(sp.Symbol, out var l)) splits[sp.Symbol] = l = [];
                l.Add((sp.ExDate, (double)(sp.OldRate / sp.NewRate)));
            }
        shares ??= SharesOutstandingHistory.ReadCsv(SharesCsv());
        var cik = LoadCikMap();
        var topMovers = new ConcurrentDictionary<DateOnly, Dictionary<string, int>>();
        await Parallel.ForEachAsync(keyed.Select(x => x.Session.Day).Distinct().ToList(), new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct }, async (d, token) =>
        {
            await using var db = await s.Db.CreateDbContextAsync(token);
            topMovers[d] = await GapperSnapshotStage.TopMoverRanksAsync(db, d, "train", token);
        });
        var (multiplier, _) = GapperSpreadMultiplier.Resolve(s.Paths, p);
        var notional = GapperSpreadMultiplier.TradeNotional(p) ?? 1000;
        double impact = GapperSpreadMultiplier.ImpactConstant(p), floor = p.CostModel.RoundTripFloor;
        var folds = ExploreFolds.Build(MarketCalendar.TradingDays(train.From, train.To), foldMonths.Initial, foldMonths.Eval);
        var store = await s.StoreAsync(ct);

        // ── per symbol: minute bars of P..E, outcomes ──
        var bySymbol = keyed.GroupBy(x => x.It.Symbol, StringComparer.Ordinal).ToList();
        var rows = new ConcurrentBag<(string Symbol, DateOnly Day, DateTime Utc, string Line)>();
        int done = 0, noBars = 0, noRef = 0;
        Parallel.ForEach(bySymbol, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, req.Parallelism), CancellationToken = ct }, group =>
        {
            var sym = group.Key;
            var ser = daily.GetValueOrDefault(sym);
            var spl = splits.GetValueOrDefault(sym) ?? [];
            var days = group.Select(x => x.Session.Day).Distinct().OrderBy(d => d).ToList();
            // Read contiguous stretches once (bars of the prior session too, for after-hours and overnight items).
            var barsByDay = new Dictionary<DateOnly, List<Candle>>();
            int k0 = 0;
            while (k0 < days.Count)
            {
                int k1 = k0;
                while (k1 + 1 < days.Count && days[k1 + 1].DayNumber - days[k0].DayNumber <= 31) k1++;
                foreach (var b in store.ReadDays(sym, MarketCalendar.PreviousTradingDay(days[k0]), days[k1]))
                {
                    var d = DateOnly.FromDateTime(MinuteBarMath.ToEastern(b.StartUtc));
                    if (!barsByDay.TryGetValue(d, out var l)) barsByDay[d] = l = [];
                    l.Add(b);
                }
                k0 = k1 + 1;
            }
            foreach (var l in barsByDay.Values) l.Sort((a, b) => a.StartUtc.CompareTo(b.StartUtc));
            foreach (var dayGroup in group.GroupBy(x => x.Session.Day))
            {
                var e = dayGroup.Key;
                var prev = MarketCalendar.PreviousTradingDay(e);
                var eBars = barsByDay.GetValueOrDefault(e) ?? [];
                var g = eBars.Count == 0 ? null : RunnerGrid.Build(eBars);
                if (g is null) { Interlocked.Add(ref noBars, dayGroup.Count()); continue; }
                var timeline = (barsByDay.GetValueOrDefault(prev) ?? []).Concat(eBars).Where(b => b.Volume > 0 && b.Close > 0).ToList();
                DateTime? eFirstEnd = eBars.Where(b => b.Volume > 0 && b.Close > 0).Select(b => (DateTime?)b.StartUtc.AddMinutes(1)).FirstOrDefault();
                var u = universe[(sym, e)];
                int j = ser?.Index(e) ?? -1;
                double sigma = ser is not null && j >= 0 ? OrbSigma(ser, j) ?? double.NaN : double.NaN;
                double prevClose = ser is not null && j > 0 && ser.D[j - 1] == prev ? ser.C[j - 1] : double.NaN;
                double dayHigh = double.NaN;
                for (int m = 0; m < RunnerGrid.Minutes; m++) if (g.Real(m) && !(g.H[m] <= dayHigh)) dayHigh = g.H[m];
                var rth = eBars.Where(b => b.Volume > 0 && b.Low > 0 && RunnerGrid.MinuteOf(b.StartUtc) is >= RunnerGrid.RthOpen and < OpeningRangeBreakout.DefaultExitMinute).ToList();
                double cs = rth.Count >= 2 ? TradingCosts.CorwinSchultzSpread(rth) : double.NaN;
                bool SplitBetween(DateOnly a, DateOnly b) => spl.Any(x => x.Ex > a && x.Ex <= b);
                double DailyAt(DateOnly from, int ahead, bool open)
                {
                    if (ser is null) return double.NaN;
                    var target = from;
                    for (int a = 0; a < ahead; a++) target = MarketCalendar.NextTradingDay(target);
                    int k = ser.Index(target);
                    if (k < 0 || target > train.To || SplitBetween(from, target)) return double.NaN;
                    return open ? ser.O[k] : ser.C[k];
                }
                var rs = spl.Where(x => x.Ratio > 1 && x.Ex <= e).OrderByDescending(x => x.Ex).FirstOrDefault();
                double daysSinceRs = rs.Ratio > 1 ? e.DayNumber - rs.Ex.DayNumber : double.NaN;
                double logShares = double.NaN;
                if (cik is not null && shares is not null && cik.TryGetValue(sym, out var c) && shares.KnownBefore(c, e) is { } f && f.Shares > 0) logShares = Math.Log(f.Shares);
                int topRank = topMovers.TryGetValue(e, out var tm) && tm.TryGetValue(sym, out var rk) ? rk : 0;
                int evalFold = folds.FirstOrDefault(fd => fd.InEval(e))?.Number ?? 0;
                string fit = string.Join("", folds.Select(fd => fd.InFit(e) ? '1' : '0'));
                bool splitToday = spl.Any(x => x.Ex == e);
                int nOnDay = dayGroup.Count();

                foreach (var (it, session) in dayGroup)
                {
                    var pubUtc = it.Utc;
                    int start = session.Start;
                    // Reference: the last trade that ended by publication (the prior session's after-hours count); else the prior daily close.
                    double refPx = CloseBy(timeline, pubUtc)?.Price ?? double.NaN;
                    bool refFromPrev = eFirstEnd is not { } fe || fe > pubUtc;
                    if (!(refPx > 0)) { refPx = prevClose; refFromPrev = true; }
                    if (!(refPx > 0) || (splitToday && refFromPrev)) { Interlocked.Increment(ref noRef); continue; }
                    double MaxHigh(int from, int to)
                    {
                        double h = double.NaN;
                        for (int m = Math.Max(0, from); m < Math.Min(RunnerGrid.Minutes, to); m++) if (g.Real(m) && !(g.H[m] <= h)) h = g.H[m];
                        return h;
                    }
                    double run60 = MaxHigh(start, start + 60) / refPx - 1, run240 = MaxHigh(start, start + 240) / refPx - 1;
                    double hiAfter = MaxHigh(start, RunnerGrid.Minutes);
                    int hiMin = -1;
                    for (int m = Math.Max(0, start); m < RunnerGrid.Minutes; m++) if (g.Real(m) && g.H[m] >= hiAfter) { hiMin = m; break; }
                    double closePx = g.Last >= 0 ? g.Cff[RunnerGrid.Minutes - 1] : double.NaN;
                    double runClose = hiAfter / refPx - 1, retClose = closePx / refPx - 1, fade = closePx / hiAfter - 1;
                    double d1 = DailyAt(e, 1, false) / refPx - 1, d2 = DailyAt(e, 2, false) / refPx - 1;
                    double pre30 = double.NaN, pre60 = double.NaN, preVol60 = 0;
                    if (CloseBy(timeline, pubUtc.AddMinutes(-30)) is { } a30) pre30 = Math.Log(refPx / a30.Price);
                    if (CloseBy(timeline, pubUtc.AddMinutes(-60)) is { } a60) pre60 = Math.Log(refPx / a60.Price);
                    foreach (var b in timeline) if (b.StartUtc >= pubUtc.AddMinutes(-60) && b.StartUtc.AddMinutes(1) <= pubUtc) preVol60 += (double)b.Volume;
                    double zScale = sigma > 0 ? sigma : double.NaN;
                    double pre30z = pre30 / (zScale * Math.Sqrt(30.0 / 390)), pre60z = pre60 / (zScale * Math.Sqrt(60.0 / 390));
                    var pubEt = MinuteBarMath.ToEastern(pubUtc);

                    // First-bar entry: the open of the first real bar from the first full minute after publication, within the window, by 15:40.
                    double fbPx = double.NaN, fbClose = double.NaN, fbD1o = double.NaN, fbD2c = double.NaN, fbFrozen = double.NaN;
                    int fbMin = -1;
                    if (start <= CatalystMlLastEntry && OpeningRangeBreakout.FirstOpen(g, start, Math.Min(CatalystMlLastEntry + 1, start + CatalystMlEntryWindow)) is { } fb && fb.Price > 0)
                    {
                        fbMin = fb.Minute; fbPx = fb.Price;
                        fbClose = OpeningRangeBreakout.ExitAt(g, OpeningRangeBreakout.DefaultExitMinute)?.Price ?? double.NaN;
                        fbD1o = DailyAt(e, 1, true); fbD2c = DailyAt(e, 2, false);
                        fbFrozen = Frozen(fbPx, fb.Minute);
                    }
                    // Next 09:30 open: E's open when published by 09:30, else the next session's (daily bar).
                    double opPx = double.NaN, opClose = double.NaN, opD1o = double.NaN, opD2c = double.NaN, opFrozen = double.NaN;
                    int opOff = start <= RunnerGrid.RthOpen ? 0 : 1;
                    if (opOff == 0)
                    {
                        if (OpeningRangeBreakout.FirstOpen(g, RunnerGrid.RthOpen, RunnerGrid.RthOpen + 5) is { } op && op.Price > 0)
                        {
                            opPx = op.Price;
                            opClose = OpeningRangeBreakout.ExitAt(g, OpeningRangeBreakout.DefaultExitMinute)?.Price ?? double.NaN;
                            opD1o = DailyAt(e, 1, true); opD2c = DailyAt(e, 2, false);
                            opFrozen = Frozen(opPx, op.Minute);
                        }
                    }
                    else
                    {
                        var x = e;
                        double o = DailyAt(e, 1, true);
                        if (o > 0)
                        {
                            x = MarketCalendar.NextTradingDay(e);
                            opPx = o;
                            opClose = DailyAt(x, 0, false); opD1o = DailyAt(x, 1, true); opD2c = DailyAt(x, 2, false);
                            opFrozen = Frozen(opPx, RunnerGrid.RthOpen);
                        }
                    }
                    double costRth = (double.IsFinite(cs) ? cs : 0) + 2 * OrbCommissionPerShare / (fbPx > 0 ? fbPx : opPx > 0 ? opPx : refPx);
                    double costRthOp = (double.IsFinite(cs) ? cs : 0) + 2 * OrbCommissionPerShare / (opPx > 0 ? opPx : refPx);

                    var sb = new StringBuilder(512);
                    void F(double v) { sb.Append(','); if (double.IsFinite(v)) sb.Append(v.ToString("G6", CultureInfo.InvariantCulture)); }
                    void S(string v) { sb.Append(','); sb.Append(Csv(v)); }
                    void I(long v) { sb.Append(','); sb.Append(v.ToString(CultureInfo.InvariantCulture)); }
                    sb.Append(Csv(it.Id));
                    S(it.Kind); S(sym); S(e.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)); S(pubEt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
                    I(pubEt.Hour * 60 + pubEt.Minute); I(start); I(evalFold); S(fit);
                    S(it.Form); S(it.Items); S(it.PreClass); I(it.NSymbols); I(nOnDay);
                    S(it.Headline); S(it.Snippet);
                    I(it.Kind == "news" ? CatalystTypes.FromHeadline(it.Headline, it.PreClass) : CatalystTypes.FromFiling(it.Form, it.Items.Split(';', StringSplitOptions.RemoveEmptyEntries)));
                    F(refPx); F(run60); F(run240); F(runClose); F(retClose); F(fade); F(d1); F(d2); I(hiMin);
                    F(dayHigh / u.Pc - 1);
                    F(pre30); F(pre60); F(pre30z); F(pre60z); F(preVol60); F(refPx / u.Pc - 1);
                    F(Math.Log(u.Pc)); F(Math.Log(Math.Max(1, u.AdvDollars))); I(u.Decile); I(u.IsAdr ? 1 : 0); I(u.IsCandidate ? 1 : 0); I(u.Excluded ? 1 : 0);
                    F(logShares); I(topRank); F(daysSinceRs); F(rs.Ratio > 1 ? Math.Log(rs.Ratio) : double.NaN); F(sigma); I((int)e.DayOfWeek);
                    I(fbMin); F(fbPx); F(fbClose); F(fbD1o); F(fbD2c); F(costRth); F(fbFrozen);
                    I(opOff); F(opPx); F(opClose); F(opD1o); F(opD2c); F(costRthOp); F(opFrozen);
                    rows.Add((sym, e, pubUtc, sb.ToString()));
                }

                double Frozen(double entry, int entryMinute)
                {
                    if (multiplier is not { } mult) return double.NaN;
                    double pmVol = 0;
                    for (int m = Math.Max(0, g.First); m < Math.Min(entryMinute, RunnerGrid.Minutes); m++) pmVol += g.V[m];
                    return GapperCostModel.Estimate(new GapperCostInputs(double.IsFinite(cs) ? cs : null, mult, pmVol, double.IsFinite(sigma) ? sigma : null, entry, notional, impact, floor)).RoundTrip;
                }
            }
            int now = Interlocked.Increment(ref done);
            if (now % 500 == 0) s.Logger.LogInformation("[{Now}] explore catalyst-events: {Done}/{Total} symbols", GapperStudyServices.Et(s.UtcNow), now, bySymbol.Count);
        });

        var file = Path.Combine(CatalystMlDir, "events.csv");
        await using (var w = new StreamWriter(file + ".tmp", false, new UTF8Encoding(false)))
        {
            await w.WriteLineAsync(string.Join(',', CatalystMlColumns));
            foreach (var r in rows.OrderBy(r => r.Day).ThenBy(r => r.Symbol, StringComparer.Ordinal).ThenBy(r => r.Utc)) await w.WriteLineAsync(r.Line);
        }
        File.Move(file + ".tmp", file, true);
        var lines = new List<string>
        {
            $"{rows.Count:N0} event rows ({rows.Select(r => (r.Symbol, r.Day)).Distinct().Count():N0} symbol-days, {evSymbols.Count:N0} symbols) -> {file}",
            $"news items read {newsItems:N0}, filings read {filingsRead:N0} (forms 3/4/5 skipped); symbol rows on a train universe session {keyed.Count:N0}; dropped {noBars:N0} without minute bars on E, {noRef:N0} without a reference price (or a split on E with the reference before it)",
        };
        foreach (var l in lines) s.Logger.LogInformation("{Line}", l);
        return (lines, 0, file);
    }

    public static readonly string[] CatalystMlColumns =
    [
        "item_id", "kind", "symbol", "day", "pub_et", "pub_min_et", "start_min", "eval_fold", "fit_mask",
        "form", "items", "pre_class", "n_symbols", "n_on_day", "headline", "snippet", "regex_bits",
        "ref_px", "run60", "run240", "run_close", "ret_close", "fade", "ret_d1", "ret_d2", "hi_min",
        "day_gain", "pre30", "pre60", "pre30_z", "pre60_z", "pre_vol60", "gap_at_pub",
        "log_price", "log_adv_dollars", "adv_decile", "is_adr", "is_candidate", "excluded",
        "log_shares", "top_mover_rank", "days_since_rs", "rs_log_ratio", "sigma_daily", "weekday",
        "fb_min", "fb_px", "fb_close", "fb_d1_open", "fb_d2_close", "cost_rth", "cost_frozen_fb",
        "op_offset", "op_px", "op_close", "op_d1_open", "op_d2_close", "cost_rth_op", "cost_frozen_op",
    ];

    private static string Csv(string v)
    {
        v = v.Replace('\r', ' ').Replace('\n', ' ');
        return v.IndexOfAny([',', '"']) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
    }
}
