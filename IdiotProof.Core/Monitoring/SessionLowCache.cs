using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using IdiotProof.DataFeeds;
using IdiotProof.Scripting;
using IdiotProof.Shared;
using Microsoft.Extensions.Logging;

namespace IdiotProof.Core.Monitoring;

/// <summary>
/// The low of each symbol's current ET day since 4:00 AM ET, for <c>DippedTo(level)</c> (IP-T363).
/// The Monitor's 1-minute window holds only 240 bars (4 hours), so a premarket dip followed by a
/// late-morning reclaim fell out of it. This fetches the day's 5-minute bars from the feed (never
/// SQL), once per symbol per day from 4:00 ET and then incrementally, so a restart or DB outage
/// rebuilds the same low from the feed on the next look.
/// </summary>
public sealed class SessionLowCache(IMarketDataFeed feed, ILogger logger)
{
    private static readonly TimeSpan BarSize = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan Refresh = TimeSpan.FromSeconds(60);

    private sealed record Entry(DateOnly Day, decimal? Low, DateTime FetchedThroughUtc, DateTime LastFetchUtc);

    private readonly ConcurrentDictionary<string, Entry> entries = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Lowers <see cref="IndicatorSnapshot.SessionLow"/> to the day's low since 4:00 ET when the feed
    /// knows of a lower one. A fetch failure leaves the snapshot's own window low in place.
    /// </summary>
    public async Task ApplyAsync(string symbol, IndicatorSnapshot snapshot, DateTime nowUtc, CancellationToken ct)
    {
        var day = EtDay(snapshot.Timestamp);
        var low = await GetDayLowAsync(symbol, day, nowUtc, ct);
        if (low is { } l && (snapshot.SessionLow is not { } own || (double)l < own))
            snapshot.SessionLow = (double)l;
    }

    public void Forget(string symbol) => entries.TryRemove(symbol, out _);

    private static readonly ConditionalWeakTable<StrategyDefinition, object> usesDippedTo = new();

    /// <summary>Whether any entry condition, While guard or branch of <paramref name="def"/> uses DippedTo.</summary>
    public static bool Uses(StrategyDefinition def) =>
        (bool)usesDippedTo.GetValue(def, d =>
            d.EntryConditions.Concat(d.WhileConditions).Any(c => c.ToScript().Contains("DippedTo(", StringComparison.Ordinal))
            || d.ConditionalBlocks.Any(b => b.ToScript().Contains("DippedTo(", StringComparison.Ordinal)));

    private async Task<decimal?> GetDayLowAsync(string symbol, DateOnly day, DateTime nowUtc, CancellationToken ct)
    {
        entries.TryGetValue(symbol, out var entry);
        if (entry is not null && entry.Day != day) entry = null;
        if (entry is not null && nowUtc - entry.LastFetchUtc < Refresh) return entry.Low;

        var dayStartUtc = TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(new TimeOnly(4, 0)), MarketTime.Eastern);
        // Re-read the last bar fetched: it may have been still forming.
        var fromUtc = entry is null ? dayStartUtc : Max(dayStartUtc, entry.FetchedThroughUtc - BarSize);
        var low = entry?.Low;
        var through = entry?.FetchedThroughUtc ?? dayStartUtc;
        try
        {
            await foreach (var c in feed.GetHistoricalCandlesAsync(symbol, fromUtc, nowUtc, BarSize, ct))
            {
                if (c.StartUtc < dayStartUtc || EtDay(c.StartUtc) != day) continue;
                if (low is not { } l || c.Low < l) low = c.Low;
                if (c.StartUtc > through) through = c.StartUtc;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Day-low fetch for {Symbol} failed; DippedTo uses the 1-minute window this tick.", symbol);
            return entry?.Low;
        }

        entries[symbol] = new Entry(day, low, through, nowUtc);
        return low;
    }

    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;

    private static DateOnly EtDay(DateTime utc) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), MarketTime.Eastern));
}
