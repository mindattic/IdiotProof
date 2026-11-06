using System.Diagnostics;
using System.Text.Json;
using IdiotProof.Core.Catalysts;
using IdiotProof.Core.Data;
using IdiotProof.Core.Evidence;
using IdiotProof.Core.Services;
using IdiotProof.Evidence.Gappers;
using IdiotProof.Models;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Core.Research.Gappers;

public sealed record SnapshotStageRequest(string Split, bool Smoke, DateOnly? From, DateOnly? To);

public sealed record SnapshotStageResult(string Split, int SymbolDays, int Rows, int WithProfile, string ProfileFile, bool ProfileBuilt, int ProfileDays, string? HoldoutNote, double Minutes);

/// <summary>
/// <c>research gappers snapshot</c>: for every candidate day and control day of a split, at every
/// decision time of the frozen schedule, assembles <see cref="GapperFeatureInputs"/> from the file
/// store, the daily bars, catalysts and filings known by t, calls the one shared
/// <see cref="GapperFormationFeatures.Compute"/> and writes <c>Source = "backtest"</c> rows. The
/// time-of-day profile is fit on TRAIN control days only and saved before any ratio is computed; the
/// holdout runs once through <see cref="HoldoutLock"/>.
/// </summary>
public sealed class GapperSnapshotStage(GapperStudyServices s)
{
    public const string Source = "backtest";
    public const int FeatureVersion = 1;

    public async Task<SnapshotStageResult> RunAsync(SnapshotStageRequest req, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var p = s.Params;
        var split = req.Smoke ? GapperStudyServices.SmokeSplit : req.Split.ToLowerInvariant();
        var store = await s.StoreAsync(ct);
        string? holdoutNote = null;
        HoldoutLock? gate = null;
        if (split == "holdout")
        {
            gate = new HoldoutLock(s.Paths, p);
            var check = gate.Check("snapshot");
            if (!check.Allowed) throw new InvalidOperationException("Holdout locked: " + check.Reason);
            holdoutNote = $"holdout unlocked with {check.FrozenHash}";
        }

        var (profile, built, profileDays) = await ProfileAsync(split, req.Smoke, store, ct);
        var days = await ScopeAsync(split, req, ct);
        gate?.BeginStage("snapshot", s.UtcNow, new { split, symbolDays = days.Count, profileFile = s.Paths.ProfileFile(req.Smoke) });

        var schedule = p.DecisionTimes();
        var registry = Registry(p);
        int rows = 0, withProfile = 0, done = 0;
        foreach (var dayGroup in days.GroupBy(d => d.DateEt).OrderBy(g => g.Key))
        {
            var topMovers = await TopMoverRanksAsync(dayGroup.Key, split, ct);
            var toWrite = new List<GapperFormationSnapshotRow>();
            foreach (var u in dayGroup)
            {
                var ctx = await LoadAsync(u, store, ct);
                foreach (var dt in schedule)
                {
                    var decisionDay = dt.DayOffset == 0 ? u.DateEt : OffsetTradingDay(u.DateEt, dt.DayOffset);
                    var t = MinuteBarMath.ToUtc(decisionDay, dt.TimeEt);
                    var inputs = new GapperFeatureInputs
                    {
                        Symbol = u.Symbol, DateEt = u.DateEt, DecisionUtc = t, MinuteBars = ctx.DayBars, DailyBars = ctx.Daily,
                        PriorClose = u.AdjPrevClose, Profile = profile, AdvDecile = u.AdvDecile, Adv20Shares = (double)u.Adv20Shares,
                        PriorDayMinuteBars = ctx.PriorDays, Catalysts = ctx.Catalysts, Filings = ctx.Filings,
                        CorporateActions = new CorporateActionDates(ctx.ReverseSplit), AbnormalVolumeMultiple = p.Features.AbnormalVolumeMultiple,
                    };
                    var extras = new GapperFeatureRegistry();
                    foreach (var f in registry.Features) extras.Add(f);
                    extras.Add(new ConstantFeature("priorDayTopMoverRank", topMovers.TryGetValue(u.Symbol, out var rank) && rank <= p.Features.PriorDayTopMoverMaxRank ? rank : null));
                    extras.Add(new ConstantFeature("promoSourceShare", PromoShare(ctx.CatalystRows, t, p)));
                    var snap = GapperFormationFeatures.Compute(inputs, extras);
                    toWrite.Add(ToRow(snap, u, dt, split));
                    rows++;
                    if (snap.VolRatioTod is not null) withProfile++;
                }
            }
            await using (var db = await s.Db.CreateDbContextAsync(ct))
            {
                var symbols = dayGroup.Select(d => d.Symbol).ToList();
                var day = dayGroup.Key;
                await GapperStudyServices.DeleteAsync(db, db.GapperFormationSnapshots.Where(r => r.Source == Source && r.DateEt == day && symbols.Contains(r.Symbol)), ct);
                db.GapperFormationSnapshots.AddRange(toWrite);
                await db.SaveChangesAsync(ct);
            }
            done += dayGroup.Count();
            s.Logger.LogInformation("[{Now}] snapshot {Split}: {Day} done, {Done}/{Total} symbol-days, {Rows} rows", GapperStudyServices.Et(s.UtcNow), split, dayGroup.Key, done, days.Count, rows);
        }
        gate?.EndStage("snapshot", s.UtcNow, new { rows, symbolDays = days.Count });
        return new SnapshotStageResult(split, days.Count, rows, withProfile, s.Paths.ProfileFile(req.Smoke), built, profileDays, holdoutNote, Math.Round(sw.Elapsed.TotalMinutes, 2));
    }

    public static GapperFeatureRegistry Registry(GapperStudyParams p) => GapperFeatureRegistry.Default()
        .Add(new LeeMyklandJumpFeature(p.Features.LeeMykland.LocalWindowBars, p.Features.LeeMykland.Alpha))
        .Add(new PriorDaysCorwinSchultzFeature(p.Features.CorwinSchultz.MinBars));

    /// <summary>D + offset trading days (offset -1 = the previous session).</summary>
    public static DateOnly OffsetTradingDay(DateOnly d, int offset)
    {
        var cursor = d;
        for (int i = 0; i < Math.Abs(offset); i++) cursor = offset < 0 ? MarketCalendar.PreviousTradingDay(cursor) : MarketCalendar.NextTradingDay(cursor);
        return cursor;
    }

    private async Task<List<GapperStudyUniverseDay>> ScopeAsync(string split, SnapshotStageRequest req, CancellationToken ct)
    {
        await using var db = await s.Db.CreateDbContextAsync(ct);
        var q = db.GapperStudyUniverseDays.AsNoTracking().Where(r => r.Split == split && r.ExcludedReason == null && (r.IsCandidate || r.ControlForSymbol != null));
        if (req.From is { } f) q = q.Where(r => r.DateEt >= f);
        if (req.To is { } t) q = q.Where(r => r.DateEt <= t);
        return await q.OrderBy(r => r.DateEt).ThenBy(r => r.Symbol).ToListAsync(ct);
    }

    /// <summary>Loads the saved profile, or fits it on train control days (smoke: the smoke run's control days, else its candidate days) and saves it; other splits refuse without the file.</summary>
    private async Task<(TimeOfDayProfile? Profile, bool Built, int Days)> ProfileAsync(string split, bool smoke, FileBackedBarStore store, CancellationToken ct)
    {
        var path = s.Paths.ProfileFile(smoke);
        if (File.Exists(path)) return (TimeOfDayProfile.Load(path), false, 0);
        var fitOn = s.Params.Features.TodProfile.FitOn.ToLowerInvariant();
        if (!smoke && split != fitOn)
            throw new InvalidOperationException($"{Path.GetFileName(path)} does not exist; run `snapshot --split {fitOn}` first so the profile is fit on {fitOn} control days only.");

        await using var db = await s.Db.CreateDbContextAsync(ct);
        var fitSplit = smoke ? GapperStudyServices.SmokeSplit : fitOn;
        var controls = await db.GapperStudyUniverseDays.AsNoTracking()
            .Where(r => r.Split == fitSplit && r.ControlForSymbol != null)
            .Select(r => new { r.Symbol, r.DateEt, r.AdvDecile }).ToListAsync(ct);
        if (controls.Count == 0 && smoke)
            controls = await db.GapperStudyUniverseDays.AsNoTracking()
                .Where(r => r.Split == fitSplit && r.IsCandidate)
                .Select(r => new { r.Symbol, r.DateEt, r.AdvDecile }).ToListAsync(ct);
        var days = new List<(int, IReadOnlyList<Candle>)>();
        foreach (var c in controls)
        {
            var bars = store.ReadDay(c.Symbol, c.DateEt);
            if (bars.Count > 0) days.Add((c.AdvDecile, bars));
        }
        if (days.Count == 0) return (null, false, 0);
        var profile = TimeOfDayProfile.Build(days);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        profile.Save(path);
        s.Logger.LogInformation("[{Now}] profile: fit on {Days} {Split} days → {File}", GapperStudyServices.Et(s.UtcNow), days.Count, fitSplit, path);
        return (profile, true, days.Count);
    }

    private sealed record DayContext(
        IReadOnlyList<Candle> DayBars,
        IReadOnlyList<Candle> Daily,
        IReadOnlyList<IReadOnlyList<Candle>> PriorDays,
        IReadOnlyList<CatalystObservation> Catalysts,
        IReadOnlyList<CatalystEvent> CatalystRows,
        IReadOnlyList<FilingObservation> Filings,
        DateOnly? ReverseSplit);

    private async Task<DayContext> LoadAsync(GapperStudyUniverseDay u, FileBackedBarStore store, CancellationToken ct)
    {
        var p = s.Params;
        var dayBars = store.ReadDay(u.Symbol, u.DateEt);
        var priors = MarketCalendar.PriorTradingDays(u.DateEt, p.Features.OwnProfile.PriorDays)
            .Select(d => store.ReadDay(u.Symbol, d)).Where(b => b.Count > 0).ToList();

        await using var db = await s.Db.CreateDbContextAsync(ct);
        var dailyFrom = u.DateEt.AddDays(-60);
        var rawDaily = await db.GapperStudyDailyBars.AsNoTracking()
            .Where(b => b.Symbol == u.Symbol && b.DateEt >= dailyFrom && b.DateEt < u.DateEt).OrderBy(b => b.DateEt).ToListAsync(ct);
        var splits = (await db.GapperStudySplits.AsNoTracking().Where(x => x.Symbol == u.Symbol && x.ExDate <= u.DateEt).ToListAsync(ct))
            .Select(x => new SplitEvent(x.Symbol, x.ExDate, x.OldRate, x.NewRate)).ToList();
        var daily = rawDaily.Select(b =>
        {
            var f = GapperUniverseBuilder.SplitFactor(splits, b.DateEt, u.DateEt);
            return new Candle
            {
                Symbol = b.Symbol, StartUtc = MinuteBarMath.ToUtc(b.DateEt, TimeSpan.Zero), EndUtc = MinuteBarMath.ToUtc(b.DateEt, TimeSpan.Zero).AddDays(1),
                Open = b.Open * f, High = b.High * f, Low = b.Low * f, Close = b.Close * f, Volume = b.Volume / f, Note = b.Feed,
            };
        }).ToList();
        var reverse = splits.Where(x => x.OldRate > x.NewRate && (u.DateEt.DayNumber - x.ExDate.DayNumber) <= p.Features.DaysSinceMaxDays)
            .OrderByDescending(x => x.ExDate).Select(x => (DateOnly?)x.ExDate).FirstOrDefault();

        var catalystFrom = MinuteBarMath.ToUtc(u.DateEt.AddDays(-p.Features.Catalyst.NoveltyLookbackDays), TimeSpan.Zero);
        var catalystTo = MinuteBarMath.ToUtc(u.DateEt.AddDays(1), TimeSpan.Zero);
        var ids = db.CatalystEventSymbols.Where(x => x.Symbol == u.Symbol).Select(x => x.EventId);
        var catalystRows = await db.CatalystEvents.AsNoTracking()
            .Where(e => ids.Contains(e.Id) && e.PublishedUtc >= catalystFrom && e.PublishedUtc < catalystTo)
            .OrderBy(e => e.PublishedUtc).ToListAsync(ct);
        var catalysts = catalystRows.Select(e => new CatalystObservation(e.PublishedUtc, e.PreClass, e.Direction, e.SourceTier, e.CatalystScore, CatalystStampKind.IsUpperBound(e.StampKind))).ToList();

        var filingFrom = u.DateEt.AddDays(-p.Features.DaysSinceMaxDays - 5);
        var filings = (await db.EdgarFilingIndex.AsNoTracking()
                .Where(f => f.Symbol == u.Symbol && f.FiledDate >= filingFrom && f.FiledDate <= u.DateEt).ToListAsync(ct))
            .Select(f => new FilingObservation(f.FormType, f.AcceptedUtc ?? EasternTime.ToUtc(f.FiledDate.ToDateTime(new TimeOnly(23, 59, 59))),
                f.ItemsJson is null ? [] : JsonSerializer.Deserialize<List<string>>(f.ItemsJson) ?? []))
            .ToList();
        return new DayContext(dayBars, daily, priors, catalysts, catalystRows, filings, reverse);
    }

    /// <summary>Rank of each U(D-1) member by its D-1 daily return (1 = largest gain), from raw daily closes.</summary>
    private async Task<Dictionary<string, int>> TopMoverRanksAsync(DateOnly d, string split, CancellationToken ct)
    {
        var prev = MarketCalendar.PreviousTradingDay(d);
        var prev2 = MarketCalendar.PreviousTradingDay(prev);
        await using var db = await s.Db.CreateDbContextAsync(ct);
        var members = await db.GapperStudyUniverseDays.AsNoTracking().Where(r => r.DateEt == prev && r.Split == split && r.ExcludedReason == null).Select(r => r.Symbol).ToListAsync(ct);
        if (members.Count == 0) return new Dictionary<string, int>(StringComparer.Ordinal);
        var closes = await db.GapperStudyDailyBars.AsNoTracking()
            .Where(b => (b.DateEt == prev || b.DateEt == prev2) && members.Contains(b.Symbol))
            .Select(b => new { b.Symbol, b.DateEt, b.Close }).ToListAsync(ct);
        var returns = closes.GroupBy(c => c.Symbol)
            .Select(g =>
            {
                var a = g.FirstOrDefault(x => x.DateEt == prev2);
                var b = g.FirstOrDefault(x => x.DateEt == prev);
                return a is null || b is null || a.Close <= 0 ? null : new { g.Key, Ret = (double)(b.Close / a.Close - 1m) };
            })
            .Where(x => x is not null).OrderByDescending(x => x!.Ret).ThenBy(x => x!.Key, StringComparer.Ordinal).ToList();
        var ranks = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < returns.Count; i++) ranks[returns[i]!.Key] = i + 1;
        return ranks;
    }

    private static double? PromoShare(IReadOnlyList<CatalystEvent> rows, DateTime t, GapperStudyParams p)
    {
        var from = t.AddDays(-p.Features.Catalyst.NoveltyLookbackDays);
        var inWindow = rows.Where(r => r.PublishedUtc >= from && r.PublishedUtc <= t).ToList();
        if (inWindow.Count == 0) return null;
        var promo = inWindow.Count(r => r.PreClass == CatalystPreClass.PromoPr.ToString() || p.Features.Catalyst.PromotionalSources.Contains(r.Source, StringComparer.OrdinalIgnoreCase));
        return (double)promo / inWindow.Count;
    }

    public static GapperFormationSnapshotRow ToRow(GapperFormationSnapshot snap, GapperStudyUniverseDay u, DecisionTime dt, string split) => new()
    {
        Source = Source, Symbol = snap.Symbol, DateEt = snap.DateEt, DecisionLabel = dt.Label, DecisionTimeEt = TimeOnly.FromTimeSpan(dt.TimeEt),
        DecisionUtc = snap.DecisionUtc, Split = split, FeatureVersion = FeatureVersion, IsCandidate = u.IsCandidate, IsControl = u.ControlForSymbol != null,
        GapPct = snap.GapPct, PmCumVol = snap.PmCumVol, VolRatioTod = snap.VolRatioTod, OwnProfileRatio = snap.OwnProfileRatio,
        Velocity1m = snap.Velocity1m, Velocity3m = snap.Velocity3m, Velocity5m = snap.Velocity5m, Velocity15m = snap.Velocity15m,
        Acceleration = snap.Acceleration, VolumeRoc = snap.VolumeRoc, TradesProxy = snap.TradesProxy, SpreadProxy = snap.SpreadProxy,
        DistToPmHighPct = snap.DistToPmHighPct, MinsSinceFirstAbnormal = snap.MinsSinceFirstAbnormal, SessionVwap = snap.SessionVwap,
        AbnormalReturnD1 = snap.AbnormalReturnD1, AbnormalReturnD2 = snap.AbnormalReturnD2, AbnormalReturnD3 = snap.AbnormalReturnD3,
        AbnormalReturnD4 = snap.AbnormalReturnD4, AbnormalReturnD5 = snap.AbnormalReturnD5,
        VolumeRatioD1 = snap.VolumeRatioD1, VolumeRatioD2 = snap.VolumeRatioD2, VolumeRatioD3 = snap.VolumeRatioD3, VolumeRatioD4 = snap.VolumeRatioD4, VolumeRatioD5 = snap.VolumeRatioD5,
        DaysSinceReverseSplit = snap.DaysSinceReverseSplit, DaysSince424B = snap.DaysSince424B, DaysSinceS1 = snap.DaysSinceS1,
        CatalystScore = snap.CatalystScore, CatalystAgeMinutes = snap.CatalystAgeMinutes, CatalystDirection = snap.CatalystDirection, CatalystPreClass = snap.CatalystPreClass,
        CatalystStampIsUpperBound = snap.CatalystStampIsUpperBound, Halted = snap.Halted, ExtraJson = JsonSerializer.Serialize(snap.Extra), ComputedUtc = DateTime.UtcNow,
    };
}
