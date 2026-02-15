// ============================================================================
// Global Configuration - IB Connection Settings
// ============================================================================
//
// ╔═══════════════════════════════════════════════════════════════════════════╗
// ║  IBKR API CONNECTION SETTINGS                                             ║
// ║                                                                           ║
// ║  These settings are used to connect to Interactive Brokers TWS/Gateway.   ║
// ║                                                                           ║
// ║  Port Numbers (standard IB configuration):                                ║
// ║    • TWS Paper:     7497                                                  ║
// ║    • TWS Live:      7496                                                  ║
// ║    • Gateway Paper: 4002                                                  ║
// ║    • Gateway Live:  4001                                                  ║
// ║                                                                           ║
// ║  Client ID: Must be unique per connection to the same TWS/Gateway.        ║
// ║  Account:   Required if multiple accounts under one login.                ║
// ║                                                                           ║
// ║  Timezone:  Your local timezone for time display and input.               ║
// ║             All market times are internally stored in Eastern Time.       ║
// ║             Supported: EST, CST, MST, PST                                 ║
// ║                                                                           ║
// ║  Reference: https://interactivebrokers.github.io/tws-api/               ║
// ╚═══════════════════════════════════════════════════════════════════════════╝

using IdiotProof.Enums;

namespace IdiotProof.Settings {
    /// <summary>
    /// Global configuration for IB connection settings.
    /// These settings apply to all jobs.
    /// </summary>
    /// <remarks>
    /// <para><b>IBKR Connection Reference:</b></para>
    /// <list type="table">
    ///   <listheader><term>Port</term><description>Usage</description></listheader>
    ///   <item><term>7497</term><description>TWS Paper Trading</description></item>
    ///   <item><term>7496</term><description>TWS Live Trading</description></item>
    ///   <item><term>4002</term><description>IB Gateway Paper Trading</description></item>
    ///   <item><term>4001</term><description>IB Gateway Live Trading</description></item>
    /// </list>
    /// </remarks>
    public static class AppSettings
    {
        // ----- IB Connection Settings -----
        public const string Host = "127.0.0.1";

        /// <summary>
        /// Gateway paper: 4002, Gateway live: 4001
        /// </summary>
        public const int Port = 4001;

        public static bool IsPaperTrading => Port == 4002;

        /// <summary>
        /// Unique client ID for this connection.
        /// Each running instance needs a unique ID.
        /// </summary>
        public const int ClientId = 99;

        /// <summary>
        /// Timeout in seconds to wait for connection.
        /// </summary>
        public const int ConnectionTimeoutSeconds = 10;

        /// <summary>
        /// Your IBKR account ID (e.g., "U1234567" or "DU1234567" for paper).
        /// Required if multiple accounts under one login.
        /// Leave empty to use the default/primary account.
        /// </summary>
        public const string AccountNumber = "U22434144";

        /// <summary>
        /// Secondary IBKR account for hedged trading (Long on one, Short on other).
        /// Leave empty to disable dual-account mode.
        /// </summary>
        public const string SecondaryAccountNumber = "U23270497";

        /// <summary>
        /// When true, enables dual-account hedging (LONG on primary, SHORT on secondary).
        /// Both accounts must be configured and accessible via the same TWS/Gateway.
        /// </summary>
        public static bool DualAccountHedgingEnabled { get; set; } = false;

        // ----- Timezone Settings -----

        /// <summary>
        /// Your local timezone for time display and user input.
        /// </summary>
        /// <remarks>
        /// <para><b>Important:</b> All market times are internally stored in Eastern Time (ET).</para>
        /// <para>This setting controls how times are displayed in the console and how user input times are interpreted.</para>
        /// <para><b>Supported Timezones:</b></para>
        /// <list type="bullet">
        ///   <item><term>EST</term><description>Eastern Time - Market time (no conversion)</description></item>
        ///   <item><term>CST</term><description>Central Time - 1 hour behind Eastern</description></item>
        ///   <item><term>MST</term><description>Mountain Time - 2 hours behind Eastern</description></item>
        ///   <item><term>PST</term><description>Pacific Time - 3 hours behind Eastern</description></item>
        /// </list>
        /// <para><b>Market Hours in Each Timezone:</b></para>
        /// <code>
        /// EST: Market Open 9:30 AM, Close 4:00 PM
        /// CST: Market Open 8:30 AM, Close 3:00 PM
        /// MST: Market Open 7:30 AM, Close 2:00 PM
        /// PST: Market Open 6:30 AM, Close 1:00 PM
        /// </code>
        /// <para><b>Default:</b> EST (Eastern Standard Time) - New York / US equity market time.</para>
        /// </remarks>
        public const MarketTimeZone Timezone = MarketTimeZone.EST;

        // ----- Heartbeat Settings -----

        /// <summary>
        /// Interval between heartbeat checks to verify IB connection is alive.
        /// </summary>
        public static readonly TimeSpan Heartbeat = TimeSpan.FromMinutes(5);

        // ----- Backtest Settings -----

        /// <summary>
        /// When true, shows individual trade details per day in backtest output.
        /// When false, only shows the summary.
        /// </summary>
        public static bool ShowBacktestDailyDetails { get; set; } = false;

        // ----- Backend Mode Settings -----

        /// <summary>
        /// When true, suppresses most console output. Only shows minimal heartbeat (*Blip*).
        /// </summary>
        public static bool SilentMode { get; set; } = false;

        // ----- Web Frontend Settings -----

        /// <summary>
        /// URL of the IdiotProof.Web frontend for live data streaming.
        /// Set to null to disable web frontend integration.
        /// </summary>
        public static string? WebFrontendUrl { get; set; } = "http://localhost:5000";

        // ----- Risk Management Settings -----

        /// <summary>
        /// When true, enables stop loss orders in autonomous trading.
        /// Set to false to disable stop losses and let positions run until score-based exit or TP.
        /// </summary>
        /// <remarks>
        /// Use this to focus on perfecting LONG entry timing before worrying about risk management.
        /// When disabled, exits will only occur via Take Profit, score-based exit, or manual intervention.
        /// </remarks>
        public static bool UseStopLoss { get; set; } = false;

        /// <summary>
        /// When true, enables trailing stop loss in autonomous trading.
        /// Set to false to disable trailing stops and let winners run longer.
        /// </summary>
        /// <remarks>
        /// Use this alongside UseStopLoss = false to maximize upside capture while learning optimal entries.
        /// </remarks>
        public static bool UseTrailingStopLoss { get; set; } = false;

        // ----- Indicator Settings -----

        /// <summary>
        /// Maximum number of candlesticks to retain for indicator calculations.
        /// Higher values allow for longer-period indicators (e.g., EMA200) but use more memory.
        /// </summary>
        /// <remarks>
        /// <para><b>Recommended Values:</b></para>
        /// <list type="bullet">
        ///   <item><term>255</term><description>Supports up to EMA(200) with buffer (default)</description></item>
        ///   <item><term>50</term><description>Minimal - only supports short EMAs (9, 21)</description></item>
        ///   <item><term>500</term><description>Extended - for longer backtesting needs</description></item>
        /// </list>
        /// </remarks>
        public const int MaxCandlesticks = 255;

        // ----- IPC Settings -----

        /// <summary>
        /// Interval in seconds between IPC ping messages to verify connection is alive.
        /// </summary>
        public const int IpcPingIntervalSeconds = 60 * 5;

        // ----- Price Check Settings -----

        /// <summary>
        /// Interval in seconds between ticker price check reports.
        /// Set to 0 to disable price reporting.
        /// </summary>
        public const int TickerPriceCheckIntervalSeconds = 60 * 5;
    }
}




