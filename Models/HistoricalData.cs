// ============================================================================
// Historical Data Models - Types for backtesting
// ============================================================================
//
// ╔═══════════════════════════════════════════════════════════════════════════╗
// ║  IBKR HISTORICAL DATA API REFERENCE                                       ║
// ║                                                                           ║
// ║  IB Gateway/TWS provides historical data via:                             ║
// ║    • reqHistoricalData() - OHLCV bars                                    ║
// ║    • reqHistoricalTicks() - Tick-by-tick data                            ║
// ║                                                                           ║
// ║  CONNECTION (IB Gateway):                                                 ║
// ║    • Paper: Port 4002                                                     ║
// ║    • Live:  Port 4001                                                     ║
// ║                                                                           ║
// ║  BAR SIZE LIMITS:                                                         ║
// ║    • 1 sec:  Max 30 min (1800 S)                                         ║
// ║    • 1 min:  Max 1 day (1 D)                                             ║
// ║    • 1 hour: Max 1 month (1 M)                                           ║
// ║    • 1 day:  Max 1 year (1 Y)                                            ║
// ║                                                                           ║
// ║  PACING RULES:                                                            ║
// ║    • Max 60 requests per 10 minutes                                      ║
// ║    • 15+ second wait between identical requests                          ║
// ║                                                                           ║
// ║  Reference: https://interactivebrokers.github.io/tws-api/               ║
// ╚═══════════════════════════════════════════════════════════════════════════╝

using System;
using IdiotProof.Enums;

namespace IdiotProof.Models
{
    /// <summary>
    /// Represents a single historical price bar (OHLCV).
    /// </summary>
    public sealed record HistoricalBar
    {
        /// <summary>Bar timestamp.</summary>
        public required DateTime Time { get; init; }

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

        /// <summary>Volume-weighted average price (if available).</summary>
        public double? Vwap { get; init; }

        /// <summary>Number of trades during the bar.</summary>
        public int? TradeCount { get; init; }
    }

    /// <summary>
    /// Represents a single historical tick (trade).
    /// </summary>
    public sealed record HistoricalTick
    {
        /// <summary>Tick timestamp.</summary>
        public required DateTime Time { get; init; }

        /// <summary>Trade price.</summary>
        public required double Price { get; init; }

        /// <summary>Size of the trade.</summary>
        public required int Size { get; init; }

        /// <summary>Exchange where the tick occurred.</summary>
        public string? Exchange { get; init; }
    }

    /// <summary>
    /// Extension methods for historical data enums to get IB API strings.
    /// </summary>
    public static class HistoricalDataExtensions
    {
        /// <summary>Gets the IB API bar size string.</summary>
        public static string ToIbString(this BarSize barSize) => barSize switch
        {
            BarSize.Seconds1 => "1 secs",
            BarSize.Seconds5 => "5 secs",
            BarSize.Seconds15 => "15 secs",
            BarSize.Seconds30 => "30 secs",
            BarSize.Minutes1 => "1 min",
            BarSize.Minutes2 => "2 mins",
            BarSize.Minutes5 => "5 mins",
            BarSize.Minutes15 => "15 mins",
            BarSize.Minutes30 => "30 mins",
            BarSize.Hours1 => "1 hour",
            BarSize.Days1 => "1 day",
            _ => "1 min"
        };

        /// <summary>Gets the IB API whatToShow string.</summary>
        public static string ToIbString(this HistoricalDataType dataType) => dataType switch
        {
            HistoricalDataType.Trades => "TRADES",
            HistoricalDataType.Midpoint => "MIDPOINT",
            HistoricalDataType.Bid => "BID",
            HistoricalDataType.Ask => "ASK",
            _ => "TRADES"
        };

        /// <summary>Gets the maximum duration string for a bar size.</summary>
        public static string GetMaxDuration(this BarSize barSize) => barSize switch
        {
            BarSize.Seconds1 => "1800 S",
            BarSize.Seconds5 => "7200 S",
            BarSize.Seconds15 => "14400 S",
            BarSize.Seconds30 => "28800 S",
            BarSize.Minutes1 => "1 D",
            BarSize.Minutes2 => "2 D",
            BarSize.Minutes5 => "1 W",
            BarSize.Minutes15 => "2 W",
            BarSize.Minutes30 => "1 M",
            BarSize.Hours1 => "1 M",
            BarSize.Days1 => "1 Y",
            _ => "1 D"
        };
    }
}
