// ============================================================================
// Price Helper - Utilities for price calculations and tick size rounding
// ============================================================================
//
// Handles tick size rounding for IBKR orders to prevent
// "Price does not conform to minimum price variation" errors.
//
// Common tick sizes for US stocks:
//   - Most stocks: $0.01 (penny increment)
//   - Some stocks: $0.05 (nickel increment) 
//   - Stocks < $1.00: $0.0001 (sub-penny)
//
// ============================================================================

using System;

namespace IdiotProof.Helpers {
    /// <summary>
    /// Provides utilities for price calculations and tick size rounding.
    /// </summary>
    public static class PriceHelper
    {
        /// <summary>
        /// Default tick size for most US stocks ($0.01).
        /// </summary>
        public const double DefaultTickSize = 0.01;

        /// <summary>
        /// Rounds a price to the nearest valid tick size.
        /// </summary>
        /// <param name="price">The price to round.</param>
        /// <param name="tickSize">The minimum tick size (default: 0.01).</param>
        /// <param name="roundUp">True to round up, false to round to nearest.</param>
        /// <returns>The price rounded to the nearest valid tick.</returns>
        /// <remarks>
        /// IBKR rejects orders with prices that don't conform to the security's
        /// minimum price variation. This method ensures prices are valid.
        /// </remarks>
        public static double RoundToTickSize(double price, double tickSize = DefaultTickSize, bool roundUp = false)
        {
            if (tickSize <= 0) tickSize = DefaultTickSize;

            if (roundUp)
            {
                return Math.Ceiling(price / tickSize) * tickSize;
            }

            return Math.Round(price / tickSize) * tickSize;
        }

        /// <summary>
        /// Rounds a price down to the nearest valid tick size.
        /// </summary>
        /// <param name="price">The price to round.</param>
        /// <param name="tickSize">The minimum tick size (default: 0.01).</param>
        /// <returns>The price rounded down to the nearest valid tick.</returns>
        public static double RoundDownToTickSize(double price, double tickSize = DefaultTickSize)
        {
            if (tickSize <= 0) tickSize = DefaultTickSize;
            return Math.Floor(price / tickSize) * tickSize;
        }

        /// <summary>
        /// Determines the appropriate tick size for a given price.
        /// </summary>
        /// <param name="price">The current stock price.</param>
        /// <returns>The tick size to use for this price level.</returns>
        /// <remarks>
        /// <para>Per SEC Rule 612 (Sub-Penny Rule):</para>
        /// <list type="bullet">
        ///   <item>Stocks >= $1.00: minimum $0.01 tick</item>
        ///   <item>Stocks &lt; $1.00: minimum $0.0001 tick</item>
        /// </list>
        /// <para>Some exchanges may have different tick sizes. This provides a safe default.</para>
        /// </remarks>
        public static double GetTickSizeForPrice(double price)
        {
            // Sub-penny stocks (under $1)
            if (price < 1.0)
            {
                return 0.0001;
            }

            // Most US stocks use penny increments
            return 0.01;
        }

        /// <summary>
        /// Rounds a take profit price to valid tick size.
        /// For long positions, rounds down (more conservative profit target).
        /// </summary>
        /// <param name="tpPrice">The take profit price.</param>
        /// <param name="isLong">True if long position, false if short.</param>
        /// <param name="tickSize">The tick size (0 for auto-detect).</param>
        /// <returns>The rounded take profit price.</returns>
        public static double RoundTakeProfitPrice(double tpPrice, bool isLong, double tickSize = 0)
        {
            if (tickSize <= 0) tickSize = GetTickSizeForPrice(tpPrice);

            // For longs: selling, so round down to be conservative (ensure fill)
            // For shorts: buying to cover, so round up to be conservative
            return isLong
                ? RoundDownToTickSize(tpPrice, tickSize)
                : RoundToTickSize(tpPrice, tickSize, roundUp: true);
        }

        /// <summary>
        /// Rounds a stop loss price to valid tick size.
        /// Rounds in the direction that provides more protection.
        /// </summary>
        /// <param name="slPrice">The stop loss price.</param>
        /// <param name="isLong">True if long position, false if short.</param>
        /// <param name="tickSize">The tick size (0 for auto-detect).</param>
        /// <returns>The rounded stop loss price.</returns>
        public static double RoundStopLossPrice(double slPrice, bool isLong, double tickSize = 0)
        {
            if (tickSize <= 0) tickSize = GetTickSizeForPrice(slPrice);

            // For longs: stop triggers below entry, round up for tighter protection
            // For shorts: stop triggers above entry, round down for tighter protection
            return isLong
                ? RoundToTickSize(slPrice, tickSize, roundUp: true)
                : RoundDownToTickSize(slPrice, tickSize);
        }
    }
}
