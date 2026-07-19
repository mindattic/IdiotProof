namespace IdiotProof.Scripting;

/// <summary>
/// Canonical walk of every EMA period a strategy references, so indicator
/// snapshot builders can pre-compute exactly those series. Covers base
/// EntryConditions (which include Filters-phase conditions) plus every
/// ConditionalBlock branch condition and branch override — a strategy whose
/// only EMA reference lives inside an .If/.ElseIf branch still gets its
/// periods computed.
///
/// This is the single source of truth. DslStrategy (live evaluation),
/// MonitorWorker (console progress), and StrategyBacktester (replay) all call
/// this — three hand-rolled copies previously drifted, and the backtester's
/// copy missed ConditionalBlocks entirely.
/// </summary>
public static class EmaPeriodCollector
{
    /// <summary>All EMA periods referenced anywhere in the strategy.</summary>
    public static HashSet<int> Collect(StrategyDefinition def)
    {
        var periods = new HashSet<int>();
        foreach (var c in WalkAllConditions(def))
        {
            if (c is not IndicatorCondition ic) continue;

            if (ic.Type is IndicatorType.EmaAbove
                        or IndicatorType.EmaBelow
                        or IndicatorType.ReclaimEma
                && ic.Parameter is { } p)
            {
                periods.Add((int)p);
            }
            else if (ic.Type is IndicatorType.BetweenEma or IndicatorType.EmaStack
                     && ic.Parameter is { } p1 && ic.Parameter2 is { } p2)
            {
                periods.Add((int)p1);
                periods.Add((int)p2);
            }
        }
        return periods;
    }

    private static IEnumerable<ICondition> WalkAllConditions(StrategyDefinition def)
    {
        foreach (var c in def.EntryConditions)
            foreach (var sub in WalkOne(c)) yield return sub;

        foreach (var block in def.ConditionalBlocks)
            foreach (var branch in block.Branches)
            {
                if (branch.Condition is not null)
                    foreach (var sub in WalkOne(branch.Condition)) yield return sub;
                foreach (var c in branch.Overrides.EntryConditions)
                    foreach (var sub in WalkOne(c)) yield return sub;
            }
    }

    private static IEnumerable<ICondition> WalkOne(ICondition c)
    {
        yield return c;
        switch (c)
        {
            case AndCondition a:
                foreach (var x in WalkOne(a.Left)) yield return x;
                foreach (var x in WalkOne(a.Right)) yield return x;
                break;
            case OrCondition o:
                foreach (var x in WalkOne(o.Left)) yield return x;
                foreach (var x in WalkOne(o.Right)) yield return x;
                break;
            case NotCondition n:
                foreach (var x in WalkOne(n.Inner)) yield return x;
                break;
        }
    }
}
