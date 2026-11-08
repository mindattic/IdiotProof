using System.Globalization;
using System.Text;
using IdiotProof.Evidence.Gappers;
using IdiotProof.Models;

namespace IdiotProof.Core.Research.Gappers.Explore;

public sealed partial class GapperExploreStage
{
    /// <summary>The detection grid for track 4: time-of-day volume ratio and 3-minute velocity floors at t.</summary>
    public static readonly double[] EntryVolRatioMins = [3, 5, 10];
    public static readonly double[] EntryV3Mins = [0, 0.5];

    /// <summary>The one exit every entry is judged with, so entries are compared like for like.</summary>
    public static readonly GapperRuleConfig EntryExit = new(0, 0, VelocityDeathWindow: 3, StopLossPct: 10, SellByEt: new TimeSpan(9, 45, 0), VwapLossExit: true);

    private sealed record EntryVariant(string Name, GapperEntryKind Kind, bool NewsOnly);

    private static readonly EntryVariant[] EntryVariants =
    [
        new("buy now", GapperEntryKind.Now, false),
        new("first VWAP pullback", GapperEntryKind.VwapPullback, false),
        new("premarket-high breakout", GapperEntryKind.PmHighBreakout, false),
        new("buy now, news <= 60 min only", GapperEntryKind.Now, true),
    ];

    /// <summary>
    /// Track 4: for mornings still below +20% that pass a detection at t, how each entry does, net of the
    /// frozen cost model, with the same exit (velocity death on 3 bars, 10% stop, VWAP loss, sell by
    /// 09:45). Weighted by the amendment-01 population weight; one ledger trial per detection x entry,
    /// fold values are the eval-window weighted means.
    /// </summary>
    private async Task<(List<string>, int, string?)> EntriesAsync(IReadOnlyList<string> times, CancellationToken ct)
    {
        var decision = times.Count == 1 ? times[0] : "08:30";
        var dt = DecisionTime.Parse(decision);
        if (dt.DayOffset != 0) throw new InvalidOperationException("Entries are simulated on the morning itself; pick a same-day decision time.");
        var (multiplier, source) = GapperSpreadMultiplier.Resolve(s.Paths, s.Params);
        if (multiplier is null) return ([$"cost model unavailable ({source}); run `calibrate-spread` first"], 0, null);
        var p = s.Params;
        var notional = GapperSpreadMultiplier.TradeNotional(p) ?? throw new InvalidDataException("params.json has no costModel.impact.tradeNotionalUsd.");
        var exits = new GapperRuleExitParameters(p.Labels.VelocityDeath.ArmThresholdPctPerMin, p.Labels.VelocityDeath.ConsecutiveNegativeBars, p.Labels.Vwap.MinConsecutiveAboveBeforeLoss);

        var t = Load(decision);
        var folds = Folds(t);
        int gap = t.Feature("GapPct"), vr = t.Feature("VolRatioTod"), v3 = t.Feature("Velocity3m"), age = t.Feature("CatalystAgeMinutes"),
            cs = t.Feature("x." + PriorDaysCorwinSchultzFeature.FeatureName), logVol = t.Feature("LogPmCumVol");
        float At(int r, int f) => f < 0 ? float.NaN : t.Value(r, f);
        var loosest = Enumerable.Range(0, t.Rows).Where(r => t.Weights[r] > 0 && At(r, gap) is var g && !float.IsNaN(g) && g < FormedGapPct
            && At(r, vr) >= EntryVolRatioMins.Min() && At(r, v3) >= EntryV3Mins.Min()).ToList();

        var panel = new GapperStudyPanel(s);
        var store = await s.StoreAsync(ct);
        // results[detection][variant] = (row, net)
        var results = new Dictionary<(int, int), List<(int Row, double Net)>>();
        int simulated = 0;
        foreach (var r in loosest)
        {
            ct.ThrowIfCancellationRequested();
            var bars = store.ReadDay(t.Symbols[r], t.Days[r]);
            if (bars.Count == 0) continue;
            var decisionUtc = MinuteBarMath.ToUtc(t.Days[r], dt.TimeEt);
            var sigma = GapperCostModel.Sigma(await panel.PriorClosesAsync(t.Symbols[r], t.Days[r], p.Features.Adv20Sessions, ct), p.Features.Adv20Sessions);
            double pmCumVol = At(r, logVol) is var lv && !float.IsNaN(lv) ? Math.Exp(lv) - 1 : 0;
            double? spread = At(r, cs) is var c && !float.IsNaN(c) ? c : null;
            bool news = At(r, age) is var a && !float.IsNaN(a) && a <= 60;
            simulated++;
            for (int e = 0; e < EntryVariants.Length; e++)
            {
                var variant = EntryVariants[e];
                if (variant.NewsOnly && !news) continue;
                var net = SimulateEntry(variant.Kind, bars, t.Days[r], decisionUtc, spread, multiplier.Value, pmCumVol, sigma, notional, exits,
                    GapperSpreadMultiplier.ImpactConstant(p), p.CostModel.RoundTripFloor);
                if (net is null) continue;
                for (int i = 0; i < EntryVolRatioMins.Length; i++)
                    for (int j = 0; j < EntryV3Mins.Length; j++)
                    {
                        if (!(At(r, vr) >= EntryVolRatioMins[i] && At(r, v3) >= EntryV3Mins[j])) continue;
                        int key = i * EntryV3Mins.Length + j;
                        if (!results.TryGetValue((key, e), out var list)) results[(key, e)] = list = [];
                        list.Add((r, net.Value));
                    }
            }
        }

        var md = new StringBuilder(Header($"entries after a detection at {decision} (net of the frozen cost model)", folds));
        md.AppendLine(string.Create(CultureInfo.InvariantCulture, $"Mornings below +20% at t passing the detection; exit for every entry: {EntryExit.Label}. Spread multiplier {multiplier:0.###} ({source}). {simulated:N0} symbol-days simulated."));
        md.AppendLine();
        md.AppendLine("| detection | entry | trades | days | mean net [95% day-clustered] | per fold |");
        md.AppendLine("|---|---|---|---|---|---|");
        var trials = new List<ExploreTrial>();
        var json = new List<object>();
        for (int i = 0; i < EntryVolRatioMins.Length; i++)
            for (int j = 0; j < EntryV3Mins.Length; j++)
                for (int e = 0; e < EntryVariants.Length; e++)
                {
                    var detection = string.Create(CultureInfo.InvariantCulture, $"volRatioTod>={EntryVolRatioMins[i]} v3>={EntryV3Mins[j]}");
                    var list = results.GetValueOrDefault((i * EntryV3Mins.Length + j, e)) ?? [];
                    var all = ExploreDerived.ClusteredMean(list.Select(x => (t.Days[x.Row], x.Net, t.Weights[x.Row])));
                    var perFold = folds.Select(f => ExploreDerived.ClusteredMean(list.Where(x => f.InEval(t.Days[x.Row])).Select(x => (t.Days[x.Row], x.Net, t.Weights[x.Row])))?.Mean).ToList();
                    string Pct(double? v) => v is { } x ? x.ToString("+0.00%;-0.00%", CultureInfo.InvariantCulture) : "n/a";
                    md.AppendLine($"| {detection} | {EntryVariants[e].Name} | {list.Count:N0} | {all?.Days ?? 0} | {(all is { } m ? $"{Pct(m.Mean)} [{Pct(m.Lo)}, {Pct(m.Hi)}]" : "n/a")} | {string.Join(" / ", perFold.Select(Pct))} |");
                    trials.Add(new ExploreTrial { Track = "entries", Config = $"t={decision} {detection} entry={EntryVariants[e].Name} exit={EntryExit.Label}", Metric = "meanNetReturn", FoldValues = perFold, Pooled = all?.Mean, Rows = list.Count });
                    json.Add(new { detection, entry = EntryVariants[e].Name, trades = list.Count, mean = all?.Mean, lo = all?.Lo, hi = all?.Hi, days = all?.Days, folds = perFold });
                }
        md.AppendLine();
        md.AppendLine("A waiting entry (pullback, breakout) that never triggers within 30 minutes is no trade. Finalists need >= +1.0%/trade net with a lower bound above 0 on >= 150 trades over >= 40 days (explore-plan.md).");
        return Write("entries", md, trials, json);
    }

    /// <summary>One entry plus the shared exit; the net return from the entry fill, or null when the entry never triggers.</summary>
    public static double? SimulateEntry(GapperEntryKind kind, IReadOnlyList<Candle> bars, DateOnly dateEt, DateTime decisionUtc, double? spread, double multiplier,
        double pmCumVol, double? sigma, double notional, GapperRuleExitParameters exits, double impactConstant, double floor)
    {
        var entry = GapperEntrySimulator.Find(kind, bars, dateEt, decisionUtc);
        if (entry is null || entry.Fill <= 0) return null;
        var cost = GapperCostModel.Estimate(new GapperCostInputs(spread, multiplier, pmCumVol, sigma, (double)entry.Fill, notional, impactConstant, floor)).RoundTrip;
        var trade = GapperRuleSimulator.Simulate(EntryExit, bars, dateEt, entry.BarStartUtc, cost, exits);
        if (trade is null) return null;
        return (double)(trade.Exit / entry.Fill - 1m) - cost;
    }
}
