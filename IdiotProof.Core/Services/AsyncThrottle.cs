namespace IdiotProof.Blazor.Services;

/// <summary>
/// Caps concurrency and enforces a minimum spacing between calls to one external
/// API. The research scanner is the first thing in this codebase to hit EDGAR,
/// Alpaca, and the LLM gateway across hundreds/thousands of tickers per pass
/// instead of one ticker at a time — nothing here throttled before because
/// nothing needed to.
/// </summary>
public sealed class AsyncThrottle : IDisposable
{
    private readonly SemaphoreSlim gate;
    private readonly TimeSpan      minInterval;
    private readonly object        lastCallLock = new();
    private DateTime               lastCallUtc  = DateTime.MinValue;

    public AsyncThrottle(int maxConcurrent, TimeSpan minInterval)
    {
        if (maxConcurrent < 1) throw new ArgumentOutOfRangeException(nameof(maxConcurrent));
        gate = new SemaphoreSlim(maxConcurrent, maxConcurrent);
        this.minInterval = minInterval;
    }

    /// <summary>Runs <paramref name="work"/> once a concurrency slot is free and
    /// at least <c>minInterval</c> has elapsed since the last call started.</summary>
    public async Task<T> RunAsync<T>(Func<Task<T>> work, CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            TimeSpan wait;
            lock (lastCallLock)
            {
                var since = DateTime.UtcNow - lastCallUtc;
                wait = since < minInterval ? minInterval - since : TimeSpan.Zero;
                lastCallUtc = DateTime.UtcNow + wait;
            }
            if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);

            return await work();
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task RunAsync(Func<Task> work, CancellationToken ct = default)
    {
        await RunAsync(async () => { await work(); return true; }, ct);
    }

    public void Dispose() => gate.Dispose();
}
