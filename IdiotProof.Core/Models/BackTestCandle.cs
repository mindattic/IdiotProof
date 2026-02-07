// ============================================================================
// BackTest Candle - OHLCV data for backtesting simulation
// ============================================================================

namespace IdiotProof.BackTesting.Models;

/// <summary>
/// Represents a single 1-minute OHLCV candlestick for backtesting.
/// </summary>
public sealed record BackTestCandle
{
    /// <summary>Bar timestamp (start of the 1-minute period).</summary>
    public required DateTime Timestamp { get; init; }

    /// <summary>Opening price.</summary>
    public required double Open { get; init; }

    /// <summary>Highest price during the bar.</summary>
    public required double High { get; init; }

    /// <summary>Lowest price during the bar.</summary>
    public required double Low { get; init; }

    /// <summary>Closing price.</summary>
    public required double Close { get; init; }

    /// <summary>Volume during the bar.</summary>
    public required long Volume { get; init; }

    /// <summary>Volume-weighted average price (running VWAP from open).</summary>
    public double Vwap { get; init; }

    // ========================================================================
    // Calculated Properties
    // ========================================================================

    /// <summary>Bar range (High - Low).</summary>
    public double Range => High - Low;

    /// <summary>Body size (absolute difference between Open and Close).</summary>
    public double BodySize => Math.Abs(Close - Open);

    /// <summary>Upper wick size.</summary>
    public double UpperWick => High - Math.Max(Open, Close);

    /// <summary>Lower wick size.</summary>
    public double LowerWick => Math.Min(Open, Close) - Low;

    /// <summary>Whether the candle is bullish (close >= open).</summary>
    public bool IsBullish => Close >= Open;

    /// <summary>Whether the candle is bearish (close < open).</summary>
    public bool IsBearish => Close < Open;

    /// <summary>
    /// Creates a BackTestCandle from CSV row data.
    /// Expected format: DateTime,Open,High,Low,Close,Volume
    /// </summary>
    public static BackTestCandle FromCsv(string[] fields)
    {
        return new BackTestCandle
        {
            Timestamp = DateTime.Parse(fields[0]),
            Open = double.Parse(fields[1]),
            High = double.Parse(fields[2]),
            Low = double.Parse(fields[3]),
            Close = double.Parse(fields[4]),
            Volume = long.Parse(fields[5]),
            Vwap = fields.Length > 6 ? double.Parse(fields[6]) : 0
        };
    }

    public override string ToString() =>
        $"{Timestamp:HH:mm} O:{Open:F2} H:{High:F2} L:{Low:F2} C:{Close:F2} V:{Volume}";
}
