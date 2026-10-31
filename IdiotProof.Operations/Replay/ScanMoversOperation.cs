using System.Globalization;
using IdiotProof.Blazor.Data;
using IdiotProof.Monitor;
using IdiotProof.Operations.Market;
using IdiotProof.Operations.Rendering;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Operations.Replay;

public sealed record ScanMoversArgs
{
    [Option("date", Description = "ET day to replay the survivors on (default today ET — movers are live).", Short = 'd')]
    public DateOnly? Date { get; init; }

    [Option("replay", Description = "Replay each survivor with the gapper profile and save the runs (and pages).", Short = 'r')]
    public bool Replay { get; init; }

    [Option("top", Description = "Keep at most this many gappers (default 10, max 25).")]
    public int Top { get; init; } = 10;

    [Option("min-gap", Description = "Minimum percent change (default 5).")]
    public double MinGap { get; init; } = 5;

    [Option("min-price", Description = "Lowest price (default 1).")]
    public double MinPrice { get; init; } = 1;

    [Option("max-price", Description = "Highest price (default 50).")]
    public double MaxPrice { get; init; } = 50;

    [Option("profile", Description = "Gapper profile or family to replay with (default classic-gapper).", Short = 'p')]
    public string Profile { get; init; } = "classic-gapper";

    [Option("pages", Description = "With --replay: render run pages and archive indexes (HTML only).")]
    public bool Pages { get; init; } = true;

    [Option("out", Description = "Archive folder for the pages (default the mindattic.com replays folder).")]
    public string? Out { get; init; }
}

/// <summary>
/// The gapper scanner (the old <c>scan</c>): Alpaca's live top gainers screened to the gapper band
/// (percent change and price), biggest first; with <c>--replay</c> each survivor is replayed with
/// the gapper profile through the same harness as <c>replay run</c>, populating the archive.
/// </summary>
[Operation("scan movers", Summary = "Screen today's top gainers to the gapper band; --replay replays each survivor with a gapper profile and saves the runs.", Risk = OperationRisk.Write)]
[Example("idiotproof scan movers")]
[Example("idiotproof scan movers --min-gap 10 --max-price 20 --replay")]
public sealed class ScanMoversOperation : IOperation<ScanMoversArgs>
{
    public async Task<OperationResult> ExecuteAsync(ScanMoversArgs args, OperationContext ctx)
    {
        ctx.RequireOwner();
        if (args.Top is < 1 or > 25) return OperationResult.Usage("--top must be 1-25.");
        if (args.MaxPrice <= args.MinPrice) return OperationResult.Usage("--max-price must be above --min-price.");

        List<(string Symbol, double Percent, double Price)> gainers;
        await using (var api = await MarketData.OpenAsync(ctx))
        {
            var root = await api.GetDataAsync("/v1beta1/screener/stocks/movers", MarketData.Q(("top", "50")), ctx.CancellationToken);
            gainers = StrategyScanner.ParseGainers(root);
        }
        var picks = StrategyScanner.SelectGappers(gainers, args.MinGap, args.MinPrice, args.MaxPrice, args.Top);
        var head = $"{gainers.Count} gainers screened → {picks.Count} in the gapper band (gap ≥{args.MinGap.ToString("0.##", CultureInfo.InvariantCulture)}%, " +
                   $"{Fmt.Money(args.MinPrice)}-{Fmt.Money(args.MaxPrice)}).";

        if (!args.Replay || picks.Count == 0)
        {
            var t = new TextTable { MaxWidth = ctx.Width }.Column("#", Align.Right).Column("sym").Column("price", Align.Right).Column("change", Align.Right);
            for (var i = 0; i < picks.Count; i++)
                t.Row((i + 1).ToString(), picks[i].Symbol, Fmt.Money(picks[i].Price), Fmt.PercentPoints((decimal)picks[i].Percent, 1, signed: true));
            return OperationResult.Ok(new { Screened = gainers.Count, Picks = picks.Select(p => new { p.Symbol, p.Percent, p.Price }).ToList(), Replayed = Array.Empty<object>() },
                head + (picks.Count == 0 ? " Widen --min-gap or the price band, or the market is quiet." : "\n" + t.Render() + "\nAdd --replay to replay each with the gapper profile."));
        }

        var day = args.Date ?? DateOnly.FromDateTime(EasternClock.ToEastern(ctx.UtcNow));
        var feed = ReplayKit.RequireRealFeed(ctx);
        var dbf = ctx.Get<IDbContextFactory<AppDbContext>>();
        var outRoot = args.Pages ? ReplayKit.ArchiveRoot(args.Out) : null;
        var table = new TextTable { MaxWidth = ctx.Width }
            .Column("sym").Column("change", Align.Right).Column("run").Column("fired").Column("P&L", Align.Right).Column("note", maxWidth: 40);
        var replayed = new List<object>();
        foreach (var g in picks)
        {
            var def = StrategyReplay.ResolveFamily(args.Profile, g.Symbol)
                ?? throw OperationException.Usage($"Unknown profile/family '{args.Profile}'.");
            try
            {
                var loaded = await StrategyReplay.LoadDayAsync(feed, ReplayKit.FeedLabel(feed), g.Symbol, day, ctx.UtcNow, ctx.CancellationToken);
                var o = await StrategyReplay.ReplayAsync(dbf, loaded, def, def.Name ?? $"{g.Symbol} {args.Profile}", repeat: false, outRoot, ctx.UtcNow, ctx.CancellationToken);
                replayed.Add(new { g.Symbol, Run = ReplayKit.RunData(o.Run) });
                table.Row(g.Symbol, Fmt.PercentPoints((decimal)g.Percent, 1, signed: true), ReplayKit.ShortId(o.Run.Id),
                    o.Run.Fired ? $"● {o.Run.FirstFireEt}" : "·", Fmt.PercentPoints((decimal)o.Run.TotalPnl, 2, signed: true),
                    o.Run.Fired ? "" : $"best {o.BestPassed}/{o.ConditionCount}");
            }
            catch (ReplayException ex)
            {
                replayed.Add(new { g.Symbol, Error = ex.Message });
                table.Row(g.Symbol, Fmt.PercentPoints((decimal)g.Percent, 1, signed: true), "—", "", "", ex.Message);
            }
        }
        return OperationResult.Ok(new { Screened = gainers.Count, Picks = picks.Select(p => new { p.Symbol, p.Percent, p.Price }).ToList(), Day = day.ToString("yyyy-MM-dd"), Replayed = replayed },
            head + $"\nReplayed on {day:ddd yyyy-MM-dd} with {args.Profile}:\n" + table.Render() + (outRoot is null ? "" : $"\nPages under {outRoot} (not published)."));
    }
}
