using IdiotProof.Operations.Hosting;

namespace IdiotProof.Operations.Tests.Hosting;

/// <summary>IP-T79: shutdown refuses new operations, waits for running ones, and gives up after its timeout.</summary>
public class InFlightOperationsTests
{
    [Test]
    public async Task Drain_WithNothingRunning_CompletesAtOnce_AndRefusesNewCalls()
    {
        var inFlight = new InFlightOperations();

        Assert.That(await inFlight.DrainAsync(TimeSpan.FromSeconds(1)), Is.True);
        Assert.That(inFlight.IsDraining, Is.True);
        Assert.That(inFlight.TryEnter(), Is.False, "a call arriving during shutdown is refused");
    }

    [Test]
    public async Task Drain_WaitsForTheRunningCall()
    {
        var inFlight = new InFlightOperations();
        Assert.That(inFlight.TryEnter(), Is.True);
        Assert.That(inFlight.TryEnter(), Is.True);

        var drain = inFlight.DrainAsync(TimeSpan.FromSeconds(10));
        Assert.That(drain.IsCompleted, Is.False);
        inFlight.Exit();
        Assert.That(drain.IsCompleted, Is.False, "one call still running");
        inFlight.Exit();

        Assert.That(await drain, Is.True);
        Assert.That(inFlight.Count, Is.Zero);
    }

    [Test]
    public async Task Drain_GivesUpAfterTheTimeout()
    {
        var inFlight = new InFlightOperations();
        inFlight.TryEnter();

        Assert.That(await inFlight.DrainAsync(TimeSpan.FromMilliseconds(50)), Is.False);
        Assert.That(inFlight.Count, Is.EqualTo(1));
    }

    [Test]
    public async Task DrainService_ReportsWhatItWaitedFor()
    {
        var inFlight = new InFlightOperations();
        inFlight.TryEnter();
        var lines = new List<string>();
        var service = new OperationDrainService(inFlight, lines.Add);

        var stop = service.StopAsync(CancellationToken.None);
        inFlight.Exit();
        await stop;

        Assert.That(lines, Has.Count.EqualTo(2));
        Assert.That(lines[0], Does.Contain("waiting for 1 running operation"));
        Assert.That(lines[1], Is.EqualTo("All running operations finished."));
    }
}
