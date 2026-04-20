using System.Globalization;

namespace IdiotProof.Models;

/// <summary>
/// Represents a single OHLCV candle in UTC time.
/// </summary>
public class Candle
{
    public string Symbol { get; set; } = string.Empty;
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    public decimal Volume { get; set; }
    public string Note { get; set; } = string.Empty;

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
}
