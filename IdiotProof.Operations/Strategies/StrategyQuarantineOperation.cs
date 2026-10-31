using IdiotProof.Blazor.Services;
using IdiotProof.Core.Trading;
using IdiotProof.Operations.Rendering;

namespace IdiotProof.Operations.Strategies;

public sealed record StrategyQuarantineArgs
{
    [Option("symbol", Description = "Only this symbol.")]
    public string? Symbol { get; init; }
}

[Operation("strategy quarantine", Summary = "List strategies the Monitor refuses to evaluate (canon unreadable, or no canon and a script that doesn't parse), with the reason.", Risk = OperationRisk.Read,
    Description = "A quarantined strategy never fires; one that is quarantined while holding a position has NO managed exit and is listed first.")]
[Example("idiotproof strategy quarantine")]
public sealed class StrategyQuarantineOperation : IOperation<StrategyQuarantineArgs>
{
    public async Task<OperationResult> ExecuteAsync(StrategyQuarantineArgs args, OperationContext ctx)
    {
        var owner = ctx.RequireOwner();
        var all = await ctx.Get<StrategyRepository>().GetAllForUserAsync(owner, ctx.CancellationToken);
        var rows = all
            .Where(s => args.Symbol is null || s.Symbol.Equals(args.Symbol, StringComparison.OrdinalIgnoreCase))
            .Select(s => (s, Load: StrategyRefs.Load(s)))
            .Where(x => x.Load.Definition is null)
            .Select(x => new
            {
                x.s.Id,
                Short = StrategyRefs.ShortGuid(x.s.Id),
                x.s.Symbol,
                x.s.Title,
                Mode = OrderIntent.ParseMode(x.s.BrokerMode).ToString(),
                x.s.IsActive,
                Position = x.s.PositionQty,
                Reason = x.Load.CanonicalError ?? (string.IsNullOrWhiteSpace(x.s.ScriptJson)
                    ? "no canon, and the script doesn't parse"
                    : "the canon can't be read"),
                Kind = x.Load.CanonicalError is not null ? "canon" : "script",
            })
            .OrderByDescending(r => r.Position > 0).ThenByDescending(r => r.IsActive).ThenBy(r => r.Symbol)
            .ToList();

        if (rows.Count == 0)
            return OperationResult.Ok(rows, $"No quarantined strategies ({all.Count} checked).");

        var table = new TextTable { MaxWidth = ctx.Width }
            .Column("id").Column("sym").Column("title", maxWidth: 28).Column("mode").Column("on").Column("pos", Align.Right).Column("reason", maxWidth: 60);
        foreach (var r in rows)
            table.Row(r.Short, r.Symbol, r.Title, r.Mode, r.IsActive ? "●" : "·", r.Position == 0 ? "" : r.Position.ToString(), r.Reason);
        var holding = rows.Count(r => r.Position > 0);
        var human = table.Render() + $"\n{rows.Count} of {all.Count} strategies quarantined." +
                    (holding > 0 ? $"\nWARNING: {holding} hold a position with no managed exit — manage them by hand (`idiotproof positions show <sym>`)." : "") +
                    "\nTry `idiotproof strategy resync-canon` (dry run) to repair canons from their scripts.";
        return OperationResult.Ok(rows, human);
    }
}
