// ============================================================================
// SetupState - Strategy State Machine States
// ============================================================================

namespace IdiotProof.Enums {
    /// <summary>
    /// Represents the current state of the pullback strategy state machine.
    /// </summary>
    public enum SetupState
    {
        /// <summary>Stage 1: Waiting for price >= BreakoutLevel</summary>
        WaitingForBreakout = 0,

        /// <summary>Stage 2: Waiting for price <= PullbackLevel</summary>
        WaitingForPullback = 1,

        /// <summary>Stage 3: Waiting for price >= VWAP</summary>
        WaitingForVwapReclaim = 2,

        /// <summary>Entry order has been submitted, waiting for fill</summary>
        OrderSubmitted = 3,

        /// <summary>Strategy complete (filled or stopped)</summary>
        Done = 4
    }
}


