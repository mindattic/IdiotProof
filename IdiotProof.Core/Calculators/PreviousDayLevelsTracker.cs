// ============================================================================
// PreviousDayLevelsTracker - Tracks previous day high/low/close for S/R
// ============================================================================
//
// PDH (Previous Day High) = Resistance level
// PDL (Previous Day Low) = Support level  
// PDC (Previous Day Close) = Pivot point
//
// These are the most reliable intraday support/resistance levels.
// When price breaks PDH, it's a breakout signal. When it holds PDL, it's 
// a support bounce. Price relative to PDC shows bullish/bearish bias.
//
// Also tracks 2-day high/low for wider structure context and current
// session high/low for intraday range awareness.
//
// ============================================================================

using IdiotProof.Core.Models;
using IdiotProof.Helpers;
using IdiotProof.Services;
using IdiotProof.Models;

namespace IdiotProof.Calculators;

/// <summary>
/// Tracks previous day and multi-day high/low/close levels for S/R analysis.
/// Automatically rolls over at day boundaries.
/// </summary>
public sealed class PreviousDayLevelsTracker
{
    private readonly string symbol;
    
    // Previous day levels (the main S/R)
    private double prevDayHigh;
    private double prevDayLow;
    private double prevDayClose;
    
    // Two days ago (for multi-day S/R context)
    private double twoDaysAgoHigh;
    private double twoDaysAgoLow;
    
    // Current session tracking (to become "previous day" at rollover)
    private double sessionHigh = double.MinValue;
    private double sessionLow = double.MaxValue;
    private double sessionClose;
    private DateTime sessionDate = DateTime.MinValue;
    private bool hasSessionData;
    
    // Multi-day extremes
    private double twoDayHigh;
    private double twoDayLow;
    
    /// <summary>Previous day's highest price (Resistance).</summary>
    public double PrevDayHigh => prevDayHigh;
    
    /// <summary>Previous day's lowest price (Support).</summary>
    public double PrevDayLow => prevDayLow;
    
    /// <summary>Previous day's closing price (Pivot).</summary>
    public double PrevDayClose => prevDayClose;
    
    /// <summary>Highest price across the last 2 days.</summary>
    public double TwoDayHigh => twoDayHigh;
    
    /// <summary>Lowest price across the last 2 days.</summary>
    public double TwoDayLow => twoDayLow;
    
    /// <summary>Current session's highest price so far.</summary>
    public double SessionHigh => sessionHigh == double.MinValue ? 0 : sessionHigh;
    
    /// <summary>Current session's lowest price so far.</summary>
    public double SessionLow => sessionLow == double.MaxValue ? 0 : sessionLow;
    
    /// <summary>True if we have valid previous day data for S/R.</summary>
    public bool HasData => prevDayHigh > 0 && prevDayLow > 0;
    
    /// <summary>Previous day's price range (PDH - PDL).</summary>
    public double PrevDayRange => prevDayHigh - prevDayLow;
    
    public PreviousDayLevelsTracker(string symbol)
    {
        this.symbol = symbol;
    }
    
    /// <summary>
    /// Initializes from TickerDataCache (which loads from historical data).
    /// Call this once when the StrategyRunner starts.
    /// </summary>
    public void InitializeFromCache()
    {
        var cached = TickerDataCache.Get(symbol);
        if (cached != null)
        {
            prevDayHigh = cached.Hod;
            prevDayLow = cached.Lod;
            prevDayClose = cached.PrevClose;
            
            // Initially, 2-day range equals 1-day (will update as data flows)
            twoDayHigh = prevDayHigh;
            twoDayLow = prevDayLow;
        }
    }
    
    /// <summary>
    /// Seeds previous day levels from historical candle data.
    /// Extracts the last 2 complete trading days from the candles.
    /// </summary>
    public void SeedFromHistoricalCandles(IEnumerable<Candlestick> candles)
    {
        // Group candles by date to find daily H/L/C
        var dailyBars = candles
            .GroupBy(c => c.Timestamp.Date)
            .OrderByDescending(g => g.Key)
            .ToList();
        
        if (dailyBars.Count == 0)
            return;
        
        // Today's candles (most recent date) - don't use as previous day
        // Previous day = the day before the most recent
        DateTime today = DateTime.Now.Date;
        
        // Find the most recent complete day (not today)
        var completeDays = dailyBars.Where(g => g.Key < today).ToList();
        
        if (completeDays.Count >= 1)
        {
            var prevDay = completeDays[0];
            prevDayHigh = prevDay.Max(c => c.High);
            prevDayLow = prevDay.Min(c => c.Low);
            prevDayClose = prevDay.Last().Close;
            
            twoDayHigh = prevDayHigh;
            twoDayLow = prevDayLow;
        }
        
        if (completeDays.Count >= 2)
        {
            var twoDaysAgo = completeDays[1];
            twoDaysAgoHigh = twoDaysAgo.Max(c => c.High);
            twoDaysAgoLow = twoDaysAgo.Min(c => c.Low);
            
            // 2-day range = max of last 2 complete days
            twoDayHigh = Math.Max(prevDayHigh, twoDaysAgoHigh);
            twoDayLow = Math.Min(prevDayLow, twoDaysAgoLow);
        }
    }
    
    /// <summary>
    /// Updates with a new completed candle. Handles day boundary rollover.
    /// Call this from OnCandleComplete.
    /// </summary>
    public void Update(Candlestick candle)
    {
        var candleDate = candle.Timestamp.Date;
        
        // Day boundary - roll levels forward
        if (candleDate != sessionDate && sessionDate != DateTime.MinValue && hasSessionData)
        {
            // Previous "previous day" becomes "two days ago"
            twoDaysAgoHigh = prevDayHigh;
            twoDaysAgoLow = prevDayLow;
            
            // Current session becomes "previous day"
            prevDayHigh = sessionHigh;
            prevDayLow = sessionLow;
            prevDayClose = sessionClose;
            
            // Update 2-day extremes
            twoDayHigh = Math.Max(prevDayHigh, twoDaysAgoHigh > 0 ? twoDaysAgoHigh : prevDayHigh);
            twoDayLow = twoDaysAgoLow > 0
                ? Math.Min(prevDayLow, twoDaysAgoLow)
                : prevDayLow;
            
            // Reset session tracking for the new day
            sessionHigh = candle.High;
            sessionLow = candle.Low;
            sessionClose = candle.Close;
        }
        
        // Initialize session if first candle
        if (sessionDate == DateTime.MinValue || candleDate != sessionDate)
        {
            sessionDate = candleDate;
            sessionHigh = candle.High;
            sessionLow = candle.Low;
            sessionClose = candle.Close;
            hasSessionData = true;
        }
        else
        {
            // Update current session extremes
            sessionHigh = Math.Max(sessionHigh, candle.High);
            sessionLow = Math.Min(sessionLow, candle.Low);
            sessionClose = candle.Close; // Always update to latest close
        }
    }
    
    /// <summary>
    /// Updates with a HistoricalBar (for backtest use). Same logic as Update(Candlestick).
    /// </summary>
    public void UpdateFromBar(HistoricalBar bar)
    {
        var barDate = bar.Time.Date;
        
        // Day boundary - roll levels forward
        if (barDate != sessionDate && sessionDate != DateTime.MinValue && hasSessionData)
        {
            twoDaysAgoHigh = prevDayHigh;
            twoDaysAgoLow = prevDayLow;
            
            prevDayHigh = sessionHigh;
            prevDayLow = sessionLow;
            prevDayClose = sessionClose;
            
            twoDayHigh = Math.Max(prevDayHigh, twoDaysAgoHigh > 0 ? twoDaysAgoHigh : prevDayHigh);
            twoDayLow = twoDaysAgoLow > 0
                ? Math.Min(prevDayLow, twoDaysAgoLow)
                : prevDayLow;
            
            sessionHigh = bar.High;
            sessionLow = bar.Low;
            sessionClose = bar.Close;
        }
        
        if (sessionDate == DateTime.MinValue || barDate != sessionDate)
        {
            sessionDate = barDate;
            sessionHigh = bar.High;
            sessionLow = bar.Low;
            sessionClose = bar.Close;
            hasSessionData = true;
        }
        else
        {
            sessionHigh = Math.Max(sessionHigh, bar.High);
            sessionLow = Math.Min(sessionLow, bar.Low);
            sessionClose = bar.Close;
        }
    }
    
    /// <summary>
    /// Calculates classic pivot points from previous day levels.
    /// R2, R1, Pivot, S1, S2 - widely used by professional traders.
    /// </summary>
    public (double R2, double R1, double Pivot, double S1, double S2) GetPivotPoints()
    {
        if (!HasData)
            return (0, 0, 0, 0, 0);
        
        double pivot = (prevDayHigh + prevDayLow + prevDayClose) / 3.0;
        double r1 = 2.0 * pivot - prevDayLow;
        double s1 = 2.0 * pivot - prevDayHigh;
        double r2 = pivot + (prevDayHigh - prevDayLow);
        double s2 = pivot - (prevDayHigh - prevDayLow);
        
        return (R2: r2, R1: r1, Pivot: pivot, S1: s1, S2: s2);
    }
    
    /// <summary>
    /// Returns a formatted string of all levels for logging.
    /// </summary>
    public string GetLevelsSummary()
    {
        if (!HasData)
            return "PDH/PDL/PDC: Not available";
        
        var pivots = GetPivotPoints();
        return $"PDH: ${prevDayHigh:F2} | PDL: ${prevDayLow:F2} | PDC: ${prevDayClose:F2} | " +
               $"Range: ${PrevDayRange:F2} | Pivot: ${pivots.Pivot:F2} | " +
               $"R1: ${pivots.R1:F2} | S1: ${pivots.S1:F2} | " +
               $"2D-High: ${twoDayHigh:F2} | 2D-Low: ${twoDayLow:F2}";
    }
}
