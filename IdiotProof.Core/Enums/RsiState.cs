// ============================================================================
// RSI State Enum - Relative Strength Index conditions
// ============================================================================

namespace IdiotProof.Enums {
    /// <summary>
    /// RSI (Relative Strength Index) market states for condition evaluation.
    /// </summary>
    /// <remarks>
    /// <para><b>Standard RSI Thresholds:</b></para>
    /// <list type="bullet">
    ///   <item><b>Overbought:</b> RSI >= 70 (potential selling pressure)</item>
    ///   <item><b>Oversold:</b> RSI &lt;= 30 (potential buying opportunity)</item>
    /// </list>
    /// </remarks>
    public enum RsiState
    {
        /// <summary>
        /// RSI >= 70 - Stock may be overvalued, potential reversal to downside.
        /// </summary>
        Overbought,

        /// <summary>
        /// RSI &lt;= 30 - Stock may be undervalued, potential reversal to upside.
        /// </summary>
        Oversold
    }
}


