namespace IdiotProof.Engine;

/// <summary>
/// Long-running loop that survives per-tick failures.
///
/// Behavior:
///  • Calls <see cref="SupervisedLoopOptions.Tick"/> on a fixed interval.
///  • Catches anything the tick throws, invokes <see cref="SupervisedLoopOptions.OnTickFailed"/>,
///    and continues — the loop never exits because of an evaluation error.
///  • Applies exponential backoff while consecutive failures are accumulating, capped at
///    <see cref="SupervisedLoopOptions.MaxBackoff"/>. Resets to the normal interval after the
///    next successful tick.
///  • Writes a heartbeat file (if <see cref="SupervisedLoopOptions.HeartbeatPath"/> is set)
///    after each tick — both successes and failures — so an external watchdog can detect
///    a hung process even when the strategy loop itself is silent.
///  • Exits cleanly on <see cref="OperationCanceledException"/> tied to the supplied token.
/// </summary>
public static class SupervisedLoop
{
    public static async Task RunAsync(SupervisedLoopOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Tick);

        var consecutiveFailures = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            var tickStartUtc = DateTime.UtcNow;
            var success = false;
            Exception? failure = null;

            try
            {
                await options.Tick(cancellationToken).ConfigureAwait(false);
                success = true;
                consecutiveFailures = 0;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                failure = ex;
                consecutiveFailures++;
            }

            if (success)
                options.OnTickSucceeded?.Invoke();
            else if (failure is not null)
                options.OnTickFailed?.Invoke(failure, consecutiveFailures);

            WriteHeartbeat(options.HeartbeatPath, tickStartUtc, success, failure, consecutiveFailures);

            var delay = consecutiveFailures == 0
                ? options.Interval
                : ComputeBackoff(consecutiveFailures, options.MinBackoff, options.MaxBackoff);

            try { await Task.Delay(delay, cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }

    private static TimeSpan ComputeBackoff(int consecutiveFailures, TimeSpan min, TimeSpan max)
    {
        // 2^(n-1) * min, capped at max. n=1 → min, n=2 → 2*min, n=3 → 4*min, ...
        var multiplier = Math.Min(1L << Math.Min(consecutiveFailures - 1, 30), 1L << 30);
        var ticks = min.Ticks * multiplier;
        if (ticks <= 0 || ticks > max.Ticks) return max;
        return TimeSpan.FromTicks(ticks);
    }

    private static void WriteHeartbeat(
        string? path,
        DateTime tickStartUtc,
        bool success,
        Exception? failure,
        int consecutiveFailures)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);

            var status = success ? "ok" : "fail";
            var line =
                $"{DateTime.UtcNow:O}\ttickStart={tickStartUtc:O}\tstatus={status}" +
                $"\tconsecutiveFailures={consecutiveFailures}" +
                (failure is null ? "" : $"\terror={Sanitize(failure.Message)}");

            File.WriteAllText(path, line);
        }
        catch
        {
            // Heartbeat is best-effort; never let a disk error kill the loop.
        }
    }

    private static string Sanitize(string s) => s.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');
}

public sealed class SupervisedLoopOptions
{
    public required Func<CancellationToken, Task> Tick { get; init; }

    public TimeSpan Interval { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan MinBackoff { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan MaxBackoff { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>If set, the loop writes a one-line status file after every tick.</summary>
    public string? HeartbeatPath { get; init; }

    /// <summary>Invoked after a tick throws. Args: exception, consecutive failure count.</summary>
    public Action<Exception, int>? OnTickFailed { get; init; }

    /// <summary>Invoked after a tick returns without throwing.</summary>
    public Action? OnTickSucceeded { get; init; }
}
