using IdiotProof.Models;
using IdiotProof.Scripting;

namespace IdiotProof.Strategies.Tests;

/// <summary>
/// The TradeSignal a DslStrategy emits must carry the FULL exit plan the
/// author wrote — the backtester honors the multi-target scale-out ladder, so
/// the live signal must too, or backtest results silently diverge from live
/// behavior (money-path divergence, not a display bug).
/// </summary>
public class DslStrategySignalTests
{
    private static IReadOnlyList<Candle> Bars()
    {
        var start = new DateTime(2026, 7, 17, 13, 30, 0, DateTimeKind.Utc);
        return
        [
            new Candle { Symbol = "TEST", StartUtc = start,                EndUtc = start.AddMinutes(1), Open = 4.0m, High = 4.2m, Low = 3.9m, Close = 4.1m, Volume = 1000 },
            new Candle { Symbol = "TEST", StartUtc = start.AddMinutes(1),  EndUtc = start.AddMinutes(2), Open = 4.1m, High = 4.3m, Low = 4.0m, Close = 4.2m, Volume = 1200 },
        ];
    }

    [Test]
    public void Evaluate_MultiTargetScaleOut_EmitsTheFullLadder()
    {
        // TakeProfit(t1, t2, t3) sets TakeProfitPrice = t1 AND populates
        // TakeProfitTargets. Regression: the signal used to read only
        // TakeProfitPrice, silently dropping T2/T3 on the live path.
        var def = Stock.Ticker("TEST")
            .Long()
            .StopLoss(3.5)
            .TakeProfit(5.00, 6.50, 8.00)
            .Build();

        var signals = new DslStrategy(def).Evaluate("TEST", Bars(), new StrategyContext());

        Assert.That(signals, Has.Count.EqualTo(1));
        Assert.That(signals[0].Targets, Is.EqualTo(new[] { 5.00m, 6.50m, 8.00m }));
    }

    [Test]
    public void Evaluate_SingleTarget_StillEmitsOneTarget()
    {
        var def = Stock.Ticker("TEST")
            .Long()
            .StopLoss(3.5)
            .TakeProfit(5.00)
            .Build();

        var signals = new DslStrategy(def).Evaluate("TEST", Bars(), new StrategyContext());

        Assert.That(signals, Has.Count.EqualTo(1));
        Assert.That(signals[0].Targets, Is.EqualTo(new[] { 5.00m }));
    }
}
