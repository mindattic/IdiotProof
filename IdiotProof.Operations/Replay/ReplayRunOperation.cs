using IdiotProof.Blazor.Data;
using IdiotProof.Blazor.Services;
using IdiotProof.Monitor;
using IdiotProof.Operations.Rendering;
using IdiotProof.Operations.Strategies;
using IdiotProof.Scripting;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Operations.Replay;

public sealed record ReplayRunArgs
{
    [Argument(0, Name = "symbol", Description = "The ticker to replay.")]
    public string Symbol { get; init; } = "";

    [Argument(1, Name = "date", Required = false, Description = "The ET trading day (yyyy-MM-dd); default the last completed trading day.")]
    public DateOnly? Date { get; init; }

    [Option("strategy", Description = "Replay this saved strategy (id, id prefix, stamp id or symbol) instead of the symbol's own.", Short = 's')]
    public string? Strategy { get; init; }

    [Option("profile", Description = "Replay a gapper profile on the fly (e.g. classic-gapper, penny-runner).", Short = 'p')]
    public string? Profile { get; init; }

    [Option("family", Description = "Replay a built-in family (momentum, reversal, rsireversal, emabreak, rthdrive, shortfade, swingreversal).", Short = 'f')]
    public string? Family { get; init; }

    [Option("repeat", Description = "Re-arm after every exit to show each round-trip the day offered.", Short = 'r')]
    public bool Repeat { get; init; }

    [Option("pages", Description = "Render the run page and archive indexes (HTML only; publishing stays manual).")]
    public bool Pages { get; init; } = true;

    [Option("out", Description = "Archive folder for the pages (default the mindattic.com replays folder).")]
    public string? Out { get; init; }
}

/// <summary>
/// The offline replay (the old <c>replay</c> command): one past day's 1-minute bars walked through
/// the same snapshot builder, conditions, session gate and GapperExitEvaluator the live Monitor runs.
/// The run is persisted to SQL (archive + ML feature store) and, by default, rendered as a page.
/// </summary>
[Operation("replay run", Summary = "Replay a strategy, gapper profile or built-in family over one past day with the Monitor's own evaluator; saves the run and its page.", Risk = OperationRisk.Write)]
[Example("idiotproof replay run NVDA 2026-10-29")]
[Example("idiotproof replay run ABCD 2026-10-29 --profile penny-runner")]
[Example("idiotproof replay run TSLA --family rthdrive --repeat --no-pages")]
public sealed class ReplayRunOperation : IOperation<ReplayRunArgs>
{
    public async Task<OperationResult> ExecuteAsync(ReplayRunArgs args, OperationContext ctx)
    {
        var owner = ctx.RequireOwner();
        var symbol = ReplayKit.Symbol(args.Symbol);
        var picks = new[] { args.Strategy, args.Profile, args.Family }.Count(x => !string.IsNullOrWhiteSpace(x));
        if (picks > 1) return OperationResult.Usage("Pick one of --strategy, --profile or --family.");
        var day = ReplayKit.Day(args.Date, ctx);

        StrategyDefinition def;
        string title;
        if (!string.IsNullOrWhiteSpace(args.Profile) || !string.IsNullOrWhiteSpace(args.Family))
        {
            var id = (args.Profile ?? args.Family)!.Trim();
            def = StrategyReplay.ResolveFamily(id, symbol)
                ?? throw OperationException.Usage($"Unknown profile/family '{id}'. `idiotproof replay families` lists the families; `idiotproof gapper profiles` the profiles.");
            title = def.Name ?? $"{symbol} {id}";
        }
        else
        {
            Strategy row;
            if (!string.IsNullOrWhiteSpace(args.Strategy)) row = await StrategyRefs.ResolveAsync(ctx, args.Strategy);
            else
            {
                var mine = (await ctx.Get<StrategyRepository>().GetAllForUserAsync(owner, ctx.CancellationToken))
                    .Where(s => s.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase)).ToList();
                row = mine.FirstOrDefault(s => s.IsActive) ?? mine.FirstOrDefault()
                    ?? throw OperationException.Usage($"No saved strategy for {symbol}. Pass --profile <id> or --family <id> to replay one on the fly.");
            }
            def = StrategyRefs.RequireDefinition(row);
            if (!def.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase))
                return OperationResult.Usage($"Strategy {StrategyRefs.ShortGuid(row.Id)} trades {def.Symbol}, not {symbol}.");
            title = row.Title;
        }

        var feed = ReplayKit.RequireRealFeed(ctx);
        var loaded = await ReplayKit.LoadDayAsync(ctx, feed, symbol, day);
        var outRoot = args.Pages ? ReplayKit.ArchiveRoot(args.Out) : null;
        var o = await StrategyReplay.ReplayAsync(ctx.Get<IDbContextFactory<AppDbContext>>(), loaded, def, title, args.Repeat, outRoot, ctx.UtcNow, ctx.CancellationToken);

        var kv = new KeyValueBlock()
            .Add("run", ReplayKit.ShortId(o.Run.Id))
            .Add("replay", $"{symbol} {day:ddd yyyy-MM-dd} · \"{title}\" · {o.BarCount} bars ({o.Run.Feed})")
            .Add("result", ReplayKit.Verdict(o))
            .Add("page", o.PagePath ?? "(not rendered)");
        var text = kv.Render() + (o.Payoffs.Count > 0 ? "\n\n" + ReplayKit.Payoffs(o.Payoffs, ctx.Width) : "") +
                   $"\n\nDetails: idiotproof replay show {ReplayKit.ShortId(o.Run.Id)}";
        return OperationResult.Ok(new
        {
            Run = ReplayKit.RunData(o.Run), o.Payoffs, Bars = o.BarCount, o.BestPassed, Conditions = o.ConditionCount, o.BestFail, o.Repeat, Page = o.PagePath,
        }, text);
    }
}
