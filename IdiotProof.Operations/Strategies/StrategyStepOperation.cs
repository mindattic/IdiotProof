using IdiotProof.Core.Data;
using IdiotProof.Core.Services;
using IdiotProof.Operations.Rendering;
using IdiotProof.Scripting;

namespace IdiotProof.Operations.Strategies;

/// <summary>Where a sequenced strategy stands, in words shared by <c>strategy show</c> and <c>strategy step</c>.</summary>
public static class StrategySequence
{
    public static string Describe(Strategy s, StrategyDefinition root)
    {
        var index = Math.Clamp(s.StepIndex, 0, root.StepCount - 1);
        var step = root.StepAt(index);
        var name = step.StepName is { Length: > 0 } n ? $" \"{n}\"" : "";
        var state = s.PositionQty > 0 ? "holding"
            : s.AwaitingTransition ? $"awaiting transition since {Fmt.Et(s.LastExitedUtc)}"
            : s.StepArmedUtc is { } armed ? $"armed {Fmt.Et(armed)}"
            : "armed";
        return $"step {index + 1}/{root.StepCount}{name} ({state})";
    }
}

public sealed record StrategyStepArgs
{
    [Argument(0, Name = "strategy", Description = "Id, id prefix, stamp id (IP-XXXX-XXXX) or symbol.")]
    public string Strategy { get; init; } = "";

    [Option("reset", Description = "Put the sequence back at step 1 (refused while it holds a position).")]
    public bool Reset { get; init; }
}

[Operation("strategy step", Summary = "Where a sequenced strategy stands (step k/N, armed or awaiting its transition); --reset starts it over at step 1.", Risk = OperationRisk.Write)]
[Example("idiotproof strategy step NVDA")]
[Example("idiotproof strategy step NVDA --reset")]
public sealed class StrategyStepOperation : IOperation<StrategyStepArgs>
{
    public async Task<OperationResult> ExecuteAsync(StrategyStepArgs args, OperationContext ctx)
    {
        var s = await StrategyRefs.ResolveAsync(ctx, args.Strategy);
        var def = StrategyRefs.RequireDefinition(s);
        if (!def.IsSequence)
            return OperationResult.Ok(new { s.Id, s.Symbol, s.Title, StepIndex = 0, StepCount = 1, s.AwaitingTransition, Reset = false },
                $"{s.Symbol} '{s.Title}' is a single-step strategy; it has no sequence to step through.");

        if (!args.Reset)
            return OperationResult.Ok(Payload(s, def, reset: false), $"{s.Symbol} '{s.Title}': {StrategySequence.Describe(s, def)}.");

        var result = await ctx.Get<StrategyRepository>().ResetSequenceAsync(s.Id, ctx.RequireOwner(), ctx.CancellationToken);
        switch (result)
        {
            case StrategyMutation.Ok:
                var fresh = await ctx.Get<StrategyRepository>().GetByIdAsync(s.Id, ctx.CancellationToken) ?? s;
                return OperationResult.Ok(Payload(fresh, def, reset: true),
                    $"{s.Symbol} '{s.Title}' is back at step 1/{def.StepCount}{Name(def)}" +
                    (fresh.IsActive ? " — the Monitor evaluates it from the top on its next tick." : " (inactive)."));
            case StrategyMutation.PositionOpen:
                return OperationResult.Blocked($"{s.Symbol} holds {s.PositionQty} shares in step {Math.Clamp(s.StepIndex, 0, def.StepCount - 1) + 1}; that step must manage the exit first. Flatten before resetting.");
            case StrategyMutation.NotOwner:
                return OperationResult.Blocked("That strategy belongs to another user.");
            case StrategyMutation.NotFound:
                return OperationResult.Fail("Strategy not found.");
            default:
                return OperationResult.Fail($"{s.Symbol} '{s.Title}' was not changed: {result}.");
        }
    }

    private static string Name(StrategyDefinition root) => root.StepName is { Length: > 0 } n ? $" \"{n}\"" : "";

    private static object Payload(Strategy s, StrategyDefinition def, bool reset) => new
    {
        s.Id, s.Symbol, s.Title, s.StepIndex, def.StepCount,
        StepName = def.StepAt(Math.Clamp(s.StepIndex, 0, def.StepCount - 1)).StepName,
        s.AwaitingTransition, s.StepArmedUtc, s.LastExitedUtc, s.IsActive, Reset = reset,
    };
}
