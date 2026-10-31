using IdiotProof.Core.Trading;
using IdiotProof.DataFeeds;
using IdiotProof.Models;
using IdiotProof.Operations.Rendering;
using IdiotProof.Operations.Replay;
using IdiotProof.Operations.Strategies;
using IdiotProof.Scripting;
using IdiotProof.Strategies;

namespace IdiotProof.Operations.Gapper;

public sealed record GapperApplyTunedArgs
{
    [Argument(0, Name = "run", Description = "The run id `gapper backtest-day` printed (gb-xxxxxxxx).")]
    public string Run { get; init; } = "";

    [Option("arm", Description = "Arm it right away (one-per-symbol and the 3-gapper cap still apply).")]
    public bool Arm { get; init; }

    [Option("mode", Description = "Broker mode for the new gapper: paper (default) or sandbox.", Short = 'm')]
    public BrokerMode Mode { get; init; } = BrokerMode.Paper;
}

/// <summary>
/// The re-dial half of the loop: takes a saved backtest's hindsight-tuned profile and saves it as
/// a new gapper strategy for that symbol — only on this explicit command, never automatically.
/// </summary>
[Operation("gapper apply-tuned", Summary = "Save a backtest's hindsight-tuned profile as a new gapper strategy (inactive unless --arm).", Risk = OperationRisk.Write)]
[Example("idiotproof gapper apply-tuned gb-1a2b3c4d")]
[Example("idiotproof gapper apply-tuned gb-1a2b3c4d --arm")]
public sealed class GapperApplyTunedOperation : IOperation<GapperApplyTunedArgs>
{
    public async Task<OperationResult> ExecuteAsync(GapperApplyTunedArgs args, OperationContext ctx)
    {
        var owner = ctx.RequireOwner();
        var saved = await SavedRunStore.For(ctx).LoadAsync<GapperBacktestRun>(args.Run, "gb", owner, ctx.CancellationToken)
            ?? throw OperationException.Usage($"No gapper backtest '{args.Run}' of yours. Run `idiotproof gapper backtest-day <symbol> <date>` first; it prints the id.");
        var run = saved.Payload;
        if (run.Report.TunedProfile is not { } tuned)
            return OperationResult.Fail($"Backtest {saved.Id} has no tuned profile — the strategy never entered on {run.Day:yyyy-MM-dd}, so there is nothing to learn from.");

        if (args.Arm && await GapperRules.RefuseActivationAsync(ctx, run.Symbol) is { } refusal) return OperationResult.Blocked(refusal);

        var build = GapperRules.Compose(run.Symbol, tuned);
        var name = $"{run.ProfileName.Replace(" (tuned)", "")} (tuned {run.Day:MM-dd})";
        var created = await GapperRules.CreateAsync(ctx, build, name, args.Mode);
        var summary = $"Saved {run.Symbol} '{created.Title}' as {StrategyRefs.ShortGuid(created.Id)} from backtest {saved.Id}: " +
                      $"stop {GapperRules.Num(tuned.StopLossPercent)}%, giveback {GapperRules.Num(tuned.PeakGivebackPercent)}%.";
        if (!args.Arm)
            return OperationResult.Ok(new { created.Id, run.Symbol, created.Title, Profile = tuned, Mode = args.Mode.ToString(), Armed = false, Backtest = saved.Id },
                summary + $"\nIt is inactive; arm it with `idiotproof gapper arm {StrategyRefs.ShortGuid(created.Id)}`.");

        var armed = await GapperRules.ArmAsync(ctx, created, new { Backtest = saved.Id, Profile = tuned });
        return armed.IsOk ? armed with { Human = summary + "\n" + armed.Human } : armed;
    }
}
