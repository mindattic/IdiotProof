using IdiotProof.Blazor.Data;
using IdiotProof.Operations.Rendering;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Operations.Replay;

public sealed record ReplayCompareArgs
{
    [Argument(0, Name = "run-a", Description = "First run id or prefix.")]
    public string RunA { get; init; } = "";

    [Argument(1, Name = "run-b", Description = "Second run id or prefix.")]
    public string RunB { get; init; } = "";
}

/// <summary>Two replay runs side by side: what each traded, when it fired, its round-trips and P&amp;L, and the difference.</summary>
[Operation("replay compare", Summary = "Two replay runs side by side: strategy, day, fires, round-trips, win rate, hold time and P&L, with the difference.", Risk = OperationRisk.Read)]
[Example("idiotproof replay compare 0190a3f2 0190b7c1")]
public sealed class ReplayCompareOperation : IOperation<ReplayCompareArgs>
{
    public async Task<OperationResult> ExecuteAsync(ReplayCompareArgs args, OperationContext ctx)
    {
        var a = await ReplayKit.FindRunAsync(ctx, args.RunA);
        var b = await ReplayKit.FindRunAsync(ctx, args.RunB);
        if (a.Id == b.Id) return OperationResult.Usage("Those are the same run.");

        List<ReplayTrade> ta, tb;
        await using (var db = await ctx.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync(ctx.CancellationToken))
        {
            ta = await db.ReplayTrades.AsNoTracking().Where(t => t.ReplayRunId == a.Id).ToListAsync(ctx.CancellationToken);
            tb = await db.ReplayTrades.AsNoTracking().Where(t => t.ReplayRunId == b.Id).ToListAsync(ctx.CancellationToken);
        }
        var sa = Stats(a, ta);
        var sb = Stats(b, tb);

        var table = new TextTable { MaxWidth = ctx.Width }.Column("").Column("A", maxWidth: 40).Column("B", maxWidth: 40).Column("B - A", Align.Right);
        table.Row("run", ReplayKit.ShortId(a.Id), ReplayKit.ShortId(b.Id), "")
             .Row("ticker / day", $"{a.Symbol} {a.DateEt}", $"{b.Symbol} {b.DateEt}", "")
             .Row("strategy", a.Strategy, b.Strategy, "")
             .Row("kind / feed", $"{a.Kind} / {a.Feed}", $"{b.Kind} / {b.Feed}", "")
             .Row("first fire", a.FirstFireEt ?? "—", b.FirstFireEt ?? "—", "")
             .Row("round-trips", a.PayoffCount.ToString(), b.PayoffCount.ToString(), (b.PayoffCount - a.PayoffCount).ToString("+0;-0;0"))
             .Row("win rate", Pct(sa.WinRate), Pct(sb.WinRate), sa.WinRate is { } wa && sb.WinRate is { } wb ? Fmt.PercentPoints((decimal)((wb - wa) * 100), 0, signed: true) : "")
             .Row("avg hold", sa.AvgHold is { } ha ? $"{ha:0}m" : "—", sb.AvgHold is { } hb ? $"{hb:0}m" : "—", "")
             .Row("best / worst", BestWorst(sa), BestWorst(sb), "")
             .Row("total P&L", Fmt.PercentPoints((decimal)a.TotalPnl, 2, signed: true), Fmt.PercentPoints((decimal)b.TotalPnl, 2, signed: true),
                  Fmt.PercentPoints((decimal)(b.TotalPnl - a.TotalPnl), 2, signed: true));

        var verdict = a.TotalPnl == b.TotalPnl ? "Same total P&L."
            : $"{(b.TotalPnl > a.TotalPnl ? "B" : "A")} did better by {Fmt.PercentPoints((decimal)Math.Abs(b.TotalPnl - a.TotalPnl), 2)}.";
        return OperationResult.Ok(new
        {
            A = new { Run = ReplayKit.RunData(a), sa.WinRate, sa.AvgHold, sa.Best, sa.Worst },
            B = new { Run = ReplayKit.RunData(b), sb.WinRate, sb.AvgHold, sb.Best, sb.Worst },
            PnlDifference = Math.Round(b.TotalPnl - a.TotalPnl, 2),
        }, table.Render() + "\n" + verdict);
    }

    private sealed record RunStats(double? WinRate, double? AvgHold, double? Best, double? Worst);

    private static RunStats Stats(ReplayRun r, List<ReplayTrade> t) => t.Count == 0
        ? new RunStats(null, null, null, null)
        : new RunStats(t.Count(x => x.Won) / (double)t.Count, t.Average(x => x.HoldMin), t.Max(x => x.PnlPct), t.Min(x => x.PnlPct));

    private static string Pct(double? fraction) => fraction is { } f ? Fmt.Percent(f, 0) : "—";

    private static string BestWorst(RunStats s) => s.Best is { } b && s.Worst is { } w
        ? $"{Fmt.PercentPoints((decimal)b, 2, signed: true)} / {Fmt.PercentPoints((decimal)w, 2, signed: true)}" : "—";
}
