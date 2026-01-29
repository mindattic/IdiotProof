// ============================================================================
// Global Configuration - IB Connection Settings
// ============================================================================

namespace IdiotProof
{
    /// <summary>
    /// Global configuration for IB connection settings.
    /// These settings apply to all jobs.
    /// </summary>
    public static class Settings
    {
        // ----- IB Connection Settings -----
        public const string IB_HOST = "127.0.0.1";
        
        /// <summary>
        /// Gateway paper: 4002, Gateway live: 4001
        /// </summary>
        public const int IB_PORT = 4002;
        
        public static bool IsPaperTrading => IB_PORT == 4002;

        /// <summary>
        /// Unique client ID for this connection.
        /// Each running instance needs a unique ID.
        /// </summary>
        public const int IB_CLIENT_ID = 99;

        /// <summary>
        /// Timeout in seconds to wait for connection.
        /// </summary>
        public const int CONNECTION_TIMEOUT_SECONDS = 10;
    }
}
