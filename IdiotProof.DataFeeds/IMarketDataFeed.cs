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

        // "Today" on the US-market clock: a 4AM-ET premarket bar belongs to
        // today's session even though a naive UTC date would agree; the ET
        // conversion matters for late-evening UTC times (8PM ET = next UTC day).
        var easternToday = ToEasternDate(beforeUtc);

        await foreach (var bar in GetHistoricalCandlesAsync(symbol, startUtc, beforeUtc, TimeSpan.FromDays(1), ct).ConfigureAwait(false))
        {
            if (ToEasternDate(bar.StartUtc) < easternToday)
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
