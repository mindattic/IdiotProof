using IdiotProof.Core.Services;
using IdiotProof.Core.Data;
namespace IdiotProof.Core.Monitoring;

/// <summary>
/// What the hosted Monitor is doing right now, in memory, for <c>monitor status|heartbeat|lease</c>
/// (IP-T256): whether its loop runs, whether it holds the SQL leader lease, when it last ticked and
/// how that went, and whether it is paused. <see cref="MonitorWorker"/> writes it; operations read
/// a <see cref="Snapshot"/>. Registered by <c>AddIdiotProofMonitor</c>, so a Hub started with
/// <c>IDIOTPROOF_HUB_MONITOR=0</c> has none and the operations say "not hosted".
/// </summary>
public sealed class MonitorRuntimeState(TimeProvider? clock = null)
{
    private readonly TimeProvider clock = clock ?? TimeProvider.System;
    private readonly Lock gate = new();

    private bool running;
    private bool leaseHeld;
    private bool paused;
    private bool tickInProgress;
    private DateTime? startedUtc;
    private DateTime? leaseAcquiredUtc;
    private DateTime? leaseReleasedUtc;
    private DateTime? pausedSinceUtc;
    private DateTime? lastTickUtc;
    private DateTime? lastTickSuccessUtc;
    private long ticks;
    private long failedTicks;
    private int consecutiveFailures;
    private string? lastError;
    private string? window;
    private int activeStrategies;
    private string? feed;
    private TimeSpan interval;

    /// <summary>The worker started and is waiting for (or holds) the lease.</summary>
    public void MarkStarting(string feedName, TimeSpan tickInterval)
    {
        lock (gate)
        {
            running = true;
            startedUtc = Now;
            feed = feedName;
            interval = tickInterval;
        }
    }

    /// <summary>This process now holds the leader lease and will evaluate and trade.</summary>
    public void MarkLeaseAcquired()
    {
        lock (gate)
        {
            leaseHeld = true;
            leaseAcquiredUtc = Now;
            leaseReleasedUtc = null;
        }
    }

    /// <summary>The loop ended and the lease was released (graceful shutdown, IP-T79).</summary>
    public void MarkStopped()
    {
        lock (gate)
        {
            running = false;
            tickInProgress = false;
            if (leaseHeld) leaseReleasedUtc = Now;
            leaseHeld = false;
        }
    }

    /// <summary>A tick began. Graceful shutdown waits for <see cref="Snapshot"/>.TickInProgress to clear.</summary>
    public void MarkTickStarted()
    {
        lock (gate) tickInProgress = true;
    }

    /// <summary>A tick finished (success or failure). <paramref name="error"/> is null on success.</summary>
    public void MarkTickFinished(string? error)
    {
        lock (gate)
        {
            tickInProgress = false;
            ticks++;
            lastTickUtc = Now;
            if (error is null)
            {
                lastTickSuccessUtc = lastTickUtc;
                consecutiveFailures = 0;
            }
            else
            {
                failedTicks++;
                consecutiveFailures++;
                lastError = error;
            }
        }
    }

    /// <summary>The trading window the last tick classified (Hibernate, Premarket, Rth, …).</summary>
    public void SetWindow(string name)
    {
        lock (gate) window = name;
    }

    /// <summary>How many active strategies the last evaluating tick read.</summary>
    public void SetActiveStrategies(int count)
    {
        lock (gate) activeStrategies = count;
    }

    /// <summary>Paused per the SettingsKv flag (IP-T80), as the last tick saw it.</summary>
    public void SetPaused(bool value)
    {
        lock (gate)
        {
            if (value && !paused) pausedSinceUtc = Now;
            if (!value) pausedSinceUtc = null;
            paused = value;
        }
    }

    /// <summary>A consistent copy for an operation to render.</summary>
    public MonitorRuntimeSnapshot Snapshot()
    {
        lock (gate)
        {
            return new MonitorRuntimeSnapshot
            {
                Running = running,
                LeaseHeld = leaseHeld,
                Paused = paused,
                TickInProgress = tickInProgress,
                StartedUtc = startedUtc,
                LeaseAcquiredUtc = leaseAcquiredUtc,
                LeaseReleasedUtc = leaseReleasedUtc,
                PausedSinceUtc = pausedSinceUtc,
                LastTickUtc = lastTickUtc,
                LastTickSuccessUtc = lastTickSuccessUtc,
                Ticks = ticks,
                FailedTicks = failedTicks,
                ConsecutiveFailures = consecutiveFailures,
                LastError = lastError,
                Window = window,
                ActiveStrategies = activeStrategies,
                Feed = feed,
                IntervalSeconds = interval.TotalSeconds,
            };
        }
    }

    private DateTime Now => clock.GetUtcNow().UtcDateTime;
}

/// <summary>A point-in-time copy of <see cref="MonitorRuntimeState"/>. Times are UTC; render them in ET.</summary>
public sealed record MonitorRuntimeSnapshot
{
    public bool Running { get; init; }
    public bool LeaseHeld { get; init; }
    public bool Paused { get; init; }
    public bool TickInProgress { get; init; }
    public DateTime? StartedUtc { get; init; }
    public DateTime? LeaseAcquiredUtc { get; init; }
    public DateTime? LeaseReleasedUtc { get; init; }
    public DateTime? PausedSinceUtc { get; init; }
    public DateTime? LastTickUtc { get; init; }
    public DateTime? LastTickSuccessUtc { get; init; }
    public long Ticks { get; init; }
    public long FailedTicks { get; init; }
    public int ConsecutiveFailures { get; init; }
    public string? LastError { get; init; }
    public string? Window { get; init; }
    public int ActiveStrategies { get; init; }
    public string? Feed { get; init; }
    public double IntervalSeconds { get; init; }
}
