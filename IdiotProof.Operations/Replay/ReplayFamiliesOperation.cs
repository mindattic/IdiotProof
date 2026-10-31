using IdiotProof.Scripting;
using IdiotProof.Monitor;
using IdiotProof.Operations.Rendering;

namespace IdiotProof.Operations.Replay;

[Operation("replay families", Summary = "The built-in strategy families `replay run --family` and `replay all` use, with what each trades.", Risk = OperationRisk.Read)]
[Example("idiotproof replay families")]
public sealed class ReplayFamiliesOperation : IOperation<NoArgs>
{
    public Task<OperationResult> ExecuteAsync(NoArgs args, OperationContext ctx)
    {
        var rows = StrategyReplay.Families.Select(f =>
        {
            var def = StrategyReplay.ResolveFamily(f.Id, "TICK");
            return new
            {
                f.Id,
                f.Description,
                Session = def?.Session.ToString(),
                Side = def is null ? null : def.Direction.ToString(),
                Repeats = def?.ShouldRepeat ?? false,
                Script = def?.ToScript(),
            };
        }).ToList();
        var table = new TextTable { MaxWidth = ctx.Width }.Column("family").Column("session").Column("side").Column("what it trades");
        foreach (var r in rows) table.Row(r.Id, r.Session ?? "?", r.Side ?? "?", r.Description);
        return Task.FromResult(OperationResult.Ok(rows, table.Render() +
            $"\n{rows.Count} families. `idiotproof replay run <symbol> <date> --family <id>`; `idiotproof replay all <symbol>` tries them all."));
    }
}
