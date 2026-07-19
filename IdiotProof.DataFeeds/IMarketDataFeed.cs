using IdiotProof.Models;

namespace IdiotProof.DataFeeds;

/// <summary>
/// Abstraction for any market data provider.
/// </summary>
public interface IMarketDataFeed
{
    string FeedName { get; }

    IAsyncEnumerable<Candle> GetHistoricalCandlesAsync(
        string symbol,
        DateTime startUtc,
        DateTime endUtc,
        TimeSpan candleSize,
        CancellationToken ct = default);

    Task<LatestPrice?> GetLatestPriceAsync(string symbol, CancellationToken ct = default);

    /// <summary>
    /// The previous trading day's official close — the reference price for gap
    /// math (gap % = premarket price vs this). Default implementation pulls
    /// daily bars for the last ten calendar days and returns the close of the
    /// last bar that ended before <paramref name="beforeUtc"/>'s ET date, so
    /// weekends/holidays are skipped naturally. Null when no daily data exists.
    /// </summary>
    async Task<decimal?> GetPreviousCloseAsync(string symbol, DateTime beforeUtc, CancellationToken ct = default)
    {
        var startUtc = beforeUtc.Date.AddDays(-10);
        Candle? previous = null;

        // "Today" on the US-market clock: a 4AM-ET premarket instant belongs
        // to today's trading session even though a naive UTC date would
        // agree only coincidentally; ET conversion matters here because
        // beforeUtc is a specific INSTANT (evening UTC can already be the
        // next ET calendar day).
        var easternToday = ToEasternDate(beforeUtc);

        // Daily bars are a different case: a "1Day" bar already denotes one
        // full trading session as a whole calendar date — that identity is
        // the provider's, not ours to reinterpret. Comparing bar.StartUtc's
        // ET-converted date (rather than its raw UTC date) would shift a
        // midnight-UTC-stamped bar back to the PRIOR ET calendar date (UTC
        // midnight = ~7-8PM ET the day before), silently misdating every
        // daily bar by one day and risking "previous close" picking up
        // today's own (still-forming) bar instead of yesterday's.
        await foreach (var bar in GetHistoricalCandlesAsync(symbol, startUtc, beforeUtc, TimeSpan.FromDays(1), ct).ConfigureAwait(false))
        {
            if (DateOnly.FromDateTime(bar.StartUtc) < easternToday)
                previous = bar;
        }
        return previous?.Close;
    }

    private static DateOnly ToEasternDate(DateTime utc)
    {
        try
        {
            var eastern = TimeZoneInfo.FindSystemTimeZoneById(OperatingSystem.IsWindows() ? "Eastern Standard Time" : "America/New_York");
            return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), eastern));
        }
        catch (TimeZoneNotFoundException)
        {
            return DateOnly.FromDateTime(utc);
        }
    }
}
