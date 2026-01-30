// ============================================================================
// BarSize - Historical Data Bar Size Options
// ============================================================================

namespace IdiotProof.Enums
{
    /// <summary>
    /// Bar size options for historical data requests.
    /// </summary>
    /// <remarks>
    /// <para><b>⚠️ IBKR API MAPPING ⚠️</b></para>
    /// <para>Maps to <c>barSizeSetting</c> parameter in <c>reqHistoricalData()</c>.</para>
    /// </remarks>
    public enum BarSize
    {
        /// <summary>IB: "1 secs". Max duration: 1800 S (30 min).</summary>
        Seconds1,
        /// <summary>IB: "5 secs". Max duration: 7200 S (2 hours).</summary>
        Seconds5,
        /// <summary>IB: "15 secs". Max duration: 14400 S (4 hours).</summary>
        Seconds15,
        /// <summary>IB: "30 secs". Max duration: 28800 S (8 hours).</summary>
        Seconds30,
        /// <summary>IB: "1 min". Max duration: 1 D.</summary>
        Minutes1,
        /// <summary>IB: "2 mins". Max duration: 2 D.</summary>
        Minutes2,
        /// <summary>IB: "5 mins". Max duration: 1 W.</summary>
        Minutes5,
        /// <summary>IB: "15 mins". Max duration: 2 W.</summary>
        Minutes15,
        /// <summary>IB: "30 mins". Max duration: 1 M.</summary>
        Minutes30,
        /// <summary>IB: "1 hour". Max duration: 1 M.</summary>
        Hours1,
        /// <summary>IB: "1 day". Max duration: 1 Y.</summary>
        Days1
    }
}
