using IdiotProof.Blazor.Data;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Blazor.Services;

/// <summary>
/// Singleton background service that polls AuditLog every second.
/// Fires <see cref="LogChanged"/> only when the Monitor has written at least
/// one new audit row since the last check — the Logs page subscribes instead
/// of running its own polling timer.
/// </summary>
public sealed class AuditLogPusher(IDbContextFactory<AppDbContext> dbFactory) : BackgroundService
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

                lastCheck = next;

                if (hasNew)
                    LogChanged?.Invoke();
            }
            catch (OperationCanceledException) { return; }
            catch { /* never crash the background service */ }
        }
    }
}
