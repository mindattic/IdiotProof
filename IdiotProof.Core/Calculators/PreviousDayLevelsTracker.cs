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
    private readonly string _symbol;
    
    // Previous day levels (the main S/R)
    private double _prevDayHigh;
    private double _prevDayLow;
    private double _prevDayClose;
    
    // Two days ago (for multi-day S/R context)
    private double _twoDaysAgoHigh;
    private double _twoDaysAgoLow;
    
    // Current session tracking (to become "previous day" at rollover)
    private double _sessionHigh = double.MinValue;
    private double _sessionLow = double.MaxValue;
    private double _sessionClose;
    private DateTime _sessionDate = DateTime.MinValue;
    private bool _hasSessionData;
    
    // Multi-day extremes
    private double _twoDayHigh;
    private double _twoDayLow;
    
    /// <summary>Previous day's highest price (Resistance).</summary>
    public double PrevDayHigh => _prevDayHigh;
    
    /// <summary>Previous day's lowest price (Support).</summary>
    public double PrevDayLow => _prevDayLow;
    
    /// <summary>Previous day's closing price (Pivot).</summary>
    public double PrevDayClose => _prevDayClose;
    
    /// <summary>Highest price across the last 2 days.</summary>
    public double TwoDayHigh => _twoDayHigh;
    
    /// <summary>Lowest price across the last 2 days.</summary>
    public double TwoDayLow => _twoDayLow;
    
    /// <summary>Current session's highest price so far.</summary>
    public double SessionHigh => _sessionHigh == double.MinValue ? 0 : _sessionHigh;
    
    /// <summary>Current session's lowest price so far.</summary>
    public double SessionLow => _sessionLow == double.MaxValue ? 0 : _sessionLow;
    
    /// <summary>True if we have valid previous day data for S/R.</summary>
    public bool HasData => _prevDayHigh > 0 && _prevDayLow > 0;
    
    /// <summary>Previous day's price range (PDH - PDL).</summary>
    public double PrevDayRange => _prevDayHigh - _prevDayLow;
    
    public PreviousDayLevelsTracker(string symbol)
    {
        _symbol = symbol;
    }
    
    /// <summary>
    /// Initializes from TickerDataCache (which loads from historical data).
    /// Call this once when the StrategyRunner starts.
    /// </summary>
    public void InitializeFromCache()
    {
        var cached = TickerDataCache.Get(_symbol);
        if (cached != null)
        {
            _prevDayHigh = cached.Hod;
            _prevDayLow = cached.Lod;
            _prevDayClose = cached.PrevClose;
            
            // Initially, 2-day range equals 1-day (will update as data flows)
            _twoDayHigh = _prevDayHigh;
            _twoDayLow = _prevDayLow;
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
            _prevDayHigh = prevDay.Max(c => c.High);
            _prevDayLow = prevDay.Min(c => c.Low);
            _prevDayClose = prevDay.Last().Close;
            
            _twoDayHigh = _prevDayHigh;
            _twoDayLow = _prevDayLow;
        }
        
        if (completeDays.Count >= 2)
        {
            var twoDaysAgo = completeDays[1];
            _twoDaysAgoHigh = twoDaysAgo.Max(c => c.High);
            _twoDaysAgoLow = twoDaysAgo.Min(c => c.Low);
            
            // 2-day range = max of last 2 complete days
            _twoDayHigh = Math.Max(_prevDayHigh, _twoDaysAgoHigh);
            _twoDayLow = Math.Min(_prevDayLow, _twoDaysAgoLow);
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
        if (candleDate != _sessionDate && _sessionDate != DateTime.MinValue && _hasSessionData)
        {
            // Previous "previous day" becomes "two days ago"
            _twoDaysAgoHigh = _prevDayHigh;
            _twoDaysAgoLow = _prevDayLow;
            
            // Current session becomes "previous day"
            _prevDayHigh = _sessionHigh;
            _prevDayLow = _sessionLow;
            _prevDayClose = _sessionClose;
            
            // Update 2-day extremes
            _twoDayHigh = Math.Max(_prevDayHigh, _twoDaysAgoHigh > 0 ? _twoDaysAgoHigh : _prevDayHigh);
            _twoDayLow = _twoDaysAgoLow > 0
                ? Math.Min(_prevDayLow, _twoDaysAgoLow)
                : _prevDayLow;
            
            // Reset session tracking for the new day
            _sessionHigh = candle.High;
            _sessionLow = candle.Low;
            _sessionClose = candle.Close;
        }
        
        // Initialize session if first candle
        if (_sessionDate == DateTime.MinValue || candleDate != _sessionDate)
        {
            _sessionDate = candleDate;
            _sessionHigh = candle.High;
            _sessionLow = candle.Low;
            _sessionClose = candle.Close;
            _hasSessionData = true;
        }
        else
        {
            // Update current session extremes
            _sessionHigh = Math.Max(_sessionHigh, candle.High);
            _sessionLow = Math.Min(_sessionLow, candle.Low);
            _sessionClose = candle.Close; // Always update to latest close
        }
    }
    
    /// <summary>
    /// Updates with a HistoricalBar (for backtest use). Same logic as Update(Candlestick).
    /// </summary>
    public void UpdateFromBar(HistoricalBar bar)
    {
        var barDate = bar.Time.Date;
        
        // Day boundary - roll levels forward
        if (barDate != _sessionDate && _sessionDate != DateTime.MinValue && _hasSessionData)
        {
            _twoDaysAgoHigh = _prevDayHigh;
            _twoDaysAgoLow = _prevDayLow;
            
            _prevDayHigh = _sessionHigh;
            _prevDayLow = _sessionLow;
            _prevDayClose = _sessionClose;
            
            _twoDayHigh = Math.Max(_prevDayHigh, _twoDaysAgoHigh > 0 ? _twoDaysAgoHigh : _prevDayHigh);
            _twoDayLow = _twoDaysAgoLow > 0
                ? Math.Min(_prevDayLow, _twoDaysAgoLow)
                : _prevDayLow;
            
            _sessionHigh = bar.High;
            _sessionLow = bar.Low;
            _sessionClose = bar.Close;
        }
        
        if (_sessionDate == DateTime.MinValue || barDate != _sessionDate)
        {
            _sessionDate = barDate;
            _sessionHigh = bar.High;
            _sessionLow = bar.Low;
            _sessionClose = bar.Close;
            _hasSessionData = true;
        }
        else
        {
            _sessionHigh = Math.Max(_sessionHigh, bar.High);
            _sessionLow = Math.Min(_sessionLow, bar.Low);
            _sessionClose = bar.Close;
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
        
        double pivot = (_prevDayHigh + _prevDayLow + _prevDayClose) / 3.0;
        double r1 = 2.0 * pivot - _prevDayLow;
        double s1 = 2.0 * pivot - _prevDayHigh;
        double r2 = pivot + (_prevDayHigh - _prevDayLow);
        double s2 = pivot - (_prevDayHigh - _prevDayLow);
        
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
        return $"PDH: ${_prevDayHigh:F2} | PDL: ${_prevDayLow:F2} | PDC: ${_prevDayClose:F2} | " +
               $"Range: ${PrevDayRange:F2} | Pivot: ${pivots.Pivot:F2} | " +
               $"R1: ${pivots.R1:F2} | S1: ${pivots.S1:F2} | " +
               $"2D-High: ${_twoDayHigh:F2} | 2D-Low: ${_twoDayLow:F2}";
    }
}
