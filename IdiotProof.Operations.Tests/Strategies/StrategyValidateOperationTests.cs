using IdiotProof.Operations.Strategies;

namespace IdiotProof.Operations.Tests.Strategies;

/// <summary>strategy validate (IP-T146): saved strategy or script; read-only.</summary>
public class StrategyValidateOperationTests
{
    [Test]
    public async Task Validate_ASavedStrategy_ReportsRoundTripsAndRisk()
    {
        var kit = new StrategyKit();
        var s = await kit.AddAsync("NVDA", "Stock.Ticker(\"NVDA\").Entry(175).Long().StopLoss(170).TakeProfit(190).Build()");

        var result = await new StrategyValidateOperation().ExecuteAsync(new StrategyValidateArgs { Strategy = "NVDA" }, kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.Contain("canon round-trip: yes").And.Contain("Valid"));
        Assert.That(result.Human, Does.Contain("risks $5.00/share").And.Contain("reward/risk 3.0:1"));
        Assert.That((await kit.ReloadAsync(s.Id)).UpdatedUtc, Is.EqualTo(s.UpdatedUtc), "validate changes nothing");
    }

    [Test]
    public async Task Validate_AScript_FlagsErrors()
    {
        var kit = new StrategyKit();

        var result = await new StrategyValidateOperation().ExecuteAsync(
            new StrategyValidateArgs { Script = "Stock.Ticker(\"AMD\").IsAboveVwap().Short().StopLoss(140).TakeProfit(150).Build()" }, kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.Contain("INVALID").And.Contain("must sit above its target").And.Contain("short-enabled"));
    }

    [Test]
    public async Task Validate_AQuarantinedRow_SaysSo_AndNeedsExactlyOneSource()
    {
        var kit = new StrategyKit();
        await kit.AddAsync("BAD", rawJson: "{\"schemaVersion\":99}");

        var result = await new StrategyValidateOperation().ExecuteAsync(new StrategyValidateArgs { Strategy = "BAD" }, kit.Context());
        Assert.That(result.Human, Does.Contain("QUARANTINED"));

        var none = await new StrategyValidateOperation().ExecuteAsync(new StrategyValidateArgs(), kit.Context());
        Assert.That(none.Status, Is.EqualTo(OperationStatus.UsageError));
    }
}
