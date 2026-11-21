using System.Globalization;
using System.Text.Json;
using IdiotProof.Core.Catalysts;
using IdiotProof.Core.Data;
using IdiotProof.Core.Services;
using IdiotProof.Evidence.Gappers;
using IdiotProof.Models;
using IdiotProof.Scripting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IdiotProof.Core.Monitoring.Formation;

/// <summary>One live move start: m0's index from 04:00 ET, its close over the prior close, its volume and the profile's expected volume.</summary>
public sealed record MoveStartHit(int Index, TimeSpan EndEt, double Gain, double Volume, double ExpectedVolume);

/// <summary>Today's minute bars as amendment 09's per-minute arrays, then <see cref="MoveStartRule.Find"/> (IP-T628).</summary>
public static class MoveStartDetector
{
    public const double AbnormalVolumeMultiple = 5;

    /// <summary>
    /// The move start among bars that have ended by <paramref name="nowUtc"/>, or null: none yet, or a premarket high already
    /// reached +20% (outside amendment 09's population; <paramref name="excluded"/> is then true and the day can be skipped).
    /// </summary>
    public static MoveStartHit? Evaluate(IReadOnlyList<Candle> todayBars, DateOnly dateEt, DateTime nowUtc, double prevClose,
        double adv20Shares, int advDecile, TimeOfDayProfile profile, out bool excluded)
    {
        excluded = false;
        if (prevClose <= 0 || adv20Shares <= 0) return null;
        var start = MinuteBarMath.ToUtc(dateEt, MinuteBarMath.SessionStartEt);
        int n = MoveStartRule.RthEndIndex;
        var v = new double[n];
        var c = new double[n];
        var h = new double[n];
        Array.Fill(c, double.NaN);
        Array.Fill(h, double.NaN);
        int done = 0;
        foreach (var b in todayBars)
        {
            if (b.EndUtc > nowUtc) continue;
            int i = (int)Math.Floor((b.StartUtc - start).TotalMinutes);
            if (i < 0 || i >= n) continue;
            v[i] = (double)b.Volume;
            c[i] = (double)b.Close;
            h[i] = (double)b.High;
            done = Math.Max(done, i + 1);
        }
        for (int i = 0; i < MoveStartRule.RthStartIndex; i++)
            if (!double.IsNaN(h[i]) && h[i] / prevClose - 1 >= MoveStartRule.Level) { excluded = true; return null; }

        int m0 = MoveStartRule.Find(v, c, h, prevClose, adv20Shares, advDecile, profile, AbnormalVolumeMultiple,
            MoveStartRule.Level, MoveStartRule.RthStartIndex, Math.Min(done, n));
        if (m0 < 0) return null;
        var endEt = MinuteBarMath.SessionStartEt + TimeSpan.FromMinutes(m0 + 1);
        return new MoveStartHit(m0, endEt, c[m0] / prevClose - 1, v[m0], adv20Shares * profile.MinuteFractionAt(advDecile, endEt));
    }
}

/// <summary>
/// Move-start shadow rows in <c>GapperFormationSnapshots</c> under their own <see cref="Source"/>, one per symbol-day, so
/// they never collide with the recorder's <c>live</c> rows, its retention or the amendment 06 shadow.
/// </summary>
public sealed class MoveStartShadowStore(IDbContextFactory<AppDbContext> dbFactory)
{
    public const string Source = "live-m0";
    public const string Label = "m0";
    public const string Reason = "move-start";
    public const string GainKey = "moveStartGain";
    public const string VolumeKey = "moveStartVolume";
    public const string ExpectedVolumeKey = "moveStartExpectedVolume";
    public const string PrevCloseKey = "prevClose";
    public const string FreshCatalystKey = "freshCatalyst";
    public const string DetectionLagKey = "detectionLagMinutes";

    /// <summary>Writes the row unless this symbol-day already has one (the first m0 is the only m0); true when written.</summary>
    public async Task<bool> TryAddAsync(FormationSnapshotRow row, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await db.GapperFormationSnapshots.AnyAsync(e => e.Source == Source && e.Symbol == row.Symbol && e.DateEt == row.DateEt && e.DecisionLabel == Label, ct))
            return false;
        var e = SqlGapperSnapshotSink.Apply(row, new GapperFormationSnapshotRow());
        e.Source = Source;
        e.DecisionLabel = Label;
        e.IsCandidate = true;
        e.IsControl = false;
        db.GapperFormationSnapshots.Add(e);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { return false; }
        return true;
    }

    public async Task<HashSet<string>> SymbolsOnAsync(DateOnly dateEt, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var symbols = await db.GapperFormationSnapshots.AsNoTracking()
            .Where(e => e.Source == Source && e.DateEt == dateEt).Select(e => e.Symbol).ToListAsync(ct);
        return new HashSet<string>(symbols, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<List<GapperFormationSnapshotRow>> ReadAsync(DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var q = db.GapperFormationSnapshots.AsNoTracking().Where(e => e.Source == Source);
        if (from is { } f) q = q.Where(e => e.DateEt >= f);
        if (to is { } t) q = q.Where(e => e.DateEt <= t);
        return await q.OrderBy(e => e.DateEt).ThenBy(e => e.DecisionUtc).ThenBy(e => e.Symbol).ToListAsync(ct);
    }
}

/// <summary>
/// The move-start watch (IP-T628; gapper study amendment 11's closing note, option 4): amendment 09's move start (m0),
/// validated on train and validation (A09-H1, A09-H2), evaluated live for the formation universe from 09:30 to 16:00 ET.
/// The first m0 of a symbol-day becomes one <see cref="MoveStartShadowStore.Source"/> row (the formation features at the
/// end of m0) and one <see cref="AlertAudit"/> audit row. Idle unless <c>formation.enabled</c> and
/// <c>formation.moveStart.enabled</c>, an owner, paper, the Monitor lease and the time-of-day profile with decile edges.
/// Watch only: no trading rule off m0 survived (H29, H30), so it never arms, places or sizes anything.
/// </summary>
public sealed class MoveStartWatchWorker(
    FormationSettings settings,
    MonitorRuntimeState monitor,
    FormationUniverseBuilder universeBuilder,
    IFormationBarSource bars,
    MoveStartShadowStore store,
    IServiceScopeFactory scopes,
    AuditLogRepository audit,
    TimeProvider time,
    ILogger<MoveStartWatchWorker> log) : BackgroundService
{
    public const string AlertAudit = "move-start-alert";
    public const string ErrorAudit = "move-start-error";
    public static readonly TimeSpan WindowStartEt = new(9, 31, 0);
    public static readonly TimeSpan WindowEndEt = new(16, 1, 0);
    public static readonly TimeSpan Cadence = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan UniverseRefresh = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan CatalystLookback = TimeSpan.FromHours(24);
    private static readonly TimeSpan ErrorAuditSpacing = TimeSpan.FromMinutes(15);

    private DateOnly day;
    private HashSet<string> done = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<FormationMember> universe = [];
    private DateTime universeBuiltUtc;
    private TimeOfDayProfile? profile;
    private string? profilePath;
    private DateTime profileStampUtc;
    private DateTime lastErrorAuditUtc;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { await ErrorAsync($"tick: {ex.Message}", null, stoppingToken); }
            try { await Task.Delay(Cadence, time, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    public static bool InWindow(DateTime nowEt)
    {
        if (nowEt.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) return false;
        if (MarketTime.IsMarketHoliday(DateOnly.FromDateTime(nowEt))) return false;
        return nowEt.TimeOfDay >= WindowStartEt && nowEt.TimeOfDay < WindowEndEt;
    }

    /// <summary>One pass; returns the number of new move starts recorded.</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        var cfg = await settings.ReadAsync(ct);
        var now = time.GetUtcNow().UtcDateTime;
        var nowEt = MinuteBarMath.ToEastern(now);
        if (!cfg.Enabled || !cfg.MoveStartEnabled || cfg.OwnerUserId is not { } owner || !cfg.IsPaper || !InWindow(nowEt)) return 0;
        if (cfg.TodProfilePath is null || (cfg.TodDecileEdgesDollars.Count == 0 && cfg.TodDecileEdges.Count == 0)) return 0;
        if (!await monitor.IsLeaderAsync(ct)) return 0;
        if (!LoadProfile(cfg.TodProfilePath)) return 0;

        var dateEt = DateOnly.FromDateTime(nowEt);
        if (day != dateEt)
        {
            day = dateEt;
            done = await store.SymbolsOnAsync(dateEt, ct);
            universe = [];
            universeBuiltUtc = default;
        }
        if (universe.Count == 0 || now - universeBuiltUtc >= UniverseRefresh)
        {
            var built = await universeBuilder.BuildAsync(owner, now, cfg, ct);
            universe = built.Members;
            universeBuiltUtc = now;
            log.LogInformation("Move-start watch universe: {Note}.", built.Note);
        }

        var priorCloseUtc = FormationUniverseBuilder.PriorCloseUtc(now);
        int recorded = 0;
        var errors = new List<string>();
        foreach (var member in universe)
        {
            ct.ThrowIfCancellationRequested();
            if (done.Contains(member.Symbol)) continue;
            try
            {
                if (await EvaluateAsync(member.Symbol, dateEt, now, priorCloseUtc, cfg, owner, ct)) recorded++;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                errors.Add($"{member.Symbol}: {ex.Message}");
                log.LogWarning(ex, "Move-start watch: {Symbol} failed this pass — continuing with the rest.", member.Symbol);
            }
        }
        if (errors.Count > 0) await ErrorAsync($"{errors.Count} symbol(s) failed: {string.Join("; ", errors.Take(3))}", owner, ct);
        return recorded;
    }

    private async Task<bool> EvaluateAsync(string symbol, DateOnly dateEt, DateTime now, DateTime priorCloseUtc, FormationConfig cfg, Guid owner, CancellationToken ct)
    {
        var daily = await bars.DailyBarsAsync(symbol, dateEt, ct);
        if (daily.Count == 0) return false;
        var prev = (double)daily[^1].Close;
        var adv20 = GapperFormationScanner.Adv20(daily);
        var decile = cfg.TodDecileEdgesDollars.Count > 0
            ? GapperFormationScanner.Decile(GapperFormationScanner.Adv20Dollars(daily), cfg.TodDecileEdgesDollars)
            : GapperFormationScanner.Decile(adv20, cfg.TodDecileEdges);
        if (adv20 is not { } adv || decile is not { } dec) return false;

        var minuteBars = await bars.TodayMinuteBarsAsync(symbol, dateEt, now, ct);
        var hit = MoveStartDetector.Evaluate(minuteBars, dateEt, now, prev, adv, dec, profile!, out var excluded);
        if (excluded) { done.Add(symbol); return false; }
        if (hit is null) return false;

        var m0EndUtc = MinuteBarMath.ToUtc(dateEt, hit.EndEt);
        List<CatalystEvent> events;
        using (var scope = scopes.CreateScope())
            events = await scope.ServiceProvider.GetRequiredService<CatalystLookup>().RecentAsync([symbol], m0EndUtc - CatalystLookback, ct);
        var known = events.Where(e => e.PublishedUtc <= m0EndUtc).ToList();
        bool fresh = known.Any(e => e.PublishedUtc > priorCloseUtc && !string.Equals(e.PreClass, nameof(CatalystPreClass.NoCatalyst), StringComparison.OrdinalIgnoreCase));

        var snap = GapperFormationFeatures.Compute(new GapperFeatureInputs
        {
            Symbol = symbol,
            DateEt = dateEt,
            DecisionUtc = m0EndUtc,
            MinuteBars = minuteBars,
            DailyBars = daily,
            Profile = profile,
            AdvDecile = dec,
            Adv20Shares = adv,
            PriorDayMinuteBars = await bars.PriorDayMinuteBarsAsync(symbol, dateEt, ct),
            Catalysts = known.Select(e => new CatalystObservation(e.PublishedUtc, e.PreClass, e.Direction, e.SourceTier, e.CatalystScore,
                string.Equals(e.StampKind, "batch", StringComparison.OrdinalIgnoreCase))).ToList(),
        });
        double lag = Math.Round((now - m0EndUtc).TotalMinutes, 1);
        snap = snap with
        {
            Extra = new Dictionary<string, double>(snap.Extra, StringComparer.Ordinal)
            {
                [MoveStartShadowStore.GainKey] = Math.Round(hit.Gain, 6),
                [MoveStartShadowStore.VolumeKey] = hit.Volume,
                [MoveStartShadowStore.ExpectedVolumeKey] = Math.Round(hit.ExpectedVolume, 2),
                [MoveStartShadowStore.PrevCloseKey] = prev,
                [MoveStartShadowStore.FreshCatalystKey] = fresh ? 1 : 0,
                [MoveStartShadowStore.DetectionLagKey] = lag,
            },
        };
        var label = hit.EndEt.ToString(@"hh\:mm", CultureInfo.InvariantCulture);
        if (!await store.TryAddAsync(FormationSnapshotRow.FromSnapshot(snap, label, MoveStartShadowStore.Reason, false, now), ct))
        {
            done.Add(symbol);
            return false;
        }
        done.Add(symbol);

        var message = FormattableString.Invariant(
            $"{symbol} move start (amendment 09's m0) ending {label} ET: close {hit.Gain:+0.0%;-0.0%} over the prior close {prev:0.####}, volume {hit.Volume:N0} = {hit.Volume / Math.Max(1, hit.ExpectedVolume):0.0}x the profile's minute, fresh catalyst {(fresh ? "yes" : "no")}, seen {lag:0.#} min after. ") +
            "Watch only: the signal is validated (A09-H1/H2) but no ride off it survived (H29, H30); nothing was armed.";
        var data = JsonSerializer.Serialize(new
        {
            symbol, dateEt = dateEt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), m0EndEt = label, gain = Math.Round(hit.Gain, 6), prevClose = prev,
            volume = hit.Volume, expectedVolume = Math.Round(hit.ExpectedVolume, 2), freshCatalyst = fresh, detectionLagMinutes = lag,
            snap.VolRatioTod, snap.Velocity3m, snap.CatalystScore, snap.CatalystPreClass,
        });
        await audit.LogAsync(AlertAudit, message, userId: owner, dataJson: data, ct: ct);
        log.LogInformation("Move-start watch: {Message}", message);
        return true;
    }

    private bool LoadProfile(string path)
    {
        try
        {
            var stamp = File.GetLastWriteTimeUtc(path);
            if (profile is not null && profilePath == path && stamp == profileStampUtc) return true;
            profile = TimeOfDayProfile.Load(path);
            profilePath = path;
            profileStampUtc = stamp;
            return true;
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Move-start watch: time-of-day profile at {Path} could not be loaded; idle.", path);
            return profile is not null && profilePath == path;
        }
    }

    private async Task ErrorAsync(string message, Guid? owner, CancellationToken ct)
    {
        log.LogWarning("Move-start watch: {Message}", message);
        var now = time.GetUtcNow().UtcDateTime;
        if (now - lastErrorAuditUtc < ErrorAuditSpacing) return;
        lastErrorAuditUtc = now;
        try { await audit.LogAsync(ErrorAudit, $"Move-start watch: {message}", userId: owner, ct: ct); }
        catch (Exception ex) { log.LogDebug(ex, "move-start error audit failed"); }
    }
}
