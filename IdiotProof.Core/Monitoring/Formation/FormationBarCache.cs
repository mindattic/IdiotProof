using IdiotProof.Core.Evidence;
using IdiotProof.DataFeeds;
using IdiotProof.Evidence.Gappers;
using IdiotProof.Models;
using Microsoft.Extensions.Logging;

namespace IdiotProof.Core.Monitoring.Formation;

/// <summary>The bars one formation snapshot needs; the scanner never talks to a feed directly.</summary>
public interface IFormationBarSource
{
    /// <summary>Today's one-minute bars from 04:00 ET up to <paramref name="nowUtc"/>, ascending.</summary>
    Task<IReadOnlyList<Candle>> TodayMinuteBarsAsync(string symbol, DateOnly dateEt, DateTime nowUtc, CancellationToken ct);

    /// <summary>Daily bars dated before <paramref name="dateEt"/> (enough for ADV20 and the D-1..D-5 run-up), ascending.</summary>
    Task<IReadOnlyList<Candle>> DailyBarsAsync(string symbol, DateOnly dateEt, CancellationToken ct);

    /// <summary>One-minute bars of the five sessions before <paramref name="dateEt"/>, one list per session.</summary>
    Task<IReadOnlyList<IReadOnlyList<Candle>>> PriorDayMinuteBarsAsync(string symbol, DateOnly dateEt, CancellationToken ct);
}

/// <summary>
/// Today's premarket minutes per symbol: primed from the feed's REST minute bars (04:00 ET to now),
/// extended by the bars the shared SIP socket pushes, and re-pulled from REST every
/// <see cref="HealInterval"/> so a dropped frame cannot leave a hole. Prior sessions come from
/// <see cref="IHistoricalBarStore"/>, which caches completed days in SQL. Everything is forgotten
/// on the first call of a new ET day.
/// </summary>
public sealed class FormationBarCache(IMarketDataFeed feed, IHistoricalBarStore store, ILogger<FormationBarCache> log) : IFormationBarSource
{
    public static readonly TimeSpan HealInterval = TimeSpan.FromMinutes(5);
    public const int DailyCalendarDays = 45;
    public const int PriorSessions = 5;

    private readonly Lock gate = new();
    private DateOnly day;
    private readonly Dictionary<string, Entry> today = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyList<Candle>> daily = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyList<IReadOnlyList<Candle>>> priorMinutes = new(StringComparer.OrdinalIgnoreCase);
    private long streamedBars;

    private sealed class Entry
    {
        public SortedDictionary<DateTime, Candle> Bars { get; } = new();
        public DateTime? LastFetchUtc { get; set; }
    }

    /// <summary>Bars the shared socket pushed today for symbols this cache already tracks.</summary>
    public long StreamedBars => Interlocked.Read(ref streamedBars);

    /// <summary>A streamed minute bar; ignored for symbols not yet primed today.</summary>
    public void Push(Candle bar)
    {
        if (string.IsNullOrWhiteSpace(bar.Symbol)) return;
        lock (gate)
        {
            if (!today.TryGetValue(bar.Symbol, out var entry)) return;
            entry.Bars[Floor(bar.StartUtc)] = bar;
        }
        Interlocked.Increment(ref streamedBars);
    }

    public async Task<IReadOnlyList<Candle>> TodayMinuteBarsAsync(string symbol, DateOnly dateEt, DateTime nowUtc, CancellationToken ct)
    {
        symbol = symbol.Trim().ToUpperInvariant();
        var sessionStart = MinuteBarMath.ToUtc(dateEt, MinuteBarMath.SessionStartEt);
        DateTime? fetchFrom = null;
        lock (gate)
        {
            EnsureDay(dateEt);
            if (!today.TryGetValue(symbol, out var entry)) today[symbol] = entry = new Entry();
            if (entry.LastFetchUtc is null) fetchFrom = sessionStart;
            else if (nowUtc - entry.LastFetchUtc.Value >= HealInterval)
                fetchFrom = entry.Bars.Count == 0 ? sessionStart : Max(sessionStart, entry.Bars.Keys.Last().AddMinutes(-2));
        }

        if (fetchFrom is { } from && from < nowUtc)
        {
            var fetched = new List<Candle>();
            try
            {
                await foreach (var c in feed.GetHistoricalCandlesAsync(symbol, from, nowUtc, TimeSpan.FromMinutes(1), ct).ConfigureAwait(false))
                    fetched.Add(c);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Formation bars: minute fetch for {Symbol} failed; using what is cached.", symbol);
            }
            lock (gate)
            {
                if (today.TryGetValue(symbol, out var entry))
                {
                    foreach (var c in fetched)
                        if (c.StartUtc >= sessionStart) entry.Bars[Floor(c.StartUtc)] = c;
                    entry.LastFetchUtc = nowUtc;
                }
            }
        }

        lock (gate)
            return today.TryGetValue(symbol, out var entry) ? entry.Bars.Values.Where(b => b.EndUtc <= nowUtc).ToList() : [];
    }

    public async Task<IReadOnlyList<Candle>> DailyBarsAsync(string symbol, DateOnly dateEt, CancellationToken ct)
    {
        symbol = symbol.Trim().ToUpperInvariant();
        lock (gate)
        {
            EnsureDay(dateEt);
            if (daily.TryGetValue(symbol, out var cached)) return cached;
        }
        IReadOnlyList<Candle> bars = [];
        try { bars = await store.GetBarsAsync(symbol, dateEt.AddDays(-DailyCalendarDays), dateEt.AddDays(-1), BarTimeframe.Day1, ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { log.LogWarning(ex, "Formation bars: daily fetch for {Symbol} failed.", symbol); return bars; }
        lock (gate) daily[symbol] = bars;
        return bars;
    }

    public async Task<IReadOnlyList<IReadOnlyList<Candle>>> PriorDayMinuteBarsAsync(string symbol, DateOnly dateEt, CancellationToken ct)
    {
        symbol = symbol.Trim().ToUpperInvariant();
        lock (gate)
        {
            EnsureDay(dateEt);
            if (priorMinutes.TryGetValue(symbol, out var cached)) return cached;
        }
        IReadOnlyList<IReadOnlyList<Candle>> sessions = [];
        try
        {
            var bars = await store.GetBarsAsync(symbol, dateEt.AddDays(-(PriorSessions * 2 + 2)), dateEt.AddDays(-1), BarTimeframe.Minute1, ct).ConfigureAwait(false);
            sessions = bars.GroupBy(b => MinuteBarMath.SessionDate(b.EndUtc)).OrderBy(g => g.Key).TakeLast(PriorSessions)
                .Select(g => (IReadOnlyList<Candle>)g.OrderBy(b => b.StartUtc).ToList()).ToList();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { log.LogWarning(ex, "Formation bars: prior-session minute fetch for {Symbol} failed.", symbol); return sessions; }
        lock (gate) priorMinutes[symbol] = sessions;
        return sessions;
    }

    private void EnsureDay(DateOnly dateEt)
    {
        if (day == dateEt) return;
        day = dateEt;
        today.Clear();
        daily.Clear();
        priorMinutes.Clear();
    }

    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;

    private static DateTime Floor(DateTime utc) => new(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, 0, DateTimeKind.Utc);
}
