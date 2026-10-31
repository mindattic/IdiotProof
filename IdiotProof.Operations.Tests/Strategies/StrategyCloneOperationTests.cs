using IdiotProof.Core.Trading;
using IdiotProof.Operations.Strategies;
using IdiotProof.Scripting;

namespace IdiotProof.Operations.Tests.Strategies;

/// <summary>strategy clone (IP-T153).</summary>
public class StrategyCloneOperationTests
{
    [Test]
    public async Task Clone_CopiesTheCanon_NeverThePositionOrLiveRoute()
    {
        var kit = new StrategyKit();
        var s = await kit.AddAsync("NVDA", mode: BrokerMode.Live, active: true, positionQty: 2);

        var result = await new StrategyCloneOperation().ExecuteAsync(new StrategyCloneArgs { Strategy = "NVDA" }, kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        var copy = (await kit.AllAsync()).Single(r => r.Id != s.Id);
        Assert.Multiple(() =>
        {
            Assert.That(copy.Title, Is.EqualTo("NVDA test (copy)"));
            Assert.That(copy.BrokerMode, Is.EqualTo("Sandbox"));
            Assert.That(copy.IsActive, Is.False);
            Assert.That(copy.PositionQty, Is.EqualTo(0));
            Assert.That(StrategyJson.Deserialize(copy.ScriptJson!).ShortId(), Is.EqualTo(StrategyJson.Deserialize(s.ScriptJson!).ShortId()));
        });
    }

    [Test]
    public async Task Clone_ToAnotherSymbol_WarnsAboutFixedPrices()
    {
        var kit = new StrategyKit();
        await kit.AddAsync("NVDA");

        var result = await new StrategyCloneOperation().ExecuteAsync(new StrategyCloneArgs { Strategy = "NVDA", Symbol = "amd", Mode = BrokerMode.Paper }, kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        var copy = (await kit.AllAsync()).Single(r => r.Symbol == "AMD");
        Assert.That(StrategyJson.Deserialize(copy.ScriptJson!).Symbol, Is.EqualTo("AMD"));
        Assert.That(copy.BrokerMode, Is.EqualTo("Paper"));
        Assert.That(result.Human, Does.Contain("fixed prices").And.Contain("stop $170.00"));
    }
}
