using IdiotProof.Blazor.Data;
using IdiotProof.Blazor.Services;
using IdiotProof.DataFeeds;
using IdiotProof.Engine.Settings;
using IdiotProof.Models;
using IdiotProof.Scripting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IdiotProof.Monitor;

/// <summary>
/// ON-DEMAND gapper generator (operator CLI: <c>auto-gapper</c>). Pulls the
/// current movers from Alpaca, keeps those gapping up ≥ the configured threshold,
/// and — for the top-N by conviction — SYNTHESIZES a gapper strategy whose
/// risk/exit/size parameters are DETERMINED PER TICKER from that name's own
/// behavior (price tier, gap size, premarket volume, realized volatility), then
/// arms it. There is NO scheduled trigger: a standardized, information-complete
/// auto-generation flow is future work (see USER_STORIES Epic S). This engine is
/// the seed for it.
///
/// Every screened candidate — armed or skipped — is written to
/// <see cref="AutoGapperCandidate"/> with its full feature vector and the chosen
/// parameters, so a future model can learn which gappers to arm and how to tune
/// them (join back to the TradeDiary via StrategyId for the realized-P&amp;L label).
///
/// Safety: defaults to PAPER routing and will NOT arm if the account routes live
/// under "paper" mode. Arming only queues the strategy — the Monitor's three
/// gates (conditions → LLM → RiskGuardian) and the mock-data block still guard
/// every actual order.
/// </summary>
public sealed class AutoGapperScanner(
    StrategyRepository strategyRepo,
    IMarketDataFeed feed,
    UserBrokerResolver brokerResolver,
    UserKeyService userKeys,
    AuditLogRepository auditLogRepo,
    Microsoft.EntityFrameworkCore.IDbContextFactory<AppDbContext> dbFactory,
    AppSettings settings,
    ILogger<AutoGapperScanner> logger)
{
    public sealed record ScanSummary(int Screened, int Qualified, int Armed, int Skipped, string Note);

    /// <summary>
    /// The pipeline. When <paramref name="dryRun"/> is true it computes and
    /// prints the plan but arms nothing and writes no rows (the operator preview,
    /// so it never consumes the day's once-per-day slot).
    /// </summary>
    public async Task<ScanSummary> RunScanAsync(Guid userId, bool dryRun, string phase, CancellationToken ct)
    {
        var k = await userKeys.GetOrCreateAsync(userId);
        if (string.IsNullOrWhiteSpace(k.AlpacaApiKeyId) || string.IsNullOrWhiteSpace(k.AlpacaApiSecretKey))
            return new ScanSummary(0, 0, 0, 0, "user has no Alpaca data keys");

        var brokerMode = (settings.AutoGapperBrokerMode ?? "paper").ToLowerInvariant();

        // Paper-only guard: if the feature is paper-mode but the account routes
        // live, refuse to arm — never place a real order from an unattended job.
        if (brokerMode == "paper")
        {
            var broker = await brokerResolver.ResolveAsync(userId, "Paper", ct);
            if (!broker.IsPaper)
            {
                var msg = $"broker mode is 'paper' but {broker.BrokerType} routes LIVE — refusing to arm auto-gappers";
                logger.LogError("Auto-gapper: {Msg}.", msg);
                if (!dryRun)
                    await auditLogRepo.LogAsync("auto-gapper-blocked", $"Auto-gapper aborted: {msg}.", userId: userId, ct: ct);
                return new ScanSummary(0, 0, 0, 0, msg);
            }
        }

        List<(string Symbol, double Percent, double Price)> movers;
        try { movers = await StrategyScanner.FetchMoversAsync(k.AlpacaApiKeyId!, k.AlpacaApiSecretKey!, 50); }
        catch (Exception ex) { return new ScanSummary(0, 0, 0, 0, $"movers screen failed: {ex.Message}"); }

        var qualified = movers
            .Where(m => m.Percent >= settings.AutoGapperMinGapPercent && m.Price >= settings.AutoGapperMinPrice)
            .Select(m => m.Symbol)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var todayEt = EtDate(DateTime.UtcNow);
        logger.LogInformation("Auto-gapper: {Screened} movers → {Qualified} gapping ≥{Gap}% and ≥${Px}.",
            movers.Count, qualified.Count, settings.AutoGapperMinGapPercent, settings.AutoGapperMinPrice);

        // Gather per-ticker signals and synthesize adaptive plans. One bad
        // ticker (a glitched premarket print producing a degenerate price
        // band, GapperProfile.Validate() rejecting it, GapperScriptFactory.
        // Compose throwing) must not abort the scan for every OTHER
        // candidate — the arm loop below already isolates per-symbol
        // failures the same way; this loop previously didn't.
        var plans = new List<CandidatePlan>();
        foreach (var m in movers.Where(m => qualified.Contains(m.Symbol, StringComparer.OrdinalIgnoreCase))
                                 .GroupBy(m => m.Symbol, StringComparer.OrdinalIgnoreCase).Select(g => g.First()))
        {
            var sym = m.Symbol.ToUpperInvariant();
            try
            {
                var sig = await GatherSignalsAsync(sym, m.Percent, m.Price, ct);
                plans.Add(Synthesize(sym, sig));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Auto-gapper: failed to synthesize a plan for {Symbol} — skipping.", sym);
            }
        }

        // Rank by conviction (gap × premarket-liquidity) and keep the top N.
        var ranked = plans.OrderByDescending(p => p.Score).ToList();
        for (var i = 0; i < ranked.Count; i++) ranked[i].Rank = i + 1;
        var toArm = ranked.Take(Math.Max(0, settings.AutoGapperMaxCount)).ToList();

        // Persist the scan header (also the once-per-day idempotency guard).
        AutoGapperScan? header = null;
        if (!dryRun)
        {
            header = new AutoGapperScan
            {
                ScanEtDate = todayEt,
                Phase = phase,
                ScanStartedUtc = DateTime.UtcNow,
                MoversScreened = movers.Count,
                Qualified = qualified.Count,
                MinGapPercent = settings.AutoGapperMinGapPercent,
                MaxCount = settings.AutoGapperMaxCount,
                BrokerMode = brokerMode,
            };
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            db.AutoGapperScans.Add(header);
            await db.SaveChangesAsync(ct);
        }

        int armed = 0, skipped = 0;
        foreach (var plan in ranked)
        {
            var willArm = toArm.Contains(plan);
            string? skipReason = willArm ? null : "below-rank-cutoff";
            Guid? strategyId = null;

            if (willArm)
            {
                // Dedup against an already-active strategy for this symbol.
                if (await strategyRepo.CountActiveForSymbolAsync(userId, plan.Symbol, ct) > 0)
                {
                    skipReason = "duplicate-active";
                }
                else if (armed >= settings.AutoGapperMaxCount)
                {
                    skipReason = "cap-reached";
                }
                else if (!dryRun)
                {
                    try
                    {
                        var built = plan.Builder.Build();
                        var canon = StrategyJson.Serialize(built);
                        var created = await strategyRepo.CreateAsync(
                            userId,
                            title: plan.Title,
                            symbol: plan.Symbol,
                            scriptText: plan.Builder.ToScript(),
                            description: $"Auto-armed {plan.Class} gapper (+{plan.Signals.GapPercent:0.#}% @ ${plan.Signals.Price:0.##}).",
                            scriptJson: canon,
                            author: "IdiotProof Auto-Gapper",
                            ct: ct);
                        var mut = await strategyRepo.SetActiveAsync(created.Id, true, userId, ct);
                        if (mut == StrategyMutation.Ok) { strategyId = created.Id; armed++; }
                        else skipReason = $"activate:{mut}";
                    }
                    catch (Exception ex)
                    {
                        skipReason = "synthesize-error";
                        logger.LogWarning(ex, "Auto-gapper: failed to arm {Symbol}.", plan.Symbol);
                    }
                }
                else
                {
                    armed++; // dry-run: count what WOULD arm
                }
            }

            if (skipReason is not null) skipped++;

            if (!dryRun && header is not null)
            {
                await using var db = await dbFactory.CreateDbContextAsync(ct);
                db.AutoGapperCandidates.Add(ToRow(header.Id, todayEt, plan, strategyId is not null, strategyId, skipReason));
                await db.SaveChangesAsync(ct);
            }

            logger.LogInformation("Auto-gapper: #{Rank} {Symbol} +{Gap:0.#}% ${Px:0.##} [{Class}] stop {Stop:0.#}% gv {Gv:0.#}% ${Notional} → {Outcome}",
                plan.Rank, plan.Symbol, plan.Signals.GapPercent, plan.Signals.Price, plan.Class,
                plan.Profile.StopLossPercent, plan.Profile.PeakGivebackPercent, plan.Profile.DefaultNotional,
                strategyId is not null ? "ARMED" : dryRun && toArm.Contains(plan) && skipReason is null ? "would-arm" : $"skip:{skipReason ?? "n/a"}");
        }

        if (!dryRun && header is not null)
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var h = await db.AutoGapperScans.FirstAsync(s => s.Id == header.Id, ct);
            h.Armed = armed; h.Skipped = skipped; h.ScanCompletedUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            await auditLogRepo.LogAsync("auto-gapper-scan",
                $"Auto-gapper armed {armed} of {qualified.Count} qualifying gappers (paper: {brokerMode == "paper"}).",
                userId: userId, ct: ct);
        }

        return new ScanSummary(movers.Count, qualified.Count, armed, skipped,
            dryRun ? "dry-run (nothing armed or written)" : "complete");
    }

    // ── Signals ─────────────────────────────────────────────────────────

    private sealed record Signals(
        double Price, double GapPercent, double? PreviousClose,
        long? PremarketVolume, double? AvgDailyVolume, double? VolumeRatio, double? AtrPercent);

    private async Task<Signals> GatherSignalsAsync(string symbol, double screenerPct, double screenerPx, CancellationToken ct)
    {
        var nowUtc = DateTime.UtcNow;
        double? prevClose = null;
        try { prevClose = (double?)await feed.GetPreviousCloseAsync(symbol, nowUtc, ct); } catch { }

        // Daily bars → average volume + ATR% (the ticker's own volatility).
        double? avgVol = null, atrPct = null;
        try
        {
            var daily = new List<Candle>();
            await foreach (var c in feed.GetHistoricalCandlesAsync(symbol, nowUtc.AddDays(-21), nowUtc, TimeSpan.FromDays(1), ct))
                daily.Add(c);
            var recent = daily.TakeLast(15).ToList();
            if (recent.Count >= 3)
            {
                avgVol = recent.Average(c => (double)c.Volume);
                atrPct = recent.Where(c => c.Close > 0).Select(c => (double)(c.High - c.Low) / (double)c.Close * 100.0).DefaultIfEmpty(0).Average();
            }
        }
        catch { }

        // Today's premarket minute bars → cumulative premarket volume + last price.
        long? pmVol = null; double px = screenerPx;
        try
        {
            var etMidnightUtc = TimeZoneInfo.ConvertTimeToUtc(
                DateTime.SpecifyKind(DateTime.Parse(EtDate(nowUtc)).Date.Add(new TimeSpan(4, 0, 0)), DateTimeKind.Unspecified),
                MarketTime.Eastern);
            var pm = new List<Candle>();
            await foreach (var c in feed.GetHistoricalCandlesAsync(symbol, etMidnightUtc, nowUtc, TimeSpan.FromMinutes(1), ct))
                pm.Add(c);
            if (pm.Count > 0)
            {
                pmVol = (long)pm.Sum(c => c.Volume);
                if (pm[^1].Close > 0) px = (double)pm[^1].Close;
            }
        }
        catch { }

        // The screener's percent_change is authoritative (it's what qualified the
        // name). Only recompute from previous close when we actually have premarket
        // trades to price against — before the 4:00 open there are none, and the
        // last daily close ≈ current price would falsely read as a ~0% gap.
        var gap = pmVol is not null && prevClose is > 0
            ? (px - prevClose.Value) / prevClose.Value * 100.0
            : screenerPct;
        double? volRatio = avgVol is > 0 && pmVol is not null ? pmVol.Value / avgVol.Value : null;
        return new Signals(px, gap, prevClose, pmVol, avgVol, volRatio, atrPct);
    }

    // ── Adaptive synthesis ──────────────────────────────────────────────

    private sealed class CandidatePlan
    {
        public required string Symbol { get; init; }
        public required string Title { get; init; }
        public required string Class { get; init; }
        public required Signals Signals { get; init; }
        public required GapperProfile Profile { get; init; }
        public required StrategyBuilder Builder { get; init; }
        public required double Score { get; init; }
        public int Rank { get; set; }
    }

    private static double Clamp(double v, double lo, double hi) => Math.Max(lo, Math.Min(hi, v));

    private CandidatePlan Synthesize(string symbol, Signals s)
    {
        // Behavior class by price tier — the coarse shape of how the name trades.
        var (cls, stopMin, stopMax, trailMult, gvCap, arm, sellBy, minVol, baseNotional, typAtr) =
            s.Price < 5   ? ("penny",  6.0, 12.0, 1.25, 22.0, "09:10", "09:27", 3.0,  400.0, 12.0)
          : s.Price < 20  ? ("mid",    4.0,  8.0, 1.50, 28.0, "09:12", "09:28", 2.0,  800.0,  7.0)
          :                 ("large",  2.5,  5.0, 0.00, 35.0, "09:15", "09:29", 1.5, 1500.0,  4.0);

        var atr = s.AtrPercent is > 0 ? s.AtrPercent.Value : typAtr;
        var stop = Clamp(atr, stopMin, stopMax);                    // stop sized to the ticker's own volatility
        double? trailing = cls == "large" ? null : Math.Round(stop * trailMult, 1);
        var gapGiveback = Clamp(30.0 - (s.GapPercent - 15.0) * 0.4, 15.0, 30.0); // bigger gap → tighter giveback
        var giveback = Math.Min(gvCap, gapGiveback);

        var priceLow = Math.Round(Math.Max(0.1, s.Price * 0.6), 2);
        var priceHigh = Math.Round(s.Price * 1.8, 2);

        // Adaptive notional: more premarket liquidity → larger; more volatile → smaller.
        var convFactor = Clamp(1.0 + (s.VolumeRatio ?? 0.0), 0.75, 1.6);
        var volFactor = Clamp(atr / typAtr, 0.75, 1.6);
        var notional = Clamp(Math.Round(baseNotional * convFactor / volFactor / 50.0) * 50.0, 200.0, 3000.0);

        var profile = new GapperProfile
        {
            Id = "auto", Name = $"Auto {cls}",
            MinGapPercent = 15, MaxGapPercent = null,
            MinVolumeRatio = minVol, MinPrice = priceLow, MaxPrice = priceHigh,
            EntryWindowStartEt = "04:00", EntryWindowEndEt = "09:00",
            StopLossPercent = stop, TrailingStopPercent = trailing,
            PeakGivebackPercent = giveback, ArmExitAtEt = arm, SellByEt = sellBy,
            DefaultNotional = (decimal)notional,
        };
        var builder = GapperScriptFactory.Compose(symbol, profile);
        var score = s.GapPercent * (1.0 + (s.VolumeRatio ?? 0.0));

        return new CandidatePlan
        {
            Symbol = symbol,
            Title = $"{symbol} Auto-Gapper +{s.GapPercent:0}% ({cls})",
            Class = cls,
            Signals = s,
            Profile = profile,
            Builder = builder,
            Score = score,
        };
    }

    private static AutoGapperCandidate ToRow(Guid scanId, string dateEt, CandidatePlan p, bool armed, Guid? strategyId, string? skipReason) => new()
    {
        ScanId = scanId, ScanEtDate = dateEt, Symbol = p.Symbol, CapturedUtc = DateTime.UtcNow,
        Price = p.Signals.Price, PreviousClose = p.Signals.PreviousClose, GapPercent = p.Signals.GapPercent,
        PremarketVolume = p.Signals.PremarketVolume, AvgDailyVolume = p.Signals.AvgDailyVolume,
        VolumeRatio = p.Signals.VolumeRatio, AtrPercent = p.Signals.AtrPercent,
        Score = p.Score, Rank = p.Rank,
        BehaviorClass = p.Class, StopLossPercent = p.Profile.StopLossPercent, TrailingStopPercent = p.Profile.TrailingStopPercent,
        PeakGivebackPercent = p.Profile.PeakGivebackPercent, ArmExitEt = p.Profile.ArmExitAtEt, SellByEt = p.Profile.SellByEt,
        MinVolumeRatio = p.Profile.MinVolumeRatio, PriceBandLow = p.Profile.MinPrice, PriceBandHigh = p.Profile.MaxPrice,
        Notional = p.Profile.DefaultNotional,
        Armed = armed, StrategyId = strategyId, SkipReason = skipReason,
    };

    // ── helpers ─────────────────────────────────────────────────────────

    private static string EtDate(DateTime utc) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), MarketTime.Eastern))
            .ToString("yyyy-MM-dd");
}
