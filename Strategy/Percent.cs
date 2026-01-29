// ============================================================================
// Percent - Percentage Value Definitions
// ============================================================================
//
// BEST PRACTICES:
// 1. Use named constants (Percent.Ten) for readability over magic numbers.
// 2. For trailing stops, 5-15% is typical for volatile stocks.
// 3. Use Percent.Custom() for non-standard percentages.
// 4. Values are expressed as decimals (0.10 = 10%) for calculation ease.
//
// USAGE:
//   .TrailingStopLoss(Percent.Ten)       // 10% trailing stop
//   .TrailingStopLoss(Percent.Custom(12)) // 12% trailing stop
//
// ============================================================================

namespace IdiotProof.Models
{
    /// <summary>
    /// Common percentage values for trailing stop losses and other calculations.
    /// </summary>
    /// <remarks>
    /// <para><b>Best Practices:</b></para>
    /// <list type="bullet">
    ///   <item>Use named properties for code readability.</item>
    ///   <item>For trailing stops: 5-10% for stable stocks, 10-20% for volatile.</item>
    ///   <item>Use <see cref="Custom"/> for percentages not covered by constants.</item>
    ///   <item>All values are expressed as decimals (e.g., 0.10 for 10%).</item>
    /// </list>
    /// 
    /// <para><b>Trailing Stop Guidelines:</b></para>
    /// <list type="table">
    ///   <item><term>Conservative</term><description>3-5% - Less room, more exits</description></item>
    ///   <item><term>Moderate</term><description>7-10% - Balanced approach</description></item>
    ///   <item><term>Aggressive</term><description>15-25% - More room, fewer exits</description></item>
    /// </list>
    /// </remarks>
    public static class Percent
    {
        /// <summary>1% (0.01) - Very tight trailing stop.</summary>
        /// <remarks><b>Warning:</b> May trigger on normal price fluctuations.</remarks>
        public static double One => 0.01;

        /// <summary>2% (0.02) - Tight trailing stop.</summary>
        public static double Two => 0.02;

        /// <summary>3% (0.03) - Conservative trailing stop.</summary>
        public static double Three => 0.03;

        /// <summary>4% (0.04) - Conservative trailing stop.</summary>
        public static double Four => 0.04;

        /// <summary>5% (0.05) - Standard conservative trailing stop.</summary>
        /// <remarks><b>Good for:</b> Low-volatility stocks, day trading.</remarks>
        public static double Five => 0.05;

        /// <summary>6% (0.06) - Moderate trailing stop.</summary>
        public static double Six => 0.06;

        /// <summary>7% (0.07) - Moderate trailing stop.</summary>
        public static double Seven => 0.07;

        /// <summary>8% (0.08) - Moderate trailing stop.</summary>
        public static double Eight => 0.08;

        /// <summary>9% (0.09) - Moderate trailing stop.</summary>
        public static double Nine => 0.09;

        /// <summary>10% (0.10) - Standard moderate trailing stop.</summary>
        /// <remarks><b>Good for:</b> Most swing trades, moderate volatility.</remarks>
        public static double Ten => 0.10;

        /// <summary>15% (0.15) - Wider trailing stop.</summary>
        /// <remarks><b>Good for:</b> Volatile stocks, longer holding periods.</remarks>
        public static double Fifteen => 0.15;

        /// <summary>20% (0.20) - Aggressive trailing stop.</summary>
        /// <remarks><b>Good for:</b> High-volatility stocks, position trades.</remarks>
        public static double Twenty => 0.20;

        /// <summary>25% (0.25) - Very aggressive trailing stop.</summary>
        /// <remarks><b>Warning:</b> Large drawdowns possible before triggering.</remarks>
        public static double TwentyFive => 0.25;

        /// <summary>50% (0.50) - Extremely wide trailing stop.</summary>
        /// <remarks><b>Warning:</b> Rarely used; consider if this is appropriate.</remarks>
        public static double Fifty => 0.50;

        /// <summary>
        /// Creates a custom percentage value.
        /// </summary>
        /// <param name="value">The percentage as a whole number (e.g., 12 for 12%).</param>
        /// <returns>The percentage as a decimal (e.g., 0.12 for 12%).</returns>
        /// <exception cref="System.ArgumentOutOfRangeException">
        /// Thrown if value is negative or greater than 100.
        /// </exception>
        /// <remarks>
        /// <para><b>Example:</b></para>
        /// <code>
        /// Percent.Custom(12)   // Returns 0.12 (12%)
        /// Percent.Custom(7.5)  // Returns 0.075 (7.5%)
        /// </code>
        /// </remarks>
        public static double Custom(double value)
        {
            // Best Practice: Validate input range
            if (value < 0 || value > 100)
            {
                throw new System.ArgumentOutOfRangeException(
                    nameof(value), 
                    value, 
                    "Percentage must be between 0 and 100.");
            }

            return value / 100.0;
        }
    }
}
