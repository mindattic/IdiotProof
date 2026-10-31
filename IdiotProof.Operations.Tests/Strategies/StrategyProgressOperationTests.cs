using IdiotProof.Operations.Strategies;

namespace IdiotProof.Operations.Tests.Strategies;

/// <summary>strategy progress (IP-T150): N/M, first failing verb, bounded watch.</summary>
public class StrategyProgressOperationTests
{
    [Test]
    public async Task Progress_ShowsPassingCountAndTheFailingVerb()
    {
        var kit = new StrategyKit();
        var s = await kit.AddAsync("NVDA", "Stock.Ticker(\"NVDA\").IsAboveVwap().IsAboveEma(9).Long().StopLoss(170).Build()", active: true);
        await kit.AddProgressAsync(s.Id, 1, 2, "IsAboveEma", 176.25m);

        var result = await new StrategyProgressOperation().ExecuteAsync(new StrategyProgressArgs { Strategy = "NVDA" }, kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.Contain("1/2 conditions passing").And.Contain("waiting on: IsAboveEma").And.Contain("$176.25"));
        Assert.That(result.Human, Does.Contain("✗ IsAboveEma(9)"));
    }

    [Test]
    public async Task Watch_IsBounded_AndStopsWhenEverythingPasses()
    {
        var kit = new StrategyKit();
        var s = await kit.AddAsync("NVDA", active: true);
        await kit.AddProgressAsync(s.Id, 1, 1, null);

        var result = await new StrategyProgressOperation().ExecuteAsync(
            new StrategyProgressArgs { Strategy = "NVDA", Watch = true, Every = 0, Samples = 50 }, kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.Contain("ALL PASS").And.Contain("1 distinct reading"));
    }

    [Test]
    public async Task Watch_SamplesUntilTheBound_ReportingOnlyChanges()
    {
        var kit = new StrategyKit();
        var s = await kit.AddAsync("NVDA", active: true);
        await kit.AddProgressAsync(s.Id, 0, 1, "IsAboveVwap");

        var result = await new StrategyProgressOperation().ExecuteAsync(
            new StrategyProgressArgs { Strategy = "NVDA", Watch = true, Every = 0, Samples = 5 }, kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.Contain("1 distinct reading"), "five samples of an unchanged row are one reading");
    }

    [Test]
    public async Task Watch_RejectsOutOfRangeBounds()
    {
        var kit = new StrategyKit();
        await kit.AddAsync("NVDA");
        var r = await new StrategyProgressOperation().ExecuteAsync(new StrategyProgressArgs { Strategy = "NVDA", Watch = true, Samples = 500 }, kit.Context());
        Assert.That(r.Status, Is.EqualTo(OperationStatus.UsageError));
    }
}
