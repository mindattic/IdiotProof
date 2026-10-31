using IdiotProof.Operations.Replay;

namespace IdiotProof.Operations.Tests.Replay;

/// <summary>scan movers (IP-T186): scripted movers screened to the gapper band; --replay replays each survivor.</summary>
public class ScanMoversOperationTests
{
    private const string Movers = """
        {"gainers":[
          {"symbol":"GAPT","percent_change":42.0,"price":12.5},
          {"symbol":"GAPX","percent_change":18.0,"price":7.0},
          {"symbol":"TINY","percent_change":3.0,"price":4.0},
          {"symbol":"PRICY","percent_change":30.0,"price":400.0}
        ],"losers":[],"last_updated":"2026-07-17T12:00:00Z"}
        """;

    [Test]
    public async Task ScreensToTheBand_BiggestFirst_WithoutReplaying()
    {
        using var w = new ReplayWorld();
        w.Market.On("/v1beta1/screener/stocks/movers", Movers);

        var result = await new ScanMoversOperation().ExecuteAsync(new ScanMoversArgs(), w.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.Multiple(() =>
        {
            Assert.That(result.Human, Does.Contain("4 gainers screened → 2 in the gapper band"));
            Assert.That(result.Human!.IndexOf("GAPT"), Is.LessThan(result.Human.IndexOf("GAPX")));
            Assert.That(result.Human, Does.Not.Contain("TINY").And.Not.Contain("PRICY"));
            Assert.That(w.Market.Query("/v1beta1/screener/stocks/movers", "top"), Is.EqualTo("50"));
        });
        Assert.That(await w.RunsAsync(), Is.Empty);
    }

    [Test]
    public async Task Replay_ReplaysEachSurvivor_AndSavesTheRuns()
    {
        using var w = new ReplayWorld();
        w.Market.On("/v1beta1/screener/stocks/movers", Movers);

        var result = await new ScanMoversOperation().ExecuteAsync(new ScanMoversArgs { Replay = true, Date = ReplayWorld.Day, Out = w.Pages }, w.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        var runs = await w.RunsAsync();
        Assert.That(runs.Select(r => r.Symbol), Is.EquivalentTo(new[] { "GAPT", "GAPX" }));
        Assert.That(result.Human, Does.Contain("Replayed on Fri 2026-07-17 with classic-gapper").And.Contain("not published"));
    }
}
