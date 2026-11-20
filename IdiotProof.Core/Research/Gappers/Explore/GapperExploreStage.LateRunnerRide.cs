using System.Globalization;
using System.Text;
using System.Text.Json;
using IdiotProof.Evidence;
using IdiotProof.Evidence.Evaluation;
using IdiotProof.Evidence.Gappers;
using IdiotProof.Evidence.Search;
using IdiotProof.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IdiotProof.Core.Research.Gappers.Explore;

/// <summary>
/// `explore late-runner-ride` (IP-T617, gapper study amendment 10, H29), TRAIN only: trade the late-runner move start.
/// Exactly the registration in amendment-10.json (committed before this code): amendment 09's rows with an m0, the H27
/// full-feature model's pooled out-of-fold score (amendment 09's five month blocks), an entry at the next bar's open
/// after m0 when the score is at or above the registered threshold (skipped when that open is already at +20%), H12/H18's
/// frozen exit ladder (GapperRuleSimulator unchanged) on the registered 4-configuration grid (time stop x profit target)
/// and H18's frozen cost model. Scored as H12p/H18's ScoreWeightedGrid: weighted mean net per trade with a day-clustered
/// bound, DSR at the unweighted trade count, SPA on per-day weighted means per population cell, selection by the bound
/// among configurations with enough trades, plus the registration's 3-of-5-folds rule. One ledger trial per configuration.
/// Validation is never read here. Research only: nothing here arms, sizes, places or exits an order.
/// </summary>
public sealed partial class GapperExploreStage
{
    public const string LateRunnerRideTrack = "late-runner-ride";

    /// <summary>The latest sell-by of any A10 configuration (amendment-10.json: entry+60m is capped here).</summary>
    private static readonly TimeSpan A10LatestSellBy = new(15, 55, 0);
    private const int A10MinFoldsPositive = 3;

    private sealed record A10Config(string Target, string TimeStop, TimeSpan? FixedSellBy, int? HoldMinutes)
    {
        public string Label => $"s>=threshold target {Target} timeStop {TimeStop}";
    }

    private sealed record A10Trade(DateOnly Day, int Fold, double Net, double Gross, double Cost, string Reason, double Weight);

    private async Task<(List<string>, int, string?)> LateRunnerRideAsync(ExploreRequest req, CancellationToken ct)
    {
        var p = s.Params;
        var profilePath = s.Paths.ProfileFile(false);
        if (!File.Exists(profilePath)) return ([$"{profilePath} is missing: run `snapshot train` first so the time-of-day profile is fit"], 0, null);

        // ── the registration, read from amendment-10.json (nothing here is chosen in code) ──
        var a10 = s.Amendments.FirstOrDefault(a => a.Id == "A10") ?? throw new InvalidOperationException("amendment-10.json (A10) is not registered.");
        var h = a10.Find("H29") ?? throw new InvalidOperationException("amendment-10.json registers no H29.");
        var grid = h.Grid ?? throw new InvalidDataException("H29 registers no grid.");
        double threshold = grid.GetProperty("scoreThreshold").GetDouble();
        int deathWindow = grid.GetProperty("velocityDeathWindow").GetInt32();
        double stopPct = grid.GetProperty("stopLossPct").GetDouble();
        bool vwap = grid.GetProperty("vwapLossExit").GetBoolean();
        var configs = new List<A10Config>();
        foreach (var target in grid.GetProperty("profitTarget").EnumerateArray().Select(e => e.GetString()!))
        {
            if (target is not ("none" or "label20")) throw new InvalidDataException($"H29 profitTarget '{target}' is not none or label20.");
            foreach (var stop in grid.GetProperty("timeStop").EnumerateArray().Select(e => e.GetString()!))
            {
                if (stop.StartsWith("entry+", StringComparison.Ordinal) && stop.EndsWith('m'))
                    configs.Add(new A10Config(target, stop, null, int.Parse(stop[6..^1], CultureInfo.InvariantCulture)));
                else
                    configs.Add(new A10Config(target, stop, GapperStudyParams.ParseEt(stop), null));
            }
        }
        double Min(string key) => h.MinEffect.TryGetValue(key, out var v) ? v.GetDouble() : throw new InvalidDataException($"H29 has no minEffect.{key}.");
        double minMean = Min("meanNetPctPerTrade"), lowerBoundAbove = Min("clusteredLowerBoundAbove");
        int minTrades = (int)Min("minTrades"), minDays = (int)Min("minDays");

        var (multiplier, source) = GapperSpreadMultiplier.Resolve(s.Paths, p);
        if (multiplier is null) return ([$"cost model unavailable ({source}); run `calibrate-spread` first"], 0, null);
        var notional = GapperSpreadMultiplier.TradeNotional(p) ?? throw new InvalidDataException("params.json has no costModel.impact.tradeNotionalUsd.");
        double impact = GapperSpreadMultiplier.ImpactConstant(p), floor = p.CostModel.RoundTripFloor;
        var exits = new GapperRuleExitParameters(p.Labels.VelocityDeath.ArmThresholdPctPerMin, p.Labels.VelocityDeath.ConsecutiveNegativeBars, p.Labels.Vwap.MinConsecutiveAboveBeforeLoss);

        // ── amendment 09's rows and H27's pooled out-of-fold score (the A09-H1 train run's exact folds and fit) ──
        var (all, train, _, _, _, _, _) = await A09RowsAsync("train", req.Parallelism, ct);
        var rows = all.Where(d => d.M0 >= 0).ToList();
        int n = rows.Count;
        var firstMonth = new DateOnly(train.From.Year, train.From.Month, 1);
        int months = (train.To.Year - firstMonth.Year) * 12 + train.To.Month - firstMonth.Month + 1;
        int FoldOf(DateOnly d) => ((d.Year - firstMonth.Year) * 12 + d.Month - firstMonth.Month) * A09Folds / months;
        var (names, kept, _, indicators, _) = A09Features(rows, n);
        var fullX = rows.Select(r => A09FullVector(r, kept, indicators)).ToArray();
        var labels = rows.Select(r => r.Y).ToArray();
        var weights = rows.Select(r => r.Weight).ToArray();
        var fold = rows.Select(r => FoldOf(r.Day)).ToArray();
        var score = new double[n];
        Array.Fill(score, double.NaN);
        for (int k = 0; k < A09Folds; k++)
        {
            var fit = Enumerable.Range(0, n).Where(i => fold[i] != k).ToList();
            var eval = Enumerable.Range(0, n).Where(i => fold[i] == k).ToList();
            if (fit.Count == 0 || eval.Count == 0 || !fit.Any(i => labels[i]) || fit.All(i => labels[i])) continue;
            var model = LogisticRegression.FitWeighted(names, fit.Select(i => fullX[i]).ToList(), fit.Select(i => labels[i]).ToList(), fit.Select(i => weights[i]).ToList());
            foreach (var i in eval) score[i] = model.Predict(fullX[i]);
        }
        int unscored = score.Count(double.IsNaN);
        var signal = Enumerable.Range(0, n).Where(i => !double.IsNaN(score[i]) && score[i] >= threshold).ToList();
        s.Logger.LogInformation("[{Now}] explore late-runner-ride: {Rows} move starts, {Signal} with s >= {T}", GapperStudyServices.Et(s.UtcNow), n, signal.Count, threshold);

        // ── the frozen cost model's spread input: the same symbol-day's 08:00 snapshot csSpreadPrior (H18's source) ──
        var need = signal.Select(i => (rows[i].Symbol, rows[i].Day)).ToHashSet();
        var csPrior = new Dictionary<(string, DateOnly), double?>();
        await using (var db = await s.Db.CreateDbContextAsync(ct))
        {
            db.WithCommandTimeout(TimeSpan.FromMinutes(15));
            await foreach (var r in db.GapperFormationSnapshots.AsNoTracking()
                               .Where(r => r.Source == GapperSnapshotStage.Source && r.Split == "train" && r.DecisionLabel == "08:00")
                               .Select(r => new { r.Symbol, r.DateEt, r.ExtraJson }).AsAsyncEnumerable().WithCancellation(ct))
                if (need.Contains((r.Symbol, r.DateEt))) csPrior[(r.Symbol, r.DateEt)] = GapperSnapshotExtras.Parse(r.ExtraJson).Number(PriorDaysCorwinSchultzFeature.FeatureName);
        }

        // ── simulate ──
        var store = await s.StoreAsync(ct);
        var panel = new GapperStudyPanel(s);
        var trades = configs.Select(_ => new List<A10Trade>()).ToList();
        int noEntry = 0, alreadyAt20 = 0, noSnapshot = 0, csMissing = 0, entries = 0, done = 0;
        var costs = new List<double>();
        foreach (var i in signal)
        {
            ct.ThrowIfCancellationRequested();
            var r = rows[i];
            var bars = store.ReadDay(r.Symbol, r.Day).OrderBy(b => b.StartUtc).ToList();
            var m0End = MinuteBarMath.ToUtc(r.Day, MinuteBarMath.SessionStartEt + TimeSpan.FromMinutes(r.M0 + 1));
            var entry = bars.FirstOrDefault(b => b.Volume > 0 && b.Open > 0 && b.StartUtc >= m0End);
            var latest = MinuteBarMath.ToUtc(r.Day, A10LatestSellBy);
            if (entry is null || entry.StartUtc >= latest) { noEntry++; continue; }
            var targetPx = (decimal)r.PrevClose * (1m + (decimal)A09Level);
            if (entry.Open >= targetPx) { alreadyAt20++; continue; }

            if (!csPrior.TryGetValue((r.Symbol, r.Day), out var cs)) noSnapshot++;
            if (cs is null) csMissing++;
            var sigma = GapperCostModel.Sigma(await panel.PriorClosesAsync(r.Symbol, r.Day, p.Features.Adv20Sessions, ct), p.Features.Adv20Sessions);
            double cumVol = (double)bars.Where(b => b.StartUtc < entry.StartUtc).Sum(b => b.Volume);
            double cost = GapperCostModel.Estimate(new GapperCostInputs(cs, multiplier.Value, cumVol, sigma, (double)entry.Open, notional, impact, floor)).RoundTrip;
            costs.Add(cost);
            entries++;
            var entryEt = MinuteBarMath.ToEastern(entry.StartUtc).TimeOfDay;
            var real = bars.Where(b => b.Volume > 0 && b.Open > 0).ToList();

            for (int k = 0; k < configs.Count; k++)
            {
                var c = configs[k];
                TimeSpan sellBy;
                if (c.FixedSellBy is { } fixedSellBy) sellBy = fixedSellBy;
                else
                {
                    var hold = entryEt + TimeSpan.FromMinutes(c.HoldMinutes!.Value);
                    sellBy = hold < A10LatestSellBy ? hold : A10LatestSellBy;
                }
                if (entryEt >= sellBy) continue;
                var trade = GapperRuleSimulator.Simulate(new GapperRuleConfig(0, 0, deathWindow, stopPct, sellBy, vwap), bars, r.Day, entry.StartUtc, cost, exits);
                if (trade is null) continue;
                double gross = trade.GrossReturn;
                string reason = trade.ExitReason;
                if (c.Target == "label20")
                {
                    // A resting sell at +20% over the prior close: the first bar from the entry bar on, before the ladder's own
                    // exit bar, whose high reaches it (the exit bar is excluded, so a stop in the same bar wins the tie).
                    var hit = real.FirstOrDefault(b => b.StartUtc >= trade.EntryUtc && b.StartUtc < trade.ExitUtc && b.High >= targetPx);
                    if (hit is not null)
                    {
                        var fill = hit.Open >= targetPx ? hit.Open : targetPx;
                        gross = (double)(fill / trade.Entry - 1m);
                        reason = "target +20%";
                    }
                }
                trades[k].Add(new A10Trade(r.Day, fold[i], gross - cost, gross, cost, reason, r.Weight));
            }
            if (++done % 500 == 0) s.Logger.LogInformation("[{Now}] explore late-runner-ride: {Done}/{Total} signals simulated", GapperStudyServices.Et(s.UtcNow), done, signal.Count);
        }

        return A10Report(h, configs, rows, fold, score, threshold, signal.Count, unscored, noEntry, alreadyAt20, noSnapshot, csMissing, entries, costs, trades,
            multiplier.Value, source, notional, deathWindow, stopPct, vwap, minMean, lowerBoundAbove, minTrades, minDays, train);
    }

    private (List<string>, int, string?) A10Report(AmendedHypothesis h, List<A10Config> configs, List<A09Day> rows, int[] fold, double[] score, double threshold,
        int signals, int unscored, int noEntry, int alreadyAt20, int noSnapshot, int csMissing, int entries, List<double> costs, List<List<A10Trade>> trades,
        double multiplier, string multiplierSource, double notional, int deathWindow, double stopPct, bool vwap,
        double minMean, double lowerBoundAbove, int minTrades, int minDays, GapperStudyParams.SplitRange train)
    {
        var p = s.Params;
        int draws = p.Statistics.BootstrapDraws, seed = p.Statistics.Seed;
        double ci = p.Statistics.CiLevel > 0 ? p.Statistics.CiLevel : 0.95;
        string Pct(double v) => double.IsFinite(v) ? v.ToString("+0.00;-0.00", CultureInfo.InvariantCulture) + "%" : "n/a";
        string N3(double v) => double.IsFinite(v) ? v.ToString("0.000", CultureInfo.InvariantCulture) : "n/a";

        // ── ScoreWeightedGrid's statistics (GapperTestStage.Amendment.cs), over the population cells with an m0 ──
        var dayKeys = rows.Select(r => r.Day).Distinct().OrderBy(d => d).ToList();
        var dayIndex = dayKeys.Select((d, i) => (d, i)).ToDictionary(x => x.d, x => x.i);
        var dayWeight = new double[dayKeys.Count];
        foreach (var r in rows) dayWeight[dayIndex[r.Day]] += r.Weight;
        var series = trades.Select(t =>
        {
            var sums = new double[dayKeys.Count];
            foreach (var x in t) sums[dayIndex[x.Day]] += x.Weight * x.Net;
            return sums.Select((v, i) => dayWeight[i] > 0 ? v / dayWeight[i] : 0).ToArray();
        }).ToList();
        var moments = trades.Select(t => ReturnMoments.Of(WeightedPopulation.Replicate(t.Select(x => (x.Net, x.Weight)))) with { Count = t.Count }).ToList();
        var naiveP = moments.Select(A10OneSidedP).ToArray();
        var q = MultipleTesting.BenjaminiHochberg(naiveP);
        var sharpes = moments.Select(m => m.Sharpe).ToList();
        double sharpeVariance = sharpes.Count > 1 ? Math.Pow(EvidenceStats.StdDev(sharpes), 2) : 0;
        var dsr = moments.Select(m => MultipleTesting.DeflatedSharpe(m, configs.Count, sharpeVariance)).ToArray();
        var bounds = trades.Select(t => t.Count == 0
            ? new BootstrapInterval(double.NaN, double.NaN, double.NaN, 1, 0, 0, 0)
            : WeightedPopulation.Mean(t.Select(x => new WeightedValue(x.Day, x.Net * 100, x.Weight)).ToList(), draws, seed, ci, 5)).ToList();
        var rc = dayKeys.Count >= 2 ? RealityCheck.Run(series, Math.Max(100, p.Statistics.PlaceboDraws), 5, seed) : null;
        var foldMeans = trades.Select(t => Enumerable.Range(0, A09Folds).Select(k =>
        {
            var l = t.Where(x => x.Fold == k).ToList();
            double w = l.Sum(x => x.Weight);
            return w > 0 ? (double?)(l.Sum(x => x.Weight * x.Net) / w * 100) : null;
        }).ToList()).ToList();
        var days = trades.Select(t => t.Select(x => x.Day).Distinct().Count()).ToList();

        var eligible = Enumerable.Range(0, configs.Count).Where(k => trades[k].Count >= minTrades && !double.IsNaN(bounds[k].Low)).ToList();
        int? selected = eligible.Count == 0 ? null : eligible.OrderByDescending(k => bounds[k].Low).ThenByDescending(k => bounds[k].Estimate).First();
        double spaP = rc?.SpaPValue ?? double.NaN;
        bool pass = false;
        var failures = new List<string>();
        if (selected is { } b)
        {
            int foldsPositive = foldMeans[b].Count(v => v is > 0);
            if (!(bounds[b].Estimate >= minMean)) failures.Add($"mean {Pct(bounds[b].Estimate)} < +{minMean:0.0}%");
            if (!(bounds[b].Low > lowerBoundAbove)) failures.Add($"lower bound {Pct(bounds[b].Low)} not > {lowerBoundAbove:0}");
            if (trades[b].Count < minTrades) failures.Add($"{trades[b].Count} trades < {minTrades}");
            if (days[b] < minDays) failures.Add($"{days[b]} days < {minDays}");
            if (!(spaP < p.Statistics.SpaAlpha)) failures.Add($"SPA p {N3(spaP)} not < {p.Statistics.SpaAlpha}");
            if (!(dsr[b] >= p.Statistics.DsrMin)) failures.Add($"DSR {N3(dsr[b])} < {p.Statistics.DsrMin}");
            if (foldsPositive < A10MinFoldsPositive) failures.Add($"{foldsPositive} of {A09Folds} folds positive < {A10MinFoldsPositive}");
            pass = failures.Count == 0;
        }
        else failures.Add($"no configuration with at least {minTrades} unweighted trades");

        // ── report ──
        var md = new StringBuilder();
        md.AppendLine("# Explore: trading the late-runner move start (amendment 10, H29, IP-T617)");
        md.AppendLine();
        md.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"Train only ({train.From:yyyy-MM-dd}..{train.To:yyyy-MM-dd}); validation and the holdout are not read. Every choice is amendment-10.json's, registered and pushed before this code was written. Research only: nothing here arms, sizes, places or exits an order."));
        md.AppendLine();
        md.AppendLine("## Entries");
        md.AppendLine();
        md.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"- Move starts (amendment 09's train rows with an m0): {rows.Count:N0} (weighted {rows.Sum(r => r.Weight):N0}); not scored (a fold without both classes to fit): {unscored:N0}."));
        md.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"- H27 full-feature model, pooled out of fold over amendment 09's five month blocks: s >= {threshold:0.00} on {signals:N0} move starts ({signals / (double)Math.Max(1, rows.Count):P2}; weighted {Enumerable.Range(0, rows.Count).Where(i => score[i] >= threshold).Sum(i => rows[i].Weight):N0}); y = 1 on {Enumerable.Range(0, rows.Count).Count(i => score[i] >= threshold && rows[i].Y):N0} of them."));
        md.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"- No trade: {noEntry:N0} with no bar with volume after m0 before {A10LatestSellBy:hh\\:mm}; {alreadyAt20:N0} whose fill was already >= +20% over the prior close. Entries: {entries:N0}."));
        md.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"- Cost model (H18's, frozen): prior-days Corwin-Schultz x {multiplier:0.###} ({multiplierSource}), square-root impact at ${notional:N0}, floor {p.CostModel.RoundTripFloor:P1}; mean round trip {(costs.Count == 0 ? double.NaN : costs.Average() * 100):0.00}%, median {(costs.Count == 0 ? double.NaN : A09Median(costs) * 100):0.00}%; no 08:00 snapshot row {noSnapshot:N0}, no Corwin-Schultz estimate {csMissing:N0} (spread 0, floor applies)."));
        md.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"- Exit ladder (GapperRuleSimulator unchanged): stop {stopPct}%, VWAP loss {(vwap ? "on" : "off")}, velocity death {deathWindow}m, sell-by per configuration; target label20 = resting sell at 1.20 x the prior close."));
        md.AppendLine();
        md.AppendLine("## H29 grid (train)");
        md.AppendLine();
        md.AppendLine("Weighted mean net per trade (amendment 01 weights) with a day-clustered bootstrap 95% interval; trades and days unweighted; DSR over the 4 configurations at the unweighted trade count; per-fold weighted means over amendment 09's five month blocks.");
        md.AppendLine();
        md.AppendLine("| configuration | trades | days | mean net | 95% interval | Sharpe | DSR | BH q | folds (mean net) | exits |");
        md.AppendLine("|---|---|---|---|---|---|---|---|---|---|");
        var configJson = new List<object>();
        var trials = new List<ExploreTrial>();
        for (int k = 0; k < configs.Count; k++)
        {
            var exitsBy = trades[k].GroupBy(x => x.Reason).OrderByDescending(g => g.Count()).ToDictionary(g => g.Key, g => g.Count());
            string folds = string.Join(" / ", foldMeans[k].Select(v => v is { } x ? Pct(x) : "n/a"));
            md.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"| {configs[k].Label}{(selected == k ? " **(selected)**" : "")} | {trades[k].Count:N0} | {days[k]:N0} | {Pct(bounds[k].Estimate)} | [{Pct(bounds[k].Low)}, {Pct(bounds[k].High)}] | {moments[k].Sharpe:0.000} | {N3(dsr[k])} | {N3(q[k])} | {folds} | {string.Join(", ", exitsBy.Select(e => $"{e.Key} {e.Value}"))} |"));
            double meanGross = trades[k].Count == 0 ? double.NaN : trades[k].Sum(x => x.Weight * x.Gross) / trades[k].Sum(x => x.Weight) * 100;
            configJson.Add(new { label = configs[k].Label, target = configs[k].Target, timeStop = configs[k].TimeStop, trades = trades[k].Count, days = days[k], meanNetPct = bounds[k].Estimate, lowPct = bounds[k].Low, highPct = bounds[k].High, meanGrossPct = meanGross, sharpe = moments[k].Sharpe, dsr = dsr[k], naiveP = naiveP[k], q = q[k], foldMeansPct = foldMeans[k], exits = exitsBy, selected = selected == k });
            trials.Add(new ExploreTrial
            {
                Track = LateRunnerRideTrack, Config = $"A10-H29 {configs[k].Label.Replace("threshold", threshold.ToString("0.00", CultureInfo.InvariantCulture))}",
                Metric = "weighted mean net % per trade after the frozen cost model", FoldValues = foldMeans[k], Pooled = double.IsFinite(bounds[k].Estimate) ? bounds[k].Estimate : null, Rows = trades[k].Count,
                Note = string.Create(CultureInfo.InvariantCulture, $"low={N3(bounds[k].Low)} high={N3(bounds[k].High)} meanGross={meanGross:0.000} days={days[k]} dsr={N3(dsr[k])}"),
            });
        }
        md.AppendLine();
        md.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"Reality check over the 4 configurations (per-day weighted mean net per population cell, zero where a configuration did not trade; {rc?.Resamples ?? 0} resamples): White p {N3(rc?.WhitePValue ?? double.NaN)}, SPA p {N3(spaP)}, best {(rc is null ? "n/a" : configs[rc.BestConfig].Label)}."));
        md.AppendLine();
        md.AppendLine("## Verdict on train");
        md.AppendLine();
        md.AppendLine(selected is { } sel
            ? $"Selected (highest weighted lower bound among configurations with >= {minTrades} trades): **{configs[sel].Label}**."
            : $"No configuration has >= {minTrades} trades; nothing is selected.");
        md.AppendLine();
        md.AppendLine(pass
            ? $"**H29 passes on train** (mean >= +{minMean:0.0}%, bound > {lowerBoundAbove:0}, >= {minTrades} trades on >= {minDays} days, SPA p < {p.Statistics.SpaAlpha}, DSR >= {p.Statistics.DsrMin}, >= {A10MinFoldsPositive} of {A09Folds} folds positive). Per amendment 10 the selected configuration is written into amendment-10.md as a dated note, then validation is read once."
            : $"**H29 fails on train**: {string.Join("; ", failures)}. Per amendment 10: stop. Validation is not read, and no other threshold, target, time stop, stop size or cost model is tried under this amendment. The move-start signal stays real (amendment 09); this entry and exit do not make it tradable after costs.");
        md.AppendLine();

        var json = new Dictionary<string, object?>
        {
            ["amendment"] = "A10",
            ["hypothesis"] = h.Id,
            ["train"] = new { from = train.From, to = train.To },
            ["entries"] = new { moveStarts = rows.Count, unscored, threshold, signals, noEntry, alreadyAt20, entries, noSnapshot, csMissing, meanRoundTripCost = costs.Count == 0 ? double.NaN : costs.Average(), medianRoundTripCost = A09Median(costs) },
            ["costModel"] = new { spreadMultiplier = multiplier, spreadMultiplierSource = multiplierSource, notional, floor = p.CostModel.RoundTripFloor },
            ["exitLadder"] = new { stopPct, vwapLossExit = vwap, velocityDeathWindow = deathWindow, latestSellBy = A10LatestSellBy.ToString(@"hh\:mm", CultureInfo.InvariantCulture) },
            ["configs"] = configJson,
            ["realityCheck"] = new { whiteP = rc?.WhitePValue, spaP = rc?.SpaPValue, best = rc is null ? null : configs[rc.BestConfig].Label, resamples = rc?.Resamples },
            ["selected"] = selected is { } s2 ? configs[s2].Label : null,
            ["worthTrading"] = new { meanNetPctPerTrade = minMean, clusteredLowerBoundAbove = lowerBoundAbove, minTrades, minDays, spaAlpha = p.Statistics.SpaAlpha, dsrMin = p.Statistics.DsrMin, minFoldsPositive = A10MinFoldsPositive },
            ["pass"] = pass,
            ["failures"] = failures,
        };
        return Write(LateRunnerRideTrack, md, trials, json);
    }

    /// <summary>One-sided t p that the weighted mean is above zero, unweighted trade count as the sample size (GapperTestStage.OneSidedP).</summary>
    private static double A10OneSidedP(ReturnMoments m)
    {
        if (m.Count < 2) return 1;
        double sampleSd = m.StdDev * Math.Sqrt((double)m.Count / (m.Count - 1));
        if (sampleSd <= 0) return m.Mean > 0 ? 0 : 1;
        return Distributions.StudentTUpperTail(m.Mean / (sampleSd / Math.Sqrt(m.Count)), m.Count - 1);
    }
}
