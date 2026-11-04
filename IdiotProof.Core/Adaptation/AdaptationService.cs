using System.Text.Json;
using IdiotProof.Core.Data;
using IdiotProof.Core.Monitoring;
using IdiotProof.Core.Services;
using IdiotProof.Evidence.Adaptation;
using IdiotProof.Models;
using IdiotProof.Scripting;
using IdiotProof.Strategies.Backtesting;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Core.Adaptation;

/// <summary>The result of one adaptation pass over one strategy. Refusals are returned but not persisted.</summary>
public sealed record AdaptationResult(string Outcome, string Reason, AdaptationDecision? Decision = null)
{
    public bool Applied => Outcome == AdaptationService.Applied;
}

/// <summary>
/// Living strategies (IP-T373): while a PAPER strategy is armed and its owner opted in, re-measures
/// the ticker's own noise on today's closed bars, shadow-replays a grid of stop/trail variants of the
/// real strategy through the backtester, lets a Thompson bandit pick one, routes the result through the
/// Risk Guardian, and only then rewrites the strategy's stop/trail. Every decision past the information
/// gate is persisted with the script it replaced. Live strategies are refused outright.
/// </summary>
public sealed class AdaptationService(IDbContextFactory<AppDbContext> dbFactory, SettingsRepository settings, RiskGuardianService risk)
{
    public const string GlobalEnabledKey = "adaptation.enabled";
    public const string Applied = "applied";
    public const string Unchanged = "unchanged";
    public const string RiskBlocked = "risk-blocked";
    public const string Refused = "refused";

    private static readonly JsonSerializerOptions Json = new() { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals };

    public static string OptInKey(Guid strategyId) => $"adaptation.optIn.{strategyId:N}";

    public async Task<bool> IsGloballyEnabledAsync(CancellationToken ct = default) =>
        await settings.GetBoolAsync(GlobalEnabledKey, ct) ?? false;

    public async Task<bool> IsOptedInAsync(Guid strategyId, CancellationToken ct = default) =>
        await settings.GetBoolAsync(OptInKey(strategyId), ct) ?? false;

    public Task SetOptInAsync(Guid strategyId, bool on, CancellationToken ct = default) =>
        settings.SetAsync(OptInKey(strategyId), on ? "true" : "false", ct);

    public async Task<AdaptationResult> AdaptAsync(Guid strategyId, IReadOnlyList<Candle> todaysCandles,
        DateTime periodStartUtc, DateTime nowUtc, InformationGateOptions? gate = null, CancellationToken ct = default,
        Func<StrategyDefinition, CancellationToken, Task<BacktestLeaderContext?>>? leaders = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var strategy = await db.Strategies.FirstOrDefaultAsync(s => s.Id == strategyId, ct);
        if (strategy is null) return new(Refused, "strategy not found");

        if (!string.Equals(strategy.BrokerMode, "Paper", StringComparison.OrdinalIgnoreCase))
            return new(Refused, $"adaptation is paper-only; this strategy is {strategy.BrokerMode}");
        if (!strategy.IsActive) return new(Refused, "strategy is not armed");
        if (strategy.PositionQty != 0) return new(Refused, "strategy is holding a position; exits are never retuned mid-trade");
        if (!await IsOptedInAsync(strategy.Id, ct)) return new(Refused, "owner has not opted this strategy in");
        if (string.IsNullOrWhiteSpace(strategy.ScriptJson)) return new(Refused, "strategy has no ScriptJson");

        var check = InformationGate.Check(todaysCandles, periodStartUtc, nowUtc, gate);
        if (!check.Ready) return new(Refused, $"waiting for information: {check.Reason}");

        var closed = InformationGate.ClosedBars(todaysCandles.Where(c => c.StartUtc >= periodStartUtc), nowUtc);
        var baseDef = StrategyJson.Deserialize(strategy.ScriptJson);
        var derived = AdaptiveParameterDeriver.Derive(closed);
        var variants = AdaptiveParameterDeriver.VariantsAround(derived)
            .Select(v => v with { TakeProfitPercent = null })
            .DistinctBy(v => (v.StopLossPercent, v.TrailingStopPercent))
            .ToList();

        // Leader-gated strategies replay with the same prior-session leaders the Monitor would use;
        // without them LeaderConfirms/NotLeaderDiverging block every variant and the bandit learns nothing.
        BacktestLeaderContext? leaderContext = null;
        if (leaders is not null && LeaderContextCache.Uses(baseDef)) leaderContext = await leaders(baseDef, ct);

        var etDate = EasternTime.FromUtc(nowUtc).ToString("yyyy-MM-dd");
        int seed = HashCode.Combine(strategy.Id, etDate, closed.Count);
        var eval = ShadowEvaluator.Evaluate(closed, nowUtc, variants,
            (v, bars) => ShadowTrades(Adapted(baseDef, v), bars, leaderContext), seed);
        var chosen = eval.Chosen;

        var decision = new AdaptationDecision
        {
            StrategyId = strategy.Id,
            OwnerUserId = strategy.OwnerUserId,
            Symbol = strategy.Symbol,
            EtDate = etDate,
            DecidedUtc = nowUtc,
            ClosedBars = closed.Count,
            FeaturesJson = JsonSerializer.Serialize(derived, Json),
            VariantsJson = JsonSerializer.Serialize(eval.Scores, Json),
            ChosenVariant = chosen.Name,
            OriginalScriptJson = strategy.ScriptJson,
        };

        if (baseDef.StopLossPercent == chosen.StopLossPercent && baseDef.TrailingStopPercent == chosen.TrailingStopPercent
            && baseDef.StopLossPrice is null)
        {
            decision.Outcome = Unchanged;
            decision.Reason = "the strategy already uses the chosen parameters";
            return await SaveAsync(db, decision, ct);
        }

        var adapted = Adapted(baseDef, chosen);
        var guardian = await risk.GetForUserAsync(strategy.OwnerUserId, ct);
        var verdict = guardian.ValidateTrade(SetupFor(adapted, closed[^1].Close));
        decision.RiskSummary = Truncate(verdict.Summary, 400);
        if (!verdict.IsApproved)
        {
            decision.Outcome = RiskBlocked;
            decision.Reason = Truncate(string.Join("; ", verdict.BlockReasons), 400);
            return await SaveAsync(db, decision, ct);
        }

        decision.Outcome = Applied;
        decision.AppliedOverrides = Truncate(OverridesFor(chosen).ToScript(), 400);
        decision.Reason = $"P(best)={eval.Scores[eval.ChosenIndex].ProbabilityBest:0.00} over {variants.Count} variants on {closed.Count} closed bars";
        strategy.ScriptJson = StrategyJson.Serialize(adapted);
        strategy.ScriptText = adapted.ToScript();
        strategy.UpdatedUtc = nowUtc;
        return await SaveAsync(db, decision, ct);
    }

    /// <summary>Joins closed TradeDiary rows entered after each decision onto it (the realized-P&amp;L label for S4).</summary>
    public async Task<int> RecordOutcomesAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var pending = await db.AdaptationDecisions
            .Where(d => d.Outcome == Applied && d.OutcomeRecordedUtc == null).ToListAsync(ct);
        int updated = 0;
        foreach (var d in pending)
        {
            var dayEnd = d.DecidedUtc.Date.AddDays(1).AddHours(12);
            var trades = await db.TradeDiary
                .Where(t => t.StrategyId == d.StrategyId && t.EntryUtc >= d.DecidedUtc && t.EntryUtc < dayEnd && t.ExitUtc != null)
                .ToListAsync(ct);
            bool anyOpen = await db.TradeDiary.AnyAsync(t => t.StrategyId == d.StrategyId && t.EntryUtc >= d.DecidedUtc && t.ExitUtc == null, ct);
            if (trades.Count == 0 || anyOpen) continue;
            d.RealizedTrades = trades.Count;
            d.RealizedPnL = trades.Sum(t => t.RealizedPnL ?? 0);
            d.RealizedReturnPercent = trades.Average(t => t.ReturnPercent ?? 0);
            d.OutcomeRecordedUtc = DateTime.UtcNow;
            updated++;
        }
        await db.SaveChangesAsync(ct);
        return updated;
    }

    private static async Task<AdaptationResult> SaveAsync(AppDbContext db, AdaptationDecision decision, CancellationToken ct)
    {
        db.AdaptationDecisions.Add(decision);
        await db.SaveChangesAsync(ct);
        return new(decision.Outcome, decision.Reason ?? "", decision);
    }

    internal static StrategyDefinition Adapted(StrategyDefinition baseDef, ParameterVariant v)
    {
        var copy = StrategyJson.Deserialize(StrategyJson.Serialize(baseDef));
        copy.StopLossPrice = null;
        copy.TrailingStopPercent = null;
        OverridesFor(v).ApplyTo(copy);
        return copy;
    }

    private static StrategyOverrides OverridesFor(ParameterVariant v) => new()
    {
        StopLossPercent = v.StopLossPercent,
        TrailingStopPercent = v.TrailingStopPercent,
    };

    /// <summary>The real strategy replayed on the closed bars; a trade the backtester closed only because the data ended is unresolved and dropped.</summary>
    internal static IEnumerable<ShadowTrade> ShadowTrades(StrategyDefinition def, IReadOnlyList<Candle> closed, BacktestLeaderContext? leaders = null)
    {
        var report = StrategyBacktester.Run(def, closed, new BacktestOptions { Leaders = leaders });
        foreach (var t in report.Trades)
        {
            if (t.ExitUtc is not { } exit || t.ExitReason == "End of session") continue;
            yield return new ShadowTrade(t.EntryUtc, exit, (double)t.ReturnPercent);
        }
    }

    private static TradeSetup SetupFor(StrategyDefinition def, decimal entry)
    {
        bool isLong = def.Direction != TradeDirection.Short;
        decimal stopPct = (decimal)(def.StopLossPercent ?? 0) / 100m;
        int qty = def.IsNotional && entry > 0 ? (int)Math.Floor(def.NotionalAmount!.Value / entry) : def.Quantity;
        return new TradeSetup
        {
            Symbol = def.Symbol,
            Direction = def.Direction,
            EntryPrice = entry,
            StopLoss = isLong ? entry * (1 - stopPct) : entry * (1 + stopPct),
            Quantity = Math.Max(1, qty),
        };
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
