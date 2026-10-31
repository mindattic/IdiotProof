using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IdiotProof.Core.Data;
using IdiotProof.Core.Trading;
using IdiotProof.DataFeeds;
using IdiotProof.Models;
using IdiotProof.Operations.Rendering;
using IdiotProof.Operations.Strategies;
using IdiotProof.Scripting;
using IdiotProof.Strategies;
using Microsoft.Extensions.DependencyInjection;

using IdiotProof.Core.Services;
namespace IdiotProof.Operations.Monitor;

public sealed record MonitorPauseArgs
{
    [Option("reason", Description = "Why (kept with the flag and shown by `monitor status`).")]
    public string? Reason { get; init; }
}

[Operation("monitor pause", Summary = "Pause the Monitor: from its next tick it evaluates nothing - no entries and NO exits - until resumed.", Risk = OperationRisk.Write,
    Description = "Sets the SettingsKv flag monitor.paused, honored at the top of every tick (within 2 s). The loop, heartbeat and leader lease stay up. Open positions are not managed while paused - flatten them first if that matters.")]
[Example("idiotproof monitor pause")]
[Example("idiotproof monitor pause --reason \"news pending\"")]
public sealed class MonitorPauseOperation : IOperation<MonitorPauseArgs>
{
    public async Task<OperationResult> ExecuteAsync(MonitorPauseArgs args, OperationContext ctx)
    {
        var flag = MonitorReads.PauseFlag(ctx);
        var (already, _) = await flag.ReadAsync(ctx.CancellationToken);
        var reason = string.IsNullOrWhiteSpace(args.Reason)
            ? $"paused via {ctx.Caller.ToString().ToLowerInvariant()} at {Fmt.Et(ctx.UtcNow)}"
            : $"{args.Reason.Trim()} ({ctx.Caller.ToString().ToLowerInvariant()}, {Fmt.Et(ctx.UtcNow)})";
        await flag.SetAsync(true, reason, ctx.CancellationToken);

        // The pause is global, so every unmanaged position is counted, but only the caller's are named.
        var allHolding = (await ctx.Get<StrategyRepository>().GetActiveAsync(ctx.CancellationToken)).Where(s => s.PositionQty != 0).ToList();
        var holding = allHolding.Where(s => s.OwnerUserId == ctx.OwnerId).ToList();
        var othersHolding = allHolding.Count - holding.Count;
        var text = new StringBuilder(already ? "The Monitor was already paused; reason updated." : "Monitor paused - it skips every tick from its next one (within 2 s).");
        if (allHolding.Count > 0)
        {
            text.Append($"\nWARNING: {allHolding.Count} open position(s) are NOT managed while paused (no stops, no targets, no sell-by):");
            foreach (var s in holding) text.Append($"\n  {s.Symbol,-6} {s.PositionQty,6} sh  {s.Title}");
            if (othersHolding > 0) text.Append($"\n  ...and {othersHolding} belonging to other users.");
            text.Append("\nResume with `idiotproof monitor resume`, or flatten with `idiotproof trade flatten`.");
        }
        else text.Append("\nNo open positions. Resume with `idiotproof monitor resume`.");

        return OperationResult.Ok(new { Paused = true, WasPaused = already, Reason = reason, OpenPositions = holding.Select(s => new { s.Id, s.Symbol, s.PositionQty }).ToList() }, text.ToString());
    }
}

[Operation("monitor resume", Summary = "Resume a paused Monitor (needs a confirmation code when a Live strategy is active).", Risk = OperationRisk.Money,
    Description = "Clears the SettingsKv flag monitor.paused; the Monitor evaluates again from its next tick. When any active strategy routes Live the first call returns a code: resuming means real-money entries.")]
[Example("idiotproof monitor resume")]
[Example("idiotproof monitor resume --confirm K7Q2M9")]
public sealed class MonitorResumeOperation : IOperation<NoArgs>
{
    public async Task<OperationResult> ExecuteAsync(NoArgs args, OperationContext ctx)
    {
        var flag = MonitorReads.PauseFlag(ctx);
        var (paused, _) = await flag.ReadAsync(ctx.CancellationToken);
        if (!paused) return OperationResult.Ok(new { Paused = false, WasPaused = false }, "The Monitor is not paused.");

        var active = await ctx.Get<StrategyRepository>().GetActiveAsync(ctx.CancellationToken);
        var live = active.Where(s => OrderIntent.ParseMode(s.BrokerMode) == BrokerMode.Live).ToList();
        if (live.Count > 0)
        {
            var gate = await LiveResumeGateAsync(ctx, live);
            if (gate is not null) return gate;
        }

        await flag.SetAsync(false, $"resumed via {ctx.Caller.ToString().ToLowerInvariant()} at {Fmt.Et(ctx.UtcNow)}", ctx.CancellationToken);
        return OperationResult.Ok(new { Paused = false, WasPaused = true, ActiveStrategies = active.Count, LiveStrategies = live.Count },
            $"Monitor resumed - it evaluates {active.Count} active strateg{(active.Count == 1 ? "y" : "ies")} from its next tick" +
            (live.Count > 0 ? $" ({live.Count} Live)." : "."));
    }

    private static async Task<OperationResult?> LiveResumeGateAsync(OperationContext ctx, IReadOnlyList<Strategy> live)
    {
        var owner = ctx.RequireOwner();
        var payload = JsonSerializer.Serialize(new { action = "monitor-resume", live = live.Select(s => s.Id).OrderBy(i => i) });
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        var store = ctx.Get<ConfirmationStore>();
        if (string.IsNullOrWhiteSpace(ctx.ConfirmationCode))
        {
            var preview = $"RESUME the Monitor with {live.Count} LIVE strateg{(live.Count == 1 ? "y" : "ies")} active ({string.Join(", ", live.Select(s => s.Symbol).Distinct())}) - real-money entries from its next tick.";
            var pending = await store.IssueAsync(owner, ConfirmationStore.KindActivateLive, payload, hash, preview, ctx.CancellationToken);
            return OperationResult.NeedsConfirmation(pending.Code, preview, pending.ExpiresUtc, new { action = "monitor-resume", live = live.Count });
        }
        var redemption = await store.RedeemAsync(ctx.ConfirmationCode, owner, ctx.Caller.ToString().ToLowerInvariant(),
            p => p.Kind == ConfirmationStore.KindActivateLive && p.PayloadJson == payload ? hash : "mismatch", ctx.CancellationToken);
        return redemption.Succeeded ? null : OperationResult.Blocked(redemption.Message, new { failure = redemption.Failure.ToString() });
    }
}

public sealed record MonitorTickOnceArgs
{
    [Option("dry", Description = "Write nothing at all (by default the pass is recorded in the audit log as monitor-tick-once).")]
    public bool Dry { get; init; }

    [Option("symbol", Description = "Only strategies on this symbol.")]
    public string? Symbol { get; init; }
}

[Operation("monitor tick-once", Summary = "Run one evaluation pass over your active strategies and show each verdict - never places, cancels or records an order.", Risk = OperationRisk.Read,
    Description = "Gate 1 only (entry conditions against fresh bars from the Hub's feed); the LLM panel, RiskGuardian and the broker are never called, and no position, progress row or live bar is written. Without --dry the pass is noted in the audit log.")]
[Example("idiotproof monitor tick-once --dry")]
[Example("idiotproof monitor tick-once --symbol NVDA")]
public sealed class MonitorTickOnceOperation : IOperation<MonitorTickOnceArgs>
{
    /// <summary>Same window the Monitor keeps per symbol (4 hours of minute bars).</summary>
    private const int CandleWindow = 240;

    public async Task<OperationResult> ExecuteAsync(MonitorTickOnceArgs args, OperationContext ctx)
    {
        var owner = ctx.RequireOwner();
        var now = ctx.UtcNow;
        var feed = ctx.Get<IMarketDataFeed>();
        var strategies = (await ctx.Get<StrategyRepository>().GetActiveAsync(ctx.CancellationToken))
            .Where(s => s.OwnerUserId == owner)
            .Where(s => args.Symbol is null || s.Symbol.Equals(args.Symbol, StringComparison.OrdinalIgnoreCase))
            .OrderBy(s => s.Symbol).ThenBy(s => s.Title)
            .ToList();
        if (strategies.Count == 0)
            return OperationResult.Ok(new { Verdicts = Array.Empty<object>() }, "No active strategies to evaluate" + (args.Symbol is null ? "." : $" on {args.Symbol.ToUpperInvariant()}."));

        var window = TradingSchedule.Classify(now);
        var verdicts = new List<TickVerdict>();
        foreach (var group in strategies.GroupBy(s => s.Symbol.ToUpperInvariant()))
        {
            IReadOnlyList<Candle> candles;
            decimal? previousClose;
            try
            {
                var list = new List<Candle>();
                await foreach (var c in feed.GetHistoricalCandlesAsync(group.Key, now - TimeSpan.FromMinutes(CandleWindow), now, TimeSpan.FromMinutes(1), ctx.CancellationToken))
                    list.Add(c);
                candles = list;
                previousClose = await feed.GetPreviousCloseAsync(group.Key, now, ctx.CancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                verdicts.AddRange(group.Select(s => new TickVerdict(s, "no-data", $"market data unavailable: {ex.Message}", 0, 0, null)));
                continue;
            }
            foreach (var s in group) verdicts.Add(Evaluate(s, candles, previousClose, now));
        }

        if (!args.Dry)
        {
            try
            {
                var summary = string.Join("; ", verdicts.Select(v => $"{v.Strategy.Symbol} {v.Verdict}"));
                await ctx.Get<AuditLogRepository>().LogAsync("monitor-tick-once",
                    $"tick-once ({ctx.Caller.ToString().ToLowerInvariant()}): {summary}", owner, ct: ctx.CancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { /* the audit note is best-effort */ }
        }

        var table = new TextTable { MaxWidth = ctx.Width }
            .Column("sym").Column("title", maxWidth: 28).Column("verdict").Column("cond", Align.Right).Column("price", Align.Right).Column("detail", maxWidth: 48);
        foreach (var v in verdicts)
            table.Row(v.Strategy.Symbol, v.Strategy.Title, v.Verdict, v.Total > 0 ? $"{v.Passed}/{v.Total}" : "", v.Price is { } p ? Fmt.Money(p) : "", v.Detail);

        var fires = verdicts.Count(v => v.Verdict == "would-fire");
        var text = $"One pass at {Fmt.Et(now)} ({window}) - {verdicts.Count} strateg{(verdicts.Count == 1 ? "y" : "ies")}, {fires} would fire. Nothing was ordered{(args.Dry ? " or written" : "")}.\n" + table.Render();
        return OperationResult.Ok(new
        {
            EvaluatedUtc = now,
            Window = window.ToString(),
            args.Dry,
            Verdicts = verdicts.Select(v => new
            {
                v.Strategy.Id,
                Short = StrategyRefs.ShortGuid(v.Strategy.Id),
                v.Strategy.Symbol,
                v.Strategy.Title,
                v.Verdict,
                v.Passed,
                v.Total,
                v.Price,
                v.Detail,
            }).ToList(),
        }, text);
    }

    /// <summary>One strategy's verdict: <c>would-fire</c>, <c>waiting</c>, <c>holding</c>, <c>outside-session</c>, <c>done-today</c>, <c>quarantined</c>, <c>no-data</c>.</summary>
    internal sealed record TickVerdict(Strategy Strategy, string Verdict, string Detail, int Passed, int Total, decimal? Price);

    /// <summary>Gate 1 exactly as MonitorWorker walks it, with every side effect removed.</summary>
    internal static TickVerdict Evaluate(Strategy s, IReadOnlyList<Candle> candles, decimal? previousClose, DateTime nowUtc)
    {
        var loaded = StrategyLoader.Load(s.ScriptJson, s.ScriptText);
        if (loaded.CanonicalError is { } error) return new(s, "quarantined", error, 0, 0, null);
        if (loaded.Definition is not { } def) return new(s, "quarantined", "unparseable script", 0, 0, null);
        if (s.PositionQty != 0)
            return new(s, "holding", $"{s.PositionQty} sh - the Monitor manages the exit", 0, 0, candles.Count > 0 ? candles[^1].Close : null);
        if (!MarketTime.IsInsideSession(def.Session, nowUtc))
            return new(s, "outside-session", $"outside {def.Session}", 0, Math.Max(1, def.EntryConditions.Count), null);
        if (!def.ShouldRepeat && s.EntryFilledUtc is { } filled
            && DateOnly.FromDateTime(EasternClock.ToEastern(DateTime.SpecifyKind(filled, DateTimeKind.Utc))) == DateOnly.FromDateTime(EasternClock.ToEastern(nowUtc)))
            return new(s, "done-today", "already traded today (no Repeat)", 0, Math.Max(1, def.EntryConditions.Count), null);
        if (candles.Count == 0) return new(s, "no-data", "no bars in the last 4 hours", 0, def.EntryConditions.Count, null);

        var snapshot = IndicatorSnapshotBuilder.BuildWithEmas(s.Symbol, candles, EmaPeriodCollector.Collect(def), previousClose);
        var resolved = StrategyBranchResolver.Resolve(def, snapshot);
        var conditions = resolved.EntryConditions;
        var price = (decimal)snapshot.Price;
        if (conditions.Count == 0) return new(s, "would-fire", "setup-only strategy (no conditions)", 0, 0, price);

        var bits = conditions.Select(c => c.Evaluate(snapshot)).ToArray();
        var firstFalse = Array.IndexOf(bits, false);
        var passed = firstFalse >= 0 ? firstFalse : bits.Length;
        return firstFalse < 0
            ? new(s, "would-fire", "all conditions met (LLM + RiskGuardian gates not run)", passed, conditions.Count, price)
            : new(s, "waiting", conditions[firstFalse].ToScript(), passed, conditions.Count, price);
    }
}
