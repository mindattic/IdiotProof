using System.Globalization;
using System.Text;
using System.Text.Json;
using IdiotProof.Core.Evidence;
using IdiotProof.Core.Services;
using IdiotProof.Evidence;
using IdiotProof.Evidence.Economics;
using IdiotProof.Evidence.Evaluation;
using IdiotProof.Evidence.Gappers;
using IdiotProof.Evidence.Search;
using IdiotProof.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IdiotProof.Core.Research.Gappers.Explore;

/// <summary>
/// <c>explore split-days-pull</c> and <c>explore split-days</c> (IP-T490), TRAIN only: the days protocol U(D) drops around a
/// reverse split or ADS ratio change, and the unexplained > +200% moves, which IP-T484 found run >= +100% at 40-60x the panel
/// base on daily bars but which carry no minute bars. The event set is every recorded reverse split (GapperStudySplits, the
/// Alpaca corporate-action tier), every inferred ratio change (the watchlist's <see cref="BuildSeries"/> rule) and every
/// split-adjusted close-to-close move above +200% on a day the universe excluded, with effective day D in train; each event
/// covers D-1..D+10 where the split-adjusted prior close is $0.10-$20 and the day is in train. The pull writes those
/// symbol-days' raw 04:00-20:00 minute bars into the study's file store through the frozen pull path (resumable). The
/// analysis puts every price on one split-adjusted basis, describes the runs by day offset, ratio, advance announcement,
/// float and promotion profile, and tests a long-only calendar entry and an ignition entry with a ride grid net of the
/// frozen cost model at 1.0/0.5/0.25x spread, one ledger trial per configuration.
/// </summary>
public sealed partial class GapperExploreStage
{
    public const int SplitOffsetMin = -1, SplitOffsetMax = 10;
    /// <summary>IP-T484's minute-bar population base rates (weighted, >= +50/+100/+300% by the 04:00-16:00 high).</summary>
    private static readonly double[] PanelMinuteBase = [0.00199, 0.00052, 0.00003];
    /// <summary>News or 8-K evidence of the split counts as an advance announcement when published in the 60 days before D 04:00 ET.</summary>
    public const int AnnouncementLookbackDays = 60;
    private const double UnexplainedMoveMin = 2.0;

    private enum SdClass { Recorded, Inferred, Unexplained }

    private static readonly string[] SdClassNames = ["recorded reverse split (Alpaca corporate action)", "inferred ratio change (no split on record)", "unexplained > +200% close move (excluded day)"];

    private sealed class SdSymbol
    {
        public string Symbol = "";
        public DateOnly[] D = [];
        public double[] O = [], H = [], L = [], C = [], V = [];
        /// <summary>Every price-basis change: recorded splits (any direction) and inferred ratio changes.</summary>
        public readonly List<(DateOnly Ex, double F, bool Inferred)> Chain = [];

        /// <summary>The product of every basis change after <paramref name="date"/>: price on date's basis times this = price on the latest basis.</summary>
        public double Cum(DateOnly date)
        {
            double f = 1;
            foreach (var (ex, factor, _) in Chain) if (ex > date) f *= factor;
            return f;
        }

        public int Index(DateOnly day) => Array.BinarySearch(D, day);
    }

    private sealed class SdDay
    {
        public int Offset;
        public DateOnly Day;
        public int J;
        /// <summary>Split-adjusted prior close on this day's own (as-traded) basis.</summary>
        public double Pc;
        /// <summary>The split-adjusted prior close is inside the universe price band ($0.10-$20).</summary>
        public bool InBand;
        /// <summary>Split or ratio-change event day whose first bar opens >= 3x above or <= 1/3 of the adjusted prior close: the recorded ex-date and the as-traded basis disagree (an off-by-one ex-date), so the day is left out.</summary>
        public bool BasisMismatch;
        public bool Usable => InBand && !BasisMismatch && Facts is not null;
        public double Ret1 = double.NaN, Ret2 = double.NaN;
        public double Adv20Shares, Adv20Dollars;
        public double? Sigma;
        public RunnerDayFacts? Facts;
        public RunnerGrid? Grid;
        public double CsSum;
        public int CsPairs, Bars;
    }

    private sealed class SdEvent
    {
        public int Id;
        public SdSymbol Sym = null!;
        public DateOnly D;
        public SdClass Class;
        public double Ratio;
        public readonly SdDay?[] Days = new SdDay?[SplitOffsetMax - SplitOffsetMin + 1];
        public DateOnly? AnnouncedOn;
        public string? AnnouncedBy;
        public double? Shares;
        public bool Foreign, Holding, NewListing;
        public string Name = "";
        public SdDay? At(int offset) => offset is < SplitOffsetMin or > SplitOffsetMax ? null : Days[offset - SplitOffsetMin];
        public bool Announced => AnnouncedOn is not null;
        public bool Promo => Foreign && NewListing && Shares is < 10e6;
    }

    private sealed record SdExit(RunnerExitPlan Plan, int ExtraSessions, string Name);

    /// <summary>The exit grid: stop x trail x (plain, half at +30%, velocity death, VWAP loss) out 15:55 the entry day; stop x trail plain out 15:55 the next session; and a plain hold to 15:55.</summary>
    public static List<(RunnerExitPlan Plan, int ExtraSessions, string Name)> SplitDayExits()
    {
        var list = new List<(RunnerExitPlan, int, string)>();
        (double Arm, double Trail)[] trails = [(10, 25), (20, 35)];
        foreach (var stop in new[] { 8.0, 15.0 })
            foreach (var (arm, trail) in trails)
                foreach (var mode in new[] { "plain", "half", "velocity", "vwap" })
                {
                    var cool = mode switch { "velocity" => RunnerCooling.Velocity, "vwap" => RunnerCooling.VwapLoss, _ => RunnerCooling.None };
                    string name = string.Create(CultureInfo.InvariantCulture,
                        $"stop -{stop:0}%, trail {trail:0}% once +{arm:0}%{(mode == "half" ? ", half at +30%" : "")}{(mode == "velocity" ? ", velocity death" : mode == "vwap" ? ", VWAP loss" : "")}, out 15:55 same day");
                    list.Add((new RunnerExitPlan(name, stop, arm, trail, mode == "half" ? 30 : null, cool), 0, name));
                }
        foreach (var stop in new[] { 8.0, 15.0 })
            foreach (var (arm, trail) in trails)
            {
                string name = string.Create(CultureInfo.InvariantCulture, $"stop -{stop:0}%, trail {trail:0}% once +{arm:0}%, out 15:55 next session");
                list.Add((new RunnerExitPlan(name, stop, arm, trail, null, RunnerCooling.None), 1, name));
            }
        list.Add((new RunnerExitPlan("no stop, out 15:55 same day", 100, 1e9, 0, null, RunnerCooling.None), 0, "no stop, out 15:55 same day (hold)"));
        return list;
    }

    private static readonly string[] SplitEntryNames =
        ["calendar D 08:00 (premarket, marketable)", "calendar D 09:30 open", "calendar D+1 08:00 (premarket, marketable)", "calendar D+1 09:30 open",
         "ignition k=5 on D, else D+1", "ignition k=10 on D, else D+1", "ignition k=20 on D, else D+1"];

    // ── the event set (shared by the pull and the analysis) ─────────────────

    private async Task<(List<SdEvent> Events, int RecordedInTrain, DateOnly TrainFrom, DateOnly TrainTo)> SplitEventsAsync(CancellationToken ct)
    {
        var train = s.Params.Split("train");
        DateOnly trainFrom = train.From, trainTo = train.To;
        var splits = new Dictionary<string, List<(DateOnly Ex, double Factor)>>(StringComparer.Ordinal);
        var excluded = new HashSet<(string, DateOnly)>();
        await using (var db = await s.Db.CreateDbContextAsync(ct))
        {
            foreach (var sp in await db.GapperStudySplits.AsNoTracking().Select(x => new { x.Symbol, x.ExDate, x.OldRate, x.NewRate }).ToListAsync(ct))
            {
                if (sp.NewRate <= 0 || sp.OldRate <= 0) continue;
                if (!splits.TryGetValue(sp.Symbol, out var l)) splits[sp.Symbol] = l = [];
                l.Add((sp.ExDate, (double)(sp.OldRate / sp.NewRate)));
            }
            // Train only: the universe rows of the train split, never validation or holdout.
            foreach (var u in await db.GapperStudyUniverseDays.AsNoTracking().Where(u => u.Split == "train" && u.ExcludedReason != null)
                         .Select(u => new { u.Symbol, u.DateEt }).ToListAsync(ct))
                excluded.Add((u.Symbol, u.DateEt));
        }
        var symbolSet = excluded.Select(e => e.Item1).ToHashSet(StringComparer.Ordinal);
        foreach (var (sym, l) in splits)
            if (l.Any(x => x.Factor > 1 && x.Ex >= trainFrom && x.Ex <= trainTo)) symbolSet.Add(sym);
        var symbols = symbolSet.OrderBy(x => x, StringComparer.Ordinal).ToList();

        var barFrom = s.Params.DataTiers.DailyBars.From;
        var data = new Dictionary<string, SdSymbol>(StringComparer.Ordinal);
        for (int i = 0; i < symbols.Count; i += 400)
        {
            var batch = symbols.Skip(i).Take(400).ToList();
            await using var db = await s.Db.CreateDbContextAsync(ct);
            // Daily bars stop at the last train day: nothing after train is read.
            var raw = await db.GapperStudyDailyBars.AsNoTracking().Where(b => batch.Contains(b.Symbol) && b.DateEt >= barFrom && b.DateEt <= trainTo)
                .Select(b => new { b.Symbol, b.DateEt, b.Open, b.High, b.Low, b.Close, b.Volume }).ToListAsync(ct);
            foreach (var g in raw.GroupBy(b => b.Symbol))
            {
                var bars = g.OrderBy(b => b.DateEt).Select(b => (b.DateEt, (double)b.Open, (double)b.High, (double)b.Low, (double)b.Close, (double)b.Volume)).ToList();
                var rec = splits.GetValueOrDefault(g.Key) ?? [];
                var sym = new SdSymbol
                {
                    Symbol = g.Key, D = bars.Select(b => b.DateEt).ToArray(), O = bars.Select(b => b.Item2).ToArray(), H = bars.Select(b => b.Item3).ToArray(),
                    L = bars.Select(b => b.Item4).ToArray(), C = bars.Select(b => b.Item5).ToArray(), V = bars.Select(b => b.Item6).ToArray(),
                };
                foreach (var (ex, f) in rec) sym.Chain.Add((ex, f, false));
                // The watchlist's ratio-change inference (IP-T484): no split on record and the whole day 3x away on ordinary dollar volume.
                foreach (var r in BuildSeries(bars, rec).Reverse.Where(r => r.Inferred)) sym.Chain.Add((r.Ex, r.Ratio, true));
                sym.Chain.Sort((a, b) => a.Ex.CompareTo(b.Ex));
                data[g.Key] = sym;
            }
        }

        var events = new List<SdEvent>();
        foreach (var sym in data.Values)
        {
            foreach (var (ex, f, inferred) in sym.Chain)
            {
                if (!(f > 1) || ex < trainFrom || ex > trainTo) continue;
                var d = MarketCalendar.IsTradingDay(ex) ? ex : MarketCalendar.NextTradingDay(ex);
                if (d > trainTo) continue;
                events.Add(new SdEvent { Sym = sym, D = d, Class = inferred ? SdClass.Inferred : SdClass.Recorded, Ratio = f });
            }
            for (int j = 1; j < sym.D.Length; j++)
            {
                var d = sym.D[j];
                if (d < trainFrom || d > trainTo || !excluded.Contains((sym.Symbol, d))) continue;
                var prev = sym.D[j - 1];
                if (sym.Chain.Any(c => c.Ex > prev && c.Ex <= d)) continue;
                if (!(sym.C[j - 1] > 0) || sym.C[j] / sym.C[j - 1] - 1 < UnexplainedMoveMin) continue;
                events.Add(new SdEvent { Sym = sym, D = d, Class = SdClass.Unexplained, Ratio = double.NaN });
            }
        }
        events = events.OrderBy(e => e.D).ThenBy(e => e.Sym.Symbol, StringComparer.Ordinal).ThenBy(e => e.Class).ToList();
        for (int i = 0; i < events.Count; i++) events[i].Id = i;

        var band = s.Params.Universe;
        foreach (var ev in events)
        {
            var sym = ev.Sym;
            var day = MarketCalendar.PreviousTradingDay(ev.D);
            for (int k = SplitOffsetMin; k <= SplitOffsetMax; k++, day = MarketCalendar.NextTradingDay(day))
            {
                if (day > trainTo) break;
                int j = sym.Index(day);
                if (j < 1) continue;
                double pc = sym.C[j - 1] * sym.Cum(sym.D[j - 1]) / sym.Cum(day);
                if (!(pc > 0)) continue;
                // Out-of-band days are kept (pulled) so a held position can still exit on them; only in-band days are described or entered.
                var row = new SdDay { Offset = k, Day = day, J = j, Pc = pc, InBand = pc >= (double)band.PriceMinInclusive && pc < (double)band.PriceMaxExclusive };
                double close = sym.C[j] * sym.Cum(day);
                if (j + 1 < sym.D.Length) row.Ret1 = sym.C[j + 1] * sym.Cum(sym.D[j + 1]) / close - 1;
                if (j + 2 < sym.D.Length) row.Ret2 = sym.C[j + 2] * sym.Cum(sym.D[j + 2]) / close - 1;
                double sh = 0, dol = 0; int n = 0;
                for (int i = Math.Max(0, j - 20); i < j; i++) { sh += sym.V[i] * sym.Cum(day) / sym.Cum(sym.D[i]); dol += sym.C[i] * sym.V[i]; n++; }
                row.Adv20Shares = n > 0 ? sh / n : 0;
                row.Adv20Dollars = n > 0 ? dol / n : 0;
                var rets = new List<double>();
                for (int i = Math.Max(1, j - 20); i < j; i++)
                {
                    double a = sym.C[i - 1] * sym.Cum(sym.D[i - 1]), b = sym.C[i] * sym.Cum(sym.D[i]);
                    if (a > 0 && b > 0) rets.Add(Math.Log(b / a));
                }
                row.Sigma = rets.Count >= 3 ? EvidenceStats.StdDev(rets) : null;
                ev.Days[k - SplitOffsetMin] = row;
            }
        }
        int recordedInTrain = splits.Values.Sum(l => l.Count(x => x.Factor > 1 && x.Ex >= trainFrom && x.Ex <= trainTo));
        return (events, recordedInTrain, trainFrom, trainTo);
    }

    // ── pull ─────────────────────────────────────────────────────────────

    /// <summary>`explore split-days-pull`: raw 04:00-20:00 minute bars for every event symbol-day through the frozen pull path; resumable (a day on file is skipped).</summary>
    private async Task<(List<string>, int, string?)> SplitDaysPullAsync(ExploreRequest req, CancellationToken ct)
    {
        var (events, _, _, trainTo) = await SplitEventsAsync(ct);
        var store = await s.StoreAsync(ct);
        var wanted = new HashSet<(string Symbol, DateOnly DateEt)>();
        foreach (var ev in events)
            foreach (var d in ev.Days)
                if (d is not null && d.Day <= trainTo) wanted.Add((ev.Sym.Symbol, d.Day));
        int before = wanted.Count(w => store.HasDay(w.Symbol, w.DateEt));
        s.Logger.LogInformation("[{Now}] explore split-days-pull: {Events} events, {Wanted} symbol-days wanted, {Have} already on file", GapperStudyServices.Et(s.UtcNow), events.Count, wanted.Count, before);
        var keys = await s.ResolveKeysAsync(req.Owner, ct);
        await using var feed = s.Feed(keys);
        var (calls, written) = await GapperPullStage.PullSymbolDaysAsync(s, feed, store, wanted, new CallBudget(0), ct);
        int withBars = wanted.Count(w => store.ReadDay(w.Symbol, w.DateEt).Count > 0);
        var lines = new List<string>
        {
            $"events: {string.Join(", ", Enum.GetValues<SdClass>().Select(c => $"{events.Count(e => e.Class == c):N0} {SdClassNames[(int)c]}"))}",
            $"symbol-days wanted (D-1..D+10 in train with a daily bar, any price: the analysis describes and enters the $0.10-$20 days and lets held positions exit on the rest): {wanted.Count:N0}; on file before: {before:N0}; written now: {written:N0} ({calls:N0} requests)",
            $"symbol-days with at least one minute bar: {withBars:N0} ({(wanted.Count == 0 ? 0 : 100.0 * withBars / wanted.Count):0.0}%)",
        };
        return (lines, 0, null);
    }

    // ── analysis ─────────────────────────────────────────────────────────

    private async Task<(List<string>, int, string?)> SplitDaysAsync(ExploreRequest req, CancellationToken ct)
    {
        var p = s.Params;
        var (multiplier, source) = GapperSpreadMultiplier.Resolve(s.Paths, p);
        if (multiplier is null) return ([$"cost model unavailable ({source}); run `calibrate-spread` first"], 0, null);
        var notional = GapperSpreadMultiplier.TradeNotional(p) ?? throw new InvalidDataException("params.json has no costModel.impact.tradeNotionalUsd.");
        double impact = GapperSpreadMultiplier.ImpactConstant(p), floor = p.CostModel.RoundTripFloor;
        var profilePath = s.Paths.ProfileFile(false);
        if (!File.Exists(profilePath)) throw new InvalidOperationException($"{Path.GetFileName(profilePath)} does not exist; run `snapshot train` first.");
        var profile = TimeOfDayProfile.Load(profilePath);
        var store = await s.StoreAsync(ct);
        var (events, recordedInTrain, trainFrom, trainTo) = await SplitEventsAsync(ct);
        s.Logger.LogInformation("[{Now}] explore split-days: {Events} events", GapperStudyServices.Et(s.UtcNow), events.Count);

        // ── 1. minute bars: facts per day, grids for D..D+2, the prior-day Corwin-Schultz pairs ──
        int done = 0;
        Parallel.ForEach(events, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, req.Parallelism), CancellationToken = ct }, ev =>
        {
            foreach (var d in ev.Days)
            {
                if (d is null) continue;
                var bars = store.ReadDay(ev.Sym.Symbol, d.Day);
                d.Bars = bars.Count;
                if (bars.Count == 0) continue;
                var ext = bars.Where(b => b.Volume > 0 && b.Low > 0).OrderBy(b => b.StartUtc).ToList();
                if (ext.Count >= 2)
                {
                    double cs = TradingCosts.CorwinSchultzSpread(ext);
                    if (double.IsFinite(cs)) { d.CsSum = cs * (ext.Count - 1); d.CsPairs = ext.Count - 1; }
                }
                var g = RunnerGrid.Build(bars);
                if (g is null) continue;
                d.Facts = RunnerIgnition.Facts(g, d.Pc);
                d.BasisMismatch = ev.Class != SdClass.Unexplained && (d.Facts.FirstGap >= 2.0 || d.Facts.FirstGap <= -2.0 / 3);
                if (d.Offset is >= 0 and <= 2) d.Grid = g;
            }
            if (Interlocked.Increment(ref done) % 200 == 0) s.Logger.LogInformation("[{Now}] explore split-days: bars for {Done}/{Total} events", GapperStudyServices.Et(s.UtcNow), done, events.Count);
        });

        // ── 2. announcement, float, promotion profile, headlines ──
        var symSet = events.Select(e => e.Sym.Symbol).ToHashSet(StringComparer.Ordinal);
        var minD = events.Count == 0 ? trainFrom : events.Min(e => e.D);
        var newsFrom = MinuteBarMath.ToUtc(minD.AddDays(-AnnouncementLookbackDays - 5), TimeSpan.Zero);
        var newsTo = MinuteBarMath.ToUtc(trainTo, new TimeSpan(20, 0, 0));
        var items = new Dictionary<string, List<(DateTime Utc, int Bits, string Text)>>(StringComparer.Ordinal);
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        await using (var db = await s.Db.CreateDbContextAsync(ct))
        {
            var news = new Dictionary<Guid, (DateTime Utc, int Bits, string Text)>();
            await foreach (var e in db.CatalystEvents.AsNoTracking().Where(e => e.PublishedUtc >= newsFrom && e.PublishedUtc <= newsTo)
                               .Select(e => new { e.Id, e.PublishedUtc, e.Headline, e.PreClass }).AsAsyncEnumerable().WithCancellation(ct))
                news[e.Id] = (e.PublishedUtc, CatalystTypes.FromHeadline(e.Headline, e.PreClass), e.Headline ?? "");
            await foreach (var x in db.CatalystEventSymbols.AsNoTracking().Select(x => new { x.EventId, x.Symbol }).AsAsyncEnumerable().WithCancellation(ct))
            {
                if (!symSet.Contains(x.Symbol) || !news.TryGetValue(x.EventId, out var ev)) continue;
                if (!items.TryGetValue(x.Symbol, out var l)) items[x.Symbol] = l = [];
                l.Add(ev);
            }
            var fFrom = DateOnly.FromDateTime(newsFrom);
            var filings = await db.EdgarFilingIndex.AsNoTracking().Where(f => f.Symbol != null && f.FormType.StartsWith("8-K") && f.FiledDate >= fFrom && f.FiledDate <= trainTo)
                .Select(f => new { f.Symbol, f.FiledDate, f.AcceptedUtc, f.FormType, f.ItemsJson }).ToListAsync(ct);
            foreach (var f in filings)
            {
                if (!symSet.Contains(f.Symbol!)) continue;
                string[] its = [];
                if (!string.IsNullOrEmpty(f.ItemsJson)) try { its = JsonSerializer.Deserialize<string[]>(f.ItemsJson) ?? []; } catch (JsonException) { }
                int bits = CatalystTypes.FromFiling(f.FormType, its);
                // 8-K item 5.03 (articles amended, how a reverse split is effected) counts as split evidence too.
                if (its.Any(i => i.Trim() == "5.03")) bits |= CatalystTypes.Bit("rsplit");
                var known = f.AcceptedUtc ?? MinuteBarMath.ToUtc(f.FiledDate, new TimeSpan(17, 30, 0));
                if (!items.TryGetValue(f.Symbol!, out var l)) items[f.Symbol!] = l = [];
                l.Add((known, bits, $"{f.FormType} items {(its.Length == 0 ? "n/a" : string.Join(",", its))}"));
            }
            foreach (var c in await db.GapperStudyCikMap.AsNoTracking().Select(c => new { c.Symbol, c.Name }).ToListAsync(ct)) names[c.Symbol] = c.Name;
        }
        foreach (var l in items.Values) l.Sort((a, b) => a.Utc.CompareTo(b.Utc));
        shares ??= SharesOutstandingHistory.ReadCsv(SharesCsv());
        LoadCikMap();
        int rsBit = CatalystTypes.Bit("rsplit");
        foreach (var ev in events)
        {
            var sym = ev.Sym;
            var cutoff = MinuteBarMath.ToUtc(ev.D, new TimeSpan(4, 0, 0));
            if (ev.Class != SdClass.Unexplained && items.TryGetValue(sym.Symbol, out var l))
                foreach (var (utc, bits, text) in l)
                    if ((bits & rsBit) != 0 && utc < cutoff && utc >= cutoff.AddDays(-AnnouncementLookbackDays))
                    {
                        ev.AnnouncedOn = DateOnly.FromDateTime(EasternTime.FromUtc(utc));
                        ev.AnnouncedBy = text.Length > 80 ? text[..80] : text;
                        break;
                    }
            ev.Name = names.GetValueOrDefault(sym.Symbol) ?? "";
            if (cikMap is not null && shares is not null && cikMap.TryGetValue(sym.Symbol, out var cik) && shares.KnownBefore(cik, ev.D) is { } fact)
            {
                // A count filed before a basis change is on the old basis: put it on D's.
                double f = 1;
                foreach (var (ex, factor, _) in sym.Chain) if (ex > fact.Filed && ex <= ev.D) f *= factor;
                ev.Shares = fact.Shares / f;
            }
            ev.Foreign = GapperUniverseBuilder.IsAdr(ev.Name, p.Universe.AdrNamePatterns) || ForeignName.IsMatch(ev.Name);
            ev.Holding = HoldingName.IsMatch(ev.Name);
            ev.NewListing = sym.D.Length > 0 && sym.D[0] > new DateOnly(2021, 9, 15) && ev.D.DayNumber - sym.D[0].DayNumber < 730;
        }

        // ── 3. ADV deciles of the entry days (train universe, non-excluded rows) ──
        var deciles = new Dictionary<DateOnly, double[]>();
        await using (var db = await s.Db.CreateDbContextAsync(ct))
        {
            var cuts = await db.GapperStudyUniverseDays.AsNoTracking().Where(x => x.Split == "train" && x.ExcludedReason == null)
                .GroupBy(x => new { x.DateEt, x.AdvDecile }).Select(g => new { g.Key.DateEt, g.Key.AdvDecile, Min = g.Min(x => x.Adv20Dollars) }).ToListAsync(ct);
            foreach (var g in cuts.GroupBy(c => c.DateEt))
            {
                var arr = new double[10];
                Array.Fill(arr, double.MaxValue);
                foreach (var c in g) if (c.AdvDecile is >= 0 and < 10) arr[c.AdvDecile] = (double)c.Min;
                deciles[g.Key] = arr;
            }
        }
        int DecileOf(DateOnly day, double adv)
        {
            if (!deciles.TryGetValue(day, out var arr)) return 0;
            int k = 0;
            for (int i = 0; i < 10; i++) if (adv >= arr[i]) k = i;
            return k;
        }

        // ── 4. the trade grid ──
        var trainDays = MarketCalendar.TradingDays(trainFrom, trainTo);
        var dayIndex = trainDays.Select((d, i) => (d, i)).ToDictionary(x => x.d, x => x.i);
        var folds = ExploreFolds.Build(trainDays, foldMonths.Initial, foldMonths.Eval);
        var months = trainDays.Select(d => d.Year * 12 + d.Month).Distinct().OrderBy(m => m).ToList();
        var dayMonth = trainDays.Select(d => months.IndexOf(d.Year * 12 + d.Month)).ToArray();
        var dayFold = trainDays.Select(d => folds.FindIndex(f => f.InEval(d))).ToArray();
        var exits = SplitDayExits().Select(x => new SdExit(x.Plan, x.ExtraSessions, x.Name)).ToList();
        var trades = new List<(WlRow Row, WlSim Sim, SdEvent Ev)>[SplitEntryNames.Length];
        for (int e = 0; e < trades.Length; e++) trades[e] = [];
        var entryCounts = new int[SplitEntryNames.Length];

        WlSim? Trade(SdEvent ev, int entryOffset, int minute, double fill)
        {
            var row = ev.At(entryOffset);
            if (row?.Grid is null) return null;
            var next = ev.At(entryOffset + 1);
            var g = row.Grid;
            RunnerGrid? g2 = null;
            if (next is { Bars: > 0, BasisMismatch: false })
            {
                // The next session on the entry day's basis (a fresh grid when a basis change falls between them).
                double f = ev.Sym.Cum(next.Day) / ev.Sym.Cum(row.Day);
                if (Math.Abs(f - 1) < 1e-9 && next.Grid is not null) g2 = next.Grid;
                else
                {
                    g2 = RunnerGrid.Build(store.ReadDay(ev.Sym.Symbol, next.Day));
                    if (g2 is not null) RunnerPath.Rebase(g2, f);
                }
            }
            var path = new RunnerGrid?[] { g, g2 };
            var cum = new double[RunnerGrid.Minutes + 1];
            for (int m = 0; m < RunnerGrid.Minutes; m++) cum[m + 1] = cum[m] + g.V[m];
            double pmCumVol = cum[minute] - cum[Math.Max(0, g.First)];
            double v5 = cum[minute] - cum[Math.Max(0, minute - 5)];
            // Corwin-Schultz over the prior pulled sessions in the window (D-1, and D for a D+1 entry), pooled by bar pairs.
            double csSum = 0; int csPairs = 0;
            for (int k = SplitOffsetMin; k < entryOffset; k++) if (ev.At(k) is { CsPairs: > 0 } pr) { csSum += pr.CsSum; csPairs += pr.CsPairs; }
            double? cs = csPairs >= 59 ? csSum / csPairs : null;
            double Cost(double size, double scale) => GapperCostModel.Estimate(new GapperCostInputs(cs, multiplier.Value * scale, pmCumVol, row.Sigma, fill, size, impact, floor)).RoundTrip;
            var sim = new WlSim { Minute = minute, Fill = (float)fill, Gross = new float[exits.Count], Cap1k = new float[exits.Count], Cap5k = new float[exits.Count], Full1k = new bool[exits.Count], Full5k = new bool[exits.Count] };
            for (int l = 0; l < WatchSpreadScales.Length; l++) sim.Cost[l] = (float)Cost(notional, WatchSpreadScales[l]);
            int decile = DecileOf(row.Day, row.Adv20Dollars);
            var entryEt = RunnerGrid.StartEt + TimeSpan.FromMinutes(minute);
            for (int x = 0; x < exits.Count; x++)
            {
                int last = exits[x].ExtraSessions;
                if (last == 1 && (next is null || next.BasisMismatch || next.Day > trainTo)) { sim.Gross[x] = sim.Cap1k[x] = sim.Cap5k[x] = float.NaN; continue; }
                var res = RunnerPath.Simulate(path, 0, minute, fill, exits[x].Plan, last);
                if (res is not { } rr) { sim.Gross[x] = sim.Cap1k[x] = sim.Cap5k[x] = float.NaN; continue; }
                sim.Gross[x] = rr.Gross;
                var exitEt = RunnerGrid.StartEt + TimeSpan.FromMinutes(rr.ExitMinute);
                // An overnight exit is forecast on the entry day's profile at the exit clock time (an approximation).
                double? vexit = ParticipationCap.ForecastWindowVolume(profile, decile, pmCumVol, entryEt, exitEt - TimeSpan.FromMinutes(1), exitEt + TimeSpan.FromMinutes(4));
                var c1 = ParticipationCap.Size(fill, WatchCapacity[0], v5, vexit);
                var c5 = ParticipationCap.Size(fill, WatchCapacity[1], v5, vexit);
                sim.Cap1k[x] = c1.Skip ? float.NaN : (float)(rr.Gross - Cost(c1.NotionalUsd, 1));
                sim.Cap5k[x] = c5.Skip ? float.NaN : (float)(rr.Gross - Cost(c5.NotionalUsd, 1));
                sim.Full1k[x] = c1.FullSize;
                sim.Full5k[x] = c5.FullSize;
            }
            return sim;
        }

        static (int Minute, double Fill)? FirstBarFrom(RunnerGrid g, int from, int to)
        {
            for (int m = from; m <= Math.Min(to, g.Last); m++) if (g.Real(m)) return (m, g.O[m]);
            return null;
        }

        foreach (var ev in events)
        {
            if (ev.Class == SdClass.Unexplained) continue;
            for (int e = 0; e < SplitEntryNames.Length; e++)
            {
                int entryOffset = -1, minute = -1;
                double fill = 0;
                if (e < 4)
                {
                    if (ev.Class != SdClass.Recorded) continue;
                    entryOffset = e < 2 ? 0 : 1;
                    if (ev.At(entryOffset) is not { InBand: true, BasisMismatch: false, Grid: { } g }) continue;
                    var en = e % 2 == 0 ? FirstBarFrom(g, 240, RunnerGrid.RthOpen - 1) : FirstBarFrom(g, RunnerGrid.RthOpen, RunnerGrid.RthOpen + 30);
                    if (en is not { } v) continue;
                    (minute, fill) = v;
                }
                else
                {
                    double k = IgnitionKs[e - 4];
                    foreach (var off in new[] { 0, 1 })
                    {
                        if (ev.At(off) is not { InBand: true, BasisMismatch: false, Grid: { } g } row) continue;
                        double minuteFloor = Math.Max(IgnitionMinShares, row.Adv20Shares / IgnitionAdvDivisor);
                        var hit = RunnerIgnition.Ignitions(g, row.Pc, k, minuteFloor, IgnitionDollarFloor).FirstOrDefault();
                        if (hit.Close <= 0) continue;
                        if (RunnerIgnition.MarketEntry(g, hit.Minute) is not { } en || en.Minute >= LastEntryMinute || en.Fill <= 0) continue;
                        (entryOffset, minute, fill) = (off, en.Minute, en.Fill);
                        break;
                    }
                    if (entryOffset < 0) continue;
                }
                var entryRow = ev.At(entryOffset)!;
                if (!dayIndex.TryGetValue(entryRow.Day, out var di)) continue;
                var sim = Trade(ev, entryOffset, minute, fill);
                if (sim is null) continue;
                entryCounts[e]++;
                trades[e].Add((new WlRow { DayIdx = di, Weight = 1, Fold = dayFold[di] }, sim, ev));
            }
        }
        s.Logger.LogInformation("[{Now}] explore split-days: trades per entry {Counts}", GapperStudyServices.Et(s.UtcNow), string.Join("/", entryCounts));

        var configs = new List<(int E, int X)>();
        for (int e = 0; e < SplitEntryNames.Length; e++) for (int x = 0; x < exits.Count; x++) configs.Add((e, x));
        var stats = new WlStats[configs.Count];
        Parallel.For(0, configs.Count, ci =>
        {
            var (e, x) = configs[ci];
            stats[ci] = TradeStats(trades[e].Where(z => !float.IsNaN(z.Sim.Gross[x])).Select(z => (z.Row, z.Sim)).ToList(), x, trainDays.Count, dayFold, dayMonth, months.Count, folds.Count);
        });

        return SplitDaysReport(events, recordedInTrain, folds, trainDays, exits, configs, stats, trades, entryCounts, items, multiplier.Value, source, notional);
    }

    // ── report ────────────────────────────────────────────────────────────

    private (List<string>, int, string?) SplitDaysReport(List<SdEvent> events, int recordedInTrain, List<ExploreFold> folds, List<DateOnly> trainDays, List<SdExit> exits, List<(int E, int X)> configs,
        WlStats[] stats, List<(WlRow Row, WlSim Sim, SdEvent Ev)>[] trades, int[] entryCounts, Dictionary<string, List<(DateTime Utc, int Bits, string Text)>> items,
        double multiplier, string source, double notional)
    {
        var p = s.Params;
        var md = new StringBuilder(Header("split days (IP-T490): reverse splits, ADS ratio changes and unexplained moves, D-1..D+10 on minute bars", folds));
        var json = new Dictionary<string, object?>();
        var trials = new List<ExploreTrial>();
        string Pct(double? v) => DeepPct(v);
        string Sh(double? v) => DeepShare(v);
        string Num(double? v, string fmt = "0.0") => v is { } x && double.IsFinite(x) ? x.ToString(fmt, CultureInfo.InvariantCulture) : "n/a";
        string Clk(double? minute) => minute is { } m && double.IsFinite(m) ? Clock(240 + m) : "n/a";
        double? Med(IEnumerable<double> v) => MedianOf(v.Where(double.IsFinite).ToList());

        // ── 0. the event set and coverage ──
        md.AppendLine("Events: every reverse split recorded in GapperStudySplits (Alpaca corporate actions; price factor old/new > 1), every inferred ratio change (IP-T484's rule: no split on record and the whole day trades >= 3x above the prior close on < 3x the 20-day dollar volume; ratio = open / prior close; this catches unrecorded ADS ratio changes) and every close-to-close move above +200% that no recorded or inferred basis change explains, on a day the universe excluded; effective day D (the first trading day on or after the ex-date) in train 2021-11-01..2024-10-31. Each event covers the trading days D-1..D+10 that are in train, where the symbol has a daily bar and the split-adjusted prior close is $0.10-$20. Every price is put on one basis with every recorded split and inferred change (prices before a change times its ratio). The raw (as-traded) 04:00-20:00 minute bars of those symbol-days were pulled into the study store (`explore split-days-pull`, the frozen pull path, resumable).");
        md.AppendLine();
        var rec = events.Where(e => e.Class == SdClass.Recorded).ToList();
        md.AppendLine("| class | events | events with >= 1 covered day | symbol-days covered (D-1..D+10, $0.10-$20) | with minute bars | coverage |");
        md.AppendLine("|---|---|---|---|---|---|");
        var covJson = new List<object>();
        foreach (var c in Enum.GetValues<SdClass>())
        {
            var evs = events.Where(e => e.Class == c).ToList();
            int days = evs.Sum(e => e.Days.Count(d => d is { InBand: true })), withBars = evs.Sum(e => e.Days.Count(d => d is { InBand: true, Bars: > 0 }));
            md.AppendLine(string.Create(CultureInfo.InvariantCulture, $"| {SdClassNames[(int)c]} | {evs.Count:N0} | {evs.Count(e => e.Days.Any(d => d is { InBand: true })):N0} | {days:N0} | {withBars:N0} | {(days == 0 ? "n/a" : $"{100.0 * withBars / days:0.0}%")} |"));
            covJson.Add(new { cls = c.ToString(), events = evs.Count, covered = evs.Count(e => e.Days.Any(d => d is { InBand: true })), days, withBars });
        }
        int allDays = events.Sum(e => e.Days.Count(d => d is { InBand: true })), allBars = events.Sum(e => e.Days.Count(d => d is { InBand: true, Bars: > 0 }));
        md.AppendLine(string.Create(CultureInfo.InvariantCulture, $"| all | {events.Count:N0} | {events.Count(e => e.Days.Any(d => d is { InBand: true })):N0} | {allDays:N0} | {allBars:N0} | {(allDays == 0 ? "n/a" : $"{100.0 * allBars / allDays:0.0}%")} |"));
        md.AppendLine();
        int outBand = events.Sum(e => e.Days.Count(d => d is { InBand: false })), outBandBars = events.Sum(e => e.Days.Count(d => d is { InBand: false, Bars: > 0 }));
        md.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"Recorded reverse splits with an ex-date in train in GapperStudySplits: {recordedInTrain:N0}; {rec.Count:N0} became events (the symbol has study daily bars around D). Also pulled, for exits only: {outBand:N0} event days whose split-adjusted prior close is outside $0.10-$20 ({outBandBars:N0} with bars). A day with no daily bar for the symbol (halted, not yet listed) is not covered. Basis guard: {events.Sum(e => e.Days.Count(d => d is { InBand: true, BasisMismatch: true })):N0} covered split / ratio-change days ({events.Where(e => e.Days.Any(d => d is { InBand: true, BasisMismatch: true })).Select(e => e.Sym.Symbol).Distinct().Count():N0} symbols) open >= 3x above or <= 1/3 of the split-adjusted prior close, i.e. the recorded ex-date and the as-traded price disagree (mostly an ex-date off by a session); they are left out of every table and entry, and an overnight exit onto one is not simulated. Single bad prints (a lone 04:00-05:00 spike) are not filtered."));
        md.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"Recorded reverse splits: {rec.Count(e => e.Announced):N0} of {rec.Count:N0} ({(double)rec.Count(e => e.Announced) / Math.Max(1, rec.Count):P0}) have stored news (headline or pre-class names a reverse split / share consolidation / ADS ratio change) or an 8-K with item 3.03 or 5.03 published in the {AnnouncementLookbackDays} days before D 04:00 ET (median lead {Num(Med(rec.Where(e => e.Announced).Select(e => (double)(e.D.DayNumber - e.AnnouncedOn!.Value.DayNumber))), "0")} calendar days); the rest have none in the study's stores, which cover a fraction of microcap news. Every recorded split is calendared regardless: Nasdaq Rule 5250(b)(4) requires public disclosure at least 2 business days before the market effective date and Rule 5250(e)(7) notice to Nasdaq before that, so the calendar entry below trades all of them and splits the result by this flag. Shares outstanding (SEC XBRL cover-page count filed before D, put on D's basis): known for {rec.Count(e => e.Shares is not null):N0}; median {Num(Med(rec.Where(e => e.Shares is not null).Select(e => e.Shares!.Value / 1e6)), "0.0")}M. Promotion profile (foreign proxy: ADR/ADS or a Ltd/Limited/plc/N.V./S.A./AG/SE name; first daily bar after 2021-09-15 and within 730 days; shares < 10M): {rec.Count(e => e.Promo):N0}."));
        md.AppendLine();
        json["coverage"] = covJson;

        // ── 1. by day offset ──
        md.AppendLine("## A. Runs by day offset");
        md.AppendLine();
        md.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"Run = the 04:00-16:00 minute-bar high over the split-adjusted prior close, minus 1 (on D-1 both are pre-split, so the ratio cancels); time of high = the first bar printing it; fade = the last 04:00-16:00 close over the high; next 1 / 2 sessions = the split-adjusted daily close 1 and 2 sessions later over this day's close (train only). Lift against IP-T484's minute-bar population base ({PanelMinuteBase[0]:P3} / {PanelMinuteBase[1]:P3} / {PanelMinuteBase[2]:P3} at +50/+100/+300%; its daily-bar panel base for >= +100% is 0.039%). Symbol-days with minute bars only."));
        md.AppendLine();
        const string dh = "| days | symbol-days | >= +50% | >= +100% | >= +300% | lift >= +100% | median run | median time of high | median fade by close | median next 1 session | median next 2 sessions |";
        const string ds = "|---|---|---|---|---|---|---|---|---|---|---|";
        object DescJson(string label, List<SdDay> l)
        {
            double R(double lvl) => l.Count == 0 ? double.NaN : (double)l.Count(d => d.Facts!.Gain >= lvl) / l.Count;
            return new { label, n = l.Count, r50 = R(0.5), r100 = R(1.0), r300 = R(3.0), medianRun = Med(l.Select(d => d.Facts!.Gain)), medianHod = Med(l.Select(d => (double)d.Facts!.Hod)), medianFade = Med(l.Select(d => d.Facts!.Fade)), medianNext1 = Med(l.Select(d => d.Ret1)), medianNext2 = Med(l.Select(d => d.Ret2)) };
        }
        string Desc(string label, List<SdDay> l)
        {
            if (l.Count == 0) return $"| {label} | 0 | n/a | n/a | n/a | n/a | n/a | n/a | n/a | n/a | n/a |";
            double R(double lvl) => (double)l.Count(d => d.Facts!.Gain >= lvl) / l.Count;
            return string.Create(CultureInfo.InvariantCulture,
                $"| {label} | {l.Count:N0} | {R(0.5):P1} | {R(1.0):P1} | {R(3.0):P1} | {R(1.0) / PanelMinuteBase[1]:0}x | {Pct(Med(l.Select(d => d.Facts!.Gain)))} | {Clk(Med(l.Select(d => (double)d.Facts!.Hod)))} | {Pct(Med(l.Select(d => d.Facts!.Fade)))} | {Pct(Med(l.Select(d => d.Ret1)))} | {Pct(Med(l.Select(d => d.Ret2)))} |");
        }
        static string Off(int k) => k == 0 ? "D" : k < 0 ? $"D{k}" : $"D+{k}";
        List<SdDay> Rows(IEnumerable<SdEvent> evs, Func<int, bool> offset) => evs.SelectMany(e => e.Days).Where(d => d is { Usable: true } && offset(d.Offset)).Select(d => d!).ToList();
        var offsetJson = new List<object>();
        foreach (var (title, evs) in new (string, List<SdEvent>)[]
                 {
                     ("recorded reverse splits", rec),
                     ("inferred ratio changes (no split on record)", events.Where(e => e.Class == SdClass.Inferred).ToList()),
                     ("recorded + inferred", events.Where(e => e.Class != SdClass.Unexplained).ToList()),
                     ("unexplained > +200% close moves (D is selected by its own outcome: D's rates are true by construction)", events.Where(e => e.Class == SdClass.Unexplained).ToList()),
                 })
        {
            md.AppendLine($"**{title}** ({evs.Count:N0} events)");
            md.AppendLine();
            md.AppendLine(dh);
            md.AppendLine(ds);
            for (int k = SplitOffsetMin; k <= SplitOffsetMax; k++)
            {
                int kk = k;
                var l = Rows(evs, o => o == kk);
                md.AppendLine(Desc(Off(k), l));
                offsetJson.Add(new { group = title, offset = k, stats = DescJson(Off(k), l) });
            }
            md.AppendLine(Desc("D+1..D+10 pooled", Rows(evs, o => o >= 1)));
            md.AppendLine();
        }
        json["byOffset"] = offsetJson;

        // ── 2. splits ──
        md.AppendLine("## B. By ratio, advance announcement, float and promotion profile (recorded + inferred)");
        md.AppendLine();
        var rs = events.Where(e => e.Class != SdClass.Unexplained).ToList();
        (string Name, Func<int, bool> In)[] groups = [("D-1", o => o == -1), ("D", o => o == 0), ("D+1", o => o == 1), ("D+2..D+5", o => o is >= 2 and <= 5), ("D+6..D+10", o => o >= 6)];
        string RatioBucket(SdEvent e) => e.Ratio <= 5 ? "ratio <= 5:1" : e.Ratio <= 10 ? "ratio 5-10:1" : e.Ratio <= 25 ? "ratio 10-25:1" : "ratio > 25:1";
        string FloatBucket(SdEvent e) => e.Shares is not { } sh ? "shares unknown" : sh < 1e6 ? "shares < 1M" : sh < 3e6 ? "shares 1-3M" : sh < 10e6 ? "shares 3-10M" : "shares >= 10M";
        var dims = new (string Title, Func<SdEvent, string> Key, IEnumerable<SdEvent> Evs)[]
        {
            ("ratio (price factor)", RatioBucket, rs),
            ("advance announcement in the stores (recorded splits only)", e => e.Announced ? "news / 8-K before D" : "none found", rec),
            ("float proxy (shares outstanding on D's basis)", FloatBucket, rs),
            ("promotion profile", e => e.Promo ? "foreign + listed < 2 years + shares < 10M" : e.Foreign ? "foreign proxy, not the full profile" : "not foreign", rs),
            ("class", e => SdClassNames[(int)e.Class], rs),
        };
        var dimJson = new List<object>();
        foreach (var (title, key, evs) in dims)
        {
            md.AppendLine($"**{title}**");
            md.AppendLine();
            md.AppendLine(dh.Replace("| days |", "| bucket, days |"));
            md.AppendLine(ds);
            foreach (var b in evs.GroupBy(key).OrderBy(g => g.Key, StringComparer.Ordinal))
                foreach (var (gname, inG) in groups)
                {
                    var l = Rows(b, inG);
                    md.AppendLine(Desc($"{b.Key} ({b.Count():N0} events), {gname}", l));
                    dimJson.Add(new { dimension = title, bucket = b.Key, days = gname, stats = DescJson(gname, l) });
                }
            md.AppendLine();
        }
        json["byDimension"] = dimJson;

        // ── 3. trades ──
        int nTrials = Ledger.Count + configs.Count;
        var sharpes = stats.Where(st => st.Trades >= 2).Select(st => st.Moments.Sharpe).Where(double.IsFinite).ToList();
        double varSr = sharpes.Count > 1 ? Math.Pow(EvidenceStats.StdDev(sharpes), 2) : 0;
        double Dsr(WlStats st) => st.Trades >= 2 ? MultipleTesting.DeflatedSharpe(st.Moments, nTrials, varSr) : double.NaN;
        double dsrMin = p.Statistics.DsrMin;
        string Cfg(int ci) => $"{SplitEntryNames[configs[ci].E]} | {exits[configs[ci].X].Name}";
        foreach (var (c, ci) in configs.Select((c, i) => (c, i)))
        {
            var st = stats[ci];
            trials.Add(new ExploreTrial
            {
                Track = "split-days", Config = "long " + Cfg(ci), Metric = "meanNetReturn (long, frozen cost, all train days, pooled)", FoldValues = st.Folds[0], Pooled = st.Net[0]?.Mean, Rows = st.Trades,
                Note = string.Create(CultureInfo.InvariantCulture, $"lo={Pct(st.Net[0]?.Lo)} hi={Pct(st.Net[0]?.Hi)} days={st.Days} gross={Pct(st.Gross)} net0.5x={Pct(st.Net[1]?.Mean)} net0.25x={Pct(st.Net[2]?.Mean)} worst1={Pct(st.Worst1)} top1={Sh(st.Top1)} lomoLo={Pct(st.LomoLo[0])}"),
            });
        }
        md.AppendLine("## C. Trade test (long only)");
        md.AppendLine();
        md.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"Entries: (a) calendar, every recorded reverse split (no signal): the open of the first real bar 08:00-09:29 (premarket, marketable) or 09:30-10:00 (the 09:30 open) on D or on D+1; (b) ignition, every recorded split and inferred ratio change: IP-T484's `RunnerIgnition` rule (volume >= k x max(the trailing 30-minute mean, max({IgnitionMinShares:0} shares, ADV20 / {IgnitionAdvDivisor:0}) on D's basis), close >= 2% over the session high so far, >= ${IgnitionDollarFloor:N0} in the minute, no gap cap) with k = 5/10/20, first ignition on D (else on D+1), bought at the next real bar's open (within 30 min, before 15:45). Exits (`RunnerPath`, the IP-T484 fill rules over one or two sessions on the entry day's price basis): initial stop -8/-15% (filled at the worse of the stop and the bar open, an overnight gap through it at the next session's first open), trail 25% once +10% or 35% once +20% under the highest earlier high, optionally half at +30% (the rest trailed), velocity-death or VWAP-loss cooling once armed, out at 15:55 on the entry day or 15:55 the next session; plus a no-stop hold to 15:55. {configs.Count} configurations ({SplitEntryNames.Length} entries x {exits.Count} exits), each a ledger trial. Trades per entry: {string.Join(", ", SplitEntryNames.Select((n, i) => $"{n} {entryCounts[i]:N0}"))}."));
        md.AppendLine();
        md.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"Costs: the frozen round trip at ${notional:N0} (Corwin-Schultz over the pulled prior sessions in the window, D-1 for a D entry and D-1..D for a D+1 entry, pooled by bar pairs and needing >= 60 bars (the frozen D-1..D-5 were not pulled), x the premarket multiplier {multiplier:0.###} from {source}; square-root impact on the volume from 04:00 to entry; 20-session sigma from split-adjusted daily closes; floor {p.CostModel.RoundTripFloor:P1}) and with the spread at 0.5x / 0.25x. Pooled = every train day 2021-11..2024-10 (no model is fitted, so the first year counts); per fold = the four half-year eval windows; day-clustered 95% bounds by entry day. Break-even cost = the gross mean. Capacity: `ParticipationCap` at $1k / $5k (5% of the 5 minutes before entry and of the profile-forecast shares around the exit; an overnight exit is forecast on the entry day's profile). P&L concentration: the top 1% / 5% of trades' share of summed net. DSR: per-trade deflated Sharpe of the frozen-cost net series at N = {nTrials:N0} (ledger total after this run), variance of this run's trial Sharpes {varSr:0.0000}; frozen minimum {dsrMin:0.00}. Finalist bar (explore-plan.md): >= +1.0% net with lower bound > 0, >= 150 trades over >= 40 days, > 0 in at least 3 of 4 folds, leave-one-month-out lower bound > 0, plus DSR >= {dsrMin:0.00}."));
        md.AppendLine();
        const string head = "| entry | exit | trades | days | trades/day | gross = break-even | mean cost | net frozen [95%] | net 0.5x [95%] | net 0.25x [95%] | per fold (frozen) | per fold (0.25x) | worst 1% | top 1% / 5% P&L | >= +50% / >= +100% trades | cap $1k (full) | cap $5k (full) | LOMO lo frozen / 0.25x | DSR | finalist 1/0.5/0.25 |";
        const string sep = "|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|";
        string Row(int ci)
        {
            var (e, x) = configs[ci];
            var st = stats[ci];
            string N(int l) => st.Net[l] is { } m ? DeepBounds(m.Mean, m.Lo, m.Hi) : "n/a";
            bool dsrOk = Dsr(st) >= dsrMin;
            return string.Create(CultureInfo.InvariantCulture,
                $"| {SplitEntryNames[e]} | {exits[x].Name} | {st.Trades:N0} | {st.Days:N0} | {st.WeightedTrades / Math.Max(1, trainDays.Count):0.000} | {Pct(st.Gross)} | {Pct(st.MeanCost)} | {N(0)} | {N(1)} | {N(2)} | {string.Join(" / ", st.Folds[0].Select(Pct))} | {string.Join(" / ", st.Folds[2].Select(Pct))} | {Pct(st.Worst1)} | {Sh(st.Top1)} / {Sh(st.Top5)} | {Sh(st.Ge50)} / {Sh(st.Ge100)} | {Pct(st.Cap[0])} ({Sh(st.CapFull[0])}) | {Pct(st.Cap[1])} ({Sh(st.CapFull[1])}) | {Pct(st.LomoLo[0])} / {Pct(st.LomoLo[2])} | {Num(Dsr(st), "0.000")} | {string.Join("/", st.Finalist.Select(f => f && dsrOk ? "Y" : f ? "y" : "-"))} |");
        }
        foreach (var (title, l) in new[] { ("frozen cost", 0), ("0.5x spread", 1), ("0.25x spread", 2) })
        {
            md.AppendLine($"**Top 10 by net at {title} (>= 30 trades)**");
            md.AppendLine();
            md.AppendLine(head);
            md.AppendLine(sep);
            foreach (var ci in Enumerable.Range(0, configs.Count).Where(i => stats[i].Trades >= 30 && stats[i].Net[l] is not null).OrderByDescending(i => stats[i].Net[l]!.Value.Mean).Take(10)) md.AppendLine(Row(ci));
            md.AppendLine();
        }
        md.AppendLine("**Every entry: its best exit by net at 0.25x spread, and its no-stop hold**");
        md.AppendLine();
        md.AppendLine(head);
        md.AppendLine(sep);
        var bestPerEntry = new List<int>();
        for (int e = 0; e < SplitEntryNames.Length; e++)
        {
            int e0 = e;
            var cand = Enumerable.Range(0, configs.Count).Where(i => configs[i].E == e0 && stats[i].Trades >= 2 && stats[i].Net[2] is not null).ToList();
            if (cand.Count > 0) { int best = cand.OrderByDescending(i => stats[i].Net[2]!.Value.Mean).First(); bestPerEntry.Add(best); md.AppendLine(Row(best)); }
            int hold = configs.FindIndex(c => c == (e0, exits.Count - 1));
            if (hold >= 0) md.AppendLine(Row(hold));
        }
        md.AppendLine();

        // Breakdown of each entry's best configuration (descriptive): announced or not, and recorded vs inferred.
        md.AppendLine("Each entry's best configuration split by the advance-announcement flag and by class (descriptive, frozen cost and 0.25x; day-clustered):");
        md.AppendLine();
        md.AppendLine("| configuration | subset | trades | gross | net frozen [95%] | net 0.25x [95%] |");
        md.AppendLine("|---|---|---|---|---|---|");
        foreach (var b in bestPerEntry)
        {
            var (e, x) = configs[b];
            var l = trades[e].Where(z => !float.IsNaN(z.Sim.Gross[x])).ToList();
            foreach (var (name, keep) in new (string, Func<SdEvent, bool>)[]
                     {
                         ("recorded, news / 8-K before D", ev => ev.Class == SdClass.Recorded && ev.Announced),
                         ("recorded, none found", ev => ev.Class == SdClass.Recorded && !ev.Announced),
                         ("inferred ratio change", ev => ev.Class == SdClass.Inferred),
                         ("shares < 3M", ev => ev.Shares is < 3e6),
                         ("ratio > 10:1", ev => ev.Ratio > 10),
                     })
            {
                var sub = l.Where(z => keep(z.Ev)).ToList();
                if (sub.Count == 0) continue;
                string Part(int lv) => ExploreDerived.ClusteredMean(sub.Select(z => (trainDays[z.Row.DayIdx], (double)(z.Sim.Gross[x] - z.Sim.Cost[lv]), 1.0))) is { } m ? DeepBounds(m.Mean, m.Lo, m.Hi) : "n/a";
                md.AppendLine($"| {Cfg(b)} | {name} | {sub.Count:N0} | {Pct(sub.Average(z => (double)z.Sim.Gross[x]))} | {Part(0)} | {Part(2)} |");
            }
        }
        md.AppendLine();
        for (int l = 0; l < 3; l++)
        {
            var fin = Enumerable.Range(0, configs.Count).Where(i => stats[i].Finalist[l]).ToList();
            md.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"- Finalist bar at {(l == 0 ? "the frozen cost" : $"{WatchSpreadScales[l]:0.##}x spread")}: {fin.Count} configuration(s) before DSR, {fin.Count(i => Dsr(stats[i]) >= dsrMin)} after{(fin.Count == 0 ? "" : ": " + string.Join("; ", fin.OrderByDescending(i => stats[i].Net[l]!.Value.Mean).Take(5).Select(i => $"{Cfg(i)} {Pct(stats[i].Net[l]!.Value.Mean)}, DSR {Dsr(stats[i]):0.000}")))}."));
        }
        int positiveGross = Enumerable.Range(0, configs.Count).Count(i => stats[i].Gross > 0);
        md.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"- {positiveGross} of {configs.Count} configurations are positive before cost; {Enumerable.Range(0, configs.Count).Count(i => stats[i].Net[0]?.Mean > 0)} / {Enumerable.Range(0, configs.Count).Count(i => stats[i].Net[1]?.Mean > 0)} / {Enumerable.Range(0, configs.Count).Count(i => stats[i].Net[2]?.Mean > 0)} are positive net at 1.0 / 0.5 / 0.25x spread."));
        md.AppendLine();

        // ── 4. examples ──
        md.AppendLine("## D. The 10 biggest train split-day runners (recorded + inferred, D-1..D+10, one day per event)");
        md.AppendLine();
        md.AppendLine("Path: 04:00 first-bar gap / premarket high / the 04:00-16:00 high (time) / close vs high / next session's close vs this close; all over the split-adjusted prior close. Catalyst: the advance announcement found (if any) and up to three stored items (news headlines, 8-Ks) from 10 days before D to the runner day's 16:00.");
        md.AppendLine();
        md.AppendLine("| symbol | name | class, ratio | D | runner day | run | 04:00 gap | premarket high | high at | close vs high | next session | shares (D basis) | promotion | announced | items |");
        md.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        var exJson = new List<object>();
        var top = rs.Select(e => (Ev: e, Day: e.Days.Where(d => d is { Usable: true } && double.IsFinite(d.Facts!.Gain)).OrderByDescending(d => d!.Facts!.Gain).FirstOrDefault()))
            .Where(x => x.Day is not null).OrderByDescending(x => x.Day!.Facts!.Gain).Take(10).ToList();
        foreach (var (ev, d) in top)
        {
            var f = d!.Facts!;
            var from = MinuteBarMath.ToUtc(ev.D.AddDays(-10), TimeSpan.Zero);
            var to = MinuteBarMath.ToUtc(d.Day, new TimeSpan(16, 0, 0));
            var its = (items.GetValueOrDefault(ev.Sym.Symbol) ?? []).Where(i => i.Utc >= from && i.Utc <= to).ToList();
            var shown = its.OrderByDescending(i => i.Bits != 0).ThenBy(i => i.Utc).Take(3).OrderBy(i => i.Utc)
                .Select(i => $"{DateOnly.FromDateTime(EasternTime.FromUtc(i.Utc)):MM-dd}: {Clean(i.Text)}").ToList();
            string cls = ev.Class == SdClass.Recorded ? "recorded" : "inferred";
            md.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"| {ev.Sym.Symbol} | {Clean(ev.Name)} | {cls} 1:{ev.Ratio:0.##} | {ev.D:yyyy-MM-dd} | {Off(d.Offset)} {d.Day:yyyy-MM-dd} | {Pct(f.Gain)} | {Pct(f.FirstGap)} | {Pct(f.PmGain)} | {Clk(f.Hod)} | {Pct(f.Fade)} | {Pct(d.Ret1)} | {(ev.Shares is { } sh ? $"{sh / 1e6:0.00}M" : "n/a")} | {(ev.Promo ? "profile" : ev.Foreign ? "foreign" : "-")}{(ev.Holding ? ", holding-co name" : "")} | {(ev.AnnouncedOn is { } a ? $"{a:yyyy-MM-dd} ({Clean(ev.AnnouncedBy ?? "")})" : "none found")} | {(shown.Count == 0 ? "none stored" : string.Join("; ", shown))} ({its.Count} items) |"));
            exJson.Add(new { symbol = ev.Sym.Symbol, name = ev.Name, cls, ratio = ev.Ratio, d = ev.D, offset = d.Offset, day = d.Day, run = f.Gain, firstGap = f.FirstGap, pmGain = f.PmGain, hod = Clk(f.Hod), fade = f.Fade, next1 = d.Ret1, shares = ev.Shares, promo = ev.Promo, announced = ev.AnnouncedOn, items = shown });
        }
        md.AppendLine();
        md.AppendLine("Exploration only (train). No validation or holdout row or bar is read: daily bars stop at 2024-10-31 and every event day after it is dropped. Inferred ratio changes are not known in advance, so they get no calendar entry. The unexplained-move class is descriptive only.");
        json["examples"] = exJson;
        json["configs"] = Enumerable.Range(0, configs.Count).Select(i => new
        {
            entry = SplitEntryNames[configs[i].E], exit = exits[configs[i].X].Name, stats[i].Trades, stats[i].Days, gross = stats[i].Gross, meanCost = stats[i].MeanCost,
            net = stats[i].Net.Select(m => m is { } v ? new { v.Mean, v.Lo, v.Hi } : null).ToList(), folds = stats[i].Folds, worst1 = stats[i].Worst1, top1 = stats[i].Top1, top5 = stats[i].Top5, ge50 = stats[i].Ge50, ge100 = stats[i].Ge100,
            capacity = stats[i].Cap, capacityFull = stats[i].CapFull, lomoLo = stats[i].LomoLo, sharpe = stats[i].Moments.Sharpe, dsr = Dsr(stats[i]), finalist = stats[i].Finalist,
        });
        json["dsr"] = new { nTrials, varianceOfTrialSharpes = varSr, dsrMin };
        json["entryCounts"] = SplitEntryNames.Select((n, i) => new { entry = n, trades = entryCounts[i] });
        return Write("split-days", md, trials, json);
    }

    private static string Clean(string text)
    {
        var t = text.Replace('|', '/').Replace('\n', ' ').Replace('\r', ' ').Trim();
        return t.Length > 90 ? t[..90] + "..." : t;
    }
}
