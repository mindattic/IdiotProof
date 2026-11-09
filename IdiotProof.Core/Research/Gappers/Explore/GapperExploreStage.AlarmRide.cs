using System.Globalization;
using System.Text;
using IdiotProof.Core.Catalysts;
using IdiotProof.Core.Data;
using IdiotProof.Evidence;
using IdiotProof.Evidence.Gappers;
using IdiotProof.Evidence.Search;
using IdiotProof.Models;
using Microsoft.Extensions.Logging;

namespace IdiotProof.Core.Research.Gappers.Explore;

public sealed partial class GapperExploreStage
{
    /// <summary>Panel decision times the boosted gate reads; an alarm uses the latest one at or before its minute (no look-ahead).</summary>
    public static readonly string[] AlarmRidePanelTimes = ["06:00", "07:00", "08:00", "08:30", "09:00"];
    public static readonly int[] AlarmRideTopK = [1, 2, 3];
    public static readonly double[] AlarmRideTakeProfits = [8, 10, 15, 20];
    public static readonly double[] AlarmRideStops = [4, 6, 8];
    public static readonly int[] AlarmRideTimeStops = [15, 30, 60];
    public static readonly double[] AlarmRideTrails = [15, 25, 35];
    public const double AlarmRideTrailArmPct = 10;
    public const double AlarmRideHalfPct = 20;
    /// <summary>The passive entry: a limit this far under the alarm minute's close, working this many minutes.</summary>
    public const double AlarmRidePassiveDiscount = 0.01;
    public const int AlarmRidePassiveMinutes = 5;
    /// <summary>Spread-component scales: the frozen model, then 0.5x and 0.25x of the premarket multiplier (impact and floor unchanged).</summary>
    public static readonly double[] AlarmRideSpreadScales = [1.0, 0.5, 0.25];
    public static readonly double[] AlarmRideCapacity = [1_000, 5_000];
    /// <summary>Velocity death: the shared rule with a 3-minute window and a 0 %/min floor (the run stopped rising), no minimum run.</summary>
    public static readonly VelocityExitParameters AlarmRideVelocity = new(0, 3);

    private static readonly (string Name, TimeSpan At)[] AlarmRideClocks = [("09:28 opg (09:30 open)", new(9, 30, 0)), ("09:31", new(9, 31, 0))];
    private static readonly TimeSpan[] AlarmRideHardExits = [new(10, 30, 0), new(15, 55, 0)];

    private sealed record RideExitSpec(string Family, GapperLongPlan Plan, int? TimeStopMinutes, bool Velocity)
    {
        public string Name => Plan.Name;
        public bool Lottery => Family != "bracket";
    }

    private static List<RideExitSpec> AlarmRideExits()
    {
        var list = new List<RideExitSpec>();
        foreach (var (clock, at) in AlarmRideClocks)
            foreach (var tp in AlarmRideTakeProfits)
                foreach (var st in AlarmRideStops)
                {
                    foreach (var ts in AlarmRideTimeStops)
                        list.Add(new("bracket", new GapperLongPlan($"tp +{tp:0}% stop -{st:0}% time {ts} min, sell-by {clock}", at, TakeProfitPct: tp, StopPct: st), ts, false));
                    list.Add(new("bracket", new GapperLongPlan($"tp +{tp:0}% stop -{st:0}% velocity death, sell-by {clock}", at, TakeProfitPct: tp, StopPct: st), null, true));
                }
        foreach (var hard in AlarmRideHardExits)
            foreach (var half in new[] { false, true })
                foreach (var st in AlarmRideStops)
                    foreach (var tr in AlarmRideTrails)
                        list.Add(new(half ? "lottery half" : "lottery", new GapperLongPlan(
                            $"lottery stop -{st:0}%, trail {tr:0}% once +{AlarmRideTrailArmPct:0}%{(half ? $", half at +{AlarmRideHalfPct:0}%" : "")}, sell-by {hard:hh\\:mm}",
                            hard, StopPct: st, TrailActivatePct: AlarmRideTrailArmPct, TrailPct: tr, HalfTargetPct: half ? AlarmRideHalfPct : null), null, false));
        return list;
    }

    private sealed record RideSignal(string Name, Func<RideAlarm, bool> Pass);

    private static List<RideSignal> AlarmRideSignals()
    {
        var list = new List<RideSignal> { new("R4", _ => true) };
        foreach (var k in AlarmRideTopK) list.Add(new($"R4 + boosted top{k}", a => a.BoostRank is { } r && r <= k - 1 + 1e-9));
        list.Add(new("R4 + news <= 60 min", a => a.News));
        return list;
    }

    private readonly record struct RideExit(float Gross, bool Target, float Cap1k, float Cap5k, bool Full1k, bool Full5k);

    private sealed record RideEntry(decimal Fill, double[] Cost, RideExit?[] Exits);

    private sealed record RideAlarm(string Symbol, DateOnly Day, int Fold, double Weight, int AlarmIdx, bool Hit, bool Reach20, bool News, double? BoostRank,
        double PmRet, double MaxGain, RideEntry?[] Entries);

    private sealed record RideMorning(DateOnly Day, double Weight, double MaxGain, double? First100Et, double? First500Et, int Alarm, double? AlarmEt, bool Forming, int First20, double PmRetAtAlarm);

    private sealed record RideStats(int Trades, int Days, double? Precision, double? Gross, (double Mean, double Lo, double Hi, int Days)?[] Net, List<double?>[] Folds,
        double? Worst1, double?[] Cap, int[] CapTraded, double?[] CapFull, double? MeanCost, double?[] LomoLo, string?[] LomoMonth, ReturnMoments Moments, bool[] Finalist,
        double? Ge50, double? Ge100, double? Ge300, double? MaxGross, double? Top1Share, double? Top5Share);

    /// <summary>
    /// `explore alarm-ride` (IP-T483): long only, TRAIN only. Buy amendment 04's R4 alarm (alone, gated by the fold-fitted boosted model's
    /// within-morning top k at the latest panel time at or before the alarm, or gated by fresh news) with a marketable next-open entry or a
    /// passive limit 1% under the alarm minute's close, and sell by a take-profit x stop x time-stop (or velocity death) bracket with a
    /// 09:30 opening-print or 09:31 clock, or by the lottery exits (stop, then a trailing stop once +10%, optional half at +20%, out by
    /// 10:30 or 15:55). Net of the frozen cost model and at 0.5x / 0.25x of its spread component; eval windows of the four folds only.
    /// One ledger trial per signal x fill x exit.
    /// </summary>
    private async Task<(List<string>, int, string?)> AlarmRideAsync(ExploreRequest req, CancellationToken ct)
    {
        var p = s.Params;
        var (multiplier, source) = GapperSpreadMultiplier.Resolve(s.Paths, p);
        if (multiplier is null) return ([$"cost model unavailable ({source}); run `calibrate-spread` first"], 0, null);
        var notional = GapperSpreadMultiplier.TradeNotional(p) ?? throw new InvalidDataException("params.json has no costModel.impact.tradeNotionalUsd.");
        double impact = GapperSpreadMultiplier.ImpactConstant(p), floor = p.CostModel.RoundTripFloor;
        var profilePath = s.Paths.ProfileFile(false);
        if (!File.Exists(profilePath)) throw new InvalidOperationException($"{Path.GetFileName(profilePath)} does not exist; run `snapshot train` first so the time-of-day profile is fit.");
        var profile = TimeOfDayProfile.Load(profilePath);
        var store = await s.StoreAsync(ct);
        var tradeProfile = await TradeProfileAsync(store, ct);
        var amendment = s.Amendment;

        // ── 1. boosted within-morning ranks per panel time, fit per fold on the fit window, scored on the eval window ──
        var times = AlarmRidePanelTimes.Where(x => ExplorePanelFile.Exists(CacheDir(), x)).ToList();
        if (times.Count == 0) return (["no explore panel among " + string.Join(", ", AlarmRidePanelTimes) + "; run `explore build` first"], 0, null);
        var ranks = new Dictionary<(string, DateOnly), double[]>();
        var csSpread = new Dictionary<(string, DateOnly), double>();
        var panelFolds = new List<string>();
        for (int ti = 0; ti < times.Count; ti++)
        {
            GC.Collect(); // the previous panel is unreachable here; about 5.8 GB is free
            var t = Load(times[ti]);
            var tf = Folds(t);
            if (ti == 0) panelFolds.AddRange(tf.Select(f => f.Label));
            int cs = t.Feature("x." + PriorDaysCorwinSchultzFeature.FeatureName);
            for (int r = 0; r < t.Rows; r++)
                if (cs >= 0 && t.Value(r, cs) is var v && !float.IsNaN(v)) csSpread.TryAdd((t.Symbols[r], t.Days[r]), v);
            var rows = Scorable(t, formingOnly: true, sameDay: true);
            var all = AllFeatures(t);
            var y = t.Binary(Target);
            for (int k = 0; k < tf.Count; k++)
            {
                var fit = rows.Where(r => tf[k].InFit(t.Days[r])).ToList();
                var eval = rows.Where(r => tf[k].InEval(t.Days[r])).ToList();
                if (fit.Count == 0 || eval.Count == 0 || !fit.Any(r => y[r] == true)) continue;
                var scores = Boosted(t, all, fit, eval);
                foreach (var day in Enumerable.Range(0, eval.Count).GroupBy(i => t.Days[eval[i]]))
                {
                    double cum = 0;
                    foreach (var i in day.OrderByDescending(i => scores[i]).ThenBy(i => eval[i]))
                    {
                        var key = (t.Symbols[eval[i]], t.Days[eval[i]]);
                        if (!ranks.TryGetValue(key, out var arr)) { ranks[key] = arr = new double[times.Count]; Array.Fill(arr, double.NaN); }
                        arr[ti] = cum;
                        cum += t.Weights[eval[i]];
                    }
                }
                s.Logger.LogInformation("[{Now}] explore alarm-ride: boosted at {Time} fold {Fold} scored {Eval} eval rows", GapperStudyServices.Et(s.UtcNow), times[ti], k + 1, eval.Count);
            }
        }
        var panelTimes = times.Select(x => DecisionTime.Parse(x).TimeEt).ToArray();

        // ── 2. the R4 sweep over every eval-window population morning, with every entry and exit simulated per alarm ──
        var population = (await s.StudyDaysAsync("train", req.From, req.To, ct))
            .Select(u => (U: u, Sampled: !u.IsCandidate && amendment is not null && amendment.IsSampleMember(u.Symbol, u.DateEt)))
            .Where(x => x.U.IsCandidate || x.Sampled).ToList();
        var folds = ExploreFolds.Build(population.Select(x => x.U.DateEt), foldMonths.Initial, foldMonths.Eval);
        if (folds.Count == 0) return (["no train fold has an evaluation window in the requested range"], 0, null);
        var reachEt = GapperStudyParams.ParseEt(p.Labels.Reach20.ByEt);
        var exits = AlarmRideExits();
        var latestBracket = AlarmRideClocks.Max(c => c.At);
        var velocityRule = new VelocityExitRule(AlarmRideVelocity);
        var panel = new GapperStudyPanel(s);
        var alarms = new List<RideAlarm>();
        var mornings = new List<RideMorning>();
        int evalDays = 0;

        foreach (var group in population.GroupBy(x => x.U.DateEt).OrderBy(g => g.Key))
        {
            ct.ThrowIfCancellationRequested();
            var day = group.Key;
            int fold = folds.FindIndex(f => f.InEval(day));
            if (fold < 0) continue;
            var rows = group.ToList();
            var symbols = rows.Select(x => x.U.Symbol).Distinct().ToList();
            Dictionary<string, double> adt;
            Dictionary<string, List<CatalystObservation>> news;
            await using (var db = await s.Db.CreateDbContextAsync(ct))
            {
                adt = await GapperAlarmData.AdtAsync(db, symbols, day, ct);
                news = await GapperAlarmData.NewsAsync(db, symbols, day, ct);
            }
            var bars = new IReadOnlyList<Candle>[rows.Count];
            var computed = new AlarmMorning?[rows.Count];
            Parallel.For(0, rows.Count, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, req.Parallelism), CancellationToken = ct }, i =>
            {
                var (u, sampled) = rows[i];
                bars[i] = store.ReadDay(u.Symbol, u.DateEt);
                computed[i] = GapperAlarmData.MorningOf(u, sampled ? amendment!.SampleWeight : 1, sampled, bars[i], adt.TryGetValue(u.Symbol, out var a) ? a : null,
                    news.GetValueOrDefault(u.Symbol) ?? [], profile, tradeProfile, reachEt, p.Labels.Reach20.Threshold);
            });
            var idx = Enumerable.Range(0, rows.Count).Where(i => computed[i] is not null).ToList();
            if (idx.Count == 0) continue;
            evalDays++;
            var dayRows = idx.Select(i => computed[i]!).ToList();
            var alarm = AlarmSweep.RankAlarms(dayRows, AlarmSweep.Scores(dayRows).Sorted(GapperAlarmData.R4.Score), GapperAlarmData.R4);

            var sessionStart = MinuteBarMath.ToUtc(day, MinuteBarMath.SessionStartEt);
            var close16 = MinuteBarMath.ToUtc(day, new TimeSpan(16, 0, 0));
            var pending = new List<(int J, GapperStudyUniverseDay U, AlarmMorning Row, List<Candle> Real, double MaxGain, double? Sigma)>();
            for (int j = 0; j < idx.Count; j++)
            {
                var u = rows[idx[j]].U;
                var row = dayRows[j];
                double prev = (double)u.AdjPrevClose;
                double maxHigh = 0;
                double? first100 = null, first500 = null;
                foreach (var b in bars[idx[j]].OrderBy(b => b.StartUtc))
                {
                    if (b.StartUtc < sessionStart || b.EndUtc > close16 || b.Volume <= 0) continue;
                    double h = (double)b.High;
                    maxHigh = Math.Max(maxHigh, h);
                    double et = (MinuteBarMath.ToEastern(b.StartUtc).TimeOfDay).TotalMinutes;
                    if (first100 is null && h / prev - 1 >= 1.0) first100 = et;
                    if (first500 is null && h / prev - 1 >= 5.0) first500 = et;
                }
                double maxGain = prev > 0 && maxHigh > 0 ? maxHigh / prev - 1 : double.NaN;
                int al = alarm[j];
                double? alarmEt = al >= 0 ? (AlarmSweep.WindowStartEt + TimeSpan.FromMinutes(al)).TotalMinutes : null;
                mornings.Add(new RideMorning(day, row.Weight, maxGain, first100, first500, al, alarmEt, row.Forming, row.First20, al >= 0 ? row.PmRet[al] : double.NaN));
                if (al < 0) continue;
                var sigma = GapperCostModel.Sigma(await panel.PriorClosesAsync(u.Symbol, day, p.Features.Adv20Sessions, ct), p.Features.Adv20Sessions);
                pending.Add((j, u, row, GapperLongPath.Real(bars[idx[j]]), maxGain, sigma));
            }

            var simulated = new RideAlarm[pending.Count];
            Parallel.For(0, pending.Count, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, req.Parallelism), CancellationToken = ct }, pi =>
            {
                var (j, u, row, real, maxGain, sigma) = pending[pi];
                int al = alarm[j];
                var alarmEt = AlarmSweep.WindowStartEt + TimeSpan.FromMinutes(al);
                var decisionUtc = MinuteBarMath.ToUtc(day, alarmEt);
                double? boost = null;
                if (ranks.TryGetValue((u.Symbol, day), out var rk))
                {
                    int ti = Array.FindLastIndex(panelTimes, x => x <= alarmEt);
                    if (ti >= 0 && !double.IsNaN(rk[ti])) boost = rk[ti];
                }
                double? spread = csSpread.TryGetValue((u.Symbol, day), out var csv) ? csv : null;
                var entries = new RideEntry?[2];
                for (int fi = 0; fi < 2; fi++)
                {
                    GapperEntry? entry = fi == 0
                        ? GapperEntrySimulator.Find(GapperEntryKind.Now, real, day, decisionUtc)
                        : MinuteBarMath.CloseAsOf(real, decisionUtc) is { } c && c > 0
                            ? GapperLongPath.PassiveLimit(real, decisionUtc, c * (1m - (decimal)AlarmRidePassiveDiscount), AlarmRidePassiveMinutes)
                            : null;
                    if (entry is null || entry.Fill <= 0 || entry.BarStartUtc >= MinuteBarMath.ToUtc(day, AlarmRideClocks.Min(x => x.At))) continue;
                    var since = real.Where(b => b.StartUtc >= entry.BarStartUtc).ToList();
                    double price = (double)entry.Fill;
                    double pmCumVol = (double)MinuteBarMath.VolumeBetween(real, sessionStart, entry.BarStartUtc);
                    double Cost(double size, double scale) => GapperCostModel.Estimate(new GapperCostInputs(spread, multiplier.Value * scale, pmCumVol, sigma, price, size, impact, floor)).RoundTrip;
                    var costs = AlarmRideSpreadScales.Select(sc => Cost(notional, sc)).ToArray();
                    double v5 = (double)MinuteBarMath.VolumeBetween(real, entry.BarStartUtc.AddMinutes(-5), entry.BarStartUtc);
                    var entryEt = MinuteBarMath.ToEastern(entry.BarStartUtc).TimeOfDay;
                    var death = GapperLongPath.VelocityDeath(since, entry.BarStartUtc, entry.Fill, MinuteBarMath.ToUtc(day, latestBracket), velocityRule);
                    var results = new RideExit?[exits.Count];
                    for (int ei = 0; ei < exits.Count; ei++)
                    {
                        var ex = exits[ei];
                        DateTime? signal = ex.Velocity ? death : ex.TimeStopMinutes is { } m ? entry.BarStartUtc.AddMinutes(m) : null;
                        var res = GapperLongPath.Simulate(ex.Plan, since, day, entry.BarStartUtc, entry.Fill, signal, targetsOnEntryBar: fi == 0);
                        if (res is null) continue;
                        var exitEt = MinuteBarMath.ToEastern(res.FinalExitUtc).TimeOfDay;
                        double? vexit = ParticipationCap.ForecastWindowVolume(profile, u.AdvDecile, pmCumVol, entryEt, exitEt - TimeSpan.FromMinutes(1), exitEt + TimeSpan.FromMinutes(4));
                        var caps = AlarmRideCapacity.Select(n => ParticipationCap.Size(price, n, v5, vexit)).ToArray();
                        float CapNet(ParticipationSize z) => z.Skip ? float.NaN : (float)(res.GrossReturn - Cost(z.NotionalUsd, 1));
                        results[ei] = new RideExit((float)res.GrossReturn, res.TargetHit, CapNet(caps[0]), CapNet(caps[1]), caps[0].FullSize, caps[1].FullSize);
                    }
                    entries[fi] = new RideEntry(entry.Fill, costs, results);
                }
                simulated[pi] = new RideAlarm(u.Symbol, day, fold, row.Weight, al, row.Forming && al < row.First20, row.Reach20, row.News?[al] == true, boost,
                    row.PmRet[al], maxGain, entries);
            });
            alarms.AddRange(simulated);
            s.Logger.LogInformation("[{Now}] explore alarm-ride: {Day} fold {Fold}: {Rows} mornings, {Alarms} R4 alarms ({Total} so far)", GapperStudyServices.Et(s.UtcNow), day, fold + 1, dayRows.Count, pending.Count, alarms.Count);
        }

        return AlarmRideReport(folds, panelFolds, times, exits, alarms, mornings, evalDays, multiplier.Value, source, notional);
    }

    private (List<string>, int, string?) AlarmRideReport(List<ExploreFold> folds, List<string> panelFolds, List<string> times, List<RideExitSpec> exits,
        List<RideAlarm> alarms, List<RideMorning> mornings, int evalDays, double multiplier, string source, double notional)
    {
        var p = s.Params;
        var signals = AlarmRideSignals();
        string[] fills = ["marketable next open", "passive limit -1% (5 min)"];
        int levels = AlarmRideSpreadScales.Length;
        (double Mean, double Lo, double Hi, int Days)? Clustered(IEnumerable<(RideAlarm A, double V)> v) => ExploreDerived.ClusteredMean(v.Select(x => (x.A.Day, x.V, x.A.Weight)));

        RideStats Stats(List<(RideAlarm A, RideEntry E, RideExit X)> list, bool lottery)
        {
            double w = list.Sum(x => x.A.Weight);
            double? precision = w > 0 ? list.Where(x => x.X.Target).Sum(x => x.A.Weight) / w : null;
            var gross = Clustered(list.Select(x => (x.A, (double)x.X.Gross)));
            var net = new (double, double, double, int)?[levels];
            var perFold = new List<double?>[levels];
            var lomoLo = new double?[levels];
            var lomoMonth = new string?[levels];
            var finalist = new bool[levels];
            var months = list.Select(x => x.A.Day.ToString("yyyy-MM", CultureInfo.InvariantCulture)).Distinct().ToList();
            for (int l = 0; l < levels; l++)
            {
                int li = l;
                double Net((RideAlarm A, RideEntry E, RideExit X) x) => x.X.Gross - x.E.Cost[li];
                net[l] = Clustered(list.Select(x => (x.A, Net(x))));
                perFold[l] = folds.Select((_, k) => Clustered(list.Where(x => x.A.Fold == k).Select(x => (x.A, Net(x))))?.Mean).ToList();
                foreach (var month in months)
                {
                    var m = Clustered(list.Where(x => x.A.Day.ToString("yyyy-MM", CultureInfo.InvariantCulture) != month).Select(x => (x.A, Net(x))));
                    if (m is null) continue;
                    if (lomoLo[l] is null || m.Value.Lo < lomoLo[l]) { lomoLo[l] = m.Value.Lo; lomoMonth[l] = month; }
                }
                finalist[l] = net[l] is { } pm && pm.Item1 >= 0.01 && pm.Item2 > 0 && list.Count >= 150 && pm.Item4 >= 40 && perFold[l].Count(f => f > 0) >= 3 && lomoLo[l] > 0;
            }
            var frozen = list.Select(x => x.X.Gross - x.E.Cost[0]).ToList();
            var cap = new double?[AlarmRideCapacity.Length];
            var capN = new int[AlarmRideCapacity.Length];
            var capFull = new double?[AlarmRideCapacity.Length];
            for (int c = 0; c < AlarmRideCapacity.Length; c++)
            {
                int ci = c;
                var traded = list.Where(x => !float.IsNaN(ci == 0 ? x.X.Cap1k : x.X.Cap5k)).ToList();
                cap[c] = Clustered(traded.Select(x => (x.A, (double)(ci == 0 ? x.X.Cap1k : x.X.Cap5k))))?.Mean;
                capN[c] = traded.Count;
                capFull[c] = list.Count == 0 ? null : (double)list.Count(x => ci == 0 ? x.X.Full1k : x.X.Full5k) / list.Count;
            }
            double? meanCost = w > 0 ? list.Sum(x => x.E.Cost[0] * x.A.Weight) / w : null;
            double? ge50 = null, ge100 = null, ge300 = null, max = null, top1 = null, top5 = null;
            if (lottery && list.Count > 0)
            {
                double Share(double th) => list.Where(x => x.X.Gross >= th).Sum(x => x.A.Weight) / w;
                ge50 = Share(0.5); ge100 = Share(1.0); ge300 = Share(3.0);
                max = list.Max(x => (double)x.X.Gross);
                var pnl = list.Select(x => (x.X.Gross - x.E.Cost[0]) * x.A.Weight).OrderByDescending(v => v).ToList();
                double total = pnl.Sum();
                double TopShare(double q) => total == 0 ? double.NaN : pnl.Take(Math.Max(1, (int)Math.Ceiling(q * pnl.Count))).Sum() / total;
                top1 = TopShare(0.01); top5 = TopShare(0.05);
            }
            return new RideStats(list.Count, gross?.Days ?? 0, precision, gross?.Mean, net, perFold,
                WeightedQuantile(list.Select(x => (x.X.Gross - x.E.Cost[0], x.A.Weight)).ToList(), 0.01), cap, capN, capFull, meanCost, lomoLo, lomoMonth,
                ReturnMoments.Of(frozen), finalist, ge50, ge100, ge300, max, top1, top5);
        }

        var configs = new List<(int Sig, int Fill, int Exit, RideStats St)>();
        for (int si = 0; si < signals.Count; si++)
        {
            var pass = alarms.Where(signals[si].Pass).ToList();
            for (int fi = 0; fi < fills.Length; fi++)
                for (int ei = 0; ei < exits.Count; ei++)
                {
                    var list = pass.Where(a => a.Entries[fi]?.Exits[ei] is not null).Select(a => (a, a.Entries[fi]!, a.Entries[fi]!.Exits[ei]!.Value)).ToList();
                    configs.Add((si, fi, ei, Stats(list, exits[ei].Lottery)));
                }
        }

        // ── trials and the deflated Sharpe ──
        string Cfg(int si, int fi, int ei) => $"long {signals[si].Name} entry={fills[fi]} exit={exits[ei].Name}";
        var trials = configs.Select(c => new ExploreTrial
        {
            Track = "alarm-ride", Config = Cfg(c.Sig, c.Fill, c.Exit), Metric = "meanNetReturn (long, frozen cost, eval windows)",
            FoldValues = c.St.Folds[0], Pooled = c.St.Net[0]?.Mean, Rows = c.St.Trades,
            Note = string.Create(CultureInfo.InvariantCulture,
                $"lo={DeepPct(c.St.Net[0]?.Lo)} hi={DeepPct(c.St.Net[0]?.Hi)} days={c.St.Days} gross={DeepPct(c.St.Gross)} precision={DeepShare(c.St.Precision)} net0.5x={DeepPct(c.St.Net[1]?.Mean)} net0.25x={DeepPct(c.St.Net[2]?.Mean)} worst1={DeepPct(c.St.Worst1)}"),
        }).ToList();
        int nTrials = Ledger.Count + trials.Count;
        var sharpes = configs.Where(c => c.St.Trades >= 2).Select(c => c.St.Moments.Sharpe).Where(double.IsFinite).ToList();
        double varSr = sharpes.Count > 1 ? Math.Pow(EvidenceStats.StdDev(sharpes), 2) : 0;
        double Dsr(RideStats st) => st.Trades >= 2 ? MultipleTesting.DeflatedSharpe(st.Moments, nTrials, varSr) : double.NaN;
        double dsrMin = p.Statistics.DsrMin;

        // ── report ──
        var md = new StringBuilder(Header("alarm ride, long only (IP-T483): buy amendment 04's R4 alarm, sell into the run", folds));
        md.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"Alarm: R4 = `{GapperAlarmData.R4Name}`, its first minute in {AlarmSweep.WindowStartEt:hh\\:mm}-{AlarmSweep.WindowEndEt:hh\\:mm} ET over every population morning (candidates + amendment 01's weighted sample) of the four eval windows ({evalDays:N0} eval days, {alarms.Count:N0} R4 alarms, weighted {alarms.Sum(a => a.Weight):N0}). The shared `AlarmSweep` / `GapperAlarmData` read the morning exactly as amendment 04's test does."));
        md.AppendLine();
        md.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"Signals: (a) R4; (b) R4 only when the boosted model (all features, target Reach20By0945, mornings still forming) ranks the name in that morning's weighted top k (k = 1/2/3: the weight of better-scored names is under k), fitted per fold on the fold's fit window at each panel time ({string.Join(", ", times)}) and scored on its eval window; the panels exist only at those times, so an alarm reads the latest panel time at or before its minute (never a later one); (c) R4 with fresh news (an item within {AlarmSweep.FreshNewsMinutes:0} min) at the alarm minute. Panel folds: {string.Join("; ", panelFolds)} (the sweep's folds are built the same way from the population days)."));
        md.AppendLine();
        md.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"Fills: marketable = the open of the first bar starting at or after the alarm minute's end; passive = a limit {AlarmRidePassiveDiscount:P0} under the alarm minute's close, filled at the better of the limit and the open on the first bar within {AlarmRidePassiveMinutes} minutes whose low trades at or below it, else no trade (no target can fill inside a passive entry's own bar; the stop can). An entry at or after 09:30 is dropped. Exits, first hit wins: a stop fills at the worse of the stop and the bar open; a take-profit at the target only when a bar's high trades through it; time stops and velocity death (`VelocityExitRule`, 3-minute window, under 0 %/min, no minimum run) sell at the next bar's open; the clock sells at the 09:30 bar's open (an opg order sent by 09:28; the opening print is the 09:30 minute bar's open) or at 09:31. Lottery exits: initial stop, replaced once a high reaches +{AlarmRideTrailArmPct:0}% by a trailing stop that far under the highest high of the bars before the current one; optional half sold at +{AlarmRideHalfPct:0}%; out at 10:30 or 15:55."));
        md.AppendLine();
        md.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"Cost: the frozen model at ${notional:N0} (Corwin-Schultz x the premarket multiplier {multiplier:0.###} from {source}, plus square-root impact on the premarket volume to entry and the 20-session sigma, floored at {p.CostModel.RoundTripFloor:P1}), and the same with the spread component at 0.5x and 0.25x (impact and floor unchanged). The multiplier is applied unchanged to exits after the open. Break-even cost = the mean gross return (the round trip at which net is zero). Capacity: `ParticipationCap` (5% of the 5 minutes before entry and of the profile-forecast shares in the minute before to 4 after the actual exit), net at the capped size, skipped under $200."));
        md.AppendLine();

        // 1. alarm-level precision
        md.AppendLine("## 1. Alarm precision: R4 alone vs gated");
        md.AppendLine();
        md.AppendLine("Weighted. Hit = still below +20% at 06:00, reaches +20% by 09:45, alarmed before the first +20% touch; false = never reaches +20% by 09:45; reach20 = reached +20% by 09:45 at all (including names already past +20% when alarmed, which R4's 15% gap cap rules out after 06:00).");
        md.AppendLine();
        md.AppendLine("| signal | alarms | weighted | hit share | false share | reach20 share | marketable entries | passive fills | per fold hit share |");
        md.AppendLine("|---|---|---|---|---|---|---|---|---|");
        var precisionJson = new List<object>();
        foreach (var sig in signals)
        {
            var l = alarms.Where(sig.Pass).ToList();
            double w = l.Sum(a => a.Weight);
            double? Sh(Func<RideAlarm, bool> f) => w > 0 ? l.Where(f).Sum(a => a.Weight) / w : null;
            var perFold = folds.Select((_, k) => { var fl = l.Where(a => a.Fold == k).ToList(); double fw = fl.Sum(a => a.Weight); return fw > 0 ? fl.Where(a => a.Hit).Sum(a => a.Weight) / fw : (double?)null; }).ToList();
            md.AppendLine($"| {sig.Name} | {l.Count:N0} | {w:N0} | {DeepShare(Sh(a => a.Hit))} | {DeepShare(Sh(a => !a.Reach20))} | {DeepShare(Sh(a => a.Reach20))} | {l.Count(a => a.Entries[0] is not null):N0} | {l.Count(a => a.Entries[1] is not null):N0} ({DeepShare(l.Count == 0 ? null : (double)l.Count(a => a.Entries[1] is not null) / l.Count)}) | {string.Join(" / ", perFold.Select(DeepShare))} |");
            precisionJson.Add(new { signal = sig.Name, alarms = l.Count, weighted = w, hit = Sh(a => a.Hit), falseShare = Sh(a => !a.Reach20), reach20 = Sh(a => a.Reach20), perFold });
        }
        md.AppendLine();

        // 2. monsters
        md.AppendLine("## 2. Monsters: mornings that reached +100% / +500% over the prior close by 16:00");
        md.AppendLine();
        md.AppendLine("Every eval-window population morning (weighted counts; raw in brackets). Alarmed early = R4 alarmed before the first +20% touch (or the name never touched +20% by 09:45 but ran later).");
        md.AppendLine();
        md.AppendLine("| level | mornings | per eval day | R4 alarmed | alarmed early | share of R4 alarms that became one | median gap at alarm | median minutes from alarm to the level |");
        md.AppendLine("|---|---|---|---|---|---|---|---|");
        var monsterJson = new List<object>();
        double alarmW = mornings.Where(m => m.Alarm >= 0).Sum(m => m.Weight);
        foreach (var (name, lvl, first) in new (string, double, Func<RideMorning, double?>)[] { ("+100%", 1.0, m => m.First100Et), ("+500%", 5.0, m => m.First500Et) })
        {
            var ms = mornings.Where(m => m.MaxGain >= lvl).ToList();
            var al = ms.Where(m => m.Alarm >= 0).ToList();
            var early = al.Where(m => m.First20 == int.MaxValue || m.Alarm < m.First20).ToList();
            var leads = al.Where(m => first(m) is { } f && m.AlarmEt is { } a && f >= a).Select(m => first(m)!.Value - m.AlarmEt!.Value).ToList();
            double W(IEnumerable<RideMorning> x) => x.Sum(m => m.Weight);
            md.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"| {name} | {W(ms):N1} ({ms.Count}) | {(evalDays == 0 ? "n/a" : (W(ms) / evalDays).ToString("0.000", CultureInfo.InvariantCulture))} | {W(al):N1} ({al.Count}, {DeepShare(W(ms) > 0 ? W(al) / W(ms) : null)}) | {W(early):N1} ({early.Count}, {DeepShare(W(ms) > 0 ? W(early) / W(ms) : null)}) | {DeepShare(alarmW > 0 ? W(al) / alarmW : null)} | {DeepPct(MedianOf(al.Select(m => m.PmRetAtAlarm).Where(double.IsFinite).ToList()))} | {DeepNum(MedianOf(leads))} |"));
            monsterJson.Add(new { level = name, mornings = W(ms), raw = ms.Count, alarmed = W(al), alarmedRaw = al.Count, early = W(early), earlyRaw = early.Count, shareOfAlarms = alarmW > 0 ? W(al) / alarmW : (double?)null, medianLeadMinutes = MedianOf(leads) });
        }
        md.AppendLine();

        // 3. best configurations
        string Row((int Sig, int Fill, int Exit, RideStats St) c)
        {
            var st = c.St;
            string N(int l) => st.Net[l] is { } m ? DeepBounds(m.Mean, m.Lo, m.Hi) : "n/a";
            return string.Create(CultureInfo.InvariantCulture,
                $"| {signals[c.Sig].Name} | {(c.Fill == 0 ? "market" : "passive")} | {exits[c.Exit].Name} | {st.Trades:N0} | {st.Days:N0} | {DeepShare(st.Precision)} | {DeepPct(st.Gross)} | {DeepPct(st.MeanCost)} | {N(0)} | {N(1)} | {N(2)} | {string.Join(" / ", st.Folds[0].Select(DeepPct))} | {st.Folds[0].Count(f => f > 0)}/{folds.Count} | {DeepPct(st.Worst1)} | {DeepPct(st.Cap[0])} ({DeepShare(st.CapFull[0])}) | {DeepPct(st.Cap[1])} ({DeepShare(st.CapFull[1])}) | {DeepPct(st.LomoLo[0])} | {Dsr(st):0.000} | {string.Join("/", st.Finalist.Select(f => f ? "Y" : "-"))} |");
        }
        const string head = "| signal | fill | exit | trades | days | precision (target hit) | gross mean = break-even cost | mean frozen cost | net frozen [95%] | net 0.5x spread [95%] | net 0.25x spread [95%] | per fold (frozen) | folds > 0 | worst 1% | cap $1k net (full size) | cap $5k net (full size) | LOMO min lower bound | DSR | finalist frozen/0.5x/0.25x |";
        const string sep = "|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|";
        md.AppendLine("## 3. Best configurations");
        md.AppendLine();
        md.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"Ranked by pooled mean net (weighted, day-clustered 95%), configurations with at least 30 trades. DSR = per-trade deflated Sharpe of the unweighted frozen-cost net series at N = {nTrials:N0} (the ledger total after this run) with the variance of trial Sharpes over this run's {sharpes.Count:N0} configurations ({varSr:0.0000}); frozen minimum {dsrMin:0.00}. Finalist (explore-plan.md): >= +1.0% net with lower bound > 0, >= 150 trades over >= 40 days, > 0 in at least 3 of 4 folds, and the leave-one-month-out lower bound still > 0."));
        foreach (var (title, l) in new[] { ("frozen cost", 0), ("0.5x spread", 1), ("0.25x spread", 2) })
        {
            md.AppendLine();
            md.AppendLine($"**Top 10 at {title}**");
            md.AppendLine();
            md.AppendLine(head);
            md.AppendLine(sep);
            foreach (var c in configs.Where(c => c.St.Trades >= 30 && c.St.Net[l] is not null).OrderByDescending(c => c.St.Net[l]!.Value.Mean).Take(10)) md.AppendLine(Row(c));
        }
        md.AppendLine();
        md.AppendLine("**Top 5 by gross mean (break-even cost)**");
        md.AppendLine();
        md.AppendLine(head);
        md.AppendLine(sep);
        foreach (var c in configs.Where(c => c.St.Trades >= 30 && c.St.Gross is not null).OrderByDescending(c => c.St.Gross!.Value).Take(5)) md.AppendLine(Row(c));
        md.AppendLine();
        for (int l = 0; l < levels; l++)
        {
            var fin = configs.Where(c => c.St.Finalist[l]).ToList();
            md.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"- Finalist bar at {(l == 0 ? "the frozen cost" : $"{AlarmRideSpreadScales[l]:0.##}x spread")}: {fin.Count} configuration(s){(fin.Count == 0 ? "" : ": " + string.Join("; ", fin.OrderByDescending(c => c.St.Net[l]!.Value.Mean).Take(5).Select(c => $"{Cfg(c.Sig, c.Fill, c.Exit)} {DeepPct(c.St.Net[l]!.Value.Mean)}, DSR {Dsr(c.St):0.000}")))}."));
        }
        md.AppendLine();

        // 4. lottery
        md.AppendLine("## 4. Lottery exits: the shape of the payoff");
        md.AppendLine();
        md.AppendLine("Per-trade gross returns (weighted shares); P&L share = the top 1% / 5% of trades' share of the summed weighted frozen-cost net (over 100% when the rest loses money; n/a at zero).");
        md.AppendLine();
        md.AppendLine("| signal | fill | exit | trades | gross mean | net frozen [95%] | net 0.5x | >= +50% | >= +100% | >= +300% | max | top 1% P&L share | top 5% P&L share | folds > 0 |");
        md.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        var lotteryJson = new List<object>();
        foreach (var c in configs.Where(c => exits[c.Exit].Lottery))
        {
            var st = c.St;
            md.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"| {signals[c.Sig].Name} | {(c.Fill == 0 ? "market" : "passive")} | {exits[c.Exit].Name} | {st.Trades:N0} | {DeepPct(st.Gross)} | {(st.Net[0] is { } m ? DeepBounds(m.Mean, m.Lo, m.Hi) : "n/a")} | {DeepPct(st.Net[1]?.Mean)} | {DeepShare(st.Ge50)} | {DeepShare(st.Ge100)} | {DeepShare(st.Ge300)} | {DeepPct(st.MaxGross)} | {DeepShare(st.Top1Share)} | {DeepShare(st.Top5Share)} | {st.Folds[0].Count(f => f > 0)}/{folds.Count} |"));
            lotteryJson.Add(new { signal = signals[c.Sig].Name, fill = fills[c.Fill], exit = exits[c.Exit].Name, st.Trades, st.Gross, net = st.Net[0]?.Mean, netHalf = st.Net[1]?.Mean, st.Ge50, st.Ge100, st.Ge300, max = st.MaxGross, top1 = st.Top1Share, top5 = st.Top5Share });
        }
        md.AppendLine();

        // 5. every configuration
        md.AppendLine("## 5. Every configuration");
        md.AppendLine();
        md.AppendLine(head);
        md.AppendLine(sep);
        foreach (var c in configs) md.AppendLine(Row(c));
        md.AppendLine();
        md.AppendLine("Exploration only (train eval windows). The frozen cost model was calibrated for premarket fills; exits after the open reuse its multiplier. The boosted gate reads the panel at the latest decision time at or before the alarm, so it is up to an hour stale for late alarms.");

        var json = new
        {
            precision = precisionJson, monsters = monsterJson, lottery = lotteryJson, nTrials, varianceOfTrialSharpes = varSr, dsrMin,
            configs = configs.Select(c => new
            {
                signal = signals[c.Sig].Name, fill = fills[c.Fill], exit = exits[c.Exit].Name, family = exits[c.Exit].Family, c.St.Trades, c.St.Days, c.St.Precision, gross = c.St.Gross, meanCost = c.St.MeanCost,
                net = c.St.Net.Select(m => m is { } x ? new { x.Mean, x.Lo, x.Hi } : null).ToList(), folds = c.St.Folds, worst1 = c.St.Worst1, capacity = c.St.Cap, capacityTraded = c.St.CapTraded, capacityFull = c.St.CapFull,
                lomoLo = c.St.LomoLo, lomoMonth = c.St.LomoMonth, sharpe = c.St.Moments.Sharpe, dsr = Dsr(c.St), finalist = c.St.Finalist,
            }),
        };
        return Write("alarm-ride", md, trials, json);
    }
}
