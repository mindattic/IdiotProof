using System.Globalization;
using System.Net;
using IdiotProof.Core.Data;
using IdiotProof.DataFeeds;
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

    private readonly IDbContextFactory<AppDbContext> dbFactory;
    private readonly IMarketDataFeed feed;
    private readonly TimeProvider clock;
    private readonly Func<TimeSpan, CancellationToken, Task> delay;

    public HistoricalBarStore(IDbContextFactory<AppDbContext> dbFactory, IMarketDataFeed feed,
        TimeProvider? clock = null, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        this.dbFactory = dbFactory;
        this.feed = feed;
        this.clock = clock ?? TimeProvider.System;
        this.delay = delay ?? Task.Delay;
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

        string fromKey = Key(fromEt), toKey = Key(toEt);
        await using var db = await dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var covered = (await db.HistoricalBarCoverage.AsNoTracking()
                .Where(c => c.Symbol == symbol && c.Timeframe == tf
                         && string.Compare(c.DateEt, fromKey) >= 0 && string.Compare(c.DateEt, toKey) <= 0)
                .Select(c => c.DateEt).ToListAsync(ct).ConfigureAwait(false))
            .ToHashSet(StringComparer.Ordinal);

        var missing = tradingDays.Where(d => !covered.Contains(Key(d))).ToList();
        var live = new List<Candle>();

        foreach (var run in Runs(missing))
        {
            var fetched = await FetchWithRetryAsync(symbol, run[0], run[^1], timeframe, ct).ConfigureAwait(false);
            var byDay = fetched.GroupBy(c => DateOf(c, timeframe)).ToDictionary(g => g.Key, g => g.ToList());

            var completed = run.Where(d => d < todayEt).ToList();
            foreach (var d in run.Where(d => d >= todayEt))
                if (byDay.TryGetValue(d, out var partial)) live.AddRange(partial);
            if (completed.Count == 0) continue;

            string runFrom = Key(completed[0]), runTo = Key(completed[^1]);
            var startKeys = (await db.HistoricalBars.AsNoTracking()
                    .Where(b => b.Symbol == symbol && b.Timeframe == tf
                             && string.Compare(b.DateEt, runFrom) >= 0 && string.Compare(b.DateEt, runTo) <= 0)
                    .Select(b => b.StartUtc).ToListAsync(ct).ConfigureAwait(false))
                .ToHashSet();

            var now = clock.GetUtcNow().UtcDateTime;
            foreach (var d in completed)
            {
                var bars = byDay.TryGetValue(d, out var list) ? list : new List<Candle>();
                foreach (var c in bars.DistinctBy(c => c.StartUtc))
                {
                    if (!startKeys.Add(c.StartUtc)) continue;
                    db.HistoricalBars.Add(new HistoricalBar
                    {
                        Symbol = symbol, Timeframe = tf, DateEt = Key(d), StartUtc = c.StartUtc,
                        Open = c.Open, High = c.High, Low = c.Low, Close = c.Close, Volume = c.Volume,
                        Feed = feed.FeedName,
                    });
                }
                db.HistoricalBarCoverage.Add(new HistoricalBarCoverage
                {
                    Symbol = symbol, Timeframe = tf, DateEt = Key(d), BarCount = bars.Count, FetchedUtc = now,
                });
            }
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        var stored = await db.HistoricalBars.AsNoTracking()
            .Where(b => b.Symbol == symbol && b.Timeframe == tf
                     && string.Compare(b.DateEt, fromKey) >= 0 && string.Compare(b.DateEt, toKey) <= 0)
            .ToListAsync(ct).ConfigureAwait(false);

        var step = Span(timeframe);
        return stored
            .Select(b => new Candle
            {
                Symbol = b.Symbol, StartUtc = DateTime.SpecifyKind(b.StartUtc, DateTimeKind.Utc),
                EndUtc = DateTime.SpecifyKind(b.StartUtc, DateTimeKind.Utc) + step,
                Open = b.Open, High = b.High, Low = b.Low, Close = b.Close, Volume = b.Volume,
            })
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
