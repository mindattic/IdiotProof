using System.Diagnostics;
using IdiotProof.Evidence.Evaluation;
using IdiotProof.Evidence.Gappers;

namespace IdiotProof.Core.Research.Gappers;

/// <summary>
/// Amendment 03 (IP-T451): a Corobs look-alike detector on the 08:00 population panel, scored apart
/// from every existing score. H21 asks whether it beats the catalyst-latency benchmark alone, H22
/// whether it adds to the H11p logistic detector, H23 how it compares with that detector head to head,
/// H24 whether its novelty score finds gappers. All four read one cached computation per split.
/// </summary>
public sealed partial class GapperTestStage
{
    public const string CorobsAucTest = "corobs-auc";
    public const string CorobsPluginTest = "corobs-plugin-auc";
    public const string CorobsVersusLogisticTest = "corobs-vs-logistic-auc";
    public const string CorobsNoveltyTest = "corobs-novelty-lift";
    public const string LogitCorobsFeature = "LogitCorobs";
    private const int CorobsK = 100;
    private const double CorobsPriorWeight = 10;

    private readonly Dictionary<string, CorobsPanel> corobsPanels = new(StringComparer.Ordinal);

    /// <summary>Every score of amendment 03 for one split's rows, plus the training rows' leave-one-day-out scores.</summary>
    private sealed record CorobsPanel(
        List<PanelRow> Rows, double[] Corobs, double[] Novelty, double[] Logistic, double[] Combined, double[] Benchmark,
        double[] TrainNovelty, List<PanelRow> TrainRows, CorobsLookAlike Model, LogisticModel CombinedModel, double Seconds, bool InSample);

    private static double Logit(double p)
    {
        p = Math.Clamp(p, 1e-6, 1 - 1e-6);
        return Math.Log(p / (1 - p));
    }

    private static double[] WithLogit(double[] row, double score) => [.. row, Logit(score)];

    private async Task<CorobsPanel?> CorobsPanelAsync(string split, CancellationToken ct)
    {
        if (corobsPanels.TryGetValue(split, out var cached)) return cached;
        const string decision = "08:00";
        static bool Keep(PanelRow r) => r.InPopulation && r.Label.Reach20By0945 is not null;
        static IOrderedEnumerable<PanelRow> Ordered(IEnumerable<PanelRow> rows) => rows.OrderBy(r => r.DateEt).ThenBy(r => r.Symbol, StringComparer.Ordinal);
        var rows = Ordered((await panel.LoadAsync(split, decision, ct)).Where(Keep)).ToList();
        var trainRows = Ordered((await TrainRowsAsync(split, decision, rows, ct)).Where(Keep)).ToList();
        if (rows.Count < P.Statistics.MinBucketN || trainRows.Count < P.Statistics.MinBucketN || rows.All(r => r.Label.Reach20By0945 == true) || rows.All(r => r.Label.Reach20By0945 == false))
            return null;

        var clock = Stopwatch.StartNew();
        var xTrain = trainRows.Select(DetectorRow).ToList();
        var yTrain = trainRows.Select(r => r.Label.Reach20By0945!.Value).ToList();
        var wTrain = trainRows.Select(r => r.Weight).ToList();
        var dTrain = trainRows.Select(r => r.DateEt.DayNumber).ToList();
        var model = CorobsLookAlike.Fit(DetectorFeatures, xTrain, yTrain, wTrain, dTrain, P.Statistics.Seed, CorobsK, CorobsPriorWeight);
        var trainScores = model.Score(xTrain, dTrain);

        bool inSample = split == "train" || split == GapperStudyServices.SmokeSplit;
        var xRows = inSample ? xTrain : rows.Select(DetectorRow).ToList();
        if (inSample) rows = trainRows;
        var rowScores = inSample ? trainScores : model.Score(xRows);

        var logistic = LogisticRegression.FitWeighted(DetectorFeatures, xTrain, yTrain, wTrain);
        var combinedFeatures = DetectorFeatures.Append(LogitCorobsFeature).ToList();
        var combined = LogisticRegression.FitWeighted(combinedFeatures, xTrain.Select((x, i) => WithLogit(x, trainScores[i].Score)).ToList(), yTrain, wTrain);
        var benchmark = FeatureCondition.Require(GapperHypothesisSet.Load(s.Paths).Find("H11").Feature, "pubToTMinutes");

        var result = new CorobsPanel(
            rows,
            rowScores.Select(c => c.Score).ToArray(),
            rowScores.Select(c => c.Novelty).ToArray(),
            xRows.Select(x => logistic.Predict(x)).ToArray(),
            xRows.Select((x, i) => combined.Predict(WithLogit(x, rowScores[i].Score))).ToArray(),
            rows.Select(r => benchmark.Holds(r.Snapshot.CatalystAgeMinutes) ? 1.0 : 0.0).ToArray(),
            trainScores.Select(c => c.Novelty).ToArray(),
            trainRows, model, combined, clock.Elapsed.TotalSeconds, inSample);
        corobsPanels[split] = result;
        return result;
    }

    private Task DispatchCorobsAsync(GapperHypothesis h, AmendedHypothesis a, HypothesisRun run, string split, CancellationToken ct) => a.Test switch
    {
        CorobsAucTest => CorobsAucAsync(h, run, split, c => c.Corobs, "corobs", c => c.Benchmark, "benchmark", ct),
        CorobsPluginTest => CorobsAucAsync(h, run, split, c => c.Combined, "combined", c => c.Logistic, "logistic", ct),
        CorobsVersusLogisticTest => CorobsAucAsync(h, run, split, c => c.Corobs, "corobs", c => c.Logistic, "logistic", ct),
        CorobsNoveltyTest => CorobsNoveltyAsync(h, run, split, ct),
        _ => Unknown(h, run),
    };

    private static bool IsCorobsTest(string? test) => test is CorobsAucTest or CorobsPluginTest or CorobsVersusLogisticTest or CorobsNoveltyTest;

    private void DescribeCorobs(HypothesisRun run, CorobsPanel? c, List<PanelRow> fallback, string what)
    {
        var rows = c?.Rows ?? fallback;
        Describe(run, Keys(rows).ToList(), PopulationDescription(rows, "population at 08:00"),
            DetectorFeatures.Select(f => $"{f} (Corobs ply, {CorobsLookAlike.DimensionsPerPly} dims)").Append(what), ["Reach20By0945"]);
    }

    private static void CorobsStatistics(HypothesisPart part, CorobsPanel c)
    {
        var labels = c.Rows.Select(r => r.Label.Reach20By0945!.Value).ToList();
        var weights = c.Rows.Select(r => r.Weight).ToList();
        part.Statistic["aucBenchmark"] = WeightedAuc.Compute(c.Benchmark, labels, weights);
        part.Statistic["aucLogistic"] = WeightedAuc.Compute(c.Logistic, labels, weights);
        part.Statistic["aucCorobs"] = WeightedAuc.Compute(c.Corobs, labels, weights);
        part.Statistic["aucCombined"] = WeightedAuc.Compute(c.Combined, labels, weights);
        part.Statistic["fitOn"] = c.InSample ? "this split (training rows scored leave-one-day-out; logistic in-sample)" : "train";
        part.Statistic["trainRows"] = c.TrainRows.Count;
        part.Statistic["dimensions"] = c.Model.Dimensions;
        part.Statistic["k"] = c.Model.K;
        part.Statistic["priorWeight"] = c.Model.PriorWeight;
        part.Statistic["trainBaseRate"] = c.Model.BaseRate;
        part.Statistic["logitCorobsCoefficient"] = Math.Round(c.CombinedModel.Weights[^1], 6);
        part.Statistic["scoringSeconds"] = Math.Round(c.Seconds, 1);
        part.Statistic["ranges"] = c.Model.Features.Select((f, i) => new { feature = f, low = c.Model.Ranges[i].Low, high = c.Model.Ranges[i].High }).ToList();
    }

    private async Task CorobsAucAsync(GapperHypothesis h, HypothesisRun run, string split, Func<CorobsPanel, double[]> a, string aName, Func<CorobsPanel, double[]> b, string bName, CancellationToken ct)
    {
        var c = await CorobsPanelAsync(split, ct);
        DescribeCorobs(run, c, [], $"{aName} vs {bName}");
        var minEffect = h.MinEffectNumber("aucDifference");
        var part = new HypothesisPart { Name = "aucDifference", EffectName = "aucDifference", MinEffect = minEffect, Direction = EffectDirection.AtLeast };
        if (c is null)
        {
            part.Verdict = HypothesisVerdict.Void;
            part.Reasons.Add($"fewer than minBucketN {P.Statistics.MinBucketN} scored or training rows at 08:00, or a single-class label");
            run.Parts.Add(part);
            return;
        }
        PopulationStatistics(part, c.Rows);
        part.N = c.Rows.Count;
        part.Days = c.Rows.Select(r => r.DateEt).Distinct().Count();
        var cmp = WeightedAuc.CompareByDay(a(c), b(c), c.Rows.Select(r => r.Label.Reach20By0945!.Value).ToList(), c.Rows.Select(r => r.Weight).ToList(),
            c.Rows.Select(r => r.DateEt).ToList(), P.Statistics.BootstrapDraws, P.Statistics.Seed, P.Statistics.CiLevel, MinDays);
        part.Effect = cmp.Difference;
        part.CiLow = double.IsNaN(cmp.Low) ? null : cmp.Low;
        part.CiHigh = double.IsNaN(cmp.High) ? null : cmp.High;
        part.RawP = cmp.PValueTwoSided;
        part.MeetsMinimum = cmp.Difference >= minEffect;
        part.Statistic["compared"] = $"{aName} - {bName}";
        part.Statistic["positives"] = cmp.Positives;
        CorobsStatistics(part, c);
        part.Statistic["bootstrap"] = $"day-block resampling of weighted AUC({aName}) - weighted AUC({bName})";
        run.Parts.Add(part);
    }

    private async Task CorobsNoveltyAsync(GapperHypothesis h, HypothesisRun run, string split, CancellationToken ct)
    {
        var c = await CorobsPanelAsync(split, ct);
        DescribeCorobs(run, c, [], "novelty (top decile, weighted train edge)");
        const string effectName = "precisionOverBaseRate";
        var part = new HypothesisPart { Name = effectName, EffectName = effectName, MinEffect = h.MinEffectNumber(effectName), Direction = EffectDirection.AtLeast };
        if (c is null)
        {
            part.Verdict = HypothesisVerdict.Void;
            part.Reasons.Add($"fewer than minBucketN {P.Statistics.MinBucketN} scored or training rows at 08:00, or a single-class label");
            run.Parts.Add(part);
            return;
        }
        PopulationStatistics(part, c.Rows);
        var edge = CorobsLookAlike.WeightedQuantile(c.TrainRows.Select((r, i) => (c.TrainNovelty[i], r.Weight)).Where(v => double.IsFinite(v.Item1)).ToList(), 0.90);
        var obs = c.Rows.Select((r, i) => new WeightedOutcome(r.DateEt, c.Novelty[i] > edge, r.Label.Reach20By0945!.Value, r.Weight)).ToList();
        var lift = WeightedPopulation.Lift(obs, P.Statistics.BootstrapDraws, P.Statistics.Seed, P.Statistics.CiLevel, MinDays);
        if (lift.GroupRows < P.Statistics.MinBucketN || double.IsNaN(lift.BaseRate) || lift.BaseRate <= 0)
        {
            part.Verdict = HypothesisVerdict.Void;
            part.Reasons.Add($"{lift.GroupRows} rows in the top novelty decile (minBucketN {P.Statistics.MinBucketN}) or no base rate");
            run.Parts.Add(part);
            return;
        }
        part.N = lift.GroupRows;
        part.Days = obs.Where(o => o.InGroup).Select(o => o.Day).Distinct().Count();
        part.Effect = lift.Lift;
        part.CiLow = double.IsNaN(lift.LiftLow) ? null : lift.LiftLow;
        part.CiHigh = double.IsNaN(lift.LiftHigh) ? null : lift.LiftHigh;
        part.RawP = lift.PValueTwoSided;
        part.MeetsMinimum = part.Effect >= part.MinEffect;
        part.Statistic["groupRate"] = lift.Rate;
        part.Statistic["baseRate"] = lift.BaseRate;
        part.Statistic["groupWeight"] = lift.GroupWeight;
        part.Statistic["noveltyEdge"] = edge;
        CorobsStatistics(part, c);
        part.Statistic["pRule"] = "day-clustered bootstrap of the weighted group rate minus the weighted base rate";
        run.Parts.Add(part);
    }
}
