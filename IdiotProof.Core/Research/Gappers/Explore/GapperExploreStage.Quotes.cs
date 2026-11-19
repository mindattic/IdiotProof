using System.Globalization;
using System.Net;
using System.Text;
using IdiotProof.Evidence.Gappers;

namespace IdiotProof.Core.Research.Gappers.Explore;

public sealed partial class GapperExploreStage
{
    /// <summary>Quotes are pulled this far either side of each simulated fill (strategy-refined.md §3.2: ±60 s).</summary>
    public static readonly TimeSpan QuoteWindow = TimeSpan.FromSeconds(60);

    /// <summary>The trade set the quotes check runs on (see <see cref="QuotesPullAsync"/>).</summary>
    public const string QuoteTradeSet = "entries-0830-buy-now";

    private const string QuoteHeader = "symbol,date,leg,at_utc,weight,fill,charged_spread,charged_impact_side,charged_round_trip,n_quotes,rel_spread_at,rel_spread_median,bid_at,ask_at,bid_size_at,ask_size_at";

    public string QuoteDir() => Path.Combine(Path.GetDirectoryName(CacheDir().TrimEnd('\\', '/'))!, "quotes");

    private string QuoteFile() => Path.Combine(QuoteDir(), QuoteTradeSet + ".csv");

    private sealed record QuoteEvent(string Symbol, DateOnly Day, string Leg, DateTime AtUtc, double Weight, double Fill, double ChargedSpread, double ChargedImpactSide, double ChargedRoundTrip,
        int Quotes, double RelAt, double RelMedian, double BidAt, double AskAt, double BidSizeAt, double AskSizeAt);

    /// <summary>
    /// `explore quotes-pull` (IP-T472): NBBO quotes within ±60 s of every simulated entry and exit, to check the frozen
    /// cost model against what was actually quoted. No configuration cleared the explore finalist bar (every long rule
    /// lost after costs; amendment 04 registered detection only), so the trade set is the pool every entries finalist
    /// would have been drawn from: train mornings still below +20% at 08:30 passing the loosest entries detection
    /// (time-of-day volume ratio >= 3, v3 >= 0), bought now, sold by the entries track's shared exit. One row per leg with
    /// the cost model's charge beside the quotes' summary; resumable (rows already in the file are skipped), paced by
    /// the study's Alpaca gate and paused through premarket.
    /// </summary>
    private async Task<(List<string>, int, string?)> QuotesPullAsync(ExploreRequest req, CancellationToken ct)
    {
        var (multiplier, source) = GapperSpreadMultiplier.Resolve(s.Paths, s.Params);
        if (multiplier is null) return ([$"cost model unavailable ({source}); run `calibrate-spread` first"], 0, null);
        var p = s.Params;
        var notional = GapperSpreadMultiplier.TradeNotional(p) ?? throw new InvalidDataException("params.json has no costModel.impact.tradeNotionalUsd.");
        var exits = new GapperRuleExitParameters(p.Labels.VelocityDeath.ArmThresholdPctPerMin, p.Labels.VelocityDeath.ConsecutiveNegativeBars, p.Labels.Vwap.MinConsecutiveAboveBeforeLoss);
        const string decision = "08:30";
        var dt = DecisionTime.Parse(decision);
        var t = Load(decision);
        int gap = t.Feature("GapPct"), vr = t.Feature("VolRatioTod"), v3 = t.Feature("Velocity3m"),
            cs = t.Feature("x." + PriorDaysCorwinSchultzFeature.FeatureName), logVol = t.Feature("LogPmCumVol");
        float At(int r, int f) => f < 0 ? float.NaN : t.Value(r, f);
        var pool = Enumerable.Range(0, t.Rows).Where(r => t.Weights[r] > 0 && At(r, gap) is var g && !float.IsNaN(g) && g < FormedGapPct
            && At(r, vr) >= EntryVolRatioMins.Min() && At(r, v3) >= EntryV3Mins.Min())
            .Where(r => (req.From is not { } f || t.Days[r] >= f) && (req.To is not { } to || t.Days[r] <= to)).ToList();

        var file = QuoteFile();
        Directory.CreateDirectory(QuoteDir());
        var done = File.Exists(file)
            ? File.ReadLines(file).Skip(1).Select(l => l.Split(',')).Where(f => f.Length >= 3).Select(f => $"{f[0]}|{f[1]}|{f[2]}").ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
        if (!File.Exists(file)) await File.WriteAllTextAsync(file, QuoteHeader + "\n", ct);
        s.Logger.LogInformation("[{Now}] explore quotes-pull: {Pool:N0} mornings in the pool, {Done:N0} legs already on file", GapperStudyServices.Et(s.UtcNow), pool.Count, done.Count);

        var keys = await s.ResolveKeysAsync(req.Owner, ct);
        using var quotes = new AlpacaQuoteSource(keys, p.DataTiers.AlpacaFeed, s.RateGate);
        var panel = new GapperStudyPanel(s);
        var store = await s.StoreAsync(ct);
        int trades = 0, legs = 0, calls = 0, failed = 0;
        foreach (var r in pool)
        {
            ct.ThrowIfCancellationRequested();
            string symbol = t.Symbols[r]; var day = t.Days[r];
            if (done.Contains($"{symbol}|{day:yyyy-MM-dd}|entry") && done.Contains($"{symbol}|{day:yyyy-MM-dd}|exit")) continue;
            var bars = store.ReadDay(symbol, day);
            if (bars.Count == 0) continue;
            var entry = GapperEntrySimulator.Find(GapperEntryKind.Now, bars, day, MinuteBarMath.ToUtc(day, dt.TimeEt));
            if (entry is null || entry.Fill <= 0) continue;
            var sigma = GapperCostModel.Sigma(await panel.PriorClosesAsync(symbol, day, p.Features.Adv20Sessions, ct), p.Features.Adv20Sessions);
            double pmCumVol = At(r, logVol) is var lv && !float.IsNaN(lv) ? Math.Exp(lv) - 1 : 0;
            double? spread = At(r, cs) is var c && !float.IsNaN(c) ? c : null;
            var cost = GapperCostModel.Estimate(new GapperCostInputs(spread, multiplier.Value, pmCumVol, sigma, (double)entry.Fill, notional, GapperSpreadMultiplier.ImpactConstant(p), p.CostModel.RoundTripFloor));
            var trade = GapperRuleSimulator.Simulate(EntryExit, bars, day, entry.BarStartUtc, cost.RoundTrip, exits);
            if (trade is null) continue;
            trades++;
            var sb = new StringBuilder();
            foreach (var (leg, at, fill) in new[] { ("entry", entry.BarStartUtc, (double)entry.Fill), ("exit", trade.ExitUtc, (double)trade.Exit) })
            {
                if (done.Contains($"{symbol}|{day:yyyy-MM-dd}|{leg}")) continue;
                await WaitOutPremarketAsync("explore quotes-pull", ct);
                IReadOnlyList<QuoteTick> ticks;
                try
                {
                    calls++;
                    ticks = await quotes.GetQuotesAsync(symbol, at - QuoteWindow, at + QuoteWindow, 10000, ct);
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
                {
                    failed++;
                    if (ex is HttpRequestException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden }) throw;
                    s.Logger.LogWarning("explore quotes-pull: {Symbol} {Day} {Leg} failed ({Message}); left for a rerun", symbol, day, leg, ex.Message);
                    continue;
                }
                var valid = ticks.Where(q => q.Bid > 0 && q.Ask >= q.Bid).OrderBy(q => q.TimeUtc).ToList();
                var atQuote = valid.LastOrDefault(q => q.TimeUtc <= at) is { Bid: > 0 } before ? before : valid.FirstOrDefault();
                static double Rel(QuoteTick q) => (q.Ask - q.Bid) / ((q.Ask + q.Bid) / 2);
                var rel = valid.Select(Rel).OrderBy(x => x).ToList();
                double median = rel.Count == 0 ? double.NaN : rel.Count % 2 == 1 ? rel[rel.Count / 2] : (rel[rel.Count / 2 - 1] + rel[rel.Count / 2]) / 2;
                bool has = valid.Count > 0;
                sb.Append(string.Create(CultureInfo.InvariantCulture,
                    $"{symbol},{day:yyyy-MM-dd},{leg},{at:yyyy-MM-ddTHH:mm:ssZ},{t.Weights[r]:R},{fill:R},{cost.Spread:R},{cost.ImpactPerSide:R},{cost.RoundTrip:R},{valid.Count},{(has ? Rel(atQuote) : double.NaN):R},{median:R},{(has ? atQuote.Bid : double.NaN):R},{(has ? atQuote.Ask : double.NaN):R},{(has ? atQuote.BidSize : double.NaN):R},{(has ? atQuote.AskSize : double.NaN):R}\n"));
                legs++;
            }
            if (sb.Length > 0) await File.AppendAllTextAsync(file, sb.ToString(), ct);
            if (trades % 100 == 0)
                s.Logger.LogInformation("[{Now}] explore quotes-pull: {Trades:N0} trades, {Legs:N0} legs written, {Calls:N0} quote calls ({Day})", GapperStudyServices.Et(s.UtcNow), trades, legs, calls, day);
        }
        return ([$"{legs:N0} legs of {trades:N0} simulated trades written to {file} ({calls:N0} quote calls, {failed:N0} failed and left for a rerun); spread multiplier {multiplier:0.###} ({source})"], 0, file);
    }

    private static List<QuoteEvent> ReadQuoteEvents(string file)
    {
        static double D(string v) => double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : double.NaN;
        var list = new List<QuoteEvent>();
        foreach (var line in File.ReadLines(file).Skip(1))
        {
            var f = line.Split(',');
            if (f.Length != 16 || !DateOnly.TryParseExact(f[1], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
                || !DateTime.TryParse(f[3], CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var at)) continue;
            list.Add(new QuoteEvent(f[0], day, f[2], at, D(f[4]), D(f[5]), D(f[6]), D(f[7]), D(f[8]), int.TryParse(f[9], CultureInfo.InvariantCulture, out var n) ? n : 0,
                D(f[10]), D(f[11]), D(f[12]), D(f[13]), D(f[14]), D(f[15])));
        }
        return list;
    }

    /// <summary>
    /// `explore quotes-check` (IP-T472, descriptive): the frozen cost model's charge against the NBBO actually quoted at
    /// the simulated fills of the quotes-pull trade set. Per leg, the quoted relative spread at the fill (last quote at
    /// or before it) and over ±60 s; per trade, the quoted round-trip spread (half the spread on each side) against the
    /// spread the model charged (prior-days Corwin-Schultz x the calibrated multiplier), and the quoted spread plus the
    /// model's own impact against its round trip (floor included); dollar depth at the touch against the frozen trade
    /// notional. Unweighted and weighted (amendment 01) medians. No trial is logged.
    /// </summary>
    private async Task<(List<string>, int, string?)> QuotesCheckAsync(CancellationToken ct)
    {
        await Task.CompletedTask;
        var file = QuoteFile();
        if (!File.Exists(file)) return ([$"{file} does not exist; run `explore quotes-pull` first"], 0, null);
        var events = ReadQuoteEvents(file);
        var notional = GapperSpreadMultiplier.TradeNotional(s.Params) ?? double.NaN;
        var trades = events.GroupBy(e => (e.Symbol, e.Day))
            .Select(g => (Entry: g.FirstOrDefault(e => e.Leg == "entry"), Exit: g.FirstOrDefault(e => e.Leg == "exit")))
            .Where(x => x.Entry is not null && x.Exit is not null).Select(x => (Entry: x.Entry!, Exit: x.Exit!)).ToList();
        var quoted = trades.Where(x => double.IsFinite(x.Entry.RelAt) && double.IsFinite(x.Exit.RelAt)).ToList();

        static double Med(IEnumerable<double> v) { var l = v.Where(double.IsFinite).OrderBy(x => x).ToList(); return l.Count == 0 ? double.NaN : l.Count % 2 == 1 ? l[l.Count / 2] : (l[l.Count / 2 - 1] + l[l.Count / 2]) / 2; }
        static double WMed(IEnumerable<(double V, double W)> v)
        {
            var l = v.Where(x => double.IsFinite(x.V) && x.W > 0).OrderBy(x => x.V).ToList();
            double total = l.Sum(x => x.W), run = 0;
            foreach (var x in l) { run += x.W; if (run >= total / 2) return x.V; }
            return double.NaN;
        }
        static double Share(IEnumerable<bool> v) { var l = v.ToList(); return l.Count == 0 ? double.NaN : (double)l.Count(b => b) / l.Count; }
        static string P(double v, string fmt = "0.00%") => double.IsNaN(v) ? "n/a" : v.ToString(fmt, CultureInfo.InvariantCulture);
        static string X(double v) => double.IsNaN(v) ? "n/a" : v.ToString("0.00", CultureInfo.InvariantCulture) + "x";

        var md = new StringBuilder("# Explore: the frozen cost model against quoted NBBO at simulated fills (train, descriptive)\n\n");
        md.AppendLine($"Trade set `{QuoteTradeSet}`: train mornings below +20% at 08:30 passing volRatioTod >= {EntryVolRatioMins.Min()} and v3 >= {EntryV3Mins.Min()}, bought now, exit {EntryExit.Label}. No explore configuration cleared the finalist bar, so this is the pool any entries finalist would have been drawn from, not a finalist.");
        md.AppendLine();
        md.AppendLine($"Legs on file: {events.Count:N0}; complete trades: {trades.Count:N0}; with a quote on both legs: {quoted.Count:N0}.");
        md.AppendLine();
        md.AppendLine("## Per leg");
        md.AppendLine();
        md.AppendLine("| leg | legs | with quotes | median quotes in ±60 s | median quoted spread at the fill | weighted | median of the ±60 s median | median model spread | dollar depth at the touch (ask for entry, bid for exit) / trade notional |");
        md.AppendLine("|---|---|---|---|---|---|---|---|---|");
        var json = new Dictionary<string, object?>();
        foreach (var leg in new[] { "entry", "exit" })
        {
            var l = events.Where(e => e.Leg == leg).ToList();
            var q = l.Where(e => double.IsFinite(e.RelAt)).ToList();
            var depth = q.Select(e => leg == "entry" ? e.AskSizeAt * e.AskAt : e.BidSizeAt * e.BidAt).Select(d => d > 0 && double.IsFinite(notional) && notional > 0 ? d / notional : double.NaN);
            md.AppendLine($"| {leg} | {l.Count:N0} | {q.Count:N0} | {Med(l.Select(e => (double)e.Quotes)):0} | {P(Med(q.Select(e => e.RelAt)))} | {P(WMed(q.Select(e => (e.RelAt, e.Weight))))} | {P(Med(q.Select(e => e.RelMedian)))} | {P(Med(l.Select(e => e.ChargedSpread)))} | {X(Med(depth))} |");
            json[leg] = new { legs = l.Count, withQuotes = q.Count, relAtMedian = Med(q.Select(e => e.RelAt)), relAtWeighted = WMed(q.Select(e => (e.RelAt, e.Weight))), relWindowMedian = Med(q.Select(e => e.RelMedian)), chargedSpreadMedian = Med(l.Select(e => e.ChargedSpread)) };
        }
        md.AppendLine();

        md.AppendLine("## Per trade: quoted against charged");
        md.AppendLine();
        md.AppendLine("| exit window | trades | quoted round-trip spread (median) | model spread (median) | quoted / model (median) | quoted > model | quoted spread + model impact (median) | model round trip (median) | quoted + impact > model round trip |");
        md.AppendLine("|---|---|---|---|---|---|---|---|---|");
        var rthOpen = MinuteBarMath.RthOpenEt;
        var windows = new (string Name, Func<(QuoteEvent Entry, QuoteEvent Exit), bool> In)[]
        {
            ("all", _ => true),
            ("exit before 09:30", x => MinuteBarMath.ToEastern(x.Exit.AtUtc).TimeOfDay < rthOpen),
            ("exit 09:30 or later", x => MinuteBarMath.ToEastern(x.Exit.AtUtc).TimeOfDay >= rthOpen),
        };
        var perTrade = new List<object>();
        foreach (var (name, inWindow) in windows)
        {
            var w = quoted.Where(inWindow).ToList();
            var rt = w.Select(x => (x.Entry.RelAt + x.Exit.RelAt) / 2).ToList();
            var model = w.Select(x => x.Entry.ChargedSpread).ToList();
            var ratio = w.Select(x => x.Entry.ChargedSpread > 0 ? (x.Entry.RelAt + x.Exit.RelAt) / 2 / x.Entry.ChargedSpread : double.NaN).ToList();
            var withImpact = w.Select(x => (x.Entry.RelAt + x.Exit.RelAt) / 2 + 2 * x.Entry.ChargedImpactSide).ToList();
            var roundTrip = w.Select(x => x.Entry.ChargedRoundTrip).ToList();
            md.AppendLine($"| {name} | {w.Count:N0} | {P(Med(rt))} | {P(Med(model))} | {X(Med(ratio))} | {P(Share(w.Select(x => (x.Entry.RelAt + x.Exit.RelAt) / 2 > x.Entry.ChargedSpread)), "0.0%")} | {P(Med(withImpact))} | {P(Med(roundTrip))} | {P(Share(w.Select(x => (x.Entry.RelAt + x.Exit.RelAt) / 2 + 2 * x.Entry.ChargedImpactSide > x.Entry.ChargedRoundTrip)), "0.0%")} |");
            perTrade.Add(new { window = name, trades = w.Count, quotedRoundTripMedian = Med(rt), modelSpreadMedian = Med(model), ratioMedian = Med(ratio), quotedPlusImpactMedian = Med(withImpact), modelRoundTripMedian = Med(roundTrip) });
        }
        json["perTrade"] = perTrade;
        md.AppendLine();
        md.AppendLine("The model charges its spread once per round trip (half on each side), so the quoted round trip is half the entry spread plus half the exit spread. A quoted / model ratio near 1 says the frozen multiplier holds at the fills it priced; well above 1 says the cost model flattered every long result, well below says it was harsh. Quote sizes are as the SIP feed reports them. Descriptive; no trial is logged.");
        return Write("quotes-check", md, [], json);
    }
}
