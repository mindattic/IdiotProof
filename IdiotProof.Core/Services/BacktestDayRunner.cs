using IdiotProof.Core.Monitoring;
using IdiotProof.DataFeeds;
using IdiotProof.Engine.Settings;
using IdiotProof.Models;
using IdiotProof.Scripting;
using IdiotProof.Strategies.Backtesting;
using Microsoft.Extensions.Logging.Abstractions;

namespace IdiotProof.Core.Services;

/// <summary>
/// The /backtest page's day run (IP-US-J1): fetch the chosen ET day's 1-minute candles for the
/// strategy's session from the feed, look up the previous close, and replay through
/// <see cref="StrategyBacktester.Run"/>.
/// </summary>
public static class BacktestDayRunner
{
    /// <summary>
    /// The UTC fetch window for a session on an ET calendar day, converted through the real Eastern
    /// zone so it is right on both sides of a DST change.
    /// </summary>
    public static (DateTime StartUtc, DateTime EndUtc) SessionWindowUtc(DateOnly dayEt, TradingSession session)
    {
        var (startEt, endEt) = session switch
        {
            TradingSession.Premarket  => (new TimeOnly(4, 0),  new TimeOnly(9, 30)),
            TradingSession.AfterHours => (new TimeOnly(16, 0), new TimeOnly(20, 0)),
            TradingSession.Extended   => (new TimeOnly(4, 0),  new TimeOnly(20, 0)),
            _                         => (new TimeOnly(9, 30), new TimeOnly(16, 0)),
        };
        return (ToUtc(dayEt, startEt), ToUtc(dayEt, endEt));
    }

    public static async Task<BacktestReport> RunAsync(
        StrategyDefinition def, DateOnly dayEt, IMarketDataFeed feed, CancellationToken ct = default) =>
        (await RunWithCandlesAsync(def, dayEt, feed, ct)).Report;

    /// <summary>The day run plus the candles it replayed, so the page can chart exactly what was tested.</summary>
    public static async Task<(BacktestReport Report, IReadOnlyList<Candle> Candles)> RunWithCandlesAsync(
        StrategyDefinition def, DateOnly dayEt, IMarketDataFeed feed, CancellationToken ct = default,
        LeaderContextCache? leaders = null, IEnumerable<string>? peers = null)
    {
        var (start, end) = SessionWindowUtc(dayEt, def.Session);
        var candles = new List<Candle>();
        await foreach (var c in feed.GetHistoricalCandlesAsync(def.Symbol, start, end, TimeSpan.FromMinutes(1), ct))
            candles.Add(c);

        // Without the previous close every gap condition fails closed and a gap strategy never triggers.
        var previousClose = await feed.GetPreviousCloseAsync(def.Symbol, start.AddHours(2), ct);
        var (leaderContext, leaderNote) = await LeadersForDayAsync(def, dayEt, feed, leaders, peers, ct);
        var report = StrategyBacktester.Run(def, candles, new BacktestOptions { PreviousClose = previousClose, Leaders = leaderContext });
        if (leaderNote is not null) report.Note = report.Note is null ? leaderNote : $"{report.Note} {leaderNote}";
        return (report, candles);
    }

    /// <summary>
    /// The ET trading days (weekends skipped) a <paramref name="days"/>-day replay covers, starting at
    /// <paramref name="firstDayEt"/>: the window a sequenced strategy needs to hand off between steps.
    /// </summary>
    public static IReadOnlyList<DateOnly> TradingDays(DateOnly firstDayEt, int days)
    {
        if (days < 1) throw new ArgumentOutOfRangeException(nameof(days), "At least one day.");
        var result = new List<DateOnly>();
        for (var day = firstDayEt; result.Count < days; day = day.AddDays(1))
            if (day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) result.Add(day);
        return result;
    }

    /// <summary>
    /// One continuous replay over <paramref name="days"/> trading days from <paramref name="firstDayEt"/>:
    /// each day's session candles are fetched and concatenated so a sequence's wait between steps,
    /// and a Within deadline, run across the day boundaries. The previous close is the first day's.
    /// </summary>
    public static async Task<(BacktestReport Report, IReadOnlyList<Candle> Candles)> RunDaysAsync(
        StrategyDefinition def, DateOnly firstDayEt, int days, IMarketDataFeed feed, CancellationToken ct = default,
        LeaderContextCache? leaders = null, IEnumerable<string>? peers = null, int? defaultQuantity = null)
    {
        var tradingDays = TradingDays(firstDayEt, days);
        var candles = new List<Candle>();
        foreach (var day in tradingDays)
        {
            var (start, end) = SessionWindowUtc(day, def.Session);
            await foreach (var c in feed.GetHistoricalCandlesAsync(def.Symbol, start, end, TimeSpan.FromMinutes(1), ct))
                candles.Add(c);
        }
        var (firstStart, _) = SessionWindowUtc(tradingDays[0], def.Session);
        var previousClose = await feed.GetPreviousCloseAsync(def.Symbol, firstStart.AddHours(2), ct);
        var (leaderContext, leaderNote) = await LeadersForDayAsync(def, tradingDays[0], feed, leaders, peers, ct);
        var options = new BacktestOptions { PreviousClose = previousClose, Leaders = leaderContext, DefaultQuantity = defaultQuantity ?? 100 };
        var report = StrategyBacktester.Run(def, candles, options);
        if (leaderNote is not null) report.Note = report.Note is null ? leaderNote : $"{report.Note} {leaderNote}";
        return (report, candles);
    }

    /// <summary>
    /// Leader context for a leader-gated strategy (null and no fetch otherwise). A failure leaves the
    /// guards fail-closed, like the live Monitor, and returns a note saying why.
    /// </summary>
    public static async Task<(BacktestLeaderContext? Context, string? Note)> LeadersForDayAsync(
        StrategyDefinition def, DateOnly dayEt, IMarketDataFeed feed, LeaderContextCache? leaders, IEnumerable<string>? peers, CancellationToken ct)
    {
        if (!LeaderContextCache.Uses(def)) return (null, null);
        leaders ??= new LeaderContextCache(feed, null, () => new AppSettings().LeaderEtfs, NullLogger.Instance);
        try
        {
            var context = await leaders.ForDayAsync(def.Symbol, dayEt, peers ?? [], ct);
            return (context, context.Links.Count == 0 ? "No leader was measured from the prior sessions, so the leader guards blocked every entry." : null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (null, $"Leader context unavailable ({ex.Message}), so the leader guards blocked every entry.");
        }
    }

    private static DateTime ToUtc(DateOnly day, TimeOnly et) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(day.ToDateTime(et), DateTimeKind.Unspecified), MarketTime.Eastern);
}
