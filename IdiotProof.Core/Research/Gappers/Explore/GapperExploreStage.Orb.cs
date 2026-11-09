using System.Globalization;
using System.Text;
using System.Text.Json;
using IdiotProof.Core.Evidence;
using IdiotProof.DataFeeds;
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
/// <c>explore orb-pull</c> and <c>explore orb</c> (IP-T492), TRAIN only: the 5-minute opening-range breakout on "stocks in play"
/// (Zarattini, Barbon and Aziz 2024; methods-daytrader-evidence.md), the one published net-positive intraday result. Universe each
/// day: the train universe rows (not excluded) with prior close $5-$20 (and a $1-$5 variant), 14-day ATR >= $0.50 and 20-day
/// average volume >= 1M shares. Stocks in play: the top 10/20/50 by relative volume in the first five minutes (the 09:30-09:35
/// volume over its mean on the prior 14 sessions), at least 1.0. The pull fetches the 09:30 five-minute bar for the whole universe
/// (one multi-symbol request per 100 names and day) and then the selected names' minute bars through the frozen pull path; both
/// resumable and paced by the study's Alpaca gate. The analysis trades the 09:30-09:35 candle's side at the 09:35 open with a stop
/// x ATR14 away, out 15:55, net of a regular-hours cost (Corwin-Schultz on the day's regular-hours minute bars, no premarket
/// multiplier, plus $0.0035/share each side), beside the frozen model; and the same rule on the IP-T484 D-1 watchlist top 50 and
/// on random universe names. One ledger trial per configuration.
/// </summary>
public sealed partial class GapperExploreStage
{
    public static readonly (string Name, double Lo, double Hi)[] OrbBands = [("$5-$20", 5, 20), ("$1-$5", 1, 5)];
    public static readonly int[] OrbTopN = [10, 20, 50];
    public static readonly double[] OrbStopAtr = [0.05, 0.10, 0.20];
    public const double OrbMinAtr = 0.50, OrbMinAdvShares = 1_000_000, OrbMinRelVol = 1.0;
    public const int OrbRvSessions = 14, OrbRvMinSessions = 10, OrbRandomPerDay = 20, OrbWatchlistTop = 50;
    /// <summary>Open-bar days fetched at once (each multi-symbol request takes seconds; the gate still spaces request starts).</summary>
    public const int OrbPullParallel = 6;
    /// <summary>Commission per share per side (the paper's $0.0035).</summary>
    public const double OrbCommissionPerShare = 0.0035;
    private static readonly string[] OrbSides = ["long only", "short only", "long + short"];
    private static readonly string[] OrbCostNames = ["regular-hours (Corwin-Schultz RTH + commission)", "frozen model", "commission only"];

    private sealed class OrbDailySeries
    {
        public DateOnly[] D = [];
        public double[] O = [], H = [], L = [], C = [];
        public int Index(DateOnly day) => Array.BinarySearch(D, day);
    }

    private sealed record OrbName(string Symbol, double Pc, double Atr, double Rv, double? Sigma, int Decile);

    private sealed class OrbSelection
    {
        public List<DateOnly> TrainDays = [];
        /// <summary>[band][day] eligible names (ATR and ADV filters, a relative volume >= 1), highest relative volume first.</summary>
        public Dictionary<DateOnly, List<OrbName>>[] InPlay = [];
        /// <summary>Every eligible $5-$20 name of the day (any relative volume) for the random draw, and its draw.</summary>
        public Dictionary<DateOnly, List<OrbName>> Random = [];
        public Dictionary<string, OrbDailySeries> Daily = new(StringComparer.Ordinal);
        public int[][] UniverseSize = [];
        public int Open5Days, Open5Missing;
    }

    private string OrbDir => Path.Combine(CacheDir(), "orb");
    private string Open5File(DateOnly d) => Path.Combine(OrbDir, "open5", d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".csv");

    /// <summary>The universe rows the ORB reads: train, not excluded, prior close $1-$20, 20-day average volume >= 1M shares.</summary>
    private async Task<Dictionary<DateOnly, List<(string Symbol, double Pc, int Decile)>>> OrbUniverseAsync(CancellationToken ct)
    {
        var map = new Dictionary<DateOnly, List<(string, double, int)>>();
        await using var db = await s.Db.CreateDbContextAsync(ct);
        decimal lo = (decimal)OrbBands.Min(b => b.Lo), hi = (decimal)OrbBands.Max(b => b.Hi), adv = (decimal)OrbMinAdvShares;
        var rows = await db.GapperStudyUniverseDays.AsNoTracking()
            .Where(u => u.Split == "train" && u.ExcludedReason == null && u.AdjPrevClose >= lo && u.AdjPrevClose < hi && u.Adv20Shares >= adv)
            .Select(u => new { u.DateEt, u.Symbol, u.AdjPrevClose, u.AdvDecile }).ToListAsync(ct);
        foreach (var r in rows)
        {
            if (!map.TryGetValue(r.DateEt, out var l)) map[r.DateEt] = l = [];
            l.Add((r.Symbol, (double)r.AdjPrevClose, r.AdvDecile));
        }
        return map;
    }

    private async Task<Dictionary<string, OrbDailySeries>> OrbDailyAsync(IEnumerable<string> symbols, DateOnly to, CancellationToken ct)
    {
        var list = symbols.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var result = new Dictionary<string, OrbDailySeries>(StringComparer.Ordinal);
        var from = s.Params.DataTiers.DailyBars.From;
        for (int i = 0; i < list.Count; i += 400)
        {
            var batch = list.Skip(i).Take(400).ToList();
            await using var db = await s.Db.CreateDbContextAsync(ct);
            // Daily bars stop at the last train day: nothing after train is read.
            var raw = await db.GapperStudyDailyBars.AsNoTracking().Where(b => batch.Contains(b.Symbol) && b.DateEt >= from && b.DateEt <= to)
                .Select(b => new { b.Symbol, b.DateEt, b.Open, b.High, b.Low, b.Close }).ToListAsync(ct);
            foreach (var g in raw.GroupBy(b => b.Symbol))
            {
                var o = g.OrderBy(b => b.DateEt).ToList();
                result[g.Key] = new OrbDailySeries
                {
                    D = o.Select(b => b.DateEt).ToArray(), O = o.Select(b => (double)b.Open).ToArray(), H = o.Select(b => (double)b.High).ToArray(),
                    L = o.Select(b => (double)b.Low).ToArray(), C = o.Select(b => (double)b.Close).ToArray(),
                };
            }
        }
        return result;
    }

    private static double? OrbSigma(OrbDailySeries d, int j)
    {
        var r = new List<double>();
        for (int i = Math.Max(1, j - 20); i < j; i++)
            if (d.C[i - 1] > 0 && d.C[i] > 0) r.Add(Math.Log(d.C[i] / d.C[i - 1]));
        return r.Count >= 3 ? EvidenceStats.StdDev(r) : null;
    }

    /// <summary>The 09:30-09:35 five-minute bar per requested symbol of one day file; a requested symbol with no bar has volume 0 and no price.</summary>
    private Dictionary<string, (double O, double C, double V)>? ReadOpen5(DateOnly d)
    {
        var f = Open5File(d);
        if (!File.Exists(f)) return null;
        var map = new Dictionary<string, (double, double, double)>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines(f))
        {
            var p = line.Split(',');
            if (p.Length < 4) continue;
            double P(string x) => double.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : double.NaN;
            map[p[0]] = (P(p[1]), P(p[2]), P(p[3]));
        }
        return map;
    }

    /// <summary>History days (the 14 sessions before train) plus every train day, and per day the symbols whose relative volume needs it.</summary>
    private static List<(DateOnly Day, HashSet<string> Symbols)> Open5Plan(Dictionary<DateOnly, List<(string Symbol, double Pc, int Decile)>> universe, List<DateOnly> trainDays)
    {
        var all = MarketCalendar.PriorTradingDays(trainDays[0], OrbRvSessions).OrderBy(d => d).Concat(trainDays).ToList();
        var sets = all.Select(_ => new HashSet<string>(StringComparer.Ordinal)).ToArray();
        int offset = all.Count - trainDays.Count;
        for (int k = 0; k < trainDays.Count; k++)
        {
            if (!universe.TryGetValue(trainDays[k], out var l)) continue;
            int j = offset + k;
            for (int i = Math.Max(0, j - OrbRvSessions); i <= j; i++)
                foreach (var u in l) sets[i].Add(u.Symbol);
        }
        return all.Select((d, i) => (d, sets[i])).Where(x => x.Item2.Count > 0).ToList();
    }

    private async Task<OrbSelection> OrbSelectAsync(CancellationToken ct)
    {
        var train = s.Params.Split("train");
        var trainDays = MarketCalendar.TradingDays(train.From, train.To);
        var universe = await OrbUniverseAsync(ct);
        var daily = await OrbDailyAsync(universe.Values.SelectMany(l => l.Select(u => u.Symbol)), train.To, ct);
        var plan = Open5Plan(universe, trainDays);
        var open5 = new Dictionary<DateOnly, Dictionary<string, (double O, double C, double V)>>();
        int missing = 0;
        foreach (var (d, _) in plan)
        {
            var m = ReadOpen5(d);
            if (m is null) { missing++; continue; }
            open5[d] = m;
        }
        var allDays = plan.Select(x => x.Day).ToList();
        var pos = allDays.Select((d, i) => (d, i)).ToDictionary(x => x.d, x => x.i);
        var sel = new OrbSelection
        {
            TrainDays = trainDays, Daily = daily, InPlay = OrbBands.Select(_ => new Dictionary<DateOnly, List<OrbName>>()).ToArray(),
            UniverseSize = OrbBands.Select(_ => new int[trainDays.Count]).ToArray(), Open5Days = open5.Count, Open5Missing = missing,
        };
        for (int k = 0; k < trainDays.Count; k++)
        {
            var day = trainDays[k];
            for (int b = 0; b < OrbBands.Length; b++) sel.InPlay[b][day] = [];
            sel.Random[day] = [];
            if (!universe.TryGetValue(day, out var rows) || !pos.TryGetValue(day, out var at) || !open5.TryGetValue(day, out var today)) continue;
            foreach (var (sym, pc, decile) in rows)
            {
                int band = Array.FindIndex(OrbBands, b => pc >= b.Lo && pc < b.Hi);
                if (band < 0 || !daily.TryGetValue(sym, out var ser)) continue;
                int j = ser.Index(day);
                if (j < 0) continue;
                if (OrbStopAtrFree(ser, j) is not { } atr || atr < OrbMinAtr) continue;
                sel.UniverseSize[band][k]++;
                double rv = double.NaN;
                if (today.TryGetValue(sym, out var bar) && bar.V > 0)
                {
                    double sum = 0; int n = 0;
                    for (int i = Math.Max(0, at - OrbRvSessions); i < at; i++)
                        if (open5.TryGetValue(allDays[i], out var h) && h.TryGetValue(sym, out var hb) && hb.V > 0) { sum += hb.V; n++; }
                    if (n >= OrbRvMinSessions) rv = bar.V / (sum / n);
                }
                var name = new OrbName(sym, pc, atr, rv, OrbSigma(ser, j), decile);
                if (double.IsFinite(rv) && rv >= OrbMinRelVol) sel.InPlay[band][day].Add(name);
                if (band == 0) sel.Random[day].Add(name);
            }
            for (int b = 0; b < OrbBands.Length; b++) sel.InPlay[b][day].Sort((x, y) => y.Rv.CompareTo(x.Rv));
            sel.Random[day] = sel.Random[day].OrderBy(n => StableHash($"{n.Symbol}|{day:yyyy-MM-dd}")).Take(OrbRandomPerDay).ToList();
        }
        return sel;
    }

    private static double? OrbStopAtrFree(OrbDailySeries ser, int j) => OpeningRangeBreakout.Atr(ser.H, ser.L, ser.C, j, 14);

    private static ulong StableHash(string text)
    {
        ulong h = 14695981039346656037UL;
        foreach (char c in text) { h ^= c; h *= 1099511628211UL; }
        return h;
    }

    // ── pull ─────────────────────────────────────────────────────────────

    /// <summary>
    /// `explore orb-pull`: (1) the 09:30 five-minute bar for every universe name on every train day and the 14 sessions before
    /// (raw, as traded; one multi-symbol request per 100 names and day; a day file is written once every chunk returned, so a
    /// rerun skips it); (2) the minute bars of the selected names (top 50 by relative volume in each price band, the 20 random
    /// $5-$20 names, and the hard-catalyst open-entry days of `explore catalyst-split`) through the frozen pull path (resumable).
    /// </summary>
    private async Task<(List<string>, int, string?)> OrbPullAsync(ExploreRequest req, CancellationToken ct)
    {
        var train = s.Params.Split("train");
        var trainDays = MarketCalendar.TradingDays(train.From, train.To);
        var universe = await OrbUniverseAsync(ct);
        // Only names that pass the ATR floor on a train day need their open bars (on that day and the 14 sessions before).
        var atrDaily = await OrbDailyAsync(universe.Values.SelectMany(l => l.Select(u => u.Symbol)), train.To, ct);
        universe = universe.ToDictionary(kv => kv.Key, kv => kv.Value.Where(u => atrDaily.TryGetValue(u.Symbol, out var ser) && ser.Index(kv.Key) is var j && j >= 0 && OrbStopAtrFree(ser, j) is >= OrbMinAtr).ToList());
        atrDaily.Clear();
        var plan = Open5Plan(universe, trainDays);
        Directory.CreateDirectory(Path.GetDirectoryName(Open5File(trainDays[0]))!);
        var keys = await s.ResolveKeysAsync(req.Owner, ct);
        await using var feed = s.Feed(keys);
        int calls = 0, written = 0, done = 0;
        var todo = plan.Where(x => !File.Exists(Open5File(x.Day))).ToList();
        s.Logger.LogInformation("[{Now}] explore orb-pull: {Todo} of {Days} open-bar days to fetch ({Symbols:N0} symbol-days)", GapperStudyServices.Et(s.UtcNow), todo.Count, plan.Count, todo.Sum(x => x.Symbols.Count));
        var clock = System.Diagnostics.Stopwatch.StartNew();
        // A multi-symbol request takes seconds to answer, so several days are in flight at once; the shared gate still spaces every request start.
        await Parallel.ForEachAsync(todo, new ParallelOptions { MaxDegreeOfParallelism = OrbPullParallel, CancellationToken = ct }, async (item, token) =>
        {
            var (day, symbols) = item;
            var start = MinuteBarMath.ToUtc(day, new TimeSpan(9, 30, 0));
            var end = MinuteBarMath.ToUtc(day, new TimeSpan(9, 34, 59));
            var sb = new StringBuilder();
            bool ok = true;
            foreach (var chunk in symbols.OrderBy(x => x, StringComparer.Ordinal).Chunk(AlpacaDataFeed.MaxSymbolsPerRequest))
            {
                MultiSymbolBars result;
                try
                {
                    result = await feed.GetHistoricalCandlesForSymbolsAsync(chunk, TimeSpan.FromMinutes(1), start, end, 10000, s.RateGate.WaitAsync, token);
                    Interlocked.Add(ref calls, result.Pages);
                }
                catch (HttpRequestException ex) when (ex.StatusCode is System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.UnprocessableEntity or System.Net.HttpStatusCode.NotFound)
                {
                    // One bad symbol rejects the chunk: retry symbol by symbol so the day is still complete.
                    s.Logger.LogWarning("orb-pull: {Day} chunk rejected ({Status}); retrying {Count} symbols one by one", day, ex.StatusCode, chunk.Length);
                    Interlocked.Increment(ref calls);
                    var merged = new Dictionary<string, List<Candle>>(StringComparer.OrdinalIgnoreCase);
                    foreach (var one in chunk)
                    {
                        try
                        {
                            var r1 = await feed.GetHistoricalCandlesForSymbolsAsync([one], TimeSpan.FromMinutes(1), start, end, 10000, s.RateGate.WaitAsync, token);
                            Interlocked.Add(ref calls, r1.Pages);
                            foreach (var kv in r1.Bars) merged[kv.Key] = kv.Value.ToList();
                        }
                        catch (HttpRequestException) { Interlocked.Increment(ref calls); }
                    }
                    foreach (var sym in chunk) Append(sym, merged.TryGetValue(sym, out var l) ? l : []);
                    continue;
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !token.IsCancellationRequested)
                {
                    s.Logger.LogWarning("orb-pull: {Day} failed ({Message}); the day is left for a rerun", day, ex.Message);
                    ok = false;
                    break;
                }
                foreach (var sym in chunk) Append(sym, result.Bars.TryGetValue(sym, out var list) ? list : []);
            }
            if (ok)
            {
                var file = Open5File(day);
                File.WriteAllText(file + ".tmp", sb.ToString());
                File.Move(file + ".tmp", file, true);
                Interlocked.Increment(ref written);
            }
            int now = Interlocked.Increment(ref done);
            if (now % 20 == 0 || now == todo.Count)
                s.Logger.LogInformation("[{Now}] explore orb-pull: open bars {Done}/{Total} days ({Day}), {Calls} requests, {Rate:0} requests/min", GapperStudyServices.Et(s.UtcNow), now, todo.Count, day, calls, calls / Math.Max(1e-9, clock.Elapsed.TotalMinutes));

            void Append(string sym, IReadOnlyList<Candle> bars)
            {
                // The five 09:30-09:34 minute bars rolled into one five-minute bar: first open, last close, summed volume.
                var five = bars.Where(x => x.Volume > 0 && x.Open > 0 && MinuteBarMath.ToEastern(x.StartUtc).TimeOfDay is var tod && tod >= new TimeSpan(9, 30, 0) && tod < new TimeSpan(9, 35, 0))
                    .OrderBy(x => x.StartUtc).ToList();
                sb.Append(sym).Append(',');
                if (five.Count == 0) { sb.Append(",,0\n"); return; }
                sb.Append(((double)five[0].Open).ToString("R", CultureInfo.InvariantCulture)).Append(',')
                  .Append(((double)five[^1].Close).ToString("R", CultureInfo.InvariantCulture)).Append(',')
                  .Append(five.Sum(x => (double)x.Volume).ToString("R", CultureInfo.InvariantCulture)).Append('\n');
            }
        });

        // ── minute bars of the selected names ──
        var sel = await OrbSelectAsync(ct);
        var store = await s.StoreAsync(ct);
        var wanted = new HashSet<(string Symbol, DateOnly DateEt)>();
        foreach (var day in sel.TrainDays)
        {
            for (int b = 0; b < OrbBands.Length; b++)
                foreach (var n in sel.InPlay[b][day].Take(OrbTopN.Max())) wanted.Add((n.Symbol, day));
            foreach (var n in sel.Random[day]) wanted.Add((n.Symbol, day));
        }
        int orbWanted = wanted.Count;
        foreach (var e in await HardCatalystEventsAsync(ct)) wanted.Add((e.Symbol, e.Day));
        int before = wanted.Count(w => store.HasDay(w.Symbol, w.DateEt));
        s.Logger.LogInformation("[{Now}] explore orb-pull: {Wanted} symbol-days wanted ({Orb} ORB), {Have} on file", GapperStudyServices.Et(s.UtcNow), wanted.Count, orbWanted, before);
        var (mcalls, mwritten) = await GapperPullStage.PullSymbolDaysAsync(s, feed, store, wanted, new CallBudget(0), ct);
        int withBars = wanted.Count(w => store.ReadDay(w.Symbol, w.DateEt).Count > 0);
        return ([
            $"open bars: {plan.Count:N0} days planned ({plan.Count - trainDays.Count:N0} history sessions before train), {written:N0} fetched now ({calls:N0} requests), {sel.Open5Missing:N0} still missing",
            $"minute bars: {wanted.Count:N0} symbol-days wanted ({orbWanted:N0} ORB selections and random draws, the rest hard-catalyst days), {before:N0} on file before, {mwritten:N0} written now ({mcalls:N0} requests); {withBars:N0} with at least one bar",
        ], 0, null);
    }

    // ── analysis ─────────────────────────────────────────────────────────

    /// <summary>One symbol-day the ORB rule trades: the range's side, the per-stop gross, the costs and the paper's sizing inputs.</summary>
    private sealed class OrbSim
    {
        public int Direction;
        public int EntryMinute;
        public double Entry, AtrPct;
        public readonly float[] Gross = new float[OrbStopAtr.Length];
        public readonly bool[] Stopped = new bool[OrbStopAtr.Length];
        public readonly float[] Cost = new float[3];
        public double Cs;
    }

    private async Task<(List<string>, int, string?)> OrbAsync(ExploreRequest req, CancellationToken ct)
    {
        var p = s.Params;
        var (multiplier, source) = GapperSpreadMultiplier.Resolve(s.Paths, p);
        if (multiplier is null) return ([$"cost model unavailable ({source}); run `calibrate-spread` first"], 0, null);
        var notional = GapperSpreadMultiplier.TradeNotional(p) ?? throw new InvalidDataException("params.json has no costModel.impact.tradeNotionalUsd.");
        double impact = GapperSpreadMultiplier.ImpactConstant(p), floor = p.CostModel.RoundTripFloor;
        var store = await s.StoreAsync(ct);
        var sel = await OrbSelectAsync(ct);
        var trainDays = sel.TrainDays;
        var trainTo = trainDays[^1];
        var dayIndex = trainDays.Select((d, i) => (d, i)).ToDictionary(x => x.d, x => x.i);
        var folds = ExploreFolds.Build(trainDays, foldMonths.Initial, foldMonths.Eval);
        var months = trainDays.Select(d => d.Year * 12 + d.Month).Distinct().OrderBy(m => m).ToList();
        var dayMonth = trainDays.Select(d => months.IndexOf(d.Year * 12 + d.Month)).ToArray();
        var dayFold = trainDays.Select(d => folds.FindIndex(f => f.InEval(d))).ToArray();

        // The IP-T484 watchlist top 50 (eval mornings only), from `explore catalyst-split`'s capture of the watchlist run.
        var wlFile = Path.Combine(CatalystDir, "watchlist.json");
        var watch = new Dictionary<DateOnly, List<string>>();
        if (File.Exists(wlFile))
        {
            var dump = JsonSerializer.Deserialize<CaptureDump>(await File.ReadAllTextAsync(wlFile, ct)) ?? new CaptureDump();
            foreach (var m in dump.Watchlist.Where(m => m.Rank < OrbWatchlistTop && m.Day <= trainTo))
            {
                if (!watch.TryGetValue(m.Day, out var l)) watch[m.Day] = l = [];
                l.Add(m.Symbol);
            }
        }
        var extraDaily = await OrbDailyAsync(watch.Values.SelectMany(l => l).Where(x => !sel.Daily.ContainsKey(x)), trainTo, ct);
        foreach (var (k, v) in extraDaily) sel.Daily[k] = v;

        // ── 1. every symbol-day any selection trades, simulated once ──
        var need = new Dictionary<(string Symbol, DateOnly Day), (double? Atr, double? Sigma, int Decile)>();
        foreach (var day in trainDays)
        {
            for (int b = 0; b < OrbBands.Length; b++)
                foreach (var n in sel.InPlay[b][day].Take(OrbTopN.Max())) need.TryAdd((n.Symbol, day), (n.Atr, n.Sigma, n.Decile));
            foreach (var n in sel.Random[day]) need.TryAdd((n.Symbol, day), (n.Atr, n.Sigma, n.Decile));
            if (watch.TryGetValue(day, out var wl))
                foreach (var sym in wl)
                {
                    if (need.ContainsKey((sym, day))) continue;
                    double? atr = null, sigma = null;
                    if (sel.Daily.TryGetValue(sym, out var ser) && ser.Index(day) is var j && j >= 0) { atr = OrbStopAtrFree(ser, j); sigma = OrbSigma(ser, j); }
                    need[(sym, day)] = (atr, sigma, 0);
                }
        }
        var keysList = need.Keys.ToList();
        var sims = new OrbSim?[keysList.Count];
        int done = 0, noBars = 0;
        Parallel.For(0, keysList.Count, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, req.Parallelism), CancellationToken = ct }, i =>
        {
            var (sym, day) = keysList[i];
            var info = need[keysList[i]];
            if (Interlocked.Increment(ref done) % 20_000 == 0) s.Logger.LogInformation("[{Now}] explore orb: simulated {Done}/{Total}", GapperStudyServices.Et(s.UtcNow), done, keysList.Count);
            if (info.Atr is not { } atr || !(atr > 0)) return;
            var bars = store.ReadDay(sym, day);
            var g = bars.Count == 0 ? null : RunnerGrid.Build(bars);
            if (g is null) { Interlocked.Increment(ref noBars); return; }
            if (OpeningRangeBreakout.Range(g) is not { Direction: not 0 } range) return;
            var sim = new OrbSim { Direction = range.Direction };
            for (int x = 0; x < OrbStopAtr.Length; x++)
            {
                if (OpeningRangeBreakout.Simulate(g, range.Direction, atr, OrbStopAtr[x]) is not { } tr) return;
                sim.Gross[x] = (float)tr.Gross;
                sim.Stopped[x] = tr.Stopped;
                sim.EntryMinute = tr.EntryMinute;
                sim.Entry = tr.Entry;
            }
            sim.AtrPct = atr / sim.Entry;
            // Regular-hours spread: Corwin-Schultz over the day's 09:30-15:55 minute bars (a cost estimate only; nothing selects on it).
            var rth = bars.Where(b => b.Volume > 0 && b.Low > 0 && RunnerGrid.MinuteOf(b.StartUtc) is >= RunnerGrid.RthOpen and < OpeningRangeBreakout.DefaultExitMinute).OrderBy(b => b.StartUtc).ToList();
            double cs = rth.Count >= 2 ? TradingCosts.CorwinSchultzSpread(rth) : double.NaN;
            sim.Cs = double.IsFinite(cs) ? cs : 0;
            double commission = 2 * OrbCommissionPerShare / sim.Entry;
            double pmCumVol = 0;
            for (int m = Math.Max(0, g.First); m < sim.EntryMinute; m++) pmCumVol += g.V[m];
            sim.Cost[0] = (float)(sim.Cs + commission);
            sim.Cost[1] = (float)GapperCostModel.Estimate(new GapperCostInputs(double.IsFinite(cs) ? cs : null, multiplier.Value, pmCumVol, info.Sigma, sim.Entry, notional, impact, floor)).RoundTrip;
            sim.Cost[2] = (float)commission;
            sims[i] = sim;
        });
        var simOf = new Dictionary<(string, DateOnly), OrbSim>();
        for (int i = 0; i < keysList.Count; i++) if (sims[i] is { } z) simOf[keysList[i]] = z;
        s.Logger.LogInformation("[{Now}] explore orb: {Sims} trades simulated of {Need} symbol-days ({NoBars} without minute bars)", GapperStudyServices.Et(s.UtcNow), simOf.Count, need.Count, noBars);

        // ── 2. configurations ──
        var configs = new List<OrbConfig>();
        for (int b = 0; b < OrbBands.Length; b++)
            foreach (var n in OrbTopN)
                for (int side = 0; side < OrbSides.Length; side++)
                    for (int x = 0; x < OrbStopAtr.Length; x++)
                    {
                        int bb = b, nn = n;
                        configs.Add(new OrbConfig("main", $"{OrbBands[b].Name} stocks in play top {n}", side, x, n, false, day => sel.InPlay[bb][day].Take(nn).Select(z => z.Symbol)));
                    }
        var evalOnly = trainDays.Where(d => dayFold[dayIndex[d]] >= 0).ToHashSet();
        foreach (var (name, pick) in new (string, Func<DateOnly, IEnumerable<string>>)[]
                 {
                     ("(a) $5-$20 stocks in play top 20", day => sel.InPlay[0][day].Take(20).Select(z => z.Symbol)),
                     ($"(b) D-1 watchlist top {OrbWatchlistTop} (IP-T484 boosted >= +100%)", day => watch.TryGetValue(day, out var l) ? l : []),
                     ($"(c) {OrbRandomPerDay} random $5-$20 universe names", day => sel.Random[day].Select(z => z.Symbol)),
                 })
            foreach (var side in new[] { 0, 2 })
                for (int x = 0; x < OrbStopAtr.Length; x++)
                    configs.Add(new OrbConfig("ablation", name, side, x, name.StartsWith("(b)", StringComparison.Ordinal) ? OrbWatchlistTop : 20, true, pick));

        var results = new OrbResult[configs.Count];
        Parallel.For(0, configs.Count, ci =>
        {
            var c = configs[ci];
            var trades = new List<(WlRow Row, WlSim Sim, OrbSim Orb, DateOnly Day)>();
            int selected = 0, untraded = 0;
            foreach (var day in trainDays)
            {
                if (c.EvalOnly && !evalOnly.Contains(day)) continue;
                int di = dayIndex[day];
                foreach (var sym in c.Pick(day))
                {
                    selected++;
                    if (!simOf.TryGetValue((sym, day), out var z)) { untraded++; continue; }
                    if (c.Side == 0 && z.Direction != 1) continue;
                    if (c.Side == 1 && z.Direction != -1) continue;
                    var ws = new WlSim { Minute = z.EntryMinute, Fill = (float)z.Entry, Gross = z.Gross, Cap1k = [float.NaN, float.NaN, float.NaN], Cap5k = [float.NaN, float.NaN, float.NaN], Full1k = new bool[3], Full5k = new bool[3] };
                    for (int l = 0; l < 3; l++) ws.Cost[l] = z.Cost[l];
                    trades.Add((new WlRow { DayIdx = di, Weight = 1, Fold = dayFold[di] }, ws, z, day));
                }
            }
            var st = TradeStats(trades.Select(t => (t.Row, t.Sim)).ToList(), c.StopIdx, trainDays.Count, dayFold, dayMonth, months.Count, folds.Count);
            var days = c.EvalOnly ? trainDays.Where(evalOnly.Contains).ToList() : trainDays;
            results[ci] = new OrbResult(st, Portfolio(trades, c, days, folds, paper: false, 0), Portfolio(trades, c, days, folds, paper: true, 0), Portfolio(trades, c, days, folds, paper: false, 1),
                selected, untraded, trades.Count(t => t.Orb.Stopped[c.StopIdx]) / (double)Math.Max(1, trades.Count), trades.Count == 0 ? double.NaN : trades.Average(t => (double)t.Orb.Cs), trades.Count == 0 ? double.NaN : trades.Average(t => (double)t.Sim.Cost[0]));
        });
        return OrbReport(sel, configs, results, folds, trainDays, evalOnly, watch, simOf.Count, need.Count, noBars, multiplier.Value, source, notional);
    }

    private sealed record OrbConfig(string Group, string Selection, int Side, int StopIdx, int SlotN, bool EvalOnly, Func<DateOnly, IEnumerable<string>> Pick)
    {
        public string Name => $"{Selection} | {OrbSides[Side]} | stop {OrbStopAtr[StopIdx]:0.00} ATR";
    }

    /// <summary>A daily portfolio: annualized Sharpe of the daily return, the annualized mean, the maximum drawdown of the compounded curve, per-fold Sharpe.</summary>
    private sealed record OrbPortfolio(double Sharpe, double AnnualReturn, double MaxDrawdown, double TradeDays, double[] FoldSharpe);

    private sealed record OrbResult(WlStats Stats, OrbPortfolio Equal, OrbPortfolio Paper, OrbPortfolio EqualFrozen, int Selected, int Untraded, double StopShare, double MeanCs, double MeanRthCost);

    /// <summary>
    /// Equal weight: each day's return is the mean net of its trades (cash on a day without one). Paper sizing (Zarattini et al.):
    /// each trade risks 1% of equity at its stop, capped at 4x equity spread over the N slots, so a trade's weight is
    /// min(1% / (x ATR / entry), 4 / N) and the day's return is the weighted sum.
    /// </summary>
    private OrbPortfolio Portfolio(List<(WlRow Row, WlSim Sim, OrbSim Orb, DateOnly Day)> trades, OrbConfig c, List<DateOnly> days, List<ExploreFold> folds, bool paper, int costLevel)
    {
        var byDay = trades.GroupBy(t => t.Day).ToDictionary(g => g.Key, g => g.ToList());
        var r = new double[days.Count];
        var foldOf = new int[days.Count];
        int traded = 0;
        for (int i = 0; i < days.Count; i++)
        {
            foldOf[i] = -1;
            if (!byDay.TryGetValue(days[i], out var l) || l.Count == 0) continue;
            traded++;
            double Net((WlRow Row, WlSim Sim, OrbSim Orb, DateOnly Day) t) => t.Sim.Gross[c.StopIdx] - t.Sim.Cost[costLevel];
            r[i] = paper
                ? l.Sum(t => Math.Min(0.01 / (OrbStopAtr[c.StopIdx] * t.Orb.AtrPct), 4.0 / c.SlotN) * Net(t))
                : l.Average(Net);
        }
        static double Sharpe(IReadOnlyList<double> v) => v.Count < 2 ? double.NaN : EvidenceStats.StdDev(v) is var sd && sd > 0 ? v.Average() / sd * Math.Sqrt(252) : double.NaN;
        double eq = 1, peak = 1, mdd = 0;
        foreach (var x in r) { eq *= 1 + x; peak = Math.Max(peak, eq); mdd = Math.Min(mdd, eq / peak - 1); }
        var fs =ExploreFolds.Build(days, foldMonths.Initial, foldMonths.Eval);
        var foldSharpe = folds.Select(f => Sharpe(days.Select((d, i) => (d, i)).Where(z => f.InEval(z.d)).Select(z => r[z.i]).ToList())).ToArray();
        return new OrbPortfolio(Sharpe(r), r.Length == 0 ? double.NaN : r.Average() * 252, mdd, traded == 0 ? 0 : (double)trades.Count / traded, foldSharpe);
    }

    // ── report ────────────────────────────────────────────────────────────

    private (List<string>, int, string?) OrbReport(OrbSelection sel, List<OrbConfig> configs, OrbResult[] results, List<ExploreFold> folds, List<DateOnly> trainDays, HashSet<DateOnly> evalOnly,
        Dictionary<DateOnly, List<string>> watch, int simulated, int needed, int noBars, double multiplier, string source, double notional)
    {
        var p = s.Params;
        var md = new StringBuilder(Header("opening-range breakout on stocks in play (IP-T492): the 5-minute ORB of Zarattini, Barbon and Aziz", folds));
        var trials = new List<ExploreTrial>();
        var json = new Dictionary<string, object?>();
        string Pct(double? v) => DeepPct(v);
        string Sh(double? v) => DeepShare(v);
        string Num(double? v, string fmt = "0.00") => v is { } x && double.IsFinite(x) ? x.ToString(fmt, CultureInfo.InvariantCulture) : "n/a";
        int nTrials = Ledger.Count + configs.Count;
        var sharpes = results.Where(r => r.Stats.Trades >= 2).Select(r => r.Stats.Moments.Sharpe).Where(double.IsFinite).ToList();
        double varSr = sharpes.Count > 1 ? Math.Pow(EvidenceStats.StdDev(sharpes), 2) : 0;
        double Dsr(WlStats st) => st.Trades >= 2 ? MultipleTesting.DeflatedSharpe(st.Moments, nTrials, varSr) : double.NaN;
        double dsrMin = p.Statistics.DsrMin;

        md.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"Universe each train day: the frozen universe rows (not excluded: no split window, no unexplained move) with prior close in the band, 14-day ATR (mean true range of the 14 daily bars before D, raw) >= ${OrbMinAtr:0.00} and 20-day average volume >= {OrbMinAdvShares / 1e6:0}M shares. Bands: $5-$20 (the paper's > $5 universe cut at the study's $20 ceiling) and $1-$5 (the same ATR and volume floors, so only the most volatile $1-$5 names qualify). Median names per day: {string.Join(", ", OrbBands.Select((b, i) => $"{b.Name} {MedianOf(sel.UniverseSize[i].Where(v => v > 0).Select(v => (double)v).ToList()):0}"))}."));
        md.AppendLine();
        md.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"Stocks in play: relative volume = the 09:30-09:35 volume (Alpaca SIP five-minute bar, raw) over its mean on the prior {OrbRvSessions} sessions where the name had that bar (at least {OrbRvMinSessions}); ranked per day, at least {OrbMinRelVol:0.0}x, top 10 / 20 / 50. The open bars were fetched for every universe name of the day and of the next {OrbRvSessions} sessions (so a name's history exists before it enters), {sel.Open5Days:N0} day files ({sel.Open5Missing:N0} missing). A name without a bar in a history session is not counted there (a zero for an untraded or not-yet-listed session would inflate its relative volume)."));
        md.AppendLine();
        md.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"Trade (`OpeningRangeBreakout`, on the name's minute bars): side = the 09:30-09:35 candle (long if it closed above its open, short if below, a doji is skipped); entry = the open of the first real bar from 09:35 (within 5 minutes); stop = entry -/+ x ATR14 for x = {string.Join(" / ", OrbStopAtr.Select(v => v.ToString("0.00", CultureInfo.InvariantCulture)))} (the paper used 0.10), filled at the worse of the stop and the bar open, the entry bar's own range counting; else out at the 15:55 bar's open. Long only takes the up-candle names; short only the down-candle names; long + short both. Minute bars: {simulated:N0} symbol-days traded of {needed:N0} selected ({noBars:N0} without minute bars)."));
        md.AppendLine();
        md.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"Costs per round trip: (1) regular hours = Corwin-Schultz over the day's own 09:30-15:55 minute bars (a cost estimate only, no premarket multiplier; nothing selects on it) plus ${OrbCommissionPerShare:0.0000}/share each side; (2) the frozen model at ${notional:N0} (that same spread x the premarket multiplier {multiplier:0.###} from {source}, square-root impact on the 04:00-to-entry volume, 20-session sigma, floor {p.CostModel.RoundTripFloor:P1}); (3) commission only. Pooled = every train day (no model is fitted); per fold = the four half-year eval windows; day-clustered 95% bounds. Portfolio: the daily equal-weight return of the day's trades (cash on a day without one), annualized Sharpe (x sqrt 252), maximum drawdown of the compounded curve; and the paper's sizing (risk 1% of equity at the stop, at most 4x equity over N slots). DSR: per-trade deflated Sharpe of the regular-hours net at N = {nTrials:N0} (ledger total after this run), variance of this run's trial Sharpes {varSr:0.0000}; frozen minimum {dsrMin:0.00}. Finalist bar (explore-plan.md): >= +1.0% net per trade with lower bound > 0, >= 150 trades over >= 40 days, > 0 in at least 3 of 4 folds, leave-one-month-out lower bound > 0, plus DSR >= {dsrMin:0.00} (a per-trade bar: the ORB's edge, if any, is a few basis points per trade over many trades, so the Sharpe columns matter more here)."));
        md.AppendLine();

        const string head = "| selection | side | stop | trades | days | trades/day | stopped | gross | RTH cost (CS) | net RTH [95%] | net frozen [95%] | net commission only | per fold (RTH) | LOMO lo (RTH) | Sharpe EW (RTH) | per fold Sharpe | ann. return EW | max DD EW | Sharpe EW (frozen) | Sharpe paper sizing | ann. return paper | max DD paper | DSR | finalist RTH/frozen/comm |";
        const string sep = "|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|";
        string Row(int ci)
        {
            var c = configs[ci];
            var r = results[ci];
            var st = r.Stats;
            string N(int l) => st.Net[l] is { } m ? DeepBounds(m.Mean, m.Lo, m.Hi) : "n/a";
            bool dsrOk = Dsr(st) >= dsrMin;
            double nDays = c.EvalOnly ? evalOnly.Count : trainDays.Count;
            return string.Create(CultureInfo.InvariantCulture,
                $"| {c.Selection} | {OrbSides[c.Side]} | {OrbStopAtr[c.StopIdx]:0.00} | {st.Trades:N0} | {st.Days:N0} | {st.Trades / nDays:0.0} | {Sh(r.StopShare)} | {Pct(st.Gross)} | {Pct(r.MeanCs)} | {N(0)} | {N(1)} | {Pct(st.Net[2]?.Mean)} | {string.Join(" / ", st.Folds[0].Select(Pct))} | {Pct(st.LomoLo[0])} | {Num(r.Equal.Sharpe)} | {string.Join(" / ", r.Equal.FoldSharpe.Select(v => Num(v)))} | {Pct(r.Equal.AnnualReturn)} | {Pct(r.Equal.MaxDrawdown)} | {Num(r.EqualFrozen.Sharpe)} | {Num(r.Paper.Sharpe)} | {Pct(r.Paper.AnnualReturn)} | {Pct(r.Paper.MaxDrawdown)} | {Num(Dsr(st), "0.000")} | {string.Join("/", st.Finalist.Select(f => f && dsrOk ? "Y" : f ? "y" : "-"))} |");
        }

        for (int ci = 0; ci < configs.Count; ci++)
        {
            var c = configs[ci];
            var st = results[ci].Stats;
            trials.Add(new ExploreTrial
            {
                Track = "orb", Config = $"{c.Group} {c.Name}", Metric = $"meanNetReturn ({OrbSides[c.Side]}, regular-hours cost, {(c.EvalOnly ? "eval windows" : "all train days")})",
                FoldValues = st.Folds[0], Pooled = st.Net[0]?.Mean, Rows = st.Trades,
                Note = string.Create(CultureInfo.InvariantCulture, $"lo={Pct(st.Net[0]?.Lo)} hi={Pct(st.Net[0]?.Hi)} days={st.Days} gross={Pct(st.Gross)} netFrozen={Pct(st.Net[1]?.Mean)} netComm={Pct(st.Net[2]?.Mean)} sharpeEW={Num(results[ci].Equal.Sharpe)} sharpePaper={Num(results[ci].Paper.Sharpe)} mddEW={Pct(results[ci].Equal.MaxDrawdown)} lomoLo={Pct(st.LomoLo[0])}"),
            });
        }

        foreach (var (b, band) in OrbBands.Select((x, i) => (i, x)))
        {
            md.AppendLine($"## {(b == 0 ? "A" : "B")}. {band.Name} stocks in play, every train day");
            md.AppendLine();
            foreach (var side in new[] { 0, 2, 1 })
            {
                md.AppendLine($"**{OrbSides[side]}{(side == 0 ? " (primary)" : side == 1 ? " (reference only)" : " (secondary reference)")}**");
                md.AppendLine();
                md.AppendLine(head);
                md.AppendLine(sep);
                for (int ci = 0; ci < configs.Count; ci++)
                    if (configs[ci].Group == "main" && configs[ci].Selection.StartsWith(band.Name, StringComparison.Ordinal) && configs[ci].Side == side) md.AppendLine(Row(ci));
                md.AppendLine();
            }
        }

        md.AppendLine("## C. Selection ablation (eval windows only: the watchlist is scored there)");
        md.AppendLine();
        md.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"The same ORB rule on (a) the $5-$20 top 20 stocks in play, (b) the IP-T484 D-1 16:00 watchlist top {OrbWatchlistTop} (the boosted >= +100% model fitted per fold, its eval mornings; re-derived by `explore catalyst-split`; {watch.Count:N0} days, ATR from the name's own daily bars, no price, ATR or volume filter) and (c) {OrbRandomPerDay} random $5-$20 universe names per day (fixed hash of symbol and day, any relative volume). {evalOnly.Count:N0} eval days."));
        md.AppendLine();
        md.AppendLine(head);
        md.AppendLine(sep);
        for (int ci = 0; ci < configs.Count; ci++) if (configs[ci].Group == "ablation") md.AppendLine(Row(ci));
        md.AppendLine();
        md.AppendLine("Selected names without a trade (no minute bars, no ATR, a doji, or no bar from 09:35 within 5 minutes), per selection: " + string.Join("; ", configs.Select((c, i) => (c, i)).Where(z => z.c.Side == 2 && z.c.StopIdx == 1).Select(z => $"{z.c.Selection} {results[z.i].Untraded:N0} of {results[z.i].Selected:N0}")) + ".");
        md.AppendLine();

        for (int l = 0; l < 3; l++)
        {
            var fin = Enumerable.Range(0, configs.Count).Where(i => results[i].Stats.Finalist[l]).ToList();
            md.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"- Finalist bar at the {OrbCostNames[l]} cost: {fin.Count} configuration(s) before DSR, {fin.Count(i => Dsr(results[i].Stats) >= dsrMin)} after{(fin.Count == 0 ? "" : ": " + string.Join("; ", fin.Take(5).Select(i => $"{configs[i].Name} {Pct(results[i].Stats.Net[l]!.Value.Mean)}, DSR {Dsr(results[i].Stats):0.000}")))}."));
        }
        int posLong = Enumerable.Range(0, configs.Count).Count(i => configs[i].Side == 0 && results[i].Stats.Net[0]?.Mean > 0);
        md.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"- Long-only configurations positive net of the regular-hours cost: {posLong} of {configs.Count(c => c.Side == 0)}; with a lower bound above 0: {Enumerable.Range(0, configs.Count).Count(i => configs[i].Side == 0 && results[i].Stats.Net[0]?.Lo > 0)}. Positive gross: {Enumerable.Range(0, configs.Count).Count(i => configs[i].Side == 0 && results[i].Stats.Gross > 0)}."));
        md.AppendLine();
        md.AppendLine("Exploration only (train 2021-11-01..2024-10-31; the open-bar history reaches 14 sessions before it). No validation or holdout bar, row or label is read: daily bars and universe rows stop at the last train day.");

        json["configs"] = Enumerable.Range(0, configs.Count).Select(i => new
        {
            group = configs[i].Group, selection = configs[i].Selection, side = OrbSides[configs[i].Side], stopAtr = OrbStopAtr[configs[i].StopIdx], evalOnly = configs[i].EvalOnly,
            results[i].Stats.Trades, results[i].Stats.Days, gross = results[i].Stats.Gross, meanCs = results[i].MeanCs, meanRthCost = results[i].MeanRthCost, stopShare = results[i].StopShare,
            net = results[i].Stats.Net.Select(m => m is { } v ? new { v.Mean, v.Lo, v.Hi } : null).ToList(), costLevels = OrbCostNames, folds = results[i].Stats.Folds, lomoLo = results[i].Stats.LomoLo,
            sharpeTrade = results[i].Stats.Moments.Sharpe, dsr = Dsr(results[i].Stats), finalist = results[i].Stats.Finalist,
            equal = results[i].Equal, paper = results[i].Paper, equalFrozen = results[i].EqualFrozen, selected = results[i].Selected, untraded = results[i].Untraded,
        });
        json["dsr"] = new { nTrials, varianceOfTrialSharpes = varSr, dsrMin };
        json["universeMedian"] = OrbBands.Select((b, i) => new { band = b.Name, median = MedianOf(sel.UniverseSize[i].Where(v => v > 0).Select(v => (double)v).ToList()) });
        return Write("orb", md, trials, json);
    }
}
