using System.Globalization;
using System.Text;
using IdiotProof.Evidence;
using IdiotProof.Evidence.Evaluation;
using IdiotProof.Evidence.Gappers;
using IdiotProof.Evidence.Search;
using IdiotProof.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IdiotProof.Core.Research.Gappers.Explore;

/// <summary>
/// `explore late-runner-ride-liquidity` (IP-T619, gapper study amendment 11, H30), TRAIN only: amendment 10's
/// late-runner ride (same rows, same pooled out-of-fold H27 score, same fill rule, same frozen exit ladder and
/// cost model, same single best-performing configuration -- target label20, sell-by 15:55 ET), with one added
/// no-trade rule: logAdv20Dollars (amendment 09's own feature, already computed per row) below the registered
/// $2,000,000 20-day-ADV floor. One configuration, not a grid: one ledger trial. Exactly the registration in
/// amendment-11.json (committed before this code). Validation is never read here. Research only: nothing here
/// arms, sizes, places or exits an order.
/// </summary>
public sealed partial class GapperExploreStage
{
    public const string LateRunnerRideLiquidityTrack = "late-runner-ride-liquidity";

    private async Task<(List<string>, int, string?)> LateRunnerRideLiquidityAsync(ExploreRequest req, CancellationToken ct)
    {
        var p = s.Params;
        var profilePath = s.Paths.ProfileFile(false);
        if (!File.Exists(profilePath)) return ([$"{profilePath} is missing: run `snapshot train` first so the time-of-day profile is fit"], 0, null);

        // ── the registration, read from amendment-11.json (nothing here is chosen in code) ──
        var a11 = s.Amendments.FirstOrDefault(a => a.Id == "A11") ?? throw new InvalidOperationException("amendment-11.json (A11) is not registered.");
        var h = a11.Find("H30") ?? throw new InvalidOperationException("amendment-11.json registers no H30.");
        var cfg = h.Grid ?? throw new InvalidDataException("H30 registers no configuration.");
        double threshold = cfg.GetProperty("scoreThreshold").GetDouble();
        double advFloor = cfg.GetProperty("advDollarFloor").GetDouble();
        double logAdvFloor = Math.Log(advFloor);
        int deathWindow = cfg.GetProperty("velocityDeathWindow").GetInt32();
        double stopPct = cfg.GetProperty("stopLossPct").GetDouble();
        bool vwap = cfg.GetProperty("vwapLossExit").GetBoolean();
        string target = cfg.GetProperty("profitTarget").GetString()!;
        if (target != "label20") throw new InvalidDataException($"H30 profitTarget '{target}' is not label20.");
        var sellBy = GapperStudyParams.ParseEt(cfg.GetProperty("timeStop").GetString()!);
        double Min(string key) => h.MinEffect.TryGetValue(key, out var v) ? v.GetDouble() : throw new InvalidDataException($"H30 has no minEffect.{key}.");
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
        var afterScore = Enumerable.Range(0, n).Where(i => !double.IsNaN(score[i]) && score[i] >= threshold).ToList();
        int belowLiquidity = afterScore.Count(i => !(rows[i].X[12] >= logAdvFloor));
        var signal = afterScore.Where(i => rows[i].X[12] >= logAdvFloor).ToList();
        s.Logger.LogInformation("[{Now}] explore late-runner-ride-liquidity: {Rows} move starts, {AfterScore} with s >= {T}, {Signal} after the ${Adv:N0} ADV floor",
            GapperStudyServices.Et(s.UtcNow), n, afterScore.Count, threshold, signal.Count, advFloor);

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

        // ── simulate (one configuration only) ──
        var store = await s.StoreAsync(ct);
        var panel = new GapperStudyPanel(s);
        var trades = new List<A10Trade>();
        int noEntry = 0, alreadyAt20 = 0, noSnapshot = 0, csMissing = 0, entries = 0, done = 0;
        var costs = new List<double>();
        foreach (var i in signal)
        {
            ct.ThrowIfCancellationRequested();
            var r = rows[i];
            var bars = store.ReadDay(r.Symbol, r.Day).OrderBy(b => b.StartUtc).ToList();
            var m0End = MinuteBarMath.ToUtc(r.Day, MinuteBarMath.SessionStartEt + TimeSpan.FromMinutes(r.M0 + 1));
            var entry = bars.FirstOrDefault(b => b.Volume > 0 && b.Open > 0 && b.StartUtc >= m0End);
            var latest = MinuteBarMath.ToUtc(r.Day, sellBy);
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
            var real = bars.Where(b => b.Volume > 0 && b.Open > 0).ToList();

            var entryEt = MinuteBarMath.ToEastern(entry.StartUtc).TimeOfDay;
            if (entryEt >= sellBy) continue;
            var trade = GapperRuleSimulator.Simulate(new GapperRuleConfig(0, 0, deathWindow, stopPct, sellBy, vwap), bars, r.Day, entry.StartUtc, cost, exits);
            if (trade is null) continue;
            double gross = trade.GrossReturn;
            string reason = trade.ExitReason;
            // target label20: resting sell at +20% over the prior close, first qualifying bar before the ladder's own exit bar
            var hit = real.FirstOrDefault(b => b.StartUtc >= trade.EntryUtc && b.StartUtc < trade.ExitUtc && b.High >= targetPx);
            if (hit is not null)
            {
                var fill = hit.Open >= targetPx ? hit.Open : targetPx;
                gross = (double)(fill / trade.Entry - 1m);
                reason = "target +20%";
            }
            trades.Add(new A10Trade(r.Day, fold[i], gross - cost, gross, cost, reason, r.Weight));
            if (++done % 500 == 0) s.Logger.LogInformation("[{Now}] explore late-runner-ride-liquidity: {Done}/{Total} signals simulated", GapperStudyServices.Et(s.UtcNow), done, signal.Count);
        }

        return A11Report(h, rows, fold, score, threshold, advFloor, afterScore.Count, belowLiquidity, unscored, noEntry, alreadyAt20, noSnapshot, csMissing, entries, costs, trades,
            multiplier.Value, source, notional, deathWindow, stopPct, vwap, sellBy, minMean, lowerBoundAbove, minTrades, minDays, train);
    }

    private (List<string>, int, string?) A11Report(AmendedHypothesis h, List<A09Day> rows, int[] fold, double[] score, double threshold, double advFloor,
        int afterScore, int belowLiquidity, int unscored, int noEntry, int alreadyAt20, int noSnapshot, int csMissing, int entries, List<double> costs, List<A10Trade> trades,
        double multiplier, string multiplierSource, double notional, int deathWindow, double stopPct, bool vwap, TimeSpan sellBy,
        double minMean, double lowerBoundAbove, int minTrades, int minDays, GapperStudyParams.SplitRange train)
    {
        var p = s.Params;
        int draws = p.Statistics.BootstrapDraws, seed = p.Statistics.Seed;
        double ci = p.Statistics.CiLevel > 0 ? p.Statistics.CiLevel : 0.95;
        string Pct(double v) => double.IsFinite(v) ? v.ToString("+0.00;-0.00", CultureInfo.InvariantCulture) + "%" : "n/a";
        string N3(double v) => double.IsFinite(v) ? v.ToString("0.000", CultureInfo.InvariantCulture) : "n/a";

        var moments = ReturnMoments.Of(WeightedPopulation.Replicate(trades.Select(x => (x.Net, x.Weight)))) with { Count = trades.Count };
        // one trial: DSR's own-trial deflation with a single candidate (sharpeVariance 0, configs.Count 1), per amendment-11's decisionRules
        var dsr = MultipleTesting.DeflatedSharpe(moments, 1, 0);
        var bound = trades.Count == 0
            ? new BootstrapInterval(double.NaN, double.NaN, double.NaN, 1, 0, 0, 0)
            : WeightedPopulation.Mean(trades.Select(x => new WeightedValue(x.Day, x.Net * 100, x.Weight)).ToList(), draws, seed, ci, 5);
        var foldMeans = Enumerable.Range(0, A09Folds).Select(k =>
        {
            var l = trades.Where(x => x.Fold == k).ToList();
            double w = l.Sum(x => x.Weight);
            return w > 0 ? (double?)(l.Sum(x => x.Weight * x.Net) / w * 100) : null;
        }).ToList();
        int days = trades.Select(x => x.Day).Distinct().Count();
        int foldsPositive = foldMeans.Count(v => v is > 0);

        var failures = new List<string>();
        if (!(bound.Estimate >= minMean)) failures.Add($"mean {Pct(bound.Estimate)} < +{minMean:0.0}%");
        if (!(bound.Low > lowerBoundAbove)) failures.Add($"lower bound {Pct(bound.Low)} not > {lowerBoundAbove:0}");
        if (trades.Count < minTrades) failures.Add($"{trades.Count} trades < {minTrades}");
        if (days < minDays) failures.Add($"{days} days < {minDays}");
        if (!(dsr >= p.Statistics.DsrMin)) failures.Add($"DSR {N3(dsr)} < {p.Statistics.DsrMin}");
        if (foldsPositive < 3) failures.Add($"{foldsPositive} of {A09Folds} folds positive < 3");
        bool pass = trades.Count > 0 && failures.Count == 0;
        if (trades.Count == 0) failures.Add("no trades");

        // ── report ──
        var md = new StringBuilder();
        md.AppendLine("# Explore: liquidity-filtered late-runner ride (amendment 11, H30, IP-T619)");
        md.AppendLine();
        md.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"Train only ({train.From:yyyy-MM-dd}..{train.To:yyyy-MM-dd}); validation and the holdout are not read. Every choice is amendment-11.json's, registered and pushed before this code was written. One configuration, not a grid. Research only: nothing here arms, sizes, places or exits an order."));
        md.AppendLine();
        md.AppendLine("## Entries");
        md.AppendLine();
        md.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"- Move starts (amendment 09's train rows with an m0): {rows.Count:N0}; not scored (a fold without both classes to fit): {unscored:N0}."));
        md.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"- H27 full-feature model, pooled out of fold: s >= {threshold:0.00} on {afterScore:N0} move starts; of those, {belowLiquidity:N0} below the ${advFloor:N0} 20-day-ADV floor. After both: {afterScore - belowLiquidity:N0} signals."));
        md.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"- No trade: {noEntry:N0} with no bar with volume after m0 before {sellBy:hh\\:mm}; {alreadyAt20:N0} whose fill was already >= +20% over the prior close. Entries: {entries:N0}."));
        md.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"- Cost model (amendment 10's, frozen): prior-days Corwin-Schultz x {multiplier:0.###} ({multiplierSource}), square-root impact at ${notional:N0}, floor {p.CostModel.RoundTripFloor:P1}; mean round trip {(costs.Count == 0 ? double.NaN : costs.Average() * 100):0.00}%, median {(costs.Count == 0 ? double.NaN : A09Median(costs) * 100):0.00}%; no 08:00 snapshot row {noSnapshot:N0}, no Corwin-Schultz estimate {csMissing:N0} (spread 0, floor applies)."));
        md.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"- Exit ladder (GapperRuleSimulator unchanged): stop {stopPct}%, VWAP loss {(vwap ? "on" : "off")}, velocity death {deathWindow}m, sell-by {sellBy:hh\\:mm} ET, target label20 = resting sell at 1.20 x the prior close."));
        md.AppendLine();
        md.AppendLine("## H30 (train, single configuration)");
        md.AppendLine();
        var exitsBy = trades.GroupBy(x => x.Reason).OrderByDescending(g => g.Count()).ToDictionary(g => g.Key, g => g.Count());
        md.AppendLine("| trades | days | mean net | 95% interval | Sharpe | DSR | folds (mean net) | exits |");
        md.AppendLine("|---|---|---|---|---|---|---|---|");
        string folds = string.Join(" / ", foldMeans.Select(v => v is { } x ? Pct(x) : "n/a"));
        md.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"| {trades.Count:N0} | {days:N0} | {Pct(bound.Estimate)} | [{Pct(bound.Low)}, {Pct(bound.High)}] | {moments.Sharpe:0.000} | {N3(dsr)} | {folds} | {string.Join(", ", exitsBy.Select(e => $"{e.Key} {e.Value}"))} |"));
        md.AppendLine();
        md.AppendLine("## Verdict on train");
        md.AppendLine();
        md.AppendLine(pass
            ? $"**H30 passes on train** (mean >= +{minMean:0.0}%, bound > {lowerBoundAbove:0}, >= {minTrades} trades on >= {minDays} days, DSR >= {p.Statistics.DsrMin}, >= 3 of {A09Folds} folds positive). Per amendment 11 this is written into amendment-11.md as a dated note, then validation is read once."
            : $"**H30 fails on train**: {string.Join("; ", failures)}. Per amendment 11: stop. Validation is not read, and no other liquidity floor, score threshold, exit or cost model is tried under this amendment. The move-start signal stays real (amendment 09); this specific liquidity-filtered entry/exit does not make it tradable after costs.");
        md.AppendLine();

        double meanGross = trades.Count == 0 ? double.NaN : trades.Sum(x => x.Weight * x.Gross) / trades.Sum(x => x.Weight) * 100;
        var trial = new ExploreTrial
        {
            Track = LateRunnerRideLiquidityTrack, Config = $"A11-H30 s>={threshold:0.00} adv>=${advFloor:N0} target label20 timeStop {sellBy:hh\\:mm}",
            Metric = "weighted mean net % per trade after the frozen cost model", FoldValues = foldMeans, Pooled = double.IsFinite(bound.Estimate) ? bound.Estimate : null, Rows = trades.Count,
            Note = string.Create(CultureInfo.InvariantCulture, $"low={N3(bound.Low)} high={N3(bound.High)} meanGross={meanGross:0.000} days={days} dsr={N3(dsr)}"),
        };

        var json = new Dictionary<string, object?>
        {
            ["amendment"] = "A11",
            ["hypothesis"] = h.Id,
            ["train"] = new { from = train.From, to = train.To },
            ["entries"] = new { moveStarts = rows.Count, unscored, threshold, advFloor, afterScore, belowLiquidity, signals = afterScore - belowLiquidity, noEntry, alreadyAt20, entries, noSnapshot, csMissing, meanRoundTripCost = costs.Count == 0 ? double.NaN : costs.Average(), medianRoundTripCost = A09Median(costs) },
            ["costModel"] = new { spreadMultiplier = multiplier, spreadMultiplierSource = multiplierSource, notional, floor = p.CostModel.RoundTripFloor },
            ["exitLadder"] = new { stopPct, vwapLossExit = vwap, velocityDeathWindow = deathWindow, sellBy = sellBy.ToString(@"hh\:mm", CultureInfo.InvariantCulture) },
            ["trades"] = trades.Count, ["days"] = days, ["meanNetPct"] = bound.Estimate, ["lowPct"] = bound.Low, ["highPct"] = bound.High, ["meanGrossPct"] = meanGross,
            ["sharpe"] = moments.Sharpe, ["dsr"] = dsr, ["foldMeansPct"] = foldMeans, ["exits"] = exitsBy,
            ["worthTrading"] = new { meanNetPctPerTrade = minMean, clusteredLowerBoundAbove = lowerBoundAbove, minTrades, minDays, dsrMin = p.Statistics.DsrMin, minFoldsPositive = 3 },
            ["pass"] = pass,
            ["failures"] = failures,
        };
        return Write(LateRunnerRideLiquidityTrack, md, [trial], json);
    }
}
