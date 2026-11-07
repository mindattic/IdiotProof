using IdiotProof.Evidence;
using IdiotProof.Evidence.Evaluation;
using IdiotProof.Evidence.Gappers;
using IdiotProof.Evidence.Search;

namespace IdiotProof.Core.Research.Gappers;

/// <summary>
/// Amendment 01 (IP-T448): H1p, H9p, H11p and H12p re-ask H1, H9, H11 and H12 over the population
/// (every candidate day at weight 1 plus the seeded population sample at weight 1/rate) with the
/// frozen thresholds, feature reading, simulator and cost model. Only the population, the weights and
/// the weighted statistics differ; no frozen hypothesis reads a sample row.
/// </summary>
public sealed partial class GapperTestStage
{
    private Task DispatchAmendedAsync(GapperHypothesis h, AmendedHypothesis a, HypothesisRun run, string split, CancellationToken ct) => a.BasedOn.ToUpperInvariant() switch
    {
        "" => DispatchNewQuestionAsync(h, a, run, split, ct),
        "H1" or "H9" => PopulationLiftAsync(h, a, run, split, ct),
        "H11" => PopulationAucAsync(h, run, split, ct),
        "H12" => PopulationGridAsync(h, run, split, ct),
        _ => Unknown(h, run),
    };

    private static string PopulationDescription(IReadOnlyCollection<PanelRow> rows, string what) =>
        $"{what}: {rows.Count(r => r.IsCandidate):N0} candidate days (weight 1) and {rows.Count(r => r.IsPopulationSample):N0} population-sample days (weight {rows.FirstOrDefault(r => r.IsPopulationSample)?.Weight ?? 0:0})";

    private static void PopulationStatistics(HypothesisPart part, IReadOnlyCollection<PanelRow> rows)
    {
        part.Statistic["candidateRows"] = rows.Count(r => r.IsCandidate);
        part.Statistic["sampleRows"] = rows.Count(r => r.IsPopulationSample);
        part.Statistic["populationWeight"] = rows.Sum(r => r.Weight);
    }

    // ── H1p / H9p: population-weighted bucket lift ───────────────────────

    private async Task PopulationLiftAsync(GapperHypothesis h, AmendedHypothesis a, HypothesisRun run, string split, CancellationToken ct)
    {
        bool isH1 = a.BasedOn.Equals("H1", StringComparison.OrdinalIgnoreCase);
        var decision = Decision(h, isH1 ? "08:00" : "09:00");
        List<PanelRow> rows, trainRows;
        List<WeightedOutcome> obs;
        string effectName;
        if (isH1)
        {
            static bool Keep(PanelRow r) => r.InPopulation && r.Snapshot.VolRatioTod is not null && r.Snapshot.PmCumVol > 0 && r.Label.Reach20By0945 is not null;
            rows = (await panel.LoadAsync(split, decision, ct)).Where(Keep).ToList();
            trainRows = (await TrainRowsAsync(split, decision, rows, ct)).Where(Keep).ToList();
            var binner = new QuantileBinner(WeightedPopulation.Replicate(trainRows.Select(r => (r.Snapshot.VolRatioTod!.Value, r.Weight))), P.Statistics.BucketBins);
            int bucketOfInterest = P.Statistics.BucketBins - 1;
            obs = rows.Select(r => new WeightedOutcome(r.DateEt, binner.Bin(r.Snapshot.VolRatioTod!.Value) == bucketOfInterest, r.Label.Reach20By0945!.Value, r.Weight)).ToList();
            effectName = "precisionOverBaseRate";
            run.Inputs.FeatureColumns = ["VolRatioTod (top quintile, weighted train edges)"];
            run.Inputs.LabelColumns = ["Reach20By0945"];
            Describe(run, Keys(rows).ToList(), PopulationDescription(rows, $"population with premarket volume by {decision}"), run.Inputs.FeatureColumns, run.Inputs.LabelColumns);
        }
        else
        {
            var clauses = FeatureCondition.Parse(h.Feature);
            var labelClause = FeatureCondition.Require(h.Label, "fwdMax_09:45");
            rows = (await panel.LoadAsync(split, decision, ct)).Where(r => r.InPopulation && r.Label.FwdMax0945 is not null).ToList();
            trainRows = rows;
            bool Passes(PanelRow r) => clauses.All(c => c.Holds(r.Column(c.Name)));
            obs = rows.Select(r => new WeightedOutcome(r.DateEt, Passes(r), labelClause.Holds(r.Label.FwdMax0945), r.Weight)).ToList();
            effectName = "lift";
            Describe(run, Keys(rows).ToList(), PopulationDescription(rows, $"population at {decision}"), clauses.Select(c => $"{c.Name} {c.Operator} {c.Threshold}"), [$"FwdMax0945 {labelClause.Operator} {labelClause.Threshold}"]);
        }

        var lift = WeightedPopulation.Lift(obs, P.Statistics.BootstrapDraws, P.Statistics.Seed, P.Statistics.CiLevel, MinDays);
        var part = new HypothesisPart { Name = effectName, EffectName = effectName, MinEffect = h.MinEffectNumber(effectName), Direction = EffectDirection.AtLeast };
        PopulationStatistics(part, rows);
        if (lift.GroupRows < P.Statistics.MinBucketN || double.IsNaN(lift.BaseRate) || lift.BaseRate <= 0)
        {
            part.Verdict = HypothesisVerdict.Void;
            part.Reasons.Add($"{lift.GroupRows} rows in the tested group (minBucketN {P.Statistics.MinBucketN}) or no base rate");
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
        part.Statistic["trainRows"] = trainRows.Count;
        part.Statistic["edgesFitOn"] = isH1 ? (split is "train" ? "this split (in-sample)" : "train") : "none (pass flag)";
        part.Statistic["pRule"] = "day-clustered bootstrap of the weighted group rate minus the weighted base rate";
        run.Parts.Add(part);
    }

    // ── H11p: weighted detector against the catalyst-latency benchmark ────

    private async Task PopulationAucAsync(GapperHypothesis h, HypothesisRun run, string split, CancellationToken ct)
    {
        var decision = Decision(h, "08:00");
        var benchmark = FeatureCondition.Require(h.Feature, "pubToTMinutes");
        static bool Keep(PanelRow r) => r.InPopulation && r.Label.Reach20By0945 is not null;
        var rows = (await panel.LoadAsync(split, decision, ct)).Where(Keep).ToList();
        var trainRows = (await TrainRowsAsync(split, decision, rows, ct)).Where(Keep).ToList();
        Describe(run, Keys(rows).ToList(), PopulationDescription(rows, $"population at {decision}"), DetectorFeatures.Append($"benchmark: pubToTMinutes {benchmark.Operator} {benchmark.Threshold}"), ["Reach20By0945"]);
        var minEffect = h.MinEffectNumber("aucDifference");
        var part = new HypothesisPart { Name = "aucDifference", EffectName = "aucDifference", MinEffect = minEffect, Direction = EffectDirection.AtLeast, N = rows.Count, Days = rows.Select(r => r.DateEt).Distinct().Count() };
        PopulationStatistics(part, rows);
        if (rows.Count < P.Statistics.MinBucketN || trainRows.Count < P.Statistics.MinBucketN || rows.All(r => r.Label.Reach20By0945 == true) || rows.All(r => r.Label.Reach20By0945 == false))
        {
            part.Verdict = HypothesisVerdict.Void;
            part.Reasons.Add($"{rows.Count} scored rows / {trainRows.Count} training rows (minBucketN {P.Statistics.MinBucketN}), or a single-class label");
            run.Parts.Add(part);
            return;
        }
        var model = LogisticRegression.FitWeighted(DetectorFeatures, trainRows.Select(DetectorRow).ToList(), trainRows.Select(r => r.Label.Reach20By0945!.Value).ToList(), trainRows.Select(r => r.Weight).ToList());
        var detector = rows.Select(r => model.Predict(DetectorRow(r))).ToList();
        var bench = rows.Select(r => benchmark.Holds(r.Snapshot.CatalystAgeMinutes) ? 1.0 : 0.0).ToList();
        var labels = rows.Select(r => r.Label.Reach20By0945!.Value).ToList();
        var weights = rows.Select(r => r.Weight).ToList();
        var cmp = WeightedAuc.CompareByDay(detector, bench, labels, weights, rows.Select(r => r.DateEt).ToList(), P.Statistics.BootstrapDraws, P.Statistics.Seed, P.Statistics.CiLevel, MinDays);
        part.Effect = cmp.Difference;
        part.CiLow = double.IsNaN(cmp.Low) ? null : cmp.Low;
        part.CiHigh = double.IsNaN(cmp.High) ? null : cmp.High;
        part.RawP = cmp.PValueTwoSided;
        part.MeetsMinimum = cmp.Difference >= minEffect;
        part.Statistic["aucDetector"] = cmp.AucA;
        part.Statistic["aucBenchmark"] = cmp.AucB;
        part.Statistic["positives"] = cmp.Positives;
        part.Statistic["benchmarkPositiveWeightedShare"] = bench.Select((b, i) => b * weights[i]).Sum() / weights.Sum();
        part.Statistic["fitOn"] = split is "train" ? "this split (in-sample)" : "train";
        part.Statistic["trainRows"] = trainRows.Count;
        part.Statistic["epochs"] = model.Epochs;
        part.Statistic["trainLogLoss"] = model.FinalLoss;
        part.Statistic["weights"] = DetectorFeatures.Select((f, i) => new { feature = f, weight = Math.Round(model.Weights[i], 6) }).ToList();
        part.Statistic["bootstrap"] = "day-block resampling of weighted AUC(detector) - weighted AUC(benchmark)";
        run.Parts.Add(part);
    }

    // ── H12p: the grid over every pass in the population ─────────────────

    private sealed record WeightedTrade(DateOnly Day, double Net, string Reason, double Weight);

    private async Task PopulationGridAsync(GapperHypothesis h, HypothesisRun run, string split, CancellationToken ct)
    {
        var (multiplier, source) = GapperSpreadMultiplier.Resolve(s.Paths, P);
        var configs = Grid(P);
        var passAt = P.H12Grid.PassAtEt;
        var rows = (await panel.LoadAsync(split, passAt, ct)).Where(r => r.InPopulation).OrderBy(r => r.DateEt).ThenBy(r => r.Symbol, StringComparer.Ordinal).ToList();
        Describe(run, Keys(rows).ToList(), PopulationDescription(rows, $"population at {passAt}"), ["VolRatioTod", "Velocity3m", "PmCumVol", PriorDaysCorwinSchultzFeature.FeatureName, "sigma20"], ["weighted net return per trade after the frozen cost model"]);
        var minEffect = h.MinEffectNumber("meanNetPctPerTrade");
        var part = new HypothesisPart { Name = "meanNetPctPerTrade", EffectName = "meanNetPctPerTrade", MinEffect = minEffect, Direction = EffectDirection.AtLeast };
        PopulationStatistics(part, rows);
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
        var trades = configs.Select(_ => new List<WeightedTrade>()).ToList();
        var dayKeys = rows.Select(r => r.DateEt).Distinct().OrderBy(d => d).ToList();
        var dayIndex = dayKeys.Select((d, i) => (d, i)).ToDictionary(x => x.d, x => x.i);
        var dayWeight = new double[dayKeys.Count];
        var daySums = configs.Select(_ => new double[dayKeys.Count]).ToList();
        foreach (var r in rows) dayWeight[dayIndex[r.DateEt]] += r.Weight;
        int cellsPassing = 0, samplePassing = 0;
        double costSum = 0;
        foreach (var r in rows)
        {
            if (!(r.Snapshot.VolRatioTod >= loosestPm && r.Snapshot.Velocity3m >= loosestV3) || r.Label.EntryRef is not > 0) continue;
            var bars = store.ReadDay(r.Symbol, r.DateEt);
            if (bars.Count == 0) continue;
            cellsPassing++;
            if (r.IsPopulationSample) samplePassing++;
            var sigma = GapperCostModel.Sigma(await panel.PriorClosesAsync(r.Symbol, r.DateEt, P.Features.Adv20Sessions, ct), P.Features.Adv20Sessions);
            var cost = GapperCostModel.Estimate(new GapperCostInputs(r.Extras.Number(PriorDaysCorwinSchultzFeature.FeatureName), multiplier.Value, r.Snapshot.PmCumVol, sigma,
                (double)r.Label.EntryRef!.Value, notional, GapperSpreadMultiplier.ImpactConstant(P), P.CostModel.RoundTripFloor)).RoundTrip;
            costSum += cost;
            for (int k = 0; k < configs.Count; k++)
            {
                if (!configs[k].Passes(r.Snapshot.VolRatioTod, r.Snapshot.Velocity3m)) continue;
                var trade = GapperRuleSimulator.Simulate(configs[k], bars, r.DateEt, r.Snapshot.DecisionUtc, cost, exits);
                if (trade is null) continue;
                trades[k].Add(new WeightedTrade(r.DateEt, trade.NetReturn, trade.ExitReason, r.Weight));
                daySums[k][dayIndex[r.DateEt]] += r.Weight * trade.NetReturn;
            }
        }

        part.Statistic["cellsPassingLoosest"] = cellsPassing;
        part.Statistic["sampleCellsPassingLoosest"] = samplePassing;
        part.Statistic["meanRoundTripCost"] = cellsPassing == 0 ? double.NaN : costSum / cellsPassing;
        part.Statistic["spreadMultiplier"] = multiplier;
        part.Statistic["spreadMultiplierSource"] = source;
        ScoreWeightedGrid(h, run, part, split, configs.Select(c => c.Label).ToList(), trades, daySums, dayWeight, rows.Count);
    }

    /// <summary>
    /// The weighted grid verdict shared by H12p and H18: per-configuration weighted mean and day-clustered
    /// bound, DSR at the unweighted trade count, SPA on per-day weighted mean returns per population cell,
    /// the configuration selected on train (carried to later splits), and the worth-trading minimums.
    /// </summary>
    private void ScoreWeightedGrid(GapperHypothesis h, HypothesisRun run, HypothesisPart part, string split, IReadOnlyList<string> labels,
        List<List<WeightedTrade>> trades, List<double[]> daySums, double[] dayWeight, int cells)
    {
        var series = daySums.Select(sums => sums.Select((v, i) => dayWeight[i] > 0 ? v / dayWeight[i] : 0).ToArray()).ToList();
        var replicated = trades.Select(t => WeightedPopulation.Replicate(t.Select(x => (x.Net, x.Weight)))).ToList();
        var moments = trades.Select((t, k) => ReturnMoments.Of(replicated[k]) with { Count = t.Count }).ToList();
        var naiveP = moments.Select(OneSidedP).ToArray();
        var q = MultipleTesting.BenjaminiHochberg(naiveP);
        var sharpes = moments.Select(m => m.Sharpe).ToList();
        double sharpeVariance = sharpes.Count > 1 ? Math.Pow(EvidenceStats.StdDev(sharpes), 2) : 0;
        var dsr = moments.Select(m => MultipleTesting.DeflatedSharpe(m, labels.Count, sharpeVariance)).ToArray();
        var summaries = new List<ConfigSummary>();
        var bounds = new List<BootstrapInterval>();
        for (int k = 0; k < labels.Count; k++)
        {
            var b = trades[k].Count == 0
                ? new BootstrapInterval(double.NaN, double.NaN, double.NaN, 1, 0, 0, 0)
                : WeightedPopulation.Mean(trades[k].Select(t => new WeightedValue(t.Day, t.Net * 100, t.Weight)).ToList(), P.Statistics.BootstrapDraws, P.Statistics.Seed, P.Statistics.CiLevel, MinDays);
            bounds.Add(b);
            summaries.Add(new ConfigSummary(labels[k], trades[k].Count, trades[k].Select(t => t.Day).Distinct().Count(), b.Estimate, b.Low, moments[k].Sharpe, naiveP[k], q[k], dsr[k],
                trades[k].GroupBy(t => t.Reason).ToDictionary(g => g.Key, g => g.Count())));
        }
        double minEffect = h.MinEffectNumber("meanNetPctPerTrade");
        int minTrades = (int)h.MinEffectNumber("minTrades"), minDays = (int)h.MinEffectNumber("minDays");
        double lowerBoundAbove = h.MinEffectNumber("clusteredLowerBoundAbove");
        var rc = dayWeight.Length >= 2 ? RealityCheck.Run(series, Math.Max(100, P.Statistics.PlaceboDraws), 5, P.Statistics.Seed) : null;

        int? selected = null;
        string selection;
        var carried = split is "train" ? null : LatestTrainSelection(labels, h.Id);
        if (carried is { } c) { selected = c; selection = "carried from the latest train run of " + h.Id; }
        else
        {
            var eligible = Enumerable.Range(0, labels.Count).Where(k => summaries[k].Trades >= minTrades && !double.IsNaN(summaries[k].LowerBoundPct)).ToList();
            if (eligible.Count > 0) selected = eligible.OrderByDescending(k => summaries[k].LowerBoundPct).ThenByDescending(k => summaries[k].MeanNetPct).First();
            selection = split is "train" ? "highest weighted day-clustered lower bound on this split among configs with enough unweighted trades" : "in-sample selection on this split (no train run file found)";
        }
        part.Statistic["configs"] = summaries;
        part.Statistic["configCount"] = labels.Count;
        part.Statistic["cells"] = cells;
        part.Statistic["selection"] = selection;
        part.Statistic["whiteP"] = rc?.WhitePValue;
        part.Statistic[HypothesisCorrections.SpaKey] = rc?.SpaPValue;
        part.Statistic["spaBestConfig"] = rc is null ? null : labels[rc.BestConfig];
        part.Statistic["spaResamples"] = rc?.Resamples;
        part.Statistic["spaSeries"] = "per-day weighted mean net return per population cell (zero where the configuration did not trade)";
        part.Statistic["dsrLength"] = "unweighted trade count";
        if (selected is not { } best || summaries[best].Trades == 0)
        {
            part.Verdict = HypothesisVerdict.Void;
            part.Reasons.Add($"no configuration with at least {minTrades} unweighted trades on {cells} population cells");
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
        part.Statistic["selectedSampleTrades"] = trades[best].Count(t => t.Weight > 1);
        part.Statistic["selectedWeightedTrades"] = trades[best].Sum(t => t.Weight);
        part.Statistic["selectedDays"] = chosen.Days;
        part.Statistic["selectedLowerBoundPct"] = chosen.LowerBoundPct;
        part.Statistic[HypothesisCorrections.DsrKey] = chosen.Dsr;
        part.Statistic["selectedQ"] = chosen.Q;
        part.Statistic["selectedExits"] = chosen.Exits;
        part.Statistic["worthTrading"] = new { minTrades, minDays, lowerBoundAbove, meanNetPctPerTrade = minEffect };
        run.Parts.Add(part);
    }

    /// <summary>One-sided t p that the weighted mean is above zero, with the unweighted trade count as the sample size.</summary>
    private static double OneSidedP(ReturnMoments m)
    {
        if (m.Count < 2) return 1;
        double sampleSd = m.StdDev * Math.Sqrt((double)m.Count / (m.Count - 1));
        if (sampleSd <= 0) return m.Mean > 0 ? 0 : 1;
        return Distributions.StudentTUpperTail(m.Mean / (sampleSd / Math.Sqrt(m.Count)), m.Count - 1);
    }
}
