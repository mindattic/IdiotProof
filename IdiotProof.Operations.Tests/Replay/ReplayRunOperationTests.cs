using IdiotProof.DataFeeds;
using IdiotProof.Operations.Replay;

namespace IdiotProof.Operations.Tests.Replay;

/// <summary>replay run (IP-T176): the Monitor's evaluator over a past day, persisted with its feature rows and page.</summary>
public class ReplayRunOperationTests
{
    [Test]
    public async Task ReplaysTheSavedGapper_PersistsTheRun_AndRendersItsPage()
    {
        using var w = new ReplayWorld();
        await w.AddGapperAsync();

        var result = await new ReplayRunOperation().ExecuteAsync(new ReplayRunArgs { Symbol = "gapt", Date = ReplayWorld.Day, Out = w.Pages }, w.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        var run = (await w.RunsAsync()).Single();
        Assert.Multiple(() =>
        {
            Assert.That(run.Symbol, Is.EqualTo("GAPT"));
            Assert.That(run.DateEt, Is.EqualTo("2026-07-17"));
            Assert.That(run.Fired, Is.True, result.Human);
            Assert.That(run.Strategy, Is.EqualTo("GAPT test gapper"));
            Assert.That(run.GeneratedEt, Is.EqualTo("2026-07-18 12:00:00 ET"), "stamped from the call's clock, in ET");
            Assert.That(result.Human, Does.Contain("round-trip").And.Contain("entry ET").And.Contain("replay show " + run.Id.ToString("N")[..8]));
            Assert.That(File.Exists(Path.Combine(w.Pages, "gapt", run.Stamp, "index.htm")), Is.True);
            Assert.That(File.Exists(Path.Combine(w.Pages, "index.htm")), Is.True);
        });
    }

    [Test]
    public async Task AFamilyReplay_NeedsNoSavedStrategy_AndNoPagesWritesNoFiles()
    {
        using var w = new ReplayWorld();
        var result = await new ReplayRunOperation().ExecuteAsync(
            new ReplayRunArgs { Symbol = "GAPT", Date = ReplayWorld.Day, Family = "momentum", Pages = false, Out = w.Pages }, w.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That((await w.RunsAsync()).Single().Strategy, Does.Contain("Momentum"));
        Assert.That(Directory.Exists(w.Pages), Is.False);
    }

    [Test]
    public async Task RefusesTheMockFeed_AndAnUnknownFamily()
    {
        using var mock = new ReplayWorld(new MockDataFeed());
        var blocked = Assert.ThrowsAsync<OperationException>(() => new ReplayRunOperation().ExecuteAsync(new ReplayRunArgs { Symbol = "GAPT", Family = "momentum" }, mock.Context()));
        Assert.That(blocked!.Status, Is.EqualTo(OperationStatus.Blocked));

        using var w = new ReplayWorld();
        var ex = Assert.ThrowsAsync<OperationException>(() => new ReplayRunOperation().ExecuteAsync(new ReplayRunArgs { Symbol = "GAPT", Family = "nope" }, w.Context()));
        Assert.That(ex!.Status, Is.EqualTo(OperationStatus.UsageError));
        Assert.That(await w.RunsAsync(), Is.Empty);
    }
}
