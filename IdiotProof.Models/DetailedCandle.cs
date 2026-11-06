namespace IdiotProof.Models;

/// <summary>
/// A bar that also carries the provider's trade count and volume-weighted average price, as the
/// Alpaca bars endpoint returns them (<c>n</c>, <c>vw</c>). Everything that consumes a
/// <see cref="Candle"/> keeps working; the gapper study's file store keeps the extra two fields.
/// </summary>
public sealed class DetailedCandle : Candle
{
    public long TradeCount { get; init; }
    public decimal Vwap { get; init; }
}
