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
    public static readonly TimeSpan AlarmWindowStartEt = new(6, 0, 0);
    public static readonly TimeSpan AlarmWindowEndEt = new(9, 10, 0);

    /// <summary>The protocol's premarket dollar-volume floor at the alarm minute.</summary>
    public const double AlarmMinPmDollarVolume = 50_000;

    /// <summary>conf(t) floor of the rank rules: most of the gap has actually traded through.</summary>
    public const double AlarmMinConf = 0.5;

    /// <summary>CUSUM drift grid k: detect a sustained 1.5x, 2x or 3x over the time-of-day expectation.</summary>
    public static readonly double[] AlarmDrifts = [Math.Log(1.5), Math.Log(2), Math.Log(3)];
    private static readonly string[] AlarmDriftLabels = ["ln1.5", "ln2", "ln3"];
    private const int DefaultDriftIndex = 1;

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
    public const double AlarmFreshNewsMinutes = 60;

    private enum AlarmSeries { Volume, Trades, Either }
    private enum AlarmScore { Composite, CusumVol, GainersNews }

    /// <summary>One alarm rule. Family: cusum (CUSUM alone), rank (rank-k rule), benchmark (B3).</summary>
    private sealed record AlarmConfig(string Name, string Family, AlarmSeries Series, int Drift, double? H, int CalTarget,
        AlarmScore Score, int K, double GapLo, double GapHi, bool Gate, bool NeedConf, string? Pair);

    /// <summary>One population morning, the window minutes only (index 0 = the bar ending 06:00).</summary>
    private sealed class AlarmRow
    {
        public required DateOnly Day { get; init; }
        public required double Weight { get; init; }
        public required bool Sampled { get; init; }
        public required bool Reach20 { get; init; }
        /// <summary>Window-relative minute of the first +20% / +10% touch (bar high), int.MaxValue when none by the reach cut-off.</summary>
        public required int First20 { get; init; }
        public required int First10 { get; init; }
        public required float[]?[] SVol { get; init; }
        public required float[]?[] STr { get; init; }
        public required float[] MaxSVol { get; init; }
        public required float[] MaxSTr { get; init; }
        public required float[] PmRet { get; init; }
        public required float[] Conf { get; init; }
        public required float[] CumDollar { get; init; }
        public required float[] VolRatio { get; init; }
        public bool[]? News { get; init; }
    }

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
        foreach (var score in new[] { AlarmScore.Composite, AlarmScore.CusumVol })
            foreach (var k in AlarmRankKs)
                foreach (var lo in AlarmGapLo)
                    foreach (var hi in AlarmGapHi)
                        foreach (var gate in new[] { false, true })
                            list.Add(new($"rank {(score == AlarmScore.Composite ? "composite" : "cusumVol")} top{k}, gap [{Pct(lo)},{Pct(hi)}), $50k, conf>=0.5{(gate ? ", cusum gate cal 1/20" : "")}",
                                "rank", AlarmSeries.Either, DefaultDriftIndex, null, gate ? 0 : -1, score, k, lo, hi, gate, true, Matched(k, lo, hi)));
        foreach (var k in AlarmRankKs)
        {
            foreach (var lo in AlarmGapLo)
                foreach (var hi in AlarmGapHi)
                    list.Add(new(Matched(k, lo, hi), "benchmark", AlarmSeries.Either, DefaultDriftIndex, null, -1, AlarmScore.GainersNews, k, lo, hi, false, false, null));
            list.Add(new(Natural(k), "benchmark", AlarmSeries.Either, DefaultDriftIndex, null, -1, AlarmScore.GainersNews, k, double.NegativeInfinity, FormedGapPct / 100, false, false, null));
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
                var from = day.AddDays(-40);
                adt = (await db.GapperStudyDailyBars.AsNoTracking()
                        .Where(b => symbols.Contains(b.Symbol) && b.DateEt >= from && b.DateEt < day && b.TradeCount > 0)
                        .Select(b => new { b.Symbol, b.DateEt, b.TradeCount }).ToListAsync(ct))
                    .GroupBy(b => b.Symbol).ToDictionary(g => g.Key, g => g.OrderBy(b => b.DateEt).TakeLast(20).Average(b => (double)b.TradeCount), StringComparer.Ordinal);
                if (fold >= 0)
                {
                    var cFrom = MinuteBarMath.ToUtc(day, new TimeSpan(2, 0, 0));
                    var cTo = MinuteBarMath.ToUtc(day, AlarmWindowEndEt);
                    var items = await (from x in db.CatalystEventSymbols.AsNoTracking()
                                       where symbols.Contains(x.Symbol)
                                       join e in db.CatalystEvents.AsNoTracking() on x.EventId equals e.Id
                                       where e.PublishedUtc >= cFrom && e.PublishedUtc <= cTo
                                       select new { x.Symbol, e.PublishedUtc, e.PreClass, e.Direction, e.SourceTier, e.CatalystScore, e.StampKind }).ToListAsync(ct);
                    foreach (var it in items)
                    {
                        if (!news.TryGetValue(it.Symbol, out var l)) news[it.Symbol] = l = [];
                        l.Add(new CatalystObservation(it.PublishedUtc, it.PreClass, it.Direction, it.SourceTier, it.CatalystScore, CatalystStampKind.IsUpperBound(it.StampKind)));
                    }
                }
            }

            var computed = new AlarmRow?[rows.Count];
            Parallel.For(0, rows.Count, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, req.Parallelism), CancellationToken = ct }, i =>
            {
                var (u, sampled) = rows[i];
                double weight = sampled ? amendment!.SampleWeight : 1;
                computed[i] = AlarmRowOf(u, weight, sampled, store.ReadDay(u.Symbol, u.DateEt), adt.TryGetValue(u.Symbol, out var a) ? a : null,
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

    private static AlarmRow? AlarmRowOf(GapperStudyUniverseDay u, double weight, bool sampled, IReadOnlyList<Candle> bars, double? adt, List<CatalystObservation>? news,
        TimeOfDayProfile profile, TimeOfDayProfile? tradeProfile, TimeSpan reachEt, double reachThreshold)
    {
        if (u.AdjPrevClose <= 0 || bars.Count == 0) return null;
        var day = u.DateEt;
        var start = MinuteBarMath.ToUtc(day, MinuteBarMath.SessionStartEt);
        var reachBy = MinuteBarMath.ToUtc(day, reachEt);
        int i0 = OnsetSeries.IndexOf(AlarmWindowStartEt), i1 = OnsetSeries.IndexOf(AlarmWindowEndEt), w = i1 - i0 + 1;

        // First touches, as the labeler defines Reach20By0945 (bar highs of bars ending by the cut-off).
        bool any = false;
        int first20 = int.MaxValue, first10 = int.MaxValue;
        double prev = (double)u.AdjPrevClose;
        foreach (var b in bars)
        {
            if (b.StartUtc < start || b.EndUtc > reachBy) continue;
            any = true;
            int idx = (int)Math.Round((b.EndUtc - start).TotalMinutes) - 1 - i0;
            double high = (double)b.High;
            if (high / prev - 1 >= reachThreshold && idx < first20) first20 = idx;
            if (high / prev - 1 >= 0.10 && idx < first10) first10 = idx;
        }
        if (!any) return null;

        var series = OnsetTrack.Compute(new GapperFeatureInputs
        {
            Symbol = u.Symbol, DateEt = day, DecisionUtc = MinuteBarMath.ToUtc(day, AlarmWindowEndEt), MinuteBars = bars, PriorClose = u.AdjPrevClose,
            Profile = profile, AdvDecile = u.AdvDecile, Adv20Shares = (double)u.Adv20Shares, Adt20Trades = adt, TradeProfile = tradeProfile,
        });
        if (series.Minutes <= i1) return null;
        float[] Window(double[] v) { var r = new float[w]; for (int i = 0; i < w; i++) r[i] = (float)v[i0 + i]; return r; }
        var sVol = new float[]?[AlarmDrifts.Length];
        var sTr = new float[]?[AlarmDrifts.Length];
        var maxVol = new float[AlarmDrifts.Length];
        var maxTr = new float[AlarmDrifts.Length];
        for (int d = 0; d < AlarmDrifts.Length; d++)
        {
            var pv = series.CusumVolume(AlarmDrifts[d]);
            var pt = series.CusumTrades(AlarmDrifts[d]);
            sVol[d] = pv is null ? null : Window(pv);
            sTr[d] = pt is null ? null : Window(pt);
            maxVol[d] = sVol[d] is { } a ? a.Max() : float.NaN;
            maxTr[d] = sTr[d] is { } b ? b.Max() : float.NaN;
        }
        bool[]? fresh = null;
        if (news is not null)
        {
            fresh = new bool[w];
            if (news.Count > 0)
                for (int i = 0; i < w; i++)
                {
                    var t = start.AddMinutes(OnsetSeries.EndMinute(i0 + i));
                    fresh[i] = GapperFormationFeatures.LatestCatalyst(news, t) is { } c && (t - c.PublishedUtc).TotalMinutes <= AlarmFreshNewsMinutes;
                }
        }
        return new AlarmRow
        {
            Day = day, Weight = weight, Sampled = sampled, Reach20 = first20 != int.MaxValue, First20 = first20, First10 = first10,
            SVol = sVol, STr = sTr, MaxSVol = maxVol, MaxSTr = maxTr,
            PmRet = Window(series.PmRet), Conf = Window(series.Conf), CumDollar = Window(series.CumDollar), VolRatio = Window(series.VolRatioTod), News = fresh,
        };
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

    private static void EvaluateDay(List<AlarmRow> rows, List<AlarmConfig> configs, AlarmTally[] tallies, int fold, double?[,,] h)
    {
        int w = rows[0].PmRet.Length, n = rows.Count;
        var weights = rows.Select(r => r.Weight).ToList();

        // Scores per minute; rows sorted by each score, best first (NaN scores left out).
        var scores = new Dictionary<AlarmScore, double[][]>
        {
            [AlarmScore.Composite] = new double[w][], [AlarmScore.CusumVol] = new double[w][], [AlarmScore.GainersNews] = new double[w][],
        };
        var sorted = new Dictionary<AlarmScore, int[][]> { [AlarmScore.Composite] = new int[w][], [AlarmScore.CusumVol] = new int[w][], [AlarmScore.GainersNews] = new int[w][] };
        for (int i = 0; i < w; i++)
        {
            var volRank = CrossSectionalRank.PercentileRanks(rows.Select(r => (double)r.VolRatio[i]).ToList(), weights);
            var retRank = CrossSectionalRank.PercentileRanks(rows.Select(r => (double)r.PmRet[i]).ToList(), weights);
            var cusum = rows.Select(r => r.SVol[DefaultDriftIndex] is { } s ? (double)s[i] : double.NaN).ToList();
            var cusumRank = CrossSectionalRank.PercentileRanks(cusum, weights);
            var comp = new double[n]; var gn = new double[n];
            for (int r = 0; r < n; r++)
            {
                double sum = 0; int cnt = 0;
                foreach (var v in new[] { volRank[r], retRank[r], cusumRank[r] }) if (double.IsFinite(v)) { sum += v; cnt++; }
                comp[r] = cnt > 0 ? sum / cnt : double.NaN;
                gn[r] = float.IsNaN(rows[r].PmRet[i]) ? double.NaN : (rows[r].News?[i] == true ? 1e6 : 0) + rows[r].PmRet[i];
            }
            scores[AlarmScore.Composite][i] = comp;
            scores[AlarmScore.CusumVol][i] = cusum.ToArray();
            scores[AlarmScore.GainersNews][i] = gn;
            foreach (var (key, v) in scores)
                sorted[key][i] = Enumerable.Range(0, n).Where(r => double.IsFinite(v[i][r])).OrderByDescending(r => v[i][r]).ThenBy(r => r).ToArray();
        }

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
                for (int i = 0; i < w; i++)
                {
                    double cum = 0;
                    foreach (var r in sorted[cfg.Score][i])
                    {
                        var row = rows[r];
                        // Ranked among the morning's names not yet alarmed and still forming (below the rule's gap cap).
                        if (alarm[r] >= 0 || !(row.PmRet[i] < cfg.GapHi)) continue;
                        if (1 + cum > cfg.K) break;
                        bool pass = row.PmRet[i] >= cfg.GapLo && row.CumDollar[i] >= AlarmMinPmDollarVolume
                            && (!cfg.NeedConf || row.Conf[i] >= AlarmMinConf)
                            && (!cfg.Gate || (gv is not null && row.SVol[DefaultDriftIndex] is { } sv && sv[i] >= gv) || (gt is not null && row.STr[DefaultDriftIndex] is { } st && st[i] >= gt));
                        cum += row.Weight;
                        if (pass) alarm[r] = i;
                    }
                }
            }

            for (int r = 0; r < n; r++)
            {
                var row = rows[r];
                tally.Pop[fold] += row.Weight;
                bool forming = row.Reach20 && row.First20 > 0;
                if (forming) tally.Gappers[fold] += row.Weight;
                int a = alarm[r];
                if (a < 0) continue;
                tally.Alarms[fold] += row.Weight;
                if (!row.Reach20) { tally.False[fold] += row.Weight; continue; }
                if (forming && a < row.First20)
                {
                    tally.Hits[fold] += row.Weight;
                    tally.Lead20.Add(row.First20 - a);
                    tally.Lead20ByFold[fold].Add(row.First20 - a);
                    if (row.First10 > a && row.First10 != int.MaxValue) tally.Lead10.Add(row.First10 - a);
                    tally.RetAtAlarm.Add(row.PmRet[a]);
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
