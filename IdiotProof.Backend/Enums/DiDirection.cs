// ============================================================================
// DI Direction Enum - Directional Indicator states
// ============================================================================

namespace IdiotProof.Backend.Enums
{
    /// <summary>
    /// Directional Indicator (+DI/-DI) relationship states.
    /// </summary>
    /// <remarks>
    /// <para><b>DI Interpretation:</b></para>
    /// <list type="bullet">
    ///   <item><b>Positive (+DI > -DI):</b> Bullish pressure dominates - upward price movement</item>
    ///   <item><b>Negative (-DI > +DI):</b> Bearish pressure dominates - downward price movement</item>
    /// </list>
    /// <para>DI crossovers often signal trend changes when confirmed by ADX.</para>
    /// </remarks>
    public enum DiDirection
    {
        /// <summary>
        /// +DI > -DI - Bullish directional movement dominates.
        /// </summary>
        Positive,

        /// <summary>
        /// -DI > +DI - Bearish directional movement dominates.
        /// </summary>
        Negative
    }
}
