using System.Globalization;
using IdiotProof.DataFeeds;
using IdiotProof.Indicators;
using IdiotProof.Models;

namespace IdiotProof.Operations.Indicators;

/// <summary>One indicator the <c>indicators compute</c> command knows: how to run it and how many bars it needs.</summary>
/// <param name="Name">Canonical lower-case name (<c>rsi</c>, <c>bollinger</c>).</param>
/// <param name="Aliases">Other spellings accepted on the command line.</param>
/// <param name="DefaultPeriod">The period used when <c>--period</c> is omitted; null when it takes none.</param>
/// <param name="Description">One line for help and the catalog.</param>
/// <param name="Compute">Named output series, each one value per bar (oldest first).</param>
public sealed record IndicatorSpec(
    string Name, string[] Aliases, int? DefaultPeriod, string Description,
    Func<IReadOnlyList<Candle>, int, IReadOnlyList<(string Column, decimal[] Values)>> Compute)
{
    /// <summary>Bars needed before the output means anything: the period's warm-up plus one.</summary>
    public int MinBars(int period) => Name switch
    {
        "macd" => 26 + 9,
        "adx" => period * 2 + 1,
        "vwap" or "obv" => 1,
        _ => period + 1,
    };
}

/// <summary>
/// The indicator set the CLI exposes: the same <c>IdiotProof.Indicators</c> math the Monitor's
/// <c>IndicatorSnapshotBuilder</c> uses, so a value printed here is the value a strategy condition
/// sees on the same bars.
/// </summary>
public static class IndicatorCatalog
{
    public static readonly IReadOnlyList<IndicatorSpec> All =
    [
        new("rsi", [], 14, "Relative Strength Index (Wilder), 0-100.", (c, p) => [("RSI", RSI.Calculate(c, p))]),
        new("ema", [], 9, "Exponential moving average of the close.", (c, p) => [($"EMA{p}", EMA.Calculate(c, p))]),
        new("sma", [], 20, "Simple moving average of the close.", (c, p) => [($"SMA{p}", SMA.Calculate(c, p))]),
        new("atr", [], 14, "Average True Range, in dollars.", (c, p) => [("ATR", ATR.Calculate(c, p))]),
        new("macd", [], null, "MACD 12/26/9: line, signal and histogram.", (c, _) =>
        {
            var r = MACD.Calculate(c);
            return [("MACD", r.Select(x => x.Macd).ToArray()), ("Signal", r.Select(x => x.Signal).ToArray()), ("Hist", r.Select(x => x.Histogram).ToArray())];
        }),
        new("vwap", [], null, "Volume-weighted average price, reset each ET session at 4:00 AM.", (c, _) => [("VWAP", VWAP.Calculate(c))]),
        new("adx", ["dmi"], 14, "Average Directional Index with +DI and -DI.", (c, p) =>
        {
            var r = ADX.Calculate(c, p);
            return [("ADX", r.Select(x => x.ADX).ToArray()), ("+DI", r.Select(x => x.PlusDI).ToArray()), ("-DI", r.Select(x => x.MinusDI).ToArray())];
        }),
        new("bollinger", ["bb", "bbands"], 20, "Bollinger Bands (2 sigma): upper, middle, lower, %B.", (c, p) =>
        {
            var r = BollingerBands.Calculate(c, p);
            return [("Upper", r.Select(x => x.Upper).ToArray()), ("Middle", r.Select(x => x.Middle).ToArray()),
                ("Lower", r.Select(x => x.Lower).ToArray()), ("%B", r.Select(x => x.PercentB).ToArray())];
        }),
        new("stochastic", ["stoch"], 14, "Stochastic oscillator %K / %D (3).", (c, p) =>
        {
            var r = Stochastic.Calculate(c, p);
            return [("%K", r.Select(x => x.PercentK).ToArray()), ("%D", r.Select(x => x.PercentD).ToArray())];
        }),
        new("cci", [], 20, "Commodity Channel Index.", (c, p) => [("CCI", CCI.Calculate(c, p))]),
        new("williams-r", ["williamsr", "willr"], 14, "Williams %R, -100..0.", (c, p) => [("%R", WilliamsR.Calculate(c, p))]),
        new("momentum", ["mom"], 10, "Close minus the close N bars ago.", (c, p) => [("Momentum", Momentum.Calculate(c, p))]),
        new("roc", [], 10, "Rate of change over N bars, in percent.", (c, p) => [("ROC", ROC.Calculate(c, p))]),
        new("obv", [], null, "On-balance volume.", (c, _) => [("OBV", OBV.Calculate(c))]),
    ];

    public static IndicatorSpec? Find(string name)
    {
        var n = (name ?? "").Trim().ToLowerInvariant();
        return All.FirstOrDefault(s => s.Name == n || s.Aliases.Contains(n));
    }

    public static string Names => string.Join(", ", All.Select(s => s.Name));
}

/// <summary>Bars for the indicator commands, from the Hub's market data feed.</summary>
internal static class IndicatorBars
{
    /// <summary><c>1Min</c>, <c>5Min</c>, <c>15Min</c>, <c>1Hour</c>, <c>1Day</c> (case-insensitive) as a bar size.</summary>
    public static TimeSpan? ParseTimeframe(string? text) => (text ?? "").Trim().ToLowerInvariant() switch
    {
        "1min" or "1m" => TimeSpan.FromMinutes(1),
        "5min" or "5m" => TimeSpan.FromMinutes(5),
        "15min" or "15m" => TimeSpan.FromMinutes(15),
        "1hour" or "1h" => TimeSpan.FromHours(1),
        "1day" or "1d" => TimeSpan.FromDays(1),
        _ => null,
    };

    public static string Label(TimeSpan size) =>
        size >= TimeSpan.FromDays(1) ? "1Day" : size >= TimeSpan.FromHours(1) ? "1Hour" : $"{size.TotalMinutes:0}Min";

    /// <summary>
    /// The newest <paramref name="count"/> bars of <paramref name="symbol"/> up to now, fetched far
    /// enough back to cover weekends and holidays. Refuses the Mock feed: an indicator on made-up
    /// prices is worse than none.
    /// </summary>
    public static async Task<List<Candle>> LoadAsync(OperationContext ctx, string symbol, TimeSpan size, int count)
    {
        var feed = ctx.Get<IMarketDataFeed>();
        if (feed.IsSynthetic())
            throw OperationException.Blocked("The Hub is on the Mock data feed, so there are no real prices to compute on (`idiotproof market feed status`).");

        var now = ctx.UtcNow;
        // Regular + extended hours give ~960 one-minute bars per weekday; pad for weekends/holidays.
        var perDay = size >= TimeSpan.FromDays(1) ? 1.0 : Math.Max(1.0, TimeSpan.FromHours(16) / size);
        var days = Math.Clamp((int)Math.Ceiling(count / perDay * 7 / 5) + 4, 5, 3700);
        var candles = new List<Candle>();
        await foreach (var c in feed.GetHistoricalCandlesAsync(symbol, now.AddDays(-days), now, size, ctx.CancellationToken))
            candles.Add(c);
        if (candles.Count == 0)
            throw OperationException.Failed($"No {Label(size)} bars for {symbol} from {feed.FeedName} in the last {days} days.");
        candles.Sort((a, b) => a.StartUtc.CompareTo(b.StartUtc));
        return candles.Count > count ? candles[^count..] : candles;
    }

    public static string Num(decimal? v, int decimals = 2) =>
        v is { } d ? d.ToString("#,0." + new string('0', decimals), CultureInfo.InvariantCulture) : "-";

    public static string Num(double? v, int decimals = 2) => Num(v is { } d ? (decimal)d : null, decimals);

    /// <summary>A bar's start in ET: <c>10/30 9:31 AM</c>, or the date alone for daily bars.</summary>
    public static string BarTime(DateTime startUtc, TimeSpan size) =>
        size >= TimeSpan.FromDays(1)
            ? DateOnly.FromDateTime(startUtc).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : EasternClock.ToEastern(startUtc).ToString("M/d h:mm tt", CultureInfo.InvariantCulture);
}
