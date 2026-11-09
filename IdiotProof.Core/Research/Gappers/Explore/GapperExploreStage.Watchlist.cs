using System.Globalization;
using System.Text;
using System.Text.Json;
using IdiotProof.Core.Evidence;
using IdiotProof.Evidence;
using IdiotProof.Evidence.Evaluation;
using IdiotProof.Evidence.Gappers;
using IdiotProof.Evidence.Search;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IdiotProof.Core.Research.Gappers.Explore;

/// <summary>
/// <c>explore watchlist</c> (IP-T484): runner anatomy and a two-stage watchlist on TRAIN only. A runner on D is a day whose
/// 04:00-16:00 high reached +50/+100/+300% over the adjusted prior close (minute bars). Stage 1 ranks every population
/// symbol-day at D-1 16:00 (the D-1 panel plus daily-bar, split, halt, dilution and catalyst prerequisites) with fold-fitted
/// boosted trees, logistic regression and a point score, and reports recall@N / precision@N. Stage 2 detects ignition all
/// day (a volume burst over the trailing 30 minutes with a 2% break of the session high, under a gap cap) on the watchlist
/// and on every name. Stage 3 buys the bar after ignition and rides it out with stops, trails, half sales and cooling exits,
/// net of the frozen cost model at 1.0/0.5/0.25x spread. One ledger trial per detection and trade configuration.
/// </summary>
public sealed partial class GapperExploreStage
{
    public const string WatchlistDecision = "D-1 16:00";
    public static readonly double[] RunnerLevels = [0.5, 1.0, 3.0];
    private static readonly string[] RunnerLevelNames = [">=+50%", ">=+100%", ">=+300%"];
    public static readonly int[] WatchlistSizes = [10, 20, 50, 100];
    public static readonly int[] WatchlistTradeSizes = [20, 50, 100];
    public static readonly double[] IgnitionKs = [5, 10, 20];
    public static readonly double?[] IgnitionCaps = [null, 0.5, 1.0];
    /// <summary>The ignition minute must print at least this many dollars.</summary>
    public const double IgnitionDollarFloor = 5_000;
    /// <summary>Trailing-volume floor per minute: max(100 shares, ADV20 / 1560 = a quarter of the mean regular-hours minute).</summary>
    public const double IgnitionMinShares = 100;
    public const double IgnitionAdvDivisor = 1560;
    public static readonly double[] WatchSpreadScales = [1.0, 0.5, 0.25];
    public static readonly double[] WatchCapacity = [1_000, 5_000];
    /// <summary>Catalysts known by D-1 16:00: items published in the five calendar days before.</summary>
    public const int CatalystLookbackDays = 5;
    /// <summary>No entry at or after 15:45 ET (grid minute 705).</summary>
    public const int LastEntryMinute = 705;
    private const int IgnitionCount = 9;
    /// <summary>The trades and the stage-2 watchlist use the boosted model for >= +100% (fixed before the run).</summary>
    private const int PrimaryLearner = 0, PrimaryTarget = 1;

    private static readonly string[] LearnerNames = ["boosted trees (all features)", "logistic (prerequisite set)", "point score (fold-fitted top 5 prerequisites)", "benchmark: D-1 top movers (D-1 close return)"];

    public static List<RunnerExitPlan> WatchlistExits()
    {
        var list = new List<RunnerExitPlan>();
        foreach (var cool in new[] { RunnerCooling.None, RunnerCooling.Velocity, RunnerCooling.VwapLoss })
            foreach (var half in new[] { false, true })
                foreach (var stop in new[] { 5.0, 8.0 })
                    foreach (var trail in new[] { 15.0, 25.0, 35.0 })
                        list.Add(new RunnerExitPlan(string.Create(CultureInfo.InvariantCulture,
                                $"stop -{stop:0}%, trail {trail:0}% once +10%{(half ? ", half at +20%" : "")}{cool switch { RunnerCooling.Velocity => ", velocity death", RunnerCooling.VwapLoss => ", VWAP loss", _ => "" }}, out 15:55"),
                            stop, 10, trail, half ? 20 : null, cool));
        return list;
    }

    private static string IgnName(int ic) => string.Create(CultureInfo.InvariantCulture, $"k={IgnitionKs[ic / 3]:0} cap={(IgnitionCaps[ic % 3] is { } c ? $"<+{c * 100:0}%" : "none")}");

    private sealed class WlSim
    {
        public int Minute;
        public float Fill;
        public readonly float[] Cost = new float[3];
        public float[] Gross = [];
        public float[] Cap1k = [];
        public float[] Cap5k = [];
        public bool[] Full1k = [];
        public bool[] Full5k = [];
    }

    private sealed class WlIgn
    {
        public int Minute;
        public float Close, Gap, Remaining;
        public bool BeforeHod;
        public readonly WlSim?[] Sims = new WlSim?[2];
    }

    private sealed class WlRow
    {
        public int R;
        public int Fold = -1;
        public int DayIdx = -1;
        public double Weight;
        public bool HasBars;
        public RunnerDayFacts? Facts;
        public readonly WlIgn?[] Ign = new WlIgn?[IgnitionCount];
        public readonly int[] Episodes = new int[3];
        public bool HaltAtHigh;
        public sbyte Rerun50 = -1, Rerun100 = -1;
        public int CatD1, CatSame;
        public bool AnyD1, AnySame;
        /// <summary>[learner, target]: weight of better-scored names that eval morning (NaN off the eval windows).</summary>
        public readonly double[,] Rank = new double[4, 3];
        public double Gain => Facts?.Gain ?? double.NaN;
        public bool Runner(int level) => Facts is { } f && f.Gain >= RunnerLevels[level];
    }

    private sealed class DailySeries
    {
        public DateOnly[] D = [];
        public double[] HighRet = [], CloseRet = [], Dvol = [], LogRet = [];
        public readonly List<(DateOnly Ex, double Ratio, bool Inferred)> Reverse = [];
    }

    private static readonly string[] DailyNames =
        ["x.wl.up20Days5", "x.wl.up20Days10", "x.wl.up20Days30", "x.wl.up50Days30", "x.wl.up100Days10", "x.wl.up100Days30", "x.wl.sessionsSinceUp20", "x.wl.sessionsSinceUp100",
         "x.wl.dormancy", "x.wl.dvolD1Ratio", "x.wl.logDollarVol20", "x.wl.retD1", "x.wl.daysSinceRs", "x.wl.rsLogRatio", "x.wl.rsInferred",
         "x.wl.rsEffToday", "x.wl.rsEffTomorrow", "x.wl.rsCumLogRatio2y", "x.wl.newListing"];

    private static readonly string[] OtherNames = ["x.wl.logPrice", "x.wl.isAdr", "x.wl.halts5", "x.wl.dilFilings90", "x.wl.catAny", "x.wl.foreignProxy", "x.wl.holdingCo", "x.wl.noFiling24h", "x.wl.dilSupply90"];

    /// <summary>A name that reads foreign: Ltd / Limited / plc / N.V. / S.A. / AG / SE (Cayman, BVI and most Asia-linked listings use these).</summary>
    private static readonly System.Text.RegularExpressions.Regex ForeignName = new(@"\b(Ltd|Limited|plc|N\.?V\.?|S\.?A\.?|AG|SE|Pte)\b\.?", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    private static readonly System.Text.RegularExpressions.Regex HoldingName = new(@"\bHoldings?\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private sealed record WlStats(int Trades, int Days, double WeightedTrades, double? Gross, (double Mean, double Lo, double Hi, int Days)?[] Net, double?[][] Folds, double? Worst1,
        double? Top1, double? Top5, double? Ge50, double? Ge100, double?[] Cap, double?[] CapFull, double? MeanCost, double?[] LomoLo, ReturnMoments Moments, bool[] Finalist);

    private async Task<(List<string>, int, string?)> WatchlistAsync(ExploreRequest req, CancellationToken ct)
    {
        var p = s.Params;
        var (multiplier, source) = GapperSpreadMultiplier.Resolve(s.Paths, p);
        if (multiplier is null) return ([$"cost model unavailable ({source}); run `calibrate-spread` first"], 0, null);
        var notional = GapperSpreadMultiplier.TradeNotional(p) ?? throw new InvalidDataException("params.json has no costModel.impact.tradeNotionalUsd.");
        double impact = GapperSpreadMultiplier.ImpactConstant(p), floor = p.CostModel.RoundTripFloor;
        var profilePath = s.Paths.ProfileFile(false);
        if (!File.Exists(profilePath)) throw new InvalidOperationException($"{Path.GetFileName(profilePath)} does not exist; run `snapshot train` first.");
        var profile = TimeOfDayProfile.Load(profilePath);
        var store = await s.StoreAsync(ct);

        // ── 0. the D-1 16:00 panel (train only; Load refuses nothing else exists) ──
        var t = Load(WatchlistDecision);
        var folds = Folds(t);
        if (folds.Count == 0) return (["no folds: the panel has too few train days"], 0, null);
        var trainEnd = t.Days.Max();
        var trainStart = t.Days.Min();
        var universe = (await s.StudyDaysAsync("train", null, null, ct)).GroupBy(u => (u.Symbol, u.DateEt)).ToDictionary(g => g.Key, g => g.First());
        var seen = new HashSet<(string, DateOnly)>();
        var rows = new List<WlRow>();
        for (int r = 0; r < t.Rows; r++)
            if (t.Weights[r] > 0 && universe.ContainsKey((t.Symbols[r], t.Days[r])) && seen.Add((t.Symbols[r], t.Days[r])))
                rows.Add(new WlRow { R = r, Weight = t.Weights[r], Fold = folds.FindIndex(f => f.InEval(t.Days[r])) });
        var evalDays = rows.Where(w => w.Fold >= 0).Select(w => t.Days[w.R]).Distinct().OrderBy(d => d).ToList();
        var dayIndex = evalDays.Select((d, i) => (d, i)).ToDictionary(x => x.d, x => x.i);
        foreach (var w in rows) if (w.Fold >= 0) w.DayIdx = dayIndex[t.Days[w.R]];
        foreach (var w in rows) for (int a = 0; a < 4; a++) for (int b = 0; b < 3; b++) w.Rank[a, b] = double.NaN;
        s.Logger.LogInformation("[{Now}] explore watchlist: {Rows} panel rows, {Eval} eval days", GapperStudyServices.Et(s.UtcNow), rows.Count, evalDays.Count);

        // ── 1. daily bars, splits (recorded and inferred), excluded-day census ──
        var symbols = rows.Select(w => t.Symbols[w.R]).Distinct(StringComparer.Ordinal).ToList();
        var splits = new Dictionary<string, List<(DateOnly Ex, double Factor)>>(StringComparer.Ordinal);
        var excluded = new List<(string Reason, int N, int R50, int R100, int R300)>();
        await using (var db = await s.Db.CreateDbContextAsync(ct))
        {
            foreach (var sp in await db.GapperStudySplits.AsNoTracking().Select(x => new { x.Symbol, x.ExDate, x.OldRate, x.NewRate }).ToListAsync(ct))
            {
                if (sp.NewRate <= 0 || sp.OldRate <= 0) continue;
                if (!splits.TryGetValue(sp.Symbol, out var l)) splits[sp.Symbol] = l = [];
                l.Add((sp.ExDate, (double)(sp.OldRate / sp.NewRate)));
            }
            var ex = await db.GapperStudyUniverseDays.AsNoTracking().Where(u => u.Split == "train" && u.ExcludedReason != null)
                .Select(u => new { u.ExcludedReason, u.DayHigh, u.AdjPrevClose }).ToListAsync(ct);
            foreach (var g in ex.GroupBy(x => x.ExcludedReason!))
            {
                int Cnt(double lvl) => g.Count(x => x.DayHigh is { } h && x.AdjPrevClose > 0 && (double)(h / x.AdjPrevClose) - 1 >= lvl);
                excluded.Add((g.Key, g.Count(), Cnt(0.5), Cnt(1.0), Cnt(3.0)));
            }
        }
        var series = new Dictionary<string, DailySeries>(StringComparer.Ordinal);
        var barFrom = new DateOnly(2021, 3, 1);
        for (int i = 0; i < symbols.Count; i += 400)
        {
            var batch = symbols.Skip(i).Take(400).ToList();
            await using var db = await s.Db.CreateDbContextAsync(ct);
            var raw = await db.GapperStudyDailyBars.AsNoTracking().Where(b => batch.Contains(b.Symbol) && b.DateEt >= barFrom && b.DateEt <= trainEnd)
                .Select(b => new { b.Symbol, b.DateEt, b.Open, b.High, b.Low, b.Close, b.Volume }).ToListAsync(ct);
            foreach (var g in raw.GroupBy(b => b.Symbol))
                series[g.Key] = BuildSeries(g.OrderBy(b => b.DateEt).Select(b => (b.DateEt, (double)b.Open, (double)b.High, (double)b.Low, (double)b.Close, (double)b.Volume)).ToList(),
                    splits.GetValueOrDefault(g.Key) ?? []);
        }
        s.Logger.LogInformation("[{Now}] explore watchlist: daily series for {Symbols} symbols", GapperStudyServices.Et(s.UtcNow), series.Count);
        var splitSection = await SplitWindowSectionAsync(rows, t, series, splits, barFrom, trainEnd, ct);

        // ── 2. catalysts: news headlines and EDGAR filings, classified once ──
        var catFrom = MinuteBarMath.ToUtc(trainStart.AddDays(-CatalystLookbackDays - 3), TimeSpan.Zero);
        var catTo = MinuteBarMath.ToUtc(trainEnd, new TimeSpan(16, 0, 0));
        var catalysts = new Dictionary<string, List<(DateTime Utc, int Bits)>>(StringComparer.Ordinal);
        var offerings = new Dictionary<string, List<DateTime>>(StringComparer.Ordinal);
        var supply = new Dictionary<string, List<DateTime>>(StringComparer.Ordinal);
        var anyFiling = new Dictionary<string, List<DateTime>>(StringComparer.Ordinal);
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        int newsItems = 0, filingItems = 0;
        await using (var db = await s.Db.CreateDbContextAsync(ct))
        {
            var events = new Dictionary<Guid, (DateTime Utc, int Bits)>();
            await foreach (var e in db.CatalystEvents.AsNoTracking().Where(e => e.PublishedUtc >= catFrom && e.PublishedUtc <= catTo)
                               .Select(e => new { e.Id, e.PublishedUtc, e.Headline, e.PreClass }).AsAsyncEnumerable().WithCancellation(ct))
                events[e.Id] = (e.PublishedUtc, CatalystTypes.FromHeadline(e.Headline, e.PreClass));
            var symSet = symbols.ToHashSet(StringComparer.Ordinal);
            await foreach (var x in db.CatalystEventSymbols.AsNoTracking().Select(x => new { x.EventId, x.Symbol }).AsAsyncEnumerable().WithCancellation(ct))
            {
                if (!symSet.Contains(x.Symbol) || !events.TryGetValue(x.EventId, out var ev)) continue;
                if (!catalysts.TryGetValue(x.Symbol, out var l)) catalysts[x.Symbol] = l = [];
                l.Add(ev);
                newsItems++;
            }
            string[] forms = ["8-K", "8-K/A", "S-1", "S-3", "424B3", "424B4", "424B5", "SC 13D"];
            var fFrom = DateOnly.FromDateTime(catFrom).AddDays(-100);
            var filings = await db.EdgarFilingIndex.AsNoTracking().Where(f => f.Symbol != null && forms.Contains(f.FormType) && f.FiledDate >= fFrom && f.FiledDate <= trainEnd)
                .Select(f => new { f.Symbol, f.FiledDate, f.AcceptedUtc, f.FormType, f.ItemsJson }).ToListAsync(ct);
            foreach (var f in filings)
            {
                if (!symSet.Contains(f.Symbol!)) continue;
                // Known at acceptance; without a fetched header, assume after the close of the filing day (17:30 ET).
                var known = f.AcceptedUtc ?? MinuteBarMath.ToUtc(f.FiledDate, new TimeSpan(17, 30, 0));
                string[] items = [];
                if (!string.IsNullOrEmpty(f.ItemsJson)) try { items = JsonSerializer.Deserialize<string[]>(f.ItemsJson) ?? []; } catch (JsonException) { }
                int bits = CatalystTypes.FromFiling(f.FormType, items);
                if (!catalysts.TryGetValue(f.Symbol!, out var l)) catalysts[f.Symbol!] = l = [];
                l.Add((known, bits));
                filingItems++;
                if (!f.FormType.StartsWith("8-K", StringComparison.Ordinal) && !f.FormType.StartsWith("SC", StringComparison.Ordinal))
                {
                    if (!offerings.TryGetValue(f.Symbol!, out var o)) offerings[f.Symbol!] = o = [];
                    o.Add(known);
                }
                // Dilution supply (methods-who-buys.md): 424B, S-1 and 8-K item 1.01 (F-1 lives in the dilution files, added as a prerequisite).
                if (f.FormType.StartsWith("424B", StringComparison.Ordinal) || f.FormType == "S-1" || (f.FormType.StartsWith("8-K", StringComparison.Ordinal) && items.Any(it => it.Trim() == "1.01")))
                {
                    if (!supply.TryGetValue(f.Symbol!, out var o)) supply[f.Symbol!] = o = [];
                    o.Add(known);
                }
            }
            // Any SEC filing (every form in the index, Form 4 included) for "no filing in the prior 24 hours".
            await foreach (var f in db.EdgarFilingIndex.AsNoTracking().Where(f => f.Symbol != null && f.FiledDate >= fFrom && f.FiledDate <= trainEnd)
                               .Select(f => new { f.Symbol, f.FiledDate, f.AcceptedUtc }).AsAsyncEnumerable().WithCancellation(ct))
            {
                if (!symSet.Contains(f.Symbol!)) continue;
                if (!anyFiling.TryGetValue(f.Symbol!, out var o)) anyFiling[f.Symbol!] = o = [];
                o.Add(f.AcceptedUtc ?? MinuteBarMath.ToUtc(f.FiledDate, new TimeSpan(17, 30, 0)));
            }
            foreach (var c in await db.GapperStudyCikMap.AsNoTracking().Select(c => new { c.Symbol, c.Name }).ToListAsync(ct)) names[c.Symbol] = c.Name;
        }
        foreach (var l in catalysts.Values) l.Sort((a, b) => a.Utc.CompareTo(b.Utc));
        s.Logger.LogInformation("[{Now}] explore watchlist: {News} news and {Filings} filing items classified", GapperStudyServices.Et(s.UtcNow), newsItems, filingItems);

        halts ??= HaltHistory.Load(HaltsDir());
        var haltHistory = halts;

        // ── 3. per-row point-in-time prerequisites (all known by D-1 16:00) and the day-2 outcome ──
        var prevDay = new Dictionary<DateOnly, DateOnly>();
        DateOnly Prev(DateOnly d) { lock (prevDay) { if (!prevDay.TryGetValue(d, out var v)) prevDay[d] = v = MarketCalendar.PreviousTradingDay(d); return v; } }
        var sigma = new double[rows.Count];
        var extra = new Dictionary<int, double[]>(rows.Count);
        string[] catNames = CatalystTypes.Keys.Select(k => "x.wl.cat." + k).ToArray();
        var newNames = DailyNames.Concat(OtherNames).Concat(catNames).ToArray();
        int col6k = t.Feature(DilutionFeatures.SixKCount30), colF1 = t.Feature(DilutionFeatures.DaysSinceForeignReg);
        for (int i = 0; i < rows.Count; i++)
        {
            var w = rows[i];
            string sym = t.Symbols[w.R];
            var day = t.Days[w.R];
            var u = universe[(sym, day)];
            var vals = new double[newNames.Length];
            Array.Fill(vals, double.NaN);
            sigma[i] = double.NaN;
            if (series.TryGetValue(sym, out var ser))
            {
                var dv = DailyFeatures(ser, day, MarketCalendar.NextTradingDay(day), trainEnd, out var r50, out var r100, out var sg);
                Array.Copy(dv, vals, dv.Length);
                w.Rerun50 = r50; w.Rerun100 = r100;
                sigma[i] = sg ?? double.NaN;
            }
            var d1 = Prev(day);
            var cutoff = MinuteBarMath.ToUtc(d1, new TimeSpan(16, 0, 0));
            var dayEnd = MinuteBarMath.ToUtc(day, new TimeSpan(16, 0, 0));
            int o = DailyNames.Length;
            vals[o] = u.AdjPrevClose > 0 ? Math.Log((double)u.AdjPrevClose) : double.NaN;
            vals[o + 1] = u.IsAdr ? 1 : 0;
            if (haltHistory is not null)
            {
                var d5 = d1;
                for (int k = 0; k < 4; k++) d5 = Prev(d5);
                vals[o + 2] = haltHistory.Between(sym, MinuteBarMath.ToUtc(d5, new TimeSpan(4, 0, 0)), cutoff).Count();
            }
            vals[o + 3] = offerings.TryGetValue(sym, out var off) ? off.Count(x => x <= cutoff && x > cutoff.AddDays(-90)) : 0;
            if (catalysts.TryGetValue(sym, out var cl))
            {
                var from = cutoff.AddDays(-CatalystLookbackDays);
                foreach (var (utc, bits) in cl)
                {
                    if (utc > from && utc <= cutoff) { w.AnyD1 = true; w.CatD1 |= bits; }
                    else if (utc > cutoff && utc <= dayEnd) { w.AnySame = true; w.CatSame |= bits; }
                }
            }
            vals[o + 4] = w.AnyD1 ? 1 : 0;
            string name = names.GetValueOrDefault(sym) ?? "";
            bool sixK = col6k >= 0 && t.Value(w.R, col6k) >= 1, f1 = colF1 >= 0 && !float.IsNaN(t.Value(w.R, colF1));
            vals[o + 5] = u.IsAdr || ForeignName.IsMatch(name) || sixK || f1 ? 1 : 0;
            vals[o + 6] = HoldingName.IsMatch(name) ? 1 : 0;
            vals[o + 7] = anyFiling.TryGetValue(sym, out var af) && af.Any(x => x <= cutoff && x > cutoff.AddHours(-24)) ? 0 : 1;
            vals[o + 8] = supply.TryGetValue(sym, out var sp) ? sp.Count(x => x <= cutoff && x > cutoff.AddDays(-90)) : 0;
            for (int k = 0; k < catNames.Length; k++) vals[o + OtherNames.Length + k] = (w.CatD1 & (1 << k)) != 0 ? 1 : 0;
            extra[w.R] = vals;
        }
        var t2 = ExploreDerived.AddColumns(t, newNames, r => extra.TryGetValue(r, out var v) ? v : Enumerable.Repeat(double.NaN, newNames.Length).ToArray());
        extra.Clear();
        GC.Collect();

        // ── 4. the bar pass: label, anatomy, ignitions and (eval windows) the trade grid ──
        var exits = WatchlistExits();
        int csCol = t2.Feature("x." + PriorDaysCorwinSchultzFeature.FeatureName);
        int done = 0;
        Parallel.For(0, rows.Count, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, req.Parallelism), CancellationToken = ct }, i =>
        {
            var w = rows[i];
            string sym = t.Symbols[w.R];
            var day = t.Days[w.R];
            var u = universe[(sym, day)];
            double pc = (double)u.AdjPrevClose;
            var g = pc > 0 ? RunnerGrid.Build(store.ReadDay(sym, day)) : null;
            if (Interlocked.Increment(ref done) % 10_000 == 0)
                s.Logger.LogInformation("[{Now}] explore watchlist: bar pass {Done}/{Total}", GapperStudyServices.Et(s.UtcNow), done, rows.Count);
            if (g is null) return;
            var f = RunnerIgnition.Facts(g, pc);
            w.HasBars = true;
            w.Facts = f;
            if (haltHistory is not null && f.Hod >= 0)
            {
                var hodUtc = MinuteBarMath.ToUtc(day, RunnerGrid.StartEt + TimeSpan.FromMinutes(f.Hod));
                w.HaltAtHigh = haltHistory.Between(sym, hodUtc.AddMinutes(-5), hodUtc.AddMinutes(6)).Any();
            }
            var cum = new double[RunnerGrid.Minutes + 1];
            for (int m = 0; m < RunnerGrid.Minutes; m++) cum[m + 1] = cum[m] + g.V[m];
            double minuteFloor = Math.Max(IgnitionMinShares, (double)u.Adv20Shares / IgnitionAdvDivisor);
            double? cs = csCol >= 0 && t2.Value(w.R, csCol) is var cv && !float.IsNaN(cv) ? cv : null;
            double? sg = double.IsFinite(sigma[i]) ? sigma[i] : null;
            var byMinute = new Dictionary<int, WlIgn>();
            for (int ki = 0; ki < IgnitionKs.Length; ki++)
            {
                var ev = RunnerIgnition.Ignitions(g, pc, IgnitionKs[ki], minuteFloor, IgnitionDollarFloor);
                int lastEp = -1000;
                foreach (var e in ev) if (e.Minute - lastEp >= 30) { w.Episodes[ki]++; lastEp = e.Minute; }
                for (int ci = 0; ci < IgnitionCaps.Length; ci++)
                {
                    var cap = IgnitionCaps[ci];
                    var hit = ev.FirstOrDefault(e => cap is null || (!float.IsNaN(e.GapBefore) && e.GapBefore < cap));
                    if (hit.Close <= 0) continue;
                    if (!byMinute.TryGetValue(hit.Minute, out var ign))
                    {
                        float after = 0;
                        for (int m = hit.Minute + 1; m <= g.Last; m++) if (g.Real(m) && g.H[m] > after) after = g.H[m];
                        ign = new WlIgn { Minute = hit.Minute, Close = hit.Close, Gap = hit.GapBefore, Remaining = after > 0 ? after / hit.Close - 1 : 0, BeforeHod = f.Hod > hit.Minute };
                        if (w.Fold >= 0)
                            for (int fi = 0; fi < 2; fi++)
                            {
                                var entry = fi == 0 ? RunnerIgnition.MarketEntry(g, hit.Minute) : RunnerIgnition.PassiveEntry(g, hit.Minute);
                                if (entry is not { } en || en.Minute >= LastEntryMinute || en.Fill <= 0) continue;
                                ign.Sims[fi] = Simulate(g, en.Minute, en.Fill, fi == 1, exits, cum, cs, sg, u.AdvDecile, day, profile, multiplier.Value, notional, impact, floor);
                            }
                        byMinute[hit.Minute] = ign;
                    }
                    w.Ign[ki * 3 + ci] = ign;
                }
            }
        });
        s.Logger.LogInformation("[{Now}] explore watchlist: bar pass done, {Bars} rows with bars", GapperStudyServices.Et(s.UtcNow), rows.Count(w => w.HasBars));

        // ── 5. stage 1 models per fold and target ──
        var prereqs = Prerequisites(t2);
        var scorable = rows.Where(w => w.HasBars && double.IsFinite(w.Gain)).ToList();
        var allFeatures = Enumerable.Range(0, t2.FeatureCount).ToArray();
        string[] logisticBase = ["AbnormalReturnD1", "AbnormalReturnD2", "AbnormalReturnD3", "VolumeRatioD1", "VolumeRatioD2", "VolumeRatioD3", "x.priorDayTopMoverRank", "x.priorDayVolumeRatio",
            "x.csSpreadPrior", "DaysSinceReverseSplit", "DaysSince424B", "DaysSinceS1", "CatalystScore", FloatFeatures.LogShares, RegShoFeatures.ListedD1, DilutionFeatures.AtmActive, DilutionFeatures.SixKCount30];
        var logisticFeatures = Enumerable.Range(0, t2.FeatureCount).Where(f => t2.FeatureNames[f].StartsWith("x.wl.", StringComparison.Ordinal) || logisticBase.Contains(t2.FeatureNames[f])).ToArray();
        int retD1 = t2.Feature("x.wl.retD1"), dvRatio = t2.Feature("x.wl.dvolD1Ratio");
        var scores = new double[4, 3][];
        for (int a = 0; a < 4; a++) for (int b = 0; b < 3; b++) { scores[a, b] = new double[scorable.Count]; Array.Fill(scores[a, b], double.NaN); }
        var pointRules = new List<string>();
        IReadOnlyList<(string Feature, double Share)> importance = [];
        var idxOf = scorable.Select((w, i) => (w, i)).ToDictionary(x => x.w, x => x.i);
        for (int ti = 0; ti < RunnerLevels.Length; ti++)
        {
            var y = scorable.Select(w => w.Gain >= RunnerLevels[ti]).ToArray();
            for (int k = 0; k < folds.Count; k++)
            {
                var fit = Enumerable.Range(0, scorable.Count).Where(i => folds[k].InFit(t2.Days[scorable[i].R])).ToList();
                var eval = Enumerable.Range(0, scorable.Count).Where(i => folds[k].InEval(t2.Days[scorable[i].R])).ToList();
                if (fit.Count == 0 || eval.Count == 0 || !fit.Any(i => y[i])) continue;
                var b = FitBoosted(t2, allFeatures, scorable, y, fit, eval, out var model);
                var l = FitLogistic(t2, logisticFeatures, scorable, y, fit, eval);
                for (int j = 0; j < eval.Count; j++) { scores[0, ti][eval[j]] = b[j]; scores[1, ti][eval[j]] = l[j]; }
                if (ti == PrimaryTarget && k == folds.Count - 1) importance = model.Importance();
                // Point score: the five prerequisites with the highest fit-window lift (>= 2, >= 15 weighted runners), log2-lift points.
                double baseRate = fit.Sum(i => y[i] ? scorable[i].Weight : 0) / Math.Max(1e-9, fit.Sum(i => scorable[i].Weight));
                var chosen = prereqs.Select(pq =>
                {
                    double wq = 0, pos = 0;
                    foreach (var i in fit) if (pq.Has(scorable[i].R, scorable[i])) { wq += scorable[i].Weight; if (y[i]) pos += scorable[i].Weight; }
                    double lift = wq > 0 && baseRate > 0 ? pos / wq / baseRate : 0;
                    return (pq, lift, pos);
                }).Where(x => x.lift >= 2 && x.pos >= 15).OrderByDescending(x => x.lift).Take(5).ToList();
                pointRules.Add($"{RunnerLevelNames[ti]} fold {k + 1}: " + (chosen.Count == 0 ? "none qualify" : string.Join("; ", chosen.Select(c => string.Create(CultureInfo.InvariantCulture, $"{c.pq.Name} (lift {c.lift:0.0}, {Math.Clamp(Math.Round(Math.Log2(c.lift)), 1, 4):0} pt)")))));
                foreach (var j in eval)
                {
                    var w = scorable[j];
                    double sc = chosen.Sum(c => c.pq.Has(w.R, w) ? Math.Clamp(Math.Round(Math.Log2(c.lift)), 1, 4) : 0);
                    double tie = dvRatio >= 0 && t2.Value(w.R, dvRatio) is var dr && !float.IsNaN(dr) ? Math.Tanh(dr / 10) * 1e-3 : 0;
                    scores[2, ti][j] = sc + tie;
                    scores[3, ti][j] = retD1 >= 0 && t2.Value(w.R, retD1) is var rv && !float.IsNaN(rv) ? rv : -1e9;
                }
                s.Logger.LogInformation("[{Now}] explore watchlist: {Target} fold {Fold} fitted ({Fit} fit, {Eval} eval rows)", GapperStudyServices.Et(s.UtcNow), RunnerLevelNames[ti], k + 1, fit.Count, eval.Count);
            }
        }
        // Ranks per eval morning: the weight of better-scored names (a sampled name stands for 10).
        foreach (var dayGroup in Enumerable.Range(0, scorable.Count).Where(i => scorable[i].Fold >= 0).GroupBy(i => scorable[i].DayIdx))
            for (int a = 0; a < 4; a++)
                for (int b = 0; b < 3; b++)
                {
                    double cum = 0;
                    foreach (var i in dayGroup.Where(i => !double.IsNaN(scores[a, b][i])).OrderByDescending(i => scores[a, b][i]).ThenBy(i => scorable[i].R))
                    {
                        scorable[i].Rank[a, b] = cum;
                        cum += scorable[i].Weight;
                    }
                }

        return WatchlistReport(t2, folds, rows, scorable, evalDays, prereqs, importance, pointRules, exits, excluded, splitSection, multiplier.Value, source, notional, newsItems, filingItems, haltHistory is not null);
    }

    /// <summary>
    /// The days protocol U(D) drops (split ex-date in [D-25, D+1], or an unexplained +200% / -66.7% close move in [D-25, D]), on daily bars only
    /// (they carry no minute bars): the split-adjusted daily high and close over the adjusted prior close, by where D sits against the nearest
    /// reverse split or ratio change, beside the same daily-bar label on the panel rows. Rendered as report lines.
    /// </summary>
    private async Task<(List<string> Lines, object Json)> SplitWindowSectionAsync(List<WlRow> rows, ExploreTable t, Dictionary<string, DailySeries> series,
        Dictionary<string, List<(DateOnly Ex, double Factor)>> splits, DateOnly barFrom, DateOnly trainEnd, CancellationToken ct)
    {
        List<(string Symbol, DateOnly Day, string Reason)> ex;
        await using (var db = await s.Db.CreateDbContextAsync(ct))
            ex = (await db.GapperStudyUniverseDays.AsNoTracking().Where(u => u.Split == "train" && (u.ExcludedReason == "split-window" || u.ExcludedReason == "unexplained-move"))
                .Select(u => new { u.Symbol, u.DateEt, u.ExcludedReason }).ToListAsync(ct)).Select(u => (u.Symbol, u.DateEt, u.ExcludedReason!)).ToList();
        var missing = ex.Select(e => e.Symbol).Distinct(StringComparer.Ordinal).Where(x => !series.ContainsKey(x)).ToList();
        for (int i = 0; i < missing.Count; i += 400)
        {
            var batch = missing.Skip(i).Take(400).ToList();
            await using var db = await s.Db.CreateDbContextAsync(ct);
            var raw = await db.GapperStudyDailyBars.AsNoTracking().Where(b => batch.Contains(b.Symbol) && b.DateEt >= barFrom && b.DateEt <= trainEnd)
                .Select(b => new { b.Symbol, b.DateEt, b.Open, b.High, b.Low, b.Close, b.Volume }).ToListAsync(ct);
            foreach (var g in raw.GroupBy(b => b.Symbol))
                series[g.Key] = BuildSeries(g.OrderBy(b => b.DateEt).Select(b => (b.DateEt, (double)b.Open, (double)b.High, (double)b.Low, (double)b.Close, (double)b.Volume)).ToList(), splits.GetValueOrDefault(g.Key) ?? []);
        }
        static (double High, double Fade)? At(DailySeries ser, DateOnly day)
        {
            int j = Array.BinarySearch(ser.D, day);
            if (j < 1 || !double.IsFinite(ser.HighRet[j])) return null;
            return (ser.HighRet[j], (1 + ser.CloseRet[j]) / (1 + ser.HighRet[j]) - 1);
        }
        var buckets = new Dictionary<string, List<(double High, double Fade)>>(StringComparer.Ordinal);
        void Add(string b, (double, double) v) { if (!buckets.TryGetValue(b, out var l)) buckets[b] = l = []; l.Add(v); }
        foreach (var (sym, day, reason) in ex)
        {
            if (!series.TryGetValue(sym, out var ser) || At(ser, day) is not { } v) continue;
            var next = MarketCalendar.NextTradingDay(day);
            string bucket;
            if (ser.Reverse.FirstOrDefault(r => r.Ex == day) is { Ratio: > 0 } r0) bucket = r0.Inferred ? "inferred ratio change effective on D (no split on record)" : "reverse split effective on D (calendared)";
            else if (ser.Reverse.Any(r => r.Ex == next && !r.Inferred)) bucket = "reverse split effective on D+1 (calendared)";
            else if (ser.Reverse.Any(r => r.Ex < day && r.Ex.DayNumber >= day.DayNumber - 10)) bucket = "1-10 days after a reverse split / ratio change";
            else if (ser.Reverse.Any(r => r.Ex < day && r.Ex.DayNumber >= day.DayNumber - 25)) bucket = "11-25 days after a reverse split / ratio change";
            else if ((splits.GetValueOrDefault(sym) ?? []).Any(sp => sp.Factor < 1 && sp.Ex.DayNumber >= day.DayNumber - 25 && sp.Ex.DayNumber <= next.DayNumber)) bucket = "forward-split window";
            else bucket = $"{reason}, no split or ratio change found";
            Add(bucket, v);
        }
        // The panel on the same daily-bar label (weighted), so the rates compare like for like.
        double pw = 0;
        var pr = new double[RunnerLevels.Length];
        foreach (var w in rows)
        {
            if (!series.TryGetValue(t.Symbols[w.R], out var ser) || At(ser, t.Days[w.R]) is not { } v) continue;
            pw += w.Weight;
            for (int l = 0; l < RunnerLevels.Length; l++) if (v.High >= RunnerLevels[l]) pr[l] += w.Weight;
        }
        var lines = new List<string>
        {
            "**The excluded split days on daily bars.** The frozen sample drops the split effective day through D+1 (and 25 days after), so the panel's runner set does NOT include them. Here they are added back for this exploration on daily bars (no minute bars exist for them, so no ignition or trade): the daily high and close over the split-adjusted prior close (recorded split factor, or the inferred ratio for an unrecorded change), by where D sits against the nearest reverse split. The panel line is the same daily-bar label on the panel rows (weighted), the like-for-like base. Daily bars are the regular-session bar, so a premarket-only spike is missed here.",
            "",
            "| days | symbol-days | high >= +50% | >= +100% | >= +300% | lift >= +100% vs panel | median high | median fade (close vs high) |",
            "|---|---|---|---|---|---|---|---|",
        };
        double base100 = pw > 0 ? pr[1] / pw : double.NaN;
        lines.Add(string.Create(CultureInfo.InvariantCulture, $"| panel (population, weighted) | {rows.Count:N0} | {pr[0] / pw:P2} | {pr[1] / pw:P3} | {pr[2] / pw:P3} | 1.0x | n/a | n/a |"));
        var json = new List<object>();
        foreach (var (b, l) in buckets.OrderByDescending(kv => kv.Value.Count))
        {
            double R(double lvl) => (double)l.Count(x => x.High >= lvl) / l.Count;
            lines.Add(string.Create(CultureInfo.InvariantCulture,
                $"| {b} | {l.Count:N0} | {R(0.5):P1} | {R(1.0):P1} | {R(3.0):P1} | {R(1.0) / base100:0.0}x | {DeepPct(MedianOf(l.Select(x => x.High).ToList()))} | {DeepPct(MedianOf(l.Select(x => x.Fade).ToList()))} |"));
            json.Add(new { bucket = b, days = l.Count, r50 = R(0.5), r100 = R(1.0), r300 = R(3.0), lift100 = R(1.0) / base100, medianHigh = MedianOf(l.Select(x => x.High).ToList()), medianFade = MedianOf(l.Select(x => x.Fade).ToList()) });
        }
        lines.Add("");
        return (lines, new { panelDaily = new { weight = pw, r50 = pr[0] / pw, r100 = pr[1] / pw, r300 = pr[2] / pw }, buckets = json });
    }

    private static double[] FitBoosted(ExploreTable t, int[] featureIdx, List<WlRow> rows, bool[] y, List<int> fit, List<int> eval, out BoostedTreeModel model)
    {
        int d = featureIdx.Length;
        var x = new float[(long)fit.Count * d];
        for (int i = 0; i < fit.Count; i++)
            for (int j = 0; j < d; j++) x[(long)i * d + j] = t.Value(rows[fit[i]].R, featureIdx[j]);
        model = GradientBoostedTrees.Fit(featureIdx.Select(f => t.FeatureNames[f]).ToList(), x, fit.Select(i => y[i]).ToArray(), fit.Select(i => rows[i].Weight).ToArray());
        var row = new float[d];
        var result = new double[eval.Count];
        for (int i = 0; i < eval.Count; i++)
        {
            for (int j = 0; j < d; j++) row[j] = t.Value(rows[eval[i]].R, featureIdx[j]);
            result[i] = model.Predict(row);
        }
        return result;
    }

    private static double[] FitLogistic(ExploreTable t, int[] featureIdx, List<WlRow> rows, bool[] y, List<int> fit, List<int> eval)
    {
        double[] Row(int i) => featureIdx.Select(f => (double)t.Value(rows[i].R, f)).ToArray();
        var model = LogisticRegression.FitWeighted(featureIdx.Select(f => t.FeatureNames[f]).ToList(), fit.Select(Row).ToList(), fit.Select(i => y[i]).ToList(), fit.Select(i => rows[i].Weight).ToList());
        return eval.Select(i => model.Predict(Row(i))).ToArray();
    }

    private static WlSim Simulate(RunnerGrid g, int minute, double fill, bool passive, List<RunnerExitPlan> exits, double[] cum, double? cs, double? sigma, int decile, DateOnly day,
        TimeOfDayProfile profile, double multiplier, double notional, double impact, double floor)
    {
        double pmCumVol = cum[minute] - cum[Math.Max(0, g.First)];
        double v5 = cum[minute] - cum[Math.Max(0, minute - 5)];
        double Cost(double size, double scale) => GapperCostModel.Estimate(new GapperCostInputs(cs, multiplier * scale, pmCumVol, sigma, fill, size, impact, floor)).RoundTrip;
        var sim = new WlSim { Minute = minute, Fill = (float)fill, Gross = new float[exits.Count], Cap1k = new float[exits.Count], Cap5k = new float[exits.Count], Full1k = new bool[exits.Count], Full5k = new bool[exits.Count] };
        for (int l = 0; l < WatchSpreadScales.Length; l++) sim.Cost[l] = (float)Cost(notional, WatchSpreadScales[l]);
        var entryEt = RunnerGrid.StartEt + TimeSpan.FromMinutes(minute);
        for (int x = 0; x < exits.Count; x++)
        {
            var res = RunnerIgnition.Simulate(g, minute, fill, exits[x], passive);
            if (res is not { } rr) { sim.Gross[x] = float.NaN; sim.Cap1k[x] = sim.Cap5k[x] = float.NaN; continue; }
            sim.Gross[x] = rr.Gross;
            var exitEt = RunnerGrid.StartEt + TimeSpan.FromMinutes(rr.ExitMinute);
            double? vexit = ParticipationCap.ForecastWindowVolume(profile, decile, pmCumVol, entryEt, exitEt - TimeSpan.FromMinutes(1), exitEt + TimeSpan.FromMinutes(4));
            var c1 = ParticipationCap.Size(fill, WatchCapacity[0], v5, vexit);
            var c5 = ParticipationCap.Size(fill, WatchCapacity[1], v5, vexit);
            sim.Cap1k[x] = c1.Skip ? float.NaN : (float)(rr.Gross - Cost(c1.NotionalUsd, 1));
            sim.Cap5k[x] = c5.Skip ? float.NaN : (float)(rr.Gross - Cost(c5.NotionalUsd, 1));
            sim.Full1k[x] = c1.FullSize;
            sim.Full5k[x] = c5.FullSize;
        }
        return sim;
    }

    private static DailySeries BuildSeries(List<(DateOnly D, double O, double H, double L, double C, double V)> bars, List<(DateOnly Ex, double Factor)> splits)
    {
        int n = bars.Count;
        var s = new DailySeries { D = bars.Select(b => b.D).ToArray(), HighRet = new double[n], CloseRet = new double[n], Dvol = new double[n], LogRet = new double[n] };
        for (int i = 0; i < n; i++) s.Dvol[i] = bars[i].C * bars[i].V;
        s.HighRet[0] = s.CloseRet[0] = s.LogRet[0] = double.NaN;
        for (int i = 1; i < n; i++)
        {
            double prev = bars[i - 1].C, f = 1;
            bool recorded = false;
            foreach (var (ex, factor) in splits)
                if (ex > bars[i - 1].D && ex <= bars[i].D) { f *= factor; recorded = true; }
            bool inferred = false;
            if (!recorded && prev > 0 && (bars[i].L >= 3 * prev || bars[i].H <= prev / 3))
            {
                // An unrecorded ratio change (an ADS ratio change, a missing split): the whole day trades 3x away on ordinary dollar volume.
                double mean = 0; int cnt = 0;
                for (int k = Math.Max(0, i - 20); k < i; k++) { mean += s.Dvol[k]; cnt++; }
                mean = cnt > 0 ? mean / cnt : 0;
                if (mean > 0 && s.Dvol[i] < 3 * mean) { f = bars[i].O / prev; inferred = true; }
            }
            double pc = prev * f;
            s.HighRet[i] = pc > 0 ? bars[i].H / pc - 1 : double.NaN;
            s.CloseRet[i] = pc > 0 ? bars[i].C / pc - 1 : double.NaN;
            s.LogRet[i] = pc > 0 && bars[i].C > 0 ? Math.Log(bars[i].C / pc) : double.NaN;
            if (f >= 1.5) s.Reverse.Add((bars[i].D, f, inferred));
        }
        // A recorded reverse split whose ex-date fell on a day without a bar still counts.
        foreach (var (ex, factor) in splits)
            if (factor >= 1.5 && !s.Reverse.Any(r => Math.Abs(r.Ex.DayNumber - ex.DayNumber) <= 3)) s.Reverse.Add((ex, factor, false));
        s.Reverse.Sort((a, b) => a.Ex.CompareTo(b.Ex));
        return s;
    }

    /// <summary>The <see cref="DailyNames"/> values from sessions strictly before <paramref name="day"/>, and the day-2 outcome over the next five train sessions.</summary>
    private static double[] DailyFeatures(DailySeries s, DateOnly day, DateOnly nextDay, DateOnly trainEnd, out sbyte rerun50, out sbyte rerun100, out double? sigma)
    {
        var v = new double[DailyNames.Length];
        Array.Fill(v, double.NaN);
        int j = Array.BinarySearch(s.D, day);
        bool onDay = j >= 0;
        if (j < 0) j = ~j;
        double Count(double th, int sessions) { int c = 0; for (int k = Math.Max(1, j - sessions); k < j; k++) if (s.HighRet[k] >= th) c++; return c; }
        double Since(double th) { for (int k = j - 1; k >= 1; k--) if (s.HighRet[k] >= th) return j - k; return double.NaN; }
        if (j >= 2)
        {
            v[0] = Count(0.2, 5); v[1] = Count(0.2, 10); v[2] = Count(0.2, 30); v[3] = Count(0.5, 30); v[4] = Count(1.0, 10); v[5] = Count(1.0, 30);
            v[6] = Since(0.2); v[7] = Since(1.0);
            var last20 = Enumerable.Range(Math.Max(0, j - 20), Math.Min(20, j)).Select(k => s.Dvol[k]).ToList();
            double dv20 = last20.Count >= 10 ? last20.Average() : double.NaN;
            var hist = Enumerable.Range(Math.Max(0, j - 120), Math.Max(0, j - 20 - Math.Max(0, j - 120))).Select(k => s.Dvol[k]).OrderBy(x => x).ToList();
            double med = hist.Count >= 20 ? hist[hist.Count / 2] : double.NaN;
            v[8] = med > 0 ? dv20 / med : double.NaN;
            v[9] = dv20 > 0 ? s.Dvol[j - 1] / dv20 : double.NaN;
            v[10] = double.IsFinite(dv20) ? Math.Log(1 + dv20) : double.NaN;
            v[11] = s.CloseRet[j - 1];
        }
        var rs = s.Reverse.LastOrDefault(r => r.Ex <= day);
        if (rs.Ratio > 0)
        {
            v[12] = day.DayNumber - rs.Ex.DayNumber;
            v[13] = Math.Log(rs.Ratio);
            v[14] = rs.Inferred ? 1 : 0;
        }
        // Calendared splits (recorded ex-dates only: inferred ratio changes are not known in advance).
        v[15] = s.Reverse.Any(r => !r.Inferred && r.Ex == day) ? 1 : 0;
        v[16] = s.Reverse.Any(r => !r.Inferred && r.Ex == nextDay) ? 1 : 0;
        v[17] = s.Reverse.Where(r => r.Ex <= day && r.Ex.DayNumber > day.DayNumber - 730).Sum(r => Math.Log(r.Ratio));
        // Listed within two years: the first daily bar, when it is after the data start (2021-09); unknown earlier listings count as old.
        v[18] = s.D.Length > 0 && s.D[0] > new DateOnly(2021, 9, 15) && day.DayNumber - s.D[0].DayNumber < 730 ? 1 : 0;
        var rets = Enumerable.Range(Math.Max(1, j - 20), Math.Max(0, j - Math.Max(1, j - 20))).Select(k => s.LogRet[k]).Where(double.IsFinite).ToList();
        sigma = rets.Count >= 3 ? EvidenceStats.StdDev(rets) : null;
        rerun50 = rerun100 = -1;
        if (onDay)
        {
            double max = double.NaN;
            for (int k = j + 1; k <= Math.Min(s.D.Length - 1, j + 5) && s.D[k] <= trainEnd; k++)
                if (double.IsFinite(s.HighRet[k])) max = double.IsNaN(max) ? s.HighRet[k] : Math.Max(max, s.HighRet[k]);
            if (!double.IsNaN(max)) { rerun50 = (sbyte)(max >= 0.5 ? 1 : 0); rerun100 = (sbyte)(max >= 1.0 ? 1 : 0); }
        }
        return v;
    }

    private sealed record Prereq(string Name, Func<int, WlRow, bool> Has);

    /// <summary>The binary prerequisites for the lift table, the point score and the anatomy; every one is known by D-1 16:00.</summary>
    private static List<Prereq> Prerequisites(ExploreTable t)
    {
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int f = 0; f < t.FeatureCount; f++) index.TryAdd(t.FeatureNames[f], f);
        float V(int r, string name) => index.TryGetValue(name, out var f) ? t.Value(r, f) : float.NaN;
        bool Between(int r, string name, double lo, double hi) => V(r, name) is var v && !float.IsNaN(v) && v >= lo && v <= hi;
        var list = new List<Prereq>
        {
            new("reverse split / ratio change 0-10 days before", (r, _) => Between(r, "x.wl.daysSinceRs", 0, 10)),
            new("reverse split / ratio change 11-30 days before", (r, _) => Between(r, "x.wl.daysSinceRs", 11, 30)),
            new("reverse split / ratio change 31-90 days before", (r, _) => Between(r, "x.wl.daysSinceRs", 31, 90)),
            new("inferred ratio change (no split on record) within 90 days", (r, _) => Between(r, "x.wl.daysSinceRs", 0, 90) && V(r, "x.wl.rsInferred") > 0.5f),
            new("+20% day in the last 5 sessions", (r, _) => V(r, "x.wl.up20Days5") >= 1),
            new("+20% day in the last 10 sessions", (r, _) => V(r, "x.wl.up20Days10") >= 1),
            new("+20% day in the last 30 sessions", (r, _) => V(r, "x.wl.up20Days30") >= 1),
            new("+20% days >= 3 in the last 30 sessions", (r, _) => V(r, "x.wl.up20Days30") >= 3),
            new("+100% day in the last 10 sessions (second-run setup)", (r, _) => V(r, "x.wl.up100Days10") >= 1),
            new("D-1 top-10 mover (H15)", (r, _) => Between(r, "x.priorDayTopMoverRank", 1, 10)),
            new("D-1 dollar volume >= 5x its 20-day mean (H6-like)", (r, _) => V(r, "x.wl.dvolD1Ratio") >= 5),
            new("dormant: 20-day $ volume <= 0.5x its 21-120-session median", (r, _) => V(r, "x.wl.dormancy") <= 0.5f),
            new("no +20% day in the last 60 sessions", (r, _) => V(r, "x.wl.sessionsSinceUp20") is var v && (float.IsNaN(v) || v > 60)),
            new("shares outstanding < 10M", (r, _) => V(r, FloatFeatures.LogShares) is var v && !float.IsNaN(v) && v < Math.Log(10e6)),
            new("shares outstanding < 5M", (r, _) => V(r, FloatFeatures.LogShares) is var v && !float.IsNaN(v) && v < Math.Log(5e6)),
            new("S-1/S-3/424B on record in the last 90 days", (r, _) => V(r, "x.wl.dilFilings90") >= 1),
            new("ATM program active (x.dilAtmActive)", (r, _) => V(r, DilutionFeatures.AtmActive) > 0.5f),
            new("6-K in the last 30 days", (r, _) => V(r, DilutionFeatures.SixKCount30) >= 1),
            new("EFFECT within 90 days", (r, _) => Between(r, DilutionFeatures.DaysSinceEffect, 0, 90)),
            new("Reg SHO threshold list on D-1", (r, _) => V(r, RegShoFeatures.ListedD1) > 0.5f),
            new("halt in the last 5 sessions", (r, _) => V(r, "x.wl.halts5") >= 1),
            new("prior close < $1", (r, _) => V(r, "x.wl.logPrice") is var v && !float.IsNaN(v) && v < 0),
            new("prior close $1-$5", (r, _) => V(r, "x.wl.logPrice") is var v && !float.IsNaN(v) && v >= 0 && v < Math.Log(5)),
            new("prior close $5-$20", (r, _) => V(r, "x.wl.logPrice") is var v && !float.IsNaN(v) && v >= Math.Log(5)),
            new("ADR / ADS", (r, _) => V(r, "x.wl.isAdr") > 0.5f),
            new("reverse split effective on D (calendared)", (r, _) => V(r, "x.wl.rsEffToday") > 0.5f),
            new("reverse split effective on D+1 (calendared)", (r, _) => V(r, "x.wl.rsEffTomorrow") > 0.5f),
            new("cumulative reverse-split ratio >= 50:1 over 2 years", (r, _) => V(r, "x.wl.rsCumLogRatio2y") >= Math.Log(50)),
            new("cumulative reverse-split ratio >= 10:1 over 2 years", (r, _) => V(r, "x.wl.rsCumLogRatio2y") >= Math.Log(10)),
            new("promotion: foreign proxy (ADR, Ltd/Limited/plc/N.V./S.A. name, 6-K or F-1/F-3 filer)", (r, _) => V(r, "x.wl.foreignProxy") > 0.5f),
            new("promotion: holding-company name", (r, _) => V(r, "x.wl.holdingCo") > 0.5f),
            new("promotion: listed within 730 days (first bar after 2021-09-15)", (r, _) => V(r, "x.wl.newListing") > 0.5f),
            new("promotion: no SEC filing in the 24 h to D-1 16:00", (r, _) => V(r, "x.wl.noFiling24h") > 0.5f),
            new("promotion profile: foreign proxy + listed < 730 days + shares < 10M + no filing in 24 h",
                (r, _) => V(r, "x.wl.foreignProxy") > 0.5f && V(r, "x.wl.newListing") > 0.5f && V(r, "x.wl.noFiling24h") > 0.5f && V(r, FloatFeatures.LogShares) is var v && !float.IsNaN(v) && v < Math.Log(10e6)),
            new("promotion profile without the float (foreign + new listing + no filing 24 h)",
                (r, _) => V(r, "x.wl.foreignProxy") > 0.5f && V(r, "x.wl.newListing") > 0.5f && V(r, "x.wl.noFiling24h") > 0.5f),
            new("dilution supply: >= 1 of 424B / S-1 / 8-K 1.01 / F-1-F-3 in 90 days",
                (r, _) => V(r, "x.wl.dilSupply90") >= 1 || Between(r, DilutionFeatures.DaysSinceForeignReg, 0, 90)),
            new("dilution supply: >= 3 of 424B / S-1 / 8-K 1.01 in 90 days", (r, _) => V(r, "x.wl.dilSupply90") >= 3),
            new("any catalyst item by D-1 16:00 (5 days)", (_, w) => w.AnyD1),
            new("no catalyst item by D-1 16:00 (5 days)", (_, w) => !w.AnyD1),
        };
        for (int k = 0; k < CatalystTypes.Keys.Length; k++)
        {
            int bit = 1 << k;
            list.Add(new($"catalyst by D-1 16:00: {CatalystTypes.Names[k]}", (_, w) => (w.CatD1 & bit) != 0));
        }
        return list;
    }

    // ── report ────────────────────────────────────────────────────────────

    private (List<string>, int, string?) WatchlistReport(ExploreTable t, List<ExploreFold> folds, List<WlRow> rows, List<WlRow> scorable, List<DateOnly> evalDays, List<Prereq> prereqs,
        IReadOnlyList<(string Feature, double Share)> importance, List<string> pointRules, List<RunnerExitPlan> exits, List<(string Reason, int N, int R50, int R100, int R300)> excluded,
        (List<string> Lines, object Json) splitSection, double multiplier, string source, double notional, int newsItems, int filingItems, bool haveHalts)
    {
        var p = s.Params;
        var md = new StringBuilder(Header("runner watchlist (IP-T484): runner anatomy, the D-1 16:00 watchlist, all-day ignition and the ride", folds));
        var trials = new List<ExploreTrial>();
        var json = new Dictionary<string, object?>();
        string Pct(double? v) => DeepPct(v);
        string Sh(double? v) => DeepShare(v);
        string Num(double? v, string fmt = "0.0") => v is { } x && double.IsFinite(x) ? x.ToString(fmt, CultureInfo.InvariantCulture) : "n/a";
        string Clk(double? minute) => minute is { } m && double.IsFinite(m) ? Clock(240 + m) : "n/a";
        int nEvalDays = evalDays.Count;
        var withBars = rows.Where(w => w.HasBars).ToList();
        double W(IEnumerable<WlRow> l) => l.Sum(w => w.Weight);

        md.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"Runner on D: the highest high of D's minute bars 04:00-16:00 ET over the adjusted prior close, minus 1, at >= +50% / +100% / +300%. Population: the D-1 16:00 explore panel (candidates + amendment 01's 10% sample, sample weight 10), {rows.Count:N0} symbol-days, {withBars.Count:N0} with minute bars on D. Pooled = every train day; fold columns = the four eval windows ({nEvalDays:N0} eval days). Every candidate day is a census row, so every runner whose daily high cleared +10% is in the panel."));
        md.AppendLine();
        md.AppendLine("**What the universe hides.** Protocol U(D) drops a symbol-day whose split ex-date falls in [D-25, D+1] or that saw an unexplained close-to-close move above +200% (or below -66.7%) in [D-25, D]. So a runner within 25 days of a recorded reverse split, a runner whose own close was above +200% without a corporate action, and any second run within 25 days of such a close are not in the panel. Daily-bar census of the excluded train days (daily high over the adjusted prior close; raw, unadjusted highs on split days can mislead):");
        md.AppendLine();
        md.AppendLine("| excluded because | symbol-days | high >= +50% | >= +100% | >= +300% |");
        md.AppendLine("|---|---|---|---|---|");
        foreach (var e in excluded.OrderByDescending(e => e.N)) md.AppendLine($"| {e.Reason} | {e.N:N0} | {e.R50:N0} | {e.R100:N0} | {e.R300:N0} |");
        int inPanel100 = withBars.Count(w => w.Runner(1));
        md.AppendLine();
        md.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"In the panel: {withBars.Count(w => w.Runner(0)):N0} / {inPanel100:N0} / {withBars.Count(w => w.Runner(2)):N0} runner days at +50/+100/+300% (minute-bar high 04:00-16:00). So the reverse-split 0-10 and 11-25 day buckets below are empty by construction, and the >= +300% tier is censored most. Everything below is about the runners the universe keeps."));
        md.AppendLine();
        json["excluded"] = excluded.Select(e => new { e.Reason, e.N, e.R50, e.R100, e.R300 });
        foreach (var l in splitSection.Lines) md.AppendLine(l);
        json["excludedSplitDays"] = splitSection.Json;

        // ── A. anatomy ──
        md.AppendLine("## A. Runner anatomy (all train runners >= +100%, descriptive)");
        md.AppendLine();
        var runners = withBars.Where(w => w.Runner(1)).ToList();
        int IgnRef = 3; // k = 10, no cap
        var basePop = scorable;
        double baseRate100 = W(basePop.Where(w => w.Runner(1))) / Math.Max(1e-9, W(basePop));
        md.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"{runners.Count:N0} runner days over {runners.Select(w => t.Days[w.R]).Distinct().Count():N0} days ({(double)runners.Count / Math.Max(1, t.Days.Distinct().Count()):0.00} per train day); population base rate {baseRate100:P3} of weighted symbol-days."));
        md.AppendLine();
        // where the run starts and peaks
        string Buckets(IEnumerable<double> minutes)
        {
            var l = minutes.Where(double.IsFinite).ToList();
            if (l.Count == 0) return "n/a";
            double n = l.Count;
            return string.Create(CultureInfo.InvariantCulture, $"premarket {l.Count(m => m < 330) / n:P0} / 09:30-10:00 {l.Count(m => m is >= 330 and < 360) / n:P0} / 10:00-12:00 {l.Count(m => m is >= 360 and < 480) / n:P0} / 12:00-16:00 {l.Count(m => m >= 480) / n:P0}");
        }
        double? Med(IEnumerable<double> v) => MedianOf(v.Where(double.IsFinite).ToList());
        md.AppendLine("| measure | value |");
        md.AppendLine("|---|---|");
        md.AppendLine($"| first +100% touch (time of day) | {Buckets(runners.Select(w => (double)w.Facts!.FirstTouch[1]))} |");
        md.AppendLine($"| high of day (time of day) | {Buckets(runners.Select(w => (double)w.Facts!.Hod))}; median {Clk(Med(runners.Select(w => (double)w.Facts!.Hod)))} |");
        md.AppendLine($"| 04:00 gap (first bar open vs prior close) | median {Pct(Med(runners.Select(w => w.Facts!.FirstGap)))}; already >= +50% at the first bar {Sh((double)runners.Count(w => w.Facts!.FirstGap >= 0.5) / Math.Max(1, runners.Count))}, >= +100% {Sh((double)runners.Count(w => w.Facts!.FirstGap >= 1.0) / Math.Max(1, runners.Count))} |");
        md.AppendLine($"| premarket high vs prior close | median {Pct(Med(runners.Select(w => w.Facts!.PmGain)))}; premarket stayed under +20% (an OMH day-1 style regular-hours run) {Sh((double)runners.Count(w => !(w.Facts!.PmGain >= 0.2)) / Math.Max(1, runners.Count))} |");
        md.AppendLine($"| run (high vs prior close) | median {Pct(Med(runners.Select(w => w.Gain)))}; >= +300% {Sh((double)runners.Count(w => w.Runner(2)) / Math.Max(1, runners.Count))} |");
        md.AppendLine($"| fade (16:00 close vs high) | median {Pct(Med(runners.Select(w => w.Facts!.Fade)))}; closed > 50% under the high {Sh((double)runners.Count(w => w.Facts!.Fade < -0.5) / Math.Max(1, runners.Count))} |");
        var ignR = runners.Where(w => w.Ign[IgnRef] is { } g && g.BeforeHod).ToList();
        md.AppendLine($"| ignition (k=10, no cap) before the high | {Sh((double)ignR.Count / Math.Max(1, runners.Count))} of runners; ignition time {Buckets(ignR.Select(w => (double)w.Ign[IgnRef]!.Minute))} |");
        md.AppendLine($"| minutes from ignition to the high | median {Num(Med(ignR.Select(w => (double)(w.Facts!.Hod - w.Ign[IgnRef]!.Minute))), "0")} [IQR {Num(Q(ignR.Select(w => (double)(w.Facts!.Hod - w.Ign[IgnRef]!.Minute)), 0.25), "0")}, {Num(Q(ignR.Select(w => (double)(w.Facts!.Hod - w.Ign[IgnRef]!.Minute)), 0.75), "0")}] |");
        md.AppendLine($"| run left after ignition (high after it / ignition close) | median {Pct(Med(ignR.Select(w => (double)w.Ign[IgnRef]!.Remaining)))} |");
        md.AppendLine($"| top footprint: volume climax within 5 min of the high | {Sh((double)runners.Count(w => Math.Abs(w.Facts!.Climax - w.Facts.Hod) <= 5) / Math.Max(1, runners.Count))} |");
        md.AppendLine($"| top footprint: lower high confirmed within 30 / 60 min of the high | {Sh((double)runners.Count(w => w.Facts!.LowerHighAfter is >= 0 and <= 30) / Math.Max(1, runners.Count))} / {Sh((double)runners.Count(w => w.Facts!.LowerHighAfter is >= 0 and <= 60) / Math.Max(1, runners.Count))}; median {Num(Med(runners.Where(w => w.Facts!.LowerHighAfter >= 0).Select(w => (double)w.Facts!.LowerHighAfter)), "0")} min |");
        md.AppendLine($"| top footprint: VWAP lost within 30 / 60 min of the high | {Sh((double)runners.Count(w => w.Facts!.VwapLossAfter is >= 0 and <= 30) / Math.Max(1, runners.Count))} / {Sh((double)runners.Count(w => w.Facts!.VwapLossAfter is >= 0 and <= 60) / Math.Max(1, runners.Count))}; median {Num(Med(runners.Where(w => w.Facts!.VwapLossAfter >= 0).Select(w => (double)w.Facts!.VwapLossAfter)), "0")} min |");
        md.AppendLine($"| top footprint: LULD/halt within 5 min of the high | {(haveHalts ? Sh((double)runners.Count(w => w.HaltAtHigh) / Math.Max(1, runners.Count)) : "n/a (no halt history)")} |");
        var known = runners.Where(w => w.Rerun100 >= 0).ToList();
        var baseKnown = scorable.Where(w => w.Rerun100 >= 0).ToList();
        double rr100 = (double)known.Count(w => w.Rerun100 == 1) / Math.Max(1, known.Count), rr50 = (double)known.Count(w => w.Rerun50 == 1) / Math.Max(1, known.Count);
        double base100 = W(baseKnown.Where(w => w.Rerun100 == 1)) / Math.Max(1e-9, W(baseKnown)), base50 = W(baseKnown.Where(w => w.Rerun50 == 1)) / Math.Max(1e-9, W(baseKnown));
        md.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"| day 2: another daily high >= +100% / >= +50% within the next 5 sessions (daily bars, split-adjusted, train only) | {rr100:P1} / {rr50:P1} of {known.Count:N0} runners, against {base100:P2} / {base50:P2} for any population symbol-day (lift {rr100 / Math.Max(1e-9, base100):0.0}x / {rr50 / Math.Max(1e-9, base50):0.0}x) |"));
        md.AppendLine();
        json["anatomy"] = new { runners = runners.Count, rerun100 = rr100, rerun50 = rr50, base100, base50, ignitionBeforeHigh = (double)ignR.Count / Math.Max(1, runners.Count) };

        md.AppendLine("Share of runners with each prerequisite (known by D-1 16:00) against all population symbol-days (weighted):");
        md.AppendLine();
        md.AppendLine("| prerequisite | runners >= +100% with it | population with it | lift >= +50% | lift >= +100% | lift >= +300% | runners >= +100% (count) | median run / fade (all with it) | median run / fade (runners with it) |");
        md.AppendLine("|---|---|---|---|---|---|---|---|---|");
        var liftJson = new List<object>();
        double popW = W(scorable);
        var baseRates = RunnerLevels.Select((_, l) => W(scorable.Where(w => w.Runner(l))) / Math.Max(1e-9, popW)).ToArray();
        foreach (var pq in prereqs)
        {
            var with = scorable.Where(w => pq.Has(w.R, w)).ToList();
            double ww = W(with);
            var lifts = RunnerLevels.Select((_, l) => ww > 0 && baseRates[l] > 0 ? W(with.Where(w => w.Runner(l))) / ww / baseRates[l] : double.NaN).ToArray();
            int r100 = with.Count(w => w.Runner(1));
            md.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"| {pq.Name} | {(double)r100 / Math.Max(1, runners.Count):P1} | {ww / Math.Max(1e-9, popW):P2} | {Num(lifts[0], "0.0")}x | {Num(lifts[1], "0.0")}x | {Num(lifts[2], "0.0")}x | {r100:N0} | {Pct(Med(with.Select(w => w.Gain)))} / {Pct(Med(with.Select(w => w.Facts!.Fade)))} | {Pct(Med(with.Where(w => w.Runner(1)).Select(w => w.Gain)))} / {Pct(Med(with.Where(w => w.Runner(1)).Select(w => w.Facts!.Fade)))} |"));
            liftJson.Add(new { prerequisite = pq.Name, runnerShare = (double)r100 / Math.Max(1, runners.Count), populationShare = ww / Math.Max(1e-9, popW), lift50 = lifts[0], lift100 = lifts[1], lift300 = lifts[2], runners100 = r100,
                medianRun = Med(with.Select(w => w.Gain)), medianFade = Med(with.Select(w => w.Facts!.Fade)) });
        }
        md.AppendLine();
        md.AppendLine(string.Create(CultureInfo.InvariantCulture, $"Base rates (weighted share of population symbol-days): {string.Join(" / ", baseRates.Select(b => b.ToString("P3", CultureInfo.InvariantCulture)))} for >= +50/+100/+300%. A lift of 6.5x on a 0.07% base is still about 1 runner in 200 names."));
        md.AppendLine();
        json["prerequisiteLifts"] = liftJson;

        // ── catalysts ──
        md.AppendLine("## B. Catalysts");
        md.AppendLine();
        md.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"A transparent keyword + 8-K-item classifier (`CatalystTypes`, IdiotProof.Evidence) over {newsItems:N0} stored news (symbol, item) pairs (headline plus the rule tables' pre-class) and {filingItems:N0} EDGAR filings (S-1/S-3/424B3/4/5 = offering, SC 13D = outside investment, 8-K items 2.02 earnings, 3.02 PIPE, 1.01 contract, 2.01 acquisition, 3.03 holder-rights change, often a reverse split). Several types can fire on one item. **Watchlist catalyst** = items known in the {CatalystLookbackDays} days to D-1 16:00 (a filing without a fetched acceptance time counts from 17:30 ET on its filing day). **Same-day catalyst** = items from D-1 16:00 to D 16:00 (a same-day headline can be the move's own coverage, \"shares soar on ...\", so it is descriptive, not a signal). Lift = P(runner | type) / base rate, weighted; run = the day's high vs prior close; fade = 16:00 close vs the high."));
        md.AppendLine();
        var catJson = new List<object>();
        foreach (var (title, any, bitsOf) in new (string, Func<WlRow, bool>, Func<WlRow, int>)[] { ("known by D-1 16:00 (watchlist)", w => w.AnyD1, w => w.CatD1), ("same day, D-1 16:00 to D 16:00 (anatomy)", w => w.AnySame, w => w.CatSame) })
        {
            md.AppendLine($"**{title}**");
            md.AppendLine();
            md.AppendLine("| catalyst type | population with it | runners >= +100% with it | lift >= +50% | lift >= +100% | lift >= +300% | median run (all with it) | median fade (all) | median run (runners) | median fade (runners) |");
            md.AppendLine("|---|---|---|---|---|---|---|---|---|---|");
            var groups = new List<(string Name, Func<WlRow, bool> Has)>();
            for (int k = 0; k < CatalystTypes.Keys.Length; k++) { int bit = 1 << k; groups.Add((CatalystTypes.Names[k], w => (bitsOf(w) & bit) != 0)); }
            groups.Add(("item(s) but no type matched", w => any(w) && bitsOf(w) == 0));
            groups.Add((CatalystTypes.NoCatalyst, w => !any(w)));
            foreach (var (name, has) in groups)
            {
                var with = scorable.Where(has).ToList();
                double ww = W(with);
                var lifts = RunnerLevels.Select((_, l) => ww > 0 && baseRates[l] > 0 ? W(with.Where(w => w.Runner(l))) / ww / baseRates[l] : double.NaN).ToArray();
                var run = with.Where(w => w.Runner(1)).ToList();
                md.AppendLine(string.Create(CultureInfo.InvariantCulture,
                    $"| {name} | {ww / Math.Max(1e-9, popW):P2} | {run.Count:N0} ({(double)run.Count / Math.Max(1, runners.Count):P1}) | {Num(lifts[0], "0.0")}x | {Num(lifts[1], "0.0")}x | {Num(lifts[2], "0.0")}x | {Pct(Med(with.Select(w => w.Gain)))} | {Pct(Med(with.Select(w => w.Facts!.Fade)))} | {Pct(Med(run.Select(w => w.Gain)))} | {Pct(Med(run.Select(w => w.Facts!.Fade)))} |"));
                catJson.Add(new { window = title, type = name, populationShare = ww / Math.Max(1e-9, popW), runners100 = run.Count, lift50 = lifts[0], lift100 = lifts[1], lift300 = lifts[2], medianRun = Med(with.Select(w => w.Gain)), medianFade = Med(with.Select(w => w.Facts!.Fade)), medianRunRunners = Med(run.Select(w => w.Gain)), medianFadeRunners = Med(run.Select(w => w.Facts!.Fade)) });
            }
            md.AppendLine();
        }
        json["catalysts"] = catJson;

        // ── C. stage 1 ──
        md.AppendLine("## C. Stage 1: the evening watchlist (decision D-1 16:00)");
        md.AppendLine();
        md.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"Features: the D-1 16:00 panel (prior days, corporate actions, catalyst score and pre-class, 8-K items, float proxy, dilution filings, Reg SHO and short volume, short-sale constraints) plus `x.wl.*` from the daily bars, splits, halts and catalysts: +20/+50/+100% days in the last 5/10/30 sessions and sessions since the last one, dormancy (20-day dollar volume over its 21-120-session median), D-1 dollar volume over its 20-day mean, D-1 return, days since the last reverse split or ratio change and its log ratio (recorded in GapperStudySplits, or inferred: no split on record and the whole day trades at least 3x above or under the prior close on under 3x the 20-day dollar volume, ratio = open / prior close; this is how an ADS ratio change such as NWGL's is caught when it is missing from the split table), log price, ADR flag, halts in the last 5 sessions, S-1/S-3/424B filings in 90 days, and the watchlist catalyst types. Learners, fit per fold on the fold's fit window, scored on its eval window: {string.Join("; ", LearnerNames)}. Logistic uses the `x.wl.*` set plus {17} panel prior-day, corporate-action, float, dilution and Reg SHO features (all-feature logistic gradient descent is too slow at this size). The watchlist for N is each eval morning's top-scored names until N names are listed (a sampled row stands for 10)."));
        md.AppendLine();
        md.AppendLine("Recall@N = share of the eval window's runners on that morning's list; precision@N = runner share of the list (weighted); per fold and the mean of folds.");
        md.AppendLine();
        var stage1Json = new List<object>();
        for (int ti = 0; ti < RunnerLevels.Length; ti++)
        {
            md.AppendLine($"**Target {RunnerLevelNames[ti]}**");
            md.AppendLine();
            var foldBase = folds.Select((_, k) => { var e = scorable.Where(w => w.Fold == k).ToList(); return (Rate: W(e.Where(w => w.Runner(ti))) / Math.Max(1e-9, W(e)), PerDay: (double)e.Count(w => w.Runner(ti)) / Math.Max(1, e.Select(w => w.DayIdx).Distinct().Count())); }).ToList();
            md.AppendLine(string.Create(CultureInfo.InvariantCulture, $"Base rate per fold: {string.Join(" / ", foldBase.Select(b => $"{b.Rate:P3} ({b.PerDay:0.00} runners/day)"))}."));
            md.AppendLine();
            md.AppendLine("| learner | N | recall per fold | mean recall | precision per fold | mean precision | precision / base rate |");
            md.AppendLine("|---|---|---|---|---|---|---|");
            for (int a = 0; a < 4; a++)
                foreach (var n in WatchlistSizes)
                {
                    var rec = new double?[folds.Count];
                    var prec = new double?[folds.Count];
                    for (int k = 0; k < folds.Count; k++)
                    {
                        var e = scorable.Where(w => w.Fold == k && !double.IsNaN(w.Rank[a, ti])).ToList();
                        if (e.Count == 0) continue;
                        double run = W(e.Where(w => w.Runner(ti))), hit = W(e.Where(w => w.Runner(ti) && w.Rank[a, ti] < n)), list = W(e.Where(w => w.Rank[a, ti] < n));
                        rec[k] = run > 0 ? hit / run : null;
                        prec[k] = list > 0 ? hit / list : null;
                    }
                    double? mr = MeanOf(rec), mp = MeanOf(prec), mb = MeanOf(foldBase.Select(b => (double?)b.Rate));
                    md.AppendLine($"| {LearnerNames[a]} | {n} | {string.Join(" / ", rec.Select(Sh))} | {Sh(mr)} | {string.Join(" / ", prec.Select(Sh))} | {Sh(mp)} | {Num(mp / mb, "0.0")}x |");
                    stage1Json.Add(new { target = RunnerLevelNames[ti], learner = LearnerNames[a], n, recall = rec, meanRecall = mr, precision = prec, meanPrecision = mp });
                    trials.Add(new ExploreTrial
                    {
                        Track = "watchlist", Config = $"t={WatchlistDecision} target=runner {RunnerLevelNames[ti]} 04:00-16:00 learner={LearnerNames[a]} N={n}", Metric = "recall@N (share of eval runners on the list; mean of folds)",
                        FoldValues = rec, Pooled = mr, Rows = e2(scorable, ti),
                        Note = string.Create(CultureInfo.InvariantCulture, $"precision={string.Join("/", prec.Select(Sh))} meanPrecision={Sh(mp)}"),
                    });
                }
            md.AppendLine();
        }
        static int e2(List<WlRow> l, int ti) => l.Count(w => w.Fold >= 0);
        json["stage1"] = stage1Json;
        md.AppendLine("Point-score prerequisites chosen per fold on the fold-fit window:");
        md.AppendLine();
        foreach (var r in pointRules) md.AppendLine($"- {r}");
        md.AppendLine();
        md.AppendLine($"Boosted importance (target >= +100%, fold {folds.Count} fit window, top 20 by gain, descriptive):");
        md.AppendLine();
        foreach (var (feature, share) in importance.Take(20)) md.AppendLine($"- {feature}: {share:P1}");
        md.AppendLine();

        // ── D. stage 2 ──
        var evalRows = withBars.Where(w => w.Fold >= 0).ToList();
        var groups2 = new List<(string Name, Func<WlRow, bool> In)>();
        foreach (var n in WatchlistTradeSizes) { int nn = n; groups2.Add(($"watchlist top {n} (boosted >= +100%)", w => w.Rank[PrimaryLearner, PrimaryTarget] < nn)); }
        groups2.Add(("all names (no watchlist)", _ => true));
        md.AppendLine("## D. Stage 2: ignition, 04:00-16:00");
        md.AppendLine();
        md.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"Ignition at minute t (bars ending by t's end): volume >= k x max(the mean volume of the 30 clock minutes before, missing minutes as 0; floor max({IgnitionMinShares:0} shares, ADV20 / {IgnitionAdvDivisor:0})), close >= 2% over the session high of the earlier bars that day (the first bar of the day cannot ignite), at least ${IgnitionDollarFloor:N0} traded in the minute, and the last close before it under the gap cap (none / < +50% / < +100% over the prior close). The first such minute of the day is the ignition; episodes = ignitions at least 30 minutes apart. Eval windows only. Caught = a >= +100% runner ignited before its high; false ignition = an ignited name that does not reach +100% that day; run left = the highest high after the ignition minute over the ignition close."));
        md.AppendLine();
        md.AppendLine("| group | ignition | ignitions / day | episodes / ignited name | runners caught (of all eval runners) | false ignitions per caught runner | ignition precision (>= +100%) | runner ignition time | median minutes ignition to high | median run left (runners) | median run left (all ignitions) |");
        md.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|");
        double evalRunnersW = W(evalRows.Where(w => w.Runner(1)));
        var stage2Json = new List<object>();
        foreach (var (gname, inG) in groups2)
            for (int ic = 0; ic < IgnitionCount; ic++)
            {
                var ign = evalRows.Where(w => inG(w) && w.Ign[ic] is not null).ToList();
                var caught = ign.Where(w => w.Runner(1) && w.Ign[ic]!.BeforeHod).ToList();
                var falseIgn = ign.Where(w => !w.Runner(1)).ToList();
                double perDay = W(ign) / Math.Max(1, nEvalDays);
                double catchRate = W(caught) / Math.Max(1e-9, evalRunnersW);
                double fpr = W(falseIgn) / Math.Max(1e-9, W(caught));
                double precision = W(ign.Where(w => w.Runner(1))) / Math.Max(1e-9, W(ign));
                int kIdx = ic / 3;
                double eps = ign.Count == 0 ? double.NaN : ign.Average(w => (double)w.Episodes[kIdx]);
                var perFold = folds.Select((_, k) =>
                {
                    double rw = W(evalRows.Where(w => w.Fold == k && w.Runner(1)));
                    return rw > 0 ? W(caught.Where(w => w.Fold == k)) / rw : (double?)null;
                }).ToList();
                md.AppendLine(string.Create(CultureInfo.InvariantCulture,
                    $"| {gname} | {IgnName(ic)} | {perDay:0.0} | {Num(eps, "0.0")} | {catchRate:P1} | {Num(fpr, "0.0")} | {precision:P2} | {Buckets(caught.Select(w => (double)w.Ign[ic]!.Minute))} | {Num(Med(caught.Select(w => (double)(w.Facts!.Hod - w.Ign[ic]!.Minute))), "0")} | {Pct(Med(caught.Select(w => (double)w.Ign[ic]!.Remaining)))} | {Pct(Med(ign.Select(w => (double)w.Ign[ic]!.Remaining)))} |"));
                stage2Json.Add(new { group = gname, ignition = IgnName(ic), perDay, catchRate, falsePerCaught = fpr, precision, perFold, medianRemainingRunners = Med(caught.Select(w => (double)w.Ign[ic]!.Remaining)) });
                trials.Add(new ExploreTrial
                {
                    Track = "watchlist-ignition", Config = $"{gname} ignition {IgnName(ic)} break 2% $5k 04:00-16:00", Metric = "runner catch rate (eval >= +100% runners ignited before their high; per fold)",
                    FoldValues = perFold, Pooled = catchRate, Rows = ign.Count,
                    Note = string.Create(CultureInfo.InvariantCulture, $"ignitionsPerDay={perDay:0.0} falsePerCaught={Num(fpr, "0.0")} precision={precision:P2}"),
                });
            }
        md.AppendLine();
        json["stage2"] = stage2Json;

        // ── E. stage 3 ──
        string[] fills = ["market (next bar open)", "limit -1% (5 min)"];
        var configs = new List<(int G, int Ic, int Fi, int X)>();
        for (int gi = 0; gi < groups2.Count; gi++)
            for (int ic = 0; ic < IgnitionCount; ic++)
                for (int fi = 0; fi < 2; fi++)
                    for (int x = 0; x < exits.Count; x++) configs.Add((gi, ic, fi, x));
        var months = evalDays.Select(d => d.Year * 12 + d.Month).Distinct().OrderBy(m => m).ToList();
        var dayMonth = evalDays.Select(d => months.IndexOf(d.Year * 12 + d.Month)).ToArray();
        var dayFold = evalDays.Select(d => folds.FindIndex(f => f.InEval(d))).ToArray();
        var lists = new Dictionary<(int, int, int), List<(WlRow Row, WlSim Sim)>>();
        for (int gi = 0; gi < groups2.Count; gi++)
            for (int ic = 0; ic < IgnitionCount; ic++)
                for (int fi = 0; fi < 2; fi++)
                {
                    var inG = groups2[gi].In;
                    lists[(gi, ic, fi)] = evalRows.Where(w => inG(w) && w.Ign[ic]?.Sims[fi] is not null).Select(w => (w, w.Ign[ic]!.Sims[fi]!)).ToList();
                }
        var stats = new WlStats[configs.Count];
        Parallel.For(0, configs.Count, ci =>
        {
            var (gi, ic, fi, x) = configs[ci];
            stats[ci] = TradeStats(lists[(gi, ic, fi)].Where(z => !float.IsNaN(z.Sim.Gross[x])).ToList(), x, nEvalDays, dayFold, dayMonth, months.Count, folds.Count);
        });
        string Cfg(int ci) { var (gi, ic, fi, x) = configs[ci]; return $"{groups2[gi].Name} | {IgnName(ic)} | {fills[fi]} | {exits[x].Name}"; }
        foreach (var (c, ci) in configs.Select((c, i) => (c, i)))
        {
            var st = stats[ci];
            trials.Add(new ExploreTrial
            {
                Track = "watchlist-ride", Config = "long " + Cfg(ci), Metric = "meanNetReturn (long, frozen cost, eval windows)", FoldValues = st.Folds[0], Pooled = st.Net[0]?.Mean, Rows = st.Trades,
                Note = string.Create(CultureInfo.InvariantCulture, $"lo={Pct(st.Net[0]?.Lo)} hi={Pct(st.Net[0]?.Hi)} days={st.Days} gross={Pct(st.Gross)} net0.5x={Pct(st.Net[1]?.Mean)} net0.25x={Pct(st.Net[2]?.Mean)} worst1={Pct(st.Worst1)} top1={Sh(st.Top1)}"),
            });
        }
        int nTrials = Ledger.Count + trials.Count;
        var sharpes = stats.Where(st => st.Trades >= 2).Select(st => st.Moments.Sharpe).Where(double.IsFinite).ToList();
        double varSr = sharpes.Count > 1 ? Math.Pow(EvidenceStats.StdDev(sharpes), 2) : 0;
        double Dsr(WlStats st) => st.Trades >= 2 ? MultipleTesting.DeflatedSharpe(st.Moments, nTrials, varSr) : double.NaN;
        double dsrMin = p.Statistics.DsrMin;

        md.AppendLine("## E. Stage 3: the ride");
        md.AppendLine();
        md.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"Entry after the ignition minute: market = the open of the next real bar (within 30 min); limit = 1% under the ignition close, filled at the better of the limit and the open on the first bar within 5 min that trades through it (no half sale inside that bar). No entry at or after 15:45. Exits, first hit wins (`RunnerIgnition.Simulate`): initial stop -5/-8% (filled at the worse of the stop and the bar open), replaced once a high reaches +10% by a trailing stop 15/25/35% under the highest high of earlier bars; optional half at +20%; optional cooling once +10% (velocity death: the 3-minute close-to-close velocity under 0 %/min, the `VelocityExitRule` window; VWAP loss: a one-minute close under the 04:00-anchored session VWAP), sold at the next bar's open; everything out at the 15:55 bar's open. Costs: the frozen round trip at ${notional:N0} (Corwin-Schultz over D-1..D-5 x the premarket multiplier {multiplier:0.###} from {source}, square-root impact on the volume from 04:00 to entry, 20-session sigma from split-adjusted daily closes, floored at {p.CostModel.RoundTripFloor:P1}) and with the spread component at 0.5x / 0.25x. The frozen multiplier was calibrated on premarket fills; after 09:30 spreads are tighter, so the 0.5x / 0.25x columns are the more realistic ones for regular-hours entries (the RTH/premarket split of the best configurations is below). Break-even cost = the gross mean. Capacity: `ParticipationCap` (5% of the 5 minutes before entry and of the profile-forecast shares around the actual exit), skipped under $200. P&L concentration: the top 1% / 5% of trades' share of summed weighted frozen-cost net (above 100% when the rest loses)."));
        md.AppendLine();
        md.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"{configs.Count:N0} configurations (4 groups x 9 ignitions x 2 fills x {exits.Count} exits), each a ledger trial. DSR: per-trade deflated Sharpe of the unweighted frozen-cost net series at N = {nTrials:N0} (the ledger total after this run) with the variance of this run's trial Sharpes ({varSr:0.0000}); frozen minimum {dsrMin:0.00}. Finalist bar (explore-plan.md): >= +1.0% net with lower bound > 0, >= 150 trades over >= 40 days, > 0 in at least 3 of 4 folds, leave-one-month-out lower bound > 0, plus DSR >= {dsrMin:0.00}."));
        md.AppendLine();
        const string head = "| group | ignition | fill | exit | trades | days | trades/day | gross = break-even | mean cost | net frozen [95%] | net 0.5x [95%] | net 0.25x [95%] | per fold (frozen) | per fold (0.25x) | worst 1% | top 1% / 5% P&L | >= +50% / >= +100% trades | cap $1k (full) | cap $5k (full) | LOMO lo | DSR | finalist 1/0.5/0.25 |";
        const string sep = "|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|";
        string Row(int ci)
        {
            var (gi, ic, fi, x) = configs[ci];
            var st = stats[ci];
            string N(int l) => st.Net[l] is { } m ? DeepBounds(m.Mean, m.Lo, m.Hi) : "n/a";
            bool dsrOk = Dsr(st) >= dsrMin;
            return string.Create(CultureInfo.InvariantCulture,
                $"| {groups2[gi].Name} | {IgnName(ic)} | {(fi == 0 ? "market" : "limit -1%")} | {exits[x].Name} | {st.Trades:N0} | {st.Days:N0} | {st.WeightedTrades / Math.Max(1, nEvalDays):0.00} | {Pct(st.Gross)} | {Pct(st.MeanCost)} | {N(0)} | {N(1)} | {N(2)} | {string.Join(" / ", st.Folds[0].Select(Pct))} | {string.Join(" / ", st.Folds[2].Select(Pct))} | {Pct(st.Worst1)} | {Sh(st.Top1)} / {Sh(st.Top5)} | {Sh(st.Ge50)} / {Sh(st.Ge100)} | {Pct(st.Cap[0])} ({Sh(st.CapFull[0])}) | {Pct(st.Cap[1])} ({Sh(st.CapFull[1])}) | {Pct(st.LomoLo[0])} | {Dsr(st):0.000} | {string.Join("/", st.Finalist.Select(f => f && dsrOk ? "Y" : f ? "y" : "-"))} |");
        }
        foreach (var (title, l) in new[] { ("frozen cost", 0), ("0.5x spread", 1), ("0.25x spread", 2) })
        {
            md.AppendLine($"**Top 10 by net at {title} (>= 30 trades)**");
            md.AppendLine();
            md.AppendLine(head);
            md.AppendLine(sep);
            foreach (var ci in Enumerable.Range(0, configs.Count).Where(i => stats[i].Trades >= 30 && stats[i].Net[l] is not null).OrderByDescending(i => stats[i].Net[l]!.Value.Mean).Take(10)) md.AppendLine(Row(ci));
            md.AppendLine();
        }
        md.AppendLine("**Best configuration per group (net at 0.25x spread, >= 30 trades), and the same ignition, fill and exit on every other group**");
        md.AppendLine();
        md.AppendLine(head);
        md.AppendLine(sep);
        var bestPerGroup = new List<int>();
        for (int gi = 0; gi < groups2.Count; gi++)
        {
            int g0 = gi;
            var best = Enumerable.Range(0, configs.Count).Where(i => configs[i].G == g0 && stats[i].Trades >= 30 && stats[i].Net[2] is not null).OrderByDescending(i => stats[i].Net[2]!.Value.Mean).FirstOrDefault(-1);
            if (best >= 0) bestPerGroup.Add(best);
        }
        foreach (var b in bestPerGroup)
        {
            var (_, ic, fi, x) = configs[b];
            for (int gi = 0; gi < groups2.Count; gi++) md.AppendLine(Row(configs.FindIndex(c => c == (gi, ic, fi, x))));
        }
        md.AppendLine();
        // RTH vs premarket entries for the best configurations
        md.AppendLine("Premarket (entry before 09:30) vs regular-hours entries, for each group's best configuration:");
        md.AppendLine();
        md.AppendLine("| configuration | premarket trades | net frozen | net 0.25x | RTH trades | net frozen | net 0.25x |");
        md.AppendLine("|---|---|---|---|---|---|---|");
        foreach (var b in bestPerGroup)
        {
            var (gi, ic, fi, x) = configs[b];
            var l = lists[(gi, ic, fi)].Where(z => !float.IsNaN(z.Sim.Gross[x])).ToList();
            string Part(bool rth, int lv) => ExploreDerived.ClusteredMean(l.Where(z => (z.Sim.Minute >= RunnerGrid.RthOpen) == rth).Select(z => (evalDays[z.Row.DayIdx], (double)(z.Sim.Gross[x] - z.Sim.Cost[lv]), z.Row.Weight))) is { } m ? DeepBounds(m.Mean, m.Lo, m.Hi) : "n/a";
            md.AppendLine($"| {Cfg(b)} | {l.Count(z => z.Sim.Minute < RunnerGrid.RthOpen):N0} | {Part(false, 0)} | {Part(false, 2)} | {l.Count(z => z.Sim.Minute >= RunnerGrid.RthOpen):N0} | {Part(true, 0)} | {Part(true, 2)} |");
        }
        md.AppendLine();
        for (int l = 0; l < 3; l++)
        {
            var fin = Enumerable.Range(0, configs.Count).Where(i => stats[i].Finalist[l]).ToList();
            md.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"- Finalist bar at {(l == 0 ? "the frozen cost" : $"{WatchSpreadScales[l]:0.##}x spread")}: {fin.Count} configuration(s) before DSR, {fin.Count(i => Dsr(stats[i]) >= dsrMin)} after{(fin.Count == 0 ? "" : ": " + string.Join("; ", fin.OrderByDescending(i => stats[i].Net[l]!.Value.Mean).Take(5).Select(i => $"{Cfg(i)} {Pct(stats[i].Net[l]!.Value.Mean)}, DSR {Dsr(stats[i]):0.000}")))}."));
        }
        md.AppendLine();

        // ── F. OMH-like train cases ──
        md.AppendLine("## F. OMH-like cases in train");
        md.AppendLine();
        md.AppendLine("Train runners >= +100% with an OMH trait: a reverse split or ratio change within 120 days, a +100% day in the 10 sessions before (second run), or already >= +50% at the first bar. Top 20 by run. Rank = the boosted >= +100% watchlist rank (weight of better-scored names) on eval mornings; trade = the frozen-cost net of the best all-group configuration at 0.25x (market fill) when it traded the name.");
        md.AppendLine();
        md.AppendLine("| symbol | day | run | 04:00 gap | premarket high | high at | ignition (k=10) | gap at ignition | days since RS | +100% days in prior 10 | D-1 catalyst | same-day catalyst | watchlist rank | best-config net |");
        md.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        int rsCol = t.Feature("x.wl.daysSinceRs"), up100 = t.Feature("x.wl.up100Days10");
        int bestAll = Enumerable.Range(0, configs.Count).Where(i => stats[i].Trades >= 30 && stats[i].Net[2] is not null).OrderByDescending(i => stats[i].Net[2]!.Value.Mean).FirstOrDefault(-1);
        string Types(int bits, bool any) => !any ? "none" : bits == 0 ? "unmatched item" : string.Join(", ", Enumerable.Range(0, CatalystTypes.Keys.Length).Where(k => (bits & (1 << k)) != 0).Select(k => CatalystTypes.Keys[k]));
        var cases = runners.Where(w => (t.Value(w.R, rsCol) is var rs && !float.IsNaN(rs) && rs <= 120) || t.Value(w.R, up100) >= 1 || w.Facts!.FirstGap >= 0.5).OrderByDescending(w => w.Gain).Take(20).ToList();
        foreach (var w in cases)
        {
            var ig = w.Ign[IgnRef];
            string trade = "n/a";
            if (bestAll >= 0 && w.Fold >= 0)
            {
                var (_, ic, fi, x) = configs[bestAll];
                if (w.Ign[ic]?.Sims[fi] is { } sim && !float.IsNaN(sim.Gross[x])) trade = Pct(sim.Gross[x] - sim.Cost[0]);
            }
            md.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"| {t.Symbols[w.R]} | {t.Days[w.R]:yyyy-MM-dd} | {Pct(w.Gain)} | {Pct(w.Facts!.FirstGap)} | {Pct(w.Facts.PmGain)} | {Clk(w.Facts.Hod)} | {(ig is null ? "none" : Clk(ig.Minute))} | {(ig is null ? "n/a" : Pct(ig.Gap))} | {Num(t.Value(w.R, rsCol), "0")} | {Num(t.Value(w.R, up100), "0")} | {Types(w.CatD1, w.AnyD1)} | {Types(w.CatSame, w.AnySame)} | {(double.IsNaN(w.Rank[PrimaryLearner, PrimaryTarget]) ? "n/a" : Num(w.Rank[PrimaryLearner, PrimaryTarget], "0"))} | {trade} |"));
        }
        md.AppendLine();
        md.AppendLine("Exploration only (train). No validation or holdout row is read; the day-2 rerun window stops at the last train day. Same-day catalysts and the anatomy are descriptive. The universe exclusions above bound what any of this can say about the very largest runners.");

        json["configs"] = Enumerable.Range(0, configs.Count).Select(i => new
        {
            group = groups2[configs[i].G].Name, ignition = IgnName(configs[i].Ic), fill = fills[configs[i].Fi], exit = exits[configs[i].X].Name, stats[i].Trades, stats[i].Days, gross = stats[i].Gross, meanCost = stats[i].MeanCost,
            net = stats[i].Net.Select(m => m is { } v ? new { v.Mean, v.Lo, v.Hi } : null).ToList(), folds = stats[i].Folds, worst1 = stats[i].Worst1, top1 = stats[i].Top1, top5 = stats[i].Top5, ge50 = stats[i].Ge50, ge100 = stats[i].Ge100,
            capacity = stats[i].Cap, capacityFull = stats[i].CapFull, lomoLo = stats[i].LomoLo, sharpe = stats[i].Moments.Sharpe, dsr = Dsr(stats[i]), finalist = stats[i].Finalist,
        });
        json["dsr"] = new { nTrials, varianceOfTrialSharpes = varSr, dsrMin };
        return Write("watchlist", md, trials, json);
    }

    private static double? MeanOf(IEnumerable<double?> v) { var l = v.Where(x => x is { } y && double.IsFinite(y)).Select(x => x!.Value).ToList(); return l.Count == 0 ? null : l.Average(); }

    private static double? Q(IEnumerable<double> values, double q) { var l = values.Where(double.IsFinite).ToList(); return l.Count == 0 ? null : Quantile(l, q); }

    /// <summary>Weighted day-clustered stats of one trade configuration over the eval windows, with per-fold and leave-one-month-out bounds at each cost level.</summary>
    private static WlStats TradeStats(List<(WlRow Row, WlSim Sim)> list, int x, int days, int[] dayFold, int[] dayMonth, int monthCount, int foldCount)
    {
        int levels = WatchSpreadScales.Length;
        var sum = new double[levels][];
        for (int l = 0; l < levels; l++) sum[l] = new double[days];
        var wd = new double[days];
        double w = 0, grossW = 0, costW = 0;
        foreach (var (row, sim) in list)
        {
            double g = sim.Gross[x];
            wd[row.DayIdx] += row.Weight;
            for (int l = 0; l < levels; l++) sum[l][row.DayIdx] += (g - sim.Cost[l]) * row.Weight;
            w += row.Weight; grossW += g * row.Weight; costW += sim.Cost[0] * row.Weight;
        }
        (double Mean, double Lo, double Hi, int Days)? Clust(double[] s, Func<int, bool> keep)
        {
            int k = 0; double S = 0, Wt = 0;
            for (int d = 0; d < days; d++) if (wd[d] > 0 && keep(d)) { k++; S += s[d]; Wt += wd[d]; }
            if (k < 2 || Wt <= 0) return null;
            double mean = S / Wt, v = 0;
            for (int d = 0; d < days; d++) if (wd[d] > 0 && keep(d)) v += Math.Pow(s[d] - mean * wd[d], 2);
            v = v / (Wt * Wt) * k / (k - 1);
            double half = 1.96 * Math.Sqrt(v);
            return (mean, mean - half, mean + half, k);
        }
        var net = new (double, double, double, int)?[levels];
        var folds = new double?[levels][];
        var lomo = new double?[levels];
        var finalist = new bool[levels];
        for (int l = 0; l < levels; l++)
        {
            int li = l;
            net[l] = Clust(sum[l], _ => true);
            folds[l] = Enumerable.Range(0, foldCount).Select(k => Clust(sum[li], d => dayFold[d] == k)?.Mean).ToArray();
            for (int m = 0; m < monthCount; m++)
            {
                int mi = m;
                if (!Enumerable.Range(0, days).Any(d => dayMonth[d] == mi && wd[d] > 0)) continue;
                var c = Clust(sum[l], d => dayMonth[d] != mi);
                if (c is { } cc && (lomo[l] is null || cc.Lo < lomo[l])) lomo[l] = cc.Lo;
            }
            finalist[l] = net[l] is { } pm && pm.Item1 >= 0.01 && pm.Item2 > 0 && list.Count >= 150 && pm.Item4 >= 40 && folds[l].Count(f => f > 0) >= 3 && lomo[l] > 0;
        }
        var nets = list.Select(z => (Net: (double)(z.Sim.Gross[x] - z.Sim.Cost[0]), z.Row.Weight)).OrderBy(z => z.Net).ToList();
        double? worst = WeightedQuantile(nets, 0.01);
        double? top1 = null, top5 = null, ge50 = null, ge100 = null;
        if (list.Count > 0 && w > 0)
        {
            var pnl = nets.Select(z => z.Net * z.Weight).OrderByDescending(v => v).ToList();
            double total = pnl.Sum();
            double Top(double q) => total == 0 ? double.NaN : pnl.Take(Math.Max(1, (int)Math.Ceiling(q * pnl.Count))).Sum() / total;
            top1 = Top(0.01); top5 = Top(0.05);
            ge50 = list.Where(z => z.Sim.Gross[x] >= 0.5).Sum(z => z.Row.Weight) / w;
            ge100 = list.Where(z => z.Sim.Gross[x] >= 1.0).Sum(z => z.Row.Weight) / w;
        }
        var cap = new double?[2];
        var capFull = new double?[2];
        for (int c = 0; c < 2; c++)
        {
            int cc = c;
            var traded = list.Where(z => !float.IsNaN(cc == 0 ? z.Sim.Cap1k[x] : z.Sim.Cap5k[x])).ToList();
            double tw = traded.Sum(z => z.Row.Weight);
            cap[c] = tw > 0 ? traded.Sum(z => (cc == 0 ? z.Sim.Cap1k[x] : z.Sim.Cap5k[x]) * z.Row.Weight) / tw : null;
            capFull[c] = list.Count == 0 ? null : (double)list.Count(z => cc == 0 ? z.Sim.Full1k[x] : z.Sim.Full5k[x]) / list.Count;
        }
        return new WlStats(list.Count, net[0]?.Item4 ?? wd.Count(v => v > 0), w, w > 0 ? grossW / w : null, net, folds, worst, top1, top5, ge50, ge100, cap, capFull, w > 0 ? costW / w : null, lomo,
            ReturnMoments.Of(nets.Select(z => z.Net).ToList()), finalist);
    }
}
