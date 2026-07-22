using IdiotProof.Blazor.Data;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Blazor.Services;

/// <summary>
/// Singleton background service that polls LiveBars every second.
/// Fires BarUpdated for any strategy that received a new write since the
/// last check — open LiveChart pages subscribe and call StateHasChanged()
/// over their existing Blazor circuit (no extra WebSocket needed).
/// </summary>
public sealed class LiveBarPusher(IDbContextFactory<AppDbContext> dbFactory) : BackgroundService
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

                // Only advance the watermark after a successful query. If the
                // DB call throws, lastCheck stays at `since` so the next poll
                // retries the same window instead of silently skipping it.
                lastCheck = next;

                foreach (var sid in updatedStrategies)
                    BarUpdated?.Invoke(sid);
            }
            catch (OperationCanceledException) { return; }
            catch { /* never crash the background service */ }
        }
    }
}
