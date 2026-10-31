using IdiotProof.Blazor.Services;
using IdiotProof.Operations.Rendering;

namespace IdiotProof.Operations.Strategies;

public sealed record StrategyFiresArgs
{
    [Argument(0, Name = "strategy", Description = "Id, id prefix, stamp id or symbol.")]
    public string Strategy { get; init; } = "";

    [Option("limit", Description = "How many verdicts to show (default 20, max 200).", Short = 'n')]
    public int Limit { get; init; } = 20;
}

[Operation("strategy fires", Summary = "Why a strategy did or didn't fire: its recent gate verdicts (conditions met, LLM veto, Risk Guardian block, order placed or rejected) and where it stands now.", Risk = OperationRisk.Read)]
[Example("idiotproof strategy fires NVDA")]
[Example("idiotproof strategy fires NVDA --limit 50")]
public sealed class StrategyFiresOperation : IOperation<StrategyFiresArgs>
{
    public async Task<OperationResult> ExecuteAsync(StrategyFiresArgs args, OperationContext ctx)
    {
        if (args.Limit is < 1 or > 200) return OperationResult.Usage("--limit is 1 to 200.");
        var s = await StrategyRefs.ResolveAsync(ctx, args.Strategy);
        var rows = await StrategyTrail.AuditAsync(ctx, s, StrategyTrail.FireCategories, args.Limit);
        var progress = await ctx.Get<ConditionProgressRepository>().GetAsync(s.Id, ctx.CancellationToken);

        var verdicts = rows.Select(a => new
        {
            a.TimestampUtc,
            Gate = Gate(a.Category),
            Verdict = Verdict(a.Category),
            a.Category,
            Detail = Strip(a.Message, s),
        }).ToList();

        var now = !s.IsActive ? "inactive — the Monitor isn't evaluating it"
            : s.PositionQty > 0 ? $"holding {s.PositionQty} — exits are being managed"
            : progress is null ? "active, not evaluated yet"
            : progress.IsFullPass ? $"all {progress.TotalCount} conditions pass ({Fmt.Et(progress.EvaluatedUtc)}) — the gates decide next"
            : $"{progress.PassedCount}/{progress.TotalCount} conditions; waiting on {progress.FirstFailingVerb ?? "?"} ({Fmt.Et(progress.EvaluatedUtc)})";

        var head = $"{s.Symbol} '{s.Title}' — fired {s.FireCount}× (last {Fmt.Et(s.LastFiredUtc, "never")})\nnow: {now}";
        if (verdicts.Count == 0)
            return OperationResult.Ok(new { s.Id, s.FireCount, Now = now, Verdicts = verdicts },
                head + "\n\nNo gate verdicts recorded yet: its conditions have never all passed together.");

        var table = new TextTable { MaxWidth = ctx.Width }.Column("when (ET)").Column("gate").Column("verdict").Column("detail", maxWidth: 90);
        foreach (var v in verdicts) table.Row(Fmt.Et(v.TimestampUtc), v.Gate, v.Verdict, v.Detail);
        return OperationResult.Ok(new { s.Id, s.FireCount, Now = now, Verdicts = verdicts }, head + "\n\n" + table.Render());
    }

    private static string Gate(string category) => category switch
    {
        "signal-fire" => "conditions",
        "signal-vetoed" => "LLM panel",
        "signal-blocked" => "guardian",
        "entry" => "order",
        _ => "order",
    };

    private static string Verdict(string category) => category switch
    {
        "signal-fire" => "all met",
        "signal-vetoed" => "VETOED",
        "signal-blocked" => "BLOCKED",
        "entry" => "placed",
        "order-rejected" => "REJECTED",
        "order-placement-exception" => "ERROR",
        _ => category,
    };

    /// <summary>Drops the "[Title] SYM " prefix the Monitor writes; the header already names the strategy.</summary>
    private static string Strip(string message, Blazor.Data.Strategy s)
    {
        var m = message;
        foreach (var t in new[] { s.Title, StrategyArchive.Unmark(s.Title) })
            if (m.StartsWith($"[{t}]", StringComparison.Ordinal)) { m = m[(t.Length + 2)..].TrimStart(); break; }
        if (m.StartsWith(s.Symbol + " ", StringComparison.Ordinal)) m = m[(s.Symbol.Length + 1)..];
        return m;
    }
}
