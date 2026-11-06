using IdiotProof.Models;
using IdiotProof.Scripting;
using IdiotProof.Strategies.Backtesting;
using static IdiotProof.Strategies.Tests.Backtesting.BacktestTestData;

namespace IdiotProof.Strategies.Tests.Backtesting;

/// <summary>
/// Sequenced strategies in the replay (IP-T445): a step's exit arms its ThenWhen, which is watched
/// bar by bar while flat; when it holds the next step is armed, a Within deadline abandons the
/// sequence, Repeat restarts at step 0, and a single-step strategy replays exactly as before.
/// </summary>
public class StrategyBacktesterSequenceTests
{
    private const int Day = 24 * 60;

    /// <summary>Long rides 10 -> 12, gives back half the run on bar 5, then a lower high (pivot 12, pivot 11.5) is confirmed on bar 12.</summary>
    private static List<Candle> RideThenLowerHigh(int secondDayOffset = 0) =>
    [
        Bar(0, 10m, 10.2m, 9.9m, 10m),
        Bar(1, 10m, 11m, 9.95m, 11m),
        Bar(2, 11m, 11.5m, 10.9m, 11.4m),
        Bar(3, 11.4m, 12m, 11.3m, 11.8m),
        Bar(4, 11.8m, 11.6m, 11.1m, 11.2m),
        Bar(5, 11.2m, 11.3m, 10.7m, 10.8m),
        Bar(secondDayOffset + 6, 10.8m, 11m, 10.7m, 10.9m),
        Bar(secondDayOffset + 7, 10.9m, 11.2m, 10.8m, 11m),
        Bar(secondDayOffset + 8, 11m, 11.4m, 10.9m, 11.3m),
        Bar(secondDayOffset + 9, 11.3m, 11.5m, 11.2m, 11.4m),
        Bar(secondDayOffset + 10, 11.4m, 11.3m, 11m, 11.1m),
        Bar(secondDayOffset + 11, 11.1m, 11.1m, 10.8m, 10.9m),
        Bar(secondDayOffset + 12, 10.9m, 10.9m, 10.6m, 10.7m),
        Bar(secondDayOffset + 13, 10.7m, 10.8m, 10.5m, 10.6m),
        Bar(secondDayOffset + 14, 10.6m, 10.6m, 9.4m, 9.6m),
    ];

    private static StrategyBuilder RideThenFade(int? withinDays = null)
    {
        var b = Stock.Ticker("TST").Step("ride")
            .IsPriceBetween(1, 100).Long().StopLoss(5).PeakGiveback(50)
            .ThenWhen(c => c.IsLowerHigh());
        if (withinDays is { } days) b.Within(days);
        return b.Step("fade").IsPriceBetween(1, 100).Short().StopLoss(13).TakeProfit(9.5);
    }

    [Test]
    public void LongExitsOnGiveback_LowerHighForms_ShortStepEnters()
    {
        var bars = RideThenLowerHigh();
        var report = StrategyBacktester.Run(RideThenFade().Build(), bars);

        Assert.That(report.Trades, Has.Count.EqualTo(2));
        var ride = report.Trades[0];
        var fade = report.Trades[1];
        Assert.Multiple(() =>
        {
            Assert.That(report.StepCount, Is.EqualTo(2));
            Assert.That(ride.Step, Is.EqualTo(0));
            Assert.That(ride.StepName, Is.EqualTo("ride"));
            Assert.That(ride.Direction, Is.EqualTo(TradeDirection.Long));
            Assert.That(ride.EntryPrice, Is.EqualTo(10m));
            Assert.That(ride.ExitReason, Is.EqualTo("Peak giveback"));
            Assert.That(ride.ExitUtc, Is.EqualTo(bars[5].StartUtc));

            Assert.That(report.StepTransitions, Is.EqualTo(new[] { new StepTransition(bars[12].StartUtc, 0, 1) }));

            Assert.That(fade.Step, Is.EqualTo(1));
            Assert.That(fade.StepName, Is.EqualTo("fade"));
            Assert.That(fade.Direction, Is.EqualTo(TradeDirection.Short));
            Assert.That(fade.Cycle, Is.EqualTo(1), "a later step is the same cycle");
            Assert.That(fade.EntryUtc, Is.EqualTo(bars[13].StartUtc), "the step entered on the bar after the transition");
            Assert.That(fade.EntryPrice, Is.EqualTo(10.6m));
            Assert.That(fade.Exits[^1].Price, Is.EqualTo(9.5m));
            Assert.That(fade.PnL, Is.EqualTo((10.6m - 9.5m) * fade.Quantity));

            var waiting = report.ConditionTable.Where(r => r.AwaitingTransition).ToList();
            Assert.That(waiting.Select(r => r.Utc), Is.EqualTo(bars.Skip(6).Take(7).Select(b => b.StartUtc)));
            Assert.That(waiting.All(r => r.Step == 0 && r.Labels.SequenceEqual(["IsLowerHigh()"])), Is.True);
            Assert.That(waiting[^1].AllSatisfied, Is.True);
            Assert.That(waiting.Take(6).All(r => !r.AllSatisfied), Is.True);
            Assert.That(report.ConditionTable.Where(r => r.Utc >= bars[13].StartUtc).All(r => r.Step == 1), Is.True);
        });
    }

    [Test]
    public void WithinDeadline_AbandonsTheSequence_WhenTheTransitionComesTooLate()
    {
        var bars = RideThenLowerHigh(secondDayOffset: Day);
        var report = StrategyBacktester.Run(RideThenFade(withinDays: 1).Build(), bars);

        Assert.Multiple(() =>
        {
            Assert.That(report.Trades, Has.Count.EqualTo(1));
            Assert.That(report.Trades[0].Step, Is.EqualTo(0));
            Assert.That(report.StepTransitions, Is.Empty);
            Assert.That(report.ConditionTable.Count(r => r.AwaitingTransition), Is.EqualTo(1), "the first bar past the deadline ends the wait");
            Assert.That(report.ConditionTable.Skip(7).All(r => r.Step == 0 && !r.OpenedTrade), Is.True, "no step-0 re-entry without Repeat");
        });
    }

    [Test]
    public void WithinDeadline_SpanningDays_StillTransitionsWhenMet()
    {
        var bars = RideThenLowerHigh(secondDayOffset: Day);
        var report = StrategyBacktester.Run(RideThenFade(withinDays: 3).Build(), bars);

        Assert.Multiple(() =>
        {
            Assert.That(report.Trades.Select(t => t.Step), Is.EqualTo(new[] { 0, 1 }));
            Assert.That(report.StepTransitions.Single(), Is.EqualTo(new StepTransition(bars[12].StartUtc, 0, 1)));
            Assert.That(report.Trades[1].EntryUtc.Date, Is.Not.EqualTo(report.Trades[0].EntryUtc.Date), "the sequence crossed a day");
        });
    }

    [Test]
    public void Repeat_RestartsAtStepZero_AfterTheLastStepExits()
    {
        var bars = RideThenLowerHigh();
        bars.Add(Bar(15, 9.6m, 9.7m, 9.5m, 9.6m));
        bars.Add(Bar(16, 9.6m, 9.7m, 9.5m, 9.6m));
        var report = StrategyBacktester.Run(RideThenFade().Repeat().Build(), bars);

        Assert.Multiple(() =>
        {
            Assert.That(report.Trades.Select(t => (t.Step, t.Cycle)), Is.EqualTo(new[] { (0, 1), (1, 1), (0, 2) }));
            Assert.That(report.Trades[2].Direction, Is.EqualTo(TradeDirection.Long));
            Assert.That(report.Trades[2].EntryUtc, Is.EqualTo(bars[15].StartUtc));
            Assert.That(report.StepTransitions, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void Repeat_RestartsAtStepZero_AfterADeadlineExpires_AndMaxCyclesCapsIt()
    {
        var bars = RideThenLowerHigh(secondDayOffset: Day);
        var unlimited = StrategyBacktester.Run(RideThenFade(withinDays: 1).Repeat().Build(), bars);
        var capped = StrategyBacktester.Run(RideThenFade(withinDays: 1).Repeat(1).Build(), bars);

        Assert.Multiple(() =>
        {
            Assert.That(unlimited.Trades.Select(t => (t.Step, t.Cycle)).Take(2), Is.EqualTo(new[] { (0, 1), (0, 2) }));
            Assert.That(unlimited.Trades[1].EntryUtc, Is.EqualTo(bars[7].StartUtc), "the expiry bar re-arms step 0; it enters on the next bar");
            Assert.That(capped.Trades, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void EmptyThenWhen_ArmsTheNextStepOnTheBarAfterTheExit()
    {
        var def = Stock.Ticker("TST").Step("one").IsPriceBetween(1, 100).Long().StopLoss(5).TakeProfit(11)
            .Step("two").IsPriceBetween(1, 100).Short().StopLoss(13).TakeProfit(9).Build();
        var bars = new List<Candle>
        {
            Bar(0, 10m, 10.2m, 9.9m, 10m),  // enter long @ 10
            Bar(1, 10m, 11.5m, 9.9m, 11m),  // target 11 -> exit
            Bar(2, 11m, 11.1m, 10.9m, 11m), // flat, no condition -> transition
            Bar(3, 11m, 11.1m, 10.9m, 11m), // enter short @ 11
            Bar(4, 11m, 11.1m, 8.9m, 9m),   // target 9 -> exit
        };

        var report = StrategyBacktester.Run(def, bars);

        Assert.Multiple(() =>
        {
            Assert.That(report.StepTransitions.Single(), Is.EqualTo(new StepTransition(bars[2].StartUtc, 0, 1)));
            Assert.That(report.Trades.Select(t => (t.Step, t.EntryUtc)), Is.EqualTo(new[] { (0, bars[0].StartUtc), (1, bars[3].StartUtc) }));
            Assert.That(report.ConditionTable[2].AwaitingTransition, Is.True);
            Assert.That(report.ConditionTable[2].Labels, Is.Empty);
        });
    }

    [Test]
    public void LaterStepPatternLatch_CannotTripBeforeItsStepIsArmed()
    {
        // Step two's Breakout(12) would latch on bar 3 if it were tracked from the start.
        var def = Stock.Ticker("TST").Step("one").IsPriceBetween(1, 100).Long().StopLoss(5).PeakGiveback(50)
            .ThenWhen(c => c.IsLowerHigh())
            .Step("two").Breakout(12).Short().StopLoss(13).TakeProfit(9.5).Build();
        var report = StrategyBacktester.Run(def, RideThenLowerHigh());

        Assert.Multiple(() =>
        {
            Assert.That(report.StepTransitions, Has.Count.EqualTo(1));
            Assert.That(report.Trades, Has.Count.EqualTo(1), "the breakout above 12 happened before step two was armed");
            Assert.That(report.Triggers.Any(f => f.Condition == "Breakout(12)"), Is.False);
        });
    }

    [Test]
    public void SingleStepStrategy_ReplaysExactlyAsBefore()
    {
        var def = Stock.Ticker("TST").IsPriceBetween(1, 100).Long().StopLoss(5).TakeProfit(11).Repeat(2).Build();
        var bars = new List<Candle>
        {
            Bar(0, 10m, 10.2m, 9.9m, 10m), Bar(1, 10m, 11.5m, 9.9m, 11m),
            Bar(2, 10m, 10.2m, 9.9m, 10m), Bar(3, 10m, 11.5m, 9.9m, 11m),
            Bar(4, 10m, 10.2m, 9.9m, 10m), Bar(5, 10m, 11.5m, 9.9m, 11m),
        };

        var report = StrategyBacktester.Run(def, bars);

        Assert.Multiple(() =>
        {
            Assert.That(report.StepCount, Is.EqualTo(1));
            Assert.That(report.StepTransitions, Is.Empty);
            Assert.That(report.Trades.Select(t => (t.Cycle, t.Step, t.StepName, t.EntryPrice, t.PnL)),
                Is.EqualTo(new[] { (1, 0, (string?)null, 10m, 100m), (2, 0, (string?)null, 10m, 100m) }));
            Assert.That(report.ConditionTable, Has.Count.EqualTo(6));
            Assert.That(report.ConditionTable.All(r => r.Step == 0 && !r.AwaitingTransition), Is.True);
            Assert.That(report.ConditionTable.Select(r => r.OpenedTrade), Is.EqualTo(new[] { true, false, true, false, false, false }));
            Assert.That(report.ConditionTable.Select(r => r.InPosition), Is.EqualTo(new[] { false, true, false, true, false, false }));
            Assert.That(report.Triggers.Select(f => (f.Cycle, f.OpenedPosition)), Is.EqualTo(new[] { (1, true), (2, true) }));
        });
    }
}
