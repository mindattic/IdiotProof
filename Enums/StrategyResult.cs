// ============================================================================
// StrategyResult - Possible Outcomes for a Strategy
// ============================================================================

namespace IdiotProof.Enums
{
    /// <summary>
    /// Possible outcomes for a strategy.
    /// </summary>
    public enum StrategyResult
    {
        /// <summary>Strategy is still running.</summary>
        Running,
        /// <summary>Conditions were never met - no position taken.</summary>
        NeverBought,
        /// <summary>Price already above take profit target - opportunity missed.</summary>
        MissedTheBoat,
        /// <summary>Position taken and take profit was filled.</summary>
        TakeProfitFilled,
        /// <summary>Position taken and stop loss was triggered.</summary>
        StopLossFilled,
        /// <summary>Position taken and trailing stop loss was triggered.</summary>
        TrailingStopLossFilled,
        /// <summary>Position taken, time expired, exited with profit.</summary>
        ExitedWithProfit,
        /// <summary>Position taken, time expired, cancelled TP (holding position).</summary>
        TakeProfitCancelled,
        /// <summary>Entry order was cancelled before fill.</summary>
        EntryCancelled,
        /// <summary>Position taken, time expired, exited holding position.</summary>
        ExitedHoldingPosition,
        /// <summary>Position taken, time expired, exited with loss.</summary>
        ExitedWithLoss,
        /// <summary>Strategy window ended before conditions were met.</summary>
        TimedOut,
        /// <summary>An error occurred during strategy execution.</summary>
        Error
    }
}
