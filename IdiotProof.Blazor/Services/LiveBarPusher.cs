using IdiotProof.Blazor.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IdiotProof.Blazor.Services;

/// <summary>
/// Singleton background service that polls LiveBars every second.
/// Fires BarUpdated for any strategy that received a new write since the
/// last check — open LiveChart pages subscribe and call StateHasChanged()
/// over their existing Blazor circuit (no extra WebSocket needed).
/// </summary>
public sealed class LiveBarPusher(
    IDbContextFactory<AppDbContext> dbFactory,
    ILogger<LiveBarPusher> logger) : BackgroundService
{
    private DateTime lastCheck = DateTime.UtcNow;

    public event Action<Guid>? BarUpdated;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken).ConfigureAwait(false);

            try
            {
                var since = lastCheck;
                var next = DateTime.UtcNow;

                await using var db = await dbFactory.CreateDbContextAsync(stoppingToken);
                var updatedStrategies = await db.LiveBars
                    .Where(b => b.WrittenUtc > since)
                    .Select(b => b.StrategyId)
                    .Distinct()
                    .ToListAsync(stoppingToken);

                // Fire all notifications before advancing the watermark. A per-item
                // try-catch ensures one broken subscriber can't block later strategies.
                // lastCheck advances only after the full loop so a mid-loop exception
                // (outer catch) leaves the watermark at `since` for a retry.
                foreach (var sid in updatedStrategies)
                {
                    try { BarUpdated?.Invoke(sid); }
                    catch (Exception ex) { logger.LogWarning(ex, "BarUpdated subscriber fault for strategy {Id}", sid); }
                }

                lastCheck = next;
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                logger.LogError(ex, "LiveBarPusher poll failed");
            }
        }
    }
}
