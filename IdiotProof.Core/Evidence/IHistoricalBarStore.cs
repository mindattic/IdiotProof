using IdiotProof.Models;

namespace IdiotProof.Core.Evidence;

public enum BarTimeframe { Minute1, Day1 }

/// <summary>Filter for the point-in-time low-price universe.</summary>
public sealed record UniverseFilter
{
    /// <summary>Exclusive ceiling on the last close before the as-of date.</summary>
    public decimal MaxPrice { get; init; } = 5m;
    /// <summary>Inclusive floor on the last close (sub-dime names are untradeable noise).</summary>
    public decimal MinPrice { get; init; } = 0.10m;
    /// <summary>Daily sessions averaged for dollar volume.</summary>
    public int LookbackSessions { get; init; } = 20;
    /// <summary>Minimum sessions of history required inside the lookback.</summary>
    public int MinSessions { get; init; } = 5;
    public decimal MinAvgDollarVolume { get; init; } = 500_000m;
}

public sealed record UniverseMember(string Symbol, DateOnly LastSessionEt, decimal LastClose, decimal AvgDollarVolume, int Sessions);

/// <summary>
/// Historical bars fetched once per symbol-day and cached in SQL, plus a point-in-time
/// universe that only reads data dated before the as-of day.
/// </summary>
public interface IHistoricalBarStore
{
    /// <summary>Bars for the ET dates <paramref name="fromEt"/>..<paramref name="toEt"/> inclusive, oldest first.</summary>
    Task<IReadOnlyList<Candle>> GetBarsAsync(string symbol, DateOnly fromEt, DateOnly toEt, BarTimeframe timeframe, CancellationToken ct = default);

    /// <summary>Same as <see cref="GetBarsAsync"/> for many symbols, at most <paramref name="maxConcurrency"/> fetches at once.</summary>
    Task<IReadOnlyDictionary<string, IReadOnlyList<Candle>>> GetBarsForSymbolsAsync(
        IEnumerable<string> symbols, DateOnly fromEt, DateOnly toEt, BarTimeframe timeframe, int maxConcurrency = 4, CancellationToken ct = default);

    /// <summary>
    /// The candidates that, as of the open of <paramref name="asOfEt"/>, closed their last prior session
    /// under the price ceiling with enough dollar volume. Uses only daily bars dated before
    /// <paramref name="asOfEt"/>. Pass candidates that include since-delisted symbols to avoid
    /// survivorship bias; the store cannot invent symbols that are gone today.
    /// </summary>
    Task<IReadOnlyList<UniverseMember>> GetPointInTimeUniverseAsync(
        DateOnly asOfEt, IEnumerable<string> candidates, UniverseFilter? filter = null, CancellationToken ct = default);
}
