using IdiotProof.Operations.Research;

namespace IdiotProof.Operations.Tests.Research;

/// <summary>research scan (IP-T226): one pass through IResearchScanPass with the caller's key; the pass itself is faked.</summary>
public class ResearchScanOperationTests
{
    [Test]
    public async Task TargetedScan_PassesTickersBatchDaysAndTheOwnersKey()
    {
        var kit = new ResearchKit();

        var result = await new ResearchScanOperation().ExecuteAsync(
            new ResearchScanArgs { Tickers = ["nvda", " AMD", "NVDA"], Batch = 25, DaysBack = 3 }, kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(kit.Scan.Last!.Tickers, Is.EqualTo(new[] { "NVDA", "AMD" }));
        Assert.That(kit.Scan.Last.BatchSize, Is.EqualTo(25));
        Assert.That(kit.Scan.Last.DaysBack, Is.EqualTo(3));
        Assert.That(kit.Scan.Last.ClaudeApiKey, Is.EqualTo("owner-key"));
        Assert.That(result.Human, Does.Contain("2/2 named tickers (NVDA,AMD)").And.Contain("3 new").And.Contain("ET"));
    }

    [Test]
    public async Task DefaultScan_LeavesTheTickerListToThePass()
    {
        var kit = new ResearchKit();

        var result = await new ResearchScanOperation().ExecuteAsync(new ResearchScanArgs(), kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(kit.Scan.Last!.Tickers, Is.Null);
        Assert.That(result.Human, Does.Contain("watchlist + rotating batch").And.Contain("5,000"));
    }

    [TestCase(0, null)]
    [TestCase(null, 31)]
    public async Task OutOfRangeKnobs_AreUsageErrors(int? batch, int? daysBack)
    {
        var kit = new ResearchKit();
        var result = await new ResearchScanOperation().ExecuteAsync(new ResearchScanArgs { Batch = batch, DaysBack = daysBack }, kit.Context());
        Assert.That(result.Status, Is.EqualTo(OperationStatus.UsageError));
        Assert.That(kit.Scan.Last, Is.Null);
    }
}
