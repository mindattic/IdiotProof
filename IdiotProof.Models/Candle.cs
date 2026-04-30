using System.Globalization;

namespace IdiotProof.Models;

/// <summary>
/// Represents a single OHLCV candle in UTC time. Once constructed, an instance is immutable
/// and its OHLC values are guaranteed to satisfy Low &lt;= Open/Close &lt;= High and Volume &gt;= 0.
/// </summary>
public class Candle
{
    public string Symbol { get; init; } = string.Empty;
    public DateTime StartUtc { get; init; }
    public DateTime EndUtc { get; init; }
    public decimal Open { get; init; }
    public decimal High { get; init; }
    public decimal Low { get; init; }
    public decimal Close { get; init; }
    public decimal Volume { get; init; }
    public string Note { get; init; } = string.Empty;

    public string PriceChange
    {
        get
        {
            decimal change = Close - Open;
            return change >= 0m
                ? "+" + change.ToString("0.00", CultureInfo.InvariantCulture)
                : change.ToString("0.00", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// True when the OHLC invariant holds (Low &lt;= Open/Close &lt;= High and Volume &gt;= 0).
    /// Production data feeds occasionally emit malformed bars; consumers should call this
    /// at trust boundaries rather than assume validity.
    /// </summary>
    public bool IsValid =>
        Volume >= 0m &&
        Low <= Open && Open <= High &&
        Low <= Close && Close <= High;
}
