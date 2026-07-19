using IdiotProof.Models;
using IdiotProof.Scripting;
using IdiotProof.Shared;
using IdiotProof.Strategies.Backtesting;

namespace IdiotProof.Strategies.Tests;

/// <summary>
/// IP-A18 regressions: entry conditions must FAIL CLOSED when the data they
/// need is absent (IP-LAW-1 — an uncomputable gate blocks the fire, never
/// waves it through), and cross/round-trip semantics must survive the
/// Monitor's per-tick re-materialization of the definition.
/// </summary>
public class ConditionFailClosedTests
{
    /// <summary>A snapshot with price data only — no MACD, ADX, or prior bar.</summary>
    private static IndicatorSnapshot Sparse() => new()
    {
        Symbol = "TEST",
        Timestamp = new DateTime(2026, 7, 17, 8, 0, 0, DateTimeKind.Utc),
        Price = 10.0,
    };

    [Test]
    public void MacdBearish_WithoutMacdData_FailsClosed()
    {
        // MacdLine/SignalLine are null under ~26 bars (early premarket — the
        // gapper window!). null > null is false, so the old bare
        // !IsMacdBullish made IsMacdBearish() pass on every data-starved bar.
        Assert.That(new IndicatorCondition(IndicatorType.MacdBearish).Evaluate(Sparse()), Is.False);
    }

    [Test]
    public void DiNegative_WithoutAdxData_FailsClosed()
    {
        // Same fail-open shape: PlusDI/MinusDI null under ~28 bars made
        // IsDiNegative() spuriously true.
        Assert.That(new IndicatorCondition(IndicatorType.DiNegative).Evaluate(Sparse()), Is.False);
    }

    [Test]
    public void MacdAndDi_WithData_StillEvaluateBothDirections()
    {
        var bearish = Sparse();
        bearish.MacdLine = 1.0; bearish.SignalLine = 2.0;
        bearish.PlusDI = 10.0; bearish.MinusDI = 20.0;

        Assert.Multiple(() =>
        {
            Assert.That(new IndicatorCondition(IndicatorType.MacdBearish).Evaluate(bearish), Is.True);
            Assert.That(new IndicatorCondition(IndicatorType.MacdBullish).Evaluate(bearish), Is.False);
            Assert.That(new IndicatorCondition(IndicatorType.DiNegative).Evaluate(bearish), Is.True);
            Assert.That(new IndicatorCondition(IndicatorType.DiPositive).Evaluate(bearish), Is.False);
        });
    }

    [Test]
    public void BreakoutAndPullback_DirectEvaluation_FailClosed()
    {
        // The Breakout/Pullback latches live only in the backtester's
        // TrackedTrigger state machine. Direct evaluation (the Monitor walks
        // EntryConditions per tick) used to return TRUE unconditionally — a
        // live strategy's core trigger was always-satisfied and it fired on
        // the remaining conditions alone.
        var rich = Sparse();
        rich.PriorPrice = 9.0;

        Assert.Multiple(() =>
        {
            Assert.That(new PatternCondition(PatternType.Breakout, 5.0).Evaluate(rich), Is.False);
            Assert.That(new PatternCondition(PatternType.Pullback).Evaluate(rich), Is.False);
        });
    }

    [Test]
    public void BreaksAbove_FreshInstancePerTick_DetectsTheCrossViaPriorPrice()
    {
        // The Monitor re-materializes the definition from canonical JSON every
        // tick, so PriceLevelCondition is a FRESH instance each evaluation and
        // its instance-held previousPrice is always null — BreaksAbove could
        // never fire. The snapshot's PriorPrice restores cross semantics.
        var crossing = Sparse();
        crossing.PriorPrice = 9.95;   // prior bar at/below the level
        crossing.Price = 10.05;       // current bar above → cross

        var noCross = Sparse();
        noCross.PriorPrice = 10.02;   // already above on the prior bar
        noCross.Price = 10.05;

        Assert.Multiple(() =>
        {
            Assert.That(new PriceLevelCondition(PriceLevelType.BreaksAbove, 10.0).Evaluate(crossing), Is.True,
                "a fresh instance must see the bar-over-bar cross through the snapshot");
            Assert.That(new PriceLevelCondition(PriceLevelType.BreaksAbove, 10.0).Evaluate(noCross), Is.False,
                "no cross — prior bar was already above the level");
        });
    }

    [Test]
    public void Parser_EntryVerb_RoundTrips()
    {
        // The serializer emits Entry(12.5) and the reflected catalog teaches
        // it to Claude, but the parser had no case — the price gate silently
        // vanished on every text round trip.
        var def = ScriptParser.ParseScript("Ticker(\"TST\")\n    .Entry(12.5)\n    .Long()");

        Assert.That(def, Is.Not.Null);
        var cond = def!.EntryConditions.OfType<PriceCondition>().SingleOrDefault();
        Assert.That(cond, Is.Not.Null, "Entry(12.5) must survive the parse");
        Assert.That(cond!.Price, Is.EqualTo(12.5));
        Assert.That(cond.ToScript(), Is.EqualTo("Entry(12.5)"), "and re-serialize losslessly");
    }

    [Test]
    public void Backtester_GapCondition_EvaluatesOnlyWithPreviousClose()
    {
        var def = Stock.Ticker("TST").IsGapUp(5).Long().Quantity(10).Build();

        var start = new DateTime(2026, 7, 17, 8, 0, 0, DateTimeKind.Utc);
        var bars = Enumerable.Range(0, 5).Select(i => new Candle
        {
            Symbol = "TST",
            StartUtc = start.AddMinutes(i),
            EndUtc = start.AddMinutes(i + 1),
            Open = 10.6m, High = 10.7m, Low = 10.5m, Close = 10.6m, Volume = 1000,
        }).ToList();

        // +6% over a previous close of 10 — the gap is real, but only
        // observable when the previous close is plumbed through the options.
        var with = StrategyBacktester.Run(def, bars, new BacktestOptions { PreviousClose = 10m });
        var without = StrategyBacktester.Run(def, bars);

        Assert.Multiple(() =>
        {
            Assert.That(with.Triggers, Is.Not.Empty, "gap condition fires when the previous close is supplied");
            Assert.That(without.NoTriggersFired, Is.True, "without it the gap fails closed, same as live");
        });
    }

    [Test]
    public void PreviousEquityTradingDayEt_SkipsTheWeekend()
    {
        // Monday 01:00 UTC = Sunday evening ET → the last completed equity
        // weekday is the prior Friday. (2026-07-20 is a Monday.)
        var mondayEarlyUtc = new DateTime(2026, 7, 20, 1, 0, 0, DateTimeKind.Utc);
        Assert.That(MarketTime.PreviousEquityTradingDayEt(mondayEarlyUtc),
            Is.EqualTo(new DateOnly(2026, 7, 17)));

        // Midweek: Wednesday noon UTC → Tuesday.
        var wednesdayUtc = new DateTime(2026, 7, 22, 12, 0, 0, DateTimeKind.Utc);
        Assert.That(MarketTime.PreviousEquityTradingDayEt(wednesdayUtc),
            Is.EqualTo(new DateOnly(2026, 7, 21)));
    }
}
