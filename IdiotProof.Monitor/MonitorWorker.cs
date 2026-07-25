using IdiotProof.Blazor.Data;
using IdiotProof.Blazor.Services;
using IdiotProof.Brokers;
using IdiotProof.DataFeeds;
using IdiotProof.Engine;
using IdiotProof.Engine.Settings;
using IdiotProof.Engine.Storage;
using IdiotProof.Models;
using IdiotProof.Scripting;
using IdiotProof.Shared.Risk;
using IdiotProof.Strategies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IdiotProof.Monitor;

/// <summary>
/// The unified always-on evaluator (RFC 0002 / IP-A8). One pipeline:
///
///   SQL Strategy rows (edited live in the Blazor UI)
///     → per-tick re-read (UI changes apply without restart)
///     → candles from the configured feed (Alpaca REST + websocket stream, Mock fallback)
///     → entry conditions walked one-by-one → ConditionProgress rows (UI badges)
///     → three gates: conditions → LLM voter panel → RiskGuardian (IP-LAW-1)
///     → entry order through BrokerRouter (Sandbox default, IP-LAW-3;
///       premarket = limit + extended_hours on Alpaca)
///     → open positions tracked on the Strategy row; exits evaluated every
///       tick by GapperExitEvaluator (sell-by / stops / target / peak-giveback)
///     → realized P&amp;L fed back into RiskGuardian's daily circuit breaker.
///
/// The loop body runs under SupervisedLoop (IP-LAW-5): per-tick failures are
/// caught, backed off, and heart-beaten; the evaluator never dies on one bad tick.
/// Exit orders are risk-reducing: they skip the LLM panel by design but are
/// always audit-logged.
/// </summary>
public sealed class MonitorWorker(
    StrategyRepository strategyRepo,
    ConditionProgressRepository progressRepo,
    AuditLogRepository auditLogRepo,
    TradeDiaryRepository tradeDiary,
    LlmVotingService llmVoting,
    RiskGuardianService riskGuardianService,
    AppSettings appSettings,
    IMarketDataFeed feed,
    UserBrokerResolver brokerResolver,
    MonitorDatabase database,
    IStorageProvider storage,
    IdiotProof.Blazor.Services.LiveBarRepository liveBarRepo,
    PremarketFadeScanner premarketFadeScanner,
    IDbContextFactory<AppDbContext> dbFactory,
    ILogger<MonitorWorker> logger) : BackgroundService
{
    /// <summary>
    /// Interval between evaluation passes. Default 1s so a live market is
    /// reacted to within ~1 second: the Alpaca websocket stream keeps the last
    /// trade sub-second fresh (AppendLiveTick), and each pass re-checks price-
    /// based triggers/stops against it — no REST call per pass (candles are
    /// cached + stream-fed). Override via IDIOTPROOF_MONITOR_INTERVAL ("1s","5s","1m").
    /// </summary>
    private static readonly TimeSpan EvaluationInterval =
        TryParseInterval(Environment.GetEnvironmentVariable("IDIOTPROOF_MONITOR_INTERVAL"))
        ?? TimeSpan.FromSeconds(1);

    /// <summary>Minute bars — matches the Alpaca stream's "b" messages.</summary>
    private static readonly TimeSpan BarSize = TimeSpan.FromMinutes(1);

    /// <summary>How many minute bars to keep per symbol (4 hours — enough for EMA200 convergence).</summary>
    private const int CandleWindow = 240;

    /// <summary>REST re-sync cadence; between syncs the stream keeps the cache fresh.</summary>
    private static readonly TimeSpan RestRefresh = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Refresh cadence when the last fetch returned NO bars (overnight, halted,
    /// bad symbol). Short enough that the 4:00 ET premarket open is picked up
    /// within seconds — but without this, an empty window was never cached at
    /// all and the Monitor hammered the bars endpoint every tick all night
    /// (12 requests/min/symbol), burning the Alpaca rate limit right before
    /// the window gappers arm in.
    /// </summary>
    private static readonly TimeSpan EmptyWindowRefresh = TimeSpan.FromSeconds(30);

    private readonly Dictionary<string, (List<Candle> Candles, DateTime FetchedUtc)> candleCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (DateOnly DayEt, decimal? Close, DateTime FetchedUtc)> previousCloseCache = new(StringComparer.OrdinalIgnoreCase);

    // Throttle audit-log writes for events that would otherwise fire every tick.
    private readonly Dictionary<Guid, DateTime> lastHoldingLogUtc = new();
    private readonly Dictionary<Guid, DateTime> lastQuarantineLogUtc = new();
    private static readonly TimeSpan HoldingLogInterval    = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan QuarantineLogInterval = TimeSpan.FromMinutes(5);
    private DateTime monitorStartUtc;
    private DateTime lastPruneUtc = DateTime.MinValue;
    private DateTime lastFadeScanUtc = DateTime.MinValue;
    private static readonly TimeSpan FadeScanInterval = TimeSpan.FromMinutes(5);

    // Trading-schedule state
    private TradingWindow lastWindow        = TradingWindow.Hibernate;
    private DateTime      lastHibernatePing = DateTime.MinValue;
    private List<IdiotProof.Blazor.Data.Strategy> cachedActive = [];

    // Throttle live-bar writes: write at most once every 10 seconds per strategy
    // (the Monitor evaluates every 1s; writing every tick would hammer the DB).
    private readonly Dictionary<Guid, DateTime> lastBarWrite = new();

    /// <summary>
    /// Re-check cadence when the previous close came back NULL. IP-A18 made
    /// nulls retry (a cached null disabled gap strategies all day), but an
    /// unbounded retry hammered the daily-bars endpoint every tick during an
    /// outage — the mirror of the empty-candle-window problem. 30 s keeps the
    /// recovery fast without burning the rate limit.
    /// </summary>
    private static readonly TimeSpan MissingCloseRetry = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long a recorded-but-broker-invisible entry is assumed to still be
    /// working before the reconciler declares it a genuine non-fill. A
    /// premarket marketable-limit can rest for minutes; clearing too early
    /// would let the next tick re-enter and double the position when the
    /// resting order finally fills.
    /// </summary>
    private static readonly TimeSpan UnfilledEntryGrace = TimeSpan.FromSeconds(90);
    private readonly System.Collections.Concurrent.ConcurrentQueue<Candle> streamedBars = new();
    private AlpacaStreamingClient? streaming;

    /// <summary>
    /// Last-printed active-strategy roster fingerprint. Null until the first
    /// tick prints the startup roster; thereafter a mismatch reprints it.
    /// </summary>
    private string? lastActiveFingerprint;

    /// <summary>
    /// Print a loud, framed ENTRY/EXIT block to the console on every fill (in
    /// addition to the single-line logger). On by default; set
    /// IDIOTPROOF_PRINT_FILLS=0 to silence and rely on the logger alone.
    /// </summary>
    private static readonly bool PrintFillsEnabled =
        Environment.GetEnvironmentVariable("IDIOTPROOF_PRINT_FILLS") != "0";

    /// <summary>
    /// Self-ping cadence — the Monitor prints an "ONLINE" liveness line this
    /// often so an operator can see at a glance it is still evaluating (the
    /// tick running at all proves the loop, DB, and feed are healthy). Default
    /// 30 min; set IDIOTPROOF_SELFPING=0 to disable, or e.g. "10m"/"5m" to change.
    /// </summary>
    private static readonly bool SelfPingEnabled =
        Environment.GetEnvironmentVariable("IDIOTPROOF_SELFPING") != "0";
    private static readonly TimeSpan SelfPingInterval = ParseSelfPingInterval();
    private DateTime lastPingUtc = DateTime.MinValue;
    private DateTime lastTickSuccessUtc = DateTime.MinValue;

    private static readonly string BuildDateLabel = ComputeBuildDateLabel();
    private static string ComputeBuildDateLabel()
    {
        try
        {
            // Assembly.Location is empty in single-file publishes; fall back to the process exe.
            var loc = System.Reflection.Assembly.GetExecutingAssembly().Location;
            if (string.IsNullOrEmpty(loc))
                loc = System.Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "";
            var utc = System.IO.File.GetLastWriteTimeUtc(loc);
            var et = TimeZoneInfo.ConvertTimeFromUtc(utc, MarketTime.Eastern);
            return et.ToString("yyyy-MM-dd h:mm tt") + " ET";
        }
        catch { return "unknown"; }
    }

    private static TimeSpan ParseSelfPingInterval()
    {
        var v = Environment.GetEnvironmentVariable("IDIOTPROOF_SELFPING");
        if (string.IsNullOrWhiteSpace(v) || v == "0") return TimeSpan.FromMinutes(5);
        return TryParseInterval(v) ?? TimeSpan.FromMinutes(5);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("IdiotProof.Monitor starting — interval {Interval}s, feed {Feed}",
            EvaluationInterval.TotalSeconds, feed.FeedName);

        // Exactly one Monitor instance may evaluate/trade against a database
        // at a time (double-fire protection). Blocks here until this instance
        // is the leader; the lease auto-releases if the process dies.
        await using var lease = await MonitorLeaderLease.AcquireAsync(database.ConnectionString, logger, stoppingToken);

        monitorStartUtc = DateTime.UtcNow;
        try
        {
            await auditLogRepo.LogAsync("monitor-start",
                $"IdiotProof.Monitor started — feed {feed.FeedName}, interval {EvaluationInterval.TotalSeconds:F0}s, build {BuildDateLabel}",
                ct: stoppingToken);
        }
        catch (Exception ex) { logger.LogWarning(ex, "Failed to write monitor-start audit log."); }

        StartStreamingIfConfigured();

        await SupervisedLoop.RunAsync(new SupervisedLoopOptions
        {
            Tick          = TickAsync,
            Interval      = EvaluationInterval,
            MinBackoff    = TimeSpan.FromSeconds(5),
            MaxBackoff    = TimeSpan.FromMinutes(2),
            HeartbeatPath = Path.Combine(storage.LogsPath, "monitor.heartbeat"),
            OnTickFailed  = (ex, n) => logger.LogError(ex, "Monitor tick failed ({Count} consecutive) — backing off.", n),
        }, stoppingToken);

        if (streaming is not null) await streaming.DisposeAsync();
        logger.LogInformation("IdiotProof.Monitor stopped.");

        try
        {
            var uptime = DateTime.UtcNow - monitorStartUtc;
            await auditLogRepo.LogAsync("monitor-stop",
                $"IdiotProof.Monitor stopped — uptime {FormatUptime(uptime)}",
                ct: CancellationToken.None);
        }
        catch { /* best-effort — process is stopping */ }
    }

    /// <summary>
    /// Streaming is on whenever Alpaca keys exist (disable with IDIOTPROOF_STREAMING=0).
    /// The stream feeds the candle cache and last-trade prices; evaluation still
    /// runs on the SupervisedLoop cadence, but sees data that is seconds old, not
    /// interval-old.
    /// </summary>
    private void StartStreamingIfConfigured()
    {
        var disabled = Environment.GetEnvironmentVariable("IDIOTPROOF_STREAMING") == "0";
        if (disabled || string.IsNullOrWhiteSpace(appSettings.AlpacaApiKeyId) || string.IsNullOrWhiteSpace(appSettings.AlpacaApiSecretKey))
        {
            logger.LogInformation("Alpaca streaming off ({Reason}).", disabled ? "IDIOTPROOF_STREAMING=0" : "no Alpaca keys");
            return;
        }

        // Default to the real-time SIP consolidated tape (Algo Trader Plus, IP-A29).
        // Set IDIOTPROOF_ALPACA_FEED=iex to fall back to the free partial feed.
        var tier = Environment.GetEnvironmentVariable("IDIOTPROOF_ALPACA_FEED") ?? "sip";
        streaming = new AlpacaStreamingClient(appSettings.AlpacaApiKeyId, appSettings.AlpacaApiSecretKey, tier);
        streaming.BarReceived += bar => streamedBars.Enqueue(bar);
        streaming.Start();
        logger.LogInformation("Alpaca websocket streaming started ({Tier}).", tier);
    }

    /// <summary>One full evaluation pass.</summary>
    private async Task TickAsync(CancellationToken ct)
    {
        // ── Trading-schedule gate ─────────────────────────────────────────────
        var window = TradingSchedule.Classify(DateTime.UtcNow);

        if (window != lastWindow)
        {
            await LogWindowTransitionAsync(lastWindow, window, ct);
            lastWindow = window;
        }

        if (window == TradingWindow.Hibernate)
        {
            // Emit a liveness ping every 5 minutes so the console shows the
            // process is alive; SupervisedLoop writes the heartbeat file every tick.
            if (DateTime.UtcNow - lastHibernatePing >= TradingSchedule.HibernateInterval)
            {
                lastHibernatePing = DateTime.UtcNow;
                var etNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, MarketTime.Eastern);
                Console.WriteLine($"  [HIBERNATE] {etNow:HH:mm} ET — next active 3:55 AM ET");
            }
            return;
        }

        // Active: evaluate every 1s (SupervisedLoop already ticks at 1s).
        // Self-ping uses the cached active list so it fires even between DB reads.
        if (SelfPingEnabled && DateTime.UtcNow - lastPingUtc >= SelfPingInterval)
        {
            lastPingUtc = DateTime.UtcNow;
            PrintSelfPing(cachedActive);
        }
        // ─────────────────────────────────────────────────────────────────────

        // Re-read the active set every eval: queue/toggle/dial-in changes made
        // in the UI land in SQL and apply here automatically — no restart.
        var active = await strategyRepo.GetActiveAsync(ct);
        cachedActive = active;
        lastTickSuccessUtc = DateTime.UtcNow;

        // Roster echo: print the active-strategy list on startup (first tick,
        // lastActiveFingerprint == null) and reprint it whenever the AUTHORED
        // set changes — an add/remove, an enable/disable, or an edit to a
        // strategy's title/symbol/script. Position bookkeeping (PositionQty,
        // FireCount, UpdatedUtc) is deliberately NOT in the fingerprint, so the
        // Monitor's own fills/exits don't spam the roster — those already have
        // their own loud per-fire log lines.
        var fingerprint = RosterFingerprint(active);
        if (fingerprint != lastActiveFingerprint)
        {
            PrintActiveRoster(active);
            lastActiveFingerprint = fingerprint;
        }

        // Daily audit-log pruning — keep 30 days / minimum 2000 rows.
        if (DateTime.UtcNow - lastPruneUtc >= TimeSpan.FromHours(24))
        {
            lastPruneUtc = DateTime.UtcNow;
            try
            {
                var deleted = await auditLogRepo.PruneAsync(ct: ct);
                if (deleted > 0)
                    await auditLogRepo.LogAsync("audit-prune",
                        $"Pruned {deleted} old audit rows (retention 30d, min 2000 kept)", ct: ct);
            }
            catch (Exception ex) { logger.LogWarning(ex, "Audit log prune failed (non-fatal)."); }
        }

        // Premarket blow-off/fade alert — Mon-Fri, 9:00-10:00 AM ET (half an
        // hour either side of the bell), re-scanning every 5 minutes so it
        // catches both a stock still building toward its peak AND one that's
        // already rolled over and is fading through the open. Per-symbol
        // dedup (don't re-alert the same name at the same-or-lower severity
        // twice) lives inside PremarketFadeScanner itself, keyed for the day;
        // the 5-minute cadence here is just "don't hit the movers API every
        // second" — an in-memory timestamp is enough (same idiom as
        // lastPruneUtc/lastPingUtc above; a restart mid-window just means a
        // handful of names might re-alert once, not a correctness problem).
        var fadeScanEtNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, MarketTime.Eastern);
        var isWeekday = fadeScanEtNow.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
        if (isWeekday
            && fadeScanEtNow.TimeOfDay >= new TimeSpan(9, 0, 0) && fadeScanEtNow.TimeOfDay < new TimeSpan(10, 0, 0)
            && DateTime.UtcNow - lastFadeScanUtc >= FadeScanInterval)
        {
            lastFadeScanUtc = DateTime.UtcNow;
            // All registered users, not just owners of currently-active
            // strategies — this is an alert-only scan unrelated to any
            // strategy, so a user with valid Alpaca keys but nothing active
            // should still get premarket alerts. RunScanAsync itself already
            // no-ops (0/0) for a user with no Alpaca data keys configured.
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var owners = await db.AuthUsers.Select(u => u.Id).ToListAsync(ct);
            foreach (var owner in owners)
            {
                try
                {
                    var summary = await premarketFadeScanner.RunScanAsync(owner, ct);
                    if (summary.Flagged > 0)
                        logger.LogInformation("PremarketFadeScanner: {Screened} screened, {Flagged} flagged ({Note}) for user {UserId}.",
                            summary.Screened, summary.Flagged, summary.Note, owner);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "PremarketFadeScanner failed for user {UserId} — continuing.", owner);
                }
            }
        }

        if (active.Count == 0) return;

        DrainStreamedBars();

        var symbols = active.Select(s => s.Symbol.ToUpperInvariant()).Distinct().ToList();
        if (streaming is not null)
        {
            try { await streaming.SetSymbolsAsync(symbols, ct); }
            catch (Exception ex) { logger.LogDebug(ex, "Stream re-subscribe failed; reconnect loop will retry."); }
        }

        // Evict cache entries for symbols with no active strategy — over weeks
        // of queue/remove cycles these otherwise accumulate forever (240 bars
        // per stale ticker) in a process designed never to restart.
        var activeSet = new HashSet<string>(symbols, StringComparer.OrdinalIgnoreCase);
        foreach (var stale in candleCache.Keys.Where(k => !activeSet.Contains(k)).ToList())
            candleCache.Remove(stale);
        foreach (var stale in previousCloseCache.Keys.Where(k => !activeSet.Contains(k)).ToList())
            previousCloseCache.Remove(stale);

        foreach (var group in active.GroupBy(s => s.Symbol, StringComparer.OrdinalIgnoreCase))
        {
            var symbol = group.Key.ToUpperInvariant();
            IReadOnlyList<Candle> candles;
            decimal? previousClose;
            try
            {
                candles = await GetCandlesAsync(symbol, ct);
                previousClose = await GetPreviousCloseAsync(symbol, ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Market data unavailable for {Symbol}; skipping this tick.", symbol);
                continue;
            }

            if (candles.Count == 0) continue;

            // One-gapper-per-symbol (mirrors the UI's CountActiveForSymbolAsync
            // guard, StrategyListPageBase.cs) enforced at FIRE time too: the UI
            // only blocks at activation, so two gapper rows that both end up
            // IsActive=true (a lost UI race, AutoGapperScanner arming next to a
            // manual duplicate, a direct SQL edit) previously evaluated fully
            // independently here — both could open a real position on the same
            // symbol in the same tick with zero cross-strategy awareness.
            var gapperAlreadyFiredThisTick = false;
            foreach (var stored in group)
            {
                try
                {
                    // ScriptJson-only strategies (canonical path) have an empty ScriptText, so
                    // the text-only check was a false negative: two canonical-JSON gappers for
                    // the same symbol could both fire the same tick and open duplicate positions.
                    //
                    // NOTE: StrategyJson.Serialize always emits "peakGivebackPercent": null for
                    // non-gapper strategies, so Contains("peakGivebackPercent") alone would flag
                    // every canonical strategy as a gapper.  We must exclude the null case.
                    var sj = stored.ScriptJson;
                    var isGapperViaJson = sj is not null
                        && sj.Contains("peakGivebackPercent", StringComparison.OrdinalIgnoreCase)
                        && !sj.Contains("\"peakGivebackPercent\": null", StringComparison.OrdinalIgnoreCase);
                    var isGapper = (stored.ScriptText?.Contains("PeakGiveback(", StringComparison.OrdinalIgnoreCase) == true)
                        || isGapperViaJson;
                    if (isGapper && stored.PositionQty == 0 && gapperAlreadyFiredThisTick)
                    {
                        logger.LogWarning("[{Title}] {Symbol} skipped — another gapper strategy for this symbol already fired this tick.",
                            stored.Title, stored.Symbol);
                        continue;
                    }

                    var wasFlat = stored.PositionQty == 0;
                    await EvaluateOneAsync(stored, candles, previousClose, ct);
                    if (isGapper && wasFlat && stored.PositionQty > 0)
                        gapperAlreadyFiredThisTick = true;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Evaluation failed for {Title} ({Id}); continuing with next strategy.", stored.Title, stored.Id);
                }
            }
        }
    }

    // ── Market data ─────────────────────────────────────────────────────

    private void DrainStreamedBars()
    {
        while (streamedBars.TryDequeue(out var bar))
        {
            if (!candleCache.TryGetValue(bar.Symbol, out var entry)) continue;
            var list = entry.Candles;
            // Replace an in-progress duplicate of the same minute, else append.
            var idx = list.FindLastIndex(c => c.StartUtc == bar.StartUtc);
            if (idx >= 0) list[idx] = bar;
            else list.Add(bar);
            if (list.Count > CandleWindow) list.RemoveRange(0, list.Count - CandleWindow);
        }
    }

    private async Task<IReadOnlyList<Candle>> GetDailyCandlesAsync(string symbol, int calendarDayLookback, CancellationToken ct)
    {
        var endUtc   = DateTime.UtcNow;
        var startUtc = endUtc.AddDays(-calendarDayLookback);
        var list = new List<Candle>();
        await foreach (var c in feed.GetHistoricalCandlesAsync(symbol, startUtc, endUtc, TimeSpan.FromDays(1), ct))
            list.Add(c);
        return list;
    }

    private async Task<IReadOnlyList<Candle>> GetCandlesAsync(string symbol, CancellationToken ct)
    {
        if (candleCache.TryGetValue(symbol, out var cached)
            && DateTime.UtcNow - cached.FetchedUtc < (cached.Candles.Count > 0 ? RestRefresh : EmptyWindowRefresh))
        {
            return AppendLiveTick(symbol, cached.Candles);
        }

        var endUtc = DateTime.UtcNow;
        var startUtc = endUtc - (CandleWindow * BarSize);
        var list = new List<Candle>();
        await foreach (var c in feed.GetHistoricalCandlesAsync(symbol, startUtc, endUtc, BarSize, ct))
            list.Add(c);

        candleCache[symbol] = (list, DateTime.UtcNow);
        return AppendLiveTick(symbol, list);
    }

    /// <summary>
    /// Appends a synthetic candle from the freshest streamed trade so exits
    /// react to the live price between minute bars. Never mutates the cache —
    /// the synthetic bar exists only for this evaluation.
    ///
    /// Volume is carried forward from the last real bar rather than zeroed:
    /// IndicatorSnapshotBuilder reads Volume off whichever candle is last in
    /// the list, and a zero there drove VolumeRatio to 0 the instant this
    /// synthetic tick became "current" — spuriously failing IsVolumeAbove
    /// (and every gapper's volume screen) right when live data should make
    /// evaluation MORE accurate, not less.
    /// </summary>
    private IReadOnlyList<Candle> AppendLiveTick(string symbol, List<Candle> candles)
    {
        var lastTrade = streaming?.GetLastTrade(symbol);
        if (lastTrade is null || candles.Count == 0) return candles;
        var lastBar = candles[^1];
        if (lastTrade.TimestampUtc <= lastBar.EndUtc) return candles;

        var merged = new List<Candle>(candles)
        {
            new()
            {
                Symbol = symbol,
                StartUtc = lastTrade.TimestampUtc,
                EndUtc = lastTrade.TimestampUtc,
                Open = lastTrade.Price, High = lastTrade.Price,
                Low = lastTrade.Price, Close = lastTrade.Price,
                Volume = lastBar.Volume,
                Note = "live-tick",
            }
        };
        return merged;
    }

    private async Task<decimal?> GetPreviousCloseAsync(string symbol, CancellationToken ct)
    {
        var todayEt = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, MarketTime.Eastern));
        // A real close is good for the whole ET day; a null is only trusted
        // for MissingCloseRetry so a transient 4AM blip can't disable gap
        // strategies all day (IP-A18) but an outage can't burn the rate
        // limit at tick cadence either.
        if (previousCloseCache.TryGetValue(symbol, out var hit) && hit.DayEt == todayEt
            && (hit.Close is not null || DateTime.UtcNow - hit.FetchedUtc < MissingCloseRetry))
            return hit.Close;

        var close = await feed.GetPreviousCloseAsync(symbol, DateTime.UtcNow, ct);
        previousCloseCache[symbol] = (todayEt, close, DateTime.UtcNow);
        if (close is null)
        {
            // Throttle: this retries every MissingCloseRetry (30s), but logging
            // every retry spams the console all day for a data-less ticker
            // (e.g. a not-yet-listed post-merger symbol). Warn at most once per
            // NoCloseWarnEvery per symbol. Wording is accurate: previous close
            // only gates GAP conditions — a strategy without them is unaffected.
            if (!lastNoCloseWarnUtc.TryGetValue(symbol, out var last) || DateTime.UtcNow - last > NoCloseWarnEvery)
            {
                lastNoCloseWarnUtc[symbol] = DateTime.UtcNow;
                logger.LogWarning("{Symbol}: no previous close from the feed (new/post-merger ticker or no daily bars). " +
                    "Any GAP condition fails closed; non-gap strategies are unaffected. Retrying every {Retry}s (this warning is throttled).",
                    symbol, MissingCloseRetry.TotalSeconds);
            }
        }
        return close;
    }

    private readonly Dictionary<string, DateTime> lastNoCloseWarnUtc = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan NoCloseWarnEvery = TimeSpan.FromMinutes(10);

    // ── Evaluation ──────────────────────────────────────────────────────

    private async Task EvaluateOneAsync(
        IdiotProof.Blazor.Data.Strategy stored,
        IReadOnlyList<Candle> candles,
        decimal? previousClose,
        CancellationToken ct)
    {
        // Canonical JSON first (IP-LAW-8); the tolerant text parse only for
        // legacy rows that predate the canon. A present-but-rejected canon
        // QUARANTINES the strategy — no fragment evaluation, visible reason.
        var loaded = StrategyLoader.Load(stored.ScriptJson, stored.ScriptText);
        if (loaded.CanonicalError is { } canonError)
        {
            // Quarantine with an OPEN POSITION is an emergency, not a skip:
            // the stop/giveback/sell-by brain lives in the definition we just
            // refused to evaluate, so the held shares have NO exit management
            // until the strategy is fixed or the position flattened manually.
            // Escalate loudly instead of the ordinary quiet quarantine note.
            if (stored.PositionQty > 0)
            {
                logger.LogError(
                    "Strategy {Title} ({Id}) is QUARANTINED while HOLDING {Qty} shares of {Symbol} — " +
                    "exit rules cannot run. Fix the strategy or flatten manually. Canon error: {Error}",
                    stored.Title, stored.Id, stored.PositionQty, stored.Symbol, canonError);
                await progressRepo.UpsertAsync(stored.Id, 0, 1,
                    $"(HOLDING {stored.PositionQty} shares but strategy invalid — exits NOT managed: {Truncate(canonError)})", ct);
                // Throttled — log at most once per QuarantineLogInterval so this doesn't flood the audit table.
                if (!lastQuarantineLogUtc.TryGetValue(stored.Id, out var lastQ) || DateTime.UtcNow - lastQ >= QuarantineLogInterval)
                {
                    lastQuarantineLogUtc[stored.Id] = DateTime.UtcNow;
                    await auditLogRepo.LogAsync("strategy-error",
                        $"[{stored.Title}] QUARANTINED while holding {stored.PositionQty} {stored.Symbol} — exits NOT managed: {Truncate(canonError)}",
                        userId: stored.OwnerUserId, ct: ct);
                }
                return;
            }
            logger.LogWarning("Strategy {Title} ({Id}) quarantined — canonical JSON rejected: {Error}",
                stored.Title, stored.Id, canonError);
            await progressRepo.UpsertAsync(stored.Id, 0, 1, $"(invalid strategy: {Truncate(canonError)})", ct);
            return;
        }
        var def = loaded.Definition;
        if (def is null)
        {
            if (stored.PositionQty > 0)
            {
                logger.LogError(
                    "Strategy {Title} ({Id}) is UNPARSEABLE while HOLDING {Qty} shares of {Symbol} — " +
                    "exit rules cannot run. Fix the script or flatten manually.",
                    stored.Title, stored.Id, stored.PositionQty, stored.Symbol);
                await progressRepo.UpsertAsync(stored.Id, 0, 1,
                    $"(HOLDING {stored.PositionQty} shares but script unparseable — exits NOT managed)", ct);
                return;
            }
            logger.LogWarning("Strategy {Title} ({Id}) failed to parse — skipping.", stored.Title, stored.Id);
            await progressRepo.UpsertAsync(stored.Id, 0, 1, "(unparseable script)", ct);
            return;
        }

        // Open position → manage the exit instead of hunting a new entry.
        if (stored.PositionQty > 0)
        {
            // Holding heartbeat: log current price + unrealized P&L every HoldingLogInterval.
            if (!lastHoldingLogUtc.TryGetValue(stored.Id, out var lastHold) || DateTime.UtcNow - lastHold >= HoldingLogInterval)
            {
                lastHoldingLogUtc[stored.Id] = DateTime.UtcNow;
                var currentPx = candles.Count > 0 ? candles[^1].Close : 0m;
                var entryPx   = stored.LastEntryPrice ?? 0m;
                var unrealized = entryPx > 0 ? (currentPx - entryPx) * stored.PositionQty : 0m;
                var held = stored.EntryFilledUtc.HasValue ? FormatUptime(DateTime.UtcNow - stored.EntryFilledUtc.Value) : "?";
                await auditLogRepo.LogAsync("holding",
                    $"[{stored.Title}] HOLDING {stored.PositionQty} {stored.Symbol} " +
                    $"entry {entryPx:F2} → now {currentPx:F2}, unrealized P&L {unrealized:+0.00;-0.00}, held {held}",
                    userId: stored.OwnerUserId, ct: ct);
            }

            IReadOnlyList<Candle>? dailyCandles = null;
            var exitDailyDays = Math.Max(def.RollingHighDays ?? 0, def.RollingLowDays ?? 0);
            if (exitDailyDays > 0)
            {
                try { dailyCandles = await GetDailyCandlesAsync(stored.Symbol, exitDailyDays * 2 + 10, ct); }
                catch (Exception ex) { logger.LogWarning(ex, "[{Title}] daily bar fetch failed — rolling exit skipped this tick.", stored.Title); }
            }
            await EvaluateExitAsync(stored, def, candles, dailyCandles, ct);
            return;
        }

        // Coarse session gate (Premarket / RTH / AfterHours / Extended).
        if (!IsInsideSession(def.Session, DateTime.UtcNow))
        {
            await progressRepo.UpsertAsync(stored.Id, 0, Math.Max(1, def.EntryConditions.Count),
                $"(outside {def.Session} session)", ct);
            return;
        }

        // One-shot-per-day guard: a strategy that already traded today re-arms
        // tomorrow unless it opted into Repeat().
        if (!def.ShouldRepeat && stored.EntryFilledUtc is { } filled && IsSameEasternDay(filled, DateTime.UtcNow))
        {
            await progressRepo.UpsertAsync(stored.Id, 0, Math.Max(1, def.EntryConditions.Count),
                "(done for today)", ct);
            return;
        }

        var emas = EmaPeriodCollector.Collect(def);
        var snapshot = IndicatorSnapshotBuilder.BuildWithEmas(stored.Symbol, candles, emas, previousClose);

        // Apply ConditionalBlock branch overrides (If/Then/ElseIf/Else authored in the UI).
        // Bug 4 fix: the Monitor previously evaluated def.EntryConditions directly and silently
        // ignored all ConditionalBlocks — branches only worked in the backtester.
        var resolved = StrategyBranchResolver.Resolve(def, snapshot);

        var conditions = resolved.EntryConditions;
        if (conditions.Count == 0)
        {
            // Setup-only strategy: nothing to wait for, but it still walks the
            // LLM + risk gates and places a real order like any other fire.
            await FireAsync(stored, resolved, snapshot, candles, ct);
            return;
        }

        // Evaluate ALL conditions independently so the live chart Gantt gets a
        // bool[] per condition. Derive passed/firstFailure from the result array
        // (semantically equivalent to the old short-circuit loop).
        var condBits = conditions.Select(c => c.Evaluate(snapshot)).ToArray();
        int firstFalseIdx = System.Array.IndexOf(condBits, false);
        int passed = firstFalseIdx >= 0 ? firstFalseIdx : condBits.Length;
        string? firstFailure = firstFalseIdx >= 0 ? conditions[firstFalseIdx].ToScript() : null;

        await progressRepo.UpsertAsync(stored.Id, passed, conditions.Count, firstFailure, ct, lastPrice: (decimal)snapshot.Price);

        // Write a live bar (throttled to once every 10 seconds per strategy).
        var now = DateTime.UtcNow;
        if (!lastBarWrite.TryGetValue(stored.Id, out var lastWrite) || (now - lastWrite).TotalSeconds >= 10)
        {
            lastBarWrite[stored.Id] = now;
            var etNow = TimeZoneInfo.ConvertTimeFromUtc(now, MarketTime.Eastern);
            try
            {
                await liveBarRepo.UpsertBarAsync(new IdiotProof.Blazor.Data.LiveBar
                {
                    StrategyId   = stored.Id,
                    DateEt       = etNow.ToString("yyyy-MM-dd"),
                    Et           = etNow.ToString("HH:mm"),
                    Min          = etNow.Hour * 60 + etNow.Minute,
                    Open         = snapshot.BarOpen ?? snapshot.Price,
                    High         = snapshot.BarHigh ?? snapshot.Price,
                    Low          = snapshot.BarLow  ?? snapshot.Price,
                    Close        = snapshot.Price,
                    Volume       = snapshot.Volume,
                    Vwap         = snapshot.Vwap ?? 0,
                    WindowHigh   = snapshot.WindowHigh ?? 0,
                    Volx         = snapshot.VolumeRatio,
                    InSession    = IsInsideSession(def.Session, now),
                    CondBitsJson = System.Text.Json.JsonSerializer.Serialize(condBits),
                    Fire         = passed == conditions.Count,
                    Exit         = false,
                    WrittenUtc   = now,
                }, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[{Title}] live bar write failed (evaluation unaffected).", stored.Title);
            }
        }

        if (passed == conditions.Count)
        {
            // Rolling-range entry gates (daily bars): evaluated only when all
            // intraday conditions are met, to avoid a daily bar fetch on every tick.
            var entryDailyDays = Math.Max(resolved.EntryRollingLowDays ?? 0, resolved.EntryRollingHighDays ?? 0);
            if (entryDailyDays > 0)
            {
                IReadOnlyList<Candle>? entryDaily = null;
                try { entryDaily = await GetDailyCandlesAsync(stored.Symbol, entryDailyDays * 2 + 10, ct); }
                catch (Exception ex) { logger.LogWarning(ex, "[{Title}] daily bar fetch failed — rolling entry gate skipped this tick.", stored.Title); }

                if (entryDaily is { Count: > 0 })
                {
                    var price = (double)snapshot.Price;

                    if (resolved.EntryRollingLowDays is { } erld && erld > 0)
                    {
                        var buffer = resolved.EntryRollingLowBuffer ?? 2.5;
                        var lookback = Math.Min(erld, entryDaily.Count);
                        var low = double.MaxValue;
                        for (var i = entryDaily.Count - lookback; i < entryDaily.Count; i++)
                            if ((double)entryDaily[i].Low < low) low = (double)entryDaily[i].Low;
                        if (low < double.MaxValue && price > low * (1 + buffer / 100.0))
                        {
                            await progressRepo.UpsertAsync(stored.Id, passed, conditions.Count + 1,
                                $"(waiting for price to reach {erld}-day rolling low {low:F2} ±{buffer:F1}%)", ct);
                            return;
                        }
                    }

                    if (resolved.EntryRollingHighDays is { } erhd && erhd > 0)
                    {
                        var buffer = resolved.EntryRollingHighBuffer ?? 2.5;
                        var lookback = Math.Min(erhd, entryDaily.Count);
                        var high = 0.0;
                        for (var i = entryDaily.Count - lookback; i < entryDaily.Count; i++)
                            if ((double)entryDaily[i].High > high) high = (double)entryDaily[i].High;
                        if (high > 0 && price < high * (1 - buffer / 100.0))
                        {
                            await progressRepo.UpsertAsync(stored.Id, passed, conditions.Count + 1,
                                $"(waiting for breakout to {erhd}-day rolling high {high:F2} ±{buffer:F1}%)", ct);
                            return;
                        }
                    }
                }
            }

            logger.LogDebug("[{Title}] {Symbol} ✓ ALL {Total} conditions met → candidate fire ({Direction} @ {Price:F2})",
                stored.Title, stored.Symbol, conditions.Count, resolved.Direction, snapshot.Price);
            await FireAsync(stored, resolved, snapshot, candles, ct);
        }
        else
        {
            logger.LogDebug("[{Title}] {Symbol} {Passed}/{Total} — waiting on: {Verb}",
                stored.Title, stored.Symbol, passed, conditions.Count, firstFailure ?? "(unknown)");
        }
    }

    // ── Entry: three gates then the order ───────────────────────────────

    private async Task FireAsync(
        IdiotProof.Blazor.Data.Strategy stored,
        StrategyDefinition def,
        Shared.IndicatorSnapshot snapshot,
        IReadOnlyList<Candle> candles,
        CancellationToken ct)
    {
        var entryPrice = (decimal)snapshot.Price;
        var isShort = def.Direction == TradeDirection.Short;

        // Stop sits on the side that makes it a stop: below entry for a long,
        // above entry for a short. Using the long-only formula for a short
        // strategy placed the "stop" below entry — RiskGuardian correctly
        // rejects that as a wrong-side stop, so every short candidate used to
        // be silently blocked before it ever reached the "signal-only" path.
        var stopPrice = def.StopLossPrice is { } sl
            ? (decimal)sl
            : def.StopLossPercent is { } slPct
                ? isShort
                    ? entryPrice * (1 + (decimal)slPct / 100m)
                    : entryPrice * (1 - (decimal)slPct / 100m)
                : entryPrice; // Guardian rejects stopless setups — surfaced in audit.

        var quantity = def.Quantity > 0
            ? def.Quantity
            : def.NotionalAmount is { } notional && entryPrice > 0m
                ? Math.Max(1, (int)Math.Floor(notional / entryPrice))
                : 1;

        var signal = new TradeSignal
        {
            Symbol            = stored.Symbol,
            Direction         = def.Direction,
            ConfidencePercent = 0m,
            SuggestedEntry    = entryPrice,
            SuggestedStop     = stopPrice,
            // Full scale-out ladder — TakeProfit(t1, t2, t3) sets TakeProfitPrice = t1
            // AND populates TakeProfitTargets; reading only TakeProfitPrice hides
            // T2/T3 from the LLM panel's risk:reward view (same defect class as
            // the DslStrategy fix in IP-A15).
            Targets           = def.TakeProfitTargets.Count > 0
                                 ? def.TakeProfitTargets.Select(t => (decimal)t.Price).ToList()
                                 : def.TakeProfitPrice.HasValue
                                     ? [(decimal)def.TakeProfitPrice.Value]
                                     : [],
            StrategyName      = stored.Title,
            Reason            = $"All {def.EntryConditions.Count} conditions met",
            GeneratedUtc      = snapshot.Timestamp,
            UserId            = stored.OwnerUserId.ToString(),
        };

        // Audit: all conditions passed — include indicator snapshot in DataJson for diagnostics.
        var signalFireData = System.Text.Json.JsonSerializer.Serialize(new
        {
            price      = Math.Round(snapshot.Price, 4),
            vwap       = snapshot.Vwap.HasValue     ? (double?)Math.Round(snapshot.Vwap.Value, 4)     : null,
            gapPct     = snapshot.GapPercent.HasValue ? (double?)Math.Round(snapshot.GapPercent.Value, 2) : null,
            prevClose  = snapshot.PreviousClose,
            volx       = Math.Round(snapshot.VolumeRatio, 2),
            atr        = snapshot.Atr.HasValue       ? (double?)Math.Round(snapshot.Atr.Value, 4)     : null,
            ema9       = snapshot.Ema9,
            ema21      = snapshot.Ema21,
            conditions = def.EntryConditions.Select(c => c.ToScript()).ToList(),
        }, new System.Text.Json.JsonSerializerOptions
            { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
        await auditLogRepo.LogAsync("signal-fire",
            $"[{stored.Title}] {stored.Symbol} all {def.EntryConditions.Count} conditions met — entering gate checks",
            userId: stored.OwnerUserId, dataJson: signalFireData, ct: ct);

        // Gate 2 — LLM voter panel (skipped only when voting is disabled/unkeyed).
        // IP-LAW-1 requires the quorum to APPROVE — anything short of an
        // Approve consensus blocks the fire. The old check only blocked on an
        // explicit Reject, so a dead panel (zero votes), unparseable votes
        // (all Abstain), or a split below threshold all failed OPEN.
        if (appSettings.LlmVotingEnabled && !string.IsNullOrWhiteSpace(appSettings.ClaudeApiKey))
        {
            var voteResult = await llmVoting.VoteOnSignalAsync(signal, candles, appSettings, ct);
            if (voteResult.Votes.Count == 0 || voteResult.Consensus != VoteDecision.Approve)
            {
                var why = voteResult.Votes.Count == 0
                    ? "LLM panel unavailable (0 votes) — failing closed"
                    : voteResult.Consensus == VoteDecision.Reject
                        ? $"vetoed by LLM panel ({voteResult.Votes.Count} voters, conf {voteResult.ConsensusConfidence:F0})"
                        : $"no approval quorum (consensus {voteResult.Consensus}, {voteResult.Votes.Count} voters)";
                logger.LogInformation("[{Title}] {Symbol} ✗ BLOCKED at LLM gate — {Why}", stored.Title, stored.Symbol, why);
                await auditLogRepo.LogAsync("signal-vetoed",
                    $"[{stored.Title}] {stored.Symbol} {why}",
                    userId: stored.OwnerUserId, dataJson: voteResult.ConsensusReasoning, ct: ct);
                return;
            }
        }

        // Gate 3 — RiskGuardian holds the final veto (IP-LAW-2).
        var guardian = await riskGuardianService.GetForUserAsync(stored.OwnerUserId, ct);
        if (guardian is null)
        {
            logger.LogError("[{Title}] RiskGuardian row missing for user {UserId} — fire blocked.", stored.Title, stored.OwnerUserId);
            await auditLogRepo.LogAsync("signal-blocked",
                $"[{stored.Title}] {stored.Symbol} blocked — no RiskGuardian row for user {stored.OwnerUserId}",
                userId: stored.OwnerUserId, ct: ct);
            return;
        }
        var setup = new TradeSetup
        {
            Symbol          = stored.Symbol,
            Direction       = def.Direction,
            EntryPrice      = entryPrice,
            EntryType       = OrderType.Limit,
            StopLoss        = stopPrice,
            TakeProfit      = signal.Targets.Count > 0
                ? signal.Targets[0]
                : isShort
                    ? entryPrice - (stopPrice - entryPrice)
                    : entryPrice + (entryPrice - stopPrice),
            Quantity        = quantity,
            ConfidenceScore = 0,
            Rationale       = signal.Reason,
        };
        var verdict = guardian.ValidateTrade(setup);
        if (!verdict.IsApproved)
        {
            var reasons = string.Join("; ", verdict.BlockReasons);
            logger.LogInformation("[{Title}] {Symbol} ✗ BLOCKED by RiskGuardian — {Reasons}", stored.Title, stored.Symbol, reasons);
            await auditLogRepo.LogAsync("signal-blocked",
                $"[{stored.Title}] {stored.Symbol} blocked by RiskGuardian: {reasons}",
                userId: stored.OwnerUserId, ct: ct);
            return;
        }

        // Shorts are signal-only until short position management ships — the
        // exit brain (peak/giveback math) is long-shaped today.
        if (isShort)
        {
            await strategyRepo.RecordFiredAsync(stored.Id, ct);
            // Stamp the same day-guard a real fill would (PositionQty stays 0
            // — no position exists). Without this, a qualifying short
            // candidate re-fires and re-audit-logs every tick indefinitely
            // since nothing else marks "already signaled today" for a
            // no-order path.
            await strategyRepo.RecordEntryFillAsync(stored.Id, 0, entryPrice, DateTime.UtcNow, ct);
            await auditLogRepo.LogAsync("signal",
                $"[{stored.Title}] {stored.Symbol} SHORT signal recorded (order placement for shorts not yet enabled)",
                userId: stored.OwnerUserId, ct: ct);
            return;
        }

        // The order. Premarket/after-hours must be limit + extended_hours
        // (Alpaca requirement); RTH entries go in as marketable limits too so
        // a thin book can't fill us far off the evaluated price.
        var extendedHours = IsExtendedHours(DateTime.UtcNow);
        var limitPrice = Math.Round(entryPrice * 1.002m, 2); // +0.2% marketable buffer

        // Per-user, per-strategy routing: the strategy's BrokerMode ("Paper"|"Live"|"Sandbox")
        // overrides the global paper flag so each strategy independently controls
        // whether it trades paper or real money.
        var broker = await brokerResolver.ResolveAsync(stored.OwnerUserId, stored.BrokerMode, ct);

        // Never place a REAL order on SYNTHETIC data. The market-data feed is a
        // single global instance (keyed on the host's global Alpaca settings);
        // if the host has no data keys it falls back to Mock. Order routing,
        // however, is per-user (IP-A9) — so a host missing global data keys but
        // with a user's own Alpaca keys would evaluate strategies against fake
        // prices and fire REAL orders on them. Mock data implies Sandbox-only
        // (the intended dev pairing); block any non-Sandbox ENTRY. Exits are
        // risk-reducing and are NOT gated here — a genuinely-held position must
        // always be flatten-able.
        if (string.Equals(feed.FeedName, "Mock", StringComparison.OrdinalIgnoreCase)
            && broker.BrokerType != BrokerType.Sandbox)
        {
            logger.LogError("[{Title}] {Symbol} ✗ ENTRY BLOCKED — market data is synthetic (Mock) but routing is {Broker}. " +
                "Refusing to place a real order on mock prices. Configure real market-data keys on the host.",
                stored.Title, stored.Symbol, broker.BrokerType);
            await auditLogRepo.LogAsync("signal-blocked",
                $"[{stored.Title}] {stored.Symbol} entry blocked: mock market data cannot drive a real ({broker.BrokerType}) order.",
                userId: stored.OwnerUserId, ct: ct);
            return;
        }

        OrderResult order;
        try
        {
            order = await broker.PlaceOrderAsync(new OrderRequest
            {
                Symbol        = stored.Symbol,
                Quantity      = quantity,
                Side          = OrderSide.Buy,
                Type          = OrderType.Limit,
                LimitPrice    = limitPrice,
                TimeInForce   = "DAY",
                ExtendedHours = extendedHours,
            }, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            // An exception here (timeout, connection reset) is ambiguous —
            // Alpaca may have already accepted the order server-side. Letting
            // it escape uncaught left RecordFiredAsync/RecordEntryFillAsync
            // unrun, so PositionQty stayed 0 and the still-true entry
            // conditions fired AGAIN next tick — a real duplicate-buy risk.
            // Reconcile against the broker's actual positions before
            // deciding whether it's safe to let this strategy retry.
            logger.LogError(ex, "[{Title}] {Symbol} entry order threw — reconciling against broker before retrying.",
                stored.Title, stored.Symbol);
            try
            {
                var positions = await broker.GetPositionsAsync(ct);
                var match = positions.FirstOrDefault(p =>
                    string.Equals(p.Symbol, stored.Symbol, StringComparison.OrdinalIgnoreCase) && p.Quantity > 0);
                if (match is not null)
                {
                    var fillUtc = DateTime.UtcNow;
                    var filledQty = (int)Math.Floor(match.Quantity);
                    await strategyRepo.RecordFiredAsync(stored.Id, ct);
                    await strategyRepo.RecordEntryFillAsync(stored.Id, filledQty, match.AveragePrice, fillUtc, ct);
                    stored.PositionQty    = filledQty;
                    stored.LastEntryPrice = match.AveragePrice;
                    stored.EntryFilledUtc = fillUtc;
                    await auditLogRepo.LogAsync("entry",
                        $"[{stored.Title}] BUY {stored.Symbol} entry order threw but broker shows a filled position " +
                        $"({match.Quantity} @ {match.AveragePrice:F2}, {broker.BrokerType}) — bookkeeping reconciled, not re-firing.",
                        userId: stored.OwnerUserId, ct: ct);
                    return;
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception reconcileEx)
            {
                logger.LogWarning(reconcileEx, "[{Title}] {Symbol} post-exception reconciliation also failed.", stored.Title, stored.Symbol);
            }
            await auditLogRepo.LogAsync("order-placement-exception",
                $"[{stored.Title}] {stored.Symbol} entry order threw ({ex.Message}) and broker shows no matching position — safe to retry next tick.",
                userId: stored.OwnerUserId, ct: ct);
            return;
        }

        if (!order.IsSuccess)
        {
            logger.LogWarning("[{Title}] {Symbol} entry order REJECTED by {Broker}: {Message}",
                stored.Title, stored.Symbol, broker.BrokerType, order.Message);
            await auditLogRepo.LogAsync("order-rejected",
                $"[{stored.Title}] {stored.Symbol} entry rejected by {broker.BrokerType}: {order.Message}",
                userId: stored.OwnerUserId, ct: ct);
            return;
        }

        var entryUtc = DateTime.UtcNow;
        await strategyRepo.RecordFiredAsync(stored.Id, ct);
        await strategyRepo.RecordEntryFillAsync(stored.Id, quantity, limitPrice, entryUtc, ct);
        // Mirror the SQL write onto the in-memory row: RecordEntryFillAsync
        // loads its own DbContext instance, so without this the CALLER's
        // `stored` (shared by reference with the tick's per-symbol group
        // loop) still shows PositionQty=0 — the exact gap that let a second
        // same-symbol strategy fire in the same tick undetected.
        stored.PositionQty    = quantity;
        stored.LastEntryPrice = limitPrice;
        stored.EntryFilledUtc = entryUtc;
        await auditLogRepo.LogAsync("entry",
            $"[{stored.Title}] BUY {quantity} {stored.Symbol} @ {limitPrice:F2} ({broker.BrokerType}, {(extendedHours ? "extended-hours" : "RTH")}, order {order.BrokerOrderId})",
            userId: stored.OwnerUserId, ct: ct);

        // Trade diary — open the entry (log-and-continue: a diary write must
        // NEVER affect the trade that already happened).
        try
        {
            await tradeDiary.OpenAsync(new IdiotProof.Blazor.Data.TradeDiaryEntry
            {
                StrategyId          = stored.Id,
                OwnerUserId         = stored.OwnerUserId,
                StrategyTitle       = stored.Title,
                Symbol              = stored.Symbol,
                Direction           = def.Direction.ToString(),
                Broker              = broker.BrokerType.ToString(),
                IsPaper             = broker.IsPaper,
                EntryUtc            = entryUtc,
                EntryPrice          = limitPrice,
                Quantity            = quantity,
                Notional            = def.NotionalAmount,
                EntryOrderId        = order.BrokerOrderId,
                StopLossPrice       = def.StopLossPrice is { } sp ? (decimal)sp : null,
                StopLossPercent     = def.StopLossPercent is { } sc ? (decimal)sc : null,
                TrailingStopPercent = def.TrailingStopPercent is { } ts ? (decimal)ts : null,
                TakeProfitPrice     = def.TakeProfitPrice is { } tp ? (decimal)tp : null,
                PeakGivebackPercent = def.PeakGivebackPercent is { } pg ? (decimal)pg : null,
                PeakGivebackArmEt   = def.PeakGivebackArmTime is { } arm ? arm.ToString(@"hh\:mm") : null,
                SellByEt            = def.ExitTime is { } sb ? sb.ToString(@"hh\:mm") : null,
            }, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[{Title}] trade-diary OPEN failed (trade unaffected).", stored.Title);
        }

        logger.LogInformation("[{Title}] ✓ BUY {Qty} {Symbol} @ {Price:F2} via {Broker} ({Mode}) — position now managed for exit.",
            stored.Title, quantity, stored.Symbol, limitPrice, broker.BrokerType, broker.IsPaper ? "paper" : "LIVE");
        PrintFill("ENTRY", stored.Title, stored.Symbol, "BUY", quantity, limitPrice,
            broker.BrokerType.ToString(), broker.IsPaper, order.BrokerOrderId, entryUtc,
            "position now managed for exit");
    }

    // ── Exit: the sell-off brain ────────────────────────────────────────

    private async Task EvaluateExitAsync(
        IdiotProof.Blazor.Data.Strategy stored,
        StrategyDefinition def,
        IReadOnlyList<Candle> candles,
        IReadOnlyList<Candle>? dailyCandles,
        CancellationToken ct)
    {
        double entry;
        DateTime filledUtc;

        if (stored.LastEntryPrice is null || stored.EntryFilledUtc is null)
        {
            // No entry bookkeeping — try to bootstrap the cost basis from the live
            // broker position before treating it as orphaned. This lets strategies
            // be created with PositionQty > 0 and no entry price (e.g. silver exits
            // for positions already held in Alpaca) and have the Monitor resolve the
            // real cost basis on the first evaluation tick.
            try
            {
                var bootstrapBroker = await brokerResolver.ResolveAsync(stored.OwnerUserId, stored.BrokerMode, ct);
                var brokerPositions = await bootstrapBroker.GetPositionsAsync(ct);
                var brokerMatch = brokerPositions.FirstOrDefault(p =>
                    string.Equals(p.Symbol, stored.Symbol, StringComparison.OrdinalIgnoreCase) && p.Quantity > 0);
                if (brokerMatch is not null)
                {
                    logger.LogInformation("[{Title}] bootstrapped entry price {Price:F2} from live broker position.", stored.Title, brokerMatch.AveragePrice);
                    await strategyRepo.SetEntryBookkeepingAsync(stored.Id, brokerMatch.AveragePrice, DateTime.UtcNow, ct);
                    entry     = (double)brokerMatch.AveragePrice;
                    filledUtc = DateTime.UtcNow;
                }
                else
                {
                    logger.LogWarning("[{Title}] position has no entry bookkeeping and no broker match — clearing.", stored.Title);
                    await strategyRepo.RecordExitFillAsync(stored.Id, 0m, "Orphaned", DateTime.UtcNow, ct);
                    return;
                }
            }
            catch (Exception bootstrapEx)
            {
                logger.LogWarning(bootstrapEx, "[{Title}] could not bootstrap entry from broker — clearing orphaned position.", stored.Title);
                await strategyRepo.RecordExitFillAsync(stored.Id, 0m, "Orphaned", DateTime.UtcNow, ct);
                return;
            }
        }
        else
        {
            entry     = (double)stored.LastEntryPrice;
            filledUtc = stored.EntryFilledUtc.Value;
        }

        // Compute the original position size so GapperExitEvaluator can calculate
        // per-rung quantities for multi-target scale-out ladders (Bug 3 fix).
        // Uses the same formula as FireAsync: configured shares → else notional ÷ price.
        var initialQty = def.Quantity > 0
            ? def.Quantity
            : def.NotionalAmount is { } notional && stored.LastEntryPrice is { } ep && ep > 0m
                ? Math.Max(1, (int)Math.Floor(notional / ep))
                : 0; // unknown — evaluator falls back to full-exit behavior

        // Short exits are mirrored around entry — evaluating a short with the
        // long formula checks stop/target on the wrong side, so EvaluateShort
        // inverts the semantics. Both paths receive dailyCandles so rolling
        // N-day high/low exits work for both directions.
        var decision = def.Direction == TradeDirection.Short
            ? GapperExitEvaluator.EvaluateShort(def, entry, filledUtc, candles, DateTime.UtcNow, dailyCandles, initialQty, stored.PositionQty)
            : GapperExitEvaluator.Evaluate(def, entry, filledUtc, candles, DateTime.UtcNow, dailyCandles, initialQty, stored.PositionQty);

        // Surface "holding" in the progress badge so the UI shows live state.
        var current = (double)candles[^1].Close;
        await progressRepo.UpsertAsync(stored.Id, 1, 1,
            decision is null ? $"(holding {stored.PositionQty} @ {entry:F2}, now {current:F2})" : null, ct,
            lastPrice: (decimal)current);

        // Write live bar during hold phase so the chart keeps updating.
        var holdNow = DateTime.UtcNow;
        if (!lastBarWrite.TryGetValue(stored.Id, out var holdLastWrite) || (holdNow - holdLastWrite).TotalSeconds >= 10)
        {
            lastBarWrite[stored.Id] = holdNow;
            var etHold = TimeZoneInfo.ConvertTimeFromUtc(holdNow, MarketTime.Eastern);
            var lastCandle = candles[^1];
            try
            {
                await liveBarRepo.UpsertBarAsync(new IdiotProof.Blazor.Data.LiveBar
                {
                    StrategyId   = stored.Id,
                    DateEt       = etHold.ToString("yyyy-MM-dd"),
                    Et           = etHold.ToString("HH:mm"),
                    Min          = etHold.Hour * 60 + etHold.Minute,
                    Open         = (double)lastCandle.Open,
                    High         = (double)lastCandle.High,
                    Low          = (double)lastCandle.Low,
                    Close        = (double)lastCandle.Close,
                    Volume       = (long)lastCandle.Volume,
                    Vwap         = 0,
                    WindowHigh   = 0,
                    Volx         = 0,
                    InSession    = IsInsideSession(def.Session, holdNow),
                    CondBitsJson = "[]",
                    Fire         = false,
                    Exit         = decision is not null,
                    WrittenUtc   = holdNow,
                }, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[{Title}] live bar write (hold phase) failed (evaluation unaffected).", stored.Title);
            }
        }

        if (decision is null) return;

        // Never place the flatten while the market can't take it: a SellBy
        // decision trips every tick all weekend AND overnight (nowEt >= sellBy
        // at 22:00 on a Friday too), which would spam rejected orders — or
        // worse, queue a stale-priced regular-hours DAY sell for the next
        // open. Deferral requires BOTH a weekday and the 4:00–20:00 ET window
        // Alpaca accepts orders in; the decision re-fires next tradable tick.
        var deferNowEt = MarketTime.ToEasternTimeOfDay(DateTime.UtcNow);
        var marketTakingOrders = MarketTime.IsEquityTradingDay(DateTime.UtcNow)
            && deferNowEt >= new TimeSpan(4, 0, 0) && deferNowEt < new TimeSpan(20, 0, 0);
        if (!marketTakingOrders)
        {
            await progressRepo.UpsertAsync(stored.Id, 1, 1,
                $"(holding {stored.PositionQty} @ {entry:F2} — market closed, exit {decision.Reason} deferred)", ct);
            return;
        }

        var extendedHours = IsExtendedHours(DateTime.UtcNow);
        // Marketable sell limit: -0.5% so the flatten fills through a thin book.
        var limitPrice = Math.Round((decimal)decision.CurrentPrice * 0.995m, 2);

        // Exit through the same per-user, per-strategy broker that holds the position.
        var broker = await brokerResolver.ResolveAsync(stored.OwnerUserId, stored.BrokerMode, ct);

        // Reconcile with the broker BEFORE selling. Entry bookkeeping records
        // the fill optimistically when the order is ACCEPTED, so an unfilled
        // entry (price ran past the limit, halt, expiry at the close) leaves a
        // PHANTOM position — and selling shares we don't hold is rejected at
        // best, or opens a naked short on a margin account at worst. An empty
        // result from a SUCCESSFUL positions call means genuinely flat
        // (GetPositionsAsync now throws on failure rather than returning []).
        // On reconciliation failure we proceed with the recorded quantity —
        // never skip a risk-reducing exit because a status call hiccuped.
        // For partial scale-out: limit sell to the rung's quantity; null = full exit.
        var sellQty = decision.QuantityToSell.HasValue
            ? Math.Min(decision.QuantityToSell.Value, stored.PositionQty)
            : stored.PositionQty;

        // Multi-strategy-per-ticker (IP-A24): the broker reports ONE position
        // per symbol, but several of this user's strategies may hold the same
        // symbol at once (running competing setups to compare). When they do,
        // the aggregate can't be attributed to one strategy — so skip the
        // broker reconciliation and trust this strategy's own bookkeeping
        // (each strategy's diary P&L is computed from its own recorded fills,
        // so the comparison stays valid). Sole-holder (today's case) → reconcile
        // exactly as before.
        var sharedSymbol = await strategyRepo.CountHoldingForSymbolAsync(stored.OwnerUserId, stored.Symbol, ct) > 1;
        if (!sharedSymbol)
        try
        {
            var brokerPositions = await broker.GetPositionsAsync(ct);
            var held = brokerPositions.FirstOrDefault(p =>
                p.Symbol.Equals(stored.Symbol, StringComparison.OrdinalIgnoreCase));
            var heldQty = (int)Math.Floor(held?.Quantity ?? 0m);
            if (heldQty <= 0)
            {
                // No broker position. This is EITHER a still-working entry order
                // (a premarket marketable-limit can rest for minutes before
                // filling) OR a genuine non-fill. Distinguish by age: within the
                // grace window, assume the order is still working — do NOTHING
                // (don't sell, don't clear); clearing here would let the next
                // tick re-enter and double up when the resting order finally
                // fills. Past the grace window, treat it as a real non-fill and
                // FULLY clear (incl. EntryFilledUtc) so the strategy re-arms.
                var heldFor = DateTime.UtcNow - filledUtc;
                if (heldFor < UnfilledEntryGrace)
                {
                    await progressRepo.UpsertAsync(stored.Id, 1, 1,
                        $"(entry order working — no fill yet after {heldFor.TotalSeconds:F0}s)", ct);
                    return;
                }
                logger.LogWarning("[{Title}] {Symbol} exit ({Reason}) found NO broker position after {Secs:F0}s — " +
                    "the entry order never filled. Clearing bookkeeping so it can re-arm.",
                    stored.Title, stored.Symbol, decision.Reason, heldFor.TotalSeconds);
                await strategyRepo.ClearUnfilledEntryAsync(stored.Id, ct);
                try { await tradeDiary.MarkNotFilledAsync(stored.Id, DateTime.UtcNow, ct); }
                catch (Exception dex) { logger.LogError(dex, "[{Title}] trade-diary NotFilled mark failed.", stored.Title); }
                await auditLogRepo.LogAsync("position-reconciled",
                    $"[{stored.Title}] {stored.Symbol} entry never filled at {broker.BrokerType} — " +
                    "bookkeeping cleared, no order placed, strategy re-armed.",
                    userId: stored.OwnerUserId, ct: ct);
                return;
            }
            if (heldQty < sellQty)
            {
                logger.LogWarning("[{Title}] {Symbol} broker holds {Held} of {Recorded} recorded shares " +
                    "(partial fill) — selling the broker quantity.",
                    stored.Title, stored.Symbol, heldQty, sellQty);
                sellQty = heldQty;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[{Title}] position reconciliation failed — proceeding with the recorded quantity.",
                stored.Title);
        }

        var order = await broker.PlaceOrderAsync(new OrderRequest
        {
            Symbol         = stored.Symbol,
            Quantity       = sellQty,
            Side           = OrderSide.Sell,
            Type           = OrderType.Limit,
            LimitPrice     = limitPrice,
            TimeInForce    = "DAY",
            ExtendedHours  = extendedHours,
            // "close" is not a valid Alpaca position_intent value (only
            // buy_to_open/buy_to_close/sell_to_open/sell_to_close exist) —
            // this exit always sells to close a long, so sell_to_close is
            // the correct constant. A bare "close" would likely be rejected
            // or ignored by Alpaca, defeating the point of setting it at all
            // (stopping a naked short from opening on a zero-held-qty exit).
            PositionIntent = "sell_to_close",
        }, ct);

        if (!order.IsSuccess)
        {
            logger.LogWarning("[{Title}] {Symbol} EXIT order rejected by {Broker}: {Message} — will retry next tick.",
                stored.Title, stored.Symbol, broker.BrokerType, order.Message);
            await auditLogRepo.LogAsync("order-rejected",
                $"[{stored.Title}] {stored.Symbol} exit ({decision.Reason}) rejected by {broker.BrokerType}: {order.Message}",
                userId: stored.OwnerUserId, ct: ct);
            return;
        }

        // Feed realized P&L into the daily circuit breaker (IP-LAW-2) — the
        // audit found RecordTradePnL was never called in production, so the
        // daily-loss guard could never trip. Uses the reconciled quantity.
        var realized = (limitPrice - (decimal)entry) * sellQty;
        var guardian = await riskGuardianService.GetForUserAsync(stored.OwnerUserId, ct);
        guardian?.RecordTradePnL(realized);

        var exitUtc = DateTime.UtcNow;
        var remainingQty = stored.PositionQty - sellQty;
        var isPartialExit = decision.QuantityToSell.HasValue && remainingQty > 0;

        if (isPartialExit)
        {
            // Partial scale-out: reduce PositionQty without closing the position.
            // EntryFilledUtc is preserved so subsequent ticks continue exit management.
            await strategyRepo.RecordPartialExitAsync(stored.Id, sellQty, ct);
            await auditLogRepo.LogAsync("exit-partial",
                $"[{stored.Title}] SELL {sellQty} {stored.Symbol} @ {limitPrice:F2} — {decision.Reason} (partial, {remainingQty} shares remain): {decision.Detail} " +
                $"(P&L {realized:+0.00;-0.00}, {broker.BrokerType}, order {order.BrokerOrderId})",
                userId: stored.OwnerUserId, ct: ct);
        }
        else
        {
            await strategyRepo.RecordExitFillAsync(stored.Id, limitPrice, decision.Reason.ToString(), exitUtc, ct);
            var exitCategory = decision.Reason switch
            {
                GapperExitReason.StopLoss     => "exit-sl",
                GapperExitReason.TrailingStop => "exit-tsl",
                _                             => "exit",
            };
            await auditLogRepo.LogAsync(exitCategory,
                $"[{stored.Title}] SELL {sellQty} {stored.Symbol} @ {limitPrice:F2} — {decision.Reason}: {decision.Detail} " +
                $"(P&L {realized:+0.00;-0.00}, {broker.BrokerType}, order {order.BrokerOrderId})",
                userId: stored.OwnerUserId, ct: ct);

            // Trade diary — close the entry (log-and-continue: the sell already
            // happened; a diary write must never throw into the trade path).
            try
            {
                await tradeDiary.CloseAsync(stored.Id, limitPrice, decision.Reason.ToString(),
                    order.BrokerOrderId, realized, sellQty, exitUtc, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[{Title}] trade-diary CLOSE failed (trade unaffected).", stored.Title);
            }
        }

        logger.LogInformation("[{Title}] ✓ SOLD {Qty} {Symbol} @ {Price:F2} — {Reason} ({Mode}, P&L {Pnl:+0.00;-0.00})",
            stored.Title, sellQty, stored.Symbol, limitPrice, decision.Reason,
            isPartialExit ? $"{remainingQty} remaining" : "full exit", realized);
        var pnlText = realized >= 0 ? $"+${realized:0.00}" : $"-${Math.Abs(realized):0.00}";
        PrintFill("EXIT", stored.Title, stored.Symbol, "SELL", sellQty, limitPrice,
            broker.BrokerType.ToString(), broker.IsPaper, order.BrokerOrderId, exitUtc,
            isPartialExit ? $"{decision.Reason} (partial, {remainingQty} left)  -  P&L {pnlText}"
                          : $"{decision.Reason}  -  P&L {pnlText}");
    }

    // ── Active-strategy roster echo ─────────────────────────────────────

    /// <summary>
    /// Fingerprint of the AUTHORED active set — the Id, title, symbol, active
    /// flag, and canonical script of every active strategy. Changes exactly
    /// when a strategy is added, removed, enabled, disabled, or edited; blind
    /// to live position bookkeeping so a fill/exit never reprints the roster.
    /// </summary>
    private static string RosterFingerprint(IReadOnlyList<IdiotProof.Blazor.Data.Strategy> active) =>
        string.Join("\n", active
            .OrderBy(s => s.Id)
            .Select(s => $"{s.Id}|{s.Title}|{s.Symbol}|{s.IsActive}|{s.ScriptJson ?? s.ScriptText}"));

    /// <summary>
    /// Prints the active-strategy roster as a compact table. Written straight
    /// to the console (like the startup wordmark) rather than through the
    /// single-line logger so the table isn't shredded into timestamped lines.
    /// </summary>
    private void PrintActiveRoster(IReadOnlyList<IdiotProof.Blazor.Data.Strategy> active)
    {
        // ASCII-only framing: the Monitor console runs under the OEM codepage
        // (like the startup wordmark), where box-drawing glyphs render as '?'.
        var ts = Ts();
        var bar = new string('-', 97);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"{ts}{bar}");
        if (active.Count == 0)
        {
            sb.AppendLine($"{ts}  (none - the Monitor is idle until a strategy is activated)");
        }
        else
        {
            sb.AppendLine($"{ts}  {"Symbol",-7}  {"State",12}  {"P&L %",8}  {"SL",9}  {"TP",9}  Title");
            foreach (var s in active.OrderBy(x => x.Symbol, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase))
            {
                var loaded = StrategyLoader.Load(s.ScriptJson, s.ScriptText);
                string state = loaded.CanonicalError is not null ? "QUARANTINE"
                    : loaded.Definition is null ? "UNPARSED"
                    : s.PositionQty > 0
                        ? (loaded.Definition.Direction == TradeDirection.Short ? $"SHORT x{s.PositionQty}" : $"LONG  x{s.PositionQty}")
                    : "armed";

                string pnlPct = "-";
                if (s.PositionQty > 0 && s.LastEntryPrice is { } entryPrice && entryPrice != 0)
                {
                    var sym = s.Symbol.ToUpperInvariant();
                    var currentPrice = entryPrice;
                    var lastTrade = streaming?.GetLastTrade(sym);
                    if (lastTrade is not null)
                        currentPrice = lastTrade.Price;
                    else if (candleCache.TryGetValue(sym, out var cached) && cached.Candles.Count > 0)
                        currentPrice = cached.Candles[^1].Close;

                    var pct = (currentPrice - entryPrice) / entryPrice * 100m;
                    pnlPct = pct >= 0 ? $"+{pct:0.00}%" : $"{pct:0.00}%";
                }

                string slStr = "-";
                string tpStr = "-";
                if (loaded.Definition is { } def)
                {
                    var entryForCalc = s.LastEntryPrice;
                    bool isShort = def.Direction == TradeDirection.Short;

                    if (def.StopLossPrice.HasValue)
                        slStr = $"${def.StopLossPrice.Value:0.00}";
                    else if (def.StopLossPercent.HasValue && entryForCalc.HasValue && entryForCalc.Value != 0)
                        slStr = $"${(double)entryForCalc.Value * (isShort ? 1 + def.StopLossPercent.Value / 100 : 1 - def.StopLossPercent.Value / 100):0.00}";
                    else if (def.StopLossPercent.HasValue)
                        slStr = isShort ? $"+{def.StopLossPercent.Value:0.0}%" : $"-{def.StopLossPercent.Value:0.0}%";

                    var tpPrice = def.TakeProfitTargets?.Count > 0
                        ? (double?)def.TakeProfitTargets[0].Price
                        : def.TakeProfitPrice;
                    if (tpPrice.HasValue)
                        tpStr = $"${tpPrice.Value:0.00}";
                    else if (def.TakeProfitPercent.HasValue && entryForCalc.HasValue && entryForCalc.Value != 0)
                        tpStr = $"${(double)entryForCalc.Value * (isShort ? 1 - def.TakeProfitPercent.Value / 100 : 1 + def.TakeProfitPercent.Value / 100):0.00}";
                    else if (def.TakeProfitPercent.HasValue)
                        tpStr = isShort ? $"-{def.TakeProfitPercent.Value:0.0}%" : $"+{def.TakeProfitPercent.Value:0.0}%";
                }

                sb.AppendLine($"{ts}  {s.Symbol,-7}  {state,12}  {pnlPct,8}  {slStr,9}  {tpStr,9}  {RosterTrunc(s.Title, 38)}");
            }
        }
        sb.Append($"{ts}{bar}");
        Console.WriteLine(sb.ToString());
    }

    private static string RosterTrunc(string? s, int n) =>
        string.IsNullOrEmpty(s) ? "" : s.Length <= n ? s : s[..(n - 3)] + "...";

    // ── Fill + liveness console prints ──────────────────────────────────

    /// <summary>
    /// Loud, framed ENTRY/EXIT block on every fill — the operator-facing echo
    /// the single-line logger is too terse for. ASCII-only framing (the console
    /// runs under the OEM codepage, where box/arrow glyphs render as '?'), and
    /// all times ET (the market clock). Toggle with IDIOTPROOF_PRINT_FILLS=0.
    /// </summary>
    private void PrintFill(string kind, string title, string symbol, string side, int qty,
        decimal price, string broker, bool isPaper, string? orderId, DateTime whenUtc, string? extra)
    {
        if (!PrintFillsEnabled) return;
        var ts = Ts();
        var et = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(whenUtc, DateTimeKind.Utc), MarketTime.Eastern);
        var arrow = side == "BUY" ? ">>" : "<<";
        var bar = new string('-', 85);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"{ts}{bar}");
        sb.AppendLine($"{ts}  {arrow} {kind} FILLED   [{et.ToString(TimestampFormat)}]");
        sb.AppendLine($"{ts}  {symbol}  \"{RosterTrunc(title, 46)}\"");
        sb.AppendLine($"{ts}  {side} {qty} @ ${price:0.00}  -  {broker} {(isPaper ? "PAPER" : "LIVE")}" +
                      (string.IsNullOrEmpty(orderId) ? "" : $"  -  order {RosterTrunc(orderId, 8)}"));
        if (!string.IsNullOrEmpty(extra)) sb.AppendLine($"{ts}  {extra}");
        sb.Append($"{ts}{bar}");
        Console.WriteLine(sb.ToString());
    }

    /// <summary>
    /// Periodic "still online" liveness line (default every 30 min). Emitted
    /// from inside the tick, so it can only print when the loop is genuinely
    /// alive and the DB read for this tick already succeeded. ET clock.
    /// </summary>
    private void PrintSelfPing(IReadOnlyList<IdiotProof.Blazor.Data.Strategy> active)
    {
        var pingLabel = lastTickSuccessUtc == DateTime.MinValue ? "never"
            : TimeZoneInfo.ConvertTimeFromUtc(lastTickSuccessUtc, IdiotProof.Scripting.MarketTime.Eastern)
                .ToString("h:mm tt");
        var nowEt = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, IdiotProof.Scripting.MarketTime.Eastern).ToString("h:mm tt");
        var count = active.Count;
        Console.WriteLine($"{nowEt} ET  PING  {count} active strateg{(count == 1 ? "y" : "ies")} | last tick {pingLabel} ET | build {BuildDateLabel}");
    }

    private const string TimestampFormat = "yyyy-MM-dd hh:mm tt";

    private static string Ts() => string.Empty;

    // ── Clock helpers ───────────────────────────────────────────────────

    // Delegates to the shared ET session gate (MarketTime.IsInsideSession) so
    // the live evaluator and the offline replay harness can never diverge.
    private static bool IsInsideSession(TradingSession session, DateTime utc) =>
        MarketTime.IsInsideSession(session, utc);

    private static bool IsExtendedHours(DateTime utc)
    {
        var tod = MarketTime.ToEasternTimeOfDay(utc);
        return (tod >= new TimeSpan(4, 0, 0) && tod < new TimeSpan(9, 30, 0))
            || (tod >= new TimeSpan(16, 0, 0) && tod < new TimeSpan(20, 0, 0));
    }

    private static bool IsSameEasternDay(DateTime aUtc, DateTime bUtc)
    {
        var eastern = MarketTime.Eastern;
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(aUtc, DateTimeKind.Utc), eastern))
            == DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(bUtc, DateTimeKind.Utc), eastern));
    }

    private static string Truncate(string s) => s.Length <= 200 ? s : s[..200] + "…";

    private async Task LogWindowTransitionAsync(TradingWindow prev, TradingWindow next, CancellationToken ct)
    {
        if (next == TradingWindow.Hibernate)
        {
            Console.WriteLine();
            Console.WriteLine("  ── HIBERNATE ── Market closed. Pinging every 1 min until 3:55 AM ET. ──");
            Console.WriteLine();
            try
            {
                await auditLogRepo.LogAsync("monitor-hibernate",
                    "Monitor entering hibernate — market closed (next active 3:55 AM ET)", ct: ct);
            }
            catch { /* best-effort */ }
        }
        else if (prev == TradingWindow.Hibernate)
        {
            Console.WriteLine();
            Console.WriteLine("  ── ACTIVE ── Resuming from hibernate — 1s sub-minute evaluation. ──");
            Console.WriteLine();
            try
            {
                await auditLogRepo.LogAsync("monitor-resume",
                    "Monitor resuming from hibernate — active trading hours, 1s evaluation", ct: ct);
            }
            catch { /* best-effort */ }
        }
    }

    private static string FormatUptime(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes}m"
        : t.TotalMinutes >= 1 ? $"{(int)t.TotalMinutes}m {t.Seconds}s"
        : $"{t.Seconds}s";

    /// <summary>Lenient interval parser — accepts "30s", "5m", "120" (seconds), or null.</summary>
    private static TimeSpan? TryParseInterval(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        raw = raw.Trim().ToLowerInvariant();
        if (raw.EndsWith("ms") && double.TryParse(raw[..^2], out var ms)) return TimeSpan.FromMilliseconds(ms);
        if (raw.EndsWith('s')  && double.TryParse(raw[..^1], out var s))  return TimeSpan.FromSeconds(s);
        if (raw.EndsWith('m')  && double.TryParse(raw[..^1], out var m))  return TimeSpan.FromMinutes(m);
        if (raw.EndsWith('h')  && double.TryParse(raw[..^1], out var h))  return TimeSpan.FromHours(h);
        if (double.TryParse(raw, out var n)) return TimeSpan.FromSeconds(n);
        return null;
    }
}
