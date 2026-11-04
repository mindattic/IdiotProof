namespace IdiotProof.Core.Data;

/// <summary>One cached provider bar. Natural key (Symbol, Timeframe, StartUtc).</summary>
public sealed class HistoricalBar
{
    public long Id { get; set; }
    public string Symbol { get; set; } = "";
    public string Timeframe { get; set; } = "";   // "1Min" | "1Day"
    public string DateEt { get; set; } = "";      // "2026-07-21": ET session date (provider date for daily bars)
    public DateTime StartUtc { get; set; }
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    public decimal Volume { get; set; }
    public string Feed { get; set; } = "";
}

/// <summary>
/// Marks a completed (Symbol, Timeframe, DateEt) as fetched, so a day with no bars (halted,
/// not yet listed, delisted) is never re-requested.
/// </summary>
public sealed class HistoricalBarCoverage
{
    public long Id { get; set; }
    public string Symbol { get; set; } = "";
    public string Timeframe { get; set; } = "";
    public string DateEt { get; set; } = "";
    public int BarCount { get; set; }
    public DateTime FetchedUtc { get; set; }
}
