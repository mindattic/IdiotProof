using IdiotProof.Blazor.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

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
public sealed class ConditionProgressPusher(
    IDbContextFactory<AppDbContext> dbFactory,
    ILogger<ConditionProgressPusher> logger) : BackgroundService
{
    private DateTime lastCheck = DateTime.UtcNow;

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

                // Stage which IDs to notify + what their new SentState should be.
                // Do NOT touch lastSent or lastCheck yet — if the event dispatch throws,
                // neither watermark nor cache must advance so the next poll re-detects.
                var changed = new List<Guid>(rows.Count);
                var staged  = new Dictionary<Guid, SentState>(rows.Count);

                foreach (var r in rows)
                {
                    var priceCents = r.LastPrice.HasValue
                        ? Math.Round(r.LastPrice.Value, 2)
                        : (decimal?)null;

                    if (!lastSent.TryGetValue(r.StrategyId, out var prev))
                    {
                        changed.Add(r.StrategyId);
                        staged[r.StrategyId] = new SentState(r.PassedCount, r.TotalCount, r.FirstFailingVerb, priceCents);
                        continue;
                    }

                    bool conditionChanged = prev.PassedCount     != r.PassedCount
                                        || prev.TotalCount       != r.TotalCount
                                        || prev.FirstFailingVerb != r.FirstFailingVerb;
                    bool priceChanged     = priceCents           != prev.PriceCents;

                    if (conditionChanged || priceChanged)
                    {
                        changed.Add(r.StrategyId);
                        staged[r.StrategyId] = new SentState(r.PassedCount, r.TotalCount, r.FirstFailingVerb, priceCents);
                    }
                }

                if (changed.Count > 0)
                    ProgressChanged?.Invoke(changed);

                // Commit only after successful dispatch: apply staged cache updates
                // and advance the watermark. A throw above leaves both untouched so
                // the next iteration re-queries the same window and re-detects.
                foreach (var (k, v) in staged)
                    lastSent[k] = v;
                lastCheck = next;
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                logger.LogError(ex, "ConditionProgressPusher poll failed");
            }
        }
    }
}
