using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using IdiotProof.Core.Catalysts;
using IdiotProof.Core.Data;
using IdiotProof.Core.Evidence;
using IdiotProof.Core.Research.Gappers;
using IdiotProof.Core.Services;
using IdiotProof.DataFeeds;
using IdiotProof.Evidence.Gappers;
using IdiotProof.Models;
using IdiotProof.Operations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

/// <summary>
/// IP-T486 `casestudy`: recent-runner case studies on the window amendment 05 opened (2026-09-10 .. 2026-11-09).
/// Case study (opened window, not a test): it tests no hypothesis and reads no market data dated before
/// <see cref="OpenedFrom"/>. Per session: the top N by intraday high over the split-adjusted prior close
/// ($0.10-$20 prior close, ADV$ >= $300k over the window sessions so far or >= $1M traded that day), with
/// listed and unlisted (ADS ratio change) splits resolved and screener artifacts recorded. Per stock-day:
/// the catalyst (Alpaca news D-2..D with publish minute, EDGAR filings D-5..D with acceptance time), the
/// prior profile (ADV, XBRL shares outstanding adjusted for later splits, reverse splits and whether they were
/// announced, dilution filings in the last 90 days, prior top-mover days inside the window, foreign filer),
/// the 1-minute path (premarket high, ignition, high of day, VWAP loss, volume climax, halts, D+1/D+2) and
/// whether amendment 04's R4 alarm would have fired 06:00-09:10 on a reconstructed morning cross-section.
/// Event metadata (split dates, filing dates, news) may be read up to 120 days back for the selected names;
/// prices, volumes and rankings never are.
/// </summary>
internal static class CaseStudyCommand
{
    public static readonly DateOnly OpenedFrom = new(2026, 9, 10);
    public const string Label = "case study (opened window, not a test)";

    private static readonly TimeSpan SessionStart = new(4, 0, 0);
    private static readonly TimeSpan SessionEnd = new(20, 0, 0);
    private static readonly TimeSpan RthOpen = new(9, 30, 0);
    private static readonly TimeSpan RthClose = new(16, 0, 0);
    private static readonly TimeSpan ReachEt = new(9, 45, 0);
    private const double PriceMin = 0.10, PriceMax = 20.0, AdvMin = 300_000, DayDollarMin = 1_000_000;
    private const int MetadataLookbackDays = 120;
    private static readonly double[] CommonRatios = [2, 3, 4, 5, 6, 8, 10, 12, 15, 20, 25, 30, 35, 40, 50, 60, 75, 80, 100, 120, 150, 200, 250, 300, 400, 500, 1000];

    // ── Data shapes ──────────────────────────────────────────────────────

    private sealed record DayBar(DateOnly Date, double Open, double High, double Low, double Close, double Volume, double Vwap);

    private sealed class Series
    {
        public required string Symbol { get; init; }
        public required List<DayBar> Bars { get; init; }
        public required Dictionary<DateOnly, double> R { get; init; }
        public List<(DateOnly Ex, double Factor, string Source)> Inferred { get; } = [];
        public List<DateOnly> ListedEx { get; } = [];

        public int Index(DateOnly d) => Bars.FindIndex(b => b.Date == d);

        /// <summary>Multiplier that puts a raw price dated <paramref name="barDate"/> on the basis of <paramref name="asOf"/>.</summary>
        public double Factor(DateOnly barDate, DateOnly asOf)
        {
            double f = R.TryGetValue(barDate, out var a) && R.TryGetValue(asOf, out var b) && a > 0 && b > 0 ? a / b : 1;
            if (Math.Abs(f - 1) < 0.02) f = 1;
            foreach (var (ex, factor, _) in Inferred) if (ex > barDate && ex <= asOf) f *= factor;
            return f;
        }
    }

    private sealed class Row
    {
        public required string Symbol { get; init; }
        public required DateOnly D { get; init; }
        public required DateOnly P { get; init; }
        public required DayBar Bar { get; init; }
        public double AdjPrev { get; set; }
        public double ListedFactor { get; set; }
        public double Gain => Bar.High / AdjPrev - 1;
        public double RawGain { get; init; }
        public double Adv { get; init; }
        public double AdvShares { get; set; }
        public int Sessions { get; init; }
        public double DayDollar { get; init; }
        public bool Liquid => Adv >= AdvMin || DayDollar >= DayDollarMin;
        public bool InBand => AdjPrev >= PriceMin && AdjPrev < PriceMax;
        public bool Suspect { get; set; }
        public string? Artifact { get; set; }
        public bool Dropped { get; set; }
        public int Rank { get; set; }
    }

    private sealed class NewsItem
    {
        public string Time { get; init; } = "";
        [JsonIgnore] public DateTime Utc { get; init; }
        public string Source { get; init; } = "";
        public string Headline { get; init; } = "";
        public string Type { get; init; } = "";
        public bool Reaction { get; init; }
    }

    private sealed class FilingItem
    {
        public string Form { get; init; } = "";
        public string Filed { get; init; } = "";
        public string? Accepted { get; set; }
        [JsonIgnore] public DateTime? AcceptedUtc { get; set; }
        public string? Items { get; set; }
        public string? Description { get; init; }
        /// <summary>The opening of the exhibit 99 press release (6-K/8-K), when one was fetched.</summary>
        public string? ExhibitLead { get; set; }
        public string? Type { get; set; }
        [JsonIgnore] public string Accession { get; init; } = "";
    }

    private sealed class Halt
    {
        public string Time { get; init; } = "";
        public string Reason { get; init; } = "";
        public string? Resume { get; init; }
    }

    private sealed class SplitInfo
    {
        public string ExDate { get; init; } = "";
        public double Ratio { get; init; }
        public string Kind { get; init; } = "";
        public string Source { get; init; } = "";
        public int DaysBefore { get; init; }
        public string? AnnouncedEt { get; set; }
        public double? AnnouncedDaysBefore { get; set; }
        public string? AnnouncementHeadline { get; set; }
    }

    private sealed class R4Check
    {
        public string Status { get; set; } = "";
        public bool InPopulation { get; set; }
        public double? PmRetAt0600 { get; set; }
        public bool Scorable { get; set; }
        public bool Forming { get; set; }
        public string? First20Et { get; set; }
        public string? AlarmEt { get; set; }
        public double? PmRetAtAlarm { get; set; }
        public bool Hit { get; set; }
        public int? LeadMin { get; set; }
        public int? LeadToHodMin { get; set; }
        public double? VolRatioAtAlarm { get; set; }
    }

    private sealed class Dossier
    {
        public string Label { get; init; } = CaseStudyCommand.Label;
        public string Date { get; init; } = "";
        public string Symbol { get; init; } = "";
        public int Rank { get; init; }
        /// <summary>Named by --extra (outside the ranked top N); kept out of every ranked statistic.</summary>
        public bool Named { get; init; }
        public string Name { get; set; } = "";
        // catalyst
        public string Catalyst { get; set; } = "";
        public string? CatalystEt { get; set; }
        public string? CatalystHeadline { get; set; }
        public string? CatalystSource { get; set; }
        public bool CatalystViaReaction { get; set; }
        public bool CatalystBeforeClose { get; set; }
        public double? CatalystToHodMin { get; set; }
        public double? CatalystToIgnitionMin { get; set; }
        public bool PostSplit0To10 { get; set; }
        public string Group { get; set; } = "";
        // prior profile
        public double PriorClose { get; set; }
        public double Adv20Dollars { get; set; }
        public int AdvSessions { get; set; }
        public double? SharesOutstanding { get; set; }
        public string? SharesAsOf { get; set; }
        public bool? FloatUnder10M { get; set; }
        public SplitInfo? LastReverseSplit { get; set; }
        public int? DaysSinceReverseSplit { get; set; }
        public List<string> DilutionFilings90d { get; set; } = [];
        public bool RecentDilutionFiling { get; set; }
        public bool ElocMention { get; set; }
        public int PriorTopMoverDays { get; set; }
        public int PriorTopMoverWindowSessions { get; set; }
        public bool Adr { get; set; }
        public bool ForeignFiler { get; set; }
        public string? Country { get; set; }
        public string? Cik { get; set; }
        // artifacts
        public double ListedSplitFactorOnD { get; set; }
        public double RawGainHigh { get; set; }
        public string? Artifact { get; set; }
        // path
        public double DailyHighGain { get; set; }
        public double? PmHigh { get; set; }
        public double? PmHighGain { get; set; }
        public string? PmHighEt { get; set; }
        public double PmVolume { get; set; }
        public double? Open { get; set; }
        public double? OpenGain { get; set; }
        public string? IgnitionEt { get; set; }
        public double? IgnitionVolMultiple { get; set; }
        public bool IgnitionIsFirstPrint { get; set; }
        public double? SpyDayPct { get; set; }
        public double? IgnitionPrice { get; set; }
        public double? Hod { get; set; }
        public double? RunPct { get; set; }
        public string? HodEt { get; set; }
        public double? IgnitionToHodMin { get; set; }
        public double? RthHigh { get; set; }
        public string? RthHighEt { get; set; }
        public double Close { get; set; }
        public double CloseGain { get; set; }
        public double? D1Close { get; set; }
        public double? D2Close { get; set; }
        public double? D1High { get; set; }
        public bool D1Partial { get; set; }
        public bool D2Partial { get; set; }
        public double? FadeToClosePct { get; set; }
        public double? FadeToD1Pct { get; set; }
        public double? FadeToD2Pct { get; set; }
        public double? RunGivenBackByClose { get; set; }
        public double? RunGivenBackByD2 { get; set; }
        public bool D1HigherHigh { get; set; }
        // top
        public string? VwapLossEt { get; set; }
        public double? HodToVwapLossMin { get; set; }
        public bool VolumeClimaxAtTop { get; set; }
        public bool HaltNearTop { get; set; }
        public double? SecondaryHighPctOfHod { get; set; }
        public bool LowerHigh { get; set; }
        public List<Halt> Halts { get; set; } = [];
        public double DayDollarVolume { get; set; }
        public double? QqqDayPct { get; set; }
        public bool Partial { get; set; }
        // evidence
        public List<NewsItem> News { get; set; } = [];
        public List<FilingItem> Filings { get; set; } = [];
        public R4Check R4 { get; set; } = new();
    }

    // ── Entry point ──────────────────────────────────────────────────────

    public static async Task<OperationResult> RunAsync(IServiceProvider sp, Guid? owner, IReadOnlyDictionary<string, string?> flags, CancellationToken ct)
    {
        var nowUtc = DateTime.UtcNow;
        var todayEt = DateOnly.FromDateTime(MinuteBarMath.ToEastern(nowUtc));
        var from = DateFlag(flags, "from") ?? OpenedFrom;
        var to = DateFlag(flags, "to") ?? todayEt;
        if (from < OpenedFrom)
            return OperationResult.Fail($"Refusing: --from {from:yyyy-MM-dd} is before {OpenedFrom:yyyy-MM-dd}; earlier days are the locked holdout (amendment 05).");
        if (to > todayEt) to = todayEt;
        foreach (var part in (flags.GetValueOrDefault("extra") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (DateOnly.TryParseExact(part.Split(':')[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var xd0) && xd0 < OpenedFrom)
                return OperationResult.Fail($"Refusing: --extra date {xd0:yyyy-MM-dd} is before {OpenedFrom:yyyy-MM-dd}, inside the locked holdout (amendment 05).");
        int top = flags.TryGetValue("top", out var tv) && int.TryParse(tv, CultureInfo.InvariantCulture, out var tn) && tn > 0 ? tn : 5;

        var s = sp.GetRequiredService<GapperStudyServices>();
        var log = s.Logger;
        var http = sp.GetRequiredService<IHttpClientFactory>();
        var edgarGate = sp.GetRequiredService<EdgarRateGate>();
        var headers = sp.GetRequiredService<EdgarFilingHeaders>();
        var newsService = sp.GetRequiredService<AlpacaNewsService>();
        var keys = await s.ResolveKeysAsync(owner, ct);
        await using var feed = s.Feed(keys);
        var notes = new List<string>();

        // 1. Daily bars, raw and split-adjusted, for every us_equity asset, from the window start only.
        var assets = await s.LoadAssetsAsync(keys, ct);
        var names = assets.ToDictionary(a => a.Symbol, a => a.Name, StringComparer.Ordinal);
        // Active assets only: the inactive list is ~20k long-dead tickers, and the bars endpoint rejects many of them,
        // which costs a halving retry per bad symbol. A name delisted after the window started is missed (noted).
        var universe = assets.Where(a => a.Active).Select(a => a.Symbol).ToList();
        notes.Add($"selection universe: {universe.Count} active Alpaca us_equity assets at run time; names delisted since {OpenedFrom:yyyy-MM-dd} are not scanned");
        foreach (var etf in new[] { "QQQ", "SPY" }) if (!names.ContainsKey(etf)) universe.Add(etf);
        log.LogInformation("[{Now}] casestudy: {Assets} assets; daily bars {From}..now (raw + split)", Et(DateTime.UtcNow), universe.Count, OpenedFrom);
        var dStart = MinuteBarMath.ToUtc(OpenedFrom, TimeSpan.Zero);
        var raw = await DailyAsync(feed, s, universe, dStart, nowUtc, "raw", log, ct);
        var adj = await DailyAsync(feed, s, universe, dStart, nowUtc, "split", log, ct);
        var series = new Dictionary<string, Series>(StringComparer.Ordinal);
        foreach (var (sym, bars) in raw)
        {
            if (bars.Count == 0) continue;
            var a = adj.GetValueOrDefault(sym) ?? [];
            var aBy = a.ToDictionary(x => x.Date);
            var r = new Dictionary<DateOnly, double>();
            // the adjustment ratio from the opens: the raw and split pulls are minutes apart, and during a live session today's
            // close moves between them (a 2-3% drift once showed up as a fake "split" on the current day)
            foreach (var b in bars) r[b.Date] = aBy.TryGetValue(b.Date, out var x) && b.Open > 0 && x.Open > 0 ? x.Open / b.Open : 1;
            var se = new Series { Symbol = sym, Bars = bars, R = r };
            for (int i = 1; i < bars.Count; i++) if (Math.Abs(se.Factor(bars[i - 1].Date, bars[i].Date) - 1) >= 0.02) se.ListedEx.Add(bars[i].Date);
            series[sym] = se;
        }
        adj.Clear();
        log.LogInformation("[{Now}] casestudy: {N} symbols with window bars", Et(DateTime.UtcNow), series.Count);

        // 2. Every in-band, liquid stock-day of the window with its split-adjusted gain; ranks per day.
        var rows = new List<Row>();
        foreach (var se in series.Values)
            for (int i = 1; i < se.Bars.Count; i++)
            {
                var b = se.Bars[i];
                var p = se.Bars[i - 1];
                if (p.Close <= 0) continue;
                var lf = se.Factor(p.Date, b.Date);
                var prior = se.Bars.Take(i).TakeLast(20).ToList();
                var row = new Row
                {
                    Symbol = se.Symbol, D = b.Date, P = p.Date, Bar = b, AdjPrev = p.Close * lf, ListedFactor = lf, RawGain = b.High / p.Close - 1,
                    Adv = prior.Average(x => x.Close * x.Volume), Sessions = prior.Count,
                    AdvShares = prior.Average(x => x.Volume / se.Factor(x.Date, b.Date)),
                    DayDollar = (b.Vwap > 0 ? b.Vwap : b.Close) * b.Volume,
                };
                row.Suspect = lf == 1 && row.AdjPrev > 0 && b.Open / row.AdjPrev >= 2.5 && b.Low / row.AdjPrev >= 1.8;
                rows.Add(row);
            }
        var eligible = rows.Where(r => r.InBand && r.Liquid && !names.GetValueOrDefault(r.Symbol, "").Contains("Warrant", StringComparison.OrdinalIgnoreCase)).ToList();
        void Rerank() { foreach (var g in eligible.Where(r => !r.Dropped).GroupBy(r => r.D)) { int k = 0; foreach (var r in g.OrderByDescending(r => r.Gain)) r.Rank = ++k; } }
        Rerank();

        // 3. Screener and split artifacts: listed splits (raw gain is fake), and unlisted ratio changes found from news.
        var screenerArtifacts = rows.Where(r => r.D >= from && r.D <= to && r.Liquid && r.ListedFactor != 1 && r.RawGain >= 0.5 && r.Gain < 0.2
                && (r.AdjPrev is >= PriceMin and < PriceMax || r.Bar.Close is >= PriceMin and < PriceMax))
            .Select(r => new { date = r.D.ToString("yyyy-MM-dd"), symbol = r.Symbol, rawGainHigh = Math.Round(r.RawGain, 3), adjustedGainHigh = Math.Round(r.Gain, 3), factor = Math.Round(r.ListedFactor, 4), source = "Alpaca split adjustment" })
            .Cast<object>().ToList();
        var newsCache = new NewsCache(newsService, s, keys);
        var splitCache = new Dictionary<string, List<SplitEvent>>(StringComparer.Ordinal);
        using var splitSource = s.Splits(keys);
        async Task LoadSplitsAsync(IEnumerable<string> syms)
        {
            var missing = syms.Where(x => !splitCache.ContainsKey(x)).Distinct().ToList();
            foreach (var chunk in missing.Chunk(50))
            {
                await s.RateGate.WaitAsync(ct);
                var got = await splitSource.GetSplitsAsync(chunk, OpenedFrom.AddDays(-MetadataLookbackDays), todayEt.AddDays(2), ct);
                foreach (var x in chunk) splitCache[x] = got.TryGetValue(x, out var l) ? l.ToList() : [];
            }
        }
        var suspects = eligible.Where(r => r.Suspect && r.Rank <= top * 3 && r.D >= from && r.D <= to).ToList();
        await LoadSplitsAsync(suspects.Select(r => r.Symbol));
        foreach (var r in suspects)
        {
            var se = series[r.Symbol];
            double jump = r.Bar.Open / r.AdjPrev;
            double? ratio = null;
            string source = "";
            if (splitCache[r.Symbol].FirstOrDefault(x => x.ExDate == r.D && x.PriceFactor > 1) is { } listed)
            { ratio = (double)listed.PriceFactor; source = "Alpaca corporate action (not applied to bars)"; }
            else
            {
                var news = await newsCache.GetAsync(r.Symbol, MinuteBarMath.ToUtc(r.D.AddDays(-45), TimeSpan.Zero), MinuteBarMath.ToUtc(r.D, SessionEnd), ct);
                var hit = news.Where(n => n.CreatedAt <= MinuteBarMath.ToUtc(r.D, SessionEnd) && Taxonomy.IsSplitText(n.Headline + " " + n.Summary)).ToList();
                if (hit.Count > 0)
                {
                    ratio = hit.Select(n => Taxonomy.ParseRatio(n.Headline + " " + n.Summary)).FirstOrDefault(x => x is > 1.5) ?? Nearest(jump);
                    source = "news: " + Trunc(hit[0].Headline, 90);
                }
            }
            if (ratio is not { } f || f < 1.5 || Math.Abs(Math.Log(jump / f)) > Math.Log(3)) { r.Artifact = $"gap {jump:0.0}x with no listed split and no split news (kept, unverified)"; continue; }
            se.Inferred.Add((r.D, f, source));
            r.AdjPrev *= f;
            r.ListedFactor = f;
            r.Artifact = $"unlisted split/ratio change 1:{f:0.##} on {r.D:MM-dd} ({source}); raw gain {r.RawGain:P0} -> real {r.Gain:P0}";
            screenerArtifacts.Add(new { date = r.D.ToString("yyyy-MM-dd"), symbol = r.Symbol, rawGainHigh = Math.Round(r.RawGain, 3), adjustedGainHigh = Math.Round(r.Gain, 3), factor = f, source });
            if (r.Gain < 0.10) r.Dropped = true;
        }
        Rerank();

        // 4. Selection.
        var selected = eligible.Where(r => !r.Dropped && r.D >= from && r.D <= to && r.Rank <= top).OrderBy(r => r.D).ThenBy(r => r.Rank).ToList();
        // --extra "2026-11-09:CKX,GFR;2026-11-06:NWGL": named stock-days studied beside the ranked set (never in the ranked statistics)
        var extras = new HashSet<(string, DateOnly)>();
        foreach (var part in (flags.GetValueOrDefault("extra") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var kv = part.Split(':', 2);
            if (kv.Length != 2 || !DateOnly.TryParseExact(kv[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var xd)) continue;
            if (xd < OpenedFrom) return OperationResult.Fail($"Refusing: --extra date {xd:yyyy-MM-dd} is inside the locked holdout.");
            foreach (var sym in kv[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(x => x.ToUpperInvariant()))
            {
                if (selected.Any(r => r.Symbol == sym && r.D == xd)) continue;
                if (rows.FirstOrDefault(r => r.Symbol == sym && r.D == xd) is not { } xr) { notes.Add($"--extra {sym} {xd:yyyy-MM-dd}: no daily bar pair in the window"); continue; }
                if (xr.Rank == 0) xr.Rank = eligible.Count(e => e.D == xd && !e.Dropped && e.Gain > xr.Gain) + 1;
                selected.Add(xr);
                extras.Add((sym, xd));
            }
        }
        selected = selected.OrderBy(r => r.D).ThenBy(r => r.Rank).ToList();
        var symbolsSel = selected.Select(r => r.Symbol).Distinct(StringComparer.Ordinal).ToList();
        log.LogInformation("[{Now}] casestudy: {N} stock-days selected ({Sym} symbols) over {Days} sessions", Et(DateTime.UtcNow), selected.Count, symbolsSel.Count, selected.Select(r => r.D).Distinct().Count());
        await LoadSplitsAsync(symbolsSel);

        // 5. Minute bars for every selected stock-day (04:00-20:00 ET).
        var minute = new Dictionary<(string, DateOnly), List<Candle>>();
        foreach (var day in selected.GroupBy(r => r.D))
        {
            var got = await MinuteAsync(feed, s, day.Select(r => r.Symbol).ToList(), day.Key, SessionStart, SessionEnd, log, ct);
            foreach (var (sym, list) in got) minute[(sym, day.Key)] = list;
        }

        // 6. Halts (Nasdaq Trader day files) for window days only.
        var halts = await HaltsAsync(http, selected.Select(r => r.D).Distinct().ToList(), todayEt, log, ct);

        // 7. EDGAR: CIK, submissions (filings, country), company facts (shares outstanding), filing headers.
        var cikBySymbol = await CikMapAsync(http, edgarGate, s, ct);
        var subs = new Dictionary<string, Submissions>(StringComparer.Ordinal);
        var facts = new Dictionary<string, SharesOutstandingHistory>(StringComparer.Ordinal);
        foreach (var sym in symbolsSel)
        {
            if (!cikBySymbol.TryGetValue(sym, out var cik)) continue;
            if (await SubmissionsAsync(http, edgarGate, cik, ct) is { } sub) subs[sym] = sub;
            if (await CompanyFactsAsync(http, edgarGate, cik, ct) is { } f) facts[sym] = f;
        }
        var headerCache = new Dictionary<string, EdgarFilingHeader?>(StringComparer.Ordinal);

        // 8. News for the selected names: D-2 .. D (catalyst) and the lookback for split announcements and ELOC mentions.
        foreach (var g in selected.GroupBy(r => r.Symbol))
            await newsCache.GetAsync(g.Key, MinuteBarMath.ToUtc(g.Min(r => r.D).AddDays(-MetadataLookbackDays + 30), TimeSpan.Zero), MinuteBarMath.ToUtc(g.Max(r => r.D), SessionEnd), ct);

        // 9. R4 on a reconstructed morning cross-section.
        var profile = TimeOfDayProfile.Load(s.Paths.ProfileFile(false));
        var amendment = s.Amendment ?? throw new InvalidOperationException("Amendment 01's population sample is required for the R4 cross-section.");
        var r4 = new Dictionary<(string, DateOnly), R4Check>();
        var r4Days = new List<object>();
        var windowDays = MarketCalendar.TradingDays(OpenedFrom, todayEt);
        foreach (var day in selected.GroupBy(r => r.D).OrderBy(g => g.Key))
        {
            var d = day.Key;
            int sessionsBefore = windowDays.Count(x => x < d);
            var pool = rows.Where(r => r.D == d && r.InBand && r.Sessions >= Math.Min(5, sessionsBefore)).ToList();
            var members = new List<GapperStudyUniverseDay>();
            foreach (var r in pool)
            {
                if (r.Adv < AdvMin) continue;
                var se = series[r.Symbol];
                bool splitWindow = se.ListedEx.Concat(se.Inferred.Select(x => x.Ex)).Any(ex => ex >= d.AddDays(-25) && ex <= d.AddDays(1));
                var u = new GapperStudyUniverseDay
                {
                    Symbol = r.Symbol, DateEt = d, AdjPrevClose = (decimal)r.AdjPrev, Adv20Dollars = (decimal)r.Adv, Adv20Shares = (decimal)r.AdvShares, Sessions = r.Sessions,
                    ExcludedReason = splitWindow ? GapperUniverseBuilder.ExcludedSplitWindow : null,
                    IsCandidate = !splitWindow && (r.Gain >= 0.10 || (r.AdvShares > 0 && r.Bar.Volume / r.AdvShares >= 3)),
                };
                members.Add(u);
            }
            GapperUniverseBuilder.AssignDeciles(members);
            var advSorted = members.Where(m => m.ExcludedReason is null).Select(m => (double)m.Adv20Dollars).OrderBy(x => x).ToList();
            var popu = new List<(GapperStudyUniverseDay U, double W, bool Sampled)>();
            var selSet = day.Select(r => r.Symbol).ToHashSet(StringComparer.Ordinal);
            foreach (var u in members.Where(m => m.ExcludedReason is null && !selSet.Contains(m.Symbol)))
            {
                if (u.IsCandidate) popu.Add((u, 1, false));
                else if (amendment.IsSampleMember(u.Symbol, d)) popu.Add((u, amendment.SampleWeight, true));
            }
            foreach (var r in day)
            {
                int below = advSorted.Count(x => x < r.Adv);
                var u = new GapperStudyUniverseDay
                {
                    Symbol = r.Symbol, DateEt = d, AdjPrevClose = (decimal)r.AdjPrev, Adv20Dollars = (decimal)r.Adv, Adv20Shares = (decimal)r.AdvShares, Sessions = r.Sessions,
                    IsCandidate = true, AdvDecile = advSorted.Count == 0 ? 0 : Math.Min(9, below * 10 / advSorted.Count),
                };
                var inPop = members.Any(m => m.Symbol == r.Symbol && m.ExcludedReason is null);
                r4[(r.Symbol, d)] = new R4Check { InPopulation = inPop };
                popu.Add((u, 1, false));
            }
            var need = popu.Select(x => x.U.Symbol).Where(x => !selSet.Contains(x)).Distinct().ToList();
            var bars = await MinuteAsync(feed, s, need, d, SessionStart, ReachEt.Add(TimeSpan.FromMinutes(1)), log, ct);
            var mornings = new List<AlarmMorning>();
            var who = new List<string>();
            foreach (var (u, w, sampled) in popu)
            {
                IReadOnlyList<Candle> b = selSet.Contains(u.Symbol) ? minute.GetValueOrDefault((u.Symbol, d)) ?? [] : bars.GetValueOrDefault(u.Symbol) ?? [];
                var m = GapperAlarmData.MorningOf(u, w, sampled, b, null, null, profile, null, ReachEt, 0.20);
                if (m is null) continue;
                mornings.Add(m); who.Add(u.Symbol);
            }
            bars.Clear();
            int[] alarm = mornings.Count == 0 ? [] : AlarmSweep.RankAlarms(mornings, AlarmSweep.Scores(mornings).SortedComposite, GapperAlarmData.R4);
            double sc = 0, fo = 0, hits = 0, fa = 0, alarms = 0;
            for (int i = 0; i < mornings.Count; i++)
            {
                var m = mornings[i];
                if (selSet.Contains(who[i])) continue;
                var o = AlarmSweep.Outcome(m, alarm[i]);
                if (m.Scorable) sc += m.Weight;
                if (m.Forming) fo += m.Weight;
                if (o.Alarmed) alarms += m.Weight;
                if (o.Hit) hits += m.Weight;
                if (o.False) fa += m.Weight;
            }
            r4Days.Add(new { date = d.ToString("yyyy-MM-dd"), populationRows = mornings.Count, scorableWeighted = sc, formingWeighted = fo, alarmsWeighted = alarms, hitsWeighted = hits, falseWeighted = fa });
            foreach (var r in day)
            {
                var chk = r4[(r.Symbol, d)];
                int i = who.IndexOf(r.Symbol);
                if (i < 0) { chk.Status = "no bars 04:00-09:45 (R4 cannot see it)"; continue; }
                var m = mornings[i];
                var o = AlarmSweep.Outcome(m, alarm[i]);
                chk.PmRetAt0600 = Finite(m.PmRet[0]);
                chk.Scorable = m.Scorable; chk.Forming = m.Forming;
                chk.First20Et = m.Reach20 ? WindowEt(d, m.First20) : null;
                if (alarm[i] >= 0)
                {
                    chk.AlarmEt = WindowEt(d, alarm[i]);
                    chk.PmRetAtAlarm = Finite(m.PmRet[alarm[i]]);
                    chk.VolRatioAtAlarm = Finite(m.VolRatio[alarm[i]]);
                    chk.Hit = o.Hit;
                    chk.LeadMin = o.Hit ? o.Lead20 : null;
                }
                chk.Status = alarm[i] >= 0 ? (o.Hit ? "alarm before +20% (hit)" : m.Reach20 && m.First20 <= alarm[i] ? "alarm after +20% touch" : !m.Reach20 ? "alarm, no +20% by 09:45" : "alarm")
                    : !m.Scorable ? "already >= +20% by 06:00 (not scorable)"
                    : m.PmRet.Any(x => x >= 0.15) && !m.Reach20 ? "no alarm (gap passed 15% cap)" : "no alarm 06:00-09:10";
                if (!m.Scorable && alarm[i] < 0 && Finite(m.PmRet[0]) is { } g0 && g0 >= 0.15) chk.Status = "gap >= 15% at 06:00 (outside R4's [5%,15%) band)";
            }
            log.LogInformation("[{Now}] casestudy R4 {Day}: {Rows} mornings", Et(DateTime.UtcNow), d, mornings.Count);
        }

        // 10. Dossiers.
        var qqq = series.GetValueOrDefault("QQQ");
        var dossiers = new List<Dossier>();
        foreach (var r in selected)
        {
            var se = series[r.Symbol];
            var bars = minute.GetValueOrDefault((r.Symbol, r.D)) ?? [];
            var dz = new Dossier { Date = r.D.ToString("yyyy-MM-dd"), Symbol = r.Symbol, Rank = r.Rank, Name = names.GetValueOrDefault(r.Symbol, ""), Named = extras.Contains((r.Symbol, r.D)) };
            dz.PriorClose = Round(r.AdjPrev, 4);
            dz.Adv20Dollars = Math.Round(r.Adv);
            dz.AdvSessions = r.Sessions;
            dz.ListedSplitFactorOnD = Math.Round(r.ListedFactor, 4);
            dz.RawGainHigh = Math.Round(r.RawGain, 4);
            dz.Artifact = r.Artifact;
            dz.DailyHighGain = Math.Round(r.Gain, 4);
            dz.DayDollarVolume = Math.Round(r.DayDollar);
            dz.Partial = r.D == todayEt && MinuteBarMath.ToEastern(nowUtc).TimeOfDay < new TimeSpan(16, 5, 0);
            if (qqq is not null && qqq.Index(r.D) is var qi and > 0) dz.QqqDayPct = Math.Round(qqq.Bars[qi].Close / qqq.Bars[qi - 1].Close - 1, 4);
            if (series.GetValueOrDefault("SPY") is { } spy && spy.Index(r.D) is var si and > 0) dz.SpyDayPct = Math.Round(spy.Bars[si].Close / spy.Bars[si - 1].Close - 1, 4);
            PricePath(dz, r, bars, se, todayEt, nowUtc, halts);
            Profile(dz, r, se, subs.GetValueOrDefault(r.Symbol), facts.GetValueOrDefault(r.Symbol), cikBySymbol.GetValueOrDefault(r.Symbol), splitCache.GetValueOrDefault(r.Symbol) ?? [],
                newsCache.Peek(r.Symbol), eligible, names);
            await FilingsAsync(dz, r, subs.GetValueOrDefault(r.Symbol), headers, headerCache, http, edgarGate, ct);
            Catalyst(dz, r, newsCache.Peek(r.Symbol));
            dz.R4 = r4.GetValueOrDefault((r.Symbol, r.D)) ?? new R4Check { Status = "not computed" };
            if (dz.R4.AlarmEt is { } ae && dz.HodEt is { } he) dz.R4.LeadToHodMin = (int)(TimeSpan.Parse(he, CultureInfo.InvariantCulture) - TimeSpan.Parse(ae, CultureInfo.InvariantCulture)).TotalMinutes;
            dossiers.Add(dz);
        }

        // 11. Outputs.
        var outDir = s.Paths.StudyDir;
        var stem = $"case-studies-{from:yyyy-MM-dd}_{to:yyyy-MM-dd}";
        var summary = Summarize(dossiers.Where(d => !d.Named).ToList());
        var json = JsonSerializer.Serialize(new
        {
            label = Label, ticket = "IP-T486", generatedEt = Et(DateTime.UtcNow), from = from.ToString("yyyy-MM-dd"), to = to.ToString("yyyy-MM-dd"), top,
            selection = $"top {top} per session by daily-bar high over the split-adjusted prior close; prior close ${PriceMin}-${PriceMax}; ADV$ (window sessions so far, up to 20) >= ${AdvMin:N0} or >= ${DayDollarMin:N0} traded that day; warrants excluded",
            holdoutGuard = $"no price, volume or ranking dated before {OpenedFrom:yyyy-MM-dd} is read; event metadata (split and filing dates, news) up to {MetadataLookbackDays} days back for the selected names only",
            notes, summary, r4Days, screenerArtifacts, stockDays = dossiers,
        }, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals });
        var jsonPath = Path.Combine(outDir, stem + ".json");
        await File.WriteAllTextAsync(jsonPath, json, ct);
        var mdPath = Path.Combine(s.Paths.RunsDir, stem + ".generated.md");
        await File.WriteAllTextAsync(mdPath, Markdown(dossiers, summary, screenerArtifacts, r4Days, from, to, top), ct);
        var human = $"{dossiers.Count} stock-days ({symbolsSel.Count} symbols) -> {jsonPath}\n generated tables and dossiers -> {mdPath}\n alpaca requests {s.RateGate.Requests}, edgar requests {edgarGate.Requests}";
        return OperationResult.Ok(new { jsonPath, mdPath, stockDays = dossiers.Count, symbols = symbolsSel.Count }, human);
    }

    // ── Alpaca ───────────────────────────────────────────────────────────

    private static async Task<Dictionary<string, List<DayBar>>> DailyAsync(AlpacaDataFeed feed, GapperStudyServices s, List<string> symbols, DateTime startUtc, DateTime endUtc, string adjustment, ILogger log, CancellationToken ct)
    {
        var result = new Dictionary<string, List<DayBar>>(StringComparer.Ordinal);
        int done = 0;
        async Task Fetch(IReadOnlyList<string> chunk)
        {
            try
            {
                var got = await feed.GetHistoricalCandlesForSymbolsAsync(chunk, TimeSpan.FromDays(1), startUtc, endUtc, 10000, s.RateGate.WaitAsync, ct, adjustment);
                foreach (var (sym, bars) in got.Bars)
                    result[sym.ToUpperInvariant()] = bars.Select(c => new DayBar(DateOnly.FromDateTime(MinuteBarMath.ToEastern(c.StartUtc)), (double)c.Open, (double)c.High, (double)c.Low, (double)c.Close, (double)c.Volume,
                        c is DetailedCandle dc ? (double)dc.Vwap : 0)).Where(b => b.Date >= OpenedFrom).OrderBy(b => b.Date).ToList();
            }
            catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity or HttpStatusCode.NotFound)
            {
                if (chunk.Count == 1) return;
                await Fetch(chunk.Take(chunk.Count / 2).ToList());
                await Fetch(chunk.Skip(chunk.Count / 2).ToList());
            }
        }
        foreach (var chunk in symbols.Chunk(AlpacaDataFeed.MaxSymbolsPerRequest))
        {
            await Fetch(chunk);
            if (++done % 10 == 0) log.LogInformation("[{Now}] casestudy daily {Adj}: {Done} chunks, {Req} requests", Et(DateTime.UtcNow), adjustment, done, s.RateGate.Requests);
        }
        return result;
    }

    private static async Task<Dictionary<string, List<Candle>>> MinuteAsync(AlpacaDataFeed feed, GapperStudyServices s, List<string> symbols, DateOnly d, TimeSpan fromEt, TimeSpan toEt, ILogger log, CancellationToken ct)
    {
        if (d < OpenedFrom) throw new InvalidOperationException($"Refusing to read minute bars for {d:yyyy-MM-dd}: inside the locked holdout.");
        var result = new Dictionary<string, List<Candle>>(StringComparer.Ordinal);
        var start = MinuteBarMath.ToUtc(d, fromEt);
        var end = MinuteBarMath.ToUtc(d, toEt);
        async Task Fetch(IReadOnlyList<string> chunk)
        {
            try
            {
                var got = await feed.GetHistoricalCandlesForSymbolsAsync(chunk, TimeSpan.FromMinutes(1), start, end, 10000, s.RateGate.WaitAsync, ct);
                foreach (var (sym, bars) in got.Bars) result[sym.ToUpperInvariant()] = bars.Where(b => b.EndUtc <= end).ToList();
            }
            catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity or HttpStatusCode.NotFound)
            {
                if (chunk.Count == 1) return;
                await Fetch(chunk.Take(chunk.Count / 2).ToList());
                await Fetch(chunk.Skip(chunk.Count / 2).ToList());
            }
        }
        foreach (var chunk in symbols.Distinct().Chunk(AlpacaDataFeed.MaxSymbolsPerRequest)) await Fetch(chunk);
        return result;
    }

    private sealed class NewsCache(AlpacaNewsService service, GapperStudyServices s, StudyKeys keys)
    {
        private readonly Dictionary<string, (DateTime From, DateTime To, List<AlpacaNewsItem> Items)> cache = new(StringComparer.Ordinal);

        public List<AlpacaNewsItem> Peek(string symbol) => cache.TryGetValue(symbol, out var c) ? c.Items : [];

        public async Task<List<AlpacaNewsItem>> GetAsync(string symbol, DateTime fromUtc, DateTime toUtc, CancellationToken ct)
        {
            if (cache.TryGetValue(symbol, out var have) && have.From <= fromUtc && have.To >= toUtc) return have.Items;
            if (have.Items is not null) { fromUtc = fromUtc < have.From ? fromUtc : have.From; toUtc = toUtc > have.To ? toUtc : have.To; }
            if (toUtc > DateTime.UtcNow) toUtc = DateTime.UtcNow;
            var items = new List<AlpacaNewsItem>();
            string? token = null;
            for (int page = 0; page < 30; page++)
            {
                await s.RateGate.WaitAsync(ct);
                var (got, next) = await service.GetNewsPageAsync([symbol], fromUtc, toUtc, 50, token, (keys.KeyId, keys.Secret), ct);
                items.AddRange(got);
                if (string.IsNullOrEmpty(next) || got.Count == 0) break;
                token = next;
            }
            items = items.DistinctBy(i => i.Id).OrderBy(i => i.CreatedAt).ToList();
            cache[symbol] = (fromUtc, toUtc, items);
            return items;
        }
    }

    // ── Halts ────────────────────────────────────────────────────────────

    private static async Task<HaltHistory> HaltsAsync(IHttpClientFactory http, List<DateOnly> days, DateOnly todayEt, ILogger log, CancellationToken ct)
    {
        var dir = Environment.GetEnvironmentVariable("GAPPER_HALTS_DIR") is { Length: > 0 } d ? d : @"D:\IdiotProofData\halts";
        var all = new List<TradeHalt>();
        var client = http.CreateClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("IdiotProof research (ryandebraal@gmail.com)");
        var want = days.SelectMany(x => new[] { x, MarketCalendar.NextTradingDay(x) }).Where(x => x >= OpenedFrom && x <= todayEt).Distinct().ToList();
        foreach (var day in want)
        {
            var file = Path.Combine(dir, $"{day:yyyy-MM-dd}.xml");
            string? xml = File.Exists(file) && day < todayEt ? await File.ReadAllTextAsync(file, ct) : null;
            if (xml is null)
            {
                try
                {
                    xml = await client.GetStringAsync($"https://www.nasdaqtrader.com/rss.aspx?feed=tradehalts&haltdate={day:MMddyyyy}", ct);
                    if (day < todayEt && Directory.Exists(dir)) await File.WriteAllTextAsync(file, xml, ct);
                    await Task.Delay(500, ct);
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { log.LogWarning("halts {Day}: {Error}", day, ex.Message); continue; }
            }
            try { all.AddRange(HaltHistory.Parse(xml)); } catch (System.Xml.XmlException) { }
        }
        return new HaltHistory(all.DistinctBy(h => (h.Symbol.ToUpperInvariant(), h.HaltUtc, h.Reason)));
    }

    // ── EDGAR ────────────────────────────────────────────────────────────

    private sealed class Submissions
    {
        public string Name = "";
        public string? Country;
        public bool Foreign;
        public List<(string Form, DateOnly Filed, string? AcceptedRaw, string Accession, string Items, string Description)> Filings = [];
        public string Cik = "";
    }

    private static async Task<string?> EdgarGetAsync(IHttpClientFactory http, EdgarRateGate gate, string url, CancellationToken ct)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                return await gate.RunAsync(async c =>
                {
                    var client = http.CreateClient("edgar");
                    using var resp = await client.GetAsync(url, c);
                    return resp.IsSuccessStatusCode ? await resp.Content.ReadAsStringAsync(c) : null;
                }, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested && ex is TaskCanceledException or HttpRequestException) { await Task.Delay(2000 * (attempt + 1), ct); }
        }
        return null;
    }

    private static async Task<Dictionary<string, string>> CikMapAsync(IHttpClientFactory http, EdgarRateGate gate, GapperStudyServices s, CancellationToken ct)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (await EdgarGetAsync(http, gate, EdgarTickerMap.Url, ct) is { } json)
            foreach (var (cik, ticker) in EdgarTickerMap.Parse(json)) map.TryAdd(ticker.Replace('-', '.').Replace(".", ""), cik);
        await using var db = await s.Db.CreateDbContextAsync(ct);
        foreach (var m in await db.GapperStudyCikMap.AsNoTracking().Where(m => m.Cik != null).Select(m => new { m.Symbol, m.Cik }).ToListAsync(ct))
            map.TryAdd(m.Symbol, EdgarTickerMap.Normalize(m.Cik!));
        return map;
    }

    private static async Task<Submissions?> SubmissionsAsync(IHttpClientFactory http, EdgarRateGate gate, string cik, CancellationToken ct)
    {
        var json = await EdgarGetAsync(http, gate, $"https://data.sec.gov/submissions/CIK{long.Parse(cik, CultureInfo.InvariantCulture):D10}.json", ct);
        if (json is null) return null;
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var sub = new Submissions { Cik = cik, Name = Str(root, "name") };
        if (root.TryGetProperty("addresses", out var addr) && addr.TryGetProperty("business", out var bus))
        {
            var code = Str(bus, "stateOrCountry");
            var desc = Str(bus, "stateOrCountryDescription");
            sub.Country = desc.Length > 0 ? desc : code;
            sub.Foreign = code.Length > 0 && !UsStates.Contains(code);
        }
        if (root.TryGetProperty("filings", out var f) && f.TryGetProperty("recent", out var rec))
        {
            var forms = Arr(rec, "form"); var dates = Arr(rec, "filingDate"); var acc = Arr(rec, "accessionNumber"); var accept = Arr(rec, "acceptanceDateTime");
            var items = Arr(rec, "items"); var descs = Arr(rec, "primaryDocDescription");
            for (int i = 0; i < forms.Count; i++)
            {
                if (!DateOnly.TryParse(dates.ElementAtOrDefault(i), CultureInfo.InvariantCulture, out var fd)) continue;
                if (fd < OpenedFrom.AddDays(-MetadataLookbackDays)) continue;
                sub.Filings.Add((forms[i], fd, accept.ElementAtOrDefault(i), acc.ElementAtOrDefault(i) ?? "", items.ElementAtOrDefault(i) ?? "", descs.ElementAtOrDefault(i) ?? ""));
                if (forms[i] is "20-F" or "6-K" or "40-F") sub.Foreign = true;
            }
        }
        return sub;
    }

    private static async Task<SharesOutstandingHistory?> CompanyFactsAsync(IHttpClientFactory http, EdgarRateGate gate, string cik, CancellationToken ct)
    {
        var json = await EdgarGetAsync(http, gate, $"https://data.sec.gov/api/xbrl/companyfacts/CIK{long.Parse(cik, CultureInfo.InvariantCulture):D10}.json", ct);
        if (json is null) return null;
        try
        {
            using var ms = new MemoryStream(Encoding.UTF8.GetBytes(json));
            var list = SharesOutstandingHistory.Parse(ms);
            return list.Count == 0 ? null : new SharesOutstandingHistory(list);
        }
        catch (JsonException) { return null; }
    }

    private static readonly HashSet<string> UsStates = new(StringComparer.Ordinal)
    {
        "AL","AK","AZ","AR","CA","CO","CT","DE","DC","FL","GA","HI","ID","IL","IN","IA","KS","KY","LA","ME","MD","MA","MI","MN","MS","MO","MT","NE","NV","NH","NJ","NM","NY","NC","ND","OH","OK","OR","PA","RI","SC","SD","TN","TX","UT","VT","VA","WA","WV","WI","WY","PR","VI","GU",
    };

    private static readonly HashSet<string> DilutionForms = new(StringComparer.Ordinal)
    {
        "S-1","S-1/A","F-1","F-1/A","S-3","S-3/A","F-3","F-3/A","424B1","424B2","424B3","424B4","424B5","424B7","424B8","EFFECT","S-1MEF","F-1MEF","S-3MEF",
    };

    private static async Task FilingsAsync(Dossier dz, Row r, Submissions? sub, EdgarFilingHeaders headers, Dictionary<string, EdgarFilingHeader?> cache,
        IHttpClientFactory http, EdgarRateGate gate, CancellationToken ct)
    {
        if (sub is null) return;
        var fromD = MarketCalendar.PriorTradingDays(r.D, 5).FirstOrDefault(r.D.AddDays(-7));
        foreach (var f in sub.Filings.Where(f => f.Filed >= fromD && f.Filed <= r.D).OrderBy(f => f.Filed))
        {
            var item = new FilingItem { Form = f.Form, Filed = f.Filed.ToString("yyyy-MM-dd"), Items = f.Items.Length > 0 ? f.Items : null, Description = f.Description.Length > 0 ? Trunc(f.Description, 80) : null, Accession = f.Accession };
            if (!cache.TryGetValue(f.Accession, out var h)) cache[f.Accession] = h = await headers.FetchAsync(sub.Cik, f.Accession, ct);
            if (h?.AcceptanceUtc is { } a) { item.AcceptedUtc = a; item.Accepted = MinuteBarMath.ToEastern(a).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture); }
            if (item.Items is null && h is { Items.Count: > 0 }) item.Items = string.Join(",", h.Items);
            if (f.Form is "6-K" or "6-K/A" or "8-K" or "8-K/A" && await ExhibitTextAsync(http, gate, sub.Cik, f.Accession, ct) is { Length: > 40 } text)
            {
                item.ExhibitLead = Trunc(text, 220);
                // the release's title and first paragraph only: deeper text mentions everything (purchase orders, MOUs, offerings)
                var type = (item.Items ?? "").Contains("2.02") ? Taxonomy.EarningsOf(text.Length > 1500 ? text[..1500] : text)
                    : Taxonomy.Classify(text.Length > 1200 ? text[..1200] : text, text.Length > 300 ? text[..300] : text);
                item.Type = type != Taxonomy.OtherNews ? type : Taxonomy.FromFiling(f.Form, item.Items);
            }
            else item.Type = Taxonomy.FromFiling(f.Form, item.Items);
            dz.Filings.Add(item);
        }
    }

    /// <summary>The text of a filing's exhibit 99 (the press release), tags stripped; null when there is none.</summary>
    private static async Task<string?> ExhibitTextAsync(IHttpClientFactory http, EdgarRateGate gate, string cik, string accession, CancellationToken ct)
    {
        var folder = $"https://www.sec.gov/Archives/edgar/data/{cik.TrimStart('0')}/{accession.Replace("-", "")}";
        var index = await EdgarGetAsync(http, gate, folder + "/index.json", ct);
        if (index is null) return null;
        string? name = null;
        try
        {
            using var doc = JsonDocument.Parse(index);
            var files = doc.RootElement.GetProperty("directory").GetProperty("item").EnumerateArray()
                .Select(e => Str(e, "name")).Where(n => n.EndsWith(".htm", StringComparison.OrdinalIgnoreCase) || n.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)).ToList();
            name = files.FirstOrDefault(n => Regex.IsMatch(n, @"ex[-_]?99|exhibit[-_]?99|ex991", RegexOptions.IgnoreCase));
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException) { return null; }
        if (name is null) return null;
        var html = await EdgarGetAsync(http, gate, $"{folder}/{name}", ct);
        if (html is null) return null;
        var text = WebUtility.HtmlDecode(Regex.Replace(Regex.Replace(html, @"<(script|style)[^>]*>.*?</\1>", " ", RegexOptions.Singleline | RegexOptions.IgnoreCase), "<[^>]+>", " "));
        text = Regex.Replace(text, @"\s+", " ").Trim();
        text = Regex.Replace(text, @"^((\d{1,3}|\S+\.html?|EX-99[\.\d]*|Exhibit\s+99[\.\d]*|Document|PRESS RELEASE|NEWS RELEASE|-|–)\s+)+", "", RegexOptions.IgnoreCase);
        return text.Length > 6000 ? text[..6000] : text;
    }

    // ── Profile, path, catalyst ──────────────────────────────────────────

    private static void Profile(Dossier dz, Row r, Series se, Submissions? sub, SharesOutstandingHistory? facts, string? cik, List<SplitEvent> corp, List<AlpacaNewsItem> news,
        List<Row> eligible, Dictionary<string, string> names)
    {
        dz.Cik = cik;
        dz.Adr = GapperUniverseBuilder.IsAdr(dz.Name, ["ADR", "ADS"]) || dz.Name.Contains("Depositary", StringComparison.OrdinalIgnoreCase);
        dz.ForeignFiler = sub?.Foreign ?? false;
        dz.Country = sub?.Country;

        // every split known for the name: Alpaca corporate actions, the split-adjusted bars, and unlisted ratio changes inferred above
        var splits = corp.Select(x => (Ex: x.ExDate, Ratio: (double)x.PriceFactor, Source: "Alpaca corporate action")).ToList();
        foreach (var ex in se.ListedEx) if (!splits.Any(x => x.Ex == ex)) { var i = se.Index(ex); splits.Add((ex, se.Factor(se.Bars[i - 1].Date, ex), "Alpaca split-adjusted bars")); }
        foreach (var (ex, f, src) in se.Inferred) if (!splits.Any(x => x.Ex == ex)) splits.Add((ex, f, "inferred: " + src));
        var lastRs = splits.Where(x => x.Ratio > 1.5 && x.Ex <= r.D).OrderByDescending(x => x.Ex).FirstOrDefault();
        if (lastRs.Ratio > 0)
        {
            var info = new SplitInfo { ExDate = lastRs.Ex.ToString("yyyy-MM-dd"), Ratio = Math.Round(lastRs.Ratio, 2), Kind = Taxonomy.IsAdsText(dz.Name) || dz.Adr ? "reverse split / ADS ratio change" : "reverse split", Source = lastRs.Source,
                DaysBefore = MarketCalendar.TradingDays(lastRs.Ex, r.D).Count - 1 };
            var exOpen = MinuteBarMath.ToUtc(lastRs.Ex, RthOpen);
            var ann = news.FirstOrDefault(n => n.CreatedAt < exOpen && n.CreatedAt >= exOpen.AddDays(-60) && Relevant(n, r.Symbol, dz.Name) && Taxonomy.IsSplitText(n.Headline + " " + n.Summary));
            if (ann is not null)
            {
                info.AnnouncedEt = EtMin(ann.CreatedAt);
                info.AnnouncedDaysBefore = Math.Round((exOpen - ann.CreatedAt).TotalDays, 1);
                info.AnnouncementHeadline = Trunc(ann.Headline, 140);
            }
            dz.LastReverseSplit = info;
            dz.DaysSinceReverseSplit = info.DaysBefore;
            dz.PostSplit0To10 = info.DaysBefore <= 10;
        }

        if (facts is not null && int.TryParse(cik, CultureInfo.InvariantCulture, out var cikN) && facts.KnownBefore(cikN, r.D) is { } fact)
        {
            double shares = fact.Shares;
            foreach (var x in splits) if (x.Ex > fact.End && x.Ex <= r.D && x.Ratio > 0) shares /= x.Ratio;
            if (shares is >= 50_000 and <= 5_000_000_000)
            {
                dz.SharesOutstanding = Math.Round(shares);
                dz.SharesAsOf = $"{fact.End:yyyy-MM-dd} (filed {fact.Filed:yyyy-MM-dd}, split-adjusted to D)";
                dz.FloatUnder10M = shares < 10_000_000;
            }
            else dz.SharesAsOf = $"XBRL value {fact.Shares:N0} as of {fact.End:yyyy-MM-dd} gives {shares:N0} after later splits: implausible, left unknown";
        }
        if (sub is not null)
        {
            var dil = sub.Filings.Where(f => DilutionForms.Contains(f.Form) && f.Filed <= r.D && f.Filed >= r.D.AddDays(-90)).OrderBy(f => f.Filed).ToList();
            dz.DilutionFilings90d = dil.Select(f => $"{f.Form} {f.Filed:yyyy-MM-dd}").Distinct().ToList();
            dz.RecentDilutionFiling = dil.Count > 0;
        }
        var lookback = MinuteBarMath.ToUtc(r.D.AddDays(-90), TimeSpan.Zero);
        var cutoff = MinuteBarMath.ToUtc(r.D, SessionEnd);
        dz.ElocMention = news.Any(n => n.CreatedAt >= lookback && n.CreatedAt <= cutoff && Relevant(n, r.Symbol, dz.Name) && Taxonomy.Eloc.IsMatch(n.Headline + " " + n.Summary));

        var prior = MarketCalendar.PriorTradingDays(r.D, 30).Where(x => x > OpenedFrom).ToHashSet();
        dz.PriorTopMoverWindowSessions = prior.Count;
        dz.PriorTopMoverDays = eligible.Count(x => x.Symbol == r.Symbol && prior.Contains(x.D) && !x.Dropped && x.Rank <= 20 && x.Gain >= 0.25);
    }

    private static void PricePath(Dossier dz, Row r, List<Candle> bars, Series se, DateOnly todayEt, DateTime nowUtc, HaltHistory halts)
    {
        double prev = r.AdjPrev;
        var start = MinuteBarMath.ToUtc(r.D, SessionStart);
        const int n = 16 * 60;
        var vol = new double[n]; var hi = new double[n]; var lo = new double[n]; var cl = new double[n]; var dol = new double[n];
        Array.Fill(hi, double.NaN); Array.Fill(lo, double.NaN); Array.Fill(cl, double.NaN);
        foreach (var b in bars)
        {
            int i = (int)Math.Round((b.StartUtc - start).TotalMinutes);
            if (i < 0 || i >= n) continue;
            vol[i] += (double)b.Volume;
            hi[i] = double.IsNaN(hi[i]) ? (double)b.High : Math.Max(hi[i], (double)b.High);
            lo[i] = double.IsNaN(lo[i]) ? (double)b.Low : Math.Min(lo[i], (double)b.Low);
            cl[i] = (double)b.Close;
            dol[i] += (double)b.Volume * OnsetTrack.BarPrice(b);
        }
        string T(int i) => (SessionStart + TimeSpan.FromMinutes(i)).ToString(@"hh\:mm", CultureInfo.InvariantCulture);
        int rth = (int)(RthOpen - SessionStart).TotalMinutes, rthEnd = (int)(RthClose - SessionStart).TotalMinutes;

        double pmHigh = double.NaN; int pmHighI = -1;
        for (int i = 0; i < rth; i++) { dz.PmVolume += vol[i]; if (!double.IsNaN(hi[i]) && (double.IsNaN(pmHigh) || hi[i] > pmHigh)) { pmHigh = hi[i]; pmHighI = i; } }
        if (pmHighI >= 0) { dz.PmHigh = Round(pmHigh, 4); dz.PmHighGain = Math.Round(pmHigh / prev - 1, 4); dz.PmHighEt = T(pmHighI); }
        var openBar = bars.FirstOrDefault(b => b.StartUtc >= MinuteBarMath.ToUtc(r.D, RthOpen) && b.StartUtc < MinuteBarMath.ToUtc(r.D, RthOpen.Add(TimeSpan.FromMinutes(5))));
        if (openBar is not null) { dz.Open = Round((double)openBar.Open, 4); dz.OpenGain = Math.Round((double)openBar.Open / prev - 1, 4); }

        int hod = -1; double hodPx = double.NaN;
        for (int i = 0; i < n; i++) if (!double.IsNaN(hi[i]) && (double.IsNaN(hodPx) || hi[i] > hodPx)) { hodPx = hi[i]; hod = i; }
        if (hod < 0) { dz.Close = Round(r.Bar.Close, 4); dz.CloseGain = Math.Round(r.Bar.Close / prev - 1, 4); return; }
        dz.Hod = Round(hodPx, 4); dz.HodEt = T(hod); dz.RunPct = Math.Round(hodPx / prev - 1, 4);
        int rthHod = -1;
        for (int i = rth; i < rthEnd; i++) if (!double.IsNaN(hi[i]) && (rthHod < 0 || hi[i] > hi[rthHod])) rthHod = i;
        if (rthHod >= 0) { dz.RthHigh = Round(hi[rthHod], 4); dz.RthHighEt = T(rthHod); }

        // ignition: first minute at or before the high whose volume is >= 10x the trailing 30-minute average (empty minutes count
        // as zero) with >= $25k traded and a close above everything printed so far that session (and above the prior close)
        double rangeHigh = prev;
        for (int i = 0; i <= hod; i++)
        {
            double sum = 0; int cnt = 0;
            for (int k = Math.Max(0, i - 30); k < i; k++) { sum += vol[k]; cnt++; }
            double avg = cnt == 0 ? 0 : sum / 30.0;
            if (!double.IsNaN(cl[i]) && vol[i] > 0 && dol[i] >= 25_000 && vol[i] >= 10 * avg && cl[i] > rangeHigh && i > 0)
            {
                dz.IgnitionEt = T(i);
                dz.IgnitionVolMultiple = avg > 0 ? Math.Round(vol[i] / avg, 1) : null;
                dz.IgnitionPrice = Round(cl[i], 4);
                dz.IgnitionToHodMin = hod - i;
                break;
            }
            if (!double.IsNaN(hi[i])) rangeHigh = Math.Max(rangeHigh, hi[i]);
        }
        if (dz.IgnitionEt is null)
        {
            // no discrete ignition: the stock was already up at its first print (gapped overnight or at 04:00)
            int first = Array.FindIndex(cl, x => !double.IsNaN(x));
            if (first >= 0 && first <= hod && hi[first] / prev - 1 >= 0.20)
            {
                dz.IgnitionEt = T(first);
                dz.IgnitionIsFirstPrint = true;
                dz.IgnitionPrice = Round(cl[first], 4);
                dz.IgnitionToHodMin = hod - first;
            }
        }

        // top footprint
        double cumV = 0, cumD = 0;
        var vwap = new double[n];
        for (int i = 0; i < n; i++) { cumV += vol[i]; cumD += dol[i]; vwap[i] = cumV > 0 ? cumD / cumV : double.NaN; }
        for (int i = hod + 1; i < n; i++)
            if (!double.IsNaN(cl[i]) && cl[i] < vwap[i]) { dz.VwapLossEt = T(i); dz.HodToVwapLossMin = i - hod; break; }
        int maxVolI = Array.IndexOf(vol, vol.Max());
        dz.VolumeClimaxAtTop = Math.Abs(maxVolI - hod) <= 5;
        int pull = -1;
        for (int i = hod + 1; i < n; i++) if (!double.IsNaN(lo[i]) && lo[i] <= hodPx * 0.9) { pull = i; break; }
        if (pull > 0)
        {
            double second = double.NaN;
            for (int i = pull + 1; i < Math.Min(n, rthEnd); i++) if (!double.IsNaN(hi[i]) && (double.IsNaN(second) || hi[i] > second)) second = hi[i];
            if (!double.IsNaN(second)) { dz.SecondaryHighPctOfHod = Math.Round(second / hodPx, 3); dz.LowerHigh = second >= hodPx * 0.85 && second < hodPx; }
        }
        var hodUtc = start.AddMinutes(hod);
        foreach (var h in halts.Between(r.Symbol, MinuteBarMath.ToUtc(r.D, SessionStart), MinuteBarMath.ToUtc(r.D, SessionEnd)))
            dz.Halts.Add(new Halt { Time = EtMin(h.HaltUtc)[11..], Reason = h.Reason, Resume = h.ResumeTradeUtc is { } rt ? EtMin(rt)[11..] : null });
        dz.HaltNearTop = halts.Between(r.Symbol, hodUtc.AddMinutes(-15), hodUtc.AddMinutes(16)).Any();

        dz.Close = Round(r.Bar.Close, 4);
        dz.CloseGain = Math.Round(r.Bar.Close / prev - 1, 4);
        dz.FadeToClosePct = Math.Round(r.Bar.Close / hodPx - 1, 4);
        dz.RunGivenBackByClose = hodPx > prev ? Math.Round((hodPx - r.Bar.Close) / (hodPx - prev), 3) : null;
        int di = se.Index(r.D);
        var nowEt = MinuteBarMath.ToEastern(nowUtc);
        if (di >= 0 && di + 1 < se.Bars.Count)
        {
            var b1 = se.Bars[di + 1];
            double f1 = se.Factor(b1.Date, r.D);
            dz.D1Close = Round(b1.Close * f1, 4); dz.D1High = Round(b1.High * f1, 4);
            dz.D1Partial = b1.Date == todayEt && nowEt.TimeOfDay < new TimeSpan(16, 5, 0);
            dz.FadeToD1Pct = Math.Round(b1.Close * f1 / hodPx - 1, 4);
            dz.D1HigherHigh = b1.High * f1 > hodPx;
        }
        if (di >= 0 && di + 2 < se.Bars.Count)
        {
            var b2 = se.Bars[di + 2];
            double f2 = se.Factor(b2.Date, r.D);
            dz.D2Close = Round(b2.Close * f2, 4);
            dz.D2Partial = b2.Date == todayEt && nowEt.TimeOfDay < new TimeSpan(16, 5, 0);
            dz.FadeToD2Pct = Math.Round(b2.Close * f2 / hodPx - 1, 4);
            dz.RunGivenBackByD2 = hodPx > prev ? Math.Round((hodPx - b2.Close * f2) / (hodPx - prev), 3) : null;
        }
    }

    /// <summary>
    /// A news item is about the company when it tags at most four symbols, or its headline names the ticker or the first
    /// word of the company name: market wraps ("US Stocks Mixed; Apple Shares Slide") tag dozens of tickers.
    /// </summary>
    private static bool Relevant(AlpacaNewsItem n, string symbol, string name)
    {
        if (n.Symbols.Count <= 4) return true;
        if (Regex.IsMatch(n.Headline, $@"\b{Regex.Escape(symbol)}\b")) return true;
        var first = name.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(w => w.Length >= 4 && !w.Equals("The", StringComparison.OrdinalIgnoreCase));
        return first is not null && n.Headline.Contains(first.TrimEnd(',', '.'), StringComparison.OrdinalIgnoreCase);
    }

    private static void Catalyst(Dossier dz, Row r, List<AlpacaNewsItem> news)
    {
        var from = MinuteBarMath.ToUtc(r.D.AddDays(-2), TimeSpan.Zero);
        var to = MinuteBarMath.ToUtc(r.D, SessionEnd);
        foreach (var n in news.Where(n => n.CreatedAt >= from && n.CreatedAt <= to && Relevant(n, r.Symbol, dz.Name)))
        {
            var text = n.Headline + " " + n.Summary;
            var type = Taxonomy.Classify(text, n.Headline);
            // "X proposes to acquire <this company>" makes this company the target, not the acquirer
            var firstWord = dz.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? r.Symbol;
            if (type == Taxonomy.Acquirer && Regex.IsMatch(n.Headline, $@"acquir\w*\s+(all of\s+|the\s+)?({Regex.Escape(r.Symbol)}|{Regex.Escape(firstWord)})\b", RegexOptions.IgnoreCase)) type = Taxonomy.Target;
            dz.News.Add(new NewsItem { Utc = n.CreatedAt, Time = EtMin(n.CreatedAt), Source = n.Source, Headline = Trunc(n.Headline, 200), Type = type, Reaction = Taxonomy.IsReaction(n.Headline) });
        }
        var priorClose = MinuteBarMath.ToUtc(r.P, RthClose);
        var hodUtc = dz.HodEt is { } he ? MinuteBarMath.ToUtc(r.D, TimeSpan.Parse(he, CultureInfo.InvariantCulture)) : to;
        var ignUtc = dz.IgnitionEt is { } ie ? MinuteBarMath.ToUtc(r.D, TimeSpan.Parse(ie, CultureInfo.InvariantCulture)) : (DateTime?)null;

        // the catalyst: the earliest classified, non-reaction item between the prior close and the high; else a classified item
        // from D-2 to the prior close; else a classified 8-K/6-K/424B in that span; else a reaction article's stated reason
        NewsItem? pick = dz.News.Where(n => !n.Reaction && n.Type != Taxonomy.OtherNews && n.Utc >= priorClose && n.Utc <= hodUtc).OrderBy(n => n.Utc).FirstOrDefault()
            ?? dz.News.Where(n => !n.Reaction && n.Type != Taxonomy.OtherNews && n.Utc < priorClose).OrderByDescending(n => n.Utc).FirstOrDefault();
        string? filingType = null; FilingItem? filingPick = null;
        if (pick is null)
        {
            // a classified exhibit press release first (latest before the high), then the latest typed filing
            var usable = dz.Filings.Where(f => f.Type is not null && (f.AcceptedUtc is null || f.AcceptedUtc <= hodUtc)).OrderByDescending(f => f.AcceptedUtc ?? DateTime.MinValue).ToList();
            filingPick = usable.FirstOrDefault(f => f.Type != Taxonomy.OtherNews) ?? usable.FirstOrDefault();
            filingType = filingPick?.Type;
        }
        NewsItem? reaction = null;
        if (pick is null && filingType is null)
            reaction = dz.News.Where(n => n.Reaction && n.Type != Taxonomy.OtherNews && n.Utc <= to).OrderBy(n => n.Utc).FirstOrDefault();
        NewsItem? anyNews = dz.News.Where(n => !n.Reaction && n.Utc >= priorClose && n.Utc <= hodUtc).OrderBy(n => n.Utc).FirstOrDefault();

        bool weakFiling = filingType == Taxonomy.OtherNews;
        if (pick is not null) Set(pick.Type, pick.Utc, pick.Headline, pick.Source, false);
        else if (filingType is not null && !(weakFiling && (dz.PostSplit0To10 || anyNews is not null))) Set(filingType, filingPick!.AcceptedUtc,
            $"{filingPick.Form}{(filingPick.Items is { } it ? " items " + it : "")}{(filingPick.ExhibitLead is { } lead ? ": " + lead : filingPick.Description is { } ds ? " - " + ds : "")}", "EDGAR", false);
        else if (reaction is not null && !dz.PostSplit0To10) Set(reaction.Type, reaction.Utc, reaction.Headline, reaction.Source, true);
        else if (dz.PostSplit0To10 && dz.LastReverseSplit is { } rs) Set(Taxonomy.ReverseSplit, MinuteBarMath.ToUtc(DateOnly.Parse(rs.ExDate, CultureInfo.InvariantCulture), RthOpen),
            $"no company-specific catalyst beyond the split; {rs.Kind} 1:{rs.Ratio:0.##} effective {rs.ExDate} ({rs.DaysBefore} sessions earlier){(anyNews is not null ? $"; other news: {Trunc(anyNews.Headline, 90)}" : "")}", rs.Source, false);
        else if (anyNews is not null) Set(Taxonomy.OtherNews, anyNews.Utc, anyNews.Headline, anyNews.Source, false);
        else Set(Taxonomy.NoCatalyst, null, dz.QqqDayPct is { } q ? $"no company news or filing D-2..D; QQQ {q:+0.0%;-0.0%} on the day" : "no company news or filing D-2..D", null, false);

        dz.Group = Taxonomy.GroupOf(dz.Catalyst);

        void Set(string type, DateTime? utc, string headline, string? source, bool viaReaction)
        {
            dz.Catalyst = type; dz.CatalystHeadline = Trunc(headline, 200); dz.CatalystSource = source; dz.CatalystViaReaction = viaReaction;
            if (utc is { } u)
            {
                dz.CatalystEt = EtMin(u);
                dz.CatalystBeforeClose = u < priorClose;
                // minutes to the high only for a catalyst published since the prior close (a days-old filing or split date is not a trigger time)
                if (dz.HodEt is not null && u <= hodUtc && !dz.CatalystBeforeClose) dz.CatalystToHodMin = Math.Round((hodUtc - u).TotalMinutes);
                if (ignUtc is { } ig && u <= ig) dz.CatalystToIgnitionMin = Math.Round((ig - u).TotalMinutes);
            }
        }
    }

    // ── Summary and markdown ─────────────────────────────────────────────

    private static object Summarize(List<Dossier> ds)
    {
        object Stats(IEnumerable<Dossier> g)
        {
            var l = g.ToList();
            return new
            {
                n = l.Count, symbols = l.Select(x => x.Symbol).Distinct().Count(),
                medianRun = Median(l.Select(x => x.RunPct)), medianCatalystToHodMin = Median(l.Select(x => x.CatalystToHodMin)),
                medianIgnitionToHodMin = Median(l.Select(x => x.IgnitionToHodMin)),
                medianFadeClose = Median(l.Select(x => x.FadeToClosePct)), medianFadeD1 = Median(l.Select(x => x.FadeToD1Pct)), medianFadeD2 = Median(l.Select(x => x.FadeToD2Pct)),
                medianRunGivenBackD2 = Median(l.Select(x => x.RunGivenBackByD2)),
                d1HigherHigh = l.Count(x => x.D1HigherHigh),
                r4Alarms = l.Count(x => x.R4.AlarmEt is not null), r4Hits = l.Count(x => x.R4.Hit),
            };
        }
        return new
        {
            n = ds.Count,
            byCatalyst = ds.GroupBy(x => x.Catalyst).OrderByDescending(g => g.Count()).ToDictionary(g => g.Key, g => Stats(g)),
            byGroup = ds.GroupBy(x => x.Group).OrderByDescending(g => g.Count()).ToDictionary(g => g.Key, g => Stats(g)),
            postSplit0To10 = Stats(ds.Where(x => x.PostSplit0To10)),
            notPostSplit = Stats(ds.Where(x => !x.PostSplit0To10)),
            shares = new
            {
                reverseSplitWithin10Sessions = ds.Count(x => x.PostSplit0To10),
                reverseSplitWithin60Sessions = ds.Count(x => x.DaysSinceReverseSplit is <= 60),
                reverseSplitAnnouncedInNews = ds.Count(x => x.LastReverseSplit?.AnnouncedEt is not null && x.DaysSinceReverseSplit is <= 60),
                dilutionFiling90d = ds.Count(x => x.RecentDilutionFiling),
                elocMention90d = ds.Count(x => x.ElocMention),
                floatUnder10M = ds.Count(x => x.FloatUnder10M == true), floatKnown = ds.Count(x => x.FloatUnder10M is not null),
                priorTopMover = ds.Count(x => x.PriorTopMoverDays > 0),
                adr = ds.Count(x => x.Adr), foreignFiler = ds.Count(x => x.ForeignFiler),
                catalystViaReactionOnly = ds.Count(x => x.CatalystViaReaction),
                artifactCorrected = ds.Count(x => x.Artifact is not null),
            },
            ignition = new
            {
                none = ds.Count(x => x.IgnitionEt is null),
                firstPrintGap = ds.Count(x => x.IgnitionIsFirstPrint),
                premarket = ds.Count(x => Bucket(x.IgnitionEt) == "premarket"),
                open = ds.Count(x => Bucket(x.IgnitionEt) == "open (09:30-09:44)"),
                morning = ds.Count(x => Bucket(x.IgnitionEt) == "morning (09:45-11:59)"),
                midday = ds.Count(x => Bucket(x.IgnitionEt) == "midday/afternoon (12:00-15:59)"),
                afterHours = ds.Count(x => Bucket(x.IgnitionEt) == "after hours"),
                medianIgnitionToHodMin = Median(ds.Select(x => x.IgnitionToHodMin)),
                hodBuckets = ds.GroupBy(x => Bucket(x.HodEt)).ToDictionary(g => g.Key, g => g.Count()),
            },
            top = new
            {
                vwapLossAfterHod = ds.Count(x => x.VwapLossEt is not null), medianHodToVwapLossMin = Median(ds.Select(x => x.HodToVwapLossMin)),
                volumeClimaxAtTop = ds.Count(x => x.VolumeClimaxAtTop), haltNearTop = ds.Count(x => x.HaltNearTop), anyHalt = ds.Count(x => x.Halts.Count > 0),
                lowerHighRetest = ds.Count(x => x.LowerHigh), pulledBack10 = ds.Count(x => x.SecondaryHighPctOfHod is not null),
            },
            day2 = new
            {
                d1Known = ds.Count(x => x.D1Close is not null), d1HigherHigh = ds.Count(x => x.D1HigherHigh),
                d1CloseAboveDClose = ds.Count(x => x.D1Close is { } c1 && c1 > x.Close), d2Known = ds.Count(x => x.D2Close is not null),
                d2BelowPrior = ds.Count(x => x.D2Close is { } c2 && c2 < x.PriorClose),
                medianFadeD1 = Median(ds.Select(x => x.FadeToD1Pct)), medianFadeD2 = Median(ds.Select(x => x.FadeToD2Pct)),
                medianRunGivenBackD2 = Median(ds.Select(x => x.RunGivenBackByD2)),
            },
            r4 = new
            {
                noBars = ds.Count(x => x.R4.Status.StartsWith("no bars", StringComparison.Ordinal)),
                above15At0600 = ds.Count(x => x.R4.PmRetAt0600 is >= 0.15),
                scorable = ds.Count(x => x.R4.Scorable), forming = ds.Count(x => x.R4.Forming),
                alarms = ds.Count(x => x.R4.AlarmEt is not null), hits = ds.Count(x => x.R4.Hit),
                medianLeadMin = Median(ds.Where(x => x.R4.Hit).Select(x => (double?)x.R4.LeadMin)),
                medianLeadToHodMin = Median(ds.Where(x => x.R4.AlarmEt is not null).Select(x => (double?)x.R4.LeadToHodMin)),
                inPopulation = ds.Count(x => x.R4.InPopulation),
            },
        };
    }

    private static string Bucket(string? et)
    {
        if (et is null) return "none";
        var t = TimeSpan.Parse(et, CultureInfo.InvariantCulture);
        return t < RthOpen ? "premarket" : t < new TimeSpan(9, 45, 0) ? "open (09:30-09:44)" : t < new TimeSpan(12, 0, 0) ? "morning (09:45-11:59)" : t < RthClose ? "midday/afternoon (12:00-15:59)" : "after hours";
    }

    private static string Markdown(List<Dossier> all, object summary, List<object> artifacts, List<object> r4Days, DateOnly from, DateOnly to, int top)
    {
        var ds = all.Where(d => !d.Named).ToList();
        var named = all.Where(d => d.Named).ToList();
        var sb = new StringBuilder();
        string P(double? v) => v is { } x && double.IsFinite(x) ? x.ToString("+0%;-0%", CultureInfo.InvariantCulture) : "-";
        string N(double? v) => v is { } x && double.IsFinite(x) ? x.ToString("0", CultureInfo.InvariantCulture) : "-";
        string M(double v) => v >= 1e9 ? $"{v / 1e9:0.0}B" : v >= 1e6 ? $"{v / 1e6:0.0}M" : v >= 1e3 ? $"{v / 1e3:0}k" : v.ToString("0", CultureInfo.InvariantCulture);
        sb.AppendLine($"<!-- generated by `casestudy --from {from:yyyy-MM-dd} --to {to:yyyy-MM-dd} --top {top}` (IP-T486); {Label} -->");
        sb.AppendLine();
        sb.AppendLine("## Catalyst table");
        sb.AppendLine();
        sb.AppendLine("| Catalyst | n | names | median run | catalyst to high (min) | ignition to high (min) | close vs high | D+1 close vs high | D+2 close vs high | run given back by D+2 | D+1 higher high | R4 alarms / hits |");
        sb.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var g in ds.GroupBy(x => x.Catalyst).OrderByDescending(g => g.Count()).ThenBy(g => g.Key))
            Line(sb, g.Key, g.ToList());
        sb.AppendLine();
        sb.AppendLine("| Group | n | names | median run | catalyst to high (min) | ignition to high (min) | close vs high | D+1 close vs high | D+2 close vs high | run given back by D+2 | D+1 higher high | R4 alarms / hits |");
        sb.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var g in ds.GroupBy(x => x.Group).OrderByDescending(g => g.Count()))
            Line(sb, g.Key, g.ToList());
        Line(sb, "post-split day 0-10 (overlay)", ds.Where(x => x.PostSplit0To10).ToList());
        Line(sb, "not post-split", ds.Where(x => !x.PostSplit0To10).ToList());
        Line(sb, "all", ds);
        sb.AppendLine();
        if (named.Count > 0)
        {
            sb.AppendLine("Named stock-days (outside the ranked top N, studied on request; not in the tables above):");
            sb.AppendLine();
            sb.AppendLine("| Catalyst | n | names | median run | catalyst to high (min) | ignition to high (min) | close vs high | D+1 close vs high | D+2 close vs high | run given back by D+2 | D+1 higher high | R4 alarms / hits |");
            sb.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
            foreach (var g in named.GroupBy(x => x.Catalyst).OrderByDescending(g => g.Count()).ThenBy(g => g.Key)) Line(sb, g.Key, g.ToList());
            sb.AppendLine();
        }

        sb.AppendLine("## Stock-days");
        sb.AppendLine();
        sb.AppendLine("| Date | # | Symbol | Catalyst | Catalyst ET | Prior close | Run | HOD ET | Ignition ET | Close vs high | D+2 vs high | Split (days, ratio) | Shares out | R4 |");
        sb.AppendLine("|---|---:|---|---|---|---:|---:|---|---|---:|---:|---|---:|---|");
        foreach (var d in all)
            sb.AppendLine($"| {d.Date} | {d.Rank}{(d.Named ? " (named)" : "")} | {d.Symbol} | {d.Catalyst} | {d.CatalystEt?[5..] ?? "-"} | {d.PriorClose:0.00##} | {P(d.RunPct)} | {d.HodEt ?? "-"} | {d.IgnitionEt ?? "-"} | {P(d.FadeToClosePct)} | {P(d.FadeToD2Pct)}{(d.D2Partial ? "*" : "")} | {(d.LastReverseSplit is { } rs && d.DaysSinceReverseSplit <= 60 ? $"{d.DaysSinceReverseSplit}d 1:{rs.Ratio:0.##}" : "-")} | {(d.SharesOutstanding is { } so ? M(so) : "-")} | {R4Short(d.R4)} |");
        sb.AppendLine();
        sb.AppendLine("## Dossiers");
        foreach (var d in all)
        {
            sb.AppendLine();
            sb.AppendLine($"### {d.Date} {d.Symbol} (#{d.Rank}{(d.Named ? ", named" : "")}) - {d.Name}");
            sb.AppendLine();
            sb.AppendLine($"- **Catalyst: {d.Catalyst}**{(d.CatalystEt is { } ce ? $" at {ce} ET" : "")}{(d.CatalystSource is { } cs ? $" ({cs})" : "")}: \"{d.CatalystHeadline}\"{(d.CatalystViaReaction ? " (reason stated in a reaction article)" : "")}{(d.CatalystBeforeClose ? " (published before the prior close)" : "")}; catalyst to high {N(d.CatalystToHodMin)} min.");
            sb.AppendLine($"- **Run:** prior close ${d.PriorClose:0.00##} -> high ${d.Hod:0.00##} at {d.HodEt ?? "-"} ({P(d.RunPct)}; regular-hours high ${d.RthHigh:0.00##} at {d.RthHighEt ?? "-"}); premarket high {P(d.PmHighGain)} at {d.PmHighEt ?? "-"} on {M(d.PmVolume)} sh; open {P(d.OpenGain)}; ignition {d.IgnitionEt ?? "none"}{(d.IgnitionIsFirstPrint ? " (already >= +20% at the first print)" : d.IgnitionVolMultiple is { } iv ? $" ({iv:0}x the trailing 30-min volume)" : "")}, {N(d.IgnitionToHodMin)} min to the high; close ${d.Close:0.00##} ({P(d.CloseGain)}){(d.Partial ? " (partial session)" : "")}; ${M(d.DayDollarVolume)} traded.");
            sb.AppendLine($"- **Fade:** close {P(d.FadeToClosePct)} from the high, D+1 {P(d.FadeToD1Pct)}{(d.D1Partial ? " (partial)" : "")}, D+2 {P(d.FadeToD2Pct)}{(d.D2Partial ? " (partial)" : "")}; run given back {P(d.RunGivenBackByClose)} by the close, {P(d.RunGivenBackByD2)} by D+2; D+1 higher high: {(d.D1Close is null ? "-" : d.D1HigherHigh ? "yes" : "no")}.");
            sb.AppendLine($"- **Top:** VWAP lost {(d.VwapLossEt is { } v ? $"{v} ({d.HodToVwapLossMin} min after the high)" : "never after the high")}; volume climax at the top: {(d.VolumeClimaxAtTop ? "yes" : "no")}; lower-high retest after a 10% pullback: {(d.SecondaryHighPctOfHod is { } sh ? $"{sh:P0} of the high{(d.LowerHigh ? " (lower high)" : "")}" : "no 10% pullback")}; halts: {(d.Halts.Count == 0 ? "none" : string.Join(", ", d.Halts.Take(8).Select(h => $"{h.Time} {h.Reason}")) + (d.Halts.Count > 8 ? $" (+{d.Halts.Count - 8})" : ""))}.");
            sb.AppendLine($"- **Profile:** ADV ${M(d.Adv20Dollars)} over {d.AdvSessions} window sessions; shares outstanding {(d.SharesOutstanding is { } so ? M(so) + $" as of {d.SharesAsOf}" : "unknown")}; {(d.LastReverseSplit is { } rs ? $"last reverse split 1:{rs.Ratio:0.##} on {rs.ExDate} ({d.DaysSinceReverseSplit} sessions before; {(rs.AnnouncedEt is { } ae ? $"announced {ae} ET, {rs.AnnouncedDaysBefore} days before" : "no announcement found in Alpaca news")}; {rs.Source})" : "no reverse split found in the lookback")}; dilution filings in 90 days: {(d.DilutionFilings90d.Count == 0 ? "none" : string.Join(", ", d.DilutionFilings90d.Take(8)))}{(d.ElocMention ? "; equity-line mention in news" : "")}; prior top-mover days {d.PriorTopMoverDays} of {d.PriorTopMoverWindowSessions} window sessions; {(d.Adr ? "ADR/ADS; " : "")}{(d.ForeignFiler ? "foreign filer; " : "")}{(string.IsNullOrWhiteSpace(d.Country) ? "country unknown" : d.Country)}; QQQ {P(d.QqqDayPct)}, SPY {P(d.SpyDayPct)} on the day.");
            if (d.Artifact is not null) sb.AppendLine($"- **Adjustment:** {d.Artifact}");
            sb.AppendLine($"- **R4:** {d.R4.Status}{(d.R4.AlarmEt is { } a ? $" at {a} (gap {P(d.R4.PmRetAtAlarm)}, vol ratio {(d.R4.VolRatioAtAlarm is { } vr ? $"{vr:0.0}x" : "n/a")},{d.R4.LeadToHodMin} min before the high)" : "")}{(d.R4.LeadMin is { } lm ? $", {lm} min before the +20% touch" : "")}; gap at 06:00 {P(d.R4.PmRetAt0600)}; first +20% {d.R4.First20Et ?? "-"}{(d.R4.InPopulation ? "" : "; not in the study population (split window or ADV), added for the check")}.");
            var ev = d.News.Where(n => !n.Reaction).Take(10).Select(n => $"{n.Time[5..]} [{n.Type}] {Trunc(n.Headline, 110)}").ToList();
            if (ev.Count > 0) sb.AppendLine($"- **News D-2..D ({d.News.Count}, {d.News.Count(n => n.Reaction)} reaction):** " + string.Join("; ", ev));
            if (d.Filings.Count > 0) sb.AppendLine("- **Filings D-5..D:** " + string.Join("; ", d.Filings.Select(f => $"{f.Form} {(f.Accepted ?? f.Filed)}{(f.Items is { } it && it.Trim(',').Length > 0 ? $" items {it}" : "")}{(f.ExhibitLead is { } l ? $" [{f.Type}] \"{Trunc(l, 120)}\"" : "")}")));
        }
        sb.AppendLine();
        sb.AppendLine("## Screener and split artifacts");
        sb.AppendLine();
        sb.AppendLine("| Date | Symbol | Raw gain to high | Real gain | Factor | Source |");
        sb.AppendLine("|---|---|---:|---:|---:|---|");
        foreach (var a in artifacts)
        {
            var e = JsonSerializer.SerializeToElement(a);
            sb.AppendLine($"| {e.GetProperty("date")} | {e.GetProperty("symbol")} | {P(e.GetProperty("rawGainHigh").GetDouble())} | {P(e.GetProperty("adjustedGainHigh").GetDouble())} | {e.GetProperty("factor").GetDouble():0.####} | {e.GetProperty("source")} |");
        }
        return sb.ToString();

        void Line(StringBuilder b, string key, List<Dossier> l)
        {
            if (l.Count == 0) return;
            b.AppendLine($"| {key} | {l.Count} | {l.Select(x => x.Symbol).Distinct().Count()} | {P(Median(l.Select(x => x.RunPct)))} | {N(Median(l.Select(x => x.CatalystToHodMin)))} | {N(Median(l.Select(x => x.IgnitionToHodMin)))} | {P(Median(l.Select(x => x.FadeToClosePct)))} | {P(Median(l.Select(x => x.FadeToD1Pct)))} | {P(Median(l.Select(x => x.FadeToD2Pct)))} | {P(Median(l.Select(x => x.RunGivenBackByD2)))} | {l.Count(x => x.D1HigherHigh)} | {l.Count(x => x.R4.AlarmEt is not null)} / {l.Count(x => x.R4.Hit)} |");
        }
    }

    private static string R4Short(R4Check c) => c.AlarmEt is { } a ? $"{a}{(c.Hit ? $" hit, {c.LeadMin}m lead" : "")}" : c.Status.StartsWith("gap >= 15%", StringComparison.Ordinal) ? ">=15% at 06:00" : c.Status.StartsWith("already", StringComparison.Ordinal) ? ">=20% at 06:00" : c.Status.StartsWith("no bars", StringComparison.Ordinal) ? "no premarket" : "none";

    // ── Helpers ──────────────────────────────────────────────────────────

    private static double? Median(IEnumerable<double?> values)
    {
        var l = values.Where(v => v is { } x && double.IsFinite(x)).Select(v => v!.Value).OrderBy(x => x).ToList();
        if (l.Count == 0) return null;
        return l.Count % 2 == 1 ? l[l.Count / 2] : (l[l.Count / 2 - 1] + l[l.Count / 2]) / 2;
    }

    private static double Nearest(double jump) => CommonRatios.OrderBy(x => Math.Abs(Math.Log(x / jump))).First();
    private static double? Finite(float v) => float.IsFinite(v) ? Math.Round(v, 4) : null;
    private static double Round(double v, int d) => Math.Round(v, d);
    private static string WindowEt(DateOnly d, int idx) => (AlarmSweep.WindowStartEt + TimeSpan.FromMinutes(idx)).ToString(@"hh\:mm", CultureInfo.InvariantCulture);
    private static string Et(DateTime utc) => MinuteBarMath.ToEastern(utc).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " ET";
    private static string EtMin(DateTime utc) => MinuteBarMath.ToEastern(utc).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    private static string Trunc(string s, int n) => s.Length <= n ? s : s[..(n - 1)] + "…";
    private static string Str(JsonElement e, string key) => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    private static List<string> Arr(JsonElement e, string key) =>
        e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() ?? "" : x.ToString()).ToList() : [];

    private static DateOnly? DateFlag(IReadOnlyDictionary<string, string?> flags, string key) =>
        flags.TryGetValue(key, out var v) && DateOnly.TryParseExact(v, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
}

/// <summary>The catalyst taxonomy the user set for IP-T486, as headline/summary keyword rules (first match in priority order wins).</summary>
internal static partial class Taxonomy
{
    public const string ReverseSplit = "reverse split / ADS ratio change";
    public const string Target = "acquisition (target)";
    public const string Acquirer = "acquisition (acquirer)";
    public const string Merger = "merger";
    public const string Investment = "outside investment / PIPE / strategic stake";
    public const string Offering = "offering / dilution";
    public const string Fda = "FDA / clinical";
    public const string EarnBeat = "earnings beat";
    public const string EarnMiss = "earnings miss";
    public const string GuideUp = "guidance raised";
    public const string GuideDown = "guidance cut";
    public const string Earnings = "earnings (unrated)";
    public const string Uplisting = "uplisting";
    public const string Buyback = "buyback";
    public const string Loi = "non-binding LOI / MOU";
    public const string Contract = "contract / partnership";
    public const string Analyst = "analyst upgrade";
    public const string Theme = "theme pivot (AI, crypto, energy)";
    public const string Restructuring = "restructuring / Chapter 11 (added)";
    public const string OtherNews = "other company news";
    public const string NoCatalyst = "no catalyst (market beta)";

    private const RegexOptions O = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    private static readonly Regex SplitRx = new(@"reverse (stock |share )?split|share consolidation|consolidation of (its )?(ordinary )?shares|ratio change|ADS ratio|change (in|of|to) (the )?(ADS|ADR) ratio|\b1-for-\d+|one-for-(two|three|four|five|six|seven|eight|nine|ten|\w+ty|hundred)", O);
    private static readonly Regex TargetRx = new(@"to be acquired|acquired by|agrees? to be (acquired|bought|purchased)|take[- ]private|go(ing)?[- ]private|tender offer|per share in cash|all[- ]cash (deal|transaction|offer|acquisition)|buyout|receives? .{0,40}(proposal|offer|bid) to (acquire|purchase|buy)|unsolicited (proposal|offer)|enters into .{0,40}agreement to be acquired|to sell (the company|itself)", O);
    private static readonly Regex MergerRx = new(@"\bmerger\b|merge with|to merge|business combination|combine with|all-stock (deal|merger|combination)", O);
    private static readonly Regex InvestRx = new(@"strategic investment|investment from|\binvest(s|ing|ment of)? \$|\$\s?[\d.,]+\s?(million|billion|mn|bn|M|B)\b.{0,30}(strategic |equity )?investment|equity investment|\bPIPE\b|stake in (the company|us)|(acquires?|takes?|buys?) .{0,30}stake|anchor investor|strategic (investor|partner) .{0,30}(invest|stake)|led by .{0,40}(capital|partners|ventures)", O);
    private static readonly Regex AcquirerRx = new(@"to acquire|acquires|acquisition of|completes (the )?acquisition|closes (the )?acquisition|agreement to acquire|signs .{0,40}acquire", O);
    private static readonly Regex OfferingRx = new(@"public offering|registered direct|private placement|pricing of|prices .{0,40}offering|priced .{0,40}offering|at-the-market|\bATM (program|facility|offering)|equity line|\bELOC\b|equity purchase agreement|securities purchase agreement|warrant (inducement|exercise)|shelf registration|convertible (senior )?(note|debenture)s?|\bdirect offering|follow-on offering|\bunderwritten\b", O);
    private static readonly Regex FdaRx = new(@"\bFDA\b|clinical|\btrial\b|phase (1|2|3|I|II|III)\b|topline|\bIND\b|\bNDA\b|\bBLA\b|510\(k\)|orphan drug|fast track|breakthrough therapy|\bEMA\b|\bCE mark|patients? (dosed|enrolled)|data readout|efficacy", O);
    private static readonly Regex EarningsRx = new(@"earnings|(first|second|third|fourth|1st|2nd|3rd|4th|Q[1-4]|fiscal|full[- ]year|quarter(ly)?).{0,40}(results|revenue|EPS|financial)|reports? .{0,40}(revenue|EPS|net income|results)", O);
    private static readonly Regex BeatRx = new(@"\bbeat(s)?\b|\btops?\b|above (consensus|estimates|expectations)|exceed(s|ed)? (consensus|estimates|expectations)|record (revenue|quarter|results)|surpass", O);
    private static readonly Regex MissRx = new(@"\bmiss(es|ed)?\b|below (consensus|estimates|expectations)|falls? short", O);
    private static readonly Regex GuideUpRx = new(@"(rais(es|ed|ing)|boost(s|ed)|lift(s|ed)|increas(es|ed)|up(s|ped)) .{0,30}(guidance|outlook|forecast)", O);
    private static readonly Regex GuideDownRx = new(@"(cut(s)?|lower(s|ed)|reduc(es|ed)|slash(es|ed)|withdraw(s|n)?) .{0,30}(guidance|outlook|forecast)", O);
    private static readonly Regex UplistRx = new(@"uplist|approved (for|to) (list|trade) on (the )?(Nasdaq|NYSE)|(transfer|move)s? .{0,30}listing to (the )?(Nasdaq|NYSE)|begin(s)? trading on (the )?(Nasdaq|NYSE)", O);
    private static readonly Regex BuybackRx = new(@"buyback|share repurchase|stock repurchase|repurchase (program|plan|authorization)|repurchase of up to|authori[sz]\w* .{0,40}repurchase|redemption of .{0,60}(preferred|convertible|warrants?)|redeem(s|ed)? .{0,40}(preferred|convertible|warrants?)", O);
    private static readonly Regex RestructRx = new(@"chapter 11|recapitali[sz]ation|restructuring support|debt restructuring|bankruptcy|debt[- ]for[- ]equity|notes? for equity|exchang\w* .{0,40}notes? .{0,20}for (equity|shares)", O);
    private static readonly Regex LoiRx = new(@"letter of intent|\bLOI\b|non-binding|term sheet|memorandum of understanding|\bMOU\b", O);
    private static readonly Regex ContractRx = new(@"contract|partnership|partner(s|ed|ing)? with|collaborat|agreement with|awarded .{0,30}(contract|order|grant|project|tender)|(contract|order|tender) award|purchase order|\border(s)? (from|for|worth)|supply agreement|distribution (agreement|deal)|exclusive .{0,30}(deal|agreement|rights)|deal with|deploy|joint venture|licens(e|ing) agreement|selected (by|as)|pilot (program|project)|strategic alliance|teams? (up )?with", O);
    private static readonly Regex AnalystRx = new(@"upgrade|initiates? coverage|initiated (at|with)|price target|outperform|overweight|buy rating|strong buy", O);
    private static readonly Regex ThemeRx = new(@"\bAI\b|artificial intelligence|crypto|bitcoin|\bBTC\b|ethereum|\bETH\b|solana|\bXRP\b|digital asset|treasury (strategy|reserve)|blockchain|\btoken|stablecoin|data cent(er|re)|\bGPU|\bNvidia|\b[BH][123]00\b|quantum|nuclear|uranium|energy storage|hydrogen|rare earth|\bdrone|robot", O);
    private static readonly Regex ReactionRx = new(@"shares (are )?(trading|moving|up|down|higher|lower|soar|jump|surge|spike|rise|fall|climb|rally)|stocks? (moving|to watch)|\bmovers\b|gainers|losers|\bwhy (is )?.{0,40}(stock|shares)|mid-?day|pre-?market (session|movers|trading|gainers)|after-?hours (movers|trading)|on the move|\b(soars|jumps|surges|spikes|rallies|skyrockets|rockets|plunges|tumbles|sinks|explodes|rips|halted)\b|what's going on|here's why|trading (higher|lower)|stock is (up|down)|\b(surged|soared|jumped|spiked|rallied|rally|trending)\b|resumes? trad(e|ing)|circuit breaker", O);
    public static readonly Regex Eloc = new(@"equity line|\bELOC\b|equity purchase (agreement|facility)|committed equity|standby equity|share purchase agreement with .{0,40}(capital|fund|partners)", O);
    private static readonly Regex RatioRx = new(@"\b(\d{1,4})[- ]for[- ](\d{1,4})\b|\b1\s?:\s?(\d{1,4})\b", O);
    private static readonly Regex RepresentingRx = new(@"representing (\d[\d,]*(?:\.\d+)?)", O);
    private static readonly Regex AdsRx = new(@"\bADS\b|\bADR\b|American Depositary", O);

    public static bool IsSplitText(string text) => SplitRx.IsMatch(text);
    public static bool IsAdsText(string text) => AdsRx.IsMatch(text);
    public static bool IsReaction(string headline) => ReactionRx.IsMatch(headline);

    public static double? ParseRatio(string text)
    {
        var reps = RepresentingRx.Matches(text).Select(m => double.TryParse(m.Groups[1].Value.Replace(",", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0).Where(v => v > 0).ToList();
        if (reps.Count >= 2 && reps[1] / reps[0] is > 1.5 and var rr) return rr;
        foreach (Match m in RatioRx.Matches(text))
        {
            if (m.Groups[3].Success && double.TryParse(m.Groups[3].Value, CultureInfo.InvariantCulture, out var c) && c > 1) return c;
            if (double.TryParse(m.Groups[1].Value, CultureInfo.InvariantCulture, out var a) && double.TryParse(m.Groups[2].Value, CultureInfo.InvariantCulture, out var b) && a > 0 && b / a > 1.5) return b / a;
        }
        return null;
    }

    public static string Classify(string text, string headline)
    {
        if (LoiRx.IsMatch(text) && (TargetRx.IsMatch(text) || AcquirerRx.IsMatch(text) || MergerRx.IsMatch(text) || ContractRx.IsMatch(text))) return Loi;
        if (TargetRx.IsMatch(text)) return Target;
        if (RestructRx.IsMatch(headline)) return Restructuring;
        if (InvestRx.IsMatch(headline) && Regex.IsMatch(headline, @"\$\s?[\d.,]+\s?(million|billion|mn|bn|M|B)\b|strategic|stake", O)) return Investment;
        if (SplitRx.IsMatch(headline)) return ReverseSplit;
        if (MergerRx.IsMatch(headline)) return Merger;
        if (InvestRx.IsMatch(headline) && !OfferingRx.IsMatch(headline.Replace("private placement", "", StringComparison.OrdinalIgnoreCase))) return Investment;
        if (OfferingRx.IsMatch(headline)) return InvestRx.IsMatch(text) && Regex.IsMatch(text, "strategic|premium", O) ? Investment : Offering;
        if (AcquirerRx.IsMatch(headline)) return Acquirer;
        if (FdaRx.IsMatch(headline)) return Fda;
        if (EarningsRx.IsMatch(headline))
        {
            if (GuideUpRx.IsMatch(text)) return GuideUp;
            if (GuideDownRx.IsMatch(text)) return GuideDown;
            if (MissRx.IsMatch(text)) return EarnMiss;
            if (BeatRx.IsMatch(text)) return EarnBeat;
            return Earnings;
        }
        if (GuideUpRx.IsMatch(headline)) return GuideUp;
        if (GuideDownRx.IsMatch(headline)) return GuideDown;
        if (UplistRx.IsMatch(headline)) return Uplisting;
        if (BuybackRx.IsMatch(headline)) return Buyback;
        if (LoiRx.IsMatch(headline)) return Loi;
        if (MergerRx.IsMatch(text)) return Merger;
        if (ContractRx.IsMatch(headline)) return ThemeRx.IsMatch(headline) && Regex.IsMatch(headline, "pivot|treasury|strategy|transform|enter", O) ? Theme : Contract;
        if (AnalystRx.IsMatch(headline)) return Analyst;
        if (ThemeRx.IsMatch(headline)) return Theme;
        if (SplitRx.IsMatch(text)) return ReverseSplit;
        if (OfferingRx.IsMatch(text)) return Offering;
        if (FdaRx.IsMatch(text)) return Fda;
        if (InvestRx.IsMatch(text)) return Investment;
        if (AcquirerRx.IsMatch(text)) return Acquirer;
        if (ContractRx.IsMatch(text)) return Contract;
        return OtherNews;
    }

    /// <summary>An earnings release (8-K item 2.02) rated by its opening text: guidance first, then beat/miss words.</summary>
    public static string EarningsOf(string text) =>
        GuideUpRx.IsMatch(text) ? GuideUp : GuideDownRx.IsMatch(text) ? GuideDown : MissRx.IsMatch(text) ? EarnMiss : BeatRx.IsMatch(text) ? EarnBeat : Earnings;

    /// <summary>A filing as the catalyst when no news item explains the move (8-K items, 424B, 6-K).</summary>
    public static string? FromFiling(string form, string? items)
    {
        var it = items ?? "";
        if (form.StartsWith("424B", StringComparison.Ordinal) || form is "S-1" or "F-1" or "S-1/A" or "F-1/A" or "EFFECT") return Offering;
        if (!form.StartsWith("8-K", StringComparison.Ordinal)) return form is "6-K" or "6-K/A" ? OtherNews : null;
        if (it.Contains("2.01")) return Acquirer;
        if (it.Contains("3.02")) return Offering;
        if (it.Contains("5.03") || it.Contains("3.03")) return ReverseSplit;
        if (it.Contains("1.01")) return Contract;
        if (it.Contains("2.02")) return Earnings;
        return it.Contains("7.01") || it.Contains("8.01") ? OtherNews : null;
    }

    public static string GroupOf(string catalyst) => catalyst switch
    {
        Target or Merger or Investment or EarnBeat or EarnMiss or GuideUp or GuideDown or Earnings or Fda or Acquirer or Uplisting or Buyback or Restructuring => "real company event",
        Loi or Contract or Theme or Analyst or OtherNews => "story / PR",
        ReverseSplit => "reverse split / ratio change",
        Offering => "offering / dilution",
        _ => "no catalyst (market beta)",
    };
}
