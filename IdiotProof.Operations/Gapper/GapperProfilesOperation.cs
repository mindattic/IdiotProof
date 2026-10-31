using IdiotProof.Blazor.Services;
using IdiotProof.Operations.Rendering;
using IdiotProof.Scripting;

namespace IdiotProof.Operations.Gapper;

[Operation("gapper profiles", Summary = "List the gapper profile catalog: the templates every gapper starts from.", Risk = OperationRisk.Read)]
[Example("idiotproof gapper profiles")]
public sealed class GapperProfilesOperation : IOperation<NoArgs>
{
    public Task<OperationResult> ExecuteAsync(NoArgs args, OperationContext ctx)
    {
        var profiles = ctx.Get<GapperProfileService>().GetProfiles();
        var table = new TextTable { MaxWidth = ctx.Width }
            .Column("id").Column("name").Column("gap", Align.Right).Column("vol", Align.Right).Column("price", Align.Right)
            .Column("entry ET").Column("stop", Align.Right).Column("giveback", Align.Right).Column("sell by").Column("size", Align.Right);
        foreach (var p in profiles)
            table.Row(p.Id, p.Name, GapRange(p), GapperRules.Num(p.MinVolumeRatio) + "×",
                $"{Fmt.Money(p.MinPrice)}-{Fmt.Money(p.MaxPrice)}", $"{p.EntryWindowStartEt}-{p.EntryWindowEndEt}",
                GapperRules.Num(p.StopLossPercent) + "%", $"{GapperRules.Num(p.PeakGivebackPercent)}% @{p.ArmExitAtEt}", p.SellByEt,
                Fmt.Money(p.DefaultNotional, 0));
        return Task.FromResult(OperationResult.Ok(profiles,
            table.Render() + $"\n{profiles.Count} profiles. `idiotproof gapper profile show <id>` for one; `idiotproof gapper tune <symbol> --profile <id>` to dial one in."));
    }

    internal static string GapRange(GapperProfile p) =>
        p.MaxGapPercent is { } max ? $"{GapperRules.Num(p.MinGapPercent)}-{GapperRules.Num(max)}%" : $"≥{GapperRules.Num(p.MinGapPercent)}%";
}
