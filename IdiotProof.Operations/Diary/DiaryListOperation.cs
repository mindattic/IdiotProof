using IdiotProof.Operations.Rendering;

namespace IdiotProof.Operations.Diary;

public sealed record DiaryListArgs : DiaryFilterArgs
{
    [Option("status", Description = "Only this lifecycle state: open, closed, notfilled or orphaned.")]
    public string? Status { get; init; }

    [Option("limit", Description = "Most trades to show, newest entry first, 1-5000 (default 50).")]
    public int Limit { get; init; } = 50;
}

/// <summary>
/// The trade diary: every trade the Monitor (or a manual flatten) opened for you, entry to exit,
/// with mode, size, prices, exit reason and realized P&amp;L. Newest entry first; times in ET.
/// </summary>
[Operation("diary list", Summary = "Your trade diary, newest first: entry/exit, mode, size, exit reason and realized P&L.", Risk = OperationRisk.Read)]
[Example("idiotproof diary list")]
[Example("idiotproof diary list --since 2026-10-01 --mode paper --symbol NVDA")]
[Example("idiotproof diary list --status open")]
public sealed class DiaryListOperation : IOperation<DiaryListArgs>
{
    public async Task<OperationResult> ExecuteAsync(DiaryListArgs args, OperationContext ctx)
    {
        if (args.Limit is < 1 or > 5000) return OperationResult.Usage("--limit must be 1-5000.");
        string? status = null;
        if (!string.IsNullOrWhiteSpace(args.Status))
        {
            status = new[] { "Open", "Closed", "NotFilled", "Orphaned" }
                .FirstOrDefault(s => s.Equals(args.Status.Replace("-", "").Trim(), StringComparison.OrdinalIgnoreCase));
            if (status is null) return OperationResult.Usage("--status is open, closed, notfilled or orphaned.");
        }

        var trades = await DiarySupport.LoadAsync(ctx, args, status, args.Limit);
        var rows = trades.Select(DiarySupport.ToRow).ToList();
        var head = $"Trade diary ({DiarySupport.Describe(args)}{(status is null ? "" : $", {status.ToLowerInvariant()}")})";
        var data = new { Trades = rows };
        if (rows.Count == 0) return OperationResult.Ok(data, $"{head}\nNo trades match.");

        var table = new TextTable { MaxWidth = ctx.Width }
            .Column("id").Column("entry (ET)").Column("sym").Column("strategy", maxWidth: 24).Column("mode")
            .Column("qty", Align.Right).Column("entry", Align.Right).Column("exit", Align.Right).Column("held", Align.Right)
            .Column("reason").Column("P&L", Align.Right).Column("ret", Align.Right).Column("status");
        foreach (var r in rows)
            table.Row(r.Short, Fmt.Et(r.EntryUtc), r.Symbol, r.Strategy, r.Mode, r.Quantity.ToString(),
                Fmt.Money(r.EntryPrice), r.ExitPrice is { } x ? Fmt.Money(x) : "", DiarySupport.Hold(r.HoldMinutes),
                r.ExitReason ?? "", r.RealizedPnL is { } p ? Fmt.SignedMoney(p) : "",
                r.ReturnPercent is { } pct ? Fmt.PercentPoints(pct, 2, signed: true) : "", r.Status.ToLowerInvariant());

        var closed = rows.Where(r => r.Status == "Closed" && r.RealizedPnL is not null).ToList();
        var total = closed.Sum(r => r.RealizedPnL!.Value);
        var foot = $"{rows.Count} trades ({rows.Count(r => r.Status == "Open")} open, {closed.Count} closed); realized {Fmt.SignedMoney(total)}.";
        return OperationResult.Ok(data, $"{head}\n{table.Render()}\n{foot}");
    }
}
