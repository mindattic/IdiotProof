// ============================================================================
// MACD State Enum - Moving Average Convergence Divergence conditions
// ============================================================================

namespace IdiotProof.Enums
{
    /// <summary>
    /// MACD (Moving Average Convergence Divergence) signal states.
    /// </summary>
    /// <remarks>
    /// <para><b>MACD Components:</b></para>
    /// <list type="bullet">
    ///   <item><b>MACD Line:</b> 12-period EMA minus 26-period EMA</item>
    ///   <item><b>Signal Line:</b> 9-period EMA of the MACD line</item>
    ///   <item><b>Histogram:</b> MACD line minus Signal line</item>
    /// </list>
    /// 
    /// <para><b>Signal Interpretation:</b></para>
    /// <list type="bullet">
    ///   <item><b>Bullish:</b> MACD crosses above signal line (buy signal)</item>
    ///   <item><b>Bearish:</b> MACD crosses below signal line (sell signal)</item>
    ///   <item><b>AboveZero:</b> MACD line is positive (uptrend)</item>
    ///   <item><b>BelowZero:</b> MACD line is negative (downtrend)</item>
    /// </list>
    /// </remarks>
    public enum MacdState
    {
        /// <summary>
        /// MACD line is above the signal line (bullish momentum).
        /// </summary>
        Bullish,

        /// <summary>
        /// MACD line is below the signal line (bearish momentum).
        /// </summary>
        Bearish,

        /// <summary>
        /// MACD line is above zero (in uptrend).
        /// </summary>
        AboveZero,

        /// <summary>
        /// MACD line is below zero (in downtrend).
        /// </summary>
        BelowZero,

        /// <summary>
        /// MACD histogram is increasing (momentum building).
        /// </summary>
        HistogramRising,

        /// <summary>
        /// MACD histogram is decreasing (momentum fading).
        /// </summary>
        HistogramFalling
    }
}
