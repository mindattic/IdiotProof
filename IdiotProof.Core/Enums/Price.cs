// ============================================================================
// Price - Price Type Definitions for Orders
// ============================================================================

namespace IdiotProof.Enums {
    /// <summary>
    /// Price type for order execution, determining how the order price is calculated.
    /// </summary>
    /// <remarks>
    /// <para><b>Best Practices:</b></para>
    /// <list type="bullet">
    ///   <item><see cref="Current"/>: Best for fast execution; may have slippage.</item>
    ///   <item><see cref="VWAP"/>: Good for mean-reversion strategies; may not fill quickly.</item>
    ///   <item><see cref="Bid"/>/<see cref="Ask"/>: Precise control; requires active monitoring.</item>
    /// </list>
    /// 
    /// <para><b>Warning:</b> In pre-market/after-hours, VWAP calculation may be unreliable
    /// due to lower volume. Consider using <see cref="Current"/> during extended hours.</para>
    /// </remarks>
    public enum Price
    {
        /// <summary>
        /// Execute at current market price.
        /// </summary>
        /// <remarks>
        /// <b>Best for:</b> Fast execution when you need to enter/exit immediately.
        /// <br/><b>Risk:</b> May experience slippage in fast-moving or illiquid markets.
        /// </remarks>
        Current,

        /// <summary>
        /// Execute at Volume-Weighted Average Price (VWAP).
        /// </summary>
        /// <remarks>
        /// <b>Best for:</b> Getting a fair average price over the session.
        /// <br/><b>Risk:</b> May not fill if price moves away from VWAP.
        /// <br/><b>Note:</b> VWAP resets at market open each day.
        /// </remarks>
        VWAP,

        /// <summary>
        /// Execute at bid price (highest price buyers are willing to pay).
        /// </summary>
        /// <remarks>
        /// <b>Best for:</b> Selling with limit order at current bid.
        /// <br/><b>Risk:</b> May not fill if bid drops before execution.
        /// </remarks>
        Bid,

        /// <summary>
        /// Execute at ask price (lowest price sellers are willing to accept).
        /// </summary>
        /// <remarks>
        /// <b>Best for:</b> Buying with limit order at current ask.
        /// <br/><b>Risk:</b> May not fill if ask rises before execution.
        /// </remarks>
        Ask
    }
}


