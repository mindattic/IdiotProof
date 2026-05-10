using IdiotProof.Engine;

namespace IdiotProof.Engine.Tests;

public class SupervisedLoopTests
{
    [Test]
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

        Assert.That(successCount, Is.GreaterThanOrEqualTo(2), $"Expected >= 2 successful ticks, got {successCount}");
    }

    [Test]
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

        Assert.That(failures.Count, Is.GreaterThanOrEqualTo(3), $"Expected >= 3 failures, got {failures.Count}");
        Assert.That(failures, Has.All.Matches<(string message, int count)>(f => f.message == "boom"));
        Assert.That(failures[0].count, Is.EqualTo(1));
        Assert.That(failures[1].count, Is.EqualTo(2));
        Assert.That(failures[2].count, Is.EqualTo(3));
    }

    [Test]
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

        Assert.That(seenFailureCounts, Is.EqualTo(new[] { 1, 2, 1 }));
    }

    [Test]
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
        Assert.That(cts.IsCancellationRequested, Is.True);
    }

    [Test]
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

    [Test]
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

            Assert.That(File.Exists(heartbeat), Is.True, "Heartbeat file must exist after a tick");
            var content = await File.ReadAllTextAsync(heartbeat);
            Assert.That(content, Does.Contain("status=ok"));
            Assert.That(content, Does.Contain("consecutiveFailures=0"));
        }
        finally
        {
            if (File.Exists(heartbeat)) File.Delete(heartbeat);
        }
    }

    [Test]
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

            Assert.That(File.Exists(heartbeat), Is.True);
            var content = await File.ReadAllTextAsync(heartbeat);
            Assert.That(content, Does.Contain("status=fail"));
            Assert.That(content, Does.Contain("error=boom"));
        }
        finally
        {
            if (File.Exists(heartbeat)) File.Delete(heartbeat);
        }
    }

    [Test]
    public void NullTick_ThrowsArgumentNullException()
    {
        var options = new SupervisedLoopOptions { Tick = null! };
        Assert.ThrowsAsync<ArgumentNullException>(
            () => SupervisedLoop.RunAsync(options, CancellationToken.None));
    }
}
