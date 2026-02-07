// ============================================================================
// Strategy Parameters - Configurable strategy settings for optimization
// ============================================================================

namespace IdiotProof.BackTesting.Models;

/// <summary>
/// Represents the configurable parameters of a trading strategy.
/// These parameters can be optimized during backtesting.
/// </summary>
public record StrategyParameters
{
    // ========================================================================
    // Entry Parameters
    // ========================================================================

    /// <summary>Entry price level (breakout trigger).</summary>
    public double EntryPrice { get; init; }

    /// <summary>Pullback confirmation level.</summary>
    public double? PullbackPrice { get; init; }

    /// <summary>Whether to require price above VWAP for entry.</summary>
    public bool RequireAboveVwap { get; init; } = true;

    /// <summary>Whether to require higher lows pattern.</summary>
    public bool RequireHigherLows { get; init; }

    /// <summary>Minimum ADX value for trend confirmation.</summary>
    public double? MinAdx { get; init; }

    /// <summary>Whether to require +DI > -DI.</summary>
    public bool RequireDiPositive { get; init; }

    // ========================================================================
    // Exit Parameters
    // ========================================================================

    /// <summary>Take profit price level.</summary>
    public double TakeProfitPrice { get; init; }

    /// <summary>Take profit as percentage from entry.</summary>
    public double TakeProfitPercent { get; init; }

    /// <summary>Stop loss price level.</summary>
    public double StopLossPrice { get; init; }

    /// <summary>Stop loss as percentage from entry.</summary>
    public double StopLossPercent { get; init; }

    /// <summary>Trailing stop loss percentage.</summary>
    public double? TrailingStopPercent { get; init; }

    /// <summary>Exit at end of day if still in position.</summary>
    public bool ExitAtEod { get; init; } = true;

    /// <summary>End of day exit time.</summary>
    public TimeOnly EodExitTime { get; init; } = new TimeOnly(15, 55);

    // ========================================================================
    // Position Parameters
    // ========================================================================

    /// <summary>Position size (shares).</summary>
    public int Quantity { get; init; } = 100;

    /// <summary>Whether this is a long or short strategy.</summary>
    public bool IsLong { get; init; } = true;

    // ========================================================================
    // Calculated Properties
    // ========================================================================

    /// <summary>Risk amount per share.</summary>
    public double RiskPerShare => Math.Abs(EntryPrice - StopLossPrice);

    /// <summary>Reward amount per share.</summary>
    public double RewardPerShare => Math.Abs(TakeProfitPrice - EntryPrice);

    /// <summary>Risk/Reward ratio.</summary>
    public double RiskRewardRatio => RiskPerShare > 0 
        ? RewardPerShare / RiskPerShare 
        : 0;

    /// <summary>Total risk in dollars.</summary>
    public double TotalRisk => RiskPerShare * Quantity;

    /// <summary>Total potential reward in dollars.</summary>
    public double TotalReward => RewardPerShare * Quantity;

    // ========================================================================
    // Factory Methods
    // ========================================================================

    /// <summary>
    /// Creates parameters from price levels and percentages.
    /// </summary>
    public static StrategyParameters FromLevels(
        double entry,
        double takeProfit,
        double stopLoss,
        int quantity = 100,
        double? trailingStop = null,
        bool isLong = true)
    {
        return new StrategyParameters
        {
            EntryPrice = entry,
            TakeProfitPrice = takeProfit,
            TakeProfitPercent = (takeProfit - entry) / entry * 100,
            StopLossPrice = stopLoss,
            StopLossPercent = (entry - stopLoss) / entry * 100,
            TrailingStopPercent = trailingStop,
            Quantity = quantity,
            IsLong = isLong
        };
    }

    /// <summary>
    /// Creates parameters from percentages.
    /// </summary>
    public static StrategyParameters FromPercent(
        double entry,
        double takeProfitPct,
        double stopLossPct,
        int quantity = 100,
        double? trailingStopPct = null,
        bool isLong = true)
    {
        return new StrategyParameters
        {
            EntryPrice = entry,
            TakeProfitPrice = entry * (1 + takeProfitPct / 100),
            TakeProfitPercent = takeProfitPct,
            StopLossPrice = entry * (1 - stopLossPct / 100),
            StopLossPercent = stopLossPct,
            TrailingStopPercent = trailingStopPct,
            Quantity = quantity,
            IsLong = isLong
        };
    }

    public override string ToString() =>
        $"Entry:{EntryPrice:F2} TP:{TakeProfitPrice:F2}(+{TakeProfitPercent:F1}%) " +
        $"SL:{StopLossPrice:F2}(-{StopLossPercent:F1}%) R:R={RiskRewardRatio:F2}";
}
