using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using IdiotProof.Evidence;
using IdiotProof.Evidence.Evaluation;
using IdiotProof.Evidence.Gappers;
using IdiotProof.Evidence.Search;
using IdiotProof.Models;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Core.Research.Gappers.Explore;

public sealed partial class GapperExploreStage
{
    /// <summary>The two fade-short signals the deep dive follows (IP-T479): the IP-T476 top configuration and its +50% variant.</summary>
    private sealed record DeepSpec(string Name, double PmGapMin, double ExtensionMax)
    {
        public bool Pass(FadeShortSignal s) => s.GapPct >= FadeShortStillUpPct && s.PmHighGapPct >= PmGapMin && s.ExtensionPct < ExtensionMax;
    }

    private static readonly DeepSpec[] DeepSpecs = [new("pmHigh>=+100% ext<10%", 100, 10), new("pmHigh>=+50% ext<10%", 50, 10)];

    private enum DeepStop { None, Fixed, OpeningRange, Atr }

    /// <summary>One exit of the deep dive: a family name, the plan, and how its stop level is resolved per trade.</summary>
    private sealed record DeepExit(string Family, GapperShortPlan Plan, DeepStop Stop = DeepStop.None, double StopValue = 0)
    {
        public string Name => Plan.Name;
        public bool Base => Family == "base";

        /// <summary>The stop over entry in percent for one trade; null for none or when the name's range is unknown.</summary>
        public double? StopPct(double? openRange, double? atr) => Stop switch
        {
            DeepStop.Fixed => StopValue,
            DeepStop.OpeningRange => openRange is { } r && r > 0 ? StopValue * r * 100 : null,
            DeepStop.Atr => atr is { } a && a > 0 ? StopValue * a * 100 : null,
            _ => null,
        };
    }

    private static readonly TimeSpan DeepCloseEt = new(15, 55, 0);

    private static List<DeepExit> FadeShortDeepExits()
    {
        var list = new List<DeepExit> { new("base", new GapperShortPlan("cover 15:55, no stop (IP-T476 headline)", DeepCloseEt)) };
        foreach (var s in new[] { 20.0, 30, 40, 50, 75 })
            list.Add(new(s >= 50 ? "catastrophe stop" : "fixed stop", new GapperShortPlan($"cover 15:55, stop +{s:0}%", DeepCloseEt), DeepStop.Fixed, s));
        foreach (var k in new[] { 2.0, 3, 4 })
            list.Add(new("volatility stop", new GapperShortPlan($"cover 15:55, stop {k:0} x 09:30-09:45 range", DeepCloseEt), DeepStop.OpeningRange, k));
        foreach (var k in new[] { 2.0, 3, 4 })
            list.Add(new("volatility stop", new GapperShortPlan($"cover 15:55, stop {k:0} x prior 14-day range%", DeepCloseEt), DeepStop.Atr, k));
        foreach (var h in new[] { 11, 12, 13, 14 })
            list.Add(new("time stop", new GapperShortPlan($"cover {h}:00, no stop", new TimeSpan(h, 0, 0))));
        list.Add(new("partial cover", new GapperShortPlan("half at 11:00, rest 15:55", DeepCloseEt, HalfAtEt: new TimeSpan(11, 0, 0))));
        list.Add(new("partial cover", new GapperShortPlan("half on a -15% move (limit), rest 15:55", DeepCloseEt, HalfTargetPct: 15)));
        foreach (var x in new[] { 10.0, 20, 30 })
            list.Add(new("cover into weakness", new GapperShortPlan($"cover on a -{x:0}% move (limit), else 15:55", DeepCloseEt, TargetPct: x)));
        foreach (var a in new[] { 10.0, 20 })
            foreach (var tr in new[] { 5.0, 10 })
                list.Add(new("cover into weakness", new GapperShortPlan($"trail {tr:0}% over the low once -{a:0}%, else 15:55", DeepCloseEt, TrailActivatePct: a, TrailPct: tr)));
        return list;
    }

    private sealed record DeepCandidate(int Row, int Fold, FadeShortSignal Signal, DateTime EntryUtc, decimal Entry, List<Candle> Real, double? Spread, double? Sigma,
        double? OpenRange, double? Atr, decimal PrevClose, bool? HaltPrior5);

    /// <summary>
    /// The three-tier historical shortability proxy (methods-short-risk.md 4.2, thresholds set in advance, not fitted). Tier 3
    /// (likely unborrowable): threshold-listed D-1, shares outstanding under 10M or unknown, prior close under $1, or a halt in the
    /// prior 5 sessions. Tier 1: shares at least 30M and prior close at least $2 with no Tier 3 condition. Tier 2: the rest.
    /// </summary>
    public static int ShortabilityTier(bool regShoD1, double? sharesOutstanding, double prevClose, bool haltPrior5)
    {
        if (regShoD1 || sharesOutstanding is not { } sh || !double.IsFinite(sh) || sh < 10e6 || prevClose < 1 || haltPrior5) return 3;
        return sh >= 30e6 && prevClose >= 2 ? 1 : 2;
    }

    /// <summary>Seeded locate success by tier (assumptions, not estimates).</summary>
    public static readonly double[] TierLocateSuccess = [0.90, 0.50, 0.10];
    public const double TierLocateFee = 0.025;

    private sealed record DeepTrade(int Ci, double Net, double Gross, double Mae, string Reason);

    private sealed record DeepStats(int Trades, int Days, double? Mean, double? Lo, double? Hi, List<double?> Folds, double? Win, double? Worst1, double MaxDrawdown,
        double? MaeP90, double StopShare, double Sharpe, double DaySharpe, ReturnMoments Moments, ReturnMoments DayMoments);

    private static readonly TimeSpan[] DeepCheckpoints = [new(10, 0, 0), new(10, 30, 0), new(11, 0, 0), new(12, 0, 0), new(13, 0, 0), new(14, 0, 0), new(15, 0, 0), new(15, 55, 0)];
    private static readonly double[] DeepFractions = [0.005, 0.01, 0.02, 0.05];
    private const int DeepSizingPaths = 10_000;
    private const int DeepBootDraws = 1_000;
    /// <summary>A squeeze feature stands out only with |AUC - 0.5| at least this (set before the scored run) and an interval that excludes 0.5; a near-constant flag can otherwise clear 0.5 by a hair.</summary>
    private const double DeepMinAucEffect = 0.05;

    /// <summary>
    /// `explore fade-short-deep` (IP-T479): a deep dive on the IP-T476 fade short, TRAIN only, for its top signal (premarket high
    /// >= +100%, extension &lt; 10%) and the +50% variant, both with the SSR uptick entry, Reg SHO names included, 50% APR borrow and
    /// the frozen cost model. Reports the deflated Sharpe against the ledger's trial count, the time-of-day profile of the fade and
    /// when the post-entry high prints, MAE-aware exits (catastrophe, volatility-scaled and time stops, partial covers, covers into
    /// weakness), fixed-fraction tail sizing over day-block bootstrapped years, and single-feature squeeze predictors at 09:45 with
    /// a fold-fitted top-quintile exclusion. One ledger trial per scored exit configuration and per exclusion rule.
    /// </summary>
    private async Task<(List<string>, int, string?)> FadeShortDeepAsync(CancellationToken ct)
    {
        var decision = FadeShortDecision;
        var dt = DecisionTime.Parse(decision);
        var (multiplier, source) = GapperSpreadMultiplier.Resolve(s.Paths, s.Params);
        if (multiplier is null) return ([$"cost model unavailable ({source}); run `calibrate-spread` first"], 0, null);
        var p = s.Params;
        var notional = GapperSpreadMultiplier.TradeNotional(p) ?? throw new InvalidDataException("params.json has no costModel.impact.tradeNotionalUsd.");
        double impact = GapperSpreadMultiplier.ImpactConstant(p), floor = p.CostModel.RoundTripFloor;
        double borrow = GapperShortSimulator.BorrowCost(FadeShortHeadlineBorrowApr);

        var t = Load(decision);
        var folds = Folds(t);
        if (folds.Count == 0) return (["no folds: the panel has too few train days"], 0, null);
        int gap = t.Feature("GapPct"), cs = t.Feature("x." + PriorDaysCorwinSchultzFeature.FeatureName), ssrCol = t.Feature(ShortConstraintFeatures.SsrActive);
        float At(int r, int f) => f < 0 ? float.NaN : t.Value(r, f);
        var seen = new HashSet<(string, DateOnly)>();
        var rows = Enumerable.Range(0, t.Rows).Where(r => t.Weights[r] > 0 && At(r, gap) is var g && !float.IsNaN(g) && g >= FadeShortStillUpPct - 1 && seen.Add((t.Symbols[r], t.Days[r]))).ToList();
        var keys = rows.Select(r => (t.Symbols[r], t.Days[r])).ToHashSet();
        var prevClose = (await s.StudyDaysAsync("train", null, null, ct)).Where(u => keys.Contains((u.Symbol, u.DateEt)))
            .GroupBy(u => (u.Symbol, u.DateEt)).ToDictionary(g => g.Key, g => g.First().AdjPrevClose);

        var store = await s.StoreAsync(ct);
        var panel = new GapperStudyPanel(s);
        var loosest = DeepSpecs.MinBy(x => x.PmGapMin)!;
        var candidates = new List<DeepCandidate>();
        await using (var db = await s.Db.CreateDbContextAsync(ct))
        {
            foreach (var r in rows)
            {
                ct.ThrowIfCancellationRequested();
                if (!prevClose.TryGetValue((t.Symbols[r], t.Days[r]), out var pc) || pc <= 0) continue;
                var bars = store.ReadDay(t.Symbols[r], t.Days[r]);
                if (bars.Count == 0) continue;
                var day = t.Days[r];
                var decisionUtc = MinuteBarMath.ToUtc(day, dt.TimeEt);
                var sig = GapperShortSimulator.Signal(bars, day, pc, decisionUtc);
                if (sig is null || !DeepSpecs.Any(x => x.Pass(sig))) continue;
                var entry = GapperShortSimulator.Entry(bars, decisionUtc, At(r, ssrCol) > 0.5f, ShortSsrHandling.Uptick);
                if (entry is null || entry.Fill <= 0) continue;
                var real = GapperShortPath.Real(bars);
                var openRange = GapperShortPath.OpeningRange(real, day, decisionUtc);
                var symbol = t.Symbols[r];
                var from = day.AddDays(-40);
                var daily = await db.GapperStudyDailyBars.AsNoTracking().Where(b => b.Symbol == symbol && b.DateEt >= from && b.DateEt < day)
                    .OrderBy(b => b.DateEt).Select(b => new { b.High, b.Low, b.Close }).ToListAsync(ct);
                var atr = GapperShortPath.RangePct(daily.Select(b => (b.High, b.Low, b.Close)).ToList());
                var sigma = GapperCostModel.Sigma(await panel.PriorClosesAsync(symbol, day, p.Features.Adv20Sessions, ct), p.Features.Adv20Sessions);
                bool? haltPrior5 = null;
                if (halts is not null)
                {
                    var d5 = day;
                    for (int i = 0; i < 5; i++) d5 = IdiotProof.Core.Evidence.MarketCalendar.PreviousTradingDay(d5);
                    haltPrior5 = halts.Between(symbol, MinuteBarMath.ToUtc(d5, TimeSpan.Zero), MinuteBarMath.ToUtc(day, TimeSpan.Zero)).Any();
                }
                candidates.Add(new DeepCandidate(r, folds.FindIndex(f => f.InEval(day)), sig, entry.BarStartUtc, entry.Fill,
                    real.Where(b => b.StartUtc >= entry.BarStartUtc).ToList(), At(r, cs) is var v && !float.IsNaN(v) ? v : null, sigma, openRange, atr, pc, haltPrior5));
            }
        }
        s.Logger.LogInformation("[{Now}] explore fade-short-deep: {N} candidates (loosest signal {Spec})", GapperStudyServices.Et(s.UtcNow), candidates.Count, loosest.Name);

        double Cost(DeepCandidate c, double size) => GapperCostModel.Estimate(new GapperCostInputs(c.Spread, multiplier.Value, c.Signal.CumVolume, c.Sigma, (double)c.Entry, size, impact, floor)).RoundTrip;
        var costs = candidates.Select(c => Cost(c, notional)).ToArray();
        int regCol = t.Feature(RegShoFeatures.ListedD1), sharesCol = t.Feature(FloatFeatures.LogShares);
        var tiers = candidates.Select(c => ShortabilityTier(At(c.Row, regCol) > 0.5f, At(c.Row, sharesCol) is var ls && !float.IsNaN(ls) ? Math.Exp(ls) : null,
            (double)c.PrevClose, c.HaltPrior5 == true)).ToArray();
        DateOnly DayOf(int ci) => t.Days[candidates[ci].Row];
        double W(int ci) => t.Weights[candidates[ci].Row];

        var exits = FadeShortDeepExits();
        var trades = new Dictionary<(int Spec, int Exit), List<DeepTrade>>();
        for (int si = 0; si < DeepSpecs.Length; si++)
            for (int ei = 0; ei < exits.Count; ei++)
            {
                var list = new List<DeepTrade>();
                for (int ci = 0; ci < candidates.Count; ci++)
                {
                    var c = candidates[ci];
                    if (!DeepSpecs[si].Pass(c.Signal)) continue;
                    var res = GapperShortPath.Simulate(exits[ei].Plan, c.Real, DayOf(ci), c.EntryUtc, c.Entry, exits[ei].StopPct(c.OpenRange, c.Atr));
                    if (res is null) continue;
                    list.Add(new DeepTrade(ci, res.GrossReturn - costs[ci] - borrow, res.GrossReturn, res.MaxAdverse, res.ExitReason));
                }
                trades[(si, ei)] = list.OrderBy(x => DayOf(x.Ci)).ThenBy(x => t.Symbols[candidates[x.Ci].Row], StringComparer.Ordinal).ToList();
            }

        (double Mean, double Lo, double Hi, int Days)? Clustered(IEnumerable<(int Ci, double V)> v) => ExploreDerived.ClusteredMean(v.Select(x => (DayOf(x.Ci), x.V, W(x.Ci))));

        DeepStats Stats(List<DeepTrade> list)
        {
            var pooled = Clustered(list.Select(x => (x.Ci, x.Net)));
            var perFold = folds.Select((_, k) => Clustered(list.Where(x => candidates[x.Ci].Fold == k).Select(x => (x.Ci, x.Net)))?.Mean).ToList();
            double w = list.Sum(x => W(x.Ci));
            double? win = w > 0 ? list.Where(x => x.Net > 0).Sum(x => W(x.Ci)) / w : null;
            double? worst = WeightedQuantile(list.Select(x => (x.Net, W(x.Ci))).ToList(), 0.01);
            double cum = 0, peak = 0, mdd = 0;
            foreach (var x in list) { cum += x.Net; peak = Math.Max(peak, cum); mdd = Math.Max(mdd, peak - cum); }
            var m = ReturnMoments.Of(list.Select(x => x.Net).ToList());
            var dm = ReturnMoments.Of(list.GroupBy(x => DayOf(x.Ci)).Select(g => g.Average(x => x.Net)).ToList());
            return new DeepStats(list.Count, pooled?.Days ?? 0, pooled?.Mean, pooled?.Lo, pooled?.Hi, perFold, win, worst, mdd,
                WeightedQuantile(list.Select(x => (x.Mae, W(x.Ci))).ToList(), 0.9), list.Count == 0 ? 0 : (double)list.Count(x => x.Reason is "stop" or "trail") / list.Count,
                m.Sharpe, dm.Sharpe, m, dm);
        }

        var stats = trades.ToDictionary(kv => kv.Key, kv => Stats(kv.Value));
        int baseExit = exits.FindIndex(e => e.Base);

        // ── squeeze predictors (computed before the trials so the exclusion rule is logged with the rest) ──
        var featureCols = new (string Name, Func<DeepCandidate, double> Value)[]
        {
            ("x.siDaysToCover", c => At(c.Row, t.Feature(ShortConstraintFeatures.DaysToCover))),
            ("x.siChangePct", c => At(c.Row, t.Feature(ShortConstraintFeatures.ChangePct))),
            ("x.floatLogShares", c => At(c.Row, t.Feature(FloatFeatures.LogShares))),
            ("x.floatRotation", c => At(c.Row, t.Feature(FloatFeatures.Rotation))),
            ("x.shortVolShareD1", c => At(c.Row, t.Feature(RegShoFeatures.ShortVolShareD1))),
            ("x.shortVolShareZ", c => At(c.Row, t.Feature(RegShoFeatures.ShortVolShareZ))),
            ("x.regShoListedD1", c => At(c.Row, t.Feature(RegShoFeatures.ListedD1))),
            ("x.ssrActive", c => At(c.Row, ssrCol)),
            ("x.haltPausesD1", c => At(c.Row, t.Feature(HaltFeatures.PriorDayPauses))),
            ("x.haltNewsSinceClose", c => At(c.Row, t.Feature(HaltFeatures.NewsHaltSinceClose))),
            ("x.haltPausesToday (by 09:45)", c => At(c.Row, t.Feature(HaltFeatures.PausesToday))),
            ("x.haltMinutesSinceLast", c => At(c.Row, t.Feature(HaltFeatures.MinutesSinceLastHalt))),
            ("VolRatioTod (volume vs time of day at 09:45)", c => At(c.Row, t.Feature("VolRatioTod"))),
            ("LogPmCumVol", c => At(c.Row, t.Feature("LogPmCumVol"))),
            ("premarket volume share of 04:00-09:45", c => c.Signal.CumVolume > 0 ? (c.Signal.CumVolume - c.Signal.RthVolume) / c.Signal.CumVolume : double.NaN),
            ("premarket high gap % (signal)", c => c.Signal.PmHighGapPct),
            ("gap % at 09:45 (signal)", c => c.Signal.GapPct),
            ("extension from 09:30 open % (signal)", c => c.Signal.ExtensionPct),
            ("09:30-09:45 range / open", c => c.OpenRange ?? double.NaN),
            ("prior 14-day range %", c => c.Atr ?? double.NaN),
            ("log entry price", c => Math.Log((double)c.Entry)),
        };

        var squeeze = new List<(int Spec, double Threshold, List<(string Name, int N, double? Auc, double? Lo, double? Hi, double? MedSq, double? MedRest, bool Stands)> Rows)>();
        var filterRows = new List<(int Spec, string Feature, bool StoodOut, List<(int Fold, double? Edge, bool High, int Eval, int Excluded, int SqExcluded, int SqEval, double? AllMean, double? KeptMean, double? AllWorst, double? KeptWorst)> Folds,
            (double Mean, double Lo, double Hi, int Days)? All, (double Mean, double Lo, double Hi, int Days)? Kept)>();
        var trials = new List<ExploreTrial>();
        for (int si = 0; si < DeepSpecs.Length; si++)
        {
            var list = trades[(si, baseExit)];
            double threshold = WeightedQuantile(list.Select(x => (x.Mae, W(x.Ci))).ToList(), 0.9) ?? double.PositiveInfinity;
            bool Sq(DeepTrade x) => x.Mae >= threshold;
            var frows = new List<(string, int, double?, double?, double?, double?, double?, bool)>();
            foreach (var (name, value) in featureCols)
            {
                var obs = list.Select(x => (x.Ci, V: value(candidates[x.Ci]), Sq: Sq(x))).Where(x => double.IsFinite(x.V)).ToList();
                double? auc = Auc(obs.Select(x => (x.V, x.Sq, W(x.Ci))).ToList());
                var (lo, hi) = AucBootstrap(obs.Select(x => (DayOf(x.Ci), x.V, x.Sq, W(x.Ci))).ToList(), DeepBootDraws, p.Statistics.Seed);
                bool stands = auc is { } au && Math.Abs(au - 0.5) >= DeepMinAucEffect && lo is { } l && hi is { } h && (l > 0.5 || h < 0.5);
                frows.Add((name, obs.Count, auc, lo, hi, MedianOf(obs.Where(x => x.Sq).Select(x => x.V).ToList()), MedianOf(obs.Where(x => !x.Sq).Select(x => x.V).ToList()), stands));
            }
            squeeze.Add((si, threshold, frows));

            // The top feature by |AUC - 0.5| (flagged whether its interval clears 0.5): exclude its squeeze-side quintile,
            // the direction and the edge fitted on each fold's fit window only, scored on the eval window.
            var top = frows.Where(f => f.Item3 is not null).OrderByDescending(f => f.Item8).ThenByDescending(f => Math.Abs(f.Item3!.Value - 0.5)).FirstOrDefault();
            if (top.Item1 is null) continue;
            var fv = featureCols.First(f => f.Name == top.Item1).Value;
            var foldRows = new List<(int, double?, bool, int, int, int, int, double?, double?, double?, double?)>();
            var keptAll = new List<DeepTrade>();
            var evalAll = new List<DeepTrade>();
            for (int k = 0; k < folds.Count; k++)
            {
                var fit = list.Where(x => folds[k].InFit(DayOf(x.Ci))).Select(x => (x, V: fv(candidates[x.Ci]))).Where(x => double.IsFinite(x.V)).ToList();
                var eval = list.Where(x => folds[k].InEval(DayOf(x.Ci))).ToList();
                var fitAuc = Auc(fit.Select(x => (x.V, Sq(x.x), W(x.x.Ci))).ToList());
                bool high = fitAuc is not { } fa || fa >= 0.5;
                double? edge = WeightedQuantile(fit.Select(x => (x.V, W(x.x.Ci))).ToList(), high ? 0.8 : 0.2);
                bool Excluded(DeepTrade x) => edge is { } e && fv(candidates[x.Ci]) is var v && double.IsFinite(v) && (high ? v >= e : v <= e);
                var kept = eval.Where(x => !Excluded(x)).ToList();
                keptAll.AddRange(kept);
                evalAll.AddRange(eval);
                foldRows.Add((k + 1, edge, high, eval.Count, eval.Count - kept.Count, eval.Count(x => Sq(x) && Excluded(x)), eval.Count(Sq),
                    Clustered(eval.Select(x => (x.Ci, x.Net)))?.Mean, Clustered(kept.Select(x => (x.Ci, x.Net)))?.Mean,
                    WeightedQuantile(eval.Select(x => (x.Net, W(x.Ci))).ToList(), 0.01), WeightedQuantile(kept.Select(x => (x.Net, W(x.Ci))).ToList(), 0.01)));
            }
            var allM = Clustered(evalAll.Select(x => (x.Ci, x.Net)));
            var keptM = Clustered(keptAll.Select(x => (x.Ci, x.Net)));
            filterRows.Add((si, top.Item1, top.Item8, foldRows, allM, keptM));
            trials.Add(new ExploreTrial
            {
                Track = "fade-short-deep-filter",
                Config = string.Create(CultureInfo.InvariantCulture, $"t={decision} short {DeepSpecs[si].Name} entry=next open exit=cover 15:55, no stop ssr=Uptick regShoD1=included borrow={FadeShortHeadlineBorrowApr:P0} exclude squeeze-side quintile of {top.Item1} (fold-fitted)"),
                Metric = "meanNetReturn (short, eval windows)", FoldValues = foldRows.Select(f => f.Item9).ToList(), Pooled = keptM?.Mean, Rows = keptAll.Count,
                Note = string.Create(CultureInfo.InvariantCulture, $"lo={DeepPct(keptM?.Lo)} hi={DeepPct(keptM?.Hi)} days={keptM?.Days ?? 0} allEval={DeepPct(allM?.Mean)} stoodOut={top.Item8}"),
            });
        }

        for (int si = 0; si < DeepSpecs.Length; si++)
            for (int ei = 0; ei < exits.Count; ei++)
            {
                if (exits[ei].Base) continue; // already in the ledger from IP-T476
                var st = stats[(si, ei)];
                trials.Add(new ExploreTrial
                {
                    Track = "fade-short-deep",
                    Config = string.Create(CultureInfo.InvariantCulture, $"t={decision} short {DeepSpecs[si].Name} entry=next open exit={exits[ei].Name} ssr=Uptick regShoD1=included borrow={FadeShortHeadlineBorrowApr:P0}"),
                    Metric = "meanNetReturn (short)", FoldValues = st.Folds, Pooled = st.Mean, Rows = st.Trades,
                    Note = string.Create(CultureInfo.InvariantCulture, $"lo={DeepPct(st.Lo)} hi={DeepPct(st.Hi)} days={st.Days} win={DeepShare(st.Win)} worst1={DeepPct(st.Worst1)} maxDD={DeepPct(-st.MaxDrawdown)} sharpe={st.Sharpe:0.000}"),
                });
            }

        // ── shortability tier stress (methods-short-risk.md 4.2): seeded locate success by tier, 2.5% locate fee, 100-share lots ──
        var tierExits = new[] { baseExit, exits.FindIndex(e => e.Stop == DeepStop.Fixed && e.StopValue == 20), exits.FindIndex(e => e.Stop == DeepStop.Fixed && e.StopValue == 30) }.Where(i => i >= 0).ToList();
        var tierRows = new List<(int Spec, int Exit, string Scope, int[] Signals, int[] Traded, (double Mean, double Lo, double Hi, int Days)? M, List<double?> Folds, double? Worst1, double?[] TierMeans)>();
        for (int si = 0; si < DeepSpecs.Length; si++)
            foreach (var ei in tierExits)
                foreach (var gate3 in new[] { false, true })
                {
                    var list = trades[(si, ei)];
                    var signalsBy = new int[3];
                    var tradedBy = new int[3];
                    var kept = new List<(int Ci, double Net)>();
                    foreach (var x in list)
                    {
                        var c = candidates[x.Ci];
                        int tier = tiers[x.Ci];
                        signalsBy[tier - 1]++;
                        if (gate3 && tier == 3) continue;
                        if (GapperShortSimulator.LocateDraw(t.Symbols[c.Row], DayOf(x.Ci)) >= TierLocateSuccess[tier - 1]) continue;
                        long sh = GapperShortSimulator.RoundLotShares(notional, (double)c.Entry);
                        if (sh == 0) continue;
                        double size = sh * (double)c.Entry;
                        tradedBy[tier - 1]++;
                        kept.Add((x.Ci, x.Gross - Cost(c, size) - borrow - TierLocateFee));
                    }
                    var m = Clustered(kept);
                    var perFold = folds.Select((_, k) => Clustered(kept.Where(x => candidates[x.Ci].Fold == k))?.Mean).ToList();
                    var tierMeans = Enumerable.Range(1, 3).Select(tr => Clustered(kept.Where(x => tiers[x.Ci] == tr))?.Mean).ToArray();
                    string scope = gate3 ? "tiers 1-2 only (tier 3 gated out)" : "all tiers";
                    var w1 = WeightedQuantile(kept.Select(x => (x.Net, W(x.Ci))).ToList(), 0.01);
                    tierRows.Add((si, ei, scope, signalsBy, tradedBy, m, perFold, w1, tierMeans));
                    trials.Add(new ExploreTrial
                    {
                        Track = "fade-short-deep-tier",
                        Config = string.Create(CultureInfo.InvariantCulture, $"t={decision} short {DeepSpecs[si].Name} entry=next open exit={exits[ei].Name} ssr=Uptick regShoD1=included borrow={FadeShortHeadlineBorrowApr:P0} shortability tiers locate 90/50/10% fee 2.5% 100-share lots {scope}"),
                        Metric = "meanNetReturn (short)", FoldValues = perFold, Pooled = m?.Mean, Rows = kept.Count,
                        Note = string.Create(CultureInfo.InvariantCulture, $"lo={DeepPct(m?.Lo)} hi={DeepPct(m?.Hi)} days={m?.Days ?? 0} worst1={DeepPct(w1)} tiers={signalsBy[0]}/{signalsBy[1]}/{signalsBy[2]} traded={tradedBy[0]}/{tradedBy[1]}/{tradedBy[2]}"),
                    });
                }

        // ── deflated Sharpe ──
        int ledgerBefore = Ledger.Count;
        int nTrials = ledgerBefore + trials.Count;
        var prior = Ledger.ReadAll();
        var family = prior.Where(x => x.Track.StartsWith("fade-short", StringComparison.Ordinal)).Select(ImpliedSharpes).Where(x => x is not null).Select(x => x!.Value).ToList();
        var allReturn = prior.Where(x => x.Metric.StartsWith("meanNet", StringComparison.Ordinal)).Select(ImpliedSharpes).Where(x => x is not null).Select(x => x!.Value).ToList();
        var exact = stats.Where(kv => !exits[kv.Key.Exit].Base).Select(kv => (Trade: kv.Value.Sharpe, Day: kv.Value.DaySharpe)).ToList();
        double Var(IEnumerable<double> v) { var l = v.Where(double.IsFinite).ToList(); return l.Count > 1 ? Math.Pow(EvidenceStats.StdDev(l), 2) : 0; }
        double varTrade = Var(family.Select(x => x.Trade).Concat(exact.Select(x => x.Trade)));
        double varDay = Var(family.Select(x => x.Day).Concat(exact.Select(x => x.Day)));
        double varTradeAll = Var(allReturn.Select(x => x.Trade).Concat(exact.Select(x => x.Trade)));
        double varDayAll = Var(allReturn.Select(x => x.Day).Concat(exact.Select(x => x.Day)));
        double dsrMin = p.Statistics.DsrMin;

        // ── report ──
        var md = new StringBuilder(Header($"fade short deep dive at {decision} (IP-T479: DSR, time of day, MAE-aware exits, tail sizing, squeeze predictors)", folds));
        md.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"Signals: {string.Join("; ", DeepSpecs.Select(x => x.Name))} (both still >= +{FadeShortStillUpPct:0}% at {decision}; from bars ending by {decision}). Entry: SSR uptick entry as in IP-T476 (the first bar after {decision}, or under Rule 201 the first uptick open within {GapperShortSimulator.UptickWaitMinutes} minutes), Reg SHO D-1 names included. Net of the frozen cost model at ${notional:N0} (premarket multiplier {multiplier:0.###} from {source}, applied unchanged after the open) and {FadeShortHeadlineBorrowApr:P0} APR borrow (one day). Every name assumed borrowable. Pooled = every train day; per fold = the four eval windows."));
        md.AppendLine();
        md.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"Candidates: {candidates.Count:N0} names pass the looser signal with an entry. The base exit reproduces IP-T476's headline: {string.Join("; ", DeepSpecs.Select((x, si) => $"{x.Name} {stats[(si, baseExit)].Trades:N0} trades {DeepBounds(stats[(si, baseExit)].Mean, stats[(si, baseExit)].Lo, stats[(si, baseExit)].Hi)}"))}."));
        md.AppendLine();

        md.AppendLine("## 1. Deflated Sharpe ratio");
        md.AppendLine();
        md.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"`MultipleTesting.DeflatedSharpe` (Bailey & Lopez de Prado): the probabilistic Sharpe of the unweighted net series against the expected maximum Sharpe of N skill-less trials. N = the ledger total after this run = {nTrials:N0} ({ledgerBefore:N0} before + {trials.Count} logged here). The ledger stores no Sharpe, so the variance of trial Sharpes is estimated from the fade-short family: {family.Count} IP-T476 trials (fade-short, fade-short-stress) with each Sharpe implied from its mean and day-clustered interval (per-trade SR ~ mean / (half-width / 1.96 x sqrt(trades)); per-day SR ~ mean / (half-width / 1.96 x sqrt(days))), plus the {exact.Count} exact Sharpes of this run's exits. Sensitivity: the variance over every return-metric trial with an interval ({allReturn.Count} implied, exit routes included). Frozen minimum (params.json statistics.dsrMin): {dsrMin:0.00}."));
        md.AppendLine();
        md.AppendLine("| signal | exit | series | T (observations) | SR per period | skew | kurtosis | V[SR] trials | E[max SR] at N | DSR | DSR (all-return V) | clears dsrMin |");
        md.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|");
        var dsrJson = new List<object>();
        for (int si = 0; si < DeepSpecs.Length; si++)
            foreach (var (series, mom, v, vAll) in new[] { ("per trade", stats[(si, baseExit)].Moments, varTrade, varTradeAll), ("per trade-day (equal-weight day mean)", stats[(si, baseExit)].DayMoments, varDay, varDayAll) })
            {
                double emax = MultipleTesting.ExpectedMaxSharpe(nTrials, v);
                double dsr = MultipleTesting.DeflatedSharpe(mom, nTrials, v), dsrAll = MultipleTesting.DeflatedSharpe(mom, nTrials, vAll);
                md.AppendLine(string.Create(CultureInfo.InvariantCulture,
                    $"| {DeepSpecs[si].Name} | {exits[baseExit].Name} | {series} | {mom.Count:N0} | {mom.Sharpe:0.000} | {mom.Skewness:0.00} | {mom.Kurtosis:0.0} | {v:0.0000} | {emax:0.000} | {dsr:0.000} | {dsrAll:0.000} | {(dsr >= dsrMin ? "**yes**" : "no")} |"));
                dsrJson.Add(new { signal = DeepSpecs[si].Name, exit = exits[baseExit].Name, series, observations = mom.Count, sharpe = mom.Sharpe, skew = mom.Skewness, kurtosis = mom.Kurtosis, trials = nTrials, varianceOfTrialSharpes = v, expectedMax = emax, dsr, dsrAllReturnVariance = dsrAll, dsrMin, clears = dsr >= dsrMin });
            }
        md.AppendLine();
        md.AppendLine("The DSR column of the exit table below is the per-trade DSR of each exit at the same N and variance.");
        md.AppendLine();

        // ── 2. time of day ──
        md.AppendLine("## 2. Time-of-day profile of the fade");
        md.AppendLine();
        md.AppendLine("Gross short return from the entry fill marked at the open of the first bar at or after each time (before cost; 0 when the uptick entry had not filled yet). Mean with a day-clustered 95% interval; weighted median with a day-block bootstrap 95% interval (1,000 draws). Captured = mean at the time / mean at 15:55.");
        md.AppendLine();
        var todJson = new List<object>();
        var maeJson = new List<object>();
        for (int si = 0; si < DeepSpecs.Length; si++)
        {
            var list = trades[(si, baseExit)];
            md.AppendLine($"**{DeepSpecs[si].Name}** ({list.Count:N0} trades)");
            md.AppendLine();
            md.AppendLine("| time ET | mean [95% day-clustered] | median [95% day-block] | captured of the 15:55 mean | share of trades in profit |");
            md.AppendLine("|---|---|---|---|---|");
            var series = DeepCheckpoints.Select(cp => list.Select(x =>
            {
                var c = candidates[x.Ci];
                var at = MinuteBarMath.ToUtc(DayOf(x.Ci), cp);
                return (x.Ci, V: at < c.EntryUtc ? 0 : GapperShortPath.ReturnAt(c.Real, c.EntryUtc, c.Entry, at) ?? 0);
            }).ToList()).ToList();
            double? final = Clustered(series[^1].Select(x => (x.Ci, x.V)))?.Mean;
            for (int k = 0; k < DeepCheckpoints.Length; k++)
            {
                var m = Clustered(series[k].Select(x => (x.Ci, x.V)));
                var (med, mlo, mhi) = MedianBootstrap(series[k].Select(x => (DayOf(x.Ci), x.V, W(x.Ci))).ToList(), DeepBootDraws, p.Statistics.Seed);
                double? captured = m is { } mm && final is { } f && f != 0 ? mm.Mean / f : null;
                double inProfit = series[k].Count == 0 ? double.NaN : (double)series[k].Count(x => x.V > 0) / series[k].Count;
                md.AppendLine($"| {DeepCheckpoints[k]:hh\\:mm} | {(m is { } a ? DeepBounds(a.Mean, a.Lo, a.Hi) : "n/a")} | {DeepBounds(med, mlo, mhi)} | {DeepShare(captured)} | {DeepShare(inProfit)} |");
                todJson.Add(new { signal = DeepSpecs[si].Name, time = DeepCheckpoints[k].ToString(@"hh\:mm"), mean = m?.Mean, lo = m?.Lo, hi = m?.Hi, median = med, medianLo = mlo, medianHi = mhi, captured, inProfit });
            }
            md.AppendLine();

            // When the high after entry prints (MAE timing), for every trade and for the squeeze decile.
            var highs = list.Select(x =>
            {
                var c = candidates[x.Ci];
                var h = GapperShortPath.HighAfterEntry(c.Real, c.EntryUtc, c.Entry, MinuteBarMath.ToUtc(DayOf(x.Ci), DeepCloseEt));
                double? et = h is { } hh ? 570 + (hh.AtUtc - MinuteBarMath.ToUtc(DayOf(x.Ci), MinuteBarMath.RthOpenEt)).TotalMinutes : null;
                double? after = h is { } h2 ? (h2.AtUtc - c.EntryUtc).TotalMinutes : null;
                return (x, Et: et, After: after);
            }).Where(x => x.Et is not null).ToList();
            double thr = squeeze.First(q => q.Spec == si).Threshold;
            (string, double, double)[] buckets = [("entry-10:00", 0, 600), ("10:00-10:30", 600, 630), ("10:30-11:00", 630, 660), ("11:00-12:00", 660, 720), ("12:00-14:00", 720, 840), ("14:00-15:55", 840, 956)];
            md.AppendLine("When the highest high between entry and 15:55 printed (MAE timing):");
            md.AppendLine();
            md.AppendLine("| group | trades | median ET | median minutes after entry | " + string.Join(" | ", buckets.Select(b => b.Item1)) + " |");
            md.AppendLine("|---|---|---|---|" + string.Concat(buckets.Select(_ => "---|")));
            foreach (var (group, pick) in new (string, Func<DeepTrade, bool>)[] { ("all", _ => true), ($"squeeze decile (MAE >= {DeepPct(thr)})", x => x.Mae >= thr), ("rest", x => x.Mae < thr) })
            {
                var g = highs.Where(h => pick(h.x)).ToList();
                var shares = buckets.Select(b => g.Count == 0 ? double.NaN : (double)g.Count(h => h.Et >= b.Item2 && h.Et < b.Item3) / g.Count).ToList();
                md.AppendLine($"| {group} | {g.Count:N0} | {Clock(MedianOf(g.Select(h => h.Et!.Value).ToList()))} | {DeepNum(MedianOf(g.Select(h => h.After!.Value).ToList()))} | {string.Join(" | ", shares.Select(v => DeepShare(v)))} |");
                maeJson.Add(new { signal = DeepSpecs[si].Name, group, trades = g.Count, medianEt = MedianOf(g.Select(h => h.Et!.Value).ToList()), medianAfter = MedianOf(g.Select(h => h.After!.Value).ToList()), buckets = buckets.Select(b => b.Item1).Zip(shares).ToDictionary(z => z.First, z => z.Second) });
            }
            md.AppendLine();
        }

        // ── 3. exits ──
        md.AppendLine("## 3. MAE-aware exits");
        md.AppendLine();
        md.AppendLine("All net of the same cost and borrow. Stops fill at the worse of the level and the bar open; cover limits at the better of the level and the open; the trailing stop sits that far over the lowest low of the bars before the current one, armed once a low reached the activation level. Volatility stops: k x the 09:30-09:45 range over the 09:30 open, or k x the mean daily (high - low) / close over the 14 sessions before D (a name without it trades unstopped). Max DD = largest peak-to-trough of the cumulative per-trade net sum in date order, in units of one position. DSR = per-trade deflated Sharpe at the N and variance above.");
        md.AppendLine();
        md.AppendLine("| signal | family | exit | trades | days | mean net [95% day-clustered] | per fold | folds > 0 | win | worst 1% | max DD | MAE p90 | stopped | SR | DSR |");
        md.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        var exitJson = new List<object>();
        for (int si = 0; si < DeepSpecs.Length; si++)
            for (int ei = 0; ei < exits.Count; ei++)
            {
                var st = stats[(si, ei)];
                double dsr = MultipleTesting.DeflatedSharpe(st.Moments, nTrials, varTrade);
                md.AppendLine(string.Create(CultureInfo.InvariantCulture,
                    $"| {DeepSpecs[si].Name} | {exits[ei].Family} | {exits[ei].Name} | {st.Trades:N0} | {st.Days:N0} | {DeepBounds(st.Mean, st.Lo, st.Hi)} | {string.Join(" / ", st.Folds.Select(DeepPct))} | {st.Folds.Count(f => f > 0)}/{st.Folds.Count} | {DeepShare(st.Win)} | {DeepPct(st.Worst1)} | {DeepPct(-st.MaxDrawdown)} | {DeepPct(st.MaeP90)} | {DeepShare(st.StopShare)} | {st.Sharpe:0.000} | {dsr:0.000} |"));
                exitJson.Add(new { signal = DeepSpecs[si].Name, family = exits[ei].Family, exit = exits[ei].Name, st.Trades, st.Days, st.Mean, st.Lo, st.Hi, folds = st.Folds, st.Win, worst1 = st.Worst1, maxDrawdown = st.MaxDrawdown, maeP90 = st.MaeP90, st.StopShare, sharpe = st.Sharpe, daySharpe = st.DaySharpe, dsr });
            }
        md.AppendLine();
        md.AppendLine("Best three per signal (base exit excluded):");
        md.AppendLine();
        md.AppendLine("| signal | ranked by | exit | mean net [95%] | worst 1% | max DD | folds > 0 |");
        md.AppendLine("|---|---|---|---|---|---|---|");
        for (int si = 0; si < DeepSpecs.Length; si++)
        {
            var others = Enumerable.Range(0, exits.Count).Where(ei => !exits[ei].Base && stats[(si, ei)].Mean is not null).ToList();
            foreach (var (by, order) in new (string, Func<int, double>)[] { ("mean net", ei => stats[(si, ei)].Mean!.Value), ("worst 1%", ei => stats[(si, ei)].Worst1 ?? double.NegativeInfinity) })
                foreach (var ei in others.OrderByDescending(order).Take(3))
                {
                    var st = stats[(si, ei)];
                    md.AppendLine($"| {DeepSpecs[si].Name} | {by} | {exits[ei].Name} | {DeepBounds(st.Mean, st.Lo, st.Hi)} | {DeepPct(st.Worst1)} | {DeepPct(-st.MaxDrawdown)} | {st.Folds.Count(f => f > 0)}/{st.Folds.Count} |");
                }
        }
        md.AppendLine();

        md.AppendLine("## 3b. Historical shortability proxy (methods-short-risk.md 4.2)");
        md.AppendLine();
        md.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"Thresholds set in advance, not fitted. Tier 3 (likely unborrowable): threshold-listed D-1 (`x.regShoListedD1`; the 10-session look-back is approximated by D-1), shares outstanding (latest filing before D) under 10M or unknown, prior close under $1, or a Nasdaq Trader halt in the 5 sessions before D{(halts is null ? " (halt history unavailable: treated as none)" : "")}. Tier 1: shares >= 30M and prior close >= $2 with no Tier 3 condition. Tier 2: the rest. The note's listing-age condition is not applied (no listing date in the panel). Seeded locate success 90% / 50% / 10% by tier (the IP-T476 per symbol-day draw), a {TierLocateFee:P1} locate fee on notional per trade, and the ${notional:N0} position rounded down to 100-share lots (cost at the rounded size). One ledger trial per row."));
        md.AppendLine();
        md.AppendLine("| signal | exit | scope | signals by tier 1/2/3 | traded by tier 1/2/3 | trades | mean net [95% day-clustered] | per fold | worst 1% | mean by tier 1/2/3 |");
        md.AppendLine("|---|---|---|---|---|---|---|---|---|---|");
        var tierJson = new List<object>();
        foreach (var (si, ei, scope, sig, traded, m, perFold, w1, tm) in tierRows)
        {
            md.AppendLine($"| {DeepSpecs[si].Name} | {exits[ei].Name} | {scope} | {sig[0]}/{sig[1]}/{sig[2]} | {traded[0]}/{traded[1]}/{traded[2]} | {traded.Sum():N0} | {(m is { } a ? DeepBounds(a.Mean, a.Lo, a.Hi) : "n/a")} | {string.Join(" / ", perFold.Select(DeepPct))} | {DeepPct(w1)} | {string.Join(" / ", tm.Select(DeepPct))} |");
            tierJson.Add(new { signal = DeepSpecs[si].Name, exit = exits[ei].Name, scope, signalsByTier = sig, tradedByTier = traded, mean = m?.Mean, lo = m?.Lo, hi = m?.Hi, days = m?.Days, folds = perFold, worst1 = w1, meanByTier = tm });
        }
        md.AppendLine();
        md.AppendLine("Base exit by tier with no locate stress (trades, mean net): " + string.Join("; ", DeepSpecs.Select((x, si) => $"{x.Name}: " + string.Join(" / ",
            Enumerable.Range(1, 3).Select(tr => $"tier {tr} {trades[(si, baseExit)].Count(y => tiers[y.Ci] == tr)} at {DeepPct(Clustered(trades[(si, baseExit)].Where(y => tiers[y.Ci] == tr).Select(y => (y.Ci, y.Net)))?.Mean)}")))) + ".");
        md.AppendLine();

        // ── 4. tail sizing ──
        var trainDays = t.Days.Distinct().OrderBy(d => d).ToList();
        double years = Math.Max(0.25, (trainDays[^1].DayNumber - trainDays[0].DayNumber + 1) / 365.25);
        md.AppendLine("## 4. Tail sizing");
        md.AppendLine();
        md.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"{DeepSizingPaths:N0} bootstrapped years per row: whole trade days drawn with replacement until the year holds the train rate of trades (trades / {years:0.00} train years); each trade is short `fraction` of the day's opening equity, so equity x (1 + fraction x the day's summed net returns). Annual return = ending equity - 1. DD = max drawdown from the running peak; ruin = equity below 50% at any point. Seed {p.Statistics.Seed}. Exits: the base, the best mean net and the best worst 1% with a lower bound above 0 (non-base)."));
        md.AppendLine();
        md.AppendLine("Tail stress (methods-short-risk.md): the worst 1% of trades (at least one) replaced by -100% or by -150% before the bootstrap.");
        md.AppendLine();
        md.AppendLine("| signal | exit | tail | trades / year | fraction | median annual return | 5th pct annual return | P(DD > 25%) | P(DD > 50%) | P(ruin) |");
        md.AppendLine("|---|---|---|---|---|---|---|---|---|---|");
        var sizingJson = new List<object>();
        for (int si = 0; si < DeepSpecs.Length; si++)
        {
            var others = Enumerable.Range(0, exits.Count).Where(ei => !exits[ei].Base && stats[(si, ei)].Mean is not null).ToList();
            var picks = new List<int> { baseExit };
            if (others.Count > 0) picks.Add(others.MaxBy(ei => stats[(si, ei)].Mean!.Value));
            var safe = others.Where(ei => stats[(si, ei)].Lo > 0).ToList();
            if (safe.Count > 0) picks.Add(safe.MaxBy(ei => stats[(si, ei)].Worst1 ?? double.NegativeInfinity));
            foreach (var ei in picks.Distinct())
            {
                var list = trades[(si, ei)];
                int perYear = (int)Math.Round(list.Count / years);
                int nWorst = Math.Max(1, (int)Math.Ceiling(0.01 * list.Count));
                var worstSet = list.Select((x, i) => (x.Net, i)).OrderBy(x => x.Net).Take(nWorst).Select(x => x.i).ToHashSet();
                foreach (var (tail, replace) in new (string, double?)[] { ("as is", null), ($"worst {nWorst} -> -100%", -1.0), ($"worst {nWorst} -> -150%", -1.5) })
                foreach (var f in DeepFractions)
                {
                    var days = list.Select((x, i) => (x, Net: replace is { } rv && worstSet.Contains(i) ? rv : x.Net)).GroupBy(x => DayOf(x.x.Ci)).Select(g => g.Select(x => x.Net).ToArray()).ToList();
                    var paths = GapperShortPath.SizingPaths(days, perYear, f, DeepSizingPaths, p.Statistics.Seed + si * 100 + ei);
                    var ret = paths.Select(x => (x.Return, 1.0)).ToList();
                    double? med = WeightedQuantile(ret, 0.5), p5 = WeightedQuantile(ret, 0.05);
                    double dd25 = paths.Count(x => x.MaxDrawdown > 0.25) / (double)paths.Count, dd50 = paths.Count(x => x.MaxDrawdown > 0.50) / (double)paths.Count, ruin = paths.Count(x => x.Ruined) / (double)paths.Count;
                    md.AppendLine(string.Create(CultureInfo.InvariantCulture,
                        $"| {DeepSpecs[si].Name} | {exits[ei].Name} | {tail} | {perYear} | {f:P1} | {DeepPct(med)} | {DeepPct(p5)} | {dd25:P2} | {dd50:P2} | {ruin:P2} |"));
                    sizingJson.Add(new { signal = DeepSpecs[si].Name, exit = exits[ei].Name, tail, tradesPerYear = perYear, fraction = f, medianReturn = med, p5Return = p5, pDd25 = dd25, pDd50 = dd50, pRuin = ruin });
                }
            }
        }
        md.AppendLine();

        // ── 5. squeeze predictors ──
        md.AppendLine("## 5. Squeeze predictors at 09:45");
        md.AppendLine();
        md.AppendLine("Base exit trades. Squeeze = the worst 10% by MAE (MAE at or above the weighted 90th percentile). AUC = weighted single-feature AUC for the squeeze label (> 0.5: higher values squeeze more), rows with the feature missing dropped; interval = day-block bootstrap 95% (1,000 draws). Nothing is fitted and re-scored on the same rows. A feature stands out when its interval excludes 0.5 and |AUC - 0.5| >= 0.05 (a near-constant flag such as x.haltNewsSinceClose can exclude 0.5 by a hair).");
        md.AppendLine();
        var squeezeJson = new List<object>();
        foreach (var (si, threshold, frows) in squeeze)
        {
            md.AppendLine($"**{DeepSpecs[si].Name}** (squeeze = MAE >= {DeepPct(threshold)})");
            md.AppendLine();
            md.AppendLine("| feature | rows | AUC [95% day-block] | median, squeezes | median, rest | stands out |");
            md.AppendLine("|---|---|---|---|---|---|");
            foreach (var (name, n, auc, lo, hi, mSq, mRest, stands) in frows.OrderByDescending(f => f.Item3 is { } a ? Math.Abs(a - 0.5) : -1))
            {
                md.AppendLine($"| {name} | {n:N0} | {F(auc)} [{F(lo)}, {F(hi)}] | {DeepNum(mSq)} | {DeepNum(mRest)} | {(stands ? "**yes**" : "no")} |");
                squeezeJson.Add(new { signal = DeepSpecs[si].Name, feature = name, rows = n, auc, lo, hi, medianSqueeze = mSq, medianRest = mRest, standsOut = stands });
            }
            md.AppendLine();
        }
        md.AppendLine("Exclusion rule, scored by fold: for the top feature (standing out or not, flagged), each fold's fit window sets the squeeze direction (its fit-window AUC) and the quintile edge (weighted 80th, or 20th, percentile); the eval window drops names beyond the edge (missing values kept). One ledger trial per signal.");
        md.AppendLine();
        md.AppendLine("| signal | feature | stood out | fold | edge | side | eval trades | excluded | squeezes excluded / in eval | eval mean, all | eval mean, kept | worst 1%, all | worst 1%, kept |");
        md.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        var filterJson = new List<object>();
        foreach (var (si, feature, stood, frows2, allM, keptM) in filterRows)
        {
            foreach (var (fold, edge, high, n, ex, sqEx, sqN, allMean, keptMean, allW, keptW) in frows2)
                md.AppendLine($"| {DeepSpecs[si].Name} | {feature} | {(stood ? "yes" : "no")} | {fold} | {DeepNum(edge)} | {(high ? "top" : "bottom")} | {n:N0} | {ex:N0} | {sqEx} / {sqN} | {DeepPct(allMean)} | {DeepPct(keptMean)} | {DeepPct(allW)} | {DeepPct(keptW)} |");
            md.AppendLine($"| {DeepSpecs[si].Name} | {feature} | {(stood ? "yes" : "no")} | pooled eval | | | | | | {(allM is { } a ? DeepBounds(a.Mean, a.Lo, a.Hi) : "n/a")} | {(keptM is { } k ? DeepBounds(k.Mean, k.Lo, k.Hi) : "n/a")} | | |");
            filterJson.Add(new { signal = DeepSpecs[si].Name, feature, stoodOut = stood, folds = frows2.Select(f => new { fold = f.Item1, edge = f.Item2, side = f.Item3 ? "top" : "bottom", evalTrades = f.Item4, excluded = f.Item5, squeezesExcluded = f.Item6, squeezesInEval = f.Item7, allMean = f.Item8, keptMean = f.Item9, allWorst1 = f.Item10, keptWorst1 = f.Item11 }), allEval = allM, keptEval = keptM });
        }
        md.AppendLine();
        md.AppendLine("Exploration only (train). Historical borrow availability is unknown, so every name is assumed borrowable; the frozen cost model was built for premarket longs. The top feature was picked on all of train, so the fold rule tests the quintile edge out of sample but not the choice of feature.");

        return Write("fade-short-deep", md, trials, new { dsr = dsrJson, timeOfDay = todJson, maeTiming = maeJson, exits = exitJson, shortability = tierJson, sizing = sizingJson, squeeze = squeezeJson, filter = filterJson });
    }

    private static readonly Regex LoHi = new(@"lo=(?<lo>[-+]*[\d.]+)%\s+hi=(?<hi>[-+]*[\d.]+)%", RegexOptions.Compiled);
    private static readonly Regex DaysRx = new(@"days=(?<d>\d+)", RegexOptions.Compiled);

    /// <summary>A ledger trial's per-trade and per-day Sharpe implied from its pooled mean and day-clustered 95% interval; null without them.</summary>
    private static (double Trade, double Day)? ImpliedSharpes(ExploreTrial x)
    {
        if (x.Pooled is not { } mean || x.Note is null || x.Rows < 2) return null;
        var m = LoHi.Match(x.Note);
        if (!m.Success) return null;
        static double Parse(string v) => (v.Contains('-') ? -1 : 1) * double.Parse(v.TrimStart('-', '+'), CultureInfo.InvariantCulture) / 100;
        double se = (Parse(m.Groups["hi"].Value) - Parse(m.Groups["lo"].Value)) / (2 * 1.96);
        if (!(se > 0)) return null;
        var d = DaysRx.Match(x.Note);
        int days = d.Success ? int.Parse(d.Groups["d"].Value, CultureInfo.InvariantCulture) : x.Rows;
        return (mean / (se * Math.Sqrt(x.Rows)), mean / (se * Math.Sqrt(Math.Max(2, days))));
    }

    private static double? Auc(List<(double V, bool Label, double W)> obs)
    {
        if (obs.Count < 2 || obs.All(o => o.Label) || obs.All(o => !o.Label)) return null;
        return WeightedAuc.Compute(obs.Select(o => o.V).ToList(), obs.Select(o => o.Label).ToList(), obs.Select(o => o.W).ToList());
    }

    /// <summary>Day-block bootstrap percentile interval of the weighted AUC.</summary>
    private static (double? Lo, double? Hi) AucBootstrap(List<(DateOnly Day, double V, bool Label, double W)> obs, int draws, int seed)
    {
        var days = obs.GroupBy(o => o.Day).Select(g => g.ToList()).ToList();
        if (days.Count < 2) return (null, null);
        var rng = new Random(seed);
        var stats = new List<double>(draws);
        for (int b = 0; b < draws; b++)
        {
            var sample = new List<(double, bool, double)>();
            for (int i = 0; i < days.Count; i++) sample.AddRange(days[rng.Next(days.Count)].Select(o => (o.V, o.Label, o.W)));
            if (Auc(sample) is { } a && double.IsFinite(a)) stats.Add(a);
        }
        if (stats.Count < draws / 2) return (null, null);
        stats.Sort();
        return (stats[(int)(0.025 * (stats.Count - 1))], stats[(int)Math.Ceiling(0.975 * (stats.Count - 1))]);
    }

    /// <summary>Weighted median with a day-block bootstrap percentile interval.</summary>
    private static (double? Median, double? Lo, double? Hi) MedianBootstrap(List<(DateOnly Day, double V, double W)> obs, int draws, int seed)
    {
        var med = WeightedQuantile(obs.Select(o => (o.V, o.W)).ToList(), 0.5);
        var days = obs.GroupBy(o => o.Day).Select(g => g.Select(o => (o.V, o.W)).ToList()).ToList();
        if (days.Count < 2) return (med, null, null);
        var rng = new Random(seed);
        var stats = new List<double>(draws);
        for (int b = 0; b < draws; b++)
        {
            var sample = new List<(double, double)>();
            for (int i = 0; i < days.Count; i++) sample.AddRange(days[rng.Next(days.Count)]);
            if (WeightedQuantile(sample, 0.5) is { } q) stats.Add(q);
        }
        stats.Sort();
        return (med, stats[(int)(0.025 * (stats.Count - 1))], stats[(int)Math.Ceiling(0.975 * (stats.Count - 1))]);
    }

    private static string DeepPct(double? v) => v is { } x && double.IsFinite(x) ? x.ToString("+0.00%;-0.00%", CultureInfo.InvariantCulture) : "n/a";
    private static string DeepShare(double? v) => v is { } x && double.IsFinite(x) ? x.ToString("P1", CultureInfo.InvariantCulture) : "n/a";
    private static string DeepNum(double? v) => v is { } x && double.IsFinite(x) ? x.ToString("0.###", CultureInfo.InvariantCulture) : "n/a";
    private static string DeepBounds(double? m, double? lo, double? hi) => m is null ? "n/a" : $"{DeepPct(m)} [{DeepPct(lo)}, {DeepPct(hi)}]";
}
