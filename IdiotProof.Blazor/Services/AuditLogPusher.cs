using IdiotProof.Blazor.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IdiotProof.Blazor.Services;

/// <summary>
/// Singleton background service that polls AuditLog every second.
/// Fires <see cref="LogChanged"/> only when the Monitor has written at least
/// one new audit row since the last check — the Logs page subscribes instead
/// of running its own polling timer.
/// </summary>
public sealed class AuditLogPusher(
    IDbContextFactory<AppDbContext> dbFactory,
    ILogger<AuditLogPusher> logger) : BackgroundService
{
    private DateTime lastCheck = DateTime.UtcNow;

    /// <summary>Raised when one or more new AuditLog rows were written.</summary>
    public event Action? LogChanged;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken).ConfigureAwait(false);

            try
            {
                var since = lastCheck;
                var next  = DateTime.UtcNow;

                await using var db = await dbFactory.CreateDbContextAsync(stoppingToken);
                var hasNew = await db.AuditLogs
                    .AnyAsync(a => a.TimestampUtc > since, stoppingToken);

                // Fire before advancing the watermark. If the event throws, lastCheck
                // stays at `since` so the next poll retries the same window.
                if (hasNew)
                    LogChanged?.Invoke();

                // Watermark advances whether new rows existed or not — no-new-data
                // iterations still need to slide the window forward.
                lastCheck = next;
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                logger.LogError(ex, "AuditLogPusher poll failed");
            }
        }
    }
}
