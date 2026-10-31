using IdiotProof.Operations.Replay;

using IdiotProof.Core.Monitoring;
namespace IdiotProof.Operations.Tests.Replay;

/// <summary>replay all (IP-T178): every family on one ticker/day, bars fetched once, each saved, ranked best fit first.</summary>
public class ReplayAllOperationTests
{
    [Test]
    public async Task RunsEveryFamily_SavesEachRun_AndRanksFiredFirst()
    {
        var feed = new ReplayWorld.NamedMockFeed();
        using var w = new ReplayWorld(feed);

        var result = await new ReplayAllOperation().ExecuteAsync(new ReplayAllArgs { Symbol = "GAPT", Date = ReplayWorld.Day, Pages = false }, w.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        var runs = await w.RunsAsync();
        Assert.Multiple(() =>
        {
            Assert.That(runs, Has.Count.EqualTo(StrategyReplay.Families.Count));
            Assert.That(feed.Requested.Count(s => s == "GAPT"), Is.LessThanOrEqualTo(2), "the day's bars (and the previous close) are fetched once, not per family");
            Assert.That(result.Human, Does.Contain("classic-gapper").And.Contain("swingreversal").And.Contain(" 1 "));
        });
        var lines = result.Human!.Split('\n').Where(l => l.Contains('●') || l.Contains(" · ")).ToList();
        var firstNoFire = lines.FindIndex(l => !l.Contains('●'));
        Assert.That(firstNoFire < 0 || lines.Skip(firstNoFire).All(l => !l.Contains('●')), Is.True, "fired families rank above the rest");
    }
}
