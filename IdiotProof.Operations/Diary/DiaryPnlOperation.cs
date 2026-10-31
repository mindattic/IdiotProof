using System.Globalization;
using IdiotProof.Operations.Rendering;

namespace IdiotProof.Operations.Diary;

/// <summary>What <c>diary pnl --by</c> splits each period by.</summary>
public enum DiaryPnlBy
{
    Strategy,
    Symbol,
}

public sealed record DiaryPnlArgs : DiaryFilterArgs
{
    [Option("day", Description = "One row per ET trading day (the default).")]
    public bool Day { get; init; }

    [Option("week", Description = "One row per week (Monday-Sunday, ET).")]
    public bool Week { get; init; }

    [Option("month", Description = "One row per calendar month (ET).")]
    public bool Month { get; init; }

    [Option("by", Description = "Also split each period by strategy or symbol.")]
    public DiaryPnlBy? By { get; init; }
}

/// <summary>One period (and group) of realized P&amp;L.</summary>
public sealed record DiaryPnlRow(string Period, DateOnly Start, string? Group, int Trades, int Wins, int Losses, decimal PnL, decimal Cumulative);

/// <summary>
/// Realized P&amp;L from closed diary trades, bucketed by the ET date each trade exited: per day,
/// week or month, optionally split by strategy or symbol, with trade and win counts and a running
/// total. Open, not-filled and orphaned trades carry no realized P&amp;L and are left out.
/// </summary>
[Operation("diary pnl", Summary = "Realized P&L from closed trades per day, week or month (ET), optionally by strategy or symbol.", Risk = OperationRisk.Read)]
[Example("idiotproof diary pnl")]
[Example("idiotproof diary pnl --week --by strategy")]
[Example("idiotproof diary pnl --month --mode live --since 2026-01-01")]
public sealed class DiaryPnlOperation : IOperation<DiaryPnlArgs>
{
    public async Task<OperationResult> ExecuteAsync(DiaryPnlArgs args, OperationContext ctx)
    {
        if ((args.Day ? 1 : 0) + (args.Week ? 1 : 0) + (args.Month ? 1 : 0) > 1)
            return OperationResult.Usage("Pick one of --day, --week or --month.");
        var unit = args.Month ? "month" : args.Week ? "week" : "day";

        var closed = (await DiarySupport.LoadAsync(ctx, args, Blazor.Data.TradeDiaryStatus.Closed))
            .Where(t => t.ExitUtc is not null && t.RealizedPnL is not null)
            .ToList();

        var rows = new List<DiaryPnlRow>();
        var running = 0m;
        foreach (var period in closed.GroupBy(t => PeriodStart(Account.AccountApis.EtDate(t.ExitUtc!.Value), unit)).OrderBy(g => g.Key))
        {
            var groups = args.By switch
            {
                DiaryPnlBy.Strategy => period.GroupBy(t => (string?)t.StrategyTitle),
                DiaryPnlBy.Symbol => period.GroupBy(t => (string?)t.Symbol),
                _ => period.GroupBy(_ => (string?)null),
            };
            foreach (var g in groups.OrderByDescending(g => g.Sum(t => t.RealizedPnL!.Value)))
            {
                var pnl = g.Sum(t => t.RealizedPnL!.Value);
                running += pnl;
                rows.Add(new DiaryPnlRow(Label(period.Key, unit), period.Key, g.Key, g.Count(),
                    g.Count(t => t.RealizedPnL > 0), g.Count(t => t.RealizedPnL < 0), pnl, running));
            }
        }

        var head = $"Realized P&L by {unit}" + (args.By is { } by ? $" and {by.ToString().ToLowerInvariant()}" : "") + $" ({DiarySupport.Describe(args)})";
        var total = closed.Sum(t => t.RealizedPnL!.Value);
        var data = new { Unit = unit, By = args.By?.ToString().ToLowerInvariant(), Total = total, Trades = closed.Count, Rows = rows };
        if (rows.Count == 0) return OperationResult.Ok(data, $"{head}\nNo closed trades match.");

        var table = new TextTable { MaxWidth = ctx.Width }.Column(unit);
        if (args.By is not null) table.Column(args.By.Value.ToString().ToLowerInvariant(), maxWidth: 30);
        table.Column("trades", Align.Right).Column("won", Align.Right).Column("lost", Align.Right)
            .Column("P&L", Align.Right).Column("cumulative", Align.Right);
        foreach (var r in rows)
        {
            var cells = new List<string?> { r.Period };
            if (args.By is not null) cells.Add(r.Group);
            cells.AddRange([r.Trades.ToString(), r.Wins.ToString(), r.Losses.ToString(), Fmt.SignedMoney(r.PnL), Fmt.SignedMoney(r.Cumulative)]);
            table.Row([.. cells]);
        }
        var periods = rows.Select(r => r.Start).Distinct().Count();
        var green = rows.GroupBy(r => r.Start).Count(g => g.Sum(r => r.PnL) > 0);
        return OperationResult.Ok(data,
            $"{head}\n{table.Render()}\n{closed.Count} closed trades, {Fmt.SignedMoney(total)} over {periods} {unit}s ({green} green).");
    }

    /// <summary>The first ET date of the day/week (Monday)/month containing <paramref name="d"/>.</summary>
    public static DateOnly PeriodStart(DateOnly d, string unit) => unit switch
    {
        "month" => new DateOnly(d.Year, d.Month, 1),
        "week" => d.AddDays(-(((int)d.DayOfWeek + 6) % 7)),
        _ => d,
    };

    private static string Label(DateOnly start, string unit) => unit switch
    {
        "month" => start.ToString("yyyy-MM", CultureInfo.InvariantCulture),
        "week" => "wk of " + start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        _ => start.ToString("yyyy-MM-dd ddd", CultureInfo.InvariantCulture),
    };
}
