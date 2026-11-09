using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using IdiotProof.Evidence.Gappers;
using IdiotProof.Models;

namespace IdiotProof.Core.Research.Gappers.Explore;

public sealed partial class GapperExploreStage
{
    /// <summary>The one detection whose entries every exit route and overlay is judged on (track 5, IP-T471).</summary>
    public const double ExitRouteVolRatioMin = 5;
    public const double ExitRouteV3Min = 0;
    /// <summary>An entry filled at or after this time is dropped: the earliest clock route sells at 09:15.</summary>
    public static readonly TimeSpan ExitRouteLatestEntryEt = new(9, 10, 0);
    /// <summary>The close-based guard every configuration carries (strategy-refined.md 2.5: s in {8%, 12%}).</summary>
    public const double ExitRouteStopPct = 10;
    public static readonly double[] CapacityNotionals = [1_000, 5_000, 25_000];
    public const double CollapseHazardMultiplier = 3;
    public const double GivebackKAfterCollapse = 3;
    public const int PostHighDriftMinutes = 30;
    /// <summary>Only highs printed by 10:00 ET feed mu1: the post-high drift the exit cares about is the morning's.</summary>
    public const int PostHighFitLatestHodMinEt = 10 * 60;

    public static IReadOnlyList<GapperExitRoute> ExitRoutes() =>
    [
        GapperExitRoute.Continuous(new TimeSpan(9, 15, 0)),
        GapperExitRoute.Continuous(new TimeSpan(9, 20, 0)),
        GapperExitRoute.Opg(0.03),
        GapperExitRoute.Opg(0.05),
        GapperExitRoute.Regular(new TimeSpan(9, 31, 0)),
        GapperExitRoute.Regular(new TimeSpan(9, 44, 0)),
        GapperExitRoute.Regular(new TimeSpan(9, 50, 0)),
        GapperExitRoute.Regular(new TimeSpan(9, 50, 0), luldAware: true),
    ];

    private sealed record OverlaySpec(string Name, double? DriftP, double? GivebackK, bool Collapse);

    private static readonly OverlaySpec[] OverlaySpecs =
    [
        new("clock only", null, null, false),
        new("drift-change p*=0.5", 0.5, null, false),
        new("drift-change p*=0.7", 0.7, null, false),
        new("drift-change p*=0.9", 0.9, null, false),
        new("drift-change p*=0.7, collapse hazard x3", 0.7, null, true),
        new("giveback k=3", null, 3, false),
        new("giveback k=5", null, 5, false),
        new("giveback k=5, collapse k->3", null, 5, true),
    ];

    private sealed record RouteOutcome(int Row, int Fold, double Net, bool Halt, bool? OpgFilled, bool Early);

    private sealed record CapacityOutcome(int Row, int Fold, bool Full, bool Skip, string Binding, double Net);

    /// <summary>
    /// `explore exit-routes` (IP-T471, strategy-refined.md 2.5, methods-exit.md 1.3, methods-execution.md 2.5/4):
    /// on train mornings below +20% at t that pass volRatioTod &gt;= 5 and v3 &gt;= 0, buy the next bar's open and
    /// sell by each clock route (continuous 09:15/09:20, opg limit by 09:27 with the 09:30:05 fallback, 09:31,
    /// 09:44, 09:50 with and without the LULD 09:44 exit), alone and with each early-exit overlay (drift change
    /// with the time-of-day high hazard, volatility-scaled giveback, each with the trade-count collapse state).
    /// The hazard and mu1 are fit per fold on that fold's fit-window gapper mornings, so only eval-window entries are
    /// scored (pooled = the four eval windows). Net of the frozen cost model at the params notional; a capacity curve
    /// at $1k/$5k/$25k under the 5% participation cap. One ledger trial per route x overlay and per route x size.
    /// </summary>
    private async Task<(List<string>, int, string?)> ExitRoutesAsync(IReadOnlyList<string> times, ExploreRequest req, CancellationToken ct)
    {
        var decision = times.Count == 1 ? times[0] : "08:30";
        var dt = DecisionTime.Parse(decision);
        if (dt.DayOffset != 0) throw new InvalidOperationException("Exit routes are simulated on the morning itself; pick a same-day decision time.");
        var (multiplier, source) = GapperSpreadMultiplier.Resolve(s.Paths, s.Params);
        if (multiplier is null) return ([$"cost model unavailable ({source}); run `calibrate-spread` first"], 0, null);
        var p = s.Params;
        var notional = GapperSpreadMultiplier.TradeNotional(p) ?? throw new InvalidDataException("params.json has no costModel.impact.tradeNotionalUsd.");
        double impact = GapperSpreadMultiplier.ImpactConstant(p), floor = p.CostModel.RoundTripFloor;
        var profilePath = s.Paths.ProfileFile(false);
        if (!File.Exists(profilePath)) throw new InvalidOperationException($"{Path.GetFileName(profilePath)} does not exist; run `snapshot train` first so the time-of-day profile is fit.");
        var profile = TimeOfDayProfile.Load(profilePath);
        var deciles = (await s.StudyDaysAsync("train", null, null, ct)).GroupBy(u => (u.Symbol, u.DateEt)).ToDictionary(g => g.Key, g => g.First().AdvDecile);

        var t = Load(decision);
        var folds = Folds(t);
        if (folds.Count == 0) return (["no folds: the panel has too few train days"], 0, null);
        var store = await s.StoreAsync(ct);
        var panel = new GapperStudyPanel(s);

        // Per-fold priors from the fit window's gapper mornings: the high-of-day hazard and the post-high drift mu1.
        int isG = t.Label("IsGapper"), hodLab = t.Label("HodMinEt");
        if (isG < 0 || hodLab < 0) throw new InvalidOperationException("The panel has no IsGapper/HodMinEt labels; rebuild it with `explore build --force`.");
        var gapperRows = Enumerable.Range(0, t.Rows)
            .Where(r => t.IsCandidate[r] && t.LabelValue(r, isG) > 0.5f && !float.IsNaN(t.LabelValue(r, hodLab)))
            .GroupBy(r => (t.Symbols[r], t.Days[r])).Select(g => g.First()).ToList();
        var postHigh = new Dictionary<int, double>();
        foreach (var r in gapperRows.Where(r => folds.Any(f => f.InFit(t.Days[r])) && t.LabelValue(r, hodLab) <= PostHighFitLatestHodMinEt))
        {
            ct.ThrowIfCancellationRequested();
            var bars = store.ReadDay(t.Symbols[r], t.Days[r]);
            var hodUtc = MinuteBarMath.ToUtc(t.Days[r], TimeSpan.FromMinutes(t.LabelValue(r, hodLab)));
            if (DriftChangeExitRule.PostHighDrift(bars, hodUtc, PostHighDriftMinutes) is { } mu) postHigh[r] = mu;
        }
        var hazards = folds.Select(f => HodHazard.Fit(gapperRows.Where(r => f.InFit(t.Days[r])).Select(r => (int)t.LabelValue(r, hodLab)))).ToList();
        var mu1 = folds.Select(f => MedianOf(postHigh.Where(kv => f.InFit(t.Days[kv.Key])).Select(kv => kv.Value).ToList())).ToList();
        // overlays[fold][spec]
        var overlays = folds.Select((_, k) => OverlaySpecs.Select(o => Overlays(o, hazards[k], mu1[k])).ToArray()).ToList();

        int gap = t.Feature("GapPct"), vr = t.Feature("VolRatioTod"), v3 = t.Feature("Velocity3m"),
            cs = t.Feature("x." + PriorDaysCorwinSchultzFeature.FeatureName), logVol = t.Feature("LogPmCumVol");
        float At(int r, int f) => f < 0 ? float.NaN : t.Value(r, f);
        var entryRows = Enumerable.Range(0, t.Rows).Where(r => t.Weights[r] > 0 && At(r, gap) is var g && !float.IsNaN(g) && g < FormedGapPct
            && At(r, vr) >= ExitRouteVolRatioMin && At(r, v3) >= ExitRouteV3Min && folds.Any(f => f.InEval(t.Days[r]))).ToList();

        var routes = ExitRoutes();
        var outcomes = new ConcurrentDictionary<(int Route, int Overlay), ConcurrentBag<RouteOutcome>>();
        var capacity = new ConcurrentDictionary<(int Route, int Size), ConcurrentBag<CapacityOutcome>>();
        var snr = new ConcurrentDictionary<int, double>();
        int simulated = 0, noEntry = 0, lateEntry = 0;
        foreach (var chunk in entryRows.Chunk(256))
        {
            // Inputs that need the database are read in order; the simulation of the chunk then runs in parallel.
            var inputs = new List<(int Row, IReadOnlyList<Candle> Bars, double? Sigma)>();
            foreach (var r in chunk)
            {
                ct.ThrowIfCancellationRequested();
                var bars = store.ReadDay(t.Symbols[r], t.Days[r]);
                if (bars.Count == 0) { noEntry++; continue; }
                var sigma = GapperCostModel.Sigma(await panel.PriorClosesAsync(t.Symbols[r], t.Days[r], p.Features.Adv20Sessions, ct), p.Features.Adv20Sessions);
                inputs.Add((r, bars, sigma));
            }
            Parallel.ForEach(inputs, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, req.Parallelism), CancellationToken = ct }, input =>
            {
                var (r, bars, sigma) = input;
                var day = t.Days[r];
                var entry = GapperEntrySimulator.Find(GapperEntryKind.Now, bars, day, MinuteBarMath.ToUtc(day, dt.TimeEt));
                if (entry is null || entry.Fill <= 0) { Interlocked.Increment(ref noEntry); return; }
                var entryEt = MinuteBarMath.ToEastern(entry.BarStartUtc).TimeOfDay;
                if (entryEt >= ExitRouteLatestEntryEt) { Interlocked.Increment(ref lateEntry); return; }
                int fold = folds.FindIndex(f => f.InEval(day));
                double pmCumVol = At(r, logVol) is var lv && !float.IsNaN(lv) ? Math.Exp(lv) - 1 : 0;
                double? spread = At(r, cs) is var c && !float.IsNaN(c) ? c : null;
                double price = (double)entry.Fill;
                double Cost(double size) => GapperCostModel.Estimate(new GapperCostInputs(spread, multiplier.Value, pmCumVol, sigma, price, size, impact, floor)).RoundTrip;
                double baseCost = Cost(notional);
                if (DriftChangeExitRule.Arming(bars, entry.BarStartUtc) is ({ } m2, { } sd) && sd > 0) snr[r] = m2 / sd;

                // The trade-count collapse once per entry (to the latest route), shared by every overlay that uses it.
                var latest = routes.Max(x => MinuteBarMath.ToUtc(day, x.OverlayCutoffEt));
                var collapse = ExhaustionState.TradesCollapse(bars, entry.BarStartUtc, latest);

                // Participation inputs: shares in the 5 minutes before the entry, cumulative volume since 04:00.
                var sessionStart = MinuteBarMath.ToUtc(day, MinuteBarMath.SessionStartEt);
                double v5 = (double)MinuteBarMath.VolumeBetween(bars, entry.BarStartUtc.AddMinutes(-5), entry.BarStartUtc);
                double cum = (double)MinuteBarMath.VolumeBetween(bars, sessionStart, entry.BarStartUtc);
                int? decile = deciles.TryGetValue((t.Symbols[r], day), out var dcl) ? dcl : null;

                for (int ri = 0; ri < routes.Count; ri++)
                {
                    for (int oi = 0; oi < OverlaySpecs.Length; oi++)
                    {
                        var ov = overlays[fold][oi] with { ExternalCollapseUtc = collapse, TradesCollapseFromBars = false };
                        var trade = GapperExitRouteSimulator.Simulate(routes[ri], ov, bars, day, entry.BarStartUtc, entry.Fill);
                        if (trade is null) continue;
                        bool early = !(trade.ExitReason.StartsWith("sell-by", StringComparison.Ordinal) || trade.ExitReason.StartsWith("opg", StringComparison.Ordinal));
                        outcomes.GetOrAdd((ri, oi), _ => []).Add(new RouteOutcome(r, fold, trade.Net(baseCost), trade.HaltFill, trade.OpgFilled, early));
                        if (oi != 0) continue;
                        var (from, to) = routes[ri].ExitWindowEt;
                        double? vexit = decile is { } dd ? ParticipationCap.ForecastWindowVolume(profile, dd, cum, entryEt, from, to) : null;
                        for (int ni = 0; ni < CapacityNotionals.Length; ni++)
                        {
                            var size = ParticipationCap.Size(price, CapacityNotionals[ni], v5, vexit);
                            double net = size.Skip ? double.NaN : trade.Net(Cost(size.NotionalUsd));
                            capacity.GetOrAdd((ri, ni), _ => []).Add(new CapacityOutcome(r, fold, size.FullSize, size.Skip, size.Binding, net));
                        }
                    }
                }
                Interlocked.Increment(ref simulated);
            });
            s.Logger.LogInformation("[{Now}] explore exit-routes: {Done}/{Total} entries simulated", GapperStudyServices.Et(s.UtcNow), simulated, entryRows.Count);
        }

        return ExitRoutesReport(t, folds, decision, routes, outcomes, capacity, snr, hazards, mu1, postHigh.Count, multiplier.Value, source, notional, simulated, noEntry, lateEntry);
    }

    private static GapperExitOverlays Overlays(OverlaySpec o, HodHazard hazard, double? mu1)
    {
        DriftChangeExitRule? drift = o.DriftP is { } pStar && mu1 is { } m
            ? new DriftChangeExitRule(new DriftChangeParameters(pStar, m, CollapseHazardMultiplier: o.Collapse ? CollapseHazardMultiplier : 1), hazard)
            : null;
        GivebackExitRule? give = o.GivebackK is { } k
            ? new GivebackExitRule(new GivebackParameters(k, KAfterCollapse: o.Collapse ? Math.Min(k, GivebackKAfterCollapse) : null))
            : null;
        return new GapperExitOverlays(ExitRouteStopPct, drift, give, UseExhaustionState: o.Collapse);
    }

    private (List<string>, int, string?) ExitRoutesReport(ExploreTable t, List<ExploreFold> folds, string decision, IReadOnlyList<GapperExitRoute> routes,
        ConcurrentDictionary<(int Route, int Overlay), ConcurrentBag<RouteOutcome>> outcomes, ConcurrentDictionary<(int Route, int Size), ConcurrentBag<CapacityOutcome>> capacity,
        ConcurrentDictionary<int, double> snr, List<HodHazard> hazards, List<double?> mu1, int postHighDays, double multiplier, string source, double notional,
        int simulated, int noEntry, int lateEntry)
    {
        static string Pct(double? v) => v is { } x && double.IsFinite(x) ? x.ToString("+0.00%;-0.00%", CultureInfo.InvariantCulture) : "n/a";
        static string Share(int n, int d) => d == 0 ? "n/a" : ((double)n / d).ToString("P1", CultureInfo.InvariantCulture);
        string Bounds((double Mean, double Lo, double Hi, int Days)? m) => m is { } x ? $"{Pct(x.Mean)} [{Pct(x.Lo)}, {Pct(x.Hi)}]" : "n/a";
        (double Mean, double Lo, double Hi, int Days)? Clustered(IEnumerable<(int Row, double Net)> v) => ExploreDerived.ClusteredMean(v.Select(x => (t.Days[x.Row], x.Net, t.Weights[x.Row])));

        var detection = string.Create(CultureInfo.InvariantCulture, $"volRatioTod>={ExitRouteVolRatioMin} v3>={ExitRouteV3Min} gap<{FormedGapPct}%");
        var md = new StringBuilder(Header($"exit routes and overlays after a detection at {decision} (net of the frozen cost model)", folds));
        md.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"Entries: {detection} at t, buy the next bar's open, filled before {ExitRouteLatestEntryEt:hh\\:mm}; only rows in a fold's eval window (hazard and mu1 are fit on the fold's fit window). {simulated:N0} entries simulated, {noEntry:N0} with no entry bar, {lateEntry:N0} entered too late. Guard: close at or under entry -{ExitRouteStopPct}% sells at the next open. Cost: frozen model at ${notional:N0}, spread multiplier {multiplier:0.###} ({source})."));
        md.AppendLine();
        md.AppendLine("Priors per fold (fit-window Tier20+ mornings): " + string.Join("; ", folds.Select((f, k) => string.Create(CultureInfo.InvariantCulture,
            $"fold {f.Number}: {hazards[k].Events:N0} highs, mu1 {(mu1[k] is { } m ? (m * 100).ToString("+0.000;-0.000", CultureInfo.InvariantCulture) + "%/min" : "n/a")}"))) + $" (mu1 from {postHighDays:N0} mornings with the high by 10:00).");
        md.AppendLine();
        md.AppendLine("Fills: a signal or clock sell fills at the open of the first bar starting at or after it (a later bar is a halt fill: the first post-halt open). The opg limit (close at 09:27 less x) fills at the 09:30 bar's open only when that open is above it; otherwise the 09:30:05 fallback takes the worse of the 09:30 bar's open and close, or with no 09:30 bar the first later open. The opening print is the 09:30 minute bar's open until the auction prints are pulled, and Nasdaq's cross price validation is not modelled.");
        md.AppendLine();
        md.AppendLine("| route | overlay | trades | days | pooled mean net [95% day-clustered] | vs clock only, paired | " + string.Join(" | ", folds.Select(f => $"fold {f.Number}")) + " | early exits | halt fills | opg filled |");
        md.AppendLine("|---|---|---|---|---|---|" + string.Concat(folds.Select(_ => "---|")) + "---|---|---|");
        var trials = new List<ExploreTrial>();
        var json = new List<object>();
        for (int ri = 0; ri < routes.Count; ri++)
        {
            var clock = outcomes.GetValueOrDefault((ri, 0))?.ToDictionary(o => o.Row, o => o.Net) ?? [];
            for (int oi = 0; oi < OverlaySpecs.Length; oi++)
            {
                var list = outcomes.GetValueOrDefault((ri, oi))?.ToList() ?? [];
                var pooled = Clustered(list.Select(o => (o.Row, o.Net)));
                var perFold = folds.Select((_, k) => Clustered(list.Where(o => o.Fold == k).Select(o => (o.Row, o.Net)))).ToList();
                var paired = oi == 0 ? null : Clustered(list.Where(o => clock.ContainsKey(o.Row)).Select(o => (o.Row, o.Net - clock[o.Row])));
                int opgN = list.Count(o => o.OpgFilled is not null), opgY = list.Count(o => o.OpgFilled == true);
                md.AppendLine($"| {routes[ri].Name} | {OverlaySpecs[oi].Name} | {list.Count:N0} | {pooled?.Days ?? 0} | {Bounds(pooled)} | {(oi == 0 ? "" : Bounds(paired))} | {string.Join(" | ", perFold.Select(Bounds))} | {Share(list.Count(o => o.Early), list.Count)} | {Share(list.Count(o => o.Halt), list.Count)} | {(routes[ri].Kind == GapperExitRouteKind.OpgLimit ? Share(opgY, opgN) : "")} |");
                trials.Add(new ExploreTrial
                {
                    Track = "exit-routes", Config = $"t={decision} {detection} entry=next open route={routes[ri].Name} overlay={OverlaySpecs[oi].Name} stop={ExitRouteStopPct}%",
                    Metric = "meanNetReturn", FoldValues = perFold.Select(m => m?.Mean).ToList(), Pooled = pooled?.Mean, Rows = list.Count,
                    Note = string.Create(CultureInfo.InvariantCulture, $"lo={Pct(pooled?.Lo)} hi={Pct(pooled?.Hi)} days={pooled?.Days ?? 0} pairedVsClock={Pct(paired?.Mean)} [{Pct(paired?.Lo)}, {Pct(paired?.Hi)}]"),
                });
                json.Add(new { route = routes[ri].Name, overlay = OverlaySpecs[oi].Name, trades = list.Count, mean = pooled?.Mean, lo = pooled?.Lo, hi = pooled?.Hi, days = pooled?.Days,
                    pairedMean = paired?.Mean, pairedLo = paired?.Lo, pairedHi = paired?.Hi, folds = perFold.Select(m => m?.Mean).ToList(),
                    earlyShare = list.Count == 0 ? (double?)null : (double)list.Count(o => o.Early) / list.Count, haltShare = list.Count == 0 ? (double?)null : (double)list.Count(o => o.Halt) / list.Count,
                    opgFillShare = opgN == 0 ? (double?)null : (double)opgY / opgN });
            }
        }

        md.AppendLine();
        md.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"Capacity (clock only): size = min($N, {ParticipationCap.DefaultParticipation:P0} of the shares in the 5 minutes before entry, {ParticipationCap.DefaultParticipation:P0} of the forecast exit-window shares from the time-of-day profile); an order capped under ${ParticipationCap.DefaultMinNotionalUsd:N0} is skipped. Net at the capped size (the impact term scales with it)."));
        md.AppendLine();
        md.AppendLine("| route | size | trades | full size | skipped | binding: exit window | mean net of traded [95% day-clustered] |");
        md.AppendLine("|---|---|---|---|---|---|---|");
        for (int ri = 0; ri < routes.Count; ri++)
            for (int ni = 0; ni < CapacityNotionals.Length; ni++)
            {
                var list = capacity.GetValueOrDefault((ri, ni))?.ToList() ?? [];
                var traded = list.Where(o => !o.Skip).ToList();
                var pooled = Clustered(traded.Select(o => (o.Row, o.Net)));
                var perFold = folds.Select((_, k) => Clustered(traded.Where(o => o.Fold == k).Select(o => (o.Row, o.Net)))?.Mean).ToList();
                var size = CapacityNotionals[ni].ToString("$#,0", CultureInfo.InvariantCulture);
                md.AppendLine($"| {routes[ri].Name} | {size} | {list.Count:N0} | {Share(list.Count(o => o.Full), list.Count)} | {Share(list.Count(o => o.Skip), list.Count)} | {Share(list.Count(o => o.Binding == "exit window"), list.Count)} | {Bounds(pooled)} |");
                trials.Add(new ExploreTrial
                {
                    Track = "exit-routes-capacity", Config = $"t={decision} {detection} entry=next open route={routes[ri].Name} overlay=clock only size={size} participation={ParticipationCap.DefaultParticipation:0.##}",
                    Metric = "meanNetReturn (participation-capped)", FoldValues = perFold, Pooled = pooled?.Mean, Rows = traded.Count,
                    Note = string.Create(CultureInfo.InvariantCulture, $"fullSizeShare={(list.Count == 0 ? "n/a" : ((double)list.Count(o => o.Full) / list.Count).ToString("0.000", CultureInfo.InvariantCulture))} skipped={list.Count(o => o.Skip)} lo={Pct(pooled?.Lo)} hi={Pct(pooled?.Hi)}"),
                });
                json.Add(new { capacityRoute = routes[ri].Name, size = CapacityNotionals[ni], trades = list.Count, fullSize = list.Count(o => o.Full), skipped = list.Count(o => o.Skip), mean = pooled?.Mean, lo = pooled?.Lo, hi = pooled?.Hi, folds = perFold });
            }

        // The snr diagnostic (methods-exit.md 1.3): does the drift-change overlay beat the clock only where the run is clear of the noise?
        var medianSnr = MedianOf(snr.Values.ToList());
        if (medianSnr is { } msnr)
        {
            md.AppendLine();
            md.AppendLine(string.Create(CultureInfo.InvariantCulture, $"snr diagnostic (snr = run drift / sigma at entry, median {msnr:0.000}); descriptive, no trial logged:"));
            md.AppendLine();
            md.AppendLine("| route | overlay | snr half | trades | mean net | vs clock only, paired |");
            md.AppendLine("|---|---|---|---|---|---|");
            for (int ri = 0; ri < routes.Count; ri++)
            {
                var clock = outcomes.GetValueOrDefault((ri, 0))?.ToDictionary(o => o.Row, o => o.Net) ?? [];
                for (int oi = 1; oi < OverlaySpecs.Length; oi++)
                {
                    if (OverlaySpecs[oi].DriftP is null || OverlaySpecs[oi].Collapse) continue;
                    var list = outcomes.GetValueOrDefault((ri, oi))?.ToList() ?? [];
                    foreach (var (half, pick) in new (string, Func<double, bool>)[] { ("high", v => v > msnr), ("low", v => v <= msnr) })
                    {
                        var rows = list.Where(o => snr.TryGetValue(o.Row, out var v) && pick(v)).ToList();
                        var mean = Clustered(rows.Select(o => (o.Row, o.Net)));
                        var paired = Clustered(rows.Where(o => clock.ContainsKey(o.Row)).Select(o => (o.Row, o.Net - clock[o.Row])));
                        md.AppendLine($"| {routes[ri].Name} | {OverlaySpecs[oi].Name} | {half} | {rows.Count:N0} | {Bounds(mean)} | {Bounds(paired)} |");
                    }
                }
            }
        }

        md.AppendLine();
        md.AppendLine("Exploration only (train). Finalists need >= +1.0%/trade net with a lower bound above 0 on >= 150 trades over >= 40 days (explore-plan.md); an overlay finalist is judged paired against the clock on the same entries. The opg route is only an approximation until the opening auction prints exist.");
        return Write("exit-routes", md, trials, json);
    }
}
