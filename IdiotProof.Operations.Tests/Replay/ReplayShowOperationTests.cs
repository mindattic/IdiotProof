using IdiotProof.Operations.Replay;

namespace IdiotProof.Operations.Tests.Replay;

/// <summary>replay show (IP-T180): a run's round-trips and per-bar features from the feature store.</summary>
public class ReplayShowOperationTests
{
    [Test]
    public async Task ShowsTradesWithEntryFeatures_AndTheMarkedBars()
    {
        using var w = new ReplayWorld();
        await w.AddGapperAsync();
        var replay = await new ReplayRunOperation().ExecuteAsync(new ReplayRunArgs { Symbol = "GAPT", Date = ReplayWorld.Day, Pages = false }, w.Context());
        Assert.That(replay.IsOk, Is.True, replay.Message);
        var run = (await w.RunsAsync()).Single();

        var marked = await new ReplayShowOperation().ExecuteAsync(new ReplayShowArgs { Run = run.Id.ToString("N")[..8] }, w.Context());
        var session = await new ReplayShowOperation().ExecuteAsync(new ReplayShowArgs { Run = run.Id.ToString(), Bars = "session" }, w.Context());

        Assert.That(marked.IsOk, Is.True, marked.Message);
        Assert.Multiple(() =>
        {
            Assert.That(marked.Human, Does.Contain("Round-trips:").And.Contain("vs VWAP").And.Contain("Bars (marked)").And.Contain("ENTRY").And.Contain("EXIT"));
            Assert.That(marked.Human, Does.Contain("generated").And.Contain("ET"));
            Assert.That(session.Human!.Split('\n').Length, Is.GreaterThan(marked.Human!.Split('\n').Length), "session shows every in-session bar");
        });
    }

    [Test]
    public async Task AShortOrUnknownId_IsAUsageError()
    {
        using var w = new ReplayWorld();
        Assert.ThrowsAsync<OperationException>(() => new ReplayShowOperation().ExecuteAsync(new ReplayShowArgs { Run = "abc" }, w.Context()));
        var ex = Assert.ThrowsAsync<OperationException>(() => new ReplayShowOperation().ExecuteAsync(new ReplayShowArgs { Run = "abcdef12" }, w.Context()));
        Assert.That(ex!.Status, Is.EqualTo(OperationStatus.UsageError));
        await Task.CompletedTask;
    }
}
