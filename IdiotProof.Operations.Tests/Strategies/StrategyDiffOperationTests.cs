using IdiotProof.Core.Trading;
using IdiotProof.Operations.Strategies;

namespace IdiotProof.Operations.Tests.Strategies;

/// <summary>strategy diff (IP-T154): canon-level comparison.</summary>
public class StrategyDiffOperationTests
{
    [Test]
    public async Task Diff_ListsEveryDifferingField()
    {
        var kit = new StrategyKit();
        var a = await kit.AddAsync("NVDA");
        var b = await kit.AddAsync("AMD", "Stock.Ticker(\"AMD\").IsAboveVwap().IsAboveEma(9).Long().StopLoss(150).TakeProfit(190).Build()", mode: BrokerMode.Paper);

        var result = await new StrategyDiffOperation().ExecuteAsync(new StrategyDiffArgs { A = "NVDA", B = "AMD" }, kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.Contain("symbol").And.Contain("stopLossPrice").And.Contain("entryConditions[1]").And.Contain("mode"));
        Assert.That(result.Human, Does.Not.Contain("takeProfitPrice"), "equal fields are not listed");
    }

    [Test]
    public async Task Diff_OfTwoCopies_SaysOnlyTheRowDiffers()
    {
        var kit = new StrategyKit();
        var a = await kit.AddAsync("NVDA", title: "one");
        var b = await kit.AddAsync("NVDA", title: "two");

        var result = await new StrategyDiffOperation().ExecuteAsync(
            new StrategyDiffArgs { A = a.Id.ToString(), B = b.Id.ToString() }, kit.Context());

        Assert.That(result.Human, Does.Contain("1 difference").And.Contain("canons are identical"));
    }
}
