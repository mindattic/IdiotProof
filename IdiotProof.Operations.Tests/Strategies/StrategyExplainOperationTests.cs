using IdiotProof.Operations.Strategies;
using IdiotProof.Scripting;

namespace IdiotProof.Operations.Tests.Strategies;

/// <summary>strategy explain (IP-T159): deterministic plain English from the canon.</summary>
public class StrategyExplainOperationTests
{
    [Test]
    public async Task Explain_WalksThePhases_InPlainEnglish()
    {
        var kit = new StrategyKit();
        await kit.AddAsync("NVDA", "Stock.Ticker(\"NVDA\").RequireAdxAbove(20).IsAboveVwap().OnReclaim(9).Quantity(2).Long().StopLoss(170).TakeProfit(190).SellBy(\"15:45\").Build()",
            active: true);

        var result = await new StrategyExplainOperation().ExecuteAsync(new StrategyRefArgs { Strategy = "NVDA" }, kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.Contain("Trades NVDA during the regular session"));
        Assert.That(result.Human, Does.Contain("Only considers an entry while ADX is above 20"));
        Assert.That(result.Human, Does.Contain("price is above VWAP and price reclaims the 9 EMA"));
        Assert.That(result.Human, Does.Contain("Buys 2 shares"));
        Assert.That(result.Human, Does.Contain("Stop loss at $170.00").And.Contain("Takes profit at $190.00").And.Contain("3:45 PM ET"));
        Assert.That(result.Human, Does.Contain("ACTIVE"));
    }

    [Test]
    public void Explain_IsDeterministic_AndCoversBranchesAndLadders()
    {
        var def = StrategySamples.Find("sune-wedge")!.Build();
        var a = StrategyNarrator.Render(def);
        var b = StrategyNarrator.Render(StrategyJson.Deserialize(StrategyJson.Serialize(def)));

        Assert.That(a, Is.EqualTo(b));
        Assert.That(a, Does.Contain("Scales out:").And.Contain("price has broken out above $2.42"));
    }
}
