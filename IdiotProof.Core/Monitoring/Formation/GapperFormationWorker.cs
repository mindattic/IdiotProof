using IdiotProof.Core.Data;
using IdiotProof.Core.Services;
using IdiotProof.DataFeeds;
using IdiotProof.DataFeeds.Streaming;
using IdiotProof.Evidence.Gappers;
using IdiotProof.Scripting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IdiotProof.Core.Monitoring.Formation;

/// <summary>
/// The premarket formation recorder (IP-T443), in the shape of <see cref="CatalystIntakeWorker"/>:
/// idle unless SettingsKv <c>formation.enabled</c> is "true", this instance holds the Monitor
/// leader lease, an owner is set and the owner's route is paper. On weekdays it builds the
/// universe at 03:55 ET (refreshed every five minutes), joins the one SIP socket for bars and
/// halt statuses, and from 04:00 to 09:45 ET ticks <see cref="GapperFormationScanner"/> every
/// <c>formation.tickSeconds</c>. It writes research rows and audit lines only; it never arms or
/// trades (BIBLE:75-76). The same loop runs the deferred scheduled <c>gapper auto</c> pass when
/// <c>gapper.auto.scheduledEt</c> is set.
/// </summary>
public sealed class GapperFormationWorker(
    FormationSettings settings,
    FormationState state,
    MonitorRuntimeState monitor,
    UserKeyService userKeys,
    UserBrokerResolver brokers,
    FormationUniverseBuilder universeBuilder,
    FormationBarCache barCache,
    IGapperSnapshotSink sink,
    AutoGapperScanner autoGapper,
    IDbContextFactory<AppDbContext> dbFactory,
    IServiceScopeFactory scopes,
    AuditLogRepository auditLog,
    TimeProvider time,
    ILogger<GapperFormationWorker> log) : BackgroundService
{
    public const string AuditStart = "formation-start";
    public const string AuditStop = "formation-stop";
    public const string AuditError = "formation-error";
    public const string AuditBlocked = GapperFormationScanner.BlockedAudit;
    public const string StreamOwner = "formation";
    public const string ScheduledPhase = "scheduled";

    public static readonly TimeSpan WindowStartEt = new(3, 55, 0);
    public static readonly TimeSpan WindowEndEt = new(9, 46, 0);
    public static readonly TimeSpan RecordingStartEt = new(4, 0, 0);
    public static readonly TimeSpan UniverseRefresh = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan IdleCadence = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan RefusalCooldown = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan ScheduledAutoWindow = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ErrorAuditSpacing = TimeSpan.FromMinutes(5);

    internal Func<AlpacaStreamKeys, AlpacaStreamingClient?> CreateStream { get; init; } = keys =>
        new AlpacaStreamingClient(keys.KeyId, keys.Secret, Environment.GetEnvironmentVariable("IDIOTPROOF_ALPACA_FEED") ?? "sip");

    internal Func<AlpacaStreamingClient?> FindSharedStream { get; init; } = () => AlpacaStreamingClient.Started.FirstOrDefault();

    private bool running;
    private AlpacaStreamingClient? stream;
    private bool ownsStream;
    private DateTime blockedUntilUtc;
    private int tickSeconds = 15;
    private TimeOfDayProfile? profile;
    private string? profilePath;
    private DateTime profileStampUtc;
    private readonly Dictionary<string, DateTime> lastErrorAudit = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                bool active;
                try { active = await RunOnceAsync(stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    active = false;
                    await ErrorAsync("tick", ex.Message, null, stoppingToken);
                }
                try { await Task.Delay(active ? TimeSpan.FromSeconds(tickSeconds) : IdleCadence, time, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }
        finally
        {
            await StopAsync("host stopping", CancellationToken.None);
        }
    }

    /// <summary>One pass: reconcile with the settings, the lease and the clock; true when the recorder is inside its window and active.</summary>
    public async Task<bool> RunOnceAsync(CancellationToken ct)
    {
        var cfg = await settings.ReadAsync(ct);
        tickSeconds = cfg.TickSeconds;
        var now = Now;
        var nowEt = MinuteBarMath.ToEastern(now);
        var dateEt = DateOnly.FromDateTime(nowEt);
        var isLeader = (cfg.Enabled || cfg.ScheduledAutoEt is not null) && await monitor.IsLeaderAsync(ct);
        state.SetConfig(cfg, cfg.Enabled && isLeader);

        await ScheduledAutoAsync(cfg, now, nowEt, isLeader, ct);

        if (!cfg.Enabled || !isLeader || cfg.OwnerUserId is null)
        {
            await StopAsync(!cfg.Enabled ? "disabled" : !isLeader ? "not the leader" : "owner unset", ct);
            return false;
        }
        if (!cfg.IsPaper)
        {
            await StopAsync($"{FormationSettings.BrokerModeKey} is '{cfg.BrokerMode}', not paper", ct);
            await ErrorAsync("broker-mode", $"{FormationSettings.BrokerModeKey} must be paper (is '{cfg.BrokerMode}'); the recorder stays off", cfg.OwnerUserId, ct);
            return false;
        }
        if (!InWindow(nowEt))
        {
            await StopAsync("outside 03:55-09:45 ET on a trading day", ct);
            await FormationRetention.PruneIfDueAsync(sink, state, cfg, now, ct);
            return false;
        }
        if (now < blockedUntilUtc) return false;

        state.EnsureDay(dateEt);
        if (!running && !await StartAsync(cfg, now, ct)) return false;

        await EnsureUniverseAsync(cfg, now, ct);
        if (nowEt.TimeOfDay < RecordingStartEt || state.Universe.Count == 0) return true;

        LoadProfile(cfg);
        using var scope = scopes.CreateScope();
        var scanner = scope.ServiceProvider.GetRequiredService<GapperFormationScanner>();
        var summary = await scanner.TickAsync(now, state.Universe, cfg, profile, cfg.OwnerUserId, ct);
        if (summary.Errors.Count > 0)
            await ErrorAsync("symbols", $"{summary.Errors.Count} symbol(s) failed: {string.Join("; ", summary.Errors.Take(3))}", cfg.OwnerUserId, ct);
        if (stream is not null) state.SetStream($"{stream.Status}, {stream.SubscribedCount} symbol(s), {stream.Messages} frame(s), {barCache.StreamedBars} bar(s) streamed");
        return true;
    }

    /// <summary>Weekdays that are not market holidays, 03:55 up to (not including) 09:46 ET.</summary>
    public static bool InWindow(DateTime nowEt)
    {
        if (nowEt.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) return false;
        if (MarketTime.IsMarketHoliday(DateOnly.FromDateTime(nowEt))) return false;
        return nowEt.TimeOfDay >= WindowStartEt && nowEt.TimeOfDay < WindowEndEt;
    }

    private async Task<bool> StartAsync(FormationConfig cfg, DateTime now, CancellationToken ct)
    {
        var owner = cfg.OwnerUserId!.Value;
        var broker = await brokers.ResolveAsync(owner, "Paper", ct);
        if (!broker.IsPaper)
        {
            blockedUntilUtc = now + RefusalCooldown;
            var msg = $"paper route resolves to {broker.BrokerType} LIVE — the formation recorder refuses to run; retrying in {RefusalCooldown.TotalMinutes:0} min";
            log.LogError("Formation: {Msg}.", msg);
            await auditLog.LogAsync(AuditBlocked, $"Formation recorder blocked: {msg}.", userId: owner, ct: ct);
            state.RecordError(msg, now);
            return false;
        }

        await AttachStreamAsync(owner, ct);
        running = true;
        state.MarkStarted(now);
        await auditLog.LogAsync(AuditStart, $"Formation recorder started (paper; tick {cfg.TickSeconds}s; universe cap {cfg.MaxSymbols}; stream {(stream is null ? "none" : ownsStream ? "own SIP socket" : "shared with the Monitor")}).", userId: owner, ct: ct);
        log.LogInformation("Formation recorder started for owner {Owner}.", owner);
        return true;
    }

    private async Task StopAsync(string why, CancellationToken ct)
    {
        if (!running) return;
        running = false;
        await DetachStreamAsync();
        state.MarkStopped(Now, why);
        log.LogInformation("Formation recorder stopped ({Why}).", why);
        try { await auditLog.LogAsync(AuditStop, $"Formation recorder stopped: {why}.", ct: ct); }
        catch (Exception ex) { log.LogDebug(ex, "stop audit failed"); }
    }

    private async Task AttachStreamAsync(Guid owner, CancellationToken ct)
    {
        stream = FindSharedStream();
        ownsStream = false;
        if (stream is null)
        {
            var k = await userKeys.GetOrCreateAsync(owner, ct);
            if (!string.IsNullOrWhiteSpace(k.AlpacaApiKeyId) && !string.IsNullOrWhiteSpace(k.AlpacaApiSecretKey))
            {
                stream = CreateStream(new AlpacaStreamKeys(k.AlpacaApiKeyId, k.AlpacaApiSecretKey));
                ownsStream = stream is not null;
                stream?.Start();
            }
        }
        if (stream is null)
        {
            state.SetStream("none (no shared socket and no owner data keys); halts unknown, bars from REST every 5 min");
            return;
        }
        stream.StatusReceived += OnStatus;
        stream.BarReceived += barCache.Push;
        state.SetStream(stream.Status);
    }

    private async Task DetachStreamAsync()
    {
        if (stream is null) return;
        stream.StatusReceived -= OnStatus;
        stream.BarReceived -= barCache.Push;
        try { await stream.SetSymbolsAsync(StreamOwner, [], CancellationToken.None); }
        catch (Exception ex) { log.LogDebug(ex, "formation unsubscribe failed"); }
        if (ownsStream) await stream.DisposeAsync();
        stream = null;
        ownsStream = false;
    }

    private void OnStatus(TradingStatus status)
    {
        if (status.Halted is { } halted) state.SetHalted(status.Symbol, halted, status.TimestampUtc);
    }

    private async Task EnsureUniverseAsync(FormationConfig cfg, DateTime now, CancellationToken ct)
    {
        if (state.UniverseBuiltUtc is { } built && now - built < UniverseRefresh) return;
        var result = await universeBuilder.BuildAsync(cfg.OwnerUserId!.Value, now, cfg, ct);
        state.SetUniverse(result.Members, now, result.Note);
        log.LogInformation("Formation universe: {Note}.", result.Note);
        if (stream is not null)
        {
            try { await stream.SetSymbolsAsync(StreamOwner, result.Members.Select(m => m.Symbol), ct); }
            catch (Exception ex) { log.LogDebug(ex, "formation subscribe failed; the reconnect path re-subscribes"); }
        }
    }

    private void LoadProfile(FormationConfig cfg)
    {
        if (cfg.TodProfilePath is null)
        {
            profile = null;
            profilePath = null;
            state.SetProfileLoaded(false);
            return;
        }
        try
        {
            var stamp = File.GetLastWriteTimeUtc(cfg.TodProfilePath);
            if (profile is not null && profilePath == cfg.TodProfilePath && stamp == profileStampUtc) return;
            profile = TimeOfDayProfile.Load(cfg.TodProfilePath);
            profilePath = cfg.TodProfilePath;
            profileStampUtc = stamp;
            state.SetProfileLoaded(true);
            log.LogInformation("Formation: time-of-day profile v{Version} loaded from {Path}.", profile.Version, cfg.TodProfilePath);
        }
        catch (Exception ex)
        {
            if (profile is null || profilePath != cfg.TodProfilePath)
            {
                profile = null;
                state.SetProfileLoaded(false);
            }
            log.LogWarning(ex, "Formation: time-of-day profile at {Path} could not be loaded; VolRatioTod stays null.", cfg.TodProfilePath);
        }
    }

    /// <summary>The deferred 03:55 ET <c>gapper auto</c> pass: once per trading day inside [scheduledEt, scheduledEt + 10 min), leader-only, through the unchanged <see cref="AutoGapperScanner"/>.</summary>
    internal async Task<bool> ScheduledAutoAsync(FormationConfig cfg, DateTime now, DateTime nowEt, bool isLeader, CancellationToken ct)
    {
        if (cfg.ScheduledAutoEt is not { } at || !isLeader || cfg.OwnerUserId is null) return false;
        var dateEt = DateOnly.FromDateTime(nowEt);
        if (nowEt.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday || MarketTime.IsMarketHoliday(dateEt)) return false;
        if (nowEt.TimeOfDay < at || nowEt.TimeOfDay >= at + ScheduledAutoWindow) return false;
        if (state.ScheduledAutoLastRunEt == dateEt) return false;

        var day = dateEt.ToString("yyyy-MM-dd");
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            if (await db.AutoGapperScans.AsNoTracking().AnyAsync(s => s.ScanEtDate == day && s.Phase == ScheduledPhase, ct))
            {
                state.MarkScheduledAutoRun(dateEt);
                return false;
            }
        }
        state.MarkScheduledAutoRun(dateEt);
        var summary = await autoGapper.RunScanAsync(cfg.OwnerUserId.Value, dryRun: false, phase: ScheduledPhase, ct);
        log.LogInformation("Scheduled auto-gapper ({At} ET): screened {Screened}, qualified {Qualified}, armed {Armed}, skipped {Skipped} ({Note}).",
            FormationSchedule.Label(at), summary.Screened, summary.Qualified, summary.Armed, summary.Skipped, summary.Note);
        return true;
    }

    private async Task ErrorAsync(string kind, string message, Guid? owner, CancellationToken ct)
    {
        state.RecordError($"{kind}: {message}", Now);
        log.LogWarning("Formation {Kind}: {Message}", kind, message);
        lock (lastErrorAudit)
        {
            if (lastErrorAudit.TryGetValue(kind, out var last) && Now - last < ErrorAuditSpacing) return;
            lastErrorAudit[kind] = Now;
        }
        try { await auditLog.LogAsync(AuditError, $"Formation {kind}: {message}", userId: owner, ct: ct); }
        catch (Exception ex) { log.LogDebug(ex, "error audit failed"); }
    }

    private DateTime Now => time.GetUtcNow().UtcDateTime;
}
