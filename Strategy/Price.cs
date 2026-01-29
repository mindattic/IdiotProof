// ============================================================================
// Price - Price Type Definitions for Orders
// ============================================================================
//
// BEST PRACTICES:
// 1. Use Price.Current for most strategies - executes at market price.
// 2. Use Price.VWAP when you want to match the volume-weighted average.
// 3. Price.Bid/Ask are useful for more precise limit order placement.
// 4. Consider market conditions - VWAP may not be available in pre-market.
//
// USAGE:
//   .Buy(quantity: 100, Price.Current)   // Market order at current price
//   .Buy(quantity: 100, Price.VWAP)      // Limit order at VWAP
//
// ============================================================================

namespace IdiotProof.Models
{
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
