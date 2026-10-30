using IdiotProof.Blazor.Data;
using IdiotProof.Blazor.Services;
using IdiotProof.DataFeeds;
using IdiotProof.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IdiotProof.Monitor;

/// <summary>
/// Daily BE-vs-BEX decay/divergence check (BE = Bloom Energy stock; BEX = a 2x
/// daily-reset leveraged long ETF on BE). No strategy's DSL condition can
/// reference a second ticker's price live (every <c>IdiotProof.Scripting</c>
/// condition evaluates only against its own symbol's candles), so this signal
/// is computed one level up, in Monitor orchestration, and applied by
/// arming/disarming the BE strategy's <see cref="Strategy.IsActive"/> flag —
/// NOT by inventing a new cross-ticker condition type.
///
/// A 2x daily-reset ETF's return over N days diverges from 2x the underlying's
/// N-day return purely from daily-rebalancing volatility drag (larger in a
/// choppy/whipsaw market, smaller in a clean trend). That divergence is
/// "decayPercent" below: positive means BEX is underperforming its 2x target.
///
/// Gating goes exclusively through <see cref="StrategyRepository.SetActiveAsync"/>,
/// never a direct EF/SQL write to IsActive — that method already refuses to
/// deactivate a strategy holding a position (<see cref="StrategyMutation.PositionOpen"/>),
/// which is what stops this scanner from ever stranding an open position without
/// its exit/stop-loss/PeakGiveback management (MonitorWorker only evaluates rows
/// where IsActive=true, position or not).
/// </summary>
public sealed class BeBexDecayScanner(
    IMarketDataFeed feed,
    StrategyRepository strategyRepo,
    SettingsRepository settingsRepo,
    AuditLogRepository auditLogRepo,
    IDbContextFactory<AppDbContext> dbFactory,
    ILogger<BeBexDecayScanner> logger)
{
    public const string UnderlyingSymbol = "BE";
    public const string LeveragedSymbol = "BEX";

    /// <summary>
    /// Strategy.Author tag stamped on the specific strategy this scanner is meant
    /// to gate (tools/seed-be-decay-gated-strategy.sql). Gating by Symbol=="BE"
    /// alone would also sweep up any OTHER strategy a user (this one or, in a
    /// multi-tenant deployment, a different one) happens to name/tag "BE" for an
    /// unrelated reason — arming/disarming a strategy this scanner has no
    /// business touching. The tag scopes gating to exactly the row(s) this
    /// feature created.
    /// </summary>
    public const string DecayGatedAuthorTag = "user-pairs-trade-idea";

    private const double DefaultLeverage = 2.0;
    private const int DefaultLookbackDays = 20;
    private const double DefaultDisarmThresholdPercent = 8.0;

    /// <summary>computedUtc/lookbackDays travel with the reading so a later
    /// audit-log or SettingsKv read doesn't have to guess what produced it.</summary>
    public sealed record DecayResult(
        double BeReturnPercent, double ExpectedBexReturnPercent, double ActualBexReturnPercent, double DecayPercent);

    public async Task RunScanAsync(CancellationToken ct)
    {
        var lookbackDays = await settingsRepo.GetIntAsync("decay.be-bex.lookbackdays", ct) ?? DefaultLookbackDays;
        var thresholdPercent = (double?)await settingsRepo.GetDecimalAsync("decay.be-bex.threshold", ct) ?? DefaultDisarmThresholdPercent;

        var endUtc = DateTime.UtcNow;
        // 2x lookback + 10 calendar days of buffer so weekends/holidays never
        // starve the window of enough TRADING days.
        var startUtc = endUtc.AddDays(-(lookbackDays * 2 + 10));

        // BE and BEX are independent feed round-trips — fetch concurrently
        // rather than paying their latencies back-to-back.
        async Task<List<Candle>> CollectAsync(string symbol)
        {
            var bars = new List<Candle>();
            await foreach (var c in feed.GetHistoricalCandlesAsync(symbol, startUtc, endUtc, TimeSpan.FromDays(1), ct))
                bars.Add(c);
            return bars;
        }

        var beBarsTask = CollectAsync(UnderlyingSymbol);
        var bexBarsTask = CollectAsync(LeveragedSymbol);
        await Task.WhenAll(beBarsTask, bexBarsTask);
        var beBars = beBarsTask.Result;
        var bexBars = bexBarsTask.Result;

        var result = ComputeDecay(beBars, bexBars, lookbackDays);
        if (result is null)
        {
            logger.LogWarning(
                "BeBexDecayScanner: insufficient daily bars for a {Days}-day window (BE={BeCount}, BEX={BexCount}) — skipping this scan.",
                lookbackDays, beBars.Count, bexBars.Count);
            return;
        }

        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            computedUtc = DateTime.UtcNow,
            lookbackDays,
            beReturnPercent = Math.Round(result.BeReturnPercent, 3),
            expectedBexReturnPercent = Math.Round(result.ExpectedBexReturnPercent, 3),
            actualBexReturnPercent = Math.Round(result.ActualBexReturnPercent, 3),
            decayPercent = Math.Round(result.DecayPercent, 3),
        });
        await settingsRepo.SetAsync("decay.be-bex.latest", payload, ct);

        logger.LogInformation(
            "BeBexDecayScanner: {Days}d BE {BeReturn:+0.0;-0.0}% vs BEX expected {Expected:+0.0;-0.0}% actual {Actual:+0.0;-0.0}% — decay {Decay:+0.0;-0.0}%.",
            lookbackDays, result.BeReturnPercent, result.ExpectedBexReturnPercent, result.ActualBexReturnPercent, result.DecayPercent);
        await auditLogRepo.LogAsync("bebex-decay-scan",
            $"BE/BEX {lookbackDays}d decay signal: BE {result.BeReturnPercent:+0.0;-0.0}%, " +
            $"BEX expected {result.ExpectedBexReturnPercent:+0.0;-0.0}% vs actual {result.ActualBexReturnPercent:+0.0;-0.0}% " +
            $"— decay {result.DecayPercent:+0.0;-0.0}%.",
            ct: ct);

        await GateStrategiesAsync(result.DecayPercent, thresholdPercent, ct);
    }

    /// <summary>
    /// Arms/disarms every BE strategy row per the decay reading — never a user's
    /// OTHER manual pause, tracked via a per-strategy "did WE disarm this" flag
    /// so re-arming only ever undoes this scanner's own prior action.
    /// </summary>
    private async Task GateStrategiesAsync(double decayPercent, double thresholdPercent, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var candidates = await db.Strategies
            .Where(s => s.Symbol == UnderlyingSymbol && s.Author == DecayGatedAuthorTag)
            .ToListAsync(ct);
        var favorable = decayPercent >= thresholdPercent;

        foreach (var s in candidates)
        {
            try
            {
                if (favorable && s.IsActive)
                    await DisarmAsync(s, decayPercent, thresholdPercent, ct);
                else if (!favorable && !s.IsActive)
                    await RearmAsync(s, decayPercent, thresholdPercent, ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                // A transient failure gating ONE strategy (e.g. a DB hiccup)
                // must not abort gating for the rest of `candidates` — the old
                // unguarded version let an exception here propagate out of the
                // whole loop, and since lastDecayScanUtc is advanced by the
                // caller BEFORE this method even runs, a mid-loop abort meant
                // every strategy after the failing one went un-gated for a
                // full 24 hours with no way to retry sooner.
                logger.LogWarning(ex, "[{Title}] BE/BEX decay gate failed for this strategy — continuing with the rest.", s.Title);
            }
        }
    }

    private async Task DisarmAsync(Strategy s, double decayPercent, double thresholdPercent, CancellationToken ct)
    {
        var disarmedByUsKey = $"decay.be-bex.disarmed.{s.Id}";

        // Narrow (not eliminate) the TOCTOU window against a concurrent manual
        // pause: `s.IsActive` was read once, up in GateStrategiesAsync, and may
        // already be stale by now. Re-read the CURRENT value immediately before
        // mutating; if it's already inactive, someone else (a user, most
        // likely) turned it off in between — don't claim credit for that below.
        if (!await IsCurrentlyActiveAsync(s.Id, ct)) return;

        // Persist "we did this" BEFORE mutating, not after. The two writes go
        // through separate repositories/DbContexts, so they can't share one
        // transaction; if this method crashes between them, the ORDER matters:
        // flag-then-mutate means a crash before the mutation just means the
        // (idempotent) mutation retries clean on the next scan, because
        // s.IsActive is still unchanged and this method will be re-entered.
        // The old mutate-then-flag order had the opposite failure mode: a
        // crash after the mutation but before the flag write left the
        // strategy disarmed with no flag — and RearmAsync below refuses to
        // re-arm anything without that flag (treating it as a manual pause),
        // permanently stranding the strategy disarmed until a human noticed.
        await settingsRepo.SetAsync(disarmedByUsKey, "true", ct);

        var mutation = await strategyRepo.SetActiveAsync(s.Id, false, s.OwnerUserId, ct);
        if (mutation == StrategyMutation.PositionOpen)
        {
            await auditLogRepo.LogAsync("bebex-decay-gate",
                $"[{s.Title}] decay signal ({decayPercent:F1}% >= {thresholdPercent:F1}% threshold) favors disarming, " +
                "but the strategy is holding a position — leaving it exit-managed, will re-check next scan.",
                userId: s.OwnerUserId, ct: ct);
            return;
        }
        if (mutation != StrategyMutation.Ok) return; // NotFound/NotOwner — nothing to do
        await auditLogRepo.LogAsync("bebex-decay-gate",
            $"[{s.Title}] disarmed — BE/BEX decay signal {decayPercent:F1}% >= threshold {thresholdPercent:F1}%.",
            userId: s.OwnerUserId, ct: ct);
    }

    private async Task RearmAsync(Strategy s, double decayPercent, double thresholdPercent, CancellationToken ct)
    {
        var disarmedByUsKey = $"decay.be-bex.disarmed.{s.Id}";
        var disarmedByUs = await settingsRepo.GetBoolAsync(disarmedByUsKey, ct) ?? false;
        if (!disarmedByUs) return; // a manual user pause — this scanner never overrides it

        var mutation = await strategyRepo.SetActiveAsync(s.Id, true, s.OwnerUserId, ct);
        if (mutation != StrategyMutation.Ok) return;
        await settingsRepo.SetAsync(disarmedByUsKey, "false", ct);
        await auditLogRepo.LogAsync("bebex-decay-gate",
            $"[{s.Title}] re-armed — BE/BEX decay signal {decayPercent:F1}% back below threshold {thresholdPercent:F1}%.",
            userId: s.OwnerUserId, ct: ct);
    }

    /// <summary>Fresh (untracked) read of a strategy's current IsActive value — see DisarmAsync.</summary>
    private async Task<bool> IsCurrentlyActiveAsync(Guid strategyId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Strategies.AsNoTracking()
            .Where(x => x.Id == strategyId)
            .Select(x => x.IsActive)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// The pure decay computation — no feed dependency, so it's directly
    /// unit-testable against synthetic daily bars. Assumes both series are
    /// ordered oldest-to-newest (as every <see cref="IMarketDataFeed"/>
    /// implementation yields them) and returns null rather than throwing when
    /// there aren't enough bars to cover the lookback window.
    /// </summary>
    public static DecayResult? ComputeDecay(
        IReadOnlyList<Candle> beBars, IReadOnlyList<Candle> bexBars, int lookbackDays, double leverage = DefaultLeverage)
    {
        if (lookbackDays < 1) return null;

        // Align by calendar date rather than by raw position-from-end. BEX is a
        // thin, leveraged, daily-reset ETF — a halt, late-listing gap, or a
        // feed hiccup on just one of the two symbols shifts that symbol's bar
        // list relative to the other's, and pure index-from-end pairing would
        // then silently compare closes from two different days. Only dates
        // present in BOTH series are eligible for the lookback window.
        var beByDate = beBars.GroupBy(c => DateOnly.FromDateTime(c.StartUtc)).ToDictionary(g => g.Key, g => g.Last().Close);
        var bexByDate = bexBars.GroupBy(c => DateOnly.FromDateTime(c.StartUtc)).ToDictionary(g => g.Key, g => g.Last().Close);
        var commonDates = beByDate.Keys.Intersect(bexByDate.Keys).OrderBy(d => d).ToList();
        if (commonDates.Count < lookbackDays + 1) return null;

        var startDate = commonDates[^(lookbackDays + 1)];
        var endDate   = commonDates[^1];

        var beStart  = (double)beByDate[startDate];
        var beEnd    = (double)beByDate[endDate];
        var bexStart = (double)bexByDate[startDate];
        var bexEnd   = (double)bexByDate[endDate];
        if (beStart <= 0 || bexStart <= 0) return null;

        var beReturnPercent = (beEnd - beStart) / beStart * 100.0;
        var expectedBexReturnPercent = leverage * beReturnPercent;
        var actualBexReturnPercent = (bexEnd - bexStart) / bexStart * 100.0;
        var decayPercent = expectedBexReturnPercent - actualBexReturnPercent;

        return new DecayResult(beReturnPercent, expectedBexReturnPercent, actualBexReturnPercent, decayPercent);
    }
}
