using IdiotProof.Core.Trading;
using IdiotProof.Operations.Strategies;
using IdiotProof.Scripting;

namespace IdiotProof.Operations.Tests.Strategies;

/// <summary>strategy from-watchlist (IP-T156): port of MonitorCli create-strategies.</summary>
public class StrategyFromWatchlistOperationTests
{
    private const string Watchlist = """
        [
          {"title":"NVDA reclaim","symbol":"NVDA","script":"Stock.Ticker(\"NVDA\").IsAboveVwap().Long().StopLoss(170).TakeProfit(190).Build()","active":true,"originTranscript":"watch NVDA"},
          {"title":"NVDA reclaim","symbol":"NVDA","script":"Stock.Ticker(\"NVDA\").IsAboveVwap().Long().StopLoss(170).Build()"},
          {"title":"AMD no stop","symbol":"AMD","script":"Stock.Ticker(\"AMD\").IsAboveVwap().Long().Build()"},
          {"title":"bad","symbol":"","script":""},
          {"title":"TSLA","symbol":"TSLA","script":"Stock.Ticker(\"TSLA\").IsAboveVwap().Long().StopLossPercent(2).Build()","active":true}
        ]
        """;

    [Test]
    public async Task FromWatchlist_CreatesValidItems_SkipsTheRest_InSandbox()
    {
        var kit = new StrategyKit();

        var result = await new StrategyFromWatchlistOperation().ExecuteAsync(new StrategyFromWatchlistArgs { File = Watchlist }, kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        var rows = await kit.AllAsync();
        Assert.That(rows.Select(r => r.Symbol), Is.EquivalentTo(new[] { "NVDA", "TSLA" }));
        Assert.That(rows, Is.All.Matches<IdiotProof.Core.Data.Strategy>(r => r.BrokerMode == "Sandbox" && !r.IsActive),
            "without --activate everything starts inactive");
        Assert.That(rows.Single(r => r.Symbol == "NVDA").OriginTranscript, Is.EqualTo("watch NVDA"));
        Assert.That(StrategyJson.Deserialize(rows.Single(r => r.Symbol == "TSLA").ScriptJson!).Quantity, Is.EqualTo(1));
        Assert.That(result.Human, Does.Contain("repeated in the file").And.Contain("No stop loss").And.Contain("symbol or script missing"));

        var again = await new StrategyFromWatchlistOperation().ExecuteAsync(new StrategyFromWatchlistArgs { File = Watchlist }, kit.Context());
        Assert.That(again.Human, Does.Contain("already exists"));
        Assert.That(await kit.AllAsync(), Has.Count.EqualTo(2), "re-running is idempotent");
    }

    [Test]
    public async Task FromWatchlist_ActivateAndPaper_FromAFile_AndDryRun()
    {
        var kit = new StrategyKit();
        var path = Path.Combine(Path.GetTempPath(), $"watchlist-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(path, Watchlist);
        try
        {
            var dry = await new StrategyFromWatchlistOperation().ExecuteAsync(
                new StrategyFromWatchlistArgs { File = path, DryRun = true, Activate = true }, kit.Context());
            Assert.That(dry.Human, Does.Contain("would create + activate"));
            Assert.That(await kit.AllAsync(), Is.Empty);

            var result = await new StrategyFromWatchlistOperation().ExecuteAsync(
                new StrategyFromWatchlistArgs { File = path, Activate = true, Mode = BrokerMode.Paper }, kit.Context());
            Assert.That(result.IsOk, Is.True, result.Message);
            Assert.That(await kit.AllAsync(), Is.All.Matches<IdiotProof.Core.Data.Strategy>(r => r.BrokerMode == "Paper" && r.IsActive));
        }
        finally { File.Delete(path); }
    }
}
