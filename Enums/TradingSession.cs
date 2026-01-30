// ============================================================================
// TradingSession - Trading session time windows
// ============================================================================

namespace IdiotProof.Enums
{
    /// <summary>
    /// Predefined trading session time windows for use with <see cref="Models.Stock.SessionDuration"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>Standard Sessions (Eastern Time):</b></para>
    /// <list type="bullet">
    ///   <item><see cref="Active"/>: No time restrictions (24/7 monitoring)</item>
    ///   <item><see cref="PreMarket"/>: 4:00 AM - 9:30 AM ET</item>
    ///   <item><see cref="RTH"/>: 9:30 AM - 4:00 PM ET (Regular Trading Hours)</item>
    ///   <item><see cref="AfterHours"/>: 4:00 PM - 8:00 PM ET</item>
    ///   <item><see cref="Extended"/>: 4:00 AM - 8:00 PM ET (All sessions)</item>
    /// </list>
    /// 
    /// <para><b>Buffered Sessions (10-minute buffer):</b></para>
    /// <list type="bullet">
    ///   <item><see cref="PreMarketEndEarly"/>: 4:00 AM - 9:20 AM ET (exit before market open)</item>
    ///   <item><see cref="PreMarketStartLate"/>: 4:10 AM - 9:30 AM ET (skip initial volatility)</item>
    ///   <item><see cref="RTHEndEarly"/>: 9:30 AM - 3:50 PM ET (exit before close)</item>
    ///   <item><see cref="RTHStartLate"/>: 9:40 AM - 4:00 PM ET (skip open volatility)</item>
    ///   <item><see cref="AfterHoursEndEarly"/>: 4:00 PM - 7:50 PM ET (exit early)</item>
    /// </list>
    /// 
    /// <para><b>Example:</b></para>
    /// <code>
    /// Stock.Ticker("AAPL")
    ///     .SessionDuration(TradingSession.Active)  // No time restrictions
    ///     .Breakout(150)
    ///     .Buy(100, Price.Current)
    ///     .Build();
    /// </code>
    /// </remarks>
    public enum TradingSession
    {
        // ================================================================
        // UNRESTRICTED
        // ================================================================

        /// <summary>
        /// No time restrictions - strategy is always active and monitoring.
        /// Equivalent to not calling SessionDuration() at all.
        /// </summary>
        Active,

        // ================================================================
        // STANDARD SESSIONS (Full duration)
        // ================================================================

        /// <summary>
        /// Pre-market session: 4:00 AM - 9:30 AM ET.
        /// Lower liquidity, wider spreads. Use limit orders.
        /// </summary>
        PreMarket,

        /// <summary>
        /// Regular Trading Hours: 9:30 AM - 4:00 PM ET.
        /// Highest liquidity, tightest spreads.
        /// </summary>
        RTH,

        /// <summary>
        /// After-hours session: 4:00 PM - 8:00 PM ET.
        /// Very low liquidity, wide spreads. Use caution.
        /// </summary>
        AfterHours,

        /// <summary>
        /// Extended hours: 4:00 AM - 8:00 PM ET.
        /// Covers pre-market, RTH, and after-hours.
        /// </summary>
        Extended,

        // ================================================================
        // BUFFERED SESSIONS (10-minute buffer at start/end)
        // ================================================================

        /// <summary>
        /// Pre-market ending early: 4:00 AM - 9:20 AM ET.
        /// Exits 10 minutes before market open to close positions.
        /// </summary>
        PreMarketEndEarly,

        /// <summary>
        /// Pre-market starting late: 4:10 AM - 9:30 AM ET.
        /// Skips first 10 minutes to avoid initial volatility.
        /// </summary>
        PreMarketStartLate,

        /// <summary>
        /// RTH ending early: 9:30 AM - 3:50 PM ET.
        /// Exits 10 minutes before market close.
        /// </summary>
        RTHEndEarly,

        /// <summary>
        /// RTH starting late: 9:40 AM - 4:00 PM ET.
        /// Skips first 10 minutes after open to avoid volatility.
        /// </summary>
        RTHStartLate,

        /// <summary>
        /// After-hours ending early: 4:00 PM - 7:50 PM ET.
        /// Exits 10 minutes before session ends.
        /// </summary>
        AfterHoursEndEarly
    }
}
