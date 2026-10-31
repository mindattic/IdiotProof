using IdiotProof.Blazor.Data;
using IdiotProof.Operations.Rendering;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Operations.Strategies;

/// <summary>One thing that happened to a strategy: when, where it was recorded, what kind, and what.</summary>
public sealed record StrategyEvent(DateTime Utc, string Source, string Kind, string Text);

/// <summary>
/// Gathers a strategy's trail from what IdiotProof already records: the Monitor's audit rows
/// (they begin <c>[Title] SYMBOL</c>), the trade diary (by strategy id), the command ledger
/// (strategy commands whose arguments name it) and the row's own created/updated stamps.
/// </summary>
public static class StrategyTrail
{
    /// <summary>Monitor audit categories that are gate verdicts: why it did or didn't fire.</summary>
    public static readonly string[] FireCategories =
    [
        "signal-fire", "signal-vetoed", "signal-blocked", "entry", "order-rejected", "order-placement-exception",
    ];

    /// <summary>The owner's audit rows about this strategy, newest first.</summary>
    public static async Task<List<AuditLog>> AuditAsync(OperationContext ctx, Strategy s, string[]? categories, int take)
    {
        var owner = ctx.RequireOwner();
        await using var db = await ctx.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync(ctx.CancellationToken);
        // The Monitor writes "[Title] ..." for the title the strategy had at the time; match the current
        // title and, for a soft-deleted strategy, the title it had before it was deleted.
        var titles = new[] { s.Title, StrategyArchive.Unmark(s.Title) }.Distinct().Select(t => $"[{t}]").ToArray();
        var query = db.AuditLogs.AsNoTracking().Where(a => a.UserId == owner);
        if (categories is not null) query = query.Where(a => categories.Contains(a.Category));
        var candidates = await query.Where(a => a.Message.Contains(s.Symbol))
            .OrderByDescending(a => a.TimestampUtc).Take(Math.Max(take * 5, 500)).ToListAsync(ctx.CancellationToken);
        return candidates.Where(a => titles.Any(t => a.Message.StartsWith(t, StringComparison.Ordinal))).Take(take).ToList();
    }

    /// <summary>Everything known about the strategy, newest first.</summary>
    public static async Task<List<StrategyEvent>> GatherAsync(OperationContext ctx, Strategy s, int take)
    {
        var owner = ctx.RequireOwner();
        var events = new List<StrategyEvent>
        {
            new(s.CreatedUtc, "strategy", "created", $"created by {s.Author ?? "unknown"}"),
        };
        if (s.UpdatedUtc > s.CreatedUtc.AddSeconds(1))
            events.Add(new(s.UpdatedUtc, "strategy", "updated", "last change to the row (settings, state or bookkeeping)"));

        events.AddRange((await AuditAsync(ctx, s, null, take)).Select(a => new StrategyEvent(a.TimestampUtc, "monitor", a.Category, a.Message)));

        await using var db = await ctx.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync(ctx.CancellationToken);
        var trades = await db.TradeDiary.AsNoTracking().Where(t => t.StrategyId == s.Id && t.OwnerUserId == owner)
            .OrderByDescending(t => t.EntryUtc).Take(take).ToListAsync(ctx.CancellationToken);
        foreach (var t in trades)
        {
            events.Add(new(t.EntryUtc, "diary", "trade-entry",
                $"{t.Direction} {t.Quantity} {t.Symbol} @ {Fmt.Money(t.EntryPrice)} ({(t.IsPaper ? "paper" : t.Broker)}) — {t.Status}"));
            if (t.ExitUtc is { } exit)
                events.Add(new(exit, "diary", "trade-exit",
                    $"exit @ {(t.ExitPrice is { } px ? Fmt.Money(px) : "?")} — {t.ExitReason ?? "?"}" +
                    (t.RealizedPnL is { } pnl ? $", P&L {Fmt.SignedMoney(pnl)}" : "")));
        }

        var names = new[] { s.Id.ToString(), s.Id.ToString("N")[..8], StrategyRefs.StampId(s), s.Symbol };
        var ledger = await db.CommandLedger.AsNoTracking()
            .Where(l => l.OwnerUserId == owner && l.Operation.StartsWith("strategy ") && l.ArgsJson != null)
            .OrderByDescending(l => l.TimestampUtc).Take(500).ToListAsync(ctx.CancellationToken);
        events.AddRange(ledger
            .Where(l => names.Any(n => l.ArgsJson!.Contains(n, StringComparison.OrdinalIgnoreCase)))
            .Take(take)
            .Select(l => new StrategyEvent(l.TimestampUtc, l.Caller.ToLowerInvariant(), l.Operation, $"{l.Status}{(l.Message is null ? "" : " — " + l.Message)}")));

        return events.OrderByDescending(e => e.Utc).Take(take).ToList();
    }
}

public sealed record StrategyHistoryArgs
{
    [Argument(0, Name = "strategy", Description = "Id, id prefix, stamp id or symbol.")]
    public string Strategy { get; init; } = "";

    [Option("limit", Description = "How many events to show (default 40, max 500).", Short = 'n')]
    public int Limit { get; init; } = 40;
}

[Operation("strategy history", Summary = "A strategy's trail, newest first: Monitor verdicts and fills, diary trades, and the CLI/MCP commands that touched it.", Risk = OperationRisk.Read)]
[Example("idiotproof strategy history NVDA")]
[Example("idiotproof strategy history NVDA --limit 100")]
public sealed class StrategyHistoryOperation : IOperation<StrategyHistoryArgs>
{
    public async Task<OperationResult> ExecuteAsync(StrategyHistoryArgs args, OperationContext ctx)
    {
        if (args.Limit is < 1 or > 500) return OperationResult.Usage("--limit is 1 to 500.");
        var s = await StrategyRefs.ResolveAsync(ctx, args.Strategy);
        var events = await StrategyTrail.GatherAsync(ctx, s, args.Limit);

        var table = new TextTable { MaxWidth = ctx.Width }.Column("when (ET)").Column("source").Column("event").Column("detail", maxWidth: 90);
        foreach (var e in events) table.Row(Fmt.Et(e.Utc), e.Source, e.Kind, e.Text);
        return OperationResult.Ok(new { s.Id, s.Symbol, s.Title, Events = events },
            $"{s.Symbol} '{s.Title}' ({StrategyRefs.ShortGuid(s.Id)})\n\n" + table.Render() + $"\n{events.Count} event(s).");
    }
}
