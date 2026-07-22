using IdiotProof.Blazor.Data;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Blazor.Services;

/// <summary>
/// Singleton background service that polls ConditionProgress every second.
/// Fires <see cref="ProgressChanged"/> only when a strategy's condition state
/// or price actually changed since the last push:
///
///   • Condition state (PassedCount, TotalCount, FirstFailingVerb) — any change fires.
///   • LastPrice — only fires when the price rounds differently at 2 decimal places
///     (cents precision), so sub-penny noise never causes a push.
///
/// When the Monitor is hibernating no rows have a new EvaluatedUtc, so the
/// query returns nothing and the event never fires — the UI is truly quiet.
/// </summary>
public sealed class ConditionProgressPusher(IDbContextFactory<AppDbContext> dbFactory) : BackgroundService
{
    private DateTime lastCheck = DateTime.UtcNow;

    // Per-strategy snapshot of what was last pushed to the UI.
    private readonly Dictionary<Guid, SentState> lastSent = new();

    private sealed record SentState(int PassedCount, int TotalCount, string? FirstFailingVerb, decimal? PriceCents);

    /// <summary>
    /// Raised with the list of StrategyIds whose displayed state changed.
    /// Only fires when at least one strategy has a meaningful change.
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
                var rows = await db.ConditionProgress
                    .Where(p => p.EvaluatedUtc > since)
                    .Select(p => new
                    {
                        p.StrategyId,
                        p.PassedCount,
                        p.TotalCount,
                        p.FirstFailingVerb,
                        p.LastPrice,
                    })
                    .ToListAsync(stoppingToken);

                // Only advance the watermark after a successful query.
                lastCheck = next;

                var changed = new List<Guid>(rows.Count);
                foreach (var r in rows)
                {
                    // Round price to cents so sub-penny noise is ignored.
                    var priceCents = r.LastPrice.HasValue
                        ? Math.Round(r.LastPrice.Value, 2)
                        : (decimal?)null;

                    if (!lastSent.TryGetValue(r.StrategyId, out var prev))
                    {
                        // First time seeing this strategy — always push.
                        lastSent[r.StrategyId] = new SentState(r.PassedCount, r.TotalCount, r.FirstFailingVerb, priceCents);
                        changed.Add(r.StrategyId);
                        continue;
                    }

                    bool conditionChanged = prev.PassedCount      != r.PassedCount
                                        || prev.TotalCount        != r.TotalCount
                                        || prev.FirstFailingVerb  != r.FirstFailingVerb;
                    bool priceChanged     = priceCents            != prev.PriceCents;

                    if (conditionChanged || priceChanged)
                    {
                        lastSent[r.StrategyId] = new SentState(r.PassedCount, r.TotalCount, r.FirstFailingVerb, priceCents);
                        changed.Add(r.StrategyId);
                    }
                }

                if (changed.Count > 0)
                    ProgressChanged?.Invoke(changed);
            }
            catch (OperationCanceledException) { return; }
            catch { /* never crash the background service */ }
        }
    }
}
