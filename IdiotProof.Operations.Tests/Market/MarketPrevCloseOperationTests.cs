using IdiotProof.Operations.Market;

namespace IdiotProof.Operations.Tests.Market;

/// <summary>market prev-close (IP-T202): the snapshot's prior daily bar beside the latest trade.</summary>
public class MarketPrevCloseOperationTests
{
    [Test]
    public async Task ShowsPriorSessionCloseAndTheMoveSince()
    {
        var handler = new ScriptedMarketHandler().On("/v2/stocks/snapshots", """
            {"SPY":{"latestTrade":{"t":"2026-10-30T17:59:00Z","p":505.0,"s":100},
                    "prevDailyBar":{"t":"2026-10-29T04:00:00Z","o":495,"h":502,"l":494,"c":500,"v":61000000,"vw":499}}}
            """);

        var result = await new MarketPrevCloseOperation().ExecuteAsync(new MarketPrevCloseArgs { Symbols = ["spy", "zzzz"] }, MarketDataTestKit.Context(handler));

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(handler.Query("/v2/stocks/snapshots", "symbols"), Is.EqualTo("SPY,ZZZZ"));
        var spy = result.Human!.Split('\n').Single(l => l.StartsWith("SPY"));
        Assert.That(spy, Does.Contain("2026-10-29").And.Contain("$500.00").And.Contain("61M").And.Contain("+$5.00").And.Contain("+1.00%"));
        Assert.That(result.Human, Does.Contain("No data for: ZZZZ"));
    }
}
