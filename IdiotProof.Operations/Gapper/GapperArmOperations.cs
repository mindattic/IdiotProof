using IdiotProof.Core.Data;
using IdiotProof.Core.Trading;
using IdiotProof.Operations.Rendering;
using IdiotProof.Operations.Strategies;
using IdiotProof.Scripting;

using IdiotProof.Core.Services;
namespace IdiotProof.Operations.Gapper;

public sealed record GapperArmArgs : GapperDialArgs
{
    [Argument(0, Name = "gapper", Description = "A symbol (queues a new gapper when it has none) or a gapper's id, id prefix or stamp id.")]
    public string Gapper { get; init; } = "";

    [Option("mode", Description = "Broker mode for a newly queued gapper: paper (default) or sandbox. Live is `strategy mode`, confirmed.", Short = 'm')]
    public BrokerMode Mode { get; init; } = BrokerMode.Paper;
}

[Operation("gapper arm", Summary = "Arm a gapper: activate an existing one or queue a new one from a profile, honoring one-per-symbol and the 3-gapper cap.", Risk = OperationRisk.Money)]
[Example("idiotproof gapper arm NVDA")]
[Example("idiotproof gapper arm ABCD --profile penny-runner --stop 6")]
[Example("idiotproof gapper arm 0190a3f2")]
public sealed class GapperArmOperation : IOperation<GapperArmArgs>
{
    public async Task<OperationResult> ExecuteAsync(GapperArmArgs args, OperationContext ctx)
    {
        var existing = await GapperRules.FindAsync(ctx, args.Gapper);
        if (existing is not null)
        {
            if (args.AnyDial || args.Profile is not null)
                return OperationResult.Usage($"{existing.Symbol} already has a gapper ({StrategyRefs.ShortGuid(existing.Id)}); dials apply only when queueing a new one. " +
                    "Remove it (`gapper disarm --remove`) and arm again with new dials, or `gapper tune` a new one.");
            return await GapperRules.ArmAsync(ctx, existing);
        }

        // A symbol with no gapper yet: refuse before writing anything, then queue it.
        var symbol = GapperRules.NormalizeSymbol(args.Gapper);
        if (args.Mode == BrokerMode.Live)
            return OperationResult.Usage("New gappers arm in paper or sandbox. Route one to live afterwards with `idiotproof strategy mode <id> live` (confirmed).");
        if (await GapperRules.RefuseActivationAsync(ctx, symbol) is { } refusal) return OperationResult.Blocked(refusal);

        var profile = args.Tune(ctx);
        var baseName = GapperRules.ResolveProfile(ctx, args.Profile).Name;
        var build = GapperRules.Compose(symbol, profile);
        var created = await GapperRules.CreateAsync(ctx, build, baseName + (args.AnyDial ? " (tuned)" : ""), args.Mode);
        var armed = await GapperRules.ArmAsync(ctx, created, new { Queued = true, Profile = profile });
        return armed.IsOk ? armed with { Human = $"Queued {symbol} from '{baseName}'.\n" + armed.Human } : armed;
    }
}

public sealed record GapperDisarmArgs
{
    [Argument(0, Name = "gapper", Description = "The gapper's symbol, id, id prefix or stamp id.")]
    public string Gapper { get; init; } = "";

    [Option("remove", Description = "Also delete the gapper row (only when it holds no position).")]
    public bool Remove { get; init; }
}

[Operation("gapper disarm", Summary = "Disarm a gapper so the Monitor stops evaluating it (refused while it holds a position); --remove deletes it.", Risk = OperationRisk.Write)]
[Example("idiotproof gapper disarm NVDA")]
[Example("idiotproof gapper disarm 0190a3f2 --remove")]
public sealed class GapperDisarmOperation : IOperation<GapperDisarmArgs>
{
    public async Task<OperationResult> ExecuteAsync(GapperDisarmArgs args, OperationContext ctx)
    {
        var s = await GapperRules.FindAsync(ctx, args.Gapper)
            ?? throw OperationException.Usage($"{args.Gapper.Trim().ToUpperInvariant()} has no gapper. `idiotproof gapper list` shows them.");
        var owner = ctx.RequireOwner();
        var repo = ctx.Get<StrategyRepository>();
        if (s.PositionQty != 0)
            return OperationResult.Blocked($"{s.Symbol} holds {s.PositionQty} shares — the Monitor must stay armed to manage the exit (stop, giveback, sell-by).");

        if (s.IsActive)
        {
            var off = await StrategyActivateOperation.SetActiveAsync(ctx, s, false);
            if (!off.IsOk) return off;
        }
        if (!args.Remove)
            return OperationResult.Ok(new { s.Id, s.Symbol, s.Title, IsActive = false, Removed = false },
                $"Disarmed {s.Symbol} '{s.Title}' ({StrategyRefs.ShortGuid(s.Id)}). Re-arm with `idiotproof gapper arm {StrategyRefs.ShortGuid(s.Id)}`.");

        var deleted = await repo.DeleteAsync(s.Id, owner, ctx.CancellationToken);
        return deleted == StrategyMutation.Ok
            ? OperationResult.Ok(new { s.Id, s.Symbol, s.Title, IsActive = false, Removed = true }, $"Disarmed and removed {s.Symbol} '{s.Title}'.")
            : OperationResult.Blocked($"Disarmed {s.Symbol}, but it could not be removed: {deleted}.");
    }
}
