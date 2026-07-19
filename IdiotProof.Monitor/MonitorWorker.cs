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

    private readonly Dictionary<string, (List<Candle> Candles, DateTime FetchedUtc)> candleCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (DateOnly DayEt, decimal? Close)> previousCloseCache = new(StringComparer.OrdinalIgnoreCase);
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
            && DateTime.UtcNow - cached.FetchedUtc < RestRefresh
            && cached.Candles.Count > 0)
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
    /// Appends a synthetic zero-volume candle from the freshest streamed trade
    /// so exits react to the live price between minute bars. Never mutates the
    /// cache — the synthetic bar exists only for this evaluation.
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
                Volume = 0,
                Note = "live-tick",
            }
        };
        return merged;
    }

    private async Task<decimal?> GetPreviousCloseAsync(string symbol, CancellationToken ct)
    {
        var todayEt = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, MarketTime.Eastern));
        if (previousCloseCache.TryGetValue(symbol, out var hit) && hit.DayEt == todayEt)
            return hit.Close;

        var close = await feed.GetPreviousCloseAsync(symbol, DateTime.UtcNow, ct);
        previousCloseCache[symbol] = (todayEt, close);
        if (close is null)
            logger.LogWarning("{Symbol}: no previous close available — gap conditions will fail closed.", symbol);
        return close;
    }

    // ── Evaluation ──────────────────────────────────────────────────────

    private async Task EvaluateOneAsync(
        IdiotProof.Blazor.Data.Strategy stored,
        IReadOnlyList<Candle> candles,
        decimal? previousClose,
        CancellationToken ct)
    {
        var def = ScriptParser.ParseScript(stored.ScriptText);
        if (def is null)
        {
            logger.LogWarning("Strategy {Title} ({Id}) failed to parse — skipping.", stored.Title, stored.Id);
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

        var emas = CollectEmaPeriods(def);
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
        var stopPrice = def.StopLossPrice is { } sl
            ? (decimal)sl
            : def.StopLossPercent is { } slPct
                ? entryPrice * (1 - (decimal)slPct / 100m)
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
            Targets           = def.TakeProfitPrice.HasValue ? [(decimal)def.TakeProfitPrice.Value] : [],
            StrategyName      = stored.Title,
            Reason            = $"All {def.EntryConditions.Count} conditions met",
            GeneratedUtc      = snapshot.Timestamp,
            UserId            = stored.OwnerUserId.ToString(),
        };

        // Gate 2 — LLM voter panel (skipped only when voting is disabled/unkeyed).
        if (appSettings.LlmVotingEnabled && !string.IsNullOrWhiteSpace(appSettings.ClaudeApiKey))
        {
            var voteResult = await llmVoting.VoteOnSignalAsync(signal, candles, appSettings, ct);
            if (voteResult.Votes.Count > 0 && voteResult.Consensus == VoteDecision.Reject)
            {
                logger.LogInformation("[{Title}] {Symbol} ✗ VETOED by LLM panel ({Voters} voters).",
                    stored.Title, stored.Symbol, voteResult.Votes.Count);
                await auditLogRepo.LogAsync("signal-vetoed",
                    $"[{stored.Title}] {stored.Symbol} vetoed by LLM panel ({voteResult.Votes.Count} voters, conf {voteResult.ConsensusConfidence:F0})",
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
        if (def.Direction != TradeDirection.Long)
        {
            await strategyRepo.RecordFiredAsync(stored.Id, ct);
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

        var extendedHours = IsExtendedHours(DateTime.UtcNow);
        // Marketable sell limit: -0.5% so the flatten fills through a thin book.
        var limitPrice = Math.Round((decimal)decision.CurrentPrice * 0.995m, 2);

        // Exit through the same per-user broker that holds the position.
        var broker = await brokerResolver.ResolveAsync(stored.OwnerUserId, ct);
        var order = await broker.PlaceOrderAsync(new OrderRequest
        {
            Symbol        = stored.Symbol,
            Quantity      = stored.PositionQty,
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
        // daily-loss guard could never trip.
        var realized = (limitPrice - entry) * stored.PositionQty;
        var guardian = await riskGuardianService.GetForUserAsync(stored.OwnerUserId, ct);
        guardian.RecordTradePnL(realized);

        await strategyRepo.RecordExitFillAsync(stored.Id, limitPrice, decision.Reason.ToString(), DateTime.UtcNow, ct);
        await auditLogRepo.LogAsync("order-placed",
            $"[{stored.Title}] SELL {stored.PositionQty} {stored.Symbol} @ {limitPrice:F2} — {decision.Reason}: {decision.Detail} " +
            $"(P&L {realized:+0.00;-0.00}, {broker.BrokerType}, order {order.BrokerOrderId})",
            userId: stored.OwnerUserId, ct: ct);
        logger.LogInformation("[{Title}] ✓ SOLD {Qty} {Symbol} @ {Price:F2} — {Reason} (P&L {Pnl:+0.00;-0.00})",
            stored.Title, stored.PositionQty, stored.Symbol, limitPrice, decision.Reason, realized);
    }

    // ── Clock helpers ───────────────────────────────────────────────────

    private static bool IsInsideSession(TradingSession session, DateTime utc)
    {
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

    /// <summary>
    /// Collect EMA periods referenced by any condition so the snapshot builder
    /// pre-computes them. Mirrors DslStrategy's logic.
    /// </summary>
    private static IEnumerable<int> CollectEmaPeriods(StrategyDefinition def)
    {
        var periods = new HashSet<int>();
        foreach (var c in def.EntryConditions)
            CollectFrom(c, periods);
        foreach (var block in def.ConditionalBlocks)
            foreach (var branch in block.Branches)
            {
                if (branch.Condition is not null) CollectFrom(branch.Condition, periods);
                foreach (var c in branch.Overrides.EntryConditions) CollectFrom(c, periods);
            }
        return periods;
    }

    private static void CollectFrom(ICondition c, HashSet<int> bucket)
    {
        if (c is IndicatorCondition ic)
        {
            if (ic.Type is IndicatorType.EmaAbove or IndicatorType.EmaBelow or IndicatorType.ReclaimEma
                && ic.Parameter is { } p)
                bucket.Add((int)p);
            else if (ic.Type is IndicatorType.BetweenEma or IndicatorType.EmaStack
                     && ic.Parameter is { } p1 && ic.Parameter2 is { } p2)
            {
                bucket.Add((int)p1);
                bucket.Add((int)p2);
            }
        }
        else if (c is AndCondition a) { CollectFrom(a.Left, bucket); CollectFrom(a.Right, bucket); }
        else if (c is OrCondition  o) { CollectFrom(o.Left, bucket); CollectFrom(o.Right, bucket); }
        else if (c is NotCondition n) { CollectFrom(n.Inner, bucket); }
    }

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
