// ============================================================================
// OrderSide - Buy/Sell Direction for Orders
// ============================================================================

namespace IdiotProof.Backend.Enums
{
    /// <summary>
    /// Order side indicating whether to buy or sell.
    /// </summary>
    /// <remarks>
    /// <para><b>⚠️ IBKR API MAPPING ⚠️</b></para>
    /// <para>Maps to <c>order.Action</c> in the IB API:</para>
    /// <list type="bullet">
    ///   <item><see cref="Buy"/> → "BUY"</item>
    ///   <item><see cref="Sell"/> → "SELL"</item>
    /// </list>
    /// <para>Reference: https://interactivebrokers.github.io/tws-api/classIBApi_1_1Order.html</para>
    /// </remarks>
    public enum OrderSide
    {
        /// <summary>Buy order - go long on the security. IB API: "BUY"</summary>
        Buy,

        /// <summary>Sell order - close long position or go short. IB API: "SELL"</summary>
        Sell
    }
}


