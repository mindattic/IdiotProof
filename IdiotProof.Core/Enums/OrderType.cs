// ============================================================================
// OrderType - Order Execution Type
// ============================================================================

namespace IdiotProof.Enums {
    /// <summary>
    /// Order type determining execution method.
    /// </summary>
    /// <remarks>
    /// <para><b>! IBKR API MAPPING !</b></para>
    /// <para>Maps to <c>order.OrderType</c> in the IB API:</para>
    /// <list type="bullet">
    ///   <item><see cref="Market"/> → "MKT"</item>
    ///   <item><see cref="Limit"/> → "LMT"</item>
    /// </list>
    /// <para>Additional IB order types (not yet implemented): "STP", "STP LMT", "TRAIL", etc.</para>
    /// <para>Reference: https://interactivebrokers.github.io/tws-api/classIBApi_1_1Order.html</para>
    /// 
    /// <para><b>Best Practices:</b></para>
    /// <list type="bullet">
    ///   <item><see cref="Market"/>: Fast execution, potential slippage.</item>
    ///   <item><see cref="Limit"/>: Price control, may not fill.</item>
    ///   <item>Use Limit orders in pre-market due to lower liquidity.</item>
    /// </list>
    /// </remarks>
    public enum OrderType
    {
        /// <summary>Market order - executes immediately at best available price. IB API: "MKT"</summary>
        Market,

        /// <summary>Limit order - executes only at specified price or better. IB API: "LMT"</summary>
        Limit
    }
}


