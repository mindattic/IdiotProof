using System.Globalization;
using System.Text;
using System.Text.Json;
using IdiotProof.Core.Evidence;
using IdiotProof.Core.Services;
using IdiotProof.Evidence;
using IdiotProof.Evidence.Economics;
using IdiotProof.Evidence.Gappers;
using IdiotProof.Evidence.Search;
using IdiotProof.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IdiotProof.Core.Research.Gappers.Explore;

/// <summary>
/// <c>explore catalyst-split</c> (IP-T492), TRAIN only. (1) Re-derives the per-trade outcomes of the best configurations of
/// `alarm-ride`, `watchlist` and `split-days` (the top <see cref="CaptureTopConfigs"/> of each by net at 0.25x spread, >= 30 trades)
/// with their own simulators in capture mode (no ledger line, no report file; the dump is kept under the explore cache so a rerun
/// skips it), tags every trade by the catalyst classes (`CatalystTypes`) of the news and filings known from D-1 16:00 to its entry,
/// and reports gross and net per class. The watchlist capture also keeps the D-1 watchlist ranks `explore orb` reads. (2) The
/// "news drifts" test: buy the 09:30 open of every train universe day ($0.10-$20, not excluded) with a merger / acquisition-target,
/// PIPE / investment, earnings-beat or FDA item published from D-1 16:00 to D 09:25, sold at 09:45, 10:30, 15:55, the next open or
/// the D+2 close, per class, net of the regular-hours cost. One ledger trial per class row of (1) and per class and exit of (2).
/// </summary>
public sealed partial class GapperExploreStage
{
    public const int CaptureTopConfigs = 3;
    public static readonly TimeSpan HardCatalystCutoffEt = new(9, 25, 0);

    public sealed record CapturedTrade(string Source, string Config, string Symbol, DateOnly Day, int EntryEt, double Weight, double Gross, double[] Cost);

    public sealed record WatchMember(string Symbol, DateOnly Day, double Rank, int Fold);

    public sealed class CaptureDump
    {
        public List<CapturedTrade> Trades { get; set; } = [];
        public List<WatchMember> Watchlist { get; set; } = [];
    }

    /// <summary>Set while `catalyst-split` re-runs a command: its report captures trades here and <see cref="Write"/> logs nothing.</summary>
    private CaptureDump? capture;

    private string CatalystDir => Path.Combine(CacheDir(), "catalyst-split");

    /// <summary>The configurations a capture keeps: the best by net at 0.25x spread with >= 30 trades.</summary>
    private static List<int> CaptureTop(IReadOnlyList<(int Trades, double? Net)> stats) =>
        Enumerable.Range(0, stats.Count).Where(i => stats[i].Trades >= 30 && stats[i].Net is not null).OrderByDescending(i => stats[i].Net!.Value).Take(CaptureTopConfigs).ToList();

    /// <summary>The classes the split reports (a trade can be in several); "other item" and "no catalyst" are exclusive of them.</summary>
    private static readonly (string Name, int Mask)[] CatalystClasses =
    [
        ("merger / acquisition target", CatalystTypes.Bit("acqTarget") | CatalystTypes.Bit("merger")),
        ("PIPE / outside investment", CatalystTypes.Bit("pipe")),
        ("earnings beat", CatalystTypes.Bit("earnBeat")),
        ("earnings miss", CatalystTypes.Bit("earnMiss")),
        ("FDA / clinical", CatalystTypes.Bit("fda")),
        ("contract / partnership", CatalystTypes.Bit("contract")),
        ("non-binding LOI / MOU", CatalystTypes.Bit("loi")),
        ("reverse split", CatalystTypes.Bit("rsplit")),
        ("offering / dilution", CatalystTypes.Bit("offering")),
    ];

    private static int ClassMaskAll => CatalystClasses.Aggregate(0, (a, c) => a | c.Mask);

    /// <summary>The hard catalysts of the open-entry test (the first four classes minus the miss).</summary>
    private static readonly (string Name, int Mask)[] HardClasses = [CatalystClasses[0], CatalystClasses[1], CatalystClasses[2], CatalystClasses[4]];

    private static readonly (string Name, int Minute)[] HardExits = [("09:45", 345), ("10:30", 390), ("15:55", 715), ("next open", -1), ("D+2 close", -2)];

    private sealed record HardEvent(string Symbol, DateOnly Day, int Bits, double Pc, DateTime FirstUtc, string Headline);

    /// <summary>
    /// Stored news (headline + pre-class) and EDGAR filings (8-K, S-1/S-3/424B, SC 13D) for <paramref name="symbols"/> (null = every symbol)
    /// published in [from, to], classified once; filings without a fetched acceptance time count from 17:30 ET of their filing day.
    /// </summary>
    private async Task<Dictionary<string, List<(DateTime Utc, int Bits, string Text)>>> CatalystItemsAsync(HashSet<string>? symbols, DateTime fromUtc, DateTime toUtc, int keepMask, CancellationToken ct)
    {
        var items = new Dictionary<string, List<(DateTime, int, string)>>(StringComparer.Ordinal);
        void Add(string sym, (DateTime, int, string) it) { if (!items.TryGetValue(sym, out var l)) items[sym] = l = []; l.Add(it); }
        await using (var db = await s.Db.CreateDbContextAsync(ct))
        {
            var news = new Dictionary<Guid, (DateTime Utc, int Bits, string Text)>();
            await foreach (var e in db.CatalystEvents.AsNoTracking().Where(e => e.PublishedUtc >= fromUtc && e.PublishedUtc <= toUtc)
                               .Select(e => new { e.Id, e.PublishedUtc, e.Headline, e.PreClass }).AsAsyncEnumerable().WithCancellation(ct))
            {
                int bits = CatalystTypes.FromHeadline(e.Headline, e.PreClass);
                if (keepMask != -1 && (bits & keepMask) == 0) continue;
                news[e.Id] = (e.PublishedUtc, bits, e.Headline ?? "");
            }
            await foreach (var x in db.CatalystEventSymbols.AsNoTracking().Select(x => new { x.EventId, x.Symbol }).AsAsyncEnumerable().WithCancellation(ct))
                if ((symbols is null || symbols.Contains(x.Symbol)) && news.TryGetValue(x.EventId, out var ev)) Add(x.Symbol, ev);
            string[] forms = ["8-K", "8-K/A", "S-1", "S-3", "424B3", "424B4", "424B5", "SC 13D"];
            var fFrom = DateOnly.FromDateTime(fromUtc).AddDays(-1);
            var fTo = DateOnly.FromDateTime(toUtc);
            var filings = await db.EdgarFilingIndex.AsNoTracking().Where(f => f.Symbol != null && forms.Contains(f.FormType) && f.FiledDate >= fFrom && f.FiledDate <= fTo)
                .Select(f => new { f.Symbol, f.FiledDate, f.AcceptedUtc, f.FormType, f.ItemsJson }).ToListAsync(ct);
            foreach (var f in filings)
            {
                if (symbols is not null && !symbols.Contains(f.Symbol!)) continue;
                string[] its = [];
                if (!string.IsNullOrEmpty(f.ItemsJson)) try { its = JsonSerializer.Deserialize<string[]>(f.ItemsJson) ?? []; } catch (JsonException) { }
                int bits = CatalystTypes.FromFiling(f.FormType, its);
                if (keepMask != -1 && (bits & keepMask) == 0) continue;
                var known = f.AcceptedUtc ?? MinuteBarMath.ToUtc(f.FiledDate, new TimeSpan(17, 30, 0));
                if (known < fromUtc || known > toUtc) continue;
                Add(f.Symbol!, (known, bits, $"{f.FormType} items {(its.Length == 0 ? "n/a" : string.Join(",", its))}"));
            }
        }
        foreach (var l in items.Values) l.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        return items;
    }

    /// <summary>Train universe days ($0.10-$20, not excluded) with a hard catalyst published from D-1 16:00 to D 09:25 ET.</summary>
    private async Task<List<HardEvent>> HardCatalystEventsAsync(CancellationToken ct)
    {
        var train = s.Params.Split("train");
        int hardMask = HardClasses.Aggregate(0, (a, c) => a | c.Mask);
        var from = MinuteBarMath.ToUtc(MarketCalendar.PreviousTradingDay(train.From), new TimeSpan(16, 0, 0));
        var to = MinuteBarMath.ToUtc(train.To, HardCatalystCutoffEt);
        var items = await CatalystItemsAsync(null, from, to, hardMask, ct);
        var byKey = new Dictionary<(string, DateOnly), (int Bits, DateTime First, string Text)>();
        foreach (var (sym, l) in items)
            foreach (var (utc, bits, text) in l)
            {
                var et = EasternTime.FromUtc(utc);
                var date = DateOnly.FromDateTime(et);
                DateOnly d;
                if (MarketCalendar.IsTradingDay(date) && et.TimeOfDay <= HardCatalystCutoffEt) d = date;
                else if (!MarketCalendar.IsTradingDay(date) || et.TimeOfDay >= new TimeSpan(16, 0, 0)) d = MarketCalendar.NextTradingDay(date);
                else continue; // published during the session: not known before the open
                if (d < train.From || d > train.To) continue;
                byKey[(sym, d)] = byKey.TryGetValue((sym, d), out var v) ? (v.Bits | (bits & hardMask), v.First, v.Text) : (bits & hardMask, utc, text);
            }
        var symbols = byKey.Keys.Select(k => k.Item1).Distinct().ToList();
        var universe = new Dictionary<(string, DateOnly), double>();
        decimal lo = s.Params.Universe.PriceMinInclusive, hi = s.Params.Universe.PriceMaxExclusive;
        for (int i = 0; i < symbols.Count; i += 400)
        {
            var batch = symbols.Skip(i).Take(400).ToList();
            await using var db = await s.Db.CreateDbContextAsync(ct);
            foreach (var u in await db.GapperStudyUniverseDays.AsNoTracking()
                         .Where(u => u.Split == "train" && u.ExcludedReason == null && batch.Contains(u.Symbol) && u.AdjPrevClose >= lo && u.AdjPrevClose < hi)
                         .Select(u => new { u.Symbol, u.DateEt, u.AdjPrevClose }).ToListAsync(ct))
                universe[(u.Symbol, u.DateEt)] = (double)u.AdjPrevClose;
        }
        return byKey.Where(kv => universe.ContainsKey(kv.Key))
            .Select(kv => new HardEvent(kv.Key.Item1, kv.Key.Item2, kv.Value.Bits, universe[kv.Key], kv.Value.First, kv.Value.Text))
            .OrderBy(e => e.Day).ThenBy(e => e.Symbol, StringComparer.Ordinal).ToList();
    }

    private async Task<(List<string>, int, string?)> CatalystSplitAsync(ExploreRequest req, CancellationToken ct)
    {
        Directory.CreateDirectory(CatalystDir);
        var lines = new List<string>();
        foreach (var (source, run) in new (string, Func<Task<(List<string>, int, string?)>>)[]
                 {
                     ("split-days", () => SplitDaysAsync(req, ct)),
                     ("watchlist", () => WatchlistAsync(req, ct)),
                     ("alarm-ride", () => AlarmRideAsync(req, ct)),
                 })
        {
            var file = Path.Combine(CatalystDir, source + ".json");
            if (File.Exists(file) && !req.Force) continue;
            s.Logger.LogInformation("[{Now}] explore catalyst-split: re-deriving {Source} trades (capture mode: no ledger line, no report)", GapperStudyServices.Et(s.UtcNow), source);
            capture = new CaptureDump();
            try
            {
                await run();
                await File.WriteAllTextAsync(file + ".tmp", JsonSerializer.Serialize(capture), ct);
                File.Move(file + ".tmp", file, true);
            }
            finally
            {
                capture = null;
                halts = null;
                GC.Collect();
            }
        }
        // `--times capture-only` stops after the captures (no report, no ledger line), e.g. while `orb-pull` still fetches the open-entry days.
        if (req.Times.Contains("capture-only", StringComparer.OrdinalIgnoreCase))
            return ([$"captures in {CatalystDir}: " + string.Join(", ", Directory.GetFiles(CatalystDir, "*.json").Select(Path.GetFileName))], 0, null);
        var dumps = new Dictionary<string, CaptureDump>(StringComparer.Ordinal);
        foreach (var source in new[] { "alarm-ride", "watchlist", "split-days" })
        {
            var file = Path.Combine(CatalystDir, source + ".json");
            if (File.Exists(file)) dumps[source] = JsonSerializer.Deserialize<CaptureDump>(await File.ReadAllTextAsync(file, ct)) ?? new CaptureDump();
        }
        return await CatalystReportAsync(dumps, req, ct);
    }

    private async Task<(List<string>, int, string?)> CatalystReportAsync(Dictionary<string, CaptureDump> dumps, ExploreRequest req, CancellationToken ct)
    {
        var p = s.Params;
        var train = s.Params.Split("train");
        var trainDays = MarketCalendar.TradingDays(train.From, train.To);
        var folds = ExploreFolds.Build(trainDays, foldMonths.Initial, foldMonths.Eval);
        var md = new StringBuilder(Header("catalyst split (IP-T492): the long rides by catalyst class, and the hard-catalyst open entry", folds));
        var trials = new List<ExploreTrial>();
        var json = new Dictionary<string, object?>();
        string Pct(double? v) => DeepPct(v);
        string B((double Mean, double Lo, double Hi, int Days)? m) => m is { } x ? DeepBounds(x.Mean, x.Lo, x.Hi) : "n/a";

        // ── 1. the existing rides by class ──
        var all = dumps.Values.SelectMany(d => d.Trades).ToList();
        var symbols = all.Select(t => t.Symbol).ToHashSet(StringComparer.Ordinal);
        var tags = new Dictionary<CapturedTrade, (int Bits, bool Any)>(ReferenceEqualityComparer.Instance);
        if (all.Count > 0)
        {
            var from = MinuteBarMath.ToUtc(MarketCalendar.PreviousTradingDay(all.Min(t => t.Day)), new TimeSpan(16, 0, 0));
            var to = MinuteBarMath.ToUtc(all.Max(t => t.Day), new TimeSpan(16, 0, 0));
            var items = await CatalystItemsAsync(symbols, from, to, -1, ct);
            foreach (var t in all)
            {
                var lo = MinuteBarMath.ToUtc(MarketCalendar.PreviousTradingDay(t.Day), new TimeSpan(16, 0, 0));
                var hi = MinuteBarMath.ToUtc(t.Day, TimeSpan.FromMinutes(t.EntryEt));
                int bits = 0; bool any = false;
                if (items.TryGetValue(t.Symbol, out var l))
                    foreach (var (utc, b, _) in l)
                        if (utc > lo && utc <= hi) { any = true; bits |= b; }
                tags[t] = (bits, any);
            }
        }
        var groups = CatalystClasses.Select(c => (c.Name, Pick: (Func<CapturedTrade, bool>)(t => (tags[t].Bits & c.Mask) != 0))).ToList();
        groups.Add(("other item (none of the classes above)", t => tags[t].Any && (tags[t].Bits & ClassMaskAll) == 0));
        groups.Add((CatalystTypes.NoCatalyst, t => !tags[t].Any));

        md.AppendLine("## 1. The long rides by catalyst class");
        md.AppendLine();
        md.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"Each command was re-run in capture mode with its own simulator and data (no ledger line, no report), and the per-trade outcomes of its {CaptureTopConfigs} best configurations by net at 0.25x spread (>= 30 trades) were kept. A trade's class = the `CatalystTypes` types of the stored news (headline + pre-class) and EDGAR filings (8-K items, S-1/S-3/424B, SC 13D; a filing without a fetched acceptance time counts from 17:30 ET of its filing day) for the symbol published after D-1 16:00 ET and at or before the entry minute (known before entry). A trade can be in several classes; \"other item\" = an item matching none of them; \"no catalyst\" = no stored item at all (the news store covers the candidate windows only, so this is \"none stored\", not \"none\"). Weighted as each command weighs (the 10% population sample counts 10); mean with a day-clustered 95% interval. Costs: each command's frozen model and its 0.25x-spread variant (the split-day and watchlist rides' 0.25x column; the alarm ride's too)."));
        md.AppendLine();
        var classJson = new List<object>();
        foreach (var (source, dump) in dumps)
        {
            var configs = dump.Trades.Select(t => t.Config).Distinct().ToList();
            for (int k = 0; k < configs.Count; k++)
            {
                var cfg = configs[k];
                var list = dump.Trades.Where(t => t.Config == cfg).ToList();
                md.AppendLine($"**{source}, config {k + 1}: {cfg}** ({list.Count:N0} trades)");
                md.AppendLine();
                md.AppendLine("| class | trades | days | gross [95%] | net frozen [95%] | net 0.25x [95%] | win rate (net 0.25x) |");
                md.AppendLine("|---|---|---|---|---|---|---|");
                foreach (var (name, pick) in new[] { ("all trades", (Func<CapturedTrade, bool>)(_ => true)) }.Concat(groups))
                {
                    var sub = list.Where(pick).ToList();
                    if (sub.Count == 0) { md.AppendLine($"| {name} | 0 | 0 | n/a | n/a | n/a | n/a |"); continue; }
                    var gross = ExploreDerived.ClusteredMean(sub.Select(t => (t.Day, t.Gross, t.Weight)));
                    var net0 = ExploreDerived.ClusteredMean(sub.Select(t => (t.Day, t.Gross - t.Cost[0], t.Weight)));
                    var net2 = ExploreDerived.ClusteredMean(sub.Select(t => (t.Day, t.Gross - t.Cost[2], t.Weight)));
                    double win = sub.Where(t => t.Gross - t.Cost[2] > 0).Sum(t => t.Weight) / Math.Max(1e-9, sub.Sum(t => t.Weight));
                    md.AppendLine(string.Create(CultureInfo.InvariantCulture, $"| {name} | {sub.Count:N0} | {sub.Select(t => t.Day).Distinct().Count():N0} | {B(gross)} | {B(net0)} | {B(net2)} | {DeepShare(win)} |"));
                    classJson.Add(new { source, config = cfg, rank = k + 1, cls = name, trades = sub.Count, gross, netFrozen = net0, net025 = net2, win });
                    if (k == 0 && name != "all trades")
                    {
                        var perFold = folds.Select(f => ExploreDerived.ClusteredMean(sub.Where(t => f.InEval(t.Day)).Select(t => (t.Day, t.Gross - t.Cost[0], t.Weight)))?.Mean).ToList();
                        trials.Add(new ExploreTrial
                        {
                            Track = "catalyst-split", Config = $"long {source} best config ({cfg}) class={name}", Metric = "meanNetReturn (long, frozen cost)", FoldValues = perFold, Pooled = net0?.Mean, Rows = sub.Count,
                            Note = string.Create(CultureInfo.InvariantCulture, $"lo={Pct(net0?.Lo)} hi={Pct(net0?.Hi)} gross={Pct(gross?.Mean)} net0.25x={Pct(net2?.Mean)}"),
                        });
                    }
                }
                md.AppendLine();
            }
        }
        json["byClass"] = classJson;

        // ── 2. hard-catalyst open entry ──
        var (multiplier, source2) = GapperSpreadMultiplier.Resolve(s.Paths, p);
        var notional = GapperSpreadMultiplier.TradeNotional(p) ?? 1000;
        double impact = GapperSpreadMultiplier.ImpactConstant(p), floor = p.CostModel.RoundTripFloor;
        var events = await HardCatalystEventsAsync(ct);
        var store = await s.StoreAsync(ct);
        var daily = await OrbDailyAsync(events.Select(e => e.Symbol), train.To, ct);
        var splitDays = new Dictionary<string, List<DateOnly>>(StringComparer.Ordinal);
        await using (var db = await s.Db.CreateDbContextAsync(ct))
            foreach (var sp in await db.GapperStudySplits.AsNoTracking().Select(x => new { x.Symbol, x.ExDate }).ToListAsync(ct))
            {
                if (!splitDays.TryGetValue(sp.Symbol, out var l)) splitDays[sp.Symbol] = l = [];
                l.Add(sp.ExDate);
            }
        var outcomes = new (double Gross, double CostRth, double CostFrozen)?[events.Count, HardExits.Length];
        int noBars = 0, noOpen = 0;
        Parallel.For(0, events.Count, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, req.Parallelism), CancellationToken = ct }, i =>
        {
            var e = events[i];
            var bars = store.ReadDay(e.Symbol, e.Day);
            var g = bars.Count == 0 ? null : RunnerGrid.Build(bars);
            if (g is null) { Interlocked.Increment(ref noBars); return; }
            if (OpeningRangeBreakout.FirstOpen(g, RunnerGrid.RthOpen, RunnerGrid.RthOpen + 5) is not { } en || !(en.Price > 0)) { Interlocked.Increment(ref noOpen); return; }
            var rth = bars.Where(b => b.Volume > 0 && b.Low > 0 && RunnerGrid.MinuteOf(b.StartUtc) is >= RunnerGrid.RthOpen and < OpeningRangeBreakout.DefaultExitMinute).OrderBy(b => b.StartUtc).ToList();
            double cs = rth.Count >= 2 ? TradingCosts.CorwinSchultzSpread(rth) : double.NaN;
            double pmVol = 0;
            for (int m = Math.Max(0, g.First); m < en.Minute; m++) pmVol += g.V[m];
            double? sigma = null;
            OrbDailySeries? ser = daily.GetValueOrDefault(e.Symbol);
            int j = ser?.Index(e.Day) ?? -1;
            if (ser is not null && j >= 0) sigma = OrbSigma(ser, j);
            double costRth = (double.IsFinite(cs) ? cs : 0) + 2 * OrbCommissionPerShare / en.Price;
            double costFrozen = multiplier is { } mult
                ? GapperCostModel.Estimate(new GapperCostInputs(double.IsFinite(cs) ? cs : null, mult, pmVol, sigma, en.Price, notional, impact, floor)).RoundTrip
                : double.NaN;
            var splits = splitDays.GetValueOrDefault(e.Symbol) ?? [];
            for (int x = 0; x < HardExits.Length; x++)
            {
                double? exit = null;
                if (HardExits[x].Minute > 0) exit = OpeningRangeBreakout.ExitAt(g, HardExits[x].Minute)?.Price;
                else if (ser is not null && j >= 0)
                {
                    int ahead = -HardExits[x].Minute;
                    int k = j + ahead;
                    var target = e.Day;
                    for (int a = 0; a < ahead; a++) target = MarketCalendar.NextTradingDay(target);
                    // The held session must be the next trading day(s) on file, in train, with no split between.
                    if (k < ser.D.Length && ser.D[k] == target && target <= train.To && !splits.Any(d => d > e.Day && d <= target))
                        exit = ahead == 1 ? ser.O[k] : ser.C[k];
                }
                if (exit is { } px && px > 0) outcomes[i, x] = (px / en.Price - 1, costRth, costFrozen);
            }
        });

        md.AppendLine("## 2. Hard-catalyst open entry (\"news drifts\")");
        md.AppendLine();
        md.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"Events: train universe days (prior close $0.10-$20, not excluded) with a stored news item (`CatalystTypes` on headline + pre-class) or filing (8-K item 3.02 or SC 13D = PIPE / investment) of the class published from D-1 16:00 to D {HardCatalystCutoffEt:hh\\:mm} ET (weekend and holiday items roll to the next session; an item during the session is not counted). {events.Count:N0} events ({noBars:N0} without minute bars, {noOpen:N0} without a 09:30-09:34 bar). Buy the open of the first real bar at 09:30-09:34; sell at the open of the first bar from 09:45 / 10:30 / 15:55 (else the last close), at the next session's open, or at the D+2 close (daily bars, raw; dropped when a split falls in between or the session is past train). No stop. Cost: regular hours = Corwin-Schultz over D's 09:30-15:55 minute bars plus ${OrbCommissionPerShare:0.0000}/share each side; the frozen model ({(multiplier is { } mm ? $"premarket multiplier {mm:0.###} from {source2}" : "unavailable")}, impact on the 04:00-to-entry volume, floor {floor:P1}) for comparison. The news store holds the candidate windows (gapper days +/- a few sessions), so these are mostly small-cap names with a move around them. Per fold = the four eval windows (net, regular hours)."));
        md.AppendLine();
        md.AppendLine("| class | exit | trades | days | gross [95%] | net RTH [95%] | net frozen [95%] | per fold (net RTH) | median gross | win rate (net RTH) |");
        md.AppendLine("|---|---|---|---|---|---|---|---|---|---|");
        var hardJson = new List<object>();
        foreach (var (cname, mask) in HardClasses)
            for (int x = 0; x < HardExits.Length; x++)
            {
                var rows = Enumerable.Range(0, events.Count).Where(i => (events[i].Bits & mask) != 0 && outcomes[i, x] is not null)
                    .Select(i => (events[i].Day, O: outcomes[i, x]!.Value)).ToList();
                var gross = ExploreDerived.ClusteredMean(rows.Select(r => (r.Day, r.O.Gross, 1.0)));
                var rthNet = ExploreDerived.ClusteredMean(rows.Select(r => (r.Day, r.O.Gross - r.O.CostRth, 1.0)));
                var frozen = ExploreDerived.ClusteredMean(rows.Where(r => double.IsFinite(r.O.CostFrozen)).Select(r => (r.Day, r.O.Gross - r.O.CostFrozen, 1.0)));
                var perFold = folds.Select(f => ExploreDerived.ClusteredMean(rows.Where(r => f.InEval(r.Day)).Select(r => (r.Day, r.O.Gross - r.O.CostRth, 1.0)))?.Mean).ToList();
                double? median = MedianOf(rows.Select(r => r.O.Gross).ToList());
                double win = rows.Count == 0 ? double.NaN : (double)rows.Count(r => r.O.Gross - r.O.CostRth > 0) / rows.Count;
                md.AppendLine(string.Create(CultureInfo.InvariantCulture,
                    $"| {cname} | {HardExits[x].Name} | {rows.Count:N0} | {rows.Select(r => r.Day).Distinct().Count():N0} | {B(gross)} | {B(rthNet)} | {B(frozen)} | {string.Join(" / ", perFold.Select(Pct))} | {Pct(median)} | {DeepShare(win)} |"));
                hardJson.Add(new { cls = cname, exit = HardExits[x].Name, trades = rows.Count, gross, netRth = rthNet, netFrozen = frozen, perFold, median, win });
                trials.Add(new ExploreTrial
                {
                    Track = "hard-catalyst-open", Config = $"long {cname} news D-1 16:00..D 09:25, buy 09:30 open, sell {HardExits[x].Name}", Metric = "meanNetReturn (long, regular-hours cost, all train days)",
                    FoldValues = perFold, Pooled = rthNet?.Mean, Rows = rows.Count,
                    Note = string.Create(CultureInfo.InvariantCulture, $"lo={Pct(rthNet?.Lo)} hi={Pct(rthNet?.Hi)} gross={Pct(gross?.Mean)} netFrozen={Pct(frozen?.Mean)} median={Pct(median)}"),
                });
            }
        md.AppendLine();
        md.AppendLine("Example events (first 15 by date with an outcome at 15:55):");
        md.AppendLine();
        md.AppendLine("| symbol | day | classes | prior close | first item (ET) | to 15:55 | to D+2 close |");
        md.AppendLine("|---|---|---|---|---|---|---|");
        foreach (var i in Enumerable.Range(0, events.Count).Where(i => outcomes[i, 2] is not null).Take(15))
        {
            var e = events[i];
            string cls = string.Join(", ", HardClasses.Where(c => (e.Bits & c.Mask) != 0).Select(c => c.Name));
            md.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"| {e.Symbol} | {e.Day:yyyy-MM-dd} | {cls} | {e.Pc:0.00} | {EasternTime.FromUtc(e.FirstUtc):MM-dd HH:mm}: {Clean(e.Headline)} | {Pct(outcomes[i, 2]?.Gross)} | {Pct(outcomes[i, 4]?.Gross)} |"));
        }
        md.AppendLine();
        md.AppendLine("Exploration only (train). Nothing after 2024-10-31 is read: news to the last train day's 09:25, daily bars to its close, and a hold that would end after it is dropped.");
        json["hardOpen"] = hardJson;
        json["hardEvents"] = events.Count;
        return Write("catalyst-split", md, trials, json);
    }
}
