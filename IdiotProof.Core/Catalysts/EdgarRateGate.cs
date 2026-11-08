namespace IdiotProof.Core.Catalysts;

/// <summary>
/// Serializes every EDGAR request this process makes and keeps at least <see cref="MinSpacing"/>
/// between them (the SEC's fair-access limit is 10 requests per second; 120 ms leaves headroom).
/// </summary>
public sealed class EdgarRateGate(TimeProvider? time = null)
{
    private readonly TimeProvider time = time ?? TimeProvider.System;
    private readonly SemaphoreSlim gate = new(1, 1);
    private long lastTicks;

    public TimeSpan MinSpacing { get; init; } = TimeSpan.FromMilliseconds(120);

    public long Requests { get; private set; }

    public async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        // Spaces request STARTS (at most one per MinSpacing, under SEC's 10 req/s) without holding the gate for the call
        // itself, so concurrent callers overlap their network latency instead of queueing behind it.
        await gate.WaitAsync(ct);
        try
        {
            var now = time.GetTimestamp();
            var since = time.GetElapsedTime(lastTicks, now);
            if (lastTicks != 0 && since < MinSpacing) await Task.Delay(MinSpacing - since, time, ct);
            lastTicks = time.GetTimestamp();
            Requests++;
        }
        finally { gate.Release(); }
        return await call(ct);
    }
}
