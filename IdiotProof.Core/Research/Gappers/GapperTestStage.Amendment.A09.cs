using IdiotProof.Evidence.Evaluation;
using IdiotProof.Core.Research.Gappers.Explore;

namespace IdiotProof.Core.Research.Gappers;

/// <summary>
/// Amendment 09 (IP-T605/IP-T616): A09-H1 and A09-H2, the late-runner move-start hypotheses, scored on a split
/// other than train. Both fit their model(s) on train (the exploratory round's own <c>GapperExploreStage.A09RowsAsync
/// ("train", ...)</c> rows, never refit on the split under test -- scoring a train-fit model out of sample is the
/// same convention <see cref="PopulationAucAsync"/> (H11p) already uses), then score the split this run was asked
/// for. One Benjamini-Hochberg family ("A09"), as the train run itself used; no Holm (BasedOn is empty, so the base
/// ApplyAll never adds these ids to its Holm set).
/// </summary>
public sealed partial class GapperTestStage
{
    /// <summary>Only a day that reached its move start m0 has a label y and a feature vector X (GapperExploreStage.A09ComputeDay); every other day stays default-constructed and must not reach the model.</summary>
    private static List<GapperExploreStage.A09Day> WithM0(List<GapperExploreStage.A09Day> all) => all.Where(d => d.M0 >= 0).ToList();

    private async Task<(List<GapperExploreStage.A09Day> TrainRows, List<GapperExploreStage.A09Day> ScoreRows)> A09InputsAsync(string split, CancellationToken ct)
    {
        var (trainAll, _, _, _, _, _, _) = await explore.A09RowsAsync("train", 4, ct);
        var trainRows = WithM0(trainAll);
        if (split == "train") return (trainRows, trainRows);
        var (scoreAll, _, _, _, _, _, _) = await explore.A09RowsAsync(split, 4, ct);
        return (trainRows, WithM0(scoreAll));
    }

    /// <summary>
    /// A09-H1: weighted AUC of the eleven features at the move start, fit on train, minus a model of
    /// gainAtStart alone, scored on <paramref name="split"/>; day-clustered bootstrap.
    /// </summary>
    private async Task A09AucAsync(GapperHypothesis h, HypothesisRun run, string split, CancellationToken ct)
    {
        var (trainRows, scoreRows) = await A09InputsAsync(split, ct);
        Describe(run, scoreRows.Select(r => (r.Symbol, r.Day)).ToList(), $"late-runner move starts, {split} split", GapperExploreStage.A09FeatureNames, ["y"]);
        var minEffect = h.MinEffectNumber("aucDifference");
        var part = new HypothesisPart { Name = "aucDifference", EffectName = "aucDifference", MinEffect = minEffect, Direction = EffectDirection.AtLeast, N = scoreRows.Count, Days = scoreRows.Select(r => r.Day).Distinct().Count() };
        if (trainRows.Count < P.Statistics.MinBucketN || scoreRows.Count < P.Statistics.MinBucketN || scoreRows.All(r => r.Y) || scoreRows.All(r => !r.Y))
        {
            part.Verdict = HypothesisVerdict.Void;
            part.Reasons.Add($"{trainRows.Count} training rows / {scoreRows.Count} scored rows (minBucketN {P.Statistics.MinBucketN}), or a single-class label on {split}");
            run.Parts.Add(part);
            return;
        }
        var (names, kept, _, indicators, _) = GapperExploreStage.A09Features(trainRows, trainRows.Count);
        double[] Full(GapperExploreStage.A09Day r) => GapperExploreStage.A09FullVector(r, kept, indicators);
        double[] Base(GapperExploreStage.A09Day r) => [r.X[0]];
        var mFull = LogisticRegression.FitWeighted(names, trainRows.Select(Full).ToList(), trainRows.Select(r => r.Y).ToList(), trainRows.Select(r => r.Weight).ToList());
        var mBase = LogisticRegression.FitWeighted(["gainAtStart"], trainRows.Select(Base).ToList(), trainRows.Select(r => r.Y).ToList(), trainRows.Select(r => r.Weight).ToList());
        var fullScores = scoreRows.Select(r => mFull.Predict(Full(r))).ToList();
        var baseScores = scoreRows.Select(r => mBase.Predict(Base(r))).ToList();
        var labels = scoreRows.Select(r => r.Y).ToList();
        var weights = scoreRows.Select(r => r.Weight).ToList();
        var days = scoreRows.Select(r => r.Day).ToList();
        var cmp = WeightedAuc.CompareByDay(fullScores, baseScores, labels, weights, days, P.Statistics.BootstrapDraws, P.Statistics.Seed, P.Statistics.CiLevel, MinDays);
        part.Effect = cmp.Difference;
        part.CiLow = double.IsNaN(cmp.Low) ? null : cmp.Low;
        part.CiHigh = double.IsNaN(cmp.High) ? null : cmp.High;
        part.RawP = cmp.PValueTwoSided;
        part.MeetsMinimum = cmp.Difference >= minEffect;
        part.Statistic["aucFull"] = cmp.AucA;
        part.Statistic["aucGainAtStartOnly"] = cmp.AucB;
        part.Statistic["positives"] = cmp.Positives;
        part.Statistic["fitOn"] = "train";
        part.Statistic["scoredOn"] = split;
        part.Statistic["trainRows"] = trainRows.Count;
        part.Statistic["featuresKept"] = names;
        part.Statistic["bootstrap"] = "day-block resampling of weighted AUC(full) - weighted AUC(gainAtStart alone)";
        run.Parts.Add(part);
    }

    /// <summary>A09-H2: weighted lift of reaching +20% given a fresh catalyst right at the move start, on <paramref name="split"/>.</summary>
    private async Task A09LiftAsync(GapperHypothesis h, HypothesisRun run, string split, CancellationToken ct)
    {
        var (_, scoreRows) = await A09InputsAsync(split, ct);
        Describe(run, scoreRows.Select(r => (r.Symbol, r.Day)).ToList(), $"late-runner move starts, {split} split", ["freshCatalyst (X[5])"], ["y"]);
        var minEffect = h.MinEffectNumber("lift");
        var part = new HypothesisPart { Name = "lift", EffectName = "lift", MinEffect = minEffect, Direction = EffectDirection.AtLeast };
        if (scoreRows.Count < P.Statistics.MinBucketN)
        {
            part.Verdict = HypothesisVerdict.Void;
            part.Reasons.Add($"{scoreRows.Count} rows on {split} (minBucketN {P.Statistics.MinBucketN})");
            run.Parts.Add(part);
            return;
        }
        var obs = scoreRows.Select(r => new WeightedOutcome(r.Day, r.X[5] > 0.5, r.Y, r.Weight)).ToList();
        var lift = WeightedPopulation.Lift(obs, P.Statistics.BootstrapDraws, P.Statistics.Seed, P.Statistics.CiLevel, MinDays);
        if (lift.GroupRows < P.Statistics.MinBucketN || double.IsNaN(lift.BaseRate) || lift.BaseRate <= 0)
        {
            part.Verdict = HypothesisVerdict.Void;
            part.Reasons.Add($"{lift.GroupRows} rows with a fresh catalyst at the move start (minBucketN {P.Statistics.MinBucketN}) or no base rate on {split}");
            run.Parts.Add(part);
            return;
        }
        part.N = lift.GroupRows;
        part.Days = obs.Where(o => o.InGroup).Select(o => o.Day).Distinct().Count();
        part.Effect = lift.Lift;
        part.CiLow = double.IsNaN(lift.LiftLow) ? null : lift.LiftLow;
        part.CiHigh = double.IsNaN(lift.LiftHigh) ? null : lift.LiftHigh;
        part.RawP = lift.PValueTwoSided;
        part.MeetsMinimum = lift.Lift >= minEffect;
        part.Statistic["rateFresh"] = lift.Rate;
        part.Statistic["rateNone"] = lift.BaseRate;
        part.Statistic["scoredOn"] = split;
        part.Statistic["pRule"] = "day-clustered bootstrap of the weighted group rate minus the weighted base rate";
        run.Parts.Add(part);
    }
}
