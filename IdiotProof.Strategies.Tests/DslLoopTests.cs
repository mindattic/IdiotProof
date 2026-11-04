using IdiotProof.Models;
using IdiotProof.Scripting;
using IdiotProof.Strategies.Backtesting;
using static IdiotProof.Strategies.Tests.Backtesting.BacktestTestData;

namespace IdiotProof.Strategies.Tests;

/// <summary>DSL loops (IP-T370): Repeat(n), While()/EndWhile()/ExitWhenFalse(), For/ForEach + TakeProfitEach.</summary>
public class DslLoopTests
{
    private static StrategyDefinition Looped() =>
        Stock.Ticker("TST")
            .While().IsPriceBetween(20, 100).EndWhile()
            .ExitWhenFalse()
            .IsPriceBetween(1, 100)
            .Long()
            .StopLoss(5)
            .ForEach(30, 31, 32, 33).TakeProfitEach()
            .Repeat(2)
            .Build();

    [Test]
    public void While_MovesItsConditionsOutOfEntryConditions()
    {
        var def = Looped();
        Assert.Multiple(() =>
        {
            Assert.That(def.WhileConditions.Select(c => c.ToScript()), Is.EqualTo(new[] { "IsPriceBetween(20, 100)" }));
            Assert.That(def.EntryConditions.Select(c => c.ToScript()), Is.EqualTo(new[] { "IsPriceBetween(1, 100)" }));
            Assert.That(def.ExitWhenWhileFails, Is.True);
            Assert.That(def.MaxCycles, Is.EqualTo(2));
        });
    }

    [Test]
    public void Build_ClosesAnUnclosedWhile()
    {
        var def = Stock.Ticker("TST").IsPriceBetween(1, 100).While().IsAboveVwap().Long().StopLoss(1).Build();
        Assert.That(def.WhileConditions, Has.Count.EqualTo(1));
        Assert.That(def.EntryConditions, Has.Count.EqualTo(1));
    }

    [Test]
    public void While_LambdaForm_AddsAWhileCondition()
    {
        var def = Stock.Ticker("TST").While(c => c.IsAboveVwap()).Long().StopLoss(1).Build();
        Assert.That(def.WhileConditions.Single().ToScript(), Is.EqualTo("IsAboveVwap()"));
    }

    [Test]
    public void For_ExpandsToAnEqualSplitLadder_LastTakesTheRemainder()
    {
        var four = Stock.Ticker("TST").For(10, 0.5, 4).TakeProfitEach().Build();
        var three = Stock.Ticker("TST").For(10, 1, 3).TakeProfitEach().Build();
        Assert.Multiple(() =>
        {
            Assert.That(four.TakeProfitTargets.Select(t => t.Price), Is.EqualTo(new[] { 10, 10.5, 11, 11.5 }));
            Assert.That(four.TakeProfitTargets.Select(t => t.PercentToSell), Is.EqualTo(new[] { 25, 25, 25, 25 }));
            Assert.That(three.TakeProfitTargets.Select(t => t.PercentToSell), Is.EqualTo(new[] { 33, 33, 34 }));
            Assert.That(four.TakeProfitPrice, Is.EqualTo(10));
        });
    }

    [Test]
    public void TextRoundTrip_PreservesLoopsAndAFourTargetLadder()
    {
        var def = Looped();
        var script = def.ToScript();
        var back = ScriptParser.ParseScript(script)!;

        Assert.Multiple(() =>
        {
            Assert.That(back.ToScript(), Is.EqualTo(script));
            Assert.That(back.WhileConditions, Has.Count.EqualTo(1));
            Assert.That(back.ExitWhenWhileFails, Is.True);
            Assert.That(back.MaxCycles, Is.EqualTo(2));
            Assert.That(back.TakeProfitTargets.Select(t => (t.Price, t.PercentToSell)),
                Is.EqualTo(def.TakeProfitTargets.Select(t => (t.Price, t.PercentToSell))));
        });
    }

    [Test]
    public void TextRoundTrip_ForAndForEachVerbsParse()
    {
        var def = ScriptParser.ParseScript("Ticker(\"TST\").Long().StopLoss(1).For(10, 0.5, 4).TakeProfitEach()")!;
        Assert.That(def.TakeProfitTargets.Select(t => t.Price), Is.EqualTo(new[] { 10, 10.5, 11, 11.5 }));
    }

    [Test]
    public void TextRoundTrip_ShorthandTakeProfitStaysShorthand()
    {
        var script = Stock.Ticker("TST").Long().StopLoss(1).TakeProfit(2, 3, 4).Build().ToScript();
        Assert.That(script, Does.Contain("TakeProfit(2, 3, 4)"));
        Assert.That(script, Does.Not.Contain("AddTarget"));
    }

    [Test]
    public void JsonRoundTrip_PreservesLoops()
    {
        var def = Looped();
        var back = StrategyJson.Deserialize(StrategyJson.Serialize(def));
        Assert.Multiple(() =>
        {
            Assert.That(back.WhileConditions.Select(c => c.ToScript()), Is.EqualTo(def.WhileConditions.Select(c => c.ToScript())));
            Assert.That(back.ExitWhenWhileFails, Is.True);
            Assert.That(back.MaxCycles, Is.EqualTo(2));
            Assert.That(back.ToScript(), Is.EqualTo(def.ToScript()));
        });
    }

    [Test]
    public void Json_StrategyWithoutLoops_HasNoLoopKeys()
    {
        var json = StrategyJson.Serialize(Stock.Ticker("TST").IsAboveVwap().Long().StopLoss(1).Repeat().Build());
        Assert.That(json, Does.Not.Contain("maxCycles").And.Not.Contain("whileConditions").And.Not.Contain("exitWhenWhileFails"));
    }

    // Enter at close 10, target 11 next bar, re-enter, … — a cycle every two bars.
    private static List<Candle> CyclingBars() =>
    [
        Bar(0, 10m, 10.2m, 9.9m, 10m), Bar(1, 10m, 11.5m, 9.9m, 11m),
        Bar(2, 10m, 10.2m, 9.9m, 10m), Bar(3, 10m, 11.5m, 9.9m, 11m),
        Bar(4, 10m, 10.2m, 9.9m, 10m), Bar(5, 10m, 11.5m, 9.9m, 11m),
    ];

    [Test]
    public void Backtest_RepeatN_CapsEntries()
    {
        var capped = Stock.Ticker("TST").IsPriceBetween(1, 100).Long().StopLoss(5).TakeProfit(11).Repeat(2).Build();
        var unlimited = Stock.Ticker("TST").IsPriceBetween(1, 100).Long().StopLoss(5).TakeProfit(11).Repeat().Build();

        Assert.Multiple(() =>
        {
            Assert.That(StrategyBacktester.Run(capped, CyclingBars()).Trades, Has.Count.EqualTo(2));
            Assert.That(StrategyBacktester.Run(unlimited, CyclingBars()).Trades, Has.Count.EqualTo(3));
        });
    }

    [Test]
    public void Backtest_While_PausesEntriesUntilItHolds()
    {
        var def = Stock.Ticker("TST").While().IsPriceBetween(20, 100).EndWhile()
            .IsPriceBetween(1, 100).Long().StopLoss(5).Build();
        var candles = new List<Candle>
        {
            Bar(0, 10m, 10.2m, 9.9m, 10m),  // entry condition true, While false -> paused
            Bar(1, 10m, 10.2m, 9.9m, 10m),
            Bar(2, 24m, 25.2m, 23.9m, 25m), // While holds -> ENTER @ 25
            Bar(3, 25m, 25.2m, 24.9m, 25m),
        };

        var report = StrategyBacktester.Run(def, candles);

        Assert.That(report.Trades, Has.Count.EqualTo(1));
        Assert.That(report.Trades[0].EntryPrice, Is.EqualTo(25m));
    }

    [Test]
    public void Backtest_ExitWhenFalse_ExitsWhenTheWhileStopsHolding()
    {
        var def = Stock.Ticker("TST").While().IsPriceBetween(20, 100).EndWhile().ExitWhenFalse()
            .IsPriceBetween(1, 100).Long().StopLoss(5).Build();
        var candles = new List<Candle>
        {
            Bar(0, 25m, 25.2m, 24.9m, 25m), // ENTER @ 25
            Bar(1, 25m, 25.1m, 14m, 15m),   // While false (15 < 20), stop 5 untouched -> exit @ 15
            Bar(2, 15m, 26m, 14.9m, 25m),
        };

        var report = StrategyBacktester.Run(def, candles);

        Assert.That(report.Trades, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(report.Trades[0].ExitReason, Is.EqualTo("While condition failed"));
            Assert.That(report.Trades[0].Exits[^1].Price, Is.EqualTo(15m));
        });
    }

    [Test]
    public void BranchResolver_CarriesLoopFields()
    {
        var def = Stock.Ticker("TST").While().IsAboveVwap().EndWhile().ExitWhenFalse()
            .IsPriceBetween(1, 100).IsPriceBetween(20, 100).Then(b => b.StopLoss(5)).Else(b => b.StopLoss(9))
            .Repeat(3).Build();

        var resolved = StrategyBranchResolver.Resolve(def, new IdiotProof.Shared.IndicatorSnapshot { Price = 10 });

        Assert.Multiple(() =>
        {
            Assert.That(resolved, Is.Not.SameAs(def));
            Assert.That(resolved.WhileConditions, Has.Count.EqualTo(1));
            Assert.That(resolved.ExitWhenWhileFails, Is.True);
            Assert.That(resolved.MaxCycles, Is.EqualTo(3));
        });
    }
}
