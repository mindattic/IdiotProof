// ============================================================================
// TrendDirectionFilter - Prevents trading against clear trends
// ============================================================================
//
// PURPOSE:
// Stops the system from buying into clear downtrends or shorting clear uptrends.
// Uses multiple confirmations to identify when trend direction is obvious.
//
// WHAT IT TRACKS:
// 1. EMA Slopes: All EMAs sloping down = bearish, all sloping up = bullish
// 2. VWAP Duration: How long price has stayed below/above VWAP (minutes)
// 3. Higher Highs/Lower Lows: Price structure confirmation
// 4. ADX + DI Direction: Trend strength with directional confirmation
//
// USAGE:
//   var filter = new TrendDirectionFilter();
//   filter.Update(price, vwap, ema9, ema21, ema50, adx, plusDi, minusDi);
//   if (filter.IsInClearDowntrend) { /* Don't buy */ }
//   if (filter.IsInClearUptrend) { /* Don't short */ }
//
// ============================================================================

using System;
using System.Collections.Generic;

namespace IdiotProof.Helpers;

/// <summary>
/// Filters out trading against very obvious trend directions.
/// Prevents buying into clear downtrends and shorting into clear uptrends.
/// </summary>
public sealed class TrendDirectionFilter
{
    // ========================================================================
    // Configuration thresholds
    // ========================================================================
    
    /// <summary>
    /// Minimum consecutive candles below VWAP to confirm downtrend.
    /// </summary>
    private const int MinCandlesBelowVwap = 10;
    
    /// <summary>
    /// Minimum consecutive candles above VWAP to confirm uptrend.
    /// </summary>
    private const int MinCandlesAboveVwap = 10;
    
    /// <summary>
    /// Minimum ADX value to consider trend strong enough to filter.
    /// Below this, consider it ranging (no filter applied).
    /// </summary>
    private const double MinAdxForTrendFilter = 15;
    
    /// <summary>
    /// Number of EMA slope samples to keep for trend detection.
    /// </summary>
    private const int SlopeSampleCount = 5;

    // ========================================================================
    // Tracking state
    // ========================================================================
    
    private int _candlesBelowVwap;
    private int _candlesAboveVwap;
    
    // Track last EMA values to calculate slopes
    private readonly Queue<double> _ema9History = new();
    private readonly Queue<double> _ema21History = new();
    private readonly Queue<double> _ema50History = new();
    
    // Track swing highs/lows for structure analysis
    private readonly Queue<double> _recentHighs = new();
    private readonly Queue<double> _recentLows = new();
    private double _lastHigh;
    private double _lastLow = double.MaxValue;
    
    // Current indicator values
    private double _currentAdx;
    private double _currentPlusDi;
    private double _currentMinusDi;
    private double _currentPrice;
    private double _currentVwap;

    // ========================================================================
    // Public properties
    // ========================================================================
    
    /// <summary>
    /// True if the filter has enough data to make a determination.
    /// </summary>
    public bool IsReady { get; private set; }
    
    /// <summary>
    /// True if there's a clear downtrend - DO NOT BUY.
    /// </summary>
    public bool IsInClearDowntrend { get; private set; }
    
    /// <summary>
    /// True if there's a clear uptrend - DO NOT SHORT.
    /// </summary>
    public bool IsInClearUptrend { get; private set; }
    
    /// <summary>
    /// Trend strength from -100 (strong bearish) to +100 (strong bullish).
    /// </summary>
    public int TrendScore { get; private set; }
    
    /// <summary>
    /// Description of why the trend filter is active.
    /// </summary>
    public string Reason { get; private set; } = "";
    
    /// <summary>
    /// Number of consecutive candles below VWAP.
    /// </summary>
    public int CandlesBelowVwap => _candlesBelowVwap;
    
    /// <summary>
    /// Number of consecutive candles above VWAP.
    /// </summary>
    public int CandlesAboveVwap => _candlesAboveVwap;

    // ========================================================================
    // Update methods
    // ========================================================================
    
    /// <summary>
    /// Updates the filter with current bar data. Call once per completed candle.
    /// </summary>
    /// <param name="price">Current close price</param>
    /// <param name="vwap">Current VWAP</param>
    /// <param name="ema9">EMA(9) value</param>
    /// <param name="ema21">EMA(21) value</param>
    /// <param name="ema50">EMA(50) value</param>
    /// <param name="adx">ADX value</param>
    /// <param name="plusDi">+DI value</param>
    /// <param name="minusDi">-DI value</param>
    /// <param name="high">Bar high</param>
    /// <param name="low">Bar low</param>
    public void Update(double price, double vwap, double ema9, double ema21, double ema50,
                       double adx, double plusDi, double minusDi, double high, double low)
    {
        _currentPrice = price;
        _currentVwap = vwap;
        _currentAdx = adx;
        _currentPlusDi = plusDi;
        _currentMinusDi = minusDi;
        
        // Update VWAP position tracking
        UpdateVwapTracking(price, vwap);
        
        // Update EMA slope history
        UpdateEmaHistory(ema9, ema21, ema50);
        
        // Update swing high/low tracking
        UpdateSwingTracking(high, low);
        
        // Calculate trend assessment
        CalculateTrendAssessment();
    }
    
    /// <summary>
    /// Updates only VWAP position - call on each tick if desired.
    /// </summary>
    public void UpdateVwapPosition(double price, double vwap)
    {
        _currentPrice = price;
        _currentVwap = vwap;
    }
    
    private void UpdateVwapTracking(double price, double vwap)
    {
        if (vwap <= 0) return;
        
        if (price < vwap)
        {
            _candlesBelowVwap++;
            _candlesAboveVwap = 0;
        }
        else if (price > vwap)
        {
            _candlesAboveVwap++;
            _candlesBelowVwap = 0;
        }
        // If price == vwap exactly, don't reset either counter
    }
    
    private void UpdateEmaHistory(double ema9, double ema21, double ema50)
    {
        // Add new values
        _ema9History.Enqueue(ema9);
        _ema21History.Enqueue(ema21);
        _ema50History.Enqueue(ema50);
        
        // Keep only the last N samples
        while (_ema9History.Count > SlopeSampleCount) _ema9History.Dequeue();
        while (_ema21History.Count > SlopeSampleCount) _ema21History.Dequeue();
        while (_ema50History.Count > SlopeSampleCount) _ema50History.Dequeue();
    }
    
    private void UpdateSwingTracking(double high, double low)
    {
        // Track intraday high/low
        if (high > _lastHigh)
            _lastHigh = high;
        if (low < _lastLow)
            _lastLow = low;
        
        // Store recent highs/lows for pattern detection
        _recentHighs.Enqueue(high);
        _recentLows.Enqueue(low);
        
        while (_recentHighs.Count > 5) _recentHighs.Dequeue();
        while (_recentLows.Count > 5) _recentLows.Dequeue();
    }
    
    private void CalculateTrendAssessment()
    {
        // Need minimum data
        if (_ema9History.Count < 3)
        {
            IsReady = false;
            IsInClearDowntrend = false;
            IsInClearUptrend = false;
            TrendScore = 0;
            Reason = "Warming up";
            return;
        }
        
        IsReady = true;
        
        // Calculate individual signals
        int vwapSignal = CalculateVwapSignal();
        int emaSignal = CalculateEmaSignal();
        int diSignal = CalculateDiSignal();
        int structureSignal = CalculateStructureSignal();
        
        // Weighted combination
        // VWAP duration is strong signal (30%)
        // EMA slopes are strong signal (30%)
        // DI direction with ADX confirmation (25%)
        // Structure (higher lows / lower highs) (15%)
        TrendScore = (int)(
            vwapSignal * 0.30 +
            emaSignal * 0.30 +
            diSignal * 0.25 +
            structureSignal * 0.15
        );
        
        // Determine clear trend conditions
        // Need multiple confirmations to filter
        var reasons = new List<string>();
        
        // Clear DOWNTREND conditions
        bool vwapBearish = _candlesBelowVwap >= MinCandlesBelowVwap;
        bool emaBearish = emaSignal < -50;
        bool diBearish = _currentMinusDi > _currentPlusDi && _currentAdx >= MinAdxForTrendFilter;
        
        int bearishConfirmations = (vwapBearish ? 1 : 0) + (emaBearish ? 1 : 0) + (diBearish ? 1 : 0);
        
        if (bearishConfirmations >= 2)
        {
            IsInClearDowntrend = true;
            if (vwapBearish) reasons.Add($"below VWAP {_candlesBelowVwap} bars");
            if (emaBearish) reasons.Add("EMAs sloping down");
            if (diBearish) reasons.Add($"-DI>{_currentMinusDi:F0} > +DI>{_currentPlusDi:F0}");
        }
        else
        {
            IsInClearDowntrend = false;
        }
        
        // Clear UPTREND conditions
        bool vwapBullish = _candlesAboveVwap >= MinCandlesAboveVwap;
        bool emaBullish = emaSignal > 50;
        bool diBullish = _currentPlusDi > _currentMinusDi && _currentAdx >= MinAdxForTrendFilter;
        
        int bullishConfirmations = (vwapBullish ? 1 : 0) + (emaBullish ? 1 : 0) + (diBullish ? 1 : 0);
        
        if (bullishConfirmations >= 2)
        {
            IsInClearUptrend = true;
            if (vwapBullish) reasons.Add($"above VWAP {_candlesAboveVwap} bars");
            if (emaBullish) reasons.Add("EMAs sloping up");
            if (diBullish) reasons.Add($"+DI>{_currentPlusDi:F0} > -DI>{_currentMinusDi:F0}");
        }
        else
        {
            IsInClearUptrend = false;
        }
        
        Reason = reasons.Count > 0 ? string.Join(", ", reasons) : "No clear trend";
    }
    
    /// <summary>
    /// Returns signal from -100 (bearish) to +100 (bullish) based on VWAP position duration.
    /// </summary>
    private int CalculateVwapSignal()
    {
        if (_candlesBelowVwap >= MinCandlesBelowVwap)
        {
            // Sustained below VWAP = bearish
            // More bars = stronger signal (capped at 20 bars for max signal)
            int strength = Math.Min(_candlesBelowVwap, 20);
            return -50 - (strength * 50 / 20);  // -50 to -100
        }
        
        if (_candlesAboveVwap >= MinCandlesAboveVwap)
        {
            // Sustained above VWAP = bullish
            int strength = Math.Min(_candlesAboveVwap, 20);
            return 50 + (strength * 50 / 20);  // +50 to +100
        }
        
        // Neither condition met - return proportional signal
        if (_candlesBelowVwap > 0)
            return -_candlesBelowVwap * 5;  // -5 per bar below
        if (_candlesAboveVwap > 0)
            return _candlesAboveVwap * 5;   // +5 per bar above
            
        return 0;
    }
    
    /// <summary>
    /// Returns signal from -100 (bearish) to +100 (bullish) based on EMA slopes.
    /// </summary>
    private int CalculateEmaSignal()
    {
        if (_ema9History.Count < 3 || _ema21History.Count < 3 || _ema50History.Count < 3)
            return 0;
        
        var ema9Arr = new List<double>(_ema9History);
        var ema21Arr = new List<double>(_ema21History);
        var ema50Arr = new List<double>(_ema50History);
        
        // Calculate slopes (positive = rising, negative = falling)
        double slope9 = ema9Arr[^1] - ema9Arr[0];
        double slope21 = ema21Arr[^1] - ema21Arr[0];
        double slope50 = ema50Arr[^1] - ema50Arr[0];
        
        // Count bearish (falling) vs bullish (rising) slopes
        int bearishSlopes = (slope9 < 0 ? 1 : 0) + (slope21 < 0 ? 1 : 0) + (slope50 < 0 ? 1 : 0);
        int bullishSlopes = (slope9 > 0 ? 1 : 0) + (slope21 > 0 ? 1 : 0) + (slope50 > 0 ? 1 : 0);
        
        // Also check EMA stacking (price position relative to EMAs)
        bool belowAll = _currentPrice < ema9Arr[^1] && _currentPrice < ema21Arr[^1] && _currentPrice < ema50Arr[^1];
        bool aboveAll = _currentPrice > ema9Arr[^1] && _currentPrice > ema21Arr[^1] && _currentPrice > ema50Arr[^1];
        
        int signal = 0;
        
        // All slopes falling = strong bearish
        if (bearishSlopes == 3)
            signal -= 60;
        else if (bearishSlopes == 2)
            signal -= 30;
        
        // All slopes rising = strong bullish
        if (bullishSlopes == 3)
            signal += 60;
        else if (bullishSlopes == 2)
            signal += 30;
        
        // Price position bonus
        if (belowAll)
            signal -= 40;
        if (aboveAll)
            signal += 40;
        
        return Math.Clamp(signal, -100, 100);
    }
    
    /// <summary>
    /// Returns signal from -100 (bearish) to +100 (bullish) based on DI and ADX.
    /// </summary>
    private int CalculateDiSignal()
    {
        // No signal if ADX is too low (no trend)
        if (_currentAdx < MinAdxForTrendFilter)
            return 0;
        
        // DI spread determines direction and strength
        double diSpread = _currentPlusDi - _currentMinusDi;
        
        // Scale by ADX (stronger ADX = stronger signal)
        double adxMultiplier = Math.Min(_currentAdx / 50.0, 1.0);  // Cap at ADX 50
        
        int signal = (int)(diSpread * 2 * adxMultiplier);  // ±50 DI spread = ±100 signal
        
        return Math.Clamp(signal, -100, 100);
    }
    
    /// <summary>
    /// Returns signal from -100 (bearish) to +100 (bullish) based on price structure.
    /// </summary>
    private int CalculateStructureSignal()
    {
        if (_recentHighs.Count < 3 || _recentLows.Count < 3)
            return 0;
        
        var highs = new List<double>(_recentHighs);
        var lows = new List<double>(_recentLows);
        
        // Count lower highs and lower lows (bearish)
        int lowerHighs = 0;
        int lowerLows = 0;
        for (int i = 1; i < highs.Count; i++)
        {
            if (highs[i] < highs[i - 1]) lowerHighs++;
            if (lows[i] < lows[i - 1]) lowerLows++;
        }
        
        // Count higher highs and higher lows (bullish)
        int higherHighs = 0;
        int higherLows = 0;
        for (int i = 1; i < highs.Count; i++)
        {
            if (highs[i] > highs[i - 1]) higherHighs++;
            if (lows[i] > lows[i - 1]) higherLows++;
        }
        
        int bearishStructure = lowerHighs + lowerLows;
        int bullishStructure = higherHighs + higherLows;
        
        // Max possible is 8 (4 lower highs + 4 lower lows from 5 samples)
        int signal = (bullishStructure - bearishStructure) * 100 / 8;
        
        return Math.Clamp(signal, -100, 100);
    }
    
    /// <summary>
    /// Resets all tracking state (call at start of new session).
    /// </summary>
    public void Reset()
    {
        _candlesBelowVwap = 0;
        _candlesAboveVwap = 0;
        _ema9History.Clear();
        _ema21History.Clear();
        _ema50History.Clear();
        _recentHighs.Clear();
        _recentLows.Clear();
        _lastHigh = 0;
        _lastLow = double.MaxValue;
        IsReady = false;
        IsInClearDowntrend = false;
        IsInClearUptrend = false;
        TrendScore = 0;
        Reason = "";
    }
    
    /// <summary>
    /// Returns a summary string for logging.
    /// </summary>
    public override string ToString()
    {
        if (!IsReady)
            return "[TREND] Warming up";
        
        string status = (IsInClearDowntrend, IsInClearUptrend) switch
        {
            (true, false) => "DOWNTREND (no longs)",
            (false, true) => "UPTREND (no shorts)",
            _ => "NEUTRAL"
        };
        
        return $"[TREND] {status} | Score={TrendScore:+#;-#;0} | VWAP: {(_candlesBelowVwap > 0 ? $"-{_candlesBelowVwap}" : _candlesAboveVwap > 0 ? $"+{_candlesAboveVwap}" : "0")} bars | {Reason}";
    }
}
