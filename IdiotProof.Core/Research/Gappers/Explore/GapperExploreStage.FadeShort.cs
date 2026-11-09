using System.Globalization;
using System.Text;
using IdiotProof.Evidence.Gappers;
using IdiotProof.Models;

namespace IdiotProof.Core.Research.Gappers.Explore;

public sealed partial class GapperExploreStage
{
    /// <summary>The fade short decides at 09:45 ET: the open's first quarter hour is in, the LULD bands contract.</summary>
    public const string FadeShortDecision = "09:45";
    /// <summary>Still at least this far over the prior close at the decision (the random-short baseline's only filter).</summary>
    public const double FadeShortStillUpPct = 20;
    public static readonly double[] FadeShortExtensionMaxPct = [10, 5, 0];
    public static readonly double[] FadeShortPmGapMinPct = [50, 30, 100];
    public static readonly double[] FadeShortBorrowApr = [0, 0.5, 2.0];
    /// <summary>The borrow fee the trials and the main table carry; 0% and 200% are reported beside it.</summary>
    public const double FadeShortHeadlineBorrowApr = 0.5;
    public static readonly ShortSsrHandling[] FadeShortSsr = [ShortSsrHandling.Uptick, ShortSsrHandling.Skip, ShortSsrHandling.Ignore];

    public static IReadOnlyList<GapperShortExit> FadeShortExits()
    {
        var list = new List<GapperShortExit>();
        foreach (var cover in new[] { new TimeSpan(15, 55, 0), new TimeSpan(11, 0, 0) })
            foreach (var stop in new double?[] { null, 10, 20 })
                foreach (var vwap in new[] { false, true })
                    list.Add(new GapperShortExit(cover, stop, vwap));
        return list;
    }

    /// <summary>One signal filter: premarket high at least this far over the prior close, extension from the 09:30 open below this; null = the random short.</summary>
    private sealed record ShortSignalSpec(string Name, double? PmGapMin, double? ExtensionMax)
    {
        public bool Random => PmGapMin is null;
        public bool Pass(FadeShortSignal s) => s.GapPct >= FadeShortStillUpPct && (PmGapMin is not { } p || s.PmHighGapPct >= p) && (ExtensionMax is not { } e || s.ExtensionPct < e);
    }

    /// <summary>A borrow stress on top of a configuration: locate failures, a locate fee per trade, 100-share lots.</summary>
    private sealed record ShortStress(string Name, double LocateFailRate = 0, double LocateCost = 0, bool RoundLots = false);

    private static readonly ShortStress[] FadeShortStresses =
    [
        new("locate fails 30%", LocateFailRate: 0.30),
        new("locate fails 60%", LocateFailRate: 0.60),
        new("locate fee 1.0%", LocateCost: 0.010),
        new("locate fee 2.5%", LocateCost: 0.025),
        new("100-share lots", RoundLots: true),
        new("combined: fails 60%, fee 2.5%, 100-share lots", 0.60, 0.025, true),
    ];

    private sealed record ShortCandidate(int Row, int Fold, FadeShortSignal Signal, bool Ssr, bool SsrKnown, bool RegSho, double? Spread, double? Sigma);

    private sealed record ShortOutcome(double Gross, double Entry, string Reason, bool GapFill, double MaxAdverse);

    private sealed record ShortStats(int Trades, int Days, double? Mean, double? Lo, double? Hi, List<double?> Folds, double? Win, double? Worst1, double? MarginMean, double? MarginLo, double? MarginHi,
        double? LomoMinMean, double? LomoMinLo, string? LomoWorstMonth, double StopShare, double GapShare, double? StopGapShare, double? BorrowZero, double? BorrowHigh, bool Finalist);

    /// <summary>
    /// `explore fade-short` (IP-T476): the costed test of track 6's one hint. At 09:45 ET, short names whose premarket high
    /// (04:00-09:29) was at least +30/50/100% over the adjusted prior close, still at least +20% over it at 09:45, and up less
    /// than 10/5/0% from the 09:30 open; enter at the next bar's open (under Rule 201, at the first uptick open within 30
    /// minutes, or skip); cover by 15:55 or 11:00, with no stop or a +10/+20% stop (filled at the worse of the stop and the
    /// bar's open) and with or without a VWAP reclaim exit. Net of the frozen cost model and a borrow fee; per fold and pooled
    /// over all train; against a random short of every +20% name at 09:45. One ledger trial per signal x exit x SSR x Reg SHO
    /// at 50% APR borrow.
    /// </summary>
    private async Task<(List<string>, int, string?)> FadeShortAsync(IReadOnlyList<string> times, CancellationToken ct)
    {
        var decision = times.Count == 1 ? times[0] : FadeShortDecision;
        var dt = DecisionTime.Parse(decision);
        if (dt.DayOffset != 0 || dt.TimeEt <= MinuteBarMath.RthOpenEt) throw new InvalidOperationException("The fade short decides after the 09:30 open on the morning itself.");
        var (multiplier, source) = GapperSpreadMultiplier.Resolve(s.Paths, s.Params);
        if (multiplier is null) return ([$"cost model unavailable ({source}); run `calibrate-spread` first"], 0, null);
        var p = s.Params;
        var notional = GapperSpreadMultiplier.TradeNotional(p) ?? throw new InvalidDataException("params.json has no costModel.impact.tradeNotionalUsd.");
        double impact = GapperSpreadMultiplier.ImpactConstant(p), floor = p.CostModel.RoundTripFloor;

        var t = Load(decision);
        var folds = Folds(t);
        if (folds.Count == 0) return (["no folds: the panel has too few train days"], 0, null);
        int gap = t.Feature("GapPct"), cs = t.Feature("x." + PriorDaysCorwinSchultzFeature.FeatureName),
            ssrCol = t.Feature(ShortConstraintFeatures.SsrActive), regCol = t.Feature(RegShoFeatures.ListedD1);
        float At(int r, int f) => f < 0 ? float.NaN : t.Value(r, f);
        // Prefilter on the panel's close-based gap at t (a little slack); the signal itself is recomputed from bars.
        var seen = new HashSet<(string, DateOnly)>();
        var rows = Enumerable.Range(0, t.Rows).Where(r => t.Weights[r] > 0 && At(r, gap) is var g && !float.IsNaN(g) && g >= FadeShortStillUpPct - 1 && seen.Add((t.Symbols[r], t.Days[r]))).ToList();
        var keys = rows.Select(r => (t.Symbols[r], t.Days[r])).ToHashSet();
        var prevClose = (await s.StudyDaysAsync("train", null, null, ct)).Where(u => keys.Contains((u.Symbol, u.DateEt)))
            .GroupBy(u => (u.Symbol, u.DateEt)).ToDictionary(g => g.Key, g => g.First().AdjPrevClose);

        var store = await s.StoreAsync(ct);
        var panel = new GapperStudyPanel(s);
        var exits = FadeShortExits();
        var candidates = new List<ShortCandidate>();
        // outcomes[exit][ssr][candidate index]
        var outcomes = exits.Select(_ => FadeShortSsr.Select(_ => new Dictionary<int, ShortOutcome>()).ToArray()).ToArray();
        int noBars = 0, noSignal = 0, noPrevClose = 0, notUp = 0, ssrUnknown = 0;
        foreach (var r in rows)
        {
            ct.ThrowIfCancellationRequested();
            if (!prevClose.TryGetValue((t.Symbols[r], t.Days[r]), out var pc) || pc <= 0) { noPrevClose++; continue; }
            var bars = store.ReadDay(t.Symbols[r], t.Days[r]);
            if (bars.Count == 0) { noBars++; continue; }
            var day = t.Days[r];
            var decisionUtc = MinuteBarMath.ToUtc(day, dt.TimeEt);
            var sig = GapperShortSimulator.Signal(bars, day, pc, decisionUtc);
            if (sig is null) { noSignal++; continue; }
            if (sig.GapPct < FadeShortStillUpPct) { notUp++; continue; }
            var ssrV = At(r, ssrCol);
            if (float.IsNaN(ssrV)) ssrUnknown++;
            var sigma = GapperCostModel.Sigma(await panel.PriorClosesAsync(t.Symbols[r], day, p.Features.Adv20Sessions, ct), p.Features.Adv20Sessions);
            var c = new ShortCandidate(r, folds.FindIndex(f => f.InEval(day)), sig, ssrV > 0.5f, !float.IsNaN(ssrV), At(r, regCol) > 0.5f,
                At(r, cs) is var v && !float.IsNaN(v) ? v : null, sigma);
            int ci = candidates.Count;
            candidates.Add(c);
            for (int si = 0; si < FadeShortSsr.Length; si++)
            {
                var entry = GapperShortSimulator.Entry(bars, decisionUtc, c.Ssr, FadeShortSsr[si]);
                if (entry is null || entry.Fill <= 0) continue;
                for (int ei = 0; ei < exits.Count; ei++)
                {
                    var trade = GapperShortSimulator.Simulate(exits[ei], bars, day, entry.BarStartUtc, entry.Fill);
                    if (trade is null) continue;
                    outcomes[ei][si][ci] = new ShortOutcome(trade.GrossReturn, (double)entry.Fill, trade.ExitReason, trade.GapFill, trade.MaxAdverse);
                }
            }
            if (candidates.Count % 500 == 0)
                s.Logger.LogInformation("[{Now}] explore fade-short: {Done} candidates simulated", GapperStudyServices.Et(s.UtcNow), candidates.Count);
        }

        double Cost(ShortCandidate c, double price, double size) =>
            GapperCostModel.Estimate(new GapperCostInputs(c.Spread, multiplier.Value, c.Signal.CumVolume, c.Sigma, price, size, impact, floor)).RoundTrip;

        var signals = new List<ShortSignalSpec>();
        foreach (var pm in FadeShortPmGapMinPct)
            foreach (var ext in FadeShortExtensionMaxPct)
                signals.Add(new ShortSignalSpec(string.Create(CultureInfo.InvariantCulture, $"pmHigh>=+{pm:0}% ext<{ext:0}%"), pm, ext));
        var random = new ShortSignalSpec("random short (every name >= +20% at 09:45)", null, null);

        // Net per trade (row, net, entry) for one configuration; regSho = false drops names on a threshold list on D-1.
        // Stress (IP-T476 second pass, methods-short.md): a seeded locate failure drops a name whose draw is under the rate,
        // a locate fee is charged on notional per trade, and lots rounds the position down to whole 100-share lots (none under one lot).
        List<(int Ci, double Net, double Entry, ShortOutcome O)> Trades(ShortSignalSpec spec, int ei, int si, bool withRegSho, double apr, double? size = null, ShortStress? stress = null)
        {
            var list = new List<(int, double, double, ShortOutcome)>();
            foreach (var (ci, o) in outcomes[ei][si])
            {
                var c = candidates[ci];
                if (!spec.Pass(c.Signal) || (!withRegSho && c.RegSho)) continue;
                double tradeSize = size ?? notional;
                if (stress is { } st)
                {
                    if (st.LocateFailRate > 0 && GapperShortSimulator.LocateDraw(t.Symbols[c.Row], t.Days[c.Row]) < st.LocateFailRate) continue;
                    if (st.RoundLots)
                    {
                        long shares = GapperShortSimulator.RoundLotShares(tradeSize, o.Entry);
                        if (shares == 0) continue;
                        tradeSize = shares * o.Entry;
                    }
                }
                double net = o.Gross - Cost(c, o.Entry, tradeSize) - GapperShortSimulator.BorrowCost(apr) - (stress?.LocateCost ?? 0);
                list.Add((ci, net, o.Entry, o));
            }
            return list;
        }

        ShortStats Stats(ShortSignalSpec spec, int ei, int si, bool withRegSho)
        {
            var list = Trades(spec, ei, si, withRegSho, FadeShortHeadlineBorrowApr);
            (double Mean, double Lo, double Hi, int Days)? Clustered(IEnumerable<(int Ci, double V)> v) =>
                ExploreDerived.ClusteredMean(v.Select(x => (t.Days[candidates[x.Ci].Row], x.V, t.Weights[candidates[x.Ci].Row])));
            var pooled = Clustered(list.Select(x => (x.Ci, x.Net)));
            var perFold = folds.Select((_, k) => Clustered(list.Where(x => candidates[x.Ci].Fold == k).Select(x => (x.Ci, x.Net)))?.Mean).ToList();
            var margin = Clustered(list.Select(x => (x.Ci, x.Net / GapperShortSimulator.MarginFraction(x.Entry))));
            double w = list.Sum(x => t.Weights[candidates[x.Ci].Row]);
            double? win = w > 0 ? list.Where(x => x.Net > 0).Sum(x => t.Weights[candidates[x.Ci].Row]) / w : null;
            double? worst = WeightedQuantile(list.Select(x => (x.Net, t.Weights[candidates[x.Ci].Row])).ToList(), 0.01);
            double? lomoMean = null, lomoLo = null; string? lomoMonth = null;
            foreach (var month in list.Select(x => t.Days[candidates[x.Ci].Row].ToString("yyyy-MM", CultureInfo.InvariantCulture)).Distinct())
            {
                var m = Clustered(list.Where(x => t.Days[candidates[x.Ci].Row].ToString("yyyy-MM", CultureInfo.InvariantCulture) != month).Select(x => (x.Ci, x.Net)));
                if (m is null) continue;
                if (lomoLo is null || m.Value.Lo < lomoLo) { lomoLo = m.Value.Lo; lomoMonth = month; }
                if (lomoMean is null || m.Value.Mean < lomoMean) lomoMean = m.Value.Mean;
            }
            double? AtApr(double apr) => Clustered(Trades(spec, ei, si, withRegSho, apr).Select(x => (x.Ci, x.Net)))?.Mean;
            int n = list.Count;
            bool finalist = pooled is { } pm && pm.Mean >= 0.01 && pm.Lo > 0 && n >= 150 && pm.Days >= 40 && perFold.Count(f => f > 0) >= 3 && lomoLo > 0;
            return new ShortStats(n, pooled?.Days ?? 0, pooled?.Mean, pooled?.Lo, pooled?.Hi, perFold, win, worst, margin?.Mean, margin?.Lo, margin?.Hi, lomoMean, lomoLo, lomoMonth,
                n == 0 ? 0 : (double)list.Count(x => x.O.Reason == "stop") / n, n == 0 ? 0 : (double)list.Count(x => x.O.GapFill) / n,
                list.Count(x => x.O.Reason == "stop") is var stops and > 0 ? (double)list.Count(x => x.O.Reason == "stop" && x.O.GapFill) / stops : null,
                AtApr(0), AtApr(FadeShortBorrowApr.Max()), finalist);
        }

        // Every configuration: signal x exit x SSR handling x Reg SHO.
        var results = new List<(ShortSignalSpec Spec, int Exit, int Ssr, bool RegSho, ShortStats Stats)>();
        foreach (var spec in signals.Append(random))
            for (int ei = 0; ei < exits.Count; ei++)
                for (int si = 0; si < FadeShortSsr.Length; si++)
                    foreach (var withRegSho in new[] { true, false })
                    {
                        if (spec.Random && !withRegSho) continue;
                        results.Add((spec, ei, si, withRegSho, Stats(spec, ei, si, withRegSho)));
                    }

        return FadeShortReport(t, folds, decision, exits, candidates, outcomes, results, signals, random, multiplier.Value, source, notional,
            (rows.Count, noPrevClose, noBars, noSignal, notUp, ssrUnknown), Trades, Cost);
    }

    private (List<string>, int, string?) FadeShortReport(ExploreTable t, List<ExploreFold> folds, string decision, IReadOnlyList<GapperShortExit> exits,
        List<ShortCandidate> candidates, Dictionary<int, ShortOutcome>[][] outcomes, List<(ShortSignalSpec Spec, int Exit, int Ssr, bool RegSho, ShortStats Stats)> results,
        List<ShortSignalSpec> signals, ShortSignalSpec random, double multiplier, string source, double notional,
        (int Scanned, int NoPrevClose, int NoBars, int NoSignal, int NotUp, int SsrUnknown) counts,
        Func<ShortSignalSpec, int, int, bool, double, double?, ShortStress?, List<(int Ci, double Net, double Entry, ShortOutcome O)>> trades, Func<ShortCandidate, double, double, double> cost)
    {
        static string Pct(double? v) => v is { } x && double.IsFinite(x) ? x.ToString("+0.00%;-0.00%", CultureInfo.InvariantCulture) : "n/a";
        static string Share(double? v) => v is { } x && double.IsFinite(x) ? x.ToString("P1", CultureInfo.InvariantCulture) : "n/a";
        string Bounds(double? m, double? lo, double? hi) => m is null ? "n/a" : $"{Pct(m)} [{Pct(lo)}, {Pct(hi)}]";
        string SsrName(int si) => FadeShortSsr[si] switch { ShortSsrHandling.Uptick => "uptick entry", ShortSsrHandling.Skip => "skip", _ => "ignored (reference)" };
        int uptick = Array.IndexOf(FadeShortSsr, ShortSsrHandling.Uptick), skip = Array.IndexOf(FadeShortSsr, ShortSsrHandling.Skip), ignore = Array.IndexOf(FadeShortSsr, ShortSsrHandling.Ignore);

        var md = new StringBuilder(Header($"fade short at {decision} (costed, short side)", folds));
        md.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"Signal at {decision} ET from bars ending by then: premarket high (bars 04:00-09:29) >= +30/50/100% over the adjusted prior close, close at {decision} >= +{FadeShortStillUpPct:0}% over it, and extension from the 09:30 open (close at {decision} / 09:30 open - 1) < 10/5/0%. Entry: short at the open of the first bar at or after {decision}. Exits: cover at the open of the first bar at or after 15:55 or 11:00; stop none/+10%/+20% over entry, filled at the worse of the stop and the bar's open (a gap or halt through the stop fills at the open); optional VWAP reclaim (a one-minute close above the 04:00-anchored session VWAP after a close at or below it covers at the next bar's open). Signal and clock fills after a missing minute are halt fills at the first post-halt open."));
        md.AppendLine();
        md.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"Costs: the frozen round trip at ${notional:N0} (Corwin-Schultz over D-1..D-5 x the premarket multiplier {multiplier:0.###} from {source}, square-root impact on the 04:00-{decision} volume, 0.5% floor). The frozen model has no regular-hours spread: the premarket multiplier is applied unchanged to these 09:45+ fills, which likely overstates the spread after the open. Borrow: {string.Join("/", FadeShortBorrowApr.Select(a => a.ToString("P0", CultureInfo.InvariantCulture)))} APR, actual/360, one day per intraday short; trials and the main table carry {FadeShortHeadlineBorrowApr:P0}. Historical easy-to-borrow / locate status is not available, so every name is assumed borrowable: results are as is, with no locate failures and no hard-to-borrow fee spikes. Rule 201: SSR is in force on D when D-1's low is at least 10% under D-2's close (the `explore ssr` cache); the uptick variant enters at the first bar within {GapperShortSimulator.UptickWaitMinutes} minutes whose open is above the prior bar's close (optimistic: a real short must be priced above the bid), the skip variant drops the name. Reg SHO: names on a threshold list on D-1 (`x.regShoListedD1`) are included or dropped."));
        md.AppendLine();
        md.AppendLine("Margin: net / capital tied up, at Alpaca's short requirement (under $5 the greater of $2.50/share or 100% of value; from $5 the greater of $5/share or the Reg T 50% initial). Pooled = every train day (the fit year 2021-11..2022-10 included: nothing is fit); fold columns are the four eval windows. Worst 1% = the weighted 1st percentile trade. LOMO = leave-one-month-out: the lowest lower bound with any one month dropped.");
        md.AppendLine();
        var cands = candidates;
        md.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"Population: {counts.Scanned:N0} train panel rows >= +{FadeShortStillUpPct - 1:0}% at {decision} (panel prefilter); {counts.NoPrevClose:N0} without a prior close, {counts.NoBars:N0} without bars, {counts.NoSignal:N0} without a premarket or 09:30 bar, {counts.NotUp:N0} under +{FadeShortStillUpPct:0}% on the bars. {cands.Count:N0} names >= +{FadeShortStillUpPct:0}% at {decision} over {cands.Select(c => t.Days[c.Row]).Distinct().Count():N0} days; {cands.Count(c => c.Ssr):N0} under SSR on D ({counts.SsrUnknown:N0} with no SSR value, treated as not restricted); {cands.Count(c => c.RegSho):N0} on a Reg SHO threshold list on D-1."));
        md.AppendLine();
        md.AppendLine("Signal counts (names passing, of which under SSR / on Reg SHO D-1):");
        md.AppendLine();
        foreach (var spec in signals.Append(random))
        {
            var pass = cands.Where(c => spec.Pass(c.Signal)).ToList();
            md.AppendLine($"- {spec.Name}: {pass.Count:N0} ({pass.Count(c => c.Ssr):N0} / {pass.Count(c => c.RegSho):N0}) over {pass.Select(c => t.Days[c.Row]).Distinct().Count():N0} days");
        }
        md.AppendLine();

        string Row((ShortSignalSpec Spec, int Exit, int Ssr, bool RegSho, ShortStats Stats) x) =>
            $"| {x.Spec.Name} | {exits[x.Exit].Name} | {x.Stats.Trades:N0} | {x.Stats.Days:N0} | {Bounds(x.Stats.Mean, x.Stats.Lo, x.Stats.Hi)} | {string.Join(" / ", x.Stats.Folds.Select(Pct))} | {Share(x.Stats.Win)} | {Pct(x.Stats.Worst1)} | {Bounds(x.Stats.MarginMean, x.Stats.MarginLo, x.Stats.MarginHi)} | {Pct(x.Stats.BorrowZero)} / {Pct(x.Stats.BorrowHigh)} | {Pct(x.Stats.LomoMinLo)} ({x.Stats.LomoWorstMonth ?? "n/a"}) | {Share(x.Stats.StopShare)} ({Share(x.Stats.StopGapShare)} gapped) | {(x.Stats.Finalist ? "**yes**" : "no")} |";
        const string head = "| signal | exit | trades | days | mean net [95% day-clustered] | per fold | win | worst 1% | net on margin [95%] | borrow 0% / 200% | LOMO min lower | stopped (of stops, gapped through) | finalist bar |";
        const string rule = "|---|---|---|---|---|---|---|---|---|---|---|---|---|";

        md.AppendLine($"## Main grid: SSR uptick entry, Reg SHO names included, borrow {FadeShortHeadlineBorrowApr:P0} APR");
        md.AppendLine();
        md.AppendLine(head);
        md.AppendLine(rule);
        foreach (var x in results.Where(x => x.Ssr == uptick && x.RegSho)) md.AppendLine(Row(x));
        md.AppendLine();

        // The best three signal configurations (by pooled mean among those with >= 150 trades over >= 40 days) and the best random short.
        var headline = results.Where(x => !x.Spec.Random && x.Ssr == uptick && x.RegSho).ToList();
        var eligible = headline.Where(x => x.Stats.Trades >= 150 && x.Stats.Days >= 40 && x.Stats.Mean is not null).ToList();
        var top = (eligible.Count >= 3 ? eligible : headline.Where(x => x.Stats.Mean is not null).ToList()).OrderByDescending(x => x.Stats.Mean).Take(3).ToList();
        var bestRandom = results.Where(x => x.Spec.Random && x.Ssr == uptick && x.Stats.Mean is not null).OrderByDescending(x => x.Stats.Mean).FirstOrDefault();
        var picks = top.Select(x => (x.Spec, x.Exit)).ToList();
        if (bestRandom.Spec is not null) picks.Add((bestRandom.Spec, bestRandom.Exit));
        // The random short on each pick's own exit too, so the comparison is like for like.
        foreach (var (_, ei) in top.Select(x => (x.Spec, x.Exit)).ToList())
            if (!picks.Contains((random, ei))) picks.Add((random, ei));

        md.AppendLine("## Best three and the random short: SSR, Reg SHO and borrow sensitivity");
        md.AppendLine();
        md.AppendLine("| signal | exit | SSR | Reg SHO D-1 | trades | days | net at 0% borrow | net at 50% [95%] | net at 200% | net on margin at 50% |");
        md.AppendLine("|---|---|---|---|---|---|---|---|---|---|");
        foreach (var (spec, ei) in picks)
            for (int si = 0; si < FadeShortSsr.Length; si++)
                foreach (var withRegSho in spec.Random ? new[] { true } : new[] { true, false })
                {
                    var st = results.FirstOrDefault(x => x.Spec == spec && x.Exit == ei && x.Ssr == si && x.RegSho == withRegSho).Stats;
                    if (st is null) continue;
                    md.AppendLine($"| {spec.Name} | {exits[ei].Name} | {SsrName(si)} | {(withRegSho ? "included" : "dropped")} | {st.Trades:N0} | {st.Days:N0} | {Pct(st.BorrowZero)} | {Bounds(st.Mean, st.Lo, st.Hi)} | {Pct(st.BorrowHigh)} | {Pct(st.MarginMean)} |");
                }
        md.AppendLine();

        // Borrow stresses (methods-short.md): a +50% micro-cap is likely hard-to-borrow, whose locates are per short, in 100-share lots, non-refundable.
        md.AppendLine($"## Borrow stresses on the same picks (SSR uptick, Reg SHO included, borrow {FadeShortHeadlineBorrowApr:P0} APR)");
        md.AppendLine();
        md.AppendLine("Locate failure: a seeded per symbol-day draw drops 30% / 60% of signals (the 60% set contains the 30% set; the same names in every configuration). Locate fee: 1.0% / 2.5% of notional charged per trade on top of everything else. 100-share lots: the $1,000 position rounded down to whole lots (a name over $10 cannot be traded; the impact term uses the rounded size). One ledger trial per pick x stress.");
        md.AppendLine();
        md.AppendLine("| signal | exit | stress | trades | days | mean net [95% day-clustered] | per fold | win | worst 1% |");
        md.AppendLine("|---|---|---|---|---|---|---|---|---|");
        var stressTrials = new List<ExploreTrial>();
        var stressJson = new List<object>();
        foreach (var (spec, ei) in picks)
            foreach (var stress in FadeShortStresses)
            {
                var list = trades(spec, ei, uptick, true, FadeShortHeadlineBorrowApr, null, stress);
                var m = ExploreDerived.ClusteredMean(list.Select(x => (t.Days[cands[x.Ci].Row], x.Net, t.Weights[cands[x.Ci].Row])));
                var perFold = folds.Select((_, k) => ExploreDerived.ClusteredMean(list.Where(x => cands[x.Ci].Fold == k).Select(x => (t.Days[cands[x.Ci].Row], x.Net, t.Weights[cands[x.Ci].Row])))?.Mean).ToList();
                double w = list.Sum(x => t.Weights[cands[x.Ci].Row]);
                double? win = w > 0 ? list.Where(x => x.Net > 0).Sum(x => t.Weights[cands[x.Ci].Row]) / w : null;
                var worst = WeightedQuantile(list.Select(x => (x.Net, t.Weights[cands[x.Ci].Row])).ToList(), 0.01);
                md.AppendLine($"| {spec.Name} | {exits[ei].Name} | {stress.Name} | {list.Count:N0} | {m?.Days ?? 0:N0} | {(m is { } mm ? Bounds(mm.Mean, mm.Lo, mm.Hi) : "n/a")} | {string.Join(" / ", perFold.Select(Pct))} | {Share(win)} | {Pct(worst)} |");
                stressJson.Add(new { signal = spec.Name, exit = exits[ei].Name, stress = stress.Name, trades = list.Count, days = m?.Days, mean = m?.Mean, lo = m?.Lo, hi = m?.Hi, folds = perFold, win, worst1 = worst });
                stressTrials.Add(new ExploreTrial
                {
                    Track = "fade-short-stress",
                    Config = string.Create(CultureInfo.InvariantCulture, $"t={decision} short {spec.Name} entry=next open exit={exits[ei].Name} ssr=Uptick regShoD1=included borrow={FadeShortHeadlineBorrowApr:P0} stress={stress.Name}"),
                    Metric = "meanNetReturn (short)", FoldValues = perFold, Pooled = m?.Mean, Rows = list.Count,
                    Note = string.Create(CultureInfo.InvariantCulture, $"lo={Pct(m?.Lo)} hi={Pct(m?.Hi)} days={m?.Days ?? 0} win={Share(win)} worst1={Pct(worst)}"),
                });
            }
        md.AppendLine();

        // Maximum adverse excursion: how far each short ran against the entry before it was covered.
        md.AppendLine("## Maximum adverse excursion (SSR uptick, Reg SHO included)");
        md.AppendLine();
        md.AppendLine("MAE = the highest price traded from the entry bar to the cover (the cover fill included) over the entry, minus 1; weighted percentiles over trades. Stops gapped = the share of stop exits that filled at a bar open above the stop (a gap or a halt through it). Descriptive, no trial logged.");
        md.AppendLine();
        md.AppendLine("| signal | exit | trades | MAE p50 | p75 | p90 | p95 | p99 | max | MAE > 10% | > 20% | > 50% | stopped | stops gapped through |");
        md.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        var maeJson = new List<object>();
        foreach (var (spec, ei) in picks)
        {
            var list = trades(spec, ei, uptick, true, FadeShortHeadlineBorrowApr, null, null);
            var mae = list.Select(x => (x.O.MaxAdverse, t.Weights[cands[x.Ci].Row])).ToList();
            double w = mae.Sum(x => x.Item2);
            double? Over(double level) => w > 0 ? mae.Where(x => x.MaxAdverse > level).Sum(x => x.Item2) / w : null;
            var q = new[] { 0.5, 0.75, 0.9, 0.95, 0.99 }.Select(p => WeightedQuantile(mae, p)).ToList();
            int stops = list.Count(x => x.O.Reason == "stop"), gapped = list.Count(x => x.O.Reason == "stop" && x.O.GapFill);
            double? max = mae.Count == 0 ? null : mae.Max(x => x.MaxAdverse);
            md.AppendLine($"| {spec.Name} | {exits[ei].Name} | {list.Count:N0} | {string.Join(" | ", q.Select(Pct))} | {Pct(max)} | {Share(Over(0.10))} | {Share(Over(0.20))} | {Share(Over(0.50))} | {stops:N0} | {(stops == 0 ? "n/a" : Share((double)gapped / stops))} |");
            maeJson.Add(new { signal = spec.Name, exit = exits[ei].Name, trades = list.Count, p50 = q[0], p75 = q[1], p90 = q[2], p95 = q[3], p99 = q[4], max, over10 = Over(0.10), over20 = Over(0.20), over50 = Over(0.50), stops, stopsGapped = gapped });
        }
        md.AppendLine();

        // Capacity: size = min($N, 5% of the 09:30-decision shares); the impact term scales with the capped size.
        md.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"## Capacity (SSR uptick, Reg SHO included, borrow {FadeShortHeadlineBorrowApr:P0}): size = min($N, {ParticipationCap.DefaultParticipation:P0} of the shares printed 09:30-{decision}); an order capped under ${ParticipationCap.DefaultMinNotionalUsd:N0} is skipped; net at the capped size. Descriptive, no trial logged."));
        md.AppendLine();
        md.AppendLine("| signal | exit | size | signals | full size | skipped | mean net of traded [95%] | median filled notional |");
        md.AppendLine("|---|---|---|---|---|---|---|---|");
        var capacityJson = new List<object>();
        foreach (var (spec, ei) in picks)
            foreach (var n in CapacityNotionals)
            {
                var all = trades(spec, ei, uptick, true, FadeShortHeadlineBorrowApr, null, null);
                var sized = all.Select(x => (x.Ci, x.Entry, x.O, Size: ParticipationCap.Size(x.Entry, n, cands[x.Ci].Signal.RthVolume, null))).ToList();
                var traded = sized.Where(x => !x.Size.Skip).ToList();
                var m = ExploreDerived.ClusteredMean(traded.Select(x =>
                    (t.Days[cands[x.Ci].Row], x.O.Gross - cost(cands[x.Ci], x.Entry, x.Size.NotionalUsd) - GapperShortSimulator.BorrowCost(FadeShortHeadlineBorrowApr), t.Weights[cands[x.Ci].Row])));
                var size = n.ToString("$#,0", CultureInfo.InvariantCulture);
                double? med = MedianOf(traded.Select(x => x.Size.NotionalUsd).ToList());
                md.AppendLine($"| {spec.Name} | {exits[ei].Name} | {size} | {sized.Count:N0} | {Share(sized.Count == 0 ? null : (double)sized.Count(x => x.Size.FullSize) / sized.Count)} | {Share(sized.Count == 0 ? null : (double)sized.Count(x => x.Size.Skip) / sized.Count)} | {(m is { } mm ? Bounds(mm.Mean, mm.Lo, mm.Hi) : "n/a")} | {(med is { } md2 ? md2.ToString("$#,0", CultureInfo.InvariantCulture) : "n/a")} |");
                capacityJson.Add(new { signal = spec.Name, exit = exits[ei].Name, size = n, signals = sized.Count, fullSize = sized.Count(x => x.Size.FullSize), skipped = sized.Count(x => x.Size.Skip), mean = m?.Mean, lo = m?.Lo, hi = m?.Hi, medianNotional = med });
            }
        md.AppendLine();

        int finalists = results.Count(x => x.Stats.Finalist && x.Ssr != ignore);
        md.AppendLine("## Finalist bar");
        md.AppendLine();
        md.AppendLine(finalists == 0
            ? "No configuration clears the finalist bar (>= +1.0%/trade net at 50% APR borrow with a lower bound above 0 on >= 150 trades over >= 40 days, positive in >= 3 of 4 folds, and a LOMO lower bound above 0)."
            : $"{finalists} configuration(s) clear the bar (>= +1.0%/trade net at 50% APR borrow, lower bound > 0, >= 150 trades over >= 40 days, positive in >= 3 of 4 folds, LOMO lower bound > 0): " +
              string.Join("; ", results.Where(x => x.Stats.Finalist && x.Ssr != ignore).Select(x => $"{x.Spec.Name}, {exits[x.Exit].Name}, SSR {SsrName(x.Ssr)}, Reg SHO {(x.RegSho ? "included" : "dropped")}")) +
              ". Still subject to deflation by the ledger's trial count and to the missing borrow data.");
        md.AppendLine();
        md.AppendLine("Exploration only (train). The SSR-ignored rows are a reference (a short under Rule 201 cannot hit the bid) and log no trial. The short-squeeze tail is in the worst-1% column; the frozen cost model was built for premarket longs and is applied unchanged.");

        var trials = new List<ExploreTrial>();
        var json = new List<object>();
        foreach (var x in results)
        {
            var st = x.Stats;
            json.Add(new { signal = x.Spec.Name, exit = exits[x.Exit].Name, ssr = FadeShortSsr[x.Ssr].ToString(), regSho = x.RegSho ? "included" : "dropped", st.Trades, st.Days, st.Mean, st.Lo, st.Hi,
                folds = st.Folds, st.Win, worst1 = st.Worst1, st.MarginMean, st.MarginLo, st.MarginHi, borrow0 = st.BorrowZero, borrow200 = st.BorrowHigh, st.LomoMinMean, st.LomoMinLo, st.LomoWorstMonth,
                st.StopShare, st.GapShare, st.StopGapShare, st.Finalist });
            if (x.Ssr == ignore) continue;
            trials.Add(new ExploreTrial
            {
                Track = "fade-short",
                Config = string.Create(CultureInfo.InvariantCulture, $"t={decision} short {x.Spec.Name} entry=next open exit={exits[x.Exit].Name} ssr={FadeShortSsr[x.Ssr]} regShoD1={(x.RegSho ? "included" : "dropped")} borrow={FadeShortHeadlineBorrowApr:P0}"),
                Metric = "meanNetReturn (short)", FoldValues = st.Folds, Pooled = st.Mean, Rows = st.Trades,
                Note = string.Create(CultureInfo.InvariantCulture, $"lo={Pct(st.Lo)} hi={Pct(st.Hi)} days={st.Days} win={Share(st.Win)} worst1={Pct(st.Worst1)} margin={Pct(st.MarginMean)} borrow0={Pct(st.BorrowZero)} borrow200={Pct(st.BorrowHigh)} lomoMinLo={Pct(st.LomoMinLo)} finalist={st.Finalist}"),
            });
        }
        trials.AddRange(stressTrials);
        return Write("fade-short", md, trials, new { results = json, stress = stressJson, mae = maeJson, capacity = capacityJson });
    }

    /// <summary>The weighted q-quantile (lowest value whose cumulative weight reaches q of the total); null when empty.</summary>
    public static double? WeightedQuantile(List<(double Value, double Weight)> values, double q)
    {
        var v = values.Where(x => double.IsFinite(x.Value) && x.Weight > 0).OrderBy(x => x.Value).ToList();
        double total = v.Sum(x => x.Weight);
        if (total <= 0) return null;
        double acc = 0;
        foreach (var (value, weight) in v)
        {
            acc += weight;
            if (acc >= q * total) return value;
        }
        return v[^1].Value;
    }
}
