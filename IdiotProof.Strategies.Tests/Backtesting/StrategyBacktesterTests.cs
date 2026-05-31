using IdiotProof.Models;
using IdiotProof.Scripting;
using IdiotProof.Strategies.Backtesting;
using static IdiotProof.Strategies.Tests.Backtesting.BacktestTestData;

namespace IdiotProof.Strategies.Tests.Backtesting;

/// <summary>
/// Replay-engine tests. Each scenario uses hand-built bars so the triggers that
/// should fire and the resulting P&amp;L are known exactly. Covers the core ask:
/// "run a previous day, see the numbers, and confirm the triggers went off."
/// </summary>
public class StrategyBacktesterTests
{
    // Breakout above $10, pull back to $9, go long; target $12, stop $8.
    private static StrategyDefinition BreakoutPullbackLong(bool repeat = false)
    {
        var b = Stock.Ticker("TST").Breakout(10).Pullback(9).Long().TakeProfit(12).StopLoss(8);
        if (repeat) b.Repeat();
        return b.Build();
    }

    [Test]
    public void Breakout_Then_Pullback_FiresBothTriggers_AndHitsTarget()
    {
        var def = BreakoutPullbackLong();
        var candles = new List<Candle>
        {
            Bar(0, 9.0m,  9.5m,  8.8m, 9.3m),   // no breakout
            Bar(1, 9.3m, 10.5m,  9.2m, 10.2m),  // breakout latches (high >= 10)
            Bar(2, 10.2m, 10.3m, 8.9m, 9.2m),   // pullback latches (low <= 9) -> ENTER @ 9.20
            Bar(3, 9.2m, 12.5m,  9.1m, 12.3m),  // target 12 hit -> EXIT 100 @ 12.00
        };

        var report = StrategyBacktester.Run(def, candles);

        Assert.Multiple(() =>
        {
            Assert.That(report.Triggers, Has.Count.EqualTo(2), "Breakout + Pullback should each fire once.");
            Assert.That(report.Triggers.Any(t => t.Condition == "Breakout(10)"), Is.True);
            Assert.That(report.Triggers.Any(t => t.Condition == "Pullback(9)"), Is.True);
            Assert.That(report.Trades, Has.Count.EqualTo(1));
        });

        var trade = report.Trades[0];
        Assert.Multiple(() =>
        {
            Assert.That(trade.EntryPrice, Is.EqualTo(9.2m));
            Assert.That(trade.Quantity, Is.EqualTo(100));
            Assert.That(trade.ExitReason, Is.EqualTo("Target"));
            Assert.That(trade.PnL, Is.EqualTo(280m));   // (12 - 9.2) * 100
            Assert.That(report.Wins, Is.EqualTo(1));
            Assert.That(report.TotalPnL, Is.EqualTo(280m));
        });

        // The pullback bar is the one that completed the setup and opened the trade.
        Assert.That(report.Triggers.Single(t => t.Condition == "Pullback(9)").OpenedPosition, Is.True);
    }

    [Test]
    public void StopLoss_ProducesLosingTrade()
    {
        var def = BreakoutPullbackLong();
        var candles = new List<Candle>
        {
            Bar(0, 9.0m,  9.5m,  8.8m, 9.3m),
            Bar(1, 9.3m, 10.5m,  9.2m, 10.2m),  // breakout
            Bar(2, 10.2m, 10.3m, 8.9m, 9.2m),   // pullback -> ENTER @ 9.20
            Bar(3, 9.2m, 9.5m,   7.5m, 7.8m),   // low 7.5 <= stop 8 -> STOP @ 8.00
        };

        var report = StrategyBacktester.Run(def, candles);

        Assert.That(report.Trades, Has.Count.EqualTo(1));
        var trade = report.Trades[0];
        Assert.Multiple(() =>
        {
            Assert.That(trade.ExitReason, Is.EqualTo("Stop loss"));
            Assert.That(trade.PnL, Is.EqualTo(-120m));   // (8 - 9.2) * 100
            Assert.That(report.Losses, Is.EqualTo(1));
        });
    }

    [Test]
    public void NoBreakout_FiresNoTriggers_AndTradesNothing()
    {
        var def = BreakoutPullbackLong();
        var candles = new List<Candle>
        {
            Bar(0, 9.0m, 9.4m, 8.8m, 9.1m),
            Bar(1, 9.1m, 9.6m, 8.9m, 9.3m),
            Bar(2, 9.3m, 9.7m, 9.0m, 9.5m),
            Bar(3, 9.5m, 9.8m, 9.1m, 9.6m),   // never reaches $10
        };

        var report = StrategyBacktester.Run(def, candles);

        Assert.Multiple(() =>
        {
            Assert.That(report.NoTriggersFired, Is.True);
            Assert.That(report.Trades, Is.Empty);
            Assert.That(report.TotalPnL, Is.EqualTo(0m));
            Assert.That(report.BarsProcessed, Is.EqualTo(4));
        });
    }

    [Test]
    public void MultiTarget_ScalesOut_AcrossTwoFills()
    {
        // TakeProfit(11, 13) => T1 @ 11 (50%), T2 @ 13 (50%).
        var def = Stock.Ticker("TST").Breakout(10).Pullback(9).Long().TakeProfit(11, 13).StopLoss(8).Build();
        var candles = new List<Candle>
        {
            Bar(0, 9.0m,  9.5m,  8.8m, 9.3m),
            Bar(1, 9.3m, 10.5m,  9.2m, 10.2m),  // breakout
            Bar(2, 10.2m, 10.3m, 8.9m, 9.2m),   // pullback -> ENTER @ 9.20, qty 100
            Bar(3, 9.2m, 11.5m,  9.0m, 11.2m),  // T1 @ 11 -> sell 50
            Bar(4, 11.2m, 13.5m, 11.0m, 13.2m), // T2 @ 13 -> sell 50
        };

        var report = StrategyBacktester.Run(def, candles);

        Assert.That(report.Trades, Has.Count.EqualTo(1));
        var trade = report.Trades[0];
        Assert.Multiple(() =>
        {
            Assert.That(trade.Exits, Has.Count.EqualTo(2));
            Assert.That(trade.Exits[0].Shares, Is.EqualTo(50));
            Assert.That(trade.Exits[1].Shares, Is.EqualTo(50));
            Assert.That(trade.ExitReason, Is.EqualTo("T2"));
            Assert.That(trade.PnL, Is.EqualTo(280m));  // (11-9.2)*50 + (13-9.2)*50 = 90 + 190
        });
    }

    [Test]
    public void Repeat_AllowsASecondCycle()
    {
        var def = BreakoutPullbackLong(repeat: true);
        var candles = new List<Candle>
        {
            Bar(0, 9.0m,  9.5m,  8.8m, 9.3m),
            Bar(1, 9.3m, 10.5m,  9.2m, 10.2m),  // cycle 1 breakout
            Bar(2, 10.2m, 10.3m, 8.9m, 9.2m),   // cycle 1 pullback -> ENTER @ 9.20
            Bar(3, 9.2m, 12.5m,  9.1m, 12.3m),  // cycle 1 target -> EXIT, repeat resets
            Bar(4, 12.0m, 12.5m, 11.0m, 12.2m), // cycle 2 breakout re-latches
            Bar(5, 12.0m, 12.2m, 8.9m, 9.1m),   // cycle 2 pullback -> ENTER @ 9.10
            Bar(6, 9.1m, 12.5m,  9.0m, 12.3m),  // cycle 2 target -> EXIT
        };

        var report = StrategyBacktester.Run(def, candles);

        Assert.Multiple(() =>
        {
            Assert.That(report.Trades, Has.Count.EqualTo(2), "Repeat() should permit a second round trip.");
            Assert.That(report.Trades[0].Cycle, Is.EqualTo(1));
            Assert.That(report.Trades[1].Cycle, Is.EqualTo(2));
            Assert.That(report.Trades[1].EntryPrice, Is.EqualTo(9.1m));
            Assert.That(report.Triggers.Count(t => t.Cycle == 2), Is.EqualTo(2));
        });
    }

    [Test]
    public void OpenPosition_ClosesAtEndOfSession_WhenNeitherStopNorTargetHit()
    {
        var def = BreakoutPullbackLong();
        var candles = new List<Candle>
        {
            Bar(0, 9.0m,  9.5m,  8.8m, 9.3m),
            Bar(1, 9.3m, 10.5m,  9.2m, 10.2m),  // breakout
            Bar(2, 10.2m, 10.3m, 8.9m, 9.2m),   // pullback -> ENTER @ 9.20
            Bar(3, 9.2m, 10.0m,  9.0m, 9.7m),   // drifts; neither 12 nor 8 reached
        };

        var report = StrategyBacktester.Run(def, candles);

        Assert.That(report.Trades, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(report.Trades[0].ExitReason, Is.EqualTo("End of session"));
            Assert.That(report.Trades[0].Exits[^1].Price, Is.EqualTo(9.7m)); // last close
        });
    }

    [Test]
    public void Run_OnEmptyCandles_ReturnsEmptyReport_NoThrow()
    {
        var def = BreakoutPullbackLong();
        var report = StrategyBacktester.Run(def, new List<Candle>());

        Assert.Multiple(() =>
        {
            Assert.That(report.BarsProcessed, Is.EqualTo(0));
            Assert.That(report.Trades, Is.Empty);
            Assert.That(report.Triggers, Is.Empty);
        });
    }
}
