// ============================================================================
// BreakoutPullbackTracker - Tracks breakout/pullback patterns for optimal entries
// ============================================================================
//
// Classic pattern: Price breaks above resistance, retraces back to it (now support),
// and bounces. The old resistance becomes new support - ideal entry point.
//
// States:
//   Waiting      → Price below breakout level, waiting for breakout
//   BrokeOut     → Price crossed above breakout level
//   PullingBack  → Price retreated toward breakout level (now support)
//   Confirmed    → Price bounced from support zone - IDEAL ENTRY
//   Failed       → Price fell below support level - pattern failed
//
// ============================================================================

using IdiotProof.Services;

namespace IdiotProof.Helpers;

/// <summary>
/// State of the breakout-pullback pattern.
/// </summary>
public enum BreakoutState
{
    /// <summary>Waiting for price to break above resistance.</summary>
    Waiting,
    
    /// <summary>Price has broken above resistance level.</summary>
    BrokeOut,
    
    /// <summary>Price is pulling back toward the breakout level.</summary>
    PullingBack,
    
    /// <summary>Pullback held above support and is bouncing - IDEAL ENTRY.</summary>
    Confirmed,
    
    /// <summary>Pullback failed - price fell below support level.</summary>
    Failed
}

/// <summary>
/// Result of breakout-pullback analysis with score adjustment.
/// </summary>
public readonly struct BreakoutPullbackResult
{
    public BreakoutState State { get; init; }
    
    /// <summary>Score adjustment for entry decision (-30 to +30).</summary>
    public int ScoreAdjustment { get; init; }
    
    /// <summary>Whether this is an ideal entry point (confirmed pullback).</summary>
    public bool IsIdealEntry { get; init; }
    
    /// <summary>Whether to veto entry (pattern in progress but not confirmed).</summary>
    public bool ShouldWait { get; init; }
    
    /// <summary>Human-readable reason for the recommendation.</summary>
    public string Reason { get; init; }
    
    /// <summary>The breakout level being tracked.</summary>
    public double BreakoutLevel { get; init; }
    
    /// <summary>The support level (where pullback must hold).</summary>
    public double SupportLevel { get; init; }
    
    /// <summary>Distance from current price to support (as %).</summary>
    public double DistanceToSupportPercent { get; init; }
    
    public override string ToString() => $"[{State}] {Reason} (adj: {ScoreAdjustment:+#;-#;0})";
}

/// <summary>
/// Tracks breakout-pullback patterns for a single symbol.
/// The classic "resistance becomes support" pattern.
/// </summary>
public sealed class BreakoutPullbackTracker
{
    // Configuration
    private readonly string symbol;
    private double breakoutLevel;      // Price that must break (resistance)
    private double supportLevel;       // Where pullback must hold (often same as breakout)
    private double previousHigh;       // Previous high for reference
    
    // State tracking
    private BreakoutState state = BreakoutState.Waiting;
    private double breakoutHighWaterMark;   // Highest price after breakout
    private DateTime breakoutTime = DateTime.MinValue;
    private DateTime pullbackStartTime = DateTime.MinValue;
    private int barsAboveBreakout;
    private int barsPullingBack;
    private double pullbackLow;        // Lowest price during pullback
    
    // Configuration constants
    private const double BreakoutConfirmationPercent = 0.5;   // Must break by 0.5% to confirm
    private const double SupportZonePercent = 1.5;            // Support zone is ±1.5% of level
    private const double FailurePercent = 2.0;                // Failure if drops 2% below support
    private const int MinBarsAboveBreakout = 2;               // Need 2+ bars above to confirm breakout
    
    public BreakoutPullbackTracker(string symbol)
    {
        this.symbol = symbol;
    }
    
    /// <summary>
    /// Gets the current state of the breakout-pullback pattern.
    /// </summary>
    public BreakoutState State => state;
    
    /// <summary>
    /// Gets whether levels are configured for this tracker.
    /// </summary>
    public bool HasLevels => breakoutLevel > 0;
    
    /// <summary>
    /// Gets the configured breakout level.
    /// </summary>
    public double BreakoutLevel => breakoutLevel;
    
    /// <summary>
    /// Gets the configured support level.
    /// </summary>
    public double SupportLevel => supportLevel;
    
    /// <summary>
    /// Configures the breakout and support levels.
    /// </summary>
    public void SetLevels(double breakoutLevel, double supportLevel, double previousHigh = 0)
    {
        this.breakoutLevel = breakoutLevel;
        supportLevel = supportLevel > 0 ? supportLevel : breakoutLevel; // Default support = breakout level
        previousHigh = previousHigh > 0 ? previousHigh : breakoutLevel;
        
        // Reset state when levels change
        Reset();
    }
    
    /// <summary>
    /// Loads levels from a StrategyRule (from strategy-rules.json).
    /// </summary>
    public void LoadFromStrategyRule(StrategyRulesConfig config)
    {
        var rules = config.GetRulesForSymbol(symbol).ToList();
        if (rules.Count == 0)
            return;
            
        // Use the first rule with levels
        var ruleWithLevels = rules.FirstOrDefault(r => r.Levels != null);
        if (ruleWithLevels?.Levels == null)
            return;
            
        var levels = ruleWithLevels.Levels;
        if (levels.Breakout.HasValue)
        {
            SetLevels(
                levels.Breakout.Value,
                levels.Support ?? levels.Breakout.Value,
                levels.PreviousHigh ?? levels.Breakout.Value
            );
        }
    }
    
    /// <summary>
    /// Resets the tracker to waiting state.
    /// </summary>
    public void Reset()
    {
        state = BreakoutState.Waiting;
        breakoutHighWaterMark = 0;
        breakoutTime = DateTime.MinValue;
        pullbackStartTime = DateTime.MinValue;
        barsAboveBreakout = 0;
        barsPullingBack = 0;
        pullbackLow = double.MaxValue;
    }
    
    /// <summary>
    /// Updates the tracker with a new price and returns analysis.
    /// Call this on each tick or candle close.
    /// </summary>
    public BreakoutPullbackResult Update(double currentPrice, bool isNewBar = false)
    {
        if (!HasLevels)
        {
            return new BreakoutPullbackResult
            {
                State = BreakoutState.Waiting,
                ScoreAdjustment = 0,
                IsIdealEntry = false,
                ShouldWait = false,
                Reason = "No breakout levels configured",
                BreakoutLevel = 0,
                SupportLevel = 0
            };
        }
        
        double breakoutPercent = (currentPrice - breakoutLevel) / breakoutLevel * 100;
        double supportPercent = (currentPrice - supportLevel) / supportLevel * 100;
        
        // Update state machine
        switch (state)
        {
            case BreakoutState.Waiting:
                UpdateWaitingState(currentPrice, breakoutPercent, isNewBar);
                break;
                
            case BreakoutState.BrokeOut:
                UpdateBrokeOutState(currentPrice, breakoutPercent, isNewBar);
                break;
                
            case BreakoutState.PullingBack:
                UpdatePullingBackState(currentPrice, supportPercent, isNewBar);
                break;
                
            case BreakoutState.Confirmed:
                UpdateConfirmedState(currentPrice, supportPercent, isNewBar);
                break;
                
            case BreakoutState.Failed:
                UpdateFailedState(currentPrice, supportPercent, isNewBar);
                break;
        }
        
        return BuildResult(currentPrice, supportPercent);
    }
    
    private void UpdateWaitingState(double price, double breakoutPercent, bool isNewBar)
    {
        // Breakout confirmed when price exceeds level by confirmation threshold
        if (breakoutPercent >= BreakoutConfirmationPercent)
        {
            state = BreakoutState.BrokeOut;
            breakoutHighWaterMark = price;
            breakoutTime = DateTime.UtcNow;
            barsAboveBreakout = 1;
        }
    }
    
    private void UpdateBrokeOutState(double price, double breakoutPercent, bool isNewBar)
    {
        // Track high water mark
        if (price > breakoutHighWaterMark)
        {
            breakoutHighWaterMark = price;
        }
        
        if (isNewBar && breakoutPercent > 0)
        {
            barsAboveBreakout++;
        }
        
        // Transition to pulling back when price retreats toward breakout level
        // But only after we've had at least MinBarsAboveBreakout above
        if (barsAboveBreakout >= MinBarsAboveBreakout)
        {
            double retracePercent = (breakoutHighWaterMark - price) / breakoutHighWaterMark * 100;
            
            // If we've retraced at least 30% of the move above breakout, we're pulling back
            double moveAboveBreakout = breakoutHighWaterMark - breakoutLevel;
            double retraceFromHigh = breakoutHighWaterMark - price;
            
            if (moveAboveBreakout > 0 && retraceFromHigh >= moveAboveBreakout * 0.3)
            {
                state = BreakoutState.PullingBack;
                pullbackStartTime = DateTime.UtcNow;
                barsPullingBack = 1;
                pullbackLow = price;
            }
        }
        
        // Failed if price drops below breakout level too quickly (false breakout)
        if (breakoutPercent < -BreakoutConfirmationPercent && barsAboveBreakout < MinBarsAboveBreakout)
        {
            state = BreakoutState.Failed;
        }
    }
    
    private void UpdatePullingBackState(double price, double supportPercent, bool isNewBar)
    {
        // Track pullback low
        if (price < pullbackLow)
        {
            pullbackLow = price;
        }
        
        if (isNewBar)
        {
            barsPullingBack++;
        }
        
        // Check if in support zone (within ±SupportZonePercent of support level)
        bool inSupportZone = Math.Abs(supportPercent) <= SupportZonePercent;
        
        // CONFIRMED: Price is bouncing from support zone
        if (inSupportZone && price > pullbackLow * 1.005) // 0.5% bounce from low
        {
            state = BreakoutState.Confirmed;
        }
        
        // FAILED: Price breaks below support
        if (supportPercent < -FailurePercent)
        {
            state = BreakoutState.Failed;
        }
    }
    
    private void UpdateConfirmedState(double price, double supportPercent, bool isNewBar)
    {
        // Stay confirmed unless support breaks
        if (supportPercent < -FailurePercent)
        {
            state = BreakoutState.Failed;
        }
        
        // If price goes significantly above breakout again, could be new leg up
        // Reset to BrokeOut for fresh tracking
        double breakoutPercent = (price - breakoutLevel) / breakoutLevel * 100;
        if (breakoutPercent > 5.0) // 5% above breakout = new leg
        {
            state = BreakoutState.BrokeOut;
            breakoutHighWaterMark = price;
            barsAboveBreakout = 1;
        }
    }
    
    private void UpdateFailedState(double price, double supportPercent, bool isNewBar)
    {
        // Can rehabilitate if price reclaims support decisively
        if (supportPercent > SupportZonePercent)
        {
            // Back above support zone - try again
            double breakoutPercent = (price - breakoutLevel) / breakoutLevel * 100;
            if (breakoutPercent >= BreakoutConfirmationPercent)
            {
                state = BreakoutState.BrokeOut;
                breakoutHighWaterMark = price;
                barsAboveBreakout = 1;
            }
            else
            {
                state = BreakoutState.Waiting;
            }
        }
    }
    
    private BreakoutPullbackResult BuildResult(double currentPrice, double supportPercent)
    {
        int scoreAdj = 0;
        bool idealEntry = false;
        bool shouldWait = false;
        string reason;
        
        switch (state)
        {
            case BreakoutState.Waiting:
                reason = $"Waiting for breakout above ${breakoutLevel:F2}";
                shouldWait = true;  // Don't enter yet - no breakout
                scoreAdj = -10;     // Slight penalty - pattern not triggered
                break;
                
            case BreakoutState.BrokeOut:
                reason = $"Broke ${breakoutLevel:F2}, high ${breakoutHighWaterMark:F2} - DON'T CHASE";
                shouldWait = true;  // Don't chase the breakout - wait for pullback
                scoreAdj = -15;     // Penalty for chasing breakout
                break;
                
            case BreakoutState.PullingBack:
                double distToSupport = Math.Abs(supportPercent);
                if (distToSupport <= SupportZonePercent)
                {
                    reason = $"In support zone ${supportLevel:F2} (±{SupportZonePercent}%) - WATCH FOR BOUNCE";
                    shouldWait = false;  // Almost there - stay alert
                    scoreAdj = +15;      // Getting close to ideal entry
                }
                else
                {
                    reason = $"Pulling back toward ${supportLevel:F2} ({distToSupport:F1}% away)";
                    shouldWait = true;   // Not in zone yet
                    scoreAdj = 0;        // Neutral
                }
                break;
                
            case BreakoutState.Confirmed:
                reason = $"PULLBACK CONFIRMED - Bouncing from ${supportLevel:F2} support";
                idealEntry = true;
                shouldWait = false;
                scoreAdj = +30;  // Strong bonus for confirmed pullback entry
                break;
                
            case BreakoutState.Failed:
                reason = $"FAILED - Price below support ${supportLevel:F2}";
                shouldWait = true;  // Don't enter failed pattern
                scoreAdj = -30;     // Strong penalty
                break;
                
            default:
                reason = "Unknown state";
                break;
        }
        
        return new BreakoutPullbackResult
        {
            State = state,
            ScoreAdjustment = scoreAdj,
            IsIdealEntry = idealEntry,
            ShouldWait = shouldWait,
            Reason = reason,
            BreakoutLevel = breakoutLevel,
            SupportLevel = supportLevel,
            DistanceToSupportPercent = supportPercent
        };
    }
}
