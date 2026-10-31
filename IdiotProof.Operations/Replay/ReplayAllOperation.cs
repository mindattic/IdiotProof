using IdiotProof.Core.Data;
using IdiotProof.Operations.Rendering;
using Microsoft.EntityFrameworkCore;

using IdiotProof.Core.Monitoring;
namespace IdiotProof.Operations.Replay;

public sealed record ReplayAllArgs
{
    [Argument(0, Name = "symbol", Description = "The ticker to try every family on.")]
    public string Symbol { get; init; } = "";

    [Argument(1, Name = "date", Required = false, Description = "The ET trading day (yyyy-MM-dd); default the last completed trading day.")]
    public DateOnly? Date { get; init; }

    [Option("pages", Description = "Render each run's page and the archive indexes (HTML only).")]
    public bool Pages { get; init; } = true;

    [Option("out", Description = "Archive folder for the pages (default the mindattic.com replays folder).")]
    public string? Out { get; init; }
}

/// <summary>
/// The auto-strategy router (the old <c>replay-all</c>): every family in the library — the classic
/// gapper and each built-in — replayed with repeat on one ticker/day (bars fetched once), each
/// saved as its own run, ranked fired-first by realized P&amp;L to surface the best fit.
/// </summary>
[Operation("replay all", Summary = "Replay every strategy family on one ticker/day and rank them by realized P&L (best fit first).", Risk = OperationRisk.Write)]
[Example("idiotproof replay all NVDA 2026-10-29")]
[Example("idiotproof replay all ABCD --no-pages")]
public sealed class ReplayAllOperation : IOperation<ReplayAllArgs>
{
    public async Task<OperationResult> ExecuteAsync(ReplayAllArgs args, OperationContext ctx)
    {
        ctx.RequireOwner();
        var symbol = ReplayKit.Symbol(args.Symbol);
        var day = ReplayKit.Day(args.Date, ctx);
        var feed = ReplayKit.RequireRealFeed(ctx);
        var loaded = await ReplayKit.LoadDayAsync(ctx, feed, symbol, day);
        var outRoot = args.Pages ? ReplayKit.ArchiveRoot(args.Out) : null;

        var ranked = await StrategyReplay.ReplayAllFamiliesAsync(ctx.Get<IDbContextFactory<AppDbContext>>(), loaded, outRoot, ctx.UtcNow, ct: ctx.CancellationToken);

        var table = new TextTable { MaxWidth = ctx.Width }
            .Column("#", Align.Right).Column("family").Column("run").Column("fired").Column("trips", Align.Right).Column("P&L", Align.Right).Column("note", maxWidth: 40);
        var rank = 0;
        foreach (var r in ranked)
        {
            rank++;
            if (r.Outcome is { } o)
                table.Row(rank.ToString(), r.Family, ReplayKit.ShortId(o.Run.Id), o.Run.Fired ? $"● {o.Run.FirstFireEt}" : "·",
                    o.Run.PayoffCount.ToString(), Fmt.PercentPoints((decimal)o.Run.TotalPnl, 2, signed: true),
                    o.Run.Fired ? "" : $"best {o.BestPassed}/{o.ConditionCount}, waiting on {o.BestFail ?? "n/a"}");
            else table.Row(rank.ToString(), r.Family, "—", "error", "", "", r.Error);
        }
        var best = ranked.FirstOrDefault(r => r.Outcome?.Run.Fired == true)?.Outcome;
        var data = new
        {
            Symbol = symbol, Day = day.ToString("yyyy-MM-dd"), Bars = loaded.Candles.Count,
            Ranked = ranked.Select(r => new { r.Family, Run = r.Outcome is null ? null : ReplayKit.RunData(r.Outcome.Run), r.Error }).ToList(),
            BestFit = best is null ? null : best.Run.Strategy,
        };
        var text = $"{symbol} {day:ddd yyyy-MM-dd}: {StrategyReplay.Families.Count} families over {loaded.Candles.Count} bars ({loaded.FeedLabel.ToUpperInvariant()})\n" +
                   table.Render() + "\n" +
                   (best is not null ? $"Best fit: {best.Run.Strategy} {Fmt.PercentPoints((decimal)best.Run.TotalPnl, 2, signed: true)}" : "No family fired for this ticker/day.");
        return OperationResult.Ok(data, text);
    }
}
