using System.Diagnostics;
using System.Globalization;
using IdiotProof.Core.Evidence;
using IdiotProof.Evidence;
using IdiotProof.Evidence.Evaluation;
using IdiotProof.Evidence.EventStudy;
using IdiotProof.Evidence.Gappers;
using IdiotProof.Evidence.Search;
using IdiotProof.Models;

namespace IdiotProof.Core.Research.Gappers;

public sealed record TestStageRequest(IReadOnlyList<string> Hypotheses, string Split, bool Smoke);

public sealed record TestStageResult(string Split, List<HypothesisRun> Runs, string? HoldoutNote, double Minutes);

/// <summary>
/// <c>research gappers test</c>: runs each pre-registered hypothesis with the primitive its
/// <c>test</c> field names (bucket-lift → HoldoutBucketEvaluator; event-study-bmp → same-day BMP
/// with a within-day placebo; proportion-diff-clustered / group-diff → day-clustered bootstrap;
/// wilson-ci / quantile-ci → descriptive intervals; auc-delong → an L2 logistic fit on train against
/// the catalyst-latency benchmark with a day-block AUC bootstrap; search-grid-spa → the H12 grid
/// with the frozen cost model, SPA and DSR), corrects within family (BH) and across primaries
/// (Holm), writes one run file per hypothesis, and opens the holdout once through <see cref="HoldoutLock"/>.
/// </summary>
public sealed partial class GapperTestStage(GapperStudyServices s, GapperStudyPanel panel)
{
    public const string Stage = "test";
    private const int MinDays = 5;

    public async Task<TestStageResult> RunAsync(TestStageRequest req, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var p = s.Params;
        var set = GapperHypothesisSet.Load(s.Paths);
        var split = req.Smoke ? GapperStudyServices.SmokeSplit : req.Split.ToLowerInvariant();
        GapperHypothesis Resolve(string id) => s.FindAmended(id) is { } a ? a.Amendment.Resolve(a.Hypothesis, set) : set.Find(id);
        var ids = req.Hypotheses.Count == 0 || req.Hypotheses.Any(h => h.Equals("all", StringComparison.OrdinalIgnoreCase))
            ? set.Hypotheses.Select(h => h.Id).Concat(s.Amendments.SelectMany(a => a.Hypotheses.Select(h => h.Id))).ToList()
            : req.Hypotheses.Select(h => Resolve(h).Id).Distinct().ToList();

        string? holdoutNote = null;
        HoldoutLock? gate = null;
        if (split == "holdout")
        {
            gate = new HoldoutLock(s.Paths, p);
            var check = gate.Check(Stage);
            if (!check.Allowed) throw new InvalidOperationException("Holdout locked: " + check.Reason);
            holdoutNote = $"holdout unlocked with {check.FrozenHash}";
            gate.BeginStage(Stage, s.UtcNow, new { split, hypotheses = ids });
        }

        var runs = new List<HypothesisRun>();
        foreach (var id in ids)
        {
            var h = Resolve(id);
            var found = s.FindAmended(id);
            var amended = found?.Hypothesis;
            var started = Stopwatch.StartNew();
            var run = NewRun(h, split, req.Smoke, set);
            if (found is { } f)
            {
                run.Inputs.AmendmentId = f.Amendment.Id;
                run.Inputs.AmendmentSha256 = f.Amendment.Sha256;
                run.Inputs.AmendsHypothesis = string.IsNullOrEmpty(f.Hypothesis.BasedOn) ? null : f.Hypothesis.BasedOn;
            }
            if (!req.Smoke && !h.AllowsSplit(split))
                run.Parts.Add(HypothesisPart.VoidPart("split", $"{(found is null ? "hypotheses.json" : "amendment " + found.Value.Amendment.Id)} allows {id} on {string.Join(", ", h.SplitsAllowed)} only"));
            else
            {
                try { await (amended is null ? DispatchAsync(h, run, split, ct) : DispatchAmendedAsync(h, amended, run, split, ct)); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    s.Logger.LogWarning(ex, "test {Id} on {Split} failed", id, split);
                    run.Parts.Add(HypothesisPart.VoidPart("error", ex.Message));
                }
            }
            run.Seconds = Math.Round(started.Elapsed.TotalSeconds, 1);
            runs.Add(run);
            s.Logger.LogInformation("[{Now}] test {Id} on {Split}: {Parts} part(s) in {Seconds}s", GapperStudyServices.Et(s.UtcNow), id, split, run.Parts.Count, run.Seconds);
        }

        HypothesisCorrections.ApplyAll(runs, p, $"BH within family and Holm across the {runs.Count} hypothesis run(s) of this invocation; the report re-corrects across the latest run of every hypothesis");
        Directory.CreateDirectory(s.Paths.RunsDir);
        var stamp = s.UtcNow;
        foreach (var run in runs)
        {
            run.ComputedUtc = stamp;
            var file = Path.Combine(s.Paths.RunsDir, HypothesisRun.FileName(run.Id, split, stamp));
            run.File = file;
            File.WriteAllText(file, run.ToJson());
        }
        gate?.EndStage(Stage, s.UtcNow, new { runs = runs.Select(r => new { r.Id, r.Verdict }).ToList() });
        return new TestStageResult(split, runs, holdoutNote, Math.Round(sw.Elapsed.TotalMinutes, 2));
    }

    private HypothesisRun NewRun(GapperHypothesis h, string split, bool smoke, GapperHypothesisSet set) => new()
    {
        Id = h.Id, Family = h.Family, Primary = h.Primary, Test = h.Test, Claim = h.Claim, Feature = h.Feature, Label = h.Label, DecisionTime = h.DecisionTime,
        MinEffect = h.MinEffect, Split = split, Smoke = smoke,
        Inputs = new HypothesisRunInputs
        {
            FrozenCommit = HoldoutLock.GitFrozenCommit(s.Paths), Head = HoldoutLock.GitHead(s.Paths),
            HypothesesSha256 = set.Sha256, ParamsSha256 = s.Params.ParamsSha256,
        },
    };

    private Task DispatchAsync(GapperHypothesis h, HypothesisRun run, string split, CancellationToken ct) => h.Test switch
    {
        "bucket-lift" => BucketLiftAsync(h, run, split, ct),
        "event-study-bmp" => EventStudyAsync(h, run, split, ct),
        "proportion-diff-clustered" => ProportionDiffAsync(h, run, split, ct),
        "wilson-ci" => WilsonAsync(h, run, split, ct),
        "quantile-ci" => QuantileAsync(h, run, split, ct),
        "group-diff" => GroupDiffAsync(h, run, split, ct),
        "auc-delong" => AucAsync(h, run, split, ct),
        "search-grid-spa" => GridAsync(h, run, split, ct),
        _ => Unknown(h, run),
    };

    private static Task Unknown(GapperHypothesis h, HypothesisRun run)
    {
        run.Parts.Add(HypothesisPart.VoidPart("test", $"unknown test '{h.Test}'"));
        return Task.CompletedTask;
    }

    // ── Shared pieces ────────────────────────────────────────────────────

    private GapperStudyParams P => s.Params;

    private static string Decision(GapperHypothesis h, string fallback) => string.IsNullOrWhiteSpace(h.DecisionTime) ? fallback : h.DecisionTime;

    private static void Describe(HypothesisRun run, IReadOnlyCollection<(string Symbol, DateOnly Day)> keys, string population, IEnumerable<string> features, IEnumerable<string> labels)
    {
        run.Inputs.N = keys.Count;
        run.Inputs.Days = keys.Select(k => k.Day).Distinct().Count();
        run.Inputs.FirstDate = keys.Count == 0 ? null : keys.Min(k => k.Day).ToString("yyyy-MM-dd");
        run.Inputs.LastDate = keys.Count == 0 ? null : keys.Max(k => k.Day).ToString("yyyy-MM-dd");
        run.Inputs.Population = population;
        run.Inputs.FeatureColumns = features.ToList();
        run.Inputs.LabelColumns = labels.ToList();
    }

    private BootstrapInterval Boot(IReadOnlyList<ClusterObservation> obs, Func<IReadOnlyList<ClusterObservation>, double> statistic) =>
        DayClusteredBootstrap.Run(obs, statistic, P.Statistics.BootstrapDraws, P.Statistics.Seed, P.Statistics.CiLevel, MinDays);

    private double WilsonZ => Distributions.NormalQuantile(1 - (1 - P.Statistics.CiLevel) / 2);

    private HypothesisPart BootstrapPart(string name, string effectName, IReadOnlyList<ClusterObservation> obs, Func<IReadOnlyList<ClusterObservation>, double> statistic,
        double minEffect, EffectDirection direction, int treatedN, int? controlN = null, Func<double, double>? scale = null, bool withP = true)
    {
        var part = new HypothesisPart { Name = name, EffectName = effectName, MinEffect = minEffect, Direction = direction, N = obs.Count, Days = obs.Select(o => o.Day).Distinct().Count() };
        if (treatedN < P.Statistics.MinBucketN || controlN is { } c && c < P.Statistics.MinBucketN)
        {
            part.Verdict = HypothesisVerdict.Void;
            part.VerdictFixed = true;
            part.Reasons.Add($"{treatedN} rows in the tested group{(controlN is { } cn ? $" / {cn} in the control group" : "")}, fewer than minBucketN {P.Statistics.MinBucketN}");
            return part;
        }
        var b = Boot(obs, statistic);
        scale ??= x => x;
        part.Effect = scale(b.Estimate);
        part.CiLow = double.IsNaN(b.Low) ? null : Math.Min(scale(b.Low), scale(b.High));
        part.CiHigh = double.IsNaN(b.High) ? null : Math.Max(scale(b.Low), scale(b.High));
        part.RawP = withP ? b.PValueTwoSided : null;
        part.MeetsMinimum = part.Effect is { } e && !double.IsNaN(e) && (direction == EffectDirection.AtLeast ? e >= minEffect : e <= minEffect);
        part.Statistic["bootstrapDraws"] = b.Draws;
        part.Statistic["treatedN"] = treatedN;
        part.Statistic["controlN"] = obs.Count - treatedN;
        part.Statistic["ciLevel"] = P.Statistics.CiLevel;
        return part;
    }

    private HypothesisPart WilsonPart(string name, int successes, int n, double threshold, EffectDirection direction, string effectName = "proportion")
    {
        var part = new HypothesisPart { Name = name, EffectName = effectName, MinEffect = threshold, Direction = direction, N = n };
        if (n < P.Statistics.MinBucketN)
        {
            part.Verdict = HypothesisVerdict.Void;
            part.VerdictFixed = true;
            part.Reasons.Add($"{n} rows, fewer than minBucketN {P.Statistics.MinBucketN}");
            return part;
        }
        var (lo, hi) = EvidenceStats.WilsonInterval(successes, n, WilsonZ);
        part.Effect = (double)successes / n;
        part.CiLow = lo;
        part.CiHigh = hi;
        part.MeetsMinimum = direction == EffectDirection.AtLeast ? part.Effect >= threshold : part.Effect <= threshold;
        part.Statistic["successes"] = successes;
        part.Statistic["ciLevel"] = P.Statistics.CiLevel;
        part.Statistic["interval"] = "Wilson";
        return part;
    }

    private static IEnumerable<(string Symbol, DateOnly Day)> Keys(IEnumerable<PanelRow> rows) => rows.Select(r => (r.Symbol, r.DateEt));

    private static IEnumerable<(string Symbol, DateOnly Day)> Keys(IEnumerable<UniverseObservation> rows) => rows.Select(r => (r.Symbol, r.Day));

    private async Task<List<PanelRow>> TrainRowsAsync(string split, string decision, List<PanelRow> current, CancellationToken ct) =>
        split == "train" || split == GapperStudyServices.SmokeSplit ? current : await panel.LoadAsync("train", decision, ct);

    // ── F1: bucket-lift (H1, H6, H9, H15) ────────────────────────────────

    private async Task BucketLiftAsync(GapperHypothesis h, HypothesisRun run, string split, CancellationToken ct)
    {
        List<BucketObservation> train, test;
        int? categories;
        int bucketOfInterest;
        string effectName;
        switch (h.Id.ToUpperInvariant())
        {
            case "H1":
            {
                var decision = Decision(h, "08:00");
                var rows = (await panel.LoadAsync(split, decision, ct)).Where(r => r.IsCandidate && r.Snapshot.VolRatioTod is not null && r.Snapshot.PmCumVol > 0 && r.Label.Reach20By0945 is not null).ToList();
                var trainRows = (await TrainRowsAsync(split, decision, rows, ct)).Where(r => r.IsCandidate && r.Snapshot.VolRatioTod is not null && r.Snapshot.PmCumVol > 0 && r.Label.Reach20By0945 is not null).ToList();
                test = rows.Select(r => new BucketObservation(r.DateEt, r.Snapshot.VolRatioTod!.Value, r.Label.Reach20By0945!.Value)).ToList();
                train = trainRows.Select(r => new BucketObservation(r.DateEt, r.Snapshot.VolRatioTod!.Value, r.Label.Reach20By0945!.Value)).ToList();
                categories = null;
                bucketOfInterest = P.Statistics.BucketBins - 1;
                effectName = "precisionOverBaseRate";
                Describe(run, Keys(rows).ToList(), $"candidate days with a bar by {decision}", ["VolRatioTod"], ["Reach20By0945"]);
                break;
            }
            case "H6":
            {
                var clause = FeatureCondition.Require(h.Feature, "abnVol_1");
                var obs = await panel.AbnormalVolumeD1Async(split, ct);
                var trainObs = split is "train" or GapperStudyServices.SmokeSplit ? obs : await panel.AbnormalVolumeD1Async("train", ct);
                test = obs.Select(o => new BucketObservation(o.Day, clause.Holds(o.Value) ? 1 : 0, o.IsGapper)).ToList();
                train = trainObs.Select(o => new BucketObservation(o.Day, clause.Holds(o.Value) ? 1 : 0, o.IsGapper)).ToList();
                categories = 2;
                bucketOfInterest = 1;
                effectName = "lift";
                Describe(run, Keys(obs).ToList(), "U(D) members present in U(D-1)", ["abnVol_1 = volume(D-1)/ADV20(D-1)"], ["IsGapper (Tier20)"]);
                break;
            }
            case "H9":
            {
                var decision = Decision(h, "09:00");
                var clauses = FeatureCondition.Parse(h.Feature);
                var labelClause = FeatureCondition.Require(h.Label, "fwdMax_09:45");
                var rows = (await panel.LoadAsync(split, decision, ct)).Where(r => r.IsCandidate && r.Label.FwdMax0945 is not null).ToList();
                var trainRows = (await TrainRowsAsync(split, decision, rows, ct)).Where(r => r.IsCandidate && r.Label.FwdMax0945 is not null).ToList();
                bool Passes(PanelRow r) => clauses.All(c => c.Holds(r.Column(c.Name)));
                test = rows.Select(r => new BucketObservation(r.DateEt, Passes(r) ? 1 : 0, labelClause.Holds(r.Label.FwdMax0945))).ToList();
                train = trainRows.Select(r => new BucketObservation(r.DateEt, Passes(r) ? 1 : 0, labelClause.Holds(r.Label.FwdMax0945))).ToList();
                categories = 2;
                bucketOfInterest = 1;
                effectName = "lift";
                Describe(run, Keys(rows).ToList(), $"candidate days at {decision}", clauses.Select(c => $"{c.Name} {c.Operator} {c.Threshold}"), [$"FwdMax0945 {labelClause.Operator} {labelClause.Threshold}"]);
                break;
            }
            case "H15":
            {
                var clause = FeatureCondition.Require(h.Feature, "priorDayTopMoverRank");
                int maxRank = Math.Max((int)clause.Threshold, 1);
                var obs = await panel.PriorDayTopMoverAsync(split, maxRank, ct);
                var trainObs = split is "train" or GapperStudyServices.SmokeSplit ? obs : await panel.PriorDayTopMoverAsync("train", maxRank, ct);
                test = obs.Select(o => new BucketObservation(o.Day, clause.Holds(o.Value) ? 1 : 0, o.IsGapper)).ToList();
                train = trainObs.Select(o => new BucketObservation(o.Day, clause.Holds(o.Value) ? 1 : 0, o.IsGapper)).ToList();
                categories = 2;
                bucketOfInterest = 1;
                effectName = "lift";
                Describe(run, Keys(obs).ToList(), "U(D) members ranked among U(D-1)", [$"priorDayTopMoverRank {clause.Operator} {clause.Threshold}"], ["IsGapper (Tier20)"]);
                break;
            }
            default:
                run.Parts.Add(HypothesisPart.VoidPart("mapping", $"{h.Id} has no bucket-lift mapping"));
                return;
        }

        var report = HoldoutBucketEvaluator.Evaluate([new FeatureSamples(h.Id, train, test, categories)],
            categories ?? P.Statistics.BucketBins, P.Statistics.BhQ, MinDays, P.Statistics.BootstrapDraws, P.Statistics.Seed);
        var feature = report.Features[0];
        var bucket = feature.Buckets.FirstOrDefault(b => b.Bin == bucketOfInterest);
        var part = new HypothesisPart { Name = effectName, EffectName = effectName, MinEffect = h.MinEffectNumber(effectName), Direction = EffectDirection.AtLeast };
        if (bucket is null || bucket.HoldoutSamples < P.Statistics.MinBucketN || double.IsNaN(bucket.HoldoutBaseRate) || bucket.HoldoutBaseRate <= 0)
        {
            part.Verdict = HypothesisVerdict.Void;
            part.Reasons.Add($"{bucket?.HoldoutSamples ?? 0} rows in the tested bucket (minBucketN {P.Statistics.MinBucketN}) or no base rate");
            run.Parts.Add(part);
            return;
        }
        part.N = bucket.HoldoutSamples;
        part.Days = bucket.Days;
        part.Effect = bucket.Lift;
        part.CiLow = bucket.HoldoutLow / bucket.HoldoutBaseRate;
        part.CiHigh = bucket.HoldoutHigh / bucket.HoldoutBaseRate;
        part.RawP = bucket.P;
        part.MeetsMinimum = part.Effect >= part.MinEffect;
        part.Statistic["bucketRate"] = bucket.HoldoutRate;
        part.Statistic["baseRate"] = bucket.HoldoutBaseRate;
        part.Statistic["trainBucketRate"] = bucket.Train.Probability;
        part.Statistic["trainBaseRate"] = feature.TrainBaseRate;
        part.Statistic["binomialP"] = bucket.BinomialP;
        part.Statistic["dayClusteredP"] = bucket.DayClusteredP;
        part.Statistic["qWithinFeatureBuckets"] = bucket.Q;
        part.Statistic["bins"] = feature.Bins;
        part.Statistic["trainSamples"] = feature.TrainSamples;
        part.Statistic["splitSamples"] = feature.HoldoutSamples;
        part.Statistic["edgesFitOn"] = split is "train" or GapperStudyServices.SmokeSplit ? "this split (in-sample)" : "train";
        run.Parts.Add(part);
    }

    // ── F2: event-study-bmp (H2, H16) ────────────────────────────────────

    private async Task EventStudyAsync(GapperHypothesis h, HypothesisRun run, string split, CancellationToken ct)
    {
        var decision = Decision(h, "09:30");
        var rows = (await panel.LoadAsync(split, decision, ct)).Where(r => r.IsGapper).ToList();
        int lookback = P.Features.Catalyst.LookbackHours;
        Func<PanelRow, bool?> group;
        Func<PanelRow, double?> label;
        string labelName;
        if (h.Id.Equals("H2", StringComparison.OrdinalIgnoreCase))
        {
            var types = new HashSet<string>(["Fda", "ContractAward", "MA"], StringComparer.OrdinalIgnoreCase);
            group = r => !r.HasCatalyst(lookback) ? false : r.Snapshot.CatalystPreClass is { } c && types.Contains(c) ? true : null;
            label = r => r.Label.FwdClose1600 is { } a && r.Label.FwdClose1030 is { } b ? (a - b) * 100 : null;
            labelName = "(FwdClose1600 - FwdClose1030) x 100";
        }
        else
        {
            group = r => !r.HasCatalyst(lookback) ? null : r.Snapshot.CatalystPreClass == "PromoPr";
            label = r => r.Label.FwdClose1600 is { } a ? a * 100 : null;
            labelName = "FwdClose1600 x 100";
            run.Reasons.Add("promotional = CatalystPreClass PromoPr (params promotionalSources is empty); control = every other catalyst inside the lookback, source tier not distinguished on snapshot rows");
        }
        var events = new List<GroupEvent>();
        var keys = new List<(string, DateOnly)>();
        foreach (var r in rows)
            if (group(r) is { } g && label(r) is { } v)
            {
                events.Add(new GroupEvent(r.DateEt, v, g));
                keys.Add((r.Symbol, r.DateEt));
            }
        Describe(run, keys, $"Tier20 gapper days at {decision} with a group", [h.Feature ?? ""], [labelName]);

        var minEffect = h.MinEffectNumber("meanDifferencePct");
        var part = new HypothesisPart { Name = "meanDifferencePct", EffectName = "meanDifferencePct", MinEffect = minEffect, Direction = EffectDirection.AtLeast, N = events.Count, Days = events.Select(e => e.Day).Distinct().Count() };
        int treated = events.Count(e => e.Treated), control = events.Count - treated;
        if (treated < P.Statistics.MinBucketN || control < P.Statistics.MinBucketN)
        {
            part.Verdict = HypothesisVerdict.Void;
            part.VerdictFixed = true;
            part.Reasons.Add($"{treated} treated / {control} control events, fewer than minBucketN {P.Statistics.MinBucketN}");
            run.Parts.Add(part);
            return;
        }
        var study = SameDayGroupStudy.Run(events, P.Statistics.PlaceboDraws, P.Statistics.Seed);
        var obs = events.Select(e => new ClusterObservation(e.Day, e.Value, e.Treated ? 1 : 0)).ToList();
        var ci = Boot(obs, DayClusteredBootstrap.MeanDifference);
        part.Effect = study.MeanDifference;
        part.CiLow = double.IsNaN(ci.Low) ? null : ci.Low;
        part.CiHigh = double.IsNaN(ci.High) ? null : ci.High;
        part.RawP = double.IsNaN(study.BmpP) ? study.PlaceboP : double.IsNaN(study.PlaceboP) ? study.BmpP : Math.Max(study.BmpP, study.PlaceboP);
        part.MeetsMinimum = study.MeanDifference >= minEffect;
        part.Statistic["meanTreated"] = study.MeanTreated;
        part.Statistic["meanControl"] = study.MeanControl;
        part.Statistic["bmpT"] = study.BmpT;
        part.Statistic["bmpP"] = study.BmpP;
        part.Statistic["placeboP"] = study.PlaceboP;
        part.Statistic["placeboDraws"] = study.PlaceboDraws;
        part.Statistic["daysWithBothGroups"] = study.DaysWithBothGroups;
        part.Statistic["treatedN"] = treated;
        part.Statistic["controlN"] = control;
        part.Statistic["rawPRule"] = "max(BMP p, placebo p)";
        run.Parts.Add(part);
    }

    // ── F2: proportion-diff-clustered (H3) ───────────────────────────────

    private async Task ProportionDiffAsync(GapperHypothesis h, HypothesisRun run, string split, CancellationToken ct)
    {
        var decision = Decision(h, "09:30");
        int lookback = P.Features.Catalyst.LookbackHours;
        var rows = (await panel.LoadAsync(split, decision, ct)).Where(r => r.IsGapper && r.Label.FadeFlag30 is not null).ToList();
        var obs = rows.Select(r => new ClusterObservation(r.DateEt, r.Label.FadeFlag30!.Value ? 1 : 0, r.HasCatalyst(lookback) ? 0 : 1)).ToList();
        Describe(run, Keys(rows).ToList(), $"Tier20 gapper days at {decision}", [$"hasCatalyst (item within {lookback}h)"], ["FadeFlag30"]);
        var part = BootstrapPart("proportionDifferencePp", "proportionDifferencePp", obs, DayClusteredBootstrap.MeanDifference,
            h.MinEffectNumber("proportionDifferencePp"), EffectDirection.AtLeast, obs.Count(o => o.Group == 1), obs.Count(o => o.Group == 0), x => x * 100);
        part.Statistic["fadeRateNoCatalyst"] = obs.Where(o => o.Group == 1).Select(o => o.Value).DefaultIfEmpty(double.NaN).Average();
        part.Statistic["fadeRateCatalyst"] = obs.Where(o => o.Group == 0).Select(o => o.Value).DefaultIfEmpty(double.NaN).Average();
        run.Parts.Add(part);
    }

    // ── F3: wilson-ci (H4, H13, H17) ─────────────────────────────────────

    private async Task WilsonAsync(GapperHypothesis h, HypothesisRun run, string split, CancellationToken ct)
    {
        switch (h.Id.ToUpperInvariant())
        {
            case "H4":
            {
                var before = FeatureCondition.Time(h.Label) ?? throw new InvalidDataException("H4 label names no clock time.");
                var rows = (await panel.LoadAsync(split, "09:30", ct)).Where(r => r.IsTier50 && r.Label.HodTimeRthEt is not null).ToList();
                int hits = rows.Count(r => r.Label.HodTimeRthEt!.Value.ToTimeSpan() < before);
                Describe(run, Keys(rows).ToList(), "Tier50 gapper days", [], [$"HodTimeRthEt < {before:hh\\:mm}"]);
                run.Parts.Add(WilsonPart("proportionAtLeast", hits, rows.Count, h.MinEffectNumber("proportionAtLeast"), EffectDirection.AtLeast));
                return;
            }
            case "H13":
            {
                var clause = FeatureCondition.Require(h.Feature, "ext0945");
                var rows = (await panel.LoadAsync(split, "09:30", ct)).Where(r => r.IsGapper && clause.Holds(r.Label.ExtensionFromOpen0945) && r.Label.CloseBelowOpen is not null).ToList();
                int hits = rows.Count(r => r.Label.CloseBelowOpen == true);
                Describe(run, Keys(rows).ToList(), $"Tier20 gapper days with ext0945 {clause.Operator} {clause.Threshold}", ["ExtensionFromOpen0945"], ["CloseBelowOpen"]);
                run.Parts.Add(WilsonPart("proportionAtLeast", hits, rows.Count, h.MinEffectNumber("proportionAtLeast"), EffectDirection.AtLeast));
                return;
            }
            case "H17":
                await TradesCollapseAsync(h, run, split, ct);
                return;
            default:
                run.Parts.Add(HypothesisPart.VoidPart("mapping", $"{h.Id} has no wilson-ci mapping"));
                return;
        }
    }

    /// <summary>H17: a trade-count collapse inside the five minutes before the full-session high, against the unconditional per-window frequency on the same days.</summary>
    private async Task TradesCollapseAsync(GapperHypothesis h, HypothesisRun run, string split, CancellationToken ct)
    {
        var clause = FeatureCondition.Require(h.Feature, "tradesRoc");
        int window = P.Features.TradesWindowsMin.Count > 0 ? P.Features.TradesWindowsMin.Max() : 5;
        var rows = (await panel.LoadAsync(split, "09:30", ct)).Where(r => r.IsGapper && r.Label.HodTimeEt is not null).ToList();
        var store = await panel.StoreAsync(ct);
        int conditional = 0, usable = 0;
        long windowsHit = 0, windowsTotal = 0;
        var keys = new List<(string, DateOnly)>();
        foreach (var r in rows)
        {
            var bars = store.ReadDay(r.Symbol, r.DateEt);
            if (bars.Count == 0) continue;
            var start = MinuteBarMath.ToUtc(r.DateEt, MinuteBarMath.SessionStartEt);
            var close = MinuteBarMath.ToUtc(r.DateEt, new TimeSpan(16, 0, 0));
            var filled = MinuteBarMath.ForwardFill(bars, start, close);
            var hod = MinuteBarMath.ToUtc(r.DateEt, r.Label.HodTimeEt!.Value.ToTimeSpan());
            bool hit = false;
            for (var m = hod.AddMinutes(-window); m < hod; m = m.AddMinutes(1))
                if (clause.Holds(GapperFormationFeatures.TradesRoc(filled, m.AddMinutes(1), window))) { hit = true; break; }
            usable++;
            keys.Add((r.Symbol, r.DateEt));
            if (hit) conditional++;
            for (var w = start.AddMinutes(2 * window); w.AddMinutes(window) <= close; w = w.AddMinutes(window))
            {
                if (w < hod && hod < w.AddMinutes(window)) continue;
                bool any = false;
                for (var m = w; m < w.AddMinutes(window) && !any; m = m.AddMinutes(1))
                    any = clause.Holds(GapperFormationFeatures.TradesRoc(filled, m.AddMinutes(1), window));
                windowsTotal++;
                if (any) windowsHit++;
            }
        }
        Describe(run, keys, "Tier20 gapper days with minute bars", [$"tradesRoc {clause.Operator} {clause.Threshold} ({window}-minute trade count rate)"], ["HodTimeEt"]);
        double unconditional = windowsTotal == 0 ? double.NaN : (double)windowsHit / windowsTotal;
        var ratioMin = h.MinEffectNumber("ratioOverUnconditional");
        var part = new HypothesisPart { Name = "ratioOverUnconditional", EffectName = "ratioOverUnconditional", MinEffect = ratioMin, Direction = EffectDirection.AtLeast, N = usable, Days = keys.Select(k => k.Item2).Distinct().Count() };
        if (usable < P.Statistics.MinBucketN || double.IsNaN(unconditional) || unconditional <= 0)
        {
            part.Verdict = HypothesisVerdict.Void;
            part.Reasons.Add($"{usable} usable days (minBucketN {P.Statistics.MinBucketN}) or no unconditional windows");
            run.Parts.Add(part);
            return;
        }
        var (lo, hi) = EvidenceStats.WilsonInterval(conditional, usable, WilsonZ);
        part.Effect = (double)conditional / usable / unconditional;
        part.CiLow = lo / unconditional;
        part.CiHigh = hi / unconditional;
        part.MeetsMinimum = part.Effect >= ratioMin;
        part.Statistic["conditionalShare"] = (double)conditional / usable;
        part.Statistic["unconditionalShare"] = unconditional;
        part.Statistic["windows"] = windowsTotal;
        part.Statistic["interval"] = "Wilson on the conditional share, divided by the unconditional share";
        run.Parts.Add(part);
    }

    // ── F3: quantile-ci (H5) ─────────────────────────────────────────────

    private async Task QuantileAsync(GapperHypothesis h, HypothesisRun run, string split, CancellationToken ct)
    {
        var rows = (await panel.LoadAsync(split, "09:30", ct)).Where(r => r.IsGapper && r.Label.VelocityDeathTimeEt is not null && r.Label.DeathToHodMin is not null && r.Label.HodBucket is not null).ToList();
        Describe(run, Keys(rows).ToList(), "Tier20 gapper days with a velocity death", ["VelocityDeathTimeEt"], ["DeathToHodMin", "HodBucket"]);
        var range = h.MinEffectRange("medianWithin");
        var obs = rows.Select(r => new ClusterObservation(r.DateEt, r.Label.DeathToHodMin!.Value)).ToList();
        var median = BootstrapPart("medianDeathToHodMin", "medianDeathToHodMin", obs, DayClusteredBootstrap.Median, range[0], EffectDirection.AtLeast, obs.Count, withP: false);
        if (median.Verdict != HypothesisVerdict.Void)
        {
            median.MeetsMinimum = median.Effect is { } m && m >= range[0] && m <= range[1];
            median.Statistic["range"] = range;
            median.Verdict = median.HasInterval && median.CiLow >= range[0] && median.CiHigh <= range[1] ? HypothesisVerdict.Supported
                : median.HasInterval && (median.CiHigh < range[0] || median.CiLow > range[1]) ? HypothesisVerdict.Refuted : HypothesisVerdict.Undecided;
            median.VerdictFixed = true;
            median.Reasons.Add($"verdict: bootstrap interval [{HypothesisCorrections.Fmt(median.CiLow)}, {HypothesisCorrections.Fmt(median.CiHigh)}] against [{range[0]}, {range[1]}]; descriptive, no p");
        }
        run.Parts.Add(median);

        var bucketName = P.Labels.HodBuckets.FirstOrDefault(b => h.Label?.Contains(b, StringComparison.Ordinal) == true) ?? "09:30-09:31";
        int inBucket = rows.Count(r => r.Label.HodBucket == bucketName);
        var share = WilsonPart("bucketShareBelow", inBucket, rows.Count, h.MinEffectNumber("bucketShareBelow"), EffectDirection.AtMost, $"share of HOD in {bucketName}");
        share.Statistic["bucket"] = bucketName;
        run.Parts.Add(share);
    }

    // ── F5 / F4: group-diff (H7, H8, H10, H14) ───────────────────────────

    private async Task GroupDiffAsync(GapperHypothesis h, HypothesisRun run, string split, CancellationToken ct)
    {
        switch (h.Id.ToUpperInvariant())
        {
            case "H7":
            {
                var clause = FeatureCondition.Require(h.Feature, "daysSinceReverseSplit");
                var rows = (await panel.LoadAsync(split, Decision(h, "09:30"), ct)).Where(r => r.IsGapper).ToList();
                Describe(run, Keys(rows).ToList(), "Tier20 gapper days at 09:30", [$"daysSinceReverseSplit {clause.Operator} {clause.Threshold}"], ["FwdClose1030 x 100", "Fade x 100"]);
                int Group(PanelRow r) => clause.Holds(r.Snapshot.DaysSinceReverseSplit) ? 1 : 0;
                var fwd = rows.Where(r => r.Label.FwdClose1030 is not null).Select(r => new ClusterObservation(r.DateEt, r.Label.FwdClose1030!.Value * 100, Group(r))).ToList();
                run.Parts.Add(BootstrapPart("fwdCloseDifferencePct", "fwdClose_10:30 difference (pct)", fwd, DayClusteredBootstrap.MeanDifference, h.MinEffectNumber("fwdCloseDifferencePct"), EffectDirection.AtMost, fwd.Count(o => o.Group == 1), fwd.Count(o => o.Group == 0)));
                var fade = rows.Where(r => r.Label.Fade is not null).Select(r => new ClusterObservation(r.DateEt, r.Label.Fade!.Value * 100, Group(r))).ToList();
                run.Parts.Add(BootstrapPart("fadeDifferencePct", "fade difference (pct)", fade, DayClusteredBootstrap.MeanDifference, h.MinEffectNumber("fadeDifferencePct"), EffectDirection.AtMost, fade.Count(o => o.Group == 1), fade.Count(o => o.Group == 0)));
                return;
            }
            case "H8":
            {
                var clause = FeatureCondition.Require(h.Feature, "form4BuysD30");
                var decision = DecisionTime.Parse(Decision(h, "D-1 16:00"));
                var (obs, coverage) = await panel.Form4BuysAsync(split, decision, ct);
                Describe(run, Keys(obs).ToList(), "CIK-mapped members of U(D)", [$"form4BuysD30 {clause.Operator} {clause.Threshold}"], ["IsGapper (Tier20)"]);
                run.Inputs.FeatureColumns.Add($"cikCoverage={coverage:0.000}");
                if (coverage < P.KillCriteria.H8DropIfCikCoverageBelow)
                {
                    run.Parts.Add(HypothesisPart.VoidPart("lift", $"dropped, not redefined: CIK coverage {coverage:P1} of candidate symbols is below {P.KillCriteria.H8DropIfCikCoverageBelow:P0}"));
                    return;
                }
                var cluster = obs.Select(o => new ClusterObservation(o.Day, o.IsGapper ? 1 : 0, clause.Holds(o.Value) ? 1 : 0)).ToList();
                run.Parts.Add(LiftPart(h, "lift", cluster));
                return;
            }
            case "H10":
            {
                var clause = FeatureCondition.Require(h.Feature, "daysSince424B");
                var rows = (await panel.LoadAsync(split, Decision(h, "09:30"), ct)).Where(r => r.IsCandidate).ToList();
                Describe(run, Keys(rows).ToList(), "candidate days at 09:30 (part 1) and Tier20 gapper days (part 2)", [$"daysSince424B {clause.Operator} {clause.Threshold}"], ["IsGapper (Tier20)", "FwdClose1600 x 100"]);
                var cluster = rows.Select(r => new ClusterObservation(r.DateEt, r.IsGapper ? 1 : 0, clause.Holds(r.Snapshot.DaysSince424B) ? 1 : 0)).ToList();
                run.Parts.Add(LiftPart(h, "lift", cluster));
                var fwd = rows.Where(r => r.IsGapper && clause.Holds(r.Snapshot.DaysSince424B) && r.Label.FwdClose1600 is not null)
                    .Select(r => new ClusterObservation(r.DateEt, r.Label.FwdClose1600!.Value * 100, 1)).ToList();
                run.Parts.Add(BootstrapPart("fwdCloseMeanBelowPct", "mean fwdClose_16:00 (pct) on gapper days with the filing", fwd, DayClusteredBootstrap.Mean, h.MinEffectNumber("fwdCloseMeanBelowPct"), EffectDirection.AtMost, fwd.Count));
                return;
            }
            case "H14":
                await SellTimingAsync(h, run, split, ct);
                return;
            default:
                run.Parts.Add(HypothesisPart.VoidPart("mapping", $"{h.Id} has no group-diff mapping"));
                return;
        }
    }

    /// <summary>Rate ratio of a 0/1 label between the treated and control groups: the ratio's day-clustered interval and the proportion difference's p.</summary>
    private HypothesisPart LiftPart(GapperHypothesis h, string key, List<ClusterObservation> cluster)
    {
        int treated = cluster.Count(o => o.Group == 1);
        var part = BootstrapPart(key, "rate ratio treated/control", cluster, DayClusteredBootstrap.RateRatio, h.MinEffectNumber(key), EffectDirection.AtLeast, treated, cluster.Count - treated, withP: false);
        if (part.Verdict == HypothesisVerdict.Void) return part;
        var diff = Boot(cluster, DayClusteredBootstrap.MeanDifference);
        part.RawP = diff.PValueTwoSided;
        part.Statistic["treatedRate"] = cluster.Where(o => o.Group == 1).Select(o => o.Value).DefaultIfEmpty(double.NaN).Average();
        part.Statistic["controlRate"] = cluster.Where(o => o.Group == 0).Select(o => o.Value).DefaultIfEmpty(double.NaN).Average();
        part.Statistic["proportionDifference"] = diff.Estimate;
        part.Statistic["pRule"] = "day-clustered bootstrap of the proportion difference";
        return part;
    }

    /// <summary>H14: selling at the 09:30 open against selling at the 09:15 close, each net of the frozen per-side cost at its own time, paired per Tier50 event.</summary>
    private async Task SellTimingAsync(GapperHypothesis h, HypothesisRun run, string split, CancellationToken ct)
    {
        var (multiplier, source) = GapperSpreadMultiplier.Resolve(s.Paths, P);
        if (multiplier is null)
        {
            run.Parts.Add(HypothesisPart.VoidPart("meanPairedDifferencePct", "cost model unavailable: " + source));
            return;
        }
        var early = Decision(h, "09:15");
        var earlyRows = (await panel.LoadAsync(split, early, ct)).Where(r => r.IsTier50).ToList();
        var lateRows = (await panel.LoadAsync(split, "09:30", ct)).Where(r => r.IsTier50 && r.Label.EntryRef is > 0).ToDictionary(r => (r.Symbol, r.DateEt));
        var earlyAt = GapperStudyParams.ParseEt(early);
        var store = await panel.StoreAsync(ct);
        var notional = GapperSpreadMultiplier.TradeNotional(P) ?? throw new InvalidDataException("params.json has no costModel.impact.tradeNotionalUsd.");
        var obs = new List<ClusterObservation>();
        var keys = new List<(string, DateOnly)>();
        double costs = 0;
        foreach (var r in earlyRows)
        {
            if (!lateRows.TryGetValue((r.Symbol, r.DateEt), out var late)) continue;
            var bars = store.ReadDay(r.Symbol, r.DateEt);
            var earlyClose = MinuteBarMath.CloseAsOf(bars, MinuteBarMath.ToUtc(r.DateEt, earlyAt));
            if (earlyClose is not > 0) continue;
            var sigma = GapperCostModel.Sigma(await panel.PriorClosesAsync(r.Symbol, r.DateEt, P.Features.Adv20Sessions, ct), P.Features.Adv20Sessions);
            var cs = r.Extras.Number(PriorDaysCorwinSchultzFeature.FeatureName);
            double Side(double pmCumVol, decimal price) => GapperCostModel.Estimate(new GapperCostInputs(cs, multiplier.Value, pmCumVol, sigma, (double)price, notional, GapperSpreadMultiplier.ImpactConstant(P), P.CostModel.RoundTripFloor)).RoundTrip / 2;
            double sellEarly = (double)earlyClose.Value * (1 - Side(r.Snapshot.PmCumVol, earlyClose.Value));
            double sellLate = (double)late.Label.EntryRef!.Value * (1 - Side(late.Snapshot.PmCumVol, late.Label.EntryRef.Value));
            obs.Add(new ClusterObservation(r.DateEt, (sellLate - sellEarly) / (double)earlyClose.Value * 100, 1));
            keys.Add((r.Symbol, r.DateEt));
            costs += Side(r.Snapshot.PmCumVol, earlyClose.Value) + Side(late.Snapshot.PmCumVol, late.Label.EntryRef.Value);
        }
        Describe(run, keys, "Tier50 gapper days with a 09:15 close and a 09:30 open", ["csSpreadPrior", "PmCumVol at 09:15 and 09:30", "sigma20"], ["09:30 open vs 09:15 close, net of the frozen per-side cost, pct of the 09:15 close"]);
        var part = BootstrapPart("meanPairedDifferencePct", "meanPairedDifferencePct", obs, DayClusteredBootstrap.Mean, h.MinEffectNumber("meanPairedDifferencePct"), EffectDirection.AtLeast, obs.Count);
        part.Statistic["spreadMultiplier"] = multiplier;
        part.Statistic["spreadMultiplierSource"] = source;
        part.Statistic["meanPerSideCostBothLegs"] = obs.Count == 0 ? double.NaN : costs / obs.Count;
        run.Parts.Add(part);
    }

    // ── F1: auc-delong (H11) ─────────────────────────────────────────────

    private static readonly string[] DetectorFeatures =
    [
        "GapPct", "LogPmCumVol", "VolRatioTod", "OwnProfileRatio", "Velocity1m", "Velocity3m", "Velocity5m", "Velocity15m", "Acceleration", "VolumeRoc",
        "TradesProxy", "TradesRoc", "SpreadProxy", "DistToPmHighPct", "MinsSinceFirstAbnormal",
        "AbnormalReturnD1", "AbnormalReturnD2", "AbnormalReturnD3", "AbnormalReturnD4", "AbnormalReturnD5",
        "VolumeRatioD1", "VolumeRatioD2", "VolumeRatioD3", "VolumeRatioD4", "VolumeRatioD5",
        "DaysSinceReverseSplit", "DaysSince424B", "DaysSinceS1",
    ];

    private static double[] DetectorRow(PanelRow r)
    {
        var x = r.Snapshot;
        static double N(double? v) => v ?? double.NaN;
        return
        [
            N(x.GapPct), Math.Log(1 + Math.Max(0, x.PmCumVol)), N(x.VolRatioTod), N(x.OwnProfileRatio), N(x.Velocity1m), N(x.Velocity3m), N(x.Velocity5m), N(x.Velocity15m), N(x.Acceleration), N(x.VolumeRoc),
            x.TradesProxy, N(r.Extras.Number(GapperSnapshotExtras.TradesRoc)), N(x.SpreadProxy), N(x.DistToPmHighPct), N(x.MinsSinceFirstAbnormal),
            N(x.AbnormalReturnD1), N(x.AbnormalReturnD2), N(x.AbnormalReturnD3), N(x.AbnormalReturnD4), N(x.AbnormalReturnD5),
            N(x.VolumeRatioD1), N(x.VolumeRatioD2), N(x.VolumeRatioD3), N(x.VolumeRatioD4), N(x.VolumeRatioD5),
            N(x.DaysSinceReverseSplit), N(x.DaysSince424B), N(x.DaysSinceS1),
        ];
    }

    private async Task AucAsync(GapperHypothesis h, HypothesisRun run, string split, CancellationToken ct)
    {
        var decision = Decision(h, "08:00");
        var benchmark = FeatureCondition.Require(h.Feature, "pubToTMinutes");
        var rows = (await panel.LoadAsync(split, decision, ct)).Where(r => r.IsCandidate && r.Label.Reach20By0945 is not null).ToList();
        var trainRows = (await TrainRowsAsync(split, decision, rows, ct)).Where(r => r.IsCandidate && r.Label.Reach20By0945 is not null).ToList();
        Describe(run, Keys(rows).ToList(), $"candidate days at {decision}", DetectorFeatures.Append($"benchmark: pubToTMinutes {benchmark.Operator} {benchmark.Threshold}"), ["Reach20By0945"]);
        var minEffect = h.MinEffectNumber("aucDifference");
        var part = new HypothesisPart { Name = "aucDifference", EffectName = "aucDifference", MinEffect = minEffect, Direction = EffectDirection.AtLeast, N = rows.Count, Days = rows.Select(r => r.DateEt).Distinct().Count() };
        if (rows.Count < P.Statistics.MinBucketN || trainRows.Count < P.Statistics.MinBucketN || rows.All(r => r.Label.Reach20By0945 == true) || rows.All(r => r.Label.Reach20By0945 == false))
        {
            part.Verdict = HypothesisVerdict.Void;
            part.Reasons.Add($"{rows.Count} scored rows / {trainRows.Count} training rows (minBucketN {P.Statistics.MinBucketN}), or a single-class label");
            run.Parts.Add(part);
            return;
        }
        var model = LogisticRegression.Fit(DetectorFeatures, trainRows.Select(DetectorRow).ToList(), trainRows.Select(r => r.Label.Reach20By0945!.Value).ToList());
        var detector = rows.Select(r => model.Predict(DetectorRow(r))).ToList();
        var bench = rows.Select(r => benchmark.Holds(r.Snapshot.CatalystAgeMinutes) ? 1.0 : 0.0).ToList();
        var labels = rows.Select(r => r.Label.Reach20By0945!.Value).ToList();
        var cmp = Auc.CompareByDay(detector, bench, labels, rows.Select(r => r.DateEt).ToList(), P.Statistics.BootstrapDraws, P.Statistics.Seed, P.Statistics.CiLevel, MinDays);
        part.Effect = cmp.Difference;
        part.CiLow = double.IsNaN(cmp.Low) ? null : cmp.Low;
        part.CiHigh = double.IsNaN(cmp.High) ? null : cmp.High;
        part.RawP = cmp.PValueTwoSided;
        part.MeetsMinimum = cmp.Difference >= minEffect;
        part.Statistic["aucDetector"] = cmp.AucA;
        part.Statistic["aucBenchmark"] = cmp.AucB;
        part.Statistic["positives"] = cmp.Positives;
        part.Statistic["benchmarkPositiveShare"] = bench.Average();
        part.Statistic["fitOn"] = split is "train" or GapperStudyServices.SmokeSplit ? "this split (in-sample)" : "train";
        part.Statistic["trainRows"] = trainRows.Count;
        part.Statistic["epochs"] = model.Epochs;
        part.Statistic["trainLogLoss"] = model.FinalLoss;
        part.Statistic["weights"] = DetectorFeatures.Select((f, i) => new { feature = f, weight = Math.Round(model.Weights[i], 6) }).ToList();
        part.Statistic["bootstrap"] = "day-block resampling of AUC(detector) - AUC(benchmark)";
        run.Parts.Add(part);
    }

    // ── F4: search-grid-spa (H12) ────────────────────────────────────────

    public static List<GapperRuleConfig> Grid(GapperStudyParams p)
    {
        var g = p.H12Grid;
        var configs = new List<GapperRuleConfig>();
        foreach (var pm in g.PmVolRatioMin)
            foreach (var v3 in g.V3MinPctPerMin)
                foreach (var death in g.Exit.VelocityDeathWindow)
                    foreach (var stop in g.Exit.StopLossPct)
                        foreach (var sellBy in g.Exit.SellByEt)
                            foreach (var vwap in g.Exit.VwapLossExit)
                                configs.Add(new GapperRuleConfig(pm, v3, death, stop, GapperStudyParams.ParseEt(sellBy), vwap));
        if (configs.Count > g.MaxConfigs) throw new InvalidOperationException($"The H12 grid has {configs.Count} configurations, more than params.h12Grid.maxConfigs {g.MaxConfigs}.");
        return configs;
    }

    private sealed record ConfigSummary(string Label, int Trades, int Days, double MeanNetPct, double LowerBoundPct, double Sharpe, double NaiveP, double Q, double Dsr, Dictionary<string, int> Exits);

    private async Task GridAsync(GapperHypothesis h, HypothesisRun run, string split, CancellationToken ct)
    {
        var (multiplier, source) = GapperSpreadMultiplier.Resolve(s.Paths, P);
        var configs = Grid(P);
        var passAt = P.H12Grid.PassAtEt;
        var rows = (await panel.LoadAsync(split, passAt, ct)).Where(r => r.IsCandidate).OrderBy(r => r.DateEt).ThenBy(r => r.Symbol, StringComparer.Ordinal).ToList();
        Describe(run, Keys(rows).ToList(), $"candidate days at {passAt}", ["VolRatioTod", "Velocity3m", "PmCumVol", PriorDaysCorwinSchultzFeature.FeatureName, "sigma20"], ["net return per trade after the frozen cost model"]);
        var minEffect = h.MinEffectNumber("meanNetPctPerTrade");
        var part = new HypothesisPart { Name = "meanNetPctPerTrade", EffectName = "meanNetPctPerTrade", MinEffect = minEffect, Direction = EffectDirection.AtLeast };
        if (multiplier is null)
        {
            part.Verdict = HypothesisVerdict.Void;
            part.Reasons.Add("cost model unavailable: " + source);
            run.Parts.Add(part);
            return;
        }
        var exits = new GapperRuleExitParameters(P.Labels.VelocityDeath.ArmThresholdPctPerMin, P.Labels.VelocityDeath.ConsecutiveNegativeBars, P.Labels.Vwap.MinConsecutiveAboveBeforeLoss);
        var notional = GapperSpreadMultiplier.TradeNotional(P) ?? throw new InvalidDataException("params.json has no costModel.impact.tradeNotionalUsd.");
        double loosestPm = P.H12Grid.PmVolRatioMin.Min(), loosestV3 = P.H12Grid.V3MinPctPerMin.Min();
        var store = await panel.StoreAsync(ct);
        var series = configs.Select(_ => new double[rows.Count]).ToList();
        var trades = configs.Select(_ => new List<(DateOnly Day, double Net, string Reason)>()).ToList();
        int cellsPassing = 0;
        double costSum = 0;
        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            if (!(r.Snapshot.VolRatioTod >= loosestPm && r.Snapshot.Velocity3m >= loosestV3) || r.Label.EntryRef is not > 0) continue;
            var bars = store.ReadDay(r.Symbol, r.DateEt);
            if (bars.Count == 0) continue;
            cellsPassing++;
            var sigma = GapperCostModel.Sigma(await panel.PriorClosesAsync(r.Symbol, r.DateEt, P.Features.Adv20Sessions, ct), P.Features.Adv20Sessions);
            var cost = GapperCostModel.Estimate(new GapperCostInputs(r.Extras.Number(PriorDaysCorwinSchultzFeature.FeatureName), multiplier.Value, r.Snapshot.PmCumVol, sigma,
                (double)r.Label.EntryRef!.Value, notional, GapperSpreadMultiplier.ImpactConstant(P), P.CostModel.RoundTripFloor)).RoundTrip;
            costSum += cost;
            for (int k = 0; k < configs.Count; k++)
            {
                if (!configs[k].Passes(r.Snapshot.VolRatioTod, r.Snapshot.Velocity3m)) continue;
                var trade = GapperRuleSimulator.Simulate(configs[k], bars, r.DateEt, r.Snapshot.DecisionUtc, cost, exits);
                if (trade is null) continue;
                series[k][i] = trade.NetReturn;
                trades[k].Add((r.DateEt, trade.NetReturn, trade.ExitReason));
            }
        }

        var moments = trades.Select(t => ReturnMoments.Of(t.Select(x => x.Net).ToList())).ToList();
        var naiveP = trades.Select(t => MultipleTesting.MeanAboveZeroPValue(t.Select(x => x.Net).ToList())).ToArray();
        var q = MultipleTesting.BenjaminiHochberg(naiveP);
        var sharpes = moments.Select(m => m.Sharpe).ToList();
        double sharpeVariance = sharpes.Count > 1 ? Math.Pow(EvidenceStats.StdDev(sharpes), 2) : 0;
        var dsr = moments.Select(m => MultipleTesting.DeflatedSharpe(m, configs.Count, sharpeVariance)).ToArray();
        var summaries = new List<ConfigSummary>();
        var bounds = new List<BootstrapInterval>();
        for (int k = 0; k < configs.Count; k++)
        {
            var obs = trades[k].Select(t => new ClusterObservation(t.Day, t.Net * 100)).ToList();
            var b = trades[k].Count == 0 ? new BootstrapInterval(double.NaN, double.NaN, double.NaN, 1, 0, 0, 0) : Boot(obs, DayClusteredBootstrap.Mean);
            bounds.Add(b);
            summaries.Add(new ConfigSummary(configs[k].Label, trades[k].Count, trades[k].Select(t => t.Day).Distinct().Count(), b.Estimate, b.Low, moments[k].Sharpe, naiveP[k], q[k], dsr[k],
                trades[k].GroupBy(t => t.Reason).ToDictionary(g => g.Key, g => g.Count())));
        }
        int minTrades = (int)h.MinEffectNumber("minTrades"), minDays = (int)h.MinEffectNumber("minDays");
        double lowerBoundAbove = h.MinEffectNumber("clusteredLowerBoundAbove");
        var rc = rows.Count >= 2 ? RealityCheck.Run(series, Math.Max(100, P.Statistics.PlaceboDraws), 5, P.Statistics.Seed) : null;

        int? selected = null;
        string selection;
        var carried = split is "train" or GapperStudyServices.SmokeSplit ? null : LatestTrainSelection(configs);
        if (carried is { } c) { selected = c; selection = "carried from the latest train run"; }
        else
        {
            var eligible = Enumerable.Range(0, configs.Count).Where(k => summaries[k].Trades >= minTrades && !double.IsNaN(summaries[k].LowerBoundPct)).ToList();
            if (eligible.Count > 0) selected = eligible.OrderByDescending(k => summaries[k].LowerBoundPct).ThenByDescending(k => summaries[k].MeanNetPct).First();
            selection = split is "train" or GapperStudyServices.SmokeSplit ? "highest day-clustered lower bound on this split among configs with enough trades" : "in-sample selection on this split (no train run file found)";
        }
        part.Statistic["configs"] = summaries;
        part.Statistic["configCount"] = configs.Count;
        part.Statistic["cells"] = rows.Count;
        part.Statistic["cellsPassingLoosest"] = cellsPassing;
        part.Statistic["meanRoundTripCost"] = cellsPassing == 0 ? double.NaN : costSum / cellsPassing;
        part.Statistic["spreadMultiplier"] = multiplier;
        part.Statistic["spreadMultiplierSource"] = source;
        part.Statistic["selection"] = selection;
        part.Statistic["whiteP"] = rc?.WhitePValue;
        part.Statistic[HypothesisCorrections.SpaKey] = rc?.SpaPValue;
        part.Statistic["spaBestConfig"] = rc is null ? null : configs[rc.BestConfig].Label;
        part.Statistic["spaResamples"] = rc?.Resamples;
        if (selected is not { } best || summaries[best].Trades == 0)
        {
            part.Verdict = HypothesisVerdict.Void;
            part.Reasons.Add($"no configuration with at least {minTrades} trades on {rows.Count} candidate cells");
            run.Parts.Add(part);
            return;
        }
        var chosen = summaries[best];
        part.N = chosen.Trades;
        part.Days = chosen.Days;
        part.Effect = chosen.MeanNetPct;
        part.CiLow = double.IsNaN(bounds[best].Low) ? null : bounds[best].Low;
        part.CiHigh = double.IsNaN(bounds[best].High) ? null : bounds[best].High;
        part.RawP = rc?.SpaPValue;
        part.MeetsMinimum = chosen.MeanNetPct >= minEffect && chosen.LowerBoundPct > lowerBoundAbove && chosen.Trades >= minTrades && chosen.Days >= minDays;
        part.Statistic["selectedConfig"] = chosen.Label;
        part.Statistic["selectedTrades"] = chosen.Trades;
        part.Statistic["selectedDays"] = chosen.Days;
        part.Statistic["selectedLowerBoundPct"] = chosen.LowerBoundPct;
        part.Statistic[HypothesisCorrections.DsrKey] = chosen.Dsr;
        part.Statistic["selectedQ"] = chosen.Q;
        part.Statistic["selectedExits"] = chosen.Exits;
        part.Statistic["worthTrading"] = new { minTrades, minDays, lowerBoundAbove, meanNetPctPerTrade = minEffect };
        run.Parts.Add(part);
    }

    /// <summary>The configuration the latest train run of H12 selected, as an index into the grid; null without a train run file.</summary>
    private int? LatestTrainSelection(List<GapperRuleConfig> configs) => LatestTrainSelection(configs.Select(c => c.Label).ToList(), "H12");

    private int? LatestTrainSelection(IReadOnlyList<string> labels, string id)
    {
        if (!Directory.Exists(s.Paths.RunsDir)) return null;
        var latest = Directory.GetFiles(s.Paths.RunsDir, $"test-{id}-train-*.json").OrderByDescending(f => f, StringComparer.Ordinal)
            .Select(f => HypothesisRun.FromJson(File.ReadAllText(f))).FirstOrDefault(r => r is not null);
        var label = latest?.Parts.FirstOrDefault()?.Statistic.GetValueOrDefault("selectedConfig")?.ToString();
        if (label is null) return null;
        int idx = labels.ToList().FindIndex(c => c == label);
        return idx < 0 ? null : idx;
    }
}
