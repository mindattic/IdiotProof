using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using IdiotProof.Core.Services;
using IdiotProof.Evidence.Gappers;
using IdiotProof.Evidence.Search;
using IdiotProof.Models;
using IdiotProof.Scripting;
using IdiotProof.Strategies.Backtesting;

namespace IdiotProof.Core.Research.Gappers.Explore;

public sealed partial class GapperExploreStage
{
    public const string SequenceTrack = "gapper-sequences";
    public const string SequenceBaselineProfile = "classic-gapper";
    /// <summary>The frozen spread, then the two cost sensitivities the alarm-ride report used. Only the first decides.</summary>
    public static readonly double[] SequenceSpreadScales = [1.0, 0.5, 0.25];
    /// <summary>Amendment 07's bar, on top of the frozen worth-trading numbers in params.json.</summary>
    public const int SequenceMinSecondLegs = 150;
    public const int SequenceMinPositiveFolds = 3;

    private sealed record SeqLeg(int Step, string? StepName, DateTime EntryUtc, DateTime ExitUtc, double EntryPrice, double Gross, double CumVolume, string ExitReason)
    {
        public double[] Cost { get; set; } = [];
    }

    private sealed record SeqRun(string Symbol, DateOnly Day, double Weight, bool IsGapper, List<SeqLeg> Legs, int Transitions)
    {
        public double Net(int scale) => Legs.Sum(l => l.Gross - l.Cost[scale]);
        public double Gross => Legs.Sum(l => l.Gross);
        public double CostOf(int scale) => Legs.Sum(l => l.Cost[scale]);
    }

    private sealed record SeqStrategy(string Id, string Script, TimeSpan LastSellByEt, Func<string, StrategyDefinition> Build);

    /// <summary>
    /// `explore gapper-sequences` (IP-T527, amendment 07): the classic-gapper single-step strategy and the evaluated
    /// multi-step gapper templates, each replayed by the shared <see cref="StrategyBacktester"/> (the evaluator the
    /// Monitor uses) on every train population symbol-day's own minute bars from 04:00 ET, with the study's adjusted
    /// prior close as the gap reference. Each leg pays the frozen cost model at its own entry. One ledger trial per
    /// strategy. A symbol-day whose bars never close +5% over the prior close inside $1-$50 before 09:00 cannot enter
    /// step 0 and is skipped without a replay (an exact skip, not a filter).
    /// </summary>
    private async Task<(List<string>, int, string?)> SequencesAsync(ExploreRequest req, CancellationToken ct)
    {
        var p = s.Params;
        var (multiplier, source) = GapperSpreadMultiplier.Resolve(s.Paths, p);
        if (multiplier is null) return ([$"cost model unavailable ({source}); run `calibrate-spread` first"], 0, null);
        var notional = GapperSpreadMultiplier.TradeNotional(p) ?? throw new InvalidDataException("params.json has no costModel.impact.tradeNotionalUsd.");
        double impact = GapperSpreadMultiplier.ImpactConstant(p), floor = p.CostModel.RoundTripFloor;
        const string spreadPanel = "08:00";
        if (!ExplorePanelFile.Exists(CacheDir(), spreadPanel))
            throw new InvalidOperationException($"No explore panel for {spreadPanel} in {CacheDir()}; run `explore build --times \"{spreadPanel}\"` first (the prior-days spread estimate is read from it).");

        var profile = LoadSequenceBaselineProfile();
        var strategies = new List<SeqStrategy>
        {
            new("baseline: " + SequenceBaselineProfile, GapperScriptFactory.ToScript("SYM", profile), SequenceTime(profile.SellByEt),
                symbol => GapperScriptFactory.Compose(symbol, profile).Build()),
        };
        foreach (var t in GapperSequenceTemplates.All.Where(t => t.Evaluated))
        {
            var sample = t.Compose("SYM").Build();
            var last = sample.AllSteps().Select(d => d.ExitTime).Where(x => x.HasValue).Select(x => x!.Value).DefaultIfEmpty(new TimeSpan(16, 0, 0)).Max();
            strategies.Add(new SeqStrategy(t.Id, sample.ToScript(), last, symbol => t.Compose(symbol).Build()));
        }

        // The population: candidate days plus amendment 01's weighted sample of every other listed day, as `explore build` takes it.
        var amendment = s.Amendment;
        var days = (await s.StudyDaysAsync("train", req.From, req.To, ct))
            .Select(u => (U: u, Sampled: !u.IsCandidate && amendment is not null && amendment.IsSampleMember(u.Symbol, u.DateEt)))
            .Where(x => x.U.IsCandidate || x.Sampled).ToList();
        var train = p.Split("train");
        if (days.Any(d => d.U.DateEt < train.From || d.U.DateEt > train.To || !string.Equals(d.U.Split, "train", StringComparison.Ordinal)))
            throw new InvalidOperationException("A symbol-day outside the train split reached the sequence replay; refusing to continue.");
        var folds = ExploreFolds.Build(days.Select(d => d.U.DateEt), foldMonths.Initial, foldMonths.Eval);
        if (folds.Count == 0) return (["no folds: too few train days"], 0, null);

        var panelTable = ExplorePanelFile.Read(CacheDir(), spreadPanel);
        int csFeature = panelTable.Feature("x." + PriorDaysCorwinSchultzFeature.FeatureName);
        var spreads = new Dictionary<(string, DateOnly), double>();
        if (csFeature >= 0)
            for (int r = 0; r < panelTable.Rows; r++)
                if (panelTable.Value(r, csFeature) is var v && !float.IsNaN(v)) spreads[(panelTable.Symbols[r], panelTable.Days[r])] = v;

        var store = await s.StoreAsync(ct);
        var runs = strategies.Select(_ => new ConcurrentBag<SeqRun>()).ToArray();
        int noBars = 0, screenedOut = 0, replayed = 0, failed = 0, done = 0;
        double minGap = profile.MinGapPercent, minPrice = profile.MinPrice, maxPrice = profile.MaxPrice;
        var windowTo = SequenceTime(profile.EntryWindowEndEt);
        foreach (var chunk in days.Chunk(512))
        {
            var inputs = new List<(GapperStudyUniverseDayRef U, double Weight, IReadOnlyList<Candle> Bars)>();
            foreach (var (u, sampled) in chunk)
            {
                ct.ThrowIfCancellationRequested();
                var bars = store.ReadDay(u.Symbol, u.DateEt);
                if (bars.Count < 2) { noBars++; continue; }
                double prev = (double)u.AdjPrevClose;
                // Step 0 needs a bar ending before the window's end that closes at least the minimum gap up inside the price band.
                bool possible = prev > 0 && bars.Any(b => MinuteBarMath.ToEastern(b.StartUtc).TimeOfDay < windowTo
                    && (double)b.Close >= minPrice && (double)b.Close <= maxPrice && ((double)b.Close - prev) / prev * 100 >= minGap);
                if (!possible) { screenedOut++; continue; }
                inputs.Add((new GapperStudyUniverseDayRef(u.Symbol, u.DateEt, u.AdjPrevClose, u.IsGapper), sampled ? amendment!.SampleWeight : 1, bars));
            }
            Parallel.ForEach(inputs, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, req.Parallelism), CancellationToken = ct }, input =>
            {
                var (u, weight, bars) = input;
                try
                {
                    for (int k = 0; k < strategies.Count; k++)
                    {
                        var st = strategies[k];
                        var cut = SequenceBars(bars, MinuteBarMath.ToUtc(u.Day, st.LastSellByEt));
                        var report = StrategyBacktester.Run(st.Build(u.Symbol), cut, new BacktestOptions { PreviousClose = u.PrevClose });
                        if (report.Trades.Count == 0)
                        {
                            // Step 0 of every template is the baseline's ride: no baseline trade means no sequence either.
                            if (k == 0) break;
                            continue;
                        }
                        var legs = report.Trades.Select(t => new SeqLeg(t.Step, t.StepName, t.EntryUtc, t.Exits.Count > 0 ? t.Exits[^1].Utc : t.EntryUtc, (double)t.EntryPrice,
                            (double)t.ReturnPercent / 100, bars.Where(b => b.StartUtc <= t.EntryUtc).Sum(b => (double)b.Volume), t.ExitReason)).ToList();
                        runs[k].Add(new SeqRun(u.Symbol, u.Day, weight, u.IsGapper, legs, report.StepTransitions.Count));
                    }
                    Interlocked.Increment(ref replayed);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Interlocked.Increment(ref failed);
                    s.Logger.LogWarning("explore gapper-sequences: {Symbol} {Day} failed: {Message}", u.Symbol, u.Day, ex.Message);
                }
            });
            done += chunk.Length;
            s.Logger.LogInformation("[{Now}] explore gapper-sequences: {Done}/{Total} symbol-days, {Replayed} replayed, {Trades} baseline trades",
                GapperStudyServices.Et(s.UtcNow), done, days.Count, replayed, runs[0].Count);
        }

        // Costs: the frozen model at each leg's own entry (prior-days spread x the calibrated multiplier, square-root impact on
        // the volume since 04:00, 20-session sigma, floor), then the two spread sensitivities.
        var panel = new GapperStudyPanel(s);
        var sigmas = new Dictionary<(string, DateOnly), double?>();
        var ordered = runs.Select(b => b.OrderBy(r => r.Day).ThenBy(r => r.Symbol, StringComparer.Ordinal).ToList()).ToList();
        int spreadMissing = 0, legCount = 0;
        foreach (var run in ordered.SelectMany(l => l))
        {
            ct.ThrowIfCancellationRequested();
            if (!sigmas.TryGetValue((run.Symbol, run.Day), out var sigma))
                sigmas[(run.Symbol, run.Day)] = sigma = GapperCostModel.Sigma(await panel.PriorClosesAsync(run.Symbol, run.Day, p.Features.Adv20Sessions, ct), p.Features.Adv20Sessions);
            double? spread = spreads.TryGetValue((run.Symbol, run.Day), out var cs) ? cs : null;
            foreach (var leg in run.Legs)
            {
                legCount++;
                if (spread is null) spreadMissing++;
                var frozen = SequenceSpreadScales.Select(scale =>
                    GapperCostModel.Estimate(new GapperCostInputs(spread, multiplier.Value * scale, leg.CumVolume, sigma, leg.EntryPrice, notional, impact, floor)).RoundTrip).ToList();
                // Last level: a later leg (after 09:30) at the regular-hours cost of the ORB report; the ride keeps the frozen cost.
                frozen.Add(leg.Step == 0 ? frozen[0] : (spread ?? 0) + 2 * OrbCommissionPerShare / leg.EntryPrice);
                leg.Cost = frozen.ToArray();
            }
        }

        return SequencesReport(strategies, ordered, folds, days.Count, days.Count(d => d.U.IsCandidate), noBars, screenedOut, replayed, failed, spreadMissing, legCount,
            multiplier.Value, source, notional, req);
    }

    private static TimeSpan SequenceTime(string hhmm) => TimeSpan.ParseExact(hhmm, @"hh\:mm", CultureInfo.InvariantCulture);

    private sealed record GapperStudyUniverseDayRef(string Symbol, DateOnly Day, decimal PrevClose, bool IsGapper);

    /// <summary>The day's bars through the first bar starting at or after the strategy's last sell-by: nothing can trade after it.</summary>
    private static IReadOnlyList<Candle> SequenceBars(IReadOnlyList<Candle> bars, DateTime lastSellByUtc)
    {
        for (int i = 0; i < bars.Count; i++)
            if (bars[i].StartUtc >= lastSellByUtc) return i == bars.Count - 1 ? bars : bars.Take(i + 1).ToList();
        return bars;
    }

    private static GapperProfile LoadSequenceBaselineProfile()
    {
        var path = IdiotProof.Core.Catalogs.StaticCatalogSource.Default.Resolve("gapper-profiles.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        foreach (var el in doc.RootElement.GetProperty("profiles").EnumerateArray())
            if (el.TryGetProperty("id", out var id) && id.GetString() == SequenceBaselineProfile)
                return JsonSerializer.Deserialize<GapperProfile>(el.GetRawText()) ?? throw new InvalidDataException("classic-gapper did not deserialize.");
        throw new InvalidDataException($"gapper-profiles.json has no '{SequenceBaselineProfile}' profile.");
    }

    private static double? WeightedMedian(IEnumerable<(double Value, double Weight)> rows)
    {
        var list = rows.Where(r => double.IsFinite(r.Value) && r.Weight > 0).OrderBy(r => r.Value).ToList();
        double total = list.Sum(r => r.Weight), acc = 0;
        if (list.Count == 0 || total <= 0) return null;
        foreach (var (value, weight) in list)
        {
            acc += weight;
            if (acc >= total / 2) return value;
        }
        return list[^1].Value;
    }

    private (List<string>, int, string?) SequencesReport(List<SeqStrategy> strategies, List<List<SeqRun>> runs, List<ExploreFold> folds, int population, int candidates,
        int noBars, int screenedOut, int replayed, int failed, int spreadMissing, int legCount, double multiplier, string source, double notional, ExploreRequest req)
    {
        var p = s.Params;
        static string Pct(double? v) => v is { } x && double.IsFinite(x) ? (x * 100).ToString("+0.00;-0.00", CultureInfo.InvariantCulture) + "%" : "n/a";
        static string Bounds((double Mean, double Lo, double Hi, int Days)? m) => m is { } x ? $"{Pct(x.Mean)} [{Pct(x.Lo)}, {Pct(x.Hi)}]" : "n/a";
        static string Share(double n, double d) => d <= 0 ? "n/a" : (n / d).ToString("P1", CultureInfo.InvariantCulture);
        static (double Mean, double Lo, double Hi, int Days)? Clustered(IEnumerable<(DateOnly Day, double Value, double Weight)> v) => ExploreDerived.ClusteredMean(v);

        var worth = p.CostModel.WorthTrading;
        double minMean = worth.GetProperty("meanNetPctPerTrade").GetDouble() / 100;
        int minTrades = worth.GetProperty("minValidationTrades").GetInt32(), minDays = worth.GetProperty("minValidationDays").GetInt32();
        double dsrMin = p.Statistics.DsrMin;
        int nTrials = Ledger.Count + strategies.Count;
        var moments = runs.Select(l => ReturnMoments.Of(l.Select(r => r.Net(0)).ToList())).ToList();
        var sharpes = moments.Where(m => m.Count >= 2).Select(m => m.Sharpe).Where(double.IsFinite).ToList();
        double varSr = sharpes.Count > 1 ? Math.Pow(IdiotProof.Evidence.EvidenceStats.StdDev(sharpes), 2) : 0;
        var baseline = runs[0].ToDictionary(r => (r.Symbol, r.Day));

        var md = new StringBuilder(Header("long-only multi-step gapper templates against the single-step gapper (IP-T527, amendment 07)", folds));
        md.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"Population: {population:N0} train symbol-days ({candidates:N0} candidates, {population - candidates:N0} amendment-01 sample days at weight {s.Amendment?.SampleWeight ?? 1}){(req.From is not null || req.To is not null ? $", restricted to {req.From:yyyy-MM-dd}..{req.To:yyyy-MM-dd} (a partial run)" : "")}. {noBars:N0} had no minute bars; {screenedOut:N0} never closed the minimum gap over the prior close inside the price band before the entry window closed, so step 0 could not enter and they were not replayed; {replayed:N0} were replayed ({failed:N0} failed)."));
        md.AppendLine();
        md.AppendLine("Replay: `StrategyBacktester.Run` (IP-LAW-10: the evaluator the Monitor uses) on the day's own minute bars from 04:00 ET through the first bar at or after the strategy's last sell-by, prior close = the study's split-adjusted prior close. Entries and signal exits fill at the bar's close, a stop at the stop (or the open of a bar that gaps through it), as that evaluator does. One sequence run = one symbol-day on which step 0 entered; its return is the sum of its legs' returns, each leg on the same notional.");
        md.AppendLine();
        md.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"Cost per leg: the frozen model at ${notional:N0} at the leg's own entry (prior-days Corwin-Schultz spread x the premarket multiplier {multiplier:0.###} from {source}, square-root impact on the volume since 04:00, 20-session sigma, floor {p.CostModel.RoundTripFloor:P1}); {spreadMissing:N0} of {legCount:N0} legs had no spread estimate (floor and impact only). The 0.5x and 0.25x columns scale the spread term only; the regular-hours column charges a later leg the prior-days spread with no multiplier plus ${OrbCommissionPerShare:0.0000}/share each side (the ORB report's cost) and leaves the ride at the frozen cost. None of the three decides anything. Intervals: day-clustered 95%, weighted."));
        md.AppendLine();
        md.AppendLine("## Strategies");
        md.AppendLine();
        foreach (var st in strategies)
        {
            md.AppendLine($"**{st.Id}**");
            md.AppendLine();
            md.AppendLine("```");
            md.AppendLine(st.Script.Trim());
            md.AppendLine("```");
            md.AppendLine();
        }

        md.AppendLine("## Per sequence run (the unit amendment 07 decides on)");
        md.AppendLine();
        md.AppendLine("| strategy | runs | days | weighted runs | gapper days (Tier20+) | legs: step 0 / step 1 | gross mean | mean frozen cost | **net mean, frozen [95%]** | net median | win rate | net 0.5x spread [95%] | net 0.25x spread [95%] | net, later legs at regular-hours cost [95%] | " + string.Join(" | ", folds.Select(f => $"fold {f.Number}")) + " | folds > 0 | DSR |");
        md.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|" + string.Concat(folds.Select(_ => "---|")) + "---|---|");
        var trials = new List<ExploreTrial>();
        var json = new List<object>();
        var verdicts = new List<string>();
        for (int k = 0; k < strategies.Count; k++)
        {
            var list = runs[k];
            var net = Enumerable.Range(0, SequenceSpreadScales.Length + 1).Select(x => Clustered(list.Select(r => (r.Day, r.Net(x), r.Weight)))).ToArray();
            var gross = Clustered(list.Select(r => (r.Day, r.Gross, r.Weight)));
            var cost = Clustered(list.Select(r => (r.Day, r.CostOf(0), r.Weight)));
            var perFold = folds.Select(f => Clustered(list.Where(r => f.InEval(r.Day)).Select(r => (r.Day, r.Net(0), r.Weight)))?.Mean).ToList();
            int positiveFolds = perFold.Count(v => v is > 0);
            double wRuns = list.Sum(r => r.Weight), wWins = list.Where(r => r.Net(0) > 0).Sum(r => r.Weight);
            var median = WeightedMedian(list.Select(r => (r.Net(0), r.Weight)));
            int step0 = list.Sum(r => r.Legs.Count(l => l.Step == 0)), step1 = list.Sum(r => r.Legs.Count(l => l.Step > 0));
            double dsr = moments[k].Count >= 2 ? MultipleTesting.DeflatedSharpe(moments[k], nTrials, varSr) : double.NaN;
            int distinctDays = list.Select(r => r.Day).Distinct().Count();

            // Paired against the baseline on the same symbol-days, and the later legs on their own.
            var secondLegs = list.SelectMany(r => r.Legs.Where(l => l.Step > 0).Select(l => (r.Day, Net: l.Gross - l.Cost[0], Gross: l.Gross, r.Weight, l.ExitReason))).ToList();
            var paired = k == 0 ? null : Clustered(list.Where(r => baseline.ContainsKey((r.Symbol, r.Day))).Select(r => (r.Day, r.Net(0) - baseline[(r.Symbol, r.Day)].Net(0), r.Weight)));
            int sameRide = k == 0 ? list.Count : list.Count(r => baseline.TryGetValue((r.Symbol, r.Day), out var b) && b.Legs.Count > 0 && r.Legs.Count > 0
                && Math.Abs(b.Legs[0].Gross - r.Legs[0].Gross) < 1e-12 && b.Legs[0].EntryUtc == r.Legs[0].EntryUtc);
            var legNet = Clustered(secondLegs.Select(l => (l.Day, l.Net, l.Weight)));
            var legGross = Clustered(secondLegs.Select(l => (l.Day, l.Gross, l.Weight)));
            var legMedian = WeightedMedian(secondLegs.Select(l => (l.Net, l.Weight)));

            bool meanOk = net[0] is { } n0 && n0.Mean >= minMean && n0.Lo > 0;
            bool sizeOk = list.Count >= minTrades && distinctDays >= minDays;
            bool legsOk = k == 0 || secondLegs.Count >= SequenceMinSecondLegs;
            bool foldsOk = positiveFolds >= SequenceMinPositiveFolds;
            bool pairedOk = k == 0 || (paired is { } pd && pd.Mean > 0 && pd.Lo > 0);
            bool dsrOk = double.IsFinite(dsr) && dsr >= dsrMin;
            bool pass = meanOk && sizeOk && legsOk && foldsOk && pairedOk && dsrOk;

            md.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"| {strategies[k].Id} | {list.Count:N0} | {distinctDays:N0} | {wRuns:N0} | {list.Count(r => r.IsGapper):N0} | {step0:N0} / {step1:N0} | {Pct(gross?.Mean)} | {Pct(cost?.Mean)} | **{Bounds(net[0])}** | {Pct(median)} | {Share(wWins, wRuns)} | {Bounds(net[1])} | {Bounds(net[2])} | {Bounds(net[3])} | {string.Join(" | ", perFold.Select(Pct))} | {positiveFolds}/{folds.Count} | {(double.IsFinite(dsr) ? dsr.ToString("0.000", CultureInfo.InvariantCulture) : "n/a")} |"));

            if (k > 0)
                verdicts.Add(string.Create(CultureInfo.InvariantCulture,
                    $"| {strategies[k].Id} | {(meanOk ? "yes" : "no")} ({Bounds(net[0])}) | {(sizeOk ? "yes" : "no")} ({list.Count:N0} runs, {distinctDays:N0} days) | {(legsOk ? "yes" : "no")} ({secondLegs.Count:N0}) | {(foldsOk ? "yes" : "no")} ({positiveFolds}/{folds.Count}) | {(pairedOk ? "yes" : "no")} ({Bounds(paired)}) | {(dsrOk ? "yes" : "no")} ({(double.IsFinite(dsr) ? dsr.ToString("0.000", CultureInfo.InvariantCulture) : "n/a")}) | **{(pass ? "PASS" : "FAIL")}** |"));

            trials.Add(new ExploreTrial
            {
                Track = SequenceTrack, Config = $"{strategies[k].Id}: {strategies[k].Script.Trim().ReplaceLineEndings(" ")}",
                Metric = "meanNetReturn per sequence run (frozen cost per leg, StrategyBacktester)", FoldValues = perFold, Pooled = net[0]?.Mean, Rows = list.Count,
                Note = string.Create(CultureInfo.InvariantCulture,
                    $"lo={Pct(net[0]?.Lo)} hi={Pct(net[0]?.Hi)} days={distinctDays} median={Pct(median)} gross={Pct(gross?.Mean)} laterLegs={secondLegs.Count} laterLegNet={Pct(legNet?.Mean)} pairedVsBaseline={Pct(paired?.Mean)} [{Pct(paired?.Lo)}, {Pct(paired?.Hi)}] dsr={(double.IsFinite(dsr) ? dsr.ToString("0.000", CultureInfo.InvariantCulture) : "n/a")} pass={(k == 0 ? "n/a (baseline)" : pass ? "yes" : "no")}"),
            });
            json.Add(new
            {
                strategy = strategies[k].Id, script = strategies[k].Script.Trim(), runs = list.Count, days = distinctDays, weightedRuns = wRuns, gapperDays = list.Count(r => r.IsGapper),
                step0Legs = step0, laterLegs = step1, transitions = list.Sum(r => r.Transitions), gross = gross?.Mean, meanFrozenCost = cost?.Mean,
                net = net.Select(m => m is { } v ? new { v.Mean, v.Lo, v.Hi, v.Days } : null).ToList(), median, winRate = wRuns > 0 ? wWins / wRuns : (double?)null,
                folds = perFold, positiveFolds, sharpe = moments[k].Sharpe, dsr = double.IsFinite(dsr) ? dsr : (double?)null,
                pairedVsBaseline = paired is { } q ? new { q.Mean, q.Lo, q.Hi, q.Days } : null, sameRideAsBaseline = sameRide,
                laterLeg = new { trades = secondLegs.Count, days = secondLegs.Select(l => l.Day).Distinct().Count(), gross = legGross?.Mean, net = legNet is { } ln ? new { ln.Mean, ln.Lo, ln.Hi, ln.Days } : null, median = legMedian,
                    winRate = secondLegs.Count == 0 ? (double?)null : secondLegs.Where(l => l.Net > 0).Sum(l => l.Weight) / secondLegs.Sum(l => l.Weight),
                    exits = secondLegs.GroupBy(l => l.ExitReason).ToDictionary(g => g.Key, g => g.Count()) },
                rideExits = list.SelectMany(r => r.Legs.Where(l => l.Step == 0)).GroupBy(l => l.ExitReason).ToDictionary(g => g.Key, g => g.Count()),
                criterion = k == 0 ? null : new { meanOk, sizeOk, legsOk, foldsOk, pairedOk, dsrOk, pass },
                trades = list.Select(r => new { r.Symbol, day = r.Day.ToString("yyyy-MM-dd"), r.Weight, gapper = r.IsGapper, net = r.Net(0),
                    legs = r.Legs.Select(l => new { l.Step, entryEt = EasternTime.FromUtc(l.EntryUtc).ToString("HH:mm"), exitEt = EasternTime.FromUtc(l.ExitUtc).ToString("HH:mm"), l.EntryPrice, l.Gross, cost = l.Cost[0], l.ExitReason }) }).ToList(),
            });
        }

        md.AppendLine();
        md.AppendLine("## The later leg, and the sequence against the baseline on the same symbol-days");
        md.AppendLine();
        md.AppendLine("| strategy | runs with the same ride as the baseline | hand-offs (ThenWhen held) | later-leg trades | days | later-leg gross mean [95%] | **later-leg net mean, frozen [95%]** | later-leg net median | later-leg win rate | later-leg net at regular-hours cost [95%] | later-leg exits | **sequence minus baseline, paired [95%]** |");
        md.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|");
        for (int k = 1; k < strategies.Count; k++)
        {
            var list = runs[k];
            var legs = list.SelectMany(r => r.Legs.Where(l => l.Step > 0).Select(l => (r.Day, Net: l.Gross - l.Cost[0], l.Gross, r.Weight, l.ExitReason, NetRth: l.Gross - l.Cost[^1]))).ToList();
            var paired = Clustered(list.Where(r => baseline.ContainsKey((r.Symbol, r.Day))).Select(r => (r.Day, r.Net(0) - baseline[(r.Symbol, r.Day)].Net(0), r.Weight)));
            int sameRide = list.Count(r => baseline.TryGetValue((r.Symbol, r.Day), out var b) && b.Legs.Count > 0 && r.Legs.Count > 0
                && Math.Abs(b.Legs[0].Gross - r.Legs[0].Gross) < 1e-12 && b.Legs[0].EntryUtc == r.Legs[0].EntryUtc);
            double w = legs.Sum(l => l.Weight);
            md.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"| {strategies[k].Id} | {sameRide:N0} of {list.Count:N0} (baseline {runs[0].Count:N0}) | {list.Sum(r => r.Transitions):N0} | {legs.Count:N0} | {legs.Select(l => l.Day).Distinct().Count():N0} | {Bounds(Clustered(legs.Select(l => (l.Day, l.Gross, l.Weight))))} | **{Bounds(Clustered(legs.Select(l => (l.Day, l.Net, l.Weight))))}** | {Pct(WeightedMedian(legs.Select(l => (l.Net, l.Weight))))} | {Share(legs.Where(l => l.Net > 0).Sum(l => l.Weight), w)} | {Bounds(Clustered(legs.Select(l => (l.Day, l.NetRth, l.Weight))))} | {string.Join(", ", legs.GroupBy(l => l.ExitReason).OrderByDescending(g => g.Count()).Select(g => $"{g.Key} {g.Count():N0}"))} | **{Bounds(paired)}** |"));
        }
        md.AppendLine();
        md.AppendLine("Ride exits (step 0): " + string.Join("; ", strategies.Select((st, k) => $"{st.Id}: " + string.Join(", ", runs[k].SelectMany(r => r.Legs.Where(l => l.Step == 0)).GroupBy(l => l.ExitReason).OrderByDescending(g => g.Count()).Select(g => $"{g.Key} {g.Count():N0}")))) + ".");
        md.AppendLine();
        md.AppendLine("## Amendment 07 criterion (train, frozen cost)");
        md.AppendLine();
        md.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"A template passes only if all hold: net mean per run >= {Pct(minMean)} with the lower bound above 0; >= {minTrades} runs on >= {minDays} days; >= {SequenceMinSecondLegs} later-leg trades; net mean above 0 in >= {SequenceMinPositiveFolds} of the {folds.Count} fold windows; sequence minus baseline (paired, same symbol-days) above 0 with the lower bound above 0; deflated Sharpe >= {dsrMin:0.00} at N = {nTrials:N0} trials (the ledger after this run; variance of this run's trial Sharpes {varSr:0.0000})."));
        md.AppendLine();
        md.AppendLine("| template | mean >= bar, lower bound > 0 | runs and days | later legs | folds | beats the baseline | DSR | verdict |");
        md.AppendLine("|---|---|---|---|---|---|---|---|");
        foreach (var v in verdicts) md.AppendLine(v);
        md.AppendLine();
        md.AppendLine("Train only. A pass here would make a template selectable, never armed, and it would still owe validation, the single holdout run and the paper shadow (protocol, promotion path). A fail leaves the templates as samples.");
        return Write(SequenceTrack, md, trials, json);
    }
}
