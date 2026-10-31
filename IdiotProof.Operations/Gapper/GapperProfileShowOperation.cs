using IdiotProof.Core.Data;
using IdiotProof.Core.Trading;
using IdiotProof.Operations.Rendering;
using IdiotProof.Operations.Strategies;
using IdiotProof.Scripting;

using IdiotProof.Core.Services;
namespace IdiotProof.Operations.Gapper;

public sealed record GapperProfileShowArgs
{
    [Argument(0, Name = "name", Description = "Profile id (classic-gapper) or name.")]
    public string Name { get; init; } = "";
}

[Operation("gapper profile show", Summary = "Every dial of one gapper profile, plus the strategy it generates for a sample ticker.", Risk = OperationRisk.Read)]
[Example("idiotproof gapper profile show classic-gapper")]
[Example("idiotproof gapper profile show penny-runner")]
public sealed class GapperProfileShowOperation : IOperation<GapperProfileShowArgs>
{
    public Task<OperationResult> ExecuteAsync(GapperProfileShowArgs args, OperationContext ctx)
    {
        var p = GapperRules.ResolveProfile(ctx, args.Name);
        var script = GapperScriptFactory.ToScript("TICK", p);
        return Task.FromResult(OperationResult.Ok(new { Profile = p, SampleScript = script },
            Render(p).Render() + "\n\nGenerates (for a sample ticker):\n" + script));
    }

    /// <summary>The profile's dials as an aligned key/value block.</summary>
    internal static KeyValueBlock Render(GapperProfile p) => new KeyValueBlock()
        .Add("id", p.Id)
        .Add("name", p.Name)
        .Add("about", p.Description)
        .Add("gap", GapperProfilesOperation.GapRange(p) + " over the previous close")
        .Add("volume", $"≥{GapperRules.Num(p.MinVolumeRatio)}× average")
        .Add("price band", $"{Fmt.Money(p.MinPrice)} - {Fmt.Money(p.MaxPrice)}")
        .Add("entry window", $"{p.EntryWindowStartEt}-{p.EntryWindowEndEt} ET")
        .Add("stop", GapperRules.Num(p.StopLossPercent) + "% below entry")
        .Add("trailing stop", p.TrailingStopPercent is { } t ? GapperRules.Num(t) + "% off the high" : "off")
        .Add("giveback", $"{GapperRules.Num(p.PeakGivebackPercent)}% of the run, armed from {p.ArmExitAtEt} ET")
        .Add("sell by", p.SellByEt + " ET (always flat before the 9:30 bell)")
        .Add("size", Fmt.Money(p.DefaultNotional, 0) + " notional");
}
