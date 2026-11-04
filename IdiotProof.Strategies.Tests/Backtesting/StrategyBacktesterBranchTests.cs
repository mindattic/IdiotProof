using IdiotProof.Models;
using IdiotProof.Scripting;
using IdiotProof.Strategies.Backtesting;
using static IdiotProof.Strategies.Tests.Backtesting.BacktestTestData;

namespace IdiotProof.Strategies.Tests.Backtesting;

/// <summary>The backtester resolves If/ElseIf/Else per bar, like the Monitor (IP-T369).</summary>
public class StrategyBacktesterBranchTests
{
    // Base: price in [1,100]. If price >= 20: stop 5 / target 30, else: stop 9 / target 11.
    private static StrategyDefinition Branched() =>
        Stock.Ticker("TST")
            .IsPriceBetween(1, 100)
            .IsPriceBetween(20, 100)
            .Then(b => b.StopLoss(5).TakeProfit(30))
            .Else(b => b.StopLoss(9).TakeProfit(11))
            .Build();

    [Test]
    public void ElseBranch_SuppliesStopAndTarget_TradeExitsAtElseTarget()
    {
        var candles = new List<Candle>
        {
            Bar(0, 10m, 10.2m, 9.8m, 10m),   // price 10 < 20 -> Else branch -> ENTER @ 10
            Bar(1, 10m, 11.5m, 9.9m, 11.2m), // Else target 11 hit
        };

        var report = StrategyBacktester.Run(Branched(), candles);

        Assert.That(report.Trades, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(report.Trades[0].StopPrice, Is.EqualTo(9m));
            Assert.That(report.Trades[0].ExitReason, Is.EqualTo("Target"));
            Assert.That(report.Trades[0].Exits[^1].Price, Is.EqualTo(11m));
            Assert.That(report.Note, Is.Null, "branching is simulated now, so there is no caveat note");
        });
    }

    [Test]
    public void IfBranch_MatchesOnEntryBar_UsesIfStop()
    {
        var candles = new List<Candle>
        {
            Bar(0, 25m, 25.5m, 24.8m, 25m),  // price 25 >= 20 -> If branch -> ENTER @ 25, stop 5
            Bar(1, 25m, 25.2m, 10m, 12m),    // would hit Else stop 9? no: low 10 > 5, If stop holds
        };

        var report = StrategyBacktester.Run(Branched(), candles);

        Assert.That(report.Trades, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(report.Trades[0].StopPrice, Is.EqualTo(5m));
            Assert.That(report.Trades[0].ExitReason, Is.EqualTo("End of session"));
        });
    }

    [Test]
    public void OpenTrade_KeepsEntryBranch_WhenActiveBranchFlips()
    {
        var candles = new List<Candle>
        {
            Bar(0, 25m, 25.5m, 24.8m, 25m),  // If branch -> ENTER, target 30
            Bar(1, 25m, 25.1m, 10.5m, 10.6m),// price falls under 20: Else is now active, but its target 11 must not apply
            Bar(2, 10.6m, 11.4m, 10.5m, 11.3m),
        };

        var report = StrategyBacktester.Run(Branched(), candles);

        Assert.That(report.Trades, Has.Count.EqualTo(1));
        Assert.That(report.Trades[0].ExitReason, Is.EqualTo("End of session"),
            "the trade keeps the If branch's 30 target; the Else branch's 11 target must not fire mid-trade");
    }
}
