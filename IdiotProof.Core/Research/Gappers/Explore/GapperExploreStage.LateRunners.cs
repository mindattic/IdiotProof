using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using IdiotProof.Core.Catalysts;
using IdiotProof.Core.Data;
using IdiotProof.Core.Evidence;
using IdiotProof.Evidence.Evaluation;
using IdiotProof.Evidence.Gappers;
using IdiotProof.Evidence.Search;
using IdiotProof.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IdiotProof.Core.Research.Gappers.Explore;

/// <summary>
/// `explore late-runners` (IP-T605, gapper study amendment 09), TRAIN only: runners that start after 09:30. Reads the
/// stored 04:00-20:00 minute bars of every train population symbol-day (candidates at weight 1, amendment 01's seeded
/// sample at its weight; excluded universe days stay out), the stored news and EDGAR filings, the splits, the daily bars
/// and the SEC shares-outstanding file, and computes exactly the amendment's definitions: first +10% / +20% touches and
/// their session, reach20Pre0930 / reach20Rth / lateRunner, the move start m0, the label y, the eleven features at m0;
/// then A09-H1 (weighted logistic on the features vs on gainAtStart alone, 5 month-blocked folds, pooled out-of-fold
/// weighted AUC difference, day-clustered bootstrap) and A09-H2 (weighted lift of y with a fresh catalyst at m0), one
/// Benjamini-Hochberg family, and the descriptive tables D1-D3. Five ledger trials. Validation is not read here (the
/// explore stage refuses every split but train); a hypothesis that passes on train is run on validation by a follow-up.
/// Research only: nothing here arms, sizes or exits a trade.
/// </summary>
internal static class A09DbExtensions
{
    /// <summary>
    /// Sets the context's command timeout and returns it, for one long research scan. Relational-only (throws
    /// against the EF InMemory provider the unit tests use), so a no-op there (IP-T610).
    /// </summary>
    public static AppDbContext WithCommandTimeout(this AppDbContext db, TimeSpan timeout)
    {
        if (db.Database.IsRelational()) db.Database.SetCommandTimeout(timeout);
        return db;
    }
}

public sealed partial class GapperExploreStage
{
    public const string LateRunnerTrack = "late-runners";

    private const int A09Minutes = 960;       // 04:00..19:59 ET, index = minutes since 04:00
    private const int A09Rth = 330;           // 09:30
    private const int A09RthEnd = 720;        // 16:00 (exclusive)
    private const int A09Folds = 5;
    private const double A09Level = 0.20;
    private const double A09BhQ = 0.05;

    private static readonly string[] A09SessionNames =
        ["premarket (04:00-09:29)", "open (09:30-09:44)", "morning (09:45-11:59)", "midday (12:00-13:59)", "afternoon (14:00-15:59)", "post-market (16:00-19:59)"];

    /// <summary>Amendment 09's session bucket of a minute index (minutes since 04:00 ET).</summary>
    public static int A09Session(int minute) => minute < 330 ? 0 : minute < 345 ? 1 : minute < 480 ? 2 : minute < 600 ? 3 : minute < 720 ? 4 : 5;

    /// <summary>The fresh-catalyst groups of amendment 09 feature 6 (index 4 = none).</summary>
    private static readonly string[] A09GroupNames = ["real company event", "story or PR", "offering", "reverse split", "none"];

    /// <summary>
    /// Amendment 08 type to amendment 09's group (the dated implementation note in amendment-09.md): the case study's grouping
    /// carried over to the amendment 08 list. -1 = coverage / no catalyst, which never counts as fresh.
    /// </summary>
    public static int A09GroupOf(CatalystPreClass type) => type switch
    {
        CatalystPreClass.NoCatalyst => -1,
        CatalystPreClass.ContractPartnership or CatalystPreClass.AnalystUpgrade or CatalystPreClass.ThemePivot or CatalystPreClass.Other => 1,
        CatalystPreClass.OfferingDilution => 2,
        CatalystPreClass.ReverseSplit => 3,
        _ => 0,
    };

    /// <summary>The forms amendment 09 counts as a fresh catalyst.</summary>
    private static readonly string[] A09CatalystForms = ["8-K", "8-K/A", "6-K", "S-1", "S-3", "424B3", "424B4", "424B5", "SC 13D"];

    public static readonly string[] A09FeatureNames =
    [
        "gainAtStart", "volRatioTod", "minutesSinceOpen", "pmHighGain", "highSoFarGain", "freshCatalyst", "catalystAgeMin",
        "group.realCompanyEvent", "group.storyOrPr", "group.offering", "group.reverseSplit", "haltBeforeStart",
        "logAdv20Dollars", "logPrevClose", "reverseSplitWithin60d", "offering424BorS1Within90d", "priorTopMoverDays", "logSharesOutstanding",
    ];

    private sealed record A09Item(DateTime Utc, int Bits);

    internal sealed class A09Day
    {
        public string Symbol = "";
        public DateOnly Day;
        public double Weight, PrevClose;
        public bool Candidate;
        public int First10 = -1, First20 = -1, Hod = -1, M0 = -1;
        public double DayHigh = double.NaN, Close16 = double.NaN, LastPrint = double.NaN;
        public bool Pre0930, Rth20, Late, Post20, Y, AnyFreshToday;
        public double[] X = [];
        public int Group = 4;
        public double FwdHigh = double.NaN, FwdClose = double.NaN, CatToM0 = double.NaN, CatToFirst20 = double.NaN;
    }

    private async Task<(List<string>, int, string?)> LateRunnersAsync(ExploreRequest req, CancellationToken ct)
    {
        var profilePath = s.Paths.ProfileFile(false);
        if (!File.Exists(profilePath)) return ([$"{profilePath} is missing: run `snapshot train` first so the time-of-day profile is fit"], 0, null);
        var (all, train, populationCount, candidateCount, noBars, short16, earlyClose) = await A09RowsAsync("train", req.Parallelism, ct);
        return A09Report(all, populationCount, candidateCount, noBars, short16, earlyClose, train);
    }

    /// <summary>
    /// Amendment 09's rows (m0, label y, the eleven features at m0) for one split, read-only: candidates (census, weight
    /// 1) plus amendment 01's seeded sample (its weight) of that split's universe days. Shared by the train-only explore
    /// command above and <c>GapperTestStage</c>'s A09-H1/H2 validation dispatch (IP-T616), which calls this once for
    /// "train" (to fit) and once for the split under test (to score); the two are the same call with a different split.
    /// </summary>
    internal async Task<(List<A09Day> Rows, GapperStudyParams.SplitRange Range, int PopulationRows, int CandidateRows, int NoBars, int Short16, int EarlyClose)> A09RowsAsync(
        string split, int parallelism, CancellationToken ct)
    {
        var p = s.Params;
        var train = p.Split(split);
        var profilePath = s.Paths.ProfileFile(false);
        var profile = TimeOfDayProfile.Load(profilePath);
        var amendment = s.Amendment ?? throw new InvalidOperationException("Amendment 01's population sample is required (amendment-01.json).");

        // ── population: candidates (census, weight 1) + amendment 01's seeded sample (its weight); excluded days are already out ──
        var population = (await s.StudyDaysAsync(split, null, null, ct))
            .Select(u => (U: u, Sampled: !u.IsCandidate && amendment.IsSampleMember(u.Symbol, u.DateEt)))
            .Where(x => x.U.IsCandidate || x.Sampled).ToList();
        if (population.Any(x => x.U.DateEt < train.From || x.U.DateEt > train.To || !string.Equals(x.U.Split, split, StringComparison.Ordinal)))
            throw new InvalidOperationException($"A symbol-day outside the {split} split reached the late-runner stage; refusing to continue.");
        s.Logger.LogInformation("[{Now}] explore late-runners: {Rows} {Split} population symbol-days ({Cand} candidates)", GapperStudyServices.Et(s.UtcNow), population.Count, split, population.Count(x => x.U.IsCandidate));

        // ── event inputs for the whole train window (read once) ──
        var lo = MinuteBarMath.ToUtc(MarketCalendar.PreviousTradingDay(train.From), new TimeSpan(16, 0, 0));
        var hi = MinuteBarMath.ToUtc(train.To, new TimeSpan(20, 0, 0));
        var items = new Dictionary<string, List<A09Item>>(StringComparer.Ordinal);
        var dilution = new Dictionary<string, List<DateTime>>(StringComparer.Ordinal);
        var splits = new Dictionary<string, List<SplitEvent>>(StringComparer.Ordinal);
        void AddItem(string sym, A09Item it) { if (!items.TryGetValue(sym, out var l)) items[sym] = l = []; l.Add(it); }
        await using (var db = await s.Db.CreateDbContextAsync(ct))
        {
            if (db.Database.IsRelational()) db.Database.SetCommandTimeout(TimeSpan.FromMinutes(15));   // whole-train scans of the filing index and news (IP-T610: InMemory-safe)
            var news = new Dictionary<Guid, A09Item>();
            await foreach (var e in db.CatalystEvents.AsNoTracking().Where(e => e.PublishedUtc > lo && e.PublishedUtc <= hi)
                               .Select(e => new { e.Id, e.PublishedUtc, e.Headline, e.PreClass }).AsAsyncEnumerable().WithCancellation(ct))
                news[e.Id] = new A09Item(DateTime.SpecifyKind(e.PublishedUtc, DateTimeKind.Utc), CatalystTypes.FromHeadline(e.Headline, e.PreClass));
            await foreach (var x in db.CatalystEventSymbols.AsNoTracking().Select(x => new { x.EventId, x.Symbol }).AsAsyncEnumerable().WithCancellation(ct))
                if (news.TryGetValue(x.EventId, out var it)) AddItem(x.Symbol, it);

            var fFrom = train.From.AddDays(-100);
            var fTo = train.To;
            var filings = await db.EdgarFilingIndex.AsNoTracking()
                .Where(f => f.Symbol != null && f.FiledDate >= fFrom && f.FiledDate <= fTo
                            && (A09CatalystForms.Contains(f.FormType) || f.FormType.StartsWith("424B") || f.FormType.StartsWith("S-1")))
                .Select(f => new { f.Symbol, f.FiledDate, f.AcceptedUtc, f.FormType, f.ItemsJson }).ToListAsync(ct);
            foreach (var f in filings)
            {
                // The snapshot stage's rule: a filing with no acceptance time is known at 23:59:59 ET of its filing date.
                var known = f.AcceptedUtc is { } a ? DateTime.SpecifyKind(a, DateTimeKind.Utc) : MinuteBarMath.ToUtc(f.FiledDate, new TimeSpan(23, 59, 59));
                var form = f.FormType.Trim().ToUpperInvariant();
                if (form.StartsWith("424B", StringComparison.Ordinal) || form.StartsWith("S-1", StringComparison.Ordinal))
                {
                    if (!dilution.TryGetValue(f.Symbol!, out var dl)) dilution[f.Symbol!] = dl = [];
                    dl.Add(known);
                }
                if (!A09CatalystForms.Contains(form) || known <= lo || known > hi) continue;
                string[] its = [];
                if (!string.IsNullOrEmpty(f.ItemsJson)) try { its = JsonSerializer.Deserialize<string[]>(f.ItemsJson) ?? []; } catch (JsonException) { }
                AddItem(f.Symbol!, new A09Item(known, CatalystTypes.FromFiling(form, its)));
            }
            foreach (var sp in await db.GapperStudySplits.AsNoTracking().ToListAsync(ct))
            {
                if (!splits.TryGetValue(sp.Symbol, out var l)) splits[sp.Symbol] = l = [];
                l.Add(new SplitEvent(sp.Symbol, sp.ExDate, sp.OldRate, sp.NewRate));
            }
        }
        foreach (var l in items.Values) l.Sort((a, b) => a.Utc.CompareTo(b.Utc));
        shares ??= SharesOutstandingHistory.ReadCsv(SharesCsv());
        var cik = LoadCikMap();
        int coverageBit = CatalystTypes.Bit("coverage");

        // ── per symbol batch: daily bars for priorTopMoverDays, then the minute-bar day computation ──
        var store = await s.StoreAsync(ct);
        double multiple = p.Features.AbnormalVolumeMultiple;
        var halt = p.Labels.HaltProxy;
        var results = new ConcurrentBag<A09Day>();
        int noBars = 0, short16 = 0, earlyClose = 0, done = 0;
        var bySymbol = population.GroupBy(x => x.U.Symbol, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal).ToList();
        foreach (var batch in bySymbol.Chunk(250))
        {
            ct.ThrowIfCancellationRequested();
            var symbols = batch.Select(g => g.Key).ToList();
            var dailyFrom = train.From.AddDays(-45);
            var daily = new Dictionary<string, Dictionary<DateOnly, (double High, double Close)>>(StringComparer.Ordinal);
            await using (var db = await s.Db.CreateDbContextAsync(ct))
                foreach (var b in await db.WithCommandTimeout(TimeSpan.FromMinutes(10)).GapperStudyDailyBars.AsNoTracking().Where(b => symbols.Contains(b.Symbol) && b.DateEt >= dailyFrom && b.DateEt <= train.To)
                             .Select(b => new { b.Symbol, b.DateEt, b.High, b.Close }).ToListAsync(ct))
                {
                    if (!daily.TryGetValue(b.Symbol, out var m)) daily[b.Symbol] = m = [];
                    m[b.DateEt] = ((double)b.High, (double)b.Close);
                }
            Parallel.ForEach(batch, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, parallelism), CancellationToken = ct }, group =>
            {
                var sym = group.Key;
                var symItems = items.GetValueOrDefault(sym) ?? [];
                var symDilution = dilution.GetValueOrDefault(sym) ?? [];
                var symSplits = splits.GetValueOrDefault(sym) ?? [];
                var symDaily = daily.GetValueOrDefault(sym);
                foreach (var (u, sampled) in group)
                {
                    if (IdiotProof.Scripting.MarketTime.IsEarlyCloseDay(u.DateEt)) { Interlocked.Increment(ref earlyClose); continue; }
                    var bars = store.ReadDay(sym, u.DateEt);
                    if (bars.Count == 0) { Interlocked.Increment(ref noBars); continue; }
                    double logShares = cik is not null && shares is not null && cik.TryGetValue(sym, out var c) && shares.KnownBefore(c, u.DateEt) is { } f && f.Shares > 0 ? Math.Log(f.Shares) : double.NaN;
                    var day = A09ComputeDay(u, sampled ? amendment.SampleWeight : 1, bars, profile, multiple, halt.MinConsecutiveEmptyRthMinutes, halt.RequirePrintsInPriorMinutes,
                        symItems, symDilution, symSplits, symDaily, logShares, coverageBit);
                    if (day is null) { Interlocked.Increment(ref short16); continue; }
                    results.Add(day);
                }
            });
            done += batch.Length;
            s.Logger.LogInformation("[{Now}] explore late-runners: {Done}/{Total} symbols, {Rows} days computed", GapperStudyServices.Et(s.UtcNow), done, bySymbol.Count, results.Count);
        }
        var all = results.OrderBy(d => d.Day).ThenBy(d => d.Symbol, StringComparer.Ordinal).ToList();
        return (all, train, population.Count, population.Count(x => x.U.IsCandidate), noBars, short16, earlyClose);
    }

    /// <summary>
    /// One symbol-day under amendment 09's definitions; null when the stored bars do not reach 16:00 (no bar starting at or
    /// after 15:59, so none ends at or after 16:00). Arrays are per minute from 04:00; nothing after the end of m0 feeds X.
    /// </summary>
    private static A09Day? A09ComputeDay(GapperStudyUniverseDay u, double weight, IReadOnlyList<Candle> bars, TimeOfDayProfile profile, double multiple,
        int haltEmpty, int haltPrimed, List<A09Item> items, List<DateTime> dilution, List<SplitEvent> splits,
        Dictionary<DateOnly, (double High, double Close)>? daily, double logShares, int coverageBit)
    {
        var d = u.DateEt;
        double prev = (double)u.AdjPrevClose;
        if (prev <= 0) return null;
        var start = MinuteBarMath.ToUtc(d, MinuteBarMath.SessionStartEt);
        var o = new double[A09Minutes]; var h = new double[A09Minutes]; var c = new double[A09Minutes]; var v = new double[A09Minutes];
        Array.Fill(o, double.NaN); Array.Fill(h, double.NaN); Array.Fill(c, double.NaN);
        int last = -1;
        foreach (var b in bars)
        {
            int m = (int)Math.Floor((DateTime.SpecifyKind(b.StartUtc, DateTimeKind.Utc) - start).TotalMinutes);
            if (m < 0 || m >= A09Minutes || b.High <= 0) continue;
            if (double.IsNaN(o[m])) o[m] = (double)b.Open;
            h[m] = double.IsNaN(h[m]) ? (double)b.High : Math.Max(h[m], (double)b.High);
            c[m] = (double)b.Close;
            v[m] += (double)b.Volume;
            last = Math.Max(last, m);
        }
        if (last < A09RthEnd - 1) return null;

        int First(double gain, int from, int to)
        {
            for (int i = from; i < to; i++) if (!double.IsNaN(h[i]) && h[i] / prev - 1 >= gain) return i;
            return -1;
        }
        int LastClose(int to) { for (int i = to - 1; i >= 0; i--) if (!double.IsNaN(c[i])) return i; return -1; }

        var day = new A09Day { Symbol = u.Symbol, Day = d, Weight = weight, PrevClose = prev, Candidate = u.IsCandidate };
        day.First10 = First(0.10, 0, A09Minutes);
        day.First20 = First(A09Level, 0, A09Minutes);
        for (int i = 0; i < A09Minutes; i++) if (!double.IsNaN(h[i]) && (double.IsNaN(day.DayHigh) || h[i] > day.DayHigh)) { day.DayHigh = h[i]; day.Hod = i; }
        day.Pre0930 = day.First20 >= 0 && day.First20 < A09Rth;
        day.Rth20 = First(A09Level, A09Rth, A09RthEnd) >= 0;
        day.Late = !day.Pre0930 && day.Rth20;
        day.Post20 = day.First20 >= A09RthEnd;
        int c16 = LastClose(A09RthEnd), cl = LastClose(A09Minutes);
        if (c16 >= 0) day.Close16 = c[c16];
        if (cl >= 0) day.LastPrint = c[cl];

        // Fresh catalysts: after D-1 16:00, type not coverage / no catalyst.
        var freshFrom = MinuteBarMath.ToUtc(MarketCalendar.PreviousTradingDay(d), new TimeSpan(16, 0, 0));
        var dayEnd = MinuteBarMath.ToUtc(d, new TimeSpan(20, 0, 0));
        var fresh = new List<A09Item>();
        foreach (var it in items)
            if (it.Utc > freshFrom && it.Utc <= dayEnd && CatalystPreClassifier.FromBits(it.Bits).Type != CatalystPreClass.NoCatalyst) fresh.Add(it);
        day.AnyFreshToday = fresh.Count > 0;
        static double Signed(List<A09Item> fresh, DateTime at)
        {
            DateTime? before = null, after = null;
            foreach (var it in fresh)
            {
                if (it.Utc <= at) before = it.Utc;
                else { after ??= it.Utc; }
            }
            return before is { } b ? (at - b).TotalMinutes : after is { } a ? (at - a).TotalMinutes : double.NaN;
        }
        if (day.First20 >= 0) day.CatToFirst20 = Signed(fresh, start.AddMinutes(day.First20));

        // m0: the first 09:30-15:59 minute with abnormal volume (protocol rule, ADV$ decile profile, the minute ending at m+1),
        // a close above the prior close, and no +20% high in any earlier bar. None when +20% printed before 09:30.
        if (day.Pre0930 || u.Adv20Shares <= 0) return day;
        double adv = (double)u.Adv20Shares;
        int dec = u.AdvDecile;
        double cum = 0;
        for (int i = 0; i < A09Rth; i++) cum += v[i];
        bool reached = false;
        for (int i = A09Rth; i < A09RthEnd && !reached; i++)
        {
            cum += v[i];
            var endEt = MinuteBarMath.SessionStartEt + TimeSpan.FromMinutes(i + 1);
            double expected = adv * profile.MinuteFractionAt(dec, endEt);
            if (expected > 0 && v[i] >= multiple * expected && !double.IsNaN(c[i]) && c[i] > prev)
            {
                day.M0 = i;
                break;
            }
            if (!double.IsNaN(h[i]) && h[i] / prev - 1 >= A09Level) reached = true;
        }
        if (day.M0 < 0) return day;
        int m0 = day.M0;
        var m0End = start.AddMinutes(m0 + 1);
        day.Y = First(A09Level, m0 + 1, A09RthEnd) >= 0;
        day.CatToM0 = Signed(fresh, m0End);

        var x = new double[A09FeatureNames.Length];
        Array.Fill(x, double.NaN);
        x[0] = c[m0] / prev - 1;
        x[1] = profile.VolRatioTod(cum, adv, dec, MinuteBarMath.SessionStartEt + TimeSpan.FromMinutes(m0 + 1)) is { } vr && double.IsFinite(vr) ? vr : double.NaN;
        x[2] = m0 - A09Rth;
        double pmHigh = double.NaN, soFar = double.NaN;
        for (int i = 0; i <= m0; i++)
        {
            if (double.IsNaN(h[i])) continue;
            if (i < A09Rth && (double.IsNaN(pmHigh) || h[i] > pmHigh)) pmHigh = h[i];
            if (double.IsNaN(soFar) || h[i] > soFar) soFar = h[i];
        }
        x[3] = double.IsNaN(pmHigh) ? double.NaN : pmHigh / prev - 1;
        x[4] = soFar / prev - 1;
        // Feature 6: fresh items published after D-1 16:00 and at or before the end of m0; the group is amendment 08's day rule
        // (the highest-priority type among them).
        int bits = 0; DateTime? latest = null;
        foreach (var it in fresh) if (it.Utc <= m0End) { bits |= it.Bits; latest = it.Utc; }
        x[5] = latest is null ? 0 : 1;
        x[6] = latest is { } lt ? (m0End - lt).TotalMinutes : double.NaN;
        day.Group = latest is null ? 4 : Math.Max(0, A09GroupOf(CatalystPreClassifier.FromBits(bits & ~coverageBit).Type));
        for (int g = 0; g < 4; g++) x[7 + g] = day.Group == g ? 1 : 0;
        x[11] = A09HaltBefore(v, o, m0, adv, dec, profile, multiple, haltEmpty, haltPrimed) ? 1 : 0;
        x[12] = u.Adv20Dollars > 0 ? Math.Log((double)u.Adv20Dollars) : double.NaN;
        x[13] = Math.Log(prev);
        x[14] = splits.Any(sp => sp.OldRate > sp.NewRate && sp.ExDate <= d && d.DayNumber - sp.ExDate.DayNumber <= 60) ? 1 : 0;
        x[15] = dilution.Any(k => k <= m0End && d.DayNumber - DateOnly.FromDateTime(MinuteBarMath.ToEastern(k)).DayNumber <= 90) ? 1 : 0;
        if (daily is not null)
        {
            int top = 0;
            foreach (var sd in MarketCalendar.PriorTradingDays(d, 20))
            {
                var pd = MarketCalendar.PreviousTradingDay(sd);
                if (!daily.TryGetValue(sd, out var today) || !daily.TryGetValue(pd, out var before) || before.Close <= 0) continue;
                double factor = (double)GapperUniverseBuilder.SplitFactor(splits, pd, sd);
                if (today.High / (before.Close * factor) - 1 >= A09Level) top++;
            }
            x[16] = top;
        }
        x[17] = logShares;
        day.X = x;

        // D3: the path from the next bar's open (the first stored bar after m0, before 16:00).
        for (int j = m0 + 1; j < A09RthEnd; j++)
        {
            if (double.IsNaN(o[j]) || o[j] <= 0) continue;
            double fh = double.NaN;
            for (int i = j; i < A09RthEnd; i++) if (!double.IsNaN(h[i]) && (double.IsNaN(fh) || h[i] > fh)) fh = h[i];
            day.FwdHigh = fh / o[j] - 1;
            day.FwdClose = day.Close16 / o[j] - 1;
            break;
        }
        return day;
    }

    /// <summary>
    /// The protocol's bar-gap halt proxy (GapperStudyLabeler.HaltProxy: N consecutive empty minutes after activity began,
    /// each of the M minutes before them printed), counted only for gaps that lie inside 09:30..the start of m0.
    /// </summary>
    private static bool A09HaltBefore(double[] v, double[] o, int m0, double adv, int dec, TimeOfDayProfile profile, double multiple, int empty, int primed)
    {
        bool Printed(int i) => i >= 0 && i < A09Minutes && !double.IsNaN(o[i]) && v[i] > 0;
        int activity = -1;
        for (int i = 0; i <= m0; i++)
        {
            if (!Printed(i)) continue;
            double expected = adv * profile.MinuteFractionAt(dec, MinuteBarMath.SessionStartEt + TimeSpan.FromMinutes(i + 1));
            if (expected > 0 && v[i] >= multiple * expected) { activity = i; break; }
        }
        if (activity < 0) for (int i = A09Rth; i <= m0; i++) if (Printed(i)) { activity = i; break; }
        if (activity < 0) return false;
        for (int t = Math.Max(activity, A09Rth); t + empty <= m0; t++)
        {
            bool gap = true;
            for (int i = 0; i < empty && gap; i++) gap = !Printed(t + i);
            if (!gap) continue;
            bool ok = true;
            for (int i = 1; i <= primed && ok; i++) ok = Printed(t - i);
            if (ok) return true;
        }
        return false;
    }

    /// <summary>Weighted AUC (ties half) over rows pre-sorted by score; each row's weight is multiplied by its day's draw count when given.</summary>
    private static double A09Auc(int[] order, double[] score, bool[] y, double[] w, int[] dayOf, int[]? draws)
    {
        double wp = 0, wn = 0, acc = 0, negBelow = 0;
        int i = 0, n = order.Length;
        while (i < n)
        {
            int j = i;
            double gp = 0, gn = 0;
            while (j < n && score[order[j]] == score[order[i]])
            {
                int r = order[j];
                double ww = w[r] * (draws is null ? 1 : draws[dayOf[r]]);
                if (ww > 0) { if (y[r]) gp += ww; else gn += ww; }
                j++;
            }
            acc += gp * (negBelow + 0.5 * gn);
            negBelow += gn; wp += gp; wn += gn;
            i = j;
        }
        return wp > 0 && wn > 0 ? acc / (wp * wn) : double.NaN;
    }

    private static int[] A09Order(double[] score, IReadOnlyList<int> rows) => rows.OrderBy(r => score[r]).ToArray();

    private sealed record A09Boot(double Estimate, double Low, double High, double POneSided, int Days, int Draws);

    /// <summary>
    /// Day-clustered percentile bootstrap (the convention of <see cref="DayClusteredBootstrap"/>: days drawn with replacement,
    /// seeded): <paramref name="statistic"/> gets each day's draw count. p = (1 + draws at or below <paramref name="nullValue"/>) / (B + 1).
    /// </summary>
    private static A09Boot A09Bootstrap(int days, Func<int[]?, double> statistic, double nullValue, int draws, int seed, double ciLevel)
    {
        double estimate = statistic(null);
        if (days < 5 || double.IsNaN(estimate)) return new A09Boot(estimate, double.NaN, double.NaN, 1, days, 0);
        var rng = new Random(seed);
        var counts = new int[days];
        var boot = new List<double>(draws);
        for (int b = 0; b < draws; b++)
        {
            Array.Clear(counts);
            for (int k = 0; k < days; k++) counts[rng.Next(days)]++;
            double st = statistic(counts);
            if (!double.IsNaN(st)) boot.Add(st);
        }
        if (boot.Count == 0) return new A09Boot(estimate, double.NaN, double.NaN, 1, days, 0);
        boot.Sort();
        double tail = (1 - ciLevel) / 2;
        double low = boot[(int)Math.Floor(tail * (boot.Count - 1))];
        double high = boot[(int)Math.Ceiling((1 - tail) * (boot.Count - 1))];
        double p = (1.0 + boot.Count(x => x <= nullValue)) / (boot.Count + 1);
        return new A09Boot(estimate, low, high, p, days, boot.Count);
    }

    /// <summary>Weighted P(y | fresh) / P(y | none), each day's rows counted draw-count times.</summary>
    private static double A09Lift(IReadOnlyList<A09Day> rows, int[] dayOf, int[]? draws)
    {
        double wyF = 0, wF = 0, wyN = 0, wN = 0;
        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            double w = r.Weight * (draws is null ? 1 : draws[dayOf[i]]);
            if (w <= 0) continue;
            if (r.X[5] > 0.5) { wF += w; if (r.Y) wyF += w; }
            else { wN += w; if (r.Y) wyN += w; }
        }
        if (wF <= 0 || wN <= 0 || wyN <= 0) return double.NaN;
        return (wyF / wF) / (wyN / wN);
    }

    private static double A09Median(IEnumerable<double> values)
    {
        var l = values.Where(double.IsFinite).OrderBy(x => x).ToList();
        if (l.Count == 0) return double.NaN;
        return l.Count % 2 == 1 ? l[l.Count / 2] : (l[l.Count / 2 - 1] + l[l.Count / 2]) / 2;
    }

    private static double A09WeightedMedian(IEnumerable<(double Value, double Weight)> values)
    {
        var l = values.Where(x => double.IsFinite(x.Value) && x.Weight > 0).OrderBy(x => x.Value).ToList();
        double total = l.Sum(x => x.Weight), acc = 0;
        foreach (var (val, w) in l) { acc += w; if (acc >= total / 2) return val; }
        return double.NaN;
    }

    /// <summary>
    /// Which of the eleven features to fit on and how (IP-T616): a feature missing on more than half of <paramref
    /// name="rows"/> (meant to be train rows -- the selection is made once, on train, and reused unchanged when
    /// scoring another split) is dropped; every other feature with a missing value gets a 0/1 indicator alongside it.
    /// </summary>
    internal static (List<string> Names, List<int> Kept, List<int> Dropped, List<int> Indicators, double[] Missing) A09Features(List<A09Day> rows, int n)
    {
        var missing = Enumerable.Range(0, A09FeatureNames.Length).Select(j => n == 0 ? 0 : (double)rows.Count(r => double.IsNaN(r.X[j])) / n).ToArray();
        var kept = Enumerable.Range(0, A09FeatureNames.Length).Where(j => missing[j] <= 0.5).ToList();
        var dropped = Enumerable.Range(0, A09FeatureNames.Length).Where(j => missing[j] > 0.5).ToList();
        var indicators = kept.Where(j => missing[j] > 0).ToList();
        var names = kept.Select(j => A09FeatureNames[j]).Concat(indicators.Select(j => A09FeatureNames[j] + ".missing")).ToList();
        return (names, kept, dropped, indicators, missing);
    }

    /// <summary>One row's feature vector under a <see cref="A09Features"/> selection: the kept raw values, then one 0/1 missing indicator per feature that had any.</summary>
    internal static double[] A09FullVector(A09Day r, List<int> kept, List<int> indicators) =>
        kept.Select(j => r.X[j]).Concat(indicators.Select(j => double.IsNaN(r.X[j]) ? 1.0 : 0.0)).ToArray();

    private (List<string>, int, string?) A09Report(List<A09Day> all, int populationRows, int candidateRows, int noBars, int short16, int earlyClose, GapperStudyParams.SplitRange train)
    {
        var p = s.Params;
        string Pct(double v) => double.IsFinite(v) ? v.ToString("+0.0%;-0.0%", CultureInfo.InvariantCulture) : "n/a";
        string Sh(double v) => double.IsFinite(v) ? v.ToString("0.0%", CultureInfo.InvariantCulture) : "n/a";
        string N3(double v) => double.IsFinite(v) ? v.ToString("0.000", CultureInfo.InvariantCulture) : "n/a";
        string N0(double v) => double.IsFinite(v) ? v.ToString("0", CultureInfo.InvariantCulture) : "n/a";
        var md = new StringBuilder();
        var json = new Dictionary<string, object?>();
        var trials = new List<ExploreTrial>();

        // Month-blocked folds: the train months in order, cut into five contiguous blocks.
        var firstMonth = new DateOnly(train.From.Year, train.From.Month, 1);
        int months = (train.To.Year - firstMonth.Year) * 12 + train.To.Month - firstMonth.Month + 1;
        int FoldOf(DateOnly d) => ((d.Year - firstMonth.Year) * 12 + d.Month - firstMonth.Month) * A09Folds / months;
        string FoldLabel(int k)
        {
            int a = Enumerable.Range(0, months).First(i => i * A09Folds / months == k), b = Enumerable.Range(0, months).Last(i => i * A09Folds / months == k);
            return $"fold {k + 1}: {firstMonth.AddMonths(a):yyyy-MM}..{firstMonth.AddMonths(b):yyyy-MM}";
        }

        md.AppendLine("# Explore: runners that start after 09:30 (amendment 09, IP-T605)");
        md.AppendLine();
        md.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"Train only ({train.From:yyyy-MM-dd}..{train.To:yyyy-MM-dd}); validation and the holdout are not read. Definitions exactly as frozen in amendment-09.md, with its dated implementation note. Minute bars 04:00-20:00 ET as stored by T2, prices over the split-adjusted prior close. Research only: no strategy, template, radar or sell-by changes, whatever the result."));
        md.AppendLine();
        md.AppendLine("## Population");
        md.AppendLine();
        var withM0 = all.Where(d => d.M0 >= 0).ToList();
        md.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"- Train population symbol-days (amendment 01: candidates as a census at weight 1, the seeded {s.Amendment!.PopulationSample.Rate:P0} sample of other universe days at weight {s.Amendment.SampleWeight}; excluded and split-window days stay out): {populationRows:N0} ({candidateRows:N0} candidates)."));
        md.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"- Left out and counted: {earlyClose:N0} on early-close (13:00) sessions, {noBars:N0} with no stored minute bars, {short16:N0} whose stored bars do not reach 16:00. Computed: {all.Count:N0} ({all.Count(d => d.Candidate):N0} candidates, {all.Count(d => !d.Candidate):N0} sample)."));
        md.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"- With a move start m0: {withM0.Count:N0} ({withM0.Count(d => d.Candidate):N0} candidates, {withM0.Count(d => !d.Candidate):N0} sample; weighted {withM0.Sum(d => d.Weight):N0}); y = 1 on {withM0.Count(d => d.Y):N0} ({Sh(withM0.Where(d => d.Y).Sum(d => d.Weight) / Math.Max(1e-9, withM0.Sum(d => d.Weight)))} weighted)."));
        md.AppendLine();
        json["population"] = new { populationRows, candidateRows, earlyClose, noBars, notReaching1600 = short16, computed = all.Count, withM0 = withM0.Count, withM0Candidates = withM0.Count(d => d.Candidate), positives = withM0.Count(d => d.Y) };

        // ── D1: when runners start (census: candidate days with a +20% high 04:00-19:59) ──
        var census = all.Where(d => d.Candidate && d.First20 >= 0).ToList();
        int sampleOnly = all.Count(d => !d.Candidate && d.First20 >= 0);
        md.AppendLine("## D1. When runners start");
        md.AppendLine();
        md.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"Census: train candidate days with a high >= +20% over the prior close 04:00-19:59 ({census.Count:N0} days, {census.Select(d => d.Symbol).Distinct().Count():N0} symbols; unweighted). Start session = the session of the first +20% minute; high-of-day session = the session of the day's highest high 04:00-19:59 (first minute at that price). {sampleOnly:N0} sampled non-candidate days also printed +20% on minute bars (their daily bar did not show it) and are not in this census. Run = day high over the prior close - 1; fade = the 16:00 close (last close by 16:00) / last print by 20:00 over the day high - 1."));
        md.AppendLine();
        md.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"Late runners (no +20% in a bar ending by 09:30, +20% in a bar starting 09:30-15:59): {census.Count(d => d.Late):N0} ({Sh((double)census.Count(d => d.Late) / Math.Max(1, census.Count))}); +20% before 09:30: {census.Count(d => d.Pre0930):N0}; first +20% only 16:00-19:59 (reach20Post, reported, not tested): {census.Count(d => d.Post20):N0}."));
        md.AppendLine();
        md.AppendLine("| start session | days | share | median run | median fade to 16:00 close | median fade to last print | median minutes first +20% to high |");
        md.AppendLine("|---|---|---|---|---|---|---|");
        var d1Rows = new List<object>();
        for (int k = 0; k < 6; k++)
        {
            var l = census.Where(d => A09Session(d.First20) == k).ToList();
            double run = A09Median(l.Select(d => d.DayHigh / d.PrevClose - 1)), f16 = A09Median(l.Select(d => d.Close16 / d.DayHigh - 1)), fl = A09Median(l.Select(d => d.LastPrint / d.DayHigh - 1)),
                mins = A09Median(l.Select(d => (double)(d.Hod - d.First20)));
            md.AppendLine($"| {A09SessionNames[k]} | {l.Count:N0} | {Sh((double)l.Count / Math.Max(1, census.Count))} | {Pct(run)} | {Pct(f16)} | {Pct(fl)} | {N0(mins)} |");
            d1Rows.Add(new { startSession = A09SessionNames[k], days = l.Count, share = (double)l.Count / Math.Max(1, census.Count), medianRun = run, medianFadeTo1600 = f16, medianFadeToLast = fl, medianMinutesFirst20ToHigh = mins });
        }
        md.AppendLine();
        md.AppendLine("Start session (rows) by high-of-day session (columns), days:");
        md.AppendLine();
        md.AppendLine("| start \\ high of day | " + string.Join(" | ", A09SessionNames.Select(n => n.Split(' ')[0])) + " | total |");
        md.AppendLine("|---|" + string.Concat(Enumerable.Repeat("---|", 7)));
        var cross = new int[6, 6];
        foreach (var d in census) cross[A09Session(d.First20), A09Session(d.Hod)]++;
        for (int a = 0; a < 6; a++)
            md.AppendLine($"| {A09SessionNames[a].Split(' ')[0]} | " + string.Join(" | ", Enumerable.Range(0, 6).Select(b => cross[a, b].ToString("N0", CultureInfo.InvariantCulture))) + $" | {Enumerable.Range(0, 6).Sum(b => cross[a, b]):N0} |");
        md.AppendLine("| total | " + string.Join(" | ", Enumerable.Range(0, 6).Select(b => Enumerable.Range(0, 6).Sum(a => cross[a, b]).ToString("N0", CultureInfo.InvariantCulture))) + $" | {census.Count:N0} |");
        md.AppendLine();
        var hodShare = Enumerable.Range(0, 6).Select(b => (double)census.Count(d => A09Session(d.Hod) == b) / Math.Max(1, census.Count)).ToArray();
        md.AppendLine("High-of-day session share: " + string.Join(", ", Enumerable.Range(0, 6).Select(b => $"{A09SessionNames[b].Split(' ')[0]} {Sh(hodShare[b])}")) + ".");
        md.AppendLine();
        double lateShare = (double)census.Count(d => d.Late) / Math.Max(1, census.Count);
        json["d1"] = new { censusDays = census.Count, sampleOnlyDaysWith20 = sampleOnly, lateRunners = census.Count(d => d.Late), pre0930 = census.Count(d => d.Pre0930), post = census.Count(d => d.Post20), byStartSession = d1Rows, hodShare, cross = Enumerable.Range(0, 6).Select(a => Enumerable.Range(0, 6).Select(b => cross[a, b]).ToArray()).ToArray() };
        trials.Add(new ExploreTrial
        {
            Track = LateRunnerTrack, Config = "A09-D1 when runners start (census, start session)", Metric = "share of +20% days whose first +20% is after 09:30 and before 16:00 (lateRunner, descriptive)",
            FoldValues = Enumerable.Range(0, A09Folds).Select(k => { var l = census.Where(d => FoldOf(d.Day) == k).ToList(); return l.Count == 0 ? (double?)null : (double)l.Count(d => d.Late) / l.Count; }).ToList(),
            Pooled = lateShare, Rows = census.Count,
            Note = string.Join(" ", Enumerable.Range(0, 6).Select(k => $"{A09SessionNames[k].Split(' ')[0]}={census.Count(d => A09Session(d.First20) == k)}")),
        });

        // ── D2: catalyst timing for late runners ──
        var late = census.Where(d => d.Late).ToList();
        md.AppendLine("## D2. Catalyst timing for late runners");
        md.AppendLine();
        md.AppendLine("Late runners of the D1 census. Fresh catalyst = a stored news item or EDGAR filing (8-K, 8-K/A, 6-K, S-1, S-3, 424B3/4/5, SC 13D; no acceptance time = 23:59:59 ET of the filing date) after D-1 16:00 whose amendment 08 type is not \"no catalyst\". Signed minutes = the time point minus the latest fresh item at or before it; when there is none before it, the first fresh item after it (by 20:00), so negative = the move came first. The time points are the start of the first +20% minute and the end of m0.");
        md.AppendLine();
        var toF20 = late.Select(d => d.CatToFirst20).Where(double.IsFinite).ToList();
        var lateM0 = late.Where(d => d.M0 >= 0).ToList();
        var toM0 = lateM0.Select(d => d.CatToM0).Where(double.IsFinite).ToList();
        double noFresh = late.Count == 0 ? double.NaN : (double)late.Count(d => !d.AnyFreshToday) / late.Count;
        double Q(List<double> l, double q) { var v = l.OrderBy(x => x).ToList(); if (v.Count == 0) return double.NaN; double pos = q * (v.Count - 1); int a = (int)Math.Floor(pos), b = (int)Math.Ceiling(pos); return v[a] + (v[b] - v[a]) * (pos - a); }
        md.AppendLine("| measure | late runners | with a fresh item that day | median min | p25 | p75 | negative (move first) |");
        md.AppendLine("|---|---|---|---|---|---|---|");
        md.AppendLine($"| fresh catalyst to first +20% | {late.Count:N0} | {toF20.Count:N0} | {N0(Q(toF20, 0.5))} | {N0(Q(toF20, 0.25))} | {N0(Q(toF20, 0.75))} | {Sh(toF20.Count == 0 ? double.NaN : (double)toF20.Count(x => x < 0) / toF20.Count)} |");
        md.AppendLine($"| fresh catalyst to m0 (end) | {lateM0.Count:N0} with m0 | {toM0.Count:N0} | {N0(Q(toM0, 0.5))} | {N0(Q(toM0, 0.25))} | {N0(Q(toM0, 0.75))} | {Sh(toM0.Count == 0 ? double.NaN : (double)toM0.Count(x => x < 0) / toM0.Count)} |");
        md.AppendLine();
        md.AppendLine(FormattableString.Invariant($"Late runners with no fresh catalyst at all (none from D-1 16:00 to D 20:00): {late.Count(d => !d.AnyFreshToday):N0} ({Sh(noFresh)}). By the amendment 08 group at m0 (late runners with m0): ") +
            string.Join(", ", Enumerable.Range(0, 5).Select(g => FormattableString.Invariant($"{A09GroupNames[g]} {lateM0.Count(d => d.Group == g):N0}"))) + ".");
        md.AppendLine();
        json["d2"] = new { lateRunners = late.Count, withM0 = lateM0.Count, noFreshShare = noFresh, toFirst20 = new { n = toF20.Count, median = Q(toF20, 0.5), p25 = Q(toF20, 0.25), p75 = Q(toF20, 0.75), negative = toF20.Count(x => x < 0) },
            toM0 = new { n = toM0.Count, median = Q(toM0, 0.5), p25 = Q(toM0, 0.25), p75 = Q(toM0, 0.75), negative = toM0.Count(x => x < 0) }, groupsAtM0 = Enumerable.Range(0, 5).ToDictionary(g => A09GroupNames[g], g => lateM0.Count(d => d.Group == g)) };
        trials.Add(new ExploreTrial
        {
            Track = LateRunnerTrack, Config = "A09-D2 catalyst timing for late runners", Metric = "share of late runners with no fresh catalyst D-1 16:00..D 20:00 (descriptive)",
            FoldValues = Enumerable.Range(0, A09Folds).Select(k => { var l = late.Where(d => FoldOf(d.Day) == k).ToList(); return l.Count == 0 ? (double?)null : (double)l.Count(d => !d.AnyFreshToday) / l.Count; }).ToList(),
            Pooled = double.IsFinite(noFresh) ? noFresh : null, Rows = late.Count,
            Note = string.Create(CultureInfo.InvariantCulture, $"catToFirst20 median={N0(Q(toF20, 0.5))} catToM0 median={N0(Q(toM0, 0.5))}"),
        });

        // ── D3: what followed m0 ──
        md.AppendLine("## D3. What followed m0");
        md.AppendLine();
        md.AppendLine("Population days with an m0 (weighted). From the open of the first stored bar after m0 (before 16:00): the forward high to 16:00 and the 16:00 close, gross, no costs, no exit rule. A description of the path, not a trade test. Mean with a day-clustered 95% interval.");
        md.AppendLine();
        md.AppendLine("| group | days | weighted | median forward high | mean forward high [95%] | median 16:00 close | mean 16:00 close [95%] |");
        md.AppendLine("|---|---|---|---|---|---|---|");
        var d3Rows = new List<object>();
        foreach (var (name, pick) in new (string, Func<A09Day, bool>)[] { ("all with m0", _ => true), ("y = 1 (+20% after m0)", d => d.Y), ("y = 0", d => !d.Y) })
        {
            var l = withM0.Where(pick).Where(d => double.IsFinite(d.FwdHigh)).ToList();
            var mh = ExploreDerived.ClusteredMean(l.Select(d => (d.Day, d.FwdHigh, d.Weight)));
            var mc = ExploreDerived.ClusteredMean(l.Select(d => (d.Day, d.FwdClose, d.Weight)));
            double medH = A09WeightedMedian(l.Select(d => (d.FwdHigh, d.Weight))), medC = A09WeightedMedian(l.Select(d => (d.FwdClose, d.Weight)));
            string B((double Mean, double Lo, double Hi, int Days)? m) => m is { } x ? $"{Pct(x.Mean)} [{Pct(x.Lo)}, {Pct(x.Hi)}]" : "n/a";
            md.AppendLine(string.Create(CultureInfo.InvariantCulture, $"| {name} | {l.Count:N0} | {l.Sum(d => d.Weight):N0} | {Pct(medH)} | {B(mh)} | {Pct(medC)} | {B(mc)} |"));
            d3Rows.Add(new { group = name, days = l.Count, weighted = l.Sum(d => d.Weight), medianFwdHigh = medH, meanFwdHigh = mh?.Mean, meanFwdHighLo = mh?.Lo, meanFwdHighHi = mh?.Hi, medianFwdClose = medC, meanFwdClose = mc?.Mean, meanFwdCloseLo = mc?.Lo, meanFwdCloseHi = mc?.Hi });
        }
        md.AppendLine();
        json["d3"] = d3Rows;
        var d3All = ExploreDerived.ClusteredMean(withM0.Where(d => double.IsFinite(d.FwdClose)).Select(d => (d.Day, d.FwdClose, d.Weight)));
        trials.Add(new ExploreTrial
        {
            Track = LateRunnerTrack, Config = "A09-D3 path after m0 (next-bar open to 16:00)", Metric = "weighted mean gross 16:00 close over the next bar's open (descriptive)",
            FoldValues = Enumerable.Range(0, A09Folds).Select(k => ExploreDerived.ClusteredMean(withM0.Where(d => FoldOf(d.Day) == k && double.IsFinite(d.FwdClose)).Select(d => (d.Day, d.FwdClose, d.Weight)))?.Mean).ToList(),
            Pooled = d3All?.Mean, Rows = withM0.Count,
        });

        // ── H1 / H2 ──
        md.AppendLine("## Hypotheses (train)");
        md.AppendLine();
        var rows = withM0;
        int n = rows.Count;
        var dayIds = rows.Select(d => d.Day).Distinct().OrderBy(d => d).ToList();
        var dayIndex = dayIds.Select((d, i) => (d, i)).ToDictionary(x => x.d, x => x.i);
        var dayOf = rows.Select(d => dayIndex[d.Day]).ToArray();
        var weights = rows.Select(d => d.Weight).ToArray();
        var labels = rows.Select(d => d.Y).ToArray();
        var fold = rows.Select(d => FoldOf(d.Day)).ToArray();
        int draws = p.Statistics.BootstrapDraws, seed = p.Statistics.Seed;
        double ci = p.Statistics.CiLevel > 0 ? p.Statistics.CiLevel : 0.95;

        // Missing data: a feature missing on more than half the rows is dropped; every other feature with a missing value gets a 0/1 indicator
        // (the value itself is imputed with the fitting rows' weighted mean by LogisticRegression.FitWeighted). Selected on train only
        // (A09Features), even when scoring another split (IP-T616): redoing the selection on validation would leak it.
        var (names, kept, dropped, indicators, missing) = A09Features(rows, n);
        double[] Full(A09Day r) => A09FullVector(r, kept, indicators);
        double[] Base(A09Day r) => [r.X[0]];
        var fullX = rows.Select(Full).ToArray();
        var baseX = rows.Select(Base).ToArray();

        var fullScore = new double[n];
        var baseScore = new double[n];
        Array.Fill(fullScore, double.NaN); Array.Fill(baseScore, double.NaN);
        var foldDiff = new double?[A09Folds];
        var foldLift = new double?[A09Folds];
        for (int k = 0; k < A09Folds; k++)
        {
            var fit = Enumerable.Range(0, n).Where(i => fold[i] != k).ToList();
            var eval = Enumerable.Range(0, n).Where(i => fold[i] == k).ToList();
            if (fit.Count == 0 || eval.Count == 0 || !fit.Any(i => labels[i]) || fit.All(i => labels[i])) continue;
            var mFull = LogisticRegression.FitWeighted(names, fit.Select(i => fullX[i]).ToList(), fit.Select(i => labels[i]).ToList(), fit.Select(i => weights[i]).ToList());
            var mBase = LogisticRegression.FitWeighted(["gainAtStart"], fit.Select(i => baseX[i]).ToList(), fit.Select(i => labels[i]).ToList(), fit.Select(i => weights[i]).ToList());
            foreach (var i in eval) { fullScore[i] = mFull.Predict(fullX[i]); baseScore[i] = mBase.Predict(baseX[i]); }
            double a = A09Auc(A09Order(fullScore, eval), fullScore, labels, weights, dayOf, null), b = A09Auc(A09Order(baseScore, eval), baseScore, labels, weights, dayOf, null);
            foldDiff[k] = double.IsFinite(a - b) ? a - b : null;
            foldLift[k] = A09Lift(eval.Select(i => rows[i]).ToList(), eval.Select(i => dayOf[i]).ToArray(), null) is var lf && double.IsFinite(lf) ? lf : null;
            s.Logger.LogInformation("[{Now}] explore late-runners: H1 fold {K} fit {Fit} eval {Eval}: AUC full {A:0.000} vs gain {B:0.000}", GapperStudyServices.Et(s.UtcNow), k + 1, fit.Count, eval.Count, a, b);
        }
        var scored = Enumerable.Range(0, n).Where(i => !double.IsNaN(fullScore[i]) && !double.IsNaN(baseScore[i])).ToList();
        var orderFull = A09Order(fullScore, scored);
        var orderBase = A09Order(baseScore, scored);
        double aucFull = A09Auc(orderFull, fullScore, labels, weights, dayOf, null), aucBase = A09Auc(orderBase, baseScore, labels, weights, dayOf, null);
        var h1 = A09Bootstrap(dayIds.Count, dr => A09Auc(orderFull, fullScore, labels, weights, dayOf, dr) - A09Auc(orderBase, baseScore, labels, weights, dayOf, dr), 0, draws, seed, ci);
        var h2 = A09Bootstrap(dayIds.Count, dr => A09Lift(rows, dayOf, dr), 1, draws, seed, ci);
        var q = MultipleTesting.BenjaminiHochberg([h1.POneSided, h2.POneSided]);
        bool pass1 = h1.Low > 0 && q[0] < A09BhQ, pass2 = h2.Low > 1 && q[1] < A09BhQ;

        double wF = rows.Where(r => r.X[5] > 0.5).Sum(r => r.Weight), wN = rows.Where(r => r.X[5] <= 0.5).Sum(r => r.Weight);
        double rateF = rows.Where(r => r.X[5] > 0.5 && r.Y).Sum(r => r.Weight) / Math.Max(1e-9, wF), rateN = rows.Where(r => r.X[5] <= 0.5 && r.Y).Sum(r => r.Weight) / Math.Max(1e-9, wN);
        md.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"Unit: (symbol, day) with an m0, weighted. Folds: five contiguous blocks of train months ({string.Join("; ", Enumerable.Range(0, A09Folds).Select(FoldLabel))}); each block is scored by models fit on the other four, and the out-of-fold scores are pooled. Logistic: `LogisticRegression.FitWeighted` defaults (L2 1.0, 500 epochs, rate 0.1), features standardized on the fitting rows only. Bootstrap: {draws:N0} day-clustered draws, seed {seed}, over the pooled scored rows with the fitted scores held fixed. p = share of draws at or below the null (difference <= 0, lift <= 1). Benjamini-Hochberg over the two at q < {A09BhQ:0.00}."));
        md.AppendLine();
        md.AppendLine("| id | metric | estimate | 95% interval | one-sided p | BH q | pass on train |");
        md.AppendLine("|---|---|---|---|---|---|---|");
        md.AppendLine(string.Create(CultureInfo.InvariantCulture, $"| A09-H1 | weighted AUC, features 1-11 ({N3(aucFull)}) minus gainAtStart alone ({N3(aucBase)}), pooled out of fold | {h1.Estimate:+0.000;-0.000} | [{h1.Low:+0.000;-0.000}, {h1.High:+0.000;-0.000}] | {h1.POneSided:0.0000} | {q[0]:0.0000} | {(pass1 ? "**yes**" : "no")} |"));
        md.AppendLine(string.Create(CultureInfo.InvariantCulture, $"| A09-H2 | weighted P(y \\| fresh catalyst at m0) {Sh(rateF)} / P(y \\| none) {Sh(rateN)} | {N3(h2.Estimate)} | [{N3(h2.Low)}, {N3(h2.High)}] | {h2.POneSided:0.0000} | {q[1]:0.0000} | {(pass2 ? "**yes**" : "no")} |"));
        md.AppendLine();
        md.AppendLine(string.Create(CultureInfo.InvariantCulture, $"Per fold, AUC difference: {string.Join(" / ", foldDiff.Select(v => v is { } x ? x.ToString("+0.000;-0.000", CultureInfo.InvariantCulture) : "n/a"))}; lift: {string.Join(" / ", foldLift.Select(v => v is { } x ? x.ToString("0.000", CultureInfo.InvariantCulture) : "n/a"))}. Scored rows {scored.Count:N0} on {dayIds.Count:N0} days; fresh catalyst at m0 on {rows.Count(r => r.X[5] > 0.5):N0} rows (weighted {wF:N0}), none on {rows.Count(r => r.X[5] <= 0.5):N0} (weighted {wN:N0})."));
        md.AppendLine();
        md.AppendLine("Features (share of rows missing; > 50% is dropped before fitting, others get a 0/1 missing indicator and the fitting rows' mean):");
        md.AppendLine();
        foreach (var j in Enumerable.Range(0, A09FeatureNames.Length))
            md.AppendLine($"- {A09FeatureNames[j]}: {Sh(missing[j])} missing{(dropped.Contains(j) ? " - **dropped**" : indicators.Contains(j) ? " (indicator added)" : "")}");
        md.AppendLine();

        // Descriptive: the model validation would score, fit on every train row (standardized coefficients).
        if (n > 0 && labels.Any(v => v) && labels.Any(v => !v))
        {
            var mAll = LogisticRegression.FitWeighted(names, fullX.ToList(), labels.ToList(), weights.ToList());
            md.AppendLine("Standardized coefficients of the full model fit on all train rows (the model a validation run scores; descriptive):");
            md.AppendLine();
            foreach (var (name, w) in mAll.Features.Zip(mAll.Weights).OrderByDescending(x => Math.Abs(x.Second)))
                md.AppendLine(string.Create(CultureInfo.InvariantCulture, $"- {name}: {w:+0.000;-0.000}"));
            md.AppendLine();
            json["fullModel"] = new { features = mAll.Features, weights = mAll.Weights, means = mAll.Means, stds = mAll.Stds, bias = mAll.Bias };
        }

        md.AppendLine("## Reading");
        md.AppendLine();
        const string toValidation = "passes on train; it goes to validation once (a follow-up ticket), and counts only with the same sign there";
        const string stops = "does not pass on train; it is not run on validation";
        md.AppendLine(
            FormattableString.Invariant($"Question 1 (descriptive): {Sh(lateShare)} of train +20% days ({census.Count(d => d.Late):N0} of {census.Count:N0}) first reached +20% in the regular session after a premarket that stayed below it; {Sh((double)census.Count(d => d.Pre0930) / Math.Max(1, census.Count))} did it before 09:30 and {Sh((double)census.Count(d => d.Post20) / Math.Max(1, census.Count))} only after 16:00. ") +
            $"Question 2 (A09-H1): {(pass1 ? toValidation : stops)}. " +
            $"Question 3 (A09-H2): {(pass2 ? toValidation : stops)}. " +
            "Nothing here is tradable and no strategy, template, radar or sell-by changes (amendment 09, 'What a pass does and does not allow').");
        md.AppendLine();

        json["hypotheses"] = new
        {
            h1 = new { aucFull, aucBase, difference = h1.Estimate, low = h1.Low, high = h1.High, p = h1.POneSided, q = q[0], pass = pass1, folds = foldDiff, days = h1.Days, draws = h1.Draws, rows = scored.Count },
            h2 = new { rateFresh = rateF, rateNone = rateN, lift = h2.Estimate, low = h2.Low, high = h2.High, p = h2.POneSided, q = q[1], pass = pass2, folds = foldLift, days = h2.Days, draws = h2.Draws, rows = n },
            features = A09FeatureNames.Select((f, j) => new { feature = f, missing = missing[j], dropped = dropped.Contains(j), indicator = indicators.Contains(j) }).ToList(),
            folds = Enumerable.Range(0, A09Folds).Select(FoldLabel).ToList(),
        };
        trials.Insert(0, new ExploreTrial
        {
            Track = LateRunnerTrack, Config = "A09-H1 features 1-11 vs gainAtStart alone, weighted logistic, 5 month-blocked folds", Metric = "pooled out-of-fold weighted AUC difference (full - gain)",
            FoldValues = foldDiff, Pooled = double.IsFinite(h1.Estimate) ? h1.Estimate : null, Rows = scored.Count,
            Note = string.Create(CultureInfo.InvariantCulture, $"aucFull={N3(aucFull)} aucGain={N3(aucBase)} ci=[{N3(h1.Low)},{N3(h1.High)}] p={h1.POneSided:0.0000} q={q[0]:0.0000} pass={pass1}"),
        });
        trials.Insert(1, new ExploreTrial
        {
            Track = LateRunnerTrack, Config = "A09-H2 fresh catalyst at m0", Metric = "weighted lift P(y|fresh)/P(y|none)",
            FoldValues = foldLift, Pooled = double.IsFinite(h2.Estimate) ? h2.Estimate : null, Rows = n,
            Note = string.Create(CultureInfo.InvariantCulture, $"ci=[{N3(h2.Low)},{N3(h2.High)}] p={h2.POneSided:0.0000} q={q[1]:0.0000} pass={pass2}"),
        });
        return Write(LateRunnerTrack, md, trials, json);
    }
}
