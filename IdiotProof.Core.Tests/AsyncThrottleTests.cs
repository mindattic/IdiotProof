using System.Diagnostics;
using IdiotProof.Blazor.Services;

namespace IdiotProof.Blazor.Tests;

[TestFixture]
public class AsyncThrottleTests
{
    [Test]
    public async Task RunAsync_ReturnsWorkResult()
    {
        using var throttle = new AsyncThrottle(maxConcurrent: 4, minInterval: TimeSpan.Zero);
        var result = await throttle.RunAsync(() => Task.FromResult(42));
        Assert.That(result, Is.EqualTo(42));
    }

    [Test]
    public async Task RunAsync_LimitsConcurrencyToMax()
    {
        using var throttle = new AsyncThrottle(maxConcurrent: 2, minInterval: TimeSpan.Zero);
        var concurrentNow = 0;
        var maxObserved = 0;
        var gate = new object();

        var tasks = Enumerable.Range(0, 8).Select(_ => throttle.RunAsync(async () =>
        {
            lock (gate)
            {
                concurrentNow++;
                maxObserved = Math.Max(maxObserved, concurrentNow);
            }
            await Task.Delay(30);
            lock (gate) { concurrentNow--; }
            return true;
        }));

        await Task.WhenAll(tasks);
        Assert.That(maxObserved, Is.LessThanOrEqualTo(2));
    }

    [Test]
    public async Task RunAsync_EnforcesMinimumIntervalBetweenCallStarts()
    {
        using var throttle = new AsyncThrottle(maxConcurrent: 1, minInterval: TimeSpan.FromMilliseconds(50));
        var sw = Stopwatch.StartNew();

        await throttle.RunAsync(() => Task.FromResult(true));
        await throttle.RunAsync(() => Task.FromResult(true));
        await throttle.RunAsync(() => Task.FromResult(true));

        sw.Stop();
        // Three calls spaced >= 50ms apart should take at least ~100ms total.
        Assert.That(sw.ElapsedMilliseconds, Is.GreaterThanOrEqualTo(90));
    }

    [Test]
    public void RunAsync_RespectsCancellation()
    {
        using var throttle = new AsyncThrottle(maxConcurrent: 1, minInterval: TimeSpan.Zero);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThatAsync(
            async () => await throttle.RunAsync(() => Task.FromResult(true), cts.Token),
            Throws.InstanceOf<OperationCanceledException>());
    }

    [Test]
    public void Constructor_RejectsNonPositiveConcurrency()
    {
        Assert.That(() => new AsyncThrottle(0, TimeSpan.Zero), Throws.InstanceOf<ArgumentOutOfRangeException>());
    }
}
