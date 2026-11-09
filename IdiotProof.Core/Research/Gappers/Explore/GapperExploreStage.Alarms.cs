using System.Globalization;
using System.Text;
using IdiotProof.Core.Catalysts;
using IdiotProof.Core.Data;
using IdiotProof.Core.Evidence;
using IdiotProof.Evidence.Gappers;
using IdiotProof.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IdiotProof.Core.Research.Gappers.Explore;

/// <summary>
/// <c>explore alarms</c> (IP-T470; methods-onset.md P0/P1/P2/P4, strategy-refined.md §2.2): alarm-time
/// scoring of onset detectors on TRAIN only. Every population morning (candidates plus amendment 01's
/// weighted sample) is swept minute by minute from 04:00 ET with <see cref="OnsetTrack"/> (count CUSUM
/// on volume and on trade count against the time-of-day profiles, the volume-confirmed gap), and each
/// rule's first alarm in 06:00-09:10 is recorded. Per fold it reports the hit rate (mornings that reach
/// +20% by 09:45, still below +20% at 06:00, alarmed before the first +20%), the lead from the alarm to
/// the first +20% (median and IQR), false alarms per 100 population mornings and alarms per morning,
/// beside the gainers + news benchmark (B3). One ledger trial per configuration.
/// </summary>
public sealed partial class GapperExploreStage
{
    // The sweep itself (window, floors, drifts, scores, rank-k pass logic, outcomes) lives in the shared,
    // split-agnostic AlarmSweep / GapperAlarmData so amendment 04's confirmatory test reads it identically.
    public static readonly TimeSpan AlarmWindowStartEt = AlarmSweep.WindowStartEt;
    public static readonly TimeSpan AlarmWindowEndEt = AlarmSweep.WindowEndEt;

    /// <summary>The protocol's premarket dollar-volume floor at the alarm minute.</summary>
    public const double AlarmMinPmDollarVolume = AlarmSweep.MinPmDollarVolume;

    /// <summary>conf(t) floor of the rank rules: most of the gap has actually traded through.</summary>
    public const double AlarmMinConf = AlarmSweep.MinConf;

    /// <summary>CUSUM drift grid k: detect a sustained 1.5x, 2x or 3x over the time-of-day expectation.</summary>
    public static readonly double[] AlarmDrifts = AlarmSweep.Drifts;
    private static readonly string[] AlarmDriftLabels = ["ln1.5", "ln2", "ln3"];
    private const int DefaultDriftIndex = AlarmSweep.DefaultDriftIndex;

    /// <summary>Fixed CUSUM thresholds h, beside the per-fold calibrated ones.</summary>
    public static readonly double[] AlarmThresholds = [3, 5, 8, 12];

    /// <summary>Calibration targets: h is set so at most 1 in 20 / 1 in 50 fold-fit control mornings alarm in the window.</summary>
    public static readonly int[] AlarmCalibrationMornings = [20, 50];

    /// <summary>A calibrated h never goes below this (a morning whose statistic barely leaves zero is not an onset).</summary>
    public const double AlarmMinCalibratedThreshold = 0.5;

    public static readonly int[] AlarmRankKs = [3, 5, 10];
    public static readonly double[] AlarmGapLo = [0.02, 0.05];
    public static readonly double[] AlarmGapHi = [0.10, 0.15];

    /// <summary>Fresh news for B3: an item published within this many minutes of t.</summary>
    public const double AlarmFreshNewsMinutes = AlarmSweep.FreshNewsMinutes;

    private enum AlarmSeries { Volume, Trades, Either }

    /// <summary>One alarm rule. Family: cusum (CUSUM alone), rank (rank-k rule), benchmark (B3).</summary>
    private sealed record AlarmConfig(string Name, string Family, AlarmSeries Series, int Drift, double? H, int CalTarget,
        AlarmScoreKind Score, int K, double GapLo, double GapHi, bool Gate, bool NeedConf, string? Pair);

    private sealed class AlarmTally(int folds)
    {
        public readonly double[] Pop = new double[folds], Gappers = new double[folds], Hits = new double[folds], False = new double[folds], Alarms = new double[folds];
        public readonly bool[] Missing = new bool[folds];
        public readonly List<double> Lead20 = [], Lead10 = [], RetAtAlarm = [];
        public readonly List<double>[] Lead20ByFold = Enumerable.Range(0, folds).Select(_ => new List<double>()).ToArray();
    }

    /// <summary>The trade-count time-of-day profile: loaded, or fit on train control days like the volume profile and saved.</summary>
    public async Task<TimeOfDayProfile?> TradeProfileAsync(FileBackedBarStore store, CancellationToken ct)
    {
        var path = s.Paths.TradeProfileFile(false);
        if (File.Exists(path)) return TimeOfDayProfile.Load(path);
        var fitOn = s.Params.Features.TodProfile.FitOn.ToLowerInvariant();
        if (fitOn != "train") throw new InvalidOperationException($"The trade-count profile is fit on train control days only; params name '{fitOn}'.");
        List<(string Symbol, DateOnly DateEt, int AdvDecile)> controls;
        await using (var db = await s.Db.CreateDbContextAsync(ct))
            controls = (await db.GapperStudyUniverseDays.AsNoTracking().Where(r => r.Split == fitOn && r.ControlForSymbol != null)
                .Select(r => new { r.Symbol, r.DateEt, r.AdvDecile }).ToListAsync(ct)).Select(r => (r.Symbol, r.DateEt, r.AdvDecile)).ToList();
        var days = new List<(int, IReadOnlyList<Candle>)>();
        foreach (var c in controls)
        {
            ct.ThrowIfCancellationRequested();
            var bars = store.ReadDay(c.Symbol, c.DateEt);
            if (bars.Count > 0) days.Add((c.AdvDecile, bars));
        }
        if (days.Count == 0) return null;
        var profile = TimeOfDayProfile.BuildTradeCount(days);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        profile.Save(path);
        s.Logger.LogInformation("[{Now}] trade-count profile: fit on {Days} train control days → {File}", GapperStudyServices.Et(s.UtcNow), days.Count, path);
        return profile;
    }

    private static List<AlarmConfig> AlarmConfigs()
    {
        var list = new List<AlarmConfig>();
        string Pct(double v) => (v * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
        string Natural(int k) => $"B3 gainers+news top{k}, below +20%, $50k";
        string Matched(int k, double lo, double hi) => $"B3 gainers+news top{k}, gap [{Pct(lo)},{Pct(hi)}), $50k";
        foreach (var series in new[] { AlarmSeries.Volume, AlarmSeries.Trades, AlarmSeries.Either })
            for (int d = 0; d < AlarmDrifts.Length; d++)
            {
                foreach (var h in AlarmThresholds)
                    list.Add(new($"cusum {series.ToString().ToLowerInvariant()} k={AlarmDriftLabels[d]} h={h.ToString(CultureInfo.InvariantCulture)}", "cusum", series, d, h, -1, default, 0, 0, 0, false, false, Natural(10)));
                for (int c = 0; c < AlarmCalibrationMornings.Length; c++)
                    list.Add(new($"cusum {series.ToString().ToLowerInvariant()} k={AlarmDriftLabels[d]} h=cal 1/{AlarmCalibrationMornings[c]}", "cusum", series, d, null, c, default, 0, 0, 0, false, false, Natural(10)));
            }
        foreach (var score in new[] { AlarmScoreKind.Composite, AlarmScoreKind.CusumVol })
            foreach (var k in AlarmRankKs)
                foreach (var lo in AlarmGapLo)
                    foreach (var hi in AlarmGapHi)
                        foreach (var gate in new[] { false, true })
                            list.Add(new($"rank {(score == AlarmScoreKind.Composite ? "composite" : "cusumVol")} top{k}, gap [{Pct(lo)},{Pct(hi)}), $50k, conf>=0.5{(gate ? ", cusum gate cal 1/20" : "")}",
                                "rank", AlarmSeries.Either, DefaultDriftIndex, null, gate ? 0 : -1, score, k, lo, hi, gate, true, Matched(k, lo, hi)));
        foreach (var k in AlarmRankKs)
        {
            foreach (var lo in AlarmGapLo)
                foreach (var hi in AlarmGapHi)
                    list.Add(new(Matched(k, lo, hi), "benchmark", AlarmSeries.Either, DefaultDriftIndex, null, -1, AlarmScoreKind.GainersNews, k, lo, hi, false, false, null));
            list.Add(new(Natural(k), "benchmark", AlarmSeries.Either, DefaultDriftIndex, null, -1, AlarmScoreKind.GainersNews, k, double.NegativeInfinity, FormedGapPct / 100, false, false, null));
        }
        return list;
    }

    private async Task<(List<string>, int, string?)> AlarmsAsync(ExploreRequest req, CancellationToken ct)
    {
        var p = s.Params;
        var profilePath = s.Paths.ProfileFile(false);
        if (!File.Exists(profilePath)) throw new InvalidOperationException($"{Path.GetFileName(profilePath)} does not exist; run `snapshot train` first so the time-of-day profile is fit.");
        var profile = TimeOfDayProfile.Load(profilePath);
        var store = await s.StoreAsync(ct);
        var tradeProfile = await TradeProfileAsync(store, ct);
        var amendment = s.Amendment;

        var population = (await s.StudyDaysAsync("train", req.From, req.To, ct))
            .Select(u => (U: u, Sampled: !u.IsCandidate && amendment is not null && amendment.IsSampleMember(u.Symbol, u.DateEt)))
            .Where(x => x.U.IsCandidate || x.Sampled).ToList();
        var folds = ExploreFolds.Build(population.Select(x => x.U.DateEt), foldMonths.Initial, foldMonths.Eval);
        if (folds.Count == 0) return (["no train fold has an evaluation window in the requested range"], 0, null);
        var lastFitTo = folds.Max(f => f.FitTo);
        var configs = AlarmConfigs();
        var tallies = configs.Select(_ => new AlarmTally(folds.Count)).ToArray();
        var foldDays = new int[folds.Count];
        // calibration rows: (day, max S per drift for volume, then trades); NaN = no series
        var controls = new List<(DateOnly Day, float[] Vol, float[] Tr)>();
        var calibrated = new Dictionary<int, double?[,,]>(); // fold -> [series 0 vol/1 trades, drift, target]
        var reachEt = GapperStudyParams.ParseEt(p.Labels.Reach20.ByEt);
        int processed = 0;

        foreach (var group in population.GroupBy(x => x.U.DateEt).OrderBy(g => g.Key))
        {
            ct.ThrowIfCancellationRequested();
            var day = group.Key;
            int fold = folds.FindIndex(f => f.InEval(day));
            var rows = fold >= 0 ? group.ToList() : day <= lastFitTo ? group.Where(x => x.Sampled).ToList() : [];
            if (rows.Count == 0) continue;
            var symbols = rows.Select(x => x.U.Symbol).Distinct().ToList();

            Dictionary<string, double> adt;
            var news = new Dictionary<string, List<CatalystObservation>>(StringComparer.Ordinal);
            await using (var db = await s.Db.CreateDbContextAsync(ct))
            {
                adt = await GapperAlarmData.AdtAsync(db, symbols, day, ct);
                if (fold >= 0) news = await GapperAlarmData.NewsAsync(db, symbols, day, ct);
            }

            var computed = new AlarmMorning?[rows.Count];
            Parallel.For(0, rows.Count, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, req.Parallelism), CancellationToken = ct }, i =>
            {
                var (u, sampled) = rows[i];
                double weight = sampled ? amendment!.SampleWeight : 1;
                computed[i] = GapperAlarmData.MorningOf(u, weight, sampled, store.ReadDay(u.Symbol, u.DateEt), adt.TryGetValue(u.Symbol, out var a) ? a : null,
                    fold >= 0 ? news.GetValueOrDefault(u.Symbol) ?? [] : null, profile, tradeProfile, reachEt, p.Labels.Reach20.Threshold);
            });
            var dayRows = computed.Where(r => r is not null).Select(r => r!).ToList();
            foreach (var r in dayRows.Where(r => r.Sampled)) controls.Add((r.Day, r.MaxSVol, r.MaxSTr));

            if (fold >= 0 && dayRows.Count > 0)
            {
                if (!calibrated.TryGetValue(fold, out var h)) calibrated[fold] = h = Calibrate(controls.Where(c => folds[fold].InFit(c.Day)).ToList());
                foldDays[fold]++;
                EvaluateDay(dayRows, configs, tallies, fold, h);
            }
            processed += rows.Count;
            s.Logger.LogInformation("[{Now}] explore alarms: {Day} done ({Rows} rows, {Done} so far)", GapperStudyServices.Et(s.UtcNow), day, dayRows.Count, processed);
        }
        return AlarmReport(configs, tallies, folds, foldDays, calibrated, tradeProfile is not null);
    }

    /// <summary>
    /// Per series, drift and target: the smallest h at which at most 1/N of the fold-fit control mornings
    /// (amendment 01's non-candidate sample) reach h anywhere in the alarm window; null without controls.
    /// </summary>
    private static double?[,,] Calibrate(List<(DateOnly Day, float[] Vol, float[] Tr)> controls)
    {
        var h = new double?[2, AlarmDrifts.Length, AlarmCalibrationMornings.Length];
        for (int series = 0; series < 2; series++)
            for (int d = 0; d < AlarmDrifts.Length; d++)
            {
                var max = controls.Select(c => (double)(series == 0 ? c.Vol[d] : c.Tr[d])).Where(double.IsFinite).OrderByDescending(v => v).ToList();
                if (max.Count == 0) continue;
                for (int c = 0; c < AlarmCalibrationMornings.Length; c++)
                {
                    int m = Math.Min(max.Count - 1, (int)Math.Floor((double)max.Count / AlarmCalibrationMornings[c]));
                    h[series, d, c] = Math.Max(AlarmMinCalibratedThreshold, Math.BitIncrement(max[m]));
                }
            }
        return h;
    }

    private static void EvaluateDay(List<AlarmMorning> rows, List<AlarmConfig> configs, AlarmTally[] tallies, int fold, double?[,,] h)
    {
        int w = rows[0].PmRet.Length, n = rows.Count;

        // Scores per minute; rows sorted by each score, best first (NaN scores left out).
        var scores = AlarmSweep.Scores(rows);

        for (int c = 0; c < configs.Count; c++)
        {
            var cfg = configs[c];
            var tally = tallies[c];
            var alarm = new int[n];
            Array.Fill(alarm, -1);
            if (cfg.Family == "cusum")
            {
                double? hv = cfg.H ?? (cfg.CalTarget >= 0 ? h[0, cfg.Drift, cfg.CalTarget] : null);
                double? ht = cfg.H ?? (cfg.CalTarget >= 0 ? h[1, cfg.Drift, cfg.CalTarget] : null);
                bool usable = cfg.Series switch { AlarmSeries.Volume => hv is not null, AlarmSeries.Trades => ht is not null, _ => hv is not null || ht is not null };
                if (!usable) { tally.Missing[fold] = true; continue; }
                for (int r = 0; r < n; r++)
                {
                    var sv = cfg.Series != AlarmSeries.Trades && hv is not null ? rows[r].SVol[cfg.Drift] : null;
                    var st = cfg.Series != AlarmSeries.Volume && ht is not null ? rows[r].STr[cfg.Drift] : null;
                    for (int i = 0; i < w && alarm[r] < 0; i++)
                        if ((sv is not null && sv[i] >= hv) || (st is not null && st[i] >= ht)) alarm[r] = i;
                }
            }
            else
            {
                double? gv = cfg.Gate ? h[0, DefaultDriftIndex, 0] : null, gt = cfg.Gate ? h[1, DefaultDriftIndex, 0] : null;
                if (cfg.Gate && gv is null && gt is null) { tally.Missing[fold] = true; continue; }
                Func<AlarmMorning, int, bool>? gate = cfg.Gate
                    ? (row, i) => (gv is not null && row.SVol[DefaultDriftIndex] is { } sv && sv[i] >= gv) || (gt is not null && row.STr[DefaultDriftIndex] is { } st && st[i] >= gt)
                    : null;
                alarm = AlarmSweep.RankAlarms(rows, scores.Sorted(cfg.Score), new AlarmRankRule(cfg.Score, cfg.K, cfg.GapLo, cfg.GapHi, cfg.NeedConf), gate);
            }

            for (int r = 0; r < n; r++)
            {
                var row = rows[r];
                tally.Pop[fold] += row.Weight;
                if (row.Forming) tally.Gappers[fold] += row.Weight;
                var o = AlarmSweep.Outcome(row, alarm[r]);
                if (!o.Alarmed) continue;
                tally.Alarms[fold] += row.Weight;
                if (o.False) { tally.False[fold] += row.Weight; continue; }
                if (o.Hit)
                {
                    tally.Hits[fold] += row.Weight;
                    tally.Lead20.Add(o.Lead20);
                    tally.Lead20ByFold[fold].Add(o.Lead20);
                    if (o.Lead10 is { } l10) tally.Lead10.Add(l10);
                    tally.RetAtAlarm.Add(o.PmRetAtAlarm);
                }
            }
        }
    }

    private (List<string>, int, string?) AlarmReport(List<AlarmConfig> configs, AlarmTally[] tallies, List<ExploreFold> folds, int[] foldDays, Dictionary<int, double?[,,]> calibrated, bool hasTradeProfile)
    {
        double? Hit(AlarmTally t, int f) => t.Missing[f] || t.Gappers[f] <= 0 ? null : t.Hits[f] / t.Gappers[f];
        double? Fa(AlarmTally t, int f) => t.Missing[f] || t.Pop[f] <= 0 ? null : 100 * t.False[f] / t.Pop[f];
        double? PerDay(AlarmTally t, int f) => t.Missing[f] || foldDays[f] == 0 ? null : t.Alarms[f] / foldDays[f];
        double? Mean(IEnumerable<double?> v) { var l = v.Where(x => x is { } y && double.IsFinite(y)).Select(x => x!.Value).ToList(); return l.Count == 0 ? null : l.Average(); }
        string Pct(double? v) => v is { } x ? x.ToString("0.0%", CultureInfo.InvariantCulture) : "n/a";
        string Num(double? v, string fmt = "0.0") => v is { } x ? x.ToString(fmt, CultureInfo.InvariantCulture) : "n/a";
        string Lead(List<double> l) => l.Count == 0 ? "n/a" : $"{Num(Quantile(l, 0.5))} [{Num(Quantile(l, 0.25))}, {Num(Quantile(l, 0.75))}] ({l.Count})";
        var byName = configs.Select((c, i) => (c.Name, i)).ToDictionary(x => x.Name, x => x.i, StringComparer.Ordinal);

        var md = new StringBuilder(Header("alarm-time scoring of onset detectors (CUSUM, rank-k rules, gainers + news), 06:00-09:10 ET", folds));
        md.AppendLine("Every population morning (candidates + amendment 01's weighted sample) is swept minute by minute from 04:00 ET (`OnsetTrack`, bars ending at or before each minute). A rule's alarm is its first minute in 06:00-09:10.");
        md.AppendLine("- **hit rate**: weighted share of mornings that reach +20% by 09:45 and were still below +20% at 06:00, alarmed before the first +20% touch;");
        md.AppendLine("- **lead**: minutes from the alarm to the first +20% (median [IQR] (hits), pooled over folds); lead to +10% only where +10% came after the alarm;");
        md.AppendLine("- **FA/100**: alarms on mornings that do not reach +20%, per 100 population mornings (weighted estimate of U(D)); **alarms/day**: weighted alarms per evaluation morning across U(D);");
        md.AppendLine("- **vs B3**: hit-rate and FA/100 change against the paired gainers + news rule (same k and gap band; plain CUSUM rules pair with B3 top10 below +20%).");
        md.AppendLine($"Rank rules rank by weighted rank among the morning's names not yet alarmed and below the rule's gap cap; a sampled name weighs {s.Amendment?.SampleWeight.ToString(CultureInfo.InvariantCulture) ?? "n/a"}. CUSUM: z capped at ln 20, volume pseudocount ADV20 x 1e-5, trade pseudocount 1 print; calibrated h per fold from the fold-fit control mornings' window maximum (floor {AlarmMinCalibratedThreshold}).{(hasTradeProfile ? "" : " No trade-count profile: trade CUSUMs are n/a.")}");
        md.AppendLine();
        md.AppendLine("Calibrated h (volume / trades) per fold:");
        md.AppendLine();
        md.AppendLine("| fold | " + string.Join(" | ", AlarmDriftLabels.SelectMany(d => AlarmCalibrationMornings.Select(c => $"k={d}, 1/{c}"))) + " |");
        md.AppendLine("|---|" + string.Concat(Enumerable.Repeat("---|", AlarmDriftLabels.Length * AlarmCalibrationMornings.Length)));
        for (int f = 0; f < folds.Count; f++)
        {
            var cells = new List<string>();
            for (int d = 0; d < AlarmDrifts.Length; d++)
                for (int c = 0; c < AlarmCalibrationMornings.Length; c++)
                    cells.Add(calibrated.TryGetValue(f, out var h) ? $"{Num(h[0, d, c], "0.00")} / {Num(h[1, d, c], "0.00")}" : "n/a");
            md.AppendLine($"| {f + 1} | {string.Join(" | ", cells)} |");
        }

        var trials = new List<ExploreTrial>();
        var json = new List<object>();
        foreach (var family in new[] { "benchmark", "cusum", "rank" })
        {
            md.AppendLine();
            md.AppendLine($"## {(family == "benchmark" ? "Benchmark B3 (gainers + news)" : family == "cusum" ? "CUSUM alone" : "Rank-k rules")}");
            md.AppendLine();
            md.AppendLine("| rule | hit rate per fold | mean hit rate | mean FA/100 | alarms/day | lead to +20% min | lead to +10% min | pmRet at alarm | vs B3 (hit / FA) |");
            md.AppendLine("|---|---|---|---|---|---|---|---|---|");
            for (int c = 0; c < configs.Count; c++)
            {
                var cfg = configs[c];
                if (cfg.Family != family) continue;
                var t = tallies[c];
                var hits = Enumerable.Range(0, folds.Count).Select(f => Hit(t, f)).ToList();
                var fas = Enumerable.Range(0, folds.Count).Select(f => Fa(t, f)).ToList();
                var perDay = Mean(Enumerable.Range(0, folds.Count).Select(f => PerDay(t, f)));
                double? hit = Mean(hits), fa = Mean(fas);
                string vs = "";
                double? dHit = null, dFa = null;
                if (cfg.Pair is { } pair && byName.TryGetValue(pair, out var pi))
                {
                    var pt = tallies[pi];
                    dHit = hit - Mean(Enumerable.Range(0, folds.Count).Select(f => Hit(pt, f)));
                    dFa = fa - Mean(Enumerable.Range(0, folds.Count).Select(f => Fa(pt, f)));
                    vs = $"{(dHit is { } x ? (x * 100).ToString("+0.0;-0.0", CultureInfo.InvariantCulture) + " pt" : "n/a")} / {(dFa is { } y ? y.ToString("+0.00;-0.00", CultureInfo.InvariantCulture) : "n/a")}";
                }
                var ret = t.RetAtAlarm.Count == 0 ? (double?)null : Quantile(t.RetAtAlarm, 0.5);
                md.AppendLine($"| {cfg.Name} | {string.Join(" / ", hits.Select(Pct))} | {Pct(hit)} | {Num(fa, "0.00")} | {Num(perDay, "0.0")} | {Lead(t.Lead20)} | {Lead(t.Lead10)} | {Pct(ret)} | {vs} |");
                trials.Add(new ExploreTrial
                {
                    Track = "alarms", Config = $"window=06:00-09:10 {cfg.Name}", Metric = "hitRate (Reach20By0945 mornings below +20% at 06:00, alarmed before the first +20%; mean of folds)",
                    FoldValues = hits, Pooled = hit, Rows = (int)Math.Round(t.Pop.Sum()),
                    Note = string.Create(CultureInfo.InvariantCulture,
                        $"fa100={string.Join("/", fas.Select(v => Num(v, "0.00")))} alarmsPerDay={Num(perDay, "0.0")} lead20={Lead(t.Lead20)} lead10={Lead(t.Lead10)} pmRetAtAlarm={Pct(ret)}{(cfg.Pair is null ? "" : $" vsB3[{cfg.Pair}] dHit={Num(dHit, "0.000")} dFa100={Num(dFa, "0.00")}")}"),
                });
                json.Add(new
                {
                    rule = cfg.Name, family, hitByFold = hits, hit, faPer100ByFold = fas, faPer100 = fa, alarmsPerDay = perDay,
                    lead20 = new { median = t.Lead20.Count == 0 ? (double?)null : Quantile(t.Lead20, 0.5), q1 = t.Lead20.Count == 0 ? (double?)null : Quantile(t.Lead20, 0.25), q3 = t.Lead20.Count == 0 ? (double?)null : Quantile(t.Lead20, 0.75), n = t.Lead20.Count },
                    lead20ByFold = t.Lead20ByFold.Select(l => l.Count == 0 ? (double?)null : Quantile(l, 0.5)).ToList(),
                    lead10Median = t.Lead10.Count == 0 ? (double?)null : Quantile(t.Lead10, 0.5), pmRetAtAlarmMedian = ret, pair = cfg.Pair, dHit, dFa,
                });
            }
        }
        md.AppendLine();
        md.AppendLine("Reading: a detector is interesting only where it alarms below +10% at a false-alarm rate a human could triage, and it is a finding only if it beats B3 on hit rate at equal or fewer false alarms in at least 3 of 4 folds (strategy-refined.md §2.2). If no rule beats B3, the honest product is screen plus news (H18).");
        return Write("alarms", md, trials, json);
    }

    /// <summary>Linear-interpolated quantile of an unsorted list.</summary>
    private static double Quantile(List<double> values, double q)
    {
        var v = values.OrderBy(x => x).ToList();
        if (v.Count == 1) return v[0];
        double pos = q * (v.Count - 1);
        int lo = (int)Math.Floor(pos);
        int hi = Math.Min(v.Count - 1, lo + 1);
        return v[lo] + (v[hi] - v[lo]) * (pos - lo);
    }
}
