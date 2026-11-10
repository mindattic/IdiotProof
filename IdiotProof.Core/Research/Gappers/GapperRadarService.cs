using System.Globalization;
using IdiotProof.Core.Data;
using IdiotProof.Core.Evidence;
using IdiotProof.Core.Research.Momentum;
using IdiotProof.Core.Services;
using IdiotProof.Evidence.Gappers;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Core.Research.Gappers;

public sealed record GapperRadarRow(GapperRadarScore Score, GapperRadarFacts Facts);

public sealed record GapperRadarRun(DateOnly Session, DateOnly Basis, DateTime AsOfUtc, int Universe, int Candidates, List<GapperRadarRow> Rows, List<string> Notes);

/// <summary>
/// The gapper radar (IP-T500): every stock that could gap up on session D, ranked by the signs the gapper study found
/// known by the evening before (D-1 16:00 ET), each with its reasons and its warnings. Candidates are the basis session's
/// top movers (by high, close and gap), every reverse split calendared for D or the session after, and the latest
/// Momentum. picks for D. Data: Alpaca SIP daily bars (two years per candidate, split ratios from Alpaca's own
/// adjustment), Alpaca corporate actions, Alpaca news classified by <see cref="CatalystTypes"/>, the Nasdaq Trader halt
/// feed and Alpaca's tradable list. Read-only: no orders, no settings.
/// </summary>
public sealed class GapperRadarService(
    GapperStudyServices study,
    AlpacaNewsService news,
    IHttpClientFactory httpFactory,
    IDbContextFactory<AppDbContext> dbFactory,
    TimeProvider clock,
    ILogger<GapperRadarService> log)
{
    public const string DefaultDir = @"D:\IdiotProofData\radar";
    private static readonly TimeSpan Close = new(16, 0, 0);

    /// <summary>The session the radar is for and the completed session it reads, from an explicit session or the clock.</summary>
    public static (DateOnly Session, DateOnly Basis) Sessions(DateTime nowUtc, DateOnly? session)
    {
        if (session is { } d) return (d, MarketCalendar.PreviousTradingDay(d));
        var et = MinuteBarMath.ToEastern(nowUtc);
        var today = DateOnly.FromDateTime(et);
        if (MarketCalendar.IsTradingDay(today) && et.TimeOfDay < Close) return (today, MarketCalendar.PreviousTradingDay(today));
        var next = MarketCalendar.NextTradingDay(today);
        return (next, MarketCalendar.PreviousTradingDay(next));
    }

    public async Task<GapperRadarRun> RunAsync(Guid owner, DateOnly? session, string? dataDir, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var (d, b) = Sessions(now, session);
        var notes = new List<string>();
        // Point in time: an explicit past session reads what was known by 20:00 ET on its basis day.
        var historical = session is not null && MinuteBarMath.ToUtc(b, new TimeSpan(20, 0, 0)) < now;
        var asOf = historical ? MinuteBarMath.ToUtc(b, new TimeSpan(20, 0, 0)) : now;
        var root = string.IsNullOrWhiteSpace(dataDir) ? DefaultDir : dataDir;

        var keys = await study.ResolveKeysAsync(owner, ct);
        await using var md = new MomentumMarketData(root, keys, new AlpacaDataRateGate(300, clock), c => study.LoadAssetsAsync(keys, c), log);
        var nowEt = MinuteBarMath.ToEastern(now);
        var basisOpen = DateOnly.FromDateTime(nowEt) == b && nowEt.TimeOfDay < new TimeSpan(20, 5, 0);
        var movers = await md.MoversAsync(b, ct);
        if (basisOpen)
        {
            notes.Add($"{b:yyyy-MM-dd} is still trading (until 20:00 ET): its bar and ranks are as of now");
            var cached = Path.Combine(root, "movers", b.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".json");
            if (File.Exists(cached)) File.Delete(cached); // recompute on the next run instead of keeping a partial day
        }
        if (movers is null)
            return new(d, b, asOf, 0, 0, [], [.. notes, $"No daily cross-section for {b:yyyy-MM-dd} (no SPY bar yet, or the data API refused the keys)."]);

        var tradable = await AlpacaTradableAssets.GetAsync(keys.KeyId, keys.Secret, ct);
        if (tradable is null) notes.Add("Alpaca's tradable list was unavailable: tradability unknown");
        var momentum = await MomentumPicksAsync(d, asOf, ct);
        var rows = await ScoreSessionAsync(md, keys, movers, d, b, asOf, now, root, tradable, momentum, notes, ct);
        log.LogInformation("gapper radar {D}: {N} candidates from {B} ({Req} data requests)", d, rows.Count, b, md.Requests);
        return new(d, b, asOf, movers.Count, rows.Count, rows, notes);
    }

    /// <summary>Score every candidate for session <paramref name="d"/> from what was known at <paramref name="asOf"/>; rows ranked, untradable last.</summary>
    private async Task<List<GapperRadarRow>> ScoreSessionAsync(MomentumMarketData md, StudyKeys keys, MoversDay movers, DateOnly d, DateOnly b, DateTime asOf, DateTime now,
        string root, HashSet<string>? tradable, Dictionary<string, int> momentum, List<string> notes, CancellationToken ct)
    {
        var assets = await md.AssetsAsync(ct);

        // ── candidates ──
        var cands = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in movers.Rows) cands.Add(r.S);
        var after = MarketCalendar.NextTradingDay(d);
        var splits = study.Splits(keys);
        IReadOnlyList<IdiotProof.Models.SplitEvent> market = [];
        try { market = await splits.GetMarketSplitsAsync(b.AddDays(-90), after, ct); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { notes.Add("Corporate actions unavailable: " + ex.Message); }
        var reverse = market.Where(s => s.PriceFactor > 1.0m).ToList();
        foreach (var s in reverse.Where(s => s.ExDate == d || s.ExDate == after)) cands.Add(s.Symbol.ToUpperInvariant());
        foreach (var s in momentum.Keys) cands.Add(s);
        cands.RemoveWhere(s => !assets.TryGetValue(s, out var a) || !a.Active);

        // ── data per candidate ──
        await md.EnsureDailyAsync(cands, b.AddDays(-730), b, ct);
        var halts = await HaltsAsync(Path.Combine(root, "halts"), b, now, notes, ct);
        var catalysts = await CatalystsAsync(cands.ToList(), MinuteBarMath.ToUtc(b, Close).AddDays(-5), asOf, keys, notes, ct);

        var rows = new List<GapperRadarRow>();
        var last5 = MarketCalendar.PriorTradingDays(b, 4).Append(b).ToList();
        var haltFrom = MinuteBarMath.ToUtc(last5[0], new TimeSpan(4, 0, 0));
        var haltTo = MinuteBarMath.ToUtc(b, new TimeSpan(20, 0, 0));
        foreach (var sym in cands)
        {
            var bars = md.Daily(sym).Where(x => x.Date <= b).ToList();
            var mv = movers.Find(sym);
            var daily = DailySigns(bars, b, d);
            var known = reverse.Where(s => string.Equals(s.Symbol, sym, StringComparison.OrdinalIgnoreCase)).ToList();
            var recorded = known.Where(s => s.ExDate <= b).OrderByDescending(s => s.ExDate).FirstOrDefault();
            int? daysSince = daily.DaysSinceReverseSplit;
            decimal? rsRatio = daily.ReverseSplitRatio;
            if (recorded is not null && (daysSince is null || d.DayNumber - recorded.ExDate.DayNumber < daysSince))
            {
                daysSince = d.DayNumber - recorded.ExDate.DayNumber;
                rsRatio = recorded.PriceFactor;
            }
            var onD = known.FirstOrDefault(s => s.ExDate == d);
            var onD1 = known.FirstOrDefault(s => s.ExDate == after);
            var onBasis = known.Any(s => s.ExDate == b) || daily.DaysSinceReverseSplit == d.DayNumber - b.DayNumber;
            var cat = catalysts.TryGetValue(sym, out var c) ? c : (Bits: 0, Headlines: new List<string>());
            var facts = new GapperRadarFacts(
                sym,
                PriorClose: bars.Count > 0 ? bars[^1].Close : mv?.C,
                RankHighD1: mv?.RankHigh, RankCloseD1: mv?.RankClose,
                HighPctD1: mv?.HighPct, ClosePctD1: mv?.ClosePct,
                DollarVolumeD1: daily.DollarVolume, DollarVolumeRatioD1: daily.DollarVolumeRatio,
                Up20Last5: daily.Up20Last5, Up20Last10: daily.Up20Last10, Up20Last30: daily.Up20Last30, Up100Last10: daily.Up100Last10,
                DaysSinceReverseSplit: daysSince, ReverseSplitRatio: (onD ?? onD1)?.PriceFactor ?? rsRatio,
                ReverseSplitOnD: onD is not null, ReverseSplitOnD1: onD1 is not null, ReverseSplitOnBasis: onBasis,
                HaltsLast5: halts?.Between(sym, haltFrom, haltTo).Count(),
                CatalystBits: cat.Bits, CatalystHeadlines: cat.Headlines,
                NewListing: daily.NewListing,
                ForeignProxy: GapperRadar.IsForeignName(assets.TryGetValue(sym, out var asset) ? asset.Name : null),
                Tradable: tradable is null ? null : tradable.Contains(sym),
                MomentumRank: momentum.TryGetValue(sym, out var rk) ? rk : null);
            rows.Add(new(GapperRadar.Score(facts), facts));
        }
        return rows.OrderBy(r => r.Score.Excluded).ThenByDescending(r => r.Score.Points).ThenBy(r => r.Facts.RankHighD1 ?? int.MaxValue).ToList();
    }

    public sealed record RadarBacktestDay(DateOnly Session, DateOnly Basis, List<GapperRadarRow> Rows, Dictionary<string, double> HighPct, int Liquid);

    /// <summary>
    /// The radar replayed on past sessions: each session scored from what was known by 20:00 ET the evening before
    /// (tradability and Momentum. picks left out), with every liquid name's actual high over the prior close that
    /// session from the daily cross-section. Daily bars are pulled once for the union of candidates.
    /// </summary>
    public async Task<List<RadarBacktestDay>> BacktestAsync(Guid owner, DateOnly from, DateOnly to, string? dataDir, CancellationToken ct)
    {
        var root = string.IsNullOrWhiteSpace(dataDir) ? DefaultDir : dataDir;
        var keys = await study.ResolveKeysAsync(owner, ct);
        await using var md = new MomentumMarketData(root, keys, new AlpacaDataRateGate(300, clock), c => study.LoadAssetsAsync(keys, c), log);
        var sessions = MarketCalendar.TradingDays(from, to);
        var basisDays = sessions.Select(MarketCalendar.PreviousTradingDay).ToList();
        await md.EnsureMoversAsync(sessions.Concat(basisDays), ct);
        var union = new HashSet<string>(StringComparer.Ordinal);
        foreach (var bd in basisDays) if (await md.MoversAsync(bd, ct) is { } m) foreach (var r in m.Rows) union.Add(r.S);
        log.LogInformation("radar backtest: {N} sessions, {U} candidate symbols", sessions.Count, union.Count);
        await md.EnsureDailyAsync(union, basisDays.Min().AddDays(-730), basisDays.Max(), ct);

        var now = clock.GetUtcNow().UtcDateTime;
        var days = new List<RadarBacktestDay>();
        foreach (var d in sessions)
        {
            var b = MarketCalendar.PreviousTradingDay(d);
            var movers = await md.MoversAsync(b, ct);
            var outcome = await md.MoversAsync(d, ct);
            if (movers is null || outcome is null) continue;
            var asOf = MinuteBarMath.ToUtc(b, new TimeSpan(20, 0, 0));
            var rows = await ScoreSessionAsync(md, keys, movers, d, b, asOf, now, root, null, [], [], ct);
            days.Add(new(d, b, rows, outcome.Rows.ToDictionary(r => r.S, r => r.HighPct, StringComparer.Ordinal), outcome.Count));
            log.LogInformation("radar backtest {D}: {N} scored", d, rows.Count);
        }
        return days;
    }

    public sealed record DailySignValues(double? DollarVolume, double? DollarVolumeRatio, int Up20Last5, int Up20Last10, int Up20Last30, int Up100Last10,
        int? DaysSinceReverseSplit, decimal? ReverseSplitRatio, bool NewListing);

    /// <summary>
    /// Signs from one symbol's daily bars up to and including the basis session (raw prices with Alpaca's split ratio per
    /// day): +20% / +100% high days over the split-consistent prior close, the basis day's dollar volume against the 20
    /// sessions before it, the latest reverse split (a drop of 1.5x or more in the ratio from one day to the next) counted
    /// in calendar days to the session, and a first bar inside the two-year window (a new listing). Pure.
    /// </summary>
    public static DailySignValues DailySigns(IReadOnlyList<SymbolDay> bars, DateOnly basis, DateOnly session)
    {
        if (bars.Count == 0) return new(null, null, 0, 0, 0, 0, null, null, false);
        var hiPct = new double[bars.Count];
        int? daysSince = null; decimal? ratio = null;
        for (int i = 1; i < bars.Count; i++)
        {
            var prevAdj = bars[i - 1].Close * bars[i - 1].Ratio;
            var highAdj = bars[i].High * bars[i].Ratio;
            hiPct[i] = prevAdj > 0 ? (double)(highAdj / prevAdj) - 1 : 0;
            if (bars[i].Ratio > 0 && bars[i - 1].Ratio / bars[i].Ratio >= 1.5m)
            {
                daysSince = session.DayNumber - bars[i].Date.DayNumber;
                ratio = Math.Round(bars[i - 1].Ratio / bars[i].Ratio, 2);
            }
        }
        int Count(int lastN, double min) => Enumerable.Range(Math.Max(1, bars.Count - lastN), Math.Min(lastN, bars.Count - 1)).Count(i => hiPct[i] >= min);
        double? dv = bars[^1].Date == basis ? (double)(bars[^1].Close * bars[^1].Volume) : null;
        var prior = bars.Take(bars.Count - (bars[^1].Date == basis ? 1 : 0)).TakeLast(20).Select(x => (double)(x.Close * x.Volume)).ToList();
        double? dvRatio = dv is { } v && prior.Count >= 10 && prior.Average() > 0 ? v / prior.Average() : null;
        var newListing = bars[0].Date > basis.AddDays(-720) && bars[0].Date > new DateOnly(2021, 9, 15);
        return new(dv, dvRatio, Count(5, 0.2), Count(10, 0.2), Count(30, 0.2), Count(10, 1.0), daysSince, ratio, newListing);
    }

    /// <summary>Momentum.'s picks for session <paramref name="d"/> (symbol to best rank) from videos published by <paramref name="asOfUtc"/>.</summary>
    public async Task<Dictionary<string, int>> MomentumPicksAsync(DateOnly d, DateTime asOfUtc, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var picks = await (from c in db.MomentumClaims
                           join v in db.MomentumVideos on c.VideoId equals v.VideoId
                           where c.TradeDate == d && c.Rank != null && c.Symbol != null && v.PublishedUtc != null && v.PublishedUtc <= asOfUtc
                                 && (c.ClaimType == "watchlist-scenario" || c.ClaimType == "morning-update-scenario")
                           select new { c.Symbol, c.Rank }).ToListAsync(ct);
        return picks.GroupBy(p => p.Symbol!).ToDictionary(g => g.Key, g => g.Min(p => p.Rank!.Value), StringComparer.Ordinal);
    }

    /// <summary>The Nasdaq Trader halt feed for the five sessions to the basis day; past days are cached as files, a day still in progress is refetched.</summary>
    private async Task<HaltHistory?> HaltsAsync(string dir, DateOnly basis, DateTime nowUtc, List<string> notes, CancellationToken ct)
    {
        Directory.CreateDirectory(dir);
        var days = MarketCalendar.PriorTradingDays(basis, 4).Append(basis).ToList();
        var all = new List<TradeHalt>();
        using var http = httpFactory.CreateClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("IdiotProof-radar/1.0");
        int failed = 0;
        foreach (var day in days)
        {
            var path = Path.Combine(dir, day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".xml");
            var complete = MinuteBarMath.ToUtc(day.AddDays(1), new TimeSpan(4, 0, 0)) < nowUtc;
            string? xml = File.Exists(path) && complete ? await File.ReadAllTextAsync(path, ct) : null;
            if (xml is null)
            {
                try
                {
                    xml = await http.GetStringAsync("https://www.nasdaqtrader.com/rss.aspx?feed=tradehalts&haltdate=" + day.ToString("MMddyyyy", CultureInfo.InvariantCulture), ct);
                    if (complete) await File.WriteAllTextAsync(path, xml, ct);
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { failed++; continue; }
            }
            try { all.AddRange(HaltHistory.Parse(xml)); } catch (System.Xml.XmlException) { failed++; }
        }
        if (failed == days.Count) { notes.Add("Halt feed unavailable: halts unknown"); return null; }
        if (failed > 0) notes.Add($"Halt feed missing {failed} of {days.Count} day(s)");
        return new HaltHistory(all.DistinctBy(h => (h.Symbol.ToUpperInvariant(), h.HaltUtc, h.Reason)));
    }

    /// <summary>Alpaca news per candidate in [from, asOf], classified by <see cref="CatalystTypes.FromHeadline"/> (coverage items count as nothing).</summary>
    private async Task<Dictionary<string, (int? Bits, List<string> Headlines)>> CatalystsAsync(List<string> symbols, DateTime fromUtc, DateTime asOfUtc, StudyKeys keys,
        List<string> notes, CancellationToken ct)
    {
        var result = new Dictionary<string, (int? Bits, List<string> Headlines)>(StringComparer.Ordinal);
        var want = symbols.ToHashSet(StringComparer.Ordinal);
        int items = 0;
        foreach (var chunk in symbols.Chunk(50))
        {
            await foreach (var n in news.GetNewsRangeAsync(chunk, fromUtc, asOfUtc, maxPages: 40, keys: (keys.KeyId, keys.Secret), ct: ct))
            {
                items++;
                var bits = CatalystTypes.FromHeadline(n.Headline, null) & ~CatalystTypes.Bit("coverage");
                foreach (var s in n.Symbols.Where(want.Contains))
                {
                    if (!result.TryGetValue(s, out var have)) result[s] = have = (0, []);
                    if (bits != 0 && have.Headlines.Count < 3) have.Headlines.Add($"{MinuteBarMath.ToEastern(n.CreatedAt):MM-dd HH:mm} {n.Headline}");
                    result[s] = ((have.Bits ?? 0) | bits, have.Headlines);
                }
            }
        }
        if (items == 0) notes.Add("News returned nothing in the window (keys, or a quiet tape): a name without news counts as no catalyst");
        return result;
    }
}
