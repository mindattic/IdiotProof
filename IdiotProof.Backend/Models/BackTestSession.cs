// ============================================================================
// Backtest Session - Collection of candles for a trading day
// ============================================================================

namespace IdiotProof.BackTesting.Models;

/// <summary>
/// Represents a complete trading day of 1-minute candle data.
/// </summary>
public sealed class BackTestSession
{
    /// <summary>The ticker symbol.</summary>
    public required string Symbol { get; init; }

    /// <summary>The date of this session.</summary>
    public required DateOnly Date { get; init; }

    /// <summary>All 1-minute candles for the day.</summary>
    public required List<BackTestCandle> Candles { get; init; }

    // ========================================================================
    // Session Statistics
    // ========================================================================

    /// <summary>First candle of the day.</summary>
    public BackTestCandle? FirstCandle => Candles.FirstOrDefault();

    /// <summary>Last candle of the day.</summary>
    public BackTestCandle? LastCandle => Candles.LastOrDefault();

    /// <summary>Opening price of the day.</summary>
    public double Open => FirstCandle?.Open ?? 0;

    /// <summary>High of the day.</summary>
    public double High => Candles.Count > 0 ? Candles.Max(c => c.High) : 0;

    /// <summary>Low of the day.</summary>
    public double Low => Candles.Count > 0 ? Candles.Min(c => c.Low) : 0;

    /// <summary>Closing price of the day.</summary>
    public double Close => LastCandle?.Close ?? 0;

    /// <summary>Total volume for the day.</summary>
    public long TotalVolume => Candles.Sum(c => c.Volume);

    /// <summary>Price range for the day.</summary>
    public double Range => High - Low;

    /// <summary>Day change in dollars.</summary>
    public double Change => Close - Open;

    /// <summary>Day change as percentage.</summary>
    public double ChangePercent => Open > 0 ? Change / Open * 100 : 0;

    // ========================================================================
    // Session Times
    // ========================================================================

    /// <summary>Start time of the session.</summary>
    public DateTime StartTime => FirstCandle?.Timestamp ?? Date.ToDateTime(TimeOnly.MinValue);

    /// <summary>End time of the session.</summary>
    public DateTime EndTime => LastCandle?.Timestamp ?? Date.ToDateTime(TimeOnly.MaxValue);

    /// <summary>Session duration.</summary>
    public TimeSpan Duration => EndTime - StartTime;

    /// <summary>Number of candles in the session.</summary>
    public int CandleCount => Candles.Count;

    // ========================================================================
    // VWAP Calculation
    // ========================================================================

    /// <summary>
    /// Calculates running VWAP for all candles in the session.
    /// </summary>
    public void CalculateVwap()
    {
        double cumulativeTypicalPriceVolume = 0;
        long cumulativeVolume = 0;

        for (int i = 0; i < Candles.Count; i++)
        {
            var candle = Candles[i];
            double typicalPrice = (candle.High + candle.Low + candle.Close) / 3;
            cumulativeTypicalPriceVolume += typicalPrice * candle.Volume;
            cumulativeVolume += candle.Volume;

            double vwap = cumulativeVolume > 0 
                ? cumulativeTypicalPriceVolume / cumulativeVolume 
                : candle.Close;

            // Create new candle with VWAP (records are immutable)
            Candles[i] = candle with { Vwap = vwap };
        }
    }

    // ========================================================================
    // Query Methods
    // ========================================================================

    /// <summary>
    /// Gets candles within a time range.
    /// </summary>
    public IEnumerable<BackTestCandle> GetCandlesInRange(TimeOnly start, TimeOnly end)
    {
        return Candles.Where(c => 
        {
            var time = TimeOnly.FromDateTime(c.Timestamp);
            return time >= start && time <= end;
        });
    }

    /// <summary>
    /// Gets candles for premarket session (4:00 AM - 9:29 AM).
    /// </summary>
    public IEnumerable<BackTestCandle> GetPremarket() =>
        GetCandlesInRange(new TimeOnly(4, 0), new TimeOnly(9, 29));

    /// <summary>
    /// Gets candles for regular trading hours (9:30 AM - 4:00 PM).
    /// </summary>
    public IEnumerable<BackTestCandle> GetRth() =>
        GetCandlesInRange(new TimeOnly(9, 30), new TimeOnly(16, 0));

    /// <summary>
    /// Gets candles for after hours (4:00 PM - 8:00 PM).
    /// </summary>
    public IEnumerable<BackTestCandle> GetAfterHours() =>
        GetCandlesInRange(new TimeOnly(16, 0), new TimeOnly(20, 0));

    /// <summary>
    /// Gets the candle at or before a specific time.
    /// </summary>
    public BackTestCandle? GetCandleAt(TimeOnly time)
    {
        return Candles
            .Where(c => TimeOnly.FromDateTime(c.Timestamp) <= time)
            .LastOrDefault();
    }

    // ========================================================================
    // Display
    // ========================================================================

    public override string ToString()
    {
        return $"""
            +--------------------------------------------------+
            | {Symbol} - {Date:yyyy-MM-dd}                          
            +--------------------------------------------------+
            | Open:   ${Open,8:F2}                               
            | High:   ${High,8:F2}                               
            | Low:    ${Low,8:F2}                               
            | Close:  ${Close,8:F2}                               
            | Change: {(Change >= 0 ? "+" : "")}{ChangePercent,5:F2}%                              
            | Volume: {TotalVolume,10:N0}                         
            | Candles: {CandleCount,5}                             
            +--------------------------------------------------+
            """;
    }
}
