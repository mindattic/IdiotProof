using IdiotProof.Blazor.Data;
using IdiotProof.Blazor.Services;
using IdiotProof.Monitor;
using IdiotProof.Operations.Rendering;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Operations.Replay;

public sealed record ReplayLiveArgs
{
    [Argument(0, Name = "symbol", Required = false, Description = "Only this ticker (default every ticker the Monitor traded that day).")]
    public string? Symbol { get; init; }

    [Option("date", Description = "The ET day whose real trades to render (default today ET).", Short = 'd')]
    public DateOnly? Date { get; init; }

    [Option("pages", Description = "Render the run pages and archive indexes (HTML only; publishing stays manual).")]
    public bool Pages { get; init; } = true;

    [Option("out", Description = "Archive folder for the pages (default the mindattic.com replays folder).")]
    public string? Out { get; init; }
}

/// <summary>
/// The live replay (the old <c>replay-live</c>): what the Monitor ACTUALLY did — the trade diary's
/// real fills, prices, reasons and dollar P&amp;L — drawn on the same day's bars as a LIVE run in
/// the archive. Re-running replaces that day's live run for each (symbol, strategy).
/// </summary>
[Operation("replay live", Summary = "Render the Monitor's real trades for a day (from the trade diary) on that day's bars as LIVE runs in the archive.", Risk = OperationRisk.Write)]
[Example("idiotproof replay live")]
[Example("idiotproof replay live NVDA --date 2026-10-29")]
public sealed class ReplayLiveOperation : IOperation<ReplayLiveArgs>
{
    public async Task<OperationResult> ExecuteAsync(ReplayLiveArgs args, OperationContext ctx)
    {
        var owner = ctx.RequireOwner();
        var symbol = string.IsNullOrWhiteSpace(args.Symbol) ? null : ReplayKit.Symbol(args.Symbol);
        var day = args.Date ?? DateOnly.FromDateTime(EasternClock.ToEastern(ctx.UtcNow));
        var feed = ReplayKit.RequireRealFeed(ctx);
        var outRoot = args.Pages ? ReplayKit.ArchiveRoot(args.Out) : null;

        var r = await StrategyReplay.ReplayLiveAsync(ctx.Get<IDbContextFactory<AppDbContext>>(), ctx.Get<StrategyRepository>(),
            feed, ReplayKit.FeedLabel(feed), owner, day, symbol, outRoot, ctx.UtcNow, ctx.CancellationToken);

        var data = new { Day = day.ToString("yyyy-MM-dd"), r.Trades, Runs = r.Runs.Select(ReplayKit.RunData).ToList(), r.Skipped, Archive = outRoot };
        if (r.Trades == 0)
            return OperationResult.Ok(data, $"No live trades in your diary for {day:yyyy-MM-dd}{(symbol is null ? "" : " / " + symbol)} — nothing to render.");

        var table = new TextTable { MaxWidth = ctx.Width }
            .Column("run").Column("sym").Column("strategy", maxWidth: 36).Column("trades", Align.Right).Column("first ET").Column("P&L", Align.Right);
        foreach (var run in r.Runs)
            table.Row(ReplayKit.ShortId(run.Id), run.Symbol, run.Strategy, run.PayoffCount.ToString(), run.FirstFireEt ?? "—",
                Fmt.PercentPoints((decimal)run.TotalPnl, 2, signed: true));
        var text = $"Live replay {day:ddd yyyy-MM-dd}: {r.Trades} diary trade(s) → {r.Runs.Count} live run(s)" +
                   (outRoot is null ? "." : $", pages under {outRoot}.") + "\n" + table.Render();
        if (r.Skipped.Count > 0) text += "\nSkipped:\n" + string.Join("\n", r.Skipped.Select(s => "  " + s));
        return OperationResult.Ok(data, text);
    }
}
