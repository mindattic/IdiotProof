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
        public const int IB_PORT = 4001;

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

        /// <summary>
        /// Your IBKR account ID (e.g., "U1234567" or "DU1234567" for paper).
        /// Required if you have multiple accounts under one login.
        /// Leave empty to use the default/primary account.
        /// Main = U22434144; 
        /// Secondary = U23270497
        /// </summary>
        public const string IB_ACCOUNT = "U22434144";
    }
}


