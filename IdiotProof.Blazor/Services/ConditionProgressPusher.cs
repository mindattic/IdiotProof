using IdiotProof.Blazor.Data;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Blazor.Services;

/// <summary>
/// Singleton background service that polls ConditionProgress every second.
/// Fires <see cref="ProgressChanged"/> only when the Monitor has written a
/// new tick since the last check — Strategies page components subscribe and
/// re-render without owning their own polling timers.
/// </summary>
public sealed class ConditionProgressPusher(IDbContextFactory<AppDbContext> dbFactory) : BackgroundService
{
    private DateTime lastCheck = DateTime.UtcNow;

    /// <summary>
    /// Raised with the list of StrategyIds whose ConditionProgress row was
    /// updated since the last poll. Only fires when at least one row changed.
    /// </summary>
    public event Action<IReadOnlyList<Guid>>? ProgressChanged;

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
                var changed = await db.ConditionProgress
                    .Where(p => p.EvaluatedUtc > since)
                    .Select(p => p.StrategyId)
                    .ToListAsync(stoppingToken);

                // Only advance the watermark after a successful query so a DB
                // hiccup retries the same window instead of silently skipping it.
                lastCheck = next;

                if (changed.Count > 0)
                    ProgressChanged?.Invoke(changed);
            }
            catch (OperationCanceledException) { return; }
            catch { /* never crash the background service */ }
        }
    }
}
