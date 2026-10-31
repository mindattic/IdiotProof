using IdiotProof.Operations.Rendering;

namespace IdiotProof.Operations.Replay;

public sealed record BacktestConditionsArgs
{
    [Argument(0, Name = "run", Description = "The run id `backtest run` printed (bt-xxxxxxxx).")]
    public string Run { get; init; } = "";

    [Option("date", Description = "Which day of the run (default its first day with bars).", Short = 'd')]
    public DateOnly? Date { get; init; }

    [Option("all", Description = "Every candle, not just the ones where a condition changed, fired or traded.", Short = 'a')]
    public bool All { get; init; }
}

/// <summary>
/// The per-candle ConditionTable of one backtest day: each entry condition's truth on every bar
/// (c1..cN, legend below), whether all held, trigger fires, entries and time in position — so
/// a "why didn't it fire?" question is answered bar by bar.
/// </summary>
[Operation("backtest conditions", Summary = "A backtest day's per-candle condition table: each entry condition's truth per bar, fires, entries and time in position.", Risk = OperationRisk.Read)]
[Example("idiotproof backtest conditions bt-1a2b3c4d")]
[Example("idiotproof backtest conditions bt-1a2b3c4d --date 2026-10-29 --all")]
public sealed class BacktestConditionsOperation : IOperation<BacktestConditionsArgs>
{
    public async Task<OperationResult> ExecuteAsync(BacktestConditionsArgs args, OperationContext ctx)
    {
        var owner = ctx.RequireOwner();
        var saved = await SavedRunStore.For(ctx).LoadAsync<BacktestRunRecord>(args.Run, "bt", owner, ctx.CancellationToken)
            ?? throw OperationException.Usage($"No backtest '{args.Run}' of yours. `idiotproof backtest run ...` prints the id.");
        var rec = saved.Payload;
        var day = args.Date is { } d
            ? rec.Days.FirstOrDefault(x => x.Day == d) ?? throw OperationException.Usage($"Run {saved.Id} covers {rec.From:yyyy-MM-dd} → {rec.To:yyyy-MM-dd} (weekdays); {d:yyyy-MM-dd} isn't in it.")
            : rec.Days.FirstOrDefault(x => x.Bars > 0) ?? rec.Days[0];
        if (day.Conditions.Count == 0)
            return OperationResult.Ok(new { Run = saved.Id, Day = day.Day.ToString("yyyy-MM-dd"), day.Note, day.Labels, Rows = day.Conditions },
                $"{rec.Symbol} {day.Day:yyyy-MM-dd}: no condition table — {day.Note ?? "no bars"}");

        var rows = new List<BacktestConditionRow>();
        BacktestConditionRow? prev = null;
        foreach (var r in day.Conditions)
        {
            var changed = prev is null || !r.Results.SequenceEqual(prev.Results) || r.InPosition != prev.InPosition;
            if (args.All || changed || r.HasTriggerFire || r.OpenedTrade) rows.Add(r);
            prev = r;
        }

        var table = new TextTable { MaxWidth = ctx.Width }.Column("ET").Column("close", Align.Right);
        for (var i = 0; i < day.Labels.Count; i++) table.Column($"c{i + 1}", Align.Right);
        table.Column("all").Column("event");
        foreach (var r in rows)
        {
            var cells = new List<string?> { Fmt.EtTime(r.Utc), Fmt.Money(r.Close) };
            for (var i = 0; i < day.Labels.Count; i++) cells.Add(i < r.Results.Count && r.Results[i] ? "✓" : "·");
            cells.Add(r.AllSatisfied ? "ALL" : "");
            cells.Add(r.OpenedTrade ? "ENTRY" : r.HasTriggerFire ? "trigger" : r.InPosition ? "in position" : "");
            table.Row([.. cells]);
        }
        var legend = string.Join("\n", day.Labels.Select((l, i) => $"  c{i + 1} = {l}"));
        var text = $"{rec.Symbol} \"{rec.Strategy}\" · {day.Day:ddd yyyy-MM-dd} · {day.Conditions.Count} candles" +
                   (args.All ? "" : $" ({rows.Count} where something changed)") + "\n" + table.Render() + "\n" + legend;
        return OperationResult.Ok(new { Run = saved.Id, Day = day.Day.ToString("yyyy-MM-dd"), day.Labels, Rows = rows }, text);
    }
}
