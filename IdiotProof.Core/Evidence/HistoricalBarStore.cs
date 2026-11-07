using System.Globalization;
using System.Net;
using IdiotProof.Core.Data;
using IdiotProof.DataFeeds;
using IdiotProof.Evidence;
using IdiotProof.Models;
using IdiotProof.Scripting;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Core.Evidence;

/// <summary>
/// SQL-backed cache in front of <see cref="IMarketDataFeed"/>. A completed ET day is fetched at most
/// once per (symbol, timeframe) and marked in <see cref="HistoricalBarCoverage"/>; today's still-forming
/// day is fetched live and never cached. Rate-limit and gateway errors back off exponentially.
/// </summary>
public sealed class HistoricalBarStore : IHistoricalBarStore
{
    private const int MaxAttempts = 5;
    private const int MaxDaysPerRequest = 30;

    private readonly IBarCache cache;
    private readonly IMarketDataFeed feed;
    private readonly TimeProvider clock;
    private readonly Func<TimeSpan, CancellationToken, Task> delay;
    private readonly ISplitSource? splits;

    public HistoricalBarStore(IDbContextFactory<AppDbContext> dbFactory, IMarketDataFeed feed,
        TimeProvider? clock = null, Func<TimeSpan, CancellationToken, Task>? delay = null, ISplitSource? splits = null)
        : this(new AppDbBarCache(dbFactory), feed, clock, delay, splits)
    {
    }

    /// <summary>The same read-through store over any <see cref="IBarCache"/> (IP-T454: the history database).</summary>
    public HistoricalBarStore(IBarCache cache, IMarketDataFeed feed,
        TimeProvider? clock = null, Func<TimeSpan, CancellationToken, Task>? delay = null, ISplitSource? splits = null)
    {
        this.cache = cache;
        this.feed = feed;
        this.clock = clock ?? TimeProvider.System;
        this.delay = delay ?? Task.Delay;
        this.splits = splits;
    }

    public async Task<SplitAdjustedBars> GetSplitAdjustedBarsForSymbolsAsync(
        IEnumerable<string> symbols, DateOnly fromEt, DateOnly toEt, BarTimeframe timeframe, int maxConcurrency = 4, CancellationToken ct = default)
    {
        var raw = await GetBarsForSymbolsAsync(symbols, fromEt, toEt, timeframe, maxConcurrency, ct).ConfigureAwait(false);
        if (splits is null || raw.Count == 0)
            return new SplitAdjustedBars(raw, new Dictionary<string, int>(), SplitDataAvailable: false);

        var events = await splits.GetSplitsAsync(raw.Keys, fromEt, toEt, ct).ConfigureAwait(false);
        var bars = new Dictionary<string, IReadOnlyList<Candle>>(StringComparer.OrdinalIgnoreCase);
        var applied = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (symbol, list) in raw)
        {
            var symbolSplits = events.TryGetValue(symbol, out var s) ? s : Array.Empty<SplitEvent>();
            var adjusted = SplitAdjuster.Adjust(list, symbolSplits, c => DateOf(c, timeframe));
            bars[symbol] = adjusted.Bars;
            if (adjusted.SplitsApplied > 0) applied[symbol] = adjusted.SplitsApplied;
        }
        return new SplitAdjustedBars(bars, applied, SplitDataAvailable: true);
    }

    public async Task<IReadOnlyList<Candle>> GetBarsAsync(string symbol, DateOnly fromEt, DateOnly toEt, BarTimeframe timeframe, CancellationToken ct = default)
    {
        if (toEt < fromEt) return Array.Empty<Candle>();
        symbol = symbol.Trim().ToUpperInvariant();
        var tf = Code(timeframe);
        var todayEt = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(clock.GetUtcNow().UtcDateTime, MarketTime.Eastern));

        var tradingDays = new List<DateOnly>();
        for (var d = fromEt; d <= toEt; d = d.AddDays(1))
            if (IsTradingDay(d)) tradingDays.Add(d);

        var covered = await cache.CoveredDaysAsync(symbol, timeframe, fromEt, toEt, ct).ConfigureAwait(false);
        var missing = tradingDays.Where(d => !covered.Contains(d)).ToList();
        var live = new List<Candle>();

        foreach (var run in Runs(missing))
        {
            var fetched = await FetchWithRetryAsync(symbol, run[0], run[^1], timeframe, ct).ConfigureAwait(false);
            var byDay = fetched.GroupBy(c => DateOf(c, timeframe)).ToDictionary(g => g.Key, g => g.ToList());

            var completed = run.Where(d => d < todayEt).ToList();
            foreach (var d in run.Where(d => d >= todayEt))
                if (byDay.TryGetValue(d, out var partial)) live.AddRange(partial);
            if (completed.Count == 0) continue;
            var days = completed.Select(d => (d, (IReadOnlyList<Candle>)(byDay.TryGetValue(d, out var list) ? list : new List<Candle>()))).ToList();
            await cache.SaveDaysAsync(symbol, timeframe, days, feed.FeedName, clock.GetUtcNow().UtcDateTime, ct).ConfigureAwait(false);
        }

        var stored = await cache.ReadAsync(symbol, timeframe, fromEt, toEt, ct).ConfigureAwait(false);
        return stored
            .Concat(live)
            .DistinctBy(c => c.StartUtc)
            .OrderBy(c => c.StartUtc)
            .ToList();
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<Candle>>> GetBarsForSymbolsAsync(
        IEnumerable<string> symbols, DateOnly fromEt, DateOnly toEt, BarTimeframe timeframe, int maxConcurrency = 4, CancellationToken ct = default)
    {
        var distinct = symbols.Select(s => s.Trim().ToUpperInvariant()).Where(s => s.Length > 0).Distinct().ToList();
        var result = new Dictionary<string, IReadOnlyList<Candle>>(StringComparer.OrdinalIgnoreCase);
        using var gate = new SemaphoreSlim(Math.Max(1, maxConcurrency));
        var tasks = distinct.Select(async s =>
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try { return (s, await GetBarsAsync(s, fromEt, toEt, timeframe, ct).ConfigureAwait(false)); }
            finally { gate.Release(); }
        }).ToList();
        foreach (var (s, bars) in await Task.WhenAll(tasks).ConfigureAwait(false)) result[s] = bars;
        return result;
    }

    public async Task<IReadOnlyList<UniverseMember>> GetPointInTimeUniverseAsync(
        DateOnly asOfEt, IEnumerable<string> candidates, UniverseFilter? filter = null, CancellationToken ct = default)
    {
        filter ??= new UniverseFilter();
        var lastPrior = asOfEt.AddDays(-1);
        // ~1.5 calendar days per session plus a holiday margin covers the lookback.
        var from = asOfEt.AddDays(-(filter.LookbackSessions * 3 / 2 + 10));
        var bars = await GetBarsForSymbolsAsync(candidates, from, lastPrior, BarTimeframe.Day1, ct: ct).ConfigureAwait(false);

        var members = new List<UniverseMember>();
        foreach (var (symbol, all) in bars)
        {
            var window = all.Where(c => DateOf(c, BarTimeframe.Day1) < asOfEt)
                .OrderBy(c => c.StartUtc).TakeLast(filter.LookbackSessions).ToList();
            if (window.Count < Math.Min(filter.MinSessions, filter.LookbackSessions)) continue;

            var last = window[^1];
            if (last.Close >= filter.MaxPrice || last.Close < filter.MinPrice) continue;
            var adv = window.Average(c => c.Close * c.Volume);
            if (adv < filter.MinAvgDollarVolume) continue;

            members.Add(new UniverseMember(symbol, DateOf(last, BarTimeframe.Day1), last.Close, decimal.Round(adv, 2), window.Count));
        }
        return members.OrderBy(m => m.Symbol, StringComparer.Ordinal).ToList();
    }

    private async Task<List<Candle>> FetchWithRetryAsync(string symbol, DateOnly first, DateOnly last, BarTimeframe timeframe, CancellationToken ct)
    {
        var (startUtc, endUtc) = RequestWindow(first, last, timeframe);
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                var list = new List<Candle>();
                await foreach (var c in feed.GetHistoricalCandlesAsync(symbol, startUtc, endUtc, Span(timeframe), ct).ConfigureAwait(false))
                    list.Add(c);
                return list;
            }
            catch (HttpRequestException ex) when (attempt < MaxAttempts && IsTransient(ex))
            {
                var wait = TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, attempt - 1)));
                await delay(wait, ct).ConfigureAwait(false);
            }
        }
    }

    internal static bool IsTransient(HttpRequestException ex) =>
        ex.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout
        || (ex.StatusCode is null && ex.Message.Contains("(429", StringComparison.Ordinal));

    private static (DateTime Start, DateTime End) RequestWindow(DateOnly first, DateOnly last, BarTimeframe timeframe)
    {
        if (timeframe == BarTimeframe.Day1)
            return (first.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), last.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        // Extended-hours session: 04:00–20:00 ET.
        return (EtToUtc(first, new TimeOnly(4, 0)), EtToUtc(last, new TimeOnly(20, 0)));
    }

    private static DateTime EtToUtc(DateOnly date, TimeOnly time) =>
        TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(time, DateTimeKind.Unspecified), MarketTime.Eastern);

    /// <summary>A daily bar's date is the provider's session date (its UTC date); a minute bar's is its ET date.</summary>
    internal static DateOnly DateOf(Candle c, BarTimeframe timeframe) => timeframe == BarTimeframe.Day1
        ? DateOnly.FromDateTime(c.StartUtc)
        : DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(c.StartUtc, DateTimeKind.Utc), MarketTime.Eastern));

    /// <summary>Groups ascending trading days into runs of consecutive sessions, at most MaxDaysPerRequest long.</summary>
    private static IEnumerable<List<DateOnly>> Runs(List<DateOnly> days)
    {
        var run = new List<DateOnly>();
        foreach (var d in days)
        {
            if (run.Count > 0 && (NextTradingDay(run[^1]) != d || run.Count >= MaxDaysPerRequest))
            {
                yield return run;
                run = new List<DateOnly>();
            }
            run.Add(d);
        }
        if (run.Count > 0) yield return run;
    }

    private static DateOnly NextTradingDay(DateOnly d)
    {
        do d = d.AddDays(1); while (!IsTradingDay(d));
        return d;
    }

    private static bool IsTradingDay(DateOnly d) =>
        d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && !MarketTime.IsMarketHoliday(d);

    private static string Key(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static string Code(BarTimeframe tf) => tf == BarTimeframe.Day1 ? "1Day" : "1Min";
    private static TimeSpan Span(BarTimeframe tf) => tf == BarTimeframe.Day1 ? TimeSpan.FromDays(1) : TimeSpan.FromMinutes(1);
}
