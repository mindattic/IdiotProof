using Microsoft.Extensions.Hosting;

namespace IdiotProof.Operations.Hosting;

/// <summary>
/// Counts the operations the Hub is running right now so a graceful shutdown (IP-T79) can wait for
/// them: once <see cref="DrainAsync"/> starts, <see cref="TryEnter"/> refuses new calls (the Hub
/// answers 503 "shutting down") and the drain completes when the last running call exits or the
/// timeout passes. An order call half-way through the TransactionGateway therefore finishes and
/// writes its ledger row instead of being torn down with the process.
/// </summary>
public sealed class InFlightOperations
{
    private readonly Lock gate = new();
    private int count;
    private bool draining;
    private TaskCompletionSource drained = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Operations running now.</summary>
    public int Count
    {
        get { lock (gate) return count; }
    }

    /// <summary>True once shutdown has begun; new operations are refused.</summary>
    public bool IsDraining
    {
        get { lock (gate) return draining; }
    }

    /// <summary>Registers a call; false when the Hub is shutting down (refuse it).</summary>
    public bool TryEnter()
    {
        lock (gate)
        {
            if (draining) return false;
            count++;
            return true;
        }
    }

    /// <summary>Marks a call finished. Call exactly once per successful <see cref="TryEnter"/>.</summary>
    public void Exit()
    {
        lock (gate)
        {
            if (count > 0) count--;
            if (draining && count == 0) drained.TrySetResult();
        }
    }

    /// <summary>
    /// Stops accepting calls and waits for the running ones. True when they all finished, false
    /// when <paramref name="timeout"/> passed (or <paramref name="ct"/> fired) with some still running.
    /// </summary>
    public async Task<bool> DrainAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        Task wait;
        lock (gate)
        {
            draining = true;
            if (count == 0) return true;
            wait = drained.Task;
        }
        try
        {
            await wait.WaitAsync(timeout, ct);
            return true;
        }
        catch (TimeoutException) { return false; }
        catch (OperationCanceledException) { return false; }
    }
}

/// <summary>
/// The Hub's first stop step (IP-T79). Register it AFTER the Monitor: hosted services stop in
/// reverse order, so this drains in-flight operations first, then the Monitor's loop stops and
/// its leader lease is released.
/// </summary>
public sealed class OperationDrainService(InFlightOperations inFlight, Action<string>? report = null) : IHostedService
{
    /// <summary>How long shutdown waits for running operations before moving on.</summary>
    public static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(25);

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        var running = inFlight.Count;
        if (running > 0) report?.Invoke($"Shutting down: waiting for {running} running operation(s) to finish (up to {DrainTimeout.TotalSeconds:0}s)...");
        var finished = await inFlight.DrainAsync(DrainTimeout, cancellationToken);
        if (running > 0)
            report?.Invoke(finished ? "All running operations finished." : $"{inFlight.Count} operation(s) still running after {DrainTimeout.TotalSeconds:0}s; stopping anyway.");
    }
}
