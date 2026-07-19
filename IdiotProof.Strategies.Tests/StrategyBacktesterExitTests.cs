using IdiotProof.Models;
using IdiotProof.Scripting;
using IdiotProof.Strategies.Backtesting;

namespace IdiotProof.Strategies.Tests;

/// <summary>
/// IP-A19 regressions: the generic replay must honor the SAME Risk/Exit verbs
/// the live evaluator runs. TrailingStopLoss and PeakGiveback were silently
/// ignored — a strategy that would have sold on the rollover was reported
/// holding to the time exit / end of session (backtest ≠ live divergence on
/// the flagship gapper exit).
/// </summary>
public class StrategyBacktesterExitTests
{
    private static readonly DateTime Start = new(2026, 7, 17, 13, 30, 0, DateTimeKind.Utc); // 09:30 ET (EDT)

    private static Candle Bar(int minute, double open, double high, double low, double close) => new()
    {
        Symbol = "TST",
        StartUtc = Start.AddMinutes(minute),
        EndUtc = Start.AddMinutes(minute + 1),
        Open = (decimal)open, High = (decimal)high, Low = (decimal)low, Close = (decimal)close,
        Volume = 1000,
    };

    [Test]
    public void Replay_TrailingStop_ExitsOffThePeak()
    {
        var def = Stock.Ticker("TST")
            .IsPriceBetween(1, 1000) // always true — entry on the first bar
            .Long().Quantity(10)
            .TrailingStopLoss(5)
            .Build();

        List<Candle> bars =
        [
            Bar(0, 100, 100.5, 99.5, 100),   // entry at 100
            Bar(1, 100, 110, 108, 109),      // peak 110 → trail floor 104.50
            Bar(2, 109, 110, 104, 105),      // low 104 pierces the floor → exit
        ];

        var report = StrategyBacktester.Run(def, bars);

        Assert.That(report.Trades, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(report.Trades[0].ExitReason, Is.EqualTo("Trailing stop"));
            Assert.That(report.Trades[0].Exits[^1].Price, Is.EqualTo(104.5m), "fills at the trail floor");
        });
    }

    [Test]
    public void Replay_PeakGiveback_SellsOnTheRollover()
    {
        var def = Stock.Ticker("TST")
            .IsPriceBetween(1, 1000)
            .Long().Quantity(10)
            .PeakGiveback(25) // no arm time → armed immediately
            .Build();

        List<Candle> bars =
        [
            Bar(0, 100, 100.5, 99.5, 100),   // entry at 100
            Bar(1, 100, 120, 100, 119),      // peak 120, run 20 → giveback floor 115
            Bar(2, 119, 120, 114, 114.5),    // close 114.5 <= 115 → momentum rolled over
        ];

        var report = StrategyBacktester.Run(def, bars);

        Assert.That(report.Trades, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(report.Trades[0].ExitReason, Is.EqualTo("Peak giveback"));
            Assert.That(report.Trades[0].Exits[^1].Price, Is.EqualTo(114.5m), "close-based, like the live evaluator");
        });
    }
}
