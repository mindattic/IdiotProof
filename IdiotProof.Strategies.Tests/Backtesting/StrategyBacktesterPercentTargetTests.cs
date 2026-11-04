using IdiotProof.Models;
using IdiotProof.Scripting;
using IdiotProof.Strategies.Backtesting;
using static IdiotProof.Strategies.Tests.Backtesting.BacktestTestData;

namespace IdiotProof.Strategies.Tests.Backtesting;

/// <summary>IP-T433: TakeProfitPercent fires in backtests, resolved from the fill like StopLossPercent.</summary>
public class StrategyBacktesterPercentTargetTests
{
    [Test]
    public void LongPercentTarget_ExitsAtEntryTimesOnePlusPercent()
    {
        var def = Stock.Ticker("TST").IsPriceBetween(1, 100).Long().StopLossPercent(5).TakeProfitPercent(4).Build();
        var candles = new List<Candle>
        {
            Bar(0, 10m, 10.1m, 9.9m, 10m),     // ENTER @ 10 -> target 10.40, stop 9.50
            Bar(1, 10m, 10.3m, 9.9m, 10.2m),   // below target
            Bar(2, 10.2m, 10.6m, 10.1m, 10.5m), // target 10.40 hit
        };

        var report = StrategyBacktester.Run(def, candles);

        Assert.That(report.Trades, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(report.Trades[0].ExitReason, Is.EqualTo("Target"));
            Assert.That(report.Trades[0].Exits[^1].Price, Is.EqualTo(10.40m));
            Assert.That(report.Trades[0].Exits[^1].Utc, Is.EqualTo(candles[2].StartUtc));
        });
    }

    [Test]
    public void ShortPercentTarget_ExitsAtEntryTimesOneMinusPercent()
    {
        var def = Stock.Ticker("TST").IsPriceBetween(1, 100).Short().StopLossPercent(5).TakeProfitPercent(4).Build();
        var candles = new List<Candle>
        {
            Bar(0, 10m, 10.1m, 9.9m, 10m),   // SHORT @ 10 -> target 9.60, stop 10.50
            Bar(1, 10m, 10.1m, 9.5m, 9.55m), // target 9.60 hit
        };

        var report = StrategyBacktester.Run(def, candles);

        Assert.That(report.Trades, Has.Count.EqualTo(1));
        Assert.That(report.Trades[0].ExitReason, Is.EqualTo("Target"));
        Assert.That(report.Trades[0].Exits[^1].Price, Is.EqualTo(9.60m));
    }

    [Test]
    public void PriceAndPercentTargets_TheNearerOneFiresFirst_LikeTheLiveEvaluator()
    {
        var def = Stock.Ticker("TST").IsPriceBetween(1, 100).Long().StopLossPercent(5).TakeProfit(10.80).TakeProfitPercent(3).Build();
        var targets = StrategyBacktester.ResolveTargets(def, 10m, isLong: true);
        Assert.That(targets.Select(t => t.Price), Is.EqualTo(new[] { 10.30m, 10.80m }));
    }

    [Test]
    public void ALadder_TakesPrecedenceOverThePercentTarget()
    {
        var def = Stock.Ticker("TST").IsPriceBetween(1, 100).Long().StopLossPercent(5)
            .AddTarget(10.50, 50, "T1").AddTarget(11.00, 50, "T2").TakeProfitPercent(2).Build();
        var targets = StrategyBacktester.ResolveTargets(def, 10m, isLong: true);
        Assert.That(targets.Select(t => (t.Price, t.Label)), Is.EqualTo(new[] { (10.50m, "T1"), (11.00m, "T2") }));
    }

    [Test]
    public void NoTargets_StillHoldsToEndOfSession()
    {
        var def = Stock.Ticker("TST").IsPriceBetween(1, 100).Long().StopLossPercent(5).Build();
        var report = StrategyBacktester.Run(def, new List<Candle> { Bar(0, 10m, 10.1m, 9.9m, 10m), Bar(1, 10m, 12m, 9.9m, 11.9m) });
        Assert.That(report.Trades[0].ExitReason, Is.EqualTo("End of session"));
    }
}
