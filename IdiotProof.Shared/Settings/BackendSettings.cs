// ============================================================================
// BackendSettings - Settings specific to the Backend project
// ============================================================================

using System.Text.Json.Serialization;

namespace IdiotProof.Shared.Settings;

/// <summary>
/// Settings specific to the Backend project.
/// </summary>
public class BackendSettings : AppSettings
{
    /// <summary>
    /// IBKR connection settings.
    /// </summary>
    [JsonPropertyName("connection")]
    public ConnectionSettings Connection { get; set; } = new();

    /// <summary>
    /// Trading settings.
    /// </summary>
    [JsonPropertyName("trading")]
    public TradingSettings Trading { get; set; } = new();

    /// <summary>
    /// Heartbeat settings.
    /// </summary>
    [JsonPropertyName("heartbeat")]
    public HeartbeatSettings Heartbeat { get; set; } = new();
}

/// <summary>
/// IBKR connection settings.
/// </summary>
public class ConnectionSettings
{
    /// <summary>
    /// Host address for IB TWS/Gateway.
    /// </summary>
    [JsonPropertyName("host")]
    public string Host { get; set; } = "127.0.0.1";

    /// <summary>
    /// Port for IB TWS/Gateway.
    /// Gateway paper: 4002, Gateway live: 4001, TWS paper: 7497, TWS live: 7496
    /// </summary>
    [JsonPropertyName("port")]
    public int Port { get; set; } = 4001;

    /// <summary>
    /// Unique client ID for this connection.
    /// </summary>
    [JsonPropertyName("clientId")]
    public int ClientId { get; set; } = 99;

    /// <summary>
    /// Timeout in seconds to wait for connection.
    /// </summary>
    [JsonPropertyName("connectionTimeoutSeconds")]
    public int ConnectionTimeoutSeconds { get; set; } = 10;

    /// <summary>
    /// IBKR account number.
    /// </summary>
    [JsonPropertyName("accountNumber")]
    public string AccountNumber { get; set; } = string.Empty;

    /// <summary>
    /// Whether currently configured for paper trading.
    /// </summary>
    [JsonIgnore]
    public bool IsPaperTrading => Port == 4002 || Port == 7497;
}

/// <summary>
/// Trading behavior settings.
/// </summary>
public class TradingSettings
{
    /// <summary>
    /// Timezone for market time display.
    /// </summary>
    [JsonPropertyName("timezone")]
    public string Timezone { get; set; } = "EST";

    /// <summary>
    /// When true, suppresses most console output.
    /// </summary>
    [JsonPropertyName("silentMode")]
    public bool SilentMode { get; set; } = false;

    /// <summary>
    /// When true, auto-starts trading when strategies are loaded.
    /// </summary>
    [JsonPropertyName("autoStart")]
    public bool AutoStart { get; set; } = false;
}

/// <summary>
/// Heartbeat configuration settings.
/// </summary>
public class HeartbeatSettings
{
    /// <summary>
    /// Interval in minutes between heartbeat checks.
    /// </summary>
    [JsonPropertyName("intervalMinutes")]
    public int IntervalMinutes { get; set; } = 5;
}
