using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using IdiotProof.Evidence.Evaluation;
using IdiotProof.Evidence.Gappers;

namespace IdiotProof.Core.Research.Gappers.Explore;

public sealed record ExploreRequest(string Command, string Split, IReadOnlyList<string> Times, bool Force, DateOnly? From = null, DateOnly? To = null, int Parallelism = 4,
    int InitialFitMonths = 12, int EvalMonths = 6, Guid? Owner = null);

public sealed record ExploreResult(string Command, string? ReportFile, int Trials, int LedgerTotal, IReadOnlyList<string> Lines, double Minutes);

/// <summary>
/// <c>research gappers explore</c> (IP-T453, docs/research/gapper-study-2026-11/explore-plan.md): the
/// exploratory round, on TRAIN ONLY. <c>build</c> computes the panel cache at any decision times in
/// memory with the shared feature code and the frozen labeler (nothing is written to SQL);
/// <c>visibility</c>, <c>models</c> and <c>exits</c> are the first tracks, each scored on the four
/// rolling folds inside train and logged to the trial ledger. Every other split is refused here, so
/// no validation or holdout label can reach a search.
/// </summary>
public sealed partial class GapperExploreStage(GapperStudyServices s, GapperSnapshotStage snapshots)
{
    public const string Target = "Reach20By0945";
    /// <summary>A morning already up this much at t has formed; visibility is scored on mornings still below it.</summary>
    public const double FormedGapPct = 20;

    public static readonly string[] Commands = ["build", "visibility", "models", "exits", "news", "fade", "entries", "corobs", "ssr", "float-import", "tradeflow-pull", "tradeflow", "regsho-pull", "alarms", "exit-routes", "fade-short", "fade-short-deep", "alarm-ride", "watchlist", "split-days-pull", "split-days", "orb-pull", "orb", "catalyst-split"];

    /// <summary>Feature blocks for the ablation track: a block is removed by name prefix.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> Blocks = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["volume vs time of day"] = ["VolRatioTod", "OwnProfileRatio", "LogPmCumVol", "MinsSinceFirstAbnormal", "VolumeRoc"],
        ["velocity"] = ["Velocity1m", "Velocity3m", "Velocity5m", "Velocity15m", "Acceleration"],
        ["price level"] = ["GapPct", "DistToPmHighPct"],
        ["prior days"] = ["AbnormalReturnD", "VolumeRatioD", "x.priorDayTopMoverRank"],
        ["corporate actions"] = ["DaysSinceReverseSplit", "DaysSince424B", "DaysSinceS1"],
        ["catalyst"] = ["Catalyst", "Pre.", "x.promoSourceShare", "x.llm", "x.form4", "x.8k"],
        ["microstructure"] = ["TradesProxy", "SpreadProxy", "x.tradesRoc", "x.csSpreadPrior", "x.leeMykland"],
        ["sympathy"] = ["x.sympathy"],
        ["halts"] = ["x.halt"],
        ["short-sale constraints"] = ["x.ssr", "x.si"],
        ["reg sho and short volume"] = ["x.regSho", "x.shortVol"],
        ["float proxy"] = ["x.float"],
        ["dilution filings"] = ["x.dil"],
        ["onset cusum"] = ["x.cusum", "x.minsSinceCusumAlarm", "x.rankCusumVol"],
        ["cross-sectional rank"] = ["x.pmVolRatioMkt", "x.rankPmVolRatio", "x.rankPmRet"],
        ["volume-confirmed gap"] = ["x.pmVwapRet", "x.conf", "x.pmDollarShareAbove10"],
    };

    public string CacheDir() => Environment.GetEnvironmentVariable("GAPPER_EXPLORE_DIR") is { Length: > 0 } dir
        ? dir
        : Path.Combine(Path.GetDirectoryName(s.Params.DataTiers.MinuteBarRoot().TrimEnd('\\', '/'))!, "explore");

    public string ReportDir => Path.Combine(s.Paths.RunsDir, "explore");

    public ExploreLedger Ledger => new(Path.Combine(ReportDir, "ledger.jsonl"));

    private (int Initial, int Eval) foldMonths = (12, 6);

    private List<ExploreFold> Folds(ExploreTable t) => ExploreFolds.Build(t.Days, foldMonths.Initial, foldMonths.Eval);

    public async Task<ExploreResult> RunAsync(ExploreRequest req, CancellationToken ct)
    {
        foldMonths = (req.InitialFitMonths, req.EvalMonths);
        if (!string.Equals(req.Split, "train", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Explore refuses split '{req.Split}': the exploratory round runs on train only (explore-plan.md).");
        var sw = Stopwatch.StartNew();
        var times = req.Times.Count > 0 ? req.Times : s.Params.DecisionTimesEt;
        var command = req.Command.ToLowerInvariant();
        var (lines, trials, file) = command switch
        {
            "build" => await BuildAsync(times, req, ct),
            "visibility" => Visibility(times),
            "models" => Models(times),
            "exits" => Exits(times),
            "news" => News(times),
            "fade" => Fade(times),
            "entries" => await EntriesAsync(times, ct),
            "corobs" => Corobs(times),
            "ssr" => await SsrAsync(times, ct),
            "float-import" => await FloatImportAsync(ct),
            "tradeflow-pull" => await TradeFlowPullAsync(req, ct),
            "tradeflow" => await TradeFlowAsync(req, ct),
            "regsho-pull" => await RegShoPullAsync(req, ct),
            "exit-routes" => await ExitRoutesAsync(times, req, ct),
            "alarms" => await AlarmsAsync(req, ct),
            "fade-short" => await FadeShortAsync(times, ct),
            "fade-short-deep" => await FadeShortDeepAsync(ct),
            "alarm-ride" => await AlarmRideAsync(req, ct),
            "watchlist" => await WatchlistAsync(req, ct),
            "split-days-pull" => await SplitDaysPullAsync(req, ct),
            "split-days" => await SplitDaysAsync(req, ct),
            "orb-pull" => await OrbPullAsync(req, ct),
            "orb" => await OrbAsync(req, ct),
            "catalyst-split" => await CatalystSplitAsync(req, ct),
            _ => throw new ArgumentException($"Unknown explore command '{req.Command}'. Use {string.Join(", ", Commands)}."),
        };
        return new ExploreResult(command, file, trials, Ledger.Count, lines, Math.Round(sw.Elapsed.TotalMinutes, 2));
    }

    // ── build ─────────────────────────────────────────────────────────────

    private async Task<(List<string>, int, string?)> BuildAsync(IReadOnlyList<string> labels, ExploreRequest req, CancellationToken ct)
    {
        var p = s.Params;
        var dir = CacheDir();
        var all = p.DecisionTimes().ToDictionary(t => t.Label, StringComparer.Ordinal);
        var times = labels.Select(l => all.TryGetValue(l, out var t) ? t : DecisionTime.Parse(l)).Where(t => req.Force || !ExplorePanelFile.Exists(dir, t.Label)).ToList();
        var lines = new List<string>();
        if (times.Count == 0)
        {
            lines.Add($"every requested decision time is already cached in {dir} (pass --force to rebuild)");
            return (lines, 0, null);
        }
        var profilePath = s.Paths.ProfileFile(false);
        if (!File.Exists(profilePath)) throw new InvalidOperationException($"{Path.GetFileName(profilePath)} does not exist; run `snapshot train` first so the time-of-day profile is fit.");
        var profile = TimeOfDayProfile.Load(profilePath);
        var store = await s.StoreAsync(ct);
        var amendment = s.Amendment;
        // The frozen registry plus the post-registration onset block (IP-T470); the snapshot stage itself never adds it.
        var registry = GapperSnapshotStage.Registry(p).AddOnset(await TradeProfileAsync(store, ct));

        var days = (await s.StudyDaysAsync("train", req.From, req.To, ct))
            .Select(u => (U: u, Sampled: !u.IsCandidate && amendment is not null && amendment.IsSampleMember(u.Symbol, u.DateEt)))
            .Where(x => x.U.IsCandidate || x.Sampled).ToList();
        var writers = times.ToDictionary(t => t.Label, t => new ExplorePanelFile.Writer(dir, t.Label), StringComparer.Ordinal);
        int done = 0;
        try
        {
            var groups = days.GroupBy(d => d.U.DateEt).OrderBy(g => g.Key).ToList();
            await Parallel.ForEachAsync(groups, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, req.Parallelism), CancellationToken = ct }, async (group, token) =>
            {
                Dictionary<string, int> topMovers;
                await using (var db = await s.Db.CreateDbContextAsync(token))
                    topMovers = await GapperSnapshotStage.TopMoverRanksAsync(db, group.Key, "train", token);
                foreach (var (u, sampled) in group)
                {
                    var day = await snapshots.ComputeDayAsync(u, times, store, profile, topMovers, registry, token);
                    var dayLabels = GapperStudyLabeler.ComputeDay(p, day.DayBars, u.AdjPrevClose, u.DateEt, profile, u.AdvDecile, (double)u.Adv20Shares);
                    double weight = sampled ? amendment!.SampleWeight : 1;
                    foreach (var (dt, snap) in day.Snapshots)
                    {
                        var fwd = GapperStudyLabeler.ComputeForward(p, day.DayBars, u.DateEt, snap.DecisionUtc);
                        writers[dt.Label].Add(u.Symbol, u.DateEt, u.IsCandidate, weight, ExploreColumns.Features(snap), ExploreColumns.Labels(dayLabels, fwd, u.IsGapper, u.GapTier));
                    }
                }
                int now = Interlocked.Add(ref done, group.Count());
                s.Logger.LogInformation("[{Now}] explore build: {Day} done, {Done}/{Total} symbol-days", GapperStudyServices.Et(s.UtcNow), group.Key, now, days.Count);
            });
            foreach (var w in writers.Values) w.Finish(s.UtcNow);
        }
        finally
        {
            foreach (var w in writers.Values) w.Dispose();
        }
        lines.Add($"built {times.Count} decision times over {days.Count:N0} train population symbol-days ({days.Count(d => d.U.IsCandidate):N0} candidates, {days.Count(d => d.Sampled):N0} sample) into {dir}");
        return (lines, 0, null);
    }

    // ── shared scoring ────────────────────────────────────────────────────

    private ExploreTable Load(string decision)
    {
        var dir = CacheDir();
        if (!ExplorePanelFile.Exists(dir, decision)) throw new InvalidOperationException($"No explore panel for {decision} in {dir}; run `explore build --times \"{decision}\"` first.");
        var t = AddDilution(AddFloat(AddRegSho(AddShortSale(ExploreDerived.AddCrossSection(ExploreDerived.AddSympathy(ExplorePanelFile.Read(dir, decision)))))), decision);
        halts ??= HaltHistory.Load(HaltsDir());
        if (halts is null) return t;
        var dt = DecisionTime.Parse(decision);
        return ExploreDerived.AddHalts(t, halts, dt.DayOffset == 0 ? dt.TimeEt : null);
    }

    private HaltHistory? halts;

    /// <summary>The Nasdaq Trader halt history day files (IP-T457); GAPPER_HALTS_DIR overrides, default D:\IdiotProofData\halts beside the study folder.</summary>
    public string HaltsDir() => Environment.GetEnvironmentVariable("GAPPER_HALTS_DIR") is { Length: > 0 } dir
        ? dir
        : Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(CacheDir().TrimEnd('\\', '/'))!)!, "halts");

    /// <summary>
    /// Rows with a defined target, optionally only mornings still forming at t. On a same-day decision a morning has
    /// formed once its high so far reached the formed level, not only its last close: Reach20By0945 counts a touch
    /// before t too, so a name that spiked past +20% and pulled back was a labelled positive the panel could read off
    /// DistToPmHighPct (IP-T474; it drove late-morning AUC to 1.000). The high so far is recovered from the close-based
    /// gap and the distance to the session high, both from bars ending by t.
    /// </summary>
    public static List<int> Scorable(ExploreTable t, bool formingOnly, bool sameDay = true)
    {
        var y = t.Binary(Target);
        int gap = t.Feature("GapPct");
        int dist = t.Feature("DistToPmHighPct");
        var rows = new List<int>();
        for (int r = 0; r < t.Rows; r++)
        {
            if (y[r] is null || t.Weights[r] <= 0) continue;
            if (formingOnly && gap >= 0 && t.Value(r, gap) is var g && !float.IsNaN(g))
            {
                if (g >= FormedGapPct) continue;
                if (sameDay && HighSoFarGapPct(g, dist >= 0 ? t.Value(r, dist) : float.NaN) >= FormedGapPct) continue;
            }
            rows.Add(r);
        }
        return rows;
    }

    /// <summary>Gap of the session high so far over the prior close (%), from the close gap and the % distance below that high.</summary>
    public static double HighSoFarGapPct(double gapPct, double distToHighPct)
        => double.IsNaN(distToHighPct) || distToHighPct >= 100 ? gapPct : ((1 + gapPct / 100) / (1 - distToHighPct / 100) - 1) * 100;

    public delegate double[] Learner(ExploreTable t, int[] featureIdx, List<int> fit, List<int> eval);

    public static double[] Logistic(ExploreTable t, int[] featureIdx, List<int> fit, List<int> eval)
    {
        double[] Row(int r) => featureIdx.Select(f => (double)t.Value(r, f)).ToArray();
        var y = t.Binary(Target);
        var names = featureIdx.Select(f => t.FeatureNames[f]).ToList();
        var model = LogisticRegression.FitWeighted(names, fit.Select(Row).ToList(), fit.Select(r => y[r]!.Value).ToList(), fit.Select(r => t.Weights[r]).ToList());
        return eval.Select(r => model.Predict(Row(r))).ToArray();
    }

    public static double[] Boosted(ExploreTable t, int[] featureIdx, List<int> fit, List<int> eval) => BoostedWith(t, featureIdx, fit, eval, null, out _);

    public static double[] BoostedWith(ExploreTable t, int[] featureIdx, List<int> fit, List<int> eval, BoostingOptions? options, out BoostedTreeModel model)
    {
        var y = t.Binary(Target);
        int d = featureIdx.Length;
        var x = new float[fit.Count * d];
        for (int i = 0; i < fit.Count; i++)
            for (int j = 0; j < d; j++) x[i * d + j] = t.Value(fit[i], featureIdx[j]);
        model = GradientBoostedTrees.Fit(featureIdx.Select(f => t.FeatureNames[f]).ToList(), x, fit.Select(r => y[r]!.Value).ToArray(), fit.Select(r => t.Weights[r]).ToArray(), options);
        var row = new float[d];
        var result = new double[eval.Count];
        for (int i = 0; i < eval.Count; i++)
        {
            for (int j = 0; j < d; j++) row[j] = t.Value(eval[i], featureIdx[j]);
            result[i] = model.Predict(row);
        }
        return result;
    }

    public static double[] CorobsLearner(ExploreTable t, int[] featureIdx, List<int> fit, List<int> eval, int k)
    {
        double[] Row(int r) => featureIdx.Select(f => (double)t.Value(r, f)).ToArray();
        var y = t.Binary(Target);
        var model = CorobsLookAlike.Fit(featureIdx.Select(f => t.FeatureNames[f]).ToList(), fit.Select(Row).ToList(), fit.Select(r => y[r]!.Value).ToList(),
            fit.Select(r => t.Weights[r]).ToList(), fit.Select(r => t.Days[r].DayNumber).ToList(), seed: 1337, k: k);
        return model.Score(eval.Select(Row).ToList()).Select(c => c.Score).ToArray();
    }

    public static double[] Benchmark(ExploreTable t, List<int> eval)
    {
        int age = t.Feature("CatalystAgeMinutes");
        return eval.Select(r => FreshNews(t, age, r) ? 1.0 : 0.0).ToArray();
    }

    private static bool FreshNews(ExploreTable t, int age, int r) => age >= 0 && t.Value(r, age) is var a && !float.IsNaN(a) && a <= 60;

    /// <summary>The top-gainers screen: rank by the gap so far at t (methods-prediction.md: H11's yes/no news benchmark is easy to beat).</summary>
    public static double[] ScreenBenchmark(ExploreTable t, List<int> eval)
    {
        int gap = t.Feature("GapPct");
        return eval.Select(r => gap >= 0 && t.Value(r, gap) is var g && !float.IsNaN(g) ? g : double.MinValue).ToArray();
    }

    /// <summary>Gainers plus news: names with news in the last 60 minutes first, each group ranked by the gap so far. Finalists must beat this.</summary>
    public static double[] ScreenNewsBenchmark(ExploreTable t, List<int> eval)
    {
        int age = t.Feature("CatalystAgeMinutes");
        var screen = ScreenBenchmark(t, eval);
        return eval.Select((r, i) => (FreshNews(t, age, r) ? 1e6 : 0) + (screen[i] == double.MinValue ? -1e5 : Math.Clamp(screen[i], -1e5 + 1, 1e5))).ToArray();
    }

    /// <summary>Mornings ranked per day for precision@k: the top of a live scanner is a handful of names.</summary>
    public const int PrecisionK = 10;

    /// <summary>
    /// One scorer over the rolling folds (methods-prediction.md, IP-T465). <see cref="Folds"/>: weighted AUC
    /// per fold. <see cref="Pooled"/>: the MEAN of the fold AUCs (AUC over concatenated scores from four
    /// different fold models rewards ranking busy days over picking stocks, so it is not used).
    /// <see cref="DayAuc"/>: weighted AUC inside each eval morning that has a gapper and a non-gapper,
    /// averaged over those mornings. <see cref="PrecisionAtK"/>: weighted gapper share among each morning's
    /// top <see cref="PrecisionK"/> scores, averaged over eval mornings; <see cref="PrecisionCeiling"/> is the
    /// same with a perfect scorer, since a morning with three gappers can never score above 0.3.
    /// </summary>
    public sealed record FoldScore(double?[] Folds, double? Pooled, int Rows, double? DayAuc, double? PrecisionAtK, double? PrecisionCeiling);

    /// <summary>Score every fold (fit on its fit window, scored on its eval window); see <see cref="FoldScore"/>.</summary>
    public static FoldScore ScoreFolds(ExploreTable t, List<int> rows, IReadOnlyList<ExploreFold> folds, Func<List<int>, List<int>, double[]> score)
    {
        var y = t.Binary(Target);
        var perFold = new double?[folds.Count];
        var dayAucs = new List<double>(); var precisions = new List<double>(); var ceilings = new List<double>();
        int scored = 0;
        for (int k = 0; k < folds.Count; k++)
        {
            var fit = rows.Where(r => folds[k].InFit(t.Days[r])).ToList();
            var eval = rows.Where(r => folds[k].InEval(t.Days[r])).ToList();
            var labels = eval.Select(r => y[r]!.Value).ToList();
            if (fit.Count == 0 || eval.Count == 0 || !fit.Any(r => y[r] == true) || labels.All(v => v) || labels.All(v => !v)) continue;
            var scores = score(fit, eval);
            var weights = eval.Select(r => t.Weights[r]).ToList();
            perFold[k] = WeightedAuc.Compute(scores, labels, weights);
            scored += eval.Count;
            foreach (var day in Enumerable.Range(0, eval.Count).GroupBy(i => t.Days[eval[i]]))
            {
                var idx = day.ToList();
                var dl = idx.Select(i => labels[i]).ToList();
                if (dl.Any(v => v) && dl.Any(v => !v))
                    dayAucs.Add(WeightedAuc.Compute(idx.Select(i => scores[i]).ToList(), dl, idx.Select(i => weights[i]).ToList()));
                precisions.Add(TopShare(idx.OrderByDescending(i => scores[i]).ThenBy(i => eval[i]).Select(i => (labels[i], weights[i]))));
                ceilings.Add(TopShare(idx.OrderByDescending(i => labels[i]).ThenByDescending(i => weights[i]).Select(i => (labels[i], weights[i]))));
            }
        }
        double? Mean(IEnumerable<double> v) { var l = v.Where(double.IsFinite).ToList(); return l.Count == 0 ? null : l.Average(); }
        return new FoldScore(perFold, Mean(perFold.Where(v => v.HasValue).Select(v => v!.Value)), scored, Mean(dayAucs), Mean(precisions), Mean(ceilings));
    }

    private static double TopShare(IEnumerable<(bool Label, double Weight)> ranked)
    {
        double w = 0, hit = 0;
        foreach (var (label, weight) in ranked.Take(PrecisionK)) { w += weight; hit += label ? weight : 0; }
        return w > 0 ? hit / w : double.NaN;
    }

    private static int[] AllFeatures(ExploreTable t) => Enumerable.Range(0, t.FeatureCount).ToArray();

    private static string Cell(FoldScore r) => $"{F(r.Pooled)} / {F(r.DayAuc)} / {F(r.PrecisionAtK)}";

    /// <summary>A ledger line for one scorer: fold AUCs, their mean, and the within-morning metrics in the note.</summary>
    public static ExploreTrial Trial(string track, string config, FoldScore r) => new()
    {
        Track = track, Config = config, Metric = "weightedAuc (mean of folds)", FoldValues = r.Folds, Pooled = r.Pooled, Rows = r.Rows,
        Note = string.Create(CultureInfo.InvariantCulture, $"dayAuc={F(r.DayAuc)} p@{PrecisionK}={F(r.PrecisionAtK)} ceiling={F(r.PrecisionCeiling)}"),
    };

    private static string F(double? v) => v is { } x && !double.IsNaN(x) ? x.ToString("0.000", CultureInfo.InvariantCulture) : "n/a";

    private (List<string>, int, string?) Write(string track, StringBuilder md, List<ExploreTrial> trials, object json)
    {
        // A catalyst-split capture re-runs a command for its trades only: no ledger line, no report file (IP-T492).
        if (capture is not null) return ([], 0, null);
        Directory.CreateDirectory(ReportDir);
        var stamp = s.UtcNow.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture);
        var numbered = Ledger.Append(trials, s.UtcNow);
        var file = Path.Combine(ReportDir, $"{track}-{stamp}.md");
        md.AppendLine();
        md.AppendLine($"Trials logged: {numbered.Count} (ids {(numbered.Count > 0 ? $"{numbered[0].Id}..{numbered[^1].Id}" : "none")}); ledger total {Ledger.Count}.");
        File.WriteAllText(file, md.ToString());
        File.WriteAllText(Path.ChangeExtension(file, ".json"), JsonSerializer.Serialize(json, new JsonSerializerOptions { WriteIndented = true, NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals }));
        return (md.ToString().Split('\n').Select(l => l.TrimEnd('\r')).ToList(), numbered.Count, file);
    }

    private static string Header(string title, IReadOnlyList<ExploreFold> folds)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Explore: {title}");
        sb.AppendLine();
        sb.AppendLine("Train only. Weighted (amendment 01 population). Folds:");
        foreach (var f in folds) sb.AppendLine($"- {f.Label}");
        sb.AppendLine();
        return sb.ToString();
    }

    // ── track 1: how early is a gapper visible ────────────────────────────

    private (List<string>, int, string?) Visibility(IReadOnlyList<string> times)
    {
        var md = new StringBuilder();
        var trials = new List<ExploreTrial>();
        var json = new List<object>();
        bool headed = false;
        foreach (var decision in times.Where(d => ExplorePanelFile.Exists(CacheDir(), d)))
        {
            var t = Load(decision);
            var folds = Folds(t);
            if (!headed)
            {
                md.Append(Header("how early is a gapper visible (reach +20% by 09:45, mornings still below +20% at t)", folds));
                md.AppendLine($"Each cell: mean fold AUC / within-morning AUC / precision@{PrecisionK} (ceiling in the last column).");
                md.AppendLine();
                md.AppendLine("| decision | rows | news benchmark | gainers screen | gainers + news | logistic | boosted trees | boosted per fold | precision ceiling |");
                md.AppendLine("|---|---|---|---|---|---|---|---|---|");
                headed = true;
            }
            var rows = Scorable(t, formingOnly: true, sameDay: DecisionTime.Parse(decision).DayOffset == 0);
            var all = AllFeatures(t);
            var bench = ScoreFolds(t, rows, folds, (_, eval) => Benchmark(t, eval));
            var screen = ScoreFolds(t, rows, folds, (_, eval) => ScreenBenchmark(t, eval));
            var screenNews = ScoreFolds(t, rows, folds, (_, eval) => ScreenNewsBenchmark(t, eval));
            var logit = ScoreFolds(t, rows, folds, (fit, eval) => Logistic(t, all, fit, eval));
            var boost = ScoreFolds(t, rows, folds, (fit, eval) => Boosted(t, all, fit, eval));
            md.AppendLine($"| {decision} | {boost.Rows:N0} | {Cell(bench)} | {Cell(screen)} | {Cell(screenNews)} | {Cell(logit)} | {Cell(boost)} | {string.Join(" / ", boost.Folds.Select(F))} | {F(boost.PrecisionCeiling)} |");
            foreach (var (name, r) in new[] { ("benchmark", bench), ("screen", screen), ("screen+news", screenNews), ("logistic", logit), ("boosted", boost) })
                trials.Add(Trial("visibility", $"t={decision} learner={name} rows=forming", r));
            json.Add(new { decision, rows = boost.Rows, benchmark = bench, screen, screenNews, logistic = logit, boosted = boost });
        }
        if (!headed) return (["no explore panel is cached yet; run `explore build` first"], 0, null);
        md.AppendLine();
        md.AppendLine("Reading: where the learners pull away from the gainers-plus-news screen is when a gapper becomes visible from price and volume rather than from the news and the move itself. A finalist must beat gainers + news, not only the news benchmark.");
        return Write("visibility", md, trials, json);
    }

    // ── track 2: what makes a gapper (learners and ablation) ──────────────

    private (List<string>, int, string?) Models(IReadOnlyList<string> times)
    {
        var decision = times.Count == 1 ? times[0] : "08:00";
        var t = Load(decision);
        var folds = Folds(t);
        var rows = Scorable(t, formingOnly: true);
        var all = AllFeatures(t);
        var md = new StringBuilder(Header($"what makes a gapper at {decision} (learners and feature-block ablation)", folds));
        var trials = new List<ExploreTrial>();
        var json = new List<object>();
        md.AppendLine($"| configuration | mean fold AUC | within-morning AUC | precision@{PrecisionK} | per fold | change vs boosted-all |");
        md.AppendLine("|---|---|---|---|---|---|");

        void Add(string config, FoldScore r, double? reference)
        {
            md.AppendLine($"| {config} | {F(r.Pooled)} | {F(r.DayAuc)} | {F(r.PrecisionAtK)} | {string.Join(" / ", r.Folds.Select(F))} | {(reference is { } b && r.Pooled is { } p ? (p - b).ToString("+0.000;-0.000", CultureInfo.InvariantCulture) : "")} |");
            trials.Add(Trial("models", $"t={decision} {config}", r));
            json.Add(new { config, r.Folds, r.Pooled, r.DayAuc, r.PrecisionAtK, r.PrecisionCeiling, r.Rows });
        }

        var full = ScoreFolds(t, rows, folds, (fit, eval) => Boosted(t, all, fit, eval));
        Add("benchmark (news <= 60 min)", ScoreFolds(t, rows, folds, (_, eval) => Benchmark(t, eval)), full.Pooled);
        Add("benchmark: gainers screen", ScoreFolds(t, rows, folds, (_, eval) => ScreenBenchmark(t, eval)), full.Pooled);
        Add("benchmark: gainers + news", ScoreFolds(t, rows, folds, (_, eval) => ScreenNewsBenchmark(t, eval)), full.Pooled);
        Add("logistic, all features", ScoreFolds(t, rows, folds, (fit, eval) => Logistic(t, all, fit, eval)), full.Pooled);
        Add("corobs k=100, all features", ScoreFolds(t, rows, folds, (fit, eval) => CorobsLearner(t, all, fit, eval, 100)), full.Pooled);
        Add("boosted, all features", full, null);
        foreach (var (block, prefixes) in Blocks)
        {
            var keep = all.Where(f => !prefixes.Any(pre => t.FeatureNames[f].StartsWith(pre, StringComparison.Ordinal))).ToArray();
            if (keep.Length == all.Length) continue;
            Add($"boosted, without {block}", ScoreFolds(t, rows, folds, (fit, eval) => Boosted(t, keep, fit, eval)), full.Pooled);
            var only = all.Except(keep).ToArray();
            Add($"boosted, {block} only", ScoreFolds(t, rows, folds, (fit, eval) => Boosted(t, only, fit, eval)), full.Pooled);
        }

        // Importance from one fit on every scorable row of the fit windows' union (descriptive only).
        var fitAll = rows.Where(r => folds.Count > 0 && folds[^1].InFit(t.Days[r])).ToList();
        if (fitAll.Count > 0)
        {
            BoostedWith(t, all, fitAll, [], null, out var model);
            md.AppendLine();
            md.AppendLine("Top features by gain (fold 4 fit window, descriptive):");
            foreach (var (feature, share) in model.Importance().Take(15)) md.AppendLine($"- {feature}: {share:P1}");
        }
        return Write("models", md, trials, json);
    }

    // ── track 5: when does velocity run out ───────────────────────────────

    private (List<string>, int, string?) Exits(IReadOnlyList<string> times)
    {
        var decision = times.Count == 1 ? times[0] : "08:00";
        var t = Load(decision);
        var folds = Folds(t);
        var md = new StringBuilder(Header("when does velocity run out (gapper days, descriptive)", folds));
        int isG = t.Label("IsGapper"), t50 = t.Label("Tier50"), hod = t.Label("HodMinEt"), death = t.Label("VelocityDeathMinEt"), gap = t.Label("DeathToHodMin"), vwap = t.Label("VwapLossMinEt");
        int age = t.Feature("CatalystAgeMinutes");
        var json = new List<object>();
        md.AppendLine("| group | days | HOD median ET | HOD before 09:30 | HOD 09:30-09:45 | velocity death median ET | death-to-HOD median min | VWAP loss median ET |");
        md.AppendLine("|---|---|---|---|---|---|---|---|");
        foreach (var (name, pick) in new (string, Func<int, bool>)[]
        {
            ("Tier20+", r => t.LabelValue(r, isG) > 0.5f),
            ("Tier50+", r => t50 >= 0 && t.LabelValue(r, t50) > 0.5f),
            ("Tier20+ with news <= 24h", r => t.LabelValue(r, isG) > 0.5f && age >= 0 && !float.IsNaN(t.Value(r, age))),
            ("Tier20+ without news", r => t.LabelValue(r, isG) > 0.5f && (age < 0 || float.IsNaN(t.Value(r, age)))),
        })
        {
            var rows = Enumerable.Range(0, t.Rows).Where(r => t.IsCandidate[r] && pick(r)).ToList();
            double? Median(int label) => label < 0 ? null : MedianOf(rows.Select(r => (double)t.LabelValue(r, label)).Where(double.IsFinite).ToList());
            double Share(Func<double, bool> test) => rows.Count == 0 ? double.NaN : (double)rows.Count(r => t.LabelValue(r, hod) is var v && !float.IsNaN(v) && test(v)) / rows.Count;
            md.AppendLine($"| {name} | {rows.Count:N0} | {Clock(Median(hod))} | {Share(v => v < 570):P1} | {Share(v => v is >= 570 and < 585):P1} | {Clock(Median(death))} | {F(Median(gap))} | {Clock(Median(vwap))} |");
            json.Add(new { group = name, days = rows.Count, hodMedian = Median(hod), deathMedian = Median(death), deathToHodMedian = Median(gap), vwapLossMedian = Median(vwap) });
        }
        int pause = t.Label(ExploreDerived.OpenWindowPauseLabel);
        if (pause >= 0)
        {
            md.AppendLine();
            md.AppendLine("LULD/volatility pauses in the band-halving window (09:30-09:50 ET, Nasdaq Trader halt history):");
            md.AppendLine();
            md.AppendLine("| group | days | paused 09:30-09:50 | first pause median ET | HOD at or before the first pause |");
            md.AppendLine("|---|---|---|---|---|");
            foreach (var (name, pick) in new (string, Func<int, bool>)[] { ("Tier20+", r => t.LabelValue(r, isG) > 0.5f), ("Tier50+", r => t50 >= 0 && t.LabelValue(r, t50) > 0.5f) })
            {
                var rows = Enumerable.Range(0, t.Rows).Where(r => t.IsCandidate[r] && pick(r)).ToList();
                var paused = rows.Where(r => !float.IsNaN(t.LabelValue(r, pause))).ToList();
                double share = rows.Count == 0 ? double.NaN : (double)paused.Count / rows.Count;
                var hodBy = paused.Count(r => t.LabelValue(r, hod) is var h && !float.IsNaN(h) && h <= t.LabelValue(r, pause));
                md.AppendLine($"| {name} | {rows.Count:N0} | {share:P1} | {Clock(MedianOf(paused.Select(r => (double)t.LabelValue(r, pause)).ToList()))} | {(paused.Count == 0 ? "n/a" : ((double)hodBy / paused.Count).ToString("P1", CultureInfo.InvariantCulture))} |");
                json.Add(new { group = name + " pauses", days = rows.Count, pausedShare = share, hodAtOrBeforePause = paused.Count == 0 ? (double?)null : (double)hodBy / paused.Count });
            }
        }
        md.AppendLine();
        md.AppendLine("Descriptive only: no trial is logged for this table. Gapper days are a census (candidates), so no weights.");
        return Write("exits", md, [], json);
    }

    // ── track 7: news ride ────────────────────────────────────────────────

    /// <summary>Catalyst age buckets in minutes since publication at t.</summary>
    private static readonly (string Name, double Lo, double Hi)[] AgeBuckets =
        [("<=15 min", 0, 15), ("15-60 min", 15, 60), ("1-4 h", 60, 240), ("4-24 h", 240, 1440)];

    private static readonly string[] RideHorizons = ["FwdClose0945", "FwdClose1030", "FwdClose1600"];

    /// <summary>
    /// What buying at the next open after t returns, gross, by catalyst type, age and direction, against
    /// the same mornings without news. Mean with a day-clustered 95% interval per horizon; one ledger
    /// trial per group and horizon (fold values are the eval-window means). Gross of cost: a group worth
    /// a finalist slot is costed with the frozen model before amendment 04.
    /// </summary>
    private (List<string>, int, string?) News(IReadOnlyList<string> times)
    {
        var decision = times.Count == 1 ? times[0] : "08:00";
        var t = Load(decision);
        var folds = Folds(t);
        int age = t.Feature("CatalystAgeMinutes"), dir = t.Feature("CatalystDirection"), gap = t.Feature("GapPct"), entry = t.Label("HasEntry");
        var md = new StringBuilder(Header($"news ride from {decision} (buy the next open after t, gross)", folds));
        var trials = new List<ExploreTrial>();
        var json = new List<object>();
        var pre = Enumerable.Range(0, t.FeatureCount).Where(f => t.FeatureNames[f].StartsWith("Pre.", StringComparison.Ordinal)).ToList();

        bool HasNews(int r) => age >= 0 && t.Value(r, age) is var a && !float.IsNaN(a) && a <= 1440;
        var tradable = Enumerable.Range(0, t.Rows).Where(r => t.Weights[r] > 0 && entry >= 0 && t.LabelValue(r, entry) > 0.5f
            && gap >= 0 && t.Value(r, gap) is var g && !float.IsNaN(g) && g < FormedGapPct).ToList();

        var groups = new List<(string Name, Func<int, bool> Pick)>
        {
            ("no news <= 24h", r => !HasNews(r)),
            ("any news <= 24h", HasNews),
        };
        foreach (var (name, lo, hi) in AgeBuckets) groups.Add(($"news age {name}", r => HasNews(r) && t.Value(r, age) is var a && a >= lo && a < hi));
        groups.Add(("news, positive direction", r => HasNews(r) && dir >= 0 && t.Value(r, dir) > 0));
        groups.Add(("news, negative direction", r => HasNews(r) && dir >= 0 && t.Value(r, dir) < 0));
        foreach (var f in pre) groups.Add(($"type {t.FeatureNames[f][4..]}", r => HasNews(r) && t.Value(r, f) > 0.5f));

        md.AppendLine("Mornings still below +20% at t with a next-open entry. Mean gross return, day-clustered 95% interval, days in brackets.");
        md.AppendLine();
        md.AppendLine("| group | rows | " + string.Join(" | ", RideHorizons.Select(h => "to " + h[^4..^2] + ":" + h[^2..])) + " |");
        md.AppendLine("|---|---|" + string.Concat(RideHorizons.Select(_ => "---|")));
        foreach (var (name, pick) in groups)
        {
            var rows = tradable.Where(pick).ToList();
            if (rows.Count == 0) continue;
            var cells = new List<string>();
            foreach (var h in RideHorizons)
            {
                int lab = t.Label(h);
                if (lab < 0) { cells.Add("n/a"); continue; }
                var all = ExploreDerived.ClusteredMean(rows.Select(r => (t.Days[r], (double)t.LabelValue(r, lab), t.Weights[r])));
                cells.Add(all is { } m ? string.Create(CultureInfo.InvariantCulture, $"{m.Mean:+0.0%;-0.0%} [{m.Lo:+0.0%;-0.0%}, {m.Hi:+0.0%;-0.0%}] ({m.Days})") : "n/a");
                var perFold = folds.Select(fd => ExploreDerived.ClusteredMean(rows.Where(r => fd.InEval(t.Days[r])).Select(r => (t.Days[r], (double)t.LabelValue(r, lab), t.Weights[r])))?.Mean).ToList();
                trials.Add(new ExploreTrial { Track = "news", Config = $"t={decision} {name} horizon={h}", Metric = "meanGrossReturn", FoldValues = perFold, Pooled = all?.Mean, Rows = rows.Count });
                json.Add(new { group = name, horizon = h, rows = rows.Count, mean = all?.Mean, lo = all?.Lo, hi = all?.Hi, days = all?.Days, folds = perFold });
            }
            md.AppendLine($"| {name} | {rows.Count:N0} | {string.Join(" | ", cells)} |");
        }
        md.AppendLine();
        md.AppendLine("Gross of cost: penny names often cost several percent round trip, so a group needs a clear margin before it is costed.");
        return Write("news", md, trials, json);
    }

    // ── track 6: fade leg (descriptive) ───────────────────────────────────

    /// <summary>Extension from the open at 09:45 against how the day ends, per gap tier (the H13 shape, census of gapper days).</summary>
    private (List<string>, int, string?) Fade(IReadOnlyList<string> times)
    {
        var decision = times.Count == 1 ? times[0] : "08:00";
        var t = Load(decision);
        var folds = Folds(t);
        int isG = t.Label("IsGapper"), t50 = t.Label("Tier50"), ext = t.Label("ExtensionFromOpen0945"), below = t.Label("CloseBelowOpen"),
            fade = t.Label("Fade"), f30 = t.Label("FadeFlag30"), symp = t.Feature(ExploreDerived.SympathyFormed);
        var md = new StringBuilder(Header("fade leg: extension from the open at 09:45 vs how the day ends (gapper days, descriptive)", folds));
        var json = new List<object>();
        (string, double, double)[] buckets = [("< 10%", double.NegativeInfinity, 0.10), ("10-25%", 0.10, 0.25), ("25-50%", 0.25, 0.50), (">= 50%", 0.50, double.PositiveInfinity)];
        md.AppendLine("| tier | extension at 09:45 | days | close below open | fade >= 30% from PM high | median close vs PM high |");
        md.AppendLine("|---|---|---|---|---|---|");
        foreach (var (tier, inTier) in new (string, Func<int, bool>)[]
        {
            ("Tier20+", r => t.LabelValue(r, isG) > 0.5f),
            ("Tier50+", r => t50 >= 0 && t.LabelValue(r, t50) > 0.5f),
            ("Tier20+, another gapper formed by t", r => t.LabelValue(r, isG) > 0.5f && symp >= 0 && t.Value(r, symp) >= 1),
            ("Tier20+, alone at t", r => t.LabelValue(r, isG) > 0.5f && symp >= 0 && t.Value(r, symp) == 0),
        })
        {
            foreach (var (bucket, lo, hi) in buckets)
            {
                var rows = Enumerable.Range(0, t.Rows).Where(r => t.IsCandidate[r] && inTier(r) && ext >= 0 && t.LabelValue(r, ext) is var e && !float.IsNaN(e) && e >= lo && e < hi).ToList();
                if (rows.Count == 0) continue;
                double Share(int lab) => lab < 0 ? double.NaN : (double)rows.Count(r => t.LabelValue(r, lab) > 0.5f) / Math.Max(1, rows.Count(r => !float.IsNaN(t.LabelValue(r, lab))));
                var med = fade < 0 ? null : MedianOf(rows.Select(r => (double)t.LabelValue(r, fade)).Where(double.IsFinite).ToList());
                md.AppendLine(string.Create(CultureInfo.InvariantCulture, $"| {tier} | {bucket} | {rows.Count:N0} | {Share(below):P1} | {Share(f30):P1} | {(med is { } m ? m.ToString("+0.0%;-0.0%", CultureInfo.InvariantCulture) : "n/a")} |"));
                json.Add(new { tier, bucket, days = rows.Count, closeBelowOpen = Share(below), fade30 = Share(f30), medianFade = med });
            }
        }
        md.AppendLine();
        md.AppendLine("Descriptive only: no trial is logged. Gapper days are a census (candidates), so no weights.");
        return Write("fade", md, [], json);
    }

    private static double? MedianOf(List<double> v)
    {
        if (v.Count == 0) return null;
        v.Sort();
        return v.Count % 2 == 1 ? v[v.Count / 2] : (v[v.Count / 2 - 1] + v[v.Count / 2]) / 2;
    }

    private static string Clock(double? minutes) => minutes is { } m ? $"{(int)m / 60:00}:{(int)m % 60:00}" : "n/a";
}
