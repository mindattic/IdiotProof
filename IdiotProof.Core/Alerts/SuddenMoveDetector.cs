// ============================================================================
// SuddenMoveDetector - Real-Time Price Spike Detection
// ============================================================================
//
// PURPOSE:
// Monitors price movements and detects sudden spikes/drops that warrant
// immediate attention. When detected, generates pre-calculated trade setups
// and sends alerts through all configured channels.
//
// DETECTION METHODS:
// 1. Percent change over rolling window (e.g., 5% in 3 minutes)
// 2. Volume spike detection (e.g., 5x average volume)
// 3. Rate of change acceleration (price moving faster than normal)
// 4. Breakout/breakdown from recent range
//
// ============================================================================

using System.Collections.Concurrent;
using IdiotProof.Services;

namespace IdiotProof.Alerts;

/// <summary>
/// Tracks price history and calculates movement metrics.
/// </summary>
public sealed class PriceTracker
{
    public string Symbol { get; init; } = "";
    public double PreviousClose { get; set; }
    public double AverageVolume { get; set; } = 1_000_000;
    
    // Recent price history (timestamp, price, volume)
    private readonly List<(DateTime Time, double Price, long Volume)> history = new(100);
    private readonly object lockObj = new();
    
    // Session high/low for breakout detection
    public double SessionHigh { get; private set; }
    public double SessionLow { get; private set; } = double.MaxValue;
    public double CurrentPrice { get; private set; }
    public long CurrentVolume { get; private set; }
    
    public void AddTick(double price, long volume = 0)
    {
        lock (lockObj)
        {
            CurrentPrice = price;
            CurrentVolume = volume;
            
            if (price > SessionHigh) SessionHigh = price;
            if (price < SessionLow) SessionLow = price;
            
            history.Add((DateTime.Now, price, volume));
            
            // Keep last 100 ticks (roughly 5-10 minutes at normal pace)
            if (history.Count > 100)
                history.RemoveAt(0);
        }
    }
    
    /// <summary>
    /// Gets price from N minutes ago.
    /// </summary>
    public double? GetPriceMinutesAgo(int minutes)
    {
        lock (lockObj)
        {
            var targetTime = DateTime.Now.AddMinutes(-minutes);
            var tick = history.LastOrDefault(h => h.Time <= targetTime);
            return tick.Price > 0 ? tick.Price : null;
        }
    }
    
    /// <summary>
    /// Calculates percent change over the last N minutes.
    /// </summary>
    public double? GetPercentChangeOverMinutes(int minutes)
    {
        var oldPrice = GetPriceMinutesAgo(minutes);
        if (oldPrice == null || oldPrice.Value <= 0 || CurrentPrice <= 0)
            return null;
        
        return ((CurrentPrice - oldPrice.Value) / oldPrice.Value) * 100;
    }
    
    /// <summary>
    /// Gets total volume over the last N minutes.
    /// </summary>
    public long GetVolumeOverMinutes(int minutes)
    {
        lock (lockObj)
        {
            var cutoff = DateTime.Now.AddMinutes(-minutes);
            return history.Where(h => h.Time >= cutoff).Sum(h => h.Volume);
        }
    }
    
    /// <summary>
    /// Detects if price is accelerating (moving faster recently).
    /// Returns multiplier: 2.0 = moving 2x faster than earlier
    /// </summary>
    public double GetAcceleration()
    {
        lock (lockObj)
        {
            if (history.Count < 20) return 1.0;
            
            var midpoint = history.Count / 2;
            var firstHalf = history.Take(midpoint).ToList();
            var secondHalf = history.Skip(midpoint).ToList();
            
            if (firstHalf.Count < 2 || secondHalf.Count < 2) return 1.0;
            
            // Calculate average absolute change per tick
            double AvgChange(List<(DateTime, double, long)> ticks)
            {
                double total = 0;
                for (int i = 1; i < ticks.Count; i++)
                    total += Math.Abs(ticks[i].Item2 - ticks[i - 1].Item2);
                return total / (ticks.Count - 1);
            }
            
            var avgFirst = AvgChange(firstHalf);
            var avgSecond = AvgChange(secondHalf);
            
            if (avgFirst <= 0) return 1.0;
            return avgSecond / avgFirst;
        }
    }
    
    /// <summary>
    /// Gap percent from previous close.
    /// </summary>
    public double GapPercent => PreviousClose > 0 
        ? ((CurrentPrice - PreviousClose) / PreviousClose) * 100 
        : 0;
    
    /// <summary>
    /// Is price at or near session high?
    /// </summary>
    public bool IsNearSessionHigh => SessionHigh > 0 && CurrentPrice >= SessionHigh * 0.995;
    
    /// <summary>
    /// Is price at or near session low?
    /// </summary>
    public bool IsNearSessionLow => SessionLow < double.MaxValue && CurrentPrice <= SessionLow * 1.005;
}

/// <summary>
/// Configuration for sudden move detection.
/// </summary>
public sealed class MoveDetectorConfig
{
    /// <summary>
    /// Minimum percent change to trigger alert.
    /// </summary>
    public double MinPercentChange { get; set; } = 3.0;
    
    /// <summary>
    /// Time window to measure change (minutes).
    /// </summary>
    public int TimeWindowMinutes { get; set; } = 3;
    
    /// <summary>
    /// Minimum volume ratio vs average to boost confidence.
    /// </summary>
    public double MinVolumeRatio { get; set; } = 2.0;
    
    /// <summary>
    /// Acceleration threshold (2.0 = moving 2x faster than normal).
    /// </summary>
    public double AccelerationThreshold { get; set; } = 1.5;
    
    /// <summary>
    /// Default risk per trade for setup calculations.
    /// </summary>
    public double DefaultRiskDollars { get; set; } = 50.0;
}

/// <summary>
/// Detects sudden price moves and generates trade setups.
/// </summary>
public sealed class SuddenMoveDetector
{
    private readonly MoveDetectorConfig config;
    private readonly ConcurrentDictionary<string, PriceTracker> trackers = new();
    private readonly HashSet<string> recentAlerts = new();
    private readonly object alertLock = new();
    
    /// <summary>
    /// Event fired when a sudden move is detected.
    /// </summary>
    public event Action<TradingAlert>? OnSuddenMoveDetected;
    
    public SuddenMoveDetector(MoveDetectorConfig? config = null)
    {
        config = config ?? new MoveDetectorConfig();
    }
    
    /// <summary>
    /// Registers a symbol to track.
    /// </summary>
    public void RegisterSymbol(string symbol, double previousClose, double avgVolume = 1_000_000)
    {
        trackers[symbol] = new PriceTracker
        {
            Symbol = symbol,
            PreviousClose = previousClose,
            AverageVolume = avgVolume
        };
    }
    
    /// <summary>
    /// Processes a price tick and checks for sudden moves.
    /// </summary>
    public void OnPriceTick(string symbol, double price, long volume = 0)
    {
        if (!trackers.TryGetValue(symbol, out var tracker))
            return;
        
        tracker.AddTick(price, volume);
        
        // Check for sudden move
        var alert = CheckForSuddenMove(tracker);
        if (alert != null)
        {
            // Prevent duplicate alerts
            var alertKey = $"{symbol}_{DateTime.Now:HHmm}";
            lock (alertLock)
            {
                if (recentAlerts.Contains(alertKey))
                    return;
                recentAlerts.Add(alertKey);
            }
            
            OnSuddenMoveDetected?.Invoke(alert);
        }
    }
    
    private TradingAlert? CheckForSuddenMove(PriceTracker tracker)
    {
        var percentChange = tracker.GetPercentChangeOverMinutes(config.TimeWindowMinutes);
        if (percentChange == null) return null;
        
        var absChange = Math.Abs(percentChange.Value);
        
        // Check if change exceeds threshold
        if (absChange < config.MinPercentChange)
            return null;
        
        // Determine alert type and severity
        var type = percentChange.Value > 0 ? AlertType.SuddenSpike : AlertType.SuddenDrop;
        var severity = absChange switch
        {
            >= 10 => AlertSeverity.Critical,
            >= 7 => AlertSeverity.High,
            >= 5 => AlertSeverity.Medium,
            _ => AlertSeverity.Low
        };
        
        // Calculate confidence based on multiple factors
        var confidence = CalculateConfidence(tracker, absChange);
        
        // Generate pre-calculated trade setups
        var longSetup = GenerateLongSetup(tracker);
        var shortSetup = GenerateShortSetup(tracker);
        
        // Determine reason
        var reason = DetermineReason(tracker, percentChange.Value);
        
        return new TradingAlert
        {
            Symbol = tracker.Symbol,
            Type = type,
            Severity = severity,
            CurrentPrice = tracker.CurrentPrice,
            PreviousPrice = tracker.GetPriceMinutesAgo(config.TimeWindowMinutes) ?? tracker.PreviousClose,
            PercentChange = percentChange.Value,
            VolumeRatio = tracker.AverageVolume > 0 
                ? tracker.GetVolumeOverMinutes(config.TimeWindowMinutes) / (tracker.AverageVolume / 60.0 * config.TimeWindowMinutes)
                : 0,
            TimeFrame = TimeSpan.FromMinutes(config.TimeWindowMinutes),
            Confidence = confidence,
            Reason = reason,
            LongSetup = longSetup,
            ShortSetup = shortSetup
        };
    }
    
    private int CalculateConfidence(PriceTracker tracker, double absChange)
    {
        int score = 0;
        
        // Base score from percent change (up to 40 points)
        score += Math.Min(40, (int)(absChange * 8));
        
        // Volume confirmation (up to 30 points)
        var recentVolume = tracker.GetVolumeOverMinutes(config.TimeWindowMinutes);
        var expectedVolume = tracker.AverageVolume / 60.0 * config.TimeWindowMinutes;
        var volumeRatio = expectedVolume > 0 ? recentVolume / expectedVolume : 0;
        score += Math.Min(30, (int)(volumeRatio * 10));
        
        // Acceleration (up to 20 points)
        var acceleration = tracker.GetAcceleration();
        if (acceleration > config.AccelerationThreshold)
            score += Math.Min(20, (int)((acceleration - 1) * 10));
        
        // Near session extremes (10 points)
        if (tracker.IsNearSessionHigh || tracker.IsNearSessionLow)
            score += 10;
        
        return Math.Min(100, score);
    }
    
    private string DetermineReason(PriceTracker tracker, double percentChange)
    {
        var reasons = new List<string>();
        
        // Direction
        reasons.Add(percentChange > 0 ? "Price spiking UP" : "Price dropping DOWN");
        
        // Acceleration
        var acceleration = tracker.GetAcceleration();
        if (acceleration > 1.5)
            reasons.Add($"accelerating ({acceleration:F1}x faster)");
        
        // Volume
        var volumeRatio = tracker.GetVolumeOverMinutes(config.TimeWindowMinutes) / 
                         (tracker.AverageVolume / 60.0 * config.TimeWindowMinutes);
        if (volumeRatio > 2)
            reasons.Add($"high volume ({volumeRatio:F1}x avg)");
        
        // Session extremes
        if (tracker.IsNearSessionHigh)
            reasons.Add("at session highs");
        else if (tracker.IsNearSessionLow)
            reasons.Add("at session lows");
        
        // Gap
        var gap = Math.Abs(tracker.GapPercent);
        if (gap > 2)
            reasons.Add($"gapped {(tracker.GapPercent > 0 ? "up" : "down")} {gap:F1}%");
        
        return string.Join(", ", reasons);
    }
    
    private TradeSetup GenerateLongSetup(PriceTracker tracker)
    {
        var entry = tracker.CurrentPrice;
        
        // Stop Loss: Below session low or 2% below entry
        var stopFromLow = tracker.SessionLow < double.MaxValue 
            ? tracker.SessionLow * 0.995 
            : entry * 0.98;
        var stopFromPercent = entry * 0.98;
        var stopLoss = Math.Max(stopFromLow, stopFromPercent);  // Use tighter stop
        
        // Take Profit: 2.5x risk distance
        var riskDistance = entry - stopLoss;
        var takeProfit = entry + (riskDistance * 2.5);
        
        // Trailing stop based on volatility
        var acceleration = tracker.GetAcceleration();
        var trailingPercent = acceleration > 1.5 ? 2.0 : 1.5;
        
        // Quantity based on risk
        var riskPerShare = entry - stopLoss;
        var quantity = riskPerShare > 0 
            ? (int)Math.Floor(config.DefaultRiskDollars / riskPerShare) 
            : 1;
        quantity = Math.Max(1, quantity);
        
        var riskDollars = quantity * riskPerShare;
        var rewardDollars = quantity * (takeProfit - entry);
        var rr = riskDollars > 0 ? rewardDollars / riskDollars : 0;
        
        return new TradeSetup
        {
            Symbol = tracker.Symbol,
            IsLong = true,
            EntryPrice = Math.Round(entry, 2),
            StopLoss = Math.Round(stopLoss, 2),
            TakeProfit = Math.Round(takeProfit, 2),
            TrailingStopPercent = trailingPercent,
            Quantity = quantity,
            RiskDollars = Math.Round(riskDollars, 2),
            RewardDollars = Math.Round(rewardDollars, 2),
            RiskRewardRatio = Math.Round(rr, 2)
        };
    }
    
    private TradeSetup GenerateShortSetup(PriceTracker tracker)
    {
        var entry = tracker.CurrentPrice;
        
        // Stop Loss: Above session high or 2% above entry
        var stopFromHigh = tracker.SessionHigh > 0 
            ? tracker.SessionHigh * 1.005 
            : entry * 1.02;
        var stopFromPercent = entry * 1.02;
        var stopLoss = Math.Min(stopFromHigh, stopFromPercent);  // Use tighter stop
        
        // Take Profit: 2.5x risk distance
        var riskDistance = stopLoss - entry;
        var takeProfit = entry - (riskDistance * 2.5);
        
        // Trailing stop based on volatility
        var acceleration = tracker.GetAcceleration();
        var trailingPercent = acceleration > 1.5 ? 2.0 : 1.5;
        
        // Quantity based on risk
        var riskPerShare = stopLoss - entry;
        var quantity = riskPerShare > 0 
            ? (int)Math.Floor(config.DefaultRiskDollars / riskPerShare) 
            : 1;
        quantity = Math.Max(1, quantity);
        
        var riskDollars = quantity * riskPerShare;
        var rewardDollars = quantity * (entry - takeProfit);
        var rr = riskDollars > 0 ? rewardDollars / riskDollars : 0;
        
        return new TradeSetup
        {
            Symbol = tracker.Symbol,
            IsLong = false,
            EntryPrice = Math.Round(entry, 2),
            StopLoss = Math.Round(stopLoss, 2),
            TakeProfit = Math.Round(takeProfit, 2),
            TrailingStopPercent = trailingPercent,
            Quantity = quantity,
            RiskDollars = Math.Round(riskDollars, 2),
            RewardDollars = Math.Round(rewardDollars, 2),
            RiskRewardRatio = Math.Round(rr, 2)
        };
    }
    
    /// <summary>
    /// Clears recent alert cache (call periodically).
    /// </summary>
    public void ClearRecentAlerts()
    {
        lock (alertLock)
        {
            recentAlerts.Clear();
        }
    }
}
