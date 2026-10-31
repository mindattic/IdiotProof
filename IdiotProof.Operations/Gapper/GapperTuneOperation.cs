using IdiotProof.Core.Data;
using IdiotProof.Core.Trading;
using IdiotProof.Operations.Rendering;
using IdiotProof.Operations.Strategies;
using IdiotProof.Scripting;

using IdiotProof.Core.Services;
namespace IdiotProof.Operations.Gapper;

public sealed record GapperTuneArgs : GapperDialArgs
{
    [Argument(0, Name = "symbol", Description = "The ticker to build the gapper for.")]
    public string Symbol { get; init; } = "";

    [Option("save", Description = "Save it as an inactive gapper strategy (arm it later with `gapper arm`).", Short = 's')]
    public bool Save { get; init; }

    [Option("mode", Description = "Broker mode for a saved gapper: paper (default) or sandbox.", Short = 'm')]
    public BrokerMode Mode { get; init; } = BrokerMode.Paper;
}

[Operation("gapper tune", Summary = "Dial a gapper profile in for one ticker and show the strategy GapperScriptFactory generates; --save stores it inactive.", Risk = OperationRisk.Write)]
[Example("idiotproof gapper tune NVDA --profile classic-gapper")]
[Example("idiotproof gapper tune ABCD --profile penny-runner --gap 12 --stop 6 --giveback 18 --sell-by 09:25 --save")]
public sealed class GapperTuneOperation : IOperation<GapperTuneArgs>
{
    public async Task<OperationResult> ExecuteAsync(GapperTuneArgs args, OperationContext ctx)
    {
        var profile = args.Tune(ctx);
        var baseName = GapperRules.ResolveProfile(ctx, args.Profile).Name;
        var build = GapperRules.Compose(args.Symbol, profile);
        var human = GapperProfileShowOperation.Render(profile).Render() + "\n\n" + build.Script;

        if (!args.Save)
            return OperationResult.Ok(new { build.Symbol, Profile = profile, build.Script, Stamp = build.Definition.ShortId(), Saved = false },
                human + "\n\nPreview only. Add --save to store it, or `idiotproof gapper arm " + build.Symbol + "` with the same dials to queue it now.");

        var created = await GapperRules.CreateAsync(ctx, build, baseName + (args.AnyDial ? " (tuned)" : ""), args.Mode);
        return OperationResult.Ok(new { build.Symbol, Profile = profile, build.Script, Stamp = build.Definition.ShortId(), Saved = true, created.Id, Mode = args.Mode.ToString() },
            human + $"\n\nSaved {build.Symbol} '{created.Title}' as {StrategyRefs.ShortGuid(created.Id)}, inactive, {args.Mode}. " +
            $"Arm it with `idiotproof gapper arm {StrategyRefs.ShortGuid(created.Id)}`.");
    }
}
