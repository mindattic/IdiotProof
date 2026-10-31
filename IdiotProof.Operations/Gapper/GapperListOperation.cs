using IdiotProof.Blazor.Data;
using IdiotProof.Blazor.Services;
using IdiotProof.Core.Trading;
using IdiotProof.Operations.Rendering;
using IdiotProof.Operations.Strategies;
using IdiotProof.Scripting;

namespace IdiotProof.Operations.Gapper;

public sealed record GapperListArgs
{
    [Option("active", Description = "Only armed gappers.", Short = 'a')]
    public bool Active { get; init; }
}

[Operation("gapper list", Summary = "Your queued gappers with live state: armed, mode, position, condition progress and last exit.", Risk = OperationRisk.Read)]
[Example("idiotproof gapper list")]
[Example("idiotproof gapper list --active")]
public sealed class GapperListOperation : IOperation<GapperListArgs>
{
    public async Task<OperationResult> ExecuteAsync(GapperListArgs args, OperationContext ctx)
    {
        var gappers = (await GapperRules.GappersAsync(ctx))
            .Where(s => !args.Active || s.IsActive)
            .OrderByDescending(s => s.IsActive).ThenBy(s => s.Symbol).ThenByDescending(s => s.CreatedUtc)
            .ToList();
        var progress = await ctx.Get<ConditionProgressRepository>().GetForStrategyIdsAsync(gappers.Select(s => s.Id), ctx.CancellationToken);

        var items = gappers.Select(s =>
        {
            progress.TryGetValue(s.Id, out var p);
            var def = StrategyRefs.Load(s).Definition;
            return new
            {
                s.Id,
                Short = StrategyRefs.ShortGuid(s.Id),
                s.Symbol,
                s.Title,
                Mode = OrderIntent.ParseMode(s.BrokerMode).ToString(),
                s.IsActive,
                State = State(s),
                Position = s.PositionQty,
                s.LastEntryPrice,
                Progress = p is null ? null : $"{p.PassedCount}/{p.TotalCount}",
                WaitingOn = p?.FirstFailingVerb,
                EvaluatedUtc = p?.EvaluatedUtc,
                SellBy = def?.ExitTime is { } t ? t.ToString(@"hh\:mm") : null,
                s.LastExitReason,
                s.LastExitedUtc,
                Quarantined = def is null,
            };
        }).ToList();

        var active = items.Count(i => i.IsActive);
        if (items.Count == 0)
            return OperationResult.Ok(items, "No gappers queued. `idiotproof gapper arm <symbol> --profile <id>` queues one.");

        var table = new TextTable { MaxWidth = ctx.Width }
            .Column("id").Column("sym").Column("state").Column("mode").Column("pos", Align.Right).Column("cond", Align.Right)
            .Column("waiting on", maxWidth: 26).Column("sell by").Column("title", maxWidth: 34);
        foreach (var i in items)
            table.Row(i.Short, i.Symbol, i.State, i.Mode, i.Position == 0 ? "" : $"{i.Position} @ {Fmt.Money(i.LastEntryPrice ?? 0)}",
                i.Progress ?? "", i.WaitingOn ?? "", i.SellBy ?? "", i.Title);
        return OperationResult.Ok(items, table.Render() + $"\n{items.Count} gappers, {active}/{GapperRules.MaxActiveGappers} armed.");
    }

    private static string State(Strategy s) =>
        s.PositionQty != 0 ? "HOLDING"
        : s.IsActive ? "armed"
        : s.LastExitedUtc is not null ? $"exited ({s.LastExitReason})"
        : "disarmed";
}
