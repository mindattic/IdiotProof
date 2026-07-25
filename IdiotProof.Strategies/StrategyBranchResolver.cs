using IdiotProof.Models;
using IdiotProof.Scripting;
using IdiotProof.Shared;

namespace IdiotProof.Strategies;

/// <summary>
/// Resolves a strategy's <see cref="StrategyDefinition.ConditionalBlocks"/> against the
/// current market snapshot and returns a fully-applied working copy of the definition.
///
/// The base definition is never mutated.  When there are no ConditionalBlocks the
/// base reference is returned as-is (zero allocation on the hot path).
///
/// This is extracted from the private <c>DslStrategy.ResolveBranches</c> so that
/// <see cref="IdiotProof.Monitor.MonitorWorker"/> can call it during live evaluation —
/// previously the Monitor evaluated <c>def.EntryConditions</c> directly and silently
/// ignored every ConditionalBlock authored in the UI.
/// </summary>
public static class StrategyBranchResolver
{
    public static StrategyDefinition Resolve(StrategyDefinition baseDef, IndicatorSnapshot snapshot)
    {
        if (baseDef.ConditionalBlocks.Count == 0)
            return baseDef;

        var clone = new StrategyDefinition
        {
            Id                     = baseDef.Id,
            Symbol                 = baseDef.Symbol,
            Name                   = baseDef.Name,
            Session                = baseDef.Session,
            Quantity               = baseDef.Quantity,
            NotionalAmount         = baseDef.NotionalAmount,
            Direction              = baseDef.Direction,
            TakeProfitPrice        = baseDef.TakeProfitPrice,
            TakeProfitPercent      = baseDef.TakeProfitPercent,
            StopLossPrice          = baseDef.StopLossPrice,
            StopLossPercent        = baseDef.StopLossPercent,
            TrailingStopPercent    = baseDef.TrailingStopPercent,
            ExitTime               = baseDef.ExitTime,
            PeakGivebackPercent    = baseDef.PeakGivebackPercent,
            PeakGivebackArmTime    = baseDef.PeakGivebackArmTime,
            ExitAtPriorHigh        = baseDef.ExitAtPriorHigh,
            RollingHighDays        = baseDef.RollingHighDays,
            RollingHighBuffer      = baseDef.RollingHighBuffer,
            RollingLowDays         = baseDef.RollingLowDays,
            RollingLowBuffer       = baseDef.RollingLowBuffer,
            EntryRollingLowDays    = baseDef.EntryRollingLowDays,
            EntryRollingLowBuffer  = baseDef.EntryRollingLowBuffer,
            EntryRollingHighDays   = baseDef.EntryRollingHighDays,
            EntryRollingHighBuffer = baseDef.EntryRollingHighBuffer,
            IsAutonomous           = baseDef.IsAutonomous,
            IsAdaptive             = baseDef.IsAdaptive,
            ShouldRepeat           = baseDef.ShouldRepeat,
        };
        foreach (var c in baseDef.EntryConditions)    clone.EntryConditions.Add(c);
        foreach (var t in baseDef.TakeProfitTargets)  clone.TakeProfitTargets.Add(t);

        foreach (var block in baseDef.ConditionalBlocks)
        {
            var matched = block.Evaluate(snapshot);
            matched?.Overrides.ApplyTo(clone);
        }

        return clone;
    }
}
