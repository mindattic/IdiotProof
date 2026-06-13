using IdiotProof.Brokers;
using IdiotProof.DataFeeds;
using IdiotProof.Engine;
using IdiotProof.Engine.Settings;
using IdiotProof.Engine.Storage;
using IdiotProof.Engine.Workspace;
using IdiotProof.Blazor.Data;
using IdiotProof.Blazor.Hubs;
using IdiotProof.Models;
using IdiotProof.Shared.Risk;
using IdiotProof.Strategies;
using Microsoft.AspNetCore.SignalR;

namespace IdiotProof.Blazor.Services;

public sealed class StrategyExecutionService : BackgroundService
{
    private const int CandleLookbackMinutes = 90;
    private const int MaxCandlesPerEval = 60;

    private static readonly TimeOnly AutoTradeWindowOpen  = new(4,  0);
    private static readonly TimeOnly AutoTradeWindowClose = new(20, 0);

    private readonly IServiceScopeFactory scopeFactory;
    private readonly WorkspaceManager workspaceManager;
    private readonly StrategyRegistry strategyRegistry;
    private readonly TradingStateService tradingState;
    private readonly LlmVotingService llmVoting;
    private readonly RiskGuardianService riskGuardianService;
    private readonly AuditLogRepository auditLogRepo;
    private readonly AppSettings appSettings;
    private readonly AuditLogger auditLogger;
    private readonly IHubContext<TradingHub> hubContext;
    private readonly IStorageProvider storage;
    private readonly ILogger<StrategyExecutionService> logger;

    public StrategyExecutionService(
        IServiceScopeFactory scopeFactory,
        WorkspaceManager workspaceManager,
        StrategyRegistry strategyRegistry,
        TradingStateService tradingState,
        LlmVotingService llmVoting,
        RiskGuardianService riskGuardianService,
        AuditLogRepository auditLogRepo,
        AppSettings appSettings,
        AuditLogger auditLogger,
        IHubContext<TradingHub> hubContext,
        IStorageProvider storage,
        ILogger<StrategyExecutionService> logger)
    {
        this.scopeFactory        = scopeFactory;
        this.workspaceManager    = workspaceManager;
        this.strategyRegistry    = strategyRegistry;
        this.tradingState        = tradingState;
        this.llmVoting           = llmVoting;
        this.riskGuardianService = riskGuardianService;
        this.auditLogRepo        = auditLogRepo;
        this.appSettings         = appSettings;
        this.auditLogger         = auditLogger;
        this.hubContext          = hubContext;
        this.storage             = storage;
        this.logger              = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        tradingState.SetEngineRunning(true);
        logger.LogInformation("Strategy engine started. Interval: {Interval}s", appSettings.StrategyEvaluationIntervalSeconds);
        auditLogger.Log("ENGINE_START", $"Interval={appSettings.StrategyEvaluationIntervalSeconds}s");

        var positionCyclesSinceRefresh = 0;
        var interval = Math.Max(appSettings.StrategyEvaluationIntervalSeconds, 5);

        var options = new SupervisedLoopOptions
        {
            Tick = async ct =>
            {
                await RunEvaluationCycleAsync(ct);
                tradingState.RecordEvaluation();

                positionCyclesSinceRefresh++;
                if (positionCyclesSinceRefresh >= Math.Max(60 / interval, 1))
                {
                    positionCyclesSinceRefresh = 0;
                    await RefreshPositionsAsync(ct);
                }
            },
            Interval = TimeSpan.FromSeconds(interval),
            MinBackoff = TimeSpan.FromSeconds(Math.Max(interval, 5)),
            MaxBackoff = TimeSpan.FromMinutes(5),
            HeartbeatPath = Path.Combine(storage.LogsPath, "engine.heartbeat"),
            OnTickFailed = (ex, count) =>
            {
                logger.LogError(ex, "Evaluation cycle failed (consecutive failures: {Count})", count);
                auditLogger.Log("ENGINE_TICK_FAIL", $"Consecutive={count} Error={ex.Message}");
            }
        };

        await SupervisedLoop.RunAsync(options, stoppingToken);

        tradingState.SetEngineRunning(false);
        auditLogger.Log("ENGINE_STOP", "Strategy engine stopped cleanly");
        logger.LogInformation("Strategy engine stopped.");
    }

    private async Task RunEvaluationCycleAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var userKeyService = scope.ServiceProvider.GetRequiredService<UserKeyService>();
        var userList = await userKeyService.GetAllActiveUsersAsync(ct);

        var context = new StrategyContext
        {
            Timezone = GetTimezone(),
            EvaluationTimeUtc = DateTime.UtcNow
        };

        foreach (var (userId, keys) in userList)
        {
            if (ct.IsCancellationRequested) break;
            var feed = BuildFeed(keys);
            var tabs = workspaceManager.GetTabsForUser(userId.ToString());

            foreach (var tab in tabs)
            {
                if (ct.IsCancellationRequested) break;
                if (!tab.Strategies.Any(s => s.Enabled)) continue;

                foreach (var symbol in tab.Watchlist.ToList())
                {
                    if (ct.IsCancellationRequested) break;
                    await EvaluateSymbolAsync(userId, keys, tab, symbol, feed, context, ct);
                }
            }
        }
    }

    private async Task EvaluateSymbolAsync(
        Guid userId, UserApiKeys keys, WorkspaceTab tab,
        string symbol, SwitchableMarketDataFeed feed,
        StrategyContext context, CancellationToken ct)
    {
        var candles = new List<Candle>();
        try
        {
            var end = DateTime.UtcNow;
            var start = end.AddMinutes(-CandleLookbackMinutes);
            await foreach (var c in feed.GetHistoricalCandlesAsync(symbol, start, end, TimeSpan.FromMinutes(1), ct))
            {
                candles.Add(c);
                if (candles.Count >= MaxCandlesPerEval) break;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch candles for {Symbol} (user {UserId})", symbol, userId);
            return;
        }

        if (candles.Count > 0)
        {
            var latest = candles.MaxBy(c => c.StartUtc)!;
            tradingState.UpdatePrice(new LatestPrice(symbol, latest.Close, latest.StartUtc, feed.FeedName));
        }

        foreach (var binding in tab.Strategies.Where(s => s.Enabled).ToList())
        {
            if (ct.IsCancellationRequested) break;

            var strategy = strategyRegistry.Get(binding.StrategyName);
            if (strategy == null)
            {
                logger.LogWarning("Strategy not found: {Name}", binding.StrategyName);
                continue;
            }

            IReadOnlyList<TradeSignal> signals;
            try { signals = strategy.Evaluate(symbol, candles, context); }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Strategy {Name} threw for {Symbol}", binding.StrategyName, symbol);
                continue;
            }

            foreach (var signal in signals)
            {
                signal.UserId = userId.ToString();
                tradingState.AddSignal(signal);

                var signalKey = $"{signal.Symbol}_{signal.StrategyName}_{signal.GeneratedUtc:yyyyMMddHHmmss}";
                auditLogger.Log("SIGNAL", $"Dir={signal.Direction} Conf={signal.ConfidencePercent:F1}% Entry={signal.SuggestedEntry:F2} Strat={signal.StrategyName}", symbol);

                LlmVotingResult? voteResult = null;
                if (keys.LlmVotingEnabled && !string.IsNullOrWhiteSpace(ResolveClaudeKey(keys)))
                {
                    try
                    {
                        // Use user's Claude key if set, else fall back to the shared
                        // MindAttic LLM store loaded into appSettings.
                        var userSettings = CloneSettingsWithUserKeys(keys);
                        voteResult = await llmVoting.VoteOnSignalAsync(signal, candles, userSettings, ct);
                        if (voteResult.Votes.Count > 0)
                        {
                            tradingState.StoreVote(signalKey, voteResult);
                            auditLogger.Log("LLM_VOTE", $"Consensus={voteResult.Consensus} Confidence={voteResult.ConsensusConfidence:F1}%", symbol);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "LLM voting failed for {Key}", signalKey);
                    }
                }

                try
                {
                    await hubContext.Clients.Group(symbol).SendAsync("SignalReceived", new
                    {
                        symbol = signal.Symbol,
                        direction = signal.Direction.ToString(),
                        confidence = signal.ConfidencePercent,
                        strategy = signal.StrategyName,
                        entry = signal.SuggestedEntry,
                        stop = signal.SuggestedStop,
                        generatedUtc = signal.GeneratedUtc
                    }, ct);
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "SignalR broadcast failed for {Symbol}", symbol);
                }

                if (tab.Settings.AutoTrade && ShouldAutoTrade(keys, voteResult))
                    await ExecuteAutoTradeAsync(userId, tab, signal, keys, ct);
            }
        }
    }

    private bool ShouldAutoTrade(UserApiKeys keys, LlmVotingResult? voteResult)
    {
        var tz = GetTimezone();
        var etNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        var timeNow = TimeOnly.FromDateTime(etNow);
        if (timeNow < AutoTradeWindowOpen || timeNow >= AutoTradeWindowClose)
            return false;

        if (!keys.LlmVotingEnabled) return true;
        return voteResult?.Consensus == VoteDecision.Approve;
    }

    /// <summary>
    /// Places an auto-trade order for an approved signal. Real money path —
    /// every check below is load-bearing.
    ///
    /// Gating order (matches <c>MonitorWorker.PassesRiskGuardianAsync</c>):
    ///   1. <see cref="RiskGuardian.CalculateMaxQuantity"/> on the user's
    ///      global config (<c>UserPreferences</c>) — most-restrictive of
    ///      per-trade, daily-remaining, account-percent caps.
    ///   2. Workspace-local cap (<c>tab.Settings.MaxPositionSize</c>) tightens
    ///      further if the workspace has a smaller notional limit.
    ///   3. <see cref="RiskGuardian.ValidateTrade"/> performs the full
    ///      validation (stop-side correctness, daily-loss circuit breaker,
    ///      stop-distance bounds) on the synthesized <see cref="TradeSetup"/>.
    ///   4. Only if Guardian approves do we hit the broker.
    ///
    /// Block path writes "AUTO_TRADE_BLOCKED" to the audit DB with structured
    /// dataJson (block reasons, expected/worst-case loss, sized quantity).
    /// Exception path writes "AUTO_TRADE_FAIL" with the exception detail.
    /// Both paths skip the broker call — no order placed.
    /// </summary>
    private async Task ExecuteAutoTradeAsync(Guid userId, WorkspaceTab tab, TradeSignal signal, UserApiKeys keys, CancellationToken ct)
    {
        try
        {
            var broker = BuildBroker(keys);
            if (!broker.IsConnected)
                await broker.ConnectAsync(ct);

            var riskPerShare = Math.Abs(signal.SuggestedEntry - signal.SuggestedStop);
            if (riskPerShare <= 0)
            {
                await SafeAuditAsync("AUTO_TRADE_BLOCKED",
                    $"[{signal.StrategyName}] {signal.Symbol} blocked: signal has no stop or stop equals entry",
                    userId, ct: ct);
                return;
            }

            var guardian = await riskGuardianService.GetForUserAsync(userId, ct);

            // Guardian sizing first — uses user's UserPreferences-backed config
            // (per-trade cap, daily remaining, account-percent cap). Workspace
            // settings can only shrink the order, never grow it past Guardian.
            var qty = guardian.CalculateMaxQuantity(signal.SuggestedEntry, signal.SuggestedStop);

            // Workspace caps can only shrink the order. Take the min unconditionally:
            // a cap that rounds down to 0 shares means "this workspace forbids the
            // trade" and must drive qty to 0 (caught by the qty<=0 gate below). The
            // old `> 0` guard skipped a zero-allowance cap entirely, letting the looser
            // Guardian-sized order through and breaching the workspace limit.
            var workspacePerTradeQty = (int)Math.Floor(tab.Settings.RiskLimits.MaxLossPerTrade / riskPerShare);
            qty = Math.Min(qty, workspacePerTradeQty);

            if (signal.SuggestedEntry > 0m)
            {
                var notionalCapQty = (int)Math.Floor(tab.Settings.MaxPositionSize / signal.SuggestedEntry);
                qty = Math.Min(qty, notionalCapQty);
            }

            if (qty <= 0)
            {
                await SafeAuditAsync("AUTO_TRADE_BLOCKED",
                    $"[{signal.StrategyName}] {signal.Symbol} blocked: computed qty<=0 after Guardian + workspace caps",
                    userId, ct: ct);
                return;
            }

            // Synthesize the canonical setup and run the full Guardian
            // validation. Take-profit defaults to a +1R level when the signal
            // didn't specify one so the R:R warning has a real number.
            var takeProfit = signal.Targets.Count > 0
                ? signal.Targets[0]
                : signal.Direction == TradeDirection.Long
                    ? signal.SuggestedEntry + (signal.SuggestedEntry - signal.SuggestedStop)
                    : signal.SuggestedEntry - (signal.SuggestedStop - signal.SuggestedEntry);

            var setup = new TradeSetup
            {
                Symbol          = signal.Symbol,
                Direction       = signal.Direction,
                EntryPrice      = signal.SuggestedEntry,
                EntryType       = OrderType.Limit,
                StopLoss        = signal.SuggestedStop,
                TakeProfit      = takeProfit,
                Quantity        = qty,
                ConfidenceScore = (int)Math.Clamp(signal.ConfidencePercent, 0m, 100m),
                Rationale       = signal.Reason,
            };

            var verdict = guardian.ValidateTrade(setup);
            if (!verdict.IsApproved)
            {
                var reasons = string.Join("; ", verdict.BlockReasons);
                logger.LogWarning("Auto-trade BLOCKED by RiskGuardian for {Symbol}: {Reasons}", signal.Symbol, reasons);
                auditLogger.Log("AUTO_TRADE_BLOCKED", $"Reasons={reasons}", signal.Symbol);
                await SafeAuditAsync("AUTO_TRADE_BLOCKED",
                    message: $"[{signal.StrategyName}] {signal.Symbol} ({signal.Direction}) blocked by RiskGuardian: {reasons}",
                    userId: userId,
                    dataJson: System.Text.Json.JsonSerializer.Serialize(new
                    {
                        blockReasons = verdict.BlockReasons,
                        warnings     = verdict.Warnings,
                        expectedLoss = verdict.ExpectedLoss,
                        worstCase    = verdict.WorstCaseLoss,
                        quantity     = qty,
                        entry        = signal.SuggestedEntry,
                        stop         = signal.SuggestedStop,
                    }),
                    ct: ct);
                return;
            }

            var request = new OrderRequest
            {
                Symbol     = signal.Symbol,
                Quantity   = qty,
                Side       = signal.Direction == TradeDirection.Long ? OrderSide.Buy : OrderSide.Sell,
                Type       = OrderType.Limit,
                LimitPrice = signal.SuggestedEntry,
                TimeInForce = "DAY"
            };

            var result = await broker.PlaceOrderAsync(request, ct);
            if (result.IsSuccess)
            {
                auditLogger.Log("AUTO_TRADE", $"Side={request.Side} Qty={qty} Price={signal.SuggestedEntry:F2} OrderId={result.BrokerOrderId}", signal.Symbol);
                await SafeAuditAsync("AUTO_TRADE",
                    message: $"[{signal.StrategyName}] {signal.Symbol} {request.Side} qty={qty} @ ${signal.SuggestedEntry:F2} — broker order {result.BrokerOrderId}",
                    userId: userId, ct: ct);
            }
            else
            {
                logger.LogWarning("Auto-trade failed: {Symbol} — {Message}", signal.Symbol, result.Message);
                auditLogger.Log("AUTO_TRADE_FAIL", $"Reason={result.Message}", signal.Symbol);
                await SafeAuditAsync("AUTO_TRADE_FAIL",
                    message: $"[{signal.StrategyName}] {signal.Symbol} broker rejected order: {result.Message}",
                    userId: userId, ct: ct);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Auto-trade error for {Symbol}", signal.Symbol);
            auditLogger.Log("AUTO_TRADE_FAIL", $"Symbol={signal.Symbol} Exception={ex.Message}", signal.Symbol);
            await SafeAuditAsync("AUTO_TRADE_FAIL",
                message: $"[{signal.StrategyName}] {signal.Symbol} threw during placement: {ex.Message}",
                userId: userId,
                dataJson: System.Text.Json.JsonSerializer.Serialize(new { exception = ex.ToString() }),
                ct: ct);
        }
    }

    /// <summary>
    /// Wraps <see cref="AuditLogRepository.LogAsync"/> so a failing audit-DB
    /// write never bubbles out of the trade path. The legacy file-based
    /// <see cref="AuditLogger"/> already captured the event by the time we
    /// reach here, so swallowing here is safe and prevents audit-store
    /// outages from masking broker-call results.
    /// </summary>
    private async Task SafeAuditAsync(string category, string message, Guid? userId, string? dataJson = null, CancellationToken ct = default)
    {
        try { await auditLogRepo.LogAsync(category, message, userId, dataJson, ct); }
        catch (Exception ex) { logger.LogWarning(ex, "Audit DB write failed for {Category}", category); }
    }

    private async Task RefreshPositionsAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var userKeyService = scope.ServiceProvider.GetRequiredService<UserKeyService>();
        var userList = await userKeyService.GetAllActiveUsersAsync(ct);

        foreach (var (userId, keys) in userList)
        {
            IBrokerClient? broker = null;
            try
            {
                broker = BuildBroker(keys);
                if (!broker.IsConnected)
                    await broker.ConnectAsync(ct);

                var positions = await broker.GetPositionsAsync(ct);
                tradingState.UpdatePositions(userId.ToString(), broker.BrokerType, positions.ToList());
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Position refresh failed for user {UserId} on {Broker}", userId, broker?.BrokerType);
            }
            finally
            {
                if (broker is IAsyncDisposable iad) await iad.DisposeAsync();
                else if (broker is IDisposable id) id.Dispose();
            }
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private static SwitchableMarketDataFeed BuildFeed(UserApiKeys keys)
    {
        // Alpaca provides real-time market data alongside trading, so we no
        // longer wire a separate Polygon feed. The Mock feed stays as a safe
        // default for users without Alpaca credentials and for tests.
        var feed = new SwitchableMarketDataFeed("Mock");
        feed.Register(new MockDataFeed());
        return feed;
    }

    private static IBrokerClient BuildBroker(UserApiKeys keys)
    {
        // Honor the user's explicitly chosen broker when its credentials are configured;
        // otherwise fall back to whichever broker has credentials, then sandbox.
        var preference = keys.DefaultBroker?.Trim();

        if (string.Equals(preference, "Alpaca", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(keys.AlpacaApiKeyId))
        {
            return new AlpacaBrokerClient(keys.AlpacaApiKeyId, keys.AlpacaApiSecretKey ?? "", keys.AlpacaIsPaper);
        }

        if (!string.IsNullOrWhiteSpace(keys.AlpacaApiKeyId))
            return new AlpacaBrokerClient(keys.AlpacaApiKeyId, keys.AlpacaApiSecretKey ?? "", keys.AlpacaIsPaper);

        return new SandboxBrokerClient();
    }

    private AppSettings CloneSettingsWithUserKeys(UserApiKeys keys)
    {
        var s = new AppSettings
        {
            ClaudeApiKey     = ResolveClaudeKey(keys),
            LlmVotingEnabled = keys.LlmVotingEnabled,
            LlmVoterModel    = !string.IsNullOrWhiteSpace(keys.ClaudeModel)
                ? keys.ClaudeModel
                : appSettings.LlmVoterModel,
            LlmConsensusThreshold = appSettings.LlmConsensusThreshold,
            Timezone         = appSettings.Timezone,
            StrategyEvaluationIntervalSeconds = appSettings.StrategyEvaluationIntervalSeconds
        };
        return s;
    }

    /// <summary>
    /// Per-user Claude key resolution: the user's own DB key wins when set; otherwise
    /// fall back to appSettings.ClaudeApiKey, which is already overlaid from the
    /// MindAttic LLM credential store (%APPDATA%/MindAttic/LLM/credentials.json).
    /// </summary>
    private string ResolveClaudeKey(UserApiKeys keys) =>
        !string.IsNullOrWhiteSpace(keys.ClaudeApiKey)
            ? keys.ClaudeApiKey!
            : appSettings.ClaudeApiKey;

    // US markets run on Eastern Time. Hardcoded — no setting, no fallback to
    // UTC (a UTC fallback would silently shift session windows by 4-5 hours).
    // If "Eastern Standard Time" isn't resolvable on the host, that's a deployment
    // bug we want to surface immediately, not paper over.
    private static readonly TimeZoneInfo EasternZone =
        TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

    private TimeZoneInfo GetTimezone() => EasternZone;
}
