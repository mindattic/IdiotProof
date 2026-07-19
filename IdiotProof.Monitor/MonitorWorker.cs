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
    LlmVotingService llmVoting,
    RiskGuardianService riskGuardianService,
    AppSettings appSettings,
    IMarketDataFeed feed,
    UserBrokerResolver brokerResolver,
    MonitorDatabase database,
    IStorageProvider storage,
    ILogger<MonitorWorker> logger) : BackgroundService
{
    /// <summary>Interval between evaluation passes. Override via IDIOTPROOF_MONITOR_INTERVAL ("5s", "1m").</summary>
    private static readonly TimeSpan EvaluationInterval =
        TryParseInterval(Environment.GetEnvironmentVariable("IDIOTPROOF_MONITOR_INTERVAL"))
        ?? TimeSpan.FromSeconds(5);

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

    /// <summary>
    /// Re-check cadence when the previous close came back NULL. IP-A18 made
    /// nulls retry (a cached null disabled gap strategies all day), but an
    /// unbounded retry hammered the daily-bars endpoint every tick during an
    /// outage — the mirror of the empty-candle-window problem. 30 s keeps the
    /// recovery fast without burning the rate limit.
    /// </summary>
    private static readonly TimeSpan MissingCloseRetry = TimeSpan.FromSeconds(30);
    private readonly System.Collections.Concurrent.ConcurrentQueue<Candle> streamedBars = new();
    private AlpacaStreamingClient? streaming;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("IdiotProof.Monitor starting — interval {Interval}s, feed {Feed}",
            EvaluationInterval.TotalSeconds, feed.FeedName);

        // Exactly one Monitor instance may evaluate/trade against a database
        // at a time (double-fire protection). Blocks here until this instance
        // is the leader; the lease auto-releases if the process dies.
        await using var lease = await MonitorLeaderLease.AcquireAsync(database.ConnectionString, logger, stoppingToken);

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

        var tier = Environment.GetEnvironmentVariable("IDIOTPROOF_ALPACA_FEED") ?? "iex";
        streaming = new AlpacaStreamingClient(appSettings.AlpacaApiKeyId, appSettings.AlpacaApiSecretKey, tier);
        streaming.BarReceived += bar => streamedBars.Enqueue(bar);
        streaming.Start();
        logger.LogInformation("Alpaca websocket streaming started ({Tier}).", tier);
    }

    /// <summary>One full evaluation pass.</summary>
    private async Task TickAsync(CancellationToken ct)
    {
        // Re-read the active set every tick: queue/toggle/dial-in changes made
        // in the UI land in SQL and apply here automatically — no restart.
        var active = await strategyRepo.GetActiveAsync(ct);
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
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Market data unavailable for {Symbol}; skipping this tick.", symbol);
                continue;
            }

            if (candles.Count == 0) continue;

            foreach (var stored in group)
            {
                try
                {
                    await EvaluateOneAsync(stored, candles, previousClose, ct);
                }
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
            logger.LogWarning("{Symbol}: no previous close available — gap conditions fail closed; retrying in {Retry}s.",
                symbol, MissingCloseRetry.TotalSeconds);
        return close;
    }

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
            await EvaluateExitAsync(stored, def, candles, ct);
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

        var conditions = def.EntryConditions;
        if (conditions.Count == 0)
        {
            // Setup-only strategy: nothing to wait for, but it still walks the
            // LLM + risk gates and places a real order like any other fire.
            await FireAsync(stored, def, snapshot, candles, ct);
            return;
        }

        int passed = 0;
        string? firstFailure = null;
        foreach (var cond in conditions)
        {
            if (cond.Evaluate(snapshot)) passed++;
            else { firstFailure = cond.ToScript(); break; }
        }

        await progressRepo.UpsertAsync(stored.Id, passed, conditions.Count, firstFailure, ct);

        if (passed == conditions.Count)
        {
            logger.LogInformation("[{Title}] {Symbol} ✓ ALL {Total} conditions met → candidate fire ({Direction} @ {Price:F2})",
                stored.Title, stored.Symbol, conditions.Count, def.Direction, snapshot.Price);
            await FireAsync(stored, def, snapshot, candles, ct);
        }
        else
        {
            logger.LogInformation("[{Title}] {Symbol} {Passed}/{Total} — waiting on: {Verb}",
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

        // Per-user routing: the owner's own Alpaca account when configured,
        // else the global router (Sandbox default, IP-LAW-3).
        var broker = await brokerResolver.ResolveAsync(stored.OwnerUserId, ct);
        var order = await broker.PlaceOrderAsync(new OrderRequest
        {
            Symbol        = stored.Symbol,
            Quantity      = quantity,
            Side          = OrderSide.Buy,
            Type          = OrderType.Limit,
            LimitPrice    = limitPrice,
            TimeInForce   = "DAY",
            ExtendedHours = extendedHours,
        }, ct);

        if (!order.IsSuccess)
        {
            logger.LogWarning("[{Title}] {Symbol} entry order REJECTED by {Broker}: {Message}",
                stored.Title, stored.Symbol, broker.BrokerType, order.Message);
            await auditLogRepo.LogAsync("order-rejected",
                $"[{stored.Title}] {stored.Symbol} entry rejected by {broker.BrokerType}: {order.Message}",
                userId: stored.OwnerUserId, ct: ct);
            return;
        }

        await strategyRepo.RecordFiredAsync(stored.Id, ct);
        await strategyRepo.RecordEntryFillAsync(stored.Id, quantity, limitPrice, DateTime.UtcNow, ct);
        await auditLogRepo.LogAsync("order-placed",
            $"[{stored.Title}] BUY {quantity} {stored.Symbol} @ {limitPrice:F2} ({broker.BrokerType}, {(extendedHours ? "extended-hours" : "RTH")}, order {order.BrokerOrderId})",
            userId: stored.OwnerUserId, ct: ct);
        logger.LogInformation("[{Title}] ✓ BUY {Qty} {Symbol} @ {Price:F2} via {Broker} — position now managed for exit.",
            stored.Title, quantity, stored.Symbol, limitPrice, broker.BrokerType);
    }

    // ── Exit: the sell-off brain ────────────────────────────────────────

    private async Task EvaluateExitAsync(
        IdiotProof.Blazor.Data.Strategy stored,
        StrategyDefinition def,
        IReadOnlyList<Candle> candles,
        CancellationToken ct)
    {
        if (stored.LastEntryPrice is not { } entry || stored.EntryFilledUtc is not { } filledUtc)
        {
            logger.LogWarning("[{Title}] position without entry bookkeeping — clearing.", stored.Title);
            await strategyRepo.RecordExitFillAsync(stored.Id, 0m, "Orphaned", DateTime.UtcNow, ct);
            return;
        }

        var decision = GapperExitEvaluator.Evaluate(def, (double)entry, filledUtc, candles, DateTime.UtcNow);

        // Surface "holding" in the progress badge so the UI shows live state.
        var current = (double)candles[^1].Close;
        await progressRepo.UpsertAsync(stored.Id, 1, 1,
            decision is null ? $"(holding {stored.PositionQty} @ {entry:F2}, now {current:F2})" : null, ct);

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

        // Exit through the same per-user broker that holds the position.
        var broker = await brokerResolver.ResolveAsync(stored.OwnerUserId, ct);

        // Reconcile with the broker BEFORE selling. Entry bookkeeping records
        // the fill optimistically when the order is ACCEPTED, so an unfilled
        // entry (price ran past the limit, halt, expiry at the close) leaves a
        // PHANTOM position — and selling shares we don't hold is rejected at
        // best, or opens a naked short on a margin account at worst. An empty
        // result from a SUCCESSFUL positions call means genuinely flat
        // (GetPositionsAsync now throws on failure rather than returning []).
        // On reconciliation failure we proceed with the recorded quantity —
        // never skip a risk-reducing exit because a status call hiccuped.
        var sellQty = stored.PositionQty;
        try
        {
            var brokerPositions = await broker.GetPositionsAsync(ct);
            var held = brokerPositions.FirstOrDefault(p =>
                p.Symbol.Equals(stored.Symbol, StringComparison.OrdinalIgnoreCase));
            var heldQty = (int)Math.Floor(held?.Quantity ?? 0m);
            if (heldQty <= 0)
            {
                logger.LogWarning("[{Title}] {Symbol} exit ({Reason}) found NO broker position — " +
                    "the entry order never filled. Clearing phantom bookkeeping.",
                    stored.Title, stored.Symbol, decision.Reason);
                await strategyRepo.RecordExitFillAsync(stored.Id, 0m, "NotFilled", DateTime.UtcNow, ct);
                await auditLogRepo.LogAsync("position-reconciled",
                    $"[{stored.Title}] {stored.Symbol} exit ({decision.Reason}) found no position at {broker.BrokerType} — " +
                    "entry never filled; phantom bookkeeping cleared, no order placed.",
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
            Symbol        = stored.Symbol,
            Quantity      = sellQty,
            Side          = OrderSide.Sell,
            Type          = OrderType.Limit,
            LimitPrice    = limitPrice,
            TimeInForce   = "DAY",
            ExtendedHours = extendedHours,
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
        var realized = (limitPrice - entry) * sellQty;
        var guardian = await riskGuardianService.GetForUserAsync(stored.OwnerUserId, ct);
        guardian.RecordTradePnL(realized);

        await strategyRepo.RecordExitFillAsync(stored.Id, limitPrice, decision.Reason.ToString(), DateTime.UtcNow, ct);
        await auditLogRepo.LogAsync("order-placed",
            $"[{stored.Title}] SELL {sellQty} {stored.Symbol} @ {limitPrice:F2} — {decision.Reason}: {decision.Detail} " +
            $"(P&L {realized:+0.00;-0.00}, {broker.BrokerType}, order {order.BrokerOrderId})",
            userId: stored.OwnerUserId, ct: ct);
        logger.LogInformation("[{Title}] ✓ SOLD {Qty} {Symbol} @ {Price:F2} — {Reason} (P&L {Pnl:+0.00;-0.00})",
            stored.Title, sellQty, stored.Symbol, limitPrice, decision.Reason, realized);
    }

    // ── Clock helpers ───────────────────────────────────────────────────

    private static bool IsInsideSession(TradingSession session, DateTime utc)
    {
        // Weekend gate first — the time-of-day windows below would happily
        // pass on a Saturday, and conditions evaluated against Friday's stale
        // bars could fire an order that sits queued until Monday's open.
        if (!MarketTime.IsEquityTradingDay(utc)) return false;

        var tod = MarketTime.ToEasternTimeOfDay(utc);
        var premarket  = tod >= new TimeSpan(4, 0, 0) && tod < new TimeSpan(9, 30, 0);
        var rth        = tod >= new TimeSpan(9, 30, 0) && tod < new TimeSpan(16, 0, 0);
        var afterHours = tod >= new TimeSpan(16, 0, 0) && tod < new TimeSpan(20, 0, 0);
        return session switch
        {
            TradingSession.Premarket  => premarket,
            TradingSession.RTH        => rth,
            TradingSession.AfterHours => afterHours,
            TradingSession.Extended   => premarket || rth || afterHours,
            _                         => rth,
        };
    }

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
