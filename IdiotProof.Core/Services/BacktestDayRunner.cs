using IdiotProof.DataFeeds;
using IdiotProof.Models;
using IdiotProof.Scripting;
using IdiotProof.Strategies.Backtesting;

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
        StrategyDefinition def, DateOnly dayEt, IMarketDataFeed feed, CancellationToken ct = default)
    {
        var (start, end) = SessionWindowUtc(dayEt, def.Session);
        var candles = new List<Candle>();
        await foreach (var c in feed.GetHistoricalCandlesAsync(def.Symbol, start, end, TimeSpan.FromMinutes(1), ct))
            candles.Add(c);

        // Without the previous close every gap condition fails closed and a gap strategy never triggers.
        var previousClose = await feed.GetPreviousCloseAsync(def.Symbol, start.AddHours(2), ct);
        return StrategyBacktester.Run(def, candles, new BacktestOptions { PreviousClose = previousClose });
    }

    private static DateTime ToUtc(DateOnly day, TimeOnly et) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(day.ToDateTime(et), DateTimeKind.Unspecified), MarketTime.Eastern);
}
