// ============================================================================
// DI Direction Enum - Directional Indicator states
// ============================================================================

namespace IdiotProof.Enums {
    /// <summary>
    /// Directional Indicator (+DI/-DI) relationship states.
    /// </summary>
    /// <remarks>
    /// <para><b>DI Interpretation:</b></para>
    /// <list type="bullet">
    ///   <item><b>Positive (+DI > -DI):</b> Bullish pressure dominates - upward price movement</item>
    ///   <item><b>Negative (-DI > +DI):</b> Bearish pressure dominates - downward price movement</item>
    ///   <item><b>Equal (+DI = -DI):</b> No direction dominates - condition returns false</item>
    /// </list>
    /// <para>DI crossovers often signal trend changes when confirmed by ADX.</para>
    /// <para><b>Note:</b> The comparison is strictly greater than. When +DI equals -DI, 
    /// neither direction dominates and the condition evaluates to false.</para>
    /// </remarks>
    public enum DiDirection
    {
        /// <summary>
        /// +DI > -DI - Bullish directional movement dominates.
        /// Returns false when +DI equals -DI (no dominance).
        /// </summary>
        Positive,

        /// <summary>
        /// -DI > +DI - Bearish directional movement dominates.
        /// Returns false when -DI equals +DI (no dominance).
        /// </summary>
        Negative
    }
}


