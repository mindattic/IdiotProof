using System.Globalization;
using System.Text.Json;
using IdiotProof.Core.Catalysts;
using IdiotProof.Core.Evidence;
using IdiotProof.Evidence.Evaluation;
using IdiotProof.Evidence.Gappers;
using IdiotProof.Models;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Core.Research.Gappers;

/// <summary>One H18 configuration: how late the second-hand reader buys, which news, whether price must already be reacting, and when to be out.</summary>
public sealed record NewsRideConfig(int LatencyMinutes, string NewsFilter, string Confirmation, TimeSpan SellByEt)
{
    public string Label => string.Create(CultureInfo.InvariantCulture, $"latency{LatencyMinutes}m news={NewsFilter} confirm={Confirmation} sellBy{SellByEt:hh\\:mm}");
}

/// <summary>The first realtime news item of a symbol-day: when it published and the pre-classifier's direction.</summary>
public sealed record FirstNews(DateTime PublishedUtc, int PreDirection, string? PreClass);

/// <summary>
/// Amendment 02 (IP-T450): H18 buys the first premarket news item second-hand and exits on the frozen
/// ladder; H19 measures how much of a news-driven gapper's hour happens before the headline; H20
/// measures how fast the second-hand edge decays with latency. The H12 simulator, exit ladder and
/// frozen cost model are reused unchanged, on amendment 01's weighted population.
/// </summary>
public sealed partial class GapperTestStage
{
    private static readonly TimeSpan NewsWindowEndEt = new(9, 20, 0);
    private static readonly TimeSpan LastEntryEt = new(9, 25, 0);
    private static readonly TimeSpan PriorCloseEt = new(16, 0, 0);
    private static readonly TimeSpan PrePubWindow = TimeSpan.FromMinutes(30);
    private static readonly int[] DecayLatencies = [1, 2, 5, 10, 15];
    private static readonly TimeSpan DecayExitEt = new(9, 28, 0);

    private Task DispatchNewQuestionAsync(GapperHypothesis h, AmendedHypothesis a, HypothesisRun run, string split, CancellationToken ct) => a.Test switch
    {
        HypothesisCorrections.NewsRideGridTest => NewsRideGridAsync(h, a, run, split, ct),
        "prepub-drift" => PrePublicationDriftAsync(h, run, split, ct),
        "latency-decay" => LatencyDecayAsync(h, run, split, ct),
        _ => Unknown(h, run),
    };

    /// <summary>
    /// The trading day a news item belongs to: the first trading day D with publication in
    /// (previous trading day 16:00 ET, D 09:20 ET]; null when it publishes between 09:20 and 16:00 ET.
    /// </summary>
    public static DateOnly? NewsDay(DateTime publishedUtc)
    {
        var et = MinuteBarMath.ToEastern(publishedUtc);
        var d = DateOnly.FromDateTime(et);
        if (et.TimeOfDay > NewsWindowEndEt) d = d.AddDays(1);
        while (!MarketCalendar.IsTradingDay(d)) d = d.AddDays(1);
        return publishedUtc > MinuteBarMath.ToUtc(MarketCalendar.PreviousTradingDay(d), PriorCloseEt) ? d : null;
    }

    /// <summary>The first realtime Alpaca news item per (symbol, trading day) inside the split.</summary>
    private async Task<Dictionary<(string Symbol, DateOnly Day), FirstNews>> FirstNewsAsync(string split, CancellationToken ct)
    {
        var range = P.Split(split);
        var fromUtc = MinuteBarMath.ToUtc(MarketCalendar.PreviousTradingDay(range.From), PriorCloseEt);
        var toUtc = MinuteBarMath.ToUtc(range.To, NewsWindowEndEt);
        await using var db = await s.Db.CreateDbContextAsync(ct);
        var rows = await (from e in db.CatalystEvents.AsNoTracking()
                          join x in db.CatalystEventSymbols.AsNoTracking() on e.Id equals x.EventId
                          where e.Source == CatalystIntake.AlpacaNews && e.StampKind == CatalystStampKind.Realtime && e.PublishedUtc > fromUtc && e.PublishedUtc <= toUtc
                          select new { x.Symbol, e.PublishedUtc, e.PreDirection, e.PreClass }).ToListAsync(ct);
        var result = new Dictionary<(string, DateOnly), FirstNews>();
        foreach (var r in rows.OrderBy(r => r.PublishedUtc))
            if (NewsDay(r.PublishedUtc) is { } day && day >= range.From && day <= range.To)
                result.TryAdd((r.Symbol, day), new FirstNews(r.PublishedUtc, r.PreDirection, r.PreClass));
        return result;
    }

    private async Task<Dictionary<(string Symbol, DateOnly Day), decimal>> PriorClosesAsync(string split, IReadOnlyCollection<(string Symbol, DateOnly Day)> keys, CancellationToken ct)
    {
        var wanted = keys.ToHashSet();
        await using var db = await s.Db.CreateDbContextAsync(ct);
        var result = new Dictionary<(string, DateOnly), decimal>();
        foreach (var r in await db.GapperStudyUniverseDays.AsNoTracking().Where(r => r.Split == split && r.ExcludedReason == null).Select(r => new { r.Symbol, r.DateEt, r.AdjPrevClose }).ToListAsync(ct))
            if (wanted.Contains((r.Symbol, r.DateEt))) result[(r.Symbol, r.DateEt)] = r.AdjPrevClose;
        return result;
    }

    private static DateTime Trigger(FirstNews news, DateOnly day, int latencyMinutes)
    {
        var open = MinuteBarMath.ToUtc(day, MinuteBarMath.SessionStartEt);
        return (news.PublishedUtc > open ? news.PublishedUtc : open).AddMinutes(latencyMinutes);
    }

    /// <summary>The first traded bar starting at or after the trigger and before <paramref name="latestStartEt"/>.</summary>
    private static Candle? EntryBar(IReadOnlyList<Candle> bars, DateOnly day, DateTime triggerUtc, TimeSpan latestStartEt)
    {
        var latest = MinuteBarMath.ToUtc(day, latestStartEt);
        foreach (var b in bars)
            if (b.Volume > 0 && b.Open > 0 && b.StartUtc >= triggerUtc) return b.StartUtc < latest ? b : null;
        return null;
    }

    public static List<NewsRideConfig> NewsRideGrid(JsonElement grid)
    {
        static List<T> Read<T>(JsonElement g, string key, Func<JsonElement, T> read) =>
            g.TryGetProperty(key, out var v) ? v.EnumerateArray().Select(read).ToList() : throw new InvalidDataException($"amendment grid has no {key}.");
        var configs = new List<NewsRideConfig>();
        foreach (var latency in Read(grid, "latencyMinutes", e => e.GetInt32()))
            foreach (var filter in Read(grid, "newsFilter", e => e.GetString()!))
                foreach (var confirm in Read(grid, "confirmation", e => e.GetString()!))
                    foreach (var sellBy in Read(grid, "sellByEt", e => GapperStudyParams.ParseEt(e.GetString()!)))
                        configs.Add(new NewsRideConfig(latency, filter, confirm, sellBy));
        return configs;
    }

    // ── H18: ride the news second-hand ───────────────────────────────────

    private async Task NewsRideGridAsync(GapperHypothesis h, AmendedHypothesis a, HypothesisRun run, string split, CancellationToken ct)
    {
        var grid = a.Grid ?? throw new InvalidDataException($"{a.Id} registers no grid.");
        var configs = NewsRideGrid(grid);
        int deathWindow = grid.GetProperty("velocityDeathWindow").GetInt32();
        double stopPct = grid.GetProperty("stopLossPct").GetDouble();
        bool vwap = grid.GetProperty("vwapLossExit").GetBoolean();
        var (multiplier, source) = GapperSpreadMultiplier.Resolve(s.Paths, P);
        var rows = (await panel.LoadAsync(split, "08:00", ct)).Where(r => r.InPopulation).OrderBy(r => r.DateEt).ThenBy(r => r.Symbol, StringComparer.Ordinal).ToList();
        var news = await FirstNewsAsync(split, ct);
        var withNews = rows.Where(r => news.ContainsKey((r.Symbol, r.DateEt))).ToList();
        Describe(run, Keys(rows).ToList(), PopulationDescription(rows, "population days") + $"; {withNews.Count:N0} with a first realtime news item in (prior close, 09:20]",
            ["first realtime news item", "pre-classifier direction", "close before the trigger vs the adjusted prior close"], ["weighted net return per trade after the frozen cost model"]);
        var part = new HypothesisPart { Name = "meanNetPctPerTrade", EffectName = "meanNetPctPerTrade", MinEffect = h.MinEffectNumber("meanNetPctPerTrade"), Direction = EffectDirection.AtLeast };
        PopulationStatistics(part, rows);
        part.Statistic["newsDays"] = withNews.Count;
        part.Statistic["newsDaysBullish"] = withNews.Count(r => news[(r.Symbol, r.DateEt)].PreDirection == 1);
        if (multiplier is null)
        {
            part.Verdict = HypothesisVerdict.Void;
            part.Reasons.Add("cost model unavailable: " + source);
            run.Parts.Add(part);
            return;
        }

        var exits = new GapperRuleExitParameters(P.Labels.VelocityDeath.ArmThresholdPctPerMin, P.Labels.VelocityDeath.ConsecutiveNegativeBars, P.Labels.Vwap.MinConsecutiveAboveBeforeLoss);
        var notional = GapperSpreadMultiplier.TradeNotional(P) ?? throw new InvalidDataException("params.json has no costModel.impact.tradeNotionalUsd.");
        var priorClose = await PriorClosesAsync(split, Keys(withNews).ToList(), ct);
        var store = await panel.StoreAsync(ct);
        var dayKeys = rows.Select(r => r.DateEt).Distinct().OrderBy(d => d).ToList();
        var dayIndex = dayKeys.Select((d, i) => (d, i)).ToDictionary(x => x.d, x => x.i);
        var dayWeight = new double[dayKeys.Count];
        foreach (var r in rows) dayWeight[dayIndex[r.DateEt]] += r.Weight;
        var trades = configs.Select(_ => new List<WeightedTrade>()).ToList();
        var daySums = configs.Select(_ => new double[dayKeys.Count]).ToList();
        int entries = 0;
        double costSum = 0;
        foreach (var r in withNews)
        {
            var ev = news[(r.Symbol, r.DateEt)];
            var bars = store.ReadDay(r.Symbol, r.DateEt).OrderBy(b => b.StartUtc).ToList();
            if (bars.Count == 0 || !priorClose.TryGetValue((r.Symbol, r.DateEt), out var prev)) continue;
            bool sigmaLoaded = false;
            double? sigma = null;
            var costByEntry = new Dictionary<DateTime, double>();
            for (int k = 0; k < configs.Count; k++)
            {
                var c = configs[k];
                if (c.NewsFilter == "bullish" && ev.PreDirection != 1) continue;
                var trigger = Trigger(ev, r.DateEt, c.LatencyMinutes);
                var entry = EntryBar(bars, r.DateEt, trigger, c.SellByEt < LastEntryEt ? c.SellByEt : LastEntryEt);
                if (entry is null) continue;
                if (c.Confirmation == "up5" && !(MinuteBarMath.CloseAsOf(bars, trigger) is { } last && last >= prev * 1.05m)) continue;
                if (!costByEntry.TryGetValue(entry.StartUtc, out var cost))
                {
                    if (!sigmaLoaded)
                    {
                        sigma = GapperCostModel.Sigma(await panel.PriorClosesAsync(r.Symbol, r.DateEt, P.Features.Adv20Sessions, ct), P.Features.Adv20Sessions);
                        sigmaLoaded = true;
                    }
                    double pmCumVol = (double)bars.Where(b => b.StartUtc < entry.StartUtc).Sum(b => b.Volume);
                    cost = GapperCostModel.Estimate(new GapperCostInputs(r.Extras.Number(PriorDaysCorwinSchultzFeature.FeatureName), multiplier.Value, pmCumVol, sigma,
                        (double)entry.Open, notional, GapperSpreadMultiplier.ImpactConstant(P), P.CostModel.RoundTripFloor)).RoundTrip;
                    costByEntry[entry.StartUtc] = cost;
                    entries++;
                    costSum += cost;
                }
                var trade = GapperRuleSimulator.Simulate(new GapperRuleConfig(0, 0, deathWindow, stopPct, c.SellByEt, vwap), bars, r.DateEt, entry.StartUtc, cost, exits);
                if (trade is null) continue;
                trades[k].Add(new WeightedTrade(r.DateEt, trade.NetReturn, trade.ExitReason, r.Weight));
                daySums[k][dayIndex[r.DateEt]] += r.Weight * trade.NetReturn;
            }
        }
        part.Statistic["distinctEntries"] = entries;
        part.Statistic["meanRoundTripCost"] = entries == 0 ? double.NaN : costSum / entries;
        part.Statistic["spreadMultiplier"] = multiplier;
        part.Statistic["spreadMultiplierSource"] = source;
        part.Statistic["exitLadder"] = $"stop {stopPct}%, VWAP loss {(vwap ? "on" : "off")}, velocity death {deathWindow}m, sell-by per configuration";
        ScoreWeightedGrid(h, run, part, split, configs.Select(c => c.Label).ToList(), trades, daySums, dayWeight, rows.Count);
    }

    // ── H19: does price move before the headline? ────────────────────────

    private async Task PrePublicationDriftAsync(GapperHypothesis h, HypothesisRun run, string split, CancellationToken ct)
    {
        var rows = (await panel.LoadAsync(split, "08:00", ct)).Where(r => r.IsGapper).ToList();
        var news = await FirstNewsAsync(split, ct);
        var store = await panel.StoreAsync(ct);
        var shares = new List<ClusterObservation>();
        var pres = new List<double>();
        var posts = new List<double>();
        var keys = new List<(string, DateOnly)>();
        foreach (var r in rows)
        {
            if (!news.TryGetValue((r.Symbol, r.DateEt), out var ev)) continue;
            var et = MinuteBarMath.ToEastern(ev.PublishedUtc);
            if (DateOnly.FromDateTime(et) != r.DateEt || et.TimeOfDay < new TimeSpan(4, 30, 0) || et.TimeOfDay > new TimeSpan(9, 15, 0)) continue;
            var bars = store.ReadDay(r.Symbol, r.DateEt).OrderBy(b => b.StartUtc).ToList();
            var before = MinuteBarMath.CloseAsOf(bars, ev.PublishedUtc - PrePubWindow);
            var atPub = MinuteBarMath.CloseAsOf(bars, ev.PublishedUtc);
            var after = bars.Where(b => b.EndUtc > ev.PublishedUtc && b.EndUtc <= ev.PublishedUtc + PrePubWindow && b.Volume > 0).ToList();
            if (before is not > 0 || atPub is not > 0 || after.Count == 0) continue;
            double pre = (double)(atPub.Value / before.Value - 1m);
            double post = (double)(after.Max(b => b.High) / atPub.Value - 1m);
            double denom = Math.Max(pre, 0) + Math.Max(post, 0);
            if (denom <= 0) continue;
            shares.Add(new ClusterObservation(r.DateEt, Math.Max(pre, 0) / denom));
            pres.Add(pre * 100);
            posts.Add(post * 100);
            keys.Add((r.Symbol, r.DateEt));
        }
        Describe(run, keys, "Tier20 gapper days whose first realtime news item published between 04:30 and 09:15 ET", ["return over the 30 minutes before publication"], ["highest high in the 30 minutes after publication"]);
        var part = BootstrapPart("medianPreShare", "median share of the hour's move before publication", shares, DayClusteredBootstrap.Median,
            h.MinEffectNumber("medianPreShareAtMost"), EffectDirection.AtMost, shares.Count, withP: false);
        part.Statistic["medianPrePct"] = Median(pres);
        part.Statistic["medianPostPct"] = Median(posts);
        part.Statistic["shareWithPreAbove5Pct"] = pres.Count == 0 ? double.NaN : pres.Count(x => x >= 5) / (double)pres.Count;
        part.Statistic["window"] = "30 minutes before and after publication";
        run.Parts.Add(part);
    }

    // ── H20: how fast does the second-hand edge decay? ───────────────────

    private async Task LatencyDecayAsync(GapperHypothesis h, HypothesisRun run, string split, CancellationToken ct)
    {
        var rows = (await panel.LoadAsync(split, "08:00", ct)).Where(r => r.InPopulation).ToList();
        var news = await FirstNewsAsync(split, ct);
        var store = await panel.StoreAsync(ct);
        var diffs = new List<WeightedValue>();
        var sums = new double[DecayLatencies.Length];
        double weight = 0;
        var keys = new List<(string, DateOnly)>();
        foreach (var r in rows)
        {
            if (!news.TryGetValue((r.Symbol, r.DateEt), out var ev) || ev.PreDirection != 1) continue;
            var bars = store.ReadDay(r.Symbol, r.DateEt).OrderBy(b => b.StartUtc).ToList();
            var exitAt = MinuteBarMath.ToUtc(r.DateEt, DecayExitEt);
            var exit = bars.FirstOrDefault(b => b.Volume > 0 && b.Open > 0 && b.StartUtc >= exitAt);
            if (exit is null) continue;
            var gross = new double[DecayLatencies.Length];
            bool all = true;
            for (int i = 0; i < DecayLatencies.Length && all; i++)
            {
                var entry = EntryBar(bars, r.DateEt, Trigger(ev, r.DateEt, DecayLatencies[i]), LastEntryEt);
                if (entry is null) all = false;
                else gross[i] = (double)(exit.Open / entry.Open - 1m) * 100;
            }
            if (!all) continue;
            diffs.Add(new WeightedValue(r.DateEt, gross[0] - gross[^1], r.Weight));
            for (int i = 0; i < gross.Length; i++) sums[i] += r.Weight * gross[i];
            weight += r.Weight;
            keys.Add((r.Symbol, r.DateEt));
        }
        Describe(run, keys, PopulationDescription(rows, "population days") + "; bullish first news items with an entry at every latency", [$"entry {string.Join("/", DecayLatencies)} minutes after publication"], ["gross return to the 09:28 open, pct"]);
        var minEffect = h.MinEffectNumber("meanDifferencePctAtLeast");
        var part = new HypothesisPart { Name = "meanDifferencePct", EffectName = $"mean gross return at {DecayLatencies[0]}m minus at {DecayLatencies[^1]}m (pct)", MinEffect = minEffect, Direction = EffectDirection.AtLeast, N = diffs.Count, Days = diffs.Select(d => d.Day).Distinct().Count() };
        part.Statistic["curve"] = DecayLatencies.Select((l, i) => new { latencyMinutes = l, meanGrossPct = weight > 0 ? sums[i] / weight : double.NaN }).ToList();
        if (diffs.Count < P.Statistics.MinBucketN)
        {
            part.Verdict = HypothesisVerdict.Void;
            part.VerdictFixed = true;
            part.Reasons.Add($"{diffs.Count} events with an entry at every latency, fewer than minBucketN {P.Statistics.MinBucketN}");
            run.Parts.Add(part);
            return;
        }
        var b = WeightedPopulation.Mean(diffs, P.Statistics.BootstrapDraws, P.Statistics.Seed, P.Statistics.CiLevel, MinDays);
        part.Effect = b.Estimate;
        part.CiLow = double.IsNaN(b.Low) ? null : b.Low;
        part.CiHigh = double.IsNaN(b.High) ? null : b.High;
        part.MeetsMinimum = b.Estimate >= minEffect;
        run.Parts.Add(part);
    }

    private static double Median(List<double> values)
    {
        if (values.Count == 0) return double.NaN;
        var sorted = values.OrderBy(v => v).ToList();
        int n = sorted.Count;
        return n % 2 == 1 ? sorted[n / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) / 2;
    }
}
