// ============================================================================
// MarketTime - Trading Session Time Definitions
// ============================================================================
// All times are in Eastern Time (ET) - the standard for US equity markets.
// ============================================================================

namespace IdiotProof.Core.Helpers;

/// <summary>
/// Trading time definitions for different market sessions.
/// All times are in Eastern Time (ET).
/// </summary>
public static class MarketTime
{
    /// <summary>
    /// Pre-market trading session: 4:00 AM - 9:30 AM ET.
    /// </summary>
    public static class PreMarket
    {
        /// <summary>Pre-market start: 4:00 AM ET</summary>
        public static TimeOnly Start => new(4, 0);
        
        /// <summary>Common exit time before market open: 9:15 AM ET</summary>
        public static TimeOnly Ending => new(9, 15);

        public static TimeOnly RightBeforeBell => new(9, 29);

        /// <summary>Pre-market end / RTH start: 9:30 AM ET</summary>
        public static TimeOnly End => new(9, 30);
    }

    /// <summary>
    /// Regular trading hours: 9:30 AM - 4:00 PM ET.
    /// </summary>
    public static class RTH
    {
        /// <summary>Market open: 9:30 AM ET</summary>
        public static TimeOnly Start => new(9, 30);
        
        /// <summary>Common exit time before market close: 3:45 PM ET</summary>
        public static TimeOnly Ending => new(15, 45);
        
        /// <summary>Market close: 4:00 PM ET</summary>
        public static TimeOnly End => new(16, 0);
    }

    /// <summary>
    /// After-hours trading session: 4:00 PM - 8:00 PM ET.
    /// </summary>
    public static class AfterHours
    {
        /// <summary>After-hours start: 4:00 PM ET</summary>
        public static TimeOnly Start => new(16, 0);
        
        /// <summary>Common exit time: 7:45 PM ET</summary>
        public static TimeOnly Ending => new(19, 45);
        
        /// <summary>After-hours end: 8:00 PM ET</summary>
        public static TimeOnly End => new(20, 0);
    }
}


