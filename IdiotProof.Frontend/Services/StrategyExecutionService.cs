using IdiotProof.Brokers;
using IdiotProof.DataFeeds;
using IdiotProof.Engine;
using IdiotProof.Engine.Settings;
using IdiotProof.Engine.Workspace;
using IdiotProof.Frontend.Data;
using IdiotProof.Frontend.Hubs;
using IdiotProof.Models;
using IdiotProof.Strategies;
using Microsoft.AspNetCore.SignalR;

namespace IdiotProof.Frontend.Services;

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
    private readonly AppSettings appSettings;
    private readonly AuditLogger auditLogger;
    private readonly IHubContext<TradingHub> hubContext;
    private readonly ILogger<StrategyExecutionService> logger;

    public StrategyExecutionService(
        IServiceScopeFactory scopeFactory,
        WorkspaceManager workspaceManager,
        StrategyRegistry strategyRegistry,
        TradingStateService tradingState,
        LlmVotingService llmVoting,
        AppSettings appSettings,
        AuditLogger auditLogger,
        IHubContext<TradingHub> hubContext,
        ILogger<StrategyExecutionService> logger)
    {
        this.scopeFactory      = scopeFactory;
        this.workspaceManager  = workspaceManager;
        this.strategyRegistry  = strategyRegistry;
        this.tradingState      = tradingState;
        this.llmVoting         = llmVoting;
        this.appSettings       = appSettings;
        this.auditLogger       = auditLogger;
        this.hubContext        = hubContext;
        this.logger            = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        tradingState.SetEngineRunning(true);
        logger.LogInformation("Strategy engine started. Interval: {Interval}s", appSettings.StrategyEvaluationIntervalSeconds);
        auditLogger.Log("ENGINE_START", $"Interval={appSettings.StrategyEvaluationIntervalSeconds}s");

        var positionCyclesSinceRefresh = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            var interval = Math.Max(appSettings.StrategyEvaluationIntervalSeconds, 5);

            try
            {
                await RunEvaluationCycleAsync(stoppingToken);
                tradingState.RecordEvaluation();
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unhandled error in evaluation cycle");
            }

            positionCyclesSinceRefresh++;
            if (positionCyclesSinceRefresh >= (60 / interval))
            {
                positionCyclesSinceRefresh = 0;
                await RefreshPositionsAsync(stoppingToken);
            }

            try { await Task.Delay(TimeSpan.FromSeconds(interval), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }

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
            var tabs = workspaceManager.GetTabsForUser(userId);

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
        string userId, UserApiKeys keys, WorkspaceTab tab,
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
                signal.UserId = userId;
                tradingState.AddSignal(signal);

                var signalKey = $"{signal.Symbol}_{signal.StrategyName}_{signal.GeneratedUtc:yyyyMMddHHmmss}";
                auditLogger.Log("SIGNAL", $"Dir={signal.Direction} Conf={signal.ConfidencePercent:F1}% Entry={signal.SuggestedEntry:F2} Strat={signal.StrategyName}", symbol);

                LlmVotingResult? voteResult = null;
                if (keys.LlmVotingEnabled && !string.IsNullOrWhiteSpace(keys.ClaudeApiKey))
                {
                    try
                    {
                        // Temporarily overlay user's Claude key for this vote
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
                    await ExecuteAutoTradeAsync(tab, signal, keys, ct);
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

    private async Task ExecuteAutoTradeAsync(WorkspaceTab tab, TradeSignal signal, UserApiKeys keys, CancellationToken ct)
    {
        try
        {
            var broker = BuildBroker(keys);
            if (!broker.IsConnected) return;

            var riskPerShare = Math.Abs(signal.SuggestedEntry - signal.SuggestedStop);
            if (riskPerShare <= 0) return;

            var qty = (int)Math.Floor(tab.Settings.RiskLimits.MaxLossPerTrade / riskPerShare);
            if (qty <= 0) return;

            var positionValue = qty * signal.SuggestedEntry;
            if (positionValue > tab.Settings.MaxPositionSize)
                qty = (int)Math.Floor(tab.Settings.MaxPositionSize / signal.SuggestedEntry);

            if (qty <= 0) return;

            var request = new OrderRequest
            {
                Symbol = signal.Symbol,
                Quantity = qty,
                Side = signal.Direction == TradeDirection.Long ? OrderSide.Buy : OrderSide.Sell,
                Type = OrderType.Limit,
                LimitPrice = signal.SuggestedEntry,
                TimeInForce = "DAY"
            };

            var result = await broker.PlaceOrderAsync(request, ct);
            if (result.IsSuccess)
            {
                auditLogger.Log("AUTO_TRADE", $"Side={request.Side} Qty={qty} Price={signal.SuggestedEntry:F2} OrderId={result.BrokerOrderId}", signal.Symbol);
            }
            else
            {
                logger.LogWarning("Auto-trade failed: {Symbol} — {Message}", signal.Symbol, result.Message);
                auditLogger.Log("AUTO_TRADE_FAIL", $"Reason={result.Message}", signal.Symbol);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Auto-trade error for {Symbol}", signal.Symbol);
        }
    }

    private async Task RefreshPositionsAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var userKeyService = scope.ServiceProvider.GetRequiredService<UserKeyService>();
        var userList = await userKeyService.GetAllActiveUsersAsync(ct);

        foreach (var (userId, keys) in userList)
        {
            if (string.IsNullOrWhiteSpace(keys.AlpacaApiKeyId)) continue;
            try
            {
                var broker = BuildBroker(keys);
                var positions = await broker.GetPositionsAsync(ct);
                tradingState.UpdatePositions(userId, BrokerType.Alpaca, positions.ToList());
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Position refresh failed for user {UserId}", userId);
            }
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private static SwitchableMarketDataFeed BuildFeed(UserApiKeys keys)
    {
        var feed = new SwitchableMarketDataFeed("Mock");
        feed.Register(new MockDataFeed());

        if (!string.IsNullOrWhiteSpace(keys.PolygonApiKey))
        {
            feed.Register(new PolygonDataFeed(keys.PolygonApiKey));
            feed.SetActiveFeed("Polygon");
        }

        return feed;
    }

    private static IBrokerClient BuildBroker(UserApiKeys keys)
    {
        if (!string.IsNullOrWhiteSpace(keys.AlpacaApiKeyId))
            return new AlpacaBrokerClient(keys.AlpacaApiKeyId, keys.AlpacaApiSecretKey ?? "", keys.AlpacaIsPaper);

        return new SandboxBrokerClient();
    }

    private AppSettings CloneSettingsWithUserKeys(UserApiKeys keys)
    {
        var s = new AppSettings
        {
            ClaudeApiKey     = keys.ClaudeApiKey ?? "",
            LlmVotingEnabled = keys.LlmVotingEnabled,
            Timezone         = appSettings.Timezone,
            StrategyEvaluationIntervalSeconds = appSettings.StrategyEvaluationIntervalSeconds
        };
        return s;
    }

    private TimeZoneInfo GetTimezone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(appSettings.Timezone); }
        catch { return TimeZoneInfo.Utc; }
    }
}
