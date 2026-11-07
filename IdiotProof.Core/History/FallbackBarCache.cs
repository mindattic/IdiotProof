using IdiotProof.Core.Evidence;
using IdiotProof.DataFeeds;
using IdiotProof.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace IdiotProof.Core.History;

/// <summary>
/// The app's bar cache (IP-T454): the permanent history database first, the app database's own
/// cache when the history server does not answer, so a stopped SQL Server instance never takes the
/// Monitor's bars down with it. After a failure the history database is skipped for
/// <see cref="RetryAfter"/> before it is tried again.
/// </summary>
public sealed class FallbackBarCache(IBarCache primary, IBarCache fallback, TimeProvider time, ILogger? log = null) : IBarCache
{
    public static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(5);
    private DateTime downUntilUtc = DateTime.MinValue;

    private bool PrimaryUp => time.GetUtcNow().UtcDateTime >= downUntilUtc;

    private async Task<T> Try<T>(Func<IBarCache, Task<T>> call)
    {
        if (PrimaryUp)
        {
            try { return await call(primary).ConfigureAwait(false); }
            catch (SqlException ex)
            {
                downUntilUtc = time.GetUtcNow().UtcDateTime + RetryAfter;
                log?.LogWarning("History database unavailable, using the app cache for {Minutes} min: {Message}", RetryAfter.TotalMinutes, ex.Message);
            }
        }
        return await call(fallback).ConfigureAwait(false);
    }

    public Task<HashSet<DateOnly>> CoveredDaysAsync(string symbol, BarTimeframe timeframe, DateOnly from, DateOnly to, CancellationToken ct) =>
        Try(c => c.CoveredDaysAsync(symbol, timeframe, from, to, ct));

    public Task SaveDaysAsync(string symbol, BarTimeframe timeframe, IReadOnlyList<(DateOnly Day, IReadOnlyList<Candle> Bars)> days, string feedName, DateTime nowUtc, CancellationToken ct) =>
        Try(async c => { await c.SaveDaysAsync(symbol, timeframe, days, feedName, nowUtc, ct).ConfigureAwait(false); return true; });

    public Task<List<Candle>> ReadAsync(string symbol, BarTimeframe timeframe, DateOnly from, DateOnly to, CancellationToken ct) =>
        Try(c => c.ReadAsync(symbol, timeframe, from, to, ct));
}
