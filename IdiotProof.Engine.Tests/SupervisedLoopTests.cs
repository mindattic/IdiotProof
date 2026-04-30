using IdiotProof.Engine;

namespace IdiotProof.Engine.Tests;

public class SupervisedLoopTests
{
    [Fact]
    public async Task SuccessfulTick_InvokesOnTickSucceeded()
    {
        var successCount = 0;
        using var cts = new CancellationTokenSource();

        var options = new SupervisedLoopOptions
        {
            Tick = _ => Task.CompletedTask,
            Interval = TimeSpan.FromMilliseconds(10),
            MinBackoff = TimeSpan.FromMilliseconds(10),
            MaxBackoff = TimeSpan.FromMilliseconds(50),
            OnTickSucceeded = () =>
            {
                if (Interlocked.Increment(ref successCount) >= 2)
                    cts.Cancel();
            }
        };

        await SupervisedLoop.RunAsync(options, cts.Token);

        Assert.True(successCount >= 2, $"Expected >= 2 successful ticks, got {successCount}");
    }

    [Fact]
    public async Task FailingTick_InvokesOnTickFailedWithCount_AndContinues()
    {
        var failures = new List<(string message, int count)>();
        using var cts = new CancellationTokenSource();

        var options = new SupervisedLoopOptions
        {
            Tick = _ => throw new InvalidOperationException("boom"),
            Interval = TimeSpan.FromMilliseconds(10),
            MinBackoff = TimeSpan.FromMilliseconds(10),
            MaxBackoff = TimeSpan.FromMilliseconds(20),
            OnTickFailed = (ex, count) =>
            {
                lock (failures) failures.Add((ex.Message, count));
                if (count >= 3) cts.Cancel();
            }
        };

        await SupervisedLoop.RunAsync(options, cts.Token);

        Assert.True(failures.Count >= 3, $"Expected >= 3 failures, got {failures.Count}");
        Assert.All(failures, f => Assert.Equal("boom", f.message));
        Assert.Equal(1, failures[0].count);
        Assert.Equal(2, failures[1].count);
        Assert.Equal(3, failures[2].count);
    }

    [Fact]
    public async Task SuccessAfterFailures_ResetsConsecutiveCounter()
    {
        var ticks = 0;
        var seenFailureCounts = new List<int>();
        using var cts = new CancellationTokenSource();

        var options = new SupervisedLoopOptions
        {
            Tick = _ =>
            {
                var n = Interlocked.Increment(ref ticks);
                // Tick 1 & 2 fail, tick 3 succeeds, tick 4 fails — counter must reset to 1, not 3.
                if (n is 1 or 2 or 4) throw new InvalidOperationException();
                return Task.CompletedTask;
            },
            Interval = TimeSpan.FromMilliseconds(5),
            MinBackoff = TimeSpan.FromMilliseconds(5),
            MaxBackoff = TimeSpan.FromMilliseconds(20),
            OnTickFailed = (_, count) =>
            {
                lock (seenFailureCounts) seenFailureCounts.Add(count);
                if (seenFailureCounts.Count >= 3) cts.Cancel();
            }
        };

        await SupervisedLoop.RunAsync(options, cts.Token);

        Assert.Equal([1, 2, 1], seenFailureCounts);
    }

    [Fact]
    public async Task Cancellation_ExitsCleanly()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        var ticks = 0;

        var options = new SupervisedLoopOptions
        {
            Tick = _ => { Interlocked.Increment(ref ticks); return Task.CompletedTask; },
            Interval = TimeSpan.FromMilliseconds(10),
            MinBackoff = TimeSpan.FromMilliseconds(10),
            MaxBackoff = TimeSpan.FromMilliseconds(50)
        };

        await SupervisedLoop.RunAsync(options, cts.Token);
        // No assertion on ticks count beyond "we exited" — the contract is "cancellation exits cleanly".
        Assert.True(cts.IsCancellationRequested);
    }

    [Fact]
    public async Task TickThrowingOperationCanceled_DuringCancellation_ExitsCleanly()
    {
        using var cts = new CancellationTokenSource();

        var options = new SupervisedLoopOptions
        {
            Tick = ct =>
            {
                cts.Cancel();
                ct.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            },
            Interval = TimeSpan.FromMilliseconds(10),
            MinBackoff = TimeSpan.FromMilliseconds(10),
            MaxBackoff = TimeSpan.FromMilliseconds(50)
        };

        // Should NOT throw — OperationCanceledException tied to the supplied token must be swallowed.
        await SupervisedLoop.RunAsync(options, cts.Token);
    }

    [Fact]
    public async Task HeartbeatFile_IsWrittenAfterSuccessfulTick()
    {
        var heartbeat = Path.Combine(Path.GetTempPath(), $"sup-loop-test-{Guid.NewGuid():N}.heartbeat");
        try
        {
            using var cts = new CancellationTokenSource();
            var ticks = 0;

            var options = new SupervisedLoopOptions
            {
                Tick = _ =>
                {
                    if (Interlocked.Increment(ref ticks) >= 1) cts.CancelAfter(50);
                    return Task.CompletedTask;
                },
                Interval = TimeSpan.FromMilliseconds(10),
                MinBackoff = TimeSpan.FromMilliseconds(10),
                MaxBackoff = TimeSpan.FromMilliseconds(50),
                HeartbeatPath = heartbeat
            };

            await SupervisedLoop.RunAsync(options, cts.Token);

            Assert.True(File.Exists(heartbeat), "Heartbeat file must exist after a tick");
            var content = await File.ReadAllTextAsync(heartbeat);
            Assert.Contains("status=ok", content);
            Assert.Contains("consecutiveFailures=0", content);
        }
        finally
        {
            if (File.Exists(heartbeat)) File.Delete(heartbeat);
        }
    }

    [Fact]
    public async Task HeartbeatFile_RecordsFailure()
    {
        var heartbeat = Path.Combine(Path.GetTempPath(), $"sup-loop-test-{Guid.NewGuid():N}.heartbeat");
        try
        {
            using var cts = new CancellationTokenSource();
            var ticks = 0;

            var options = new SupervisedLoopOptions
            {
                Tick = _ =>
                {
                    Interlocked.Increment(ref ticks);
                    throw new InvalidOperationException("boom");
                },
                Interval = TimeSpan.FromMilliseconds(10),
                MinBackoff = TimeSpan.FromMilliseconds(10),
                MaxBackoff = TimeSpan.FromMilliseconds(20),
                HeartbeatPath = heartbeat,
                OnTickFailed = (_, count) =>
                {
                    if (count >= 1) cts.Cancel();
                }
            };

            await SupervisedLoop.RunAsync(options, cts.Token);

            Assert.True(File.Exists(heartbeat));
            var content = await File.ReadAllTextAsync(heartbeat);
            Assert.Contains("status=fail", content);
            Assert.Contains("error=boom", content);
        }
        finally
        {
            if (File.Exists(heartbeat)) File.Delete(heartbeat);
        }
    }

    [Fact]
    public async Task NullTick_ThrowsArgumentNullException()
    {
        var options = new SupervisedLoopOptions { Tick = null! };
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => SupervisedLoop.RunAsync(options, CancellationToken.None));
    }
}
