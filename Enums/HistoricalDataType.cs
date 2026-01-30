// ============================================================================
// HistoricalDataType - Data Type for Historical Requests
// ============================================================================

namespace IdiotProof.Enums
{
    /// <summary>
    /// Data type for historical requests.
    /// </summary>
    /// <remarks>
    /// <para><b>⚠️ IBKR API MAPPING ⚠️</b></para>
    /// <para>Maps to <c>whatToShow</c> parameter in <c>reqHistoricalData()</c>.</para>
    /// </remarks>
    public enum HistoricalDataType
    {
        /// <summary>IB: "TRADES" - Last trade prices.</summary>
        Trades,
        /// <summary>IB: "MIDPOINT" - Midpoint between bid/ask.</summary>
        Midpoint,
        /// <summary>IB: "BID" - Bid prices.</summary>
        Bid,
        /// <summary>IB: "ASK" - Ask prices.</summary>
        Ask
    }
}
