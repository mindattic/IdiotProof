using IdiotProof.Scripting;

namespace IdiotProof.Strategies.Tests;

/// <summary>
/// EmaPeriodCollector is the single source of truth for "which EMA series must
/// the snapshot pre-compute" — DslStrategy (live), MonitorWorker (console), and
/// StrategyBacktester (replay) all call it. Three hand-rolled copies previously
/// drifted; the backtester's copy skipped ConditionalBlocks entirely.
/// </summary>
public class EmaPeriodCollectorTests
{
    [Test]
    public void Collect_FindsPeriods_InBaseEntryConditions()
    {
        var def = Stock.Ticker("TEST")
            .IsEmaAbove(21)
            .IsBetweenEma(9, 31)
            .Long()
            .StopLoss(4.0)
            .TakeProfit(6.0)
            .Build();

        var periods = EmaPeriodCollector.Collect(def);

        Assert.That(periods, Is.SupersetOf(new[] { 21, 9, 31 }));
    }

    [Test]
    public void Collect_FindsPeriods_InsideConditionalBranches()
    {
        // Regression: StrategyBacktester's private copy of this walk skipped
        // ConditionalBlocks, so a strategy whose only EMA references live
        // inside .If/.ElseIf branches replayed against missing EMA series.
        var def = Stock.Ticker("TEST")
            .Long()
            .StopLoss(4.0)
            .TakeProfit(6.0)
            .Build();

        var branch = new ConditionalBranch
        {
            Condition = new IndicatorCondition(IndicatorType.EmaAbove, 65),
        };
        branch.Overrides.EntryConditions.Add(new IndicatorCondition(IndicatorType.BetweenEma, 7, 42));

        var block = new ConditionalBlock();
        block.Branches.Add(branch);
        def.ConditionalBlocks.Add(block);

        var periods = EmaPeriodCollector.Collect(def);

        Assert.That(periods, Is.SupersetOf(new[] { 65, 7, 42 }));
    }

    [Test]
    public void Collect_WalksComposedConditions()
    {
        var def = Stock.Ticker("TEST")
            .Long()
            .StopLoss(4.0)
            .TakeProfit(6.0)
            .Build();

        // IsEmaAbove(9).And(IsEmaBelow(200).Not()) — nested composition.
        var composed = new AndCondition(
            new IndicatorCondition(IndicatorType.EmaAbove, 9),
            new NotCondition(new IndicatorCondition(IndicatorType.EmaBelow, 200)));
        def.EntryConditions.Add(composed);

        var periods = EmaPeriodCollector.Collect(def);

        Assert.That(periods, Is.SupersetOf(new[] { 9, 200 }));
    }
}
