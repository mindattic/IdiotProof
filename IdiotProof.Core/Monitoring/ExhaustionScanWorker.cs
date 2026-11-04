using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IdiotProof.Core.Monitoring;

/// <summary>
/// Runs <see cref="ExhaustionScanner"/> every five minutes, outside the Monitor's tick loop. Idle unless
/// SettingsKv <c>exhaustion.enabled</c> is "true" (absent, i.e. off, by default), and only on the
/// instance holding the Monitor leader lease, so a standby never arms a duplicate.
/// </summary>
public sealed class ExhaustionScanWorker(
    ExhaustionScanner scanner,
    MonitorRuntimeState monitor,
    TimeProvider time,
    ILogger<ExhaustionScanWorker> log) : BackgroundService
{
    private static readonly TimeSpan Cadence = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Cadence, time);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { log.LogWarning(ex, "Exhaustion scan failed"); }
        }
    }

    /// <summary>One scan, or false when disabled or this instance is not the leader.</summary>
    public async Task<bool> RunOnceAsync(CancellationToken ct)
    {
        if (!await scanner.IsEnabledAsync(ct)) return false;
        if (!await monitor.IsLeaderAsync(ct)) return false;
        await scanner.RunScanAsync(time.GetUtcNow().UtcDateTime, ct: ct);
        return true;
    }
}
